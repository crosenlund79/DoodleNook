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
/// Mission 007 (Brush Laboratory) adds image tips (tinted, or in their own colours for stamp brushes, which are
/// layered dab over dab in a separate colour buffer), custom spacing, rotation that follows the stroke, scatter and
/// size/rotation/opacity jitter from a seeded generator (the same stroke always gives the same marks), a per-brush
/// pressure curve, pressure-controlled flow, textures from the library and watercolour-like wet edges. Brushes that
/// use none of these take exactly the Mission 006 path (a test compares them with a frozen copy of that engine).
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
    private readonly Dictionary<int, float[]> _color = new();
    private readonly Stack<float[]> _colorPool = new();

    private BrushTip? _tip;
    private BrushTexture? _texture;
    private bool _colorTip;
    private bool _extended;
    private BrushRandom _rng;
    private double _dirDeg;
    private double _texCos = 1, _texSin;

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
        _tip = _brush.TipId is not null ? style.Resources?.Tip : null;
        _texture = style.Resources?.Texture;
        _colorTip = _tip is { HasColor: true } && _brush.TipColors && style.Mode == PaintMode.Paint;
        _extended = _brush.UsesExtendedDynamics;
        _rng = new BrushRandom(Seed(first));
        _dirDeg = 0;
        double tr = _brush.TextureRotation * Math.PI / 180;
        _texCos = Math.Cos(tr);
        _texSin = Math.Sin(tr);
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
        if (len >= 1e-9 && _brush.FollowDirection) _dirDeg = Math.Atan2(dy, dx) * 180 / Math.PI;
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
        if (_brush.Spacing > 0) return Math.Max(MinSpacing, _brush.Spacing * diameter);
        if (_tip is not null) return Math.Max(MinSpacing, 0.1 * diameter);
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
        if (!_extended)
        {
            Mirrored(cx, cy, diameter, alpha, mapped, angle);
            return;
        }

        // Brush Laboratory dynamics.
        mapped = _brush.MapPressure(pressure, _style.Curve);
        if (_brush.BuildUp && _brush.PressureFlow) alpha *= Math.Max(0.05f, mapped);
        if (_brush.FollowDirection) angle += _dirDeg;
        for (int i = 0; i < _brush.ScatterCount; i++)
        {
            double x = cx, y = cy, d = diameter, a = angle;
            float al = alpha;
            if (_brush.Scatter > 0)
            {
                double r = Math.Sqrt(_rng.Next()) * _brush.Scatter * diameter, t = _rng.Next() * 2 * Math.PI;
                x += r * Math.Cos(t);
                y += r * Math.Sin(t);
            }
            if (_brush.SizeJitter > 0) d *= 1 - _brush.SizeJitter * _rng.Next();
            if (_brush.RotationJitter > 0) a += (_rng.Next() * 2 - 1) * 180 * _brush.RotationJitter;
            if (_brush.OpacityJitter > 0) al *= (float)(1 - _brush.OpacityJitter * _rng.Next());
            Mirrored(x, y, d, al, mapped, a);
        }
    }

    private void Mirrored(double cx, double cy, double diameter, float alpha, float mapped, double angle)
    {
        Stamp(cx, cy, diameter, alpha, mapped, angle);
        if (_style.Symmetry == Symmetry.None) return;
        // Mirror lines through the origin (the canvas centre unless moved): x' = 2·ox − x.
        double ox = _style.Placement.OriginX(_doc.Width), oy = _style.Placement.OriginY(_doc.Height);
        double mx = 2 * ox - cx, my = 2 * oy - cy;
        switch (_style.Symmetry)
        {
            case Symmetry.Vertical:
                Stamp(mx, cy, diameter, alpha, mapped, -angle);
                break;
            case Symmetry.Horizontal:
                Stamp(cx, my, diameter, alpha, mapped, -angle);
                break;
            case Symmetry.Both:
                Stamp(mx, cy, diameter, alpha, mapped, -angle);
                Stamp(cx, my, diameter, alpha, mapped, -angle);
                Stamp(mx, my, diameter, alpha, mapped, angle);
                break;
            case Symmetry.Radial:
            {
                int n = _style.Placement.Count;
                double dx = cx - ox, dy = cy - oy;
                for (int k = 1; k < n; k++)
                {
                    double t = k * 2 * Math.PI / n, cos = Math.Cos(t), sin = Math.Sin(t);
                    Stamp(ox + dx * cos - dy * sin, oy + dx * sin + dy * cos, diameter, alpha, mapped, angle + k * 360.0 / n);
                }
                break;
            }
        }
    }

    private void Stamp(double cx, double cy, double diameter, float alpha, float mappedPressure, double angleDeg)
    {
        if (_tip is not null)
        {
            StampTip(cx, cy, diameter, alpha, mappedPressure, angleDeg);
            return;
        }
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
                            float g = GrainValue(x, y);
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

    /// <summary>The texture value at a pixel: the paper grain unless the brush names a texture; optionally rotated or inverted.</summary>
    private float GrainValue(int x, int y)
    {
        float g;
        if (_texture is not null) g = _texture.Sample(x, y, _brush.GrainScale, _texCos, _texSin);
        else if (_brush.TextureRotation == 0) g = PaperGrain.Sample(x, y, _brush.GrainScale);
        else g = PaperGrain.Sample((int)Math.Floor(x * _texCos + y * _texSin), (int)Math.Floor(-x * _texSin + y * _texCos), _brush.GrainScale);
        return _brush.InvertTexture ? 1 - g : g;
    }

    private static ulong Seed(StrokeSample s) =>
        (ulong)BitConverter.DoubleToInt64Bits(Math.Round(s.X, 2)) * 0x9E3779B97F4A7C15UL ^ (ulong)BitConverter.DoubleToInt64Bits(Math.Round(s.Y, 2));

    /// <summary>An image-tip dab: the tip is scaled so its longer side is the dab size, squashed by the roundness and
    /// rotated by the angle. Tinted tips raise the coverage mask; colour tips are laid over the stroke's colour buffer.</summary>
    private void StampTip(double cx, double cy, double diameter, float alpha, float mappedPressure, double angleDeg)
    {
        if (diameter < 1)
        {
            alpha *= (float)(diameter * diameter);
            diameter = 1;
        }
        if (alpha <= 0) return;
        var tip = _tip!;
        double maxDim = Math.Max(tip.Width, tip.Height);
        double s = diameter / maxDim; // canvas pixels per tip pixel
        var (lw, lh, la, lc, lscale) = tip.LevelFor(diameter);
        if (!_colorTip) lc = null;
        double roundness = _brush.Roundness;
        double halfW = tip.Width * s * 0.5, halfH = tip.Height * s * roundness * 0.5;
        double ext = Math.Sqrt(halfW * halfW + halfH * halfH) + 1;
        var box = PixelRect.FromBounds(cx - ext, cy - ext, cx + ext, cy + ext).Intersect(_doc.Bounds);
        if (box.IsEmpty) return;
        double rad = angleDeg * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        double inv = 1 / s, invRound = 1 / roundness;
        double ox = tip.Width * 0.5, oy = tip.Height * 0.5;
        bool grain = _brush.Grain > 0;
        float grainAmount = (float)_brush.Grain;
        float level = 0.35f + 0.65f * mappedPressure;
        float g0 = 1 - level - 0.18f, g1 = 1 - level + 0.18f;
        bool buildUp = _brush.BuildUp;

        int tx0 = box.X / T, ty0 = box.Y / T, tx1 = (box.Right - 1) / T, ty1 = (box.Bottom - 1) / T;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                _edit!.BeforeTile(tx, ty);
                int key = _edit.TileKey(tx, ty);
                var m = _colorTip ? null : MaskTile(key);
                var c = _colorTip ? ColorTile(key) : null;
                var part = box.Intersect(new PixelRect(tx * T, ty * T, T, T));
                for (int y = part.Y; y < part.Bottom; y++)
                {
                    double fy = y + 0.5 - cy;
                    int row = (y - ty * T) * T - tx * T;
                    for (int x = part.X; x < part.Right; x++)
                    {
                        double fx = x + 0.5 - cx;
                        double u = fx * cos + fy * sin, v = (-fx * sin + fy * cos) * invRound;
                        double px = (u * inv + ox) * lscale, py = (v * inv + oy) * lscale;
                        if (px < -1 || py < -1 || px > lw + 1 || py > lh + 1) continue;
                        float a = BrushTip.Sample(lw, lh, la, lc, px, py, out float r, out float g, out float b);
                        if (a <= 0.0005f) continue;
                        float k = alpha;
                        if (grain)
                        {
                            float gv = GrainValue(x, y);
                            float f = gv <= g0 ? 0 : gv >= g1 ? 1 : (gv - g0) / (g1 - g0);
                            k *= 1 - grainAmount * (1 - f);
                            if (k <= 0) continue;
                        }
                        if (c is not null)
                        {
                            int i = (row + x) * 4;
                            float sa = Math.Min(1f, a * k), keep = 1 - sa;
                            c[i] = r * k + c[i] * keep;
                            c[i + 1] = g * k + c[i + 1] * keep;
                            c[i + 2] = b * k + c[i + 2] * keep;
                            c[i + 3] = sa + c[i + 3] * keep;
                        }
                        else
                        {
                            float cov = Math.Min(1f, a * k);
                            ref float slot = ref m![row + x];
                            if (buildUp) slot += (1 - slot) * cov;
                            else if (cov > slot) slot = cov;
                        }
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
        if (_colorTip) return FlushColor(area);
        float wet = (float)_brush.WetEdges;

        var layer = _edit.Layer;
        var pixels = _edit.Target.Pixels;
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
                        if (wet > 0) cov = WetEdge(cov, wet);
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

    /// <summary>Watercolour-like rim: the inside of a stroke is lighter and paint collects where coverage falls off.</summary>
    private static float WetEdge(float cov, float wet) =>
        Math.Clamp(cov * (1 - 0.6f * wet) + wet * 2.4f * cov * cov * (1 - cov), 0f, 1f);

    /// <summary>Composites a colour-tip stroke (stamp brush): the stroke's own colours over the original pixels.</summary>
    private PixelRect FlushColor(PixelRect area)
    {
        var layer = _edit!.Layer;
        var pixels = _edit.Target.Pixels;
        int w = _doc.Width;
        float opacity = (float)_brush.Opacity;
        bool alphaLock = layer.AlphaLocked;
        var selection = _doc.Selection;
        int tx0 = area.X / T, ty0 = area.Y / T, tx1 = (area.Right - 1) / T, ty1 = (area.Bottom - 1) / T;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int key = _edit.TileKey(tx, ty);
                if (!_color.TryGetValue(key, out var ct)) continue;
                var before = _edit.BeforeTile(tx, ty);
                var part = area.Intersect(new PixelRect(tx * T, ty * T, T, T));
                for (int y = part.Y; y < part.Bottom; y++)
                {
                    int local = (y - ty * T) * T - tx * T;
                    int dst = y * w;
                    for (int x = part.X; x < part.Right; x++)
                    {
                        int ci = (local + x) * 4;
                        uint baseline = before[local + x];
                        float k = opacity;
                        if (selection is not null) k = k * selection.Data[dst + x] / 255f;
                        int a = (int)(ct[ci + 3] * k * 255 + 0.5f);
                        if (a <= 0)
                        {
                            pixels[dst + x] = baseline;
                            continue;
                        }
                        if (a > 255) a = 255;
                        byte r = (byte)Math.Min(a, (int)(ct[ci] * k * 255 + 0.5f));
                        byte g = (byte)Math.Min(a, (int)(ct[ci + 1] * k * 255 + 0.5f));
                        byte b = (byte)Math.Min(a, (int)(ct[ci + 2] * k * 255 + 0.5f));
                        uint over = Pixel.Over(Pixel.Pack((byte)a, r, g, b), baseline);
                        if (alphaLock)
                        {
                            byte ba = Pixel.A(baseline);
                            if (ba == 0) over = baseline;
                            else
                            {
                                var (sr, sg, sb, _) = Pixel.ToStraight(over);
                                over = Pixel.FromStraight(sr, sg, sb, ba);
                            }
                        }
                        pixels[dst + x] = over;
                    }
                }
            }
        }
        _edit.Changed(area);
        return area;
    }

    private float[] ColorTile(int key)
    {
        if (!_color.TryGetValue(key, out var tile))
        {
            tile = _colorPool.Count > 0 ? _colorPool.Pop() : new float[T * T * 4];
            _color[key] = tile;
        }
        return tile;
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
        foreach (var tile in _color.Values)
        {
            Array.Clear(tile);
            if (_colorPool.Count < 64) _colorPool.Push(tile);
        }
        _color.Clear();
        _edit = null;
        _pending = PixelRect.Empty;
        _active = false;
    }
}
