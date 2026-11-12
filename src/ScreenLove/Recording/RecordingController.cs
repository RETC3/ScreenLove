using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ScreenLove.Capture;
using ScreenLove.Services;

namespace ScreenLove.Recording
{
    /// <summary>Gestisce una sessione di registrazione: scelta della zona, conto alla rovescia, controlli, salvataggio.</summary>
    public sealed class RecordingController
    {
        private readonly AppSettings _settings;
        private readonly CaptureStore _store;
        private ScreenRecorder? _recorder;
        private RecordingFrameWindow? _frame;
        private RecordingBarWindow? _bar;
        private string? _path;
        private bool _busy;
        private WebcamWindow? _webcam;

        private async Task CloseWebcamAsync()
        {
            if (_webcam == null) return;
            var w = _webcam;
            _webcam = null;
            await w.StopCameraAsync();
            w.Close();
        }

        public RecordingController(AppSettings settings, CaptureStore store)
        {
            _settings = settings;
            _store = store;
        }

        public bool IsRecording => _recorder != null;

        /// <summary>Video salvato (percorso del file).</summary>
        public event Action<string>? Saved;

        /// <summary>Messaggi per l'utente (titolo, testo).</summary>
        public event Action<string, string>? Message;

        /// <summary>Avvia, oppure ferma e salva se una registrazione è in corso.</summary>
        public Task ToggleAsync() => IsRecording ? StopAsync() : StartAsync();

        public async Task StartAsync()
        {
            if (_busy || IsRecording) return;
            _busy = true;
            try
            {
                var shot = ScreenCapture.CaptureVirtualScreen(includeCursor: false);
                var overlay = new CaptureOverlayWindow(shot, WindowFinder.GetWindowRects(shot.Bounds), WindowFinder.GetMonitorRects(shot.Bounds),
                    "VIDEO — Clic: finestra · Trascina: zona · F: schermo intero · Esc: annulla");
                if (overlay.ShowDialog() != true)
                    return;

                var sel = overlay.Selection;
                var region = new Int32Rect(sel.X + shot.Bounds.X, sel.Y + shot.Bounds.Y, sel.Width, sel.Height);
                if (region.Width < 16 || region.Height < 16)
                    return;

                if (_settings.RecordWebcam)
                {
                    var (webcam, error) = await WebcamWindow.OpenAsync(region);
                    _webcam = webcam;
                    if (error != null)
                        Message?.Invoke("Webcam non disponibile", "La registrazione continua senza webcam: " + error);
                }

                await CountdownWindow.RunAsync(3);

                _path = _store.NewPath(".mp4");
                _recorder = new ScreenRecorder(new RecordingOptions
                {
                    Region = region,
                    OutputPath = _path,
                    Fps = _settings.RecordFps,
                    ShowCursor = _settings.RecordCursor,
                    SystemAudio = _settings.RecordSystemAudio,
                    Microphone = _settings.RecordMicrophone,
                });

                try
                {
                    await _recorder.StartAsync();
                }
                catch (Exception ex)
                {
                    _recorder = null;
                    TryDelete(_path);
                    await CloseWebcamAsync();
                    Message?.Invoke("Registrazione non avviata", ex.Message);
                    return;
                }

                if (_recorder.Warning != null)
                    Message?.Invoke("Registrazione avviata", "Attenzione: " + _recorder.Warning);

                string audio = !_recorder.HasAudio ? "senza audio"
                    : _settings.RecordSystemAudio && _settings.RecordMicrophone ? "sistema + microfono"
                    : _settings.RecordMicrophone ? "microfono" : "audio di sistema";

                _frame = new RecordingFrameWindow(region);
                _frame.Show();
                var recorder = _recorder;
                _bar = new RecordingBarWindow(region, () => recorder.Elapsed, audio);
                _bar.PauseClicked += TogglePause;
                _bar.StopClicked += () => _ = StopAsync();
                _bar.CancelClicked += () => _ = StopAsync(discard: true);
                _bar.Show();
            }
            finally
            {
                _busy = false;
            }
        }

        public void TogglePause()
        {
            if (_recorder == null) return;
            if (_recorder.IsPaused) _recorder.Resume();
            else _recorder.Pause();
            _bar?.SetPaused(_recorder.IsPaused);
            _frame?.SetPaused(_recorder.IsPaused);
        }

        public async Task StopAsync(bool discard = false)
        {
            if (_busy || _recorder == null) return;
            _busy = true;
            var recorder = _recorder;
            var path = _path!;
            try
            {
                _frame?.Close();
                _frame = null;
                _bar?.SetSaving();

                try
                {
                    await recorder.StopAsync();
                    await CloseWebcamAsync();
                }
                catch (Exception ex)
                {
                    if (!discard)
                        Message?.Invoke("Errore nella registrazione", ex.Message);
                    discard = true;
                }

                if (discard || !File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    TryDelete(path);
                }
                else
                {
                    _store.Refresh(path);
                    Saved?.Invoke(path);
                }
            }
            finally
            {
                await CloseWebcamAsync();
                _bar?.Close();
                _bar = null;
                _recorder = null;
                _path = null;
                _busy = false;
            }
        }

        private static void TryDelete(string? path)
        {
            try
            {
                if (path != null && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // file ancora aperto: verrà sovrascritto o eliminato più tardi
            }
        }
    }
}
