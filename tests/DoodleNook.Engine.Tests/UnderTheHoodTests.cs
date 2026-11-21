using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 011: the faster pixel arithmetic is exact, and the faster undo, save and compositing paths give the
/// same pictures as before.</summary>
public class UnderTheHoodTests
{
    [Fact]
    public void Two_lane_over_equals_the_channel_by_channel_formula_for_every_alpha_and_channel_value()
    {
        // Channels are independent, so every (source alpha, source channel, destination channel) triple covers all cases;
        // the destination's alpha runs through every value too.
        for (int sa = 0; sa < 256; sa++)
            for (int sc = 0; sc <= sa + 2 && sc < 256; sc++) // premultiplied colours (plus slightly invalid ones)
                for (int dc = 0; dc < 256; dc += 1)
                {
                    uint src = Pixel.Pack((byte)sa, (byte)sc, (byte)(sa - sc / 2 < 0 ? 0 : sa - sc / 2), (byte)(sc / 3));
                    uint dst = Pixel.Pack((byte)dc, (byte)(255 - dc), (byte)dc, (byte)(dc ^ 0x5A));
                    Assert.Equal(Pixel.OverReference(src, dst), Pixel.Over(src, dst));
                }
    }

    [Fact]
    public void Two_lane_over_and_fade_match_the_reference_on_random_pixels()
    {
        var rng = new Random(11);
        for (int i = 0; i < 2_000_000; i++)
        {
            uint src = (uint)rng.NextInt64(0, 1L << 32), dst = (uint)rng.NextInt64(0, 1L << 32);
            Assert.Equal(Pixel.OverReference(src, dst), Pixel.Over(src, dst));
            int f = rng.Next(0, 256);
            byte fb = (byte)f;
            uint expected = f >= 255 || src == 0 ? src : f <= 0 ? 0
                : Pixel.Pack(Pixel.Mul(Pixel.A(src), fb), Pixel.Mul(Pixel.R(src), fb), Pixel.Mul(Pixel.G(src), fb), Pixel.Mul(Pixel.B(src), fb));
            Assert.Equal(expected, Blending.Fade(src, f));
        }
    }

    [Fact]
    public void Undo_and_redo_leave_the_projection_exactly_as_a_full_recomposite()
    {
        var doc = UnderTheHoodBenchmarks.Layered(900, 600, 5, 2);
        var engine = new StrokeEngine(doc);
        for (int s = 0; s < 6; s++)
        {
            engine.Begin(new StrokeStyle(new BrushSettings { Size = 20 + s * 15, Hardness = 0.6 }, PressureCurve.Normal, ColorRgb.Parse("#203040"), PaintMode.Paint),
                new StrokeSample(30, 40 + s * 80), doc.Layers[s % 5]);
            for (int e = 1; e <= 50; e++) engine.Add(new StrokeSample(30 + e * 16, 40 + s * 80 + 60 * Math.Sin(e / 6.0)));
            engine.End();
        }
        var expectedAfter = doc.Flatten().Pixels;
        for (int i = 0; i < 6; i++)
        {
            Assert.True(doc.Undo());
            Assert.Equal(doc.Flatten().Pixels, doc.Projection.Pixels);
        }
        for (int i = 0; i < 6; i++)
        {
            Assert.True(doc.Redo());
            Assert.Equal(doc.Flatten().Pixels, doc.Projection.Pixels);
        }
        Assert.Equal(expectedAfter, doc.Projection.Pixels);
    }

    [Fact]
    public void Undo_reports_the_area_it_recomposited_once()
    {
        var doc = UnderTheHoodBenchmarks.Layered(640, 480, 3, 1);
        var engine = new StrokeEngine(doc);
        engine.Begin(new StrokeStyle(new BrushSettings { Size = 12, Hardness = 1 }, PressureCurve.Normal, ColorRgb.Black, PaintMode.Paint), new StrokeSample(10, 10));
        engine.Add(new StrokeSample(630, 470));
        engine.End();
        var areas = new List<PixelRect>();
        doc.ProjectionChanged += areas.Add;
        doc.Undo();
        var area = Assert.Single(areas);
        Assert.True(area.Contains(10, 10) && area.Contains(629, 469));
    }

    [Fact]
    public void A_saved_thumbnail_is_the_same_whether_the_picture_is_flattened_again_or_taken_from_the_screen()
    {
        var doc = UnderTheHoodBenchmarks.Layered(700, 420, 4, 2);
        using var a = new MemoryStream();
        DnkFile.Write(doc, a);
        // The same picture, but with an onion-skin-like overlay forcing the slow path is not possible for a still picture;
        // instead compare with the documented thumbnail of a fresh flatten.
        var expected = PngCodec.Encode(DnkFile.Thumbnail(doc, 256));
        a.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(a, System.IO.Compression.ZipArchiveMode.Read);
        using var thumb = zip.GetEntry("thumbnail.png")!.Open();
        using var bytes = new MemoryStream();
        thumb.CopyTo(bytes);
        Assert.Equal(expected, bytes.ToArray());
    }

    /// <summary>After every pen event, the projection (built with the cache under the painted layer) equals a full composite.</summary>
    private static void PaintAndCompare(Document doc, Layer layer, PaintMode mode = PaintMode.Paint, double size = 170)
    {
        var engine = new StrokeEngine(doc);
        engine.Begin(new StrokeStyle(new BrushSettings { Size = size, Hardness = 0.5, Opacity = 0.85 }, PressureCurve.Normal, ColorRgb.Parse("#C0392B"), mode),
            new StrokeSample(20, 30, 0.4f), layer);
        for (int e = 1; e <= 40; e++)
        {
            engine.Add(new StrokeSample(20 + e * 14, 30 + e * 7 + 25 * Math.Sin(e / 4.0), (float)(0.3 + 0.7 * Math.Sin(e / 13.0))));
            var full = new RasterImage(doc.Width, doc.Height);
            Compositor.Compose(doc, doc.Bounds, full, includeOverlays: true);
            Assert.Equal(full.Pixels, doc.Projection.Pixels);
        }
        engine.End();
    }

    [Fact]
    public void Painting_with_the_cache_under_the_layer_shows_exactly_the_full_composite_after_every_pen_event()
    {
        var doc = UnderTheHoodBenchmarks.Layered(640, 420, 6, 5);
        // Below the painted layer: a group with a Multiply member, an adjustment and an invisible layer.
        doc.GroupLayers([doc.Layers[1], doc.Layers[2]], "Group");
        doc.SetLayerProperties(doc.Layers[0], doc.Layers[0].Properties with { Visible = false });
        doc.SetActiveLayer(doc.Layers[^1]);
        doc.AddAdjustmentLayer(new HueSaturation(30, 10, 0));
        var painted = doc.AddLayer("Painted");
        // Above it: a layer clipped to it and another adjustment.
        var clipped = doc.AddLayer("Clipped");
        UnderTheHoodBenchmarks.Paint(doc, clipped, 3);
        doc.SetLayerProperties(clipped, clipped.Properties with { ClipToBelow = true, Blend = BlendMode.Screen });
        doc.AddAdjustmentLayer(new Levels(5, 245, 1.1));
        PaintAndCompare(doc, painted);
        PaintAndCompare(doc, painted, PaintMode.Erase, 40); // small events: composed directly, the cache stays
        PaintAndCompare(doc, painted, size: 220);
        // Painting the mask of the layer.
        doc.SetActiveLayer(painted);
        doc.AddMask(painted);
        doc.SetEditingMask(true);
        PaintAndCompare(doc, painted, size: 200);
        doc.SetEditingMask(false);
        // Something below changes between strokes: the next stroke must see it.
        doc.SetLayerProperties(doc.Layers[0], doc.Layers[0].Properties with { Visible = true });
        PaintAndCompare(doc, painted);
        doc.Undo();
        PaintAndCompare(doc, painted);
        // Another layer (small and large), then back.
        PaintAndCompare(doc, clipped, size: 30);
        PaintAndCompare(doc, painted);
        PaintAndCompare(doc, clipped);
        PaintAndCompare(doc, painted, size: 30);
        PaintAndCompare(doc, painted);
        Assert.True(doc.StrokeCacheBytes > 0, "the big strokes should have used the cache under the layer");
    }

    [Fact]
    public void Painting_with_onion_skins_and_a_palette_lock_matches_the_full_composite()
    {
        var anim = Document.NewAnimation(400, 260, ColorRgb.White, 4, 12, "Bg");
        UnderTheHoodBenchmarks.Paint(anim, anim.ActiveLayer, 0);
        var mid = anim.AddLayer("Mid");
        UnderTheHoodBenchmarks.Paint(anim, mid, 1);
        var ink = anim.AddLayer("Ink");
        anim.SetCurrentFrame(1);
        UnderTheHoodBenchmarks.Paint(anim, ink, 2);
        anim.SetCurrentFrame(2);
        var (onion, bounds) = OnionSkin.Build(anim, new OnionSkinSettings(Enabled: true, Before: 1, After: 1));
        anim.SetOnionSkin(onion, bounds);
        PaintAndCompare(anim, ink);
        anim.SetCurrentFrame(3);
        PaintAndCompare(anim, ink);

        var locked = new Document(360, 240, ColorRgb.White, "A");
        locked.ChangePalette(Palette.Default(), true, null, "Lock");
        for (int i = 1; i < 4; i++) UnderTheHoodBenchmarks.Paint(locked, locked.AddLayer($"L{i}"), i);
        locked.AddAdjustmentLayer(new HueSaturation(40, 0, 0));
        PaintAndCompare(locked, locked.Layers[3]);
    }
}
