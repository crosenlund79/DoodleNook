using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>Animated GIF options. GIF has at most 256 colours and only fully transparent or fully opaque pixels.</summary>
public sealed record GifOptions(bool Loop = true, bool Dither = false, int AlphaThreshold = 128);

/// <summary>
/// A small, dependency-free animated GIF89a encoder.
/// <list type="bullet">
/// <item>Pictures with at most 255 colours (pixel art) keep their exact colours; others are reduced with median cut,
/// optionally with Floyd–Steinberg dithering.</item>
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
            var (r, g, b, _) = Pixel.ToStraight(p);
            result[i] = r << 16 | g << 8 | b;
        }
        return result;
    }

    /// <summary>Exact colours when they fit, otherwise a median-cut palette.</summary>
    internal static List<int> BuildPalette(IReadOnlyList<int[]> frames, int maxColours, out bool exact)
    {
        var counts = new Dictionary<int, int>();
        foreach (var f in frames)
            foreach (int c in f)
            {
                if (c < 0) continue;
                counts[c] = counts.GetValueOrDefault(c) + 1;
                if (counts.Count > 100_000) break;
            }
        if (counts.Count <= maxColours)
        {
            exact = true;
            var list = counts.Keys.OrderBy(c => c).ToList();
            if (list.Count == 0) list.Add(0);
            return list;
        }
        exact = false;
        return MedianCut(counts, maxColours);
    }

    private static List<int> MedianCut(Dictionary<int, int> counts, int maxColours)
    {
        var boxes = new List<List<(int C, int N)>> { counts.Select(kv => (kv.Key, kv.Value)).ToList() };
        while (boxes.Count < maxColours)
        {
            // Split the box with the widest channel range (weighted by population).
            int best = -1;
            long bestScore = 0;
            int bestChannel = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Count < 2) continue;
                for (int ch = 0; ch < 3; ch++)
                {
                    int shift = 16 - ch * 8;
                    int lo = 255, hi = 0;
                    long pop = 0;
                    foreach (var (c, n) in box)
                    {
                        int v = (c >> shift) & 255;
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                        pop += n;
                    }
                    long score = (hi - lo) * (long)Math.Sqrt(pop);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                        bestChannel = ch;
                    }
                }
            }
            if (best < 0) break;
            int sh = 16 - bestChannel * 8;
            var sorted = boxes[best].OrderBy(e => (e.C >> sh) & 255).ToList();
            long total = sorted.Sum(e => (long)e.N), acc = 0;
            int cut = 1;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                acc += sorted[i].N;
                if (acc * 2 >= total)
                {
                    cut = i + 1;
                    break;
                }
            }
            boxes[best] = sorted.GetRange(0, cut);
            boxes.Add(sorted.GetRange(cut, sorted.Count - cut));
        }
        return boxes.Select(box =>
        {
            long n = 0, r = 0, g = 0, b = 0;
            foreach (var (c, k) in box)
            {
                n += k;
                r += (long)((c >> 16) & 255) * k;
                g += (long)((c >> 8) & 255) * k;
                b += (long)(c & 255) * k;
            }
            return (int)(r / n) << 16 | (int)(g / n) << 8 | (int)(b / n);
        }).Distinct().ToList();
    }

    private sealed class ColourMap(List<int> palette, bool exact)
    {
        private readonly Dictionary<int, int> _exact = exact ? palette.Select((c, i) => (c, i)).ToDictionary(e => e.c, e => e.i) : new();
        private readonly short[] _cache = Enumerable.Repeat((short)-1, 1 << 18).ToArray();

        public int Nearest(int c)
        {
            if (exact && _exact.TryGetValue(c, out int i)) return i;
            int key = ((c >> 18) & 63) << 12 | ((c >> 10) & 63) << 6 | ((c >> 2) & 63);
            if (_cache[key] >= 0) return _cache[key];
            int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            int best = 0, bestD = int.MaxValue;
            for (int k = 0; k < palette.Count; k++)
            {
                int p = palette[k];
                int dr = ((p >> 16) & 255) - r, dg = ((p >> 8) & 255) - g, db = (p & 255) - b;
                int d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                if (d < bestD)
                {
                    bestD = d;
                    best = k;
                }
            }
            _cache[key] = (short)best;
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
