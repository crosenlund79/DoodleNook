using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DoodleNook.App.Files;

namespace DoodleNook.App.Platform;

/// <summary>
/// Mission 012: reads pictures from the Windows clipboard directly, so transparency is handled exactly: the "PNG"
/// format (screenshot tools, browsers, Paint.NET, GIMP) first, then copied files (Explorer), then the device-independent
/// bitmaps every program provides (CF_DIBV5, CF_DIB; Windows makes one from the other). Another program may hold the
/// clipboard for a moment, so opening it is retried briefly. Nothing is written to the clipboard while reading.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsClipboard : ISystemClipboard
{
    public const uint CfDib = 8, CfHdrop = 15, CfDibV5 = 17;
    private const long MaxBytes = 1_600_000_000; // beyond DoodleNook's largest picture either way

    public uint? ChangeCount => GetClipboardSequenceNumber();

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
        for (int attempt = 0; attempt < 20 && !OpenClipboard(0); attempt++) Thread.Sleep(25);
        try
        {
            if (!EmptyClipboard()) return false;
            foreach (var (name, fixedFormat, data) in items)
            {
                uint format = name is null ? fixedFormat : RegisterClipboardFormat(name);
                nint h = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (nuint)data.Length);
                if (h == 0) return false;
                nint p = GlobalLock(h);
                Marshal.Copy(data, 0, p, data.Length);
                GlobalUnlock(h);
                if (SetClipboardData(format, h) == 0)
                {
                    GlobalFree(h);
                    return false;
                }
            }
            return true;
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
