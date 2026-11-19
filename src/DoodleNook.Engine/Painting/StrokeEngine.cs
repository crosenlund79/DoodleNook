using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Painting;

/// <summary>
/// Turns input samples into paint on a layer of a <see cref="Document"/>.
///
/// How a stroke is built (independently written; the ideas are the usual ones in painting apps):
/// <list type="number">
/// <item>Samples are optionally smoothed, then joined by straight segments. Pressure is interpolated along each segment.</item>
/// <item>Round or flattened "dabs" are placed along the path at a spacing relative to the current diameter, small enough
/// that the stroke edge stays smooth (the scallop between dabs stays under about 0.1 px).</item>
/// <item>Dabs are not painted directly. Each dab raises a per-stroke coverage mask to its own coverage (a maximum, not a
/// sum), so a stroke never darkens where it crosses itself. Build-up brushes (airbrush) add to the mask instead.</item>
/// <item>Grainy brushes multiply coverage by a fixed paper texture; light pressure lets more grain show.</item>
/// <item>Only the touched area is recomposited: original pixels + colour × coverage × opacity × selection
/// (or reduced, for the eraser). Transparency-locked layers keep their alpha.</item>
/// <item>When the stroke ends, the changed tiles are stored as a single undo step.</item>
/// </list>
/// </summary>
public sealed class StrokeEngine
{
    private const int T = Document.TileSize;
    private const double MinSpacing = 0.25;
    private const double SprayIntervalMs = 25;

    private readonly Document _doc;
    private readonly Dictionary<int, float[]> _mask = new();
    private readonly Stack<float[]> _maskPool = new();
    private readonly uint[] _paintLut = new uint[256];

    private LayerEdit? _edit;
    private StrokeStyle _style;
    private BrushSettings _brush = new();
    private bool _active;

    private double _x, _y;
    private float _p;
    private double _smX, _smY;
    private StrokeSample _lastRaw;
    private double _distanceToNextDab;
    private double _distanceSinceDab;
    private float _lastDabPressure;
    private double _sprayMs;
    private PixelRect _pending = PixelRect.Empty;

    public StrokeEngine(Document document)
    {
        _doc = document;
    }

    public Document Document => _doc;
    public bool IsActive => _active;

    /// <summary>The layer being painted by the current (or last) stroke.</summary>
    public Layer? TargetLayer { get; private set; }

    public int DabCount { get; private set; }
    public double LastDabDiameter { get; private set; }
    public PixelRect StrokeBounds { get; private set; } = PixelRect.Empty;

    /// <summary>Starts a stroke on the active layer (or <paramref name="layer"/>) and paints its first dab immediately.
    /// Returns the changed area. Locked layers are left alone (the stroke does not start).</summary>
    public PixelRect Begin(StrokeStyle style, StrokeSample first, Layer? layer = null)
    {
        if (_active) End();
        layer ??= _doc.ActiveLayer;
        if (!first.IsFinite || layer.Locked) return PixelRect.Empty;

        _style = style;
        _brush = style.Brush.Normalized();
        BuildPaintLut();
        _edit = _doc.BeginEdit(layer, style.Mode == PaintMode.Erase ? "Erase" : "Brush stroke");
        TargetLayer = layer;
        _active = true;
        DabCount = 0;
        StrokeBounds = PixelRect.Empty;
        _pending = PixelRect.Empty;
        _sprayMs = 0;

        float p = PressureCurve.Sanitize(first.Pressure);
        _x = _smX = first.X;
        _y = _smY = first.Y;
        _p = p;
        _lastRaw = first with { Pressure = p };
        Dab(first.X, first.Y, p);
        _distanceToNextDab = Spacing(LastDabDiameter);
        return Flush();
    }

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

    /// <summary>Airbrush: keeps adding paint at the current position while time passes (call regularly while the pen rests).</summary>
    public PixelRect Spray(double elapsedMs)
    {
        if (!_active || !_brush.Spray || elapsedMs <= 0) return PixelRect.Empty;
        _sprayMs += elapsedMs;
        while (_sprayMs >= SprayIntervalMs)
        {
            _sprayMs -= SprayIntervalMs;
            Dab(_x, _y, _p);
        }
        return Flush();
    }

    /// <summary>Finishes the stroke and records it as one undo step (only if it changed any pixel).</summary>
    public PixelRect End()
    {
        if (!_active) return PixelRect.Empty;
        if (_brush.Smoothing > 0 && (_x != _lastRaw.X || _y != _lastRaw.Y))
            MoveTo(_lastRaw.X, _lastRaw.Y, _lastRaw.Pressure);
        if (_distanceSinceDab > 0.01 && !_brush.BuildUp) Dab(_x, _y, _p);
        var changed = Flush();
        _edit!.Commit();
        Reset();
        return changed;
    }

    /// <summary>Abandons the stroke and restores the original pixels (nothing is added to the history).</summary>
    public PixelRect Cancel()
    {
        if (!_active) return PixelRect.Empty;
        var area = StrokeBounds;
        _edit!.Cancel();
        Reset();
        return area;
    }

    // ------------------------------------------------------------------ path

    private void Accept(StrokeSample s)
    {
        if (!s.IsFinite) return;
        float p = PressureCurve.Sanitize(s.Pressure);
        if (s.X == _lastRaw.X && s.Y == _lastRaw.Y && p == _lastRaw.Pressure) return;
        _lastRaw = s with { Pressure = p };
        _sprayMs = 0;

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
    /// Distance between dabs for a diameter: at most 10 % of the (narrowest) diameter, and close enough that the
    /// scallops between neighbouring dabs stay below ~0.1 px.
    /// </summary>
    internal double Spacing(double diameter)
    {
        double d = diameter * _brush.Roundness;
        double r = Math.Max(d * 0.5, 0.5);
        double fall = EdgeWidth(r);
        double s = Math.Min(0.1 * d, Math.Sqrt(0.8 * r * fall));
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
        float mapped = _style.Curve.Map(pressure);
        if (_brush.BuildUp) alpha *= (float)_brush.Flow;

        double angle = _brush.Angle;
        Stamp(cx, cy, diameter, alpha, mapped, angle);
        int w = _doc.Width, h = _doc.Height;
        switch (_style.Symmetry)
        {
            case Symmetry.Vertical:
                Stamp(w - cx, cy, diameter, alpha, mapped, -angle);
                break;
            case Symmetry.Horizontal:
                Stamp(cx, h - cy, diameter, alpha, mapped, -angle);
                break;
            case Symmetry.Both:
                Stamp(w - cx, cy, diameter, alpha, mapped, -angle);
                Stamp(cx, h - cy, diameter, alpha, mapped, -angle);
                Stamp(w - cx, h - cy, diameter, alpha, mapped, angle);
                break;
        }
    }

    private void Stamp(double cx, double cy, double diameter, float alpha, float mappedPressure, double angleDeg)
    {
        double r = diameter * 0.5;
        if (r < 0.5)
        {
            alpha *= (float)(4 * r * r);
            r = 0.5;
        }
        if (alpha <= 0) return;

        double roundness = _brush.Roundness;
        bool elliptic = roundness < 0.999;
        double cosA = 1, sinA = 0;
        if (elliptic)
        {
            double a = angleDeg * Math.PI / 180;
            cosA = Math.Cos(a);
            sinA = Math.Sin(a);
        }
        double invRound = 1 / roundness;

        double fall = EdgeWidth(r);
        bool linear = fall <= 1.0001;
        double outer = r + 0.5;
        double inner = outer - fall;
        double outer2 = outer * outer;
        double inner2 = inner > 0 ? inner * inner : -1;
        bool grain = _brush.Grain > 0;
        float grainAmount = (float)_brush.Grain;
        float level = 0.35f + 0.65f * mappedPressure;
        float g0 = 1 - level - 0.18f, g1 = 1 - level + 0.18f;
        bool buildUp = _brush.BuildUp;

        var box = PixelRect.FromBounds(cx - outer, cy - outer, cx + outer, cy + outer).Intersect(_doc.Bounds);
        if (box.IsEmpty) return;

        int tx0 = box.X / T, ty0 = box.Y / T, tx1 = (box.Right - 1) / T, ty1 = (box.Bottom - 1) / T;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                _edit!.BeforeTile(tx, ty);
                var m = MaskTile(_edit.TileKey(tx, ty));
                var part = box.Intersect(new PixelRect(tx * T, ty * T, T, T));
                for (int y = part.Y; y < part.Bottom; y++)
                {
                    double fy = y + 0.5 - cy;
                    int row = (y - ty * T) * T - tx * T;
                    for (int x = part.X; x < part.Right; x++)
                    {
                        double fx = x + 0.5 - cx;
                        double ux = fx, uy = fy;
                        if (elliptic)
                        {
                            ux = fx * cosA + fy * sinA;
                            uy = (-fx * sinA + fy * cosA) * invRound;
                        }
                        double d2 = ux * ux + uy * uy;
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
                        if (grain)
                        {
                            float g = PaperGrain.Sample(x, y, _brush.GrainScale);
                            float f = g <= g0 ? 0 : g >= g1 ? 1 : (g - g0) / (g1 - g0);
                            cov *= 1 - grainAmount * (1 - f);
                            if (cov <= 0) continue;
                        }
                        ref float slot = ref m[row + x];
                        if (buildUp) slot += (1 - slot) * cov;
                        else if (cov > slot) slot = cov;
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
        if (area.IsEmpty || _edit is null) return PixelRect.Empty;

        var layer = _edit.Layer;
        var pixels = layer.Image.Pixels;
        int w = _doc.Width;
        float opacity = (float)_brush.Opacity * 255f;
        bool erase = _style.Mode == PaintMode.Erase;
        bool alphaLock = layer.AlphaLocked;
        var selection = _doc.Selection;
        var color = _style.Color;

        int tx0 = area.X / T, ty0 = area.Y / T, tx1 = (area.Right - 1) / T, ty1 = (area.Bottom - 1) / T;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int key = _edit.TileKey(tx, ty);
                if (!_mask.TryGetValue(key, out var m)) continue;
                var before = _edit.BeforeTile(tx, ty);
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
                            pixels[dst + x] = baseline;
                            continue;
                        }
                        float c = cov * opacity;
                        if (selection is not null) c = c * selection.Data[dst + x] / 255f;
                        int a = (int)(c + 0.5f);
                        if (a <= 0)
                        {
                            pixels[dst + x] = baseline;
                            continue;
                        }
                        if (a > 255) a = 255;
                        if (erase) pixels[dst + x] = alphaLock ? baseline : Blending.Fade(baseline, 255 - a);
                        else if (alphaLock) pixels[dst + x] = Tint(baseline, color, a);
                        else pixels[dst + x] = Pixel.Over(_paintLut[a], baseline);
                    }
                }
            }
        }
        _edit.Changed(area);
        return area;
    }

    /// <summary>Paints colour into existing pixels without changing their transparency (transparency lock).</summary>
    private static uint Tint(uint baseline, ColorRgb c, int a)
    {
        byte ba = Pixel.A(baseline);
        if (ba == 0) return baseline;
        var (r, g, b, _) = Pixel.ToStraight(baseline);
        int nr = r + (c.R - r) * a / 255, ng = g + (c.G - g) * a / 255, nb = b + (c.B - b) * a / 255;
        return Pixel.FromStraight((byte)nr, (byte)ng, (byte)nb, ba);
    }

    private float[] MaskTile(int key)
    {
        if (!_mask.TryGetValue(key, out var tile))
        {
            tile = _maskPool.Count > 0 ? _maskPool.Pop() : new float[T * T];
            _mask[key] = tile;
        }
        return tile;
    }

    private void Reset()
    {
        foreach (var tile in _mask.Values)
        {
            Array.Clear(tile);
            if (_maskPool.Count < 256) _maskPool.Push(tile);
        }
        _mask.Clear();
        _edit = null;
        _pending = PixelRect.Empty;
        _active = false;
    }
}
