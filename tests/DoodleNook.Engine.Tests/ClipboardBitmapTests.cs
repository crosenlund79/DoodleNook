using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 012: Windows clipboard bitmaps (CF_DIB / CF_DIBV5) read pixel for pixel, including the rule that a
/// 32-bit bitmap whose alpha is zero everywhere is an opaque picture.</summary>
public sealed class ClipboardBitmapTests
{
    /// <summary>A device-independent bitmap as Windows keeps it on the clipboard. <paramref name="row"/> gives one row's
    /// pixel bytes (top row first, without padding).</summary>
    internal static byte[] Dib(int width, int height, int bits, Func<int, byte[]> row, bool topDown = false, int headerSize = 40,
        int compression = 0, uint[]? masks = null, uint[]? table = null)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(headerSize);
        bw.Write(width);
        bw.Write(topDown ? -height : height);
        bw.Write((short)1);
        bw.Write((short)bits);
        bw.Write(compression);
        bw.Write(0);
        bw.Write(2835);
        bw.Write(2835);
        bw.Write(table?.Length ?? 0);
        bw.Write(0);
        if (headerSize > 40)
        {
            foreach (var m in masks ?? [0, 0, 0, 0]) bw.Write(m);
            bw.Write(new byte[headerSize - 40 - 16]);
        }
        else if (masks is not null)
            foreach (var m in masks) bw.Write(m);
        foreach (var c in table ?? []) bw.Write(c);
        int stride = (width * bits + 31) / 32 * 4;
        for (int i = 0; i < height; i++)
        {
            var line = new byte[stride];
            row(topDown ? i : height - 1 - i).CopyTo(line, 0);
            bw.Write(line);
        }
        return ms.ToArray();
    }

    private static byte[] Bgra(int width, Func<int, (byte B, byte G, byte R, byte A)> px) =>
        Enumerable.Range(0, width).SelectMany(x => { var p = px(x); return new[] { p.B, p.G, p.R, p.A }; }).ToArray();

    [Fact]
    public void A_32_bit_bitmap_with_zero_alpha_everywhere_is_opaque_and_the_right_way_up()
    {
        var dib = Dib(3, 2, 32, y => Bgra(3, x => ((byte)(x * 10), (byte)(y * 50), 200, 0)));
        var img = DibDecoder.Decode(dib);
        Assert.Equal((3, 2), (img.Width, img.Height));
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                Assert.Equal(Pixel.Opaque(new ColorRgb(200, (byte)(y * 50), (byte)(x * 10))), img[x, y]);
    }

    [Fact]
    public void A_32_bit_bitmap_with_alpha_keeps_its_transparency()
    {
        var dib = Dib(2, 2, 32, y => Bgra(2, x => (30, 60, 90, x == 0 ? (byte)0 : (byte)128)));
        var img = DibDecoder.Decode(dib);
        Assert.Equal(0u, Pixel.A(img[0, 0]));
        Assert.Equal(Pixel.FromStraight(90, 60, 30, 128), img[1, 1]);
    }

    [Fact]
    public void Twenty_four_bit_rows_are_padded_and_top_down_bitmaps_read_in_order()
    {
        byte[] Row(int y) => Enumerable.Range(0, 3).SelectMany(x => new[] { (byte)(x + 1), (byte)(y + 1), (byte)250 }).ToArray();
        foreach (bool topDown in new[] { false, true })
        {
            var img = DibDecoder.Decode(Dib(3, 3, 24, Row, topDown));
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    Assert.Equal(Pixel.Opaque(new ColorRgb(250, (byte)(y + 1), (byte)(x + 1))), img[x, y]);
        }
    }

    [Fact]
    public void A_v5_header_with_colour_masks_and_alpha_is_read()
    {
        var dib = Dib(2, 1, 32, _ => Bgra(2, x => (10, 20, 30, x == 0 ? (byte)255 : (byte)64)), headerSize: 124, compression: 3,
            masks: [0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000]);
        var img = DibDecoder.Decode(dib);
        Assert.Equal(Pixel.FromStraight(30, 20, 10, 255), img[0, 0]);
        Assert.Equal(Pixel.FromStraight(30, 20, 10, 64), img[1, 0]);
    }

    [Fact]
    public void Sixteen_bit_565_colour_masks_after_a_plain_header_are_read()
    {
        ushort[] values = [0xF800, 0x07E0, 0x0010];
        var dib = Dib(3, 1, 16, _ => values.SelectMany(BitConverter.GetBytes).ToArray(), compression: 3, masks: [0xF800, 0x07E0, 0x001F]);
        var img = DibDecoder.Decode(dib);
        Assert.Equal(Pixel.Opaque(new ColorRgb(255, 0, 0)), img[0, 0]);
        Assert.Equal(Pixel.Opaque(new ColorRgb(0, 255, 0)), img[1, 0]);
        Assert.Equal(Pixel.Opaque(new ColorRgb(0, 0, 132)), img[2, 0]); // 16 of 31, rounded
    }

    [Fact]
    public void Palette_bitmaps_of_8_and_1_bits_use_their_colour_table()
    {
        uint[] table = [0x00FF0000, 0x0000FF00, 0x000000FF, 0x00102030];
        var img8 = DibDecoder.Decode(Dib(5, 1, 8, _ => [0, 1, 2, 3, 0], table: table));
        Assert.Equal(Pixel.Opaque(new ColorRgb(255, 0, 0)), img8[0, 0]);
        Assert.Equal(Pixel.Opaque(new ColorRgb(0, 0, 255)), img8[2, 0]);
        Assert.Equal(Pixel.Opaque(new ColorRgb(0x10, 0x20, 0x30)), img8[3, 0]);

        var img1 = DibDecoder.Decode(Dib(10, 1, 1, _ => [0b1010_0000, 0b0100_0000], table: [0x00000000, 0x00FFFFFF]));
        bool[] white = [true, false, true, false, false, false, false, false, false, true];
        for (int x = 0; x < 10; x++)
            Assert.Equal(Pixel.Opaque(white[x] ? new ColorRgb(255, 255, 255) : new ColorRgb(0, 0, 0)), img1[x, 0]);
    }

    [Fact]
    public void Damaged_compressed_or_huge_bitmaps_give_a_readable_message()
    {
        var good = Dib(4, 4, 24, _ => new byte[12]);
        Assert.Throws<InvalidDataException>(() => DibDecoder.Decode(good.AsSpan(0, good.Length - 10)));
        Assert.Throws<InvalidDataException>(() => DibDecoder.Decode(new byte[12]));
        var rle = Dib(4, 4, 8, _ => new byte[4], compression: 1, table: [0, 0]);
        Assert.Contains("compressed", Assert.Throws<InvalidDataException>(() => DibDecoder.Decode(rle)).Message);
        var huge = Dib(1, 1, 24, _ => new byte[3]);
        BitConverter.GetBytes(40_000).CopyTo(huge, 4);
        Assert.Contains("larger than DoodleNook supports", Assert.Throws<InvalidDataException>(() => DibDecoder.Decode(huge)).Message);
    }
}
