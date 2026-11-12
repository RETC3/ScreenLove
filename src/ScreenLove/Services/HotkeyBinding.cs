using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using ScreenLove.Native;

namespace ScreenLove.Services
{
    /// <summary>Azioni che si possono collegare a un tasto rapido globale.</summary>
    public enum HotkeyAction
    {
        Region,
        FullScreen,
        Scrolling,
        Text,
        Video,
        Delayed,
    }

    /// <summary>
    /// Combinazione di tasti salvata come testo, es. "Ctrl+Alt+R" o "Snapshot" (= Stamp).
    /// I nomi dei tasti sono quelli dell'enumerazione WPF <see cref="Key"/>.
    /// </summary>
    public readonly record struct HotkeyBinding(ModifierKeys Modifiers, Key Key)
    {
        public static readonly IReadOnlyDictionary<HotkeyAction, string> Defaults = new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.Region] = "Snapshot",
            [HotkeyAction.FullScreen] = "Ctrl+Snapshot",
            [HotkeyAction.Scrolling] = "Ctrl+Shift+Snapshot",
            [HotkeyAction.Text] = "Ctrl+Alt+Snapshot",
            [HotkeyAction.Video] = "Ctrl+Alt+R",
            [HotkeyAction.Delayed] = "Shift+Snapshot",
        };

        public static string Describe(HotkeyAction action) => action switch
        {
            HotkeyAction.Region => "Cattura area o finestra",
            HotkeyAction.FullScreen => "Cattura schermo intero",
            HotkeyAction.Scrolling => "Cattura a scorrimento",
            HotkeyAction.Text => "Testo (OCR) e QR code",
            HotkeyAction.Video => "Avvia / ferma video",
            HotkeyAction.Delayed => "Cattura con ritardo",
            _ => action.ToString(),
        };

        public bool IsEmpty => Key == Key.None;

        public uint NativeModifiers =>
            ((Modifiers & ModifierKeys.Control) != 0 ? NativeMethods.MOD_CONTROL : 0) |
            ((Modifiers & ModifierKeys.Alt) != 0 ? NativeMethods.MOD_ALT : 0) |
            ((Modifiers & ModifierKeys.Shift) != 0 ? NativeMethods.MOD_SHIFT : 0) |
            ((Modifiers & ModifierKeys.Windows) != 0 ? 0x8u : 0);

        public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

        public static HotkeyBinding Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return default;
            var mods = ModifierKeys.None;
            var key = Key.None;
            foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": mods |= ModifierKeys.Control; break;
                    case "alt": mods |= ModifierKeys.Alt; break;
                    case "shift": mods |= ModifierKeys.Shift; break;
                    case "win": mods |= ModifierKeys.Windows; break;
                    default:
                        if (Enum.TryParse<Key>(part, true, out var k)) key = k;
                        break;
                }
            }
            return new HotkeyBinding(mods, key);
        }

        /// <summary>Forma salvata nelle impostazioni.</summary>
        public override string ToString()
        {
            if (IsEmpty) return "";
            var parts = new List<string>();
            if ((Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(Key == Key.PrintScreen ? "Snapshot" : Key.ToString());
            return string.Join("+", parts);
        }

        /// <summary>Forma leggibile in italiano, es. "Ctrl+Maiusc+Stamp".</summary>
        public string Display
        {
            get
            {
                if (IsEmpty) return "(nessuno)";
                return string.Join("+", ToString().Split('+').Select(p => p switch
                {
                    "Shift" => "Maiusc",
                    "Snapshot" => "Stamp",
                    "Return" => "Invio",
                    "Space" => "Spazio",
                    _ when p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) => p[1].ToString(),
                    _ => p,
                }));
            }
        }
    }
}
