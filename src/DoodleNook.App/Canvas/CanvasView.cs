using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using DoodleNook.App.Diagnostics;
using DoodleNook.App.Editor;
using DoodleNook.App.Input;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Viewing;
using SkiaSharp;

using PixelRect = DoodleNook.Engine.Imaging.PixelRect;

namespace DoodleNook.App.Canvas;

/// <summary>
/// The drawing surface, shared by all five workspaces. It shows the document's composited projection through a
/// <see cref="ViewTransform"/> (zoom, pan, rotation, flip), draws the pixel grid, tiled preview, selection outline,
/// brush cursor and tool overlays, and translates Avalonia pointer events into <see cref="PointerInput"/> for the
/// <see cref="CanvasInputController"/>. It never edits pixels itself.
/// </summary>
public sealed class CanvasView : Control, ICustomHitTest
{
    private readonly DocumentTexture _texture = new();
    private readonly List<PointerInput> _inputs = new(64);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _ants = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _spray = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private double _lastSprayMs;
    private EditorSession? _session;
    private Document? _watched;
    private bool _fitMode = true;
    private bool _fitAllowsEnlarging;
    private Size _lastSize;
    private SKPath? _selectionPath;
    private float _antsOffset;

    public CanvasView()
    {
        ClipToBounds = true;
        Focusable = true;
        View = new ViewTransform(1, 1);
        View.Changed += () =>
        {
            InvalidateVisual();
            ViewChanged?.Invoke();
        };
        _ants.Tick += (_, _) =>
        {
            _antsOffset = (_antsOffset + 1) % 8;
            InvalidateVisual();
        };
        _spray.Tick += (_, _) =>
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            Input?.Tick(now - _lastSprayMs);
            _lastSprayMs = now;
        };
    }

    public ViewTransform View { get; }
    public CanvasInputController? Input { get; private set; }
    public InputDiagnostics Diagnostics { get; } = new();

    /// <summary>Show the pixel grid when zoomed in far enough.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Repeat the picture around itself (seamless textures).</summary>
    public bool Tiled { get; set; }

    /// <summary>Always show exact pixels (Pixel Art) instead of smoothing when zoomed out or in between.</summary>
    public bool PixelExact { get; set; }

    /// <summary>Snap zoom to whole numbers (Pixel Art).</summary>
    public bool IntegerZoom { get; set; }

    public event Action? ViewChanged;
    public event Action<Gesture>? GestureStarted;

    public bool UsesGpu => _texture.LastFrameUsedGpu;
    public long UploadedPixels => _texture.UploadedPixels;
    public long GpuFrames => _texture.GpuFrames;
    public long SoftwareFrames => _texture.SoftwareFrames;

    /// <summary>The whole canvas area takes pointer input, even where nothing hit-testable is drawn.</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    /// <summary>Zoom as shown to the user: 1 = one picture pixel per physical screen pixel.</summary>
    public double ActualZoom => View.Scale * RenderScaling;

    /// <summary>Last pointer position in document pixels (for the status bar), or null.</summary>
    public (int X, int Y)? PointerDocument
    {
        get
        {
            if (Input?.Hover is not { } h) return null;
            var (x, y) = View.ViewToDocument(h.X, h.Y);
            return ((int)Math.Floor(x), (int)Math.Floor(y));
        }
    }

    public void Attach(EditorSession session)
    {
        _session = session;
        Input = new CanvasInputController(session, View);
        Input.OverlayChanged += () =>
        {
            UpdateCursor();
            InvalidateVisual();
        };
        Input.ViewPannedByUser += () => _fitMode = false;
        session.DocumentReplaced += OnDocumentReplaced;
        session.StateChanged += () =>
        {
            Input.RefreshTransformFrame();
            UpdateCursor();
            InvalidateVisual();
        };
        OnDocumentReplaced();
    }

    private void OnDocumentReplaced()
    {
        if (_session is null) return;
        if (_watched is not null)
        {
            _watched.ProjectionChanged -= OnProjectionChanged;
            _watched.SelectionChanged -= OnSelectionChanged;
            _watched.SizeChanged -= OnSizeChangedDoc;
        }
        var doc = _session.Document;
        _watched = doc;
        doc.ProjectionChanged += OnProjectionChanged;
        doc.SelectionChanged += OnSelectionChanged;
        doc.SizeChanged += OnSizeChangedDoc;
        _texture.Load(doc.Projection);
        View.SetDocumentSize(doc.Width, doc.Height);
        Input?.CancelCrop();
        OnSelectionChanged();
        FitToView(enlarge: IntegerZoom);
    }

    private void OnProjectionChanged(PixelRect area)
    {
        if (_session is null) return;
        _texture.Update(_session.Document.Projection, area);
        InvalidateVisual();
    }

    private void OnSizeChangedDoc()
    {
        if (_session is null) return;
        _texture.Load(_session.Document.Projection);
        View.SetDocumentSize(_session.Document.Width, _session.Document.Height);
        FitToView(enlarge: IntegerZoom);
    }

    private void OnSelectionChanged()
    {
        _selectionPath?.Dispose();
        _selectionPath = null;
        var sel = _session?.Document.Selection;
        if (sel is not null && !sel.IsEmpty)
        {
            var path = new SKPath();
            foreach (var (x0, y0, x1, y1) in sel.Outline())
            {
                path.MoveTo(x0, y0);
                path.LineTo(x1, y1);
            }
            _selectionPath = path;
            _ants.Start();
        }
        else _ants.Stop();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ view commands

    public void FitToView(bool enlarge = true)
    {
        _fitMode = true;
        _fitAllowsEnlarging = enlarge;
        if (Bounds.Width < 2 || Bounds.Height < 2) return;
        double max = enlarge ? 64.0 / RenderScaling : 1.0 / RenderScaling;
        View.Fit(Bounds.Width, Bounds.Height, margin: 24, maxScale: max);
        if (IntegerZoom) SnapIntegerZoom(fit: true);
    }

    private void SnapIntegerZoom(bool fit)
    {
        double actual = ActualZoom;
        if (actual < 1) return;
        double snapped = fit ? Math.Floor(actual + 1e-6) : Math.Round(actual);
        View.ZoomCentered(Bounds.Width, Bounds.Height, Math.Max(1, snapped) / RenderScaling);
    }

    public void ZoomToActualPixels()
    {
        _fitMode = false;
        View.ZoomCentered(Bounds.Width, Bounds.Height, 1.0 / RenderScaling);
    }

    public void ZoomStep(int direction)
    {
        _fitMode = false;
        double next = IntegerZoom ? ViewTransform.NextIntegerStep(ActualZoom, direction) : ViewTransform.NextStep(ActualZoom, direction);
        View.ZoomCentered(Bounds.Width, Bounds.Height, next / RenderScaling);
    }

    public void ToggleFlip() => View.ToggleFlip(Bounds.Width, Bounds.Height);

    public void RotateBy(double degrees)
    {
        _fitMode = false;
        View.RotateAround(Bounds.Width / 2, Bounds.Height / 2, View.Rotation + degrees);
    }

    public void ResetRotation() => View.RotateAround(Bounds.Width / 2, Bounds.Height / 2, 0);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Input is not null) Input.Viewport = (e.NewSize.Width, e.NewSize.Height);
        if (_fitMode) FitToView(_fitAllowsEnlarging);
        else if (_lastSize.Width > 0)
            View.PanBy((e.NewSize.Width - _lastSize.Width) / 2, (e.NewSize.Height - _lastSize.Height) / 2);
        _lastSize = e.NewSize;
    }

    // ------------------------------------------------------------------ rendering

    private Point V(double x, double y)
    {
        var (vx, vy) = View.DocumentToView(x, y);
        return new Point(vx, vy);
    }

    public override void Render(DrawingContext context)
    {
        if (_session is null) return;
        var doc = _session.Document;
        var m = View.Matrix;
        var visible = new Rect(Bounds.Size);

        // Paper shadow under the (possibly rotated) picture.
        var corners = new[] { V(0, 0), V(doc.Width, 0), V(doc.Width, doc.Height), V(0, doc.Height) };
        var docBox = new Rect(new Point(corners.Min(c => c.X), corners.Min(c => c.Y)), new Point(corners.Max(c => c.X), corners.Max(c => c.Y)));
        if (!Tiled && !docBox.Contains(visible) && Math.Abs(View.Rotation) < 0.01)
        {
            var shadowRect = docBox.Intersect(visible.Inflate(80));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 21, 19)), null, new RoundedRect(shadowRect),
                new BoxShadows(new BoxShadow { OffsetY = 10, Blur = 34, Color = Color.FromArgb(128, 0, 0, 0) }));
        }

        double zoom = ActualZoom;
        context.Custom(new DocumentDrawOperation(visible, _texture, m, doc.Width, doc.Height, zoom, PixelExact, Tiled,
            ShowGrid && zoom >= 5, _selectionPath, _antsOffset, View.Scale));

        DrawOverlays(context);
    }

    private static readonly IBrush Shade = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

    private void DrawOverlays(DrawingContext context)
    {
        if (_session is null || Input is null) return;
        var o = Input.Overlay;
        var white = new Pen(Brushes.White, 1.3);
        var dark = new Pen(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), 3);
        var dash = new Pen(Brushes.White, 1.2, new DashStyle([4, 4], 0));

        if (o.Crop is { } crop)
        {
            var g = Quad(crop.X, crop.Y, crop.Right, crop.Bottom);
            var outer = new RectangleGeometry(new Rect(Bounds.Size));
            context.DrawGeometry(Shade, null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, g));
            context.DrawGeometry(null, dark, g);
            context.DrawGeometry(null, white, g);
        }
        if (o.Rect is { } r)
        {
            double l = Math.Min(r.X0, r.X1), t = Math.Min(r.Y0, r.Y1), rr = Math.Max(r.X0, r.X1), b = Math.Max(r.Y0, r.Y1);
            Geometry g = o.RectIsEllipse ? EllipseGeometryDoc(l, t, rr, b) : Quad(l, t, rr, b);
            if (_session.Tool == ToolKind.Crop)
            {
                var outer = new RectangleGeometry(new Rect(Bounds.Size));
                context.DrawGeometry(Shade, null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, g));
            }
            context.DrawGeometry(null, dark, g);
            context.DrawGeometry(null, dash, g);
        }
        if (o.Polyline is { Count: > 1 } pl)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(V(pl[0].X, pl[0].Y), false);
                for (int i = 1; i < pl.Count; i++) ctx.LineTo(V(pl[i].X, pl[i].Y));
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, dark, geo);
            context.DrawGeometry(null, dash, geo);
        }
        if (o.Line is { } ln)
        {
            var a = V(ln.X0, ln.Y0);
            var b = V(ln.X1, ln.Y1);
            context.DrawLine(dark, a, b);
            context.DrawLine(white, a, b);
            context.DrawEllipse(Brushes.White, dark, a, 4, 4);
            context.DrawEllipse(Brushes.White, dark, b, 4, 4);
        }
        if (o.TransformFrame is { } frame)
        {
            var pts = frame.Select(p => V(p.X, p.Y)).ToArray();
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], false);
                for (int i = 1; i < 4; i++) ctx.LineTo(pts[i]);
                ctx.EndFigure(true);
            }
            context.DrawGeometry(null, dark, geo);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Controls.DnColors.Accent), 1.5), geo);
            var handles = pts.Concat(Enumerable.Range(0, 4).Select(i => (pts[i] + pts[(i + 1) % 4]) / 2));
            foreach (var h in handles)
                context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Controls.DnColors.Accent), 1.5), new Rect(h.X - 5, h.Y - 5, 10, 10));
        }
        DrawCursor(context, dark);
    }

    private Geometry Quad(double l, double t, double r, double b)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(V(l, t), true);
            ctx.LineTo(V(r, t));
            ctx.LineTo(V(r, b));
            ctx.LineTo(V(l, b));
            ctx.EndFigure(true);
        }
        return geo;
    }

    private Geometry EllipseGeometryDoc(double l, double t, double r, double b)
    {
        var pts = Rasterize.EllipsePoints(l, t, r, b);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(V(pts[0].X, pts[0].Y), true);
            foreach (var p in pts.Skip(1)) ctx.LineTo(V(p.X, p.Y));
            ctx.EndFigure(true);
        }
        return geo;
    }

    private void DrawCursor(DrawingContext context, Pen dark)
    {
        if (_session is null || Input is null) return;
        var shape = Input.Cursor;
        var black = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

        if (Input.Gesture == Gesture.ResizeBrush)
        {
            var (ax, ay) = Input.ResizeAnchor;
            var p = new Point(ax, ay);
            double r = _session.IsPixelTool(_session.Tool) ? _session.Pixel.Size * View.Scale / 2 : Math.Max(1, _session.CurrentPaintSettings.Size * View.Scale / 2);
            context.DrawEllipse(null, new Pen(black, 3), p, r, r);
            context.DrawEllipse(null, new Pen(Brushes.White, 1.5), p, r, r);
            var label = new FormattedText($"{_session.CurrentSize:0} px", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("fonts:Inter#Inter, $Default")), 13, Brushes.White);
            var box = new Rect(ax + r + 10, ay - 14, label.Width + 20, 28);
            context.DrawRectangle(new SolidColorBrush(Controls.DnColors.Ui), new Pen(new SolidColorBrush(Controls.DnColors.Line), 1), new RoundedRect(box, 8));
            context.DrawText(label, new Point(box.X + 10, box.Y + (28 - label.Height) / 2));
            return;
        }

        if (Input.Hover is not { } h) return;
        var at = new Point(h.X, h.Y);
        if (shape == CursorShape.Eyedropper)
        {
            if (Input.Gesture == Gesture.Pick)
            {
                var fg = _session.Foreground;
                context.DrawEllipse(null, new Pen(black, 12), at, 26, 26);
                context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B)), 9), at, 26, 26);
            }
            return;
        }
        if (shape != CursorShape.BrushOutline) return;

        bool erasing = Input.Gesture is Gesture.Paint or Gesture.PixelPaint ? Input.StrokeErases : _session.Tool is ToolKind.Eraser or ToolKind.PixelEraser;
        if (_session.IsPixelTool(_session.Tool))
        {
            // The exact pixels the pixel brush will cover.
            var (dx, dy) = View.ViewToDocument(h.X, h.Y);
            int n = _session.Pixel.Size, half = (n - 1) / 2;
            int x0 = (int)Math.Floor(dx) - half, y0 = (int)Math.Floor(dy) - half;
            var q = Quad(x0, y0, x0 + n, y0 + n);
            context.DrawGeometry(null, dark, q);
            context.DrawGeometry(null, erasing ? new Pen(Brushes.White, 1.2, new DashStyle([3, 3], 0)) : new Pen(Brushes.White, 1.2), q);
            return;
        }

        var brush = _session.CurrentPaintSettings;
        double radius = brush.Size * View.Scale / 2;
        if (radius >= 3)
        {
            double ry = radius * (_session.PaintTool == ToolKind.Brush ? brush.Roundness : 1);
            double angle = (_session.PaintTool == ToolKind.Brush ? brush.Angle : 0) + View.Rotation;
            using (context.PushTransform(Matrix.CreateRotation(angle * Math.PI / 180) * Matrix.CreateTranslation(at.X, at.Y)))
            {
                context.DrawEllipse(null, new Pen(black, 2.5), new Point(0, 0), radius, ry);
                context.DrawEllipse(null, erasing ? new Pen(Brushes.White, 1.3, new DashStyle([3, 3], 0)) : new Pen(Brushes.White, 1.3), new Point(0, 0), radius, ry);
            }
        }
        double arm = radius >= 3 ? 3 : 6;
        var cross = new Pen(Brushes.White, 1.2);
        context.DrawLine(new Pen(black, 3), new Point(at.X - arm, at.Y), new Point(at.X + arm, at.Y));
        context.DrawLine(new Pen(black, 3), new Point(at.X, at.Y - arm), new Point(at.X, at.Y + arm));
        context.DrawLine(cross, new Point(at.X - arm, at.Y), new Point(at.X + arm, at.Y));
        context.DrawLine(cross, new Point(at.X, at.Y - arm), new Point(at.X, at.Y + arm));
    }

    private static readonly Cursor NoCursor = new(StandardCursorType.None);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    private void UpdateCursor()
    {
        if (Input is null) return;
        Cursor = Input.Cursor switch
        {
            CursorShape.Hand or CursorShape.Grabbing or CursorShape.Rotate => HandCursor,
            CursorShape.Eyedropper or CursorShape.Crosshair => CrossCursor,
            CursorShape.Resize => ResizeCursor,
            CursorShape.Move => MoveCursor,
            CursorShape.Arrow => ArrowCursor,
            _ => NoCursor,
        };
    }

    public void RefreshCursor()
    {
        UpdateCursor();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ input

    private static PointerKind KindOf(IPointer pointer) => pointer.Type switch
    {
        PointerType.Pen => PointerKind.Pen,
        PointerType.Touch => PointerKind.Touch,
        _ => PointerKind.Mouse,
    };

    private static PointerInput ToInput(PointerPoint pt, IPointer pointer, KeyModifiers modifiers)
    {
        var pp = pt.Properties;
        return new PointerInput(
            pointer.Id,
            KindOf(pointer),
            pt.Position.X,
            pt.Position.Y,
            pp.Pressure,
            Primary: pp.IsLeftButtonPressed,
            Secondary: pp.IsRightButtonPressed || pp.IsBarrelButtonPressed,
            Middle: pp.IsMiddleButtonPressed,
            Eraser: pp.IsEraser || pp.IsInverted,
            Alt: modifiers.HasFlag(KeyModifiers.Alt),
            Shift: modifiers.HasFlag(KeyModifiers.Shift),
            Ctrl: modifiers.HasFlag(KeyModifiers.Control));
    }

    private void Observe(PointerEventArgs e, PointerPoint pt, int samples, bool recordPressure)
    {
        var pp = pt.Properties;
        Diagnostics.Record(e.Timestamp, KindOf(e.Pointer), samples, pp.IsLeftButtonPressed || pp.IsRightButtonPressed,
            pp.Pressure, pp.XTilt, pp.YTilt, pp.IsBarrelButtonPressed, pp.IsEraser || pp.IsInverted,
            pp.IsRightButtonPressed, pp.IsMiddleButtonPressed, recordPressure);
        if (_session is not null && Input?.Gesture == Gesture.Paint)
        {
            var settings = Input.StrokeErases ? _session.Eraser : _session.Brush;
            float p = KindOf(e.Pointer) == PointerKind.Pen ? pp.Pressure : CanvasInputController.MousePressure;
            Diagnostics.RecordBrush(_session.Curve.Map(p), settings.Normalized().Evaluate(p, _session.Curve).Diameter);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Input is null) return;
        Focus();
        var pt = e.GetCurrentPoint(this);
        var start = _clock.Elapsed.TotalMilliseconds;
        Input.Viewport = (Bounds.Width, Bounds.Height);
        bool started = Input.Press(ToInput(pt, e.Pointer, e.KeyModifiers));
        if (started)
        {
            e.Pointer.Capture(this);
            e.PreventGestureRecognition();
            e.Handled = true;
            if (Input.Gesture == Gesture.Paint)
            {
                Diagnostics.StrokeStarted();
                Diagnostics.RecordEngineTime(_clock.Elapsed.TotalMilliseconds, _clock.Elapsed.TotalMilliseconds - start);
                if (_session!.Brush.Spray)
                {
                    _lastSprayMs = _clock.Elapsed.TotalMilliseconds;
                    _spray.Start();
                }
            }
            GestureStarted?.Invoke(Input.Gesture);
        }
        Observe(e, pt, 1, started && Input.Gesture == Gesture.Paint);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Input is null) return;
        var mods = e.KeyModifiers;
        if (!Input.AltHeld && mods.HasFlag(KeyModifiers.Alt) && Input.Gesture == Gesture.None)
        {
            Input.AltHeld = true;
            UpdateCursor();
        }

        _inputs.Clear();
        PointerPoint current;
        if (Input.Gesture is Gesture.Paint or Gesture.PixelPaint)
        {
            var points = e.GetIntermediatePoints(this);
            foreach (var pt in points) _inputs.Add(ToInput(pt, e.Pointer, mods));
            current = points[^1];
        }
        else
        {
            current = e.GetCurrentPoint(this);
            _inputs.Add(ToInput(current, e.Pointer, mods));
        }

        double start = _clock.Elapsed.TotalMilliseconds;
        Input.Move(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_inputs));
        if (Input.Gesture == Gesture.Paint)
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            Diagnostics.RecordEngineTime(now, now - start);
            _lastSprayMs = now;
            e.Handled = true;
        }
        Observe(e, current, _inputs.Count, Input.Gesture == Gesture.Paint);
        PointerMovedOverCanvas?.Invoke();
    }

    /// <summary>Raised on every pointer move (status bar cursor position).</summary>
    public event Action? PointerMovedOverCanvas;

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Input is null) return;
        _spray.Stop();
        var pt = e.GetCurrentPoint(this);
        bool wasActive = Input.Gesture != Gesture.None;
        Input.Release(ToInput(pt, e.Pointer, e.KeyModifiers));
        if (wasActive && Input.Gesture == Gesture.None)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        Diagnostics.PointerLifted();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _spray.Stop();
        Input?.Interrupt();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Input?.Leave();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Input?.Gesture is not (null or Gesture.None)) return;
        double dy = e.Delta.Y;
        if (Math.Abs(dy) < 1e-6) return;
        _fitMode = false;
        var p = e.GetPosition(this);
        if (IntegerZoom)
        {
            double next = ViewTransform.NextIntegerStep(ActualZoom, dy > 0 ? 1 : -1);
            View.ZoomAround(p.X, p.Y, next / RenderScaling);
        }
        else View.ZoomAround(p.X, p.Y, View.Scale * Math.Pow(1.2, Math.Clamp(dy, -4, 4)));
        e.Handled = true;
    }

    public void InterruptGesture()
    {
        _spray.Stop();
        Input?.Interrupt();
        if (Input is not null)
        {
            Input.SpaceHeld = false;
            Input.AltHeld = false;
            Input.RotateHeld = false;
        }
        UpdateCursor();
    }

    /// <summary>Renders the projection with Skia (GPU when available), plus grid, tiles and the selection outline.</summary>
    private sealed class DocumentDrawOperation(Rect bounds, DocumentTexture texture, Affine matrix, int docWidth, int docHeight,
        double zoom, bool pixelExact, bool tiled, bool grid, SKPath? selection, float antsOffset, double scale) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public void Dispose()
        {
        }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        private static SKShader? s_checker;

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var m = new SKMatrix((float)matrix.M11, (float)matrix.M21, (float)matrix.Dx, (float)matrix.M12, (float)matrix.M22, (float)matrix.Dy, 0, 0, 1);
            var docRect = new SKRect(0, 0, docWidth, docHeight);

            var sampling = pixelExact || Math.Abs(zoom - 1) < 0.001 || zoom >= 2
                ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
                : zoom > 1
                    ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)
                    : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

            int range = tiled ? 1 : 0;
            for (int ty = -range; ty <= range; ty++)
            {
                for (int tx = -range; tx <= range; tx++)
                {
                    canvas.Save();
                    var tile = m.PreConcat(SKMatrix.CreateTranslation(tx * docWidth, ty * docHeight));
                    using (var clip = new SKPath())
                    {
                        clip.AddRect(docRect);
                        clip.Transform(tile);
                        canvas.ClipPath(clip, antialias: true);
                    }
                    DrawChecker(canvas);
                    canvas.SetMatrix(canvas.TotalMatrix.PreConcat(tile));
                    texture.Draw(canvas, lease.GrContext, docRect, sampling);
                    if (tx != 0 || ty != 0)
                    {
                        using var dim = new SKPaint { Color = new SKColor(24, 21, 19, 90) };
                        canvas.DrawRect(docRect, dim);
                    }
                    canvas.Restore();
                }
            }

            if (grid) DrawGrid(canvas, m);

            if (selection is not null)
            {
                using var path = new SKPath(selection);
                path.Transform(m);
                using var under = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
                using var over = new SKPaint
                {
                    Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false,
                    PathEffect = SKPathEffect.CreateDash([4, 4], antsOffset),
                };
                canvas.DrawPath(path, under);
                canvas.DrawPath(path, over);
            }
        }

        private void DrawGrid(SKCanvas canvas, SKMatrix m)
        {
            // Only the visible part of the document.
            if (!m.TryInvert(out var inv)) return;
            var b = Bounds;
            var corners = new[] { inv.MapPoint(0, 0), inv.MapPoint((float)b.Width, 0), inv.MapPoint(0, (float)b.Height), inv.MapPoint((float)b.Width, (float)b.Height) };
            int x0 = Math.Max(0, (int)Math.Floor(corners.Min(c => c.X))), x1 = Math.Min(docWidth, (int)Math.Ceiling(corners.Max(c => c.X)));
            int y0 = Math.Max(0, (int)Math.Floor(corners.Min(c => c.Y))), y1 = Math.Min(docHeight, (int)Math.Ceiling(corners.Max(c => c.Y)));
            if (x1 <= x0 || y1 <= y0 || (x1 - x0) * (y1 - y0) > 2_000_000) return;
            using var path = new SKPath();
            for (int x = x0; x <= x1; x++) { path.MoveTo(x, y0); path.LineTo(x, y1); }
            for (int y = y0; y <= y1; y++) { path.MoveTo(x0, y); path.LineTo(x1, y); }
            path.Transform(m);
            using var paint = new SKPaint { Color = new SKColor(40, 32, 28, 70), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
            canvas.DrawPath(path, paint);
        }

        private static void DrawChecker(SKCanvas canvas)
        {
            if (s_checker is null)
            {
                using var bmp = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
                var light = new SKColor(0xEB, 0xE5, 0xDC);
                var dark = new SKColor(0xCF, 0xC7, 0xBB);
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        bmp.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? light : dark);
                using var img = SKImage.FromBitmap(bmp);
                s_checker = img.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.Identity);
            }
            using var paint = new SKPaint { Shader = s_checker };
            canvas.DrawPaint(paint);
        }
    }
}
