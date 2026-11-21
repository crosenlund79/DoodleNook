using System.Buffers.Binary;
using System.IO.Compression;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>
/// Mission 012: reads an animated PNG (APNG) as the frames a viewer shows. Each frame is a rectangle at an offset with its
/// own delay; it either replaces the pixels under it or is laid over them, and afterwards it is kept, cleared or replaced
/// by what was there before. All standard PNG colour types and bit depths are read (palette and grey with transparency
/// included); interlaced animations are not. A PNG without animation chunks isn't an APNG (see <see cref="IsAnimated"/>).
/// </summary>
public static class ApngDecoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>True for a PNG whose animation control chunk comes before its image data.</summary>
    public static bool IsAnimated(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature)) return false;
        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
            if (len < 0 || pos + 12 + len > data.Length) return false;
            var type = data.Slice(pos + 4, 4);
            if (type.SequenceEqual("acTL"u8)) return true;
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8)) return false;
            pos += 12 + len;
        }
        return false;
    }

    private sealed record FrameControl(int Width, int Height, int X, int Y, int DelayMs, int Dispose, int Blend)
    {
        public readonly MemoryStream Data = new();
    }

    public static ImportedAnimation Decode(byte[] data, IProgress<int>? framesRead = null, CancellationToken cancel = default)
    {
        if (!IsAnimated(data)) throw new InvalidDataException("This PNG isn't animated.");
        int pos = 8, width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        var frames = new List<FrameControl>();
        FrameControl? current = null;
        while (pos + 8 <= data.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            if (len < 0 || pos + 12 + len > data.Length) throw new InvalidDataException("The PNG data is damaged.");
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            var body = data.AsSpan(pos + 8, len);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    depth = body[8];
                    colorType = body[9];
                    interlace = body[12];
                    RasterImage.Validate(width, height);
                    break;
                case "PLTE":
                    palette = body.ToArray();
                    break;
                case "tRNS":
                    trns = body.ToArray();
                    break;
                case "fcTL":
                {
                    int fw = BinaryPrimitives.ReadInt32BigEndian(body[4..]), fh = BinaryPrimitives.ReadInt32BigEndian(body[8..]);
                    int fx = BinaryPrimitives.ReadInt32BigEndian(body[12..]), fy = BinaryPrimitives.ReadInt32BigEndian(body[16..]);
                    int num = BinaryPrimitives.ReadUInt16BigEndian(body[20..]), den = BinaryPrimitives.ReadUInt16BigEndian(body[22..]);
                    if (den == 0) den = 100;
                    int ms = (int)Math.Round(num * 1000.0 / den);
                    current = new FrameControl(fw, fh, fx, fy, ms <= 10 ? GifDecoder.DefaultDelayMs : ms, body[24], body[25]);
                    frames.Add(current);
                    break;
                }
                case "IDAT":
                    // Image data is the first frame only when a frame control came before it; otherwise it is a still
                    // "default image" for viewers without APNG support, and not part of the animation.
                    if (current is not null && frames.Count == 1) current.Data.Write(body);
                    break;
                case "fdAT":
                    current?.Data.Write(body[4..]); // after the sequence number
                    break;
                case "IEND":
                    pos = data.Length;
                    continue;
            }
            pos += 12 + len;
        }
        if (width <= 0 || height <= 0) throw new InvalidDataException("The PNG has no header.");
        if (interlace != 0) throw new InvalidDataException("Interlaced animated PNGs aren't supported. Save it without interlacing and try again.");
        var usable = frames.Where(f => f.Data.Length > 0).ToList();
        if (usable.Count == 0) throw new InvalidDataException("The animated PNG has no frames.");

        var canvas = new RasterImage(width, height);
        var result = new List<RasterImage>();
        var delays = new List<int?>();
        int pendingDispose = 0;
        PixelRect pendingRect = PixelRect.Empty;
        uint[]? pendingRestore = null;
        for (int i = 0; i < usable.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var f = usable[i];
            if (pendingDispose == 1) canvas.Clear(pendingRect);
            else if (pendingDispose == 2 && pendingRestore is not null) canvas.WriteRegion(pendingRect, pendingRestore);

            var rect = new PixelRect(f.X, f.Y, f.Width, f.Height).Intersect(canvas.Bounds);
            int dispose = f.Dispose == 2 && i == 0 ? 1 : f.Dispose; // "previous" on the first frame means clear
            uint[]? restore = dispose == 2 && !rect.IsEmpty ? canvas.CopyRegion(rect) : null;
            var rgba = DecodePixels(f.Data.ToArray(), f.Width, f.Height, depth, colorType, palette, trns);
            for (int y = 0; y < f.Height; y++)
            {
                int cy = f.Y + y;
                if (cy < 0 || cy >= height) continue;
                for (int x = 0; x < f.Width; x++)
                {
                    int cx = f.X + x;
                    if (cx < 0 || cx >= width) continue;
                    int o = (y * f.Width + x) * 4;
                    uint src = Pixel.FromStraight(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
                    int di = cy * width + cx;
                    canvas.Pixels[di] = f.Blend == 0 ? src : Pixel.Over(src, canvas.Pixels[di]);
                }
            }
            result.Add(canvas.Clone());
            delays.Add(f.DelayMs);
            framesRead?.Report(result.Count);
            pendingDispose = dispose;
            pendingRect = rect;
            pendingRestore = restore;
        }
        return new ImportedAnimation(width, height, result, delays, []);
    }

    /// <summary>One frame's zlib data → straight RGBA, for any PNG colour type (0, 2, 3, 4, 6) and bit depth.</summary>
    internal static byte[] DecodePixels(byte[] zdata, int w, int h, int depth, int colorType, byte[]? palette, byte[]? trns)
    {
        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException("Unknown PNG colour type.") };
        if (depth is not (1 or 2 or 4 or 8 or 16) || (depth < 8 && colorType is not (0 or 3)) || (depth == 16 && colorType == 3))
            throw new InvalidDataException("Unsupported PNG bit depth.");
        int bitsPerPixel = channels * depth;
        int bpp = Math.Max(1, bitsPerPixel / 8);
        int stride = (w * bitsPerPixel + 7) / 8;
        var raw = new byte[(long)(stride + 1) * h];
        using (var z = new ZLibStream(new MemoryStream(zdata), CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n == 0) break; // incomplete: the rest stays zero
                read += n;
            }
        }
        var rgba = new byte[w * h * 4];
        var prev = new byte[stride];
        var cur = new byte[stride];
        int maxGrey = (1 << Math.Min(depth, 8)) - 1;
        for (int y = 0; y < h; y++)
        {
            int filter = raw[(long)y * (stride + 1)];
            Array.Copy(raw, (long)y * (stride + 1) + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = filter switch
                {
                    0 => cur[i],
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + ((a + b) >> 1)),
                    4 => (byte)(cur[i] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException("The PNG uses an unknown row filter."),
                };
            }
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                byte r, g, bl, al = 255;
                if (depth < 8)
                {
                    int bit = x * depth;
                    int v = (cur[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                    if (colorType == 3) (r, g, bl, al) = PaletteEntry(palette, trns, v);
                    else
                    {
                        byte grey = (byte)(v * 255 / maxGrey);
                        (r, g, bl) = (grey, grey, grey);
                        if (trns is { Length: >= 2 } && v == ((trns[0] << 8) | trns[1])) al = 0;
                    }
                }
                else
                {
                    int s = x * bpp, step = depth / 8;
                    int Sample(int k) => step == 1 ? cur[s + k] : (cur[s + 2 * k] << 8) | cur[s + 2 * k + 1];
                    byte B(int k) => step == 1 ? cur[s + k] : cur[s + 2 * k];
                    switch (colorType)
                    {
                        case 0:
                            r = g = bl = B(0);
                            if (trns is { Length: >= 2 } && Sample(0) == ((trns[0] << 8) | trns[1])) al = 0;
                            break;
                        case 2:
                            (r, g, bl) = (B(0), B(1), B(2));
                            if (trns is { Length: >= 6 } && Sample(0) == ((trns[0] << 8) | trns[1]) && Sample(1) == ((trns[2] << 8) | trns[3])
                                && Sample(2) == ((trns[4] << 8) | trns[5])) al = 0;
                            break;
                        case 3:
                            (r, g, bl, al) = PaletteEntry(palette, trns, cur[s]);
                            break;
                        case 4:
                            r = g = bl = B(0);
                            al = B(1);
                            break;
                        default:
                            (r, g, bl, al) = (B(0), B(1), B(2), B(3));
                            break;
                    }
                }
                rgba[o] = r;
                rgba[o + 1] = g;
                rgba[o + 2] = bl;
                rgba[o + 3] = al;
            }
            (prev, cur) = (cur, prev);
        }
        return rgba;
    }

    private static (byte R, byte G, byte B, byte A) PaletteEntry(byte[]? palette, byte[]? trns, int index)
    {
        if (palette is null || index * 3 + 2 >= palette.Length) return (0, 0, 0, 0);
        byte a = trns is not null && index < trns.Length ? trns[index] : (byte)255;
        return (palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2], a);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
