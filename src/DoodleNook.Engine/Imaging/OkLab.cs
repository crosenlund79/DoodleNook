namespace DoodleNook.Engine.Imaging;

/// <summary>
/// OKLab (Björn Ottosson, 2020): a perceptual colour space where straight-line distances match how different two
/// colours look much better than RGB distances do. L is lightness (0 = black, 1 = white); a and b are the green–red and
/// blue–yellow axes.
/// </summary>
public readonly record struct OkLab(float L, float A, float B)
{
    private static readonly float[] Linear = BuildLinear();

    public static OkLab From(ColorRgb c) => From(c.R, c.G, c.B);

    public static OkLab From(byte r8, byte g8, byte b8)
    {
        float r = Linear[r8], g = Linear[g8], b = Linear[b8];
        float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        return new OkLab(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    /// <summary>Squared distance (cheaper than the distance, same order).</summary>
    public float DistanceSquared(OkLab o)
    {
        float dl = L - o.L, da = A - o.A, db = B - o.B;
        return dl * dl + da * da + db * db;
    }

    /// <summary>Chroma: how colourful (0 for greys).</summary>
    public float Chroma => MathF.Sqrt(A * A + B * B);

    private static float[] BuildLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            t[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return t;
    }
}
