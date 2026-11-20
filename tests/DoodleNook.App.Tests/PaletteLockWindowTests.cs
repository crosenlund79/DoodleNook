using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Palette Lock and Remap to Palette through the real window.</summary>
public sealed class PaletteLockWindowTests : IDisposable
{
    private static readonly ColorRgb Red = ColorRgb.Parse("#C8473E"), Blue = ColorRgb.Parse("#3F6A82"), Cream = ColorRgb.Parse("#F2E3C6"),
        Dark = ColorRgb.Parse("#1A1C2C"), Green = ColorRgb.Parse("#00C060");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-lock-" + Guid.NewGuid().ToString("N")[..8]);

    public PaletteLockWindowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Palette Mini() => new("Mini", [Red, Blue, Cream, Dark]);

    private MainWindow Window(WorkspaceKind ws = WorkspaceKind.Painting)
    {
        var w = Open(1280, 800, ws);
        w.UseLibrary(new CreativeLibrary(Path.Combine(_dir, "Library")));
        w.Session.ReplaceDocument(new Document(300, 200, null, "Sketch"), null);
        Pump();
        w.Canvas.FitToView();
        Pump();
        return w;
    }

    private static void PaintLine(MainWindow w, ColorRgb colour, double y, double size = 14, double hardness = 1)
    {
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new Engine.Painting.BrushSettings { Size = size, Hardness = hardness, Opacity = 1 }, "pencil");
        w.Session.SetForeground(colour);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (30, y), (150, y), (270, y));
    }

    /// <summary>Lets the window run (dialogs, background work) until <paramref name="done"/> or a timeout.</summary>
    private static async Task Until(Func<bool> done, int ms = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The window didn't get there in time.");
            Pump();
            await Task.Delay(15);
            Pump();
        }
    }

    private static void AssertOnlyPalette(Document doc)
    {
        var c = doc.PaletteConformer ?? new PaletteConformer(doc.Palette.Colors);
        foreach (var (_, img) in doc.AllImages())
            Assert.All(img.Pixels.Where(p => p != 0), p => Assert.True(c.ExactIndex(p) >= 0, $"{p:X8} is not a palette colour"));
    }

    [AvaloniaFact]
    public async Task A_picture_using_only_palette_colours_locks_at_once_and_painting_stays_in_the_palette()
    {
        var w = Window();
        PaintLine(w, Red, 60, hardness: 0.3);
        Assert.False(Has(w, "PaletteLockBadge"));
        Assert.False(Has(w, "PaletteLockRow"));

        Assert.True(await w.LockToPaletteAsync(Mini()));
        Pump();
        var doc = w.Session.Document;
        Assert.True(doc.PaletteLocked);
        Assert.False(Has(w, "PaletteRemapDialog")); // nothing to ask
        Assert.True(Has(w, "PaletteLockBadge"));    // the small lock by the colour chips
        Assert.True(Has(w, "PaletteLockRow"));      // the colour panel says it, and its picker is gone

        // The eyedropper, the hex box or any picked colour resolves to an allowed colour.
        w.Session.SetForeground(Green);
        Assert.Contains(w.Session.Foreground, doc.Palette.Colors);
        // Soft, pressure-dependent painting over other colours stays in the palette and keeps its transparency.
        w.Session.SetForeground(Blue);
        w.Session.UseBrush(new Engine.Painting.BrushSettings { Size = 30, Hardness = 0.1, Opacity = 0.7, PressureOpacity = true }, "pencil");
        Drag(w, MouseButton.Left, RawInputModifiers.None, (150, 20), (150, 100), (150, 180));
        AssertOnlyPalette(doc);
        Assert.True(doc.ActiveLayer.Image.Pixels.Select(Pixel.A).Distinct().Count() > 20);

        w.Undo();
        w.Undo();
        Assert.False(doc.PaletteLocked);
        Pump();
        Assert.False(Has(w, "PaletteLockBadge"));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Locking_a_picture_with_other_colours_asks_first_and_cancel_changes_nothing()
    {
        var w = Window();
        PaintLine(w, Red, 50);
        PaintLine(w, Green, 120);
        var doc = w.Session.Document;
        var before = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        int steps = doc.History.UndoCount;

        var cancelled = w.LockToPaletteAsync(Mini());
        await Until(() => Has(w, "PaletteRemapDialog"));
        var dialog = w.LastRemapDialog!;
        Assert.True(dialog.LockAfter);
        Assert.False(Find<Button>(w, "RemapLock").IsEnabled); // locking is the point here
        w.Dialogs.Close("cancel");
        Assert.False(await cancelled);
        Assert.False(doc.PaletteLocked);
        Assert.Equal(before, doc.ActiveLayer.Image.Pixels);
        Assert.Equal(steps, doc.History.UndoCount);

        // Again, this time choosing where green goes (blue) and applying.
        var applied = w.LockToPaletteAsync(Mini());
        await Until(() => Has(w, "PaletteRemapDialog"));
        dialog = w.LastRemapDialog!;
        int greenRow = dialog.Remap.RowOf(Green);
        Assert.True(greenRow >= 0);
        Click(w, Find<Button>(w, $"RemapRow_{greenRow}"));
        Assert.Equal(greenRow, dialog.SelectedRow);
        Click(w, Find<Button>(w, "RemapTarget_1"));     // blue
        Assert.Equal(1, dialog.Remap.TargetOf(greenRow));
        var preview = dialog.PreviewNow(0);
        Assert.Equal(Pixel.Opaque(Blue), preview[150, 120]);
        Assert.Equal(before, doc.ActiveLayer.Image.Pixels); // still only a preview
        w.Dialogs.Close("apply");
        Assert.True(await applied);
        Assert.True(doc.PaletteLocked);
        Assert.Equal(Pixel.Opaque(Blue), doc.ActiveLayer.Image[150, 120]);
        Assert.Equal(Pixel.Opaque(Red), doc.ActiveLayer.Image[150, 50]);
        AssertOnlyPalette(doc);
        Assert.Equal(steps + 1, doc.History.UndoCount); // one step for the whole conversion

        w.Undo();
        Assert.Equal(before, doc.ActiveLayer.Image.Pixels);
        Assert.False(doc.PaletteLocked);
        w.Redo();
        Assert.True(doc.PaletteLocked);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_remap_dialog_compares_resets_and_changes_palette_and_is_reached_from_the_image_menu()
    {
        var w = Window();
        PaintLine(w, Red, 50);
        PaintLine(w, Green, 120);
        var before = (uint[])w.Session.Document.ActiveLayer.Image.Pixels.Clone();
        Menu(w, "Image", "Remap to palette");
        await Until(() => Has(w, "PaletteRemapDialog"));
        var dialog = w.LastRemapDialog!;
        Assert.True(Has(w, "RemapCompare"));
        Assert.True(Has(w, "RemapPreview"));
        Assert.True(Has(w, "RemapPaletteChooser"));
        var meadow = w.Session.Library.FindPalette(BuiltInAssets.Prefix + "meadow")!;
        await dialog.ChangeTargetAsync(new Palette(meadow.Name, meadow.Colors) { LibraryId = meadow.Id });
        Pump();
        Assert.Equal(meadow.Colors, dialog.Remap.Target);
        dialog.SelectRow(0);
        dialog.AssignSelected(0);
        Assert.True(dialog.Remap.IsChosen(0));
        Click(w, Find<Button>(w, "RemapReset"));
        Assert.False(dialog.Remap.IsChosen(0));
        w.Dialogs.Close("cancel");
        Pump();
        Assert.Equal(before, w.Session.Document.ActiveLayer.Image.Pixels);
        Assert.False(w.Session.Document.PaletteLocked);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Colours_are_added_or_replaced_only_by_explicit_steps()
    {
        var w = Window();
        PaintLine(w, Blue, 80);
        await w.LockToPaletteAsync(Mini());
        var doc = w.Session.Document;

        // Add: a colour dialog, then the colour is allowed (nothing recoloured).
        var pixels = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        var add = w.AddPaletteColourAsync();
        await Until(() => Has(w, "ColourHex"));
        Find<TextBox>(w, "ColourHex").Text = "#00AA55";
        w.Dialogs.Close("ok");
        await add;
        Assert.Contains(ColorRgb.Parse("#00AA55"), doc.Palette.Colors);
        Assert.Equal(ColorRgb.Parse("#00AA55"), w.Session.Foreground);
        Assert.Equal(pixels, doc.ActiveLayer.Image.Pixels);

        // Replace: old blue → new blue, previewed, then applied.
        var newBlue = ColorRgb.Parse("#5FA8E8");
        var replace = w.ReplacePaletteColourAsync(Blue);
        await Until(() => Has(w, "ColourHex"));
        Find<TextBox>(w, "ColourHex").Text = newBlue.ToHex();
        w.Dialogs.Close("ok");
        await Until(() => Has(w, "PaletteRemapDialog"));
        Assert.Equal(Pixel.Opaque(Blue), doc.ActiveLayer.Image[150, 80]); // not yet
        w.Dialogs.Close("apply");
        Assert.True(await replace);
        Assert.Equal(Pixel.Opaque(newBlue), doc.ActiveLayer.Image[150, 80]);
        Assert.Equal(newBlue, doc.Palette.Colors[1]);
        Assert.DoesNotContain(Blue, doc.Palette.Colors);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Library_edits_never_recolour_a_locked_picture_until_the_artist_updates_it()
    {
        var w = Window();
        var lib = w.Session.Library;
        var mine = lib.AddPalette("Mine", "My palettes", Mini().Colors);
        PaintLine(w, Blue, 80);
        await w.LockToPaletteAsync(new Palette(mine.Name, mine.Colors) { LibraryId = mine.Id });
        var doc = w.Session.Document;
        var pixels = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        Assert.DoesNotContain(w.PaletteLockEntries(), e => e.Label.StartsWith("Update from", StringComparison.Ordinal));

        var newBlue = ColorRgb.Parse("#2F8FDF");
        lib.UpdatePalette(mine.Id, [Red, newBlue, Cream, Dark]); // edited in the palette manager
        Pump();
        Assert.Equal(pixels, doc.ActiveLayer.Image.Pixels);     // the picture keeps its own copy
        Assert.Equal(Blue, doc.Palette.Colors[1]);
        Assert.Contains(w.PaletteLockEntries(), e => e.Label.StartsWith("Update from", StringComparison.Ordinal));

        var update = w.UpdateFromLibraryAsync();
        await Until(() => Has(w, "PaletteRemapDialog"));
        w.Dialogs.Close("apply");
        Assert.True(await update);
        Assert.Equal(Pixel.Opaque(newBlue), doc.ActiveLayer.Image[150, 80]);
        Assert.Equal(newBlue, doc.Palette.Colors[1]);
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_locked_picture_reopens_locked()
    {
        var w = Window();
        PaintLine(w, Red, 80, hardness: 0.4);
        await w.LockToPaletteAsync(Mini());
        var path = Path.Combine(_dir, "locked.dnk");
        Assert.True(await w.SaveToPathAsync(path));
        w.Session.ReplaceDocument(new Document(50, 50, null), null);
        Pump();
        Assert.False(Has(w, "PaletteLockBadge"));
        Assert.True(await w.OpenPathAsync(path));
        Pump();
        Assert.True(w.Session.Document.PaletteLocked);
        Assert.Equal(Mini().Colors, w.Session.Document.Palette.Colors);
        Assert.True(Has(w, "PaletteLockBadge"));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Remapping_an_animation_keeps_frames_timing_and_held_drawings()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.UseLibrary(new CreativeLibrary(Path.Combine(_dir, "Library")));
        w.CreateNewAnimation(new NewAnimationChoice(160, 120, 4, 12, true, null));
        Pump();
        var doc = w.Session.Document;
        var layer = doc.ActiveLayer;
        w.Session.SelectTool(ToolKind.PixelPencil);
        w.Session.SetForeground(Green);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (10.5, 100.5), (150.5, 100.5));
        while (doc.HoldRange(layer, 0).End < 3) doc.ExtendHold(layer, 0);
        doc.SetFrameDuration(1, 300);
        var held = doc.CelAt(layer, 0);
        int frames = doc.FrameCount;

        var remap = w.RemapToPaletteAsync(Mini(), forceLock: true);
        await Until(() => Has(w, "PaletteRemapDialog"));
        Assert.True(Has(w, "RemapPlay")); // animations can be played in the preview
        w.Dialogs.Close("apply");
        Assert.True(await remap);
        Assert.Equal(frames, doc.FrameCount);
        Assert.Equal(300, doc.Animation!.DurationOf(1));
        Assert.All(Enumerable.Range(0, 4), f => Assert.Same(held, doc.CelAt(layer, f)));
        AssertOnlyPalette(doc);
        Assert.True(doc.PaletteLocked);
        w.Close();
    }

    [AvaloniaFact]
    public void The_everyday_interface_shows_nothing_of_the_lock_until_it_is_used()
    {
        foreach (var ws in new[] { WorkspaceKind.QuickSketch, WorkspaceKind.Painting, WorkspaceKind.PixelArt, WorkspaceKind.Animation })
        {
            var w = Window(ws);
            Assert.False(Has(w, "PaletteLockBadge"));
            Assert.False(Has(w, "PaletteLockRow"));
            Assert.False(Has(w, "QuickPaletteLock"));
            Assert.False(Has(w, "PixelPaletteLock"));
            w.Session.Document.LockToPalette(Mini());
            Pump();
            Assert.True(ws == WorkspaceKind.QuickSketch ? Has(w, "QuickPaletteLock") : Has(w, "PaletteLockBadge") || Has(w, "PixelPaletteLock"), ws.ToString());
            w.Close();
        }
    }
}
