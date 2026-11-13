using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using ScreenLove.Capture;
using ScreenLove.Editor;
using ScreenLove.Native;
using ScreenLove.Services;
using ScreenLove.Workspace;
using WF = System.Windows.Forms;

namespace ScreenLove
{
    public partial class App : Application
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private Mutex? _mutex;
        private WF.NotifyIcon? _tray;
        private HotkeyManager? _hotkeys;
        private WorkspaceWindow? _workspace;

        public static AppSettings Settings { get; private set; } = null!;
        public static CaptureStore Store { get; private set; } = null!;
        public static CaptureController Capture { get; private set; } = null!;
        public static Recording.RecordingController Recorder { get; private set; } = null!;

        /// <summary>Descrizione dei tasti rapidi effettivamente registrati.</summary>
        public static string HotkeySummary { get; private set; } = "";

        public static new App Current => (App)Application.Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "ScreenLove.SingleInstance", out bool created);
            if (!created)
            {
                MessageBox.Show("ScreenLove è già in esecuzione: trovi l'icona nell'area di notifica.", "ScreenLove");
                Shutdown();
                return;
            }

            base.OnStartup(e);
            DispatcherUnhandledException += (_, args) =>
            {
                MessageBox.Show(args.Exception.Message, "ScreenLove — errore", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            Settings = AppSettings.Load();
            Store = new CaptureStore(Settings.CapturesFolder);
            Store.Load();
            Capture = new CaptureController(Settings, Store);
            Capture.Captured += OnCaptured;
            Capture.ColorCopied += c => Notify("Colore copiato", $"{c} è negli appunti.");
            Capture.ScrollingFinished += msg => Notify("Cattura a scorrimento", msg);
            Capture.TextAreaCaptured += ShowTextRecognition;

            Recorder = new Recording.RecordingController(Settings, Store);
            Recorder.Message += Notify;
            Recorder.Saved += path =>
            {
                if (Settings.ShowWorkspaceAfterCapture)
                    ShowWorkspace(path);
                else
                    Notify("Video salvato", System.IO.Path.GetFileName(path));
            };

            SetupHotkeys();
            SetupTray();

            if (!e.Args.Contains("--tray"))
                ShowWorkspace();
            else
                Notify("ScreenLove è attivo", HotkeySummary);

            // "--check-updates": controllo immediato con esito sempre visibile.
            if (e.Args.Contains("--check-updates"))
            {
                Dispatcher.BeginInvoke(() => _ = CheckForUpdatesAsync(manual: true));
                return;
            }

            // Controllo automatico degli aggiornamenti, poco dopo l'avvio e al massimo una volta al giorno.
            var updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            updateTimer.Tick += (_, _) =>
            {
                updateTimer.Stop();
                _ = CheckForUpdatesAsync(manual: false);
            };
            updateTimer.Start();
        }

        // ---------- aggiornamenti ----------

        private UpdateInfo? _pendingUpdate;
        private Options.UpdateWindow? _updateWindow;
        private bool _checkingUpdates;

        /// <summary>
        /// Cerca una nuova versione. Manuale: mostra sempre l'esito. Automatico: solo se attivo nelle
        /// impostazioni, al massimo una volta al giorno, e avvisa con una notifica senza aprire finestre.
        /// </summary>
        public async System.Threading.Tasks.Task CheckForUpdatesAsync(bool manual)
        {
            if (_checkingUpdates) return;
            if (!manual)
            {
                if (!Settings.CheckForUpdates) return;
                if (Settings.LastUpdateCheck is DateTime last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
            }

            _checkingUpdates = true;
            try
            {
                var update = await Updater.CheckAsync();
                Settings.LastUpdateCheck = DateTime.UtcNow;
                Settings.Save();
                _pendingUpdate = update;
                if (update == null)
                {
                    if (manual)
                        MessageBox.Show($"Stai usando l'ultima versione di ScreenLove ({Updater.CurrentVersion}).", "ScreenLove");
                }
                else if (manual)
                    ShowUpdate();
                else
                {
                    Notify($"Aggiornamento disponibile: ScreenLove {update.Version}", "Clicca qui per vedere le novità e installarlo.");
                    _updateBalloon = true;
                }
            }
            catch (Exception ex)
            {
                // Il controllo automatico fallisce in silenzio (es. PC offline).
                if (manual)
                    MessageBox.Show("Impossibile controllare gli aggiornamenti.\n\n" + ex.Message, "ScreenLove",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _checkingUpdates = false;
            }
        }

        public void ShowUpdate()
        {
            if (_pendingUpdate == null) return;
            if (_updateWindow != null)
            {
                _updateWindow.Activate();
                return;
            }
            _updateWindow = new Options.UpdateWindow(_pendingUpdate);
            _updateWindow.Closed += (_, _) => _updateWindow = null;
            _updateWindow.Show();
            _updateWindow.Activate();
        }

        // ---------- tasti rapidi ----------

        /// <summary>Tasti che non è stato possibile registrare (già usati da un'altra app).</summary>
        public static IReadOnlyList<HotkeyAction> FailedHotkeys { get; private set; } = Array.Empty<HotkeyAction>();

        private Action ActionFor(HotkeyAction a) => a switch
        {
            HotkeyAction.Region => () => _ = Capture.CaptureRegionAsync(),
            HotkeyAction.FullScreen => () => _ = Capture.CaptureFullScreenAsync(),
            HotkeyAction.Scrolling => () => _ = Capture.CaptureScrollingAsync(),
            HotkeyAction.Text => () => _ = Capture.CaptureTextAsync(),
            HotkeyAction.Video => () => _ = Recorder.ToggleAsync(),
            HotkeyAction.Delayed => () => _ = Capture.CaptureRegionAsync(Settings.DelaySeconds),
            _ => () => { },
        };

        /// <summary>(Ri)registra i tasti rapidi letti dalle impostazioni.</summary>
        public void RegisterHotkeys()
        {
            _hotkeys ??= new HotkeyManager();
            _hotkeys.UnregisterAll();
            var lines = new List<string>();
            var failed = new List<HotkeyAction>();

            foreach (var action in Enum.GetValues<HotkeyAction>())
            {
                var binding = Settings.GetHotkey(action);
                if (binding.IsEmpty) continue;
                if (_hotkeys.Register(binding.NativeModifiers, binding.VirtualKey, ActionFor(action)))
                    lines.Add($"{binding.Display}: {HotkeyBinding.Describe(action).ToLowerInvariant()}");
                else
                    failed.Add(action);
            }

            FailedHotkeys = failed;
            HotkeySummary = lines.Count > 0 ? string.Join("\n", lines) : "Nessun tasto rapido attivo: usa l'icona nell'area di notifica.";
        }

        /// <summary>Sospende i tasti rapidi (serve mentre se ne sceglie uno nuovo).</summary>
        public void SuspendHotkeys() => _hotkeys?.UnregisterAll();

        private void SetupHotkeys()
        {
            RegisterHotkeys();
            if (FailedHotkeys.Count > 0)
                Dispatcher.BeginInvoke(() => Notify("Alcuni tasti rapidi sono occupati",
                    string.Join(", ", FailedHotkeys.Select(a => Settings.GetHotkey(a).Display)) +
                    " · cambiali in Impostazioni."));
        }

        private Options.SettingsWindow? _settingsWindow;

        public void OpenSettings()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new Options.SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        /// <summary>Applica le impostazioni salvate dalla finestra delle impostazioni.</summary>
        public void ApplySettings()
        {
            Settings.Save();
            Store.SetFolder(Settings.CapturesFolder);
            RegisterHotkeys();
            _workspace?.RefreshLabels();
        }

        // ---------- area di notifica ----------

        private void SetupTray()
        {
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("Cattura area o finestra", null, (_, _) => _ = Capture.CaptureRegionAsync());
            menu.Items.Add("Cattura schermo intero", null, (_, _) => _ = Capture.CaptureFullScreenAsync());
            menu.Items.Add("Cattura a scorrimento", null, (_, _) => _ = Capture.CaptureScrollingAsync());
            menu.Items.Add("Cattura testo (OCR / QR code)", null, (_, _) => _ = Capture.CaptureTextAsync());
            var recordItem = new WF.ToolStripMenuItem("Registra video");
            recordItem.Click += (_, _) => _ = Recorder.ToggleAsync();
            menu.Opening += (_, _) => recordItem.Text = Recorder.IsRecording ? "Ferma e salva il video" : "Registra video";
            menu.Items.Add(recordItem);
            menu.Items.Add("Cattura con ritardo", null, (_, _) => _ = Capture.CaptureRegionAsync(Settings.DelaySeconds));
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Apri workspace", null, (_, _) => ShowWorkspace());
            menu.Items.Add("Apri cartella catture", null, (_, _) => OpenCapturesFolder());
            menu.Items.Add(new WF.ToolStripSeparator());
            AddToggle(menu, "Copia negli appunti dopo la cattura", () => Settings.CopyToClipboard, v => Settings.CopyToClipboard = v);
            AddToggle(menu, "Apri l'editor dopo la cattura", () => Settings.OpenEditorAfterCapture, v => Settings.OpenEditorAfterCapture = v);
            AddToggle(menu, "Mostra il workspace dopo la cattura", () => Settings.ShowWorkspaceAfterCapture, v => Settings.ShowWorkspaceAfterCapture = v);
            AddToggle(menu, "Includi il cursore (schermo intero)", () => Settings.IncludeCursor, v => Settings.IncludeCursor = v);
            AddToggle(menu, "Video: registra l'audio di sistema", () => Settings.RecordSystemAudio, v => Settings.RecordSystemAudio = v);
            AddToggle(menu, "Video: registra il microfono", () => Settings.RecordMicrophone, v => Settings.RecordMicrophone = v);
            AddToggle(menu, "Video: mostra il cursore", () => Settings.RecordCursor, v => Settings.RecordCursor = v);
            AddToggle(menu, "Avvia con Windows", IsAutostartEnabled, SetAutostart);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Impostazioni…", null, (_, _) => OpenSettings());
            var updateItem = new WF.ToolStripMenuItem("Cerca aggiornamenti…");
            updateItem.Click += (_, _) =>
            {
                if (_pendingUpdate != null) ShowUpdate();
                else _ = CheckForUpdatesAsync(manual: true);
            };
            menu.Opening += (_, _) => updateItem.Text = _pendingUpdate != null
                ? $"Installa l'aggiornamento {_pendingUpdate.Version}…"
                : "Cerca aggiornamenti…";
            menu.Items.Add(updateItem);
            menu.Items.Add("Esci", null, (_, _) => ExitApp());

            _tray = new WF.NotifyIcon
            {
                Icon = IconFactory.AppIcon,
                Text = "ScreenLove",
                ContextMenuStrip = menu,
                Visible = true,
            };
            _tray.BalloonTipClicked += (_, _) =>
            {
                if (_updateBalloon) ShowUpdate();
            };
            _tray.MouseClick += (_, args) =>
            {
                if (args.Button == WF.MouseButtons.Left)
                    ShowWorkspace();
            };
        }

        private static void AddToggle(WF.ContextMenuStrip menu, string text, Func<bool> get, Action<bool> set)
        {
            var item = new WF.ToolStripMenuItem(text) { Checked = get(), CheckOnClick = true };
            item.CheckedChanged += (_, _) =>
            {
                set(item.Checked);
                Settings.Save();
            };
            menu.Opening += (_, _) => item.Checked = get();
            menu.Items.Add(item);
        }

        private bool _updateBalloon;

        public void Notify(string title, string text)
        {
            _updateBalloon = false;
            _tray?.ShowBalloonTip(2500, title, text, WF.ToolTipIcon.None);
        }

        // ---------- azioni ----------

        private void OnCaptured(string path)
        {
            if (Settings.OpenEditorAfterCapture)
                OpenEditor(path);
            else if (Settings.ShowWorkspaceAfterCapture)
                ShowWorkspace(path);
            else
                Notify("Cattura salvata", Settings.CopyToClipboard ? "Salvata e copiata negli appunti." : "Salvata nel workspace.");
        }

        public void ShowWorkspace(string? selectPath = null)
        {
            _workspace ??= new WorkspaceWindow();
            _workspace.ShowAndActivate(selectPath);
        }

        public void OpenEditor(string path)
        {
            var editor = new EditorWindow(path);
            editor.Show();
            editor.Activate();
        }

        public void ShowTextRecognition(System.Windows.Media.Imaging.BitmapSource image)
        {
            var window = new Ocr.OcrResultWindow(image);
            window.Show();
            window.Activate();
        }

        public void OpenCapturesFolder()
        {
            System.IO.Directory.CreateDirectory(Store.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Store.Folder}\"") { UseShellExecute = true });
        }

        public static bool IsAutostartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("ScreenLove") != null;
        }

        public static void SetAutostart(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue("ScreenLove", $"\"{Environment.ProcessPath}\" --tray");
            else
                key.DeleteValue("ScreenLove", throwOnMissingValue: false);
        }

        public async void ExitApp()
        {
            // Una registrazione in corso viene salvata prima di uscire.
            if (Recorder.IsRecording)
                await Recorder.StopAsync();
            Settings.Save();
            _hotkeys?.Dispose();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
