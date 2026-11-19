namespace DoodleNook.Engine.Painting;

/// <summary>
/// Maps raw pen pressure (0–1) to brush response with a simple power curve.
/// "Soft" gives more ink for a light touch; "Firm" needs a heavier hand.
/// </summary>
public readonly record struct PressureCurve(double Gamma)
{
    public static readonly PressureCurve Soft = new(0.6);
    public static readonly PressureCurve Normal = new(1.0);
    public static readonly PressureCurve Firm = new(1.6);

    /// <summary>Clamps raw pressure into 0–1. Missing (NaN) pressure counts as full pressure.</summary>
    public static float Sanitize(float pressure)
    {
        if (float.IsNaN(pressure)) return 1f;
        return Math.Clamp(pressure, 0f, 1f);
    }

    public float Map(float pressure)
    {
        float p = Sanitize(pressure);
        double g = double.IsFinite(Gamma) && Gamma > 0 ? Gamma : 1.0;
        if (g == 1.0 || p <= 0f || p >= 1f) return p;
        return (float)Math.Pow(p, g);
    }
}
