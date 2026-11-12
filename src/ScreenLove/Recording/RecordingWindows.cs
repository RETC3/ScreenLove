using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenLove.Native;

namespace ScreenLove.Recording
{
    internal static class OverlayHelpers
    {
        public static readonly Color Accent = Color.FromRgb(0xFF, 0x3D, 0x7F);

        /// <summary>Esclude la finestra da qualunque cattura (anche dalla nostra registrazione).</summary>
        public static void ExcludeFromCapture(Window w) =>
            NativeMethods.SetWindowDisplayAffinity(new WindowInteropHelper(w).Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

        public static void MoveTo(Window w, int x, int y, int width, int height, bool resize)
        {
            uint flags = 0x0010 /* NOACTIVATE */ | (resize ? 0u : 0x0001u /* NOSIZE */);
            NativeMethods.SetWindowPos(new WindowInteropHelper(w).Handle, new IntPtr(-1) /* TOPMOST */, x, y, width, height, flags);
        }
    }

    /// <summary>Cornice attorno alla zona registrata: lascia passare i clic e non compare nel video.</summary>
    public sealed class RecordingFrameWindow : Window
    {
        private const int Thickness = 3;
        private readonly Int32Rect _region;
        private readonly Border _border;

        public RecordingFrameWindow(Int32Rect region)
        {
            _region = region;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            IsHitTestVisible = false;
            Left = -32000;
            Top = -32000;
            Width = 10;
            Height = 10;
            _border = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), BorderThickness = new Thickness(Thickness) };
            Content = _border;

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
                    ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW);
                OverlayHelpers.ExcludeFromCapture(this);
                // La cornice sta appena fuori dalla zona, così non la copre.
                OverlayHelpers.MoveTo(this, _region.X - Thickness, _region.Y - Thickness,
                    _region.Width + Thickness * 2, _region.Height + Thickness * 2, resize: true);
            };
        }

        public void SetPaused(bool paused) =>
            _border.BorderBrush = new SolidColorBrush(paused ? Color.FromRgb(0xFB, 0x8C, 0x00) : Color.FromRgb(0xE5, 0x39, 0x35));
    }

    /// <summary>Barra con tempo trascorso, Pausa, Stop e Annulla. Non compare nel video.</summary>
    public sealed class RecordingBarWindow : Window
    {
        private readonly Int32Rect _region;
        private readonly Func<TimeSpan> _elapsed;
        private readonly TextBlock _time;
        private readonly TextBlock _dot;
        private readonly Button _pause;
        private readonly DispatcherTimer _timer;
        private bool _paused;

        public RecordingBarWindow(Int32Rect region, Func<TimeSpan> elapsed, string audioLabel)
        {
            _region = region;
            _elapsed = elapsed;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Left = -32000;
            Top = -32000;
            FontFamily = new FontFamily("Segoe UI");

            _dot = new TextBlock { Text = "●", Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 1) };
            _time = new TextBlock { Text = "00:00", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 52 };
            var audio = new TextBlock { Text = audioLabel, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9C, 0xA5)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };

            _pause = MakeButton("Pausa", false, (_, _) => PauseClicked?.Invoke());
            var stop = MakeButton("■  Stop", true, (_, _) => StopClicked?.Invoke());
            var cancel = MakeButton("Annulla", false, (_, _) => CancelClicked?.Invoke());
            cancel.ToolTip = "Ferma ed elimina la registrazione";

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(_dot);
            panel.Children.Add(_time);
            panel.Children.Add(audio);
            panel.Children.Add(_pause);
            panel.Children.Add(stop);
            panel.Children.Add(cancel);

            Content = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(240, 30, 31, 36)),
                BorderBrush = new SolidColorBrush(OverlayHelpers.Accent),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(8, 5, 6, 5),
                Child = panel,
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) => Refresh();
            SourceInitialized += (_, _) => OverlayHelpers.ExcludeFromCapture(this);
            Loaded += (_, _) =>
            {
                Place();
                _timer.Start();
            };
            Closed += (_, _) => _timer.Stop();
        }

        public event Action? PauseClicked;
        public event Action? StopClicked;
        public event Action? CancelClicked;

        public void SetPaused(bool paused)
        {
            _paused = paused;
            _pause.Content = paused ? "▶  Riprendi" : "Pausa";
            Refresh();
        }

        public void SetSaving()
        {
            _timer.Stop();
            _time.Text = "Salvataggio…";
            foreach (var child in ((StackPanel)((Border)Content).Child).Children)
                if (child is Button b) b.IsEnabled = false;
        }

        private void Refresh()
        {
            var t = _elapsed();
            _time.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
            // il punto lampeggia durante la pausa
            _dot.Opacity = _paused && DateTime.Now.Millisecond < 500 ? 0.25 : 1;
        }

        private Button MakeButton(string text, bool accent, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = text,
                Style = (Style)Application.Current.FindResource(accent ? "AccentButton" : "ToolButton"),
                Padding = new Thickness(10, 4, 10, 4),
            };
            b.Click += click;
            return b;
        }

        /// <summary>Sotto la zona, al centro; se non c'è spazio sopra; altrimenti dentro, in basso.</summary>
        private void Place()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            var screen = System.Windows.Forms.Screen.FromRectangle(
                new System.Drawing.Rectangle(_region.X, _region.Y, _region.Width, _region.Height)).WorkingArea;

            int x = _region.X + (_region.Width - w) / 2;
            int y = _region.Y + _region.Height + 10;
            if (y + h > screen.Bottom) y = _region.Y - h - 10;
            if (y < screen.Top) y = _region.Y + _region.Height - h - 16;
            x = Math.Clamp(x, screen.Left + 8, Math.Max(screen.Left + 8, screen.Right - w - 8));
            OverlayHelpers.MoveTo(this, x, y, 0, 0, resize: false);
        }
    }
}
