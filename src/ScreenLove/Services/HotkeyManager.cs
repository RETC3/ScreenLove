using System;
using System.Collections.Generic;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenLove.Native;

namespace ScreenLove.Services
{
    /// <summary>Tasti di scelta rapida globali tramite RegisterHotKey su una finestra nascosta.</summary>
    public sealed class HotkeyManager : IDisposable
    {
        private readonly HwndSource _source;
        private readonly Dictionary<int, Action> _actions = new();
        private int _nextId = 1;

        public HotkeyManager()
        {
            var parameters = new HwndSourceParameters("ScreenLoveHotkeys") { Width = 0, Height = 0, WindowStyle = 0 };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        public bool Register(uint modifiers, uint virtualKey, Action action)
        {
            int id = _nextId++;
            if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
                return false;
            _actions[id] = action;
            return true;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
            {
                handled = true;
                // Esegue fuori dal WndProc per evitare rientranze (l'overlay apre un dialog modale).
                Dispatcher.CurrentDispatcher.BeginInvoke(action, DispatcherPriority.Normal);
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            foreach (var id in _actions.Keys)
                NativeMethods.UnregisterHotKey(_source.Handle, id);
            _actions.Clear();
            _source.Dispose();
        }
    }
}
