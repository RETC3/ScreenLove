using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ScreenLove;
using ScreenLove.Documents;
using ScreenLove.Editor;
using ScreenLove.Recording;
using ScreenLove.Services;
using ScreenLove.Video;

namespace ScreenLove.Checks
{
    /// <summary>Esegue i controlli e stampa OK / ERRORE per ciascuno. Codice di uscita = numero di errori.</summary>
    internal static class Program
    {
        private static int _failures;
        private static string _dir = "";

        [STAThread]
        private static int Main(string[] args)
        {
            _dir = args.FirstOrDefault(a => !a.StartsWith("-")) ?? Path.Combine(Path.GetTempPath(), "ScreenLove.Checks");
            Directory.CreateDirectory(_dir);
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine($"Cartella di lavoro: {_dir}\n");

            Section("Tasti rapidi", CheckHotkeys);
            Section("Impostazioni", CheckSettings);
            Section("Aggiornamenti", CheckUpdater);
            if (args.Contains("--online"))
                Section("Aggiornamenti: release vera su GitHub", () => RunOnDispatcher(CheckUpdaterOnlineAsync));
            var sample = SampleImage(900, 520);
            Section("Effetti immagine", () => CheckEffects(sample));
            Section("Generatore di documenti", CheckDocuments);
            bool skipVideo = args.Contains("--no-video");
            if (!skipVideo)
                Section("Video: registrazione, taglio, unione", () => RunOnDispatcher(CheckVideoAsync));

            Console.WriteLine(_failures == 0 ? "\nTutti i controlli superati." : $"\n{_failures} controlli NON superati.");
            return _failures;
        }

        // ------------------------------------------------------------------ utilità

        private static void Section(string name, Action body)
        {
            Console.WriteLine($"== {name}");
            try { body(); }
            catch (Exception ex) { Check(false, "eccezione: " + ex.GetType().Name + ": " + ex.Message); }
            Console.WriteLine();
        }

        /// <summary>
        /// Esegue codice asincrono sul thread STA con un dispatcher WPF, come fa l'app
        /// (le API di montaggio video di Windows lo richiedono).
        /// </summary>
        private static void RunOnDispatcher(Func<Task> body)
        {
            Exception? error = null;
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try { await body(); }
                catch (Exception ex) { error = ex; }
                finally { frame.Continue = false; }
            });
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            if (error != null)
            {
                Check(false, $"eccezione: {error.GetType().Name} 0x{error.HResult:X8}: {error.Message}");
                foreach (var line in (error.StackTrace ?? "").Split('\n').Where(l => l.Contains("ScreenLove")).Take(4))
                    Console.WriteLine("          " + line.Trim());
            }
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "OK    " : "ERRORE")}  {what}");
            if (!ok) _failures++;
        }

        private static (byte b, byte g, byte r, byte a) Pixel(BitmapSource img, int x, int y)
        {
            var px = new byte[4];
            ImageIO.Normalize(img).CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
            return (px[0], px[1], px[2], px[3]);
        }

        /// <summary>Immagine di prova opaca con testo e forme, simile a una cattura.</summary>
        private static BitmapSource SampleImage(int w, int h)
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF9)), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)), null, new Rect(0, 0, w, 60));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0x3D, 0x7F)), null, new Rect(60, 120, 260, 160));
                var ft = new FormattedText("Finestra di prova ScreenLove", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 28, Brushes.Black, 1.0);
                dc.DrawText(ft, new Point(360, 160));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(v);
            return ImageIO.Normalize(rtb);
        }

        // ------------------------------------------------------------------ controlli

        private static void CheckHotkeys()
        {
            foreach (var (action, text) in HotkeyBinding.Defaults)
            {
                var b = HotkeyBinding.Parse(text);
                Check(!b.IsEmpty && b.ToString() == text && b.VirtualKey != 0, $"{action}: \"{text}\" → {b.Display}");
            }
            var r = HotkeyBinding.Parse("Ctrl+Alt+R");
            Check(r.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && r.Key == Key.R && r.VirtualKey == 0x52, "Ctrl+Alt+R: modificatori e codice tasto corretti");
            Check(HotkeyBinding.Parse("Shift+Snapshot").Display == "Maiusc+Stamp", "nomi italiani (Maiusc, Stamp)");
            Check(HotkeyBinding.Parse("").IsEmpty && HotkeyBinding.Parse(null).IsEmpty, "combinazione vuota = nessun tasto");
        }

        /// <summary>Con --online: legge l'ultima release pubblicata e ne scarica l'installer.</summary>
        private static async Task CheckUpdaterOnlineAsync()
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ScreenLove-Checks");
            var json = await http.GetStringAsync("https://api.github.com/repos/RETC3/ScreenLove/releases/latest");

            // una copia "vecchia" (0.0.1) deve vedere la release come aggiornamento
            var u = Updater.Parse(json, new Version(0, 0, 1));
            Check(u != null, $"una versione 0.0.1 trova l'aggiornamento ({u?.Version})");
            Check(u?.SetupUrl != null, $"la release contiene l'installer ({u?.SetupName}, {u?.SetupSize / 1048576.0:N1} MB)");
            Check(Updater.Parse(json, Updater.CurrentVersion) == null || Updater.CurrentVersion < u!.Version,
                $"la versione in uso ({Updater.CurrentVersion}) non si aggiorna a sé stessa");
            if (u?.SetupUrl == null) return;

            var path = await Updater.DownloadSetupAsync(u, null);
            var info = new FileInfo(path);
            Check(info.Length == u.SetupSize, $"installer scaricato per intero ({info.Length:N0} byte)");
            var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            // Inno Setup riempie i campi della versione con spazi
            Check(ver.ProductName?.Trim() == "ScreenLove" && ver.ProductVersion?.Trim() == u.Version.ToString(),
                $"il file scaricato è l'installer di ScreenLove {ver.ProductVersion?.Trim()}");
            File.Delete(path);
        }

        private static void CheckUpdater()
        {
            var current = new Version(0, 1, 0);
            string Release(string tag, bool pre = false) =>
                "{\"tag_name\":\"" + tag + "\",\"prerelease\":" + (pre ? "true" : "false") + ",\"html_url\":\"https://github.com/RETC3/ScreenLove/releases/tag/" + tag + "\",\"body\":\"Novità\"," +
                "\"assets\":[{\"name\":\"ScreenLove-0.2.0-win-x64.zip\",\"browser_download_url\":\"https://github.com/RETC3/ScreenLove/releases/download/v0.2.0/z.zip\",\"size\":5}," +
                "{\"name\":\"ScreenLove-Setup-0.2.0.exe\",\"browser_download_url\":\"https://github.com/RETC3/ScreenLove/releases/download/v0.2.0/ScreenLove-Setup-0.2.0.exe\",\"size\":1234}]}";

            var u = Updater.Parse(Release("v0.2.0"), current);
            Check(u != null && u.Version == new Version(0, 2, 0), "v0.2.0 è più recente di 0.1.0");
            Check(u?.SetupName == "ScreenLove-Setup-0.2.0.exe" && u.SetupSize == 1234, "trova l'installer tra gli allegati (non lo zip)");
            Check(u?.Notes == "Novità", "legge le note della release");
            Check(Updater.Parse(Release("v0.1.0"), current) == null, "stessa versione: nessun aggiornamento");
            Check(Updater.Parse(Release("v0.0.9"), current) == null, "versione più vecchia: nessun aggiornamento");
            Check(Updater.Parse(Release("v0.3.0", pre: true), current) == null, "le pre-release vengono ignorate");
            Check(Updater.Parse(Release("nightly"), current) == null, "tag non numerico ignorato");
            Check(Updater.CurrentVersion.Major >= 0 && Updater.CurrentVersion.Revision == -1, $"versione corrente leggibile ({Updater.CurrentVersion})");
        }

        private static void CheckSettings()
        {
            var s = new AppSettings { WatermarkText = "Prova", RecordFps = 60 };
            s.SetHotkey(HotkeyAction.Video, HotkeyBinding.Parse("Ctrl+Shift+V"));
            var c = s.Clone();
            Check(c.WatermarkText == "Prova" && c.RecordFps == 60, "Clone copia i valori");
            Check(c.GetHotkey(HotkeyAction.Video).ToString() == "Ctrl+Shift+V", "Clone copia i tasti rapidi");
            Check(c.GetHotkey(HotkeyAction.Region).ToString() == "Snapshot", "tasto non personalizzato = predefinito");
            c.WatermarkText = "Altro";
            Check(s.WatermarkText == "Prova", "la copia è indipendente dall'originale");
            var t = new AppSettings();
            t.CopyFrom(c);
            Check(t.WatermarkText == "Altro" && t.GetHotkey(HotkeyAction.Video).ToString() == "Ctrl+Shift+V", "CopyFrom applica i valori salvati");
        }

        private static void CheckEffects(BitmapSource img)
        {
            int w = img.PixelWidth, h = img.PixelHeight;

            var round = ImageEffects.RoundCorners(img, 12);
            Check(round.PixelWidth == w && Pixel(round, 0, 0).a == 0 && Pixel(round, w / 2, h / 2).a == 255,
                "Angoli arrotondati: angolo trasparente, centro opaco, stessa dimensione");

            var border = ImageEffects.AddBorder(img, 5, Color.FromRgb(0xE5, 0x39, 0x35));
            var bp = Pixel(border, 2, 2);
            Check(border.PixelWidth == w + 10 && border.PixelHeight == h + 10 && bp.r == 0xE5 && bp.g == 0x39 && bp.b == 0x35,
                "Bordo: +5 px per lato nel colore scelto");

            var shadow = ImageEffects.AddShadow(img);
            var sp = Pixel(shadow, shadow.PixelWidth - 20, shadow.PixelHeight - 20);
            Check(shadow.PixelWidth > w + 20 && Pixel(shadow, 0, 0).a == 0 && sp.a > 0,
                $"Ombra: immagine allargata ({shadow.PixelWidth}×{shadow.PixelHeight}), angolo trasparente, ombra in basso a destra (alfa {sp.a})");

            var torn = ImageEffects.TornBottomEdge(img);
            int transparentBottom = Enumerable.Range(0, w).Count(x => Pixel(torn, x, h - 1).a == 0);
            Check(transparentBottom > w / 3 && Pixel(torn, w / 2, 10).a == 255,
                $"Bordo strappato: fondo frastagliato ({transparentBottom}/{w} px trasparenti), resto intatto");

            var mark = ImageEffects.AddWatermark(img, "ScreenLove");
            int changed = 0;
            for (int x = w - 260; x < w - 10; x += 3)
                for (int y = h - 70; y < h - 10; y += 3)
                    if (!Pixel(mark, x, y).Equals(Pixel(img, x, y))) changed++;
            Check(changed > 30 && Pixel(mark, 20, 20).Equals(Pixel(img, 20, 20)), $"Watermark: testo in basso a destra ({changed} punti modificati), resto invariato");

            // watermark dopo l'ombra: deve stare dentro l'immagine, non nel margine trasparente
            var shadowed = ImageEffects.AddShadow(img);
            var marked = ImageEffects.AddWatermark(shadowed, "ScreenLove");
            int outside = 0;
            for (int x = 0; x < marked.PixelWidth; x += 2)
                for (int y = marked.PixelHeight - 24; y < marked.PixelHeight; y++)
                    if (!Pixel(marked, x, y).Equals(Pixel(shadowed, x, y))) outside++;
            Check(outside == 0, $"Watermark dopo l'ombra: niente testo nel margine dell'ombra ({outside} punti fuori)");

            string jpg = Path.Combine(_dir, "effetto.jpg");
            ImageIO.Save(round, jpg);
            var back = ImageIO.LoadNormalized(jpg);
            var corner = Pixel(back, 0, 0);
            Check(corner.r > 240 && corner.g > 240 && corner.b > 240, "JPEG: le zone trasparenti diventano bianche (non nere)");
            string png = Path.Combine(_dir, "effetto.png");
            ImageIO.Save(ImageEffects.AddShadow(ImageEffects.RoundCorners(img, 10)), png);
            Check(Pixel(ImageIO.LoadNormalized(png), 0, 0).a == 0, "PNG: la trasparenza viene conservata");
        }

        private static void CheckDocuments()
        {
            var paths = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                string p = Path.Combine(_dir, $"passo{i + 1}.png");
                ImageIO.Save(SampleImage(900 - i * 150, 520 + i * 40), p);
                paths.Add(p);
            }
            var model = new DocumentModel { Title = "Guida di prova àèìòù", NumberSteps = true };
            for (int i = 0; i < paths.Count; i++)
                model.Steps.Add(new DocumentStep
                {
                    ImagePath = paths[i],
                    Title = $"Titolo del passaggio {i + 1}",
                    Comment = string.Join(" ", Enumerable.Repeat("Commento lungo per verificare l'a capo automatico del testo.", 6)) + "\nSeconda riga.",
                });

            string pdf = Path.Combine(_dir, "guida.pdf");
            DocumentBuilder.Build(model, pdf);
            var pdfBytes = File.ReadAllBytes(pdf);
            using (var read = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                Check(pdfBytes.Length > 10_000 && System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 5) == "%PDF-" && read.PageCount >= 1,
                    $"PDF valido: {read.PageCount} pagine, {pdfBytes.Length / 1024} KB");

            string docx = Path.Combine(_dir, "guida.docx");
            DocumentBuilder.Build(model, docx);
            using (var wd = WordprocessingDocument.Open(docx, false))
            {
                var errors = new OpenXmlValidator().Validate(wd).ToList();
                foreach (var e in errors.Take(3)) Console.WriteLine("          " + e.Description);
                int images = wd.MainDocumentPart!.ImageParts.Count();
                Check(errors.Count == 0, $"Word: documento conforme allo standard Open XML ({errors.Count} errori)");
                Check(images == 3 && wd.MainDocumentPart.Document.Body!.InnerText.Contains("Titolo del passaggio 2"), $"Word: {images} immagini e testi presenti");
            }

            string html = Path.Combine(_dir, "guida.html");
            DocumentBuilder.Build(model, html);
            string text = File.ReadAllText(html);
            int imgs = text.Split("data:image/png;base64,").Length - 1;
            Check(imgs == 3 && text.Contains("àèìòù") && text.Contains("Titolo del passaggio 3"), $"HTML autonomo: {imgs} immagini incorporate, accenti corretti");
        }

        private static async Task CheckVideoAsync()
        {
            // 1) registrazione di 3 s di una zona dello schermo (senza audio)
            string rec = Path.Combine(_dir, "registrazione.mp4");
            var recorder = new ScreenRecorder(new RecordingOptions { Region = new Int32Rect(0, 0, 640, 360), OutputPath = rec, Fps = 30, SystemAudio = true });
            await recorder.StartAsync();
            Console.WriteLine($"          audio nella registrazione: {recorder.HasAudio}{(recorder.Warning != null ? " · " + recorder.Warning : "")}");
            await Task.Delay(3000);
            await recorder.StopAsync();
            var d0 = await VideoEditing.GetDurationAsync(rec);
            Check(Math.Abs(d0.TotalSeconds - 3) < 0.4, $"Registrazione: {d0.TotalSeconds:F2} s (attesi ~3)");

            // 2) taglio da 0,5 s a 2,0 s
            string cut = Path.Combine(_dir, "tagliato.mp4");
            await VideoEditing.TrimAsync(rec, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2.0), cut);
            var d1 = await VideoEditing.GetDurationAsync(cut);
            Check(Math.Abs(d1.TotalSeconds - 1.5) < 0.25, $"Taglio: {d1.TotalSeconds:F2} s (attesi ~1,5)");

            // 3) unione registrazione + tagliato
            string merged = Path.Combine(_dir, "unito.mp4");
            await VideoEditing.MergeAsync(new[] { rec, cut }, merged);
            var d2 = await VideoEditing.GetDurationAsync(merged);
            Check(Math.Abs(d2.TotalSeconds - (d0 + d1).TotalSeconds) < 0.4, $"Unione: {d2.TotalSeconds:F2} s (attesi ~{(d0 + d1).TotalSeconds:F2})");

            async Task<bool> HasAudio(string p) =>
                (await Windows.Media.MediaProperties.MediaEncodingProfile.CreateFromFileAsync(
                    await Windows.Storage.StorageFile.GetFileFromPathAsync(p))).Audio != null;
            bool a0 = await HasAudio(rec), a1 = await HasAudio(cut), a2 = await HasAudio(merged);
            if (!a0)
                Console.WriteLine("          (nessun dispositivo audio sul PC: registrazione senza audio, come previsto)");

            // 4) unione di clip sintetiche con e senza audio: il pezzo muto va riempito di silenzio
            string s1 = Path.Combine(_dir, "sintetica_tono.mp4"), s2 = Path.Combine(_dir, "sintetica_muta.mp4"), s3 = Path.Combine(_dir, "sintetica_tono2.mp4");
            await SyntheticClipAsync(s1, 2.0, true, Colors.Crimson);
            await SyntheticClipAsync(s2, 1.0, false, Colors.SeaGreen);
            await SyntheticClipAsync(s3, 1.5, true, Colors.RoyalBlue);
            string cutAudio = Path.Combine(_dir, "taglio_audio.mp4");
            await VideoEditing.TrimAsync(s1, TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(1.5), cutAudio);
            var d4 = await VideoEditing.GetDurationAsync(cutAudio);
            Check(Math.Abs(d4.TotalSeconds - 1.1) < 0.25 && await HasAudio(cutAudio), $"Taglio di un video con audio: {d4.TotalSeconds:F2} s (attesi ~1,1), audio presente");

            // stesso taglio con il file tenuto aperto da un'altra lettura (come fa il lettore)
            string cutOpen = Path.Combine(_dir, "taglio_file_aperto.mp4");
            using (var reader = new FileStream(s1, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                try
                {
                    await VideoEditing.TrimAsync(s1, TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(1.5), cutOpen);
                    Console.WriteLine("          (taglio riuscito anche con il file aperto in lettura)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"          (con il file aperto il taglio fallisce: 0x{ex.HResult:X8} → il lettore va chiuso prima)");
                }
            }

            string mix = Path.Combine(_dir, "unione_audio.mp4");
            await VideoEditing.MergeAsync(new[] { s1, s2, s3 }, mix);
            var d3 = await VideoEditing.GetDurationAsync(mix);
            Check(Math.Abs(d3.TotalSeconds - 4.5) < 0.3 && await HasAudio(mix), $"Unione con audio (tono + muto + tono): {d3.TotalSeconds:F2} s, traccia audio presente");
        }

        /// <summary>Clip MP4 di prova: tinta unita e, se richiesto, un tono continuo a 440 Hz.</summary>
        private static async Task SyntheticClipAsync(string path, double seconds, bool tone, Color color)
        {
            const int w = 320, h = 240, fps = 30, rate = 48000;
            var vdesc = new Windows.Media.Core.VideoStreamDescriptor(
                Windows.Media.MediaProperties.VideoEncodingProperties.CreateUncompressed(Windows.Media.MediaProperties.MediaEncodingSubtypes.Bgra8, w, h));
            var adesc = new Windows.Media.Core.AudioStreamDescriptor(Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(rate, 2, 16));
            var mss = tone ? new Windows.Media.Core.MediaStreamSource(vdesc, adesc) : new Windows.Media.Core.MediaStreamSource(vdesc);
            mss.BufferTime = TimeSpan.Zero;
            var frame = new byte[w * h * 4];
            for (int i = 0; i < frame.Length; i += 4) { frame[i] = color.B; frame[i + 1] = color.G; frame[i + 2] = color.R; frame[i + 3] = 255; }
            int totalFrames = (int)(seconds * fps), videoIndex = 0;
            long audioDone = 0, audioTotal = (long)(seconds * rate);
            mss.SampleRequested += (_, e) =>
            {
                if (e.Request.StreamDescriptor is Windows.Media.Core.VideoStreamDescriptor)
                {
                    if (videoIndex >= totalFrames) { e.Request.Sample = null; return; }
                    var s = Windows.Media.Core.MediaStreamSample.CreateFromBuffer(
                        System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(frame), TimeSpan.FromSeconds((double)videoIndex / fps));
                    s.Duration = TimeSpan.FromSeconds(1.0 / fps);
                    e.Request.Sample = s;
                    videoIndex++;
                }
                else
                {
                    if (audioDone >= audioTotal) { e.Request.Sample = null; return; }
                    int n = (int)Math.Min(rate / 10, audioTotal - audioDone);
                    var pcm = new byte[n * 4];
                    for (int i = 0; i < n; i++)
                    {
                        short v = (short)(Math.Sin(2 * Math.PI * 440 * (audioDone + i) / rate) * 8000);
                        pcm[i * 4] = pcm[i * 4 + 2] = (byte)v;
                        pcm[i * 4 + 1] = pcm[i * 4 + 3] = (byte)(v >> 8);
                    }
                    var s = Windows.Media.Core.MediaStreamSample.CreateFromBuffer(
                        System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(pcm), TimeSpan.FromTicks(audioDone * TimeSpan.TicksPerSecond / rate));
                    s.Duration = TimeSpan.FromTicks((long)n * TimeSpan.TicksPerSecond / rate);
                    e.Request.Sample = s;
                    audioDone += n;
                }
            };
            var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.Vga);
            profile.Video.Width = w; profile.Video.Height = h;
            profile.Video.FrameRate.Numerator = fps; profile.Video.FrameRate.Denominator = 1;
            profile.Audio = tone ? Windows.Media.MediaProperties.AudioEncodingProperties.CreateAac(rate, 2, 128_000) : null;
            using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            var prepared = await new Windows.Media.Transcoding.MediaTranscoder().PrepareMediaStreamSourceTranscodeAsync(
                mss, System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(fs), profile);
            if (!prepared.CanTranscode) throw new InvalidOperationException("clip sintetica: " + prepared.FailureReason);
            await prepared.TranscodeAsync();
        }
    }
}
