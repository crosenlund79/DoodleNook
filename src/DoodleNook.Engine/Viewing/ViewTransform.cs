namespace DoodleNook.Engine.Viewing;

/// <summary>
/// Maps between document pixels and view coordinates (device-independent pixels of the canvas control).
/// View state lives here, never in the document: zooming or panning changes what you see, not the picture.
/// </summary>
public sealed class ViewTransform
{
    public const double MinZoom = 0.02;
    public const double MaxZoom = 64;

    /// <summary>Zoom steps used by the + / − buttons and keyboard shortcuts (as actual-pixel zoom).</summary>
    public static readonly double[] ZoomSteps =
        [0.0625, 0.0833, 0.125, 0.1667, 0.25, 0.3333, 0.5, 0.6667, 0.75, 1, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32];

    public ViewTransform(int documentWidth, int documentHeight)
    {
        DocumentWidth = documentWidth;
        DocumentHeight = documentHeight;
    }

    public int DocumentWidth { get; private set; }
    public int DocumentHeight { get; private set; }

    /// <summary>View units (DIPs) per document pixel.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>View position of the document's top-left corner (or top-right corner when flipped).</summary>
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    /// <summary>Mirror the view horizontally (handy for checking a drawing). Drawing still lands on the right pixels.</summary>
    public bool FlipHorizontal { get; private set; }

    /// <summary>Raised whenever the mapping changes.</summary>
    public event Action? Changed;

    public void SetDocumentSize(int width, int height)
    {
        DocumentWidth = width;
        DocumentHeight = height;
        Changed?.Invoke();
    }

    public (double X, double Y) DocumentToView(double x, double y)
    {
        double fx = FlipHorizontal ? DocumentWidth - x : x;
        return (OffsetX + fx * Scale, OffsetY + y * Scale);
    }

    public (double X, double Y) ViewToDocument(double x, double y)
    {
        double fx = (x - OffsetX) / Scale;
        return (FlipHorizontal ? DocumentWidth - fx : fx, (y - OffsetY) / Scale);
    }

    public void Set(double scale, double offsetX, double offsetY)
    {
        Scale = ClampScale(scale);
        OffsetX = offsetX;
        OffsetY = offsetY;
        Changed?.Invoke();
    }

    public void PanBy(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
        Changed?.Invoke();
    }

    /// <summary>Changes the scale while keeping the document point under <paramref name="viewX"/>,<paramref name="viewY"/> in place.</summary>
    public void ZoomAround(double viewX, double viewY, double newScale)
    {
        newScale = ClampScale(newScale);
        var (dx, dy) = ViewToDocument(viewX, viewY);
        Scale = newScale;
        double fx = FlipHorizontal ? DocumentWidth - dx : dx;
        OffsetX = viewX - fx * Scale;
        OffsetY = viewY - dy * Scale;
        Changed?.Invoke();
    }

    /// <summary>Fits the whole document inside a viewport, centred, with a margin, never enlarging beyond <paramref name="maxScale"/>.</summary>
    public void Fit(double viewportWidth, double viewportHeight, double margin, double maxScale)
    {
        double w = Math.Max(1, viewportWidth - 2 * margin), h = Math.Max(1, viewportHeight - 2 * margin);
        double s = Math.Min(w / DocumentWidth, h / DocumentHeight);
        s = ClampScale(Math.Min(s, maxScale));
        Scale = s;
        OffsetX = (viewportWidth - DocumentWidth * s) / 2;
        OffsetY = (viewportHeight - DocumentHeight * s) / 2;
        Changed?.Invoke();
    }

    /// <summary>Sets an exact scale, keeping the view centre on the same document point.</summary>
    public void ZoomCentered(double viewportWidth, double viewportHeight, double newScale) =>
        ZoomAround(viewportWidth / 2, viewportHeight / 2, newScale);

    /// <summary>Mirrors the view around the viewport centre (the picture stays where it is on screen).</summary>
    public void ToggleFlip(double viewportWidth)
    {
        double cx = viewportWidth / 2;
        // The document's left/right edges swap around the view centre.
        double right = OffsetX + DocumentWidth * Scale;
        FlipHorizontal = !FlipHorizontal;
        OffsetX = 2 * cx - right;
        Changed?.Invoke();
    }

    /// <summary>The next larger (direction &gt; 0) or smaller zoom step from an actual-pixel zoom.</summary>
    public static double NextStep(double currentZoom, int direction)
    {
        if (direction > 0)
        {
            foreach (var s in ZoomSteps) if (s > currentZoom * 1.001) return s;
            return ZoomSteps[^1];
        }
        for (int i = ZoomSteps.Length - 1; i >= 0; i--) if (ZoomSteps[i] < currentZoom * 0.999) return ZoomSteps[i];
        return ZoomSteps[0];
    }

    private static double ClampScale(double s) => double.IsFinite(s) ? Math.Clamp(s, MinZoom, MaxZoom) : 1;
}
