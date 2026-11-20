using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Painting;

/// <summary>The loaded image tip and texture a stroke uses (resolved from <see cref="BrushSettings.TipId"/> and
/// <see cref="BrushSettings.TextureId"/> by whoever starts the stroke).</summary>
public sealed record BrushResources(BrushTip? Tip, BrushTexture? Texture);

/// <summary>
/// An image brush tip: a coverage mask (0–1) and, for images with transparency, their premultiplied colours (stamp
/// brushes). Kept at several halved sizes so small dabs of a big image stay smooth instead of shimmering.
/// <list type="bullet">
/// <item>An image with transparency uses its alpha: what is opaque paints.</item>
/// <item>A fully opaque image (say black ink on white paper) uses its darkness: black paints, white doesn't.</item>
/// </list>
/// Fully transparent (or white) borders are trimmed so the tip is centred on its content.
/// </summary>
public sealed class BrushTip
{
    public const int MaxSize = 512;
    private readonly List<Level> _levels = new();

    private sealed record Level(int Width, int Height, float[] Alpha, float[]? Color);

    private BrushTip(int width, int height, float[] alpha, float[]? color)
    {
        Width = width;
        Height = height;
        HasColor = color is not null;
        _levels.Add(new Level(width, height, alpha, color));
        // Halved copies down to a few pixels.
        var cur = _levels[0];
        while (Math.Max(cur.Width, cur.Height) > 8)
        {
            cur = Halve(cur);
            _levels.Add(cur);
        }
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The tip has its own colours (an image with transparency), so it can paint as a stamp brush.</summary>
    public bool HasColor { get; }

    public static BrushTip FromImage(RasterImage image)
    {
        bool opaque = true;
        foreach (var p in image.Pixels)
            if (Pixel.A(p) < 255)
            {
                opaque = false;
                break;
            }
        int w = image.Width, h = image.Height;
        var alpha = new float[w * h];
        float[]? color = opaque ? null : new float[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            uint p = image.Pixels[i];
            if (opaque)
            {
                var (r, g, b, _) = Pixel.ToStraight(p);
                alpha[i] = 1f - (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
            }
            else
            {
                alpha[i] = Pixel.A(p) / 255f;
                // Stored premultiplied, so bilinear sampling never bleeds the colour of invisible pixels.
                color![i * 3] = Pixel.R(p) / 255f;
                color[i * 3 + 1] = Pixel.G(p) / 255f;
                color[i * 3 + 2] = Pixel.B(p) / 255f;
            }
        }
        // Trim empty borders.
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (alpha[y * w + x] > 0.004f)
                {
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
        if (x1 < 0) return Dot();
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        var ca = new float[cw * ch];
        var cc = color is null ? null : new float[cw * ch * 3];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int s = (y + y0) * w + x + x0, d = y * cw + x;
                ca[d] = alpha[s];
                if (cc is not null)
                {
                    cc[d * 3] = color![s * 3];
                    cc[d * 3 + 1] = color[s * 3 + 1];
                    cc[d * 3 + 2] = color[s * 3 + 2];
                }
            }
        var tip = new BrushTip(cw, ch, ca, cc);
        // Very large images are reduced (a tip is never painted bigger than the brush size anyway).
        while (Math.Max(tip.Width, tip.Height) > MaxSize) tip = tip.Smaller();
        return tip;
    }

    /// <summary>A coverage mask from a function (built-in tips).</summary>
    public static BrushTip FromMask(int width, int height, Func<int, int, float> alpha)
    {
        var a = new float[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                a[y * width + x] = Math.Clamp(alpha(x, y), 0f, 1f);
        return new BrushTip(width, height, a, null);
    }

    private static BrushTip Dot() => FromMask(1, 1, (_, _) => 1f);

    private BrushTip Smaller()
    {
        var l = _levels[1];
        return new BrushTip(l.Width, l.Height, l.Alpha, l.Color);
    }

    private static Level Halve(Level s)
    {
        int w = Math.Max(1, (s.Width + 1) / 2), h = Math.Max(1, (s.Height + 1) / 2);
        var a = new float[w * h];
        var c = s.Color is null ? null : new float[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sa = 0, sr = 0, sg = 0, sb = 0;
                int n = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Math.Min(s.Width - 1, x * 2 + dx), sy = Math.Min(s.Height - 1, y * 2 + dy);
                        int i = sy * s.Width + sx;
                        sa += s.Alpha[i];
                        if (c is not null)
                        {
                            sr += s.Color![i * 3];
                            sg += s.Color[i * 3 + 1];
                            sb += s.Color[i * 3 + 2];
                        }
                        n++;
                    }
                a[y * w + x] = sa / n;
                if (c is not null)
                {
                    c[(y * w + x) * 3] = sr / n;
                    c[(y * w + x) * 3 + 1] = sg / n;
                    c[(y * w + x) * 3 + 2] = sb / n;
                }
            }
        return new Level(w, h, a, c);
    }

    /// <summary>Picks the copy whose size is closest above <paramref name="pixels"/> (the dab's size on the canvas).</summary>
    internal (int Width, int Height, float[] Alpha, float[]? Color, double Scale) LevelFor(double pixels)
    {
        var l = _levels[0];
        foreach (var level in _levels)
        {
            if (Math.Max(level.Width, level.Height) < pixels) break;
            l = level;
        }
        return (l.Width, l.Height, l.Alpha, l.Color, (double)l.Width / Width);
    }

    /// <summary>Bilinear coverage (and premultiplied colour) at a point of a level; 0 outside.</summary>
    internal static float Sample(int w, int h, float[] alpha, float[]? color, double fx, double fy, out float r, out float g, out float b)
    {
        r = g = b = 0;
        fx -= 0.5;
        fy -= 0.5;
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        float tx = (float)(fx - x0), ty = (float)(fy - y0);
        float a = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = x0 + (k & 1), y = y0 + (k >> 1);
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            float wgt = ((k & 1) == 1 ? tx : 1 - tx) * ((k >> 1) == 1 ? ty : 1 - ty);
            if (wgt <= 0) continue;
            int i = y * w + x;
            a += alpha[i] * wgt;
            if (color is not null)
            {
                r += color[i * 3] * wgt;
                g += color[i * 3 + 1] * wgt;
                b += color[i * 3 + 2] * wgt;
            }
        }
        return a;
    }

    /// <summary>A small RGBA picture of the tip (previews).</summary>
    public RasterImage ToImage(ColorRgb tint)
    {
        var l = _levels[0];
        var img = new RasterImage(l.Width, l.Height);
        for (int i = 0; i < l.Width * l.Height; i++)
        {
            byte a = (byte)Math.Clamp((int)(l.Alpha[i] * 255 + 0.5f), 0, 255);
            if (l.Color is not null)
                img.Pixels[i] = Pixel.Pack(a, (byte)Math.Clamp(l.Color[i * 3] * 255, 0, 255), (byte)Math.Clamp(l.Color[i * 3 + 1] * 255, 0, 255),
                    (byte)Math.Clamp(l.Color[i * 3 + 2] * 255, 0, 255));
            else img.Pixels[i] = Pixel.Pack(a, Pixel.Mul(tint.R, a), Pixel.Mul(tint.G, a), Pixel.Mul(tint.B, a));
        }
        return img;
    }
}

/// <summary>A repeating grey texture for brush grain (light parts take paint), read in document space like paper.</summary>
public sealed class BrushTexture
{
    private readonly float[] _values;

    public BrushTexture(int width, int height, float[] values)
    {
        Width = width;
        Height = height;
        _values = values;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>From any picture: its lightness (transparent areas count as light).</summary>
    public static BrushTexture FromImage(RasterImage image)
    {
        var v = new float[image.Width * image.Height];
        for (int i = 0; i < v.Length; i++)
        {
            var (r, g, b, a) = Pixel.ToStraight(image.Pixels[i]);
            float l = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
            float al = a / 255f;
            v[i] = l * al + (1 - al);
        }
        return new BrushTexture(image.Width, image.Height, v);
    }

    /// <summary>The value at a document pixel for a scale and rotation (wraps around, nearest sample).</summary>
    public float Sample(int x, int y, double scale, double cos, double sin)
    {
        double px = (x * cos + y * sin) / scale, py = (-x * sin + y * cos) / scale;
        int ix = (int)Math.Floor(px) % Width, iy = (int)Math.Floor(py) % Height;
        if (ix < 0) ix += Width;
        if (iy < 0) iy += Height;
        return _values[iy * Width + ix];
    }
}

/// <summary>A small deterministic random generator for scatter and jitter (the same stroke gives the same marks).</summary>
internal struct BrushRandom
{
    private ulong _state;

    public BrushRandom(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    /// <summary>0 ≤ value &lt; 1.</summary>
    public double Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return (_state >> 11) * (1.0 / (1UL << 53));
    }
}
