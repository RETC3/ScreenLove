using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ScreenLove.Services
{
    /// <summary>Una release più recente di quella in uso.</summary>
    public sealed record UpdateInfo(Version Version, string PageUrl, string Notes,
        string? SetupUrl, string? SetupName, long SetupSize, string? SetupSha256 = null);

    /// <summary>
    /// Aggiornamenti: legge l'ultima release dal sito di ScreenLove (latest.json) o, se il sito non
    /// risponde, da GitHub; scarica l'installer (ScreenLove-Setup-x.y.z.exe), ne controlla dimensione e
    /// impronta SHA-256 e lo avvia in modalità silenziosa; l'installer riapre l'app.
    /// </summary>
    public static class Updater
    {
        private const string Repo = "RETC3/ScreenLove";
        public const string SiteUrl = "https://screenlove.it";

        /// <summary>Indirizzo di Cloudflare Pages dello stesso sito: riserva se il dominio non risponde.</summary>
        public const string PagesUrl = "https://screenlove.pages.dev";

        private static readonly string GitHubDownloadPrefix = $"https://github.com/{Repo}/releases/download/";
        private static readonly string[] SiteDownloadPrefixes = { SiteUrl + "/download/", PagesUrl + "/download/" };

        /// <summary>
        /// Fonti dell'ultima release, in ordine. SCREENLOVE_UPDATE_FEED permette di provare gli aggiornamenti
        /// con un file JSON locale (stesso formato di latest.json e dell'API di GitHub).
        /// </summary>
        private static string? FeedOverride => Environment.GetEnvironmentVariable("SCREENLOVE_UPDATE_FEED");

        private static IEnumerable<string> Feeds => FeedOverride != null
            ? new[] { FeedOverride }
            : new[] { SiteUrl + "/latest.json", PagesUrl + "/latest.json", $"https://api.github.com/repos/{Repo}/releases/latest" };

        public static string ReleasesPage => SiteUrl + "/#scarica";

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
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return http;
        }

        /// <summary>Restituisce la release più recente se è più nuova di quella in uso, altrimenti null.</summary>
        public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
        {
            Exception? last = null;
            foreach (var url in Feeds)
            {
                try
                {
                    return Parse(await ReadFeedAsync(url, ct), CurrentVersion);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex; // si prova la fonte successiva
                }
            }
            throw last ?? new InvalidOperationException("Nessuna fonte per gli aggiornamenti.");
        }

        private static async Task<string> ReadFeedAsync(string url, CancellationToken ct)
        {
            var feed = new Uri(url);
            if (feed.IsFile)
                return await File.ReadAllTextAsync(feed.LocalPath, ct);

            using var http = CreateClient(TimeSpan.FromSeconds(15));
            using var resp = await http.GetAsync(feed, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException("Non ci sono ancora versioni pubblicate di ScreenLove.");
            if ((int)resp.StatusCode == 403 || (int)resp.StatusCode == 429)
                throw new InvalidOperationException("Troppe richieste al server degli aggiornamenti: riprova tra un'ora.");
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        /// <summary>Interpreta latest.json o la risposta di /releases/latest di GitHub (stesso formato).</summary>
        public static UpdateInfo? Parse(string json, Version current)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

            string tag = Str(root, "tag_name") ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
            version = Normalize(version);
            if (version <= current) return null;

            string? setupUrl = null, setupName = null, sha = null;
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
                    sha = Str(a, "sha256");
                    if (sha != null && !Regex.IsMatch(sha, "^[0-9a-fA-F]{64}$")) sha = null;
                    break;
                }
            }

            // Si apre solo una pagina del sito o del repository, non un indirizzo qualsiasi.
            var page = Str(root, "html_url");
            if (page == null || !(page.StartsWith(SiteUrl + "/", StringComparison.OrdinalIgnoreCase)
                                  || page.StartsWith(PagesUrl + "/", StringComparison.OrdinalIgnoreCase)
                                  || page.StartsWith($"https://github.com/{Repo}/", StringComparison.OrdinalIgnoreCase)))
                page = ReleasesPage;

            return new UpdateInfo(version, page, (Str(root, "body") ?? "").Trim(), setupUrl, setupName, setupSize, sha?.ToLowerInvariant());
        }

        /// <summary>True se da questo indirizzo si può scaricare ed eseguire un installer.</summary>
        public static bool IsTrustedDownload(string url) =>
            Array.Exists(SiteDownloadPrefixes, p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || url.StartsWith(GitHubDownloadPrefix, StringComparison.OrdinalIgnoreCase)
            || (FeedOverride != null && new Uri(url).IsFile);

        /// <summary>Scarica l'installer nella cartella temporanea e ne restituisce il percorso.</summary>
        public static async Task<string> DownloadSetupAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
        {
            if (update.SetupUrl == null || update.SetupName == null)
                throw new InvalidOperationException("Questa versione non contiene l'installer.");
            if (!IsTrustedDownload(update.SetupUrl))
                throw new InvalidOperationException("Indirizzo dell'installer non riconosciuto: aggiornamento annullato.");

            var source = new Uri(update.SetupUrl);
            var folder = Path.Combine(Path.GetTempPath(), "ScreenLove-aggiornamento");
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "*.exe"))
                try { File.Delete(old); } catch { /* in uso: pazienza */ }
            var target = Path.Combine(folder, Path.GetFileName(update.SetupName));
            var partial = target + ".part";

            try
            {
                await using (var output = File.Create(partial))
                using (var sha = SHA256.Create())
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
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            done += read;
                            if (total > 0) progress?.Report((double)done / total);
                        }
                        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                        if (update.SetupSize > 0 && done != update.SetupSize)
                            throw new IOException($"Download incompleto ({done:N0} di {update.SetupSize:N0} byte).");
                        if (update.SetupSha256 != null && Convert.ToHexString(sha.Hash!).ToLowerInvariant() != update.SetupSha256)
                            throw new IOException("Il file scaricato non corrisponde a quello pubblicato (impronta SHA-256 diversa).");
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
            catch
            {
                try { File.Delete(partial); } catch { /* niente */ }
                throw;
            }
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
