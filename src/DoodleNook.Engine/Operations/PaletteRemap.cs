using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>How the automatic suggestion chooses colours.</summary>
public enum RemapStyle
{
    /// <summary>Each colour goes to the palette colour that looks most alike (OKLab); colours already in the palette stay.</summary>
    Closest,

    /// <summary>The picture's range from dark to light is stretched over the palette's, then the closest colour is taken:
    /// keeps contrast when the palette is lighter, darker or wider than the picture.</summary>
    LightAndDark,
}

/// <summary>One row of the mapping editor: a colour of the picture, or a group of similar colours.</summary>
public sealed class RemapSource
{
    internal RemapSource(ColorRgb colour, long weight, int[] members)
    {
        Colour = colour;
        Weight = weight;
        Members = members;
    }

    /// <summary>The colour shown for the row (for a group, its most used colour).</summary>
    public ColorRgb Colour { get; }

    /// <summary>How much of the picture uses it (pixels weighted by their opacity).</summary>
    public long Weight { get; }

    /// <summary>The exact colours (0xRRGGBB) the row stands for.</summary>
    public IReadOnlyList<int> Members { get; }

    public bool IsGroup => Members.Count > 1;
}

/// <summary>
/// Remap to Palette: proposes, lets the artist change, previews and applies a mapping from the picture's colours to a
/// palette. Only RGB changes: every pixel keeps its exact alpha, layers keep their order, opacity and blend modes, and an
/// animation keeps its frames, timing and held drawings.
/// <para>
/// Pictures with few colours (pixel art, flat colours) get one row per exact colour. Pictures with many (paintings,
/// photos, soft edges) get rows for groups of similar colours (weighted k-means in OKLab): each colour inside a group
/// still gets its own closest suggestion, and choosing a target for a group sends all of its colours there.
/// </para>
/// </summary>
public sealed class PaletteRemap
{
    /// <summary>Pictures with at most this many distinct colours get one row per colour.</summary>
    public const int MaxExactRows = 48;

    /// <summary>Otherwise the colours are summarised in this many groups.</summary>
    public const int GroupRows = 24;

    private readonly ColorRgb[] _target;
    private readonly OkLab[] _targetLab;
    private readonly Dictionary<int, long> _weights;
    private Dictionary<int, int> _auto = new();
    private Dictionary<int, int> _override = new();
    private readonly Dictionary<int, int> _rowOf = new();
    private ulong[] _pixelCache = new ulong[1 << 16];
    private RemapStyle _style;
    private SourceResolver? _resolver;

    private PaletteRemap(IReadOnlyList<ColorRgb> target, Dictionary<int, long> weights, List<RemapSource> sources, bool grouped, PaletteConformer? sourcePalette)
    {
        _target = target.ToArray();
        _targetLab = _target.Select(OkLab.From).ToArray();
        _weights = weights;
        Sources = sources;
        Grouped = grouped;
        SourcePalette = sourcePalette;
        for (int i = 0; i < sources.Count; i++)
            foreach (int m in sources[i].Members) _rowOf[m] = i;
        Suggest();
    }

    public IReadOnlyList<ColorRgb> Target => _target;
    public IReadOnlyList<RemapSource> Sources { get; }

    /// <summary>How many distinct colours the picture has.</summary>
    public int DistinctColours => _weights.Count;

    /// <summary>True when rows are groups of similar colours rather than single colours.</summary>
    public bool Grouped { get; }

    /// <summary>The locked palette the picture had (its colours are read exactly), or null.</summary>
    public PaletteConformer? SourcePalette { get; }

    public RemapStyle Style
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            Suggest();
        }
    }

    // ================================================================== analysis

    /// <summary>Collects the picture's colours (every layer or only visible ones, every drawing of an animation).</summary>
    public static PaletteRemap Analyse(Document doc, IReadOnlyList<ColorRgb> target, bool includeHidden = true, RemapStyle style = RemapStyle.Closest)
    {
        if (target.Count == 0) throw new ArgumentException("The palette has no colours.", nameof(target));
        var images = doc.AllImages(includeHidden).Select(t => t.Image).Distinct().ToList();
        // Pass 1: the solid colours (opaque pixels, or exact palette pixels of a locked picture) are reliable.
        var solid = Count(images, p => Pixel.A(p) == 255 || doc.PaletteConformer?.ExactIndex(p) >= 0, p => Straight(p, doc.PaletteConformer));
        var resolver = new SourceResolver(doc.PaletteConformer, solid);
        // Pass 2: every pixel, with partly transparent ones resolved to the solid colour they were painted with.
        var total = Count(images, _ => true, resolver.Key);

        List<RemapSource> rows;
        bool grouped = total.Count > MaxExactRows;
        if (!grouped)
            rows = total.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                .Select(kv => new RemapSource(FromKey(kv.Key), kv.Value, [kv.Key])).ToList();
        else rows = Group(total, GroupRows);
        var remap = new PaletteRemap(target, total, rows, grouped, doc.PaletteConformer) { _style = style, _resolver = resolver };
        if (style != RemapStyle.Closest) remap.Suggest();
        return remap;
    }

    private static int Straight(uint p, PaletteConformer? source)
    {
        var (r, g, b, _) = source is null ? Pixel.ToStraight(p) : source.Decode(p);
        return r << 16 | g << 8 | b;
    }

    /// <summary>Colour keys (0xRRGGBB) weighted by alpha, over the painted area of every image.</summary>
    private static Dictionary<int, long> Count(List<RasterImage> images, Func<uint, bool> include, Func<uint, int> key)
    {
        var total = new Dictionary<int, long>();
        var gate = new object();
        Parallel.ForEach(images, () => new Dictionary<int, long>(), (img, _, local) =>
        {
            var area = img.ContentBounds();
            var px = img.Pixels;
            for (int y = area.Y; y < area.Bottom; y++)
                for (int i = y * img.Width + area.X, end = i + area.Width; i < end; i++)
                {
                    uint p = px[i];
                    byte a = Pixel.A(p);
                    if (a == 0 || !include(p)) continue;
                    int k = key(p);
                    local[k] = local.TryGetValue(k, out var w) ? w + a : a;
                }
            return local;
        }, local =>
        {
            lock (gate)
                foreach (var (k, w) in local) total[k] = total.TryGetValue(k, out var t) ? t + w : w;
        });
        return total;
    }

    /// <summary>
    /// Which colour a pixel "is". Premultiplied 8-bit storage can't hold the exact RGB of a faint pixel (at alpha 1 every
    /// colour becomes nearly black or one primary), so a partly transparent pixel that is consistent — within that
    /// rounding — with one of the picture's solid colours counts as that colour; otherwise its own un-premultiplied colour.
    /// Deterministic: the most used consistent colour wins, then the closest.
    /// </summary>
    private sealed class SourceResolver
    {
        private readonly PaletteConformer? _source;
        private readonly ColorRgb[] _candidates;
        private readonly ulong[] _cache = new ulong[1 << 16];

        public SourceResolver(PaletteConformer? source, Dictionary<int, long> solid)
        {
            _source = source;
            _candidates = solid.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(256).Select(kv => FromKey(kv.Key)).ToArray();
        }

        public int Key(uint p)
        {
            byte a = Pixel.A(p);
            if (a == 255 || _source?.ExactIndex(p) >= 0 || _candidates.Length == 0) return Straight(p, _source);
            int slot = (int)((p * 2654435761u) >> 16);
            ulong e = _cache[slot];
            if ((uint)(e >> 32) == p) return (int)(uint)e;
            var (r, g, b, _) = Pixel.ToStraight(p);
            int tolerance = 1 + (255 + a) / (2 * a);
            int best = -1, bestD = int.MaxValue;
            foreach (var c in _candidates)
            {
                int dr = Math.Abs(c.R - r), dg = Math.Abs(c.G - g), db = Math.Abs(c.B - b);
                if (dr > tolerance || dg > tolerance || db > tolerance) continue;
                int d = dr + dg + db;
                if (d < bestD)
                {
                    bestD = d;
                    best = c.R << 16 | c.G << 8 | c.B;
                    if (d == 0) break;
                }
            }
            int key = best >= 0 ? best : r << 16 | g << 8 | b;
            _cache[slot] = ((ulong)p << 32) | (uint)key;
            return key;
        }
    }

    /// <summary>Weighted k-means in OKLab (deterministic seeding: the most used colour, then the farthest ones).</summary>
    private static List<RemapSource> Group(Dictionary<int, long> colours, int k)
    {
        var keys = colours.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).ToArray();
        // Very colourful pictures: cluster the most used colours, then place every colour in its nearest group.
        var train = keys.Length > 60_000 ? keys[..60_000] : keys;
        var labs = train.Select(key => OkLab.From(FromKey(key))).ToArray();
        var weights = train.Select(key => (double)colours[key]).ToArray();
        k = Math.Min(k, train.Length);
        var centres = new List<OkLab> { labs[0] };
        var nearestD = labs.Select(l => (double)l.DistanceSquared(labs[0])).ToArray();
        while (centres.Count < k)
        {
            int best = 0;
            double bestScore = -1;
            for (int i = 0; i < labs.Length; i++)
            {
                double score = nearestD[i] * Math.Sqrt(weights[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            centres.Add(labs[best]);
            for (int i = 0; i < labs.Length; i++) nearestD[i] = Math.Min(nearestD[i], labs[i].DistanceSquared(labs[best]));
        }
        var assign = new int[labs.Length];
        for (int iteration = 0; iteration < 8; iteration++)
        {
            Parallel.For(0, labs.Length, i => assign[i] = NearestCentre(labs[i], centres));
            var sum = new double[k, 4];
            for (int i = 0; i < labs.Length; i++)
            {
                int c = assign[i];
                sum[c, 0] += labs[i].L * weights[i];
                sum[c, 1] += labs[i].A * weights[i];
                sum[c, 2] += labs[i].B * weights[i];
                sum[c, 3] += weights[i];
            }
            for (int c = 0; c < k; c++)
                if (sum[c, 3] > 0) centres[c] = new OkLab((float)(sum[c, 0] / sum[c, 3]), (float)(sum[c, 1] / sum[c, 3]), (float)(sum[c, 2] / sum[c, 3]));
        }
        var members = Enumerable.Range(0, k).Select(_ => new List<int>()).ToArray();
        var rowWeights = new long[k];
        var allAssign = new int[keys.Length];
        Parallel.For(0, keys.Length, i => allAssign[i] = i < labs.Length ? NearestCentre(labs[i], centres) : NearestCentre(OkLab.From(FromKey(keys[i])), centres));
        for (int i = 0; i < keys.Length; i++)
        {
            members[allAssign[i]].Add(keys[i]); // keys are sorted by weight, so the first member is the most used
            rowWeights[allAssign[i]] += colours[keys[i]];
        }
        return Enumerable.Range(0, k).Where(c => members[c].Count > 0)
            .Select(c => new RemapSource(FromKey(members[c][0]), rowWeights[c], members[c].ToArray()))
            .OrderByDescending(r => r.Weight).ToList();
    }

    private static int NearestCentre(OkLab lab, List<OkLab> centres)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int c = 0; c < centres.Count; c++)
        {
            float d = lab.DistanceSquared(centres[c]);
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }
        return best;
    }

    // ================================================================== suggestion and editing

    private void Suggest()
    {
        _auto.Clear();
        Array.Clear(_pixelCache);
        float srcLo = 0, srcHi = 1, palLo = _targetLab.Min(l => l.L), palHi = _targetLab.Max(l => l.L);
        if (_style == RemapStyle.LightAndDark)
        {
            // The picture's 2nd to 98th percentile of lightness (by use), stretched over the palette's range.
            var byL = _weights.Select(kv => (L: OkLab.From(FromKey(kv.Key)).L, W: kv.Value)).OrderBy(t => t.L).ToList();
            double total = byL.Sum(t => (double)t.W), acc = 0;
            srcLo = byL[0].L;
            srcHi = byL[^1].L;
            bool lowSet = false;
            foreach (var (l, w) in byL)
            {
                acc += w;
                if (!lowSet && acc >= total * 0.02)
                {
                    srcLo = l;
                    lowSet = true;
                }
                if (acc >= total * 0.98)
                {
                    srcHi = l;
                    break;
                }
            }
        }
        foreach (var key in _weights.Keys)
        {
            var c = FromKey(key);
            int exact = Array.IndexOf(_target, c);
            if (_style == RemapStyle.Closest && exact >= 0)
            {
                _auto[key] = exact;
                continue;
            }
            var lab = OkLab.From(c);
            if (_style == RemapStyle.LightAndDark && srcHi - srcLo > 0.02f)
            {
                float t = Math.Clamp((lab.L - srcLo) / (srcHi - srcLo), 0, 1);
                lab = lab with { L = palLo + t * (palHi - palLo) };
            }
            _auto[key] = NearestTarget(lab);
        }
    }

    private int NearestTarget(OkLab lab)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < _targetLab.Length; i++)
        {
            float d = lab.DistanceSquared(_targetLab[i]);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>The palette colour a picture colour (0xRRGGBB) goes to.</summary>
    public int TargetIndexOf(int key)
    {
        if (_rowOf.TryGetValue(key, out var row) && _override.TryGetValue(row, out var o)) return o;
        if (_auto.TryGetValue(key, out var a)) return a;
        return NearestTarget(OkLab.From(FromKey(key)));
    }

    /// <summary>Where a row goes: its own choice, or (for a group) where most of its colours go.</summary>
    public int TargetOf(int row)
    {
        if (_override.TryGetValue(row, out var o)) return o;
        var votes = new Dictionary<int, long>();
        foreach (int m in Sources[row].Members)
        {
            int t = _auto[m];
            votes[t] = votes.TryGetValue(t, out var v) ? v + _weights[m] : _weights[m];
        }
        return votes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }

    /// <summary>True for a group whose colours go to different palette colours (the automatic suggestion).</summary>
    public bool IsMixed(int row) => !_override.ContainsKey(row) && Sources[row].Members.Select(m => _auto[m]).Distinct().Count() > 1;

    /// <summary>True when the artist chose this row's colour.</summary>
    public bool IsChosen(int row) => _override.ContainsKey(row);

    /// <summary>Sends a row (all of a group's colours) to a palette colour.</summary>
    public void Assign(int row, int targetIndex)
    {
        if (row < 0 || row >= Sources.Count || targetIndex < 0 || targetIndex >= _target.Length) throw new ArgumentOutOfRangeException(nameof(row));
        _override[row] = targetIndex;
        _pixelCache = new ulong[1 << 16];
    }

    /// <summary>Back to the automatic suggestion everywhere.</summary>
    public void Reset()
    {
        _override.Clear();
        _pixelCache = new ulong[1 << 16];
    }

    /// <summary>The row holding a picture colour, or -1.</summary>
    public int RowOf(ColorRgb colour) => _rowOf.TryGetValue(colour.R << 16 | colour.G << 8 | colour.B, out var row) ? row : -1;

    /// <summary>
    /// For an edited version of the same palette (a colour replaced, "update from the library"): a colour of the old
    /// palette that the new one no longer has goes to the new colour at the same position, when the picture uses it as
    /// a row of its own.
    /// </summary>
    public void MatchByPosition(IReadOnlyList<ColorRgb> oldPalette)
    {
        for (int i = 0; i < oldPalette.Count && i < _target.Length; i++)
        {
            if (Array.IndexOf(_target, oldPalette[i]) >= 0) continue;
            int row = RowOf(oldPalette[i]);
            if (row >= 0 && !Sources[row].IsGroup) Assign(row, i);
        }
    }

    /// <summary>An independent copy (for computing previews in the background while the artist keeps editing).</summary>
    public PaletteRemap Clone()
    {
        var copy = (PaletteRemap)MemberwiseClone();
        copy.CopyFrom(this);
        return copy;
    }

    private void CopyFrom(PaletteRemap source)
    {
        _auto = new Dictionary<int, int>(source._auto);
        _override = new Dictionary<int, int>(source._override);
        _pixelCache = new ulong[1 << 16];
    }

    // ================================================================== applying

    /// <summary>A pixel with its colour mapped and its alpha unchanged.</summary>
    public uint MapPixel(uint p)
    {
        if (p == 0) return 0;
        byte a = Pixel.A(p);
        if (a == 0) return 0;
        var cache = _pixelCache;
        int slot = (int)((p * 2654435761u) >> 16);
        ulong e = cache[slot];
        if ((uint)(e >> 32) == p) return (uint)e;
        int key = _resolver?.Key(p) ?? Straight(p, SourcePalette);
        uint result = PaletteConformer.Encode(_target[TargetIndexOf(key)], a);
        cache[slot] = ((ulong)p << 32) | result;
        return result;
    }

    /// <summary>A remapped copy of an image (the original is not touched).</summary>
    public RasterImage Map(RasterImage image)
    {
        var copy = new RasterImage(image.Width, image.Height);
        var src = image.Pixels;
        var dst = copy.Pixels;
        int w = image.Width;
        var area = image.ContentBounds();
        void Row(int y)
        {
            for (int i = y * w + area.X, end = i + area.Width; i < end; i++) dst[i] = MapPixel(src[i]);
        }
        if ((long)area.Width * area.Height >= 128 * 1024) Parallel.For(area.Y, area.Bottom, Row);
        else
            for (int y = area.Y; y < area.Bottom; y++) Row(y);
        return copy;
    }

    /// <summary>What a frame looks like after the remap (the document is not changed).</summary>
    public RasterImage Preview(Document doc, int frame, bool includeHidden = true)
    {
        var layers = doc.Layers.Select(l =>
        {
            var img = doc.IsAnimated ? doc.CelAt(l, frame)?.Image ?? new RasterImage(doc.Width, doc.Height) : l.Image;
            return (l.Properties, includeHidden || l.Visible ? Map(img) : img);
        }).ToList();
        var copy = new Document(doc.Width, doc.Height, doc.Paper, doc.PaperVisible, layers);
        return copy.Flatten();
    }

    /// <summary>
    /// Applies the remap to the whole picture as one undo step: every layer (or only visible ones), every drawing of an
    /// animation (held drawings stay shared), and the palette becomes the document's palette, locked if asked.
    /// Locking always includes hidden layers, so the whole picture keeps to the palette.
    /// </summary>
    public void Apply(Document doc, string paletteName, bool includeHidden = true, bool lockAfter = false, string? libraryId = null,
        string historyName = "Remap to palette")
    {
        if (lockAfter) includeHidden = true;
        var state = doc.MapImages((layer, img) => includeHidden || layer.Visible ? Map(img) : img);
        var palette = new Palette(paletteName, _target) { LibraryId = libraryId };
        doc.ChangePalette(palette, lockAfter, state, historyName);
    }

    private static ColorRgb FromKey(int key) => new((byte)(key >> 16), (byte)(key >> 8), (byte)key);
}
