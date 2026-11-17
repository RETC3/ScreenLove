using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenLove.Native;
using ScreenLove.Services;

namespace ScreenLove.Capture
{
    /// <summary>
    /// Finestra a schermo intero sopra l'istantanea congelata del desktop.
    /// Passando col mouse evidenzia la finestra sotto il cursore, trascinando seleziona un'area.
    /// Click = cattura finestra evidenziata, trascina = area, F = schermo intero,
    /// frecce = sposta di 1 px, Ctrl+C = copia il colore, Esc/clic destro = annulla.
    /// Con <c>chooseMonitor</c> serve solo a scegliere lo schermo per la cattura a schermo intero:
    /// clic = schermo sotto il mouse, 1-9 = schermo numero N, T/F = tutti gli schermi.
    /// </summary>
    public sealed class CaptureOverlayWindow : Window
    {
        private static readonly Color Accent = Color.FromRgb(0xFF, 0x3D, 0x7F);

        private readonly ScreenShot _shot;
        private readonly List<Int32Rect> _windows;
        private readonly List<Int32Rect> _monitors;
        private readonly bool _chooseMonitor;

        private Point _mouse;          // pixel fisici, relativi al desktop virtuale
        private bool _hasMouse;
        private bool _down;
        private bool _dragging;
        private Point _downPoint;
        private Int32Rect _hover = Int32Rect.Empty;
        private Int32Rect _selection = Int32Rect.Empty;
        private readonly Surface _surface;

        private const int ZoomPixels = 15; // pixel visualizzati per lato nella lente
        private readonly WriteableBitmap _zoom = new(ZoomPixels, ZoomPixels, 96, 96, PixelFormats.Bgra32, null);
        private readonly byte[] _zoomPixels = new byte[ZoomPixels * ZoomPixels * 4];

        /// <summary>
        /// Elemento figlio che disegna l'overlay (disegnare in Window.OnRender non basta:
        /// il bordo del template della finestra ci finirebbe sopra).
        /// </summary>
        private sealed class Surface : FrameworkElement
        {
            private readonly CaptureOverlayWindow _owner;
            public Surface(CaptureOverlayWindow owner) => _owner = owner;
            protected override void OnRender(DrawingContext dc) => _owner.RenderOverlay(dc);
        }

        private new void InvalidateVisual() => _surface.InvalidateVisual();

        private readonly string _hint;

        public CaptureOverlayWindow(ScreenShot shot, List<Int32Rect> windows, List<Int32Rect> monitors, string? hint = null,
            bool chooseMonitor = false)
        {
            _chooseMonitor = chooseMonitor;
            _shot = shot;
            _windows = chooseMonitor ? new List<Int32Rect>() : windows;
            // Schermi numerati da sinistra a destra (e dall'alto in basso), ritagliati sull'istantanea.
            _monitors = new List<Int32Rect>();
            foreach (var m in monitors)
            {
                var c = WindowFinder.Clip(m, shot.Width, shot.Height);
                if (!c.IsEmpty) _monitors.Add(c);
            }
            _monitors.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            _hint = hint ?? (chooseMonitor
                ? $"SCHERMO INTERO — Clic: questo schermo · 1-{_monitors.Count}: schermo numero · T: tutti gli schermi · Esc: annulla"
                : "Clic: finestra · Trascina: area · F: schermo intero · Ctrl+C: copia colore · Esc: annulla");

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = chooseMonitor ? Cursors.Hand : Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = shot.Bounds.X;
            Top = shot.Bounds.Y;
            Width = 100;
            Height = 100;
            Title = "ScreenLove — cattura";
            Focusable = true;

            // L'istantanea è un'immagine fissa, disegnata una volta sola; a ogni movimento del mouse si
            // ridisegnano solo selezione, mirino e lente (prima si ridisegnava tutto lo schermo).
            _surface = new Surface(this);
            var background = new Image { Source = shot.Image, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(background, BitmapScalingMode.NearestNeighbor);
            Content = new Grid { Children = { background, _surface } };
            RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.NearestNeighbor);
            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) =>
            {
                Activate();
                Keyboard.Focus(this);
                InitMouseFromCursor();
            };
        }

        public Int32Rect Selection { get; private set; }

        /// <summary>Colore copiato con Ctrl+C (se l'utente ha usato il color picker).</summary>
        public string? CopiedColor { get; private set; }

        private double ScaleX => ActualWidth > 0 ? _shot.Width / ActualWidth : 1;
        private double ScaleY => ActualHeight > 0 ? _shot.Height / ActualHeight : 1;

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            // Impedisce a WPF di ridimensionare la finestra quando cambia DPI tra monitor diversi.
            source.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == NativeMethods.WM_DPICHANGED)
                    handled = true;
                return IntPtr.Zero;
            });
            NativeMethods.SetWindowPos(source.Handle, new IntPtr(-1) /* HWND_TOPMOST */,
                _shot.Bounds.X, _shot.Bounds.Y, _shot.Bounds.Width, _shot.Bounds.Height, 0x0040 /* SWP_SHOWWINDOW */);
        }

        private void InitMouseFromCursor()
        {
            var p = System.Windows.Forms.Cursor.Position;
            _mouse = new Point(p.X - _shot.Bounds.X, p.Y - _shot.Bounds.Y);
            _hasMouse = true;
            _hover = FindHover(_mouse);
            InvalidateVisual();
        }

        // ---------- input ----------

        private Point ToPixels(Point dip) => new(
            Math.Clamp(Math.Floor(dip.X * ScaleX), 0, _shot.Width - 1),
            Math.Clamp(Math.Floor(dip.Y * ScaleY), 0, _shot.Height - 1));

        private Rect ToDip(Int32Rect r) => new(r.X / ScaleX, r.Y / ScaleY, r.Width / ScaleX, r.Height / ScaleY);

        private Int32Rect FindHover(Point p)
        {
            int x = (int)p.X, y = (int)p.Y;
            foreach (var r in _windows)
                if (WindowFinder.Contains(r, x, y))
                    return r;
            foreach (var m in _monitors)
                if (WindowFinder.Contains(m, x, y))
                    return m;
            return _chooseMonitor ? Int32Rect.Empty : new Int32Rect(0, 0, _shot.Width, _shot.Height);
        }

        private Int32Rect SelectionFrom(Point a, Point b)
        {
            int x1 = (int)Math.Min(a.X, b.X), y1 = (int)Math.Min(a.Y, b.Y);
            int x2 = (int)Math.Max(a.X, b.X) + 1, y2 = (int)Math.Max(a.Y, b.Y) + 1;
            return WindowFinder.Clip(new Int32Rect(x1, y1, x2 - x1, y2 - y1), _shot.Width, _shot.Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _mouse = ToPixels(e.GetPosition(this));
            _hasMouse = true;
            if (_down && !_dragging && !_chooseMonitor && (Math.Abs(_mouse.X - _downPoint.X) > 3 || Math.Abs(_mouse.Y - _downPoint.Y) > 3))
                _dragging = true;

            if (_dragging)
                _selection = SelectionFrom(_downPoint, _mouse);
            else
                _hover = FindHover(_mouse);
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _mouse = ToPixels(e.GetPosition(this));
            _down = true;
            _downPoint = _mouse;
            CaptureMouse();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!_down) return;
            _down = false;
            ReleaseMouseCapture();
            var r = _dragging ? _selection : _hover;
            _dragging = false;
            if (r.Width >= 2 && r.Height >= 2)
                Complete(r);
            else
                InvalidateVisual();
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                _dragging = _down = false;
                ReleaseMouseCapture();
                _hover = FindHover(_mouse);
                InvalidateVisual();
            }
            else
            {
                DialogResult = false;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    DialogResult = false;
                    break;
                case Key.Enter:
                    Complete(_dragging ? _selection : _hover);
                    break;
                case Key.F:
                case Key.T when _chooseMonitor:
                    Complete(new Int32Rect(0, 0, _shot.Width, _shot.Height));
                    break;
                case >= Key.D1 and <= Key.D9 when _chooseMonitor:
                    CompleteMonitor(e.Key - Key.D1);
                    break;
                case >= Key.NumPad1 and <= Key.NumPad9 when _chooseMonitor:
                    CompleteMonitor(e.Key - Key.NumPad1);
                    break;
                case Key.C when !_chooseMonitor && (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                    var c = _shot.GetPixel((int)_mouse.X, (int)_mouse.Y);
                    CopiedColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                    try { Clipboard.SetText(CopiedColor); } catch { /* appunti occupati */ }
                    DialogResult = false;
                    break;
                case Key.Left: NudgeCursor(-1, 0); break;
                case Key.Right: NudgeCursor(1, 0); break;
                case Key.Up: NudgeCursor(0, -1); break;
                case Key.Down: NudgeCursor(0, 1); break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void NudgeCursor(int dx, int dy)
        {
            var p = System.Windows.Forms.Cursor.Position;
            NativeMethods.SetCursorPos(p.X + dx, p.Y + dy);
        }

        private void CompleteMonitor(int index)
        {
            if (index >= 0 && index < _monitors.Count)
                Complete(_monitors[index]);
        }

        private void Complete(Int32Rect r)
        {
            if (r.Width < 1 || r.Height < 1) return;
            Selection = r;
            DialogResult = true;
        }

        // ---------- rendering ----------

        private void RenderOverlay(DrawingContext dc)
        {
            var full = new Rect(0, 0, ActualWidth, ActualHeight);

            var active = _dragging ? _selection : _hover;
            Geometry dim = new RectangleGeometry(full);
            if (!active.IsEmpty)
                dim = new CombinedGeometry(GeometryCombineMode.Exclude, dim, new RectangleGeometry(ToDip(active)));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, dim);

            if (_chooseMonitor)
            {
                RenderMonitorChoice(dc, active);
            }
            else if (!active.IsEmpty)
            {
                var r = ToDip(active);
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(Accent), 2), r);
                DrawLabel(dc, $"{active.Width} × {active.Height}", new Point(r.X, r.Y), above: true);
            }

            if (_hasMouse && !_chooseMonitor)
            {
                var m = new Point((_mouse.X + 0.5) / ScaleX, (_mouse.Y + 0.5) / ScaleY);
                if (!_dragging)
                {
                    var cross = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1) { DashStyle = DashStyles.Dash };
                    dc.DrawLine(cross, new Point(0, m.Y), new Point(ActualWidth, m.Y));
                    dc.DrawLine(cross, new Point(m.X, 0), new Point(m.X, ActualHeight));
                }
                DrawMagnifier(dc, m);
            }

            var hintSize = MakeText(_hint, 12);
            DrawLabel(dc, _hint, new Point((ActualWidth - hintSize.Width) / 2 - 6, ActualHeight - hintSize.Height - 60), above: false);
        }

        /// <summary>Numero grande al centro di ogni schermo; quello sotto il mouse è evidenziato.</summary>
        private void RenderMonitorChoice(DrawingContext dc, Int32Rect active)
        {
            var accent = new SolidColorBrush(Accent);
            for (int i = 0; i < _monitors.Count; i++)
            {
                var m = _monitors[i];
                var r = ToDip(m);
                bool on = m.Equals(active);
                if (on)
                    dc.DrawRectangle(null, new Pen(accent, 4), new Rect(r.X + 2, r.Y + 2, Math.Max(0, r.Width - 4), Math.Max(0, r.Height - 4)));

                var number = MakeText((i + 1).ToString(CultureInfo.InvariantCulture), 72);
                var detail = MakeText($"Schermo {i + 1} · {m.Width} × {m.Height}", 15);
                double w = Math.Max(number.Width, detail.Width) + 48, h = number.Height + detail.Height + 24;
                var box = new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
                dc.DrawRoundedRectangle(on ? accent : new SolidColorBrush(Color.FromArgb(220, 30, 31, 36)), null, box, 14, 14);
                dc.DrawText(number, new Point(box.X + (w - number.Width) / 2, box.Y + 8));
                dc.DrawText(detail, new Point(box.X + (w - detail.Width) / 2, box.Y + 12 + number.Height));
            }
        }

        private void DrawMagnifier(DrawingContext dc, Point m)
        {
            const double size = 132;
            const int pixels = ZoomPixels;
            double cell = size / pixels;

            double x = m.X + 24, y = m.Y + 24;
            if (x + size > ActualWidth) x = m.X - 24 - size;
            if (y + size + 46 > ActualHeight) y = m.Y - 24 - size - 46;
            var box = new Rect(x, y, size, size);

            // Lente: copia i 15 × 15 pixel attorno al mouse in una piccola immagine riutilizzata
            // (un ImageBrush sull'istantanea intera costava molta memoria e GPU a ogni movimento).
            int left = (int)_mouse.X - pixels / 2, top = (int)_mouse.Y - pixels / 2;
            for (int row = 0; row < pixels; row++)
            {
                int sy = top + row;
                for (int col = 0; col < pixels; col++)
                {
                    int sx = left + col, d = (row * pixels + col) * 4;
                    if (sx < 0 || sy < 0 || sx >= _shot.Width || sy >= _shot.Height)
                    {
                        _zoomPixels[d] = _zoomPixels[d + 1] = _zoomPixels[d + 2] = 0;
                        _zoomPixels[d + 3] = 255;
                    }
                    else
                        Buffer.BlockCopy(_shot.Pixels, sy * _shot.Stride + sx * 4, _zoomPixels, d, 4);
                }
            }
            _zoom.WritePixels(new Int32Rect(0, 0, pixels, pixels), _zoomPixels, pixels * 4, 0);
            dc.DrawRectangle(Brushes.Black, null, box);
            dc.DrawImage(_zoom, box);
            dc.DrawRectangle(null, new Pen(Brushes.White, 2), box);

            var center = new Rect(x + (pixels / 2) * cell, y + (pixels / 2) * cell, cell, cell);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Accent), 1.5), center);

            var c = _shot.GetPixel((int)_mouse.X, (int)_mouse.Y);
            var info = new Rect(x, y + size + 4, size, 42);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(230, 30, 31, 36)), null, info, 4, 4);
            dc.DrawRectangle(new SolidColorBrush(c), new Pen(Brushes.White, 1), new Rect(info.X + 6, info.Y + 6, 30, 30));
            DrawText(dc, $"{(int)_mouse.X + _shot.Bounds.X}, {(int)_mouse.Y + _shot.Bounds.Y}", new Point(info.X + 44, info.Y + 4), 12);
            DrawText(dc, $"#{c.R:X2}{c.G:X2}{c.B:X2}", new Point(info.X + 44, info.Y + 21), 12);
        }

        private FormattedText MakeText(string text, double size) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size,
                Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        private void DrawText(DrawingContext dc, string text, Point at, double size) =>
            dc.DrawText(MakeText(text, size), at);

        private void DrawLabel(DrawingContext dc, string text, Point at, bool above)
        {
            var ft = MakeText(text, 12);
            double w = ft.Width + 12, h = ft.Height + 6;
            double y = above ? at.Y - h - 4 : at.Y;
            if (y < 0) y = at.Y + 4;
            double x = Math.Min(at.X, Math.Max(0, ActualWidth - w));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 30, 31, 36)), null, new Rect(x, y, w, h), 4, 4);
            dc.DrawText(ft, new Point(x + 6, y + 3));
        }
    }
}
