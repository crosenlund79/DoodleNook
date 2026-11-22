using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 012: the Quick Start guide's pictures (docs/screenshots/quick-start), captured from the real app
/// (headless Skia rendering, the same code and styles as on Windows) with DoodleNook's own sample art. They are written to
/// tests/DoodleNook.App.Tests/out/screenshots/quick-start and copied into docs by hand after a look.
/// </summary>
public sealed class QuickStartScreenshots
{
    private const double Width = 1440, Height = 900;

    private static string Dir
    {
        get
        {
            var d = Path.Combine(Screenshots.OutDir, "quick-start");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static void Save(MainWindow w, string name)
    {
        Pump();
        using var frame = w.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(Dir, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static MainWindow Open(WorkspaceKind kind)
    {
        var w = WindowTests.Open(Width, Height, kind);
        w.SetRenderScaling(1);
        Pump();
        return w;
    }

    [AvaloniaFact]
    public void Capture_draw_workspaces_palettes_and_pixel_art()
    {
        // 1. Quick Sketch, ready to draw.
        var w = Open(WorkspaceKind.QuickSketch);
        FinishingTouchesScreenshots.PaintScene(w);
        w.ShowToast("");
        Save(w, "01-draw");
        FinishingTouchesScreenshots.CloseNow(w);

        // 2. Painting with its layers.
        w = WorkshopScreenshots.Painting(Width, Height, 1);
        Save(w, "02-workspaces");

        // 3. The Palettes window showing a bundled palette's credit.
        var shown = w.OpenPalettesAsync();
        Pump();
        w.LastPaletteManager!.Select(CuratedPalettes.All.Single(p => p.Name == "Resurrect 64").Id);
        Save(w, "03-palettes");
        w.Dialogs.Close("close");
        _ = shown;
        FinishingTouchesScreenshots.CloseNow(w);

        // 4. Pixel Art: the mug sprite remapped to the PICO-8 palette and locked to it.
        w = Open(WorkspaceKind.PixelArt);
        Screenshots.DrawSprite(w);
        var pico = CuratedPalettes.All.Single(p => p.Name == "PICO-8 palette");
        PaletteRemap.Analyse(w.Session.Document, pico.Colors).Apply(w.Session.Document, pico.Name, lockAfter: true);
        w.Session.Document.Palette.LibraryId = pico.Id;
        w.Session.SetActivePalette(pico.Id);
        w.Session.SetForeground(pico.Colors[12]);
        w.Session.SelectTool(Editor.ToolKind.PixelPencil);
        w.Canvas.FitToView();
        w.ShowToast("");
        Save(w, "04-pixel-art");
        FinishingTouchesScreenshots.CloseNow(w);
    }

    [AvaloniaFact]
    public async Task Capture_learn_by_doing()
    {
        // 9. The first guided tutorial: a ball drawn, the guide pointing at New frame.
        var w = Open(WorkspaceKind.Painting);
        w.Session.Document.MarkSaved();
        w.Tutorials.Start(Tutorials.FirstAnimationTutorial.Create());
        w.Tutorials.Next();
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, Find<Button>(w, "TutorialAction"));
        await ArtistsCompanionWindowTests.Until(() => w.Tutorials.Index == 1);
        w.Session.SelectTool(Editor.ToolKind.Brush);
        w.Session.SetSize(10);
        w.Session.SetForeground(ColorRgb.Parse("#C96F4A"));
        var ball = Enumerable.Range(0, 30).Select(i => (128 + 26 * Math.Cos(i * Math.PI / 14), 52 + 26 * Math.Sin(i * Math.PI / 14))).ToArray();
        Drag(w, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None, ball);
        await ArtistsCompanionWindowTests.Until(() => w.Tutorials.Index == 2);
        w.ShowToast("");
        Save(w, "09-learn-by-doing");
        w.Tutorials.Exit();
        FinishingTouchesScreenshots.CloseNow(w);
    }

    [AvaloniaFact]
    public async Task Capture_animate_import_and_reference()
    {
        // 5. Animation Studio with the bouncing ball sample, onion skin on.
        var w = Open(WorkspaceKind.Animation);
        await w.OpenSampleAsync(SampleAnimation.BouncingBall);
        w.Session.GoToFrame(3);
        if (!w.Session.Onion.Enabled) w.Session.ToggleOnion();
        w.ShowToast("");
        Save(w, "05-animate");

        // 6. The Import animation window, for a GIF exported from that sample.
        var folder = Path.Combine(Path.GetTempPath(), $"dn-guide-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var gif = Path.Combine(folder, "Bouncing ball.gif");
        try
        {
            using (var fs = File.Create(gif))
                GifEncoder.Write(AnimationExport.RenderFrames(w.Session.Document, includePaper: true), fs, new GifOptions());
            w.Session.Document.MarkSaved();
            var import = w.ImportAnimationFromAsync([gif]);
            await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
            w.CaptureRenderedFrame()?.Dispose();
            Click(w, Find<Button>(w, "ImportFrame2"));
            Save(w, "06-import");
            w.Dialogs.Close("import");
            Assert.True(await import);
        }
        finally
        {
            Directory.Delete(folder, true);
        }

        // 7. The reference cycle library: a walk chosen and placed over the imported animation.
        {
            w.Session.GoToFrame(0);
            var pick = w.InsertReferenceCycleAsync();
            await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
            w.CaptureRenderedFrame()?.Dispose();
            Click(w, Find<Button>(w, "ReferenceCycle_walk"));
            await Task.Delay(250); // let the preview play a couple of drawings
            w.ShowToast("");
            Save(w, "07-reference-cycle");
            w.Dialogs.Close("cancel");
            Assert.False(await pick);
        }

        // 8. A picture copied elsewhere, pasted as a reference and being placed (handles showing).
        var doc = w.Session.Document;
        var picture = new RasterImage(doc.Width / 2, doc.Height / 2);
        for (int y = 0; y < picture.Height; y++)
            for (int x = 0; x < picture.Width; x++)
            {
                double dx = x - picture.Width * 0.5, dy = y - picture.Height * 0.55;
                bool ball = dx * dx + dy * dy < Math.Pow(picture.Height * 0.3, 2);
                picture[x, y] = ball ? Pixel.Opaque(ColorRgb.Parse("#C96F4A")) : y > picture.Height * 0.85 ? Pixel.Opaque(ColorRgb.Parse("#5B8C51")) : 0;
            }
        w.SystemClipboard = new ClipboardWindowTests.FakeClipboard { Content = new ClipboardContent(PngCodec.Encode(picture), null, [], true), ChangeCount = 7 };
        w.Session.GoToFrame(4);
        Assert.True(await w.PasteAsync(asReference: true));
        Save(w, "08-paste-reference");
        FinishingTouchesScreenshots.CloseNow(w);
    }

    [AvaloniaFact]
    public async Task Capture_sprite_sheets_and_storybook()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dn-quickstart-15-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // 10. A sprite sheet with its grid, about to be imported.
            var w = Open(WorkspaceKind.Animation);
            w.Session.Document.MarkSaved();
            var png = Path.Combine(folder, "walk-sheet.png");
            File.WriteAllBytes(png, PngCodec.Encode(GameArtistScreenshots.WalkSheet()));
            var import = w.ImportSpriteSheetFromAsync(png);
            await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
            await ArtistsCompanionWindowTests.Until(() => Has(w, "SheetFrame8"));
            Save(w, "10-sprite-sheet");
            w.Dialogs.Close("cancel");
            await import;

            // 11. Storybook Studio with the sample's first page.
            await w.OpenSampleStorybookAsync(Path.Combine(folder, "story"));
            var view = w.StorybookView!;
            await ArtistsCompanionWindowTests.Until(() => w.Storybook!.Usage().Keys.All(k => view.Thumbnails.Get(k, true) is { Large: true }), 20_000);
            view.Select(0);
            w.ShowToast("");
            Save(w, "11-storybook");
            w.CloseStorybook();
            FinishingTouchesScreenshots.CloseNow(w);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
