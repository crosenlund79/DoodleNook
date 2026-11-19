using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>A colour or filter adjustment applied to an area of an image.</summary>
public abstract record Adjustment
{
    public abstract string Name { get; }

    /// <summary>How far (in pixels) the result at a pixel depends on its neighbours (0 for per-pixel adjustments).</summary>
    public virtual int Reach => 0;

    /// <summary>Writes the adjusted pixels of <paramref name="area"/> from <paramref name="source"/> into <paramref name="target"/>.</summary>
    public abstract void Apply(RasterImage source, RasterImage target, PixelRect area);

    protected static void PerPixel(RasterImage source, RasterImage target, PixelRect area, Func<double, double, double, (double R, double G, double B)> f)
    {
        int w = source.Width;
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                int i = y * w + x;
                uint p = source.Pixels[i];
                byte a = Pixel.A(p);
                if (a == 0) { target.Pixels[i] = 0; continue; }
                var (r, g, b, _) = Pixel.ToStraight(p);
                var (nr, ng, nb) = f(r / 255.0, g / 255.0, b / 255.0);
                target.Pixels[i] = Pixel.FromStraight(To8(nr), To8(ng), To8(nb), a);
            }
    }

    protected static byte To8(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
}

/// <summary>Brightness and contrast, −100 to 100 each.</summary>
public sealed record BrightnessContrast(double Brightness, double Contrast) : Adjustment
{
    public override string Name => "Brightness/Contrast";

    public override void Apply(RasterImage source, RasterImage target, PixelRect area)
    {
        double b = Brightness / 100 * 0.5;
        double c = Contrast >= 0 ? 1 + Contrast / 100 * 2 : 1 + Contrast / 100;
        PerPixel(source, target, area, (r, g, bl) => (F(r), F(g), F(bl)));
        double F(double v) => (v - 0.5) * c + 0.5 + b;
    }
}

/// <summary>Hue shift (−180 to 180°), saturation and lightness (−100 to 100).</summary>
public sealed record HueSaturation(double Hue, double Saturation, double Lightness) : Adjustment
{
    public override string Name => "Hue/Saturation";

    public override void Apply(RasterImage source, RasterImage target, PixelRect area)
    {
        double sat = 1 + Saturation / 100, light = Lightness / 100;
        PerPixel(source, target, area, (r, g, b) =>
        {
            var (h, s, l) = ToHsl(r, g, b);
            h = (h + Hue / 360 + 1) % 1;
            s = Math.Clamp(s * sat, 0, 1);
            l = light >= 0 ? l + (1 - l) * light : l * (1 + light);
            return FromHsl(h, s, l);
        });
    }

    internal static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    internal static (double R, double G, double B) FromHsl(double h, double s, double l)
    {
        if (s < 1e-9) return (l, l, l);
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        return (Hue2(p, q, h + 1.0 / 3), Hue2(p, q, h), Hue2(p, q, h - 1.0 / 3));
        static double Hue2(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}

/// <summary>Grayscale (keeps perceived brightness).</summary>
public sealed record Desaturate : Adjustment
{
    public override string Name => "Desaturate";

    public override void Apply(RasterImage source, RasterImage target, PixelRect area) =>
        PerPixel(source, target, area, (r, g, b) =>
        {
            double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            return (y, y, y);
        });
}

public sealed record Invert : Adjustment
{
    public override string Name => "Invert colours";

    public override void Apply(RasterImage source, RasterImage target, PixelRect area) =>
        PerPixel(source, target, area, (r, g, b) => (1 - r, 1 - g, 1 - b));
}

/// <summary>Gaussian-like blur (three box passes), radius in pixels.</summary>
public sealed record Blur(double Radius) : Adjustment
{
    public override string Name => "Blur";
    public override int Reach => (int)Math.Ceiling(Radius * 3) + 2;

    public override void Apply(RasterImage source, RasterImage target, PixelRect area) =>
        BlurInto(source, target, area, Radius);

    internal static void BlurInto(RasterImage source, RasterImage target, PixelRect area, double radius)
    {
        if (radius < 0.25)
        {
            for (int y = area.Y; y < area.Bottom; y++)
                Array.Copy(source.Pixels, y * source.Width + area.X, target.Pixels, y * source.Width + area.X, area.Width);
            return;
        }
        int pad = (int)Math.Ceiling(radius * 3) + 2;
        var work = area.Inflate(pad).Intersect(source.Bounds);
        int w = work.Width, h = work.Height;
        var ch = new float[4][];
        for (int c = 0; c < 4; c++) ch[c] = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint p = source.Pixels[(work.Y + y) * source.Width + work.X + x];
                int i = y * w + x;
                ch[0][i] = Pixel.A(p); ch[1][i] = Pixel.R(p); ch[2][i] = Pixel.G(p); ch[3][i] = Pixel.B(p);
            }
        foreach (int box in BoxSizes(radius))
            for (int c = 0; c < 4; c++)
            {
                BoxPass(ch[c], w, h, (box - 1) / 2, horizontal: true);
                BoxPass(ch[c], w, h, (box - 1) / 2, horizontal: false);
            }
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                int i = (y - work.Y) * w + (x - work.X);
                int a = Math.Clamp((int)(ch[0][i] + 0.5f), 0, 255);
                target.Pixels[y * source.Width + x] = a == 0 ? 0 : Pixel.Pack((byte)a,
                    (byte)Math.Clamp((int)(ch[1][i] + 0.5f), 0, a), (byte)Math.Clamp((int)(ch[2][i] + 0.5f), 0, a), (byte)Math.Clamp((int)(ch[3][i] + 0.5f), 0, a));
            }
    }

    private static int[] BoxSizes(double sigma)
    {
        double wIdeal = Math.Sqrt(12 * sigma * sigma / 3 + 1);
        int wl = (int)Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        int wu = wl + 2;
        double mIdeal = (12 * sigma * sigma - 3 * wl * wl - 12 * wl - 9) / (-4.0 * wl - 4);
        int m = (int)Math.Round(mIdeal);
        return [.. Enumerable.Range(0, 3).Select(i => Math.Max(1, i < m ? wl : wu))];
    }

    private static void BoxPass(float[] data, int w, int h, int r, bool horizontal)
    {
        if (r <= 0) return;
        int lines = horizontal ? h : w, len = horizontal ? w : h;
        var tmp = new float[len];
        float inv = 1f / (2 * r + 1);
        for (int line = 0; line < lines; line++)
        {
            int Idx(int k) => horizontal ? line * w + Math.Clamp(k, 0, len - 1) : Math.Clamp(k, 0, len - 1) * w + line;
            float acc = 0;
            for (int k = -r; k <= r; k++) acc += data[Idx(k)];
            for (int k = 0; k < len; k++)
            {
                tmp[k] = acc * inv;
                acc += data[Idx(k + r + 1)] - data[Idx(k - r)];
            }
            for (int k = 0; k < len; k++)
            {
                if (horizontal) data[line * w + k] = tmp[k];
                else data[k * w + line] = tmp[k];
            }
        }
    }
}

/// <summary>Unsharp-mask sharpening: amount in percent, radius in pixels.</summary>
public sealed record Sharpen(double Amount, double Radius) : Adjustment
{
    public override string Name => "Sharpen";
    public override int Reach => (int)Math.Ceiling(Radius * 3) + 2;

    public override void Apply(RasterImage source, RasterImage target, PixelRect area)
    {
        var blurred = new RasterImage(source.Width, source.Height);
        Blur.BlurInto(source, blurred, area, Radius);
        double k = Amount / 100;
        int w = source.Width;
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                int i = y * w + x;
                uint o = source.Pixels[i], b = blurred.Pixels[i];
                int a = Pixel.A(o);
                if (a == 0) { target.Pixels[i] = 0; continue; }
                byte C(byte oc, byte bc) => (byte)Math.Clamp((int)Math.Round(oc + (oc - bc) * k), 0, a);
                target.Pixels[i] = Pixel.Pack((byte)a, C(Pixel.R(o), Pixel.R(b)), C(Pixel.G(o), Pixel.G(b)), C(Pixel.B(o), Pixel.B(b)));
            }
    }
}

/// <summary>
/// Applies an <see cref="Adjustment"/> to a layer (inside the selection) with a live preview: <see cref="Preview"/> shows
/// the result on screen without touching the layer; <see cref="Commit"/> applies it as one undo step.
/// </summary>
public sealed class AdjustmentOperation
{
    private readonly Document _doc;
    private readonly Layer _layer;
    private readonly PixelRect _area;
    private RasterImage? _preview;

    public AdjustmentOperation(Document doc, Layer layer)
    {
        if (layer.Locked) throw new InvalidOperationException("The layer is locked.");
        _doc = doc;
        _layer = layer;
        var area = layer.ContentBounds.Intersect(doc.Bounds);
        if (doc.Selection is { } sel) area = area.Intersect(sel.Bounds);
        _area = area;
    }

    public bool HasPixels => !_area.IsEmpty;

    public void Preview(Adjustment adjustment)
    {
        if (_area.IsEmpty) return;
        _preview ??= _layer.Image.Clone();
        Render(adjustment, _preview);
        _doc.SetLayerOverride(_layer, _preview, _area);
    }

    public bool Commit(Adjustment adjustment)
    {
        _doc.SetLayerOverride(_layer, null, _area);
        if (_area.IsEmpty) return false;
        var edit = _doc.BeginEdit(_layer, adjustment.Name);
        edit.Capture(_area);
        var result = _layer.Image.Clone();
        Render(adjustment, result);
        var px = _layer.Image.Pixels;
        for (int y = _area.Y; y < _area.Bottom; y++)
            Array.Copy(result.Pixels, y * _doc.Width + _area.X, px, y * _doc.Width + _area.X, _area.Width);
        edit.Changed(_area);
        return edit.Commit();
    }

    public void Cancel() => _doc.SetLayerOverride(_layer, null, _area);

    private void Render(Adjustment adjustment, RasterImage into)
    {
        var original = _layer.Image;
        adjustment.Apply(original, into, _area);
        bool alphaLock = _layer.AlphaLocked;
        // Blend with the original by the selection strength (soft selection edges) and honour transparency lock.
        for (int y = _area.Y; y < _area.Bottom; y++)
            for (int x = _area.X; x < _area.Right; x++)
            {
                int i = y * _doc.Width + x;
                int s = _doc.SelectionAt(x, y);
                uint o = original.Pixels[i], n = into.Pixels[i];
                if (alphaLock && Pixel.A(n) != Pixel.A(o))
                {
                    var (r, g, b, a) = Pixel.ToStraight(n);
                    n = a == 0 || Pixel.A(o) == 0 ? o : Pixel.FromStraight(r, g, b, Pixel.A(o));
                }
                if (s == 255) { into.Pixels[i] = n; continue; }
                if (s == 0) { into.Pixels[i] = o; continue; }
                into.Pixels[i] = Mix(o, n, s);
            }
    }

    private static uint Mix(uint a, uint b, int t)
    {
        int u = 255 - t;
        return Pixel.Pack((byte)((Pixel.A(a) * u + Pixel.A(b) * t + 127) / 255), (byte)((Pixel.R(a) * u + Pixel.R(b) * t + 127) / 255),
            (byte)((Pixel.G(a) * u + Pixel.G(b) * t + 127) / 255), (byte)((Pixel.B(a) * u + Pixel.B(b) * t + 127) / 255));
    }
}
