namespace DoodleNook.Engine.Imaging;

/// <summary>
/// Helpers for DoodleNook's working pixel format: 8-bit premultiplied BGRA packed into a
/// <see cref="uint"/> as 0xAARRGGBB (so the bytes in memory are B, G, R, A on little-endian
/// machines — the native format of Skia and Windows).
/// </summary>
public static class Pixel
{
    public const uint Transparent = 0;

    public static uint Pack(byte a, byte r, byte g, byte b) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    public static byte A(uint p) => (byte)(p >> 24);
    public static byte R(uint p) => (byte)(p >> 16);
    public static byte G(uint p) => (byte)(p >> 8);
    public static byte B(uint p) => (byte)p;

    /// <summary>Premultiplies a straight (unassociated) colour.</summary>
    public static uint FromStraight(byte r, byte g, byte b, byte a)
    {
        if (a == 255) return Pack(255, r, g, b);
        if (a == 0) return Transparent;
        return Pack(a, Mul(r, a), Mul(g, a), Mul(b, a));
    }

    /// <summary>Returns the straight (unpremultiplied) colour of a premultiplied pixel.</summary>
    public static (byte R, byte G, byte B, byte A) ToStraight(uint p)
    {
        byte a = A(p);
        if (a == 255) return (R(p), G(p), B(p), 255);
        if (a == 0) return (0, 0, 0, 0);
        return (Div(R(p), a), Div(G(p), a), Div(B(p), a), a);
    }

    public static uint Opaque(ColorRgb c) => Pack(255, c.R, c.G, c.B);

    /// <summary>Rounded (x * a / 255).</summary>
    public static byte Mul(byte x, byte a)
    {
        int t = x * a + 128;
        return (byte)((t + (t >> 8)) >> 8);
    }

    /// <summary>Rounded (x * 255 / a), clamped to 255.</summary>
    public static byte Div(byte x, byte a) => (byte)Math.Min(255, (x * 255 + a / 2) / a);

    /// <summary>Premultiplied "source over" of <paramref name="src"/> onto <paramref name="dst"/>.</summary>
    public static uint Over(uint src, uint dst)
    {
        byte sa = A(src);
        if (sa == 255) return src;
        if (sa == 0) return dst;
        byte inv = (byte)(255 - sa);
        return Pack(
            (byte)(sa + Mul(A(dst), inv)),
            (byte)Math.Min(255, R(src) + Mul(R(dst), inv)),
            (byte)Math.Min(255, G(src) + Mul(G(dst), inv)),
            (byte)Math.Min(255, B(src) + Mul(B(dst), inv)));
    }
}
