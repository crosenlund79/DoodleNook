namespace DoodleNook.Engine.Painting;

/// <summary>Whether a stroke adds paint or removes it (the eraser shares the same stroke pipeline).</summary>
public enum PaintMode
{
    Paint,
    Erase,
}

/// <summary>
/// Settings for DoodleNook's round brush. Immutable; the UI creates modified copies with <c>with</c>.
/// </summary>
public sealed record BrushSettings
{
    public const double MinSize = 1;
    public const double MaxSize = 1000;

    /// <summary>Diameter in document pixels at full pressure.</summary>
    public double Size { get; init; } = 12;

    /// <summary>Stroke opacity, 0–1. A stroke never gets darker than this, even where it crosses itself.</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>Edge hardness, 0 (very soft) to 1 (crisp, anti-aliased edge).</summary>
    public double Hardness { get; init; } = 0.9;

    /// <summary>Pen pressure controls the size.</summary>
    public bool PressureSize { get; init; } = true;

    /// <summary>Pen pressure controls the opacity.</summary>
    public bool PressureOpacity { get; init; }

    /// <summary>Size at zero pressure, as a fraction of <see cref="Size"/> (only with <see cref="PressureSize"/>).</summary>
    public double MinimumSizeFraction { get; init; } = 0.05;

    /// <summary>Gentle stabilisation, 0 (off) to 1. The stroke always ends where the pen lifted.</summary>
    public double Smoothing { get; init; }

    /// <summary>Returns a copy with every value clamped to its valid range (NaN becomes the default).</summary>
    public BrushSettings Normalized() => this with
    {
        Size = Clamp(Size, MinSize, MaxSize, 12),
        Opacity = Clamp(Opacity, 0, 1, 1),
        Hardness = Clamp(Hardness, 0, 1, 0.9),
        MinimumSizeFraction = Clamp(MinimumSizeFraction, 0, 1, 0.05),
        Smoothing = Clamp(Smoothing, 0, 1, 0),
    };

    /// <summary>
    /// The dab diameter and per-dab opacity for a pen pressure (0–1, already sanitised),
    /// after the pressure curve has been applied.
    /// </summary>
    public (double Diameter, float Alpha) Evaluate(float pressure, PressureCurve curve)
    {
        float p = curve.Map(pressure);
        double diameter = PressureSize ? Size * (MinimumSizeFraction + (1 - MinimumSizeFraction) * p) : Size;
        float alpha = PressureOpacity ? p : 1f;
        return (diameter, alpha);
    }

    private static double Clamp(double v, double min, double max, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}
