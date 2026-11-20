using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Painting;

/// <summary>One input sample in document coordinates (pixels; 0,0 is the top-left corner of the canvas).</summary>
public readonly record struct StrokeSample(double X, double Y, float Pressure = 1f)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

/// <summary>Everything a stroke needs: the brush, pressure feel, colour, paint or erase, mirror symmetry (and where its
/// lines cross) and (for image tips and textures) the loaded tip and texture.</summary>
public readonly record struct StrokeStyle(BrushSettings Brush, PressureCurve Curve, ColorRgb Color, PaintMode Mode, Symmetry Symmetry = Symmetry.None,
    BrushResources? Resources = null, SymmetryPlacement Placement = default);
