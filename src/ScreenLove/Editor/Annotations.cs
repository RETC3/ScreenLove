using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLove.Editor
{
    public enum ToolKind
    {
        Select,
        Arrow,
        Rectangle,
        Ellipse,
        Highlight,
        Text,
        Step,
        Blur,
        Spotlight,
        Crop,
    }

    /// <summary>Elemento vettoriale disegnato sopra la cattura. Coordinate in pixel dell'immagine.</summary>
    public abstract class Annotation
    {
        public Color Color { get; set; } = Colors.Red;
        public double Thickness { get; set; } = 4;

        /// <summary>Ordine di disegno: sfocature sotto, spotlight in mezzo, il resto sopra.</summary>
        public virtual int Layer => 2;

        public abstract Rect Bounds { get; }
        public abstract void Render(DrawingContext dc, EditorDocument doc);
        public abstract void Move(Vector delta);

        /// <summary>Aggiorna la forma mentre viene trascinata (start = punto iniziale).</summary>
        public virtual void Drag(Point start, Point current, bool constrain) { }

        public virtual bool IsValid => Bounds.Width >= 3 || Bounds.Height >= 3;

        public virtual bool HitTest(Point p)
        {
            var b = Bounds;
            b.Inflate(Thickness / 2 + 4, Thickness / 2 + 4);
            return b.Contains(p);
        }

        public Annotation Clone() => (Annotation)MemberwiseClone();

        protected Brush MakeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        protected Pen MakePen(Color c, double thickness)
        {
            var p = new Pen(MakeBrush(c), thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze();
            return p;
        }

        protected static Rect RectFrom(Point a, Point b, bool square)
        {
            if (square)
            {
                double size = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                b = new Point(a.X + Math.Sign(b.X - a.X) * size, a.Y + Math.Sign(b.Y - a.Y) * size);
            }
            return new Rect(a, b);
        }
    }

    public abstract class BoxAnnotation : Annotation
    {
        public Rect Rect { get; set; }
        public override Rect Bounds => Rect;

        public override void Move(Vector delta) => Rect = Rect.Offset(Rect, delta);

        public override void Drag(Point start, Point current, bool constrain) => Rect = RectFrom(start, current, constrain);
    }

    public sealed class RectangleAnnotation : BoxAnnotation
    {
        public override void Render(DrawingContext dc, EditorDocument doc) =>
            dc.DrawRoundedRectangle(null, MakePen(Color, Thickness), Rect, 2, 2);

        public override bool HitTest(Point p)
        {
            if (!base.HitTest(p)) return false;
            var inner = Rect;
            double d = Thickness / 2 + 6;
            if (inner.Width <= 2 * d || inner.Height <= 2 * d) return true;
            inner.Inflate(-d, -d);
            return !inner.Contains(p); // si seleziona cliccando sul bordo
        }
    }

    public sealed class EllipseAnnotation : BoxAnnotation
    {
        public override void Render(DrawingContext dc, EditorDocument doc)
        {
            var c = new Point(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2);
            dc.DrawEllipse(null, MakePen(Color, Thickness), c, Rect.Width / 2, Rect.Height / 2);
        }
    }

    public sealed class HighlightAnnotation : BoxAnnotation
    {
        public override int Layer => 1;

        public override void Render(DrawingContext dc, EditorDocument doc) =>
            dc.DrawRectangle(MakeBrush(Color.FromArgb(0x66, Color.R, Color.G, Color.B)), null, Rect);
    }

    /// <summary>Oscura tutto tranne il rettangolo, per mettere in risalto una zona.</summary>
    public sealed class SpotlightAnnotation : BoxAnnotation
    {
        public override int Layer => 1;

        public override void Render(DrawingContext dc, EditorDocument doc)
        {
            var full = new RectangleGeometry(new Rect(0, 0, doc.Width, doc.Height));
            var hole = new RectangleGeometry(Rect, 6, 6);
            var geo = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
            dc.DrawGeometry(MakeBrush(Color.FromArgb(0x99, 0, 0, 0)), null, geo);
        }

        public override bool HitTest(Point p) => Rect.Contains(p);
    }

    /// <summary>Pixela una zona per nascondere dati sensibili.</summary>
    public sealed class BlurAnnotation : BoxAnnotation
    {
        private BitmapSource? _cache;
        private Rect _cacheRect;
        private int _cacheVersion;

        public override int Layer => 0;

        public override void Render(DrawingContext dc, EditorDocument doc)
        {
            var r = new Int32Rect((int)Math.Round(Rect.X), (int)Math.Round(Rect.Y),
                                  (int)Math.Round(Rect.Width), (int)Math.Round(Rect.Height));
            var clipped = doc.ClipToImage(r);
            if (clipped.IsEmpty) return;

            var target = new Rect(clipped.X, clipped.Y, clipped.Width, clipped.Height);
            if (_cache == null || _cacheRect != target || _cacheVersion != doc.Version)
            {
                int block = Math.Max(6, (int)(Thickness * 2.5));
                _cache = doc.Pixelate(clipped, block);
                _cacheRect = target;
                _cacheVersion = doc.Version;
            }
            if (_cache != null)
                dc.DrawImage(_cache, target);
        }

        public override bool HitTest(Point p) => Rect.Contains(p);
    }

    public sealed class ArrowAnnotation : Annotation
    {
        public Point Start { get; set; }
        public Point End { get; set; }

        public override Rect Bounds => new(Start, End);
        public override bool IsValid => (End - Start).Length >= 6;

        public override void Move(Vector delta)
        {
            Start += delta;
            End += delta;
        }

        public override void Drag(Point start, Point current, bool constrain)
        {
            Start = start;
            if (constrain)
            {
                // blocca a multipli di 45°
                var v = current - start;
                double angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
                current = start + new Vector(Math.Cos(angle), Math.Sin(angle)) * v.Length;
            }
            End = current;
        }

        public override void Render(DrawingContext dc, EditorDocument doc)
        {
            var v = End - Start;
            double len = v.Length;
            if (len < 1) return;
            var dir = v / len;
            var normal = new Vector(-dir.Y, dir.X);

            double head = Math.Min(Math.Max(12, Thickness * 4.2), len * 0.7);
            double halfWidth = head * 0.55;
            var basePoint = End - dir * head;

            dc.DrawLine(MakePen(Color, Thickness), Start, basePoint + dir * (head * 0.3));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(End, true, true);
                ctx.LineTo(basePoint + normal * halfWidth, true, true);
                ctx.LineTo(basePoint + dir * (head * 0.15), true, true);
                ctx.LineTo(basePoint - normal * halfWidth, true, true);
            }
            geo.Freeze();
            var stroke = new Pen(MakeBrush(Color), 1) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(MakeBrush(Color), stroke, geo);
        }

        public override bool HitTest(Point p)
        {
            var v = End - Start;
            double lenSq = v.LengthSquared;
            double t = lenSq == 0 ? 0 : Math.Clamp(Vector.Multiply(p - Start, v) / lenSq, 0, 1);
            var closest = Start + v * t;
            return (p - closest).Length <= Thickness / 2 + 6;
        }
    }

    public sealed class TextAnnotation : Annotation
    {
        public Point Position { get; set; }
        public string Text { get; set; } = "";
        public double FontSize { get; set; } = 24;

        public static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private FormattedText Format() =>
            new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize, MakeBrush(Color), 1.0);

        public override Rect Bounds
        {
            get
            {
                var ft = Format();
                return new Rect(Position, new Size(Math.Max(ft.WidthIncludingTrailingWhitespace, 4), ft.Height));
            }
        }

        public override bool IsValid => !string.IsNullOrWhiteSpace(Text);

        public override void Move(Vector delta) => Position += delta;

        public override void Render(DrawingContext dc, EditorDocument doc)
        {
            var geo = Format().BuildGeometry(Position);
            // contorno di contrasto per leggere il testo su qualunque sfondo
            double lum = (0.299 * Color.R + 0.587 * Color.G + 0.114 * Color.B) / 255;
            var outline = lum < 0.6 ? Colors.White : Color.FromRgb(0x22, 0x22, 0x22);
            var pen = new Pen(MakeBrush(outline), Math.Max(2, FontSize / 7)) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, geo);
            dc.DrawGeometry(MakeBrush(Color), null, geo);
        }
    }

    /// <summary>Cerchio numerato per indicare una sequenza di passaggi.</summary>
    public sealed class StepAnnotation : Annotation
    {
        public Point Center { get; set; }
        public int Number { get; set; } = 1;
        public double Radius => 11 + Thickness * 1.8;

        public override Rect Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
        public override bool IsValid => true;

        public override void Move(Vector delta) => Center += delta;

        public override void Drag(Point start, Point current, bool constrain) => Center = current;

        public override void Render(DrawingContext dc, EditorDocument doc)
        {
            dc.DrawEllipse(MakeBrush(Color), MakePen(Colors.White, Math.Max(2, Radius / 7)), Center, Radius, Radius);
            var lum = (0.299 * Color.R + 0.587 * Color.G + 0.114 * Color.B) / 255;
            var ft = new FormattedText(Number.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                Radius * 1.15, lum > 0.7 ? Brushes.Black : Brushes.White, 1.0);
            dc.DrawText(ft, new Point(Center.X - ft.Width / 2, Center.Y - ft.Height / 2));
        }

        public override bool HitTest(Point p) => (p - Center).Length <= Radius + 3;
    }
}
