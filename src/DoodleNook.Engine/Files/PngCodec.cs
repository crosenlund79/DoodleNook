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

    public static byte[] Encode(RasterImage image, CompressionLevel level = CompressionLevel.Optimal)
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
                // filter instead of a switch per byte; the same bytes and the same choice as before.
                int best = 0;
                long bestScore = long.MaxValue;
                for (int f = 0; f < 5; f++)
                {
                    long score = FilterRow(f, cur, prev, candidates[f]);
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
        WriteChunk(png, "IDAT", raw.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
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
            Unfilter(filter, cur, prev, bpp);
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

    /// <summary>Filters one row with PNG filter <paramref name="f"/> into <paramref name="o"/>; returns the sum of absolute values.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static long FilterRow(int f, byte[] cur, byte[] prev, byte[] o)
    {
        const int bpp = 4;
        int n = cur.Length;
        long score = 0;
        switch (f)
        {
            case 0:
                for (int i = 0; i < n; i++) score += Abs(o[i] = cur[i]);
                break;
            case 1:
                for (int i = 0; i < n; i++) score += Abs(o[i] = (byte)(cur[i] - (i >= bpp ? cur[i - bpp] : 0)));
                break;
            case 2:
                for (int i = 0; i < n; i++) score += Abs(o[i] = (byte)(cur[i] - prev[i]));
                break;
            case 3:
                for (int i = 0; i < n; i++) score += Abs(o[i] = (byte)(cur[i] - (((i >= bpp ? cur[i - bpp] : 0) + prev[i]) >> 1)));
                break;
            default:
                for (int i = 0; i < n; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, c = i >= bpp ? prev[i - bpp] : 0;
                    score += Abs(o[i] = (byte)(cur[i] - Paeth(a, prev[i], c)));
                }
                break;
        }
        return score;
    }

    private static int Abs(byte v) => v < 128 ? v : 256 - v;

    /// <summary>Undoes PNG filter <paramref name="filter"/> on one row, in place.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        int n = cur.Length;
        switch (filter)
        {
            case 0:
                break;
            case 1:
                for (int i = bpp; i < n; i++) cur[i] += cur[i - bpp];
                break;
            case 2:
                for (int i = 0; i < n; i++) cur[i] += prev[i];
                break;
            case 3:
                for (int i = 0; i < n; i++) cur[i] += (byte)(((i >= bpp ? cur[i - bpp] : 0) + prev[i]) >> 1);
                break;
            case 4:
                for (int i = 0; i < n; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, c = i >= bpp ? prev[i - bpp] : 0;
                    cur[i] += (byte)Paeth(a, prev[i], c);
                }
                break;
            default:
                throw new InvalidDataException("The PNG uses an unknown row filter.");
        }
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
