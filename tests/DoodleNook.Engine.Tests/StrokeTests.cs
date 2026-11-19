using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public class StrokeTests
{
    [Fact]
    public void Single_dab_is_round_and_centred_on_the_sample()
    {
        var doc = new Document(64, 64);
        Stroke(doc, Paint(Hard(10)), S(32, 32));

        Assert.Equal(255, Alpha(doc, 32, 32));
        Assert.Equal(Pixel.Opaque(Ink), doc.Layer[32, 32]);
        // Pixel centres 4.5 px left and right of the centre must match (symmetry).
        Assert.Equal(Alpha(doc, 27, 32), Alpha(doc, 36, 32));
        Assert.Equal(Alpha(doc, 32, 27), Alpha(doc, 32, 36));
        // Nothing beyond the radius (+ anti-aliasing).
        Assert.Equal(0, Alpha(doc, 32 + 6, 32));
        Assert.Equal(0, Alpha(doc, 32, 32 - 7));
        Assert.Equal(1, doc.History.UndoCount);
    }

    [Fact]
    public void Dab_lands_on_the_pixel_under_the_sample()
    {
        var doc = new Document(40, 40);
        Stroke(doc, Paint(Hard(1)), S(10.5, 20.5));
        Assert.Equal(255, Alpha(doc, 10, 20));
        Assert.Equal(0, Alpha(doc, 11, 20));
        Assert.Equal(0, Alpha(doc, 10, 21));
    }

    [Fact]
    public void A_stroke_begins_painting_immediately_on_pen_down()
    {
        var doc = new Document(64, 64);
        var engine = new StrokeEngine(doc);
        var changed = engine.Begin(Paint(Hard(8)), S(20, 20));
        Assert.False(changed.IsEmpty);
        Assert.Equal(255, Alpha(doc, 20, 20));
        Assert.True(engine.IsActive);
        engine.End();
    }

    [Fact]
    public void Two_distant_samples_produce_a_continuous_line()
    {
        var doc = new Document(200, 100);
        Stroke(doc, Paint(Hard(4)), S(10, 50), S(190, 50));
        for (int x = 10; x < 190; x++)
            Assert.True(Alpha(doc, x, 50) == 255 && Alpha(doc, x, 49) == 255, $"gap at x={x}");
    }

    [Fact]
    public void Rapid_zigzag_with_large_jumps_has_no_gaps()
    {
        var doc = new Document(400, 200);
        var pts = new[] { S(20, 20), S(120, 180), S(220, 20), S(320, 180), S(380, 30) };
        Stroke(doc, Paint(Hard(3)), pts);
        for (int i = 0; i < pts.Length - 1; i++)
        {
            for (int k = 0; k <= 50; k++)
            {
                double t = k / 50.0;
                int x = (int)(pts[i].X + (pts[i + 1].X - pts[i].X) * t);
                int y = (int)(pts[i].Y + (pts[i + 1].Y - pts[i].Y) * t);
                Assert.True(Alpha(doc, x, y) > 0, $"gap on segment {i} at ({x},{y})");
            }
        }
    }

    [Fact]
    public void Slow_stroke_matches_fast_stroke_without_blobs()
    {
        var slow = new Document(200, 60);
        var fast = new Document(200, 60);
        var brush = Hard(10, opacity: 0.5);
        Stroke(slow, Paint(brush), Line(20, 30, 180, 30, step: 0.05));
        Stroke(fast, Paint(brush), S(20, 30), S(180, 30));

        // Same width and same (not darker) opacity everywhere along the middle of the line.
        for (int x = 30; x <= 170; x += 7)
        {
            Assert.Equal(ColumnCoverage(fast, x), ColumnCoverage(slow, x));
            Assert.Equal(Alpha(fast, x, 30), Alpha(slow, x, 30));
        }
        Assert.Equal(128, Alpha(slow, 100, 30));
    }

    [Fact]
    public void Sharp_direction_change_keeps_the_corner_filled()
    {
        var doc = new Document(200, 200);
        Stroke(doc, Paint(Hard(6)), S(20, 180), S(100, 20), S(180, 180));
        Assert.Equal(255, Alpha(doc, 100, 20));
        Assert.Equal(255, Alpha(doc, 100, 22));
        Assert.True(Alpha(doc, 60, 100) == 255 && Alpha(doc, 140, 100) == 255);
    }

    [Fact]
    public void Stroke_ends_with_a_clean_round_cap_at_the_last_sample()
    {
        var doc = new Document(200, 60);
        Stroke(doc, Paint(Hard(20)), S(20, 30), S(150, 30));
        Assert.Equal(255, Alpha(doc, 150, 30));
        Assert.Equal(255, Alpha(doc, 158, 30)); // inside the round end (radius 10)
        Assert.Equal(0, Alpha(doc, 161, 30));   // nothing past it
        Assert.Equal(0, Alpha(doc, 150, 41));
    }

    [Fact]
    public void Semi_transparent_stroke_does_not_darken_where_it_crosses_itself()
    {
        var doc = new Document(200, 200);
        // A loop that crosses itself at (100,100), drawn back and forth over the same line as well.
        Stroke(doc, Paint(Hard(8, opacity: 0.5)),
            S(40, 100), S(160, 100), S(160, 160), S(100, 160), S(100, 40), S(60, 40), S(60, 100), S(140, 100));
        byte crossing = Alpha(doc, 100, 100);
        byte plain = Alpha(doc, 160, 130);
        Assert.Equal(plain, crossing);
        Assert.Equal(128, plain);
    }

    [Fact]
    public void Opacity_caps_the_stroke_alpha()
    {
        var doc = new Document(100, 40);
        Stroke(doc, Paint(Hard(10, opacity: 0.4)), S(10, 20), S(90, 20));
        Assert.Equal(102, Alpha(doc, 50, 20));
    }

    [Fact]
    public void Separate_strokes_do_build_up()
    {
        var doc = new Document(100, 40);
        Stroke(doc, Paint(Hard(10, opacity: 0.5)), S(10, 20), S(90, 20));
        Stroke(doc, Paint(Hard(10, opacity: 0.5)), S(10, 20), S(90, 20));
        Assert.True(Alpha(doc, 50, 20) > 180);
    }

    [Fact]
    public void Pressing_harder_without_moving_grows_the_dot()
    {
        var doc = new Document(100, 100);
        var engine = new StrokeEngine(doc);
        var brush = Hard(40, pressureSize: true);
        engine.Begin(Paint(brush), S(50, 50, 0.1f));
        int small = ColumnCoverage(doc, 50);
        engine.Add(S(50, 50, 0.9f));
        engine.End();
        Assert.True(ColumnCoverage(doc, 50) > small + 20);
    }

    [Fact]
    public void Smoothing_still_ends_where_the_pen_lifted()
    {
        var doc = new Document(300, 100);
        var brush = Hard(6) with { Smoothing = 1 };
        Stroke(doc, Paint(brush), Line(20, 50, 250, 50, step: 25));
        Assert.Equal(255, Alpha(doc, 250, 50));
    }

    [Fact]
    public void Invalid_samples_are_ignored_and_out_of_range_pressure_is_clamped()
    {
        var a = new Document(120, 60);
        var b = new Document(120, 60);
        var brush = Hard(20, pressureSize: true);
        Stroke(a, Paint(brush), S(10, 30, 5f), S(double.NaN, 30), S(110, 30, 7f), S(double.PositiveInfinity, 1));
        Stroke(b, Paint(brush), S(10, 30, 1f), S(110, 30, 1f));
        Assert.Equal(b.Layer.Pixels, a.Layer.Pixels);

        var c = new Document(120, 60);
        Stroke(c, Paint(brush), S(10, 30, -3f), S(110, 30, -1f));
        Assert.Equal(ColumnCoverage(c, 60), ColumnCoverage(Draw(brush, 0f), 60));
    }

    private static Document Draw(BrushSettings brush, float pressure)
    {
        var d = new Document(120, 60);
        Stroke(d, Paint(brush), S(10, 30, pressure), S(110, 30, pressure));
        return d;
    }

    [Fact]
    public void Strokes_partly_or_fully_outside_the_canvas_are_safe()
    {
        var doc = new Document(50, 50);
        Stroke(doc, Paint(Hard(30)), S(-20, -20), S(10, 10), S(-1000, 5000), S(1e7, -1e7));
        Assert.Equal(255, Alpha(doc, 0, 0));
        Assert.Equal(255, Alpha(doc, 10, 10));
    }

    [Fact]
    public void Starting_with_an_invalid_sample_does_not_start_a_stroke()
    {
        var doc = new Document(20, 20);
        var engine = new StrokeEngine(doc);
        engine.Begin(Paint(Hard(4)), S(double.NaN, 3));
        Assert.False(engine.IsActive);
        Assert.True(engine.End().IsEmpty);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void Cancel_restores_pixels_and_adds_no_history()
    {
        var doc = new Document(100, 100);
        Stroke(doc, Paint(Hard(10)), S(10, 10), S(90, 10));
        var before = Snapshot(doc);
        var engine = new StrokeEngine(doc);
        engine.Begin(Paint(Hard(10), new ColorRgb(200, 0, 0)), S(10, 10));
        engine.Add(S(90, 90));
        engine.Cancel();
        Assert.Equal(before, doc.Layer.Pixels);
        Assert.Equal(1, doc.History.UndoCount);
    }

    [Fact]
    public void Eraser_makes_pixels_transparent_and_can_be_undone()
    {
        var doc = new Document(100, 60);
        Stroke(doc, Paint(Hard(40)), S(10, 30), S(90, 30));
        var painted = Snapshot(doc);
        Stroke(doc, Erase(Hard(20)), S(10, 30), S(90, 30));

        Assert.Equal(Pixel.Transparent, doc.Layer[50, 30]);
        Assert.Equal(255, Alpha(doc, 50, 12)); // outside the eraser stroke
        doc.Undo();
        Assert.Equal(painted, doc.Layer.Pixels);
        doc.Redo();
        Assert.Equal(Pixel.Transparent, doc.Layer[50, 30]);
    }

    [Fact]
    public void Soft_eraser_with_low_opacity_only_thins_the_paint()
    {
        var doc = new Document(100, 60);
        Stroke(doc, Paint(Hard(40)), S(10, 30), S(90, 30));
        Stroke(doc, Erase(Hard(20, opacity: 0.5)), S(10, 30), S(90, 30));
        Assert.Equal(127, Alpha(doc, 50, 30));
    }

    [Fact]
    public void Erasing_empty_canvas_adds_no_undo_step()
    {
        var doc = new Document(64, 64);
        Stroke(doc, Erase(Hard(20)), S(5, 5), S(60, 60));
        Assert.False(doc.CanUndo);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Painted_colour_is_exact_when_opaque_and_premultiplied_when_not()
    {
        var doc = new Document(60, 30);
        var c = new ColorRgb(200, 100, 50);
        Stroke(doc, Paint(Hard(10), c), S(5, 15), S(55, 15));
        Assert.Equal(Pixel.Pack(255, 200, 100, 50), doc.Layer[30, 15]);

        var half = new Document(60, 30);
        Stroke(half, Paint(Hard(10, opacity: 0.5), c), S(5, 15), S(55, 15));
        Assert.Equal(Pixel.FromStraight(200, 100, 50, 128), half.Layer[30, 15]);
    }

    [Fact]
    public void Spacing_is_relative_to_diameter_and_never_degenerate()
    {
        var engine = new StrokeEngine(new Document(10, 10));
        engine.Begin(Paint(Hard(10)), S(1, 1));
        engine.End();
        Assert.InRange(engine.Spacing(10), 0.25, 1.0);
        Assert.InRange(engine.Spacing(400), 5, 40);
        Assert.Equal(0.25, engine.Spacing(0.01));
    }
}
