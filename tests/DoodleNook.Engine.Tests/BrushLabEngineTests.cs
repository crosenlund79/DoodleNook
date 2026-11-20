using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>The Mission 007 brush dynamics: image tips, colour tips, spacing, scatter, direction, pressure, texture.</summary>
public class BrushLabEngineTests
{
    private static readonly ColorRgb Ink = ColorRgb.Parse("#3B2F2A");

    private static Document Stroke(BrushSettings brush, BrushResources? res, params StrokeSample[] samples)
    {
        var doc = new Document(200, 120, null);
        var e = new StrokeEngine(doc);
        e.Begin(new StrokeStyle(brush, PressureCurve.Normal, Ink, PaintMode.Paint, Symmetry.None, res), samples[0]);
        foreach (var s in samples.Skip(1)) e.Add(s);
        e.End();
        return doc;
    }

    private static StrokeSample[] Line(double x0, double x1, double y, int n = 40, float p = 1) =>
        Enumerable.Range(0, n + 1).Select(i => new StrokeSample(x0 + (x1 - x0) * i / n, y, p)).ToArray();

    private static int Painted(Document d) => d.ActiveLayer.Image.Pixels.Count(p => Pixel.A(p) > 0);

    private static RasterImage SquareWithHole()
    {
        // A 20 × 20 opaque square with a transparent 8 × 8 hole: alpha decides what paints.
        var img = new RasterImage(20, 20);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
                img[x, y] = x is >= 6 and < 14 && y is >= 6 and < 14 ? 0u : Pixel.Opaque(ColorRgb.Parse("#5F8FA8"));
        return img;
    }

    [Fact]
    public void An_image_tip_paints_its_shape_and_alpha_in_the_brush_colour()
    {
        var tip = BrushTip.FromImage(SquareWithHole());
        var brush = new BrushSettings { Size = 40, PressureSize = false, TipId = "tip:test" };
        var d = Stroke(brush, new BrushResources(tip, null), new StrokeSample(100, 60));
        var img = d.ActiveLayer.Image;
        Assert.Equal(255, Pixel.A(img[85, 60]));            // inside the square
        Assert.Equal(0, Pixel.A(img[100, 60]));             // the hole stays empty
        Assert.Equal(Ink, Pixel.ToStraight(img[85, 60]) is var (r, g, b, _) ? new ColorRgb(r, g, b) : default); // tinted
        Assert.Equal(0, Pixel.A(img[100, 15]));             // nothing outside the 40 px dab
    }

    [Fact]
    public void An_opaque_picture_tip_paints_where_it_is_dark()
    {
        var img = new RasterImage(10, 10);
        img.Fill(Pixel.Opaque(ColorRgb.White));
        for (int x = 0; x < 10; x++) img[x, 5] = Pixel.Opaque(ColorRgb.Black);
        var tip = BrushTip.FromImage(img);
        Assert.False(tip.HasColor);
        Assert.Equal(1, tip.Height); // the white border is trimmed: just the black line remains
    }

    [Fact]
    public void A_colour_tip_paints_the_stamp_colours_layered_over_each_other()
    {
        var tip = BrushTip.FromImage(SquareWithHole());
        Assert.True(tip.HasColor);
        var brush = new BrushSettings { Size = 40, PressureSize = false, TipId = "stamp:test", TipColors = true };
        var d = Stroke(brush, new BrushResources(tip, null), new StrokeSample(100, 60));
        var (r, g, b, a) = Pixel.ToStraight(d.ActiveLayer.Image[85, 60]);
        Assert.Equal(255, a);
        Assert.Equal(ColorRgb.Parse("#5F8FA8"), new ColorRgb(r, g, b));
    }

    [Fact]
    public void Spacing_controls_the_distance_between_dabs()
    {
        var tip = BrushTip.FromMask(4, 4, (_, _) => 1);
        var res = new BrushResources(tip, null);
        var wide = Stroke(new BrushSettings { Size = 8, PressureSize = false, TipId = "t", Spacing = 3 }, res, Line(20, 180, 60));
        var dense = Stroke(new BrushSettings { Size = 8, PressureSize = false, TipId = "t", Spacing = 0.25 }, res, Line(20, 180, 60));
        // 3 × the size apart: separate squares with gaps; a quarter apart: one continuous bar.
        var row = Enumerable.Range(20, 160).Select(x => Pixel.A(wide.ActiveLayer.Image[x, 60]) > 0).ToList();
        Assert.Contains(false, row);
        Assert.All(Enumerable.Range(24, 150), x => Assert.True(Pixel.A(dense.ActiveLayer.Image[x, 60]) > 0));
    }

    [Fact]
    public void Scatter_and_jitter_are_random_but_repeatable()
    {
        var brush = new BrushSettings { Size = 10, PressureSize = false, Hardness = 1, Spacing = 1, Scatter = 2, ScatterCount = 3, SizeJitter = 0.5, OpacityJitter = 0.5 };
        var a = Stroke(brush, null, Line(30, 170, 60));
        var b = Stroke(brush, null, Line(30, 170, 60));
        Assert.Equal(a.ActiveLayer.Image.Pixels, b.ActiveLayer.Image.Pixels); // same stroke, same marks
        // Dabs land off the line.
        Assert.Contains(Enumerable.Range(30, 140), x => Pixel.A(a.ActiveLayer.Image[x, 60 - 12]) > 0 || Pixel.A(a.ActiveLayer.Image[x, 60 + 12]) > 0);
        var plain = Stroke(brush with { Scatter = 0, ScatterCount = 1, SizeJitter = 0, OpacityJitter = 0 }, null, Line(30, 170, 60));
        Assert.All(Enumerable.Range(30, 140), x => Assert.Equal(0, Pixel.A(plain.ActiveLayer.Image[x, 60 - 12])));
    }

    [Fact]
    public void A_tip_can_follow_the_stroke_direction()
    {
        // A long thin horizontal tip: across a vertical stroke it makes a wide band unless it turns with the stroke.
        var tip = BrushTip.FromMask(40, 4, (_, _) => 1);
        var res = new BrushResources(tip, null);
        StrokeSample[] down = Enumerable.Range(0, 30).Select(i => new StrokeSample(100, 20 + i * 3)).ToArray();
        var fixedTip = Stroke(new BrushSettings { Size = 40, PressureSize = false, TipId = "t" }, res, down);
        var following = Stroke(new BrushSettings { Size = 40, PressureSize = false, TipId = "t", FollowDirection = true }, res, down);
        int Width(Document d) => Enumerable.Range(0, 200).Count(x => Pixel.A(d.ActiveLayer.Image[x, 60]) > 0);
        Assert.InRange(Width(fixedTip), 38, 44);
        Assert.InRange(Width(following), 3, 8);
    }

    [Fact]
    public void The_brush_pressure_curve_changes_light_strokes_only()
    {
        var soft = new BrushSettings { Size = 30, PressureSize = true, MinimumSizeFraction = 0, PressureGamma = 0.5 };
        var firm = soft with { PressureGamma = 2 };
        var (dSoft, _) = soft.Evaluate(0.25f, PressureCurve.Normal);
        var (dFirm, _) = firm.Evaluate(0.25f, PressureCurve.Normal);
        Assert.True(dSoft > dFirm * 3);
        Assert.Equal(soft.Evaluate(1f, PressureCurve.Normal).Diameter, firm.Evaluate(1f, PressureCurve.Normal).Diameter);
    }

    [Fact]
    public void A_texture_from_the_library_shows_through_the_stroke()
    {
        // Stripes: light columns take paint, dark ones don't.
        var stripes = new RasterImage(8, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                stripes[x, y] = Pixel.Opaque(x < 4 ? ColorRgb.White : ColorRgb.Black);
        var res = new BrushResources(null, BrushTexture.FromImage(stripes));
        // A light touch (as with paper grain, firm pressure fills the texture in).
        var d = Stroke(new BrushSettings { Size = 30, PressureSize = false, Hardness = 1, Grain = 1, TextureId = "pattern:x" }, res, Line(20, 180, 60, p: 0.4f));
        var img = d.ActiveLayer.Image;
        Assert.True(Pixel.A(img[81, 60]) > 200); // x % 8 = 1: light
        Assert.True(Pixel.A(img[85, 60]) < 40);  // x % 8 = 5: dark
        var inverted = Stroke(new BrushSettings { Size = 30, PressureSize = false, Hardness = 1, Grain = 1, TextureId = "pattern:x", InvertTexture = true }, res, Line(20, 180, 60, p: 0.4f));
        Assert.True(Pixel.A(inverted.ActiveLayer.Image[85, 60]) > 200);
    }

    [Fact]
    public void Wet_edges_make_the_rim_darker_than_the_middle()
    {
        var d = Stroke(new BrushSettings { Size = 60, PressureSize = false, Hardness = 0.3, WetEdges = 1 }, null, Line(20, 180, 60));
        var img = d.ActiveLayer.Image;
        byte middle = Pixel.A(img[100, 60]);
        byte rim = Enumerable.Range(60, 30).Select(y => Pixel.A(img[100, y])).Max();
        Assert.True(rim > middle + 20, $"rim {rim} middle {middle}");
    }

    [Fact]
    public void Image_tip_strokes_are_one_undo_step()
    {
        var doc = new Document(200, 120, null);
        var e = new StrokeEngine(doc);
        var res = new BrushResources(BrushTip.FromImage(SquareWithHole()), null);
        e.Begin(new StrokeStyle(new BrushSettings { Size = 30, TipId = "t", TipColors = true }, PressureCurve.Normal, Ink, PaintMode.Paint, Symmetry.None, res), new StrokeSample(40, 60));
        foreach (var s in Line(40, 160, 60).Skip(1)) e.Add(s);
        e.End();
        Assert.True(Painted(doc) > 0);
        Assert.Equal(1, doc.History.UndoCount);
        Assert.True(doc.Undo());
        Assert.Equal(0, Painted(doc));
    }

    [Fact]
    public void Every_built_in_brush_paints_and_resolves_its_parts()
    {
        var lib = new CreativeLibrary();
        foreach (var b in BuiltInAssets.Brushes)
        {
            Assert.False(lib.HasMissingParts(b.Settings), b.Id);
            var d = Stroke(b.Settings with { Spray = false }, lib.Resolve(b.Settings), Line(20, 180, 60, 60, 0.8f));
            Assert.True(Painted(d) > 50, $"{b.Id} painted {Painted(d)} pixels");
        }
        Assert.Equal(BuiltInAssets.Brushes.Count, BuiltInAssets.Brushes.Select(b => b.Id).Distinct().Count());
        Assert.All(BuiltInAssets.ClassicIds, id => Assert.Contains(BuiltInAssets.Brushes, b => b.Id == id));
    }
}
