using System.Runtime.InteropServices;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using DoodleNook.Engine.Viewing;

namespace DoodleNook.App.Input;

public enum PointerKind
{
    Mouse,
    Pen,
    Touch,
}

/// <summary>
/// One pointer sample, already translated out of the UI framework: position in view coordinates (DIPs of the canvas),
/// pressure 0–1, buttons and modifier keys. Pen side buttons may arrive as <see cref="Secondary"/> (barrel or
/// right-click) or <see cref="Middle"/>, depending on the Huion driver settings.
/// </summary>
public readonly record struct PointerInput(
    long PointerId,
    PointerKind Kind,
    double X,
    double Y,
    float Pressure,
    bool Primary,
    bool Secondary = false,
    bool Middle = false,
    bool Eraser = false,
    bool Alt = false,
    bool Shift = false,
    bool Ctrl = false);

public enum Gesture
{
    None,
    Paint,
    PixelPaint,
    Pan,
    Pick,
    ResizeBrush,
    RotateView,
    SecondaryPending,
    Gradient,
    Shape,
    Select,
    Lasso,
    Move,
    Transform,
    Crop,
}

public enum CursorShape
{
    BrushOutline,
    Hand,
    Grabbing,
    Eyedropper,
    Resize,
    Crosshair,
    Move,
    Rotate,
    Arrow,
}

/// <summary>What the canvas should draw on top of the picture for the current tool (all in document coordinates).</summary>
public sealed class CanvasOverlay
{
    public (double X0, double Y0, double X1, double Y1)? Rect { get; set; }
    public bool RectIsEllipse { get; set; }
    public List<(double X, double Y)>? Polyline { get; set; }
    public (double X0, double Y0, double X1, double Y1)? Line { get; set; }
    public (double X, double Y)[]? TransformFrame { get; set; }
    public PixelRect? Crop { get; set; }

    /// <summary>The stamp tool's preview under the pointer (picture and placement in document coordinates).</summary>
    public (RasterImage Image, StampPlacement Placement)? Stamp { get; set; }
}

/// <summary>
/// Decides what a pointer gesture on the canvas means for the current tool and forwards it to the
/// <see cref="EditorSession"/>, the document operations or the <see cref="ViewTransform"/>. It never draws pixels
/// itself and knows nothing about the UI framework, so it can be unit-tested with synthetic input.
/// </summary>
public sealed class CanvasInputController
{
    public const float MousePressure = 1f;
    private const double TapSlop = 6; // view units a "click" may move

    private readonly EditorSession _session;
    private readonly ViewTransform _view;
    private readonly List<StrokeSample> _buffer = new(64);
    private long _pointerId = -1;
    private double _startX, _startY, _lastX, _lastY;
    private double _startDocX, _startDocY;
    private bool _moved;

    // Per-gesture state.
    private GradientOperation? _gradient;
    private ShapeOperation? _shape;
    private FloatingTransform? _move;
    private Affine _transformStart;
    private int _handle = -1;
    private double _rotateStartAngle, _rotateStartView;
    private bool _pixelBackground;

    public CanvasInputController(EditorSession session, ViewTransform view)
    {
        _session = session;
        _view = view;
    }

    public Gesture Gesture { get; private set; }
    public PointerKind ActiveKind { get; private set; }
    public bool SpaceHeld { get; set; }
    public bool AltHeld { get; set; }

    /// <summary>True while the R key is held: drag to rotate the view.</summary>
    public bool RotateHeld { get; set; }

    /// <summary>Viewport size in view units (needed to rotate around the centre).</summary>
    public (double Width, double Height) Viewport { get; set; } = (800, 600);

    /// <summary>Actual-pixel zoom per view unit (render scaling), for integer pixel-art zoom.</summary>
    public double RenderScaling { get; set; } = 1;

    /// <summary>Pixel Art: a right-click paints with the background colour instead of opening the quick palette.</summary>
    public bool PixelRightPaintsBackground { get; set; }

    public (double X, double Y)? Hover { get; private set; }
    public bool StrokeErases { get; private set; }
    public (double X, double Y) ResizeAnchor { get; private set; }
    public double ResizeStartSize { get; private set; }

    /// <summary>The crop rectangle waiting for Apply (Enter) or Cancel (Esc).</summary>
    public PixelRect? PendingCrop { get; private set; }

    public CanvasOverlay Overlay { get; } = new();

    public event Action? ViewPannedByUser;
    public event Action? OverlayChanged;

    /// <summary>A right-click / pen-button tap asks for the quick palette at a view position.</summary>
    public event Action<double, double>? RadialRequested;

    public CursorShape Cursor => Gesture switch
    {
        Gesture.Pan => CursorShape.Grabbing,
        Gesture.Pick => CursorShape.Eyedropper,
        Gesture.ResizeBrush => CursorShape.Resize,
        Gesture.RotateView => CursorShape.Rotate,
        Gesture.Paint or Gesture.PixelPaint => CursorShape.BrushOutline,
        Gesture.Move or Gesture.Transform => CursorShape.Move,
        Gesture.None when SpaceHeld || _session.Tool == ToolKind.Hand => CursorShape.Hand,
        Gesture.None when RotateHeld => CursorShape.Rotate,
        Gesture.None when _session.ActiveTransform is not null => CursorShape.Move,
        Gesture.None when AltHeld && !IsSelectionTool(_session.Tool) => CursorShape.Eyedropper,
        _ => _session.Tool switch
        {
            ToolKind.Brush or ToolKind.Eraser or ToolKind.PixelPencil or ToolKind.PixelEraser => CursorShape.BrushOutline,
            ToolKind.Eyedropper => CursorShape.Eyedropper,
            ToolKind.Move => CursorShape.Move,
            _ => CursorShape.Crosshair,
        },
    };

    public static bool IsSelectionTool(ToolKind t) => t is ToolKind.RectSelect or ToolKind.EllipseSelect or ToolKind.Lasso or ToolKind.MagicWand;

    private (double X, double Y) Doc(in PointerInput p) => _view.ViewToDocument(p.X, p.Y);

    private Document Document => _session.Document;

    // ================================================================ press

    public bool Press(in PointerInput p)
    {
        if (Gesture != Gesture.None) return false;
        Hover = (p.X, p.Y);
        Gesture g = Classify(p);
        if (g == Gesture.None) return false;

        Gesture = g;
        ActiveKind = p.Kind;
        _pointerId = p.PointerId;
        _startX = _lastX = p.X;
        _startY = _lastY = p.Y;
        (_startDocX, _startDocY) = Doc(p);
        _moved = false;

        switch (g)
        {
            case Gesture.Paint:
                StrokeErases = p.Eraser || _session.Tool == ToolKind.Eraser;
                _session.BeginStroke(ToSample(p), forceEraser: p.Eraser);
                if (!_session.IsStrokeActive) Gesture = Gesture.None;
                break;
            case Gesture.PixelPaint:
            {
                var (x, y) = Doc(p);
                StrokeErases = p.Eraser || _session.Tool == ToolKind.PixelEraser;
                _session.BeginPixelStroke((int)Math.Floor(x), (int)Math.Floor(y), StrokeErases, _pixelBackground);
                if (!_session.IsStrokeActive) Gesture = Gesture.None;
                break;
            }
            case Gesture.Pick:
                Pick(p);
                break;
            case Gesture.ResizeBrush:
                ResizeAnchor = (p.X, p.Y);
                ResizeStartSize = _session.CurrentSize;
                break;
            case Gesture.RotateView:
                _rotateStartAngle = AngleFromCentre(p.X, p.Y);
                _rotateStartView = _view.Rotation;
                break;
            case Gesture.Gradient:
                if (!_session.CanEditActiveLayer()) { Gesture = Gesture.None; break; }
                _gradient = new GradientOperation(Document, Document.ActiveLayer, _session.Foreground, _session.Background, _session.Gradient);
                Overlay.Line = (_startDocX, _startDocY, _startDocX, _startDocY);
                break;
            case Gesture.Shape:
                if (!_session.CanEditActiveLayer()) { Gesture = Gesture.None; break; }
                _shape = new ShapeOperation(Document, Document.ActiveLayer, _session.Foreground, ShapeOptionsNow());
                break;
            case Gesture.Select:
            case Gesture.Crop:
                Overlay.Rect = (_startDocX, _startDocY, _startDocX, _startDocY);
                Overlay.RectIsEllipse = _session.Tool == ToolKind.EllipseSelect && g == Gesture.Select;
                if (g == Gesture.Crop) PendingCrop = null;
                break;
            case Gesture.Lasso:
                Overlay.Polyline = [(_startDocX, _startDocY)];
                break;
            case Gesture.Move:
                if (!_session.CanEditActiveLayer()) { Gesture = Gesture.None; break; }
                _move = FloatingTransform.Begin(Document, Document.ActiveLayer, "Move");
                if (_move is null) Gesture = Gesture.None;
                break;
            case Gesture.Transform:
                _transformStart = _session.ActiveTransform!.Transform;
                _handle = HitHandle(p.X, p.Y);
                break;
        }
        OverlayChanged?.Invoke();
        return Gesture != Gesture.None;
    }

    private Gesture Classify(in PointerInput p)
    {
        var tool = _session.Tool;
        _pixelBackground = false;
        if (p.Kind == PointerKind.Touch) return Gesture.Pan;
        if (p.Middle) return Gesture.Pan;
        if (SpaceHeld || tool == ToolKind.Hand) return p.Primary || p.Secondary ? Gesture.Pan : Gesture.None;
        if (RotateHeld && (p.Primary || p.Secondary)) return Gesture.RotateView;
        if (p.Secondary)
        {
            if (_session.PixelMode && PixelRightPaintsBackground && tool is ToolKind.PixelPencil)
            {
                _pixelBackground = true;
                return Gesture.PixelPaint;
            }
            return Gesture.SecondaryPending; // tap = quick palette, drag = brush size
        }
        if (!p.Primary) return Gesture.None;
        bool alt = p.Alt || AltHeld;
        if (alt && !IsSelectionTool(tool) && _session.ActiveTransform is null) return Gesture.Pick;
        if (_session.ActiveTransform is not null && tool != ToolKind.Hand) return Gesture.Transform;

        switch (tool)
        {
            case ToolKind.Brush:
            case ToolKind.Eraser:
                return Gesture.Paint;
            case ToolKind.PixelPencil:
            case ToolKind.PixelEraser:
                return Gesture.PixelPaint;
            case ToolKind.Eyedropper:
                return Gesture.Pick;
            case ToolKind.Fill:
                DoFill(p);
                return Gesture.None;
            case ToolKind.Stamp:
                DoStamp(p);
                return Gesture.None;
            case ToolKind.MagicWand:
                DoWand(p);
                return Gesture.None;
            case ToolKind.Gradient:
                return Gesture.Gradient;
            case ToolKind.Shape:
            case ToolKind.PixelLine:
            case ToolKind.PixelRect:
            case ToolKind.PixelEllipse:
                return Gesture.Shape;
            case ToolKind.RectSelect:
            case ToolKind.EllipseSelect:
                return Gesture.Select;
            case ToolKind.Lasso:
                return Gesture.Lasso;
            case ToolKind.Move:
                return Gesture.Move;
            case ToolKind.Crop:
                return Gesture.Crop;
            default:
                return Gesture.None;
        }
    }

    // ================================================================ move

    public void Move(ReadOnlySpan<PointerInput> points)
    {
        if (points.IsEmpty) return;
        var last = points[^1];
        if (Gesture == Gesture.None)
        {
            Hover = (last.X, last.Y);
            UpdateStampPreview();
            OverlayChanged?.Invoke();
            return;
        }
        if (Gesture is Gesture.Paint or Gesture.PixelPaint && last.PointerId != _pointerId) return;

        Hover = (last.X, last.Y);
        if (Math.Abs(last.X - _startX) > TapSlop || Math.Abs(last.Y - _startY) > TapSlop) _moved = true;
        var (dx, dy) = Doc(last);

        switch (Gesture)
        {
            case Gesture.Paint:
                _buffer.Clear();
                foreach (var p in points)
                    if (p.PointerId == _pointerId) _buffer.Add(ToSample(p));
                _session.ContinueStroke(CollectionsMarshal.AsSpan(_buffer));
                break;
            case Gesture.PixelPaint:
                foreach (var p in points)
                {
                    if (p.PointerId != _pointerId) continue;
                    var (x, y) = Doc(p);
                    _session.ContinuePixelStroke((int)Math.Floor(x), (int)Math.Floor(y));
                }
                break;
            case Gesture.Pan:
                _view.PanBy(last.X - _lastX, last.Y - _lastY);
                ViewPannedByUser?.Invoke();
                break;
            case Gesture.Pick:
                Pick(last);
                break;
            case Gesture.SecondaryPending:
                if (_moved && _session.Tool is ToolKind.Brush or ToolKind.Eraser or ToolKind.PixelPencil or ToolKind.PixelEraser)
                {
                    Gesture = Gesture.ResizeBrush;
                    ResizeAnchor = (_startX, _startY);
                    ResizeStartSize = _session.CurrentSize;
                    goto case Gesture.ResizeBrush;
                }
                break;
            case Gesture.ResizeBrush:
            {
                double delta = last.X - ResizeAnchor.X;
                double size = _session.IsPixelTool(_session.Tool)
                    ? ResizeStartSize + delta / (_view.Scale * 4)
                    : ResizeStartSize + 2 * delta / _view.Scale;
                _session.SetSize(size);
                break;
            }
            case Gesture.RotateView:
            {
                double angle = _rotateStartView + AngleFromCentre(last.X, last.Y) - _rotateStartAngle;
                if (last.Shift) angle = Math.Round(angle / 15) * 15;
                _view.RotateAround(Viewport.Width / 2, Viewport.Height / 2, angle);
                ViewPannedByUser?.Invoke();
                break;
            }
            case Gesture.Gradient:
            {
                var (ex, ey) = Constrain(dx, dy, last.Shift, angles: true);
                _gradient!.Update(_startDocX, _startDocY, ex, ey);
                Overlay.Line = (_startDocX, _startDocY, ex, ey);
                break;
            }
            case Gesture.Shape:
            {
                bool line = ShapeOptionsNow().Kind == ShapeKind.Line;
                var (ex, ey) = Constrain(dx, dy, last.Shift, angles: line);
                if (_session.PixelMode || _session.IsPixelTool(_session.Tool))
                    _shape!.Update(Math.Floor(_startDocX), Math.Floor(_startDocY), Math.Floor(ex), Math.Floor(ey));
                else _shape!.Update(_startDocX, _startDocY, ex, ey);
                break;
            }
            case Gesture.Select:
            case Gesture.Crop:
            {
                var (ex, ey) = Constrain(dx, dy, last.Shift && Gesture == Gesture.Select, angles: false);
                Overlay.Rect = (_startDocX, _startDocY, ex, ey);
                break;
            }
            case Gesture.Lasso:
                Overlay.Polyline!.Add((dx, dy));
                break;
            case Gesture.Move:
            {
                int mx = (int)Math.Round(dx - _startDocX), my = (int)Math.Round(dy - _startDocY);
                _move!.MoveTo(mx, my);
                break;
            }
            case Gesture.Transform:
                DragTransform(last, dx, dy);
                break;
        }
        _lastX = last.X;
        _lastY = last.Y;
        OverlayChanged?.Invoke();
    }

    // ================================================================ release

    public void Release(in PointerInput p)
    {
        if (Gesture == Gesture.None) return;
        if (Gesture is Gesture.Paint or Gesture.PixelPaint && p.PointerId != _pointerId) return;
        if (Gesture is not (Gesture.Paint or Gesture.PixelPaint) && p.PointerId != _pointerId && (p.Primary || p.Secondary || p.Middle)) return;

        var (dx, dy) = Doc(p);
        switch (Gesture)
        {
            case Gesture.Paint:
                if (p.X != _lastX || p.Y != _lastY)
                {
                    _buffer.Clear();
                    _buffer.Add(ToSample(p));
                    _session.ContinueStroke(CollectionsMarshal.AsSpan(_buffer));
                }
                _session.EndStroke();
                break;
            case Gesture.PixelPaint:
                _session.EndStroke();
                break;
            case Gesture.SecondaryPending:
                RadialRequested?.Invoke(_startX, _startY);
                break;
            case Gesture.Gradient:
                if (_moved) _gradient!.Commit();
                else _gradient!.Cancel();
                _gradient = null;
                Overlay.Line = null;
                break;
            case Gesture.Shape:
                if (_moved || _session.IsPixelTool(_session.Tool)) _shape!.Commit();
                else _shape!.Cancel();
                _shape = null;
                break;
            case Gesture.Select:
                FinishSelect(p, dx, dy);
                break;
            case Gesture.Lasso:
                if (Overlay.Polyline is { Count: >= 3 } pts)
                    _session.ApplySelection(Selections.Lasso(Document, pts, !_session.PixelMode && _session.Selection.AntiAlias), "Lasso", ModeFor(p));
                else if (!_moved) _session.Deselect();
                Overlay.Polyline = null;
                break;
            case Gesture.Move:
                _move!.Commit();
                _move = null;
                break;
            case Gesture.Crop:
            {
                var r = Overlay.Rect!.Value;
                var rect = PixelRect.FromEdges(
                    (int)Math.Round(Math.Min(r.X0, r.X1)), (int)Math.Round(Math.Min(r.Y0, r.Y1)),
                    (int)Math.Round(Math.Max(r.X0, r.X1)), (int)Math.Round(Math.Max(r.Y0, r.Y1))).Intersect(Document.Bounds);
                PendingCrop = rect.IsEmpty || !_moved ? null : rect;
                Overlay.Crop = PendingCrop;
                Overlay.Rect = null;
                break;
            }
        }
        Finish();
    }

    private void FinishSelect(in PointerInput p, double dx, double dy)
    {
        var r = Overlay.Rect!.Value;
        Overlay.Rect = null;
        if (!_moved)
        {
            _session.Deselect(); // a click without dragging clears the selection
            return;
        }
        var (ex, ey) = Constrain(dx, dy, p.Shift && !p.Alt, angles: false);
        bool aa = !_session.PixelMode && _session.Selection.AntiAlias;
        double x0 = r.X0, y0 = r.Y0;
        if (_session.PixelMode)
        {
            x0 = Math.Round(x0); y0 = Math.Round(y0); ex = Math.Round(ex); ey = Math.Round(ey);
        }
        var mask = _session.Tool == ToolKind.EllipseSelect
            ? Selections.Ellipse(Document, x0, y0, ex, ey, aa)
            : Selections.Rectangle(Document, x0, y0, ex, ey);
        _session.ApplySelection(mask, _session.Tool == ToolKind.EllipseSelect ? "Ellipse select" : "Rectangle select", ModeFor(p));
    }

    /// <summary>Selection mode from modifier keys (Shift add, Alt subtract, both intersect), else the tool option.</summary>
    private SelectionMode ModeFor(in PointerInput p)
    {
        bool alt = p.Alt || AltHeld;
        if (p.Shift && alt) return SelectionMode.Intersect;
        if (p.Shift) return SelectionMode.Add;
        if (alt) return SelectionMode.Subtract;
        return _session.Selection.Mode;
    }

    /// <summary>The gesture was interrupted (capture lost, window deactivated, pointer cancelled). Paint so far is kept.</summary>
    public void Interrupt()
    {
        switch (Gesture)
        {
            case Gesture.Paint:
            case Gesture.PixelPaint:
                _session.EndStroke();
                break;
            case Gesture.Gradient:
                _gradient?.Commit();
                break;
            case Gesture.Shape:
                _shape?.Commit();
                break;
            case Gesture.Move:
                _move?.Commit();
                break;
        }
        _gradient = null;
        _shape = null;
        _move = null;
        Overlay.Rect = null;
        Overlay.Polyline = null;
        Overlay.Line = null;
        Finish();
    }

    public void Leave()
    {
        if (Gesture != Gesture.None) return;
        Hover = null;
        Overlay.Stamp = null;
        OverlayChanged?.Invoke();
    }

    private void Finish()
    {
        Gesture = Gesture.None;
        _pointerId = -1;
        StrokeErases = false;
        _handle = -1;
        RefreshTransformFrame();
        OverlayChanged?.Invoke();
    }

    /// <summary>Airbrush: called regularly by the window so a resting pen keeps spraying.</summary>
    public void Tick(double elapsedMs)
    {
        if (Gesture == Gesture.Paint && _session.Brush.Spray && !StrokeErases) _session.Spray(elapsedMs);
    }

    // ================================================================ crop

    public bool ApplyCrop()
    {
        if (PendingCrop is not { } rect) return false;
        _session.RunImageOperation(d => ImageOperations.Crop(d, rect));
        CancelCrop();
        return true;
    }

    public void CancelCrop()
    {
        PendingCrop = null;
        Overlay.Crop = null;
        OverlayChanged?.Invoke();
    }

    // ================================================================ transform handles

    /// <summary>Updates the drawn frame of an active free transform (call after it starts or changes).</summary>
    public void RefreshTransformFrame()
    {
        var t = _session.ActiveTransform;
        if (t is null)
        {
            Overlay.TransformFrame = null;
            return;
        }
        double w = t.Source.Width, h = t.Source.Height;
        var m = t.Transform;
        Overlay.TransformFrame = [m.Apply(0, 0), m.Apply(w, 0), m.Apply(w, h), m.Apply(0, h)];
    }

    /// <summary>Handles in source-local coordinates: 0–3 corners, 4–7 edge midpoints.</summary>
    private (double X, double Y) LocalHandle(int i, double w, double h) => i switch
    {
        0 => (0, 0), 1 => (w, 0), 2 => (w, h), 3 => (0, h),
        4 => (w / 2, 0), 5 => (w, h / 2), 6 => (w / 2, h), _ => (0, h / 2),
    };

    /// <summary>Which handle is under a view point: 0–7 handles, 8 = inside (move), 9 = outside (rotate).</summary>
    public int HitHandle(double vx, double vy)
    {
        var t = _session.ActiveTransform;
        if (t is null) return -1;
        double w = t.Source.Width, h = t.Source.Height;
        // Small frames: leave room to grab the inside (move) by dropping edge handles that would crowd it.
        var (c0x, c0y) = _view.DocumentToView(t.Transform.Apply(0, 0).X, t.Transform.Apply(0, 0).Y);
        var (c1x, c1y) = _view.DocumentToView(t.Transform.Apply(w, 0).X, t.Transform.Apply(w, 0).Y);
        var (c3x, c3y) = _view.DocumentToView(t.Transform.Apply(0, h).X, t.Transform.Apply(0, h).Y);
        double viewW = Math.Sqrt((c1x - c0x) * (c1x - c0x) + (c1y - c0y) * (c1y - c0y));
        double viewH = Math.Sqrt((c3x - c0x) * (c3x - c0x) + (c3y - c0y) * (c3y - c0y));
        for (int i = 0; i < 8; i++)
        {
            if (i is 4 or 6 && viewH < 44) continue;
            if (i is 5 or 7 && viewW < 44) continue;
            if (i < 4 && (viewW < 24 || viewH < 24)) continue;
            var (lx, ly) = LocalHandle(i, w, h);
            var (dx, dy) = t.Transform.Apply(lx, ly);
            var (hx, hy) = _view.DocumentToView(dx, dy);
            if (Math.Abs(hx - vx) <= 10 && Math.Abs(hy - vy) <= 10) return i;
        }
        var (docX, docY) = _view.ViewToDocument(vx, vy);
        var (ux, uy) = t.Transform.Invert().Apply(docX, docY);
        return ux >= 0 && uy >= 0 && ux <= w && uy <= h ? 8 : 9;
    }

    private void DragTransform(in PointerInput p, double docX, double docY)
    {
        var t = _session.ActiveTransform;
        if (t is null) return;
        double w = t.Source.Width, h = t.Source.Height;
        var start = _transformStart;
        if (_handle == 8 || _handle < 0)
        {
            double mx = docX - _startDocX, my = docY - _startDocY;
            if (_session.PixelMode) { mx = Math.Round(mx); my = Math.Round(my); }
            t.SetTransform(start.Then(Affine.Translation(mx, my)));
        }
        else if (_handle == 9)
        {
            var (cx, cy) = start.Apply(w / 2, h / 2);
            double a0 = Math.Atan2(_startDocY - cy, _startDocX - cx), a1 = Math.Atan2(docY - cy, docX - cx);
            double deg = (a1 - a0) * 180 / Math.PI;
            if (p.Shift || _session.PixelMode) deg = Math.Round(deg / (_session.PixelMode ? 90 : 15)) * (_session.PixelMode ? 90 : 15);
            t.SetTransform(start.Then(Affine.Translation(-cx, -cy)).Then(Affine.Rotation(deg)).Then(Affine.Translation(cx, cy)));
        }
        else
        {
            int opposite = _handle < 4 ? (_handle + 2) % 4 : 4 + (_handle - 4 + 2) % 4;
            var (ax, ay) = LocalHandle(opposite, w, h);
            var (hx, hy) = LocalHandle(_handle, w, h);
            var (lx, ly) = start.Invert().Apply(docX, docY);
            double sx = Math.Abs(hx - ax) < 1e-9 ? 1 : (lx - ax) / (hx - ax);
            double sy = Math.Abs(hy - ay) < 1e-9 ? 1 : (ly - ay) / (hy - ay);
            if (_handle >= 4)
            {
                if (_handle is 4 or 6) sx = 1; else sy = 1;
            }
            else if (!p.Shift)
            {
                double s = Math.Max(Math.Abs(sx), Math.Abs(sy));
                sx = Math.Sign(sx == 0 ? 1 : sx) * s;
                sy = Math.Sign(sy == 0 ? 1 : sy) * s;
            }
            if (Math.Abs(sx) < 1e-3) sx = 1e-3;
            if (Math.Abs(sy) < 1e-3) sy = 1e-3;
            var local = Affine.Translation(-ax, -ay).Then(Affine.Scale(sx, sy)).Then(Affine.Translation(ax, ay));
            t.SetTransform(local.Then(start));
        }
        RefreshTransformFrame();
    }

    // ================================================================ helpers

    /// <summary>Shows the current stamp under the pointer (stamp tool only).</summary>
    public void UpdateStampPreview()
    {
        if (_session.Tool != ToolKind.Stamp || Hover is not { } h || _session.StampOptions.StampId is not { } id || _session.Library.StampImage(id) is not { } img)
        {
            Overlay.Stamp = null;
            return;
        }
        var (x, y) = _view.ViewToDocument(h.X, h.Y);
        var placement = _session.StampPlacementAt(x, y);
        Overlay.Stamp = (img, _session.PixelMode ? placement.PixelExact(img) : placement);
    }

    private void DoStamp(in PointerInput p)
    {
        var (x, y) = Doc(p);
        _session.PlaceStamp(x, y);
    }

    private void DoFill(in PointerInput p)
    {
        if (!_session.CanEditActiveLayer()) return;
        var (x, y) = Doc(p);
        if (_session.PatternOptions.UsePattern)
        {
            _session.PatternFillAt((int)Math.Floor(x), (int)Math.Floor(y));
            return;
        }
        var options = _session.PixelMode ? _session.Fill with { Exact = true, Tolerance = 0 } : _session.Fill;
        FloodFill.Apply(Document, Document.ActiveLayer, (int)Math.Floor(x), (int)Math.Floor(y), _session.Foreground, options);
    }

    private void DoWand(in PointerInput p)
    {
        var (x, y) = Doc(p);
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (!Document.Bounds.Contains(ix, iy)) return;
        var o = _session.Selection;
        var mask = Selections.MagicWand(Document, Document.ActiveLayer, ix, iy, _session.PixelMode ? 0 : o.WandTolerance, o.WandContiguous, o.SampleMerged);
        _session.ApplySelection(mask, "Magic wand", ModeFor(p));
    }

    private ShapeOptions ShapeOptionsNow()
    {
        var o = _session.Shape;
        return _session.Tool switch
        {
            ToolKind.PixelLine => new ShapeOptions(ShapeKind.Line, false, _session.Pixel.Size, 1, AntiAlias: false),
            ToolKind.PixelRect => new ShapeOptions(ShapeKind.Rectangle, _session.Pixel.FilledShapes, 1, 1, AntiAlias: false),
            ToolKind.PixelEllipse => new ShapeOptions(ShapeKind.Ellipse, _session.Pixel.FilledShapes, 1, 1, AntiAlias: false),
            _ => o with { Width = Math.Max(1, _session.Brush.Size), Opacity = _session.Brush.Opacity, AntiAlias = !_session.PixelMode },
        };
    }

    /// <summary>Shift: 45° steps for lines, squares/circles for boxes.</summary>
    private (double X, double Y) Constrain(double x, double y, bool shift, bool angles)
    {
        if (!shift) return (x, y);
        double dx = x - _startDocX, dy = y - _startDocY;
        if (angles)
        {
            double len = Math.Sqrt(dx * dx + dy * dy), a = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
            return (_startDocX + len * Math.Cos(a), _startDocY + len * Math.Sin(a));
        }
        double s = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return (_startDocX + Math.Sign(dx == 0 ? 1 : dx) * s, _startDocY + Math.Sign(dy == 0 ? 1 : dy) * s);
    }

    private double AngleFromCentre(double x, double y) =>
        Math.Atan2(y - Viewport.Height / 2, x - Viewport.Width / 2) * 180 / Math.PI;

    private void Pick(in PointerInput p)
    {
        var (x, y) = Doc(p);
        _session.PickColor(x, y);
    }

    private StrokeSample ToSample(in PointerInput p)
    {
        var (x, y) = Doc(p);
        float pressure = p.Kind == PointerKind.Pen ? p.Pressure : MousePressure;
        return new StrokeSample(x, y, pressure);
    }
}
