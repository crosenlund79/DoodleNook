using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Files;

/// <summary>
/// What another program left on the system clipboard, as raw data: an encoded picture (PNG, the format that keeps
/// transparency), a Windows bitmap (CF_DIB / CF_DIBV5), copied files (Explorer's file list), whether anything else is
/// there (text, for instance), or why the clipboard couldn't be read. Reading is quick; decoding happens separately.
/// </summary>
public sealed record ClipboardContent(byte[]? Encoded, byte[]? Dib, IReadOnlyList<string> Files, bool HasOther, string? Problem = null)
{
    public static readonly ClipboardContent Empty = new(null, null, [], false);

    public static ClipboardContent Failed(string problem) => new(null, null, [], false, problem);

    public bool MayHavePicture => Encoded is not null || Dib is not null || Files.Count > 0;
}

/// <summary>The system clipboard as DoodleNook reads pictures from it. Tests replace it with a fake.</summary>
public interface ISystemClipboard
{
    /// <summary>A number that changes whenever any program puts something new on the clipboard; null when the platform
    /// can't tell. DoodleNook's own Copy doesn't change the system clipboard, so an unchanged number means DoodleNook's
    /// copy is the newest thing copied.</summary>
    uint? ChangeCount { get; }

    Task<ClipboardContent> ReadAsync();
}

/// <summary>Mission 012: turns what was on the clipboard into a picture, or a short reason why not.</summary>
public static class ClipboardPictures
{
    /// <summary>The picture, preferring the format that keeps the most: PNG first (transparency), then a copied image
    /// file, then the Windows bitmap. Never throws.</summary>
    public static (RasterImage? Image, string? Problem) Decode(ClipboardContent content)
    {
        if (content.Problem is not null) return (null, content.Problem);
        string? problem = null;
        if (content.Encoded is { } encoded)
        {
            if (PngSize(encoded) is var (w, h) && !RasterImage.IsSupportedSize(w, h))
                return (null, $"The picture on the clipboard is {w} × {h} pixels, which is larger than DoodleNook supports.");
            try
            {
                using var stream = new MemoryStream(encoded, writable: false);
                return (ImageFiles.Decode(stream), null);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException)
            {
                problem = "The picture on the clipboard couldn't be read.";
            }
        }
        foreach (var file in content.Files)
        {
            if (!ImageFiles.ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            {
                problem ??= content.Files.Count == 1 ? $"“{Path.GetFileName(file)}” isn't a picture DoodleNook can paste." : "None of the copied files is a picture DoodleNook can paste.";
                continue;
            }
            try
            {
                return (ImageFiles.Decode(file), null);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                problem = $"“{Path.GetFileName(file)}” couldn't be read: {ex.Message}";
            }
        }
        if (content.Dib is { } dib)
        {
            try
            {
                return (DibDecoder.Decode(dib), null);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            {
                problem = ex.Message;
            }
        }
        return (null, problem ?? (content.HasOther ? "The clipboard has no picture. Copy an image and try again." : "The clipboard is empty. Copy a picture first."));
    }

    /// <summary>The width and height in a PNG's header (null when the data isn't a PNG).</summary>
    private static (int Width, int Height)? PngSize(byte[] data)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 24 || !data.AsSpan(0, 8).SequenceEqual(signature)) return null;
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20)));
    }

    /// <summary>Explorer's copied files (CF_HDROP): a DROPFILES header, then the paths, each ending in a zero, and a
    /// final zero.</summary>
    public static List<string> ParseFileDrop(ReadOnlySpan<byte> data)
    {
        var files = new List<string>();
        if (data.Length < 20) return files;
        int offset = BitConverter.ToInt32(data[..4]);
        bool wide = BitConverter.ToInt32(data.Slice(16, 4)) != 0;
        if (offset < 20 || offset >= data.Length) return files;
        var rest = data[offset..];
        string text = wide ? Encoding.Unicode.GetString(rest[..(rest.Length & ~1)]) : Encoding.Default.GetString(rest);
        foreach (var part in text.Split('\0'))
        {
            if (part.Length == 0) break;
            files.Add(part);
        }
        return files;
    }

    /// <summary>The platform's clipboard reader: Windows' own clipboard formats on Windows (exact control over bitmap
    /// transparency), Avalonia's clipboard elsewhere.</summary>
    public static ISystemClipboard ForWindow(TopLevel window) =>
        OperatingSystem.IsWindows() ? new Platform.WindowsClipboard() : new AvaloniaClipboard(window);
}

/// <summary>Reads pictures and files through Avalonia's clipboard (non-Windows platforms).</summary>
internal sealed class AvaloniaClipboard(TopLevel window) : ISystemClipboard
{
    public uint? ChangeCount => null;

    public async Task<ClipboardContent> ReadAsync()
    {
        if (window.Clipboard is not { } clipboard) return ClipboardContent.Empty;
        try
        {
            var formats = await clipboard.GetDataFormatsAsync();
            var files = (await clipboard.TryGetFilesAsync() ?? []).Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            byte[]? png = null;
            if (files.Count == 0 && await clipboard.TryGetBitmapAsync() is { } bitmap)
            {
                using var stream = new MemoryStream();
                bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                png = stream.ToArray();
                bitmap.Dispose();
            }
            return new ClipboardContent(png, null, files, formats.Count > 0);
        }
        catch (Exception ex)
        {
            return ClipboardContent.Failed($"The clipboard couldn't be read ({ex.GetType().Name}).");
        }
    }
}
