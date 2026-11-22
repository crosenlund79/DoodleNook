using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 015 — the Sprite Sheet Workshop: slicing a sheet into frames, building sheets from pictures, and the
/// round trip animation → sheet → animation.</summary>
public sealed class SpriteSheetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-sheets-" + Guid.NewGuid().ToString("N"));

    public SpriteSheetTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly ColorRgb Ink = new(40, 30, 60), Leaf = new(90, 160, 70);

    /// <summary>A sheet of <paramref name="cols"/> × <paramref name="rows"/> frames, each with a different mark well inside it.</summary>
    private static RasterImage Sheet(int fw, int fh, int cols, int rows, int margin = 0, int spacing = 0, int drawn = int.MaxValue)
    {
        var sheet = new RasterImage(2 * margin + cols * fw + (cols - 1) * spacing, 2 * margin + rows * fh + (rows - 1) * spacing);
        for (int i = 0; i < Math.Min(drawn, cols * rows); i++)
        {
            int x = margin + i % cols * (fw + spacing), y = margin + i / cols * (fh + spacing);
            // A body in the middle of the cell (as a sprite has), and a mark that differs from frame to frame.
            for (int yy = fh / 5; yy < fh * 4 / 5; yy++)
                for (int xx = fw / 5; xx < fw * 4 / 5; xx++) sheet[x + xx, y + yy] = Pixel.Opaque(Leaf);
            for (int k = 0; k <= i % (fw - 2); k++) sheet[x + 1 + k, y + 1 + i % (fh - 2)] = Pixel.Opaque(Ink);
        }
        return sheet;
    }

    // ================================================================== the grid

    [Fact]
    public void Whole_cells_only_in_reading_order_with_margins_and_spacing()
    {
        var g = new SliceGrid(16, 16, MarginLeft: 2, MarginTop: 3, SpacingX: 1, SpacingY: 2);
        // 2 + 16 + 1 + 16 + 1 + 16 = 52; a 60 px wide sheet has 8 px left over: no fourth, partial cell.
        var layout = SpriteSheetSlicer.Layout(g, 60, 40);
        Assert.Equal((3, 2), (layout.Columns, layout.Rows));
        Assert.Equal(6, layout.Frames.Count);
        Assert.Equal(new PixelRect(2, 3, 16, 16), layout.Frames[0]);
        Assert.Equal(new PixelRect(19, 3, 16, 16), layout.Frames[1]);
        Assert.Equal(new PixelRect(2, 21, 16, 16), layout.Frames[3]);
        Assert.All(layout.Frames, r => Assert.True(r.Right <= 60 && r.Bottom <= 40));
        Assert.Equal((8, 3), SpriteSheetSlicer.Leftover(g, 60, 40)); // 3 + 16 + 2 + 16 = 37 of 40

        var columns = SpriteSheetSlicer.Layout(g with { Order = FrameOrder.DownColumns }, 60, 40);
        Assert.Equal(new PixelRect(2, 21, 16, 16), columns.Frames[1]);
        Assert.Equal(new PixelRect(19, 3, 16, 16), columns.Frames[2]);

        var some = SpriteSheetSlicer.Layout(g with { FirstFrame = 2, Count = 3 }, 60, 40);
        Assert.Equal([layout.Frames[1], layout.Frames[2], layout.Frames[3]], some.Frames);
        Assert.Equal(5, SpriteSheetSlicer.Layout(g with { FirstFrame = 2 }, 60, 40).Frames.Count);
        Assert.Equal(5, SpriteSheetSlicer.Layout(g with { FirstFrame = 2, Count = 99 }, 60, 40).Frames.Count);
    }

    [Fact]
    public void Impossible_grids_are_explained_in_plain_words()
    {
        Assert.Contains("at least 1 pixel", SpriteSheetSlicer.Problem(new SliceGrid(0, 16), 64, 64));
        Assert.Contains("negative", SpriteSheetSlicer.Problem(new SliceGrid(16, 16, SpacingX: -1), 64, 64));
        Assert.Contains("wider than the sheet (64 px)", SpriteSheetSlicer.Problem(new SliceGrid(80, 16), 64, 64));
        Assert.Contains("after the 60 px left margin", SpriteSheetSlicer.Problem(new SliceGrid(16, 16, MarginLeft: 60), 64, 64));
        Assert.Contains("taller than the sheet", SpriteSheetSlicer.Problem(new SliceGrid(16, 80), 64, 64));
        Assert.Contains("the first frame must be 1 to 16", SpriteSheetSlicer.Problem(new SliceGrid(16, 16, FirstFrame: 17), 64, 64));
        Assert.Contains("at least one frame", SpriteSheetSlicer.Problem(new SliceGrid(16, 16, Count: 0), 64, 64));
        Assert.Null(SpriteSheetSlicer.Problem(new SliceGrid(16, 16), 64, 64));
        Assert.Empty(SpriteSheetSlicer.Layout(new SliceGrid(80, 16), 64, 64).Frames);
        Assert.Throws<ArgumentException>(() => SpriteSheetSlicer.Slice(new RasterImage(64, 64), new SliceGrid(80, 16)));
    }

    [Fact]
    public void Slicing_copies_exact_pixels()
    {
        var sheet = Sheet(12, 10, 4, 2, margin: 3, spacing: 2);
        var frames = SpriteSheetSlicer.Slice(sheet, new SliceGrid(12, 10, 3, 3, 2, 2));
        Assert.Equal(8, frames.Count);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(12, frames[i].Width);
            Assert.Equal(sheet.Crop(new PixelRect(3 + i % 4 * 14, 3 + i / 4 * 12, 12, 10)).Pixels, frames[i].Pixels);
        }
    }

    // ================================================================== first guesses

    [Fact]
    public void The_first_guess_finds_frames_between_clear_lines_even_when_they_are_not_square()
    {
        var sheet = Sheet(48, 64, 4, 2); // 192 × 128: a square guess (64) would cut through the frames
        var g = SpriteSheetSlicer.Suggest(sheet);
        Assert.Equal((48, 64), (g.FrameWidth, g.FrameHeight));
        Assert.Equal(8, SpriteSheetSlicer.Layout(g, sheet.Width, sheet.Height).Frames.Count);
    }

    [Fact]
    public void The_first_guess_leaves_out_empty_cells_at_the_end_and_cuts_an_opaque_strip_into_squares()
    {
        var sheet = Sheet(32, 32, 4, 2, drawn: 6); // the last row has two unused cells
        var g = SpriteSheetSlicer.Suggest(sheet);
        Assert.Equal((32, 32), (g.FrameWidth, g.FrameHeight));
        Assert.Equal(6, g.Count);
        Assert.True(SpriteSheetSlicer.IsEmpty(sheet, new PixelRect(96, 32, 32, 32)));
        Assert.False(SpriteSheetSlicer.IsEmpty(sheet, new PixelRect(0, 0, 32, 32)));

        var opaque = new RasterImage(96, 24);
        opaque.Fill(Pixel.Opaque(Leaf));
        var strip = SpriteSheetSlicer.Suggest(opaque);
        Assert.Equal((24, 24), (strip.FrameWidth, strip.FrameHeight));
        Assert.Equal(4, SpriteSheetSlicer.Layout(strip, 96, 24).Frames.Count);
    }

    /// <summary>Mission 016, from real sheets (OpenGameArt characters, Kenney's tile sheets): characters standing on the
    /// bottom row of their cell, and tiles separated by a transparent line, are guessed right.</summary>
    [Fact]
    public void The_first_guess_allows_sprites_touching_their_cell_border_and_finds_gaps_between_frames()
    {
        // 5 × 3 cells of 20 × 24: each figure stands on the cell's last pixel row and is 2 px narrower than the cell.
        var standing = new RasterImage(100, 72);
        for (int i = 0; i < 15; i++)
        {
            int x0 = i % 5 * 20, y0 = i / 5 * 24;
            for (int y = 6 + i % 3; y < 24; y++)
                for (int x = 1; x < 19; x++) standing[x0 + x, y0 + y] = Pixel.Opaque(Leaf);
        }
        var g = SpriteSheetSlicer.Suggest(standing);
        Assert.Equal((20, 24, 0, 0), (g.FrameWidth, g.FrameHeight, g.SpacingX, g.SpacingY));

        // A figure that does cross into the next cell is never cut: 24 isn't offered for its rows.
        standing[30, 24] = Pixel.Opaque(Leaf);
        standing[30, 23] = Pixel.Opaque(Leaf);
        standing[30, 25] = Pixel.Opaque(Leaf);
        standing[30, 22] = Pixel.Opaque(Leaf);
        Assert.NotEqual(24, SpriteSheetSlicer.Suggest(standing).FrameHeight);

        // Tiles filling their whole 12 × 12 cell, one transparent line between them (4 × 2 tiles: 51 × 25).
        var tiles = Sheet(12, 12, 4, 2, spacing: 1);
        for (int i = 0; i < 8; i++)
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 12; x++) tiles[i % 4 * 13 + x, i / 4 * 13 + y] = Pixel.Opaque(Ink);
        var t = SpriteSheetSlicer.Suggest(tiles);
        Assert.Equal((12, 12, 1, 1), (t.FrameWidth, t.FrameHeight, t.SpacingX, t.SpacingY));
        Assert.Equal(8, SpriteSheetSlicer.Layout(t, tiles.Width, tiles.Height).Frames.Count);
    }

    [Fact]
    public void A_sheets_json_gives_the_exact_grid()
    {
        var frames = Enumerable.Range(0, 5).Select(i => new ExportFrame(Sheet(10, 8, 1, 1), 100, i)).ToList();
        var sheet = AnimationExport.BuildSheet(frames, new SpriteSheetOptions(SheetLayout.Grid, 3, Spacing: 4, Padding: 0, Margin: 6));
        var json = AnimationExport.SheetJson(sheet, "walk.png", 10, true);
        var g = SpriteSheetSlicer.Suggest(sheet.Image, json);
        Assert.Equal(new SliceGrid(10, 8, 6, 6, 4, 4, FrameOrder.AcrossRows, 1, 5), g);

        // The usual "frame" objects written by other sprite tools, listed down the columns.
        var other = """{ "frames": [ { "frame": {"x":0,"y":0,"w":16,"h":16} }, { "frame": {"x":0,"y":16,"w":16,"h":16} }, { "frame": {"x":16,"y":0,"w":16,"h":16} } ] }""";
        Assert.Equal(new SliceGrid(16, 16, 0, 0, 0, 0, FrameOrder.DownColumns, 1, 3), SpriteSheetSlicer.FromJson(other, 32, 32));
        Assert.Null(SpriteSheetSlicer.FromJson("not json", 32, 32));
        Assert.Null(SpriteSheetSlicer.FromJson("""{ "frames": [ {"x":0,"y":0,"w":16,"h":16}, {"x":16,"y":0,"w":8,"h":16} ] }""", 32, 32));
    }

    // ================================================================== documents

    [Fact]
    public void A_normal_import_is_one_transparent_layer_and_identical_frames_are_held()
    {
        var a = Sheet(8, 8, 1, 1);
        var b = new RasterImage(8, 8);
        b[2, 2] = Pixel.FromStraight(10, 200, 30, 128); // half transparent
        var doc = SpriteSheetImport.NormalDocument([a, a, b], 8);
        Assert.Equal(3, doc.FrameCount);
        Assert.Single(doc.Layers);
        Assert.False(doc.Layers[0].Locked);
        Assert.Null(doc.Paper);
        Assert.Same(doc.CelAt(doc.Layers[0], 0), doc.CelAt(doc.Layers[0], 1));
        Assert.Equal(a.Pixels, doc.RenderFrame(0, includePaper: false).Pixels);
        Assert.Equal(b.Pixels, doc.RenderFrame(2, includePaper: false).Pixels);
        Assert.Equal(8, doc.Animation!.Fps);
    }

    [Fact]
    public void A_reference_import_is_the_mission_012_four_layer_structure()
    {
        var doc = SpriteSheetImport.ReferenceDocument(SpriteSheetSlicer.Slice(Sheet(16, 16, 3, 1), new SliceGrid(16, 16)), 12);
        Assert.Equal(["Reference", "Sketch", "Outline", "Coloring"], doc.Layers.Select(l => l.Name).ToArray());
        Assert.True(doc.Layers[0].Locked);
        Assert.Equal(0.35, doc.Layers[0].Opacity, 3);
        Assert.Same(doc.Layers[1], doc.ActiveLayer);
        Assert.Equal(3, doc.FrameCount);
    }

    // ================================================================== pictures → sheet

    [Fact]
    public void Pictures_of_different_sizes_go_into_equal_cells_unscaled_and_the_pictures_are_untouched()
    {
        var small = new RasterImage(4, 6);
        small.Fill(Pixel.Opaque(Ink));
        var big = new RasterImage(10, 12);
        big.Fill(Pixel.Opaque(Leaf));
        var before = (uint[])small.Pixels.Clone();

        var frames = PicturesToSheet.Frames([small, big], CellAlignment.BottomCenter);
        Assert.All(frames, f => Assert.Equal((10, 12), (f.Image.Width, f.Image.Height)));
        // Bottom centre: x 3–6, y 6–11.
        Assert.Equal(Pixel.Opaque(Ink), frames[0].Image[3, 6]);
        Assert.Equal(Pixel.Opaque(Ink), frames[0].Image[6, 11]);
        Assert.Equal(0u, frames[0].Image[2, 11]);
        Assert.Equal(0u, frames[0].Image[3, 5]);
        Assert.Same(big, frames[1].Image);
        Assert.Equal(before, small.Pixels);

        Assert.Equal(Pixel.Opaque(Ink), PicturesToSheet.Frames([small, big], CellAlignment.TopLeft)[0].Image[0, 0]);
        Assert.Equal(Pixel.Opaque(Ink), PicturesToSheet.Frames([small, big], CellAlignment.Center)[0].Image[3, 3]);
        Assert.Equal(0u, PicturesToSheet.Frames([small, big], CellAlignment.Center)[0].Image[3, 2]);

        var sheet = AnimationExport.BuildSheet(frames, new SpriteSheetOptions(SheetLayout.Grid, PicturesToSheet.DefaultColumns(2)));
        Assert.Equal((20, 12), (sheet.Image.Width, sheet.Image.Height));
        Assert.Equal(4, PicturesToSheet.DefaultColumns(16));
        Assert.Equal(6, PicturesToSheet.DefaultColumns(6));
    }

    [Fact]
    public void Numbered_pictures_are_ordered_the_way_people_count()
    {
        var names = new[] { "walk_10.png", "walk_2.png", "walk_1.png", "walk_002b.png" }.Select(n => Path.Combine(_dir, n)).ToList();
        Assert.Equal(new[] { "walk_1.png", "walk_2.png", "walk_002b.png", "walk_10.png" }, AnimationImport.NaturalSort(names).Select(n => Path.GetFileName(n)).ToArray());
    }

    // ================================================================== the round trip

    /// <summary>A 5-frame 20 × 14 animation with two layers (one at 60 %, one hidden), soft and hard pixels, no paper.</summary>
    private static Document Animation()
    {
        var doc = Document.NewAnimation(20, 14, null, frames: 1, fps: 10);
        doc.AddLayer("Top");
        var pencil = new PixelStroke(doc);
        for (int f = 0; f < 5; f++)
        {
            if (f > 0) doc.NewFrame();
            doc.SetActiveLayer(doc.Layers[0]);
            pencil.Begin(new PixelStrokeStyle(Ink, false, 2, false, Symmetry.None, false, "Pixel"), 2 + 3 * f, 3);
            pencil.End();
            doc.SetActiveLayer(doc.Layers[1]);
            pencil.Begin(new PixelStrokeStyle(Leaf, false, 1, false, Symmetry.None, false, "Pixel"), 15 - 2 * f, 10);
            pencil.End();
        }
        doc.SetLayerProperties(doc.Layers[1], doc.Layers[1].Properties with { Opacity = 0.6 });
        doc.AddLayer("Hidden");
        doc.SetLayerProperties(doc.Layers[2], doc.Layers[2].Properties with { Visible = false });
        return doc;
    }

    [Fact]
    public void Animation_to_sheet_and_back_is_pixel_for_pixel_the_same()
    {
        var doc = Animation();
        var frames = AnimationExport.RenderFrames(doc, includePaper: false);
        Assert.Contains(frames[0].Image.Pixels, p => p >> 24 is > 0 and < 255); // the 60 % layer: partly transparent pixels
        foreach (var options in new[]
                 {
                     new SpriteSheetOptions(SheetLayout.Horizontal),
                     new SpriteSheetOptions(SheetLayout.Grid, 2, Spacing: 3, Padding: 2, Margin: 5),
                     new SpriteSheetOptions(SheetLayout.Vertical, Spacing: 1),
                 })
        {
            var sheet = AnimationExport.BuildSheet(frames, options);
            var png = Path.Combine(_dir, "sheet.png");
            File.WriteAllBytes(png, PngCodec.Encode(sheet.Image));
            var reread = PngCodec.Decode(File.ReadAllBytes(png));

            // With the sheet's JSON, the grid is found exactly …
            var grid = SpriteSheetSlicer.Suggest(reread, AnimationExport.SheetJson(sheet, "sheet.png", 10, true));
            // … and with matching settings typed in by hand, too.
            var o = options.Normalized();
            var typed = new SliceGrid(20, 14, o.Margin + o.Padding, o.Margin + o.Padding, o.Spacing + 2 * o.Padding, o.Spacing + 2 * o.Padding, Count: 5);
            Assert.Equal(SpriteSheetSlicer.Layout(typed, reread.Width, reread.Height).Frames, SpriteSheetSlicer.Layout(grid, reread.Width, reread.Height).Frames);

            var back = SpriteSheetImport.NormalDocument(SpriteSheetSlicer.Slice(reread, grid), 10);
            Assert.Equal(5, back.FrameCount);
            for (int f = 0; f < 5; f++)
            {
                // Through PNG (straight colour) and back; then as the new animation's frames.
                var expected = PngCodec.Decode(PngCodec.Encode(frames[f].Image));
                Assert.Equal(expected.Pixels, back.RenderFrame(f, includePaper: false).Pixels);
                Assert.Equal(expected.Pixels, AnimationExport.RenderFrames(back, includePaper: false)[f].Image.Pixels);
            }
        }
    }
}
