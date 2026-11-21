using System.Numerics;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.References;

/// <summary>The three viewpoints game animations are drawn in.</summary>
public enum ReferenceView
{
    /// <summary>Profile, as in platformers.</summary>
    Side,
    /// <summary>Straight down from above, as in twin-stick and overhead games.</summary>
    TopDown,
    /// <summary>From above at an angle, as in classic RPGs.</summary>
    ThreeQuarter,
}

/// <summary>Where a reference figure is drawn: the view and facing, its standing height in pixels, and the point on the
/// canvas its feet stand on (the anchor).</summary>
public sealed record ReferenceFraming(ReferenceView View, ReferenceFacing Facing, double Height, double AnchorX, double AnchorY,
    bool HardEdges = false, bool Marker = true);

/// <summary>The direction a figure faces on screen.</summary>
public enum ReferenceFacing
{
    Right,
    Left,
    Up,
    Down,
    DownRight,
    DownLeft,
    UpRight,
    UpLeft,
}

/// <summary>
/// Mission 013: draws the mannequin (<see cref="Mannequin"/>) as seen from the side, from above or at three-quarter
/// view, in clean flat colours with a dark outline: a neutral, instructional figure to trace over, not finished art.
/// The figure is drawn at the requested size (so it is crisp at every size) and, for pixel art, with hard pixel edges.
/// A small cross marks the anchor where the feet stand (with a short ground line in side view).
/// </summary>
public static class ReferenceRenderer
{
    private static readonly Dictionary<PartPaint, uint> Paints = new()
    {
        [PartPaint.Body] = Pixel.Opaque(new ColorRgb(0xCF, 0xD3, 0xDA)),
        [PartPaint.Right] = Pixel.Opaque(new ColorRgb(0xE8, 0x9A, 0x5E)),
        [PartPaint.Left] = Pixel.Opaque(new ColorRgb(0x6F, 0x9C, 0xD3)),
        [PartPaint.Face] = Pixel.Opaque(new ColorRgb(0x5B, 0x60, 0x6E)),
        [PartPaint.Wood] = Pixel.Opaque(new ColorRgb(0xA0, 0x7A, 0x52)),
        [PartPaint.Metal] = Pixel.Opaque(new ColorRgb(0xB9, 0xC4, 0xCC)),
        [PartPaint.Glow] = Pixel.Opaque(new ColorRgb(0x9F, 0xE0, 0xE8)),
        [PartPaint.Shadow] = Pixel.FromStraight(0x2B, 0x2F, 0x3A, 60),
    };

    private static readonly uint Outline = Pixel.Opaque(new ColorRgb(0x2B, 0x2F, 0x3A));
    private static readonly uint MarkerColour = Pixel.Opaque(new ColorRgb(0xD9, 0x4F, 0x8C));

    /// <summary>The facings each view offers (side view: right and left; from above: four; three-quarter: four diagonals).</summary>
    public static IReadOnlyList<ReferenceFacing> FacingsFor(ReferenceView view) => view switch
    {
        ReferenceView.Side => [ReferenceFacing.Right, ReferenceFacing.Left],
        ReferenceView.TopDown => [ReferenceFacing.Right, ReferenceFacing.Down, ReferenceFacing.Left, ReferenceFacing.Up],
        _ => [ReferenceFacing.DownRight, ReferenceFacing.DownLeft, ReferenceFacing.UpRight, ReferenceFacing.UpLeft],
    };

    public static ReferenceFacing DefaultFacing(ReferenceView view) => FacingsFor(view)[0];

    /// <summary>The camera's tilt (0 = level, 90 = straight down).</summary>
    private static double Pitch(ReferenceView view) => view switch
    {
        ReferenceView.Side => 0,
        ReferenceView.TopDown => 90,
        _ => 36,
    };

    /// <summary>The compass heading of a facing (0 = away from the viewer, 90 = to the right).</summary>
    private static double Heading(ReferenceFacing f) => f switch
    {
        ReferenceFacing.Right => 90,
        ReferenceFacing.Left => 270,
        ReferenceFacing.Up => 0,
        ReferenceFacing.Down => 180,
        ReferenceFacing.DownRight => 135,
        ReferenceFacing.DownLeft => 225,
        ReferenceFacing.UpRight => 45,
        _ => 315,
    };

    /// <summary>Body space → screen (x right, y down, in body heights) and depth (larger = nearer the viewer).</summary>
    private readonly struct Camera
    {
        private readonly Vector3 _right, _forward;
        private readonly float _cos, _sin;

        public Camera(ReferenceView view, ReferenceFacing facing)
        {
            double h = Heading(facing) * Math.PI / 180, p = Pitch(view) * Math.PI / 180;
            _forward = new Vector3((float)Math.Sin(h), 0, (float)Math.Cos(h));
            _right = new Vector3((float)Math.Cos(h), 0, (float)-Math.Sin(h));
            _cos = (float)Math.Cos(p);
            _sin = (float)Math.Sin(p);
        }

        public (float X, float Y, float Depth) Project(Vector3 v)
        {
            // World: X east, Y up, Z north. The camera looks north from the south, tilted down by the pitch.
            var w = _right * v.X + Vector3.UnitY * v.Y + _forward * v.Z;
            return (w.X, -w.Y * _cos - w.Z * _sin, -w.Z * _cos + w.Y * _sin);
        }

        public float GroundSquash => _sin;
    }

    /// <summary>Draws the pose onto <paramref name="target"/> (over what is there).</summary>
    public static void Draw(RasterImage target, Pose pose, ReferenceFraming framing)
    {
        var cam = new Camera(framing.View, framing.Facing);
        double s = Math.Max(4, framing.Height);
        double ax = framing.AnchorX, ay = framing.AnchorY;
        bool hard = framing.HardEdges;
        double outline = Math.Max(1, s * 0.011);
        var parts = Mannequin.Build(pose);

        // The ground shadow first, then the anchor marker, then the figure from far to near.
        foreach (var p in parts.Where(p => p.Kind == PartKind.Shadow))
        {
            var (x, y, _) = cam.Project(p.A);
            double r = p.RadiusA * s;
            Ellipse(target, ax + x * s, ay + y * s, r, Math.Max(r * cam.GroundSquash, r * 0.12 + 0.75), Paints[PartPaint.Shadow], hard);
        }
        if (framing.Marker) DrawMarker(target, framing, s, hard);

        var drawn = parts.Where(p => p.Kind != PartKind.Shadow)
            .Select(p => (Part: p, A: cam.Project(p.A), B: cam.Project(p.B)))
            .OrderBy(t => (t.A.Depth + t.B.Depth) / 2)
            .ToList();
        foreach (var (p, a, b) in drawn)
        {
            double x0 = ax + a.X * s, y0 = ay + a.Y * s, x1 = ax + b.X * s, y1 = ay + b.Y * s;
            double ra = Math.Max(0.6, p.RadiusA * s), rb = Math.Max(0.6, p.RadiusB * s);
            uint fill = Paints[p.Paint];
            if (p.Kind == PartKind.Box)
            {
                Square(target, x0, y0, ra + outline, Outline, hard);
                Square(target, x0, y0, ra, fill, hard);
                // A plank line across the box makes it read as a crate.
                Capsule(target, x0 - ra * 0.8, y0, x0 + ra * 0.8, y0, Math.Max(0.5, outline * 0.5), Math.Max(0.5, outline * 0.5), Outline, hard);
                continue;
            }
            Capsule(target, x0, y0, x1, y1, ra + outline, rb + outline, Outline, hard);
            Capsule(target, x0, y0, x1, y1, ra, rb, fill, hard);
        }
    }

    /// <summary>A canvas-sized picture of the pose (transparent elsewhere).</summary>
    public static RasterImage Render(Pose pose, int width, int height, ReferenceFraming framing)
    {
        var img = new RasterImage(width, height);
        Draw(img, pose, framing);
        return img;
    }

    private static void DrawMarker(RasterImage target, ReferenceFraming f, double s, bool hard)
    {
        double arm = Math.Max(3, s * 0.05), w = Math.Max(0.5, s * 0.006);
        if (f.View == ReferenceView.Side)
        {
            double half = s * 0.32;
            Capsule(target, f.AnchorX - half, f.AnchorY, f.AnchorX + half, f.AnchorY, w * 0.7, w * 0.7, MarkerColour, hard);
        }
        Capsule(target, f.AnchorX - arm, f.AnchorY, f.AnchorX + arm, f.AnchorY, w, w, MarkerColour, hard);
        Capsule(target, f.AnchorX, f.AnchorY - arm, f.AnchorX, f.AnchorY + arm, w, w, MarkerColour, hard);
    }

    // ------------------------------------------------------------------ rasterising

    private static void Blend(RasterImage img, int x, int y, uint colour, double coverage)
    {
        if (coverage <= 0) return;
        int i = y * img.Width + x;
        uint src = coverage >= 1 ? colour : Pixel.Scale(colour, (uint)Math.Round(coverage * 255));
        img.Pixels[i] = Pixel.Over(src, img.Pixels[i]);
    }

    private static double Coverage(double inside, bool hard) => hard ? inside >= 0 ? 1 : 0 : Math.Clamp(inside + 0.5, 0, 1);

    /// <summary>A capsule (rounded segment) whose radius goes from <paramref name="ra"/> to <paramref name="rb"/>.</summary>
    private static void Capsule(RasterImage img, double x0, double y0, double x1, double y1, double ra, double rb, uint colour, bool hard)
    {
        double r = Math.Max(ra, rb) + 1;
        int left = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - r)), right = Math.Min(img.Width - 1, (int)Math.Ceiling(Math.Max(x0, x1) + r));
        int top = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - r)), bottom = Math.Min(img.Height - 1, (int)Math.Ceiling(Math.Max(y0, y1) + r));
        double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
        for (int y = top; y <= bottom; y++)
        {
            double py = y + 0.5;
            for (int x = left; x <= right; x++)
            {
                double px = x + 0.5;
                double t = len2 < 1e-9 ? 0 : Math.Clamp(((px - x0) * dx + (py - y0) * dy) / len2, 0, 1);
                double cx = x0 + dx * t - px, cy = y0 + dy * t - py;
                double d = Math.Sqrt(cx * cx + cy * cy);
                Blend(img, x, y, colour, Coverage(ra + (rb - ra) * t - d, hard));
            }
        }
    }

    private static void Square(RasterImage img, double cx, double cy, double half, uint colour, bool hard)
    {
        int left = Math.Max(0, (int)Math.Floor(cx - half - 1)), right = Math.Min(img.Width - 1, (int)Math.Ceiling(cx + half + 1));
        int top = Math.Max(0, (int)Math.Floor(cy - half - 1)), bottom = Math.Min(img.Height - 1, (int)Math.Ceiling(cy + half + 1));
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                double d = Math.Max(Math.Abs(x + 0.5 - cx), Math.Abs(y + 0.5 - cy));
                Blend(img, x, y, colour, Coverage(half - d, hard));
            }
    }

    private static void Ellipse(RasterImage img, double cx, double cy, double rx, double ry, uint colour, bool hard)
    {
        int left = Math.Max(0, (int)Math.Floor(cx - rx - 1)), right = Math.Min(img.Width - 1, (int)Math.Ceiling(cx + rx + 1));
        int top = Math.Max(0, (int)Math.Floor(cy - ry - 1)), bottom = Math.Min(img.Height - 1, (int)Math.Ceiling(cy + ry + 1));
        double m = Math.Min(rx, ry);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                double ex = (x + 0.5 - cx) / rx, ey = (y + 0.5 - cy) / ry;
                double k = Math.Sqrt(ex * ex + ey * ey);
                Blend(img, x, y, colour, Coverage((1 - k) * m, hard));
            }
    }
}
