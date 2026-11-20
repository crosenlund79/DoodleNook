using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using DoodleNook.Engine.Samples;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// A deterministic stand-in for the app's font renderer: each line is a solid bar (0.5 × size wide per character,
/// size tall), so tests can check placement, colour, effects and missing-font handling without system fonts.
/// </summary>
internal sealed class FakeTextRenderer : ITextRenderer
{
    public const string MissingFont = "Not Installed Font";

    public TextRenderResult Render(TextContent t, RasterImage target)
    {
        var lines = t.Text.Split('\n');
        double w = t.BoxWidth > 0 ? t.BoxWidth : Math.Max(1, lines.Max(l => l.Length)) * t.Size * 0.5;
        double h = Math.Max(1, lines.Length) * t.Size * t.LineSpacing;
        var box = new ObjectBox(t.X, t.Y, w, h, t.Rotation);
        var (l, top, r, b) = box.Extent();
        var area = new PixelRect((int)Math.Floor(l), (int)Math.Floor(top), (int)Math.Ceiling(r - Math.Floor(l)) + 1, (int)Math.Ceiling(b - Math.Floor(top)) + 1)
            .Intersect(target.Bounds);
        uint ink = Pixel.Opaque(t.Color);
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                var (lx, ly) = box.ToLocal(x + 0.5, y + 0.5);
                int line = (int)((ly - t.Y) / (t.Size * t.LineSpacing));
                if (line < 0 || line >= lines.Length || ly - t.Y - line * t.Size * t.LineSpacing > t.Size) continue;
                if (lx >= t.X && lx < t.X + lines[line].Length * t.Size * 0.5) target[x, y] = ink;
            }
        return new TextRenderResult(w, h, area, t.FontFamily == MissingFont ? "Segoe UI" : null);
    }
}

/// <summary>Mission 008 — The Designer's Toolkit: groups, masks, text, shapes, adjustment layers, assistants and v4 files.</summary>
public class DesignToolkitTests
{
    private static readonly ColorRgb Red = new(200, 40, 40), Blue = new(40, 60, 200), White = ColorRgb.White, Black = ColorRgb.Black;

    public DesignToolkitTests() => Document.TextRenderer = new FakeTextRenderer();

    private static Document Doc(int w = 64, int h = 48) => new(w, h, White);

    private static void FillLayer(Document doc, Layer layer, ColorRgb c, PixelRect? area = null)
    {
        var a = area ?? doc.Bounds;
        for (int y = a.Y; y < a.Bottom; y++)
            for (int x = a.X; x < a.Right; x++)
                layer.Image[x, y] = Pixel.Opaque(c);
        layer.RecalculateBounds();
        doc.RecompositeAll();
    }

    private static (byte R, byte G, byte B, byte A) At(Document doc, int x, int y) => Pixel.ToStraight(doc.Flatten()[x, y]);

    private static void Near((byte R, byte G, byte B, byte A) a, (byte R, byte G, byte B, byte A) b, int tolerance = 1)
    {
        Assert.True(Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance
                    && Math.Abs(a.A - b.A) <= tolerance, $"{a} differs from {b}");
    }

    // ================================================================== groups

    [Fact]
    public void A_group_composites_like_its_layers_and_its_opacity_and_visibility_apply_to_the_whole_group()
    {
        var doc = Doc();
        var a = doc.ActiveLayer;
        FillLayer(doc, a, Red, new PixelRect(0, 0, 32, 48));
        var b = doc.AddLayer("B");
        FillLayer(doc, b, Blue, new PixelRect(16, 0, 32, 48));
        doc.SetLayerProperties(b, b.Properties with { Opacity = 0.5 });
        var before = doc.Flatten();

        var group = doc.GroupLayers([a, b]);
        Assert.True(group.IsGroup);
        Assert.Equal(group.Id, a.ParentId);
        Assert.Equal(group.Id, b.ParentId);
        var grouped = doc.Flatten();
        for (int i = 0; i < before.Pixels.Length; i++) Near(Pixel.ToStraight(before.Pixels[i]), Pixel.ToStraight(grouped.Pixels[i]));

        doc.SetLayerProperties(group, group.Properties with { Visible = false });
        Assert.Equal((255, 255, 255, 255), At(doc, 20, 20)); // hidden group hides everything inside
        Assert.False(doc.IsShown(a));

        doc.SetLayerProperties(group, group.Properties with { Visible = true, Opacity = 0.5 });
        // The red area becomes half red over white as one unit.
        Near(At(doc, 4, 4), (228, 148, 148, 255), 2);

        doc.Undo();
        doc.Undo();
        doc.Undo();
        Assert.Equal(0, a.ParentId);
        Assert.DoesNotContain(doc.Layers, l => l.IsGroup);
        doc.Redo();
        Assert.Equal(doc.Layers.Single(l => l.IsGroup).Id, a.ParentId);
    }

    [Fact]
    public void Groups_nest_move_in_and_out_reorder_ungroup_duplicate_and_delete_as_single_undo_steps()
    {
        var doc = Doc();
        var a = doc.ActiveLayer;
        var b = doc.AddLayer("B");
        var c = doc.AddLayer("C");
        var outer = doc.GroupLayers([a, b], "Outer");
        var inner = doc.GroupLayers([b], "Inner");
        Assert.Equal(outer.Id, inner.ParentId);
        Assert.Equal(2, doc.DepthOf(b));

        Assert.True(doc.MoveIntoGroup(c, inner));
        Assert.Equal(inner.Id, c.ParentId);
        Assert.True(doc.MoveOutOfGroup(c));
        Assert.Equal(outer.Id, c.ParentId);
        Assert.False(doc.MoveIntoGroup(outer, inner)); // a group can't go inside itself

        // Order among siblings: C is above Inner in Outer; move it down.
        var siblings = doc.ChildrenOf(outer.Id);
        Assert.Equal(c, siblings[^1]);
        Assert.True(doc.MoveAmongSiblings(c, -1));
        Assert.Equal(c, doc.ChildrenOf(outer.Id)[^2]);

        int count = doc.Layers.Count;
        var copy = doc.DuplicateLayer(outer);
        Assert.Equal(count * 2, doc.Layers.Count);
        Assert.True(copy.IsGroup);
        Assert.Equal(5, doc.Subtree(copy).Count); // copies of the group, A, the inner group, B and C
        Assert.All(doc.Subtree(copy).Where(l => !ReferenceEquals(l, copy)), l => Assert.NotEqual(0, l.ParentId));
        Assert.DoesNotContain(doc.Subtree(copy), l => doc.Subtree(outer).Contains(l));
        doc.Undo();
        Assert.Equal(count, doc.Layers.Count);

        Assert.True(doc.Ungroup(inner));
        Assert.Equal(outer.Id, b.ParentId);
        doc.Undo();
        Assert.Equal(inner.Id, b.ParentId);

        Assert.False(doc.DeleteLayer(outer)); // it holds every layer: a picture keeps at least one
        doc.SetActiveLayer(outer);
        var d = doc.AddLayer("D");
        Assert.Equal(0, d.ParentId); // new layers go next to the active one, in its group
        Assert.True(doc.DeleteLayer(outer));
        Assert.Equal(d, Assert.Single(doc.Layers));
        doc.Undo();
        Assert.Equal(count + 1, doc.Layers.Count);
        Assert.Equal(outer.Id, b.ParentId == outer.Id ? outer.Id : doc.FindLayer(b.ParentId)!.ParentId);
    }

    [Fact]
    public void Merge_down_uses_the_layer_below_in_the_same_group_and_refuses_groups()
    {
        var doc = Doc();
        var a = doc.ActiveLayer;
        var group = doc.AddGroup();
        var inside = doc.AddLayer("Inside");
        Assert.True(doc.MoveIntoGroup(inside, group));
        Assert.Null(doc.LayerBelow(inside));
        Assert.NotNull(doc.MergeDownProblem(inside));
        Assert.NotNull(doc.MergeDownProblem(group)); // a group can't be merged down
        Assert.Equal(a, doc.LayerBelow(group));
    }

    // ================================================================== masks

    [Fact]
    public void A_mask_hides_without_erasing_can_be_painted_inverted_disabled_applied_and_undone()
    {
        var doc = Doc();
        var layer = doc.ActiveLayer;
        FillLayer(doc, layer, Red);
        var pixels = (uint[])layer.Image.Pixels.Clone();

        Assert.True(doc.AddMask(layer));
        Assert.True(doc.EditingMask); // a new mask is ready to paint on
        Assert.Equal((200, 40, 40, 255), At(doc, 10, 10)); // a white mask shows everything

        // Paint black on the mask: the layer is hidden there, its pixels untouched.
        Stroke(doc, Paint(Hard(12), Black), Line(5, 10, 40, 10, 2));
        Assert.Equal((255, 255, 255, 255), At(doc, 20, 10));
        Assert.Equal(pixels, layer.Image.Pixels);
        Assert.Equal((200, 40, 40, 255), At(doc, 20, 40));

        // Erasing the mask reveals again.
        Stroke(doc, Erase(Hard(12)), Line(5, 10, 40, 10, 2));
        Assert.Equal((200, 40, 40, 255), At(doc, 20, 10));
        doc.Undo();
        Assert.Equal((255, 255, 255, 255), At(doc, 20, 10));

        doc.SetLayerProperties(layer, layer.Properties with { MaskEnabled = false });
        Assert.Equal((200, 40, 40, 255), At(doc, 20, 10));
        doc.Undo();

        Assert.True(doc.InvertMask(layer));
        Assert.Equal((200, 40, 40, 255), At(doc, 20, 10));
        Assert.Equal((255, 255, 255, 255), At(doc, 20, 40));
        doc.Undo();

        var shown = doc.Flatten();
        Assert.True(doc.ApplyMask(layer));
        Assert.False(layer.HasMask);
        Assert.False(doc.EditingMask);
        Assert.Equal(shown.Pixels, doc.Flatten().Pixels);
        Assert.Equal(0u, layer.Image[20, 10]); // now really erased
        doc.Undo();
        Assert.True(layer.HasMask);
        Assert.Equal(pixels, layer.Image.Pixels);

        Assert.True(doc.DeleteMask(layer));
        Assert.Equal((200, 40, 40, 255), At(doc, 20, 10));
        doc.Undo();
        Assert.True(layer.HasMask);
    }

    [Fact]
    public void A_mask_from_the_selection_shows_only_the_selection_and_never_takes_palette_colours()
    {
        var doc = Doc();
        var layer = doc.ActiveLayer;
        FillLayer(doc, layer, Red);
        doc.SetSelection(SelectionMask.Rectangle(doc.Width, doc.Height, new PixelRect(0, 0, 10, 10)), "Select");
        doc.LockToPalette(new Palette("Two", [Red, Blue]));
        Assert.True(doc.AddMask(layer, fromSelection: true));
        Assert.Equal((200, 40, 40, 255), At(doc, 5, 5));
        Assert.Equal((255, 255, 255, 255), At(doc, 30, 30));
        Assert.Contains(layer.Mask!.Pixels, p => p == Pixel.Opaque(Black)); // grey values kept, not conformed
    }

    // ================================================================== text

    [Fact]
    public void Text_layers_stay_editable_undoable_and_keep_a_missing_font_named()
    {
        var doc = Doc(200, 100);
        var text = doc.AddTextLayer(new TextContent("PLAY", Size: 20, Color: Blue, X: 10, Y: 10));
        Assert.Equal(LayerKind.Text, text.Kind);
        Assert.Equal("PLAY", text.Name);
        Assert.Equal((40, 60, 200, 255), At(doc, 15, 15));
        Assert.Throws<InvalidOperationException>(() => doc.BeginEdit(text, "Paint")); // never rasterized by a brush

        var t = (TextContent)text.Content!;
        Assert.True(doc.SetContent(text, t with { Text = "PLAY GAME", Color = Red }, "Edit text"));
        Assert.Equal((200, 40, 40, 255), At(doc, 15, 15));
        Assert.Equal(90, text.ContentSize.Width, 3);
        doc.Undo();
        Assert.Equal("PLAY", ((TextContent)text.Content!).Text);
        Assert.Equal((40, 60, 200, 255), At(doc, 15, 15));

        Assert.True(doc.MoveObjects([text], 50, 20));
        Assert.Equal((255, 255, 255, 255), At(doc, 15, 15));
        Assert.Equal((40, 60, 200, 255), At(doc, 65, 35));

        doc.SetContent(text, (TextContent)text.Content! with { FontFamily = FakeTextRenderer.MissingFont }, "Font");
        var missing = Assert.Single(doc.MissingFonts);
        Assert.Equal((text.Name, FakeTextRenderer.MissingFont, "Segoe UI"), missing);
        Assert.Equal(FakeTextRenderer.MissingFont, ((TextContent)text.Content!).FontFamily); // still asks for it

        var raster = doc.ConvertToPixels(text)!;
        Assert.True(raster.IsPaintable);
        Assert.Equal((40, 60, 200, 255), At(doc, 65, 35));
        doc.Undo();
        Assert.Equal(LayerKind.Text, doc.ActiveLayer.Kind);
    }

    // ================================================================== shapes

    [Fact]
    public void Shapes_render_antialiased_fill_and_outline_and_rotate_resize_duplicate_and_align()
    {
        var doc = Doc(100, 100);
        var rect = doc.AddShapeLayer(new ShapeContent(ShapeType.Rectangle, 20, 20, 40, 20, Red, Black, StrokeWidth: 4));
        Assert.Equal((200, 40, 40, 255), At(doc, 40, 30));      // fill
        Assert.Equal((0, 0, 0, 255), At(doc, 20, 30));          // outline centred on the edge
        Assert.Equal((255, 255, 255, 255), At(doc, 10, 10));    // outside
        var edge = Pixel.A(rect.Image[40, 17]);                 // 3 px above the edge: outside the 2 px half outline
        Assert.Equal(0, edge);

        var s = (ShapeContent)rect.Content!;
        doc.SetContent(rect, s with { Rotation = 90 }, "Rotate");
        Assert.Equal((200, 40, 40, 255), At(doc, 40, 40)); // a 40 × 20 box turned upright reaches y = 50
        Assert.Equal((255, 255, 255, 255), At(doc, 22, 30)); // and no longer reaches x = 22
        doc.Undo();

        var ellipse = doc.AddShapeLayer(new ShapeContent(ShapeType.Ellipse, 0, 60, 30, 30, Blue));
        Assert.Equal((40, 60, 200, 255), At(doc, 15, 75));
        Assert.True(Pixel.A(ellipse.Image[1, 61]) == 0); // corner of the box is outside the ellipse
        byte partial = Pixel.A(ellipse.Image.Pixels.Where(p => p != 0).OrderBy(Pixel.A).First());
        Assert.InRange(partial, 1, 254); // antialiased edge

        var copy = doc.DuplicateLayer(ellipse);
        Assert.Equal(ellipse.Content, copy.Content);
        Assert.True(doc.AlignObjects([copy], AlignEdge.Right, toCanvas: true));
        Assert.Equal(70, ((ShapeContent)copy.Content!).X, 6);
        Assert.True(doc.AlignObjects([rect, ellipse], AlignEdge.Left, toCanvas: false));
        Assert.Equal(0, ((ShapeContent)rect.Content!).X, 6);
        doc.Undo();
        Assert.Equal(20, ((ShapeContent)rect.Content!).X, 6);

        var line = doc.AddShapeLayer(new ShapeContent(ShapeType.Line, 10, 95, 80, 0, null, Black, StrokeWidth: 2));
        Assert.Equal((0, 0, 0, 255), At(doc, 50, 95));
        var polygon = doc.AddShapeLayer(new ShapeContent(ShapeType.Polygon, 70, 0, 30, 30, Blue, Sides: 3));
        Assert.Equal((40, 60, 200, 255), At(doc, 85, 20));
        Assert.Equal(0, Pixel.A(polygon.Image[71, 1])); // a triangle's top corners are empty
        Assert.Equal(PixelRect.Empty, ShapeRasterizer.Render(new ShapeContent(ShapeType.Rectangle, 0, 0, 10, 10, null), 20, 20).Bounds);
    }

    [Fact]
    public void Shape_shadow_sits_under_the_shape()
    {
        var doc = Doc(80, 80);
        var shape = doc.AddShapeLayer(new ShapeContent(ShapeType.Rectangle, 10, 10, 30, 30, Red, Shadow: new ShadowEffect(Black, 6, 6, 0, 1)));
        Assert.Equal((200, 40, 40, 255), At(doc, 20, 20));
        Assert.Equal((0, 0, 0, 255), At(doc, 43, 43));  // only the shadow is there
        Assert.True(shape.ContentBounds.Contains(new PixelRect(10, 10, 36, 36)));
    }

    // ================================================================== adjustment layers

    [Fact]
    public void An_adjustment_layer_changes_everything_below_in_its_group_without_changing_pixels_and_stays_editable()
    {
        var doc = Doc();
        var a = doc.ActiveLayer;
        FillLayer(doc, a, new ColorRgb(100, 120, 140));
        var pixels = (uint[])a.Image.Pixels.Clone();
        var adj = new BrightnessContrast(30, 10);
        var layer = doc.AddAdjustmentLayer(adj);
        Assert.Equal(LayerKind.Adjustment, layer.Kind);
        Assert.Equal(1, layer.Image.Width); // no pixel buffer

        var expected = new RasterImage(doc.Width, doc.Height);
        adj.Apply(new Document(doc.Width, doc.Height, null, true, [(new LayerProperties("x"), a.Image.Clone())]).Flatten(), expected, expected.Bounds);
        Near(At(doc, 5, 5), Pixel.ToStraight(expected[5, 5]));
        Assert.Equal(pixels, a.Image.Pixels);

        doc.SetContent(layer, new AdjustmentContent(new HueSaturation(0, -100, 0)), "Hue/Saturation");
        var (r, g, b, _) = At(doc, 5, 5);
        Assert.True(Math.Abs(r - g) <= 1 && Math.Abs(g - b) <= 1); // greyscale
        doc.Undo();

        doc.SetLayerProperties(layer, layer.Properties with { Opacity = 0 });
        Assert.Equal((100, 120, 140, 255), At(doc, 5, 5));
        doc.Undo();

        // In a group it only changes the group's layers.
        var top = doc.AddLayer("Top");
        FillLayer(doc, top, new ColorRgb(100, 120, 140), new PixelRect(0, 0, 8, 8));
        var group = doc.GroupLayers([top]);
        doc.SetActiveLayer(top); // a new layer goes next to the active one, in its group
        var inner = doc.AddAdjustmentLayer(new Levels(0, 255, 1, 0, 128));
        Assert.Equal(group.Id, inner.ParentId);
        var (r2, _, _, _) = At(doc, 2, 2);
        Assert.InRange(r2, 40, 60); // the top square darkened
        Near(At(doc, 30, 30), Pixel.ToStraight(expected[30, 30])); // below the group: unchanged by the inner levels
    }

    [Fact]
    public void Levels_and_colour_balance_map_colours_as_documented()
    {
        Assert.Equal(Pixel.Opaque(new ColorRgb(0, 0, 255)), new Levels(0, 255, 1, 0, 255).MapPixel(Pixel.Opaque(new ColorRgb(0, 0, 255))));
        var lv = new Levels(50, 200).MapPixel(Pixel.Opaque(new ColorRgb(50, 125, 200)));
        Assert.Equal(0, Pixel.R(lv));
        Assert.InRange(Pixel.G(lv), 127, 128);
        Assert.Equal(255, Pixel.B(lv));
        var warm = new ColourBalance(40, 0, -40).MapPixel(Pixel.Opaque(new ColorRgb(128, 128, 128)));
        Assert.True(Pixel.R(warm) > 128 && Pixel.B(warm) > 128 == false);
        Assert.Equal(0u, new ColourBalance(40, 0, 0).MapPixel(0));
    }

    // ================================================================== whole-picture operations

    [Fact]
    public void Flip_rotate_resize_and_crop_move_masks_and_objects_and_undo_restores_them()
    {
        var doc = Doc(100, 50);
        var shape = doc.AddShapeLayer(new ShapeContent(ShapeType.Rectangle, 10, 10, 20, 10, Red));
        var text = doc.AddTextLayer(new TextContent("Hi", Size: 10, Color: Blue, X: 60, Y: 5));
        doc.AddMask(shape);
        doc.SetEditingMask(false);
        shape.Mask![0, 0] = Pixel.Opaque(Black);
        var before = (ShapeContent)shape.Content!;

        ImageOperations.Flip(doc, horizontal: true);
        var flipped = (ShapeContent)shape.Content!;
        Assert.Equal(70, flipped.X, 6);
        Assert.Equal(Pixel.Opaque(Black), shape.Mask![99, 0]);
        Assert.Equal(30, ((TextContent)text.Content!).X, 6); // text moves but stays readable
        Assert.Equal((200, 40, 40, 255), At(doc, 80, 15));

        ImageOperations.Rotate(doc, 1);
        var rotated = (ShapeContent)shape.Content!;
        Assert.Equal(90, rotated.Rotation, 6);
        Assert.Equal((50, 100), (doc.Width, doc.Height));

        doc.Undo();
        doc.Undo();
        Assert.Equal(before, shape.Content);
        Assert.Equal(Pixel.Opaque(Black), shape.Mask![0, 0]);

        ImageOperations.ResizeImage(doc, 200, 100, Interpolation.Bilinear);
        var big = (ShapeContent)shape.Content!;
        Assert.Equal((20, 20, 40, 20), (big.X, big.Y, big.Width, big.Height));
        Assert.Equal(20, ((TextContent)text.Content!).Size, 6);
        Assert.Equal(200, shape.Mask!.Width);
        doc.Undo();

        ImageOperations.Crop(doc, new PixelRect(5, 5, 50, 30));
        Assert.Equal(5, ((ShapeContent)shape.Content!).X, 6);
        doc.Undo();
        Assert.Equal(before, shape.Content);
    }

    // ================================================================== palette lock

    [Fact]
    public void Palette_lock_keeps_text_shapes_and_adjustment_results_to_the_palette_with_alpha_untouched()
    {
        var doc = Doc(80, 80);
        doc.LockToPalette(new Palette("P", [Red, Blue, White, Black]));
        var shape = doc.AddShapeLayer(new ShapeContent(ShapeType.Ellipse, 5, 5, 50, 50, new ColorRgb(210, 50, 45)));
        var c = doc.PaletteConformer!;
        Assert.All(shape.Image.Pixels.Where(p => p != 0), p => Assert.True(c.IsPaletteColour(p)));
        Assert.Contains(shape.Image.Pixels, p => Pixel.A(p) is > 0 and < 255); // antialiasing alpha kept
        Assert.Equal(new ColorRgb(210, 50, 45), ((ShapeContent)shape.Content!).Fill); // the content keeps what was asked

        doc.AddAdjustmentLayer(new HueSaturation(120, 0, 0));
        var flat = doc.Flatten();
        Assert.All(flat.Pixels, p => Assert.True(c.IsPaletteColour(p) || p == 0));

        doc.UnlockPalette();
        Assert.Contains(shape.Image.Pixels, p => p == Pixel.Opaque(new ColorRgb(210, 50, 45)));

        // Remap changes an object's own colours.
        var remap = PaletteRemap.Analyse(doc, [Blue, White]);
        remap.Apply(doc, "Remapped", lockAfter: true);
        Assert.NotEqual(new ColorRgb(210, 50, 45), ((ShapeContent)shape.Content!).Fill);
        Assert.Contains(((ShapeContent)shape.Content!).Fill!.Value, new[] { Blue, White });
    }

    // ================================================================== animation

    [Fact]
    public void Text_shapes_and_groups_are_static_in_animations_and_frame_timing_and_holds_survive()
    {
        var doc = SampleAnimations.BouncingBall();
        int frames = doc.FrameCount;
        var durations = Enumerable.Range(0, frames).Select(doc.Animation!.DurationOverride).ToList();
        var holds = doc.Layers.Where(l => l.IsTrack).Select(l => l.Track.ToList()).ToList();

        var title = doc.AddTextLayer(new TextContent("Bounce!", Size: 24, Color: Red, X: 10, Y: 10));
        Assert.False(title.IsTrack);
        Assert.Empty(title.Track);
        doc.NewFrame();
        Assert.Equal(frames + 1, doc.FrameCount);
        Assert.False(title.IsTrack);
        doc.Undo();
        Assert.Equal(frames, doc.FrameCount);

        for (int f = 0; f < frames; f++)
            Assert.Equal((200, 40, 40, 255), Pixel.ToStraight(doc.RenderFrame(f)[15, 15]));

        var ms = new MemoryStream();
        DnkFile.Write(doc, ms);
        ms.Position = 0;
        var back = DnkFile.Read(ms);
        Assert.Equal(frames, back.FrameCount);
        Assert.Equal(durations, Enumerable.Range(0, frames).Select(back.Animation!.DurationOverride).ToList());
        var backTracks = back.Layers.Where(l => l.IsTrack).ToList();
        Assert.Equal(holds.Count, backTracks.Count);
        for (int i = 0; i < holds.Count; i++)
        {
            var orig = holds[i];
            var loaded = backTracks[i].Track;
            for (int f = 0; f < frames; f++)
                for (int g = 0; g < frames; g++)
                    Assert.Equal(ReferenceEquals(orig[f], orig[g]) && orig[f] is not null, ReferenceEquals(loaded[f], loaded[g]) && loaded[f] is not null);
        }
        Assert.Equal(LayerKind.Text, back.Layers.Single(l => l.Name == "Bounce!").Kind);
        Assert.Equal(doc.RenderFrame(3).Pixels, back.RenderFrame(3).Pixels);
    }

    [Fact]
    public void Merging_text_down_onto_an_animation_layer_merges_it_into_every_drawing()
    {
        var doc = SampleAnimations.BouncingBall();
        var below = doc.Layers.Last(l => l.IsTrack);
        doc.SetActiveLayer(below);
        var text = doc.AddTextLayer(new TextContent("X", Size: 10, Color: Red, X: 0, Y: 0));
        Assert.Null(doc.MergeDownProblem(text));
        Assert.True(doc.MergeDown(text));
        for (int f = 0; f < doc.FrameCount; f++)
            if (doc.CelAt(below, f) is { } cel) Assert.Equal(Pixel.Opaque(Red), cel.Image[2, 2]);
        doc.Undo();
        Assert.Contains(doc.Layers, l => l.Kind == LayerKind.Text);
    }

    // ================================================================== the screen stays right

    [Fact]
    public void After_every_operation_undo_and_redo_the_screen_shows_exactly_the_composite()
    {
        var doc = Doc(120, 90);
        void Same(string after)
        {
            var flat = doc.Flatten().Pixels;
            var shown = doc.Projection.Pixels;
            Assert.True(flat.AsSpan().SequenceEqual(shown), $"the screen is stale after {after}");
        }
        var steps = new (string Name, Action Do)[]
        {
            ("painting", () => FillLayer(doc, doc.ActiveLayer, Blue, new PixelRect(0, 0, 60, 90))),
            ("a shape", () => doc.AddShapeLayer(new ShapeContent(ShapeType.Ellipse, 10, 10, 50, 40, Red))),
            ("text", () => doc.AddTextLayer(new TextContent("Hi", Size: 20, Color: Black, X: 70, Y: 10))),
            ("an adjustment layer", () => doc.AddAdjustmentLayer(new HueSaturation(90, 0, 0))),
            ("editing the adjustment", () => doc.SetContent(doc.ActiveLayer, new AdjustmentContent(new BrightnessContrast(40, 20)), "Edit")),
            ("a group", () => doc.GroupLayers([doc.Layers.First(l => l.Kind == LayerKind.Shape)])),
            ("group opacity", () => doc.SetLayerProperties(doc.Layers.First(l => l.IsGroup), doc.Layers.First(l => l.IsGroup).Properties with { Opacity = 0.5 })),
            ("a mask", () => doc.AddMask(doc.Layers[0])),
            ("inverting the mask", () => doc.InvertMask(doc.Layers[0])),
            ("moving text", () => doc.MoveObjects([doc.Layers.First(l => l.Kind == LayerKind.Text)], 10, 30)),
            ("ungrouping", () => doc.Ungroup(doc.Layers.First(l => l.IsGroup))),
            ("deleting the adjustment layer", () => doc.DeleteLayer(doc.Layers.First(l => l.Kind == LayerKind.Adjustment))),
            ("converting text to pixels", () => doc.ConvertToPixels(doc.Layers.First(l => l.Kind == LayerKind.Text))),
            ("flipping the picture", () => ImageOperations.Flip(doc, horizontal: true)),
        };
        foreach (var (name, run) in steps)
        {
            run();
            Same(name);
            doc.Undo();
            Same($"undoing {name}");
            doc.Redo();
            Same($"redoing {name}");
        }
    }

    // ================================================================== files

    [Fact]
    public void Version_4_is_written_only_when_needed_and_round_trips_groups_masks_objects_guides_and_perspective()
    {
        var plain = Doc();
        Assert.False(plain.UsesDesignFeatures);
        Assert.Equal(DnkFile.StillVersion, DnkFile.Snapshot(plain).Manifest.Version);

        var doc = Doc(120, 90);
        FillLayer(doc, doc.ActiveLayer, Blue, new PixelRect(0, 0, 60, 90));
        var art = doc.ActiveLayer;
        doc.AddMask(art);
        art.Mask![5, 5] = Pixel.Opaque(Black);
        var group = doc.GroupLayers([art], "Character");
        doc.SetLayerProperties(group, group.Properties with { Collapsed = true, Opacity = 0.8 });
        doc.AddTextLayer(new TextContent("Title\nline two", "Georgia", 16, Red, Bold: true, Italic: true, TextAlign.Center, 5, 6, 12, 1.5, 1.4, 90,
            new OutlineEffect(White, 3), new ShadowEffect(Black, 2, 3, 4, 0.5)));
        doc.AddShapeLayer(new ShapeContent(ShapeType.RoundedRectangle, 30, 40, 50, 20, Red, Black, 3, 15, 8, 6, new ShadowEffect(Black)));
        doc.AddAdjustmentLayer(new ColourBalance(10, -20, 30));
        doc.SetGuides([new Guide(true, 60.5), new Guide(false, 45)], "Add guide");
        doc.GuidesLocked = true;
        doc.SetPerspective(new PerspectiveGuide(2, -10, 30, 130, 30));
        Assert.True(doc.UsesDesignFeatures);

        var ms = new MemoryStream();
        DnkFile.Write(doc, ms);
        ms.Position = 0;
        var back = DnkFile.Read(ms);

        Assert.Equal(doc.Layers.Select(l => (l.Kind, l.Name, l.Visible, l.Opacity, l.Properties.Collapsed)),
            back.Layers.Select(l => (l.Kind, l.Name, l.Visible, l.Opacity, l.Properties.Collapsed)));
        Assert.Equal(doc.Layers.Select(l => l.ParentId == 0 ? -1 : doc.IndexOf(doc.FindLayer(l.ParentId)!)),
            back.Layers.Select(l => l.ParentId == 0 ? -1 : back.IndexOf(back.FindLayer(l.ParentId)!)));
        Assert.Equal(doc.Layers.Select(l => l.Content), back.Layers.Select(l => l.Content));
        Assert.Equal(art.Mask!.Pixels, back.Layers[0].Mask!.Pixels);
        Assert.Equal(doc.Guides, back.Guides);
        Assert.True(back.GuidesLocked);
        Assert.Equal(doc.Perspective, back.Perspective);
        Assert.Equal(doc.Flatten().Pixels, back.Flatten().Pixels);
    }

    [Fact]
    public void A_newer_format_is_refused_and_mission_007_files_still_open()
    {
        var doc = Doc();
        var snap = DnkFile.Snapshot(doc);
        snap.Manifest.Version = DnkFile.CurrentVersion + 1;
        var ms = new MemoryStream();
        DnkFile.Write(snap, ms);
        ms.Position = 0;
        Assert.Contains("newer DoodleNook", Assert.Throws<DnkFormatException>(() => DnkFile.Read(ms)).Message);

        // A version 3 (palette-locked) picture opens unchanged.
        var locked = Doc();
        FillLayer(locked, locked.ActiveLayer, Red);
        locked.LockToPalette(new Palette("P", [Red, Blue]));
        var v3 = new MemoryStream();
        DnkFile.Write(locked, v3);
        v3.Position = 0;
        var back = DnkFile.Read(v3);
        Assert.True(back.PaletteLocked);
        Assert.Equal(locked.Flatten().Pixels, back.Flatten().Pixels);
    }

    [Fact]
    public void Guides_are_undoable_and_never_part_of_the_picture()
    {
        var doc = Doc();
        var flat = doc.Flatten();
        doc.SetGuides([new Guide(true, 10)], "Add guide");
        Assert.Single(doc.Guides);
        Assert.Equal(flat.Pixels, doc.Flatten().Pixels);
        doc.Undo();
        Assert.Empty(doc.Guides);
        doc.Redo();
        Assert.Single(doc.Guides);
    }

    // ================================================================== assistants

    [Fact]
    public void Symmetry_at_the_canvas_centre_paints_exactly_as_before_and_a_moved_origin_or_radial_symmetry_repeats_around_it()
    {
        Document Draw(Symmetry sym, SymmetryPlacement placement)
        {
            var d = new Document(80, 60, null);
            Stroke(d, Paint(Hard(6)) with { Symmetry = sym, Placement = placement }, Line(10, 10, 30, 25, 2));
            return d;
        }
        foreach (var sym in new[] { Symmetry.Vertical, Symmetry.Horizontal, Symmetry.Both })
            Assert.Equal(Draw(sym, default).ActiveLayer.Image.Pixels, Draw(sym, new SymmetryPlacement(40, 30)).ActiveLayer.Image.Pixels);

        var moved = Draw(Symmetry.Vertical, new SymmetryPlacement(30, null));
        Assert.True(Pixel.A(moved.ActiveLayer.Image[50, 10]) > 0); // mirrored around x = 30
        Assert.Equal(0, Pixel.A(moved.ActiveLayer.Image[70, 10]));

        var radial = new Document(80, 80, null);
        Stroke(radial, Paint(Hard(4)) with { Symmetry = Symmetry.Radial, Placement = new SymmetryPlacement(40, 40, 4) }, S(40, 10));
        foreach (var (x, y) in new[] { (40, 10), (70, 40), (40, 70), (10, 40) })
            Assert.True(Pixel.A(radial.ActiveLayer.Image[x, y]) > 200, $"no dab at {x},{y}");

        var pixel = new Document(16, 16, null);
        var ps = new PixelStroke(pixel);
        ps.Begin(new PixelStrokeStyle(Black, Symmetry: Symmetry.Vertical, Placement: new SymmetryPlacement(5, null)), 1, 1);
        ps.End();
        Assert.NotEqual(0u, pixel.ActiveLayer.Image[8, 1]); // 2·5 − 1 − 1
        var centre = new Document(16, 16, null);
        ps = new PixelStroke(centre);
        ps.Begin(new PixelStrokeStyle(Black, Symmetry: Symmetry.Vertical), 1, 1);
        ps.End();
        Assert.NotEqual(0u, centre.ActiveLayer.Image[14, 1]); // the canvas centre, as before
    }

    [Fact]
    public void Perspective_picks_the_line_towards_a_vanishing_point_and_holds_the_stroke_to_it()
    {
        var two = new PerspectiveGuide(2, 0, 50, 200, 50);
        var d = Perspective.Choose(two, 100, 100, 95, 97)!.Value; // moving up-left: towards V1
        var (x, y) = Perspective.Project(100, 100, d, 50, 90);
        Assert.Equal(1.0, Math.Abs((y - 100) / (x - 100) * 2), 3); // on the line from (100,100) to (0,50)
        var vertical = Perspective.Choose(two, 100, 100, 101, 140)!.Value;
        Assert.Equal((0.0, 1.0), vertical);
        var one = new PerspectiveGuide(1, 100, 40, 0, 0);
        Assert.Equal((1.0, 0.0), Perspective.Choose(one, 20, 80, 60, 81)!.Value);
        Assert.Null(Perspective.Choose(PerspectiveGuide.Off, 0, 0, 5, 5));
        Assert.NotEmpty(Perspective.Rays(two, 200, 100));
    }

    [Fact]
    public void Snapping_sticks_to_canvas_guides_objects_and_grid_within_the_tolerance()
    {
        var doc = Doc(100, 100);
        doc.SetGuides([new Guide(true, 33)], "Guide");
        var shape = doc.AddShapeLayer(new ShapeContent(ShapeType.Rectangle, 60, 60, 20, 20, Red));
        var targets = SnapTargets.For(doc, new SnapOptions(Grid: 8), exclude: null);
        Assert.Equal(50, targets.Nearest(51.5, vertical: true, 3)); // canvas centre
        Assert.Equal(33, targets.Nearest(35, vertical: true, 3));   // guide
        Assert.Equal(80, targets.Nearest(81, vertical: false, 3));  // shape edge
        Assert.Equal(16, targets.Nearest(16.4, vertical: true, 3)); // grid
        Assert.Null(targets.Nearest(25, vertical: true, 0.2));

        var excluding = SnapTargets.For(doc, new SnapOptions(Canvas: false, Guides: false), shape);
        Assert.Empty(excluding.Xs);
        var (dx, dy, lx, _) = Snapping.Box(SnapTargets.For(doc, new SnapOptions()), 31, 0, 41, 10, 3);
        Assert.Equal(2, dx, 6);
        Assert.Equal(33, lx);
        Assert.Equal(0, dy, 6);
        Assert.Equal((30.0, 45.0), StraightLine.Constrain(0, 45, 30, 47, snapAngle: true) is var p ? (Math.Round(p.X), Math.Round(p.Y)) : default);
    }
}
