using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenLove.Services
{
    /// <summary>
    /// Una sola copia di ScreenLove per utente. Se si riavvia l'exe (per esempio dall'icona sul desktop) con
    /// l'app già attiva nell'area di notifica, la nuova copia avvisa quella in esecuzione, che apre il workspace,
    /// e poi si chiude.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private const string MutexName = "ScreenLove.SingleInstance";
        private const string ShowEventName = "ScreenLove.ShowWorkspace";

        private readonly Mutex _mutex;
        private EventWaitHandle? _showEvent;
        private RegisteredWaitHandle? _wait;

        private SingleInstance(Mutex mutex) => _mutex = mutex;

        /// <summary>La copia principale, oppure null se un'altra copia è già in esecuzione.</summary>
        public static SingleInstance? TryAcquire()
        {
            var mutex = new Mutex(true, MutexName, out bool created);
            if (created)
                return new SingleInstance(mutex);
            mutex.Dispose();
            return null;
        }

        /// <summary>Copia principale: <paramref name="onShowRequested"/> viene chiamato (su un thread del pool) a ogni richiesta.</summary>
        public void ListenForShowRequests(Action onShowRequested)
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        /// <summary>Seconda copia: chiede a quella in esecuzione di mostrarsi. False se non risponde.</summary>
        public static bool RequestShow()
        {
            // La copia in esecuzione può portare la sua finestra in primo piano: l'utente ha appena aperto l'exe.
            AllowSetForegroundWindow(ASFW_ANY);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                // L'evento può mancare per un attimo se l'altra copia si sta ancora avviando.
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
                {
                    using (ev)
                        ev.Set();
                    return true;
                }
                Thread.Sleep(250);
            }
            return false;
        }

        public void Dispose()
        {
            _wait?.Unregister(null);
            _showEvent?.Dispose();
            _mutex.Dispose();
        }

        private const int ASFW_ANY = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
