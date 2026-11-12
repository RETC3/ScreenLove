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

            if (Selected != null && _doc.Items.Contains(Selected))
            {
                var b = Selected.Bounds;
                b.Inflate(Selected.Thickness / 2 + 4, Selected.Thickness / 2 + 4);
                var pen = new Pen(new SolidColorBrush(Accent), 1.5) { DashStyle = DashStyles.Dash };
                dc.DrawRectangle(null, pen, b);
            }

            if (_cropRect is { } crop)
            {
                var geo = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(crop));
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, geo);
                dc.DrawRectangle(null, new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash }, crop);
            }
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
                    _cropRect = new Rect(p, p);
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
                Cursor = Tool switch
                {
                    ToolKind.Select => HitTest(p) != null ? Cursors.SizeAll : Cursors.Arrow,
                    ToolKind.Text => HitTest(p) is TextAnnotation ? Cursors.SizeAll : Cursors.IBeam,
                    _ => Cursors.Cross,
                };
                return;
            }

            if (_creating != null)
            {
                _creating.Drag(_start, p, shift);
            }
            else if (_cropRect != null)
            {
                _cropRect = new Rect(_start, p);
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

            if (_creating != null)
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
                _cropRect = null;
                var r = new Int32Rect((int)Math.Round(crop.X), (int)Math.Round(crop.Y),
                                      (int)Math.Round(crop.Width), (int)Math.Round(crop.Height));
                if (r.Width >= 4 && r.Height >= 4)
                    CropRequested?.Invoke(r);
            }

            InvalidateVisual();
            Changed?.Invoke();
        }

        public void CancelInteraction()
        {
            _creating = null;
            _cropRect = null;
            _down = false;
            ReleaseMouseCapture();
            InvalidateVisual();
        }
    }
}
