using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenLove.Services
{
    /// <summary>Una release più recente di quella in uso.</summary>
    public sealed record UpdateInfo(Version Version, string PageUrl, string Notes, string? SetupUrl, string? SetupName, long SetupSize);

    /// <summary>
    /// Aggiornamenti dalle release di GitHub: legge l'ultima release, scarica l'installer
    /// (ScreenLove-Setup-x.y.z.exe) e lo avvia in modalità silenziosa; l'installer riapre l'app.
    /// </summary>
    public static class Updater
    {
        private const string Repo = "RETC3/ScreenLove";
        private static readonly string OfficialDownloadPrefix = $"https://github.com/{Repo}/releases/download/";

        /// <summary>
        /// Indirizzo dell'ultima release. SCREENLOVE_UPDATE_FEED permette di provare gli aggiornamenti
        /// con un file JSON locale (stesso formato dell'API di GitHub).
        /// </summary>
        private static string? FeedOverride => Environment.GetEnvironmentVariable("SCREENLOVE_UPDATE_FEED");
        private static string FeedUrl => FeedOverride ?? $"https://api.github.com/repos/{Repo}/releases/latest";

        public static string ReleasesPage => $"https://github.com/{Repo}/releases";

        public static Version CurrentVersion
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (info != null && Version.TryParse(info.Split('+', '-')[0], out var v))
                    return Normalize(v);
                return Normalize(asm.GetName().Version ?? new Version(0, 0, 0));
            }
        }

        /// <summary>True se l'app è stata installata con l'installer (accanto all'exe c'è il disinstallatore).</summary>
        public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

        private static HttpClient CreateClient(TimeSpan timeout)
        {
            var http = new HttpClient { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ScreenLove-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        /// <summary>Restituisce la release più recente se è più nuova di quella in uso, altrimenti null.</summary>
        public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
        {
            string json;
            var feed = new Uri(FeedUrl);
            if (feed.IsFile)
                json = await File.ReadAllTextAsync(feed.LocalPath, ct);
            else
            {
                using var http = CreateClient(TimeSpan.FromSeconds(15));
                using var resp = await http.GetAsync(feed, ct);
                if (resp.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException("Non ci sono ancora release pubbliche di ScreenLove su GitHub.");
                if ((int)resp.StatusCode == 403)
                    throw new InvalidOperationException("GitHub ha limitato le richieste: riprova tra un'ora.");
                resp.EnsureSuccessStatusCode();
                json = await resp.Content.ReadAsStringAsync(ct);
            }
            return Parse(json, CurrentVersion);
        }

        /// <summary>Interpreta la risposta di /releases/latest (separato per i controlli automatici).</summary>
        public static UpdateInfo? Parse(string json, Version current)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

            string tag = Str(root, "tag_name") ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            version = Normalize(version);
            if (version <= current) return null;

            string? setupUrl = null, setupName = null;
            long setupSize = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = Str(a, "name") ?? "";
                    if (!Regex.IsMatch(name, @"^ScreenLove-Setup-[\d.]+\.exe$", RegexOptions.IgnoreCase)) continue;
                    setupName = name;
                    setupUrl = Str(a, "browser_download_url");
                    setupSize = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                    break;
                }
            }

            var page = Str(root, "html_url");
            if (page == null || !page.StartsWith($"https://github.com/{Repo}/", StringComparison.OrdinalIgnoreCase))
                page = ReleasesPage;
            return new UpdateInfo(version, page, (Str(root, "body") ?? "").Trim(),
                setupUrl, setupName, setupSize);
        }

        /// <summary>Scarica l'installer nella cartella temporanea e ne restituisce il percorso.</summary>
        public static async Task<string> DownloadSetupAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
        {
            if (update.SetupUrl == null || update.SetupName == null)
                throw new InvalidOperationException("Questa release non contiene l'installer.");

            var source = new Uri(update.SetupUrl);
            // Si scaricano ed eseguono solo file delle release di questo repository
            // (o file locali quando si prova con SCREENLOVE_UPDATE_FEED).
            bool allowed = update.SetupUrl.StartsWith(OfficialDownloadPrefix, StringComparison.OrdinalIgnoreCase)
                           || (FeedOverride != null && source.IsFile);
            if (!allowed)
                throw new InvalidOperationException("Indirizzo dell'installer non riconosciuto: aggiornamento annullato.");

            var folder = Path.Combine(Path.GetTempPath(), "ScreenLove-aggiornamento");
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "*.exe"))
                try { File.Delete(old); } catch { /* in uso: pazienza */ }
            var target = Path.Combine(folder, Path.GetFileName(update.SetupName));
            var partial = target + ".part";

            await using (var output = File.Create(partial))
            {
                Stream input;
                long total;
                HttpResponseMessage? resp = null;
                HttpClient? http = null;
                if (source.IsFile)
                {
                    input = File.OpenRead(source.LocalPath);
                    total = input.Length;
                }
                else
                {
                    http = CreateClient(TimeSpan.FromMinutes(30));
                    resp = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();
                    total = resp.Content.Headers.ContentLength ?? update.SetupSize;
                    input = await resp.Content.ReadAsStreamAsync(ct);
                }

                try
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;
                        if (total > 0) progress?.Report((double)done / total);
                    }
                    if (update.SetupSize > 0 && done != update.SetupSize)
                        throw new IOException($"Download incompleto ({done:N0} di {update.SetupSize:N0} byte).");
                }
                finally
                {
                    await input.DisposeAsync();
                    resp?.Dispose();
                    http?.Dispose();
                }
            }

            File.Move(partial, target, overwrite: true);
            return target;
        }

        /// <summary>
        /// Avvia l'installer senza domande: aspetta che ScreenLove sia chiuso, sostituisce l'exe e lo riapre.
        /// Chi chiama deve chiudere l'app subito dopo.
        /// </summary>
        public static void LaunchSetup(string setupPath)
        {
            Process.Start(new ProcessStartInfo(setupPath, "/SILENT /NOCANCEL /NORESTART /UPDATE=1")
            {
                UseShellExecute = true,
            });
        }

        private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    }
}
