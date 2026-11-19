using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public class BrushKindTests
{
    private static int Painted(Document doc) => doc.ActiveLayer.Image.Pixels.Count(p => p != 0);

    private static int PartlyPainted(Document doc) => doc.ActiveLayer.Image.Pixels.Count(p => Pixel.A(p) is > 0 and < 250);

    [Fact]
    public void Grain_lets_paper_texture_show_through_light_strokes()
    {
        var round = new Document(300, 60);
        var grainy = new Document(300, 60);
        var pencil = new BrushSettings { Size = 20, Hardness = 0.9, PressureSize = false, Grain = 0.9 };
        Stroke(round, Paint(pencil with { Grain = 0 }), S(10, 30, 0.3f), S(290, 30, 0.3f));
        Stroke(grainy, Paint(pencil), S(10, 30, 0.3f), S(290, 30, 0.3f));
        Assert.True(PartlyPainted(grainy) > PartlyPainted(round) * 5, $"{PartlyPainted(grainy)} vs {PartlyPainted(round)}");

        var heavy = new Document(300, 60);
        Stroke(heavy, Paint(pencil), S(10, 30, 1f), S(290, 30, 1f));
        Assert.True(heavy.ActiveLayer.Image.Pixels.Sum(p => Pixel.A(p)) > grainy.ActiveLayer.Image.Pixels.Sum(p => Pixel.A(p)) * 1.3,
            "pressing harder fills more of the grain");
    }

    [Fact]
    public void Chisel_marker_is_thick_one_way_and_thin_the_other()
    {
        var marker = new BrushSettings { Size = 30, Hardness = 1, PressureSize = false, Roundness = 0.25, Angle = 0 };
        var h = new Document(200, 200);
        var v = new Document(200, 200);
        Stroke(h, Paint(marker), S(20, 100), S(180, 100));
        Stroke(v, Paint(marker), S(100, 20), S(100, 180));
        int horizontalWidth = ColumnCoverage(h, 100);
        int verticalWidth = Enumerable.Range(0, 200).Count(x => Pixel.A(v.ActiveLayer.Image[x, 100]) > 0);
        Assert.InRange(horizontalWidth, 6, 10);   // across a horizontal stroke: the thin side
        Assert.InRange(verticalWidth, 28, 32);    // across a vertical stroke: the wide side
    }

    [Fact]
    public void Airbrush_builds_up_and_keeps_spraying_while_the_pen_rests()
    {
        var air = new BrushSettings { Size = 40, Hardness = 0, PressureSize = false, BuildUp = true, Flow = 0.05, Spray = true };
        var doc = new Document(100, 100);
        var engine = new StrokeEngine(doc);
        engine.Begin(Paint(air), S(50, 50));
        byte first = Alpha(doc, 50, 50);
        engine.Spray(500);
        byte later = Alpha(doc, 50, 50);
        engine.End();
        Assert.True(later > first + 50, $"{first} → {later}");

        // A normal brush does not build up when passing over the same spot.
        var round = new Document(100, 100);
        Stroke(round, Paint(Hard(20, opacity: 0.4)), S(10, 50), S(90, 50), S(10, 50), S(90, 50));
        Assert.Equal(102, Alpha(round, 50, 50));
    }

    [Fact]
    public void Symmetry_mirrors_dabs_across_the_canvas_centre()
    {
        var doc = new Document(100, 80);
        var style = Paint(Hard(6)) with { Symmetry = Symmetry.Both };
        Stroke(doc, style, S(20, 20));
        Assert.True(Alpha(doc, 20, 20) > 0);
        Assert.True(Alpha(doc, 79, 20) > 0);
        Assert.True(Alpha(doc, 20, 59) > 0);
        Assert.True(Alpha(doc, 79, 59) > 0);
        Assert.Equal(1, doc.History.UndoCount);
    }

    [Fact]
    public void Paper_grain_is_seamless_and_spread()
    {
        float a = PaperGrain.Sample(0, 0, 1), b = PaperGrain.Sample(256, 512, 1);
        Assert.Equal(a, b);
        var values = Enumerable.Range(0, 256).Select(x => PaperGrain.Sample(x, 7, 1)).ToArray();
        Assert.True(values.Max() - values.Min() > 0.5f);
    }
}

public class PixelStrokeTests
{
    private static readonly ColorRgb Ink = new(36, 29, 43);

    [Fact]
    public void Pixel_pencil_sets_exact_colours_with_no_antialiasing()
    {
        var doc = new Document(16, 16);
        var p = new PixelStroke(doc);
        p.Begin(new PixelStrokeStyle(Ink), 1, 1);
        p.Add(14, 9);
        p.End();
        var px = doc.ActiveLayer.Image.Pixels;
        Assert.All(px, v => Assert.True(v == 0 || v == Pixel.Opaque(Ink)));
        Assert.Equal(Pixel.Opaque(Ink), doc.ActiveLayer.Image[1, 1]);
        Assert.Equal(Pixel.Opaque(Ink), doc.ActiveLayer.Image[14, 9]);
        Assert.Equal(1, doc.History.UndoCount);
    }

    [Fact]
    public void Pixel_perfect_removes_doubled_corners()
    {
        int Count(bool perfect)
        {
            var doc = new Document(10, 10);
            var p = new PixelStroke(doc);
            // A freehand-like staircase: right, down, right, down…
            p.Begin(new PixelStrokeStyle(Ink, PixelPerfect: perfect), 0, 0);
            int x = 0, y = 0;
            for (int i = 0; i < 4; i++)
            {
                p.Add(++x, y);
                p.Add(x, ++y);
            }
            p.End();
            return doc.ActiveLayer.Image.Pixels.Count(v => v != 0);
        }
        Assert.Equal(9, Count(false));
        Assert.Equal(5, Count(true)); // a clean one-pixel diagonal
    }

    [Fact]
    public void Brush_size_symmetry_wrap_and_eraser()
    {
        var doc = new Document(10, 10);
        var p = new PixelStroke(doc);
        p.Begin(new PixelStrokeStyle(Ink, Size: 3, PixelPerfect: false, Symmetry: Symmetry.Vertical), 1, 5);
        p.End();
        Assert.Equal(Pixel.Opaque(Ink), doc.ActiveLayer.Image[0, 4]);
        Assert.Equal(Pixel.Opaque(Ink), doc.ActiveLayer.Image[2, 6]);
        Assert.Equal(Pixel.Opaque(Ink), doc.ActiveLayer.Image[8, 5]); // mirror of x=1
        Assert.Equal(18, doc.ActiveLayer.Image.Pixels.Count(v => v != 0));

        var wrap = new Document(8, 8);
        var w = new PixelStroke(wrap);
        w.Begin(new PixelStrokeStyle(Ink, Wrap: true), 9, 3); // 9 wraps to 1
        w.End();
        Assert.Equal(Pixel.Opaque(Ink), wrap.ActiveLayer.Image[1, 3]);

        var e = new PixelStroke(doc);
        e.Begin(new PixelStrokeStyle(Ink, Erase: true, PixelPerfect: false), 1, 5);
        e.End();
        Assert.Equal(0u, doc.ActiveLayer.Image[1, 5]);
    }
}
