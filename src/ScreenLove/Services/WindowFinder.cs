using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using ScreenLove.Native;

namespace ScreenLove.Services
{
    /// <summary>Trova finestre e monitor per l'evidenziazione automatica durante la cattura.</summary>
    public static class WindowFinder
    {
        /// <summary>
        /// Rettangoli delle finestre visibili in ordine Z (dall'alto verso il basso),
        /// espressi relativamente all'origine del desktop virtuale.
        /// </summary>
        public static List<Int32Rect> GetWindowRects(Int32Rect virtualBounds)
        {
            var result = new List<Int32Rect>();
            var title = new StringBuilder(256);
            var cls = new StringBuilder(256);

            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                    return true;

                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                cls.Clear();
                NativeMethods.GetClassName(hwnd, cls, cls.Capacity);
                string className = cls.ToString();
                if (className is "Progman" or "WorkerW")
                    return true;

                int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                if ((ex & NativeMethods.WS_EX_TRANSPARENT) != 0)
                    return true; // overlay trasparenti (es. overlay dei driver video)

                title.Clear();
                NativeMethods.GetWindowText(hwnd, title, title.Capacity);
                bool isTaskbar = className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
                if (!isTaskbar && (ex & NativeMethods.WS_EX_TOOLWINDOW) != 0 && title.Length == 0)
                    return true;

                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT r,
                        System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0)
                {
                    if (!NativeMethods.GetWindowRect(hwnd, out r))
                        return true;
                }

                var rect = Clip(new Int32Rect(r.Left - virtualBounds.X, r.Top - virtualBounds.Y, r.Width, r.Height),
                                virtualBounds.Width, virtualBounds.Height);
                if (rect.Width > 8 && rect.Height > 8)
                    result.Add(rect);
                return true;
            }, IntPtr.Zero);

            return result;
        }

        public static List<Int32Rect> GetMonitorRects(Int32Rect virtualBounds)
        {
            var result = new List<Int32Rect>();
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                result.Add(new Int32Rect(b.X - virtualBounds.X, b.Y - virtualBounds.Y, b.Width, b.Height));
            }
            return result;
        }

        public static Int32Rect Clip(Int32Rect r, int maxW, int maxH)
        {
            int x1 = Math.Max(0, r.X);
            int y1 = Math.Max(0, r.Y);
            int x2 = Math.Min(maxW, r.X + r.Width);
            int y2 = Math.Min(maxH, r.Y + r.Height);
            return x2 > x1 && y2 > y1 ? new Int32Rect(x1, y1, x2 - x1, y2 - y1) : Int32Rect.Empty;
        }

        public static bool Contains(Int32Rect r, int x, int y) =>
            x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height;
    }
}
