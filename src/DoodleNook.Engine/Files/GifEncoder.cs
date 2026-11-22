using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>Animated GIF options. GIF has at most 256 colours and only fully transparent or fully opaque pixels.</summary>
public sealed record GifOptions(bool Loop = true, bool Dither = false, int AlphaThreshold = 128);

/// <summary>
/// A small, dependency-free animated GIF89a encoder.
/// <list type="bullet">
/// <item>Pictures with at most 255 colours (pixel art) keep their exact colours; others are reduced with a variance-based
/// median cut (Mission 016: the paper colour no longer swallows the artwork's colours), optionally with Floyd–Steinberg
/// dithering.</item>
/// <item>Transparency is 1-bit: pixels below the alpha threshold become transparent, others opaque.</item>
/// <item>Every frame is a full frame with "restore to background" disposal, so transparent areas never show the
/// previous frame.</item>
/// <item>Delays are in hundredths of a second, accumulated from exact millisecond times so long animations don't
/// drift; consecutive identical frames are merged into one longer frame.</item>
/// </list>
/// </summary>
public static class GifEncoder
{
    public static void Save(IReadOnlyList<ExportFrame> frames, string path, GifOptions options)
    {
        using var ms = new MemoryStream();
        Write(frames, ms, options);
        AnimationExport.WriteAtomically(path, ms.ToArray());
    }

    public static void Write(IReadOnlyList<ExportFrame> frames, Stream stream, GifOptions options)
    {
        if (frames.Count == 0) throw new ArgumentException("There are no frames to export.", nameof(frames));
        int w = frames[0].Image.Width, h = frames[0].Image.Height;
        if (w > 65535 || h > 65535) throw new ArgumentException("GIF pictures are limited to 65535 pixels per side.");
        var merged = MergeIdentical(frames);
        int threshold = Math.Clamp(options.AlphaThreshold, 1, 255);

        // Straight (un-premultiplied) colours; -1 marks transparent pixels.
        var straight = merged.Select(f => ToStraight(f.Image, threshold)).ToList();
        bool transparent = straight.Any(p => Array.IndexOf(p, -1) >= 0);
        int maxColours = transparent ? 255 : 256;
        var palette = BuildPalette(straight, maxColours, out bool exact);
        int transparentIndex = transparent ? palette.Count : -1;
        int entries = palette.Count + (transparent ? 1 : 0);
        int bits = 1;
        while ((1 << bits) < Math.Max(2, entries)) bits++;

        var o = new BinaryWriter(stream);
        o.Write("GIF89a"u8);
        o.Write((ushort)w);
        o.Write((ushort)h);
        o.Write((byte)(0x80 | ((bits - 1) << 4) | (bits - 1))); // global colour table, its size
        o.Write((byte)(transparent ? transparentIndex : 0));     // background colour index
        o.Write((byte)0);
        for (int i = 0; i < 1 << bits; i++)
        {
            int c = i < palette.Count ? palette[i] : 0;
            o.Write((byte)(c >> 16));
            o.Write((byte)(c >> 8));
            o.Write((byte)c);
        }
        if (options.Loop)
        {
            o.Write(new byte[] { 0x21, 0xFF, 0x0B });
            o.Write("NETSCAPE2.0"u8);
            o.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 }); // loop forever
        }

        var map = new ColourMap(palette, exact);
        long elapsed = 0;
        for (int i = 0; i < merged.Count; i++)
        {
            long start = elapsed;
            elapsed += merged[i].DurationMs;
            int delay = Math.Max(2, (int)(Math.Round(elapsed / 10.0) - Math.Round(start / 10.0)));
            o.Write((byte)0x21);
            o.Write((byte)0xF9);
            o.Write((byte)4);
            // Disposal 2 (restore to background) when transparency is used, else 1 (keep).
            o.Write((byte)(((transparent ? 2 : 1) << 2) | (transparent ? 1 : 0)));
            o.Write((ushort)Math.Min(delay, 65535));
            o.Write((byte)(transparent ? transparentIndex : 0));
            o.Write((byte)0);

            o.Write((byte)0x2C);
            o.Write((ushort)0);
            o.Write((ushort)0);
            o.Write((ushort)w);
            o.Write((ushort)h);
            o.Write((byte)0); // no local colour table, not interlaced

            var indices = Index(straight[i], w, h, map, transparentIndex, options.Dither && !exact);
            int minCode = Math.Max(2, bits);
            o.Write((byte)minCode);
            WriteSubBlocks(o, Lzw(indices, minCode));
        }
        o.Write((byte)0x3B);
        o.Flush();
    }

    private static List<ExportFrame> MergeIdentical(IReadOnlyList<ExportFrame> frames)
    {
        var result = new List<ExportFrame> { frames[0] };
        for (int i = 1; i < frames.Count; i++)
        {
            var last = result[^1];
            if (frames[i].Image.Pixels.AsSpan().SequenceEqual(last.Image.Pixels))
                result[^1] = last with { DurationMs = last.DurationMs + frames[i].DurationMs };
            else result.Add(frames[i]);
        }
        return result;
    }

    private static int[] ToStraight(RasterImage image, int threshold)
    {
        var px = image.Pixels;
        var result = new int[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            uint p = px[i];
            if (Pixel.A(p) < threshold)
            {
                result[i] = -1;
                continue;
            }
            var (r, g, b, _) = image.StraightAt(i);
            result[i] = r << 16 | g << 8 | b;
        }
        return result;
    }

    /// <summary>The colour weights used everywhere colours are compared (red, green, blue): green differences are
    /// the most visible, blue the least. The palette is built and matched with the same weights.</summary>
    private const int WeightR = 2, WeightG = 4, WeightB = 3;

    /// <summary>Exact colours when they fit, otherwise a reduced palette (<see cref="Reduce"/>).</summary>
    internal static List<int> BuildPalette(IReadOnlyList<int[]> frames, int maxColours, out bool exact)
    {
        var colours = Histogram(frames, out bool coarse);
        if (!coarse && colours.Count <= maxColours)
        {
            exact = true;
            var list = colours.Select(c => c.C).OrderBy(c => c).ToList();
            if (list.Count == 0) list.Add(0);
            return list;
        }
        exact = false;
        return Reduce(colours, maxColours);
    }

    /// <summary>
    /// How many pixels of every frame use each colour. Past 65,536 different colours (photos, very soft paintings) the
    /// colours are gathered in small groups (6 bits per channel) and each group is represented by its own average, so the
    /// count stays small without losing any frame's colours.
    /// </summary>
    internal static List<(int C, long N)> Histogram(IReadOnlyList<int[]> frames, out bool coarse)
    {
        const int Limit = 65_536;
        var bins = new Dictionary<int, (long N, long R, long G, long B)>();
        int mask = 0xFFFFFF;
        coarse = false;
        foreach (var f in frames)
            foreach (int c in f)
            {
                if (c < 0) continue;
                ref var bin = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(bins, c & mask, out _);
                bin = (bin.N + 1, bin.R + ((c >> 16) & 255), bin.G + ((c >> 8) & 255), bin.B + (c & 255));
                if (!coarse && bins.Count > Limit)
                {
                    coarse = true;
                    mask = 0xFCFCFC;
                    var grouped = new Dictionary<int, (long N, long R, long G, long B)>();
                    foreach (var (k, v) in bins)
                    {
                        ref var g = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(grouped, k & mask, out _);
                        g = (g.N + v.N, g.R + v.R, g.G + v.G, g.B + v.B);
                    }
                    bins = grouped;
                }
            }
        return bins.Select(kv => kv.Value.N == 0 ? (kv.Key, 0L) : (Mean(kv.Value.N, kv.Value.R, kv.Value.G, kv.Value.B), kv.Value.N)).ToList();
    }

    private static int Mean(long n, long r, long g, long b) =>
        (int)((r + n / 2) / n) << 16 | (int)((g + n / 2) / n) << 8 | (int)((b + n / 2) / n);

    /// <summary>
    /// Reduces many colours to <paramref name="maxColours"/> (variance-based median cut, then a few refining passes).
    /// The group whose pixels are furthest from their average is split first, on its most varied channel, at the point
    /// that leaves the two halves closest to their own averages. A colour that covers much of the picture (white paper)
    /// therefore gets an entry of its own, and small areas of a distinct colour (an orange shirt) are not averaged into
    /// it. Each palette entry is the average of its pixels.
    /// </summary>
    internal static List<int> Reduce(List<(int C, long N)> colours, int maxColours)
    {
        var boxes = new List<Box> { new(colours.OrderBy(c => c.C).ToArray()) };
        while (boxes.Count < maxColours)
        {
            int worst = -1;
            for (int i = 0; i < boxes.Count; i++)
                if (boxes[i].Items.Length >= 2 && boxes[i].Error > 0 && (worst < 0 || boxes[i].Error > boxes[worst].Error)) worst = i;
            if (worst < 0) break;
            var (a, b) = boxes[worst].Split();
            boxes[worst] = a;
            boxes.Add(b);
        }
        var palette = boxes.Select(b => b.Mean).Distinct().ToList();
        return Refine(colours, palette, passes: 3);
    }

    /// <summary>A few k-means passes: every colour joins its nearest entry, every entry moves to its colours' average.
    /// Each pass can only bring the palette closer to the picture.</summary>
    private static List<int> Refine(List<(int C, long N)> colours, List<int> palette, int passes)
    {
        var map = new ColourMap(palette, exact: false);
        for (int pass = 0; pass < passes; pass++)
        {
            var sums = new (long N, long R, long G, long B)[palette.Count];
            foreach (var (c, n) in colours)
            {
                ref var s = ref sums[map.Nearest(c)];
                s = (s.N + n, s.R + ((c >> 16) & 255) * n, s.G + ((c >> 8) & 255) * n, s.B + (c & 255) * n);
            }
            var next = new List<int>(palette.Count);
            for (int i = 0; i < palette.Count; i++) next.Add(sums[i].N == 0 ? palette[i] : Mean(sums[i].N, sums[i].R, sums[i].G, sums[i].B));
            next = next.Distinct().ToList();
            if (next.SequenceEqual(palette)) break;
            palette = next;
            map = new ColourMap(palette, exact: false);
        }
        return palette;
    }

    private sealed class Box
    {
        public readonly (int C, long N)[] Items;
        public readonly double Error;
        public readonly int Mean;

        public Box((int C, long N)[] items)
        {
            Items = items;
            long n = 0, r = 0, g = 0, b = 0;
            double rr = 0, gg = 0, bb = 0;
            foreach (var (c, k) in items)
            {
                int cr = (c >> 16) & 255, cg = (c >> 8) & 255, cb = c & 255;
                n += k;
                r += cr * k;
                g += cg * k;
                b += cb * k;
                rr += (double)cr * cr * k;
                gg += (double)cg * cg * k;
                bb += (double)cb * cb * k;
            }
            Mean = n == 0 ? 0 : GifEncoder.Mean(n, r, g, b);
            Error = n == 0 ? 0 : WeightR * (rr - (double)r * r / n) + WeightG * (gg - (double)g * g / n) + WeightB * (bb - (double)b * b / n);
        }

        /// <summary>Splits on the most varied channel, where the two halves' summed squared error is smallest.</summary>
        public (Box, Box) Split()
        {
            int channel = 0;
            double widest = -1;
            for (int ch = 0; ch < 3; ch++)
            {
                int shift = 16 - ch * 8;
                long n = 0, s = 0;
                double ss = 0;
                foreach (var (c, k) in Items)
                {
                    int v = (c >> shift) & 255;
                    n += k;
                    s += v * k;
                    ss += (double)v * v * k;
                }
                double variance = (ss - (double)s * s / Math.Max(1, n)) * (ch == 0 ? WeightR : ch == 1 ? WeightG : WeightB);
                if (variance > widest)
                {
                    widest = variance;
                    channel = ch;
                }
            }
            int sh = 16 - channel * 8;
            var sorted = Items.OrderBy(e => (e.C >> sh) & 255).ThenBy(e => e.C).ToArray();

            // Minimising both halves' squared error = maximising Σ weight·(sum²/n) over the halves.
            long tn = 0, tr = 0, tg = 0, tb = 0;
            foreach (var (c, k) in sorted)
            {
                tn += k;
                tr += ((c >> 16) & 255) * k;
                tg += ((c >> 8) & 255) * k;
                tb += (c & 255) * k;
            }
            long ln = 0, lr = 0, lg = 0, lb = 0;
            int cut = 1;
            double best = double.MinValue;
            for (int i = 0; i < sorted.Length - 1; i++)
            {
                var (c, k) = sorted[i];
                ln += k;
                lr += ((c >> 16) & 255) * k;
                lg += ((c >> 8) & 255) * k;
                lb += (c & 255) * k;
                long rn = tn - ln;
                if (ln == 0 || rn == 0) continue;
                double score = Score(lr, lg, lb, ln) + Score(tr - lr, tg - lg, tb - lb, rn);
                if (score > best)
                {
                    best = score;
                    cut = i + 1;
                }
            }
            return (new Box(sorted[..cut]), new Box(sorted[cut..]));

            static double Score(long r, long g, long b, long n) =>
                (WeightR * (double)r * r + WeightG * (double)g * g + WeightB * (double)b * b) / n;
        }
    }

    /// <summary>Finds the palette entry nearest to a colour (same weights as the palette). Remembers recent answers in
    /// a small table keyed by the exact colour, so every colour gets its own true nearest entry.</summary>
    private sealed class ColourMap(List<int> palette, bool exact)
    {
        private const int CacheBits = 18;
        private readonly Dictionary<int, int> _exact = exact ? palette.Select((c, i) => (c, i)).ToDictionary(e => e.c, e => e.i) : new();
        private readonly int[] _cacheColour = Enumerable.Repeat(-1, 1 << CacheBits).ToArray();
        private readonly byte[] _cacheIndex = new byte[1 << CacheBits];

        public int Nearest(int c)
        {
            if (exact && _exact.TryGetValue(c, out int i)) return i;
            int key = (int)(((uint)c * 2654435761u) >> (32 - CacheBits));
            if (_cacheColour[key] == c) return _cacheIndex[key];
            int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            int best = 0, bestD = int.MaxValue;
            for (int k = 0; k < palette.Count; k++)
            {
                int p = palette[k];
                int dr = ((p >> 16) & 255) - r, dg = ((p >> 8) & 255) - g, db = (p & 255) - b;
                int d = WeightR * dr * dr + WeightG * dg * dg + WeightB * db * db;
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }
            _cacheColour[key] = c;
            _cacheIndex[key] = (byte)best;
            return best;
        }

        public int Colour(int index) => palette[index];
    }

    private static byte[] Index(int[] colours, int w, int h, ColourMap map, int transparentIndex, bool dither)
    {
        var result = new byte[colours.Length];
        if (!dither)
        {
            for (int i = 0; i < colours.Length; i++) result[i] = (byte)(colours[i] < 0 ? transparentIndex : map.Nearest(colours[i]));
            return result;
        }
        // Floyd–Steinberg error diffusion (only between opaque pixels).
        var err = new float[(w + 2) * 3 * 2];
        for (int y = 0; y < h; y++)
        {
            int cur = (y & 1) * (w + 2) * 3, next = ((y + 1) & 1) * (w + 2) * 3;
            Array.Clear(err, next, (w + 2) * 3);
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int c = colours[i];
                if (c < 0)
                {
                    result[i] = (byte)transparentIndex;
                    continue;
                }
                int e = cur + (x + 1) * 3;
                int r = Math.Clamp((int)Math.Round(((c >> 16) & 255) + err[e]), 0, 255);
                int g = Math.Clamp((int)Math.Round(((c >> 8) & 255) + err[e + 1]), 0, 255);
                int b = Math.Clamp((int)Math.Round((c & 255) + err[e + 2]), 0, 255);
                int k = map.Nearest(r << 16 | g << 8 | b);
                result[i] = (byte)k;
                int p = map.Colour(k);
                float er = r - ((p >> 16) & 255), eg = g - ((p >> 8) & 255), eb = b - (p & 255);
                void Spread(int idx, float f)
                {
                    err[idx] += er * f;
                    err[idx + 1] += eg * f;
                    err[idx + 2] += eb * f;
                }
                Spread(cur + (x + 2) * 3, 7 / 16f);
                Spread(next + x * 3, 3 / 16f);
                Spread(next + (x + 1) * 3, 5 / 16f);
                Spread(next + (x + 2) * 3, 1 / 16f);
            }
        }
        return result;
    }

    /// <summary>GIF's variable-width LZW compression.</summary>
    internal static byte[] Lzw(byte[] indices, int minCodeSize)
    {
        int clear = 1 << minCodeSize, eoi = clear + 1;
        var output = new List<byte>(indices.Length / 2 + 16);
        int bitBuffer = 0, bitCount = 0;
        int codeSize = minCodeSize + 1;
        void Emit(int code)
        {
            bitBuffer |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                output.Add((byte)bitBuffer);
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }
        var table = new Dictionary<int, int>();
        int next = eoi + 1;
        Emit(clear);
        if (indices.Length == 0)
        {
            Emit(eoi);
            if (bitCount > 0) output.Add((byte)bitBuffer);
            return output.ToArray();
        }
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            int key = prefix << 8 | k;
            if (table.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }
            Emit(prefix);
            if (next < 4096)
            {
                table[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                Emit(clear);
                table.Clear();
                next = eoi + 1;
                codeSize = minCodeSize + 1;
            }
            prefix = k;
        }
        Emit(prefix);
        // The decoder adds one more table entry after reading that last code, which can widen the codes by one bit
        // before it reads end-of-information; mirror that so the final code has the width the decoder expects.
        if (next < 4096 && ++next > (1 << codeSize) && codeSize < 12) codeSize++;
        Emit(eoi);
        if (bitCount > 0) output.Add((byte)bitBuffer);
        return output.ToArray();
    }

    private static void WriteSubBlocks(BinaryWriter o, byte[] data)
    {
        for (int i = 0; i < data.Length; i += 255)
        {
            int n = Math.Min(255, data.Length - i);
            o.Write((byte)n);
            o.Write(data, i, n);
        }
        o.Write((byte)0);
    }
}
