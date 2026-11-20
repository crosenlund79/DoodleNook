namespace DoodleNook.Engine.Painting;

/// <summary>Whether a stroke adds paint or removes it (the eraser shares the same stroke pipeline).</summary>
public enum PaintMode
{
    Paint,
    Erase,
}

/// <summary>Mirror painting around a vertical and/or horizontal line, or repeat it around a point (radial).</summary>
public enum Symmetry
{
    None,
    Vertical,
    Horizontal,
    Both,

    /// <summary>The stroke repeats around the origin, like spokes of a wheel (see <see cref="SymmetryPlacement.Spokes"/>).</summary>
    Radial,
}

/// <summary>Where the symmetry lines cross (document pixels; null = the canvas centre) and how many times radial
/// symmetry repeats a stroke.</summary>
public readonly record struct SymmetryPlacement(double? X = null, double? Y = null, int Spokes = 6)
{
    public const int MinSpokes = 2;
    public const int MaxSpokes = 32;

    public double OriginX(int width) => X ?? width / 2.0;
    public double OriginY(int height) => Y ?? height / 2.0;

    /// <summary>Radial repeats (a default-constructed placement means 6).</summary>
    public int Count => Spokes < MinSpokes ? 6 : Math.Min(Spokes, MaxSpokes);
}

/// <summary>
/// Settings for DoodleNook's dab brush. One engine, several genuinely different behaviours:
/// round (hard or soft), grainy (pencil, chalk — the paper texture shows through), chisel-shaped (marker),
/// and build-up (airbrush — keeps adding paint, even while the pen rests). Immutable; edit with <c>with</c>.
/// </summary>
public sealed record BrushSettings
{
    public const double MinSize = 1;
    public const double MaxSize = 1000;

    /// <summary>Diameter in document pixels at full pressure.</summary>
    public double Size { get; init; } = 12;

    /// <summary>Stroke opacity, 0–1. A stroke never gets darker than this, even where it crosses itself (except build-up brushes).</summary>
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

    /// <summary>Dab roundness: 1 = circle, smaller = a flatter ellipse (chisel marker, calligraphy).</summary>
    public double Roundness { get; init; } = 1;

    /// <summary>Angle of a flattened dab, in degrees.</summary>
    public double Angle { get; init; }

    /// <summary>How much paper grain shows through, 0 (none) to 1 (strong). Light pressure shows more grain.</summary>
    public double Grain { get; init; }

    /// <summary>Size of the paper grain (1 = default).</summary>
    public double GrainScale { get; init; } = 1;

    /// <summary>Paint accumulates within a stroke (airbrush) instead of staying at the stroke's opacity.</summary>
    public bool BuildUp { get; init; }

    /// <summary>For build-up brushes: how much paint each dab adds, 0–1.</summary>
    public double Flow { get; init; } = 1;

    /// <summary>Keep spraying while the pen rests (airbrush).</summary>
    public bool Spray { get; init; }

    // ---------------------------------------------------------------- Mission 007: Brush Laboratory settings
    // Every new setting defaults to "off", so brushes saved before Mission 007 paint exactly as they did.

    /// <summary>Distance between dabs as a fraction of the dab size (0 = automatic, fine enough for a smooth edge).</summary>
    public double Spacing { get; init; }

    /// <summary>An image tip ("builtin:…", "tip:…" or "stamp:…"); null = the round/elliptic tip.</summary>
    public string? TipId { get; init; }

    /// <summary>Image tips keep their own colours (stamp brushes) instead of painting in the current colour.</summary>
    public bool TipColors { get; init; }

    /// <summary>The tip turns with the direction of the stroke (added to <see cref="Angle"/>).</summary>
    public bool FollowDirection { get; init; }

    /// <summary>How far dabs are thrown from the stroke, as a multiple of the dab size (0 = on the line).</summary>
    public double Scatter { get; init; }

    /// <summary>Dabs placed at each step (more than one only makes sense with scatter).</summary>
    public int ScatterCount { get; init; } = 1;

    /// <summary>Random size variation per dab, 0–1 (1 = down to almost nothing).</summary>
    public double SizeJitter { get; init; }

    /// <summary>Random rotation per dab, 0–1 (1 = any angle).</summary>
    public double RotationJitter { get; init; }

    /// <summary>Random opacity variation per dab, 0–1.</summary>
    public double OpacityJitter { get; init; }

    /// <summary>Texture used for the grain ("builtin:…" or "pattern:…"); null = the paper grain.</summary>
    public string? TextureId { get; init; }

    /// <summary>Rotation of the texture, in degrees.</summary>
    public double TextureRotation { get; init; }

    /// <summary>Paint goes into the texture's dark parts instead of its light parts.</summary>
    public bool InvertTexture { get; init; }

    /// <summary>Shapes the pressure response of this brush on top of the pressure feel: below 1 a light touch gives
    /// more, above 1 it needs a firmer hand. 1 = unchanged.</summary>
    public double PressureGamma { get; init; } = 1;

    /// <summary>Pen pressure controls how fast a build-up brush adds paint.</summary>
    public bool PressureFlow { get; init; }

    /// <summary>Watercolour-like edges: paint collects at the rim of a stroke, 0–1.</summary>
    public double WetEdges { get; init; }

    /// <summary>True when any Mission 007 dynamic is in use (the engine then takes its extended path).</summary>
    public bool UsesExtendedDynamics =>
        Spacing > 0 || TipId is not null || FollowDirection || Scatter > 0 || ScatterCount > 1 || SizeJitter > 0 || RotationJitter > 0 ||
        OpacityJitter > 0 || PressureGamma != 1 || PressureFlow;

    /// <summary>Returns a copy with every value clamped to its valid range (NaN becomes the default).</summary>
    public BrushSettings Normalized() => this with
    {
        Size = Clamp(Size, MinSize, MaxSize, 12),
        Opacity = Clamp(Opacity, 0, 1, 1),
        Hardness = Clamp(Hardness, 0, 1, 0.9),
        MinimumSizeFraction = Clamp(MinimumSizeFraction, 0, 1, 0.05),
        Smoothing = Clamp(Smoothing, 0, 1, 0),
        Roundness = Clamp(Roundness, 0.1, 1, 1),
        Angle = double.IsFinite(Angle) ? Angle % 360 : 0,
        Grain = Clamp(Grain, 0, 1, 0),
        GrainScale = Clamp(GrainScale, 0.25, 8, 1),
        Flow = Clamp(Flow, 0.005, 1, 1),
        Spacing = Clamp(Spacing, 0, 5, 0),
        TipId = string.IsNullOrWhiteSpace(TipId) ? null : TipId,
        Scatter = Clamp(Scatter, 0, 5, 0),
        ScatterCount = Math.Clamp(ScatterCount, 1, 16),
        SizeJitter = Clamp(SizeJitter, 0, 1, 0),
        RotationJitter = Clamp(RotationJitter, 0, 1, 0),
        OpacityJitter = Clamp(OpacityJitter, 0, 1, 0),
        TextureId = string.IsNullOrWhiteSpace(TextureId) ? null : TextureId,
        TextureRotation = double.IsFinite(TextureRotation) ? TextureRotation % 360 : 0,
        PressureGamma = Clamp(PressureGamma, 0.2, 5, 1),
        WetEdges = Clamp(WetEdges, 0, 1, 0),
    };

    /// <summary>
    /// The dab diameter and per-dab opacity for a pen pressure (0–1, already sanitised),
    /// after the pressure curve has been applied.
    /// </summary>
    public (double Diameter, float Alpha) Evaluate(float pressure, PressureCurve curve)
    {
        float p = MapPressure(pressure, curve);
        double diameter = PressureSize ? Size * (MinimumSizeFraction + (1 - MinimumSizeFraction) * p) : Size;
        float alpha = PressureOpacity ? p : 1f;
        return (diameter, alpha);
    }

    /// <summary>The pressure after the pressure feel and this brush's own curve.</summary>
    public float MapPressure(float pressure, PressureCurve curve)
    {
        float p = curve.Map(pressure);
        if (PressureGamma != 1 && p > 0f && p < 1f) p = (float)Math.Pow(p, PressureGamma);
        return p;
    }

    private static double Clamp(double v, double min, double max, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
}
