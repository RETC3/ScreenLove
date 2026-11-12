using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ScreenLove.Video
{
    /// <summary>Anteprima del video con scelta di inizio e fine; il risultato è un nuovo file (l'originale resta).</summary>
    public partial class VideoTrimWindow : Window
    {
        private readonly string _path;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private TimeSpan _duration;
        private TimeSpan _start;
        private TimeSpan _end;
        private bool _playing;
        private bool _updating;
        private bool _dragging;

        public VideoTrimWindow(string path)
        {
            InitializeComponent();
            Services.WindowFit.Apply(this);
            Icon = Services.IconFactory.AppImageSource;
            _path = path;
            Title = "Taglia video — " + Path.GetFileName(path);
            _timer.Tick += (_, _) => SyncFromPlayer();
            Player.Source = new Uri(path);
            Player.Play();   // serve per caricare il video; si mette subito in pausa all'apertura
            PreviewKeyDown += OnKey;
            Closed += (_, _) =>
            {
                _timer.Stop();
                Player.Close();
            };
            SizeChanged += (_, _) => UpdateRange();
        }

        private TimeSpan? _reopenAt;

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            Player.Pause();
            if (_reopenAt is { } at)
            {
                // riapertura dopo un salvataggio: si conservano inizio, fine e posizione
                _reopenAt = null;
                Player.Position = at;
                SyncFromPlayer();
                _timer.Start();
                return;
            }
            _duration = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero;
            _start = TimeSpan.Zero;
            _end = _duration;
            Timeline.Maximum = Math.Max(0.1, _duration.TotalSeconds);
            Player.Position = TimeSpan.Zero;
            UpdateRange();
            SyncFromPlayer();
            _timer.Start();
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e) => SetPlaying(false);

        private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e) =>
            StatusText.Text = "Impossibile riprodurre il video: " + e.ErrorException?.Message;

        private void SetPlaying(bool playing)
        {
            _playing = playing;
            if (playing)
            {
                // si riproduce solo la parte scelta
                if (Player.Position < _start || Player.Position >= _end - TimeSpan.FromMilliseconds(100))
                    Player.Position = _start;
                Player.Play();
            }
            else
            {
                Player.Pause();
            }
            PlayButton.Content = playing ? "❚❚" : "▶";
        }

        private void SyncFromPlayer()
        {
            var pos = Player.Position;
            if (_playing && pos >= _end)
            {
                SetPlaying(false);
                Player.Position = _end;
                pos = _end;
            }
            if (!_dragging)
            {
                _updating = true;
                Timeline.Value = pos.TotalSeconds;
                _updating = false;
            }
            TimeText.Text = $"{Format(pos)} / {Format(_duration)}";
        }

        private void Timeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating) return;
            Player.Position = TimeSpan.FromSeconds(e.NewValue);
            TimeText.Text = $"{Format(Player.Position)} / {Format(_duration)}";
        }

        private void Timeline_MouseDown(object sender, MouseButtonEventArgs e) => _dragging = true;
        private void Timeline_MouseUp(object sender, MouseButtonEventArgs e) => _dragging = false;

        private void Play_Click(object sender, RoutedEventArgs e) => SetPlaying(!_playing);

        private void SetStart_Click(object sender, RoutedEventArgs e) => SetStart();
        private void SetEnd_Click(object sender, RoutedEventArgs e) => SetEnd();

        private void SetStart()
        {
            _start = Player.Position;
            if (_end <= _start) _end = _duration;
            UpdateRange();
        }

        private void SetEnd()
        {
            _end = Player.Position;
            if (_end <= _start) _start = TimeSpan.Zero;
            UpdateRange();
        }

        private void UpdateRange()
        {
            if (_duration <= TimeSpan.Zero) return;
            double width = Math.Max(0, Timeline.ActualWidth - 10);
            double x1 = width * _start.TotalSeconds / _duration.TotalSeconds;
            double x2 = width * _end.TotalSeconds / _duration.TotalSeconds;
            System.Windows.Controls.Canvas.SetLeft(RangeRect, x1);
            RangeRect.Width = Math.Max(2, x2 - x1);
            RangeText.Text = $"tieni da {Format(_start)} a {Format(_end)}  ({Format(_end - _start)})";
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Space: SetPlaying(!_playing); break;
                case Key.I: SetStart(); break;
                case Key.O: SetEnd(); break;
                case Key.Left: Player.Position -= TimeSpan.FromSeconds(1.0 / 30); SyncFromPlayer(); break;
                case Key.Right: Player.Position += TimeSpan.FromSeconds(1.0 / 30); SyncFromPlayer(); break;
                default: return;
            }
            e.Handled = true;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_end - _start < TimeSpan.FromMilliseconds(200))
            {
                StatusText.Text = "La parte scelta è troppo corta.";
                return;
            }
            SetPlaying(false);
            SaveButton.IsEnabled = false;
            string output = App.Store.NewPath(".mp4");
            // Il lettore tiene aperto il file: lo si rilascia durante il taglio.
            var position = Player.Position;
            _timer.Stop();
            Player.Close();
            try
            {
                await VideoEditing.TrimAsync(_path, _start, _end, output, p => Dispatcher.BeginInvoke(() => StatusText.Text = $"Salvataggio… {p:P0}"));
                App.Store.Refresh(output);
                StatusText.Text = "Salvato come " + Path.GetFileName(output);
                App.Current.ShowWorkspace(output);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Taglio non riuscito: {ex.Message} (0x{ex.HResult:X8})";
            }
            finally
            {
                SaveButton.IsEnabled = true;
                _reopenAt = position;
                Player.Source = new Uri(_path);
                Player.Play();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private static string Format(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.f") : t.ToString(@"m\:ss\.f");
    }
}
