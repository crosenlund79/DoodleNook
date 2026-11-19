namespace DoodleNook.Engine.Geometry;

/// <summary>A 2D affine transform: x' = M11·x + M21·y + Dx, y' = M12·x + M22·y + Dy.</summary>
public readonly record struct Affine(double M11, double M12, double M21, double M22, double Dx, double Dy)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public static Affine Translation(double dx, double dy) => new(1, 0, 0, 1, dx, dy);

    public static Affine Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Rotation by <paramref name="degrees"/> (clockwise on screen, where y points down).</summary>
    public static Affine Rotation(double degrees)
    {
        double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return new Affine(c, s, -s, c, 0, 0);
    }

    /// <summary>First this, then <paramref name="next"/>.</summary>
    public Affine Then(Affine next) => new(
        M11 * next.M11 + M12 * next.M21,
        M11 * next.M12 + M12 * next.M22,
        M21 * next.M11 + M22 * next.M21,
        M21 * next.M12 + M22 * next.M22,
        Dx * next.M11 + Dy * next.M21 + next.Dx,
        Dx * next.M12 + Dy * next.M22 + next.Dy);

    public double Determinant => M11 * M22 - M12 * M21;

    public bool IsInvertible => Math.Abs(Determinant) > 1e-12;

    public Affine Invert()
    {
        double det = Determinant;
        if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("Transform cannot be inverted.");
        double i11 = M22 / det, i12 = -M12 / det, i21 = -M21 / det, i22 = M11 / det;
        return new Affine(i11, i12, i21, i22, -(Dx * i11 + Dy * i21), -(Dx * i12 + Dy * i22));
    }

    public (double X, double Y) Apply(double x, double y) => (M11 * x + M21 * y + Dx, M12 * x + M22 * y + Dy);

    /// <summary>True if the transform is a pure translation by whole pixels (pixel-exact).</summary>
    public bool IsIntegerTranslation =>
        M11 == 1 && M22 == 1 && M12 == 0 && M21 == 0 && Dx == Math.Round(Dx) && Dy == Math.Round(Dy);

    /// <summary>Axis-aligned bounds of a transformed rectangle.</summary>
    public (double Left, double Top, double Right, double Bottom) TransformBounds(double x, double y, double w, double h)
    {
        var p = new[] { Apply(x, y), Apply(x + w, y), Apply(x, y + h), Apply(x + w, y + h) };
        return (p.Min(q => q.X), p.Min(q => q.Y), p.Max(q => q.X), p.Max(q => q.Y));
    }
}
