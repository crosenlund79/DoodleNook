using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Geometry;

/// <summary>What snapping may stick to.</summary>
/// <param name="Canvas">The canvas edges and centre.</param>
/// <param name="Guides">Guide lines.</param>
/// <param name="Objects">Edges and centres of text and shape layers.</param>
/// <param name="Grid">Grid spacing in pixels (0: the grid isn't used).</param>
public sealed record SnapOptions(bool Canvas = true, bool Guides = true, bool Objects = true, double Grid = 0);

/// <summary>The lines a snap can land on: x positions of vertical lines and y positions of horizontal lines.</summary>
public sealed class SnapTargets
{
    private readonly List<double> _xs = new();
    private readonly List<double> _ys = new();

    public IReadOnlyList<double> Xs => _xs;
    public IReadOnlyList<double> Ys => _ys;

    /// <summary>Grid spacing (0: none). Grid lines are not listed; every multiple of the spacing counts.</summary>
    public double Grid { get; init; }

    public void AddX(double x) => _xs.Add(x);
    public void AddY(double y) => _ys.Add(y);

    /// <summary>Everything a document offers; <paramref name="exclude"/> (the object being moved) is left out.</summary>
    public static SnapTargets For(Document doc, SnapOptions options, Layer? exclude = null)
    {
        var t = new SnapTargets { Grid = options.Grid > 0 ? options.Grid : 0 };
        if (options.Canvas)
        {
            t.AddX(0);
            t.AddX(doc.Width / 2.0);
            t.AddX(doc.Width);
            t.AddY(0);
            t.AddY(doc.Height / 2.0);
            t.AddY(doc.Height);
        }
        if (options.Guides)
            foreach (var g in doc.Guides)
                if (g.Vertical) t.AddX(g.Position);
                else t.AddY(g.Position);
        if (options.Objects)
            foreach (var l in doc.Layers)
            {
                if (ReferenceEquals(l, exclude) || !l.IsObject || !doc.IsShown(l) || doc.BoxOf(l) is not { } box) continue;
                var (left, top, right, bottom) = box.Extent();
                t.AddX(left);
                t.AddX((left + right) / 2);
                t.AddX(right);
                t.AddY(top);
                t.AddY((top + bottom) / 2);
                t.AddY(bottom);
            }
        return t;
    }

    /// <summary>The nearest target to <paramref name="v"/> among <paramref name="lines"/> and the grid, within
    /// <paramref name="tolerance"/>; null when none is that close.</summary>
    public double? Nearest(double v, bool vertical, double tolerance)
    {
        double? best = null;
        double bestDistance = tolerance;
        foreach (var line in vertical ? _xs : _ys)
        {
            double d = Math.Abs(line - v);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = line;
            }
        }
        if (Grid > 0)
        {
            double g = Math.Round(v / Grid) * Grid;
            if (Math.Abs(g - v) < bestDistance) best = g;
        }
        return best;
    }
}

/// <summary>Snapping for points and moving boxes.</summary>
public static class Snapping
{
    /// <summary>A point snapped independently in x and y. LineX/LineY say which lines it stuck to (for showing them).</summary>
    public static (double X, double Y, double? LineX, double? LineY) Point(SnapTargets targets, double x, double y, double tolerance)
    {
        var sx = targets.Nearest(x, vertical: true, tolerance);
        var sy = targets.Nearest(y, vertical: false, tolerance);
        return (sx ?? x, sy ?? y, sx, sy);
    }

    /// <summary>How far to shift a moving box so its left edge, centre or right edge (and top, middle or bottom) sticks to
    /// the nearest target within <paramref name="tolerance"/>.</summary>
    public static (double Dx, double Dy, double? LineX, double? LineY) Box(SnapTargets targets, double left, double top, double right, double bottom,
        double tolerance)
    {
        (double D, double? Line) Best(double[] candidates, bool vertical)
        {
            double bestD = 0, bestAbs = double.MaxValue;
            double? line = null;
            foreach (var c in candidates)
                if (targets.Nearest(c, vertical, tolerance) is { } n && Math.Abs(n - c) < bestAbs)
                {
                    bestAbs = Math.Abs(n - c);
                    bestD = n - c;
                    line = n;
                }
            return (bestD, line);
        }
        var (dx, lx) = Best([left, (left + right) / 2, right], vertical: true);
        var (dy, ly) = Best([top, (top + bottom) / 2, bottom], vertical: false);
        return (dx, dy, lx, ly);
    }
}

/// <summary>
/// The perspective assistant: vanishing points, the guide rays drawn from them, and strokes held to a perspective line.
/// One-point perspective offers lines towards the vanishing point plus horizontals and verticals; two-point offers lines
/// towards either vanishing point plus verticals.
/// </summary>
public static class Perspective
{
    /// <summary>The directions (unit vectors) a perspective line through (x, y) may take.</summary>
    public static IReadOnlyList<(double Dx, double Dy)> Directions(PerspectiveGuide guide, double x, double y)
    {
        var list = new List<(double, double)>();
        void Towards(double vx, double vy)
        {
            double dx = vx - x, dy = vy - y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 1e-6) list.Add((dx / len, dy / len));
        }
        if (guide.Points >= 1) Towards(guide.V1X, guide.V1Y);
        if (guide.Points >= 2) Towards(guide.V2X, guide.V2Y);
        if (guide.Points >= 1) list.Add((0, 1)); // verticals
        if (guide.Points == 1) list.Add((1, 0)); // horizontals (one-point perspective)
        return list;
    }

    /// <summary>The perspective line through <paramref name="sx"/>, <paramref name="sy"/> that best matches the movement
    /// to (<paramref name="cx"/>, <paramref name="cy"/>). Null when perspective is off or there was no movement.</summary>
    public static (double Dx, double Dy)? Choose(PerspectiveGuide guide, double sx, double sy, double cx, double cy)
    {
        double mx = cx - sx, my = cy - sy, len = Math.Sqrt(mx * mx + my * my);
        if (guide.Points == 0 || len < 1e-6) return null;
        mx /= len;
        my /= len;
        (double, double)? best = null;
        double bestDot = -1;
        foreach (var d in Directions(guide, sx, sy))
        {
            double dot = Math.Abs(d.Dx * mx + d.Dy * my);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = d;
            }
        }
        return best;
    }

    /// <summary>The point on the line through (sx, sy) with direction d nearest to (px, py).</summary>
    public static (double X, double Y) Project(double sx, double sy, (double Dx, double Dy) d, double px, double py)
    {
        double t = (px - sx) * d.Dx + (py - sy) * d.Dy;
        return (sx + t * d.Dx, sy + t * d.Dy);
    }

    /// <summary>Guide rays for display: <paramref name="count"/> lines fanning out from each vanishing point across the
    /// picture (document coordinates), plus the horizon line through the vanishing points.</summary>
    public static IReadOnlyList<(double X1, double Y1, double X2, double Y2)> Rays(PerspectiveGuide guide, int width, int height, int count = 24)
    {
        var rays = new List<(double, double, double, double)>();
        if (guide.Points == 0) return rays;
        double reach = 4.0 * Math.Max(width, height);
        void Fan(double vx, double vy)
        {
            for (int i = 0; i < count; i++)
            {
                double a = i * Math.PI * 2 / count;
                rays.Add((vx, vy, vx + Math.Cos(a) * reach, vy + Math.Sin(a) * reach));
            }
        }
        Fan(guide.V1X, guide.V1Y);
        if (guide.Points >= 2) Fan(guide.V2X, guide.V2Y);
        double hy = guide.Points >= 2 ? guide.V1Y : guide.V1Y;
        if (guide.Points >= 2)
        {
            double dx = guide.V2X - guide.V1X, dy = guide.V2Y - guide.V1Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 1e-6)
                rays.Add((guide.V1X - dx / len * reach, guide.V1Y - dy / len * reach, guide.V1X + dx / len * reach, guide.V1Y + dy / len * reach));
        }
        else rays.Add((-reach, hy, width + reach, hy));
        return rays;
    }

    /// <summary>Sensible starting vanishing points for a picture: one in the centre on the horizon (one-point), or one
    /// at each side (two-point).</summary>
    public static PerspectiveGuide Default(int points, int width, int height) => points switch
    {
        1 => new PerspectiveGuide(1, width / 2.0, height * 0.4, 0, 0),
        2 => new PerspectiveGuide(2, width * 0.1, height * 0.4, width * 0.9, height * 0.4),
        _ => PerspectiveGuide.Off,
    };
}

/// <summary>Straight lines with a modifier key: the end point optionally kept to 15° steps.</summary>
public static class StraightLine
{
    public static (double X, double Y) Constrain(double sx, double sy, double x, double y, bool snapAngle)
    {
        if (!snapAngle) return (x, y);
        double dx = x - sx, dy = y - sy, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return (x, y);
        double step = Math.PI / 12, a = Math.Round(Math.Atan2(dy, dx) / step) * step;
        return (sx + Math.Cos(a) * len, sy + Math.Sin(a) * len);
    }
}

/// <summary>Lining up text and shape layers with each other or with the canvas.</summary>
public enum AlignEdge
{
    Left,
    CenterX,
    Right,
    Top,
    CenterY,
    Bottom,
}

public static class Alignment
{
    /// <summary>How far to move a box so the chosen edge lines up with the same edge of <paramref name="reference"/>.</summary>
    public static (double Dx, double Dy) Offset(ObjectBox box, (double Left, double Top, double Right, double Bottom) reference, AlignEdge edge)
    {
        var (l, t, r, b) = box.Extent();
        return edge switch
        {
            AlignEdge.Left => (reference.Left - l, 0),
            AlignEdge.CenterX => ((reference.Left + reference.Right) / 2 - (l + r) / 2, 0),
            AlignEdge.Right => (reference.Right - r, 0),
            AlignEdge.Top => (0, reference.Top - t),
            AlignEdge.CenterY => (0, (reference.Top + reference.Bottom) / 2 - (t + b) / 2),
            _ => (0, reference.Bottom - b),
        };
    }
}
