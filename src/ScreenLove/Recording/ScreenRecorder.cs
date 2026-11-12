using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ScreenLove.Native;
using ScreenLove.Services;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using D = System.Drawing;
using DI = System.Drawing.Imaging;

namespace ScreenLove.Recording
{
    public sealed class RecordingOptions
    {
        /// <summary>Zona da registrare, in coordinate schermo fisiche.</summary>
        public Int32Rect Region { get; init; }
        public string OutputPath { get; init; } = "";
        public int Fps { get; init; } = 30;
        public bool ShowCursor { get; init; } = true;
        public bool SystemAudio { get; init; }
        public bool Microphone { get; init; }
    }

    internal sealed record VideoFrame(byte[] Buffer, int Length, TimeSpan Time);

    internal sealed record AudioChunk(byte[] Buffer, TimeSpan Time, TimeSpan Duration);

    /// <summary>
    /// Registra una zona dello schermo in MP4 (H.264 + AAC).
    /// I fotogrammi (GDI) e l'audio (NAudio) vengono passati a un MediaStreamSource
    /// che il MediaTranscoder di Windows codifica (con accelerazione hardware se disponibile).
    /// Video e audio usano lo stesso orologio, che si ferma durante la pausa.
    /// </summary>
    public sealed class ScreenRecorder
    {
        private readonly RecordingOptions _o;
        private readonly Stopwatch _clock = new();
        private readonly BlockingCollection<VideoFrame> _video = new(boundedCapacity: 60);
        private readonly BlockingCollection<AudioChunk> _audio = new(boundedCapacity: 1000);
        private readonly ConcurrentBag<byte[]> _pool = new();
        private readonly int _width, _height, _stride;
        private readonly TimeSpan _frameDuration;

        private AudioMixer? _mixer;
        private Thread? _videoThread;
        private Task? _transcode;
        private FileStream? _file;
        private volatile bool _stopping;
        private volatile bool _paused;
        private Exception? _captureError;

        public ScreenRecorder(RecordingOptions options)
        {
            _o = options;
            // H.264 richiede dimensioni pari
            _width = Math.Max(2, options.Region.Width & ~1);
            _height = Math.Max(2, options.Region.Height & ~1);
            _stride = _width * 4;
            _frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / options.Fps);
        }

        public TimeSpan Elapsed => _clock.Elapsed;
        public bool IsPaused => _paused;
        public bool HasAudio => _mixer != null;

        /// <summary>Avvisi non bloccanti (es. microfono non trovato).</summary>
        public string? Warning { get; private set; }

        public async Task StartAsync()
        {
            NativeMethods.timeBeginPeriod(1);

            var videoProps = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_width, (uint)_height);
            var videoDesc = new VideoStreamDescriptor(videoProps);

            AudioStreamDescriptor? audioDesc = null;
            if (_o.SystemAudio || _o.Microphone)
            {
                var mixer = new AudioMixer(_o.SystemAudio, _o.Microphone, _audio);
                Warning = mixer.Warning;
                if (mixer.HasSources)
                {
                    _mixer = mixer;
                    audioDesc = new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(AudioMixer.SampleRate, 2, 16));
                }
                else
                {
                    mixer.Dispose();
                }
            }

            var source = audioDesc != null ? new MediaStreamSource(videoDesc, audioDesc) : new MediaStreamSource(videoDesc);
            source.BufferTime = TimeSpan.Zero;
            source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
            source.SampleRequested += OnSampleRequested;

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = (uint)_width;
            profile.Video.Height = (uint)_height;
            profile.Video.FrameRate.Numerator = (uint)_o.Fps;
            profile.Video.FrameRate.Denominator = 1;
            profile.Video.PixelAspectRatio.Numerator = 1;
            profile.Video.PixelAspectRatio.Denominator = 1;
            profile.Video.Bitrate = (uint)Math.Clamp((long)_width * _height * _o.Fps / 8, 2_000_000, 40_000_000);
            profile.Audio = audioDesc != null ? AudioEncodingProperties.CreateAac(AudioMixer.SampleRate, 2, 192_000) : null;

            _file = new FileStream(_o.OutputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

            // La cattura parte prima della preparazione: il transcoder potrebbe chiedere subito un campione.
            _clock.Start();
            _mixer?.Start(_clock);
            _videoThread = new Thread(VideoLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ScreenLove video" };
            _videoThread.Start();

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, _file.AsRandomAccessStream(), profile);
            if (!prepared.CanTranscode)
            {
                await StopCaptureAsync();
                throw new InvalidOperationException("La codifica video non è disponibile su questo PC (" + prepared.FailureReason + ").");
            }
            _transcode = prepared.TranscodeAsync().AsTask();
        }

        public void Pause()
        {
            if (_paused) return;
            _paused = true;
            _clock.Stop();
            _mixer?.Pause();
        }

        public void Resume()
        {
            if (!_paused) return;
            _mixer?.Resume();
            _clock.Start();
            _paused = false;
        }

        /// <summary>Ferma la registrazione e completa il file MP4.</summary>
        public async Task StopAsync()
        {
            await StopCaptureAsync();
            try
            {
                if (_transcode != null)
                    await _transcode;
            }
            finally
            {
                _file?.Dispose();
                NativeMethods.timeEndPeriod(1);
            }
            if (_captureError != null)
                throw new InvalidOperationException("Errore durante la cattura: " + _captureError.Message, _captureError);
        }

        private async Task StopCaptureAsync()
        {
            if (_stopping) return;
            _stopping = true;
            if (_paused)
            {
                _paused = false; // lascia uscire il ciclo video
            }
            await Task.Run(() =>
            {
                _videoThread?.Join();
                _mixer?.Stop();
            });
            _mixer?.Dispose();
        }

        // ---------- fotogrammi ----------

        private void VideoLoop()
        {
            var r = _o.Region;
            long index = 0;
            try
            {
                using var bmp = new D.Bitmap(_width, _height, DI.PixelFormat.Format32bppRgb);
                using var g = D.Graphics.FromImage(bmp);
                var rect = new D.Rectangle(0, 0, _width, _height);

                while (!_stopping)
                {
                    if (_paused)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    var due = TimeSpan.FromTicks(_frameDuration.Ticks * index);
                    var now = _clock.Elapsed;
                    if (now < due)
                    {
                        var wait = due - now;
                        if (wait.TotalMilliseconds >= 2) Thread.Sleep(wait - TimeSpan.FromMilliseconds(1));
                        else Thread.SpinWait(200);
                        continue;
                    }
                    // In ritardo (PC lento): si salta ai fotogrammi attuali invece di accumulare ritardo.
                    long current = now.Ticks / _frameDuration.Ticks;
                    if (current > index) index = current;

                    g.CopyFromScreen(r.X, r.Y, 0, 0, new D.Size(_width, _height), D.CopyPixelOperation.SourceCopy);
                    if (_o.ShowCursor)
                        ScreenCapture.DrawCursor(g, r.X, r.Y);

                    if (!_pool.TryTake(out var buffer))
                        buffer = new byte[_stride * _height];

                    var data = bmp.LockBits(rect, DI.ImageLockMode.ReadOnly, DI.PixelFormat.Format32bppRgb);
                    try
                    {
                        // I campioni RGB non compressi di Media Foundation sono dal basso verso l'alto:
                        // copiamo le righe in ordine inverso.
                        for (int y = 0; y < _height; y++)
                            Marshal.Copy(data.Scan0 + y * data.Stride, buffer, (_height - 1 - y) * _stride, _stride);
                    }
                    finally
                    {
                        bmp.UnlockBits(data);
                    }

                    var frame = new VideoFrame(buffer, _stride * _height, TimeSpan.FromTicks(_frameDuration.Ticks * index));
                    if (!_video.TryAdd(frame))
                        _pool.Add(buffer); // codificatore in ritardo: si scarta il fotogramma
                    index++;
                }
            }
            catch (Exception ex)
            {
                _captureError = ex;
            }
            finally
            {
                _video.CompleteAdding();
            }
        }

        // ---------- campioni per il transcoder ----------

        private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            bool isVideo = request.StreamDescriptor is VideoStreamDescriptor;

            Task.Run(() =>
            {
                try
                {
                    if (isVideo)
                    {
                        if (_video.TryTake(out var f, Timeout.Infinite))
                        {
                            var sample = MediaStreamSample.CreateFromBuffer(f.Buffer.AsBuffer(0, f.Length), f.Time);
                            sample.Duration = _frameDuration;
                            sample.Processed += (_, _) => _pool.Add(f.Buffer);
                            request.Sample = sample;
                        }
                        else
                        {
                            request.Sample = null; // fine del flusso
                        }
                    }
                    else
                    {
                        if (_audio.TryTake(out var a, Timeout.Infinite))
                        {
                            var sample = MediaStreamSample.CreateFromBuffer(a.Buffer.AsBuffer(), a.Time);
                            sample.Duration = a.Duration;
                            request.Sample = sample;
                        }
                        else
                        {
                            request.Sample = null;
                        }
                    }
                }
                catch
                {
                    request.Sample = null;
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }
    }
}
