using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenLove.Capture
{
    /// <summary>Piccolo conto alla rovescia prima di una cattura ritardata (es. per i menu contestuali).</summary>
    public sealed class CountdownWindow : Window
    {
        private readonly TextBlock _text;

        private CountdownWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false; // non rubare il focus: il menu da catturare deve restare aperto
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _text = new TextBlock
            {
                FontSize = 56,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Content = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(210, 30, 31, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3D, 0x7F)),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(34, 6, 34, 10),
                Child = _text,
            };
        }

        public static async Task RunAsync(int seconds)
        {
            var w = new CountdownWindow();
            w.Show();
            for (int i = seconds; i > 0; i--)
            {
                w._text.Text = i.ToString();
                await Task.Delay(1000);
            }
            w.Close();
            await Task.Delay(150); // lascia a DWM il tempo di togliere la finestra dallo schermo
        }
    }
}
