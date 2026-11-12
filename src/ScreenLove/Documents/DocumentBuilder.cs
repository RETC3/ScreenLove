using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ScreenLove.Services;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ScreenLove.Documents
{
    public sealed class DocumentStep
    {
        public string ImagePath { get; set; } = "";
        public string Title { get; set; } = "";
        public string Comment { get; set; } = "";
    }

    public sealed class DocumentModel
    {
        public string Title { get; set; } = "Documento";
        public bool NumberSteps { get; set; } = true;
        public List<DocumentStep> Steps { get; } = new();

        public string StepHeading(int index, DocumentStep s)
        {
            string title = s.Title.Trim();
            if (!NumberSteps) return title;
            return string.IsNullOrEmpty(title) ? $"Passo {index + 1}" : $"{index + 1}. {title}";
        }
    }

    /// <summary>Fornisce a PDFsharp i font Segoe UI installati in Windows (incorporati nel PDF).</summary>
    internal sealed class WindowsFontResolver : IFontResolver
    {
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            GlobalFontSettings.FontResolver = new WindowsFontResolver();
            _installed = true;
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            // Tutti i testi usano Segoe UI: normale o grassetto.
            return new FontResolverInfo(isBold ? "segoeuib" : "segoeui");
        }

        public byte[]? GetFont(string faceName)
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string path = Path.Combine(fonts, faceName + ".ttf");
            if (!File.Exists(path)) path = Path.Combine(fonts, "arial.ttf"); // ripiego
            return File.ReadAllBytes(path);
        }
    }

    /// <summary>Crea manuali, guide e revisioni da una serie di catture con titolo e commento.</summary>
    public static class DocumentBuilder
    {
        public static void Build(DocumentModel model, string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".pdf": BuildPdf(model, path); break;
                case ".docx": BuildDocx(model, path); break;
                default: BuildHtml(model, path); break;
            }
        }

        /// <summary>L'immagine come PNG (le catture possono essere anche JPG o BMP).</summary>
        private static byte[] PngBytes(string imagePath, out int width, out int height)
        {
            var img = ImageIO.LoadNormalized(imagePath);
            width = img.PixelWidth;
            height = img.PixelHeight;
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        // ---------------------------------------------------------------- HTML

        private static void BuildHtml(DocumentModel model, string path)
        {
            // Solo i caratteri speciali dell'HTML: gli accenti restano leggibili (file UTF-8).
            static string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
            var sb = new StringBuilder();
            sb.Append($@"<!doctype html>
<html lang=""it""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>{E(model.Title)}</title>
<style>
  :root {{ --accent:#FF3D7F; --fg:#1d1f24; --muted:#6b6f78; --line:#e7e8ec; }}
  body {{ margin:0; font:16px/1.55 'Segoe UI',system-ui,sans-serif; color:var(--fg); background:#f6f7f9; }}
  main {{ max-width:980px; margin:0 auto; padding:40px 24px 64px; }}
  h1 {{ font-size:30px; margin:0 0 4px; }}
  .date {{ color:var(--muted); margin-bottom:36px; }}
  section {{ background:#fff; border:1px solid var(--line); border-radius:12px; padding:22px 24px; margin:0 0 24px; }}
  h2 {{ font-size:19px; margin:0 0 14px; }}
  h2 .n {{ display:inline-block; min-width:28px; height:28px; border-radius:14px; background:var(--accent); color:#fff;
          text-align:center; line-height:28px; font-size:15px; margin-right:8px; }}
  img {{ max-width:100%; height:auto; border-radius:6px; box-shadow:0 2px 12px rgba(0,0,0,.12); }}
  p {{ margin:14px 0 0; white-space:pre-wrap; }}
</style></head><body><main>
<h1>{E(model.Title)}</h1><div class=""date"">{DateTime.Now:dd/MM/yyyy}</div>
");
            for (int i = 0; i < model.Steps.Count; i++)
            {
                var s = model.Steps[i];
                var png = PngBytes(s.ImagePath, out _, out _);
                string title = s.Title.Trim();
                sb.Append("<section>");
                if (model.NumberSteps)
                    sb.Append($"<h2><span class=\"n\">{i + 1}</span>{E(title)}</h2>");
                else if (title.Length > 0)
                    sb.Append($"<h2>{E(title)}</h2>");
                sb.Append($"<img alt=\"{E(title)}\" src=\"data:image/png;base64,{Convert.ToBase64String(png)}\">");
                if (!string.IsNullOrWhiteSpace(s.Comment))
                    sb.Append($"<p>{E(s.Comment.Trim())}</p>");
                sb.Append("</section>\n");
            }
            sb.Append("</main></body></html>\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        // ---------------------------------------------------------------- PDF

        private static void BuildPdf(DocumentModel model, string path)
        {
            WindowsFontResolver.Install();

            var doc = new PdfDocument();
            doc.Info.Title = model.Title;
            doc.Info.Creator = "ScreenLove";

            const double margin = 48; // punti (1/72")
            var titleFont = new XFont("Segoe UI", 22, XFontStyleEx.Bold);
            var dateFont = new XFont("Segoe UI", 10, XFontStyleEx.Regular);
            var headFont = new XFont("Segoe UI", 13, XFontStyleEx.Bold);
            var textFont = new XFont("Segoe UI", 10.5, XFontStyleEx.Regular);
            var accent = new XSolidBrush(XColor.FromArgb(0xFF, 0x3D, 0x7F));
            var muted = new XSolidBrush(XColor.FromArgb(0x6B, 0x6F, 0x78));

            PdfPage page = null!;
            XGraphics gfx = null!;
            double y = 0, contentW = 0, pageH = 0;

            void NewPage()
            {
                gfx?.Dispose();
                page = doc.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                contentW = page.Width.Point - margin * 2;
                pageH = page.Height.Point;
                y = margin;
            }

            NewPage();
            gfx.DrawString(model.Title, titleFont, XBrushes.Black, new XRect(margin, y, contentW, 30), XStringFormats.TopLeft);
            y += 32;
            gfx.DrawString(DateTime.Now.ToString("dd/MM/yyyy"), dateFont, muted, new XRect(margin, y, contentW, 14), XStringFormats.TopLeft);
            y += 30;

            for (int i = 0; i < model.Steps.Count; i++)
            {
                var s = model.Steps[i];
                var png = PngBytes(s.ImagePath, out int pw, out int ph);
                // PDFsharp legge direttamente il buffer: lo stream deve renderlo accessibile.
                using var ms = new MemoryStream(png, 0, png.Length, writable: false, publiclyVisible: true);
                using var ximg = XImage.FromStream(ms);

                // immagine: larghezza utile, al massimo metà pagina in altezza (96 DPI → punti)
                double imgW = Math.Min(contentW, pw * 0.75), imgH = imgW * ph / pw;
                double maxH = (pageH - margin * 2) * 0.55;
                if (imgH > maxH) { imgH = maxH; imgW = imgH * pw / ph; }

                string heading = model.StepHeading(i, s);
                var commentLines = string.IsNullOrWhiteSpace(s.Comment) ? new List<string>() : Wrap(gfx, s.Comment.Trim(), textFont, contentW);
                double lineH = textFont.Height * 1.25;
                double needed = (heading.Length > 0 ? 24 : 0) + imgH + 10 + Math.Min(commentLines.Count, 6) * lineH + 24;
                if (y + needed > pageH - margin && y > margin + 1)
                    NewPage();

                if (heading.Length > 0)
                {
                    if (model.NumberSteps)
                    {
                        gfx.DrawEllipse(accent, margin, y, 18, 18);
                        gfx.DrawString((i + 1).ToString(), new XFont("Segoe UI", 9.5, XFontStyleEx.Bold), XBrushes.White,
                            new XRect(margin, y, 18, 18), XStringFormats.Center);
                        string rest = s.Title.Trim();
                        gfx.DrawString(rest, headFont, XBrushes.Black, new XRect(margin + 26, y, contentW - 26, 18), XStringFormats.CenterLeft);
                    }
                    else
                    {
                        gfx.DrawString(heading, headFont, XBrushes.Black, new XRect(margin, y, contentW, 18), XStringFormats.CenterLeft);
                    }
                    y += 26;
                }

                gfx.DrawImage(ximg, margin, y, imgW, imgH);
                gfx.DrawRectangle(new XPen(XColor.FromArgb(0xE7, 0xE8, 0xEC), 0.75), margin, y, imgW, imgH);
                y += imgH + 10;

                foreach (var line in commentLines)
                {
                    if (y + lineH > pageH - margin) NewPage();
                    gfx.DrawString(line, textFont, XBrushes.Black, new XRect(margin, y, contentW, lineH), XStringFormats.TopLeft);
                    y += lineH;
                }
                y += 22;
            }

            gfx.Dispose();
            doc.Save(path);
        }

        /// <summary>A capo automatico per parole (rispettando gli a capo del testo).</summary>
        private static List<string> Wrap(XGraphics gfx, string text, XFont font, double width)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                var current = new StringBuilder();
                foreach (var word in paragraph.Split(' '))
                {
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (current.Length > 0 && gfx.MeasureString(candidate, font).Width > width)
                    {
                        lines.Add(current.ToString());
                        current.Clear().Append(word);
                    }
                    else
                    {
                        current.Clear().Append(candidate);
                    }
                }
                lines.Add(current.ToString());
            }
            return lines;
        }

        // ---------------------------------------------------------------- Word

        private static void BuildDocx(DocumentModel model, string path)
        {
            using var wd = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var main = wd.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body());
            var body = main.Document.Body!;

            body.Append(TextParagraph(model.Title, 40, bold: true, color: "1D1F24"));
            body.Append(TextParagraph(DateTime.Now.ToString("dd/MM/yyyy"), 20, color: "6B6F78", spaceAfter: 360));

            const long maxWidthEmu = 6_000_000;   // ~16,5 cm: larghezza utile su A4 con margini standard
            const long maxHeightEmu = 5_200_000;  // ~14,5 cm
            uint id = 1;
            for (int i = 0; i < model.Steps.Count; i++)
            {
                var s = model.Steps[i];
                string heading = model.StepHeading(i, s);
                if (heading.Length > 0)
                    body.Append(TextParagraph(heading, 28, bold: true, color: "1D1F24", spaceBefore: 240));

                var png = PngBytes(s.ImagePath, out int pw, out int ph);
                var imagePart = main.AddImagePart(ImagePartType.Png);
                using (var ms = new MemoryStream(png))
                    imagePart.FeedData(ms);

                long cx = pw * 9525L, cy = ph * 9525L; // pixel a 96 DPI → EMU
                if (cx > maxWidthEmu) { cy = cy * maxWidthEmu / cx; cx = maxWidthEmu; }
                if (cy > maxHeightEmu) { cx = cx * maxHeightEmu / cy; cy = maxHeightEmu; }
                body.Append(new W.Paragraph(new W.Run(ImageElement(main.GetIdOfPart(imagePart), cx, cy, id++, Path.GetFileName(s.ImagePath)))));

                if (!string.IsNullOrWhiteSpace(s.Comment))
                    foreach (var line in s.Comment.Trim().Replace("\r\n", "\n").Split('\n'))
                        body.Append(TextParagraph(line, 22));
            }

            body.Append(new W.SectionProperties(
                new W.PageSize { Width = 11906U, Height = 16838U },  // A4 in ventesimi di punto
                new W.PageMargin { Top = 1134, Bottom = 1134, Left = 1134U, Right = 1134U }));
            main.Document.Save();
        }

        private static W.Paragraph TextParagraph(string text, int halfPoints, bool bold = false, string? color = null, int spaceBefore = 0, int spaceAfter = 120)
        {
            // L'ordine degli elementi è imposto dallo standard: carattere, grassetto, colore, dimensione.
            var props = new W.RunProperties(new W.RunFonts { Ascii = "Segoe UI", HighAnsi = "Segoe UI", ComplexScript = "Segoe UI" });
            if (bold) props.Append(new W.Bold());
            if (color != null) props.Append(new W.Color { Val = color });
            props.Append(new W.FontSize { Val = halfPoints.ToString() });
            return new W.Paragraph(
                new W.ParagraphProperties(new W.SpacingBetweenLines { Before = spaceBefore.ToString(), After = spaceAfter.ToString() }),
                new W.Run(props, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        /// <summary>Immagine in linea nel testo (struttura DrawingML richiesta da Word).</summary>
        private static W.Drawing ImageElement(string relationshipId, long cx, long cy, uint id, string name) =>
            new(new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip { Embed = relationshipId },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });
    }
}
