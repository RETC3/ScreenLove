using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ScreenLove.Services;

namespace ScreenLove.Editor
{
    public partial class EditorWindow : Window
    {
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0xE5, 0x39, 0x35), // rosso
            Color.FromRgb(0xFF, 0x3D, 0x7F), // rosa ScreenLove
            Color.FromRgb(0xFB, 0x8C, 0x00), // arancio
            Color.FromRgb(0xFD, 0xD8, 0x35), // giallo
            Color.FromRgb(0x43, 0xA0, 0x47), // verde
            Color.FromRgb(0x1E, 0x88, 0xE5), // blu
            Color.FromRgb(0x8E, 0x24, 0xAA), // viola
            Colors.White,
            Color.FromRgb(0x21, 0x21, 0x21), // nero
        };

        private static readonly Dictionary<Key, ToolKind> ToolKeys = new()
        {
            [Key.V] = ToolKind.Select,
            [Key.A] = ToolKind.Arrow,
            [Key.R] = ToolKind.Rectangle,
            [Key.E] = ToolKind.Ellipse,
            [Key.H] = ToolKind.Highlight,
            [Key.T] = ToolKind.Text,
            [Key.N] = ToolKind.Step,
            [Key.B] = ToolKind.Blur,
            [Key.S] = ToolKind.Spotlight,
            [Key.C] = ToolKind.Crop,
        };

        private readonly string _path;
        private readonly EditorDocument _doc;
        private readonly Stack<EditorDocument.Snapshot> _undo = new();
        private readonly Stack<EditorDocument.Snapshot> _redo = new();
        private bool _dirty;
        private TextBox? _textBox;
        private Point _textPosition;
        private bool _fitPending = true;

        public EditorWindow(string path)
        {
            InitializeComponent();
            Services.WindowFit.Apply(this);
            Icon = IconFactory.AppImageSource;
            _path = path;
            _doc = new EditorDocument(ImageIO.LoadNormalized(path));

            Layer.Document = _doc;
            Layer.BeforeChange += PushUndo;
            Layer.Changed += UpdateStatus;
            Layer.TextRequested += BeginText;
            Layer.CropRequested += ApplyCrop;

            BuildPalette();
            SelectTool(ToolKind.Arrow);
            RefreshImage();
            ThicknessText.Text = ThicknessSlider.Value.ToString("0");

            PreviewKeyDown += OnPreviewKeyDown;
            Scroller.SizeChanged += (_, _) =>
            {
                if (_fitPending && Scroller.ActualWidth > 0)
                {
                    _fitPending = false;
                    ZoomToFit();
                }
            };
            UpdateTitle();
        }

        // ---------- configurazione UI ----------

        private void BuildPalette()
        {
            foreach (var c in Palette)
            {
                var swatch = new RadioButton
                {
                    Style = (Style)FindResource("SwatchToggle"),
                    GroupName = "palette",
                    Background = new SolidColorBrush(c),
                    Tag = c,
                    IsChecked = c == Palette[0],
                };
                swatch.Checked += (_, _) => SetColor(c);
                PalettePanel.Children.Add(swatch);
            }
            Layer.CurrentColor = Palette[0];
        }

        private void SetColor(Color c)
        {
            Layer.CurrentColor = c;
            if (_textBox != null)
                _textBox.Foreground = new SolidColorBrush(c);
            if (Layer.Selected != null && _doc.Items.Contains(Layer.Selected) && Layer.Selected.Color != c)
            {
                PushUndo();
                Layer.Selected.Color = c;
                Layer.InvalidateVisual();
            }
        }

        private void Thickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Layer == null) return; // durante InitializeComponent
            Layer.CurrentThickness = e.NewValue;
            ThicknessText.Text = e.NewValue.ToString("0");
            if (Layer.Selected != null && _doc.Items.Contains(Layer.Selected) && Layer.Selected.Thickness != e.NewValue)
            {
                PushUndo();
                Layer.Selected.Thickness = e.NewValue;
                if (Layer.Selected is TextAnnotation t)
                    t.FontSize = Layer.TextSize;
                Layer.InvalidateVisual();
            }
        }

        private void SelectTool(ToolKind tool)
        {
            foreach (var rb in ToolPanel.Children.OfType<RadioButton>())
                if ((string)rb.Tag == tool.ToString())
                    rb.IsChecked = true;
        }

        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (Layer == null) return;
            CommitText();
            Layer.CancelInteraction();
            Layer.Tool = Enum.Parse<ToolKind>((string)((FrameworkElement)sender).Tag);
            if (Layer.Tool != ToolKind.Select && Layer.Tool != ToolKind.Text)
                Layer.Selected = null;
            Layer.InvalidateVisual();
        }

        // ---------- stato / annulla ----------

        private void PushUndo()
        {
            _undo.Push(_doc.TakeSnapshot());
            _redo.Clear();
            _dirty = true;
            UpdateTitle();
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
        private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            CommitText();
            if (_undo.Count == 0) return;
            _redo.Push(_doc.TakeSnapshot());
            _doc.Restore(_undo.Pop());
            AfterRestore();
        }

        private void Redo()
        {
            CommitText();
            if (_redo.Count == 0) return;
            _undo.Push(_doc.TakeSnapshot());
            _doc.Restore(_redo.Pop());
            AfterRestore();
        }

        private void AfterRestore()
        {
            Layer.Selected = null;
            _dirty = true;
            RefreshImage();
            UpdateTitle();
        }

        private void RefreshImage()
        {
            BaseImage.Source = _doc.Base;
            BaseImage.Width = _doc.Width;
            BaseImage.Height = _doc.Height;
            Layer.SyncSize();
            UpdateStatus();
        }

        private void UpdateTitle()
        {
            Title = $"{(_dirty ? "● " : "")}{Path.GetFileName(_path)} — ScreenLove";
            UndoButton.IsEnabled = _undo.Count > 0;
            RedoButton.IsEnabled = _redo.Count > 0;
        }

        private void UpdateStatus()
        {
            StatusText.Text = $"{_doc.Width} × {_doc.Height} px  ·  zoom {Zoom.ScaleX:P0}  ·  {_doc.Items.Count} elementi";
            UpdateTitle();
        }

        // ---------- testo ----------

        private void BeginText(Point p)
        {
            CommitText();
            _textPosition = p;
            _textBox = new TextBox
            {
                MinWidth = 60,
                FontFamily = TextAnnotation.Face.FontFamily,
                FontWeight = FontWeights.SemiBold,
                FontSize = Layer.TextSize,
                Foreground = new SolidColorBrush(Layer.CurrentColor),
                Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)),
                BorderBrush = (Brush)FindResource("AccentBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                AcceptsReturn = true,
                CaretBrush = Brushes.White,
            };
            Canvas.SetLeft(_textBox, p.X - 2);
            Canvas.SetTop(_textBox, p.Y - 1);
            _textBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    CancelText();
                    e.Handled = true;
                }
            };
            _textBox.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
                {
                    CommitText();
                    e.Handled = true;
                }
            };
            _textBox.LostKeyboardFocus += (_, _) => CommitText();
            TextCanvas.Children.Add(_textBox);
            Dispatcher.BeginInvoke(() => _textBox?.Focus());
        }

        private void CommitText()
        {
            if (_textBox == null) return;
            var box = _textBox;
            _textBox = null;
            TextCanvas.Children.Remove(box);

            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                PushUndo();
                var t = new TextAnnotation
                {
                    Position = _textPosition,
                    Text = box.Text.TrimEnd(),
                    FontSize = box.FontSize,
                    Color = ((SolidColorBrush)box.Foreground).Color,
                    Thickness = Layer.CurrentThickness,
                };
                _doc.Items.Add(t);
                Layer.Selected = t;
                Layer.InvalidateVisual();
                UpdateStatus();
            }
        }

        private void CancelText()
        {
            if (_textBox == null) return;
            TextCanvas.Children.Remove(_textBox);
            _textBox = null;
        }

        // ---------- ritaglio ----------

        private void ApplyCrop(Int32Rect r)
        {
            r = _doc.ClipToImage(r);
            if (r.IsEmpty) return;
            PushUndo();
            var offset = new Vector(-r.X, -r.Y);
            _doc.SetBase(_doc.Crop(r));
            foreach (var a in _doc.Items)
                a.Move(offset);
            Layer.Selected = null;
            RefreshImage();
            SelectTool(ToolKind.Select);
        }

        // ---------- comandi ----------

        private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelected();

        private void DeleteSelected()
        {
            if (Layer.Selected == null || !_doc.Items.Contains(Layer.Selected)) return;
            PushUndo();
            _doc.Items.Remove(Layer.Selected);
            Layer.Selected = null;
            Layer.InvalidateVisual();
            UpdateStatus();
        }

        private void Copy_Click(object sender, RoutedEventArgs e) => CopyToClipboard();

        // ---------- effetti ----------

        private void Effects_Click(object sender, RoutedEventArgs e)
        {
            EffectsButton.ContextMenu.PlacementTarget = EffectsButton;
            EffectsButton.ContextMenu.Placement = PlacementMode.Bottom;
            EffectsButton.ContextMenu.IsOpen = true;
        }

        /// <summary>
        /// Applica un effetto all'immagine finale: le annotazioni vengono fissate nell'immagine
        /// (Annulla le riporta modificabili).
        /// </summary>
        private void ApplyEffect(string name, Func<System.Windows.Media.Imaging.BitmapSource, System.Windows.Media.Imaging.BitmapSource> effect)
        {
            CommitText();
            Layer.CancelInteraction();
            try
            {
                var result = effect(ImageIO.Normalize(_doc.Flatten()));
                PushUndo();
                _doc.Items = new List<Annotation>();
                _doc.SetBase(result);
                Layer.Selected = null;
                RefreshImage();
                ZoomToFit();
                StatusText.Text = $"Effetto applicato: {name}  ·  Ctrl+Z per annullare";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Effetto non riuscito: " + ex.Message;
            }
        }

        private void EffectShadow_Click(object sender, RoutedEventArgs e) =>
            ApplyEffect("ombra", img => ImageEffects.AddShadow(img));

        private void EffectRound_Click(object sender, RoutedEventArgs e) =>
            ApplyEffect("angoli arrotondati", img => ImageEffects.RoundCorners(img, 12));

        private void EffectBorder_Click(object sender, RoutedEventArgs e) =>
            ApplyEffect("bordo", img => ImageEffects.AddBorder(img, (int)Math.Max(1, Layer.CurrentThickness), Layer.CurrentColor));

        private void EffectTorn_Click(object sender, RoutedEventArgs e) =>
            ApplyEffect("bordo strappato", ImageEffects.TornBottomEdge);

        private void EffectWatermark_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(App.Settings.WatermarkText))
            {
                StatusText.Text = "Imposta il testo del watermark nelle Impostazioni.";
                return;
            }
            ApplyEffect("watermark", img => ImageEffects.AddWatermark(img, App.Settings.WatermarkText));
        }

        private void EffectDoc_Click(object sender, RoutedEventArgs e) =>
            ApplyEffect("stile documentazione", img => ImageEffects.AddShadow(ImageEffects.RoundCorners(img, 10)));

        private void ExtractText_Click(object sender, RoutedEventArgs e)
        {
            CommitText();
            // Si usa l'immagine di base: le annotazioni (frecce, sfocature…) disturberebbero il riconoscimento.
            App.Current.ShowTextRecognition(_doc.Base);
        }

        private void CopyToClipboard()
        {
            CommitText();
            try
            {
                Clipboard.SetImage(_doc.Flatten());
                StatusText.Text = "Immagine copiata negli appunti.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Impossibile copiare: " + ex.Message;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) => Save();
        private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

        private bool Save()
        {
            CommitText();
            try
            {
                ImageIO.Save(_doc.Flatten(), _path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Salvataggio non riuscito: " + ex.Message, "ScreenLove", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            _dirty = false;
            UpdateTitle();
            App.Store.Refresh(_path);
            StatusText.Text = "Salvato.";
            return true;
        }

        private void SaveAs()
        {
            CommitText();
            var dlg = new SaveFileDialog
            {
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
                FileName = Path.GetFileNameWithoutExtension(_path) + "_modificata",
                InitialDirectory = Path.GetDirectoryName(_path),
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                ImageIO.Save(_doc.Flatten(), dlg.FileName);
                App.Store.Refresh(dlg.FileName);
                StatusText.Text = "Salvato in " + dlg.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Salvataggio non riuscito: " + ex.Message, "ScreenLove", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            CommitText();
            if (_dirty)
            {
                var answer = MessageBox.Show(this, "Salvare le modifiche a " + Path.GetFileName(_path) + "?", "ScreenLove",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save()))
                    e.Cancel = true;
            }
            base.OnClosing(e);
        }

        // ---------- zoom ----------

        private void SetZoom(double z)
        {
            z = Math.Clamp(z, 0.05, 8);
            Zoom.ScaleX = Zoom.ScaleY = z;
            RenderOptions.SetBitmapScalingMode(BaseImage, z >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            UpdateStatus();
        }

        private void ZoomToFit()
        {
            double availW = Scroller.ActualWidth - 60, availH = Scroller.ActualHeight - 60;
            if (availW <= 0 || availH <= 0) return;
            SetZoom(Math.Min(1, Math.Min(availW / _doc.Width, availH / _doc.Height)));
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Zoom.ScaleX * 1.25);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Zoom.ScaleX / 1.25);
        private void ZoomFit_Click(object sender, RoutedEventArgs e) => ZoomToFit();
        private void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1);

        private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            SetZoom(Zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
            e.Handled = true;
        }

        // ---------- tastiera ----------

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_textBox != null && _textBox.IsKeyboardFocused)
                return; // si sta scrivendo un testo

            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.Z: Undo(); break;
                    case Key.Y: Redo(); break;
                    case Key.S: Save(); break;
                    case Key.C: CopyToClipboard(); break;
                    case Key.D0: SetZoom(1); break;
                    default: return;
                }
                e.Handled = true;
                return;
            }
            if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
            {
                Redo();
                e.Handled = true;
                return;
            }
            if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
            {
                SaveAs();
                e.Handled = true;
                return;
            }
            if (mods != ModifierKeys.None) return;

            if (e.Key is Key.Delete or Key.Back)
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Layer.CancelInteraction();
                Layer.Selected = null;
                Layer.InvalidateVisual();
                e.Handled = true;
            }
            else if (ToolKeys.TryGetValue(e.Key, out var tool))
            {
                SelectTool(tool);
                e.Handled = true;
            }
        }
    }
}
