using System.Buffers.Binary;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Samples;
using DoodleNook.Engine.Storybook;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 015 — Storybook Studio's projects: pages, layouts, linked drawings, one resolution for every panel,
/// safe files, and page export.</summary>
public sealed class StorybookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-storybook-" + Guid.NewGuid().ToString("N")[..8]);

    public StorybookTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static readonly ColorRgb Red = new(200, 40, 40), Blue = new(40, 60, 200);

    private string Drawing(string name, int w, int h, ColorRgb c, StorybookProject? book = null)
    {
        var img = new RasterImage(w, h);
        img.Fill(Pixel.Opaque(c));
        var path = book is null ? Path.Combine(_dir, name + ".dnk") : book.NewArtworkPath(name);
        DnkFile.Save(Document.FromImage(img), path);
        return path;
    }

    // ================================================================== resolution

    [Fact]
    public void Every_panel_has_the_same_pixels_per_centimetre_whatever_its_size()
    {
        var book = StorybookProject.Create(210, 297, 300);
        // The brief's examples: 15 × 10 cm and 7 × 8 cm at 300 dpi.
        Assert.Equal((1772, 1181), book.SuggestedCanvas(new RectMm(0, 0, 150, 100)));
        Assert.Equal((827, 945), book.SuggestedCanvas(new RectMm(0, 0, 70, 80)));
        Assert.Equal((2480, 3508), book.PagePixels); // A4 at 300 dpi
        foreach (var layout in StoryLayouts.All)
        {
            var page = book.AddPage(layout.Id);
            foreach (var r in book.PanelRects(page))
            {
                var (w, h) = book.SuggestedCanvas(r);
                Assert.Equal(300 / 2.54, w / (r.Width / 10), 0); // pixels per centimetre, within a pixel's rounding
                Assert.Equal(300 / 2.54, h / (r.Height / 10), 0);
            }
        }
        var low = StorybookProject.Create(210, 297, 150);
        Assert.Equal((886, 591), low.SuggestedCanvas(new RectMm(0, 0, 150, 100)));
    }

    [Fact]
    public void A_drawing_is_judged_by_its_resolution_at_its_size_in_the_panel()
    {
        var book = StorybookProject.Create(210, 297, 300);
        var panel = new RectMm(0, 0, 150, 100);
        var exact = book.Check(panel, 1772, 1181, PanelFit.Fit);
        Assert.False(exact.BelowTarget);
        Assert.Equal(300, exact.EffectiveDpi, 0);
        // 800 × 600 is less wide than the panel: fitted, its height fills the 10 cm (600 px over 10 cm ≈ 152 dpi).
        var small = book.Check(panel, 800, 600, PanelFit.Fit);
        Assert.True(small.BelowTarget);
        Assert.Equal(600 / (100 / 25.4), small.EffectiveDpi, 1);
        // Filled, its width covers the 15 cm (800 px over 15 cm ≈ 135 dpi): enlarged more, so a lower resolution.
        Assert.Equal(800 / (150 / 25.4), book.Check(panel, 800, 600, PanelFit.Fill).EffectiveDpi, 1);
        Assert.False(book.Check(panel, 4000, 3000, PanelFit.Fit).BelowTarget);
    }

    // ================================================================== pages and layouts

    [Fact]
    public void Pages_are_added_duplicated_moved_and_deleted()
    {
        var book = StorybookProject.Create(firstLayout: StoryLayouts.Four);
        Assert.Single(book.Pages);
        Assert.Equal(4, book.Pages[0].Panels.Count);
        var second = book.AddPage(StoryLayouts.Six);
        Assert.Equal(6, second.Panels.Count);
        book.Pages[0].Panels[0].Artwork = "art/a.dnk";
        var copy = book.DuplicatePage(0);
        Assert.Same(copy, book.Pages[1]);
        Assert.Equal("art/a.dnk", copy.Panels[0].Artwork); // the same drawing (shared)
        Assert.NotEqual(book.Pages[0].Id, copy.Id);
        book.MovePage(2, 0);
        Assert.Same(second, book.Pages[0]);
        book.DeletePage(0);
        book.DeletePage(0);
        book.DeletePage(0);
        Assert.Single(book.Pages); // a storybook always keeps a page
    }

    [Fact]
    public void Changing_to_a_layout_with_fewer_panels_keeps_every_drawing()
    {
        var book = StorybookProject.Create(firstLayout: StoryLayouts.Six);
        var page = book.Pages[0];
        for (int i = 0; i < 6; i++) page.Panels[i].Artwork = $"art/{i}.dnk";
        page.Panels[3].Artwork = null; // an empty slot
        page.Panels[3].Note = "Something happens here.";

        book.SetLayout(page, StoryLayouts.TwoStacked);
        Assert.Equal(["art/0.dnk", "art/1.dnk"], page.Panels.Select(p => p.Artwork).ToArray());
        Assert.Equal(["art/2.dnk", null, "art/4.dnk", "art/5.dnk"], page.Unplaced.Select(p => p.Artwork).ToArray()); // the note is kept too
        book.Place(page, 2, 0); // art/4 into the first panel; art/0 waits off the page instead
        Assert.Equal("art/4.dnk", page.Panels[0].Artwork);
        Assert.Contains(page.Unplaced, p => p.Artwork == "art/0.dnk");

        // Back to six: the waiting drawings fill the empty slots, in order.
        book.SetLayout(page, StoryLayouts.Six);
        Assert.Equal(6, page.Panels.Count);
        var all = page.Panels.Concat(page.Unplaced).Select(p => p.Artwork).OfType<string>().Order().ToArray();
        Assert.Equal(["art/0.dnk", "art/1.dnk", "art/2.dnk", "art/4.dnk", "art/5.dnk"], all);
        Assert.Empty(page.Unplaced.Where(p => p.Artwork is not null));
    }

    [Fact]
    public void Panels_tile_the_page_inside_the_margins_without_overlapping()
    {
        var book = StorybookProject.Create(210, 297, 300);
        foreach (var layout in StoryLayouts.All)
        {
            var page = book.AddPage(layout.Id);
            var px = book.PanelRects(page).Select(book.PanelPixels).ToList();
            Assert.Equal(layout.Count, px.Count);
            for (int a = 0; a < px.Count; a++)
            {
                Assert.True(px[a].X >= book.Pixels(book.MarginMm) - 1 && px[a].Right <= book.PagePixels.Width - book.Pixels(book.MarginMm) + 1);
                for (int b = a + 1; b < px.Count; b++) Assert.True(px[a].Intersect(px[b]).IsEmpty, $"{layout.Id}: panels {a} and {b} overlap");
            }
        }
        var comic = book.PanelRects(book.AddPage(StoryLayouts.Comic));
        Assert.True(comic[0].Width > comic[1].Width * 1.9); // wide, two, wide
        Assert.Equal(comic[0].Width, comic[3].Width, 6);
    }

    // ================================================================== files

    [Fact]
    public void A_project_saves_and_reopens_with_relative_links_that_survive_moving_the_folder()
    {
        var folder = Path.Combine(_dir, "Comic");
        Directory.CreateDirectory(folder);
        var book = StorybookProject.Create(firstLayout: StoryLayouts.Cinematic);
        book.FilePath = Path.Combine(folder, "My Story.donkbook");
        var art = Drawing("hero", 400, 300, Red, book);
        book.Pages[0].Panels[1].Artwork = book.Relate(art);
        book.Pages[0].Panels[1].Fit = PanelFit.Fill;
        book.Pages[0].Panels[1].Note = "Hello";
        book.Save();
        Assert.Equal("My Story art/hero.dnk", book.Pages[0].Panels[1].Artwork);
        Assert.DoesNotContain(".tmp", string.Join(",", Directory.GetFiles(folder)));

        // The whole folder moves: the links still work.
        var moved = Path.Combine(_dir, "Moved");
        Directory.Move(folder, moved);
        var back = StorybookProject.Load(Path.Combine(moved, "My Story.donkbook"));
        Assert.Equal(StoryLayouts.Cinematic, back.Pages[0].Layout);
        Assert.Equal(PanelFit.Fill, back.Pages[0].Panels[1].Fit);
        Assert.Equal("Hello", back.Pages[0].Panels[1].Note);
        Assert.True(File.Exists(back.Resolve(back.Pages[0].Panels[1].Artwork)));
        Assert.Equal(Path.Combine(moved, "My Story art"), back.ArtFolder);
        Assert.Contains("\"layout\": \"cinematic\"", File.ReadAllText(Path.Combine(moved, "My Story.donkbook")));
    }

    [Fact]
    public void A_missing_drawing_keeps_its_panel_and_a_damaged_or_newer_file_is_refused_clearly()
    {
        var book = StorybookProject.Create(firstLayout: StoryLayouts.TwoStacked);
        book.FilePath = Path.Combine(_dir, "s.donkbook");
        book.Pages[0].Panels[0].Artwork = "s art/gone.dnk";
        book.Save();
        var back = StorybookProject.Load(book.FilePath);
        Assert.Equal("s art/gone.dnk", back.Pages[0].Panels[0].Artwork); // kept, to relink
        Assert.False(File.Exists(back.Resolve(back.Pages[0].Panels[0].Artwork)));
        var page = StorybookRenderer.RenderPage(back, back.Pages[0], StorybookRenderer.LoadArtwork, 0.1);
        Assert.Equal(Pixel.Opaque(ColorRgb.White), page[page.Width / 2, page.Height / 4]);

        File.WriteAllText(Path.Combine(_dir, "bad.donkbook"), "{ not json");
        Assert.Throws<StorybookFormatException>(() => StorybookProject.Load(Path.Combine(_dir, "bad.donkbook")));
        File.WriteAllText(Path.Combine(_dir, "new.donkbook"), "{ \"format\": \"DoodleNook Storybook\", \"version\": 99, \"pages\": [] }");
        Assert.Contains("newer DoodleNook", Assert.Throws<StorybookFormatException>(() => StorybookProject.Load(Path.Combine(_dir, "new.donkbook"))).Message);
    }

    [Fact]
    public void Drawings_are_read_quickly_without_their_layers()
    {
        var path = Drawing("quick", 640, 320, Blue);
        var (w, h, thumb) = DnkFile.ReadSummary(path);
        Assert.Equal((640, 320), (w, h));
        Assert.NotNull(thumb);
        Assert.Equal(256, thumb!.Width);
    }

    // ================================================================== export

    [Fact]
    public void Pages_export_at_full_resolution_with_drawings_placed_unstretched_and_the_print_size_recorded()
    {
        var book = StorybookProject.Create(100, 100, 100, StoryLayouts.TwoStacked); // a 394 × 394 px page
        book.MarginMm = 0;
        book.GutterMm = 0;
        book.BorderMm = 0;
        book.FilePath = Path.Combine(_dir, "square.donkbook");
        var wide = Drawing("wide", 200, 50, Red, book); // 4:1 into a 2:1 panel
        var original = File.ReadAllBytes(wide);
        var page = book.Pages[0];
        page.Panels[0].Artwork = book.Relate(wide);
        page.Panels[1].Artwork = book.Relate(wide);
        page.Panels[1].Fit = PanelFit.Fill;

        var image = StorybookRenderer.RenderPage(book, page, StorybookRenderer.LoadArtwork);
        Assert.Equal((394, 394), (image.Width, image.Height));
        var top = book.PanelPixels(book.PanelRects(page)[0]);
        // Fit: the whole drawing, full width, a quarter as tall as wide, centred: page shows above and below.
        Assert.Equal(Pixel.Opaque(ColorRgb.White), image[top.X + top.Width / 2, top.Y + 5]);
        Assert.Equal(Pixel.Opaque(Red), image[top.X + top.Width / 2, top.Y + top.Height / 2]);
        Assert.Equal(Pixel.Opaque(Red), image[top.X + 1, top.Y + top.Height / 2]);
        // Fill: the panel covered completely (the sides cropped).
        var bottom = book.PanelPixels(book.PanelRects(page)[1]);
        Assert.Equal(Pixel.Opaque(Red), image[bottom.X + 1, bottom.Y + 1]);
        Assert.Equal(Pixel.Opaque(Red), image[bottom.Right - 2, bottom.Bottom - 2]);
        Assert.Equal(original, File.ReadAllBytes(wide)); // the drawing itself is untouched

        // PNG with the project's resolution recorded (pHYs, pixels per metre).
        var png = PngCodec.Encode(image, book.Dpi);
        int at = png.AsSpan().IndexOf("pHYs"u8);
        Assert.True(at > 0);
        Assert.Equal((uint)Math.Round(100 / 0.0254), BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 4)));
        Assert.Equal(image.Pixels, PngCodec.Decode(png).Pixels);
        Assert.Equal(["s_01.png", "s_02.png"], StorybookRenderer.PagePaths(Path.Combine(_dir, "s.png"), 2).Select(p => Path.GetFileName(p)).ToArray());
    }

    [Fact]
    public void Actual_size_places_the_drawing_at_the_projects_resolution_and_borders_are_drawn()
    {
        var book = StorybookProject.Create(100, 100, 100, StoryLayouts.Single);
        book.MarginMm = 10;
        book.FilePath = Path.Combine(_dir, "actual.donkbook");
        var small = Drawing("small", 40, 20, Blue, book);
        book.Pages[0].Panels[0].Artwork = book.Relate(small);
        book.Pages[0].Panels[0].Fit = PanelFit.Actual;
        var image = StorybookRenderer.RenderPage(book, book.Pages[0], StorybookRenderer.LoadArtwork);
        var panel = book.PanelPixels(book.PanelRects(book.Pages[0])[0]);
        int cx = panel.X + panel.Width / 2, cy = panel.Y + panel.Height / 2;
        Assert.Equal(Pixel.Opaque(Blue), image[cx - 19, cy]);
        Assert.Equal(Pixel.Opaque(ColorRgb.White), image[cx - 22, cy]); // 40 px wide: exactly its own size
        Assert.Equal(StorybookRenderer.BorderColour, image[panel.X, cy]);
        Assert.Equal(Pixel.Opaque(ColorRgb.White), image[panel.X - 2, cy]);
    }

    [Fact]
    public void The_sample_storybook_has_linked_editable_drawings_one_of_them_shared()
    {
        var path = SampleStorybook.Create(_dir, dpi: 72);
        var book = StorybookProject.Load(path);
        Assert.Equal(2, book.Pages.Count);
        var usage = book.Usage();
        Assert.Equal(6, usage.Count); // six drawings
        Assert.Contains(usage.Values, panels => panels.Count == 2); // one shown twice
        foreach (var file in usage.Keys)
        {
            var doc = DnkFile.Load(file);
            Assert.Equal(["Background", "Figure"], doc.Layers.Select(l => l.Name).ToArray());
        }
        var r = book.PanelRects(book.Pages[0])[1];
        Assert.Equal(book.SuggestedCanvas(r), DnkFile.ReadSummary(book.Resolve(book.Pages[0].Panels[1].Artwork)!) is var s ? (s.Width, s.Height) : default);
        Assert.Equal(path, SampleStorybook.Create(_dir, dpi: 72)); // an existing sample is never overwritten
    }
}
