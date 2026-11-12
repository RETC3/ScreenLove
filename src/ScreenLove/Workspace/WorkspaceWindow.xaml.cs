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
            Icon = IconFactory.AppImageSource;
            List.ItemsSource = App.Store.Items;
            App.Store.Items.CollectionChanged += (_, _) => UpdateEmptyState();
            DelayButton.Content = $"Ritardo {App.Settings.DelaySeconds} s";
            UpdateEmptyState();
            UpdateStatus();
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
                Open(item);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var items = Selected;
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
            if (ItemUnderMouse(e) is { } item)
                Open(item);
        }

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Edit_Click(sender, e);
            else if (e.Key == Key.Delete) Delete_Click(sender, e);
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) Copy_Click(sender, e);
            else return;
            e.Handled = true;
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStatus();

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
            _dragArmed = ItemUnderMouse(e) != null;
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
