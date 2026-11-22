using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using DoodleNook.App.Controls;
using DoodleNook.App.Files;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Animation;

/// <summary>
/// Mission 015 — what is shown behind transparent areas of the canvas and of the animation preview: the checkerboard,
/// white, black, a colour of your choice, or a picture (a game background, say). Display only: the picture never
/// changes, and nothing of it is exported.
/// </summary>
public static class Backdrops
{
    private static readonly Dictionary<string, (DateTime Written, Bitmap? Bitmap, RasterImage? Pixels)> Pictures = new(StringComparer.OrdinalIgnoreCase);

    public static string Describe(BackdropSetting b) => b.Kind switch
    {
        BackdropKind.White => "White",
        BackdropKind.Black => "Black",
        BackdropKind.Colour => $"Colour {b.Colour}",
        BackdropKind.Picture => b.Picture is { } p ? $"Picture: {Path.GetFileName(p)}" : "Picture",
        _ => "Checkerboard",
    };

    public static ColorRgb? SolidColour(BackdropSetting b) => b.Kind switch
    {
        BackdropKind.White => ColorRgb.White,
        BackdropKind.Black => new ColorRgb(0, 0, 0),
        BackdropKind.Colour when b.Colour is { } hex && ColorRgb.TryParse(hex, out var c) => c,
        _ => null,
    };

    /// <summary>The picture's pixels (cached by file time); null when there is none or it can't be read.</summary>
    public static RasterImage? PicturePixels(BackdropSetting b) => b.Kind == BackdropKind.Picture && b.Picture is { } path ? Load(path).Pixels : null;

    private static (Bitmap? Bitmap, RasterImage? Pixels) Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return (null, null);
            var written = File.GetLastWriteTimeUtc(path);
            if (Pictures.TryGetValue(path, out var cached) && cached.Written == written) return (cached.Bitmap, cached.Pixels);
            var pixels = ImageFiles.Decode(path);
            // Keep a screen-sized copy at most: it only sits behind the picture.
            int max = Math.Max(pixels.Width, pixels.Height);
            if (max > 2048)
            {
                double s = 2048.0 / max;
                pixels = Engine.Operations.Resampler.Resize(pixels, Math.Max(1, (int)(pixels.Width * s)), Math.Max(1, (int)(pixels.Height * s)), Engine.Operations.Interpolation.Bilinear);
            }
            var bmp = BrushPreview.ToBitmap(pixels);
            if (Pictures.Count > 8) Pictures.Clear();
            Pictures[path] = (written, bmp, pixels);
            return (bmp, pixels);
        }
        catch (Exception)
        {
            return (null, null); // unreadable: the checkerboard is shown instead
        }
    }

    /// <summary>The brush to paint behind a picture with.</summary>
    public static IBrush BrushFor(BackdropSetting b)
    {
        if (SolidColour(b) is { } c) return UiKit.Brush(c);
        if (b.Kind == BackdropKind.Picture && b.Picture is { } path && Load(path).Bitmap is { } bmp)
            return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        return UiKit.CheckerBrush();
    }

    /// <summary>The choices, as menu entries; <paramref name="set"/> receives the new setting.</summary>
    public static IEnumerable<MenuEntry> Menu(MainWindow app, BackdropSetting current, Action<BackdropSetting> set)
    {
        yield return new(null, "Checkerboard", null, () => set(new BackdropSetting()), Checked: current.Kind == BackdropKind.Checker);
        yield return new(null, "White", null, () => set(new BackdropSetting { Kind = BackdropKind.White }), Checked: current.Kind == BackdropKind.White);
        yield return new(null, "Black", null, () => set(new BackdropSetting { Kind = BackdropKind.Black }), Checked: current.Kind == BackdropKind.Black);
        yield return new("palette", current.Kind == BackdropKind.Colour ? $"Colour {current.Colour}…" : "A colour…", null, async () =>
        {
            var start = SolidColour(current) ?? app.Session.Foreground;
            if (await app.Dialogs.AskColourAsync("Background colour", start, "Use") is { } c)
                set(new BackdropSetting { Kind = BackdropKind.Colour, Colour = c.ToHex() });
        }, Checked: current.Kind == BackdropKind.Colour);
        yield return new("image", current.Kind == BackdropKind.Picture && current.Picture is { } p ? $"Picture: {Path.GetFileName(p)}…" : "A picture…", null, async () =>
        {
            var files = await app.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "A picture to show behind the drawing (it is only read, and never exported)",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] }],
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) set(new BackdropSetting { Kind = BackdropKind.Picture, Picture = path });
        }, Checked: current.Kind == BackdropKind.Picture);
    }
}
