using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenLove.Services;

namespace ScreenLove.Capture
{
    public sealed class CaptureController
    {
        private readonly AppSettings _settings;
        private readonly CaptureStore _store;
        private bool _busy;

        public CaptureController(AppSettings settings, CaptureStore store)
        {
            _settings = settings;
            _store = store;
        }

        /// <summary>Nuova cattura salvata: percorso del file.</summary>
        public event Action<string>? Captured;

        /// <summary>Colore copiato dal color picker dell'overlay.</summary>
        public event Action<string>? ColorCopied;

        public async Task CaptureRegionAsync(int delaySeconds = 0)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (delaySeconds > 0)
                    await CountdownWindow.RunAsync(delaySeconds);

                var shot = ScreenCapture.CaptureVirtualScreen(includeCursor: false);
                var windows = WindowFinder.GetWindowRects(shot.Bounds);
                var monitors = WindowFinder.GetMonitorRects(shot.Bounds);

                var overlay = new CaptureOverlayWindow(shot, windows, monitors);
                if (overlay.ShowDialog() == true)
                    Finish(shot.Crop(overlay.Selection));
                else if (overlay.CopiedColor != null)
                    ColorCopied?.Invoke(overlay.CopiedColor);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Zona scelta per il riconoscimento del testo (non viene salvata nel workspace).</summary>
        public event Action<BitmapSource>? TextAreaCaptured;

        public async Task CaptureTextAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await Task.Yield();
                var shot = ScreenCapture.CaptureVirtualScreen(includeCursor: false);
                var overlay = new CaptureOverlayWindow(shot, WindowFinder.GetWindowRects(shot.Bounds), WindowFinder.GetMonitorRects(shot.Bounds),
                    "TESTO (OCR) — Trascina sul testo o su un QR code · Clic: finestra intera · Esc: annulla");
                if (overlay.ShowDialog() == true)
                    TextAreaCaptured?.Invoke(shot.Crop(overlay.Selection));
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Messaggio per l'utente al termine di una cattura a scorrimento.</summary>
        public event Action<string>? ScrollingFinished;

        public async Task CaptureScrollingAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var shot = ScreenCapture.CaptureVirtualScreen(includeCursor: false);
                var overlay = new CaptureOverlayWindow(shot, WindowFinder.GetWindowRects(shot.Bounds), WindowFinder.GetMonitorRects(shot.Bounds),
                    "A SCORRIMENTO — Clic: finestra · Trascina: zona da scorrere (meglio solo il contenuto, senza menu) · Esc: annulla");
                if (overlay.ShowDialog() != true)
                    return;

                var sel = overlay.Selection;
                var screenRect = new Int32Rect(sel.X + shot.Bounds.X, sel.Y + shot.Bounds.Y, sel.Width, sel.Height);
                await Task.Delay(200); // lascia sparire l'overlay

                var result = await ScrollingCapture.RunAsync(screenRect);
                Finish(result.Image);
                ScrollingFinished?.Invoke(result.Frames > 1
                    ? $"{result.Image.PixelWidth} × {result.Image.PixelHeight} px da {result.Frames} fotogrammi ({result.Reason})."
                    : $"Salvata una cattura normale: {result.Reason}.");
            }
            finally
            {
                _busy = false;
            }
        }

        public async Task CaptureFullScreenAsync(int delaySeconds = 0)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (delaySeconds > 0)
                    await CountdownWindow.RunAsync(delaySeconds);
                var shot = ScreenCapture.CaptureVirtualScreen(_settings.IncludeCursor);
                Finish(shot.Image);
            }
            finally
            {
                _busy = false;
            }
        }

        private void Finish(BitmapSource image)
        {
            string path = _store.SaveNew(image);
            if (_settings.CopyToClipboard)
            {
                try { Clipboard.SetImage(image); }
                catch { /* appunti bloccati da un'altra app: la cattura è comunque salvata */ }
            }
            Captured?.Invoke(path);
        }
    }
}
