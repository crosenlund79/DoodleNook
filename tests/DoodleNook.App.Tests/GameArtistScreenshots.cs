using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.References;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 015 screenshots from the real window (out/screenshots/m015-*.png), at a 1440 × 900 desktop and the
/// Kamvas 13 at 150 %.</summary>
public sealed class GameArtistScreenshots : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-m015shots-" + Guid.NewGuid().ToString("N"));

    public GameArtistScreenshots() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    internal static readonly (string Name, double Width, double Height, double Scaling)[] Sizes =
        [("desktop", 1440, 900, 1.0), ("kamvas13-150", 1280, 720, 1.5)];

    internal static void Save(MainWindow w, string name)
    {
        Pump();
        w.CaptureRenderedFrame()?.Dispose();
        Pump();
        using var frame = w.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(Screenshots.OutDir, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static Task Until(Func<bool> done, int ms = 10_000) => ArtistsCompanionWindowTests.Until(done, ms);

    /// <summary>The library's walk, drawn as eight 64 × 64 pixel-art frames on a 4 × 2 sheet (DoodleNook's own figure).</summary>
    internal static RasterImage WalkSheet()
    {
        var walk = ReferenceLibrary.Find("walk")!;
        var framing = new ReferenceFraming(ReferenceView.Side, ReferenceFacing.Right, 54, 32, 60, HardEdges: true, Marker: false);
        var frames = Enumerable.Range(0, walk.Frames).Select(d => new ExportFrame(ReferenceRenderer.Render(walk.Pose(d), 64, 64, framing), 100, d)).ToList();
        return AnimationExport.BuildSheet(frames, new SpriteSheetOptions(SheetLayout.Grid, 4)).Image;
    }

    [AvaloniaFact]
    public async Task Capture_the_sprite_sheet_workshop()
    {
        var png = Path.Combine(_dir, "walk-sheet.png");
        File.WriteAllBytes(png, PngCodec.Encode(WalkSheet()));
        foreach (var (size, width, height, scaling) in Sizes)
        {
            var w = Open(width, height, WorkspaceKind.Animation);
            w.SetRenderScaling(scaling);
            w.Session.Document.MarkSaved();
            Pump();
            var import = w.ImportSpriteSheetFromAsync(png);
            await Until(() => w.Dialogs.IsOpen);
            await Until(() => Has(w, "SheetFrame8"));
            Click(w, SpriteSheetWindowTests.ButtonWithText(w, "Reference to redraw"));
            Save(w, $"m015-sprite-sheet-grid-{size}");
            w.Dialogs.Close("import");
            Assert.True(await import);
            w.ShowToast("");
            Pump();
            Save(w, $"m015-sprite-sheet-imported-{size}");
            w.Session.Document.MarkSaved();
            w.Close();
            Pump();
        }
    }

    [AvaloniaFact]
    public async Task Capture_the_palette_folder_import()
    {
        var folder = Path.Combine(_dir, "Game palettes");
        Directory.CreateDirectory(folder);
        PaletteFolderWindowTests.WriteSamplePalettes(folder);
        foreach (var (size, width, height, scaling) in Sizes)
        {
            var w = Open(width, height, WorkspaceKind.PixelArt);
            w.SetRenderScaling(scaling);
            Pump();
            var show = PaletteManager.ShowAsync(w);
            await Until(() => w.Dialogs.IsOpen);
            Pump();
            w.LastPaletteManager!.PrepareFolderImport(folder);
            Pump();
            Save(w, $"m015-palette-folder-found-{size}");
            await w.LastPaletteManager.RunFolderImportAsync();
            Save(w, $"m015-palette-folder-imported-{size}");
            w.Dialogs.Close("close");
            await show;
            w.Close();
            Pump();
        }
    }
}
