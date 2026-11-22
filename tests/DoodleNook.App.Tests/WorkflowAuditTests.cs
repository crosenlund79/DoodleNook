using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using DoodleNook.Engine.References;
using DoodleNook.Engine.Storybook;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 016 — the workflow audit. Each of the owner's workflows (A–H) is driven through the real window the way it is
/// used, end to end and across the features it touches, where defects tend to hide: draw and export, an animation taken
/// apart and put back together, animation references, Game Edit Mode, sprite sheets, Storybook, the clipboard and
/// personal palettes.
/// </summary>
public sealed class WorkflowAuditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-audit-" + Guid.NewGuid().ToString("N"));

    public WorkflowAuditTests() => Directory.CreateDirectory(_dir);

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

    private static Task Until(Func<bool> done, int ms = 15_000) => ArtistsCompanionWindowTests.Until(done, ms);

    /// <summary>Awaits a command, failing with the dialog's text if the command stops to ask something instead.</summary>
    private static async Task<T> Done<T>(MainWindow w, Task<T> task, string what)
    {
        await Until(() => task.IsCompleted || w.Dialogs.IsOpen);
        if (!task.IsCompleted)
            throw new Xunit.Sdk.XunitException($"{what} stopped at a dialog: " + string.Join(" | ",
                w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).Where(t => t is { Length: > 0 }).TakeLast(12)));
        return await task;
    }

    private static readonly ColorRgb Orange = new(0xE8, 0x9A, 0x5E), Slate = new(0x3C, 0x4B, 0x61);

    private static void Paint(MainWindow w, ColorRgb colour, int size, params (double X, double Y)[] docPoints)
    {
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new BrushSettings { Size = size, Hardness = 0.8, Opacity = 1 }, "audit");
        w.Session.SetForeground(colour);
        Drag(w, MouseButton.Left, RawInputModifiers.None, docPoints); // document coordinates
    }

    private static int Count(RasterImage img, ColorRgb c, int tolerance) =>
        img.Pixels.Count(p => Pixel.A(p) == 255 && Math.Abs(Pixel.ToStraight(p).R - c.R) <= tolerance && Math.Abs(Pixel.ToStraight(p).G - c.G) <= tolerance
            && Math.Abs(Pixel.ToStraight(p).B - c.B) <= tolerance);

    // ================================================================== A. draw, save, reopen, export

    [AvaloniaFact]
    public async Task A_draw_undo_save_reopen_and_export_keep_exactly_what_was_drawn()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        w.Session.ReplaceDocument(new Document(480, 320, ColorRgb.White, "Background"), null);
        w.Session.AddLayer();
        w.Canvas.FitToView();
        Pump();
        Paint(w, Orange, 24, (60, 160), (240, 120), (420, 200));
        var drawn = w.Session.Document.Flatten();
        Assert.True(Count(drawn, Orange, 3) > 1000);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(0, Count(w.Session.Document.Flatten(), Orange, 3));
        Key(w, Avalonia.Input.Key.Y, RawInputModifiers.Control);
        Assert.Equal(drawn.Pixels, w.Session.Document.Flatten().Pixels);

        var dnk = Path.Combine(_dir, "sunset.dnk");
        Assert.True(await w.SaveToPathAsync(dnk));
        Assert.False(w.Session.Document.IsDirty);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        w.ExportToPath(Path.Combine(_dir, "sunset.png"), ExportFormat.Png);
        w.ExportToPath(Path.Combine(_dir, "sunset.jpg"), ExportFormat.Jpeg);

        // Reopen (as after a restart): the same pixels, layers and file.
        w.Session.ReplaceDocument(new Document(100, 100, ColorRgb.White, "Other"), null);
        Assert.True(await w.OpenPathAsync(dnk));
        Assert.Equal(2, w.Session.Document.Layers.Count);
        Assert.Equal(drawn.Pixels, w.Session.Document.Flatten().Pixels);
        Assert.Equal(drawn.Pixels, PngCodec.DecodeAny(File.ReadAllBytes(Path.Combine(_dir, "sunset.png"))).Pixels);
        var jpg = ImageFiles.Decode(Path.Combine(_dir, "sunset.jpg"));
        Assert.True(Count(jpg, Orange, 24) > 800); // lossy, but the colour survives
        w.Close();
    }

    // ================================================================== B. an animation taken apart and put back

    [AvaloniaFact]
    public async Task B_an_exported_gif_and_sprite_sheet_import_back_with_their_frames_timing_and_colours()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        await w.OpenSampleAsync(SampleAnimation.BouncingBall);
        Pump();
        var doc = w.Session.Document;
        var rendered = AnimationExport.RenderFrames(doc, includePaper: false); // as the GIF export (no background)
        long total = rendered.Sum(f => (long)f.DurationMs);

        // GIF out (default settings: dithered, looping) and back in.
        var gif = Path.Combine(_dir, "ball.gif");
        await w.ExportAnimationToPathAsync(gif, new AnimationExportSettings(AnimationExportFormat.Gif));
        var import = w.ImportAnimationFromAsync([gif]);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("import");
        Assert.True(await import);
        var back = w.Session.Document;
        Assert.NotSame(doc, back);
        long backTotal = Enumerable.Range(0, back.FrameCount).Sum(f => (long)back.Animation!.DurationOf(f));
        Assert.InRange(backTotal, total - 10 * rendered.Count, total + 10 * rendered.Count); // GIF keeps hundredths
        // Every frame's colours survive (the Mission 016 GIF fix): no pixel far from the original.
        var reference = back.Layers.First(l => l.Name == "Reference");
        int worst = 0;
        long ms = 0;
        for (int f = 0; f < back.FrameCount; f++)
        {
            // The imported frame shows the original frame playing at this time.
            long middle = ms + back.Animation!.DurationOf(f) / 2;
            int src = 0;
            for (long t = 0; src < rendered.Count - 1 && t + rendered[src].DurationMs <= middle; src++) t += rendered[src].DurationMs;
            var cel = back.CelAt(reference, f)!.Image;
            for (int i = 0; i < cel.Pixels.Length; i += 7)
            {
                var a = Pixel.ToStraight(rendered[src].Image.Pixels[i]);
                var b = Pixel.ToStraight(cel.Pixels[i]);
                if (a.A < 128) // GIF transparency is all or nothing, at half
                {
                    Assert.Equal(0, b.A);
                    continue;
                }
                worst = Math.Max(worst, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
            }
            ms += back.Animation!.DurationOf(f);
        }
        Assert.True(worst <= 40, $"largest colour difference {worst}");

        // Sprite sheet with JSON out of the original and back in: exactly the same frames.
        w.Session.ReplaceDocument(doc, null);
        Pump();
        var sheet = Path.Combine(_dir, "ball_sheet.png");
        await w.ExportAnimationToPathAsync(sheet, new AnimationExportSettings(AnimationExportFormat.SpriteSheet, SheetLayout.Grid, Columns: 4, Spacing: 2, Json: true));
        var sliced = w.ImportSpriteSheetFromAsync(sheet);
        await Until(() => w.Dialogs.IsOpen);
        Assert.StartsWith($"{rendered.Count} of", Find<TextBlock>(w, "SheetCount").Text);
        w.Dialogs.Close("import");
        Assert.True(await sliced);
        var fromSheet = w.Session.Document;
        Assert.Equal(rendered.Count, fromSheet.FrameCount);
        for (int f = 0; f < fromSheet.FrameCount; f++) Assert.Equal(rendered[f].Image.Pixels, fromSheet.RenderFrame(f, includePaper: false).Pixels);
        fromSheet.MarkSaved();
        doc.MarkSaved();
        back.MarkSaved();
        w.Close();
    }

    // ================================================================== C. animation references

    [AvaloniaFact]
    public async Task C_a_reference_cycle_is_placed_adjusted_saved_reopened_and_left_out_of_exports_when_hidden()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        await w.OpenSampleAsync(SampleAnimation.BouncingBall);
        Pump();
        var doc = w.Session.Document;
        int layers = doc.Layers.Count;
        var insert = w.InsertReferenceCycleAsync();
        await Until(() => w.Dialogs.IsOpen && Has(w, "ReferenceCycleDialog"));
        var sit = Find<Button>(w, "ReferenceCycle_sit-down");
        sit.BringIntoView(); // further down the list: scrolled to, as a user would
        Pump();
        Click(w, sit);
        w.Dialogs.Close("ok");
        Assert.True(await insert);
        var layer = doc.Layers.Single(l => l.ReferenceCycle is not null);
        Assert.Equal("sit-down", layer.ReferenceCycle!.CycleId);
        Assert.Equal(layers + 1, doc.Layers.Count);

        doc.SetActiveLayer(layer);
        var adjust = w.AdjustReferenceCycleAsync();
        await Until(() => w.Dialogs.IsOpen && Has(w, "ReferenceCycleDialog"));
        var laugh = Find<Button>(w, "ReferenceCycle_laugh");
        laugh.BringIntoView();
        Pump();
        Click(w, laugh);
        w.Dialogs.Close("ok");
        Assert.True(await adjust);
        Assert.Equal("laugh", layer.ReferenceCycle!.CycleId);
        w.Undo();
        Assert.Equal("sit-down", doc.Layers.Single(l => l.ReferenceCycle is not null).ReferenceCycle!.CycleId);

        var path = Path.Combine(_dir, "ball with reference.dnk");
        Assert.True(await w.SaveToPathAsync(path));
        var reopened = DnkFile.Load(path);
        var kept = reopened.Layers.Single(l => l.ReferenceCycle is not null);
        Assert.Equal(("sit-down", true), (kept.ReferenceCycle!.CycleId, kept.Locked));

        // Exports show the reference while it is visible (Mission 013's rule) and leave it out once hidden.
        var withIt = AnimationExport.RenderFrames(doc, includePaper: false);
        var refLayer = doc.Layers.Single(l => l.ReferenceCycle is not null);
        w.Session.SetLayerProperty(refLayer, p => p with { Visible = false });
        var without = AnimationExport.RenderFrames(doc, includePaper: false);
        w.Session.SetLayerProperty(refLayer, p => p with { Visible = true });
        w.Session.SetLayerProperty(refLayer, p => p with { Visible = false });
        doc.SetActiveLayer(doc.Layers.First(l => l.ReferenceCycle is null && l.Visible));
        Assert.NotEqual(withIt[0].Image.Pixels, without[0].Image.Pixels);
        var gif = Path.Combine(_dir, "no reference.gif");
        await w.ExportAnimationToPathAsync(gif, new AnimationExportSettings(AnimationExportFormat.Gif, Dither: false));
        var decoded = GifDecoder.Decode(File.ReadAllBytes(gif));
        Assert.Equal(without.Count, decoded.Frames.Count + (without.Count - decoded.Frames.Count)); // identical frames may merge
        doc.MarkSaved();
        w.Close();
    }

    // ================================================================== D. Game Edit Mode

    [AvaloniaFact]
    public async Task D_a_game_image_is_edited_saved_with_a_backup_and_pixels_carried_to_another_image()
    {
        var game = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(game, "characters"));
        var hero = new RasterImage(32, 32);
        for (int y = 8; y < 30; y++)
            for (int x = 10; x < 22; x++) hero[x, y] = Pixel.Opaque(Slate);
        File.WriteAllBytes(Path.Combine(game, "characters", "hero.png"), PngCodec.Encode(hero));
        File.WriteAllBytes(Path.Combine(game, "characters", "villain.png"), PngCodec.Encode(new RasterImage(32, 32)));
        var original = File.ReadAllBytes(Path.Combine(game, "characters", "hero.png"));

        var w = Open(1280, 800, WorkspaceKind.PixelArt);
        var clip = new ClipboardWindowTests.FakeClipboard();
        w.SystemClipboard = clip;
        var opening = w.OpenGameFolderAsync(game);
        await Until(() => w.Dialogs.IsOpen); // the first-time backup question
        w.Dialogs.Close("start");
        Assert.True(await opening);
        var heroImage = w.Game!.Images.Single(i => i.FileName == "hero.png");
        Assert.True(await Done(w, w.OpenGameImageAsync(heroImage), "Opening the hero"));
        w.Canvas.FitToView();
        Pump();
        Paint(w, Orange, 3, (12, 4), (20, 4));
        Assert.True(await Done(w, w.SaveAsync(), "Saving the hero"));
        Assert.Equal(GameImageStatus.InProgress, heroImage.Status);
        Assert.Equal(original, File.ReadAllBytes(heroImage.BackupPath));
        var saved = ImageFiles.Decode(heroImage.PngPath);
        Assert.Equal(w.Session.Document.Flatten().Pixels, saved.Pixels);
        Assert.True(Count(saved, Orange, 2) > 5);

        // Copy the hero's body and paste it into the villain: in place, on a new layer, saved to the villain's PNG.
        w.Session.ApplySelection(SelectionMask.Rectangle(32, 32, new PixelRect(10, 8, 12, 22)), "Select");
        Key(w, Avalonia.Input.Key.C, RawInputModifiers.Control);
        await Until(() => clip.Written.Count == 1);
        Assert.Equal((12, 22), (clip.Written[0].Width, clip.Written[0].Height));
        var villain = w.Game.Images.Single(i => i.FileName == "villain.png");
        // Selecting is part of the picture's history, so the hero counts as changed: the usual question, answered.
        var toVillain = w.OpenGameImageAsync(villain);
        await Until(() => w.Dialogs.IsOpen || toVillain.IsCompleted);
        if (w.Dialogs.IsOpen) w.Dialogs.Close("discard");
        Assert.True(await toVillain);
        Key(w, Avalonia.Input.Key.V, RawInputModifiers.Control);
        await Until(() => w.Session.Document.Layers.Count == 2);
        Assert.True(await Done(w, w.SaveAsync(), "Saving the villain"));
        Assert.Equal(GameImageStatus.InProgress, villain.Status);
        Assert.Equal(Pixel.Opaque(Slate), ImageFiles.Decode(villain.PngPath)[15, 20]);
        w.Close();
    }

    // ================================================================== F. Storybook and the drawing underneath

    /// <summary>While the storybook is shown, the drawing underneath is out of sight: no shortcut may change it.</summary>
    [AvaloniaFact]
    public async Task F_shortcuts_pressed_in_the_storybook_never_change_the_hidden_drawing()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        w.Session.ReplaceDocument(new Document(300, 200, ColorRgb.White, "Background"), null);
        w.Session.AddLayer();
        w.Canvas.FitToView();
        Pump();
        Paint(w, Slate, 30, (40, 100), (260, 100));
        w.Copy(cut: false); // something on DoodleNook's clipboard to paste
        var doc = w.Session.Document;
        var before = doc.Flatten().Pixels.ToArray();
        int layers = doc.Layers.Count, undo = doc.History.UndoCount;
        var tool = w.Session.Tool;

        Assert.True(await w.CreateStorybookAsync(Path.Combine(_dir, "Keys.donkbook"), 210, 297, 150, StoryLayouts.Comic));
        Pump();
        Assert.True(w.StorybookShown);
        foreach (var (key, mods) in new[]
                 {
                     (Avalonia.Input.Key.Delete, RawInputModifiers.None), (Avalonia.Input.Key.Back, RawInputModifiers.None), (Avalonia.Input.Key.V, RawInputModifiers.Control),
                     (Avalonia.Input.Key.X, RawInputModifiers.Control), (Avalonia.Input.Key.A, RawInputModifiers.Control), (Avalonia.Input.Key.J, RawInputModifiers.Control),
                     (Avalonia.Input.Key.I, RawInputModifiers.Control), (Avalonia.Input.Key.E, RawInputModifiers.Control), (Avalonia.Input.Key.T, RawInputModifiers.Control),
                     (Avalonia.Input.Key.Back, RawInputModifiers.Alt), (Avalonia.Input.Key.Back, RawInputModifiers.Control), (Avalonia.Input.Key.B, RawInputModifiers.None),
                     (Avalonia.Input.Key.E, RawInputModifiers.None), (Avalonia.Input.Key.OemOpenBrackets, RawInputModifiers.None), (Avalonia.Input.Key.Left, RawInputModifiers.None),
                 })
        {
            Key(w, key, mods);
            Assert.True(w.StorybookShown, $"{mods}+{key} left the storybook");
        }
        Pump();
        Assert.Equal(layers, doc.Layers.Count);
        Assert.Equal(undo, doc.History.UndoCount);
        Assert.Equal(before, doc.Flatten().Pixels);
        Assert.Null(doc.Selection);
        Assert.Equal(tool, w.Session.Tool);

        // The menus: Edit › Undo is the storybook's; a drawing command goes back to the drawing first, where it is seen.
        var editUndo = w.MenuFor("Edit").First(e => e.Icon == "undo");
        Assert.Equal("Undo (storybook)", editUndo.Label);
        var newLayer = w.MenuFor("Layer").First(e => e.Run is not null && e.Label.StartsWith("New layer", StringComparison.Ordinal));
        newLayer.Run!();
        Pump();
        Assert.False(w.StorybookShown);
        Assert.Equal(layers + 1, doc.Layers.Count);
        w.Undo();
        Assert.Equal(layers, doc.Layers.Count);
        w.ShowStorybook();
        Pump();

        // Back to drawing: the shortcuts work again.
        Key(w, Avalonia.Input.Key.Escape);
        Assert.False(w.StorybookShown);
        Key(w, Avalonia.Input.Key.V, RawInputModifiers.Control);
        await Until(() => doc.Layers.Count == layers + 1);
        doc.MarkSaved();
        w.Close();
    }

    // ================================================================== G + H. clipboard between pictures, personal palettes

    [AvaloniaFact]
    public async Task G_copied_pixels_go_from_one_picture_to_another_and_out_to_other_programs()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var clip = new ClipboardWindowTests.FakeClipboard();
        w.SystemClipboard = clip;
        w.Session.ReplaceDocument(new Document(300, 200, null, "Art"), null);
        w.Canvas.FitToView();
        Pump();
        Paint(w, Orange, 20, (50, 100), (150, 100));
        Key(w, Avalonia.Input.Key.C, RawInputModifiers.Control);
        await Until(() => clip.Written.Count == 1);
        var published = clip.Written[0];
        Assert.True(published.Width < 300 && published.Height < 200); // just the stroke
        Assert.True(Count(published, Orange, 3) > 300);

        // Into another picture: DoodleNook's own copy, at the same place, on a new layer.
        var path = Path.Combine(_dir, "second.dnk");
        DnkFile.Save(new Document(300, 200, ColorRgb.White, "Background"), path);
        Assert.True(await w.OpenPathAsync(path));
        Key(w, Avalonia.Input.Key.V, RawInputModifiers.Control);
        await Until(() => w.Session.Document.Layers.Count == 2);
        Assert.Equal("Pasted", w.Session.Document.ActiveLayer.Name);
        Assert.True(Count(w.Session.Document.Flatten(), Orange, 3) > 300);
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task H_a_folder_of_palettes_is_imported_and_one_locks_a_pixel_art_picture()
    {
        PaletteFolderWindowTests.WriteSamplePalettes(_dir);
        var w = Open(1280, 800, WorkspaceKind.PixelArt);
        var show = PaletteManager.ShowAsync(w);
        await Until(() => w.Dialogs.IsOpen);
        var manager = w.LastPaletteManager!;
        manager.PrepareFolderImport(_dir);
        var summary = await manager.RunFolderImportAsync();
        Assert.Equal(3, summary!.ImportedCount);
        w.Dialogs.Close("use");
        await show;
        var dusk = w.Session.Library.Palettes.Single(p => p.Name == "Dusk Harbour");

        w.Session.ReplaceDocument(new Document(32, 32, null, "Sprite"), null);
        Assert.True(await w.LockToPaletteAsync(new Palette(dusk.Name, dusk.Colors) { LibraryId = dusk.Id }));
        Assert.True(w.Session.Document.PaletteLocked);
        w.Canvas.FitToView();
        Pump();
        Paint(w, new ColorRgb(250, 120, 90), 3, (4, 16), (28, 16)); // not a palette colour: the nearest one is used
        // Everything painted into the layer is palette-exact, soft edges included (Mission 007's rule: each pixel is stored
        // as a palette colour at its own alpha, which the conformer reads back exactly; composites may mix).
        var painted = w.Session.Document.ActiveLayer.Image.Pixels.Where(p => Pixel.A(p) > 0).Distinct().ToList();
        Assert.NotEmpty(painted);
        var conformer = w.Session.Document.PaletteConformer!;
        Assert.All(painted, p => Assert.True(conformer.ExactIndex(p) >= 0, $"{Pixel.ToStraight(p)} isn't a palette colour"));
        Assert.Contains(painted, p => Pixel.A(p) < 255); // soft edges kept
        w.Session.Document.MarkSaved();
        w.Close();
    }
}
