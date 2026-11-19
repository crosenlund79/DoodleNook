using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Geometry;

/// <summary>A coverage mask (0–255 per pixel) for a rectangle of the document.</summary>
public sealed class CoverageMask
{
    public CoverageMask(PixelRect bounds)
    {
        Bounds = bounds;
        Data = new byte[Math.Max(0, bounds.Width) * Math.Max(0, bounds.Height)];
    }

    public PixelRect Bounds { get; }
    public byte[] Data { get; }

    public byte this[int x, int y] => Bounds.Contains(x, y) ? Data[(y - Bounds.Y) * Bounds.Width + (x - Bounds.X)] : (byte)0;
}

/// <summary>Anti-aliased polygon filling (even–odd rule) and pixel-exact shapes for pixel art.</summary>
public static class Rasterize
{
    private const int SubRows = 4;

    /// <summary>Fills polygons with anti-aliased coverage, clipped to <paramref name="clip"/>.</summary>
    public static CoverageMask Polygon(IReadOnlyList<IReadOnlyList<(double X, double Y)>> polygons, PixelRect clip, bool antialias = true)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var poly in polygons)
            foreach (var (x, y) in poly)
            {
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        var bounds = PixelRect.FromBounds(minX, minY, maxX, maxY).Intersect(clip);
        var mask = new CoverageMask(bounds);
        if (bounds.IsEmpty) return mask;

        var acc = new float[bounds.Width + 1];
        var xs = new List<double>();
        int subRows = antialias ? SubRows : 1;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            Array.Clear(acc);
            for (int s = 0; s < subRows; s++)
            {
                double sy = antialias ? y + (s + 0.5) / subRows : y + 0.5;
                xs.Clear();
                foreach (var poly in polygons)
                {
                    int n = poly.Count;
                    for (int i = 0; i < n; i++)
                    {
                        var (x0, y0) = poly[i];
                        var (x1, y1) = poly[(i + 1) % n];
                        if ((y0 <= sy && y1 > sy) || (y1 <= sy && y0 > sy))
                            xs.Add(x0 + (sy - y0) / (y1 - y0) * (x1 - x0));
                    }
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    double a = Math.Max(xs[i], bounds.X), b = Math.Min(xs[i + 1], bounds.Right);
                    if (b <= a) continue;
                    if (!antialias)
                    {
                        // Pixel centres inside the span are covered.
                        int ia = (int)Math.Ceiling(a - 0.5), ib = (int)Math.Ceiling(b - 0.5);
                        for (int x = Math.Max(ia, bounds.X); x < Math.Min(ib, bounds.Right); x++) acc[x - bounds.X] += 1;
                        continue;
                    }
                    int xa = (int)Math.Floor(a), xb = (int)Math.Floor(b);
                    if (xa == xb)
                    {
                        acc[xa - bounds.X] += (float)(b - a);
                        continue;
                    }
                    acc[xa - bounds.X] += (float)(xa + 1 - a);
                    for (int x = xa + 1; x < xb && x < bounds.Right; x++) acc[x - bounds.X] += 1;
                    if (xb < bounds.Right) acc[xb - bounds.X] += (float)(b - xb);
                }
            }
            int row = (y - bounds.Y) * bounds.Width;
            for (int x = 0; x < bounds.Width; x++)
                mask.Data[row + x] = (byte)Math.Clamp((int)(acc[x] / subRows * 255 + 0.5f), 0, 255);
        }
        return mask;
    }

    public static CoverageMask Polygon(IReadOnlyList<(double X, double Y)> polygon, PixelRect clip, bool antialias = true) =>
        Polygon(new[] { polygon }, clip, antialias);

    /// <summary>Points around an ellipse inscribed in the given box (enough points for smooth edges).</summary>
    public static List<(double X, double Y)> EllipsePoints(double left, double top, double right, double bottom)
    {
        double cx = (left + right) / 2, cy = (top + bottom) / 2, rx = Math.Abs(right - left) / 2, ry = Math.Abs(bottom - top) / 2;
        int n = Math.Clamp((int)(Math.PI * (rx + ry) / 1.5), 24, 2048);
        var pts = new List<(double, double)>(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts.Add((cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return pts;
    }

    public static List<(double X, double Y)> RectanglePoints(double left, double top, double right, double bottom) =>
        [(left, top), (right, top), (right, bottom), (left, bottom)];

    // ------------------------------------------------------------------ pixel-exact shapes

    /// <summary>Bresenham line, both end points included.</summary>
    public static void PixelLine(int x0, int y0, int x1, int y1, Action<int, int> plot)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            plot(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public static void PixelRectangle(int x0, int y0, int x1, int y1, bool filled, Action<int, int> plot)
    {
        int l = Math.Min(x0, x1), r = Math.Max(x0, x1), t = Math.Min(y0, y1), b = Math.Max(y0, y1);
        for (int y = t; y <= b; y++)
            for (int x = l; x <= r; x++)
                if (filled || y == t || y == b || x == l || x == r) plot(x, y);
    }

    /// <summary>A pixel ellipse filling the box (x0,y0)–(x1,y1) inclusive, outline or filled.</summary>
    public static void PixelEllipse(int x0, int y0, int x1, int y1, bool filled, Action<int, int> plot)
    {
        int l = Math.Min(x0, x1), r = Math.Max(x0, x1), t = Math.Min(y0, y1), b = Math.Max(y0, y1);
        double cx = (l + r) / 2.0, cy = (t + b) / 2.0, rx = (r - l) / 2.0 + 0.5, ry = (b - t) / 2.0 + 0.5;
        var inside = new bool[r - l + 1, b - t + 1];
        for (int y = t; y <= b; y++)
            for (int x = l; x <= r; x++)
            {
                double nx = (x - cx) / rx, ny = (y - cy) / ry;
                inside[x - l, y - t] = nx * nx + ny * ny <= 1.0 + 1e-9;
            }
        for (int y = t; y <= b; y++)
            for (int x = l; x <= r; x++)
            {
                if (!inside[x - l, y - t]) continue;
                if (filled) { plot(x, y); continue; }
                bool edge = x == l || x == r || y == t || y == b
                            || !inside[x - l - 1, y - t] || !inside[x - l + 1, y - t]
                            || !inside[x - l, y - t - 1] || !inside[x - l, y - t + 1];
                if (edge) plot(x, y);
            }
    }
}
