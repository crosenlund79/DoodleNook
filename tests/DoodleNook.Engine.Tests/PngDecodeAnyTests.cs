using System.Buffers.Binary;
using System.IO.Compression;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 016: PNGs made by other programs (indexed, grey, 16-bit) are read where DoodleNook takes outside
/// files the engine decodes itself — palette pictures and storybook pictures. Found with real palette PNGs: indexed ones
/// were refused with a message about a "project file".</summary>
public sealed class PngDecodeAnyTests
{
    /// <summary>A minimal, valid PNG with the given header fields and raw scanlines (filter byte 0 on each row).</summary>
    private static byte[] Png(int w, int h, int depth, int colorType, byte[][] rows, byte[]? plte = null, byte[]? trns = null, int interlace = 0)
    {
        using var raw = new MemoryStream();
        foreach (var row in rows)
        {
            raw.WriteByte(0);
            raw.Write(row);
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw.ToArray());
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        void Chunk(string type, byte[] body)
        {
            var len = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, body.Length);
            png.Write(len);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            png.Write(t);
            png.Write(body);
            var crc = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32([.. t, .. body]));
            png.Write(crc);
        }
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = (byte)depth;
        ihdr[9] = (byte)colorType;
        ihdr[12] = (byte)interlace;
        Chunk("IHDR", ihdr);
        if (plte is not null) Chunk("PLTE", plte);
        if (trns is not null) Chunk("tRNS", trns);
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", []);
        return png.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }

    [Fact]
    public void An_indexed_4_bit_png_with_a_transparent_entry_reads_exactly()
    {
        // Four palette entries, index 3 fully transparent; one row of 4 pixels: 0 1 2 3 (two pixels per byte).
        byte[] plte = [0x1D, 0x2B, 0x53, 0xFF, 0x00, 0x4D, 0x00, 0xE4, 0x36, 0, 0, 0];
        var png = Png(4, 1, 4, 3, [[0x01, 0x23]], plte, trns: [255, 255, 255, 0]);
        Assert.Throws<InvalidDataException>(() => PngCodec.Decode(png)); // the project-file decoder doesn't read these
        var img = PngCodec.DecodeAny(png);
        Assert.Equal(Pixel.Opaque(new ColorRgb(0x1D, 0x2B, 0x53)), img[0, 0]);
        Assert.Equal(Pixel.Opaque(new ColorRgb(0xFF, 0x00, 0x4D)), img[1, 0]);
        Assert.Equal(Pixel.Opaque(new ColorRgb(0x00, 0xE4, 0x36)), img[2, 0]);
        Assert.Equal(0u, Pixel.A(img[3, 0]));
    }

    [Fact]
    public void Grey_and_16_bit_pngs_read_and_ordinary_ones_read_as_before()
    {
        var grey = PngCodec.DecodeAny(Png(2, 1, 8, 0, [[0, 200]]));
        Assert.Equal(Pixel.Opaque(new ColorRgb(200, 200, 200)), grey[1, 0]);
        var deep = PngCodec.DecodeAny(Png(1, 1, 16, 2, [[0x12, 0x34, 0x80, 0x00, 0xFF, 0xFF]]));
        Assert.Equal(Pixel.Opaque(new ColorRgb(0x12, 0x80, 0xFF)), deep[0, 0]);
        var rgba = new RasterImage(3, 2);
        rgba[1, 1] = Pixel.FromStraight(10, 20, 30, 128);
        var encoded = PngCodec.Encode(rgba);
        Assert.Equal(PngCodec.Decode(encoded).Pixels, PngCodec.DecodeAny(encoded).Pixels);
        var ex = Assert.Throws<InvalidDataException>(() => PngCodec.DecodeAny(Png(1, 1, 8, 0, [[5]], interlace: 1)));
        Assert.Contains("without interlacing", ex.Message);
    }

    [Fact]
    public void An_indexed_palette_picture_imports_as_a_palette()
    {
        byte[] plte = [0x1D, 0x2B, 0x53, 0xFF, 0x00, 0x4D, 0x00, 0xE4, 0x36, 0xFF, 0xF1, 0xE8];
        var path = Path.Combine(Path.GetTempPath(), "dn-indexed-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        File.WriteAllBytes(path, Png(4, 1, 8, 3, [[0, 1, 2, 3]], plte));
        try
        {
            var data = PaletteFiles.ReadFile(path);
            Assert.Equal(["#1D2B53", "#FF004D", "#00E436", "#FFF1E8"], data.Colors.Select(c => c.ToHex()).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
