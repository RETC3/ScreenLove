using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace ScreenLove.Services
{
    public sealed record DecodedCode(string Format, string Text)
    {
        public bool IsLink => Uri.TryCreate(Text, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
    }

    /// <summary>Legge codici QR, Data Matrix e codici a barre presenti in un'immagine (ZXing.Net, offline).</summary>
    public static class CodeReader
    {
        public static IReadOnlyList<DecodedCode> Decode(BitmapSource image)
        {
            var normalized = ImageIO.Normalize(image);
            int w = normalized.PixelWidth, h = normalized.PixelHeight;
            var pixels = new byte[w * h * 4];
            normalized.CopyPixels(pixels, w * 4, 0);

            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions { TryHarder = true, TryInverted = true },
            };
            var source = new RGBLuminanceSource(pixels, w, h, RGBLuminanceSource.BitmapFormat.BGRA32);
            var results = reader.DecodeMultiple(source);
            if (results == null)
                return Array.Empty<DecodedCode>();

            return results
                .Where(r => !string.IsNullOrEmpty(r.Text))
                .Select(r => new DecodedCode(FriendlyName(r.BarcodeFormat), r.Text))
                .DistinctBy(c => c.Text)
                .ToList();
        }

        private static string FriendlyName(BarcodeFormat f) => f switch
        {
            BarcodeFormat.QR_CODE => "QR code",
            BarcodeFormat.DATA_MATRIX => "Data Matrix",
            BarcodeFormat.AZTEC => "Aztec",
            BarcodeFormat.PDF_417 => "PDF417",
            _ => "Codice a barre " + f.ToString().Replace('_', '-'),
        };
    }
}
