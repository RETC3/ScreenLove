using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenLove.Services;

namespace ScreenLove.Workspace
{
    public partial class WorkspaceWindow : Window
    {
        private Point _dragStart;
        private bool _dragArmed;

        public WorkspaceWindow()
        {
            InitializeComponent();
            Services.WindowFit.Apply(this);
            Icon = IconFactory.AppImageSource;
            List.ItemsSource = App.Store.Items;
            App.Store.Items.CollectionChanged += (_, _) => UpdateEmptyState();
            RefreshLabels();
            UpdateEmptyState();
        }

        /// <summary>Aggiorna etichette e barra di stato dopo un cambio di impostazioni.</summary>
        public void RefreshLabels()
        {
            DelayButton.Content = $"Ritardo {App.Settings.DelaySeconds} s";
            ApplyPreviewSetting();
            UpdateStatus();
        }

        // ---------- pannello di anteprima ----------

        private const double DefaultPreviewWidth = 360;
        private int _previewVersion;
        private CaptureItem? _previewItem;

        /// <summary>Mostra o nasconde il pannello di anteprima secondo le Impostazioni.</summary>
        private void ApplyPreviewSetting()
        {
            bool on = App.Settings.WorkspacePreview;
            bool wasOn = PreviewPanel.Visibility == Visibility.Visible;
            PreviewPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            PreviewSplitter.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on && !wasOn)
            {
                PreviewColumn.MinWidth = 220;
                PreviewColumn.Width = new GridLength(DefaultPreviewWidth);
                if (Width < 900) Width = 900; // spazio per elenco e anteprima
            }
            else if (!on)
            {
                PreviewColumn.MinWidth = 0;
                PreviewColumn.Width = new GridLength(0);
            }
            UpdatePreview();
        }

        private async void UpdatePreview()
        {
            int version = ++_previewVersion;
            if (PreviewPanel.Visibility != Visibility.Visible)
            {
                // Pannello spento: niente immagine in memoria.
                PreviewImage.Source = null;
                _previewItem = null;
                return;
            }

            var items = Selected;
            var item = items.Length == 1 ? items[0] : null;
            _previewItem = item;
            PreviewVideoMark.Visibility = Visibility.Collapsed;
            PreviewHint.Visibility = item != null ? Visibility.Visible : Visibility.Collapsed;

            if (item == null)
            {
                PreviewImage.Source = null;
                PreviewName.Text = "";
                PreviewInfo.Text = "";
                PreviewEmpty.Text = items.Length > 1 ? $"{items.Length} catture selezionate" : "Seleziona una cattura per vederla qui";
                PreviewEmpty.Visibility = Visibility.Visible;
                return;
            }

            PreviewName.Text = item.Name;
            PreviewInfo.Text = item.Info;
            PreviewEmpty.Visibility = Visibility.Collapsed;

            if (item.IsVideo)
            {
                PreviewImage.Source = item.Thumbnail;
                PreviewVideoMark.Visibility = Visibility.Visible;
                return;
            }

            // Si mostra subito la miniatura, poi l'immagine più definita.
            PreviewImage.Source = item.Thumbnail;
            var image = await LoadPreviewAsync(item.Path);
            if (version == _previewVersion && image != null)
                PreviewImage.Source = image;
        }

        /// <summary>
        /// Carica l'immagine per l'anteprima, ridotta al massimo a 1600 × 3200 pixel: le catture a scorrimento
        /// possono essere alte decine di migliaia di pixel e occuperebbero centinaia di MB.
        /// </summary>
        private static Task<ImageSource?> LoadPreviewAsync(string path) => Task.Run<ImageSource?>(() =>
        {
            try
            {
                int width, height;
                using (var fs = File.OpenRead(path))
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(fs,
                        System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation | System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
                        System.Windows.Media.Imaging.BitmapCacheOption.None);
                    width = decoder.Frames[0].PixelWidth;
                    height = decoder.Frames[0].PixelHeight;
                }
                double scale = Math.Min(1.0, Math.Min(1600.0 / width, 3200.0 / height));
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // non tiene bloccato il file
                bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                bi.DecodePixelWidth = Math.Max(1, (int)Math.Round(width * scale));
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        });

        private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && _previewItem != null)
                Open(_previewItem);
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => App.Current.OpenSettings();

        private void More_Click(object sender, RoutedEventArgs e)
        {
            MoreButton.ContextMenu.PlacementTarget = MoreButton;
            MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            MoreButton.ContextMenu.IsOpen = true;
        }

        private void CreateDocument_Click(object sender, RoutedEventArgs e)
        {
            var images = Selected.Where(i => !i.IsVideo).Select(i => i.Path).ToList();
            if (images.Count == 0)
            {
                StatusText.Text = "Seleziona una o più immagini per creare un documento.";
                return;
            }
            new Documents.DocumentWindow(images) { Owner = this }.Show();
        }

        private void TrimVideo_Click(object sender, RoutedEventArgs e)
        {
            if (Selected.FirstOrDefault(i => i.IsVideo) is not { } item)
            {
                StatusText.Text = "Seleziona un video da tagliare.";
                return;
            }
            new Video.VideoTrimWindow(item.Path) { Owner = this }.Show();
        }

        private async void MergeVideos_Click(object sender, RoutedEventArgs e)
        {
            // Ordine di unione: dal più vecchio al più recente.
            var videos = Selected.Where(i => i.IsVideo).OrderBy(i => i.Created).Select(i => i.Path).ToList();
            if (videos.Count < 2)
            {
                StatusText.Text = "Seleziona almeno due video da unire (Ctrl+clic).";
                return;
            }
            StatusText.Text = $"Unione di {videos.Count} video in corso…";
            IsEnabled = false;
            try
            {
                string output = App.Store.NewPath(".mp4");
                await Video.VideoEditing.MergeAsync(videos, output,
                    p => Dispatcher.BeginInvoke(() => StatusText.Text = $"Unione di {videos.Count} video… {p:P0}"));
                App.Store.Refresh(output);
                ShowAndActivate(output);
                StatusText.Text = "Video uniti: " + Path.GetFileName(output);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Unione non riuscita: " + ex.Message;
            }
            finally
            {
                IsEnabled = true;
            }
        }

        public void ShowAndActivate(string? selectPath)
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();

            if (selectPath != null && App.Store.Find(selectPath) is { } item)
            {
                List.SelectedItems.Clear();
                List.SelectedItem = item;
                List.ScrollIntoView(item);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Chiudere il workspace lo nasconde: l'app resta nell'area di notifica.
            e.Cancel = true;
            Hide();
        }

        private void UpdateEmptyState() =>
            EmptyHint.Visibility = App.Store.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void UpdateStatus()
        {
            int n = App.Store.Items.Count, sel = List.SelectedItems.Count;
            string hotkeys = App.HotkeySummary.Replace("\n", " · ");
            StatusText.Text = sel > 0 ? $"{sel} selezionate di {n}  ·  {hotkeys}" : $"{n} catture  ·  {hotkeys}";
        }

        private CaptureItem[] Selected => List.SelectedItems.Cast<CaptureItem>().ToArray();

        // ---------- cattura ----------

        private async Task RunCaptureAsync(Func<Task> capture)
        {
            // Nasconde il workspace così non finisce nella cattura.
            Hide();
            await Task.Delay(250);
            try
            {
                await capture();
            }
            finally
            {
                if (!IsVisible)
                    ShowAndActivate(null);
            }
        }

        private async void CaptureRegion_Click(object sender, RoutedEventArgs e) =>
            await RunCaptureAsync(() => App.Capture.CaptureRegionAsync());

        private async void CaptureFull_Click(object sender, RoutedEventArgs e) =>
            await RunCaptureAsync(() => App.Capture.CaptureFullScreenAsync());

        private async void CaptureScrolling_Click(object sender, RoutedEventArgs e) =>
            await RunCaptureAsync(() => App.Capture.CaptureScrollingAsync());

        private async void CaptureText_Click(object sender, RoutedEventArgs e) =>
            await RunCaptureAsync(() => App.Capture.CaptureTextAsync());

        private async void RecordVideo_Click(object sender, RoutedEventArgs e)
        {
            if (App.Recorder.IsRecording)
            {
                await App.Recorder.StopAsync();
                return;
            }
            // Il workspace resta nascosto per tutta la registrazione (riappare quando il video è salvato).
            Hide();
            await Task.Delay(250);
            await App.Recorder.StartAsync();
            if (!App.Recorder.IsRecording)
                ShowAndActivate(null);
        }

        /// <summary>Apre un video nel lettore predefinito di Windows.</summary>
        private static void OpenVideo(CaptureItem item)
        {
            if (File.Exists(item.Path))
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }

        private static void Open(CaptureItem item)
        {
            if (item.IsVideo) OpenVideo(item);
            else App.Current.OpenEditor(item.Path);
        }

        private void ExtractText_Click(object sender, RoutedEventArgs e)
        {
            if (Selected.FirstOrDefault(i => !i.IsVideo) is not { } item) return;
            try
            {
                App.Current.ShowTextRecognition(ImageIO.LoadNormalized(item.Path));
            }
            catch (Exception ex)
            {
                StatusText.Text = "Impossibile aprire l'immagine: " + ex.Message;
            }
        }

        private async void CaptureDelayed_Click(object sender, RoutedEventArgs e) =>
            await RunCaptureAsync(() => App.Capture.CaptureRegionAsync(App.Settings.DelaySeconds));

        // ---------- azioni sulle catture ----------

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Selected.Take(5))
            {
                if (item.IsVideo) new Video.VideoTrimWindow(item.Path) { Owner = this }.Show();
                else App.Current.OpenEditor(item.Path);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e) => CopyItems(Selected);

        /// <summary>Tasto "Copia" sulla scheda: copia solo quella cattura.</summary>
        private void ItemCopy_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CaptureItem item)
                CopyItems(new[] { item });
            e.Handled = true;
        }

        /// <summary>Tasto "Salva con nome" sulla scheda: salva una copia dove si vuole.</summary>
        private void ItemSaveAs_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if ((sender as FrameworkElement)?.DataContext is not CaptureItem item || !File.Exists(item.Path)) return;

            string ext = Path.GetExtension(item.Path).ToLowerInvariant();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(item.Path),
                Filter = item.IsVideo
                    ? "Video MP4 (*.mp4)|*.mp4"
                    : "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
                FilterIndex = ext switch { ".jpg" or ".jpeg" => 2, ".bmp" => 3, _ => 1 },
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                string target = dlg.FileName;
                if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(item.Path), StringComparison.OrdinalIgnoreCase))
                {
                    StatusText.Text = "È già questo il file della cattura.";
                    return;
                }
                // Stesso formato: copia identica. Formato diverso: si converte l'immagine.
                if (item.IsVideo || string.Equals(Path.GetExtension(target), ext, StringComparison.OrdinalIgnoreCase))
                    File.Copy(item.Path, target, overwrite: true);
                else
                    ImageIO.Save(ImageIO.LoadNormalized(item.Path), target);
                App.Store.Refresh(target);
                StatusText.Text = "Salvata in " + target;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Salvataggio non riuscito: " + ex.Message;
            }
        }

        private void CopyItems(CaptureItem[] items)
        {
            if (items.Length == 0) return;
            try
            {
                var data = new DataObject();
                var files = new System.Collections.Specialized.StringCollection();
                files.AddRange(items.Select(i => i.Path).ToArray());
                data.SetFileDropList(files);
                bool singleImage = items.Length == 1 && !items[0].IsVideo;
                if (singleImage)
                    data.SetImage(ImageIO.LoadNormalized(items[0].Path));
                Clipboard.SetDataObject(data, true);
                StatusText.Text = singleImage ? "Immagine copiata negli appunti." : $"{items.Length} file copiati negli appunti.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Impossibile copiare: " + ex.Message;
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            var items = Selected;
            if (items.Length > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, items.Select(i => i.Path)));
        }

        private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (Selected.FirstOrDefault() is { } item && File.Exists(item.Path))
                Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var items = Selected;
            if (items.Length == 0) return;
            foreach (var item in items)
            {
                try { App.Store.Delete(item); }
                catch (Exception ex) { StatusText.Text = "Impossibile eliminare: " + ex.Message; }
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e) => App.Current.OpenCapturesFolder();

        // ---------- lista ----------

        private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Un doppio clic veloce sui tasti della scheda non apre la cattura.
            if (IsInsideButton(e.OriginalSource as DependencyObject)) return;
            if (ItemUnderMouse(e) is { } item)
                Open(item);
        }

        private static bool IsInsideButton(DependencyObject? element)
        {
            while (element != null && element is not ListBoxItem)
            {
                if (element is System.Windows.Controls.Primitives.ButtonBase) return true;
                element = element is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Edit_Click(sender, e);
            else if (e.Key == Key.Delete) Delete_Click(sender, e);
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) Copy_Click(sender, e);
            else return;
            e.Handled = true;
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatus();
            UpdatePreview();
        }

        private CaptureItem? ItemUnderMouse(MouseEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not ListBoxItem)
                element = VisualTreeHelper.GetParent(element);
            return (element as ListBoxItem)?.DataContext as CaptureItem;
        }

        // Trascina le catture verso altre applicazioni (email, chat, documenti...).
        private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(List);
            // Sui tasti della scheda non parte il trascinamento.
            _dragArmed = ItemUnderMouse(e) != null && !IsInsideButton(e.OriginalSource as DependencyObject);
        }

        private void List_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(List) - _dragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _dragArmed = false;
            var paths = Selected.Select(i => i.Path).Where(File.Exists).ToArray();
            if (paths.Length == 0) return;
            DragDrop.DoDragDrop(List, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
        }
    }
}
