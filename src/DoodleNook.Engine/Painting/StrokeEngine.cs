using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Painting;

/// <summary>
/// Turns input samples into paint on a <see cref="Document"/>.
///
/// How a stroke is built (independently written; the ideas are the usual ones in painting apps):
/// <list type="number">
/// <item>Samples are optionally smoothed, then joined by straight segments. Pressure is interpolated along each segment.</item>
/// <item>Round "dabs" are placed along the path at a spacing relative to the current diameter, small enough that the
/// stroke edge stays smooth (the scallop between dabs stays under about 0.1 px).</item>
/// <item>Dabs are not painted directly. Each dab raises a per-stroke coverage mask to its own coverage (a maximum, not a
/// sum). That is why a stroke never darkens where it overlaps or crosses itself.</item>
/// <item>Only the area touched since the last update is recomposited: original pixels + colour × coverage × opacity
/// (or the original pixels reduced by coverage, for the eraser).</item>
/// <item>When the stroke ends, the tiles it changed are stored as a single undo step.</item>
/// </list>
/// </summary>
public sealed class StrokeEngine
{
    private const int T = Document.TileSize;
    private const double MinSpacing = 0.25;

    private readonly Document _doc;
    private readonly Dictionary<int, float[]> _mask = new();
    private readonly Dictionary<int, uint[]> _before = new();
    private readonly Stack<float[]> _maskPool = new();
    private readonly uint[] _paintLut = new uint[256];

    private StrokeStyle _style;
    private BrushSettings _brush = new();
    private bool _active;

    private double _x, _y;          // last processed (possibly smoothed) position
    private float _p;               // pressure at that position
    private double _smX, _smY;      // smoothing filter state
    private StrokeSample _lastRaw;
    private double _distanceToNextDab;
    private double _distanceSinceDab;
    private float _lastDabPressure;
    private PixelRect _pending = PixelRect.Empty;

    public StrokeEngine(Document document)
    {
        _doc = document;
    }

    public Document Document => _doc;
    public bool IsActive => _active;

    /// <summary>Number of dabs placed in the current (or last) stroke.</summary>
    public int DabCount { get; private set; }

    /// <summary>Diameter of the most recent dab, in document pixels (for diagnostics and the brush cursor).</summary>
    public double LastDabDiameter { get; private set; }

    /// <summary>Area touched by the current (or last) stroke.</summary>
    public PixelRect StrokeBounds { get; private set; } = PixelRect.Empty;

    /// <summary>Starts a stroke and paints its first dab immediately. Returns the changed area.</summary>
    public PixelRect Begin(StrokeStyle style, StrokeSample first)
    {
        if (_active) End();
        if (!first.IsFinite) return PixelRect.Empty;

        _style = style;
        _brush = style.Brush.Normalized();
        BuildPaintLut();
        _active = true;
        DabCount = 0;
        StrokeBounds = PixelRect.Empty;
        _pending = PixelRect.Empty;

        float p = PressureCurve.Sanitize(first.Pressure);
        _x = _smX = first.X;
        _y = _smY = first.Y;
        _p = p;
        _lastRaw = first with { Pressure = p };
        Dab(first.X, first.Y, p);
        _distanceToNextDab = Spacing(LastDabDiameter);
        return Flush();
    }

    /// <summary>Adds one sample. Returns the changed area.</summary>
    public PixelRect Add(StrokeSample sample)
    {
        if (!_active) return PixelRect.Empty;
        Accept(sample);
        return Flush();
    }

    /// <summary>Adds several samples (for example all coalesced samples of one input event) and updates once.</summary>
    public PixelRect Add(ReadOnlySpan<StrokeSample> samples)
    {
        if (!_active) return PixelRect.Empty;
        foreach (var s in samples) Accept(s);
        return Flush();
    }

    /// <summary>Finishes the stroke and records it as one undo step (only if it changed any pixel).</summary>
    public PixelRect End()
    {
        if (!_active) return PixelRect.Empty;

        // Catch up with the pen if smoothing held the line back, so the stroke ends where the pen lifted.
        if (_brush.Smoothing > 0 && (_x != _lastRaw.X || _y != _lastRaw.Y))
            MoveTo(_lastRaw.X, _lastRaw.Y, _lastRaw.Pressure);

        // A final dab exactly at the end point gives every stroke a clean, round end.
        if (_distanceSinceDab > 0.01) Dab(_x, _y, _p);

        var changed = Flush();
        CommitHistory();
        Reset();
        return changed;
    }

    /// <summary>Abandons the stroke and restores the original pixels (nothing is added to the history).</summary>
    public PixelRect Cancel()
    {
        if (!_active) return PixelRect.Empty;
        var area = PixelRect.Empty;
        foreach (var (key, before) in _before)
        {
            var rect = TileRectFromKey(key);
            WriteTile(rect, before);
            area = area.Union(rect);
        }
        Reset();
        _doc.NotifyPixelsChanged(area);
        return area;
    }

    // ------------------------------------------------------------------ path

    private void Accept(StrokeSample s)
    {
        if (!s.IsFinite) return;
        float p = PressureCurve.Sanitize(s.Pressure);
        if (s.X == _lastRaw.X && s.Y == _lastRaw.Y && p == _lastRaw.Pressure) return; // duplicate event
        _lastRaw = s with { Pressure = p };

        double tx = s.X, ty = s.Y;
        if (_brush.Smoothing > 0)
        {
            double k = 1 - 0.85 * _brush.Smoothing;
            _smX += (s.X - _smX) * k;
            _smY += (s.Y - _smY) * k;
            tx = _smX;
            ty = _smY;
        }
        MoveTo(tx, ty, p);
    }

    private void MoveTo(double x, double y, float p)
    {
        double dx = x - _x, dy = y - _y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9)
        {
            // Pressing harder without moving grows the dot, like a real pen.
            if (p > _lastDabPressure + 0.02f)
            {
                Dab(x, y, p);
                _distanceToNextDab = Spacing(LastDabDiameter);
            }
            _p = p;
            return;
        }

        double t = _distanceToNextDab;
        while (t <= len)
        {
            double u = t / len;
            float pp = _p + (p - _p) * (float)u;
            Dab(_x + dx * u, _y + dy * u, pp);
            t += Spacing(LastDabDiameter);
        }
        _distanceToNextDab = t - len;
        _distanceSinceDab = Spacing(LastDabDiameter) - _distanceToNextDab;
        _x = x;
        _y = y;
        _p = p;
    }

    /// <summary>
    /// Distance between dabs for a diameter: at most 10 % of the diameter, and close enough that the scallops between
    /// neighbouring dabs stay below ~0.1 px (s² / 8r ≤ 0.1, relaxed for soft edges).
    /// </summary>
    internal double Spacing(double diameter)
    {
        double r = Math.Max(diameter * 0.5, 0.5);
        double fall = EdgeWidth(r);
        double s = Math.Min(0.1 * diameter, Math.Sqrt(0.8 * r * fall));
        return Math.Max(MinSpacing, s);
    }

    private double EdgeWidth(double radius) => Math.Max(1.0, radius * (1 - _brush.Hardness));

    // ------------------------------------------------------------------ dabs

    private void Dab(double cx, double cy, float pressure)
    {
        var (diameter, alpha) = _brush.Evaluate(pressure, _style.Curve);
        LastDabDiameter = diameter;
        _lastDabPressure = pressure;
        _distanceSinceDab = 0;
        DabCount++;

        double r = diameter * 0.5;
        if (r < 0.5)
        {
            // Below one pixel, keep the dab one pixel wide but fade it by area, so thin tapers fade instead of breaking up.
            alpha *= (float)(4 * r * r);
            r = 0.5;
        }
        if (alpha <= 0) return;

        double fall = EdgeWidth(r);
        bool linear = fall <= 1.0001;
        double outer = r + 0.5;            // coverage is 0 beyond this distance
        double inner = outer - fall;       // coverage is 1 within this distance
        double outer2 = outer * outer;
        double inner2 = inner > 0 ? inner * inner : -1;

        var box = PixelRect.FromBounds(cx - outer, cy - outer, cx + outer, cy + outer).Intersect(_doc.Bounds);
        if (box.IsEmpty) return;

        int tx0 = box.X / T, ty0 = box.Y / T, tx1 = (box.Right - 1) / T, ty1 = (box.Bottom - 1) / T;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int key = ty * _doc.TilesX + tx;
                CaptureBefore(key, tx, ty);
                var m = MaskTile(key);
                var part = box.Intersect(new PixelRect(tx * T, ty * T, T, T));
                for (int y = part.Y; y < part.Bottom; y++)
                {
                    double fy = y + 0.5 - cy;
                    double fy2 = fy * fy;
                    int row = (y - ty * T) * T - tx * T;
                    for (int x = part.X; x < part.Right; x++)
                    {
                        double fx = x + 0.5 - cx;
                        double d2 = fx * fx + fy2;
                        if (d2 >= outer2) continue;
                        float cov;
                        if (d2 <= inner2) cov = alpha;
                        else
                        {
                            double t = (outer - Math.Sqrt(d2)) / fall;
                            if (t >= 1) cov = alpha;
                            else
                            {
                                if (!linear) t = t * t * (3 - 2 * t);
                                cov = (float)(t * alpha);
                            }
                        }
                        ref float slot = ref m[row + x];
                        if (cov > slot) slot = cov;
                    }
                }
            }
        }
        _pending = _pending.Union(box);
        StrokeBounds = StrokeBounds.Union(box);
    }

    // ------------------------------------------------------------------ compositing

    private void BuildPaintLut()
    {
        var c = _style.Color;
        for (int a = 0; a < 256; a++)
        {
            byte ab = (byte)a;
            _paintLut[a] = Pixel.Pack(ab, Pixel.Mul(c.R, ab), Pixel.Mul(c.G, ab), Pixel.Mul(c.B, ab));
        }
    }

    private PixelRect Flush()
    {
        var area = _pending;
        _pending = PixelRect.Empty;
        if (area.IsEmpty) return area;

        var layer = _doc.Layer.Pixels;
        int w = _doc.Width;
        float opacity = (float)_brush.Opacity * 255f;
        bool erase = _style.Mode == PaintMode.Erase;

        int tx0 = area.X / T, ty0 = area.Y / T, tx1 = (area.Right - 1) / T, ty1 = (area.Bottom - 1) / T;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int key = ty * _doc.TilesX + tx;
                if (!_mask.TryGetValue(key, out var m) || !_before.TryGetValue(key, out var before)) continue;
                var part = area.Intersect(new PixelRect(tx * T, ty * T, T, T));
                for (int y = part.Y; y < part.Bottom; y++)
                {
                    int local = (y - ty * T) * T - tx * T;
                    int dst = y * w;
                    for (int x = part.X; x < part.Right; x++)
                    {
                        float cov = m[local + x];
                        uint baseline = before[local + x];
                        if (cov <= 0f)
                        {
                            layer[dst + x] = baseline;
                            continue;
                        }
                        int a = (int)(cov * opacity + 0.5f);
                        if (a > 255) a = 255;
                        layer[dst + x] = erase ? Reduce(baseline, (byte)(255 - a)) : Pixel.Over(_paintLut[a], baseline);
                    }
                }
            }
        }
        _doc.NotifyPixelsChanged(area);
        return area;
    }

    private static uint Reduce(uint p, byte keep)
    {
        if (keep == 255 || p == 0) return p;
        if (keep == 0) return 0;
        return Pixel.Pack(Pixel.Mul(Pixel.A(p), keep), Pixel.Mul(Pixel.R(p), keep), Pixel.Mul(Pixel.G(p), keep), Pixel.Mul(Pixel.B(p), keep));
    }

    // ------------------------------------------------------------------ tiles and history

    private float[] MaskTile(int key)
    {
        if (!_mask.TryGetValue(key, out var tile))
        {
            tile = _maskPool.Count > 0 ? _maskPool.Pop() : new float[T * T];
            _mask[key] = tile;
        }
        return tile;
    }

    private void CaptureBefore(int key, int tx, int ty)
    {
        if (_before.ContainsKey(key)) return;
        var copy = new uint[T * T];
        var rect = _doc.TileRect(tx, ty);
        var src = _doc.Layer.Pixels;
        for (int y = 0; y < rect.Height; y++)
            Array.Copy(src, (rect.Y + y) * _doc.Width + rect.X, copy, y * T, rect.Width);
        _before[key] = copy;
    }

    private PixelRect TileRectFromKey(int key) => _doc.TileRect(key % _doc.TilesX, key / _doc.TilesX);

    private void WriteTile(PixelRect rect, uint[] tile)
    {
        var dst = _doc.Layer.Pixels;
        for (int y = 0; y < rect.Height; y++)
            Array.Copy(tile, y * T, dst, (rect.Y + y) * _doc.Width + rect.X, rect.Width);
    }

    private void CommitHistory()
    {
        var patches = new List<TilePatch>();
        var layer = _doc.Layer.Pixels;
        foreach (var (key, before) in _before.OrderBy(kv => kv.Key))
        {
            var rect = TileRectFromKey(key);
            bool changed = false;
            for (int y = 0; y < rect.Height && !changed; y++)
            {
                int row = (rect.Y + y) * _doc.Width + rect.X;
                for (int x = 0; x < rect.Width; x++)
                {
                    if (layer[row + x] != before[y * T + x]) { changed = true; break; }
                }
            }
            if (!changed) continue;
            var packed = new uint[rect.Area];
            for (int y = 0; y < rect.Height; y++)
                Array.Copy(before, y * T, packed, y * rect.Width, rect.Width);
            patches.Add(new TilePatch(rect, packed));
        }
        if (patches.Count == 0) return; // no-op stroke: nothing to undo
        var name = _style.Mode == PaintMode.Erase ? "Erase" : "Brush stroke";
        _doc.Commit(_doc.History.Create(name, patches));
    }

    private void Reset()
    {
        foreach (var tile in _mask.Values)
        {
            Array.Clear(tile);
            if (_maskPool.Count < 256) _maskPool.Push(tile);
        }
        _mask.Clear();
        _before.Clear();
        _pending = PixelRect.Empty;
        _active = false;
    }
}
