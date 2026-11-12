using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenLove.Native;
using ScreenLove.Services;

namespace ScreenLove.Capture
{
    /// <summary>
    /// Finestra a schermo intero sopra l'istantanea congelata del desktop.
    /// Passando col mouse evidenzia la finestra sotto il cursore, trascinando seleziona un'area.
    /// Click = cattura finestra evidenziata, trascina = area, F = schermo intero,
    /// frecce = sposta di 1 px, Ctrl+C = copia il colore, Esc/clic destro = annulla.
    /// </summary>
    public sealed class CaptureOverlayWindow : Window
    {
        private static readonly Color Accent = Color.FromRgb(0xFF, 0x3D, 0x7F);

        private readonly ScreenShot _shot;
        private readonly List<Int32Rect> _windows;
        private readonly List<Int32Rect> _monitors;

        private Point _mouse;          // pixel fisici, relativi al desktop virtuale
        private bool _hasMouse;
        private bool _down;
        private bool _dragging;
        private Point _downPoint;
        private Int32Rect _hover = Int32Rect.Empty;
        private Int32Rect _selection = Int32Rect.Empty;
        private readonly Surface _surface;

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

        public CaptureOverlayWindow(ScreenShot shot, List<Int32Rect> windows, List<Int32Rect> monitors, string? hint = null)
        {
            _hint = hint ?? "Clic: finestra · Trascina: area · F: schermo intero · Ctrl+C: copia colore · Esc: annulla";
            _shot = shot;
            _windows = windows;
            _monitors = monitors;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = shot.Bounds.X;
            Top = shot.Bounds.Y;
            Width = 100;
            Height = 100;
            Title = "ScreenLove — cattura";
            Focusable = true;

            _surface = new Surface(this);
            Content = _surface;
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
                    return WindowFinder.Clip(m, _shot.Width, _shot.Height);
            return new Int32Rect(0, 0, _shot.Width, _shot.Height);
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
            if (_down && !_dragging && (Math.Abs(_mouse.X - _downPoint.X) > 3 || Math.Abs(_mouse.Y - _downPoint.Y) > 3))
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
                    Complete(new Int32Rect(0, 0, _shot.Width, _shot.Height));
                    break;
                case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
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
            dc.DrawImage(_shot.Image, full);

            var active = _dragging ? _selection : _hover;
            Geometry dim = new RectangleGeometry(full);
            if (!active.IsEmpty)
                dim = new CombinedGeometry(GeometryCombineMode.Exclude, dim, new RectangleGeometry(ToDip(active)));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, dim);

            if (!active.IsEmpty)
            {
                var r = ToDip(active);
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(Accent), 2), r);
                DrawLabel(dc, $"{active.Width} × {active.Height}", new Point(r.X, r.Y), above: true);
            }

            if (_hasMouse)
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

        private void DrawMagnifier(DrawingContext dc, Point m)
        {
            const double size = 132;
            const int pixels = 15; // pixel visualizzati per lato
            double cell = size / pixels;

            double x = m.X + 24, y = m.Y + 24;
            if (x + size > ActualWidth) x = m.X - 24 - size;
            if (y + size + 46 > ActualHeight) y = m.Y - 24 - size - 46;
            var box = new Rect(x, y, size, size);

            var brush = new ImageBrush(_shot.Image)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(_mouse.X - pixels / 2, _mouse.Y - pixels / 2, pixels, pixels),
                Stretch = Stretch.Fill,
            };
            dc.DrawRectangle(Brushes.Black, null, box);
            dc.DrawRectangle(brush, new Pen(Brushes.White, 2), box);

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
