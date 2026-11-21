using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Animation;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 012 — The Artist's Companion in the window: importing animations for reconstruction and the layer
/// opacity control in Animation Studio's timeline.</summary>
public sealed class ArtistsCompanionWindowTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "dn-m012-" + Guid.NewGuid().ToString("N")[..8]);

    public ArtistsCompanionWindowTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { Directory.Delete(_temp, true); } catch (IOException) { }
    }

    internal static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "DoodleNook.Engine.Tests", "Fixtures", "animation", name);
    }

    internal static async Task Until(Func<bool> done, int ms = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Pump();
            await Task.Delay(15);
            Pump();
        }
    }

    private static IEnumerable<string?> Texts(Window w) =>
        w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text);

    private static void ClickAt(MainWindow w, Control c, Point p)
    {
        w.CaptureRenderedFrame()?.Dispose();
        var at = c.TranslatePoint(p, w)!.Value;
        w.MouseMove(at);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Pump();
    }

    /// <summary>Numbered pictures, each a different colour: frame_1.png … frame_N.png, plus unrelated PNGs in the folder.</summary>
    private string Sequence(int n)
    {
        for (int i = 1; i <= n; i++)
        {
            var img = new RasterImage(24, 16);
            img.Fill(Pixel.Opaque(new ColorRgb((byte)(i * 20), 40, (byte)(250 - i * 15))));
            File.WriteAllBytes(Path.Combine(_temp, $"frame_{i}.png"), PngCodec.Encode(img));
        }
        File.WriteAllBytes(Path.Combine(_temp, "notes.png"), PngCodec.Encode(new RasterImage(8, 8)));
        File.WriteAllBytes(Path.Combine(_temp, "frame_3_shadow.png"), PngCodec.Encode(new RasterImage(8, 8)));
        return Path.Combine(_temp, "frame_2.png");
    }

    [AvaloniaFact]
    public async Task Importing_a_gif_opens_a_four_layer_animation_ready_to_draw_in_animation_studio()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var import = w.ImportAnimationFromAsync([Fixture("walk-dispose2.gif")]);
        await Until(() => w.Dialogs.IsOpen);
        Assert.True(Has(w, "ImportAnimationDialog"));
        Assert.Contains(Texts(w), t => t?.Contains("6 frames · 48 × 32") == true);
        Assert.Contains(Texts(w), t => t?.StartsWith("from the file: 12.5 frames per second") == true);
        // Leave the last frame out.
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, Find<Button>(w, "ImportFrame6"));
        Assert.Equal("5 of 6 frames", Find<TextBlock>(w, "ImportCount").Text);
        w.Dialogs.Close("import");
        Assert.True(await import);
        Pump();

        var doc = w.Session.Document;
        Assert.Equal(WorkspaceKind.Animation, w.Workspace.Kind);
        Assert.Equal(5, doc.FrameCount);
        Assert.Equal(new[] { "Reference", "Sketch", "Outline", "Coloring" }, doc.Layers.Select(l => l.Name));
        Assert.Equal("Sketch", doc.ActiveLayer.Name);
        Assert.True(w.AnimationState.PixelAnimation); // a small sprite: pixel-art style by default
        var source = GifDecoder.Decode(File.ReadAllBytes(Fixture("walk-dispose2.gif")));
        for (int f = 0; f < 5; f++) Assert.Equal(source.Frames[f].Pixels, doc.CelAt(doc.Layers[0], f)!.Image.Pixels);
        // Draw on Sketch at frame 3 only.
        w.Session.GoToFrame(2);
        w.Session.SelectTool(Editor.ToolKind.PixelPencil);
        Pump();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (5, 5), (20, 20));
        Assert.Null(doc.CelAt(doc.Layers[1], 1));
        Assert.NotNull(doc.CelAt(doc.Layers[1], 2));
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task One_numbered_png_brings_its_whole_sequence_in_natural_order_and_nothing_else()
    {
        string chosen = Sequence(12);
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var import = w.ImportAnimationFromAsync([chosen]);
        await Until(() => w.Dialogs.IsOpen);
        Assert.Contains(Texts(w), t => t?.StartsWith("frame_1.png … frame_12.png · 12 pictures · 12 frames · 24 × 16") == true);
        Find<TextBox>(w, "ImportFps").Text = "8";
        Find<TextBox>(w, "ImportFrom").Text = "2";
        Find<TextBox>(w, "ImportTo").Text = "11";
        Pump();
        Assert.Equal("10 of 12 frames", Find<TextBlock>(w, "ImportCount").Text);
        w.Dialogs.Close("import");
        Assert.True(await import);
        var doc = w.Session.Document;
        Assert.Equal(10, doc.FrameCount);
        Assert.Equal(8, doc.Animation!.Fps);
        for (int f = 0; f < 10; f++)
            Assert.Equal(Pixel.Opaque(new ColorRgb((byte)((f + 2) * 20), 40, (byte)(250 - (f + 2) * 15))), doc.CelAt(doc.Layers[0], f)!.Image[3, 3]);
        Assert.True(File.Exists(Path.Combine(_temp, "frame_1.png"))); // only read
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_damaged_file_gives_a_short_message_and_changes_nothing()
    {
        var bad = Path.Combine(_temp, "broken.gif");
        File.WriteAllBytes(bad, [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 2]);
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var before = w.Session.Document;
        var import = w.ImportAnimationFromAsync([bad]);
        await Until(() => w.Dialogs.IsOpen);
        Assert.Contains(Texts(w), t => t == "Couldn't import the animation");
        w.Dialogs.Close("ok");
        Assert.False(await import);
        Assert.Same(before, w.Session.Document);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_timeline_shows_each_layers_opacity_and_a_click_gives_a_slider_for_every_frame()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var import = w.ImportAnimationFromAsync([Fixture("walk-apng-rgba.png")]);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("import");
        Assert.True(await import);
        Pump();
        var doc = w.Session.Document;
        var grid = Find<TimelineGrid>(w, "TimelineGrid");
        var sketch = doc.Layers[1];
        // All four layers are in view without scrolling.
        w.CaptureRenderedFrame()?.Dispose();
        Assert.True(grid.LockPoint(doc.Layers[0]).Y < grid.Bounds.Height - 8, $"{grid.LockPoint(doc.Layers[0])} in {grid.Bounds}");

        ClickAt(w, grid, grid.OpacityPoint(sketch));
        Assert.True(Has(w, "LayerOpacityPanel"));
        var box = Find<TextBox>(w, "LayerOpacityBox");
        Assert.Equal("100", box.Text);
        box.Text = "25";
        box.Focus();
        PressEnter(w);
        Assert.Equal(0.25, sketch.Opacity, 3);
        int undoSteps = doc.History.UndoCount;
        // It belongs to the layer: every frame, and frame changes keep it.
        w.Session.GoToFrame(4);
        Assert.Equal(0.25, doc.Layers[1].Opacity, 3);
        Key(w, Avalonia.Input.Key.Escape);
        w.Undo();
        Assert.Equal(1, sketch.Opacity, 3);
        Assert.Equal(undoSteps - 1, doc.History.UndoCount);

        // The reference's lock mark unlocks it.
        UiKit.HideOpenFlyouts();
        Pump();
        Assert.True(doc.Layers[0].Locked);
        ClickAt(w, grid, grid.LockPoint(doc.Layers[0]));
        Assert.False(doc.Layers[0].Locked);
        Assert.Equal("Sketch", doc.ActiveLayer.Name); // unlocking doesn't move you off the layer you draw on
        // Switching workspaces keeps the opacity.
        w.Session.SetLayerProperty(doc.Layers[2], p => p with { Opacity = 0.6 });
        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        w.SetWorkspace(WorkspaceKind.Animation);
        Pump();
        Assert.Equal(0.6, w.Session.Document.Layers[2].Opacity, 3);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Bundled_palettes_show_their_colour_count_creator_and_terms_and_lock_a_pixel_picture()
    {
        var w = Open(1280, 800, WorkspaceKind.PixelArt);
        var shown = w.OpenPalettesAsync();
        await Until(() => w.Dialogs.IsOpen);
        var manager = w.LastPaletteManager!;
        var r64 = Engine.Library.CuratedPalettes.All.Single(p => p.Name == "Resurrect 64");
        manager.Select(r64.Id);
        Pump();
        var credit = Find<TextBlock>(w, "PaletteCredit");
        Assert.True(credit.IsEffectivelyVisible);
        Assert.StartsWith("By Kerrie Lake", credit.Text);
        Assert.Contains("lospec.com/palette-list/resurrect-64", credit.Text);
        Assert.Contains(Texts(w), t => t == "64");
        manager.Select(Engine.Library.BuiltInAssets.Palettes[0].Id); // DoodleNook's own: no credit line
        Pump();
        Assert.False(credit.IsEffectivelyVisible);

        // Use PICO-8 and lock the picture to it: the pixel pencil only paints its colours.
        var pico = Engine.Library.CuratedPalettes.All.Single(p => p.Name == "PICO-8 palette");
        manager.Select(pico.Id);
        w.Dialogs.Close("use");
        await shown;
        Assert.Equal(pico.Id, w.Session.ActivePaletteId);
        w.Session.Document.LockToPalette(new Palette(pico.Name, pico.Colors) { LibraryId = pico.Id });
        w.Session.SetForeground(new ColorRgb(30, 170, 255)); // not a PICO-8 colour
        w.Session.SelectTool(Editor.ToolKind.PixelPencil);
        Pump();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (10, 10), (40, 30));
        var painted = w.Session.Document.ActiveLayer.Image.Pixels.Where(p => p != 0).ToList();
        Assert.NotEmpty(painted);
        var conformer = new Engine.Imaging.PaletteConformer(pico.Colors);
        Assert.All(painted, p => Assert.True(conformer.ExactIndex(p) >= 0, $"{p:X8}"));
        w.Session.Document.MarkSaved();
        w.Close();
    }

    private static string Docs(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "docs", name);
    }

    [AvaloniaFact]
    public async Task Help_opens_the_quick_start_guide_built_in_with_every_picture()
    {
        string md = File.ReadAllText(Docs("QUICK-START.md"));
        Assert.Equal(md.Replace("\r", ""), Dialogs.QuickStartGuide.Markdown()!.Replace("\r", "")); // the built-in copy is current
        var blocks = Dialogs.QuickStartGuide.Parse(md);
        Assert.Single(blocks, b => b.Kind == "heading" && b.Level == 1);
        int sections = blocks.Count(b => b.Kind == "heading" && b.Level == 2);
        Assert.InRange(sections, 5, 8);
        var pictures = blocks.Where(b => b.Kind == "image").Select(b => b.Text).ToList();
        Assert.NotEmpty(pictures);
        var onDisk = Directory.GetFiles(Docs(Path.Combine("screenshots", "quick-start")), "*.png").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(onDisk, pictures.Select(Path.GetFileName).Order().ToList()); // every picture used, none missing
        foreach (var pic in pictures)
        {
            var bytes = Dialogs.QuickStartGuide.Resource(pic);
            Assert.NotNull(bytes);
            Assert.Equal(File.ReadAllBytes(Docs(pic)), bytes);
        }

        var w = Open(1280, 800, WorkspaceKind.Painting);
        w.CaptureRenderedFrame()?.Dispose();
        Menu(w, "Help", "Quick Start Guide");
        await Until(() => w.Dialogs.IsOpen && Has(w, "QuickStartGuide"));
        var guide = Find<StackPanel>(w, "QuickStartGuide");
        Assert.Equal(sections + 1, guide.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Classes.Contains("guide-heading")));
        var images = guide.GetVisualDescendants().OfType<Image>().ToList();
        Assert.Equal(pictures.Count, images.Count);
        Assert.All(images, i => Assert.NotNull(i.Source));
        w.Dialogs.Close("close");
        w.Close();
    }

    [AvaloniaFact]
    public void The_quick_start_guide_names_only_real_shortcuts_and_menu_entries()
    {
        string md = File.ReadAllText(Docs("QUICK-START.md"));
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var known = MainWindow.ShortcutRows.Select(r => r.Keys).ToList();
        var menus = new Dictionary<string, List<MenuEntry>>();
        foreach (var kind in Enum.GetValues<WorkspaceKind>())
        {
            w.SetWorkspace(kind);
            Pump();
            foreach (var name in w.MenuNames)
            {
                var entries = w.MenuFor(name).ToList();
                if (!menus.TryGetValue(name, out var list)) menus[name] = list = [];
                list.AddRange(entries);
                known.AddRange(entries.Where(e => e.Shortcut is not null).Select(e => e.Shortcut!));
            }
        }
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(md, "`([^`]+)`"))
        {
            string key = m.Groups[1].Value;
            if (key.StartsWith('.')) continue; // a file type, not a key
            Assert.True(known.Any(k => k.Contains(key, StringComparison.Ordinal)), $"The guide names {key}, which isn't a DoodleNook shortcut");
        }
        int paths = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(md, @"\*\*(\w+) › ([^*]+)\*\*"))
        {
            string menu = m.Groups[1].Value, entry = m.Groups[2].Value;
            Assert.True(menus.ContainsKey(menu), $"No {menu} menu");
            Assert.Contains(menus[menu], e => e.Label.StartsWith(entry, StringComparison.Ordinal));
            paths++;
        }
        Assert.True(paths >= 5);
        w.Close();
    }

    private static void PressEnter(MainWindow w)
    {
        w.KeyPress(Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        w.KeyRelease(Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump();
    }
}
