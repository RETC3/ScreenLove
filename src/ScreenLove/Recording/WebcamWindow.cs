using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace ScreenLove.Recording
{
    /// <summary>
    /// Bolla rotonda con l'immagine della webcam, sopra la zona registrata: essendo sullo schermo
    /// finisce nel video. Si può trascinare con il mouse.
    /// </summary>
    public sealed class WebcamWindow : Window
    {
        private const int Size = 190;
        private readonly Int32Rect _region;
        private readonly ImageBrush _brush = new() { Stretch = Stretch.UniformToFill };
        private MediaCapture? _capture;
        private MediaFrameReader? _reader;
        private WriteableBitmap? _bitmap;
        private byte[] _buffer = Array.Empty<byte>();
        private int _busy;

        private WebcamWindow(Int32Rect region)
        {
            _region = region;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Width = Size;
            Height = Size;
            Left = -32000;
            Top = -32000;
            Cursor = Cursors.SizeAll;
            ToolTip = "Trascina per spostare la webcam";

            Content = new Grid
            {
                Children =
                {
                    new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x24)) },
                    new Ellipse { Fill = _brush, Margin = new Thickness(3) },
                    new Ellipse { Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x3D, 0x7F)), StrokeThickness = 3 },
                },
            };
            MouseLeftButtonDown += (_, _) => DragMove();
            SourceInitialized += (_, _) => PlaceInCorner();
        }

        /// <summary>Apre la webcam predefinita. Restituisce null (con il motivo) se non è disponibile.</summary>
        public static async Task<(WebcamWindow? window, string? error)> OpenAsync(Int32Rect region)
        {
            var window = new WebcamWindow(region);
            try
            {
                await window.StartCameraAsync();
                window.Show();
                return (window, null);
            }
            catch (Exception ex)
            {
                await window.StopCameraAsync();
                window.Close();
                return (null, ex.Message);
            }
        }

        private async Task StartCameraAsync()
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var (group, info) = groups
                .SelectMany(g => g.SourceInfos.Select(i => (g, i)))
                .Where(x => x.i.SourceKind == MediaFrameSourceKind.Color)
                .OrderBy(x => x.i.MediaStreamType == MediaStreamType.VideoPreview ? 0 : 1)
                .FirstOrDefault();
            if (group == null)
                throw new InvalidOperationException("nessuna webcam trovata");

            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });

            var source = _capture.FrameSources[info.Id];
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _reader.FrameArrived += OnFrameArrived;
            var status = await _reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
                throw new InvalidOperationException("webcam non avviata (" + status + ")");
        }

        private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            // un fotogramma alla volta: se l'interfaccia è indietro si salta
            if (System.Threading.Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                using var frame = sender.TryAcquireLatestFrame();
                var sb = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (sb == null) { _busy = 0; return; }

                using var bgra = sb.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && sb.BitmapAlphaMode != BitmapAlphaMode.Straight
                    ? SoftwareBitmap.Copy(sb)
                    : SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                if (_buffer.Length != w * h * 4) _buffer = new byte[w * h * 4];
                bgra.CopyToBuffer(_buffer.AsBuffer());

                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
                        {
                            _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                            _brush.ImageSource = _bitmap;
                        }
                        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), _buffer, w * 4, 0);
                    }
                    finally
                    {
                        _busy = 0;
                    }
                });
            }
            catch
            {
                _busy = 0;
            }
        }

        /// <summary>In basso a destra dentro la zona registrata.</summary>
        private void PlaceInCorner()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            int size = (int)(Size * dpi.DpiScaleX);
            int x = _region.X + _region.Width - size - 16;
            int y = _region.Y + _region.Height - size - 16;
            Native.NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010);
        }

        public async Task StopCameraAsync()
        {
            try
            {
                if (_reader != null)
                {
                    _reader.FrameArrived -= OnFrameArrived;
                    await _reader.StopAsync();
                    _reader.Dispose();
                    _reader = null;
                }
            }
            catch { /* già fermo */ }
            _capture?.Dispose();
            _capture = null;
        }
    }
}
