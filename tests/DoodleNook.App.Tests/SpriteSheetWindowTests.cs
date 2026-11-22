using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 015 — the Sprite Sheet Workshop in the real window: import a sheet (as an animation or a reference
/// to redraw), and make a sheet from numbered pictures.</summary>
public sealed class SpriteSheetWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-sheetwin-" + Guid.NewGuid().ToString("N"));

    public SpriteSheetWindowTests() => Directory.CreateDirectory(_dir);

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

    private static Task Until(Func<bool> done, int ms = 10_000) => ArtistsCompanionWindowTests.Until(done, ms);

    internal static Button ButtonWithText(Window w, string text) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

    private static readonly ColorRgb Ink = new(40, 30, 60), Leaf = new(90, 160, 70);

    /// <summary>A 6-frame 24 × 20 animation: a body that moves and a soft (half-transparent) mark.</summary>
    internal static Document Walk()
    {
        var doc = Document.NewAnimation(24, 20, null, frames: 1, fps: 8);
        var pencil = new PixelStroke(doc);
        for (int f = 0; f < 6; f++)
        {
            if (f > 0) doc.NewFrame();
            pencil.Begin(new PixelStrokeStyle(Leaf, false, 6, false, Symmetry.None, false, "Pixel"), 6 + 2 * f, 10);
            pencil.End();
            pencil.Begin(new PixelStrokeStyle(Ink, false, 1, false, Symmetry.None, false, "Pixel"), 3 + f, 3);
            pencil.End();
        }
        doc.SetLayerProperties(doc.Layers[0], doc.Layers[0].Properties with { Opacity = 0.8 });
        return doc;
    }

    [AvaloniaFact]
    public async Task An_exported_sheet_imports_back_to_the_same_frames()
    {
        var original = Walk();
        var frames = AnimationExport.RenderFrames(original, includePaper: false);
        var sheet = AnimationExport.BuildSheet(frames, new SpriteSheetOptions(SheetLayout.Grid, 4, Spacing: 2, Padding: 1, Margin: 3));
        var png = Path.Combine(_dir, "walk.png");
        File.WriteAllBytes(png, PngCodec.Encode(sheet.Image));
        File.WriteAllText(Path.Combine(_dir, "walk.json"), AnimationExport.SheetJson(sheet, "walk.png", 8, true));
        var before = File.ReadAllBytes(png);

        var w = Open(1280, 800, WorkspaceKind.Painting);
        w.Session.Document.MarkSaved();
        var import = w.ImportSpriteSheetFromAsync(png);
        await Until(() => w.Dialogs.IsOpen);
        Assert.True(Has(w, "SpriteSheetDialog"));
        Assert.Contains("walk.json", string.Join(" ", w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        Assert.Equal("24", Find<TextBox>(w, "SheetFrameWidth").Text);
        Assert.Equal("4", Find<TextBox>(w, "SheetMarginLeft").Text); // margin 3 + padding 1
        Assert.Equal("4", Find<TextBox>(w, "SheetSpacingX").Text);   // spacing 2 + 2 × padding 1
        Assert.StartsWith("6 of 8 frames · 4 × 2 grid", Find<TextBlock>(w, "SheetCount").Text);
        w.Dialogs.Close("import");
        Assert.True(await import);
        Pump();

        var doc = w.Session.Document;
        Assert.Equal(WorkspaceKind.Animation, w.Workspace.Kind);
        Assert.Equal(6, doc.FrameCount);
        Assert.Single(doc.Layers);
        Assert.True(w.AnimationState.PixelAnimation);
        for (int f = 0; f < 6; f++)
            Assert.Equal(PngCodec.Decode(PngCodec.Encode(frames[f].Image)).Pixels, doc.RenderFrame(f, includePaper: false).Pixels);
        Assert.Equal(before, File.ReadAllBytes(png)); // the sheet is only read
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_sheet_without_json_is_guessed_adjusted_and_imported_as_a_reference_to_redraw()
    {
        // 4 × 2 cells of 32 × 32, the last two empty; nothing tells the size but the clear lines between the sprites.
        var sheet = new RasterImage(128, 64);
        for (int i = 0; i < 6; i++)
            for (int y = 8; y < 26; y++)
                for (int x = 8 + i; x < 22 + i; x++) sheet[i % 4 * 32 + x, i / 4 * 32 + y] = Pixel.Opaque(Leaf);
        var png = Path.Combine(_dir, "slime.png");
        File.WriteAllBytes(png, PngCodec.Encode(sheet));

        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.Session.Document.MarkSaved();
        var import = w.ImportSpriteSheetFromAsync(png);
        await Until(() => w.Dialogs.IsOpen);
        Assert.Equal("32", Find<TextBox>(w, "SheetFrameWidth").Text);
        Assert.Equal("32", Find<TextBox>(w, "SheetFrameHeight").Text);
        Assert.Equal("6", Find<TextBox>(w, "SheetHowMany").Text); // the unused cells at the end are not counted
        Pump();
        Assert.True(Has(w, "SheetFrame6"));
        Assert.False(Has(w, "SheetFrame7"));

        // A wrong size is explained, not imported.
        Find<TextBox>(w, "SheetFrameWidth").Text = "200";
        Pump();
        Assert.Contains("wider than the sheet (128 px)", Find<TextBlock>(w, "SheetProblem").Text);
        // "Split into 4 × 2" gives the size back.
        Find<TextBox>(w, "SheetColumns").Text = "4";
        Pump();
        Assert.Equal("32", Find<TextBox>(w, "SheetFrameWidth").Text);
        Assert.False(Has(w, "SheetProblem")); // the problem is gone
        await Until(() => Has(w, "SheetFrame8"));
        // The two empty cells start left out; leave frame 2 out as well.
        Assert.StartsWith("6 of 8 frames", Find<TextBlock>(w, "SheetCount").Text);
        Click(w, Find<Button>(w, "SheetFrame2"));
        Assert.StartsWith("5 of 8 frames", Find<TextBlock>(w, "SheetCount").Text);
        Click(w, ButtonWithText(w, "Reference to redraw"));
        w.Dialogs.Close("import");
        Assert.True(await import);
        Pump();

        var doc = w.Session.Document;
        Assert.Equal(new[] { "Reference", "Sketch", "Outline", "Coloring" }, doc.Layers.Select(l => l.Name));
        Assert.True(doc.Layers[0].Locked);
        Assert.Equal(0.35, doc.Layers[0].Opacity, 3);
        Assert.Equal("Sketch", doc.ActiveLayer.Name);
        Assert.Equal(5, doc.FrameCount);
        Assert.Equal(sheet.Crop(new PixelRect(64, 0, 32, 32)).Pixels, doc.CelAt(doc.Layers[0], 1)!.Image.Pixels); // frame 3 of the sheet
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Numbered_pictures_of_different_sizes_become_one_sheet_and_stay_untouched()
    {
        var sizes = new[] { (16, 20), (18, 24), (16, 20), (12, 12) };
        var paths = new List<string>();
        for (int i = 0; i < sizes.Length; i++)
        {
            var p = new RasterImage(sizes[i].Item1, sizes[i].Item2);
            p.Fill(Pixel.Opaque(i % 2 == 0 ? Leaf : Ink));
            var path = Path.Combine(_dir, $"run_{i + 1}.png");
            File.WriteAllBytes(path, PngCodec.Encode(p));
            paths.Add(path);
        }
        File.WriteAllBytes(Path.Combine(_dir, "other.png"), PngCodec.Encode(new RasterImage(4, 4))); // not part of the sequence
        var before = paths.Select(File.ReadAllBytes).ToList();

        var w = Open(1280, 800, WorkspaceKind.Painting);
        var save = Path.Combine(_dir, "run-sheet.png");
        var make = w.SpriteSheetFromPicturesAsync([paths[2]], save); // one is enough: the sequence is found
        await Until(() => w.Dialogs.IsOpen);
        Assert.True(Has(w, "PicturesToSheetDialog"));
        Assert.True(Has(w, "Picture4"));
        Assert.False(Has(w, "Picture5"));
        Assert.Equal("4 frames of 18 × 24 in 4 × 1 → 72 × 24 px", Find<TextBlock>(w, "PicturesSheetSize").Text);
        Find<TextBox>(w, "PicturesColumns").Text = "2";
        Find<TextBox>(w, "PicturesSpacing").Text = "1";
        Pump();
        Assert.Equal("4 frames of 18 × 24 in 2 × 2 → 37 × 49 px", Find<TextBlock>(w, "PicturesSheetSize").Text);
        w.Dialogs.Close("save");
        Assert.Equal(save, await make);

        var result = PngCodec.Decode(File.ReadAllBytes(save));
        Assert.Equal((37, 49), (result.Width, result.Height));
        // Picture 1 (16 × 20) sits at the bottom centre of its 18 × 24 cell; nothing is scaled.
        Assert.Equal(0u, result[0, 23]);
        Assert.Equal(Pixel.Opaque(Leaf), result[1, 4]);
        Assert.Equal(Pixel.Opaque(Leaf), result[16, 23]);
        Assert.Equal(0u, result[1, 3]);
        // Picture 4 (12 × 12) in the second row, second column: x 19 + 3, y 25 + 12.
        Assert.Equal(Pixel.Opaque(Ink), result[22, 37]);
        Assert.Equal(0u, result[22, 36]);
        Assert.Equal(before, paths.Select(File.ReadAllBytes).ToList());
        w.Close();
    }
}
