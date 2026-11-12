using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLove.Services
{
    public static class ImageIO
    {
        /// <summary>Salva in base all'estensione (png, jpg/jpeg, bmp). Predefinito: PNG.</summary>
        public static void Save(BitmapSource image, string path)
        {
            BitmapEncoder encoder;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                    encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                    image = OnWhite(image); // JPEG non ha trasparenza: niente sfondo nero
                    break;
                case ".bmp":
                    encoder = new BmpBitmapEncoder();
                    image = OnWhite(image);
                    break;
                default:
                    encoder = new PngBitmapEncoder();
                    break;
            }

            encoder.Frames.Add(BitmapFrame.Create(image));
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
                encoder.Save(fs);
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>Appoggia l'immagine su sfondo bianco (per i formati senza canale alfa).</summary>
        public static BitmapSource OnWhite(BitmapSource image)
        {
            int w = image.PixelWidth, h = image.PixelHeight;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, w, h));
                dc.DrawImage(image, new System.Windows.Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var bgr = new FormatConvertedBitmap(rtb, PixelFormats.Bgr32, null, 0);
            bgr.Freeze();
            return bgr;
        }

        /// <summary>
        /// Carica un'immagine senza bloccare il file e la normalizza in BGRA32 a 96 DPI,
        /// così 1 pixel = 1 unità WPF in tutto l'editor.
        /// </summary>
        public static BitmapSource LoadNormalized(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return Normalize(frame);
        }

        public static BitmapSource Normalize(BitmapSource source)
        {
            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
    }
}
