using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ScreenLove.Services;

namespace ScreenLove.Documents
{
    /// <summary>Riga dell'elenco: un passaggio del documento con la sua anteprima.</summary>
    public sealed class StepItem : INotifyPropertyChanged
    {
        private string _label = "";

        public StepItem(string path)
        {
            Step = new DocumentStep { ImagePath = path };
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 460;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            Thumbnail = bi;
        }

        public DocumentStep Step { get; }
        public ImageSource Thumbnail { get; }

        public string Label
        {
            get => _label;
            set { _label = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class DocumentWindow : Window
    {
        private static readonly (string Name, string Ext, string Filter)[] Formats =
        {
            ("PDF", ".pdf", "Documento PDF (*.pdf)|*.pdf"),
            ("Word (.docx)", ".docx", "Documento Word (*.docx)|*.docx"),
            ("Pagina HTML", ".html", "Pagina web (*.html)|*.html"),
        };

        private readonly ObservableCollection<StepItem> _steps = new();

        public DocumentWindow(IEnumerable<string> imagePaths)
        {
            InitializeComponent();
            Services.WindowFit.Apply(this);
            Icon = IconFactory.AppImageSource;
            foreach (var p in imagePaths.OrderBy(File.GetLastWriteTime))
                _steps.Add(new StepItem(p));
            StepsList.ItemsSource = _steps;
            _steps.CollectionChanged += (_, _) => Relabel();
            Relabel();

            TitleBox.Text = "Guida — " + DateTime.Now.ToString("dd/MM/yyyy");
            FormatBox.ItemsSource = Formats.Select(f => f.Name).ToList();
            FormatBox.SelectedIndex = 0;
            StatusText.Text = $"{_steps.Count} immagini · scrivi un titolo e un commento per ogni passaggio";
        }

        private void Relabel()
        {
            for (int i = 0; i < _steps.Count; i++)
                _steps[i].Label = $"Passaggio {i + 1} di {_steps.Count}";
        }

        private void Up_Click(object sender, RoutedEventArgs e) => Move(sender, -1);
        private void Down_Click(object sender, RoutedEventArgs e) => Move(sender, +1);

        private void Move(object sender, int delta)
        {
            if ((sender as FrameworkElement)?.Tag is not StepItem item) return;
            int i = _steps.IndexOf(item), j = i + delta;
            if (j >= 0 && j < _steps.Count) _steps.Move(i, j);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is StepItem item && _steps.Count > 1)
                _steps.Remove(item);
        }

        private async void Create_Click(object sender, RoutedEventArgs e)
        {
            var format = Formats[Math.Max(0, FormatBox.SelectedIndex)];
            string safeTitle = string.Concat(TitleBox.Text.Split(Path.GetInvalidFileNameChars())).Trim();
            var dlg = new SaveFileDialog
            {
                Filter = format.Filter,
                FileName = (safeTitle.Length > 0 ? safeTitle : "Documento") + format.Ext,
                InitialDirectory = App.Store.Folder,
            };
            if (dlg.ShowDialog(this) != true) return;

            var model = new DocumentModel { Title = TitleBox.Text.Trim(), NumberSteps = NumberBox.IsChecked == true };
            model.Steps.AddRange(_steps.Select(s => s.Step));

            CreateButton.IsEnabled = false;
            StatusText.Text = "Creazione del documento…";
            try
            {
                string path = dlg.FileName;
                // L'immagine va preparata sul thread dell'interfaccia (WPF), il resto è veloce.
                await Task.Yield();
                DocumentBuilder.Build(model, path);
                StatusText.Text = "Creato: " + path;
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText.Text = "Creazione non riuscita: " + ex.Message;
            }
            finally
            {
                CreateButton.IsEnabled = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
