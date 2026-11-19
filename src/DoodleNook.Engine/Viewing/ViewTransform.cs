using DoodleNook.Engine.Geometry;

namespace DoodleNook.Engine.Viewing;

/// <summary>
/// Maps between document pixels and view coordinates (device-independent pixels of the canvas control):
/// zoom, pan, rotation and horizontal flip. View state lives here, never in the document — rotating or flipping the
/// view changes what you see, not the picture.
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

    /// <summary>View rotation in degrees (clockwise), −180 to 180.</summary>
    public double Rotation { get; private set; }

    /// <summary>Mirror the view horizontally (handy for checking a drawing). Drawing still lands on the right pixels.</summary>
    public bool FlipHorizontal { get; private set; }

    /// <summary>Where the centre of the document appears in the view.</summary>
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }

    public event Action? Changed;

    /// <summary>Document → view as an affine transform (for drawing).</summary>
    public Affine Matrix =>
        Affine.Translation(-DocumentWidth / 2.0, -DocumentHeight / 2.0)
            .Then(Affine.Scale(FlipHorizontal ? -Scale : Scale, Scale))
            .Then(Affine.Rotation(Rotation))
            .Then(Affine.Translation(CenterX, CenterY));

    public void SetDocumentSize(int width, int height)
    {
        DocumentWidth = width;
        DocumentHeight = height;
        Changed?.Invoke();
    }

    public (double X, double Y) DocumentToView(double x, double y) => Matrix.Apply(x, y);

    public (double X, double Y) ViewToDocument(double x, double y) => Matrix.Invert().Apply(x, y);

    /// <summary>Sets scale and the view position of the document centre.</summary>
    public void Set(double scale, double centerX, double centerY, double rotation = double.NaN)
    {
        Scale = ClampScale(scale);
        CenterX = centerX;
        CenterY = centerY;
        if (!double.IsNaN(rotation)) Rotation = NormalizeAngle(rotation);
        Changed?.Invoke();
    }

    public void PanBy(double dx, double dy)
    {
        CenterX += dx;
        CenterY += dy;
        Changed?.Invoke();
    }

    /// <summary>Changes the scale while keeping the document point under <paramref name="viewX"/>,<paramref name="viewY"/> in place.</summary>
    public void ZoomAround(double viewX, double viewY, double newScale)
    {
        var (dx, dy) = ViewToDocument(viewX, viewY);
        Scale = ClampScale(newScale);
        KeepUnder(dx, dy, viewX, viewY);
        Changed?.Invoke();
    }

    /// <summary>Rotates the view to <paramref name="degrees"/>, keeping the document point under (viewX, viewY) in place.</summary>
    public void RotateAround(double viewX, double viewY, double degrees)
    {
        var (dx, dy) = ViewToDocument(viewX, viewY);
        Rotation = NormalizeAngle(degrees);
        KeepUnder(dx, dy, viewX, viewY);
        Changed?.Invoke();
    }

    /// <summary>Fits the whole (possibly rotated) document inside a viewport, centred, never enlarging beyond <paramref name="maxScale"/>.</summary>
    public void Fit(double viewportWidth, double viewportHeight, double margin, double maxScale)
    {
        double w = Math.Max(1, viewportWidth - 2 * margin), h = Math.Max(1, viewportHeight - 2 * margin);
        double r = Rotation * Math.PI / 180, c = Math.Abs(Math.Cos(r)), s = Math.Abs(Math.Sin(r));
        double bw = DocumentWidth * c + DocumentHeight * s, bh = DocumentWidth * s + DocumentHeight * c;
        Scale = ClampScale(Math.Min(Math.Min(w / bw, h / bh), maxScale));
        CenterX = viewportWidth / 2;
        CenterY = viewportHeight / 2;
        Changed?.Invoke();
    }

    /// <summary>Sets an exact scale, keeping the view centre on the same document point.</summary>
    public void ZoomCentered(double viewportWidth, double viewportHeight, double newScale) =>
        ZoomAround(viewportWidth / 2, viewportHeight / 2, newScale);

    /// <summary>Mirrors the view around the viewport centre (the picture stays where it is on screen).</summary>
    public void ToggleFlip(double viewportWidth, double viewportHeight = double.NaN)
    {
        double vx = viewportWidth / 2, vy = double.IsNaN(viewportHeight) ? CenterY : viewportHeight / 2;
        var (dx, dy) = ViewToDocument(vx, vy);
        FlipHorizontal = !FlipHorizontal;
        Rotation = NormalizeAngle(-Rotation);
        KeepUnder(dx, dy, vx, vy);
        Changed?.Invoke();
    }

    /// <summary>Snaps the actual-pixel zoom to a whole number (pixel art), keeping the view centre in place.</summary>
    public void SnapToIntegerZoom(double viewportWidth, double viewportHeight, double renderScaling)
    {
        double actual = Scale * renderScaling;
        double snapped = actual >= 1 ? Math.Max(1, Math.Round(actual)) : actual;
        if (Math.Abs(snapped - actual) > 1e-9) ZoomCentered(viewportWidth, viewportHeight, snapped / renderScaling);
    }

    private void KeepUnder(double docX, double docY, double viewX, double viewY)
    {
        CenterX = 0;
        CenterY = 0;
        var (vx, vy) = DocumentToView(docX, docY);
        CenterX = viewX - vx;
        CenterY = viewY - vy;
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

    /// <summary>The next whole-number zoom for pixel art (1×, 2×, 3× … 64×), or a standard step below 100 %.</summary>
    public static double NextIntegerStep(double currentZoom, int direction)
    {
        if (currentZoom < 1 || (currentZoom < 1.001 && direction < 0)) return NextStep(currentZoom, direction);
        double r = Math.Round(currentZoom);
        double next = direction > 0 ? (r < 8 ? r + 1 : r * 1.5) : (r <= 8 ? r - 1 : r / 1.5);
        return Math.Clamp(Math.Round(next), 1, 64);
    }

    public static double NormalizeAngle(double degrees)
    {
        if (!double.IsFinite(degrees)) return 0;
        degrees %= 360;
        if (degrees > 180) degrees -= 360;
        if (degrees <= -180) degrees += 360;
        return degrees;
    }

    private static double ClampScale(double s) => double.IsFinite(s) ? Math.Clamp(s, MinZoom, MaxZoom) : 1;
}
