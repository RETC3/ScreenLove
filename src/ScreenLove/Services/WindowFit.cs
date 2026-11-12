using System;
using System.Windows;

namespace ScreenLove.Services
{
    /// <summary>Riduce una finestra se è più grande dell'area di lavoro dello schermo (es. schermi bassi o Desktop remoto).</summary>
    public static class WindowFit
    {
        public static void Apply(Window window)
        {
            var area = SystemParameters.WorkArea;
            double maxW = area.Width * 0.96, maxH = area.Height * 0.94;
            if (window.Width > maxW) window.Width = maxW;
            if (window.Height > maxH) window.Height = maxH;
            window.MinWidth = Math.Min(window.MinWidth, maxW);
            window.MinHeight = Math.Min(window.MinHeight, maxH);
        }
    }
}
