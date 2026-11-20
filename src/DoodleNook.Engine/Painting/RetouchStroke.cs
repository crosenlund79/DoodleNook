using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Painting;

/// <summary>The three retouching brushes of Mission 010.</summary>
public enum RetouchKind
{
    /// <summary>Picks up the paint under the brush and carries it along the stroke.</summary>
    Smudge,

    /// <summary>Copies pixels from a source point that keeps the same offset from the brush.</summary>
    Clone,

    /// <summary>Like Clone, but the copy takes on the colour and brightness around the repaired spot; without a source it
    /// fills the spot from its surroundings.</summary>
    Repair,
}

/// <summary>How a retouching stroke works.</summary>
/// <param name="Size">Brush diameter in pixels.</param>
/// <param name="Strength">Smudge: how far paint is carried (0–1). Clone and Repair: opacity (0–1).</param>
/// <param name="Hardness">0 is a soft edge, 1 a hard one.</param>
/// <param name="PressureSize">Pen pressure changes the size.</param>
/// <param name="PressureStrength">Pen pressure changes the strength (a mouse always gives full pressure).</param>
/// <param name="FingerPaint">Smudge only: the smear starts with this colour instead of the paint under the brush.</param>
/// <param name="Source">Clone and Repair: the source is at (x + dx, y + dy), in whole pixels (null: Repair fills from the
/// surroundings; Clone needs one).</param>
/// <param name="Sampled">Clone and Repair: copy from this picture (what is seen, all layers) instead of the layer itself.</param>
public sealed record RetouchStyle(RetouchKind Kind, double Size, double Strength, double Hardness, PressureCurve Curve, bool PressureSize = false,
    bool PressureStrength = true, ColorRgb? FingerPaint = null, (int Dx, int Dy)? Source = null, RasterImage? Sampled = null);

/// <summary>
/// Smudge, Clone and Repair strokes on a layer (or its mask, or the current drawing of an animation), with the same
/// path handling as painting: dabs are placed at a fixed spacing along the path, so the result depends on the gesture,
/// not on how many input events arrive. Pixels go through a <see cref="LayerEdit"/>, so a stroke is one undo step,
/// locked layers are refused, a palette-locked picture keeps only its palette colours, and selections limit the effect.
/// <list type="bullet">
/// <item><b>Smudge</b> keeps a small "carried paint" buffer the size of the brush: each dab mixes the canvas toward the
/// carried paint and the carried paint toward the canvas. Strength is how much is carried; finger painting starts the
/// buffer with the foreground colour.</item>
/// <item><b>Clone</b> raises a per-stroke coverage mask (as painting does, so crossings don't build up) and lays the source
/// pixels, as they were before the stroke, over the original pixels. Whole-pixel offsets: no resampling blur.</item>
/// <item><b>Repair</b> uses the same mask. The colour around the masked area is filled in smoothly (a pull-push
/// interpolation, from the pixels outside the stroke); with a source, the source's own detail (its difference from the
/// same smooth fill of the source) is added on top, so the copy fits its new surroundings.</item>
/// </list>
/// Transparency: transparency-locked layers keep their alpha; otherwise alpha is carried, copied or filled like colour.
/// </summary>
public sealed class RetouchStroke
{
    private const int T = Document.TileSize;
    private readonly Document _doc;
    private readonly Dictionary<int, float[]> _mask = new();
    private LayerEdit? _edit;
    private RetouchStyle _style = null!;
    private bool _active;
    private double _x, _y;
    private float _p;
    private double _toNext;
    private PixelRect _pending = PixelRect.Empty;

    // Smudge: carried paint (premultiplied, 0–1) in a square around the dab centre.
    private float[] _carry = [];
    private int _carrySize;
    private bool _carryReady;

    public RetouchStroke(Document document) => _doc = document;

    public bool IsActive => _active;
    public int DabCount { get; private set; }
    public PixelRect StrokeBounds { get; private set; } = PixelRect.Empty;
    public RetouchKind Kind => _style.Kind;

    /// <summary>Starts a stroke on the active layer (or <paramref name="layer"/>). Returns the changed area. Nothing happens
    /// on a locked layer, a layer without pixels, or a Clone without a source.</summary>
    public PixelRect Begin(RetouchStyle style, StrokeSample first, Layer? layer = null)
    {
        if (_active) End();
        layer ??= _doc.ActiveLayer;
        if (!first.IsFinite || layer.Locked || (!layer.IsPaintable && !_doc.EditsMask(layer))) return PixelRect.Empty;
        if (style.Kind == RetouchKind.Clone && style.Source is null) return PixelRect.Empty;
        _style = style with { Size = Math.Clamp(style.Size, 1, BrushSettings.MaxSize), Strength = Math.Clamp(style.Strength, 0, 1), Hardness = Math.Clamp(style.Hardness, 0, 1) };
        _edit = _doc.BeginEdit(layer, style.Kind switch { RetouchKind.Smudge => "Smudge", RetouchKind.Clone => "Clone", _ => "Repair" });
        _active = true;
        DabCount = 0;
        StrokeBounds = PixelRect.Empty;
        _pending = PixelRect.Empty;
        _carryReady = false;
        float p = PressureCurve.Sanitize(first.Pressure);
        _x = first.X;
        _y = first.Y;
        _p = p;
        Dab(first.X, first.Y, p);
        _toNext = Spacing(Diameter(p));
        return Flush(final: false);
    }

    public PixelRect Add(StrokeSample sample) => Add([sample]);

    public PixelRect Add(ReadOnlySpan<StrokeSample> samples)
    {
        if (!_active) return PixelRect.Empty;
        foreach (var s in samples)
        {
            if (!s.IsFinite) continue;
            MoveTo(s.X, s.Y, PressureCurve.Sanitize(s.Pressure));
        }
        return Flush(final: false);
    }

    /// <summary>Finishes the stroke as one undo step (only if a pixel changed).</summary>
    public PixelRect End()
    {
        if (!_active) return PixelRect.Empty;
        var changed = Flush(final: true);
        _edit!.Commit();
        Reset();
        return changed;
    }

    /// <summary>Abandons the stroke: the original pixels come back and nothing is recorded.</summary>
    public PixelRect Cancel()
    {
        if (!_active) return PixelRect.Empty;
        var area = StrokeBounds;
        _edit!.Cancel();
        Reset();
        return area;
    }

    private void Reset()
    {
        _mask.Clear();
        _edit = null;
        _active = false;
        _pending = PixelRect.Empty;
        _carry = [];
    }

    // ------------------------------------------------------------------ path

    private void MoveTo(double x, double y, float p)
    {
        double dx = x - _x, dy = y - _y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9)
        {
            _p = p;
            return;
        }
        double t = _toNext;
        while (t <= len)
        {
            double u = t / len;
            float pp = _p + (p - _p) * (float)u;
            Dab(_x + dx * u, _y + dy * u, pp);
            t += Spacing(Diameter(pp));
        }
        _toNext = t - len;
        _x = x;
        _y = y;
        _p = p;
    }

    /// <summary>Distance between dabs: a fixed share of the diameter, so a stroke looks the same at any input rate.</summary>
    private double Spacing(double diameter) => _style.Kind == RetouchKind.Smudge ? SmudgeSpacing(diameter) : Math.Max(0.5, 0.1 * diameter);

    // Smudge dabs are close together (no visible steps in long smears); each mixes less, so the strength per distance
    // travelled is that of one dab every 12 % of the diameter, whatever the spacing.
    private static double SmudgeSpacing(double diameter) => Math.Max(0.75, 0.035 * diameter);

    private static double SmudgeShare(double diameter) => Math.Min(1, SmudgeSpacing(diameter) / Math.Max(1, 0.12 * diameter));

    private double Diameter(float p) => Math.Max(1, _style.Size * (_style.PressureSize ? 0.15 + 0.85 * _style.Curve.Map(p) : 1));

    private float StrengthAt(float p) => (float)(_style.Strength * (_style.PressureStrength ? _style.Curve.Map(p) : 1));

    /// <summary>Coverage of a round dab at distance² <paramref name="d2"/> from its centre (soft or hard edge), 0–1.</summary>
    private static float Coverage(double d2, double outer, double inner2, double fall)
    {
        if (d2 >= outer * outer) return 0;
        if (d2 <= inner2) return 1;
        double t = (outer - Math.Sqrt(d2)) / fall;
        if (t >= 1) return 1;
        return (float)(t * t * (3 - 2 * t));
    }

    private void Dab(double cx, double cy, float p)
    {
        DabCount++;
        double d = Diameter(p), r = d / 2, outer = r + 0.5;
        double fall = Math.Max(1.0, r * (1 - _style.Hardness));
        double inner = outer - fall, inner2 = inner > 0 ? inner * inner : -1;
        var box = PixelRect.FromBounds(cx - outer, cy - outer, cx + outer, cy + outer).Intersect(_doc.Bounds);
        if (box.IsEmpty) return;
        _edit!.Capture(box);
        if (_style.Kind == RetouchKind.Smudge) SmudgeDab(cx, cy, d, outer, inner2, fall, StrengthAt(p), box);
        else
        {
            float alpha = StrengthAt(p);
            for (int y = box.Y; y < box.Bottom; y++)
            {
                double fy = y + 0.5 - cy;
                for (int x = box.X; x < box.Right; x++)
                {
                    double fx = x + 0.5 - cx;
                    float cov = Coverage(fx * fx + fy * fy, outer, inner2, fall) * alpha;
                    if (cov <= 0) continue;
                    var m = MaskTile(x / T, y / T);
                    ref float slot = ref m[(y % T) * T + x % T];
                    if (cov > slot) slot = cov;
                }
            }
        }
        _pending = _pending.Union(box);
        StrokeBounds = StrokeBounds.Union(box);
    }

    private float[] MaskTile(int tx, int ty)
    {
        int key = ty * _doc.TilesX + tx;
        if (!_mask.TryGetValue(key, out var tile)) _mask[key] = tile = new float[T * T];
        return tile;
    }

    private float MaskAt(int x, int y)
    {
        if (!_doc.Bounds.Contains(x, y)) return 0;
        return _mask.TryGetValue(y / T * _doc.TilesX + x / T, out var tile) ? tile[(y % T) * T + x % T] : 0;
    }

    private float SelectionAt(int x, int y) => _doc.Selection is { } sel ? sel[x, y] / 255f : 1f;

    // ------------------------------------------------------------------ smudge

    private void SmudgeDab(double cx, double cy, double d, double outer, double inner2, double fall, float strength, PixelRect box)
    {
        int size = (int)Math.Ceiling(2 * outer) + 2;
        int ox = (int)Math.Floor(cx) - size / 2, oy = (int)Math.Floor(cy) - size / 2;
        if (!_carryReady || size != _carrySize)
        {
            var old = _carry;
            int oldSize = _carrySize;
            _carry = new float[size * size * 4];
            _carrySize = size;
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    int k = (j * size + i) * 4;
                    if (_carryReady && oldSize > 0)
                    {
                        // Pressure changed the size: keep what was carried, centred.
                        int oi = i - size / 2 + oldSize / 2, oj = j - size / 2 + oldSize / 2;
                        if ((uint)oi < (uint)oldSize && (uint)oj < (uint)oldSize)
                        {
                            Array.Copy(old, (oj * oldSize + oi) * 4, _carry, k, 4);
                            continue;
                        }
                    }
                    uint px = _style.FingerPaint is { } c ? Pixel.Opaque(c) : _edit!.Target.GetOrTransparent(ox + i, oy + j);
                    Unpack(px, _carry, k);
                }
            _carryReady = true;
        }
        var pixels = _edit!.Target.Pixels;
        int w = _doc.Width;
        bool alphaLock = _edit.AlphaLocked;
        float share = (float)SmudgeShare(d); // this dab's share of the reference step
        for (int y = box.Y; y < box.Bottom; y++)
        {
            double fy = y + 0.5 - cy;
            int j = y - oy;
            if ((uint)j >= (uint)size) continue;
            for (int x = box.X; x < box.Right; x++)
            {
                double fx = x + 0.5 - cx;
                int i = x - ox;
                if ((uint)i >= (uint)size) continue;
                float cov = Coverage(fx * fx + fy * fy, outer, inner2, fall) * SelectionAt(x, y);
                if (cov <= 0) continue;
                int k = (j * size + i) * 4, idx = y * w + x;
                uint before = pixels[idx];
                float r0 = Pixel.R(before) / 255f, g0 = Pixel.G(before) / 255f, b0 = Pixel.B(before) / 255f, a0 = Pixel.A(before) / 255f;
                // Per reference step the canvas moves cov·strength toward the carried paint, and the carried paint picks up
                // cov·(1 − strength) of the canvas; split evenly over the dabs in that step.
                float mix = 1 - MathF.Pow(1 - cov * strength, share);
                float r = r0 + (_carry[k] - r0) * mix, g = g0 + (_carry[k + 1] - g0) * mix,
                    b = b0 + (_carry[k + 2] - b0) * mix, a = a0 + (_carry[k + 3] - a0) * mix;
                pixels[idx] = alphaLock ? KeepAlpha(Pack(r, g, b, a), before) : Pack(r, g, b, a);
                // The carried paint picks up some of what it passes over.
                float pick = 1 - MathF.Pow(1 - cov * (1 - strength), share);
                _carry[k] += (r0 - _carry[k]) * pick;
                _carry[k + 1] += (g0 - _carry[k + 1]) * pick;
                _carry[k + 2] += (b0 - _carry[k + 2]) * pick;
                _carry[k + 3] += (a0 - _carry[k + 3]) * pick;
            }
        }
    }

    // ------------------------------------------------------------------ flush (clone, repair)

    private PixelRect Flush(bool final)
    {
        var area = _pending;
        _pending = PixelRect.Empty;
        if (_edit is null) return PixelRect.Empty;
        if (_style.Kind == RetouchKind.Smudge)
        {
            if (!area.IsEmpty) _edit.Changed(area);
            return area;
        }
        if (_style.Kind == RetouchKind.Repair)
        {
            // Repair looks at the stroke's surroundings: redo the area around the new dabs (and all of it at the end).
            double margin = Math.Max(8, _style.Size);
            // At the end the whole stroke is healed once more, so early parts see the final surroundings; long strokes keep
            // their step-by-step result (each step already looked a brush size around itself).
            var region = final && (long)StrokeBounds.Width * StrokeBounds.Height <= 512L * 512 ? StrokeBounds : area;
            if (region.IsEmpty) return PixelRect.Empty;
            region = PixelRect.FromBounds(region.X - margin, region.Y - margin, region.Right + margin, region.Bottom + margin).Intersect(_doc.Bounds);
            Heal(region);
            _edit.Changed(region);
            return region;
        }
        if (area.IsEmpty) return area;
        CloneArea(area);
        _edit.Changed(area);
        return area;
    }

    private uint SourcePixel(int x, int y)
    {
        var (dx, dy) = _style.Source!.Value;
        int sx = x + dx, sy = y + dy;
        if (_style.Sampled is { } img) return img.GetOrTransparent(sx, sy);
        return _edit!.BeforePixel(sx, sy); // the layer as it was before this stroke (no feedback loops)
    }

    private void CloneArea(PixelRect area)
    {
        var pixels = _edit!.Target.Pixels;
        int w = _doc.Width;
        bool alphaLock = _edit.AlphaLocked;
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                float cov = MaskAt(x, y);
                int idx = y * w + x;
                uint before = _edit.BeforePixel(x, y);
                if (cov <= 0)
                {
                    pixels[idx] = before;
                    continue;
                }
                int a = (int)(cov * SelectionAt(x, y) * 255 + 0.5f);
                uint over = Pixel.Over(Blending.Fade(SourcePixel(x, y), a), before);
                pixels[idx] = alphaLock ? KeepAlpha(over, before) : over;
            }
    }

    private void Heal(PixelRect region)
    {
        int rw = region.Width, rh = region.Height, n = rw * rh;
        bool withSource = _style.Source is not null;
        var weight = new float[n];
        var dst = new float[n * 4];
        var src = withSource ? new float[n * 4] : null;
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int gx = region.X + x, gy = region.Y + y, i = y * rw + x;
                weight[i] = 1 - MaskAt(gx, gy);
                Unpack(_edit!.BeforePixel(gx, gy), dst, i * 4);
                if (src is not null) Unpack(SourcePixel(gx, gy), src, i * 4);
            }
        var smoothDst = PullPush(dst, weight, rw, rh);
        var smoothSrc = src is null ? null : PullPush(src, weight, rw, rh);
        var pixels = _edit!.Target.Pixels;
        bool alphaLock = _edit.AlphaLocked;
        int w = _doc.Width;
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int gx = region.X + x, gy = region.Y + y, i = y * rw + x, k = i * 4;
                float m = 1 - weight[i];
                if (m <= 0) continue;
                float amount = m * SelectionAt(gx, gy);
                float Healed(int ch)
                {
                    float healed = smoothDst[k + ch];
                    if (src is not null) healed += src[k + ch] - smoothSrc![k + ch];
                    return dst[k + ch] + (Math.Clamp(healed, 0, 1) - dst[k + ch]) * amount;
                }
                float ca = Healed(3);
                uint before = _edit.BeforePixel(gx, gy);
                uint result = Pack(Math.Min(Healed(0), ca), Math.Min(Healed(1), ca), Math.Min(Healed(2), ca), ca);
                pixels[gy * w + gx] = alphaLock ? KeepAlpha(result, before) : result;
            }
    }

    /// <summary>
    /// Fills low-weight pixels smoothly from the weighted ones (pull-push): the values are averaged down a pyramid of
    /// halved images by weight, then the gaps are filled back up from the coarser levels. Pixels with weight 1 keep their
    /// value; weight 0 means "unknown, fill me". Channels are interleaved by 4.
    /// </summary>
    internal static float[] PullPush(float[] values, float[] weights, int width, int height)
    {
        var levels = new List<(float[] V, float[] W, int Wd, int Ht)> { ((float[])values.Clone(), (float[])weights.Clone(), width, height) };
        while (levels[^1].Wd > 1 || levels[^1].Ht > 1)
        {
            var (v, wt, wd, ht) = levels[^1];
            int nw = (wd + 1) / 2, nh = (ht + 1) / 2;
            var nv = new float[nw * nh * 4];
            var nwt = new float[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    float sw = 0, r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < 2; j++)
                        for (int i = 0; i < 2; i++)
                        {
                            int sx = 2 * x + i, sy = 2 * y + j;
                            if (sx >= wd || sy >= ht) continue;
                            int s = sy * wd + sx;
                            float q = wt[s];
                            if (q <= 0) continue;
                            sw += q;
                            r += v[s * 4] * q;
                            g += v[s * 4 + 1] * q;
                            b += v[s * 4 + 2] * q;
                            a += v[s * 4 + 3] * q;
                        }
                    int o = y * nw + x;
                    if (sw > 0)
                    {
                        nv[o * 4] = r / sw;
                        nv[o * 4 + 1] = g / sw;
                        nv[o * 4 + 2] = b / sw;
                        nv[o * 4 + 3] = a / sw;
                    }
                    nwt[o] = Math.Min(1, sw);
                }
            levels.Add((nv, nwt, nw, nh));
        }
        for (int l = levels.Count - 2; l >= 0; l--)
        {
            var (v, wt, wd, ht) = levels[l];
            var (cv, _, cw, ch) = levels[l + 1];
            for (int y = 0; y < ht; y++)
                for (int x = 0; x < wd; x++)
                {
                    int o = y * wd + x;
                    float q = wt[o];
                    if (q >= 1) continue;
                    // Bilinear from the coarser level (its pixel centres sit at 2x + 0.5 in this level).
                    double fx = Math.Clamp((x - 0.5) / 2, 0, cw - 1), fy = Math.Clamp((y - 0.5) / 2, 0, ch - 1);
                    int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, cw - 1), y1 = Math.Min(y0 + 1, ch - 1);
                    float ax = (float)(fx - x0), ay = (float)(fy - y0);
                    for (int c = 0; c < 4; c++)
                    {
                        float top = cv[(y0 * cw + x0) * 4 + c] * (1 - ax) + cv[(y0 * cw + x1) * 4 + c] * ax;
                        float bottom = cv[(y1 * cw + x0) * 4 + c] * (1 - ax) + cv[(y1 * cw + x1) * 4 + c] * ax;
                        float up = top * (1 - ay) + bottom * ay;
                        v[o * 4 + c] = v[o * 4 + c] * q + up * (1 - q);
                    }
                    wt[o] = 1;
                }
        }
        return levels[0].V;
    }

    // ------------------------------------------------------------------ pixels

    private static void Unpack(uint p, float[] into, int k)
    {
        into[k] = Pixel.R(p) / 255f;
        into[k + 1] = Pixel.G(p) / 255f;
        into[k + 2] = Pixel.B(p) / 255f;
        into[k + 3] = Pixel.A(p) / 255f;
    }

    private static uint Pack(float r, float g, float b, float a)
    {
        byte A = (byte)Math.Clamp((int)(a * 255 + 0.5f), 0, 255);
        if (A == 0) return 0;
        static byte C(float v, byte max) => (byte)Math.Clamp((int)(v * 255 + 0.5f), 0, max);
        return Pixel.Pack(A, C(r, A), C(g, A), C(b, A));
    }

    /// <summary>Transparency lock: the new colour with the old pixel's alpha (a transparent pixel stays transparent).</summary>
    private static uint KeepAlpha(uint result, uint before)
    {
        byte a = Pixel.A(before);
        if (a == 0) return before;
        if (Pixel.A(result) == 0) return before;
        var (r, g, b, _) = Pixel.ToStraight(result);
        return Pixel.FromStraight(r, g, b, a);
    }
}
