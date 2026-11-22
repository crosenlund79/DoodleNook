using System.Buffers.Binary;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 016: the bitmaps Copy puts on the Windows clipboard read back exactly (transparency included) and
/// are laid out the way Windows programs expect.</summary>
public sealed class DibEncoderTests
{
    private static RasterImage Picture()
    {
        var img = new RasterImage(7, 5); // odd width: rows of the 24-bit bitmap need padding
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 7; x++)
                img[x, y] = x switch
                {
                    < 3 => Pixel.Opaque(new ColorRgb((byte)(30 * x), (byte)(40 * y), 200)),
                    < 5 => Pixel.FromStraight(240, 90, 40, 128),
                    _ => 0u, // transparent
                };
        return img;
    }

    [Fact]
    public void A_32_bit_bitmap_keeps_colours_and_transparency()
    {
        var img = Picture();
        var dib = DibEncoder.EncodeV5(img);
        Assert.Equal(124, BinaryPrimitives.ReadInt32LittleEndian(dib));
        Assert.Equal(5, BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)));   // bottom-up
        Assert.Equal(32, BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal(0xFF000000u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(52))); // alpha mask
        Assert.Equal(124 + 7 * 5 * 4, dib.Length);
        var back = DibDecoder.Decode(dib);
        Assert.Equal(img.Pixels, back.Pixels);
    }

    [Fact]
    public void A_24_bit_bitmap_shows_transparent_areas_on_white()
    {
        var img = Picture();
        var dib = DibEncoder.EncodeOnWhite(img);
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(dib));
        Assert.Equal(24, BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal(40 + 24 * 5, dib.Length); // 7 × 3 = 21 bytes per row, padded to 24
        var back = DibDecoder.Decode(dib);
        Assert.Equal(img[1, 2], back[1, 2]);                              // opaque: unchanged
        Assert.Equal(Pixel.Opaque(ColorRgb.White), back[6, 0]);           // transparent: white
        var (r, g, b, a) = Pixel.ToStraight(back[3, 4]);                  // half-transparent orange over white
        Assert.Equal(255, a);
        Assert.InRange(r, 246, 248);
        Assert.InRange(g, 171, 173);
        Assert.InRange(b, 146, 148);
    }
}
