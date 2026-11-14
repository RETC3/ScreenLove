using System;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ScreenLove.Services
{
    /// <summary>
    /// Restituisce a Windows la memoria usata per catture, overlay e finestre chiuse.
    /// Ogni istantanea dello schermo occupa decine di MB (larghezza × altezza × 4 byte): .NET di solito li
    /// tiene da parte per riusarli, e in Gestione attività ScreenLove sembra occupare centinaia di MB anche
    /// quando è fermo nell'area di notifica. Dopo un lavoro pesante si pianifica un riordino completo.
    /// </summary>
    public static class MemoryRelief
    {
        private static DispatcherTimer? _timer;

        /// <summary>Riordino tra qualche secondo; le richieste ravvicinate diventano una sola.</summary>
        public static void Schedule(double seconds = 3)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() => Schedule(seconds));
                return;
            }

            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
                _timer.Tick += (_, _) =>
                {
                    _timer!.Stop();
                    Now();
                };
            }
            _timer.Stop();
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            _timer.Start();
        }

        /// <summary>Riordino immediato: raccolta completa con compattazione, anche degli oggetti grandi.</summary>
        public static void Now()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            // Aggressive: oltre a raccogliere, restituisce a Windows la memoria libera tenuta da parte.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            // Senza finestre aperte l'app è solo nell'area di notifica: può liberare anche le pagine in uso.
            var app = Application.Current;
            if (app != null && !app.Windows.Cast<Window>().Any(w => w.IsVisible))
                SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
    }
}
