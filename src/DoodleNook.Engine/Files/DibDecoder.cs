using System.Buffers.Binary;
using System.Numerics;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>
/// Mission 012: reads a device-independent bitmap as Windows puts it on the clipboard (CF_DIB and CF_DIBV5: an info
/// header, optional colour masks and colour table, then the pixels; no file header). Paint, browsers and screenshot tools
/// copy pictures this way.
/// <para>Transparency: 32-bit pixels carry alpha in their fourth byte, but many programs leave that byte at zero for an
/// opaque picture. When every pixel's alpha is zero the picture is read as opaque (the rule Windows' own tools follow);
/// otherwise the alpha is used as it is (straight, not premultiplied).</para>
/// Bit depths 1, 4, 8 (with colour table), 16, 24 and 32; uncompressed or with colour masks; bottom-up or top-down.
/// Run-length compressed and embedded JPEG/PNG bitmaps aren't read.
/// </summary>
public static class DibDecoder
{
    private const int BiRgb = 0, BiBitfields = 3, BiAlphaBitfields = 6;

    public static RasterImage Decode(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40) throw new InvalidDataException("The bitmap on the clipboard is damaged.");
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (headerSize < 40 || headerSize > dib.Length) throw new InvalidDataException("The bitmap on the clipboard is damaged.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(dib[16..]);
        int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        bool topDown = rawHeight < 0;
        int height = rawHeight == int.MinValue ? 0 : Math.Abs(rawHeight);
        if (!RasterImage.IsSupportedSize(width, height))
            throw new InvalidDataException(width > 0 && height > 0
                ? $"The picture on the clipboard is {width} × {height} pixels, which is larger than DoodleNook supports."
                : "The bitmap on the clipboard is damaged.");
        if (compression is not (BiRgb or BiBitfields or BiAlphaBitfields))
            throw new InvalidDataException("The bitmap on the clipboard is compressed in a way DoodleNook can't read.");
        if (bits is not (1 or 4 or 8 or 16 or 24 or 32)) throw new InvalidDataException("The bitmap on the clipboard has an unusual colour depth.");

        // Colour masks: inside a V4/V5 header, or three (four) DWORDs right after a plain 40-byte header.
        uint rMask = 0, gMask = 0, bMask = 0, aMask = 0;
        int offset = headerSize;
        bool masked = compression is BiBitfields or BiAlphaBitfields;
        if (masked)
        {
            if (headerSize >= 52)
            {
                rMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[40..]);
                gMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[44..]);
                bMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[48..]);
                if (headerSize >= 56) aMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[52..]);
            }
            else
            {
                int n = compression == BiAlphaBitfields ? 4 : 3;
                if (dib.Length < offset + n * 4) throw new InvalidDataException("The bitmap on the clipboard is damaged.");
                rMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[offset..]);
                gMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[(offset + 4)..]);
                bMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[(offset + 8)..]);
                if (n == 4) aMask = BinaryPrimitives.ReadUInt32LittleEndian(dib[(offset + 12)..]);
                offset += n * 4;
            }
        }
        else if (bits == 16) (rMask, gMask, bMask) = (0x7C00, 0x03E0, 0x001F);
        else if (bits == 32) (rMask, gMask, bMask, aMask) = (0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);

        // The colour table (palette pictures; other depths may carry an optional one, which is skipped).
        int tableEntries = colorsUsed > 0 ? colorsUsed : bits <= 8 ? 1 << bits : 0;
        if (tableEntries < 0 || tableEntries > 65536) throw new InvalidDataException("The bitmap on the clipboard is damaged.");
        var table = bits <= 8 ? new uint[Math.Max(tableEntries, 1 << bits)] : [];
        Array.Fill(table, Pixel.FromStraight(0, 0, 0, 255)); // an index past the table reads as black, as Windows does
        if (dib.Length < offset + tableEntries * 4L) throw new InvalidDataException("The bitmap on the clipboard is damaged.");
        for (int i = 0; i < tableEntries && bits <= 8; i++)
        {
            var e = dib.Slice(offset + i * 4, 4);
            table[i] = Pixel.FromStraight(e[2], e[1], e[0], 255);
        }
        offset += tableEntries * 4;

        long stride = ((long)width * bits + 31) / 32 * 4;
        if (dib.Length < offset + stride * height) throw new InvalidDataException("The bitmap on the clipboard is incomplete.");
        var pixels = dib[offset..];

        var image = new RasterImage(width, height);
        var dst = image.Pixels;
        bool alphaSeen = false;
        var channel = masked || bits is 16 or 32 ? new[] { new Mask(rMask), new Mask(gMask), new Mask(bMask), new Mask(aMask) } : null;
        var alpha = new byte[bits == 32 || (masked && aMask != 0) ? (long)width * height : 0];
        for (int y = 0; y < height; y++)
        {
            var row = pixels.Slice((int)(stride * (topDown ? y : height - 1 - y)), (int)stride);
            int line = y * width;
            for (int x = 0; x < width; x++)
            {
                uint c;
                switch (bits)
                {
                    case 1 or 4 or 8:
                    {
                        int bit = x * bits;
                        int index = (row[bit >> 3] >> (8 - bits - (bit & 7))) & ((1 << bits) - 1);
                        c = table[index];
                        break;
                    }
                    case 24:
                        c = Pixel.FromStraight(row[x * 3 + 2], row[x * 3 + 1], row[x * 3], 255);
                        break;
                    default:
                    {
                        uint v = bits == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]) : BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]);
                        byte a = channel![3].Read(v);
                        if (alpha.Length > 0)
                        {
                            alpha[line + x] = a;
                            if (a != 0) alphaSeen = true;
                        }
                        // Straight colour for now; alpha is applied below once we know whether it means anything.
                        c = Pixel.FromStraight(channel[0].Read(v), channel[1].Read(v), channel[2].Read(v), 255);
                        break;
                    }
                }
                dst[line + x] = c;
            }
        }
        if (alphaSeen)
        {
            for (int i = 0; i < dst.Length; i++)
            {
                uint c = dst[i];
                dst[i] = Pixel.FromStraight((byte)(c >> 16), (byte)(c >> 8), (byte)c, alpha[i]);
            }
        }
        return image;
    }

    /// <summary>One colour channel inside a packed pixel, scaled to 0–255 (a zero mask reads as 0).</summary>
    private readonly record struct Mask(uint Bits)
    {
        private int Shift => Bits == 0 ? 0 : BitOperations.TrailingZeroCount(Bits);
        private int Width => BitOperations.PopCount(Bits);

        public byte Read(uint value)
        {
            if (Bits == 0) return 0;
            uint v = (value & Bits) >> Shift;
            int w = Width;
            if (w == 8) return (byte)v;
            uint max = w >= 32 ? uint.MaxValue : (1u << w) - 1;
            return (byte)((v * 255 + max / 2) / max);
        }
    }
}
