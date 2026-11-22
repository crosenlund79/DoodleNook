using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DoodleNook.App.Files;

namespace DoodleNook.App.Platform;

/// <summary>
/// Mission 012: reads pictures from the Windows clipboard directly, so transparency is handled exactly: the "PNG"
/// format (screenshot tools, browsers, Paint.NET, GIMP) first, then copied files (Explorer), then the device-independent
/// bitmaps every program provides (CF_DIBV5, CF_DIB; Windows makes one from the other). Another program may hold the
/// clipboard for a moment, so opening it is retried briefly. Nothing is written to the clipboard while reading.
/// <para>Mission 016: Copy and Cut also put the copied pixels on the clipboard for other programs, in the same formats
/// in the same order of preference: "PNG" (transparency kept: Paint.NET, GIMP, Krita, browsers, Office), CF_DIBV5 (32
/// bits with alpha) and CF_DIB (on white, for programs that ignore alpha and would show transparent areas black).</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsClipboard(nint owner = 0) : ISystemClipboard
{
    public const uint CfDib = 8, CfHdrop = 15, CfDibV5 = 17;
    private const long MaxBytes = 1_600_000_000; // beyond DoodleNook's largest picture either way

    public uint? ChangeCount => GetClipboardSequenceNumber();

    public async Task<string?> WriteImageAsync(DoodleNook.Engine.Imaging.RasterImage image)
    {
        (string?, uint, byte[])[] items;
        try
        {
            // Encoding a large picture takes a moment: off the UI thread, so drawing never waits for it.
            items = await Task.Run(() => new (string?, uint, byte[])[]
            {
                ("PNG", 0, DoodleNook.Engine.Files.PngCodec.Encode(image)),
                (null, CfDibV5, DoodleNook.Engine.Files.DibEncoder.EncodeV5(image)),
                (null, CfDib, DoodleNook.Engine.Files.DibEncoder.EncodeOnWhite(image)),
            });
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            return "The picture is too large for the clipboard.";
        }
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var result = TryPut(owner, items);
            if (result != PutResult.Busy) return result == PutResult.Done ? null : "Windows couldn't take the picture (out of memory?).";
            await Task.Delay(25);
        }
        return "The clipboard is busy in another program.";
    }

    public async Task<ClipboardContent> ReadAsync()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (TryRead(out var content)) return content;
            await Task.Delay(25);
        }
        return ClipboardContent.Failed("The clipboard is busy in another program. Try again in a moment.");
    }

    private static bool TryRead(out ClipboardContent content)
    {
        content = ClipboardContent.Empty;
        if (!OpenClipboard(0)) return false;
        try
        {
            byte[]? encoded = null, dib = null;
            List<string> files = [];
            foreach (var name in (ReadOnlySpan<string>)["PNG", "image/png"])
            {
                uint format = RegisterClipboardFormat(name);
                if (format != 0 && IsClipboardFormatAvailable(format) && (encoded = Read(format)) is not null) break;
            }
            if (IsClipboardFormatAvailable(CfHdrop) && Read(CfHdrop) is { } drop) files = ClipboardPictures.ParseFileDrop(drop);
            if (encoded is null && files.Count == 0)
            {
                if (IsClipboardFormatAvailable(CfDibV5)) dib = Read(CfDibV5);
                if (dib is null && IsClipboardFormatAvailable(CfDib)) dib = Read(CfDib);
            }
            content = new ClipboardContent(encoded, dib, files, CountClipboardFormats() > 0);
            return true;
        }
        catch (InsufficientMemoryException)
        {
            content = ClipboardContent.Failed("The picture on the clipboard is too large to paste.");
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>A copy of one clipboard format's bytes, or null when the owner didn't provide it.</summary>
    private static byte[]? Read(uint format)
    {
        nint handle = GetClipboardData(format);
        if (handle == 0) return null;
        long size = (long)GlobalSize(handle);
        if (size <= 0) return null;
        if (size > MaxBytes) throw new InsufficientMemoryException();
        nint p = GlobalLock(handle);
        if (p == 0) return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(p, bytes, 0, (int)size);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>Puts data on the clipboard in the given formats, replacing what was there (the smoke test uses this to
    /// copy like other programs do).</summary>
    internal static bool Put(params (string? Name, uint Format, byte[] Data)[] items)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var result = TryPut(0, items);
            if (result != PutResult.Busy) return result == PutResult.Done;
            Thread.Sleep(25);
        }
        return false;
    }

    private enum PutResult { Done, Busy, Failed }

    private static PutResult TryPut(nint owner, (string? Name, uint Format, byte[] Data)[] items)
    {
        if (!OpenClipboard(owner)) return PutResult.Busy;
        try
        {
            if (!EmptyClipboard()) return PutResult.Failed;
            foreach (var (name, fixedFormat, data) in items)
            {
                uint format = name is null ? fixedFormat : RegisterClipboardFormat(name);
                nint h = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (nuint)data.Length);
                if (h == 0) return PutResult.Failed;
                nint p = GlobalLock(h);
                if (p == 0)
                {
                    GlobalFree(h);
                    return PutResult.Failed;
                }
                Marshal.Copy(data, 0, p, data.Length);
                GlobalUnlock(h);
                if (SetClipboardData(format, h) == 0)
                {
                    GlobalFree(h);
                    return PutResult.Failed;
                }
            }
            return PutResult.Done;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>The formats on the clipboard right now, as raw bytes (the smoke test checks what Copy published).</summary>
    internal static Dictionary<string, byte[]> ReadFormats()
    {
        var found = new Dictionary<string, byte[]>();
        for (int attempt = 0; attempt < 20 && !OpenClipboard(0); attempt++) Thread.Sleep(25);
        try
        {
            foreach (var (name, format) in new[] { ("PNG", RegisterClipboardFormat("PNG")), ("CF_DIBV5", CfDibV5), ("CF_DIB", CfDib) })
                if (format != 0 && IsClipboardFormatAvailable(format) && Read(format) is { } data) found[name] = data;
            return found;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll")]
    private static partial nint SetClipboardData(uint format, nint data);

    [LibraryImport("user32.dll")]
    private static partial int CountClipboardFormats();

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string name);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);
}
