using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>
/// A smooth warp of a w × h picture by a 4 × 4 grid of points (the corners, the edges' thirds and four inner points).
/// The surface passes through every point (Catmull-Rom in both directions), so dragging a point drags the picture under
/// it; an untouched, evenly spaced grid is exactly the picture's own placement.
/// </summary>
public sealed class WarpGrid
{
    public const int N = 4;
    private readonly (double X, double Y)[] _points;

    public WarpGrid(double sourceWidth, double sourceHeight, (double X, double Y)[] points)
    {
        if (points.Length != N * N) throw new ArgumentException("A warp grid has 16 points.", nameof(points));
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        _points = ((double, double)[])points.Clone();
    }

    public double SourceWidth { get; }
    public double SourceHeight { get; }

    /// <summary>The grid points (row by row, top left first), in document coordinates.</summary>
    public IReadOnlyList<(double X, double Y)> Points => _points;

    /// <summary>A grid laid over a placement (u, v in the picture → document).</summary>
    public static WarpGrid Over(double w, double h, Func<double, double, (double X, double Y)> place)
    {
        var pts = new (double X, double Y)[N * N];
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                pts[i * N + j] = place(w * j / (N - 1), h * i / (N - 1));
        return new WarpGrid(w, h, pts);
    }

    public WarpGrid With(int index, double x, double y)
    {
        var pts = ((double, double)[])_points.Clone();
        pts[index] = (x, y);
        return new WarpGrid(SourceWidth, SourceHeight, pts);
    }

    public WarpGrid Moved(double dx, double dy) => new(SourceWidth, SourceHeight, _points.Select(p => (p.X + dx, p.Y + dy)).ToArray());

    private (double X, double Y) P(int i, int j)
    {
        // Beyond the edge, continue in a straight line (so the outer cells aren't pinched).
        if (i < 0) return Lerp(P(0, j), P(1, j), -1);
        if (i >= N) return Lerp(P(N - 2, j), P(N - 1, j), 2);
        if (j < 0) return Lerp(P(i, 0), P(i, 1), -1);
        if (j >= N) return Lerp(P(i, N - 2), P(i, N - 1), 2);
        return _points[i * N + j];
    }

    private static (double, double) Lerp((double X, double Y) a, (double X, double Y) b, double t) => (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t) =>
        0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);

    /// <summary>Where the picture's point (u, v) lands in the document.</summary>
    public (double X, double Y) Map(double u, double v)
    {
        double s = Math.Clamp(u / SourceWidth, 0, 1) * (N - 1), r = Math.Clamp(v / SourceHeight, 0, 1) * (N - 1);
        int j = Math.Min((int)s, N - 2), i = Math.Min((int)r, N - 2);
        double tu = s - j, tv = r - i;
        Span<double> xs = stackalloc double[4], ys = stackalloc double[4];
        for (int k = 0; k < 4; k++)
        {
            var a = P(i - 1 + k, j - 1);
            var b = P(i - 1 + k, j);
            var c = P(i - 1 + k, j + 1);
            var d = P(i - 1 + k, j + 2);
            xs[k] = CatmullRom(a.X, b.X, c.X, d.X, tu);
            ys[k] = CatmullRom(a.Y, b.Y, c.Y, d.Y, tu);
        }
        return (CatmullRom(xs[0], xs[1], xs[2], xs[3], tv), CatmullRom(ys[0], ys[1], ys[2], ys[3], tv));
    }
}

/// <summary>Draws a picture through a perspective transform or a warp grid (Mission 010's transform modes).</summary>
public static class WarpRendering
{
    /// <summary>Renders <paramref name="source"/> placed by a perspective transform (source rectangle → document), clipped to
    /// <paramref name="clip"/>. Returns the image and where it goes.</summary>
    /// <param name="step">1 for the real thing; more draws a quick draft (one sample per step × step block) while a handle is dragged.</param>
    public static (RasterImage Image, PixelRect Bounds) Perspective(RasterImage source, Homography toDocument, PixelRect clip, Interpolation mode, int step = 1)
    {
        int w = source.Width, h = source.Height;
        var corners = new[] { toDocument.Apply(0, 0), toDocument.Apply(w, 0), toDocument.Apply(w, h), toDocument.Apply(0, h) };
        var bounds = Bounds(corners).Intersect(clip);
        if (bounds.IsEmpty || toDocument.Invert() is not { } inv) return (new RasterImage(1, 1), new PixelRect(0, 0, 1, 1));
        var small = new RasterImage((bounds.Width + step - 1) / step, (bounds.Height + step - 1) / step);
        for (int y = 0; y < small.Height; y++)
            for (int x = 0; x < small.Width; x++)
            {
                var (u, v) = inv.Apply(bounds.X + (x + 0.5) * step, bounds.Y + (y + 0.5) * step);
                if (u < -1 || v < -1 || u > w + 1 || v > h + 1) continue;
                small.Pixels[y * small.Width + x] = Resampler.Sample(source, u, v, mode);
            }
        return (Expand(small, bounds, step), bounds);
    }

    /// <summary>The steps for a quick draft of an area: about a quarter of a million samples at most.</summary>
    public static int DraftStep(PixelRect area) => Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)area.Width * area.Height / 250_000)));

    private static RasterImage Expand(RasterImage small, PixelRect bounds, int step)
    {
        if (step == 1) return small;
        var dst = new RasterImage(bounds.Width, bounds.Height);
        for (int y = 0; y < bounds.Height; y++)
        {
            int row = y / step * small.Width;
            for (int x = 0; x < bounds.Width; x++) dst.Pixels[y * bounds.Width + x] = small.Pixels[row + x / step];
        }
        return dst;
    }

    /// <summary>Renders <paramref name="source"/> through a warp grid, clipped to <paramref name="clip"/>: the picture is cut into
    /// small triangles whose corners follow the grid's surface, and each pixel is read back from the original.</summary>
    public static (RasterImage Image, PixelRect Bounds) Warp(RasterImage source, WarpGrid grid, PixelRect clip, Interpolation mode, int step = 1)
    {
        int w = source.Width, h = source.Height;
        int gx = Math.Clamp((int)Math.Ceiling(w / 10.0), 4, 64), gy = Math.Clamp((int)Math.Ceiling(h / 10.0), 4, 64);
        var at = new (double X, double Y)[(gx + 1) * (gy + 1)];
        for (int j = 0; j <= gy; j++)
            for (int i = 0; i <= gx; i++)
                at[j * (gx + 1) + i] = grid.Map((double)w * i / gx, (double)h * j / gy);
        var bounds = Bounds(at).Intersect(clip);
        if (bounds.IsEmpty) return (new RasterImage(1, 1), new PixelRect(0, 0, 1, 1));
        // A draft rasterises into a smaller image (the corners scaled down) and is enlarged afterwards.
        if (step > 1)
            for (int k = 0; k < at.Length; k++) at[k] = ((at[k].X - bounds.X) / step, (at[k].Y - bounds.Y) / step);
        var target = step > 1 ? new PixelRect(0, 0, (bounds.Width + step - 1) / step, (bounds.Height + step - 1) / step) : bounds;
        var dst = new RasterImage(target.Width, target.Height);
        for (int j = 0; j < gy; j++)
            for (int i = 0; i < gx; i++)
            {
                int k = j * (gx + 1) + i;
                var p00 = at[k];
                var p10 = at[k + 1];
                var p01 = at[k + gx + 1];
                var p11 = at[k + gx + 2];
                double u0 = (double)w * i / gx, u1 = (double)w * (i + 1) / gx, v0 = (double)h * j / gy, v1 = (double)h * (j + 1) / gy;
                Triangle(dst, target, source, mode, p00, (u0, v0), p10, (u1, v0), p11, (u1, v1));
                Triangle(dst, target, source, mode, p00, (u0, v0), p11, (u1, v1), p01, (u0, v1));
            }
        return (Expand(dst, bounds, step), bounds);
    }

    private static void Triangle(RasterImage dst, PixelRect bounds, RasterImage src, Interpolation mode,
        (double X, double Y) a, (double U, double V) ta, (double X, double Y) b, (double U, double V) tb, (double X, double Y) c, (double U, double V) tc)
    {
        double area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        if (Math.Abs(area) < 1e-9) return;
        int x0 = Math.Max(bounds.X, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), x1 = Math.Min(bounds.Right - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int y0 = Math.Max(bounds.Y, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), y1 = Math.Min(bounds.Bottom - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        const double eps = -1e-7;
        for (int y = y0; y <= y1; y++)
        {
            double py = y + 0.5;
            for (int x = x0; x <= x1; x++)
            {
                double px = x + 0.5;
                double wa = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
                double wb = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
                double wc = 1 - wa - wb;
                if (wa < eps || wb < eps || wc < eps) continue;
                double u = wa * ta.U + wb * tb.U + wc * tc.U, v = wa * ta.V + wb * tb.V + wc * tc.V;
                dst.Pixels[(y - bounds.Y) * bounds.Width + (x - bounds.X)] = Resampler.Sample(src, u, v, mode);
            }
        }
    }

    private static PixelRect Bounds(IEnumerable<(double X, double Y)> pts)
    {
        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        foreach (var (x, y) in pts)
        {
            l = Math.Min(l, x);
            t = Math.Min(t, y);
            r = Math.Max(r, x);
            b = Math.Max(b, y);
        }
        return PixelRect.FromBounds(Math.Floor(l), Math.Floor(t), Math.Ceiling(r), Math.Ceiling(b));
    }
}
