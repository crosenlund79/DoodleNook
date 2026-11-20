namespace DoodleNook.Engine.Imaging;

/// <summary>
/// Keeps pixels to a palette's colours <b>without touching their transparency</b>.
/// <para>
/// DoodleNook stores pixels premultiplied (8 bits per channel), so a partly transparent pixel can't hold every RGB value
/// exactly. A palette-locked picture therefore stores each pixel in one canonical form: the palette colour premultiplied
/// by the pixel's own alpha (<see cref="Encode"/>). <see cref="Decode"/> turns that back into exactly the palette colour
/// and the same alpha, which is what exports and the eyedropper use. Alpha is never changed: a pixel at 73 stays at 73,
/// opaque stays opaque, transparent stays transparent.
/// </para>
/// <para>
/// <see cref="Conform(uint)"/> is the palette-aware conversion for anything else (a blended brush edge, a resampled or
/// imported picture): it keeps the alpha and replaces the colour with the nearest palette colour in OKLab (ties go to the
/// earlier palette colour). A pixel that already is a palette colour keeps its exact value.
/// </para>
/// </summary>
public sealed class PaletteConformer
{
    private const int CacheBits = 16;
    private readonly ColorRgb[] _colors;
    private readonly OkLab[] _lab;
    // Direct-mapped caches, (pixel << 32) | result; a pixel is never 0 here, so empty slots never match. 64-bit writes
    // are atomic, so parallel use is safe (a lost entry is just recomputed).
    private readonly ulong[] _conform = new ulong[1 << CacheBits];
    private readonly ulong[] _exact = new ulong[1 << CacheBits];

    public PaletteConformer(IReadOnlyList<ColorRgb> colors)
    {
        if (colors.Count == 0) throw new ArgumentException("A palette needs at least one colour.", nameof(colors));
        _colors = colors.ToArray();
        _lab = _colors.Select(OkLab.From).ToArray();
    }

    public IReadOnlyList<ColorRgb> Colors => _colors;

    /// <summary>The canonical stored form of a palette colour at an alpha.</summary>
    public static uint Encode(ColorRgb c, byte alpha) => Pixel.FromStraight(c.R, c.G, c.B, alpha);

    /// <summary>The index of the palette colour whose canonical form is exactly <paramref name="p"/>, or -1.</summary>
    public int ExactIndex(uint p)
    {
        if (p == 0) return -1;
        int slot = Slot(p);
        ulong e = _exact[slot];
        if ((uint)(e >> 32) == p) return (int)(uint)e - 1;
        byte a = Pixel.A(p);
        int found = -1;
        for (int i = 0; i < _colors.Length; i++)
            if (Encode(_colors[i], a) == p)
            {
                found = i;
                break;
            }
        _exact[slot] = ((ulong)p << 32) | (uint)(found + 1);
        return found;
    }

    /// <summary>The nearest palette colour (OKLab); a colour in the palette maps to itself.</summary>
    public int NearestIndex(ColorRgb c)
    {
        for (int i = 0; i < _colors.Length; i++)
            if (_colors[i] == c) return i;
        var lab = OkLab.From(c);
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < _lab.Length; i++)
        {
            float d = lab.DistanceSquared(_lab[i]);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    public ColorRgb Nearest(ColorRgb c) => _colors[NearestIndex(c)];

    /// <summary>The pixel with its colour replaced by the nearest palette colour and its alpha unchanged.</summary>
    public uint Conform(uint p)
    {
        if (p == 0) return 0;
        byte a = Pixel.A(p);
        if (a == 0) return 0; // fully transparent: no colour to keep
        int slot = Slot(p);
        ulong e = _conform[slot];
        if ((uint)(e >> 32) == p) return (uint)e;
        uint result;
        if (ExactIndex(p) >= 0) result = p;
        else
        {
            var (r, g, b, _) = Pixel.ToStraight(p);
            result = Encode(_colors[NearestIndex(new ColorRgb(r, g, b))], a);
        }
        _conform[slot] = ((ulong)p << 32) | result;
        return result;
    }

    /// <summary>Straight RGBA: exactly the palette colour for a canonical palette pixel, otherwise the usual
    /// un-premultiplied value (for example where layer opacity mixes colours in a composite).</summary>
    public (byte R, byte G, byte B, byte A) Decode(uint p)
    {
        int i = ExactIndex(p);
        if (i < 0) return Pixel.ToStraight(p);
        var c = _colors[i];
        return (c.R, c.G, c.B, Pixel.A(p));
    }

    /// <summary>True when the pixel's colour is a palette colour up to the rounding of premultiplied storage
    /// (so conforming it changes nothing anyone could see).</summary>
    public bool IsPaletteColour(uint p)
    {
        byte a = Pixel.A(p);
        if (a == 0 || ExactIndex(p) >= 0) return true;
        var (r, g, b, _) = Pixel.ToStraight(p);
        var n = Nearest(new ColorRgb(r, g, b));
        // Premultiplied rounding moves the straight value by up to about 255 / (2·alpha) per channel.
        int tolerance = 1 + (255 + a) / (2 * a);
        return Math.Abs(n.R - r) <= tolerance && Math.Abs(n.G - g) <= tolerance && Math.Abs(n.B - b) <= tolerance;
    }

    /// <summary>Conforms every pixel of an area in place; returns how many changed.</summary>
    public int Conform(RasterImage image, PixelRect area)
    {
        area = area.Intersect(image.Bounds);
        if (area.IsEmpty) return 0;
        var px = image.Pixels;
        int w = image.Width, changed = 0;
        void Row(int y)
        {
            int n = 0;
            for (int i = y * w + area.X, end = i + area.Width; i < end; i++)
            {
                uint p = px[i];
                if (p == 0) continue;
                uint q = Conform(p);
                if (q != p)
                {
                    px[i] = q;
                    n++;
                }
            }
            if (n > 0) Interlocked.Add(ref changed, n);
        }
        if ((long)area.Width * area.Height >= 128 * 1024) Parallel.For(area.Y, area.Bottom, Row);
        else
            for (int y = area.Y; y < area.Bottom; y++) Row(y);
        return changed;
    }

    private static int Slot(uint p) => (int)((p * 2654435761u) >> (32 - CacheBits));
}
