using System.Runtime.CompilerServices;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Library;

/// <summary>
/// A tiny painter for DoodleNook's built-in stamps and patterns: shapes given as functions, filled with 4 × 4
/// supersampled anti-aliasing (or hard-edged for pixel tiles), painted over each other.
/// </summary>
internal static class ArtKit
{
    /// <summary>Paints colour where <paramref name="inside"/> holds (sub-pixel samples), shaded by <paramref name="color"/>.</summary>
    public static void Fill(RasterImage img, Func<double, double, bool> inside, Func<double, double, ColorRgb> color, double opacity = 1, bool aa = true)
    {
        int n = aa ? 4 : 1;
        // Shapes made by Polygon, Blade and Ellipse know their bounding box: only those pixels can be touched.
        int x0 = 0, y0 = 0, x1 = img.Width, y1 = img.Height;
        if (Boxes.TryGetValue(inside, out var box))
        {
            x0 = Math.Max(0, (int)Math.Floor(box.X0) - 1);
            y0 = Math.Max(0, (int)Math.Floor(box.Y0) - 1);
            x1 = Math.Min(img.Width, (int)Math.Ceiling(box.X1) + 1);
            y1 = Math.Min(img.Height, (int)Math.Ceiling(box.Y1) + 1);
        }
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < n; sy++)
                    for (int sx = 0; sx < n; sx++)
                        if (inside(x + (sx + 0.5) / n, y + (sy + 0.5) / n)) hits++;
                if (hits == 0) continue;
                var c = color(x + 0.5, y + 0.5);
                byte a = (byte)Math.Round(255.0 * hits / (n * n) * opacity);
                img[x, y] = Pixel.Over(Pixel.FromStraight(c.R, c.G, c.B, a), img[x, y]);
            }
    }

    private sealed record Box(double X0, double Y0, double X1, double Y1);

    private static readonly ConditionalWeakTable<Func<double, double, bool>, Box> Boxes = new();

    private static Func<double, double, bool> Bounded(Func<double, double, bool> inside, double x0, double y0, double x1, double y1)
    {
        Boxes.AddOrUpdate(inside, new Box(x0, y0, x1, y1));
        return inside;
    }

    public static Func<double, double, bool> Ellipse(double cx, double cy, double rx, double ry, double angleDeg = 0)
    {
        double a = angleDeg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double r = Math.Max(Math.Abs(rx), Math.Abs(ry));
        return Bounded((x, y) =>
        {
            double dx = x - cx, dy = y - cy;
            double u = (dx * cos + dy * sin) / rx, v = (-dx * sin + dy * cos) / ry;
            return u * u + v * v <= 1;
        }, cx - r, cy - r, cx + r, cy + r);
    }

    public static Func<double, double, bool> Polygon(params (double X, double Y)[] pts) => Bounded((x, y) =>
    {
        bool inside = false;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
        {
            if ((pts[i].Y > y) != (pts[j].Y > y) && x < (pts[j].X - pts[i].X) * (y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                inside = !inside;
        }
        return inside;
    }, pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));

    /// <summary>A tapering curved stroke (grass blade, vein) from a quadratic curve.</summary>
    public static Func<double, double, bool> Blade((double X, double Y) a, (double X, double Y) control, (double X, double Y) b, double width)
    {
        var pts = new List<(double X, double Y)>();
        var back = new List<(double X, double Y)>();
        const int n = 24;
        for (int i = 0; i <= n; i++)
        {
            double t = i / (double)n, mt = 1 - t;
            double x = mt * mt * a.X + 2 * mt * t * control.X + t * t * b.X;
            double y = mt * mt * a.Y + 2 * mt * t * control.Y + t * t * b.Y;
            double dx = 2 * mt * (control.X - a.X) + 2 * t * (b.X - control.X), dy = 2 * mt * (control.Y - a.Y) + 2 * t * (b.Y - control.Y);
            double len = Math.Sqrt(dx * dx + dy * dy) + 1e-9;
            double hw = width * 0.5 * (1 - t * 0.92);
            pts.Add((x - dy / len * hw, y + dx / len * hw));
            back.Add((x + dy / len * hw, y - dx / len * hw));
        }
        back.Reverse();
        return Polygon([.. pts, .. back]);
    }

    public static Func<double, double, ColorRgb> Solid(ColorRgb c) => (_, _) => c;

    /// <summary>A soft vertical light-to-dark shading.</summary>
    public static Func<double, double, ColorRgb> Shade(ColorRgb light, ColorRgb dark, double y0, double y1) => (_, y) =>
    {
        double t = Math.Clamp((y - y0) / (y1 - y0), 0, 1);
        return Mix(light, dark, t);
    };

    public static ColorRgb Mix(ColorRgb a, ColorRgb b, double t) =>
        new((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static ColorRgb C(string hex) => ColorRgb.Parse(hex);

    /// <summary>A repeatable pseudo-random value 0–1 for integer coordinates (seamless when coordinates wrap).</summary>
    public static double Hash(int x, int y, int seed = 0)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>Smooth value noise that repeats every <paramref name="period"/> cells (for seamless tiles).</summary>
    public static double TileNoise(double x, double y, int period, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double V(int i, int j) => Hash(((i % period) + period) % period, ((j % period) + period) % period, seed);
        double a = V(x0, y0), b = V(x0 + 1, y0), c = V(x0, y0 + 1), d = V(x0 + 1, y0 + 1);
        return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
    }
}
