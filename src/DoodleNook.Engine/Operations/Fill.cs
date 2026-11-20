using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>Paint bucket options.</summary>
public sealed record FillOptions(
    int Tolerance = 32,
    bool Contiguous = true,
    bool SampleMerged = false,
    double Opacity = 1,
    bool Exact = false);

/// <summary>Region finding by colour similarity (paint bucket and magic wand) and filling.</summary>
public static class FloodFill
{
    /// <summary>Pixels similar to the one at (x, y): 255 = part of the region. Similarity is the largest difference of
    /// any straight RGBA channel, compared with <paramref name="tolerance"/> (0–255).</summary>
    public static byte[] Region(RasterImage source, int x, int y, int tolerance, bool contiguous)
    {
        int w = source.Width, h = source.Height;
        var region = new byte[w * h];
        if (!source.Bounds.Contains(x, y)) return region;
        var px = source.Pixels;
        var target = Straight(px[y * w + x]);
        tolerance = Math.Clamp(tolerance, 0, 255);

        bool Match(int i) => Distance(Straight(px[i]), target) <= tolerance;

        if (!contiguous)
        {
            for (int i = 0; i < px.Length; i++) if (Match(i)) region[i] = 255;
            return region;
        }

        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();
            int row = sy * w;
            if (region[row + sx] != 0 || !Match(row + sx)) continue;
            int l = sx, r = sx;
            while (l > 0 && region[row + l - 1] == 0 && Match(row + l - 1)) l--;
            while (r < w - 1 && region[row + r + 1] == 0 && Match(row + r + 1)) r++;
            for (int i = l; i <= r; i++) region[row + i] = 255;
            foreach (int ny in new[] { sy - 1, sy + 1 })
            {
                if (ny < 0 || ny >= h) continue;
                int nrow = ny * w;
                bool inRun = false;
                for (int i = l; i <= r; i++)
                {
                    bool candidate = region[nrow + i] == 0 && Match(nrow + i);
                    if (candidate && !inRun) { stack.Push((i, ny)); inRun = true; }
                    else if (!candidate) inRun = false;
                }
            }
        }
        return region;
    }

    /// <summary>Fills the region around (x, y) on <paramref name="layer"/> with a colour, as one undo step.</summary>
    public static bool Apply(Document doc, Layer layer, int x, int y, ColorRgb color, FillOptions options)
    {
        if (layer.Locked || !doc.Bounds.Contains(x, y) || doc.SelectionAt(x, y) == 0) return false;
        var source = options.SampleMerged ? doc.Flatten() : doc.EditTargetOf(layer);
        var region = Region(source, x, y, options.Tolerance, options.Contiguous);
        return FillMask(doc, layer, region, color, options.Opacity, options.Exact, "Fill");
    }

    /// <summary>Fills the selection (or the whole layer) with a colour.</summary>
    public static bool FillSelection(Document doc, Layer layer, ColorRgb color, double opacity = 1)
    {
        if (layer.Locked) return false;
        var region = new byte[doc.Width * doc.Height];
        Array.Fill(region, (byte)255);
        return FillMask(doc, layer, region, color, opacity, exact: false, "Fill selection");
    }

    private static bool FillMask(Document doc, Layer layer, byte[] region, ColorRgb color, double opacity, bool exact, string name)
    {
        int w = doc.Width;
        var bounds = Bounds(region, w, doc.Height);
        if (doc.Selection is { } sel) bounds = bounds.Intersect(sel.Bounds);
        if (bounds.IsEmpty) return false;
        var edit = doc.BeginEdit(layer, name);
        edit.Capture(bounds);
        var px = edit.Target.Pixels;
        int op = (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                int i = y * w + x;
                if (region[i] == 0) continue;
                int s = doc.SelectionAt(x, y);
                if (s == 0) continue;
                uint baseline = px[i];
                if (exact)
                {
                    if (s < 128) continue;
                    px[i] = edit.AlphaLocked
                        ? (Pixel.A(baseline) == 0 ? baseline : Pixel.FromStraight(color.R, color.G, color.B, Pixel.A(baseline)))
                        : Pixel.Opaque(color);
                    continue;
                }
                int a = op * s / 255;
                if (a == 0) continue;
                if (edit.AlphaLocked)
                {
                    if (Pixel.A(baseline) == 0) continue;
                    var (r, g, b, ba) = Pixel.ToStraight(baseline);
                    px[i] = Pixel.FromStraight((byte)(r + (color.R - r) * a / 255), (byte)(g + (color.G - g) * a / 255), (byte)(b + (color.B - b) * a / 255), ba);
                }
                else px[i] = Pixel.Over(Pixel.FromStraight(color.R, color.G, color.B, (byte)a), baseline);
            }
        }
        edit.Changed(bounds);
        return edit.Commit();
    }

    internal static PixelRect Bounds(byte[] mask, int w, int h)
    {
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (mask[row + x] == 0) continue;
                if (x < l) l = x;
                if (x > r) r = x;
                if (y < t) t = y;
                b = y;
            }
        }
        return r < 0 ? PixelRect.Empty : PixelRect.FromEdges(l, t, r + 1, b + 1);
    }

    private static (int R, int G, int B, int A) Straight(uint p)
    {
        var (r, g, b, a) = Pixel.ToStraight(p);
        return (r, g, b, a);
    }

    private static int Distance((int R, int G, int B, int A) a, (int R, int G, int B, int A) b)
    {
        if (a.A == 0 && b.A == 0) return 0; // all fully transparent pixels count as the same colour
        return Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
    }
}
