using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using ScreenLove.Services;

namespace ScreenLove.Options
{
    /// <summary>Mostra una nuova versione, la scarica e avvia l'installer.</summary>
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _update;
        private CancellationTokenSource? _download;

        public UpdateWindow(UpdateInfo update)
        {
            InitializeComponent();
            WindowFit.Apply(this);
            _update = update;

            TitleText.Text = $"È disponibile ScreenLove {update.Version}";
            SubtitleText.Text = $"Versione in uso: {Updater.CurrentVersion}.";
            NotesBox.Text = string.IsNullOrWhiteSpace(update.Notes) ? "(nessuna nota per questa versione)" : update.Notes;

            if (!Updater.IsInstalled || update.SetupUrl == null)
            {
                // Copia portatile (o release senza installer): si scarica a mano dalla pagina.
                UpdateButton.Content = "Scarica";
                StatusText.Text = Updater.IsInstalled
                    ? "Questa release non contiene l'installer: scaricala dalla pagina della release."
                    : "Stai usando la versione portatile: scarica la nuova versione dalla pagina della release.";
            }
            else
            {
                StatusText.Text = $"L'aggiornamento scarica l'installer ({update.SetupSize / 1048576.0:N0} MB), chiude ScreenLove e lo riapre aggiornato.";
            }

            Closing += (_, e) =>
            {
                if (_download != null) _download.Cancel();
            };
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (!Updater.IsInstalled || _update.SetupUrl == null)
            {
                OpenPage();
                Close();
                return;
            }
            if (App.Recorder.IsRecording)
            {
                StatusText.Text = "Ferma la registrazione video prima di aggiornare.";
                return;
            }

            UpdateButton.IsEnabled = false;
            LaterButton.Content = "Annulla";
            Progress.Visibility = Visibility.Visible;
            StatusText.Text = "Download in corso…";
            _download = new CancellationTokenSource();
            try
            {
                var progress = new Progress<double>(p =>
                {
                    Progress.Value = p;
                    StatusText.Text = $"Download in corso… {p:P0}";
                });
                var setup = await Updater.DownloadSetupAsync(_update, progress, _download.Token);
                StatusText.Text = "Avvio dell'installer…";
                Updater.LaunchSetup(setup);
                _download = null;
                Close();
                App.Current.ExitApp();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Download annullato.";
                ResetButtons();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Aggiornamento non riuscito: " + ex.Message;
                ResetButtons();
            }
        }

        private void ResetButtons()
        {
            _download = null;
            UpdateButton.IsEnabled = true;
            LaterButton.Content = "Più tardi";
            Progress.Visibility = Visibility.Collapsed;
        }

        private void Later_Click(object sender, RoutedEventArgs e)
        {
            if (_download != null)
            {
                _download.Cancel();
                return;
            }
            Close();
        }

        private void Page_Click(object sender, RoutedEventArgs e) => OpenPage();

        private void OpenPage()
        {
            try { Process.Start(new ProcessStartInfo(_update.PageUrl) { UseShellExecute = true }); }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }
    }
}
