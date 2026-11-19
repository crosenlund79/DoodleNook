using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public class DocumentTests
{
    [Fact]
    public void One_stroke_is_one_undo_step_that_restores_exact_pixels()
    {
        var doc = new Document(200, 200);
        var blank = Snapshot(doc);
        Stroke(doc, Paint(Hard(12)), Line(10, 10, 190, 190, step: 3));
        var painted = Snapshot(doc);

        Assert.Equal(1, doc.History.UndoCount);
        Assert.Equal("Brush stroke", doc.History.NextUndoName);
        Assert.True(doc.Undo());
        Assert.Equal(blank, doc.ActiveLayer.Image.Pixels);
        Assert.True(doc.Redo());
        Assert.Equal(painted, doc.ActiveLayer.Image.Pixels);
    }

    [Fact]
    public void Overlapping_strokes_undo_and_redo_in_order()
    {
        var doc = new Document(150, 150);
        var s0 = Snapshot(doc);
        Stroke(doc, Paint(Hard(30), new ColorRgb(255, 0, 0)), S(20, 75), S(130, 75));
        var s1 = Snapshot(doc);
        Stroke(doc, Paint(Hard(30, opacity: 0.6), new ColorRgb(0, 0, 255)), S(75, 20), S(75, 130));
        var s2 = Snapshot(doc);
        Stroke(doc, Erase(Hard(20)), S(20, 20), S(130, 130));
        var s3 = Snapshot(doc);

        doc.Undo(); Assert.Equal(s2, doc.ActiveLayer.Image.Pixels);
        doc.Undo(); Assert.Equal(s1, doc.ActiveLayer.Image.Pixels);
        doc.Undo(); Assert.Equal(s0, doc.ActiveLayer.Image.Pixels);
        Assert.False(doc.CanUndo);
        Assert.False(doc.Undo());
        doc.Redo(); Assert.Equal(s1, doc.ActiveLayer.Image.Pixels);
        doc.Redo(); Assert.Equal(s2, doc.ActiveLayer.Image.Pixels);
        doc.Redo(); Assert.Equal(s3, doc.ActiveLayer.Image.Pixels);
        Assert.False(doc.CanRedo);
        Assert.False(doc.Redo());
    }

    [Fact]
    public void New_drawing_after_undo_discards_redo()
    {
        var doc = new Document(100, 100);
        Stroke(doc, Paint(Hard(10)), S(10, 10), S(90, 10));
        Stroke(doc, Paint(Hard(10)), S(10, 50), S(90, 50));
        doc.Undo();
        Assert.True(doc.CanRedo);
        Stroke(doc, Paint(Hard(10)), S(10, 90), S(90, 90));
        Assert.False(doc.CanRedo);
        Assert.Equal(2, doc.History.UndoCount);
        Assert.Equal(0, Alpha(doc, 50, 50));
    }

    [Fact]
    public void Undo_only_stores_the_tiles_a_stroke_touched()
    {
        var doc = new Document(1024, 1024);
        Stroke(doc, Paint(Hard(6)), S(10, 10), S(50, 10));
        var bytes = doc.History.TotalBytes;
        Assert.True(bytes < 1024 * 1024 * 4 / 50, $"undo used {bytes} bytes for a tiny stroke");
    }

    [Fact]
    public void Dirty_state_follows_saves_undo_and_redo()
    {
        var doc = new Document(80, 80);
        Assert.False(doc.IsDirty);
        Stroke(doc, Paint(Hard(8)), S(10, 10), S(70, 70));
        Assert.True(doc.IsDirty);
        doc.Undo();
        Assert.False(doc.IsDirty);
        doc.Redo();
        Assert.True(doc.IsDirty);
        doc.MarkSaved();
        Assert.False(doc.IsDirty);
        doc.Undo();
        Assert.True(doc.IsDirty);
        doc.Redo();
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Dirty_state_is_correct_after_old_history_is_dropped()
    {
        var doc = new Document(80, 80);
        doc.History.MaxEntries = 3;
        for (int i = 0; i < 6; i++) Stroke(doc, Paint(Hard(4)), S(5 + i * 10, 10), S(5 + i * 10, 70));
        Assert.Equal(3, doc.History.UndoCount);
        while (doc.CanUndo) doc.Undo();
        // We cannot get back to the blank (saved) canvas any more, so it must still count as changed.
        Assert.True(doc.IsDirty);
        Assert.True(Alpha(doc, 5, 40) > 0);
    }

    [Fact]
    public void History_respects_its_memory_budget()
    {
        var doc = new Document(512, 512);
        doc.History.MaxBytes = 300_000;
        for (int i = 0; i < 20; i++) Stroke(doc, Paint(Hard(40)), S(20, 20 + i * 20), S(490, 20 + i * 20));
        Assert.True(doc.History.TotalBytes <= 300_000 || doc.History.UndoCount == 1);
        Assert.True(doc.CanUndo);
    }

    [Fact]
    public void Clear_is_undoable_and_a_no_op_on_an_empty_canvas()
    {
        var doc = new Document(100, 100);
        Assert.False(doc.Clear());
        Assert.False(doc.CanUndo);

        Stroke(doc, Paint(Hard(20)), S(10, 10), S(90, 90));
        var painted = Snapshot(doc);
        Assert.True(doc.Clear());
        Assert.All(doc.ActiveLayer.Image.Pixels, p => Assert.Equal(Pixel.Transparent, p));
        Assert.Equal("Clear layer", doc.History.NextUndoName);
        doc.Undo();
        Assert.Equal(painted, doc.ActiveLayer.Image.Pixels);
    }

    [Fact]
    public void Pixel_change_events_report_stroke_undo_and_redo_areas()
    {
        var doc = new Document(100, 100);
        var areas = new List<PixelRect>();
        doc.ProjectionChanged += areas.Add;
        Stroke(doc, Paint(Hard(10)), S(20, 20), S(40, 20));
        Assert.NotEmpty(areas);
        var union = areas.Aggregate(PixelRect.Empty, (a, b) => a.Union(b));
        Assert.True(union.Contains(20, 20) && union.Contains(40, 20));
        areas.Clear();
        doc.Undo();
        Assert.Single(areas);
        Assert.True(areas[0].Contains(30, 20));
    }

    [Fact]
    public void Background_shows_through_transparent_and_erased_pixels()
    {
        var doc = new Document(40, 40, ColorRgb.White);
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.CompositeAt(5, 5));
        Stroke(doc, Paint(Hard(10, opacity: 0.5), ColorRgb.Black), S(20, 20));
        var mid = doc.CompositeAt(20, 20);
        Assert.Equal(255, Pixel.A(mid));
        Assert.InRange(Pixel.R(mid), 125, 129);
        Stroke(doc, Erase(Hard(20)), S(20, 20));
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.CompositeAt(20, 20));
        Assert.Equal(Pixel.Transparent, doc.CompositeAt(-1, 3));
    }

    [Fact]
    public void Flatten_merges_layer_over_background_and_keeps_transparency_without_one()
    {
        var withBg = new Document(30, 30, new ColorRgb(10, 20, 30));
        var flat = withBg.Flatten();
        Assert.All(flat.Pixels, p => Assert.Equal(Pixel.Pack(255, 10, 20, 30), p));

        var noBg = new Document(30, 30);
        Stroke(noBg, Paint(Hard(6, opacity: 0.5)), S(15, 15));
        var flat2 = noBg.Flatten();
        Assert.Equal(noBg.ActiveLayer.Image.Pixels, flat2.Pixels);
        Assert.Equal(Pixel.Transparent, flat2[0, 0]);
    }

    [Fact]
    public void Unsupported_sizes_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Document(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Document(20000, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Document(12000, 12000));
        Assert.True(RasterImage.IsSupportedSize(1920, 1080));
        Assert.False(RasterImage.IsSupportedSize(-1, 1080));
    }
}
