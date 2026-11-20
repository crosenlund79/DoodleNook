using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>A box (X, Y, Width, Height) turned by Rotation degrees around its centre: the frame of a text or shape layer.</summary>
public readonly record struct ObjectBox(double X, double Y, double Width, double Height, double Rotation)
{
    public (double X, double Y) Center => (X + Width / 2, Y + Height / 2);

    /// <summary>A point in document coordinates turned into the box's own (unrotated) coordinates.</summary>
    public (double X, double Y) ToLocal(double px, double py)
    {
        var (cx, cy) = Center;
        double a = -Rotation * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double dx = px - cx, dy = py - cy;
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    /// <summary>A point in the box's own coordinates turned into document coordinates.</summary>
    public (double X, double Y) ToDocument(double lx, double ly)
    {
        var (cx, cy) = Center;
        double a = Rotation * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double dx = lx - cx, dy = ly - cy;
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    /// <summary>The four corners in document coordinates (top-left, top-right, bottom-right, bottom-left).</summary>
    public (double X, double Y)[] Corners() =>
        [ToDocument(X, Y), ToDocument(X + Width, Y), ToDocument(X + Width, Y + Height), ToDocument(X, Y + Height)];

    public bool Contains(double px, double py, double margin = 0)
    {
        var (lx, ly) = ToLocal(px, py);
        return lx >= X - margin && lx <= X + Width + margin && ly >= Y - margin && ly <= Y + Height + margin;
    }

    /// <summary>The axis-aligned rectangle around the turned box.</summary>
    public (double Left, double Top, double Right, double Bottom) Extent()
    {
        var c = Corners();
        return (c.Min(p => p.X), c.Min(p => p.Y), c.Max(p => p.X), c.Max(p => p.Y));
    }
}

/// <summary>
/// Draws editable shapes into pixels: antialiased fill and outline (centred on the edge), any rotation, and an optional
/// soft shadow. Pure .NET, deterministic. Only edge pixels are supersampled (4 × 4), so big shapes stay quick.
/// </summary>
public static class ShapeRasterizer
{
    public static ObjectBox Box(ShapeContent s) => new(s.X, s.Y, s.Width, s.Height, s.Rotation);

    /// <summary>The shape as a document-sized image (premultiplied) and the area it covers.</summary>
    public static (RasterImage Image, PixelRect Bounds) Render(ShapeContent s, int width, int height)
    {
        var image = new RasterImage(width, height);
        return (image, RenderInto(s, image));
    }

    /// <summary>Draws the shape into a transparent area of <paramref name="image"/>; returns the area it covers.</summary>
    public static PixelRect RenderInto(ShapeContent s, RasterImage image)
    {
        int width = image.Width;
        var box = Box(s);
        bool line = s.Kind == ShapeType.Line;
        double half = (s.Stroke is null && !line ? 0 : Math.Max(0.5, s.StrokeWidth / 2)) + 1.5;
        var (l, t, r, b) = box.Extent();
        var area = new PixelRect((int)Math.Floor(l - half), (int)Math.Floor(t - half), 0, 0);
        area = new PixelRect(area.X, area.Y, (int)Math.Ceiling(r + half) - area.X + 1, (int)Math.Ceiling(b + half) - area.Y + 1).Intersect(image.Bounds);
        if (area.IsEmpty || (s.Fill is null && s.Stroke is null && !line)) return PixelRect.Empty;

        var sdf = Distance(s);
        uint fill = s.Fill is { } f && !line ? Pixel.Opaque(f) : 0;
        var strokeColour = s.Stroke ?? (line ? s.Fill ?? ColorRgb.Black : (ColorRgb?)null);
        uint stroke = strokeColour is { } sc ? Pixel.Opaque(sc) : 0;
        double sw = line ? Math.Max(1, s.StrokeWidth) : s.StrokeWidth;
        var px = image.Pixels;
        Parallel.For(area.Y, area.Bottom, y =>
        {
            for (int x = area.X; x < area.Right; x++)
            {
                var (lx, ly) = box.ToLocal(x + 0.5, y + 0.5);
                double d0 = sdf(lx, ly);
                float fillCov = 0, strokeCov = 0;
                if (fill != 0) fillCov = Coverage(d0, box, sdf, x, y, d => d <= 0, edge: 0);
                if (stroke != 0) strokeCov = Coverage(Math.Abs(d0) - sw / 2, box, sdf, x, y, d => Math.Abs(d) <= sw / 2, edge: 0);
                uint p = 0;
                if (fillCov > 0) p = Fade(fill, fillCov);
                if (strokeCov > 0) p = Pixel.Over(Fade(stroke, strokeCov), p);
                px[y * width + x] = p;
            }
        });
        if (s.Shadow is { } shadow) area = AddShadow(image, area, shadow);
        return area;
    }

    /// <summary>Fraction (0–1) of the pixel inside: decided from the centre when far from the edge, else 4 × 4 samples.</summary>
    private static float Coverage(double centreDistance, ObjectBox box, Func<double, double, double> sdf, int x, int y, Func<double, bool> inside, double edge)
    {
        if (centreDistance < -1.0) return 1;
        if (centreDistance > 1.0) return 0;
        int hits = 0;
        for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
            {
                var (lx, ly) = box.ToLocal(x + (sx + 0.5) / 4, y + (sy + 0.5) / 4);
                if (inside(sdf(lx, ly))) hits++;
            }
        _ = edge;
        return hits / 16f;
    }

    private static uint Fade(uint p, float k)
    {
        if (k >= 1) return p;
        return Pixel.Pack((byte)(Pixel.A(p) * k + 0.5f), (byte)(Pixel.R(p) * k + 0.5f), (byte)(Pixel.G(p) * k + 0.5f), (byte)(Pixel.B(p) * k + 0.5f));
    }

    /// <summary>Signed distance to the shape's edge in its own coordinates (negative inside).</summary>
    public static Func<double, double, double> Distance(ShapeContent s)
    {
        double cx = s.X + s.Width / 2, cy = s.Y + s.Height / 2, hw = Math.Abs(s.Width) / 2, hh = Math.Abs(s.Height) / 2;
        switch (s.Kind)
        {
            case ShapeType.Rectangle:
                return (x, y) => RoundedBox(x - cx, y - cy, hw, hh, 0);
            case ShapeType.RoundedRectangle:
                double r = Math.Clamp(s.CornerRadius, 0, Math.Min(hw, hh));
                return (x, y) => RoundedBox(x - cx, y - cy, hw, hh, r);
            case ShapeType.Ellipse:
                return (x, y) =>
                {
                    double a = Math.Max(hw, 0.5), b = Math.Max(hh, 0.5), dx = x - cx, dy = y - cy;
                    double f = dx * dx / (a * a) + dy * dy / (b * b) - 1;
                    double gx = 2 * dx / (a * a), gy = 2 * dy / (b * b);
                    double g = Math.Sqrt(gx * gx + gy * gy);
                    return g < 1e-9 ? -Math.Min(a, b) : f / g;
                };
            case ShapeType.Line:
            {
                double x0 = s.X, y0 = s.Y, x1 = s.X + s.Width, y1 = s.Y + s.Height;
                return (x, y) => Segment(x, y, x0, y0, x1, y1);
            }
            default:
            {
                int n = Math.Clamp(s.Sides, 3, 64);
                var pts = Enumerable.Range(0, n).Select(i =>
                {
                    double a = -Math.PI / 2 + i * 2 * Math.PI / n;
                    return (X: cx + Math.Cos(a) * hw, Y: cy + Math.Sin(a) * hh);
                }).ToArray();
                return (x, y) =>
                {
                    double d = double.MaxValue;
                    bool inside = false;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        d = Math.Min(d, Segment(x, y, pts[j].X, pts[j].Y, pts[i].X, pts[i].Y));
                        if ((pts[i].Y > y) != (pts[j].Y > y) && x < (pts[j].X - pts[i].X) * (y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                            inside = !inside;
                    }
                    return inside ? -d : d;
                };
            }
        }
    }

    private static double RoundedBox(double px, double py, double hw, double hh, double r)
    {
        double qx = Math.Abs(px) - (hw - r), qy = Math.Abs(py) - (hh - r);
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    private static double Segment(double px, double py, double ax, double ay, double bx, double by)
    {
        double vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
        double len2 = vx * vx + vy * vy;
        double t = len2 < 1e-12 ? 0 : Math.Clamp((wx * vx + wy * vy) / len2, 0, 1);
        double dx = wx - t * vx, dy = wy - t * vy;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Puts a soft shadow (the shape's own alpha, offset and blurred, in the shadow colour) under the pixels.</summary>
    internal static PixelRect AddShadow(RasterImage image, PixelRect area, ShadowEffect shadow)
    {
        if (area.IsEmpty || shadow.Opacity <= 0) return area;
        int blur = (int)Math.Round(Math.Clamp(shadow.Blur, 0, 64));
        int dx = (int)Math.Round(shadow.OffsetX), dy = (int)Math.Round(shadow.OffsetY);
        var grown = new PixelRect(area.X + Math.Min(0, dx) - blur * 2, area.Y + Math.Min(0, dy) - blur * 2,
            area.Width + Math.Abs(dx) + blur * 4, area.Height + Math.Abs(dy) + blur * 4).Intersect(image.Bounds);
        int w = grown.Width, h = grown.Height, iw = image.Width;
        var alpha = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = grown.X + x - dx, sy = grown.Y + y - dy;
                if (sx >= 0 && sy >= 0 && sx < iw && sy < image.Height) alpha[y * w + x] = Pixel.A(image.Pixels[sy * iw + sx]) / 255f;
            }
        if (blur > 0)
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(alpha, w, h, blur, horizontal: true);
                BoxBlur(alpha, w, h, blur, horizontal: false);
            }
        var c = shadow.Color;
        float opacity = (float)Math.Clamp(shadow.Opacity, 0, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float k = alpha[y * w + x] * opacity;
                if (k <= 0) continue;
                int i = (grown.Y + y) * iw + grown.X + x;
                uint sh = Pixel.FromStraight(c.R, c.G, c.B, (byte)Math.Clamp(k * 255 + 0.5f, 0, 255));
                image.Pixels[i] = Pixel.Over(image.Pixels[i], sh); // the shape stays on top of its shadow
            }
        return area.Union(grown);
    }

    private static void BoxBlur(float[] a, int w, int h, int r, bool horizontal)
    {
        int n = horizontal ? w : h, lines = horizontal ? h : w;
        var tmp = new float[n];
        for (int line = 0; line < lines; line++)
        {
            float sum = 0;
            int count = 0;
            for (int i = -r; i <= r; i++)
                if (i >= 0 && i < n)
                {
                    sum += a[Idx(line, i)];
                    count++;
                }
            for (int i = 0; i < n; i++)
            {
                tmp[i] = sum / (2 * r + 1);
                int outIdx = i - r, inIdx = i + r + 1;
                if (outIdx >= 0) sum -= a[Idx(line, outIdx)];
                if (inIdx < n) sum += a[Idx(line, inIdx)];
            }
            for (int i = 0; i < n; i++) a[Idx(line, i)] = tmp[i];
            _ = count;
        }
        int Idx(int line, int i) => horizontal ? line * w + i : i * w + line;
    }
}
