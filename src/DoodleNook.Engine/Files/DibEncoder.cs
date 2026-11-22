using System.Buffers.Binary;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>
/// Mission 016: writes device-independent bitmaps as Windows programs expect them on the clipboard, the counterpart of
/// <see cref="DibDecoder"/>.
/// <list type="bullet">
/// <item><see cref="EncodeV5"/>: CF_DIBV5, 32 bits with straight (not premultiplied) alpha and explicit colour masks, for
/// programs that understand transparency.</item>
/// <item><see cref="EncodeOnWhite"/>: CF_DIB, 24 bits with transparent areas on white, for programs that ignore alpha
/// (they would otherwise show transparent areas as black).</item>
/// </list>
/// Both are bottom-up, as most readers expect, with rows padded to four bytes.
/// </summary>
public static class DibEncoder
{
    private const int V5HeaderSize = 124, InfoHeaderSize = 40, BiRgb = 0, BiBitfields = 3;

    public static byte[] EncodeV5(RasterImage image)
    {
        int w = image.Width, h = image.Height;
        long size = V5HeaderSize + 4L * w * h;
        if (size > int.MaxValue) throw new ArgumentException("The picture is too large for the clipboard.", nameof(image));
        var dib = new byte[size];
        var header = dib.AsSpan(0, V5HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header, V5HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], w);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], h); // positive: bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], BiBitfields);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], 4 * w * h);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], 2835); // 72 dpi
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], 2835);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], 0x00FF0000); // red mask
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], 0x0000FF00); // green
        BinaryPrimitives.WriteUInt32LittleEndian(header[48..], 0x000000FF); // blue
        BinaryPrimitives.WriteUInt32LittleEndian(header[52..], 0xFF000000); // alpha
        BinaryPrimitives.WriteUInt32LittleEndian(header[56..], 0x73524742); // LCS_sRGB ('sRGB')
        BinaryPrimitives.WriteInt32LittleEndian(header[108..], 4);          // LCS_GM_IMAGES (rendering intent)
        for (int y = 0; y < h; y++)
        {
            var row = dib.AsSpan(V5HeaderSize + 4 * w * (h - 1 - y), 4 * w);
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, a) = image.StraightAt(y * w + x);
                if (a == 0) r = g = b = 0;
                row[4 * x] = (byte)b;
                row[4 * x + 1] = (byte)g;
                row[4 * x + 2] = (byte)r;
                row[4 * x + 3] = (byte)a;
            }
        }
        return dib;
    }

    public static byte[] EncodeOnWhite(RasterImage image)
    {
        int w = image.Width, h = image.Height;
        int stride = (3 * w + 3) & ~3;
        long size = InfoHeaderSize + (long)stride * h;
        if (size > int.MaxValue) throw new ArgumentException("The picture is too large for the clipboard.", nameof(image));
        var dib = new byte[size];
        var header = dib.AsSpan(0, InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header, InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], w);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], h);
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], 24);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], BiRgb);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], stride * h);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], 2835);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], 2835);
        var white = Pixel.Opaque(ColorRgb.White);
        for (int y = 0; y < h; y++)
        {
            var row = dib.AsSpan(InfoHeaderSize + stride * (h - 1 - y), stride);
            for (int x = 0; x < w; x++)
            {
                var (r, g, b, _) = Pixel.ToStraight(Pixel.Over(image.Pixels[y * w + x], white));
                row[3 * x] = (byte)b;
                row[3 * x + 1] = (byte)g;
                row[3 * x + 2] = (byte)r;
            }
        }
        return dib;
    }
}
