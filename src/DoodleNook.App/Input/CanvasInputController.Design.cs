using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Input;

/// <summary>
/// Mission 008 gestures: handles on text and shapes (move inside, resize at the handles, turn just outside a corner),
/// the Text tool, drawing editable shapes, guides (drag from the rulers; move or remove with the Move tool), the
/// symmetry origin and vanishing points (Move tool), strokes held to perspective lines, and Shift for straight lines.
/// </summary>
public sealed partial class CanvasInputController
{
    private const double HandleReach = 10;   // view units around a handle
    private const double RotateReach = 30;   // view units outside a corner that turn the object
    private const double SnapReach = 6;      // view units within which snapping sticks

    private Layer? _object;
    private LayerContent? _objectStart;
    private ObjectBox _boxStart;
    private int _objectHandle = -1; // 0–3 corners, 4–7 edges, 8 inside (move), 9 turn; lines: 10 start, 11 end
    private bool _clickEditsText;
    private int _guideIndex = -1;
    private bool _guideVertical;
    private bool _guideFromRuler;
    private int _assistHandle = -1; // 0 symmetry origin, 1 first vanishing point, 2 second
    private (double X, double Y)? _lastStrokeEnd;
    private float _linePressure;
    private (double Dx, double Dy)? _perspectiveDir;
    private bool _perspectiveStroke;
    private readonly List<StrokeSample> _held = new();
    private CursorShape? _hoverCursor;

    /// <summary>The Text tool wants the text editor for a layer (true: the layer was just created).</summary>
    public event Action<Layer, bool>? TextEditRequested;

    /// <summary>The text or shape layer whose frame and handles are shown now, or null.</summary>
    public Layer? FramedObject
    {
        get
        {
            if (_session.ActiveObject is not { } layer || !Document.IsShown(layer) || _session.ActiveTransform is not null) return null;
            return _session.Tool switch
            {
                ToolKind.Move => layer,
                ToolKind.Text when layer.Kind == LayerKind.Text => layer,
                ToolKind.Shape when layer.Kind == LayerKind.Shape && _session.DesignShape.Editable && !_session.PixelMode => layer,
                _ => null,
            };
        }
    }

    /// <summary>The symmetry origin and vanishing points can be dragged (Move tool) when they are shown.</summary>
    public bool AssistHandlesActive => _session.Tool == ToolKind.Move && Gesture is Gesture.None or Gesture.AssistHandle;

    private (double X, double Y) ToView(double x, double y) => _view.DocumentToView(x, y);

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    // ================================================================== classification

    private Gesture? ClassifyDesign(in PointerInput p, ToolKind tool)
    {
        _clickEditsText = false;
        var (x, y) = Doc(p);
        switch (tool)
        {
            case ToolKind.Move:
            {
                if (HitAssistHandle(p.X, p.Y) is var h and >= 0)
                {
                    _assistHandle = h;
                    return Gesture.AssistHandle;
                }
                if (HitGuide(p.X, p.Y) is var g and >= 0)
                {
                    _guideIndex = g;
                    _guideVertical = Document.Guides[g].Vertical;
                    _guideFromRuler = false;
                    return Gesture.Guide;
                }
                if (p.Ctrl && TopObjectAt(x, y, null) is { } picked)
                {
                    Document.SetActiveLayer(picked);
                    return StartObject(picked, 8);
                }
                if (_session.ActiveObject is { } active && Document.IsShown(active))
                {
                    int hit = HitObject(active, p.X, p.Y);
                    if (hit >= 0) return StartObject(active, hit);
                    if (TopObjectAt(x, y, null) is { } other)
                    {
                        Document.SetActiveLayer(other);
                        return StartObject(other, 8);
                    }
                    return Gesture.None; // a click beside the object keeps it chosen
                }
                return null;
            }
            case ToolKind.Text:
            {
                if (_session.ActiveObject is { Kind: LayerKind.Text } text && Document.IsShown(text))
                {
                    int hit = HitObject(text, p.X, p.Y);
                    if (hit is >= 0 and not 8) return StartObject(text, hit);
                }
                if (TopObjectAt(x, y, LayerKind.Text) is { } under)
                {
                    Document.SetActiveLayer(under);
                    _clickEditsText = true; // a click edits; a drag moves it
                    return StartObject(under, 8);
                }
                return Gesture.NewText;
            }
            case ToolKind.Shape when _session.DesignShape.Editable && !_session.PixelMode:
            {
                if (_session.ActiveObject is { Kind: LayerKind.Shape } shape && Document.IsShown(shape))
                {
                    int hit = HitObject(shape, p.X, p.Y);
                    if (hit >= 0) return StartObject(shape, hit);
                }
                return Gesture.NewShape;
            }
            case ToolKind.Brush or ToolKind.Eraser when p.Shift:
                return Gesture.StraightLine;
        }
        return null;
    }

    private Gesture StartObject(Layer layer, int handle)
    {
        _object = layer;
        _objectHandle = handle;
        return Gesture.ObjectEdit;
    }

    private void PressDesign(Gesture g, in PointerInput p)
    {
        switch (g)
        {
            case Gesture.ObjectEdit:
                if (_object is null || _object.Locked || Document.BoxOf(_object) is not { } box)
                {
                    if (_object is { Locked: true }) _session.EditObject(c => c, ""); // tells that it is locked
                    Gesture = Gesture.None;
                    break;
                }
                _objectStart = _object.Content;
                _boxStart = box;
                break;
            case Gesture.NewShape:
                _object = null;
                if (_session.SnapTargetsFor(null) is { } targets)
                {
                    var (sx, sy, _, _) = Snapping.Point(targets, _startDocX, _startDocY, SnapReach / _view.Scale);
                    (_startDocX, _startDocY) = (sx, sy);
                }
                break;
            case Gesture.StraightLine:
                if (!_session.CanEditActiveLayer())
                {
                    Gesture = Gesture.None;
                    break;
                }
                _linePressure = p.Kind == PointerKind.Pen ? p.Pressure : MousePressure;
                Overlay.Line = (_startDocX, _startDocY, _startDocX, _startDocY);
                break;
            case Gesture.Guide:
                if (Document.GuidesLocked)
                {
                    Gesture = Gesture.None;
                    break;
                }
                Overlay.GuidePreview = (_guideVertical, Document.Guides[_guideIndex].Position);
                break;
        }
    }

    // ================================================================== dragging

    private void MoveDesign(in PointerInput p, double x, double y)
    {
        switch (Gesture)
        {
            case Gesture.ObjectEdit:
                if (_moved) DragObject(p, x, y);
                break;
            case Gesture.NewShape:
            {
                if (!_moved) break;
                var (ex, ey) = (x, y);
                Overlay.SnapX = Overlay.SnapY = null;
                if (_session.SnapTargetsFor(_object) is { } targets && !p.Shift)
                {
                    var snapped = Snapping.Point(targets, x, y, SnapReach / _view.Scale);
                    (ex, ey) = (snapped.X, snapped.Y);
                    Overlay.SnapX = snapped.LineX;
                    Overlay.SnapY = snapped.LineY;
                }
                var content = _session.ShapeFromDrag(_startDocX, _startDocY, ex, ey, p.Shift);
                // The layer is added on the first movement; the rest of the drag only shapes it (one undo step).
                if (_object is null) _object = _session.AddShape(content);
                else Document.PreviewContent(_object, content);
                break;
            }
            case Gesture.NewText:
                Overlay.Rect = _moved ? (_startDocX, _startDocY, x, _startDocY + _session.Text.Size * 1.2) : null;
                break;
            case Gesture.Guide:
            {
                double pos = _guideVertical ? x : y;
                Overlay.SnapX = Overlay.SnapY = null;
                if (_session.Assist.Snap && SnapTargets.For(Document, new SnapOptions(Guides: false)).Nearest(pos, _guideVertical, SnapReach / _view.Scale) is { } s)
                    pos = s;
                Overlay.GuidePreview = (_guideVertical, pos);
                break;
            }
            case Gesture.AssistHandle:
                DragAssistHandle(x, y);
                break;
            case Gesture.StraightLine:
            {
                if (p.Kind == PointerKind.Pen) _linePressure = Math.Max(_linePressure, p.Pressure);
                var (ex, ey) = StraightLine.Constrain(_startDocX, _startDocY, x, y, p.Ctrl);
                Overlay.Line = (_startDocX, _startDocY, ex, ey);
                break;
            }
        }
    }

    private void DragObject(in PointerInput p, double x, double y)
    {
        if (_object is null || _objectStart is null) return;
        var start = _objectStart;
        var box = _boxStart;
        LayerContent next;
        Overlay.SnapX = Overlay.SnapY = null;
        switch (_objectHandle)
        {
            case 8:
            {
                double mx = x - _startDocX, my = y - _startDocY;
                if (p.Shift)
                {
                    if (Math.Abs(mx) > Math.Abs(my)) my = 0;
                    else mx = 0;
                }
                if (_session.SnapTargetsFor(_object) is { } targets)
                {
                    var (l, t, r, b) = box.Extent();
                    var snap = Snapping.Box(targets, l + mx, t + my, r + mx, b + my, SnapReach / _view.Scale);
                    mx += snap.Dx;
                    my += snap.Dy;
                    Overlay.SnapX = snap.LineX;
                    Overlay.SnapY = snap.LineY;
                }
                next = start.Translated(mx, my);
                break;
            }
            case 9:
            {
                var (cx, cy) = box.Center;
                double a0 = Math.Atan2(_startDocY - cy, _startDocX - cx), a1 = Math.Atan2(y - cy, x - cx);
                double deg = box.Rotation + (a1 - a0) * 180 / Math.PI;
                if (p.Shift) deg = Math.Round(deg / 15) * 15;
                deg = Math.Round(((deg % 360) + 540) % 360 - 180, 2);
                next = start switch
                {
                    TextContent t => t with { Rotation = deg },
                    ShapeContent s => s with { Rotation = deg },
                    _ => start,
                };
                break;
            }
            case 10 or 11 when start is ShapeContent line:
            {
                var a = box.ToDocument(line.X, line.Y);
                var b = box.ToDocument(line.X + line.Width, line.Y + line.Height);
                var (fx, fy) = _objectHandle == 10 ? b : a;
                var (px, py) = StraightLine.Constrain(fx, fy, x, y, p.Shift);
                if (_session.SnapTargetsFor(_object) is { } targets && !p.Shift)
                {
                    var s = Snapping.Point(targets, px, py, SnapReach / _view.Scale);
                    (px, py) = (s.X, s.Y);
                    Overlay.SnapX = s.LineX;
                    Overlay.SnapY = s.LineY;
                }
                var (sx, sy, ex, ey) = _objectHandle == 10 ? (px, py, fx, fy) : (fx, fy, px, py);
                next = line with { X = sx, Y = sy, Width = ex - sx, Height = ey - sy, Rotation = 0 };
                break;
            }
            default:
                next = Resize(start, box, _objectHandle, x, y, p.Shift);
                break;
        }
        Document.PreviewContent(_object, next);
    }

    /// <summary>Resizes from a handle, keeping the opposite handle where it is (in the object's own, turned frame).
    /// Shapes: corners keep proportions only with Shift. Text: corners scale the lettering; the side handles set the
    /// width lines wrap at.</summary>
    private static LayerContent Resize(LayerContent start, ObjectBox box, int handle, double x, double y, bool shift)
    {
        (double fx, double fy) = handle switch
        {
            0 => (0.0, 0.0), 1 => (1.0, 0.0), 2 => (1.0, 1.0), 3 => (0.0, 1.0),
            4 => (0.5, 0.0), 5 => (1.0, 0.5), 6 => (0.5, 1.0), _ => (0.0, 0.5),
        };
        double ax = 1 - fx, ay = 1 - fy; // the anchor (opposite handle), as a fraction of the box
        var anchor = box.ToDocument(box.X + ax * box.Width, box.Y + ay * box.Height);
        double rad = box.Rotation * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        double dx = x - anchor.X, dy = y - anchor.Y;
        double du = dx * cos + dy * sin, dv = -dx * sin + dy * cos; // pointer in the box's own axes
        double w = box.Width, h = box.Height;
        double signU = fx > ax ? 1 : -1, signV = fy > ay ? 1 : -1;
        if (fx != 0.5) w = Math.Max(1, du * signU);
        if (fy != 0.5) h = Math.Max(1, dv * signV);
        bool corner = handle < 4;
        if (start is TextContent && corner || shift && corner)
        {
            double k = Math.Max(w / Math.Max(1e-6, box.Width), h / Math.Max(1e-6, box.Height));
            w = box.Width * k;
            h = box.Height * k;
        }
        // New centre: from the anchor along the box's axes.
        double ux = signU * w / 2 * (fx == 0.5 ? 0 : 1), vy = signV * h / 2 * (fy == 0.5 ? 0 : 1);
        double cuOffset = fx == 0.5 ? (0.5 - ax) * box.Width : ux, cvOffset = fy == 0.5 ? (0.5 - ay) * box.Height : vy;
        double cx = anchor.X + cuOffset * cos - cvOffset * sin, cy = anchor.Y + cuOffset * sin + cvOffset * cos;
        switch (start)
        {
            case ShapeContent s:
                return s with { X = cx - w / 2, Y = cy - h / 2, Width = w, Height = h };
            case TextContent t when corner:
            {
                double k = w / Math.Max(1e-6, box.Width);
                double size = Math.Clamp(t.Size * k, 1, 2000);
                k = size / t.Size;
                double nw = box.Width * k, nh = box.Height * k;
                return t with
                {
                    Size = Math.Round(size, 1), LetterSpacing = t.LetterSpacing * k, BoxWidth = t.BoxWidth * k,
                    X = cx - nw / 2, Y = cy - nh / 2,
                    Outline = t.Outline is { } o ? o with { Width = o.Width * k } : null,
                };
            }
            case TextContent t when fx != 0.5:
                // Side handles: the wrapping width (text height then follows the wrapped lines).
                return t with { BoxWidth = Math.Round(w), X = cx - w / 2, Y = cy - box.Height / 2 };
            default:
                return start;
        }
    }

    private void DragAssistHandle(double x, double y)
    {
        switch (_assistHandle)
        {
            case 0:
                if (_session.PixelMode) (x, y) = (Math.Round(x * 2) / 2, Math.Round(y * 2) / 2); // pixel centres or edges
                _session.SetAssistOptions(_session.Assist with { OriginX = Math.Round(x, 1), OriginY = Math.Round(y, 1) });
                break;
            case 1:
                Document.SetPerspective(Document.Perspective with { V1X = x, V1Y = y });
                break;
            case 2:
                Document.SetPerspective(Document.Perspective with { V2X = x, V2Y = y });
                break;
        }
    }

    // ================================================================== release

    private void ReleaseDesign(in PointerInput p, double x, double y)
    {
        switch (Gesture)
        {
            case Gesture.ObjectEdit:
                if (_object is not null && _objectStart is not null)
                {
                    if (_moved)
                        Document.CommitContent(_object, _objectStart, _objectHandle switch { 8 => "Move", 9 => "Rotate", _ => "Resize" });
                    else if (_clickEditsText || (_session.Tool == ToolKind.Text && _objectHandle == 8))
                        TextEditRequested?.Invoke(_object, false);
                }
                break;
            case Gesture.NewText:
            {
                Overlay.Rect = null;
                double size = _session.Text.Size;
                double left = _moved ? Math.Min(_startDocX, x) : _startDocX;
                var content = _session.NewText(left, _startDocY - size * 0.75);
                if (_moved && Math.Abs(x - _startDocX) * _view.Scale > 12) content = content with { BoxWidth = Math.Round(Math.Abs(x - _startDocX)) };
                var layer = _session.AddText(content);
                TextEditRequested?.Invoke(layer, true);
                break;
            }
            case Gesture.Guide:
            {
                var preview = Overlay.GuidePreview;
                Overlay.GuidePreview = null;
                if (preview is not { } g || _guideFromRuler) break;
                double max = g.Vertical ? Document.Width : Document.Height;
                // Dragged off the picture (back towards the ruler): the guide goes away.
                if (g.Position < -2 / _view.Scale || g.Position > max + 2 / _view.Scale) _session.RemoveGuide(_guideIndex);
                else if (_moved) _session.MoveGuide(_guideIndex, g.Position);
                break;
            }
            case Gesture.StraightLine:
            {
                Overlay.Line = null;
                var (ex, ey) = StraightLine.Constrain(_startDocX, _startDocY, x, y, p.Ctrl);
                double sx = _startDocX, sy = _startDocY;
                if (!_moved && _lastStrokeEnd is { } last) (sx, sy, ex, ey) = (last.X, last.Y, _startDocX, _startDocY); // Shift+click: from the last stroke
                float pressure = Math.Max(0.05f, _linePressure);
                StrokeErases = p.Eraser || _session.Tool == ToolKind.Eraser;
                _session.BeginStroke(new StrokeSample(sx, sy, pressure), forceEraser: p.Eraser);
                if (_session.IsStrokeActive)
                {
                    _buffer.Clear();
                    _buffer.Add(new StrokeSample(ex, ey, pressure));
                    _session.ContinueStroke(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer));
                    _session.EndStroke();
                    _lastStrokeEnd = (ex, ey);
                }
                break;
            }
        }
        _object = null;
        _objectStart = null;
        _objectHandle = -1;
        _assistHandle = -1;
        _guideIndex = -1;
        Overlay.SnapX = Overlay.SnapY = null;
    }

    private void InterruptDesign()
    {
        switch (Gesture)
        {
            case Gesture.ObjectEdit when _object is not null && _objectStart is not null && _moved:
                Document.CommitContent(_object, _objectStart, "Move");
                break;
        }
        Overlay.GuidePreview = null;
        Overlay.SnapX = Overlay.SnapY = null;
        _object = null;
        _objectStart = null;
    }

    // ================================================================== rulers

    /// <summary>A guide dragged out of a ruler (the ruler forwards the drag in canvas view coordinates).</summary>
    public void BeginRulerGuide(bool vertical)
    {
        if (Gesture != Gesture.None || Document.GuidesLocked) return;
        Gesture = Gesture.Guide;
        _guideFromRuler = true;
        _guideIndex = -1;
        _guideVertical = vertical;
        if (!_session.ShowGuides) _session.SetShowGuides(true);
    }

    public void DragRulerGuide(double vx, double vy)
    {
        if (Gesture != Gesture.Guide || !_guideFromRuler) return;
        var (x, y) = _view.ViewToDocument(vx, vy);
        double pos = _guideVertical ? x : y;
        if (_session.Assist.Snap && SnapTargets.For(Document, new SnapOptions(Guides: false)).Nearest(pos, _guideVertical, SnapReach / _view.Scale) is { } s)
            pos = s;
        Overlay.GuidePreview = (_guideVertical, pos);
        OverlayChanged?.Invoke();
    }

    /// <summary>Ends a ruler drag: dropped on the canvas adds the guide; dropped back on a ruler adds nothing.</summary>
    public void EndRulerGuide(bool overCanvas)
    {
        if (Gesture != Gesture.Guide || !_guideFromRuler) return;
        if (overCanvas && Overlay.GuidePreview is { } g) _session.AddGuide(g.Vertical, g.Position);
        Overlay.GuidePreview = null;
        _guideFromRuler = false;
        Finish();
    }

    // ================================================================== hit testing

    /// <summary>The topmost shown text or shape layer (of a kind, or any) whose frame contains a document point.</summary>
    public Layer? TopObjectAt(double x, double y, LayerKind? kind)
    {
        var layers = Document.Layers;
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            var l = layers[i];
            if (!l.IsObject || (kind is { } k && l.Kind != k) || !Document.IsShown(l) || Document.BoxOf(l) is not { } box) continue;
            if (l.Content is ShapeContent { Kind: ShapeType.Line } line)
            {
                var a = box.ToDocument(line.X, line.Y);
                var b = box.ToDocument(line.X + line.Width, line.Y + line.Height);
                if (SegmentDistance(x, y, a.X, a.Y, b.X, b.Y) <= Math.Max(line.StrokeWidth / 2, 6 / _view.Scale)) return l;
                continue;
            }
            if (box.Contains(x, y, 2 / _view.Scale)) return l;
        }
        return null;
    }

    private static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay, len2 = vx * vx + vy * vy;
        double t = len2 < 1e-12 ? 0 : Math.Clamp((wx * vx + wy * vy) / len2, 0, 1);
        return Dist(px, py, ax + t * vx, ay + t * vy);
    }

    /// <summary>Handle positions of an object's frame in document coordinates (0–3 corners, 4–7 edge middles).</summary>
    public static (double X, double Y)[] FrameHandles(ObjectBox box)
    {
        var pts = new (double, double)[8];
        (double, double)[] f = [(0, 0), (1, 0), (1, 1), (0, 1), (0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)];
        for (int i = 0; i < 8; i++) pts[i] = box.ToDocument(box.X + f[i].Item1 * box.Width, box.Y + f[i].Item2 * box.Height);
        return pts;
    }

    /// <summary>Which part of an object's frame is under a view point: 0–7 handles, 8 inside, 9 turn zone, 10/11 line
    /// ends, −1 elsewhere.</summary>
    public int HitObject(Layer layer, double vx, double vy)
    {
        if (Document.BoxOf(layer) is not { } box) return -1;
        if (layer.Content is ShapeContent { Kind: ShapeType.Line } line)
        {
            var a = ToView(box.ToDocument(line.X, line.Y).X, box.ToDocument(line.X, line.Y).Y);
            var bEnd = box.ToDocument(line.X + line.Width, line.Y + line.Height);
            var b = ToView(bEnd.X, bEnd.Y);
            if (Dist(a.X, a.Y, vx, vy) <= HandleReach) return 10;
            if (Dist(b.X, b.Y, vx, vy) <= HandleReach) return 11;
            return SegmentDistance(vx, vy, a.X, a.Y, b.X, b.Y) <= Math.Max(8, line.StrokeWidth * _view.Scale / 2 + 4) ? 8 : -1;
        }
        var handles = FrameHandles(box).Select(h => ToView(h.X, h.Y)).ToArray();
        double viewW = Dist(handles[0].X, handles[0].Y, handles[1].X, handles[1].Y), viewH = Dist(handles[0].X, handles[0].Y, handles[3].X, handles[3].Y);
        for (int i = 0; i < 8; i++)
        {
            if (i is 4 or 6 && viewW < 44) continue; // small frames: keep room to grab the inside
            if (i is 5 or 7 && viewH < 44) continue;
            if (i < 4 && (viewW < 20 || viewH < 20)) continue;
            if (Math.Abs(handles[i].X - vx) <= HandleReach && Math.Abs(handles[i].Y - vy) <= HandleReach) return i;
        }
        var (dx, dy) = _view.ViewToDocument(vx, vy);
        if (box.Contains(dx, dy)) return 8;
        for (int i = 0; i < 4; i++)
            if (Dist(handles[i].X, handles[i].Y, vx, vy) <= RotateReach) return 9;
        return -1;
    }

    /// <summary>The guide under a view point (within a few view units), when guides are shown and unlocked.</summary>
    public int HitGuide(double vx, double vy)
    {
        if (!_session.ShowGuides || Document.GuidesLocked) return -1;
        var guides = Document.Guides;
        var (x, y) = _view.ViewToDocument(vx, vy);
        double reach = 5 / _view.Scale;
        int best = -1;
        double bestD = reach;
        for (int i = 0; i < guides.Count; i++)
        {
            double d = Math.Abs((guides[i].Vertical ? x : y) - guides[i].Position);
            if (d <= bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>The symmetry origin (0) or a vanishing point (1, 2) under a view point, else −1.</summary>
    public int HitAssistHandle(double vx, double vy)
    {
        var persp = Document.Perspective;
        if (persp.Points >= 1 && _session.Assist.ShowPerspective)
        {
            var v1 = ToView(persp.V1X, persp.V1Y);
            if (Dist(v1.X, v1.Y, vx, vy) <= 12) return 1;
            if (persp.Points >= 2)
            {
                var v2 = ToView(persp.V2X, persp.V2Y);
                if (Dist(v2.X, v2.Y, vx, vy) <= 12) return 2;
            }
        }
        if (CurrentSymmetry != Symmetry.None)
        {
            var o = ToView(_session.Assist.Placement.OriginX(Document.Width), _session.Assist.Placement.OriginY(Document.Height));
            if (Dist(o.X, o.Y, vx, vy) <= 12) return 0;
        }
        return -1;
    }

    /// <summary>The symmetry in use for the current tool (pixel tools have their own setting).</summary>
    public Symmetry CurrentSymmetry => _session.IsPixelTool(_session.Tool) || _session.PixelMode ? _session.Pixel.Symmetry : _session.Symmetry;

    /// <summary>The cursor over a handle when nothing is being dragged.</summary>
    private void UpdateHoverCursor(double vx, double vy)
    {
        _hoverCursor = null;
        if (_session.Tool == ToolKind.Move && (HitAssistHandle(vx, vy) >= 0 || HitGuide(vx, vy) >= 0))
        {
            _hoverCursor = CursorShape.Move;
            return;
        }
        if (FramedObject is not { } layer) return;
        _hoverCursor = HitObject(layer, vx, vy) switch
        {
            8 => _session.Tool == ToolKind.Text ? CursorShape.Text : CursorShape.Move,
            9 => CursorShape.Rotate,
            >= 0 and < 8 or 10 or 11 => CursorShape.Resize,
            _ => null,
        };
    }

    // ================================================================== perspective-assisted strokes

    private bool PerspectiveAssistOn => Document.Perspective.Points > 0 && _session.Assist.PerspectiveAssist && _session.Assist.ShowPerspective;

    /// <summary>Starts a stroke: with the perspective assistant, the stroke waits for a little movement, then follows
    /// the perspective line that matches its direction best.</summary>
    private void BeginAssistedStroke()
    {
        _perspectiveStroke = PerspectiveAssistOn;
        _perspectiveDir = null;
        _held.Clear();
    }

    /// <summary>Samples of a stroke after the assistant (projected onto the chosen line, or held until it is chosen).</summary>
    private void AssistSamples(List<StrokeSample> samples)
    {
        if (!_perspectiveStroke) return;
        if (_perspectiveDir is null)
        {
            _held.AddRange(samples);
            samples.Clear();
            var last = _held[^1];
            if (Dist(last.X, last.Y, _startDocX, _startDocY) * _view.Scale < 10) return;
            _perspectiveDir = Perspective.Choose(Document.Perspective, _startDocX, _startDocY, last.X, last.Y);
            samples.AddRange(_held);
            _held.Clear();
        }
        if (_perspectiveDir is not { } d) return;
        for (int i = 0; i < samples.Count; i++)
        {
            var (px, py) = Perspective.Project(_startDocX, _startDocY, d, samples[i].X, samples[i].Y);
            samples[i] = samples[i] with { X = px, Y = py };
        }
    }

    /// <summary>At the end of a stroke: samples still held (a very short stroke) are drawn as they were.</summary>
    private void FlushAssistedStroke()
    {
        if (_held.Count > 0)
        {
            _buffer.Clear();
            _buffer.AddRange(_held);
            _held.Clear();
            _session.ContinueStroke(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer));
        }
        _perspectiveStroke = false;
    }

    /// <summary>Remembers where a stroke ended (Shift+click draws a straight line from there).</summary>
    private void RememberStrokeEnd(double x, double y) => _lastStrokeEnd = (x, y);
}
