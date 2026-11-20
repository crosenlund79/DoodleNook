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

    /// <summary>Wraps existing pixels (no copy). The array must hold width × height pixels.</summary>
    public static RasterImage FromPixels(int width, int height, uint[] pixels)
    {
        Validate(width, height);
        if (pixels.Length != width * height) throw new ArgumentException("Pixel count mismatch.", nameof(pixels));
        return new RasterImage(width, height, pixels);
    }

    public uint this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    public Span<uint> Row(int y) => Pixels.AsSpan(y * Width, Width);

    public void Fill(uint value) => Array.Fill(Pixels, value);

    /// <summary>Makes a rectangle transparent.</summary>
    public void Clear(PixelRect region)
    {
        region = region.Intersect(Bounds);
        for (int y = region.Y; y < region.Bottom; y++) Array.Clear(Pixels, y * Width + region.X, region.Width);
    }

    public RasterImage Clone() => new(Width, Height, (uint[])Pixels.Clone());

    /// <summary>Pixel at (x, y), or transparent outside the image.</summary>
    public uint GetOrTransparent(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? Pixels[y * Width + x] : 0u;

    /// <summary>A new image holding a copy of <paramref name="region"/> (which may extend outside: those pixels are transparent).</summary>
    public RasterImage Crop(PixelRect region)
    {
        var result = new RasterImage(region.Width, region.Height);
        var src = region.Intersect(Bounds);
        for (int y = src.Y; y < src.Bottom; y++)
            Array.Copy(Pixels, y * Width + src.X, result.Pixels, (y - region.Y) * region.Width + (src.X - region.X), src.Width);
        return result;
    }

    /// <summary>Copies <paramref name="source"/> into this image with its top-left corner at (x, y), clipped. No blending.</summary>
    public void Paste(RasterImage source, int x, int y)
    {
        var dst = new PixelRect(x, y, source.Width, source.Height).Intersect(Bounds);
        for (int yy = dst.Y; yy < dst.Bottom; yy++)
            Array.Copy(source.Pixels, (yy - y) * source.Width + (dst.X - x), Pixels, yy * Width + dst.X, dst.Width);
    }

    /// <summary>The smallest rectangle containing every non-transparent pixel (empty if the image is blank).</summary>
    public PixelRect ContentBounds() => ContentBounds(Bounds);

    public PixelRect ContentBounds(PixelRect within)
    {
        within = within.Intersect(Bounds);
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (int y = within.Y; y < within.Bottom; y++)
        {
            int row = y * Width;
            int first = -1, last = -1;
            for (int x = within.X; x < within.Right; x++)
            {
                if (Pixels[row + x] == 0) continue;
                if (first < 0) first = x;
                last = x;
            }
            if (first < 0) continue;
            if (first < l) l = first;
            if (last > r) r = last;
            if (y < t) t = y;
            b = y;
        }
        return r < 0 ? PixelRect.Empty : PixelRect.FromEdges(l, t, r + 1, b + 1);
    }

    public bool IsBlank() => Array.TrueForAll(Pixels, p => p == 0);

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

    /// <summary>
    /// Set on composites of a palette-locked picture: straight conversions (exports) then return exactly the palette
    /// colour of every palette pixel, also where it is partly transparent. Not copied by <see cref="Clone"/>.
    /// </summary>
    public PaletteConformer? ExactPalette { get; set; }

    /// <summary>Straight (non-premultiplied) RGBA of one pixel, exact for palette pixels when <see cref="ExactPalette"/> is set.</summary>
    public (byte R, byte G, byte B, byte A) StraightAt(int index) =>
        ExactPalette is { } p ? p.Decode(Pixels[index]) : Pixel.ToStraight(Pixels[index]);

    /// <summary>Returns straight (non-premultiplied) RGBA bytes, as stored in PNG files.</summary>
    public byte[] ToStraightRgba()
    {
        var bytes = new byte[Pixels.Length * 4];
        var exact = ExactPalette;
        for (int i = 0, j = 0; i < Pixels.Length; i++, j += 4)
        {
            var (r, g, b, a) = exact is null ? Pixel.ToStraight(Pixels[i]) : exact.Decode(Pixels[i]);
            bytes[j] = r; bytes[j + 1] = g; bytes[j + 2] = b; bytes[j + 3] = a;
        }
        return bytes;
    }
}
