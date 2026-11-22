using System.Buffers.Binary;
using System.IO.Compression;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>
/// A small, dependency-free PNG encoder/decoder for 8-bit RGBA/RGB images (what DoodleNook stores inside .dnk files).
/// Lossless: premultiplied pixels are written as straight RGBA and read back exactly.
/// </summary>
public static class PngCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(RasterImage image, CompressionLevel level = CompressionLevel.Optimal) => Encode(image, level, null);

    /// <summary>Encodes with the print resolution stored in the file (a "pHYs" chunk), so other programs know the
    /// picture's physical size (Mission 015: Storybook pages).</summary>
    public static byte[] Encode(RasterImage image, int dpi) => Encode(image, CompressionLevel.Optimal, dpi);

    private static byte[] Encode(RasterImage image, CompressionLevel level, int? dpi)
    {
        int w = image.Width, h = image.Height, stride = w * 4;
        var rgba = image.ToStraightRgba();
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, level, leaveOpen: true))
        {
            var prev = new byte[stride];
            var cur = new byte[stride];
            var candidates = new byte[5][];
            for (int f = 0; f < 5; f++) candidates[f] = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                Array.Copy(rgba, y * stride, cur, 0, stride);
                // Pick the filter with the smallest sum of absolute values (a standard heuristic). Mission 011: one loop per
                // filter instead of a switch per byte; the same bytes and the same choice as before. The loops stay in this
                // method so the runtime optimises them while the first image is still being encoded.
                int best = 0;
                long bestScore = long.MaxValue;
                int head = Math.Min(4, stride);
                for (int f = 0; f < 5; f++)
                {
                    var o = candidates[f];
                    long score = 0;
                    switch (f)
                    {
                        case 0:
                            for (int i = 0; i < stride; i++) { byte v = cur[i]; o[i] = v; score += v < 128 ? v : 256 - v; }
                            break;
                        case 1:
                            for (int i = 0; i < head; i++) { byte v = cur[i]; o[i] = v; score += v < 128 ? v : 256 - v; }
                            for (int i = 4; i < stride; i++) { byte v = (byte)(cur[i] - cur[i - 4]); o[i] = v; score += v < 128 ? v : 256 - v; }
                            break;
                        case 2:
                            for (int i = 0; i < stride; i++) { byte v = (byte)(cur[i] - prev[i]); o[i] = v; score += v < 128 ? v : 256 - v; }
                            break;
                        case 3:
                            for (int i = 0; i < head; i++) { byte v = (byte)(cur[i] - (prev[i] >> 1)); o[i] = v; score += v < 128 ? v : 256 - v; }
                            for (int i = 4; i < stride; i++) { byte v = (byte)(cur[i] - ((cur[i - 4] + prev[i]) >> 1)); o[i] = v; score += v < 128 ? v : 256 - v; }
                            break;
                        default:
                            for (int i = 0; i < head; i++) { byte v = (byte)(cur[i] - Paeth(0, prev[i], 0)); o[i] = v; score += v < 128 ? v : 256 - v; }
                            for (int i = 4; i < stride; i++) { byte v = (byte)(cur[i] - Paeth(cur[i - 4], prev[i], prev[i - 4])); o[i] = v; score += v < 128 ? v : 256 - v; }
                            break;
                    }
                    if (score < bestScore) { bestScore = score; best = f; }
                }
                z.WriteByte((byte)best);
                z.Write(candidates[best], 0, stride);
                (prev, cur) = (cur, prev);
            }
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        WriteChunk(png, "IHDR", ihdr);
        if (dpi is { } d and > 0)
        {
            var phys = new byte[9];
            uint perMetre = (uint)Math.Round(d / 0.0254);
            BinaryPrimitives.WriteUInt32BigEndian(phys, perMetre);
            BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(4), perMetre);
            phys[8] = 1; // the unit is the metre
            WriteChunk(png, "pHYs", phys);
        }
        WriteChunk(png, "IDAT", raw.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>
    /// Mission 016: reads a PNG made by another program — every standard colour type and bit depth (palette and grey,
    /// with or without transparency), not only the 8-bit RGB(A) that <see cref="Decode"/> reads in DoodleNook's own
    /// files. Interlaced PNGs aren't read (a short message says how to fix that).
    /// </summary>
    public static RasterImage DecodeAny(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("Not a PNG image.");
        int pos = 8, w = 0, h = 0, depth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        while (pos + 8 <= data.Length)
        {
            int len = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            if (len < 0 || pos + 12 + len > data.Length) throw new InvalidDataException("The PNG data is damaged.");
            var type = data.AsSpan(pos + 4, 4);
            var body = data.AsSpan(pos + 8, len);
            if (type.SequenceEqual("IHDR"u8))
            {
                if (len < 13) throw new InvalidDataException("The PNG header is damaged.");
                w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(body);
                h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                depth = body[8];
                colorType = body[9];
                interlace = body[12];
                RasterImage.Validate(w, h);
            }
            else if (type.SequenceEqual("PLTE"u8)) palette = body.ToArray();
            else if (type.SequenceEqual("tRNS"u8)) trns = body.ToArray();
            else if (type.SequenceEqual("IDAT"u8)) idat.Write(body);
            else if (type.SequenceEqual("IEND"u8)) break;
            pos += 12 + len;
        }
        if (colorType < 0) throw new InvalidDataException("The PNG has no header.");
        if (interlace != 0) throw new InvalidDataException("Interlaced PNGs can't be read. Save it without interlacing and try again.");
        if (idat.Length == 0) throw new InvalidDataException("The PNG has no image data.");
        if (colorType == 3 && palette is null) throw new InvalidDataException("The PNG's palette is missing.");
        var rgba = ApngDecoder.DecodePixels(idat.ToArray(), w, h, depth, colorType, palette, trns);
        var image = new RasterImage(w, h);
        for (int i = 0; i < image.Pixels.Length; i++)
            image.Pixels[i] = Pixel.FromStraight(rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]);
        return image;
    }

    public static RasterImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature)) throw new InvalidDataException("Not a PNG image.");
        int pos = 8, w = 0, h = 0, colorType = -1;
        using var idat = new MemoryStream();
        while (pos + 8 <= data.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
            if (len < 0 || pos + 12 + len > data.Length) throw new InvalidDataException("The PNG data is damaged.");
            var type = System.Text.Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            var body = data.Slice(pos + 8, len);
            if (type == "IHDR")
            {
                w = BinaryPrimitives.ReadInt32BigEndian(body);
                h = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                if (body[8] != 8 || body[12] != 0 || body[10] != 0 || body[11] != 0) throw new InvalidDataException("Unsupported PNG layout in project file.");
                colorType = body[9];
                if (colorType != 6 && colorType != 2) throw new InvalidDataException("Unsupported PNG colour type in project file.");
                RasterImage.Validate(w, h);
            }
            else if (type == "IDAT") idat.Write(body);
            else if (type == "IEND") break;
            pos += 12 + len;
        }
        if (colorType < 0) throw new InvalidDataException("The PNG has no header.");

        int bpp = colorType == 6 ? 4 : 3, stride = w * bpp;
        var raw = new byte[(long)(stride + 1) * h];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n == 0) throw new InvalidDataException("The PNG pixel data is incomplete.");
                read += n;
            }
        }

        var rgba = new byte[w * h * 4];
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            int filter = raw[(long)y * (stride + 1)];
            Array.Copy(raw, (long)y * (stride + 1) + 1, cur, 0, stride);
            // Undo the row's filter in place (one loop per filter; the first pixel has no left neighbour and counts as 0).
            int head = Math.Min(bpp, stride);
            switch (filter)
            {
                case 0:
                    break;
                case 1:
                    for (int i = bpp; i < stride; i++) cur[i] += cur[i - bpp];
                    break;
                case 2:
                    for (int i = 0; i < stride; i++) cur[i] += prev[i];
                    break;
                case 3:
                    for (int i = 0; i < head; i++) cur[i] += (byte)(prev[i] >> 1);
                    for (int i = bpp; i < stride; i++) cur[i] += (byte)((cur[i - bpp] + prev[i]) >> 1);
                    break;
                case 4:
                    for (int i = 0; i < head; i++) cur[i] += (byte)Paeth(0, prev[i], 0);
                    for (int i = bpp; i < stride; i++) cur[i] += (byte)Paeth(cur[i - bpp], prev[i], prev[i - bpp]);
                    break;
                default:
                    throw new InvalidDataException("The PNG uses an unknown row filter.");
            }
            if (bpp == 4) Buffer.BlockCopy(cur, 0, rgba, y * w * 4, stride);
            else for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                rgba[o] = cur[x * bpp];
                rgba[o + 1] = cur[x * bpp + 1];
                rgba[o + 2] = cur[x * bpp + 2];
                rgba[o + 3] = bpp == 4 ? cur[x * bpp + 3] : (byte)255;
            }
            (prev, cur) = (cur, prev);
        }
        return RasterImage.FromStraightRgba(w, h, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream s, string type, byte[] body)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, body.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(body);
        uint crc = Crc(typeBytes, 0xFFFFFFFFu);
        crc = Crc(body, crc) ^ 0xFFFFFFFFu;
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static uint Crc(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}
