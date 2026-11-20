using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>What the Liquify brush does.</summary>
public enum LiquifyMode
{
    /// <summary>Pushes the picture along with the brush.</summary>
    Push,

    /// <summary>Swells what is under the brush (it grows while the pen rests).</summary>
    Inflate,

    /// <summary>Shrinks what is under the brush toward its centre.</summary>
    Pinch,

    /// <summary>Turns what is under the brush clockwise (anticlockwise when reversed).</summary>
    Twirl,

    /// <summary>Evens out sharp kinks in the reshaping.</summary>
    Smooth,

    /// <summary>Brings the original back where the brush goes.</summary>
    Restore,
}

/// <summary>How the reshaped picture is read from the original.</summary>
public enum LiquifySampling
{
    /// <summary>Bilinear: smooth, for painted and drawn artwork.</summary>
    Smooth,

    /// <summary>Nearest pixel: exact pixels, for pixel art (no new colours, no blurred edges).</summary>
    Pixels,

    /// <summary>Colour from the nearest pixel, transparency smoothed: for palette-locked artwork (only palette colours,
    /// soft edges stay soft).</summary>
    PaletteSafe,
}

/// <summary>
/// A Liquify session on one layer (or its mask, or the current drawing of an animation). The original pixels are kept
/// as a stable source and the reshaping is a displacement field: each pixel shows the original at (x + dx, y + dy).
/// Brush dabs change the field locally (composing the new move with what is already there, so long pushes don't
/// smear), and only the touched area is drawn again from the source: no repeated resampling, no growing blur.
/// The field is stored in tiles that exist only where the brush has been.
/// <para>Dabs are placed at a fixed spacing along the path (and, for the modes that keep working while the pen rests,
/// at a fixed rate in time), so the result follows the gesture, not the number of input events.</para>
/// <para><see cref="Apply"/> records the whole session as one undo step; <see cref="Cancel"/> puts the layer back as it
/// was. Selections limit the effect, transparency-locked layers keep their shape, and a palette-locked picture keeps
/// only palette colours.</para>
/// </summary>
public sealed class LiquifySession
{
    private const int T = Document.TileSize;
    private const double HoldIntervalMs = 30;

    private readonly Document _doc;
    private readonly LayerEdit _edit;
    private RasterImage? _source;
    private readonly Dictionary<int, float[]> _field = new();
    private readonly SelectionMask? _selection;
    private readonly bool _alphaLock;
    private double _x, _y, _toNext, _holdMs;
    private float _p = 1;
    private bool _stroking;

    // Per-stroke undo inside the session: the field tiles each stroke changed, as they were before it (null: new tile).
    private readonly List<Dictionary<int, float[]?>> _strokes = new();
    private Dictionary<int, float[]?>? _stroke;
    private const int MaxStrokeUndo = 40;

    private LiquifySession(Document doc, Layer layer, LayerEdit edit, LiquifySampling sampling)
    {
        _doc = doc;
        Layer = layer;
        _edit = edit;
        Sampling = sampling;
        _source = edit.Target.Clone();
        _selection = doc.Selection?.Clone();
        _alphaLock = edit.AlphaLocked;
    }

    public Layer Layer { get; }
    public LiquifySampling Sampling { get; }
    public LiquifyMode Mode { get; set; } = LiquifyMode.Push;

    /// <summary>Brush diameter in pixels.</summary>
    public double Size { get; set; } = 150;

    /// <summary>0–1: how strongly each dab reshapes (Push at 1 moves the centre exactly with the brush).</summary>
    public double Strength { get; set; } = 0.6;

    /// <summary>0–1: how gradually the effect fades toward the brush's edge (0: evenly to the edge).</summary>
    public double Softness { get; set; } = 0.7;

    /// <summary>The opposite effect (Alt): Twirl turns anticlockwise, Inflate and Pinch swap.</summary>
    public bool Reverse { get; set; }

    /// <summary>True when a stroke can be taken back (Ctrl+Z inside Liquify).</summary>
    public bool CanUndoStroke => _strokes.Count > 0;

    public bool IsFinished { get; private set; }
    public bool IsStroking => _stroking;

    /// <summary>The area drawn again so far (where the picture may differ from the original).</summary>
    public PixelRect Touched { get; private set; } = PixelRect.Empty;

    /// <summary>Memory used by the displacement field (it exists only where the brush has been).</summary>
    public long FieldBytes => _field.Count * (long)T * T * 2 * sizeof(float);

    public int Dabs { get; private set; }

    /// <summary>Starts Liquify on a layer. Null when the layer is locked or has no pixels (text and shapes must be
    /// converted to pixels first).</summary>
    public static LiquifySession? Begin(Document doc, Layer layer, LiquifySampling sampling)
    {
        if (layer.Locked || (!layer.IsPaintable && !doc.EditsMask(layer))) return null;
        return new LiquifySession(doc, layer, doc.BeginEdit(layer, "Liquify"), sampling);
    }

    // ================================================================== strokes

    public void BeginStroke(double x, double y, float pressure = 1)
    {
        if (IsFinished) return;
        _stroking = true;
        _stroke = new Dictionary<int, float[]?>();
        _strokes.Add(_stroke);
        if (_strokes.Count > MaxStrokeUndo) _strokes.RemoveAt(0);
        (_x, _y, _p) = (x, y, Pressure(pressure));
        _holdMs = 0;
        _toNext = Spacing;
        if (Mode != LiquifyMode.Push) Dab(x, y, 0, 0);
    }

    public void StrokeTo(double x, double y, float pressure = 1)
    {
        if (!_stroking || IsFinished || !double.IsFinite(x) || !double.IsFinite(y)) return;
        float p = Pressure(pressure);
        double dx = x - _x, dy = y - _y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9)
        {
            _p = p;
            return;
        }
        // Dabs every Spacing along the path; Push moves the picture by that distance, in the path's direction.
        double ux = dx / len, uy = dy / len, step = Spacing, t = _toNext;
        float p0 = _p;
        while (t <= len)
        {
            _p = p0 + (p - p0) * (float)(t / len);
            Dab(_x + dx * t / len, _y + dy * t / len, ux * step, uy * step);
            t += step;
        }
        _toNext = t - len;
        _x = x;
        _y = y;
        _p = p;
    }

    /// <summary>While the pen rests: Inflate, Pinch, Twirl, Smooth and Restore keep working at a steady rate.</summary>
    public void Hold(double elapsedMs)
    {
        if (!_stroking || IsFinished || Mode == LiquifyMode.Push || elapsedMs <= 0) return;
        _holdMs += elapsedMs;
        while (_holdMs >= HoldIntervalMs)
        {
            _holdMs -= HoldIntervalMs;
            Dab(_x, _y, 0, 0);
        }
    }

    public void EndStroke()
    {
        _stroking = false;
        if (_stroke is { Count: 0 }) _strokes.Remove(_stroke); // nothing changed: nothing to take back
        _stroke = null;
    }

    /// <summary>Takes back the last stroke of this session (the field where it went, and the picture there).</summary>
    public bool UndoStroke()
    {
        if (IsFinished || _stroking || _strokes.Count == 0) return false;
        var last = _strokes[^1];
        _strokes.RemoveAt(_strokes.Count - 1);
        var area = PixelRect.Empty;
        foreach (var (key, before) in last)
        {
            if (before is null) _field.Remove(key);
            else _field[key] = before;
            area = area.Union(_doc.TileRect(key % _doc.TilesX, key / _doc.TilesX));
        }
        area = area.Intersect(Touched);
        if (!area.IsEmpty) Render(area);
        return true;
    }

    private double Spacing => Math.Max(1, Size * 0.12);

    private static float Pressure(float p) => float.IsNaN(p) ? 1 : Math.Clamp(p, 0.05f, 1);

    // ================================================================== the field

    /// <summary>The field tile to change (created if needed), remembered as it was for this stroke's undo.</summary>
    private float[] EditTile(int tx, int ty)
    {
        int key = ty * _doc.TilesX + tx;
        bool had = _field.TryGetValue(key, out var tile);
        if (_stroke is not null && !_stroke.ContainsKey(key)) _stroke[key] = had ? (float[])tile!.Clone() : null;
        if (!had) _field[key] = tile = new float[T * T * 2];
        return tile!;
    }

    private (float Dx, float Dy) FieldAt(int x, int y)
    {
        if ((uint)x >= (uint)_doc.Width || (uint)y >= (uint)_doc.Height) return (0, 0);
        if (!_field.TryGetValue(y / T * _doc.TilesX + x / T, out var tile)) return (0, 0);
        int i = ((y % T) * T + x % T) * 2;
        return (tile[i], tile[i + 1]);
    }

    private float Falloff(double t)
    {
        if (t >= 1) return 0;
        double hard = 1 - Math.Clamp(Softness, 0.02, 1);
        if (t <= hard) return 1;
        double u = 1 - (t - hard) / (1 - hard);
        return (float)(u * u * (3 - 2 * u));
    }

    /// <summary>One dab at (cx, cy) in document coordinates; (vx, vy) is the brush movement for Push.</summary>
    private void Dab(double cx, double cy, double vx, double vy)
    {
        Dabs++;
        double r = Size / 2;
        var box = PixelRect.FromBounds(cx - r, cy - r, cx + r, cy + r).Intersect(_doc.Bounds);
        if (box.IsEmpty) return;
        // A copy of the field around the dab (reads come from here, so the update doesn't read its own writes).
        int margin = (int)Math.Ceiling(Math.Max(r * 0.5, Math.Sqrt(vx * vx + vy * vy))) + 3;
        var snap = PixelRect.FromBounds(box.X - margin, box.Y - margin, box.Right + margin, box.Bottom + margin);
        int sw = snap.Width, sh = snap.Height;
        var fx = new float[sw * sh];
        var fy = new float[sw * sh];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                var (dx, dy) = FieldAt(snap.X + x, snap.Y + y);
                fx[y * sw + x] = dx;
                fy[y * sw + x] = dy;
            }
        (float, float) Sample(double x, double y)
        {
            // Bilinear in the copy (positions are pixel indices; outside the copy the field is taken as it is at its edge).
            x = Math.Clamp(x - snap.X, 0, sw - 1);
            y = Math.Clamp(y - snap.Y, 0, sh - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
            float ax = (float)(x - x0), ay = (float)(y - y0);
            float L(float[] f) => (f[y0 * sw + x0] * (1 - ax) + f[y0 * sw + x1] * ax) * (1 - ay) + (f[y1 * sw + x0] * (1 - ax) + f[y1 * sw + x1] * ax) * ay;
            return (L(fx), L(fy));
        }
        float k = (float)(Math.Clamp(Strength, 0, 1) * _p);
        var mode = Reverse && Mode == LiquifyMode.Inflate ? LiquifyMode.Pinch : Reverse && Mode == LiquifyMode.Pinch ? LiquifyMode.Inflate : Mode;
        double pcx = cx - 0.5, pcy = cy - 0.5; // the centre in pixel-index space
        double turn = (Reverse ? 1 : -1) * 0.07; // radians per dab at full strength (sampling turns the other way)
        for (int y = box.Y; y < box.Bottom; y++)
            for (int x = box.X; x < box.Right; x++)
            {
                double ox = x - pcx, oy = y - pcy;
                float w = Falloff(Math.Sqrt(ox * ox + oy * oy) / r) * k;
                if (_selection is not null) w *= _selection[x, y] / 255f;
                if (w <= 0) continue;
                var tile = EditTile(x / T, y / T);
                int i = ((y % T) * T + x % T) * 2;
                float ndx, ndy;
                switch (mode)
                {
                    case LiquifyMode.Restore:
                    {
                        float keep = 1 - Math.Min(1, w * 0.3f);
                        ndx = tile[i] * keep;
                        ndy = tile[i + 1] * keep;
                        break;
                    }
                    case LiquifyMode.Smooth:
                    {
                        float ax = 0, ay = 0;
                        for (int j = -2; j <= 2; j++)
                            for (int q = -2; q <= 2; q++)
                            {
                                var (sx, sy) = Sample(x + q, y + j);
                                ax += sx;
                                ay += sy;
                            }
                        ndx = tile[i] + (ax / 25 - tile[i]) * Math.Min(1, w * 0.6f);
                        ndy = tile[i + 1] + (ay / 25 - tile[i + 1]) * Math.Min(1, w * 0.6f);
                        break;
                    }
                    default:
                    {
                        // m: where this pixel now looks in the picture as it was before the dab.
                        double mx, my;
                        switch (mode)
                        {
                            case LiquifyMode.Push:
                                mx = x - w * vx;
                                my = y - w * vy;
                                break;
                            case LiquifyMode.Inflate:
                                mx = x - w * 0.06 * ox;
                                my = y - w * 0.06 * oy;
                                break;
                            case LiquifyMode.Pinch:
                                mx = x + w * 0.06 * ox;
                                my = y + w * 0.06 * oy;
                                break;
                            default:
                            {
                                double a = turn * w, c = Math.Cos(a), s = Math.Sin(a);
                                mx = pcx + ox * c - oy * s;
                                my = pcy + ox * s + oy * c;
                                break;
                            }
                        }
                        var (pdx, pdy) = Sample(mx, my);
                        ndx = (float)(mx - x) + pdx;
                        ndy = (float)(my - y) + pdy;
                        break;
                    }
                }
                tile[i] = ndx;
                tile[i + 1] = ndy;
            }
        Render(box);
    }

    // ================================================================== drawing

    /// <summary>Draws an area again from the original through the field.</summary>
    private void Render(PixelRect area)
    {
        if (_source is null) return;
        _edit.Capture(area);
        var dst = _edit.Target.Pixels;
        int w = _doc.Width;
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                var (dx, dy) = FieldAt(x, y);
                uint original = _source.Pixels[y * w + x];
                uint p = dx == 0 && dy == 0 ? original : SampleSource(x + dx, y + dy);
                if (_alphaLock) p = KeepAlpha(p, original);
                dst[y * w + x] = p;
            }
        _edit.Changed(area); // palette-locked pictures keep only palette colours
        Touched = Touched.Union(area);
    }

    private uint SampleSource(double x, double y)
    {
        var src = _source!;
        if (Sampling == LiquifySampling.Pixels) return src.GetOrTransparent((int)Math.Round(x), (int)Math.Round(y));
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        float ax = (float)(x - x0), ay = (float)(y - y0);
        uint p00 = src.GetOrTransparent(x0, y0), p10 = src.GetOrTransparent(x0 + 1, y0), p01 = src.GetOrTransparent(x0, y0 + 1), p11 = src.GetOrTransparent(x0 + 1, y0 + 1);
        float w00 = (1 - ax) * (1 - ay), w10 = ax * (1 - ay), w01 = (1 - ax) * ay, w11 = ax * ay;
        float Ch(int shift) => ((p00 >> shift) & 255) * w00 + ((p10 >> shift) & 255) * w10 + ((p01 >> shift) & 255) * w01 + ((p11 >> shift) & 255) * w11;
        float a = Ch(24);
        if (Sampling == LiquifySampling.PaletteSafe)
        {
            // The nearest pixel's colour (a palette colour) with the smoothed transparency.
            uint nearest = src.GetOrTransparent((int)Math.Round(x), (int)Math.Round(y));
            byte na = (byte)Math.Clamp((int)(a + 0.5f), 0, 255);
            if (Pixel.A(nearest) == 0)
            {
                // Smoothed alpha but no colour here: take the most opaque neighbour's colour.
                nearest = new[] { p00, p10, p01, p11 }.MaxBy(Pixel.A);
                if (Pixel.A(nearest) == 0) return 0;
            }
            var (r, g, b, _) = Pixel.ToStraight(nearest);
            return Pixel.FromStraight(r, g, b, na);
        }
        byte A = (byte)Math.Clamp((int)(a + 0.5f), 0, 255);
        if (A == 0) return 0;
        byte C(int shift) => (byte)Math.Clamp((int)(Ch(shift) + 0.5f), 0, A);
        return Pixel.Pack(A, C(16), C(8), C(0));
    }

    private static uint KeepAlpha(uint p, uint original)
    {
        byte a = Pixel.A(original);
        if (a == 0) return 0;
        if (Pixel.A(p) == 0) return original;
        var (r, g, b, _) = Pixel.ToStraight(p);
        return Pixel.FromStraight(r, g, b, a);
    }

    // ================================================================== finishing

    /// <summary>Discards all reshaping so far (the session continues).</summary>
    public void Reset()
    {
        if (IsFinished) return;
        _field.Clear();
        _strokes.Clear();
        if (!Touched.IsEmpty) Render(Touched);
    }

    /// <summary>Keeps the reshaped picture: one undo step for the whole session. Returns false if nothing changed.</summary>
    public bool Apply()
    {
        if (IsFinished) return false;
        IsFinished = true;
        _stroking = false;
        bool changed = _edit.Commit();
        Release();
        return changed;
    }

    /// <summary>Puts the layer back exactly as it was (nothing is recorded).</summary>
    public void Cancel()
    {
        if (IsFinished) return;
        IsFinished = true;
        _stroking = false;
        _edit.Cancel();
        Release();
    }

    private void Release()
    {
        _field.Clear();
        _strokes.Clear();
        _source = null;
    }
}
