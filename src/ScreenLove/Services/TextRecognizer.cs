using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace ScreenLove.Services
{
    public sealed record OcrLanguage(string Tag, string Name);

    public sealed record OcrOutput(string Text, int Lines, string LanguageTag);

    /// <summary>
    /// Riconoscimento del testo con l'OCR integrato in Windows (nessun servizio esterno,
    /// funziona offline). Le lingue disponibili sono quelle installate in Windows.
    /// </summary>
    public static class TextRecognizer
    {
        public static IReadOnlyList<OcrLanguage> AvailableLanguages =>
            OcrEngine.AvailableRecognizerLanguages.Select(l => new OcrLanguage(l.LanguageTag, l.NativeName)).ToList();

        public static string? DefaultLanguageTag =>
            OcrEngine.TryCreateFromUserProfileLanguages()?.RecognizerLanguage.LanguageTag;

        public static async Task<OcrOutput> RecognizeAsync(BitmapSource image, string? languageTag = null)
        {
            var engine = languageTag != null
                ? OcrEngine.TryCreateFromLanguage(new Language(languageTag))
                : OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
                throw new InvalidOperationException(
                    "Nessuna lingua per il riconoscimento del testo è installata in Windows. " +
                    "Aggiungila da Impostazioni › Ora e lingua › Lingua (opzione \"Riconoscimento ottico dei caratteri\").");

            var (pixels, width, height) = Prepare(image);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                CryptographicBuffer.CreateFromByteArray(pixels), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);

            var result = await engine.RecognizeAsync(bitmap);
            return new OcrOutput(Format(result), result.Lines.Count, engine.RecognizerLanguage.LanguageTag);
        }

        /// <summary>
        /// Le catture dello schermo hanno testo piccolo (10–14 px): ingrandirle 2× migliora molto
        /// la precisione dell'OCR di Windows. Restano entro la dimensione massima supportata.
        /// </summary>
        private static (byte[] pixels, int width, int height) Prepare(BitmapSource image)
        {
            double max = OcrEngine.MaxImageDimension;
            double longest = Math.Max(image.PixelWidth, image.PixelHeight);
            double scale = longest * 2 <= max ? 2 : Math.Min(1, max / longest);

            BitmapSource source = image;
            if (Math.Abs(scale - 1) > 0.001)
            {
                var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
                scaled.Freeze();
                source = scaled;
            }

            var normalized = ImageIO.Normalize(source);
            int w = normalized.PixelWidth, h = normalized.PixelHeight;
            var pixels = new byte[w * h * 4];
            normalized.CopyPixels(pixels, w * 4, 0);
            return (pixels, w, h);
        }

        /// <summary>Ricompone le righe, lasciando una riga vuota tra i paragrafi.</summary>
        private static string Format(OcrResult result)
        {
            var sb = new StringBuilder();
            double prevBottom = double.NaN, prevHeight = 0;
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0) continue;
                double top = line.Words.Min(w => w.BoundingRect.Y);
                double bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
                double height = bottom - top;

                if (!double.IsNaN(prevBottom))
                {
                    sb.AppendLine();
                    if (top - prevBottom > Math.Max(height, prevHeight) * 0.9)
                        sb.AppendLine();
                }
                sb.Append(line.Text);
                prevBottom = bottom;
                prevHeight = height;
            }
            return sb.ToString();
        }
    }
}
