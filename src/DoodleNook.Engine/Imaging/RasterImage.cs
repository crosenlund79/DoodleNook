namespace DoodleNook.Engine.Imaging;

/// <summary>
/// A rectangle of premultiplied BGRA pixels (see <see cref="Pixel"/>), stored row by row.
/// This is plain data: it knows nothing about screens, views or UI frameworks.
/// </summary>
public sealed class RasterImage
{
    /// <summary>Largest width or height accepted (keeps memory and GPU textures sane).</summary>
    public const int MaxDimension = 16384;

    /// <summary>Largest pixel count accepted (about 400 MB of pixel memory).</summary>
    public const long MaxPixels = 100_000_000;

    public RasterImage(int width, int height)
    {
        Validate(width, height);
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    private RasterImage(int width, int height, uint[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Row-major pixels, index = y * Width + x.</summary>
    public uint[] Pixels { get; }

    public PixelRect Bounds => new(0, 0, Width, Height);

    public static void Validate(int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "An image needs at least one pixel.");
        if (width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(width), $"{width} × {height} is larger than DoodleNook supports ({MaxDimension} px per side, {MaxPixels / 1_000_000} megapixels).");
    }

    public static bool IsSupportedSize(int width, int height) =>
        width >= 1 && height >= 1 && width <= MaxDimension && height <= MaxDimension && (long)width * height <= MaxPixels;

    public uint this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public Span<uint> Row(int y) => Pixels.AsSpan(y * Width, Width);

    public void Fill(uint value) => Array.Fill(Pixels, value);

    public RasterImage Clone() => new(Width, Height, (uint[])Pixels.Clone());

    /// <summary>Copies a region (clipped to the image) into <paramref name="destination"/>, tightly packed.</summary>
    public void CopyRegion(PixelRect region, Span<uint> destination)
    {
        region = region.Intersect(Bounds);
        for (int row = 0; row < region.Height; row++)
            Pixels.AsSpan((region.Y + row) * Width + region.X, region.Width)
                .CopyTo(destination.Slice(row * region.Width, region.Width));
    }

    public uint[] CopyRegion(PixelRect region)
    {
        region = region.Intersect(Bounds);
        var result = new uint[region.Area];
        CopyRegion(region, result);
        return result;
    }

    /// <summary>Writes tightly packed pixels into a region (which must lie inside the image).</summary>
    public void WriteRegion(PixelRect region, ReadOnlySpan<uint> source)
    {
        if (region.Intersect(Bounds) != region) throw new ArgumentOutOfRangeException(nameof(region));
        for (int row = 0; row < region.Height; row++)
            source.Slice(row * region.Width, region.Width)
                .CopyTo(Pixels.AsSpan((region.Y + row) * Width + region.X, region.Width));
    }

    /// <summary>Creates an image from straight (non-premultiplied) RGBA bytes, as stored in PNG files.</summary>
    public static RasterImage FromStraightRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var image = new RasterImage(width, height);
        if (rgba.Length < image.Pixels.Length * 4) throw new ArgumentException("Not enough pixel data.", nameof(rgba));
        var px = image.Pixels;
        for (int i = 0, j = 0; i < px.Length; i++, j += 4)
            px[i] = Pixel.FromStraight(rgba[j], rgba[j + 1], rgba[j + 2], rgba[j + 3]);
        return image;
    }

    /// <summary>Returns straight (non-premultiplied) RGBA bytes, as stored in PNG files.</summary>
    public byte[] ToStraightRgba()
    {
        var bytes = new byte[Pixels.Length * 4];
        for (int i = 0, j = 0; i < Pixels.Length; i++, j += 4)
        {
            var (r, g, b, a) = Pixel.ToStraight(Pixels[i]);
            bytes[j] = r; bytes[j + 1] = g; bytes[j + 2] = b; bytes[j + 3] = a;
        }
        return bytes;
    }
}
