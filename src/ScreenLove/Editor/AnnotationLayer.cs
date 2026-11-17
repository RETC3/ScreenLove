using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenLove.Editor
{
    /// <summary>Superficie che disegna le annotazioni e gestisce mouse e strumenti.</summary>
    public sealed class AnnotationLayer : FrameworkElement
    {
        private static readonly Color Accent = Color.FromRgb(0xFF, 0x3D, 0x7F);

        private EditorDocument? _doc;
        private Annotation? _creating;
        private Point _start;
        private Point _last;
        private bool _down;
        private bool _snapshotTaken;
        private Rect? _cropRect;

        // Ritaglio: cornice con maniglie (angoli e lati) da trascinare, poi Applica / Invio / doppio clic.
        private enum CropDrag { None, New, Move, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
        private CropDrag _cropDrag;
        private Rect _cropStart;
        private const double MinCrop = 8;

        // Maniglie dell'elemento selezionato: indice della maniglia trascinata e forma di partenza.
        private int _handle = -1;
        private Annotation? _handleOriginal;

        private int HandleHit(Point p)
        {
            if (Selected == null || _doc == null || !_doc.Items.Contains(Selected)) return -1;
            var handles = Selected.Handles;
            double tol = 8 / ViewScale();
            for (int i = handles.Length - 1; i >= 0; i--) // la punta della freccia ha la precedenza sulla coda
                if (Math.Abs(p.X - handles[i].X) <= tol && Math.Abs(p.Y - handles[i].Y) <= tol)
                    return i;
            return -1;
        }

        private Cursor HandleCursor(int index) => Selected is BoxAnnotation
            ? index switch
            {
                0 or 4 => Cursors.SizeNWSE,
                2 or 6 => Cursors.SizeNESW,
                1 or 5 => Cursors.SizeNS,
                _ => Cursors.SizeWE,
            }
            : Cursors.SizeAll;

        private void RenderHandles(DrawingContext dc, Point[] handles)
        {
            double k = 1 / ViewScale(), size = 10 * k;
            var fill = Brushes.White;
            var pen = new Pen(new SolidColorBrush(Accent), 1.5 * k);
            foreach (var h in handles)
                dc.DrawRectangle(fill, pen, new Rect(h.X - size / 2, h.Y - size / 2, size, size));
        }

        public AnnotationLayer()
        {
            Focusable = true;
            FocusVisualStyle = null;
        }

        public EditorDocument? Document
        {
            get => _doc;
            set
            {
                _doc = value;
                Selected = null;
                SyncSize();
            }
        }

        public ToolKind Tool { get; set; } = ToolKind.Select;
        public Color CurrentColor { get; set; } = Colors.Red;
        public double CurrentThickness { get; set; } = 4;
        public double TextSize => 12 + CurrentThickness * 3;

        public Annotation? Selected { get; set; }

        /// <summary>Chiamato prima di ogni modifica: l'editor salva lo stato per Annulla.</summary>
        public event Action? BeforeChange;

        public event Action? Changed;
        public event Action<Point>? TextRequested;
        public event Action<Int32Rect>? CropRequested;

        /// <summary>Il ritaglio è iniziato o finito (per mostrare la barra Applica / Annulla).</summary>
        public event Action? CropStateChanged;

        public bool IsCropping => _cropRect != null;

        /// <summary>Mostra la cornice di ritaglio sull'intera immagine, con le maniglie.</summary>
        public void BeginCrop()
        {
            if (_doc == null) return;
            _cropRect = new Rect(0, 0, _doc.Width, _doc.Height);
            _cropDrag = CropDrag.None;
            InvalidateVisual();
            CropStateChanged?.Invoke();
        }

        /// <summary>Applica la cornice di ritaglio corrente.</summary>
        public void ApplyCrop()
        {
            if (_cropRect is not { } crop) return;
            _cropRect = null;
            _cropDrag = CropDrag.None;
            if (_down)
            {
                _down = false;
                ReleaseMouseCapture();
            }
            InvalidateVisual();
            CropStateChanged?.Invoke();
            var r = new Int32Rect((int)Math.Round(crop.X), (int)Math.Round(crop.Y),
                                  (int)Math.Round(crop.Width), (int)Math.Round(crop.Height));
            if (r.Width >= 4 && r.Height >= 4)
                CropRequested?.Invoke(r);
        }

        /// <summary>Quanti pixel di schermo vale un pixel dell'immagine (zoom), per maniglie di dimensione fissa.</summary>
        private double ViewScale()
        {
            try
            {
                if (PresentationSource.FromVisual(this) is { RootVisual: Visual root })
                {
                    double s = TransformToAncestor(root).TransformBounds(new Rect(0, 0, 100, 100)).Width / 100;
                    if (s > 0.01) return s;
                }
            }
            catch (InvalidOperationException)
            {
                // non ancora nella finestra
            }
            return 1;
        }

        private CropDrag CropHit(Point p)
        {
            if (_cropRect is not { } r) return CropDrag.New;
            double tol = 12 / ViewScale();
            bool nearL = Math.Abs(p.X - r.Left) <= tol, nearR = Math.Abs(p.X - r.Right) <= tol;
            bool nearT = Math.Abs(p.Y - r.Top) <= tol, nearB = Math.Abs(p.Y - r.Bottom) <= tol;
            bool inX = p.X >= r.Left - tol && p.X <= r.Right + tol, inY = p.Y >= r.Top - tol && p.Y <= r.Bottom + tol;
            if (nearL && nearT) return CropDrag.TopLeft;
            if (nearR && nearT) return CropDrag.TopRight;
            if (nearL && nearB) return CropDrag.BottomLeft;
            if (nearR && nearB) return CropDrag.BottomRight;
            if (nearL && inY) return CropDrag.Left;
            if (nearR && inY) return CropDrag.Right;
            if (nearT && inX) return CropDrag.Top;
            if (nearB && inX) return CropDrag.Bottom;
            return r.Contains(p) ? CropDrag.Move : CropDrag.New;
        }

        private static Cursor CropCursor(CropDrag d) => d switch
        {
            CropDrag.TopLeft or CropDrag.BottomRight => Cursors.SizeNWSE,
            CropDrag.TopRight or CropDrag.BottomLeft => Cursors.SizeNESW,
            CropDrag.Left or CropDrag.Right => Cursors.SizeWE,
            CropDrag.Top or CropDrag.Bottom => Cursors.SizeNS,
            CropDrag.Move => Cursors.SizeAll,
            _ => Cursors.Cross,
        };

        private void DragCrop(Point p)
        {
            if (_doc == null) return;
            double w = _doc.Width, h = _doc.Height;
            p = new Point(Math.Clamp(p.X, 0, w), Math.Clamp(p.Y, 0, h));
            var r = _cropStart;
            double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;

            switch (_cropDrag)
            {
                case CropDrag.New:
                    var start = new Point(Math.Clamp(_start.X, 0, w), Math.Clamp(_start.Y, 0, h));
                    _cropRect = new Rect(start, p);
                    return;
                case CropDrag.Move:
                    double dx = Math.Clamp(p.X - _start.X, -r.Left, w - r.Right);
                    double dy = Math.Clamp(p.Y - _start.Y, -r.Top, h - r.Bottom);
                    _cropRect = new Rect(r.X + dx, r.Y + dy, r.Width, r.Height);
                    return;
            }

            if (_cropDrag is CropDrag.Left or CropDrag.TopLeft or CropDrag.BottomLeft) left = Math.Min(p.X, right - MinCrop);
            if (_cropDrag is CropDrag.Right or CropDrag.TopRight or CropDrag.BottomRight) right = Math.Max(p.X, left + MinCrop);
            if (_cropDrag is CropDrag.Top or CropDrag.TopLeft or CropDrag.TopRight) top = Math.Min(p.Y, bottom - MinCrop);
            if (_cropDrag is CropDrag.Bottom or CropDrag.BottomLeft or CropDrag.BottomRight) bottom = Math.Max(p.Y, top + MinCrop);
            _cropRect = new Rect(new Point(left, top), new Point(right, bottom));
        }

        private void RenderCrop(DrawingContext dc, Rect full, Rect crop)
        {
            double k = 1 / ViewScale(); // 1 pixel di schermo in coordinate immagine
            var geo = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(crop));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, geo);

            // Griglia dei terzi, come nel ritaglio delle foto sullo smartphone.
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1 * k);
            for (int i = 1; i <= 2; i++)
            {
                double x = crop.X + crop.Width * i / 3, y = crop.Y + crop.Height * i / 3;
                dc.DrawLine(grid, new Point(x, crop.Top), new Point(x, crop.Bottom));
                dc.DrawLine(grid, new Point(crop.Left, y), new Point(crop.Right, y));
            }
            dc.DrawRectangle(null, new Pen(Brushes.White, 1.5 * k), crop);

            // Maniglie: angoli a "L" e barrette a metà dei lati, appena dentro la cornice
            // (così restano visibili anche quando la cornice coincide con il bordo dell'immagine).
            var handle = new Pen(Brushes.White, 5 * k) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
            double len = Math.Min(22 * k, Math.Min(crop.Width, crop.Height) / 2);
            double o = 2.5 * k;
            double l = crop.Left + o, t = crop.Top + o, r = crop.Right - o, b = crop.Bottom - o;
            dc.DrawLine(handle, new Point(l, t), new Point(l + len, t)); dc.DrawLine(handle, new Point(l, t), new Point(l, t + len));
            dc.DrawLine(handle, new Point(r, t), new Point(r - len, t)); dc.DrawLine(handle, new Point(r, t), new Point(r, t + len));
            dc.DrawLine(handle, new Point(l, b), new Point(l + len, b)); dc.DrawLine(handle, new Point(l, b), new Point(l, b - len));
            dc.DrawLine(handle, new Point(r, b), new Point(r - len, b)); dc.DrawLine(handle, new Point(r, b), new Point(r, b - len));
            double cx = crop.X + crop.Width / 2, cy = crop.Y + crop.Height / 2, half = len / 2;
            if (crop.Width > 3 * len)
            {
                dc.DrawLine(handle, new Point(cx - half, t), new Point(cx + half, t));
                dc.DrawLine(handle, new Point(cx - half, b), new Point(cx + half, b));
            }
            if (crop.Height > 3 * len)
            {
                dc.DrawLine(handle, new Point(l, cy - half), new Point(l, cy + half));
                dc.DrawLine(handle, new Point(r, cy - half), new Point(r, cy + half));
            }

            // Dimensione del ritaglio.
            var text = new FormattedText($"{Math.Round(crop.Width)} × {Math.Round(crop.Height)}",
                System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"),
                12 * k, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var box = new Rect(crop.X + 12 * k, crop.Y + 12 * k, text.Width + 12 * k, text.Height + 6 * k);
            if (box.Right < crop.Right && box.Bottom < crop.Bottom)
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(200, 30, 31, 36)), null, box, 4 * k, 4 * k);
                dc.DrawText(text, new Point(box.X + 6 * k, box.Y + 3 * k));
            }
        }

        public void SyncSize()
        {
            if (_doc == null) return;
            Width = _doc.Width;
            Height = _doc.Height;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_doc == null) return;
            var full = new Rect(0, 0, _doc.Width, _doc.Height);
            dc.DrawRectangle(Brushes.Transparent, null, full); // rende cliccabile tutta la superficie

            dc.PushClip(new RectangleGeometry(full));
            _doc.RenderAnnotations(dc);
            _creating?.Render(dc, _doc);
            dc.Pop();

            if (Selected != null && _doc.Items.Contains(Selected) && _cropRect == null && Selected.Handles is { Length: > 0 } handles)
            {
                RenderHandles(dc, handles);
            }
            else if (Selected != null && _doc.Items.Contains(Selected))
            {
                var b = Selected.Bounds;
                b.Inflate(Selected.Thickness / 2 + 4, Selected.Thickness / 2 + 4);
                var pen = new Pen(new SolidColorBrush(Accent), 1.5) { DashStyle = DashStyles.Dash };
                dc.DrawRectangle(null, pen, b);
            }

            if (_cropRect is { } crop)
                RenderCrop(dc, full, crop);
        }

        private Annotation? HitTest(Point p) =>
            _doc?.Items.OrderBy(a => a.Layer).Reverse().FirstOrDefault(a => a.HitTest(p));

        private Annotation? CreateFor(ToolKind tool) => tool switch
        {
            ToolKind.Arrow => new ArrowAnnotation(),
            ToolKind.Rectangle => new RectangleAnnotation(),
            ToolKind.Ellipse => new EllipseAnnotation(),
            ToolKind.Highlight => new HighlightAnnotation(),
            ToolKind.Blur => new BlurAnnotation(),
            ToolKind.Spotlight => new SpotlightAnnotation(),
            _ => null,
        };

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (_doc == null) return;
            Focus();
            var p = e.GetPosition(this);
            _start = _last = p;
            _snapshotTaken = false;

            // Maniglia dell'elemento selezionato: si ridimensiona, con qualunque strumento (tranne il ritaglio).
            if (Tool != ToolKind.Crop && HandleHit(p) is >= 0 and var h)
            {
                _handle = h;
                _handleOriginal = Selected!.Clone();
                _down = true;
                CaptureMouse();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            switch (Tool)
            {
                case ToolKind.Select:
                    Selected = HitTest(p);
                    _down = Selected != null;
                    break;

                case ToolKind.Text:
                    if (HitTest(p) is TextAnnotation existing)
                    {
                        Selected = existing;
                        _down = true; // trascina il testo esistente
                    }
                    else
                    {
                        TextRequested?.Invoke(p);
                        return;
                    }
                    break;

                case ToolKind.Step:
                    BeforeChange?.Invoke();
                    int next = _doc.Items.OfType<StepAnnotation>().Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;
                    var step = new StepAnnotation { Center = p, Number = next, Color = CurrentColor, Thickness = CurrentThickness };
                    _doc.Items.Add(step);
                    Selected = step;
                    _down = false;
                    Changed?.Invoke();
                    break;

                case ToolKind.Crop:
                    if (e.ClickCount == 2 && _cropRect is { } current && current.Contains(p))
                    {
                        ApplyCrop(); // doppio clic dentro la cornice = applica
                        e.Handled = true;
                        return;
                    }
                    bool wasCropping = _cropRect != null;
                    _cropDrag = CropHit(p);
                    _cropStart = _cropRect ?? Rect.Empty; // cornice di partenza (anche per tornarci se si clicca e basta)
                    if (_cropDrag == CropDrag.New)
                        _cropRect = new Rect(p, p); // fuori dalla cornice: se ne disegna una nuova
                    if (!wasCropping) CropStateChanged?.Invoke();
                    _down = true;
                    break;

                default:
                    _creating = CreateFor(Tool);
                    if (_creating != null)
                    {
                        _creating.Color = CurrentColor;
                        _creating.Thickness = CurrentThickness;
                        _creating.Drag(p, p, false);
                        _down = true;
                    }
                    break;
            }

            if (_down) CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (!_down)
            {
                if (Tool != ToolKind.Crop && HandleHit(p) is >= 0 and var h)
                {
                    Cursor = HandleCursor(h);
                    return;
                }
                Cursor = Tool switch
                {
                    ToolKind.Crop => CropCursor(CropHit(p)),
                    ToolKind.Select => HitTest(p) != null ? Cursors.SizeAll : Cursors.Arrow,
                    ToolKind.Text => HitTest(p) is TextAnnotation ? Cursors.SizeAll : Cursors.IBeam,
                    _ => Cursors.Cross,
                };
                return;
            }

            if (_handle >= 0 && Selected != null && _handleOriginal != null)
            {
                if (!_snapshotTaken && (p - _start).Length > 0)
                {
                    BeforeChange?.Invoke();
                    _snapshotTaken = true;
                }
                Selected.DragHandle(_handle, p, shift, _handleOriginal);
            }
            else if (_creating != null)
            {
                _creating.Drag(_start, p, shift);
            }
            else if (_cropRect != null)
            {
                DragCrop(p);
            }
            else if (Selected != null)
            {
                var delta = p - _last;
                if (delta.Length > 0)
                {
                    if (!_snapshotTaken)
                    {
                        BeforeChange?.Invoke();
                        _snapshotTaken = true;
                    }
                    Selected.Move(delta);
                }
            }

            _last = p;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!_down) return;
            _down = false;
            ReleaseMouseCapture();

            if (_handle >= 0)
            {
                // Forma troppo piccola dopo il ridimensionamento: torna com'era.
                if (Selected != null && _handleOriginal != null && !Selected.IsValid && _doc != null)
                {
                    int i = _doc.Items.IndexOf(Selected);
                    if (i >= 0)
                    {
                        _doc.Items[i] = _handleOriginal;
                        Selected = _handleOriginal;
                    }
                }
                _handle = -1;
                _handleOriginal = null;
            }
            else if (_creating != null)
            {
                if (_creating.IsValid && _doc != null)
                {
                    BeforeChange?.Invoke();
                    _doc.Items.Add(_creating);
                    Selected = _creating;
                }
                _creating = null;
            }
            else if (_cropRect is { } crop)
            {
                // La cornice resta: si regola con le maniglie e si applica con Applica, Invio o doppio clic.
                _cropDrag = CropDrag.None;
                if (crop.Width < MinCrop || crop.Height < MinCrop)
                {
                    // Un semplice clic fuori dalla cornice non la cancella.
                    bool startOk = !_cropStart.IsEmpty && _cropStart.Width >= MinCrop && _cropStart.Height >= MinCrop;
                    _cropRect = startOk ? _cropStart : _doc != null ? new Rect(0, 0, _doc.Width, _doc.Height) : null;
                }
            }

            InvalidateVisual();
            Changed?.Invoke();
        }

        public void CancelInteraction()
        {
            _creating = null;
            _handle = -1;
            _handleOriginal = null;
            bool wasCropping = _cropRect != null;
            _cropRect = null;
            _cropDrag = CropDrag.None;
            if (wasCropping) CropStateChanged?.Invoke();
            _down = false;
            ReleaseMouseCapture();
            InvalidateVisual();
        }
    }
}
