using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenLove.Recording
{
    /// <summary>
    /// Cattura audio di sistema (WASAPI loopback) e microfono, li porta a 48 kHz stereo e li mescola.
    /// Il ritmo è dato dall'orologio della registrazione: quando una sorgente tace (WASAPI loopback
    /// non invia nulla durante il silenzio) si inserisce silenzio, così l'audio resta allineato al video.
    /// </summary>
    internal sealed class AudioMixer : IDisposable
    {
        public const int SampleRate = 48000;

        /// <summary>Ritardo di lettura: assorbe l'irregolarità con cui arrivano i pacchetti audio.</summary>
        private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(120);

        private readonly BlockingCollection<AudioChunk> _output;
        private readonly List<(IWaveIn capture, BufferedWaveProvider buffer)> _sources = new();
        private readonly MixingSampleProvider _mixer;
        private Stopwatch? _clock;
        private Thread? _thread;
        private long _produced; // campioni (per canale) già prodotti
        private readonly object _pumpLock = new();
        private volatile bool _stop;
        private volatile bool _paused;

        public AudioMixer(bool systemAudio, bool microphone, BlockingCollection<AudioChunk> output)
        {
            _output = output;
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)) { ReadFully = true };
            var warnings = new List<string>();

            if (systemAudio)
            {
                try { Add(new WasapiLoopbackCapture()); }
                catch (Exception ex) { warnings.Add("audio di sistema non disponibile (" + ex.Message + ")"); }
            }
            if (microphone)
            {
                try { Add(new WasapiCapture(WasapiCapture.GetDefaultCaptureDevice())); }
                catch (Exception ex) { warnings.Add("microfono non disponibile (" + ex.Message + ")"); }
            }
            Warning = warnings.Count > 0 ? string.Join("; ", warnings) : null;
        }

        public bool HasSources => _sources.Count > 0;
        public string? Warning { get; }

        private void Add(WasapiCapture capture)
        {
            // Il formato condiviso WASAPI è sempre float a 32 bit (spesso dichiarato come "Extensible").
            var format = capture.WaveFormat;
            if (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32)
                format = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);

            var buffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                DiscardOnBufferOverflow = true,
                ReadFully = true, // silenzio quando non ci sono dati
            };
            capture.DataAvailable += (_, e) =>
            {
                if (!_paused && e.BytesRecorded > 0)
                    buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            ISampleProvider provider = buffer.ToSampleProvider();
            if (provider.WaveFormat.Channels == 1)
                provider = new MonoToStereoSampleProvider(provider);
            else if (provider.WaveFormat.Channels > 2)
                provider = new FirstTwoChannels(provider);
            if (provider.WaveFormat.SampleRate != SampleRate)
                provider = new WdlResamplingSampleProvider(provider, SampleRate);

            _mixer.AddMixerInput(provider);
            _sources.Add((capture, buffer));
        }

        public void Start(Stopwatch clock)
        {
            _clock = clock;
            foreach (var (capture, buffer) in _sources)
            {
                buffer.ClearBuffer();
                capture.StartRecording();
            }
            _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "ScreenLove audio" };
            _thread.Start();
        }

        /// <summary>
        /// Si chiama dopo aver fermato l'orologio: completa la traccia fino all'istante della pausa,
        /// così l'audio registrato dopo la ripresa parte esattamente allineato al video.
        /// </summary>
        public void Pause()
        {
            lock (_pumpLock)
            {
                _paused = true;
                Pump(TimeSpan.Zero);
            }
        }

        public void Resume()
        {
            lock (_pumpLock)
            {
                // l'audio arrivato durante la pausa viene scartato
                foreach (var (_, buffer) in _sources)
                    buffer.ClearBuffer();
                _paused = false;
            }
        }

        /// <summary>Produce l'audio fino alla fine della registrazione e chiude il flusso.</summary>
        public void Stop()
        {
            _stop = true;
            _thread?.Join();
        }

        private void Loop()
        {
            try
            {
                while (!_stop)
                {
                    Thread.Sleep(10);
                    lock (_pumpLock)
                    {
                        if (!_paused)
                            Pump(Latency);
                    }
                }
                lock (_pumpLock)
                    Pump(TimeSpan.Zero); // ultimi campioni
            }
            catch
            {
                // l'audio non deve mai bloccare il salvataggio del video
            }
            finally
            {
                _output.CompleteAdding();
            }
        }

        private void Pump(TimeSpan lag)
        {
            long target = (long)((_clock!.Elapsed - lag).TotalSeconds * SampleRate);
            int frames = (int)Math.Min(target - _produced, SampleRate); // al massimo 1 s per volta
            if (frames <= 0) return;

            var floats = new float[frames * 2];
            _mixer.Read(floats, 0, floats.Length);

            var pcm = new byte[frames * 4];
            for (int i = 0; i < floats.Length; i++)
            {
                short s = (short)Math.Clamp((int)(floats[i] * 32767f), short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)s;
                pcm[i * 2 + 1] = (byte)(s >> 8);
            }

            var chunk = new AudioChunk(pcm,
                TimeSpan.FromTicks(_produced * TimeSpan.TicksPerSecond / SampleRate),
                TimeSpan.FromTicks((long)frames * TimeSpan.TicksPerSecond / SampleRate));
            _produced += frames;
            _output.TryAdd(chunk, 2000);
        }

        public void Dispose()
        {
            foreach (var (capture, _) in _sources)
            {
                try { capture.StopRecording(); } catch { /* già fermo */ }
                capture.Dispose();
            }
            _sources.Clear();
        }

        /// <summary>Riduce a stereo le sorgenti multicanale (es. 5.1) tenendo sinistro e destro.</summary>
        private sealed class FirstTwoChannels : ISampleProvider
        {
            private readonly ISampleProvider _source;
            private readonly int _channels;
            private float[] _buffer = Array.Empty<float>();

            public FirstTwoChannels(ISampleProvider source)
            {
                _source = source;
                _channels = source.WaveFormat.Channels;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                int needed = frames * _channels;
                if (_buffer.Length < needed) _buffer = new float[needed];
                int read = _source.Read(_buffer, 0, needed) / _channels;
                for (int i = 0; i < read; i++)
                {
                    buffer[offset + i * 2] = _buffer[i * _channels];
                    buffer[offset + i * 2 + 1] = _buffer[i * _channels + 1];
                }
                return read * 2;
            }
        }
    }
}
