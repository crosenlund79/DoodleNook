using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Storybook;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 015 — Storybook Studio in the real window: a storybook made, a panel drawn in the normal editor and
/// back, shared and low-resolution drawings, missing files, layouts, undo and export.</summary>
public sealed class StorybookWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-storywin-" + Guid.NewGuid().ToString("N"));

    public StorybookWindowTests() => Directory.CreateDirectory(_dir);

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

    private static string Texts(Window w) => string.Join(" | ", w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

    private string Drawing(string name, int w, int h, ColorRgb c)
    {
        var img = new RasterImage(w, h);
        img.Fill(Pixel.Opaque(c));
        var path = Path.Combine(_dir, name + ".dnk");
        DnkFile.Save(Document.FromImage(img), path);
        return path;
    }

    [AvaloniaFact]
    public async Task A_panel_is_drawn_in_the_normal_editor_and_the_page_shows_it_afterwards()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        var path = Path.Combine(_dir, "My story.donkbook");
        Assert.True(await w.CreateStorybookAsync(path, 210, 297, 300, StoryLayouts.Comic));
        Pump();
        var view = w.StorybookView!;
        Assert.True(w.StorybookShown);
        Assert.Contains("My story", Find<TextBlock>(w, "StorybookTitle").Text);
        Assert.True(Has(w, "StorybookPanel4"));
        Assert.True(File.Exists(path));

        // Select panel 2: its size at the project's resolution.
        view.Select(1);
        Pump();
        Assert.Contains("1093 × 1051 px at 300 dpi", Find<TextBlock>(w, "StorybookPanelSize").Text);

        // A new drawing for it, at exactly that size, opened in the editor.
        var create = w.NewStorybookDrawingAsync(0, 1);
        await Until(() => w.Dialogs.IsOpen);
        Assert.Contains("1093 × 1051", Texts(w));
        w.Dialogs.Close("create");
        await create;
        Pump();
        Assert.False(w.StorybookShown);
        Assert.Equal((1093, 1051), (w.Session.Document.Width, w.Session.Document.Height));
        var art = w.Session.FilePath!;
        Assert.StartsWith(Path.Combine(_dir, "My story art"), art);
        Assert.True(Has(w, "StorybookChip"));
        Assert.Equal("My story art/page 01 panel 2.dnk", StorybookProject.Load(path).Pages[0].Panels[1].Artwork);

        // Draw, save, go back: the page shows the new drawing.
        w.Session.SelectTool(Editor.ToolKind.Brush);
        w.Session.SetSize(80);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (200, 200), (500, 500), (800, 700));
        Pump();
        Assert.True(await w.SaveAsync());
        var saved = DnkFile.Load(art);
        Assert.False(saved.Flatten().Pixels.All(p => p == Pixel.Opaque(ColorRgb.White)));
        Click(w, Find<Button>(w, "StorybookBackToBook"));
        Pump();
        Assert.True(w.StorybookShown);
        await Until(() => view.Thumbnails.Get(art, true) is { Large: true });
        Assert.Equal((1093, 1051), (view.Thumbnails.Get(art, true)!.Width, view.Thumbnails.Get(art, true)!.Height));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Shared_low_resolution_and_missing_drawings_are_handled_without_touching_the_originals()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        var path = Path.Combine(_dir, "Board.donkbook");
        await w.CreateStorybookAsync(path, 210, 297, 300, StoryLayouts.Comic);
        var view = w.StorybookView!;
        var hill = Drawing("hill", 2244, 1051, new ColorRgb(110, 160, 90));
        var small = Drawing("small", 300, 200, new ColorRgb(200, 80, 60));
        var smallBytes = File.ReadAllBytes(small);

        // One drawing in two panels: shared.
        Assert.True(await w.LinkStorybookFileAsync(0, 0, hill));
        Assert.True(await w.LinkStorybookFileAsync(0, 3, hill));
        view.Select(3);
        Pump();
        Assert.Contains("Shared: shown in 2 panels", Find<TextBlock>(w, "StorybookShared").Text);
        Assert.Contains("Prints at 300 dpi here", Find<TextBlock>(w, "StorybookResolution").Text);

        // Too small for its panel: a warning, and a resized copy on request; the original is untouched.
        Assert.True(await w.LinkStorybookFileAsync(0, 1, small, resizeIfSmall: false));
        view.Select(1);
        Pump();
        Assert.Contains("below the project's 300 dpi", string.Join(" ", w.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "StorybookWarning").SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text)));
        Assert.True(await w.ResizedStorybookCopyAsync(0, 1));
        var copy = w.Storybook!.Resolve(w.Storybook.Pages[0].Panels[1].Artwork)!;
        Assert.NotEqual(small, copy);
        var (cw, ch, _) = DnkFile.ReadSummary(copy);
        Assert.Equal((1093, 729), (cw, ch)); // as wide as the panel at 300 dpi (it limits the fit); the 3:2 shape kept
        Assert.Equal(smallBytes, File.ReadAllBytes(small));

        // "Make a copy for this panel": panel 4 gets its own, panel 1 keeps the shared one.
        Assert.True(await w.CopyStorybookDrawingAsync(0, 3));
        Assert.NotEqual(w.Storybook.Pages[0].Panels[0].Artwork, w.Storybook.Pages[0].Panels[3].Artwork);

        // A missing drawing keeps its panel, with a way to find it again.
        File.Delete(hill);
        Assert.True(await w.OpenStorybookFromAsync(path));
        Pump();
        w.StorybookView!.Select(0);
        Pump();
        Assert.True(Has(w, "StorybookRelink"));
        Assert.EndsWith("hill.dnk", StorybookProject.Load(path).Pages[0].Panels[0].Artwork);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Layouts_change_without_losing_drawings_pages_are_managed_and_every_change_undoes()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        w.Session.AddLayer(); // the drawing underneath has something to undo: Ctrl+Z in the storybook must not touch it
        var path = Path.Combine(_dir, "Pages.donkbook");
        await w.CreateStorybookAsync(path, 210, 297, 300, StoryLayouts.Four);
        var view = w.StorybookView!;
        for (int i = 0; i < 4; i++) await w.LinkStorybookFileAsync(0, i, Drawing($"d{i}", 1000, 1000, new ColorRgb((byte)(50 * i), 90, 120)), resizeIfSmall: false);

        view.SetLayout(StoryLayouts.TwoStacked);
        Pump();
        var page = w.Storybook!.Pages[0];
        Assert.Equal(2, page.Panels.Count);
        Assert.Equal(2, page.Unplaced.Count);
        Assert.True(Has(w, "StorybookUnplaced"));
        int drawingUndos = w.Session.Document.History.UndoCount;
        Assert.True(drawingUndos > 0);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control); // Ctrl+Z: the storybook's change, not the drawing's
        Pump();
        Assert.Equal(drawingUndos, w.Session.Document.History.UndoCount);
        Assert.Equal(StoryLayouts.Four, w.Storybook!.Pages[0].Layout);
        Assert.Equal(4, w.Storybook.Pages[0].Panels.Count(p => p.Artwork is not null));
        Assert.Equal(StoryLayouts.Four, StorybookProject.Load(path).Pages[0].Layout); // the undo is saved too

        view.AddPage(StoryLayouts.Six);
        Pump();
        Assert.Equal(2, w.Storybook.Pages.Count);
        Assert.Equal(1, view.PageIndex);
        Assert.True(Has(w, "StorybookPageTile2"));
        Click(w, Find<Button>(w, "StorybookPrevious"));
        Assert.Equal(0, view.PageIndex);
        var delete = view.DeletePageAsync(1);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("delete");
        await delete;
        Assert.Single(w.Storybook.Pages);
        Assert.Single(StorybookProject.Load(path).Pages);
        view.Undo();
        Assert.Equal(2, w.Storybook.Pages.Count);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Pages_export_as_png_with_their_print_resolution_and_as_one_pdf()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        var path = Path.Combine(_dir, "Out.donkbook");
        await w.CreateStorybookAsync(path, 148, 210, 150, StoryLayouts.Cinematic); // A5 at 150 dpi
        await w.LinkStorybookFileAsync(0, 0, Drawing("sky", 820, 400, new ColorRgb(120, 180, 220)), resizeIfSmall: false);
        w.StorybookView!.AddPage(StoryLayouts.Single);

        var one = await w.ExportStorybookToAsync(StorybookExport.ThisPage, Path.Combine(_dir, "page.png"));
        Assert.Single(one);
        var png = File.ReadAllBytes(one[0]);
        var image = PngCodec.Decode(png);
        Assert.Equal((874, 1240), (image.Width, image.Height)); // 14.8 × 21 cm at 150 dpi
        int at = png.AsSpan().IndexOf("pHYs"u8);
        Assert.Equal((uint)Math.Round(150 / 0.0254), BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 4)));

        var all = await w.ExportStorybookToAsync(StorybookExport.AllPng, Path.Combine(_dir, "story.png"));
        Assert.Equal(["story_01.png", "story_02.png"], all.Select(p => Path.GetFileName(p)).ToArray());

        var pdf = await w.ExportStorybookToAsync(StorybookExport.Pdf, Path.Combine(_dir, "story.pdf"));
        var bytes = File.ReadAllBytes(pdf[0]);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count);
        Assert.Contains("419.5", text); // 148 mm = 419.5 pt wide
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp", SearchOption.AllDirectories));
        w.Close();
    }
}
