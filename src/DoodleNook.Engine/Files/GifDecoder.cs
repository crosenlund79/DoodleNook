using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>
/// Mission 012: reads an animated GIF as the frames a viewer shows. GIF frames are often partial (a small rectangle that
/// changed) and say what to do with them afterwards (keep, clear to the background, or restore what was there), so each
/// frame is drawn onto a canvas of the logical screen size and the canvas after it is what the frame looks like.
/// <list type="bullet">
/// <item>Local and global colour tables, the transparent colour, interlaced images.</item>
/// <item>Disposal 0/1 (keep), 2 (clear to transparent, as browsers do) and 3 (restore the previous state).</item>
/// <item>Delays in hundredths of a second; like browsers, 0 or 1 (≤ 10 ms) means 100 ms.</item>
/// </list>
/// A damaged image's missing pixels are left as they were rather than failing the whole animation.
/// </summary>
public static class GifDecoder
{
    /// <summary>The delay browsers use for frames that say 0 or 10 ms.</summary>
    public const int DefaultDelayMs = 100;

    public static bool IsGif(ReadOnlySpan<byte> data) =>
        data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8' && (data[4] == '7' || data[4] == '9') && data[5] == 'a';

    public static ImportedAnimation Decode(byte[] data, IProgress<int>? framesRead = null, CancellationToken cancel = default)
    {
        if (!IsGif(data)) throw new InvalidDataException("This file isn't a GIF.");
        var r = new Reader(data, 6);
        int width = r.U16(), height = r.U16();
        int packed = r.U8();
        r.Skip(2); // background colour index (browsers show transparency instead) and aspect ratio
        if (width <= 0 || height <= 0) throw new InvalidDataException("The GIF has no size.");
        RasterImage.Validate(width, height);
        uint[]? global = (packed & 0x80) != 0 ? r.ColourTable(2 << (packed & 7)) : null;

        var canvas = new RasterImage(width, height);
        var frames = new List<RasterImage>();
        var delays = new List<int?>();
        // The graphic control extension for the next image.
        int disposal = 0, delayCs = 0, transparent = -1;
        // What to do before the next frame: the previous frame's disposal, its rectangle and (for "previous") a copy.
        int pendingDisposal = 0;
        PixelRect pendingRect = PixelRect.Empty;
        uint[]? pendingRestore = null;
        var notes = new List<string>();

        while (!r.End)
        {
            cancel.ThrowIfCancellationRequested();
            int block = r.U8();
            if (block == 0x3B) break; // trailer
            if (block == 0x21)
            {
                int label = r.U8();
                if (label == 0xF9 && r.Peek() >= 4)
                {
                    int size = r.U8();
                    int p = r.U8();
                    delayCs = r.U16();
                    int ti = r.U8();
                    r.Skip(size - 4);
                    disposal = (p >> 2) & 7;
                    transparent = (p & 1) != 0 ? ti : -1;
                    r.SkipSubBlocks();
                }
                else r.SkipSubBlocks();
                continue;
            }
            if (block != 0x2C)
            {
                if (frames.Count > 0) break; // trailing junk after a usable animation
                throw new InvalidDataException("The GIF data is damaged.");
            }

            int left = r.U16(), top = r.U16(), fw = r.U16(), fh = r.U16();
            int ip = r.U8();
            uint[]? local = (ip & 0x80) != 0 ? r.ColourTable(2 << (ip & 7)) : null;
            bool interlaced = (ip & 0x40) != 0;
            int minCode = r.U8();
            var lzw = r.SubBlocks();
            var table = local ?? global;

            // Before drawing: the previous frame's disposal.
            if (pendingDisposal == 2) canvas.Clear(pendingRect);
            else if (pendingDisposal == 3 && pendingRestore is not null) canvas.WriteRegion(pendingRect, pendingRestore);

            var rect = new PixelRect(left, top, fw, fh).Intersect(canvas.Bounds);
            uint[]? restore = disposal == 3 && !rect.IsEmpty ? canvas.CopyRegion(rect) : null;
            if (table is not null && fw > 0 && fh > 0)
            {
                var indices = Lzw(lzw, minCode, fw * fh, out bool complete);
                if (!complete && notes.Count == 0) notes.Add("Some frames of this GIF are incomplete; their missing pixels are left as they were.");
                if (interlaced) indices = Deinterlace(indices, fw, fh);
                for (int y = 0; y < fh; y++)
                {
                    int cy = top + y;
                    if (cy < 0 || cy >= height) continue;
                    for (int x = 0; x < fw; x++)
                    {
                        int cx = left + x;
                        if (cx < 0 || cx >= width) continue;
                        int i = y * fw + x;
                        int idx = indices[i];
                        if (idx < 0 || idx == transparent || idx >= table.Length) continue;
                        canvas.Pixels[cy * width + cx] = table[idx];
                    }
                }
            }
            frames.Add(canvas.Clone());
            delays.Add(delayCs <= 1 ? DefaultDelayMs : delayCs * 10);
            framesRead?.Report(frames.Count);

            pendingDisposal = disposal;
            pendingRect = rect;
            pendingRestore = restore;
            disposal = 0;
            delayCs = 0;
            transparent = -1;
        }
        if (frames.Count == 0) throw new InvalidDataException("The GIF has no pictures in it.");
        return new ImportedAnimation(width, height, frames, delays, notes);
    }

    /// <summary>GIF's LZW: variable-width codes (up to 12 bits), least significant bit first. Pixels not reached (damaged
    /// data) are −1.</summary>
    private static int[] Lzw(byte[] data, int minCodeSize, int count, out bool complete)
    {
        var output = new int[count];
        Array.Fill(output, -1);
        complete = false;
        if (minCodeSize is < 2 or > 11) minCodeSize = Math.Clamp(minCodeSize, 2, 11);
        int clear = 1 << minCodeSize, eoi = clear + 1;
        var prefix = new short[4096];
        var suffix = new byte[4096];
        var stack = new byte[4097];
        int codeSize = minCodeSize + 1, codeMask = (1 << codeSize) - 1, next = clear + 2;
        int old = -1, first = 0, n = 0;
        int bits = 0, buffer = 0, pos = 0;
        for (int i = 0; i < clear; i++) suffix[i] = (byte)i;

        while (n < count)
        {
            while (bits < codeSize)
            {
                if (pos >= data.Length) return output;
                buffer |= data[pos++] << bits;
                bits += 8;
            }
            int code = buffer & codeMask;
            buffer >>= codeSize;
            bits -= codeSize;

            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                codeMask = (1 << codeSize) - 1;
                next = clear + 2;
                old = -1;
                continue;
            }
            if (code == eoi) break;
            if (old == -1)
            {
                if (code >= clear) return output; // damaged: first code must be a colour
                output[n++] = code;
                old = first = code;
                continue;
            }

            int sp = 0, c = code;
            if (code >= next)
            {
                if (code > next) return output; // damaged
                stack[sp++] = (byte)first;
                c = old;
            }
            while (c >= clear)
            {
                stack[sp++] = suffix[c];
                c = prefix[c];
            }
            first = suffix[c];
            stack[sp++] = (byte)first;
            while (sp > 0 && n < count) output[n++] = stack[--sp];

            if (next < 4096)
            {
                prefix[next] = (short)old;
                suffix[next] = (byte)first;
                next++;
                if ((next & codeMask) == 0 && next < 4096)
                {
                    codeSize++;
                    codeMask = (1 << codeSize) - 1;
                }
            }
            old = code;
        }
        complete = n >= count;
        return output;
    }

    /// <summary>Interlaced rows come in four passes (every 8th from 0, every 8th from 4, every 4th from 2, every 2nd from 1).</summary>
    private static int[] Deinterlace(int[] indices, int w, int h)
    {
        var result = new int[indices.Length];
        int row = 0;
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
            for (int y = start; y < h; y += step, row++)
                Array.Copy(indices, row * w, result, y * w, w);
        return result;
    }

    private sealed class Reader(byte[] data, int pos)
    {
        public bool End => pos >= data.Length;
        public int Peek() => pos < data.Length ? data[pos] : 0;
        public int U8() => pos < data.Length ? data[pos++] : throw new InvalidDataException("The GIF data ends too early.");
        public int U16() => U8() | (U8() << 8);
        public void Skip(int n) => pos = Math.Min(data.Length, pos + Math.Max(0, n));

        public uint[] ColourTable(int entries)
        {
            var t = new uint[entries];
            for (int i = 0; i < entries; i++) t[i] = Pixel.Pack(255, (byte)U8(), (byte)U8(), (byte)U8());
            return t;
        }

        public void SkipSubBlocks()
        {
            while (!End)
            {
                int n = U8();
                if (n == 0) return;
                Skip(n);
            }
        }

        public byte[] SubBlocks()
        {
            using var ms = new MemoryStream();
            while (!End)
            {
                int n = U8();
                if (n == 0) break;
                int take = Math.Min(n, data.Length - pos);
                ms.Write(data, pos, take);
                pos += take;
            }
            return ms.ToArray();
        }
    }
}

/// <summary>Frames read from an animation file or an image sequence, each the full picture as a viewer shows it.</summary>
/// <param name="DelaysMs">Each frame's delay in milliseconds, or null where the source has no timing (image sequences).</param>
/// <param name="Notes">Anything the user should know (incomplete frames, timing that had to be adjusted).</param>
public sealed record ImportedAnimation(int Width, int Height, IReadOnlyList<RasterImage> Frames, IReadOnlyList<int?> DelaysMs, IReadOnlyList<string> Notes)
{
    public bool HasTiming => DelaysMs.Any(d => d is not null);
}
