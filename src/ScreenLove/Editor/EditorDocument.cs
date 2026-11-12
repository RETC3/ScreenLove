using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLove.Editor
{
    /// <summary>
    /// Immagine di base (BGRA32, 96 DPI: 1 pixel = 1 unità WPF) più le annotazioni vettoriali sopra.
    /// </summary>
    public sealed class EditorDocument
    {
        public EditorDocument(BitmapSource normalizedBase)
        {
            SetBase(normalizedBase);
        }

        public BitmapSource Base { get; private set; } = null!;
        public byte[] Pixels { get; private set; } = Array.Empty<byte>();
        public int Stride { get; private set; }
        public int Width => Base.PixelWidth;
        public int Height => Base.PixelHeight;

        /// <summary>Cambia a ogni modifica dell'immagine di base: invalida le cache (es. sfocature).</summary>
        public int Version { get; private set; }

        public List<Annotation> Items { get; set; } = new();

        public void SetBase(BitmapSource normalized)
        {
            Base = normalized;
            Stride = normalized.PixelWidth * 4;
            Pixels = new byte[Stride * normalized.PixelHeight];
            normalized.CopyPixels(Pixels, Stride, 0);
            Version++;
        }

        public Int32Rect ClipToImage(Int32Rect r)
        {
            int x1 = Math.Max(0, r.X), y1 = Math.Max(0, r.Y);
            int x2 = Math.Min(Width, r.X + r.Width), y2 = Math.Min(Height, r.Y + r.Height);
            return x2 > x1 && y2 > y1 ? new Int32Rect(x1, y1, x2 - x1, y2 - y1) : Int32Rect.Empty;
        }

        public BitmapSource Crop(Int32Rect r)
        {
            r = ClipToImage(r);
            int stride = r.Width * 4;
            var outPx = new byte[stride * r.Height];
            for (int y = 0; y < r.Height; y++)
                Buffer.BlockCopy(Pixels, (r.Y + y) * Stride + r.X * 4, outPx, y * stride, stride);
            var bmp = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, outPx, stride);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Versione a blocchi (pixelata) di una zona dell'immagine di base.</summary>
        public BitmapSource? Pixelate(Int32Rect area, int block)
        {
            var r = ClipToImage(area);
            if (r.IsEmpty) return null;
            int stride = r.Width * 4;
            var outPx = new byte[stride * r.Height];

            for (int by = 0; by < r.Height; by += block)
            {
                for (int bx = 0; bx < r.Width; bx += block)
                {
                    int bw = Math.Min(block, r.Width - bx), bh = Math.Min(block, r.Height - by);
                    long sb = 0, sg = 0, sr = 0;
                    for (int y = 0; y < bh; y++)
                    {
                        int i = (r.Y + by + y) * Stride + (r.X + bx) * 4;
                        for (int x = 0; x < bw; x++, i += 4)
                        {
                            sb += Pixels[i];
                            sg += Pixels[i + 1];
                            sr += Pixels[i + 2];
                        }
                    }
                    int n = bw * bh;
                    byte b = (byte)(sb / n), g = (byte)(sg / n), rr = (byte)(sr / n);
                    for (int y = 0; y < bh; y++)
                    {
                        int o = (by + y) * stride + bx * 4;
                        for (int x = 0; x < bw; x++, o += 4)
                        {
                            outPx[o] = b;
                            outPx[o + 1] = g;
                            outPx[o + 2] = rr;
                            outPx[o + 3] = 255;
                        }
                    }
                }
            }

            var bmp = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, outPx, stride);
            bmp.Freeze();
            return bmp;
        }

        public void RenderAnnotations(DrawingContext dc)
        {
            foreach (var a in Items.OrderBy(a => a.Layer))
                a.Render(dc, this);
        }

        /// <summary>Immagine finale: base + annotazioni, alla risoluzione originale.</summary>
        public BitmapSource Flatten()
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(Base, new Rect(0, 0, Width, Height));
                RenderAnnotations(dc);
            }
            var rtb = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        public Snapshot TakeSnapshot() => new(Base, Items.Select(i => i.Clone()).ToList());

        public void Restore(Snapshot s)
        {
            if (!ReferenceEquals(s.Base, Base))
                SetBase(s.Base);
            Items = s.Items.Select(i => i.Clone()).ToList();
        }

        public sealed record Snapshot(BitmapSource Base, List<Annotation> Items);
    }
}
