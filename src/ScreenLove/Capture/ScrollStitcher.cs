using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenLove.Capture
{
    /// <summary>Un fotogramma catturato con l'impronta (hash) di ogni riga.</summary>
    public sealed class ScrollFrame
    {
        public ScrollFrame(byte[] pixels, int width, int height)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            RowHash = new ulong[height];
            Uniform = new bool[height];

            // La colonna a destra contiene di solito la barra di scorrimento, che si muove: la ignoriamo nel confronto.
            int compareWidth = Math.Max(1, width - Math.Min(24, width / 10));
            Bins = Math.Min(64, compareWidth);
            Profile = new byte[height * Bins];
            Flat = new bool[height];
            var px = MemoryMarshal.Cast<byte, uint>(pixels);
            var sums = new int[Bins];
            var counts = new int[Bins];
            for (int y = 0; y < height; y++)
            {
                var row = px.Slice(y * width, compareWidth);
                ulong hash = 14695981039346656037UL;
                bool uniform = true;
                uint first = row[0];
                Array.Clear(sums);
                Array.Clear(counts);
                for (int x = 0; x < row.Length; x++)
                {
                    uint p = row[x];
                    hash = (hash ^ p) * 1099511628211UL;
                    if (p != first) uniform = false;
                    int lum = ((int)((p >> 16) & 0xFF) * 77 + (int)((p >> 8) & 0xFF) * 150 + (int)(p & 0xFF) * 29) >> 8;
                    int bin = x * Bins / row.Length;
                    sums[bin] += lum;
                    counts[bin]++;
                }
                RowHash[y] = hash;
                Uniform[y] = uniform;

                int min = 255, max = 0;
                for (int b = 0; b < Bins; b++)
                {
                    int v = counts[b] > 0 ? sums[b] / counts[b] : 0;
                    Profile[y * Bins + b] = (byte)v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                Flat[y] = max - min < 3;
            }
        }

        /// <summary>Numero di colonne del profilo di luminosità di ogni riga.</summary>
        public int Bins { get; }

        /// <summary>
        /// Luminosità media per gruppi di colonne: tollera i piccoli cambi di antialiasing
        /// dei browser che scorrono di frazioni di pixel.
        /// </summary>
        public byte[] Profile { get; }

        /// <summary>Riga quasi tinta unita: poco utile per capire lo spostamento.</summary>
        public bool[] Flat { get; }

        public int RowDiff(int y, ScrollFrame other, int otherY)
        {
            int a = y * Bins, b = otherY * Bins, sum = 0;
            for (int i = 0; i < Bins; i++)
                sum += Math.Abs(Profile[a + i] - other.Profile[b + i]);
            return sum;
        }

        public byte[] Pixels { get; }
        public int Width { get; }
        public int Height { get; }
        public int Stride => Width * 4;
        public ulong[] RowHash { get; }

        /// <summary>Riga di un solo colore: non dice nulla sulla posizione del contenuto.</summary>
        public bool[] Uniform { get; }

        public bool SameAs(ScrollFrame other)
        {
            if (other.Height != Height) return false;
            for (int y = 0; y < Height; y++)
                if (RowHash[y] != other.RowHash[y]) return false;
            return true;
        }
    }

    /// <summary>Risultato del confronto tra due fotogrammi consecutivi.</summary>
    public readonly record struct ScrollMatch(int Offset, int Header, int Footer);

    /// <summary>
    /// Trova di quanto è scorso il contenuto tra due fotogrammi e accumula le righe nuove
    /// in un'unica immagine alta. Gestisce intestazioni e piè di pagina fissi (righe che non si muovono).
    /// </summary>
    public sealed class ScrollStitcher
    {
        private readonly List<byte[]> _chunks = new();
        private ScrollFrame? _pending;   // primo fotogramma, in attesa di conoscere il piè di pagina
        private ScrollFrame? _last;
        private int _footer;
        private bool _footerKnown;

        public int Width { get; private set; }
        public int TotalHeight { get; private set; }
        public int Frames { get; private set; }

        /// <summary>
        /// Confronta B con A. Offset = 0 se il contenuto non si è mosso, null se non si trova un allineamento affidabile.
        /// </summary>
        public static ScrollMatch? FindOffset(ScrollFrame a, ScrollFrame b, int expected)
        {
            int h = a.Height;
            if (b.Height != h || b.Width != a.Width) return null;
            int maxStatic = h * 4 / 10;

            int header = StaticRun(a, b, 0, 1, maxStatic);
            int footer = StaticRun(a, b, h - 1, -1, maxStatic);

            int lo = header, hi = h - footer, n = hi - lo;
            if (n < 16 || a.SameAs(b))
                return new ScrollMatch(0, header, footer);

            // Errore medio (luminosità, 0-255) per ogni spostamento candidato: riga i di B ≈ riga i+dy di A.
            int bins = a.Bins;
            var error = new double[n];
            double minError = double.MaxValue;
            for (int dy = 1; dy <= n - 8; dy++)
            {
                long sum = 0;
                int rows = 0;
                for (int i = lo; i < hi - dy; i++)
                {
                    int d = b.RowDiff(i, a, i + dy);
                    if (d < bins * 2 && b.Flat[i] && a.Flat[i + dy])
                        continue; // righe vuote uguali: non dicono nulla
                    sum += d;
                    rows++;
                }
                error[dy] = rows < 6 ? double.MaxValue : (double)sum / rows / bins;
                if (error[dy] < minError) minError = error[dy];
            }

            if (minError > 6)
                return null; // nessun allineamento credibile

            // Contenuti ripetitivi possono avere più minimi quasi uguali: si preferisce lo spostamento atteso.
            int bestDy = -1;
            double tolerance = minError + 0.75;
            for (int dy = 1; dy <= n - 8; dy++)
            {
                if (error[dy] > tolerance) continue;
                if (bestDy < 0 ||
                    (expected > 0 ? Math.Abs(dy - expected) < Math.Abs(bestDy - expected) : error[dy] < error[bestDy]))
                    bestDy = dy;
            }

            return bestDy < 0 ? null : new ScrollMatch(bestDy, header, footer);
        }

        /// <summary>Righe consecutive identiche nella stessa posizione (zona fissa), escluse quelle tutte vuote.</summary>
        private static int StaticRun(ScrollFrame a, ScrollFrame b, int start, int step, int max)
        {
            int count = 0;
            bool informative = false;
            for (int y = start; count < max && y >= 0 && y < a.Height; y += step)
            {
                if (a.RowHash[y] != b.RowHash[y]) break;
                if (!a.Uniform[y]) informative = true;
                count++;
            }
            return informative ? count : 0;
        }

        public void AddFirst(ScrollFrame frame)
        {
            Width = frame.Width;
            _pending = frame;
            _last = frame;
            Frames = 1;
            TotalHeight = frame.Height;
        }

        /// <summary>Aggiunge un nuovo fotogramma già confrontato con l'ultimo (match.Offset > 0).</summary>
        public void Append(ScrollFrame frame, ScrollMatch match)
        {
            if (!_footerKnown)
            {
                _footer = match.Footer;
                _footerKnown = true;
            }
            if (_pending != null)
            {
                AddRows(_pending, 0, _pending.Height - _footer);
                _pending = null;
            }

            int end = frame.Height - _footer;
            int start = Math.Max(0, end - match.Offset);
            AddRows(frame, start, end);
            _last = frame;
            Frames++;
            TotalHeight = CurrentHeight + _footer;
        }

        private int CurrentHeight
        {
            get
            {
                int total = 0;
                foreach (var c in _chunks) total += c.Length / (Width * 4);
                return total;
            }
        }

        private void AddRows(ScrollFrame f, int start, int end)
        {
            if (end <= start) return;
            var chunk = new byte[(end - start) * f.Stride];
            Buffer.BlockCopy(f.Pixels, start * f.Stride, chunk, 0, chunk.Length);
            _chunks.Add(chunk);
        }

        public BitmapSource Build()
        {
            if (_last == null) throw new InvalidOperationException("Nessun fotogramma.");
            if (_pending != null)
            {
                // un solo fotogramma: niente da cucire
                return Create(_pending.Pixels, _pending.Width, _pending.Height);
            }

            AddRows(_last, _last.Height - _footer, _last.Height);
            int stride = Width * 4;
            int height = CurrentHeight;
            var all = new byte[stride * height];
            int offset = 0;
            foreach (var c in _chunks)
            {
                Buffer.BlockCopy(c, 0, all, offset, c.Length);
                offset += c.Length;
            }
            _chunks.Clear();
            _chunks.Add(all);
            _pending = null;
            _footer = 0; // il piè di pagina è già incluso: Build() è ripetibile
            return Create(all, Width, height);
        }

        private static BitmapSource Create(byte[] pixels, int w, int h)
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            bmp.Freeze();
            return bmp;
        }
    }
}
