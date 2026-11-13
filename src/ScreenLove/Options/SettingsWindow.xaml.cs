using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenLove.Services;

namespace ScreenLove.Options
{
    /// <summary>Impostazioni dell'app. Si lavora su una copia: nulla cambia finché non si preme Salva.</summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _edit = App.Settings.Clone();
        private readonly Dictionary<HotkeyAction, TextBox> _hotkeyBoxes = new();
        private bool _saved;

        public SettingsWindow()
        {
            InitializeComponent();
            Services.WindowFit.Apply(this);
            Icon = IconFactory.AppImageSource;

            FolderBox.Text = _edit.CapturesFolder;
            AutostartBox.IsChecked = App.IsAutostartEnabled();
            CopyBox.IsChecked = _edit.CopyToClipboard;
            WorkspaceBox.IsChecked = _edit.ShowWorkspaceAfterCapture;
            EditorBox.IsChecked = _edit.OpenEditorAfterCapture;
            CursorBox.IsChecked = _edit.IncludeCursor;
            DelayBox.ItemsSource = Enumerable.Range(1, 10).Select(s => $"{s} s").ToList();
            DelayBox.SelectedIndex = Math.Clamp(_edit.DelaySeconds, 1, 10) - 1;
            FpsBox.ItemsSource = new[] { 15, 24, 30, 60 };
            FpsBox.SelectedItem = new[] { 15, 24, 30, 60 }.Contains(_edit.RecordFps) ? _edit.RecordFps : 30;
            SysAudioBox.IsChecked = _edit.RecordSystemAudio;
            MicBox.IsChecked = _edit.RecordMicrophone;
            RecCursorBox.IsChecked = _edit.RecordCursor;
            WebcamBox.IsChecked = _edit.RecordWebcam;
            WatermarkBox.Text = _edit.WatermarkText;
            UpdatesBox.IsChecked = _edit.CheckForUpdates;
            StatsBox.IsChecked = _edit.SendUsageStats;
            VersionText.Text = $"Versione {Updater.CurrentVersion}" + (Updater.IsInstalled ? "" : " (portatile)");

            BuildHotkeyRows();
            // Finché si sceglie un tasto, quelli globali non devono intercettarlo.
            App.Current.SuspendHotkeys();
            Closed += (_, _) =>
            {
                if (!_saved) App.Current.RegisterHotkeys();
            };
        }

        private void BuildHotkeyRows()
        {
            int row = 0;
            foreach (var action in Enum.GetValues<HotkeyAction>())
            {
                HotkeyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = HotkeyBinding.Describe(action),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 10, 4),
                };
                Grid.SetRow(label, row);
                HotkeyGrid.Children.Add(label);

                var box = new TextBox
                {
                    IsReadOnly = true,
                    IsReadOnlyCaretVisible = false,
                    Margin = new Thickness(0, 4, 0, 4),
                    Tag = action,
                    Text = _edit.GetHotkey(action).Display,
                    ToolTip = "Clicca e premi la combinazione",
                };
                if (App.FailedHotkeys.Contains(action))
                    MarkBusy(box, true);
                box.PreviewKeyDown += HotkeyBox_Key;
                box.PreviewKeyUp += HotkeyBox_KeyUp;
                Grid.SetRow(box, row);
                Grid.SetColumn(box, 1);
                HotkeyGrid.Children.Add(box);
                _hotkeyBoxes[action] = box;

                var reset = new Button { Content = "Predefinito", Style = (Style)FindResource("ToolButton"), Margin = new Thickness(6, 2, 0, 2) };
                reset.Click += (_, _) =>
                {
                    var b = HotkeyBinding.Parse(HotkeyBinding.Defaults[action]);
                    _edit.SetHotkey(action, b);
                    box.Text = b.Display;
                    MarkBusy(box, false);
                    CheckDuplicates();
                };
                Grid.SetRow(reset, row);
                Grid.SetColumn(reset, 2);
                HotkeyGrid.Children.Add(reset);
                row++;
            }
        }

        private static void MarkBusy(TextBox box, bool busy)
        {
            box.BorderBrush = busy ? new SolidColorBrush(Color.FromRgb(0xFB, 0x8C, 0x00)) : (Brush)Application.Current.FindResource("LineBrush");
            box.ToolTip = busy ? "Combinazione già usata da un'altra applicazione: scegline un'altra" : "Clicca e premi la combinazione";
        }

        // Stamp genera solo l'evento di rilascio, gli altri tasti la pressione.
        private void HotkeyBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Snapshot || e.Key == Key.PrintScreen)
                HotkeyBox_Key(sender, e);
        }

        private void HotkeyBox_Key(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;
            var action = (HotkeyAction)box.Tag;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            e.Handled = true;

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin or Key.Tab)
                return; // si aspetta il tasto principale

            HotkeyBinding binding = key is Key.Delete or Key.Back && Keyboard.Modifiers == ModifierKeys.None
                ? default
                : new HotkeyBinding(Keyboard.Modifiers, key == Key.PrintScreen ? Key.Snapshot : key);

            _edit.SetHotkey(action, binding);
            box.Text = binding.Display;
            MarkBusy(box, false);
            CheckDuplicates();
        }

        private void CheckDuplicates()
        {
            var dup = _hotkeyBoxes.Keys
                .Select(a => (a, b: _edit.GetHotkey(a)))
                .Where(x => !x.b.IsEmpty)
                .GroupBy(x => x.b.ToString())
                .FirstOrDefault(g => g.Count() > 1);
            StatusText.Text = dup == null ? "" : $"{dup.First().b.Display} è assegnato a più azioni.";
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Cartella in cui salvare catture e video",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(FolderBox.Text) ? FolderBox.Text : "",
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                FolderBox.Text = dlg.SelectedPath;
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e) => _ = App.Current.CheckForUpdatesAsync(manual: true);

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string folder = FolderBox.Text.Trim();
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Cartella non valida: " + ex.Message;
                return;
            }
            if (!string.IsNullOrEmpty(StatusText.Text) && StatusText.Text.Contains("più azioni"))
                return;

            _edit.CapturesFolder = folder;
            _edit.CopyToClipboard = CopyBox.IsChecked == true;
            _edit.ShowWorkspaceAfterCapture = WorkspaceBox.IsChecked == true;
            _edit.OpenEditorAfterCapture = EditorBox.IsChecked == true;
            _edit.IncludeCursor = CursorBox.IsChecked == true;
            _edit.DelaySeconds = DelayBox.SelectedIndex + 1;
            _edit.RecordFps = (int)FpsBox.SelectedItem;
            _edit.RecordSystemAudio = SysAudioBox.IsChecked == true;
            _edit.RecordMicrophone = MicBox.IsChecked == true;
            _edit.RecordCursor = RecCursorBox.IsChecked == true;
            _edit.RecordWebcam = WebcamBox.IsChecked == true;
            _edit.WatermarkText = WatermarkBox.Text;
            _edit.CheckForUpdates = UpdatesBox.IsChecked == true;
            _edit.LastUpdateCheck = App.Settings.LastUpdateCheck;
            _edit.SendUsageStats = StatsBox.IsChecked == true;
            // il codice può essere nato mentre la finestra era aperta; disattivando le statistiche si cancella
            _edit.InstallId = _edit.SendUsageStats ? App.Settings.InstallId : null;

            try
            {
                App.SetAutostart(AutostartBox.IsChecked == true);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Avvio automatico non modificato: " + ex.Message;
            }

            App.Settings.CopyFrom(_edit);
            App.Current.ApplySettings();
            _saved = true;

            if (App.FailedHotkeys.Count > 0)
            {
                // resta aperta e segnala le combinazioni occupate
                foreach (var (action, box) in _hotkeyBoxes)
                    MarkBusy(box, App.FailedHotkeys.Contains(action));
                StatusText.Text = "Salvato. Alcune combinazioni sono già usate da altre app (in arancione).";
                App.Current.SuspendHotkeys();
                _saved = false;
                return;
            }
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
