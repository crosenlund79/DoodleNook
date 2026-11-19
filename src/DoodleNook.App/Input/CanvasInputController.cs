using DoodleNook.App.Editor;
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
/// pressure 0–1, and which buttons/flags are active. Pen side buttons may arrive as <see cref="Secondary"/> (barrel or
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
    bool Alt = false);

public enum Gesture
{
    None,
    Paint,
    Pan,
    Pick,
    ResizeBrush,
}

public enum CursorShape
{
    BrushOutline,
    Hand,
    Grabbing,
    Eyedropper,
    Resize,
}

/// <summary>
/// Decides what a pointer gesture on the canvas means (paint, erase, pan, pick a colour, resize the brush) and forwards
/// it to the <see cref="EditorSession"/> or the <see cref="ViewTransform"/>. It never touches pixels itself and knows
/// nothing about the UI framework, so it can be unit-tested with synthetic input.
/// </summary>
public sealed class CanvasInputController
{
    /// <summary>Pressure used for the mouse (and any device without pressure): the full slider size and opacity.</summary>
    public const float MousePressure = 1f;

    private readonly EditorSession _session;
    private readonly ViewTransform _view;
    private readonly List<StrokeSample> _buffer = new(64);
    private long _pointerId = -1;
    private double _lastX, _lastY;

    public CanvasInputController(EditorSession session, ViewTransform view)
    {
        _session = session;
        _view = view;
    }

    public Gesture Gesture { get; private set; }

    /// <summary>The kind of device that started the current gesture.</summary>
    public PointerKind ActiveKind { get; private set; }

    /// <summary>True while the space bar is held (temporary hand tool).</summary>
    public bool SpaceHeld { get; set; }

    /// <summary>True while Alt is held (temporary eyedropper).</summary>
    public bool AltHeld { get; set; }

    /// <summary>Last pointer position over the canvas in view coordinates, or null when the pointer is away.</summary>
    public (double X, double Y)? Hover { get; private set; }

    /// <summary>True when the current stroke erases (eraser tool or the pen's eraser end).</summary>
    public bool StrokeErases { get; private set; }

    /// <summary>Where a brush-resize drag started (view coordinates) and the size at that moment.</summary>
    public (double X, double Y) ResizeAnchor { get; private set; }
    public double ResizeStartSize { get; private set; }

    /// <summary>Raised when the user pans the view with the hand tool, space or middle button.</summary>
    public event Action? ViewPannedByUser;

    /// <summary>Raised when something that affects the cursor or overlays changed.</summary>
    public event Action? OverlayChanged;

    public CursorShape Cursor => Gesture switch
    {
        Gesture.Pan => CursorShape.Grabbing,
        Gesture.Pick => CursorShape.Eyedropper,
        Gesture.ResizeBrush => CursorShape.Resize,
        Gesture.Paint => CursorShape.BrushOutline,
        _ when SpaceHeld || _session.Tool == ToolKind.Hand => CursorShape.Hand,
        _ when AltHeld || _session.Tool == ToolKind.Eyedropper => CursorShape.Eyedropper,
        _ => CursorShape.BrushOutline,
    };

    /// <summary>Handles a press. Returns true when a gesture started (the caller should capture the pointer).</summary>
    public bool Press(in PointerInput p)
    {
        if (Gesture != Gesture.None) return false; // one gesture at a time; ignore other pointers and extra buttons

        Hover = (p.X, p.Y);
        Gesture g = Classify(p);
        if (g == Gesture.None) return false;

        Gesture = g;
        ActiveKind = p.Kind;
        _pointerId = p.PointerId;
        _lastX = p.X;
        _lastY = p.Y;

        switch (g)
        {
            case Gesture.Paint:
                StrokeErases = p.Eraser || _session.Tool == ToolKind.Eraser;
                _session.BeginStroke(ToSample(p), forceEraser: p.Eraser);
                break;
            case Gesture.Pick:
                Pick(p);
                break;
            case Gesture.ResizeBrush:
                ResizeAnchor = (p.X, p.Y);
                ResizeStartSize = _session.CurrentPaintSettings.Size;
                break;
        }
        OverlayChanged?.Invoke();
        return true;
    }

    private Gesture Classify(in PointerInput p)
    {
        if (p.Kind == PointerKind.Touch) return Gesture.Pan; // a finger never paints (palm safety)
        if (p.Middle) return Gesture.Pan;
        if (SpaceHeld || _session.Tool == ToolKind.Hand) return p.Primary || p.Secondary ? Gesture.Pan : Gesture.None;
        bool paintTool = _session.Tool is ToolKind.Brush or ToolKind.Eraser;
        if (p.Secondary && paintTool) return Gesture.ResizeBrush;
        if (!p.Primary) return Gesture.None;
        if (p.Alt || AltHeld || _session.Tool == ToolKind.Eyedropper) return Gesture.Pick;
        return paintTool ? Gesture.Paint : Gesture.None;
    }

    /// <summary>Handles movement. <paramref name="points"/> are all samples since the last event, oldest first.</summary>
    public void Move(ReadOnlySpan<PointerInput> points)
    {
        if (points.IsEmpty) return;
        var last = points[^1];

        if (Gesture == Gesture.None)
        {
            Hover = (last.X, last.Y);
            OverlayChanged?.Invoke();
            return;
        }
        // While painting, only the pointer that started the stroke counts (stray mouse/touch input is ignored).
        // Pan, pick and resize accept any pointer: a Huion side button mapped to a mouse click starts the gesture
        // as "mouse", but the movement then arrives from the pen.
        if (Gesture == Gesture.Paint && last.PointerId != _pointerId) return;

        Hover = (last.X, last.Y);
        switch (Gesture)
        {
            case Gesture.Paint:
                _buffer.Clear();
                foreach (var p in points)
                    if (p.PointerId == _pointerId) _buffer.Add(ToSample(p));
                _session.ContinueStroke(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer));
                break;
            case Gesture.Pan:
                _view.PanBy(last.X - _lastX, last.Y - _lastY);
                ViewPannedByUser?.Invoke();
                break;
            case Gesture.Pick:
                Pick(last);
                break;
            case Gesture.ResizeBrush:
                double dx = last.X - ResizeAnchor.X;
                _session.SetSize(ResizeStartSize + 2 * dx / _view.Scale);
                break;
        }
        _lastX = last.X;
        _lastY = last.Y;
        OverlayChanged?.Invoke();
    }

    /// <summary>Handles the release that ends a gesture (pen lifted, button released).</summary>
    public void Release(in PointerInput p)
    {
        if (Gesture == Gesture.None) return;
        if (Gesture == Gesture.Paint && p.PointerId != _pointerId) return;
        if (Gesture != Gesture.Paint && p.PointerId != _pointerId && (p.Primary || p.Secondary || p.Middle)) return;
        if (Gesture == Gesture.Paint)
        {
            // The lift-off position completes the stroke (pressure is often reported as 0 here, which gives a natural taper).
            if (p.X != _lastX || p.Y != _lastY)
            {
                _buffer.Clear();
                _buffer.Add(ToSample(p));
                _session.ContinueStroke(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer));
            }
            _session.EndStroke();
        }
        Finish();
    }

    /// <summary>The gesture was interrupted (capture lost, window deactivated, pointer cancelled). Paint so far is kept.</summary>
    public void Interrupt()
    {
        if (Gesture == Gesture.Paint && _session.IsStrokeActive) _session.EndStroke();
        Finish();
    }

    /// <summary>The pointer left the canvas while not drawing.</summary>
    public void Leave()
    {
        if (Gesture != Gesture.None) return;
        Hover = null;
        OverlayChanged?.Invoke();
    }

    private void Finish()
    {
        Gesture = Gesture.None;
        _pointerId = -1;
        StrokeErases = false;
        OverlayChanged?.Invoke();
    }

    private void Pick(in PointerInput p)
    {
        var (x, y) = _view.ViewToDocument(p.X, p.Y);
        _session.PickColor(x, y);
    }

    private StrokeSample ToSample(in PointerInput p)
    {
        var (x, y) = _view.ViewToDocument(p.X, p.Y);
        float pressure = p.Kind == PointerKind.Pen ? p.Pressure : MousePressure;
        return new StrokeSample(x, y, pressure);
    }
}
