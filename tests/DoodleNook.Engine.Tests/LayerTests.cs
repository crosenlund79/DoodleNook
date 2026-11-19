using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public class LayerTests
{
    private static readonly ColorRgb Red = new(220, 30, 30);
    private static readonly ColorRgb Blue = new(30, 60, 220);

    [Fact]
    public void Painting_changes_only_the_active_layer()
    {
        var doc = new Document(100, 60, ColorRgb.White);
        var a = doc.ActiveLayer;
        Stroke(doc, Paint(Hard(10), Red), S(10, 30), S(90, 30));
        var b = doc.AddLayer("B");
        Assert.Same(b, doc.ActiveLayer);
        Assert.True(b.Image.IsBlank());
        Stroke(doc, Paint(Hard(10), Blue), S(50, 5), S(50, 55));
        Assert.Equal(Pixel.Opaque(Red), a.Image[30, 30]);
        Assert.Equal(0u, a.Image[50, 10]);
        Assert.Equal(Pixel.Opaque(Blue), b.Image[50, 10]);
        Assert.Equal(0u, b.Image[30, 30]);
        // Composite: blue on top of red where they cross.
        Assert.Equal(Pixel.Opaque(Blue), doc.CompositeAt(50, 30));
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.CompositeAt(5, 5));
    }

    [Fact]
    public void Hidden_layers_stay_intact_and_disappear_from_the_composite()
    {
        var doc = new Document(40, 40, ColorRgb.White);
        Stroke(doc, Paint(Hard(10), Red), S(20, 20));
        var pixels = Snapshot(doc);
        var layer = doc.ActiveLayer;
        doc.SetLayerProperties(layer, layer.Properties with { Visible = false });
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.CompositeAt(20, 20));
        Assert.Equal(pixels, layer.Image.Pixels);
        doc.Undo();
        Assert.Equal(Pixel.Opaque(Red), doc.CompositeAt(20, 20));
    }

    [Fact]
    public void Layer_opacity_is_applied_when_compositing_not_to_pixels()
    {
        var doc = new Document(40, 40, ColorRgb.White);
        Stroke(doc, Paint(Hard(10), ColorRgb.Black), S(20, 20));
        var layer = doc.ActiveLayer;
        doc.SetLayerProperties(layer, layer.Properties with { Opacity = 0.5 });
        Assert.Equal(255, Pixel.A(layer.Image[20, 20]));
        Assert.InRange(Pixel.R(doc.CompositeAt(20, 20)), 126, 129);
    }

    [Theory]
    [InlineData(BlendMode.Multiply, 100, 50)]
    [InlineData(BlendMode.Screen, 100, 178)]
    [InlineData(BlendMode.Darken, 100, 100)]
    [InlineData(BlendMode.Lighten, 100, 128)]
    [InlineData(BlendMode.Add, 100, 228)]
    [InlineData(BlendMode.Overlay, 100, 100)]
    public void Blend_modes_combine_with_the_layers_below(BlendMode mode, byte below, byte expected)
    {
        var doc = new Document(4, 4);
        var bottom = doc.ActiveLayer;
        bottom.Image.Fill(Pixel.Opaque(new ColorRgb(below, below, below)));
        bottom.RecalculateBoundsForTest();
        var top = doc.AddLayer("Top");
        top.Image.Fill(Pixel.Opaque(new ColorRgb(128, 128, 128)));
        top.RecalculateBoundsForTest();
        doc.SetLayerProperties(top, top.Properties with { Blend = mode });
        Assert.InRange(Pixel.R(doc.CompositeAt(1, 1)), expected - 2, expected + 2);
    }

    [Fact]
    public void Clipped_layer_only_shows_where_the_layer_below_has_paint()
    {
        var doc = new Document(100, 40, ColorRgb.White);
        Stroke(doc, Paint(Hard(10), ColorRgb.Black), S(10, 20), S(50, 20)); // base: left half only
        var colour = doc.AddLayer("Colour");
        colour.Image.Fill(Pixel.Opaque(Red));
        colour.RecalculateBoundsForTest();
        doc.SetLayerProperties(colour, colour.Properties with { ClipToBelow = true });
        Assert.Equal(Pixel.Opaque(Red), doc.CompositeAt(30, 20));          // over the base stroke
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.CompositeAt(80, 20)); // outside the base: clipped away
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.CompositeAt(30, 2));
    }

    [Fact]
    public void Add_delete_duplicate_reorder_and_rename_are_undoable()
    {
        var doc = new Document(20, 20);
        var first = doc.ActiveLayer;
        var second = doc.AddLayer("Second");
        var copy = doc.DuplicateLayer(second);
        Assert.Equal(["Layer 1", "Second", "Second copy"], doc.Layers.Select(l => l.Name));
        doc.MoveLayer(copy, 0);
        Assert.Same(copy, doc.Layers[0]);
        doc.SetLayerProperties(first, first.Properties with { Name = "Sketch" }, "Rename layer");
        Assert.True(doc.DeleteLayer(second));
        Assert.Equal(["Second copy", "Sketch"], doc.Layers.Select(l => l.Name));

        doc.Undo(); // delete
        doc.Undo(); // rename
        doc.Undo(); // move
        Assert.Equal(["Layer 1", "Second", "Second copy"], doc.Layers.Select(l => l.Name));
        doc.Undo(); // duplicate
        doc.Undo(); // add
        Assert.Single(doc.Layers);
        Assert.Same(first, doc.ActiveLayer);
        while (doc.Redo()) { }
        Assert.Equal(["Second copy", "Sketch"], doc.Layers.Select(l => l.Name));
    }

    [Fact]
    public void The_last_layer_cannot_be_deleted()
    {
        var doc = new Document(10, 10);
        Assert.False(doc.DeleteLayer(doc.ActiveLayer));
        Assert.Single(doc.Layers);
    }

    [Fact]
    public void Merge_down_combines_pixels_respecting_opacity_and_is_undoable()
    {
        var doc = new Document(60, 30, ColorRgb.White);
        var lower = doc.ActiveLayer;
        Stroke(doc, Paint(Hard(10), Red), S(10, 15), S(30, 15));
        var upper = doc.AddLayer("Upper");
        Stroke(doc, Paint(Hard(10), Blue), S(25, 15), S(50, 15));
        doc.SetLayerProperties(upper, upper.Properties with { Opacity = 0.5 });
        var before = doc.Flatten().Pixels;
        Assert.True(doc.MergeDown(upper));
        Assert.Single(doc.Layers);
        Assert.Same(lower, doc.ActiveLayer);
        var after = doc.Flatten().Pixels;
        Assert.True(before.Zip(after).All(t => Math.Abs(Pixel.R(t.First) - Pixel.R(t.Second)) <= 2 && Math.Abs(Pixel.B(t.First) - Pixel.B(t.Second)) <= 2));
        doc.Undo();
        Assert.Equal(2, doc.Layers.Count);
        Assert.Equal(0u, lower.Image[45, 15]);
    }

    [Fact]
    public void Locked_layers_refuse_strokes_and_alpha_locked_layers_keep_their_shape()
    {
        var doc = new Document(60, 30);
        var layer = doc.ActiveLayer;
        Stroke(doc, Paint(Hard(10), Red), S(10, 15), S(30, 15));
        doc.SetLayerProperties(layer, layer.Properties with { Locked = true });
        var engine = new StrokeEngine(doc);
        Assert.True(engine.Begin(Paint(Hard(10), Blue), S(40, 15)).IsEmpty);
        Assert.False(engine.IsActive);
        Assert.Throws<InvalidOperationException>(() => doc.BeginEdit(layer, "x"));

        doc.SetLayerProperties(layer, layer.Properties with { Locked = false, AlphaLocked = true });
        Stroke(doc, Paint(Hard(30), Blue), S(5, 15), S(55, 15));
        Assert.Equal(Pixel.Opaque(Blue), layer.Image[20, 15]); // recoloured
        Assert.Equal(0u, layer.Image[45, 15]);                 // still transparent
        Stroke(doc, Erase(Hard(30)), S(5, 15), S(55, 15));
        Assert.Equal(Pixel.Opaque(Blue), layer.Image[20, 15]); // the eraser can't remove locked transparency
    }

    [Fact]
    public void Selection_limits_painting_and_soft_edges_paint_partially()
    {
        var doc = new Document(100, 40);
        doc.SetSelection(SelectionMask.Rectangle(100, 40, new PixelRect(0, 0, 50, 40)), "Select");
        Stroke(doc, Paint(Hard(10), Red), S(5, 20), S(95, 20));
        Assert.Equal(255, Alpha(doc, 40, 20));
        Assert.Equal(0, Alpha(doc, 60, 20));
        doc.SetSelection(null, "Deselect");
        Stroke(doc, Paint(Hard(10), Red), S(5, 30), S(95, 30));
        Assert.Equal(255, Alpha(doc, 60, 30));
    }

    [Fact]
    public void Paper_can_be_hidden_and_changed_as_undo_steps()
    {
        var doc = new Document(10, 10, ColorRgb.White);
        doc.SetPaper(ColorRgb.White, false);
        Assert.Equal(0u, doc.CompositeAt(3, 3));
        doc.SetPaper(new ColorRgb(1, 2, 3), true);
        Assert.Equal(Pixel.Opaque(new ColorRgb(1, 2, 3)), doc.CompositeAt(3, 3));
        doc.Undo();
        doc.Undo();
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.CompositeAt(3, 3));
    }

    [Fact]
    public void Projection_updates_only_the_changed_area_and_reports_it()
    {
        var doc = new Document(200, 100);
        doc.AddLayer();
        var areas = new List<PixelRect>();
        doc.ProjectionChanged += areas.Add;
        Stroke(doc, Paint(Hard(6)), S(20, 20), S(40, 20));
        var union = areas.Aggregate(PixelRect.Empty, (a, b) => a.Union(b));
        Assert.True(union.Width < 40 && union.Height < 20, union.ToString());
    }
}

internal static class LayerTestExtensions
{
    /// <summary>Tests fill layer pixels directly; tell the layer its content changed.</summary>
    public static void RecalculateBoundsForTest(this Layer layer)
    {
        typeof(Layer).GetMethod("RecalculateBounds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(layer, null);
    }
}
