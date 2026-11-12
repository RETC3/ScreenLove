using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ScreenLove.Services;

namespace ScreenLove.Editor
{
    /// <summary>
    /// Effetti "da documentazione" applicati all'intera immagine. Ricevono e restituiscono
    /// immagini BGRA a 96 DPI; dove serve, i bordi esterni diventano trasparenti.
    /// </summary>
    public static class ImageEffects
    {
        public static BitmapSource RoundCorners(BitmapSource src, double radius)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            radius = Math.Min(radius, Math.Min(w, h) / 2.0);
            return Render(w, h, dc =>
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), radius, radius));
                dc.DrawImage(src, new Rect(0, 0, w, h));
                dc.Pop();
            });
        }

        public static BitmapSource AddBorder(BitmapSource src, int thickness, Color color)
        {
            int w = src.PixelWidth + thickness * 2, h = src.PixelHeight + thickness * 2;
            return Render(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, w, h));
                dc.DrawImage(src, new Rect(thickness, thickness, src.PixelWidth, src.PixelHeight));
            });
        }

        /// <summary>Ombra morbida in basso a destra; l'immagine viene allargata per contenerla.</summary>
        public static BitmapSource AddShadow(BitmapSource src, double blur = 18, double depth = 6, double opacity = 0.45)
        {
            int margin = (int)Math.Ceiling(blur + depth);
            int w = src.PixelWidth + margin * 2, h = src.PixelHeight + margin * 2;

            var image = new Image
            {
                Source = src,
                Width = src.PixelWidth,
                Height = src.PixelHeight,
                Effect = new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 315, Opacity = opacity, Color = Colors.Black },
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var canvas = new Canvas { Width = w, Height = h };
            Canvas.SetLeft(image, margin - depth / 2);
            Canvas.SetTop(image, margin - depth / 2);
            canvas.Children.Add(image);
            canvas.Measure(new Size(w, h));
            canvas.Arrange(new Rect(0, 0, w, h));

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(canvas);
            return ImageIO.Normalize(rtb);
        }

        /// <summary>Bordo inferiore "strappato" a zig-zag irregolare (sempre uguale per la stessa immagine).</summary>
        public static BitmapSource TornBottomEdge(BitmapSource src)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            double amplitude = Math.Clamp(h / 40.0, 5, 12);
            var rnd = new Random(w * 31 + h);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, 0), true, true);
                ctx.LineTo(new Point(w, 0), false, false);
                ctx.LineTo(new Point(w, h - amplitude * 2), false, false);
                double x = w;
                bool up = true;
                while (x > 0)
                {
                    x -= 8 + rnd.NextDouble() * 14;
                    double y = h - (up ? amplitude * (1.6 + rnd.NextDouble() * 0.4) : amplitude * rnd.NextDouble() * 0.5);
                    ctx.LineTo(new Point(Math.Max(0, x), y), false, false);
                    up = !up;
                }
                ctx.LineTo(new Point(0, h - amplitude * 2), false, false);
            }
            geo.Freeze();

            return Render(w, h, dc =>
            {
                dc.PushClip(geo);
                dc.DrawImage(src, new Rect(0, 0, w, h));
                dc.Pop();
            });
        }

        /// <summary>Testo semitrasparente in basso a destra, con contorno per leggerlo su ogni sfondo.</summary>
        public static BitmapSource AddWatermark(BitmapSource src, string text)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            if (string.IsNullOrWhiteSpace(text)) return src;
            // Dopo un'ombra o un bordo strappato l'immagine ha margini trasparenti:
            // il testo va dentro la parte opaca, non a cavallo del bordo.
            var content = OpaqueBounds(src);
            double size = Math.Clamp(Math.Min(content.Width, content.Height) / 16.0, 12, 64);
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, 1.0);
            double margin = size * 0.6;
            var origin = new Point(
                Math.Max(content.X + margin, content.Right - ft.Width - margin),
                Math.Max(content.Y, content.Bottom - ft.Height - margin * 0.8));

            return Render(w, h, dc =>
            {
                dc.DrawImage(src, new Rect(0, 0, w, h));
                var geo = ft.BuildGeometry(origin);
                dc.PushOpacity(0.55);
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), size / 8) { LineJoin = PenLineJoin.Round }, geo);
                dc.DrawGeometry(Brushes.White, null, geo);
                dc.Pop();
            });
        }

        /// <summary>Rettangolo che contiene i pixel completamente opachi (tutta l'immagine se non ce ne sono).</summary>
        private static Rect OpaqueBounds(BitmapSource src)
        {
            var img = ImageIO.Normalize(src);
            int w = img.PixelWidth, h = img.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            img.CopyPixels(px, stride, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] == 255)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            return maxX < 0 ? new Rect(0, 0, w, h) : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static BitmapSource Render(int w, int h, Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                draw(dc);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            return ImageIO.Normalize(rtb);
        }
    }
}
