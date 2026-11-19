using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using SkiaSharp;

namespace DoodleNook.App.Files;

/// <summary>Opens and saves PNG files (8-bit RGBA with transparency), using SkiaSharp's codecs.</summary>
public static class PngFile
{
    /// <summary>Saves what the user sees (layer over background) as a PNG. Writes to a temporary file first, so a failed
    /// save never destroys an existing picture.</summary>
    public static void Save(Document document, string path)
    {
        var flat = document.Flatten();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Create(temp))
                Encode(flat, stream);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static void Encode(RasterImage image, Stream stream)
    {
        var rgba = image.ToStraightRgba();
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap();
        unsafe
        {
            fixed (byte* p = rgba)
            {
                if (!bitmap.InstallPixels(info, (nint)p, info.RowBytes)) throw new IOException("Could not prepare the image for saving.");
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("PNG encoding failed.");
                data.SaveTo(stream);
            }
        }
    }

    /// <summary>Reads a PNG (or any format Skia can decode) into a document. Throws <see cref="InvalidDataException"/> with a
    /// readable message when the file can't be used.</summary>
    public static Document Open(string path)
    {
        using var stream = File.OpenRead(path);
        return Open(stream);
    }

    public static Document Open(Stream stream)
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
        // Opened pictures keep their own transparency: the layer is the image, with no extra background.
        return new Document(RasterImage.FromStraightRgba(size.Width, size.Height, rgba), background: null);
    }
}
