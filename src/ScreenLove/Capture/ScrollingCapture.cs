using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenLove.Native;
using ScreenLove.Services;

namespace ScreenLove.Capture
{
    public sealed record ScrollingResult(BitmapSource Image, int Frames, string Reason);

    /// <summary>
    /// Cattura a scorrimento: invia la rotellina del mouse alla zona scelta, cattura dopo ogni scatto
    /// e cuce i fotogrammi finché il contenuto non si muove più (o l'utente preme Esc).
    /// </summary>
    public static class ScrollingCapture
    {
        private const int MaxHeight = 40000;
        private const int MaxFrames = 200;

        public static async Task<ScrollingResult> RunAsync(Int32Rect screenRect)
        {
            var originalCursor = System.Windows.Forms.Cursor.Position;
            var center = new System.Drawing.Point(screenRect.X + screenRect.Width / 2, screenRect.Y + screenRect.Height / 2);
            var indicator = new ScrollIndicatorWindow(screenRect);
            indicator.Show();
            NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE); // azzera lo stato "premuto da allora"

            var stitcher = new ScrollStitcher();
            string reason;
            try
            {
                NativeMethods.SetCursorPos(center.X, center.Y);
                await Task.Delay(120);

                var previous = await CaptureStableAsync(screenRect);
                stitcher.AddFirst(previous);
                Debug(0, previous, $"rect={screenRect}");

                int notches = 1, expected = 0;
                double pxPerNotch = 0;
                while (true)
                {
                    if (EscapePressed()) { reason = "fermata con Esc"; break; }
                    if (stitcher.TotalHeight >= MaxHeight) { reason = "raggiunta l'altezza massima"; break; }
                    if (stitcher.Frames >= MaxFrames) { reason = "raggiunto il numero massimo di fotogrammi"; break; }

                    // la rotellina va alla finestra sotto il cursore: lo rimettiamo al centro se l'utente l'ha spostato,
                    // con un piccolo movimento reale (i browser Chromium instradano la rotellina dove "vedono" il mouse)
                    NativeMethods.SetCursorPos(center.X, center.Y);
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, 1, 0, 0, IntPtr.Zero);
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, -1, 0, 0, IntPtr.Zero);
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, -NativeMethods.WHEEL_DELTA * notches, IntPtr.Zero);

                    var current = await CaptureAfterScrollAsync(screenRect, previous);
                    if (EscapePressed()) { reason = "fermata con Esc"; break; }

                    var prev = previous;
                    int exp = expected;
                    var match = await Task.Run(() => ScrollStitcher.FindOffset(prev, current, exp));
                    Debug(stitcher.Frames, current, $"notches={notches} expected={exp} match={match}");
                    if (match == null) { reason = "il contenuto è cambiato in modo imprevisto"; break; }
                    if (match.Value.Offset == 0) { reason = stitcher.Frames == 1 ? "la zona non scorre" : "fine della pagina"; break; }

                    stitcher.Append(current, match.Value);
                    previous = current;
                    indicator.Update(stitcher.TotalHeight, stitcher.Frames);

                    // adatta il numero di scatti: circa metà della zona utile per scatto, mantenendo sovrapposizione
                    if (pxPerNotch <= 0)
                        pxPerNotch = (double)match.Value.Offset / notches;
                    int content = current.Height - match.Value.Header - match.Value.Footer;
                    notches = Math.Clamp((int)(content * 0.55 / Math.Max(1, pxPerNotch)), 1, 10);
                    expected = (int)Math.Round(pxPerNotch * notches);
                }

                return new ScrollingResult(stitcher.Build(), stitcher.Frames, reason);
            }
            finally
            {
                indicator.Close();
                System.Windows.Forms.Cursor.Position = originalCursor;
            }
        }

        /// <summary>Diagnostica: con SCREENLOVE_SCROLL_DEBUG=cartella salva fotogrammi ed esiti dei confronti.</summary>
        private static void Debug(int index, ScrollFrame frame, string message)
        {
            var dir = Environment.GetEnvironmentVariable("SCREENLOVE_SCROLL_DEBUG");
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                var bmp = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Stride);
                ImageIO.Save(bmp, System.IO.Path.Combine(dir, $"frame{index:000}.png"));
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "log.txt"), $"{index:000} {message}{Environment.NewLine}");
            }
            catch
            {
                // solo diagnostica
            }
        }

        private static bool EscapePressed() =>
            (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8001) != 0;

        /// <summary>
        /// Dopo uno scatto: aspetta che il contenuto inizi a muoversi (i browser animano lo scorrimento
        /// con qualche centinaio di ms di ritardo), poi che si fermi. Se non cambia nulla, la zona non scorre più.
        /// </summary>
        private static async Task<ScrollFrame> CaptureAfterScrollAsync(Int32Rect r, ScrollFrame previous)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 800)
            {
                await Task.Delay(40);
                var frame = Grab(r);
                if (!frame.SameAs(previous))
                    return await CaptureStableAsync(r);
            }
            return previous;
        }

        /// <summary>Attende la fine dello scorrimento animato: due catture consecutive identiche.</summary>
        private static async Task<ScrollFrame> CaptureStableAsync(Int32Rect r)
        {
            await Task.Delay(90);
            var frame = Grab(r);
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(70);
                var next = Grab(r);
                if (next.SameAs(frame))
                    return next;
                frame = next;
            }
            return frame;
        }

        private static ScrollFrame Grab(Int32Rect r) => new(ScreenCapture.CaptureRectPixels(r), r.Width, r.Height);
    }

    /// <summary>
    /// Piccolo indicatore di avanzamento. È escluso dalle catture (WDA_EXCLUDEFROMCAPTURE),
    /// quindi non finisce mai nell'immagine anche se si sovrappone alla zona.
    /// </summary>
    public sealed class ScrollIndicatorWindow : Window
    {
        private readonly Int32Rect _target;
        private readonly TextBlock _text;

        public ScrollIndicatorWindow(Int32Rect target)
        {
            _target = target;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Left = -32000;
            Top = -32000;

            _text = new TextBlock { Foreground = Brushes.White, FontSize = 13, Text = "Cattura a scorrimento…  ·  Esc per fermare" };
            Content = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(235, 30, 31, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3D, 0x7F)),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(14, 8, 14, 9),
                Child = _text,
            };

            SourceInitialized += (_, _) =>
                NativeMethods.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            Loaded += (_, _) => PlaceNearTarget();
        }

        public void Update(int height, int frames) =>
            _text.Text = $"Cattura a scorrimento…  {height} px · {frames} fotogrammi  ·  Esc per fermare";

        private void PlaceNearTarget()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(_target.X, _target.Y, _target.Width, _target.Height)).WorkingArea;

            int x = _target.X + (_target.Width - w) / 2;
            int y = _target.Y + _target.Height + 10;               // sotto la zona…
            if (y + h > screen.Bottom) y = _target.Y - h - 10;     // …o sopra…
            if (y < screen.Top) y = screen.Bottom - h - 16;        // …o in basso sullo schermo (resta comunque invisibile alle catture)
            x = Math.Clamp(x, screen.Left + 8, Math.Max(screen.Left + 8, screen.Right - w - 8));

            NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010 /* NOSIZE | NOACTIVATE */);
        }
    }
}
