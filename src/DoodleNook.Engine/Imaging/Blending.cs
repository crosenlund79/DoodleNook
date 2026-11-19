namespace DoodleNook.Engine.Imaging;

/// <summary>How a layer combines with what is below it.</summary>
public enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    SoftLight,
    Darken,
    Lighten,
    Add,
}

/// <summary>
/// Layer blending on premultiplied pixels, following the W3C compositing formulas:
/// result = (1 − αs)·Cb + (1 − αb)·Cs + αs·αb·B(cb, cs), with αr = αs + αb·(1 − αs).
/// </summary>
public static class Blending
{
    public static string DisplayName(BlendMode mode) => mode switch
    {
        BlendMode.SoftLight => "Soft light",
        BlendMode.Add => "Add (glow)",
        _ => mode.ToString(),
    };

    /// <summary>Scales a premultiplied pixel by an opacity factor 0–255.</summary>
    public static uint Fade(uint p, int factor)
    {
        if (factor >= 255 || p == 0) return p;
        if (factor <= 0) return 0;
        byte f = (byte)factor;
        return Pixel.Pack(Pixel.Mul(Pixel.A(p), f), Pixel.Mul(Pixel.R(p), f), Pixel.Mul(Pixel.G(p), f), Pixel.Mul(Pixel.B(p), f));
    }

    /// <summary>Blends premultiplied <paramref name="src"/> onto premultiplied <paramref name="dst"/>.</summary>
    public static uint Blend(uint src, uint dst, BlendMode mode)
    {
        if (src == 0) return dst;
        if (mode == BlendMode.Normal || dst == 0) return Pixel.Over(src, dst);

        float sa = Pixel.A(src) / 255f, da = Pixel.A(dst) / 255f;
        float ra = sa + da * (1 - sa);
        float r = Channel(Pixel.R(src), Pixel.R(dst), sa, da, mode);
        float g = Channel(Pixel.G(src), Pixel.G(dst), sa, da, mode);
        float b = Channel(Pixel.B(src), Pixel.B(dst), sa, da, mode);
        return Pixel.Pack(ToByte(ra), ToByte(Math.Min(r, ra)), ToByte(Math.Min(g, ra)), ToByte(Math.Min(b, ra)));
    }

    private static float Channel(byte csPremul, byte cbPremul, float sa, float da, BlendMode mode)
    {
        float Cs = csPremul / 255f, Cb = cbPremul / 255f;
        float cs = sa > 0 ? Cs / sa : 0, cb = da > 0 ? Cb / da : 0;
        float f = Function(cb, Math.Min(cs, 1f), mode);
        return (1 - sa) * Cb + (1 - da) * Cs + sa * da * f;
    }

    /// <summary>The separable blend function B(backdrop, source) on unpremultiplied 0–1 values.</summary>
    public static float Function(float cb, float cs, BlendMode mode) => mode switch
    {
        BlendMode.Multiply => cb * cs,
        BlendMode.Screen => cb + cs - cb * cs,
        BlendMode.Overlay => cb <= 0.5f ? 2 * cb * cs : 1 - 2 * (1 - cb) * (1 - cs),
        BlendMode.SoftLight => SoftLight(cb, cs),
        BlendMode.Darken => Math.Min(cb, cs),
        BlendMode.Lighten => Math.Max(cb, cs),
        BlendMode.Add => Math.Min(1f, cb + cs),
        _ => cs,
    };

    private static float SoftLight(float cb, float cs)
    {
        if (cs <= 0.5f) return cb - (1 - 2 * cs) * cb * (1 - cb);
        float d = cb <= 0.25f ? ((16 * cb - 12) * cb + 4) * cb : MathF.Sqrt(cb);
        return cb + (2 * cs - 1) * (d - cb);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}
