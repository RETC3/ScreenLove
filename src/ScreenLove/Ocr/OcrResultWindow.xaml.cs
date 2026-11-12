using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ScreenLove.Services;

namespace ScreenLove.Ocr
{
    /// <summary>Mostra il testo (e gli eventuali QR code) riconosciuti in un'immagine e lo copia negli appunti.</summary>
    public partial class OcrResultWindow : Window
    {
        private readonly BitmapSource _image;
        private bool _loadingLanguages;
        private int _run;

        public OcrResultWindow(BitmapSource image)
        {
            InitializeComponent();
            Services.WindowFit.Apply(this);
            Icon = IconFactory.AppImageSource;
            _image = image;
            Preview.Source = image;
            Loaded += async (_, _) => await StartAsync();
        }

        private async Task StartAsync()
        {
            var languages = TextRecognizer.AvailableLanguages;
            _loadingLanguages = true;
            LanguageBox.ItemsSource = languages;
            var def = TextRecognizer.DefaultLanguageTag;
            LanguageBox.SelectedItem = languages.FirstOrDefault(l => l.Tag == def) ?? languages.FirstOrDefault();
            _loadingLanguages = false;

            // I codici si leggono una volta sola, in parallelo all'OCR.
            var codesTask = Task.Run(() => CodeReader.Decode(_image));
            await RecognizeAsync(copy: true);

            var codes = await codesTask;
            if (codes.Count > 0)
            {
                CodesList.ItemsSource = codes;
                CodesList.Visibility = Visibility.Visible;
                if (string.IsNullOrWhiteSpace(ResultText.Text))
                {
                    // nessun testo ma c'è un QR: copiamo il contenuto del codice
                    TryCopy(codes[0].Text);
                    StatusText.Text = $"{codes[0].Format} letto e copiato negli appunti.";
                }
                else
                {
                    StatusText.Text += $"  ·  {codes.Count} codice/i trovati";
                }
            }
        }

        private async Task RecognizeAsync(bool copy)
        {
            int run = ++_run;
            var lang = (LanguageBox.SelectedItem as OcrLanguage)?.Tag;
            StatusText.Text = "Riconoscimento in corso…";
            ResultText.IsEnabled = false;
            try
            {
                var result = await TextRecognizer.RecognizeAsync(_image, lang);
                if (run != _run) return; // nel frattempo è stata scelta un'altra lingua
                ResultText.Text = result.Text;
                if (result.Lines == 0)
                {
                    StatusText.Text = "Nessun testo riconosciuto in questa zona.";
                }
                else
                {
                    bool copied = copy && TryCopy(result.Text);
                    StatusText.Text = $"{result.Lines} righe" + (copied ? "  ·  copiato negli appunti" : "");
                }
            }
            catch (Exception ex)
            {
                if (run != _run) return;
                ResultText.Text = "";
                StatusText.Text = ex.Message;
            }
            finally
            {
                if (run == _run)
                {
                    ResultText.IsEnabled = true;
                    ResultText.Focus();
                }
            }
        }

        private static bool TryCopy(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                return false; // appunti occupati da un'altra app
            }
        }

        private async void Language_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingLanguages || !IsLoaded) return;
            await RecognizeAsync(copy: false);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            // Se c'è una selezione si copia solo quella.
            var text = ResultText.SelectionLength > 0 ? ResultText.SelectedText : ResultText.Text;
            StatusText.Text = TryCopy(text) ? "Copiato negli appunti." : "Niente da copiare.";
        }

        private void OneLine_Click(object sender, RoutedEventArgs e)
        {
            // Unisce le righe di ogni paragrafo, mantenendo le righe vuote tra i paragrafi.
            var paragraphs = Regex.Split(ResultText.Text.Replace("\r\n", "\n"), @"\n\s*\n")
                .Select(p => Regex.Replace(p.Trim(), @"\s*\n\s*", " "));
            ResultText.Text = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        }

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string text })
                StatusText.Text = TryCopy(text) ? "Contenuto del codice copiato." : "Impossibile copiare.";
        }

        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string url } &&
                Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
