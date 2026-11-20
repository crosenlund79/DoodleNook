using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>Where and how a stamp is placed: centre, scale, rotation (degrees), opacity and mirroring.</summary>
public sealed record StampPlacement(double X, double Y, double Scale = 1, double Rotation = 0, double Opacity = 1, bool FlipX = false, bool FlipY = false)
{
    /// <summary>For pixel art: whole-pixel position, whole-number scale and quarter turns only, so no pixel is blurred.</summary>
    public StampPlacement PixelExact(RasterImage stamp) => this with
    {
        X = Math.Floor(X) + (stamp.Width * Math.Max(1, Math.Round(Scale)) % 2 == 1 ? 0.5 : 0),
        Y = Math.Floor(Y) + (stamp.Height * Math.Max(1, Math.Round(Scale)) % 2 == 1 ? 0.5 : 0),
        Scale = Math.Max(1, Math.Round(Scale)),
        Rotation = Math.Round(Rotation / 90) * 90,
    };
}

/// <summary>Options of a pattern fill: tile scale, offset (pixels), rotation (degrees) and opacity.</summary>
public sealed record PatternFillOptions(double Scale = 1, double OffsetX = 0, double OffsetY = 0, double Rotation = 0, double Opacity = 1);

/// <summary>
/// Placing stamps and filling with patterns. Both paint into one layer (or the current animation drawing) inside the
/// selection, as one undo step, and respect locks and transparency lock. With <c>pixelExact</c> they copy whole pixels
/// (nearest neighbour, no blurring) for pixel art.
/// </summary>
public static class StampsAndPatterns
{
    // ================================================================== stamps

    /// <summary>The area a placed stamp covers (for previews and tests).</summary>
    public static PixelRect Bounds(RasterImage stamp, StampPlacement p)
    {
        double w = stamp.Width * p.Scale, h = stamp.Height * p.Scale;
        double a = p.Rotation * Math.PI / 180, cos = Math.Abs(Math.Cos(a)), sin = Math.Abs(Math.Sin(a));
        double hw = (w * cos + h * sin) / 2 + 1, hh = (w * sin + h * cos) / 2 + 1;
        return PixelRect.FromBounds(p.X - hw, p.Y - hh, p.X + hw, p.Y + hh);
    }

    /// <summary>Draws a stamp onto a layer. Returns false when nothing changed (locked layer, outside the picture…).</summary>
    public static bool PlaceStamp(Document doc, Layer layer, RasterImage stamp, StampPlacement placement, bool pixelExact = false)
    {
        if (layer.Locked || placement.Scale <= 0) return false;
        if (pixelExact) placement = placement.PixelExact(stamp);
        var box = Bounds(stamp, placement).Intersect(doc.Bounds);
        if (doc.Selection is { } sel) box = box.Intersect(sel.Bounds);
        if (box.IsEmpty) return false;
        double a = -placement.Rotation * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double inv = 1 / placement.Scale;
        double ox = stamp.Width / 2.0, oy = stamp.Height / 2.0;
        float opacity = (float)Math.Clamp(placement.Opacity, 0, 1);
        return Paint(doc, layer, box, "Stamp", (x, y) =>
        {
            double fx = x + 0.5 - placement.X, fy = y + 0.5 - placement.Y;
            double u = (fx * cos - fy * sin) * inv, v = (fx * sin + fy * cos) * inv;
            if (placement.FlipX) u = -u;
            if (placement.FlipY) v = -v;
            u += ox;
            v += oy;
            uint s = pixelExact ? Nearest(stamp, u, v, wrap: false) : Bilinear(stamp, u, v, wrap: false);
            return Scale(s, opacity);
        });
    }

    // ================================================================== patterns

    /// <summary>The pattern colour at a canvas pixel.</summary>
    public static uint PatternAt(RasterImage tile, PatternFillOptions o, int x, int y, bool pixelExact) => new PatternSampler(tile, o, pixelExact).At(x, y);

    /// <summary>Samples a pattern at canvas pixels; the rotation is worked out once per fill, not per pixel.</summary>
    private readonly struct PatternSampler
    {
        private readonly RasterImage _tile;
        private readonly double _cos, _sin, _ox, _oy;
        private readonly bool _exact;

        public PatternSampler(RasterImage tile, PatternFillOptions o, bool pixelExact)
        {
            _tile = tile;
            double a = -o.Rotation * Math.PI / 180;
            _cos = Math.Cos(a) / o.Scale;
            _sin = Math.Sin(a) / o.Scale;
            _ox = o.OffsetX;
            _oy = o.OffsetY;
            _exact = pixelExact || o.Rotation % 90 == 0 && Math.Abs(o.Scale - Math.Round(o.Scale)) < 1e-9;
        }

        public uint At(int x, int y)
        {
            double fx = x + 0.5 - _ox, fy = y + 0.5 - _oy;
            double u = fx * _cos - fy * _sin, v = fx * _sin + fy * _cos;
            return _exact ? Nearest(_tile, u, v, wrap: true) : Bilinear(_tile, u, v, wrap: true);
        }
    }

    /// <summary>Fills the area around (x, y) (like the paint bucket) with a pattern.</summary>
    public static bool FillAt(Document doc, Layer layer, int x, int y, RasterImage tile, PatternFillOptions o, FillOptions region, bool pixelExact = false)
    {
        if (layer.Locked || !doc.Bounds.Contains(x, y) || doc.SelectionAt(x, y) == 0) return false;
        var source = region.SampleMerged ? doc.Flatten() : layer.Image;
        var mask = FloodFill.Region(source, x, y, region.Tolerance, region.Contiguous);
        return FillMask(doc, layer, mask, tile, o, pixelExact, "Pattern fill");
    }

    /// <summary>Fills the selection (or the whole layer) with a pattern.</summary>
    public static bool FillSelection(Document doc, Layer layer, RasterImage tile, PatternFillOptions o, bool pixelExact = false)
    {
        if (layer.Locked) return false;
        var mask = new byte[doc.Width * doc.Height];
        Array.Fill(mask, (byte)255);
        return FillMask(doc, layer, mask, tile, o, pixelExact, "Pattern fill");
    }

    private static bool FillMask(Document doc, Layer layer, byte[] mask, RasterImage tile, PatternFillOptions o, bool pixelExact, string name)
    {
        if (o.Scale <= 0 || tile.Width == 0 || tile.Height == 0) return false;
        if (pixelExact) o = o with { Scale = Math.Max(1, Math.Round(o.Scale)), Rotation = Math.Round(o.Rotation / 90) * 90, OffsetX = Math.Round(o.OffsetX), OffsetY = Math.Round(o.OffsetY) };
        int w = doc.Width;
        var box = FloodFill.Bounds(mask, w, doc.Height);
        if (doc.Selection is { } sel) box = box.Intersect(sel.Bounds);
        if (box.IsEmpty) return false;
        float opacity = (float)Math.Clamp(o.Opacity, 0, 1);
        var sampler = new PatternSampler(tile, o, pixelExact);
        return Paint(doc, layer, box, name, (x, y) => mask[y * w + x] == 0 ? 0u : Scale(sampler.At(x, y), opacity));
    }

    // ================================================================== shared

    /// <summary>Composites premultiplied colours over a layer within an area and the selection, as one undo step.</summary>
    private static bool Paint(Document doc, Layer layer, PixelRect box, string name, Func<int, int, uint> source)
    {
        var edit = doc.BeginEdit(layer, name);
        edit.Capture(box);
        var px = layer.Image.Pixels;
        int w = doc.Width;
        for (int y = box.Y; y < box.Bottom; y++)
        {
            for (int x = box.X; x < box.Right; x++)
            {
                uint src = source(x, y);
                if (Pixel.A(src) == 0) continue;
                int s = doc.SelectionAt(x, y);
                if (s == 0) continue;
                if (s < 255) src = Scale(src, s / 255f);
                int i = y * w + x;
                uint baseline = px[i];
                if (layer.AlphaLocked)
                {
                    byte ba = Pixel.A(baseline);
                    if (ba == 0) continue;
                    var (r, g, b, _) = Pixel.ToStraight(Pixel.Over(src, baseline));
                    px[i] = Pixel.FromStraight(r, g, b, ba);
                }
                else px[i] = Pixel.Over(src, baseline);
            }
        }
        edit.Changed(box);
        return edit.Commit();
    }

    private static uint Scale(uint p, float k)
    {
        if (k >= 1) return p;
        if (k <= 0) return 0;
        return Pixel.Pack((byte)(Pixel.A(p) * k + 0.5f), (byte)(Pixel.R(p) * k + 0.5f), (byte)(Pixel.G(p) * k + 0.5f), (byte)(Pixel.B(p) * k + 0.5f));
    }

    private static uint Nearest(RasterImage img, double u, double v, bool wrap)
    {
        int x = (int)Math.Floor(u), y = (int)Math.Floor(v);
        if (wrap)
        {
            x %= img.Width;
            y %= img.Height;
            if (x < 0) x += img.Width;
            if (y < 0) y += img.Height;
        }
        else if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return 0;
        return img[x, y];
    }

    /// <summary>Bilinear sample of premultiplied pixels (no colour fringes at transparent edges).</summary>
    private static uint Bilinear(RasterImage img, double u, double v, bool wrap)
    {
        u -= 0.5;
        v -= 0.5;
        int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
        float tx = (float)(u - x0), ty = (float)(v - y0);
        float a = 0, r = 0, g = 0, b = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = x0 + (k & 1), y = y0 + (k >> 1);
            float wgt = ((k & 1) == 1 ? tx : 1 - tx) * ((k >> 1) == 1 ? ty : 1 - ty);
            if (wgt <= 0) continue;
            if (wrap)
            {
                x %= img.Width;
                y %= img.Height;
                if (x < 0) x += img.Width;
                if (y < 0) y += img.Height;
            }
            else if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) continue;
            uint p = img[x, y];
            a += Pixel.A(p) * wgt;
            r += Pixel.R(p) * wgt;
            g += Pixel.G(p) * wgt;
            b += Pixel.B(p) * wgt;
        }
        return Pixel.Pack((byte)(a + 0.5f), (byte)Math.Min(a + 0.5f, r + 0.5f), (byte)Math.Min(a + 0.5f, g + 0.5f), (byte)Math.Min(a + 0.5f, b + 0.5f));
    }
}
