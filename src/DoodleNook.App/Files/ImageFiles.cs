using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using SkiaSharp;

namespace DoodleNook.App.Files;

/// <summary>Flattened export formats. All of them merge the layers into one picture; only .dnk keeps layers.</summary>
public enum ExportFormat
{
    Png,
    Jpeg,
    WebP,
}

/// <summary>
/// Opening and saving pictures: DoodleNook projects (.dnk, layered) and common images (PNG, JPEG, WebP, BMP, GIF),
/// decoded and encoded with SkiaSharp. Every write goes to a temporary file first, so a failed save never destroys
/// an existing file.
/// </summary>
public static class ImageFiles
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];

    public static bool IsProject(string path) => path.EndsWith(DnkFile.Extension, StringComparison.OrdinalIgnoreCase);

    public static string Extension(ExportFormat f) => f switch
    {
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.WebP => ".webp",
        _ => ".png",
    };

    public static string DisplayName(ExportFormat f) => f switch
    {
        ExportFormat.Jpeg => "JPEG",
        ExportFormat.WebP => "WebP",
        _ => "PNG",
    };

    public static bool SupportsTransparency(ExportFormat f) => f != ExportFormat.Jpeg;

    public static ExportFormat? FormatForPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => ExportFormat.Png,
        ".jpg" or ".jpeg" => ExportFormat.Jpeg,
        ".webp" => ExportFormat.WebP,
        _ => null,
    };

    // ------------------------------------------------------------------ open

    /// <summary>Opens a project or an image. Images become a one-layer document named after the file.</summary>
    public static Document Open(string path)
    {
        if (IsProject(path)) return DnkFile.Load(path);
        using var stream = File.OpenRead(path);
        return Document.FromImage(Decode(stream), LayerNameFor(path));
    }

    public static string LayerNameFor(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? "Image" : name.Length > 40 ? name[..40] : name;
    }

    public static RasterImage Decode(string path)
    {
        using var stream = File.OpenRead(path);
        return Decode(stream);
    }

    /// <summary>Decodes any image Skia can read into premultiplied pixels. Throws <see cref="InvalidDataException"/>
    /// with a readable message when the file can't be used.</summary>
    public static RasterImage Decode(Stream stream)
    {
        using var managed = new SKManagedStream(stream);
        using var codec = SKCodec.Create(managed) ?? throw new InvalidDataException("This file isn't an image DoodleNook can read.");
        var size = codec.Info;
        if (!RasterImage.IsSupportedSize(size.Width, size.Height))
            throw new InvalidDataException($"The image is {size.Width} × {size.Height} pixels, which is larger than DoodleNook supports.");

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var rgba = new byte[info.BytesSize];
        unsafe
        {
            fixed (byte* p = rgba)
            {
                var result = codec.GetPixels(info, (nint)p);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                    throw new InvalidDataException($"The image could not be decoded ({result}).");
            }
        }
        var image = RasterImage.FromStraightRgba(size.Width, size.Height, rgba);
        return ApplyOrientation(image, codec.EncodedOrigin);
    }

    /// <summary>Phone photos store their rotation separately (EXIF); show them the right way up.</summary>
    private static RasterImage ApplyOrientation(RasterImage image, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft) return image;
        int w = image.Width, h = image.Height;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = swap ? new RasterImage(h, w) : new RasterImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (nx, ny) = origin switch
                {
                    SKEncodedOrigin.TopRight => (w - 1 - x, y),
                    SKEncodedOrigin.BottomRight => (w - 1 - x, h - 1 - y),
                    SKEncodedOrigin.BottomLeft => (x, h - 1 - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (h - 1 - y, x),
                    SKEncodedOrigin.RightBottom => (h - 1 - y, w - 1 - x),
                    SKEncodedOrigin.LeftBottom => (y, w - 1 - x),
                    _ => (x, y),
                };
                result[nx, ny] = image[x, y];
            }
        return result;
    }

    // ------------------------------------------------------------------ save

    /// <summary>Saves a layered DoodleNook project.</summary>
    public static void SaveProject(Document doc, string path, string? appVersion) => DnkFile.Save(doc, path, appVersion);

    /// <summary>Exports the visible picture, flattened, as PNG, JPEG or WebP. JPEG has no transparency, so transparent
    /// areas are put on <paramref name="matte"/> (white by default).</summary>
    public static void Export(Document doc, string path, ExportFormat format, int quality = 92, ColorRgb? matte = null)
    {
        var flat = doc.Flatten();
        if (!SupportsTransparency(format)) flat = OnMatte(flat, matte ?? ColorRgb.White);
        WriteAtomically(path, s => Encode(flat, s, format, quality));
    }

    public static RasterImage OnMatte(RasterImage image, ColorRgb matte)
    {
        var result = new RasterImage(image.Width, image.Height);
        uint m = Pixel.Opaque(matte);
        var src = image.Pixels;
        var dst = result.Pixels;
        for (int i = 0; i < src.Length; i++) dst[i] = Pixel.Over(src[i], m);
        return result;
    }

    public static void Encode(RasterImage image, Stream stream, ExportFormat format = ExportFormat.Png, int quality = 92)
    {
        var rgba = image.ToStraightRgba();
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap();
        var skFormat = format switch
        {
            ExportFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ExportFormat.WebP => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };
        unsafe
        {
            fixed (byte* p = rgba)
            {
                if (!bitmap.InstallPixels(info, (nint)p, info.RowBytes)) throw new IOException("Could not prepare the image for saving.");
                using var data = bitmap.Encode(skFormat, Math.Clamp(quality, 1, 100))
                    ?? throw new IOException($"{DisplayName(format)} encoding failed.");
                data.SaveTo(stream);
            }
        }
    }

    public static void WriteAtomically(string path, Action<Stream> write)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
