using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D = System.Drawing;
using D2 = System.Drawing.Drawing2D;

namespace ScreenLove.Services
{
    /// <summary>Icona dell'app disegnata a runtime: cuore bianco su quadrato rosa.</summary>
    public static class IconFactory
    {
        public static readonly D.Color Accent = D.Color.FromArgb(255, 0xFF, 0x3D, 0x7F);

        private static D.Icon? _icon;
        private static ImageSource? _imageSource;

        public static D.Icon AppIcon => _icon ??= Create(32);

        public static ImageSource AppImageSource => _imageSource ??= CreateImageSource();

        private static D.Icon Create(int size)
        {
            using var bmp = new D.Bitmap(size, size);
            using (var g = D.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = D2.SmoothingMode.AntiAlias;
                g.Clear(D.Color.Transparent);
                float s = size / 32f;

                using (var bg = RoundedRect(new D.RectangleF(1 * s, 1 * s, 30 * s, 30 * s), 7 * s))
                using (var brush = new D.SolidBrush(Accent))
                    g.FillPath(brush, bg);

                using var heart = new D2.GraphicsPath();
                heart.AddEllipse(6.5f * s, 8f * s, 10f * s, 10f * s);
                heart.AddEllipse(15.5f * s, 8f * s, 10f * s, 10f * s);
                heart.AddPolygon(new[]
                {
                    new D.PointF(7.2f * s, 15.5f * s),
                    new D.PointF(24.8f * s, 15.5f * s),
                    new D.PointF(16f * s, 25.5f * s),
                });
                heart.FillMode = D2.FillMode.Winding;
                g.FillPath(D.Brushes.White, heart);
            }
            return D.Icon.FromHandle(bmp.GetHicon());
        }

        private static ImageSource CreateImageSource()
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(AppIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }

        private static D2.GraphicsPath RoundedRect(D.RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new D2.GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
