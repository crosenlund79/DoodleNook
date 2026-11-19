using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public class OperationTests
{
    private static readonly ColorRgb Red = new(220, 30, 30);
    private static readonly ColorRgb Green = new(30, 180, 60);

    private static Document Boxes()
    {
        // Two separate red boxes on a transparent layer.
        var doc = new Document(100, 50);
        var px = doc.ActiveLayer.Image;
        for (int y = 10; y < 40; y++)
            for (int x = 10; x < 40; x++) { px[x, y] = Pixel.Opaque(Red); px[x + 50, y] = Pixel.Opaque(Red); }
        doc.ActiveLayer.RecalculateBoundsForTest();
        doc.RecompositeAll();
        return doc;
    }

    // ---------------------------------------------------------------- fill

    [Fact]
    public void Contiguous_fill_stays_inside_the_region_and_global_fill_reaches_all_matches()
    {
        var doc = Boxes();
        Assert.True(FloodFill.Apply(doc, doc.ActiveLayer, 20, 20, Green, new FillOptions(Tolerance: 10)));
        Assert.Equal(Pixel.Opaque(Green), doc.ActiveLayer.Image[20, 20]);
        Assert.Equal(Pixel.Opaque(Red), doc.ActiveLayer.Image[70, 20]);
        Assert.Equal(0u, doc.ActiveLayer.Image[45, 20]);
        doc.Undo();
        Assert.True(FloodFill.Apply(doc, doc.ActiveLayer, 20, 20, Green, new FillOptions(Tolerance: 10, Contiguous: false)));
        Assert.Equal(Pixel.Opaque(Green), doc.ActiveLayer.Image[70, 20]);
    }

    [Fact]
    public void Fill_tolerance_decides_which_colours_count_as_similar()
    {
        var doc = new Document(30, 10);
        for (int x = 0; x < 30; x++) doc.ActiveLayer.Image[x, 5] = Pixel.Opaque(new ColorRgb((byte)(100 + x * 2), 0, 0));
        doc.ActiveLayer.RecalculateBoundsForTest();
        var tight = FloodFill.Region(doc.ActiveLayer.Image, 0, 5, 5, true);
        var loose = FloodFill.Region(doc.ActiveLayer.Image, 0, 5, 40, true);
        Assert.Equal(3, Enumerable.Range(0, 30).Count(x => tight[5 * 30 + x] == 255));
        Assert.Equal(21, Enumerable.Range(0, 30).Count(x => loose[5 * 30 + x] == 255));
    }

    [Fact]
    public void Fill_respects_the_selection_and_exact_mode_sets_exact_pixels()
    {
        var doc = new Document(40, 20);
        doc.SetSelection(SelectionMask.Rectangle(40, 20, new PixelRect(0, 0, 20, 20)), "Select");
        FloodFill.Apply(doc, doc.ActiveLayer, 5, 5, Green, new FillOptions(Exact: true));
        Assert.Equal(Pixel.Opaque(Green), doc.ActiveLayer.Image[19, 10]);
        Assert.Equal(0u, doc.ActiveLayer.Image[20, 10]);
    }

    // ---------------------------------------------------------------- gradient and shapes

    [Fact]
    public void Linear_gradient_runs_from_start_to_end_colour()
    {
        var doc = new Document(101, 10);
        var g = new GradientOperation(doc, doc.ActiveLayer, ColorRgb.Black, ColorRgb.White, new GradientOptions());
        g.Update(0, 5, 101, 5);
        Assert.True(g.Commit());
        Assert.InRange(Pixel.R(doc.ActiveLayer.Image[0, 5]), 0, 3);
        Assert.InRange(Pixel.R(doc.ActiveLayer.Image[50, 5]), 124, 131);
        Assert.InRange(Pixel.R(doc.ActiveLayer.Image[100, 5]), 252, 255);
        Assert.True(doc.Undo());
        Assert.True(doc.ActiveLayer.Image.IsBlank());
    }

    [Fact]
    public void Gradient_to_transparent_fades_alpha_and_radial_grows_from_the_centre()
    {
        var doc = new Document(100, 100);
        var g = new GradientOperation(doc, doc.ActiveLayer, Red, Red, new GradientOptions(GradientShape.Radial, ToTransparent: true));
        g.Update(50, 50, 90, 50);
        g.Commit();
        Assert.True(Pixel.A(doc.ActiveLayer.Image[50, 50]) > 240);
        Assert.InRange(Pixel.A(doc.ActiveLayer.Image[70, 50]), 110, 145);
        Assert.Equal(0, Pixel.A(doc.ActiveLayer.Image[95, 50]));
        Assert.InRange(Pixel.A(doc.ActiveLayer.Image[50, 70]) - Pixel.A(doc.ActiveLayer.Image[70, 50]), -3, 3);
    }

    [Fact]
    public void Shape_preview_redraws_cleanly_and_pixel_lines_are_exact()
    {
        var doc = new Document(50, 50);
        var line = new ShapeOperation(doc, doc.ActiveLayer, Red, new ShapeOptions(ShapeKind.Line, Width: 1, AntiAlias: false));
        line.Update(0, 0, 49, 0);
        line.Update(0, 0, 9, 9); // the first preview must vanish
        Assert.True(line.Commit());
        var painted = Enumerable.Range(0, 2500).Where(i => doc.ActiveLayer.Image.Pixels[i] != 0).ToArray();
        Assert.Equal(10, painted.Length);
        Assert.All(painted, i => Assert.Equal(Pixel.Opaque(Red), doc.ActiveLayer.Image.Pixels[i]));
        Assert.Equal(Pixel.Opaque(Red), doc.ActiveLayer.Image[9, 9]);
    }

    [Fact]
    public void Smooth_rectangle_outline_and_filled_ellipse()
    {
        var doc = new Document(100, 100);
        var rect = new ShapeOperation(doc, doc.ActiveLayer, Red, new ShapeOptions(ShapeKind.Rectangle, Width: 4));
        rect.Update(20, 20, 80, 80);
        rect.Commit();
        Assert.Equal(255, Pixel.A(doc.ActiveLayer.Image[20, 50]));
        Assert.Equal(0, Pixel.A(doc.ActiveLayer.Image[50, 50]));
        var ellipse = new ShapeOperation(doc, doc.ActiveLayer, Green, new ShapeOptions(ShapeKind.Ellipse, Filled: true));
        ellipse.Update(30, 30, 70, 70);
        ellipse.Commit();
        Assert.Equal(Pixel.Opaque(Green), doc.ActiveLayer.Image[50, 50]);
        Assert.Equal(0, Pixel.A(doc.ActiveLayer.Image[31, 31]));
        Assert.Equal(2, doc.History.UndoCount);
    }

    // ---------------------------------------------------------------- selections

    [Fact]
    public void Selection_shapes_and_modes()
    {
        var doc = new Document(100, 100);
        var rect = Selections.Rectangle(doc, 10, 10, 60, 60);
        Assert.Equal(new PixelRect(10, 10, 50, 50), rect.Bounds);
        var ellipse = Selections.Ellipse(doc, 40, 40, 90, 90, antialias: true);
        Assert.Equal(255, ellipse[65, 65]);
        Assert.Equal(0, ellipse[41, 41]);
        Assert.InRange(ellipse[47, 47], 1, 254); // soft edge

        var add = SelectionMask.Combine(rect, ellipse, SelectionMode.Add)!;
        Assert.Equal(255, add[15, 15]);
        Assert.Equal(255, add[80, 65]);
        var sub = SelectionMask.Combine(rect, ellipse, SelectionMode.Subtract)!;
        Assert.Equal(255, sub[15, 15]);
        Assert.Equal(0, sub[55, 55]);
        var inter = SelectionMask.Combine(rect, ellipse, SelectionMode.Intersect)!;
        Assert.Equal(255, inter[55, 55]);
        Assert.Equal(0, inter[15, 15]);
        Assert.Equal(0, rect.Inverted()[15, 15]);
        Assert.Equal(255, rect.Inverted()[5, 5]);
    }

    [Fact]
    public void Lasso_and_magic_wand()
    {
        var doc = Boxes();
        var lasso = Selections.Lasso(doc, [(0, 0), (50, 0), (0, 50)], antialias: false);
        Assert.Equal(255, lasso[5, 5]);
        Assert.Equal(0, lasso[45, 45]);
        var wand = Selections.MagicWand(doc, doc.ActiveLayer, 20, 20, 10, contiguous: true, sampleMerged: false);
        Assert.Equal(new PixelRect(10, 10, 30, 30), wand.Bounds);
        var global = Selections.MagicWand(doc, doc.ActiveLayer, 20, 20, 10, contiguous: false, sampleMerged: false);
        Assert.Equal(new PixelRect(10, 10, 80, 30), global.Bounds);
    }

    [Fact]
    public void Selection_changes_are_undoable_and_outline_follows_the_edge()
    {
        var doc = new Document(20, 20);
        doc.SetSelection(SelectionMask.Rectangle(20, 20, new PixelRect(5, 5, 4, 3)), "Rectangle select");
        var outline = doc.Selection!.Outline();
        Assert.Equal(2 * 4 + 2 * 3, outline.Sum(s => Math.Abs(s.X1 - s.X0) + Math.Abs(s.Y1 - s.Y0)));
        doc.SetSelection(null, "Deselect");
        Assert.Null(doc.Selection);
        doc.Undo();
        Assert.NotNull(doc.Selection);
        doc.Undo();
        Assert.Null(doc.Selection);
    }

    // ---------------------------------------------------------------- move and transform

    [Fact]
    public void Moving_a_selection_moves_exact_pixels_and_the_selection()
    {
        var doc = Boxes();
        doc.SetSelection(SelectionMask.Rectangle(100, 50, new PixelRect(10, 10, 30, 30)), "Select");
        var original = Snapshot(doc);
        var ft = FloatingTransform.Begin(doc, doc.ActiveLayer, "Move")!;
        Assert.Equal(0u, doc.ActiveLayer.Image[20, 20]);         // lifted
        Assert.Equal(Pixel.Opaque(Red), doc.CompositeAt(20, 20)); // but still shown while floating
        ft.MoveTo(5, 3);
        Assert.True(ft.Commit());
        Assert.Equal(Pixel.Opaque(Red), doc.ActiveLayer.Image[15 + 29, 13 + 29]);
        Assert.Equal(0u, doc.ActiveLayer.Image[11, 11]);
        Assert.Equal(new PixelRect(15, 13, 30, 30), doc.Selection!.Bounds);
        doc.Undo();
        Assert.Equal(original, doc.ActiveLayer.Image.Pixels);
        Assert.Equal(new PixelRect(10, 10, 30, 30), doc.Selection!.Bounds);
        doc.Redo();
        Assert.Equal(new PixelRect(15, 13, 30, 30), doc.Selection!.Bounds);
    }

    [Fact]
    public void Rotating_and_flipping_floating_pixels()
    {
        var doc = new Document(40, 40);
        for (int x = 10; x < 30; x++) doc.ActiveLayer.Image[x, 20] = Pixel.Opaque(Red); // horizontal line
        doc.ActiveLayer.Image[10, 20] = Pixel.Opaque(Green);                              // green left end
        doc.ActiveLayer.RecalculateBoundsForTest();
        var ft = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
        ft.Interpolation = Interpolation.Nearest;
        ft.Flip(horizontal: true);
        ft.Commit();
        Assert.Equal(Pixel.Opaque(Green), doc.ActiveLayer.Image[29, 20]); // the green end moved to the right

        var rot = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
        rot.Interpolation = Interpolation.Nearest;
        rot.Rotate(90);
        rot.Commit();
        int vertical = Enumerable.Range(0, 40).Count(y => doc.ActiveLayer.Image[20, y] != 0 || doc.ActiveLayer.Image[19, y] != 0);
        Assert.True(vertical >= 19, $"{vertical} pixels in the column");
    }

    [Fact]
    public void Cancelled_transform_restores_everything()
    {
        var doc = Boxes();
        var original = Snapshot(doc);
        var ft = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
        ft.SetTransform(Affine.Scale(2, 2));
        ft.Cancel();
        Assert.Equal(original, doc.ActiveLayer.Image.Pixels);
        Assert.False(doc.CanUndo);
        Assert.Equal(Pixel.Opaque(Red), doc.CompositeAt(20, 20));
    }

    // ---------------------------------------------------------------- image operations

    [Fact]
    public void Crop_resize_canvas_rotate_and_flip_the_whole_picture_with_undo()
    {
        var doc = Boxes();
        var original = (uint[])doc.Layers[0].Image.Pixels.Clone();
        doc.AddLayer("Top").Image[0, 0] = Pixel.Opaque(Green);

        ImageOperations.Crop(doc, new PixelRect(5, 5, 50, 40));
        Assert.Equal((50, 40), (doc.Width, doc.Height));
        Assert.Equal(Pixel.Opaque(Red), doc.Layers[0].Image[5, 5]);

        ImageOperations.ResizeCanvas(doc, 60, 40, anchorX: 0, anchorY: 0);
        Assert.Equal(60, doc.Width);
        Assert.Equal(Pixel.Opaque(Red), doc.Layers[0].Image[5, 5]);

        ImageOperations.Rotate(doc, 1);
        Assert.Equal((40, 60), (doc.Width, doc.Height));

        ImageOperations.Flip(doc, horizontal: true);
        ImageOperations.ResizeImage(doc, 20, 30, Interpolation.Bilinear);
        Assert.Equal((20, 30), (doc.Width, doc.Height));
        Assert.Equal(20, doc.Projection.Width);

        while (doc.Undo()) { }
        Assert.Equal((100, 50), (doc.Width, doc.Height));
        Assert.Equal(original, doc.Layers[0].Image.Pixels);
    }

    [Fact]
    public void Resizing_down_averages_pixels()
    {
        var img = new RasterImage(4, 1);
        img[0, 0] = Pixel.Opaque(ColorRgb.Black); img[1, 0] = Pixel.Opaque(ColorRgb.White);
        img[2, 0] = Pixel.Opaque(ColorRgb.Black); img[3, 0] = Pixel.Opaque(ColorRgb.White);
        var small = Resampler.Resize(img, 2, 1, Interpolation.Bilinear);
        Assert.InRange(Pixel.R(small[0, 0]), 126, 129);
        var nearest = Resampler.Resize(img, 8, 1, Interpolation.Nearest);
        Assert.Equal(Pixel.Opaque(ColorRgb.Black), nearest[1, 0]);
        Assert.Equal(Pixel.Opaque(ColorRgb.White), nearest[2, 0]);
    }

    // ---------------------------------------------------------------- adjustments

    [Fact]
    public void Adjustments_preview_without_changing_pixels_and_commit_as_one_step()
    {
        var doc = Boxes();
        var layer = doc.ActiveLayer;
        var original = Snapshot(doc);
        var op = new AdjustmentOperation(doc, layer);
        op.Preview(new Invert());
        Assert.Equal(original, layer.Image.Pixels);
        Assert.Equal(Pixel.Opaque(new ColorRgb(35, 225, 225)), doc.CompositeAt(20, 20));
        op.Cancel();
        Assert.Equal(Pixel.Opaque(Red), doc.CompositeAt(20, 20));

        var op2 = new AdjustmentOperation(doc, layer);
        Assert.True(op2.Commit(new HueSaturation(0, -100, 0)));
        var grey = layer.Image[20, 20];
        Assert.Equal(Pixel.R(grey), Pixel.G(grey));
        Assert.Equal(Pixel.G(grey), Pixel.B(grey));
        doc.Undo();
        Assert.Equal(original, layer.Image.Pixels);
    }

    [Fact]
    public void Brightness_contrast_blur_and_sharpen_behave_sensibly()
    {
        var img = new RasterImage(40, 1);
        for (int x = 0; x < 40; x++) img[x, 0] = Pixel.Opaque(x < 20 ? new ColorRgb(60, 60, 60) : new ColorRgb(180, 180, 180));
        var o = new RasterImage(40, 1);
        new BrightnessContrast(50, 0).Apply(img, o, img.Bounds);
        Assert.True(Pixel.R(o[5, 0]) > 60);
        new BrightnessContrast(0, 60).Apply(img, o, img.Bounds);
        Assert.True(Pixel.R(o[5, 0]) < 60 && Pixel.R(o[30, 0]) > 180);
        new Blur(3).Apply(img, o, img.Bounds);
        Assert.InRange(Pixel.R(o[19, 0]), 90, 150); // edge softened
        Assert.InRange(Pixel.R(o[2, 0]), 58, 62);
        new Sharpen(100, 2).Apply(img, o, img.Bounds);
        Assert.True(Pixel.R(o[19, 0]) < 60 && Pixel.R(o[20, 0]) > 180); // edge contrast increased
        new Desaturate().Apply(img, o, img.Bounds);
        Assert.Equal(Pixel.R(o[5, 0]), Pixel.G(o[5, 0]));
    }
}
