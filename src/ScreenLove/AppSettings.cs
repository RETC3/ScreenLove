using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ScreenLove
{
    /// <summary>Impostazioni utente, salvate in %APPDATA%\ScreenLove\settings.json.</summary>
    public sealed class AppSettings
    {
        public string CapturesFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenLove");

        public bool CopyToClipboard { get; set; } = true;
        public bool OpenEditorAfterCapture { get; set; } = false;
        public bool ShowWorkspaceAfterCapture { get; set; } = true;
        public bool IncludeCursor { get; set; } = false;
        public int DelaySeconds { get; set; } = 3;

        // Registrazione video
        public int RecordFps { get; set; } = 30;
        public bool RecordCursor { get; set; } = true;
        public bool RecordSystemAudio { get; set; } = true;
        public bool RecordMicrophone { get; set; } = false;
        public bool RecordWebcam { get; set; } = false;

        // Editor
        public string WatermarkText { get; set; } = "ScreenLove";

        /// <summary>Tasti rapidi: nome dell'azione → combinazione (vedi Services.HotkeyBinding).</summary>
        public Dictionary<string, string> Hotkeys { get; set; } = new();

        public Services.HotkeyBinding GetHotkey(Services.HotkeyAction action) =>
            Services.HotkeyBinding.Parse(Hotkeys.TryGetValue(action.ToString(), out var s) ? s : Services.HotkeyBinding.Defaults[action]);

        public void SetHotkey(Services.HotkeyAction action, Services.HotkeyBinding binding) =>
            Hotkeys[action.ToString()] = binding.ToString();

        public AppSettings Clone() =>
            JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();

        public void CopyFrom(AppSettings other)
        {
            foreach (var p in typeof(AppSettings).GetProperties())
                if (p.CanWrite) p.SetValue(this, p.GetValue(other));
            Hotkeys = new Dictionary<string, string>(other.Hotkeys);
        }

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenLove", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
            catch
            {
                // file corrotto: si riparte dai valori predefiniti
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // non bloccare l'app per un errore di salvataggio impostazioni
            }
        }
    }
}
