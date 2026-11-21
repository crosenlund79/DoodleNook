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
    /// <remarks>Mission 011: computes two channels at once in 16-bit lanes (red with blue, alpha with green). Each lane does
    /// exactly what <see cref="Mul"/> does (x·a + 128, plus its high byte, shifted down; never more than 16 bits, so lanes
    /// can't spill into each other), and colour channels are capped at 255 like before: the result is bit-for-bit the same
    /// as the channel-by-channel formula (<see cref="OverReference"/>), only faster. Tests compare both exhaustively.</remarks>
    public static uint Over(uint src, uint dst)
    {
        uint sa = src >> 24;
        if (sa == 255) return src;
        if (sa == 0) return dst;
        uint inv = 255 - sa;
        uint rb = (dst & 0x00FF00FFu) * inv + 0x00800080u;
        rb = ((rb + ((rb >> 8) & 0x00FF00FFu)) >> 8) & 0x00FF00FFu;
        uint ag = ((dst >> 8) & 0x00FF00FFu) * inv + 0x00800080u;
        ag = ((ag + ((ag >> 8) & 0x00FF00FFu)) >> 8) & 0x00FF00FFu;
        rb += src & 0x00FF00FFu;
        ag += (src >> 8) & 0x00FF00FFu;
        // A lane above 255 has bit 8 set: cap it at 255 (only colour channels can get there; alpha never exceeds 255).
        rb = (rb | (((rb >> 8) & 0x00010001u) * 0xFFu)) & 0x00FF00FFu;
        ag = (ag | (((ag >> 8) & 0x00010001u) * 0xFFu)) & 0x00FF00FFu;
        return (ag << 8) | rb;
    }

    /// <summary>The channel-by-channel "source over" (Mission 010 and earlier); kept as the reference that tests compare
    /// <see cref="Over"/> with.</summary>
    public static uint OverReference(uint src, uint dst)
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

    /// <summary>Every channel × <paramref name="factor"/>/255, rounded like <see cref="Mul"/> (two channels at a time).</summary>
    public static uint Scale(uint p, uint factor)
    {
        uint rb = (p & 0x00FF00FFu) * factor + 0x00800080u;
        rb = ((rb + ((rb >> 8) & 0x00FF00FFu)) >> 8) & 0x00FF00FFu;
        uint ag = ((p >> 8) & 0x00FF00FFu) * factor + 0x00800080u;
        ag = ((ag + ((ag >> 8) & 0x00FF00FFu)) >> 8) & 0x00FF00FFu;
        return (ag << 8) | rb;
    }
}
