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
        private const uint VK_SNAPSHOT = 0x2C;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private Mutex? _mutex;
        private WF.NotifyIcon? _tray;
        private HotkeyManager? _hotkeys;
        private WorkspaceWindow? _workspace;

        public static AppSettings Settings { get; private set; } = null!;
        public static CaptureStore Store { get; private set; } = null!;
        public static CaptureController Capture { get; private set; } = null!;

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

            SetupHotkeys();
            SetupTray();

            if (!e.Args.Contains("--tray"))
                ShowWorkspace();
            else
                Notify("ScreenLove è attivo", HotkeySummary);
        }

        // ---------- tasti rapidi ----------

        private void SetupHotkeys()
        {
            _hotkeys = new HotkeyManager();
            var lines = new List<string>();

            if (_hotkeys.Register(0, VK_SNAPSHOT, () => _ = Capture.CaptureRegionAsync()))
                lines.Add("Stamp: cattura area/finestra");
            else if (_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 'A', () => _ = Capture.CaptureRegionAsync()))
                lines.Add("Ctrl+Alt+A: cattura area/finestra (Stamp è occupato)");

            if (_hotkeys.Register(NativeMethods.MOD_CONTROL, VK_SNAPSHOT, () => _ = Capture.CaptureFullScreenAsync()))
                lines.Add("Ctrl+Stamp: schermo intero");
            else if (_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 'F', () => _ = Capture.CaptureFullScreenAsync()))
                lines.Add("Ctrl+Alt+F: schermo intero");

            if (_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, VK_SNAPSHOT, () => _ = Capture.CaptureScrollingAsync()))
                lines.Add("Ctrl+Maiusc+Stamp: a scorrimento");
            else if (_hotkeys.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 'S', () => _ = Capture.CaptureScrollingAsync()))
                lines.Add("Ctrl+Alt+S: a scorrimento");

            if (_hotkeys.Register(NativeMethods.MOD_SHIFT, VK_SNAPSHOT, () => _ = Capture.CaptureRegionAsync(Settings.DelaySeconds)))
                lines.Add($"Maiusc+Stamp: cattura con ritardo di {Settings.DelaySeconds} s");

            HotkeySummary = lines.Count > 0 ? string.Join("\n", lines) : "Nessun tasto rapido disponibile: usa l'icona nell'area di notifica.";
        }

        // ---------- area di notifica ----------

        private void SetupTray()
        {
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("Cattura area o finestra", null, (_, _) => _ = Capture.CaptureRegionAsync());
            menu.Items.Add("Cattura schermo intero", null, (_, _) => _ = Capture.CaptureFullScreenAsync());
            menu.Items.Add("Cattura a scorrimento", null, (_, _) => _ = Capture.CaptureScrollingAsync());
            menu.Items.Add($"Cattura con ritardo ({Settings.DelaySeconds} s)", null, (_, _) => _ = Capture.CaptureRegionAsync(Settings.DelaySeconds));
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Apri workspace", null, (_, _) => ShowWorkspace());
            menu.Items.Add("Apri cartella catture", null, (_, _) => OpenCapturesFolder());
            menu.Items.Add(new WF.ToolStripSeparator());
            AddToggle(menu, "Copia negli appunti dopo la cattura", () => Settings.CopyToClipboard, v => Settings.CopyToClipboard = v);
            AddToggle(menu, "Apri l'editor dopo la cattura", () => Settings.OpenEditorAfterCapture, v => Settings.OpenEditorAfterCapture = v);
            AddToggle(menu, "Mostra il workspace dopo la cattura", () => Settings.ShowWorkspaceAfterCapture, v => Settings.ShowWorkspaceAfterCapture = v);
            AddToggle(menu, "Includi il cursore (schermo intero)", () => Settings.IncludeCursor, v => Settings.IncludeCursor = v);
            AddToggle(menu, "Avvia con Windows", IsAutostartEnabled, SetAutostart);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Esci", null, (_, _) => ExitApp());

            _tray = new WF.NotifyIcon
            {
                Icon = IconFactory.AppIcon,
                Text = "ScreenLove",
                ContextMenuStrip = menu,
                Visible = true,
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

        public void Notify(string title, string text)
        {
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

        public void OpenCapturesFolder()
        {
            System.IO.Directory.CreateDirectory(Store.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Store.Folder}\"") { UseShellExecute = true });
        }

        private static bool IsAutostartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("ScreenLove") != null;
        }

        private static void SetAutostart(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue("ScreenLove", $"\"{Environment.ProcessPath}\" --tray");
            else
                key.DeleteValue("ScreenLove", throwOnMissingValue: false);
        }

        public void ExitApp()
        {
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
