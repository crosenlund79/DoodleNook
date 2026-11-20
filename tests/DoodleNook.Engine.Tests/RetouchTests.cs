using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 010 — Smudge, Clone and Repair.</summary>
public class RetouchTests
{
    private static readonly ColorRgb Red = new(200, 40, 40), Blue = new(40, 60, 200), Grey = new(128, 128, 128);

    /// <summary>A 200 × 100 picture: red on the left half, blue on the right.</summary>
    private static Document TwoColours(bool transparentRight = false)
    {
        var doc = new Document(200, 100, null, "Paint");
        var img = doc.ActiveLayer.Image;
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 200; x++)
                img[x, y] = x < 100 ? Pixel.Opaque(Red) : transparentRight ? 0u : Pixel.Opaque(Blue);
        doc.ActiveLayer.Touch(doc.Bounds);
        return doc;
    }

    private static RetouchStyle Smudge(double size = 20, double strength = 0.8, double hardness = 0.5, ColorRgb? finger = null) =>
        new(RetouchKind.Smudge, size, strength, hardness, PressureCurve.Normal, FingerPaint: finger);

    private static void Run(Document doc, RetouchStyle style, params StrokeSample[] samples)
    {
        var r = new RetouchStroke(doc);
        r.Begin(style, samples[0]);
        r.Add(samples.AsSpan(1));
        r.End();
    }

    // ================================================================== smudge

    [Fact]
    public void Smudge_carries_paint_along_the_stroke_and_strength_decides_how_far()
    {
        var weak = TwoColours();
        var strong = TwoColours();
        Run(weak, Smudge(strength: 0.4), S(80, 50), S(150, 50));
        Run(strong, Smudge(strength: 0.9), S(80, 50), S(150, 50));
        // Red is carried into the blue half…
        Assert.True(Pixel.R(strong.ActiveLayer.Image[120, 50]) > 120);
        // …further with more strength.
        int RedAt(Document d, int x) => Pixel.R(d.ActiveLayer.Image[x, 50]);
        Assert.True(RedAt(strong, 135) > RedAt(weak, 135) + 20, $"{RedAt(strong, 135)} vs {RedAt(weak, 135)}");
        // Away from the stroke nothing changes, and it is one undo step.
        Assert.Equal(Pixel.Opaque(Blue), strong.ActiveLayer.Image[150, 5]);
        Assert.True(strong.Undo());
        Assert.Equal(Pixel.Opaque(Blue), strong.ActiveLayer.Image[120, 50]);
        Assert.False(strong.CanUndo);
    }

    [Fact]
    public void Smudge_follows_the_gesture_not_the_number_of_input_events()
    {
        var few = TwoColours();
        var many = TwoColours();
        Run(few, Smudge(), S(60, 40), S(110, 60), S(160, 40));
        var samples = new List<StrokeSample>();
        for (int i = 0; i <= 50; i++) samples.Add(S(60 + i, 40 + i * 20 / 50.0));
        for (int i = 1; i <= 50; i++) samples.Add(S(110 + i, 60 - i * 20 / 50.0));
        Run(many, Smudge(), [.. samples]);
        int diff = 0;
        for (int i = 0; i < few.ActiveLayer.Image.Pixels.Length; i++)
            diff = Math.Max(diff, Math.Abs(Pixel.R(few.ActiveLayer.Image.Pixels[i]) - Pixel.R(many.ActiveLayer.Image.Pixels[i])));
        Assert.True(diff <= 2, $"largest difference {diff}");
    }

    [Fact]
    public void Smudge_spreads_transparency_like_paint_but_a_transparency_lock_keeps_the_shape()
    {
        var free = TwoColours(transparentRight: true);
        Run(free, Smudge(), S(80, 50), S(140, 50));
        Assert.True(Pixel.A(free.ActiveLayer.Image[115, 50]) > 100); // paint pushed into the empty half

        var locked = TwoColours(transparentRight: true);
        locked.SetLayerProperties(locked.ActiveLayer, locked.ActiveLayer.Properties with { AlphaLocked = true });
        var alphaBefore = locked.ActiveLayer.Image.Pixels.Select(Pixel.A).ToArray();
        Run(locked, Smudge(), S(80, 50), S(140, 50));
        Assert.Equal(alphaBefore, locked.ActiveLayer.Image.Pixels.Select(Pixel.A).ToArray());
    }

    [Fact]
    public void Finger_painting_brings_in_the_chosen_colour()
    {
        var doc = TwoColours();
        Run(doc, Smudge(finger: new ColorRgb(250, 250, 0), strength: 0.9), S(30, 50), S(60, 50));
        var (r, g, _, _) = Pixel.ToStraight(doc.ActiveLayer.Image[40, 50]);
        Assert.True(g > 120 && r > 200, $"{r},{g}");
    }

    [Fact]
    public void Smudge_respects_the_selection_and_a_locked_palette()
    {
        var doc = TwoColours();
        doc.SetSelection(SelectionMask.Rectangle(200, 100, new PixelRect(100, 0, 100, 45)), "Select");
        Run(doc, Smudge(size: 30), S(80, 50), S(150, 50));
        Assert.Equal(Pixel.Opaque(Blue), doc.ActiveLayer.Image[120, 55]); // below the selection
        Assert.NotEqual(Pixel.Opaque(Blue), doc.ActiveLayer.Image[120, 42]);

        var locked = TwoColours();
        locked.LockToPalette(new Palette("Two", [Red, Blue]));
        Run(locked, Smudge(strength: 0.6), S(60, 50), S(160, 50));
        var conformer = new PaletteConformer([Red, Blue]);
        Assert.All(locked.ActiveLayer.Image.Pixels, p => Assert.True(conformer.IsPaletteColour(p)));
        Assert.True(Enumerable.Range(100, 60).Any(x => locked.ActiveLayer.Image[x, 50] == Pixel.Opaque(Red))); // red really moved
    }

    // ================================================================== clone

    /// <summary>A picture with a little checkerboard "texture" at the top left and a mark at the right.</summary>
    private static Document Textured()
    {
        var doc = new Document(200, 100, null, "Paint");
        var img = doc.ActiveLayer.Image;
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 200; x++)
            {
                bool check = ((x / 3) + (y / 3)) % 2 == 0;
                img[x, y] = Pixel.Opaque(x < 100 ? (check ? new ColorRgb(150, 150, 150) : new ColorRgb(110, 110, 110)) : Grey);
            }
        for (int y = 45; y < 55; y++)
            for (int x = 145; x < 155; x++)
                img[x, y] = Pixel.Opaque(new ColorRgb(10, 10, 10)); // the mark
        doc.ActiveLayer.Touch(doc.Bounds);
        return doc;
    }

    private static RetouchStyle Clone(int dx, int dy, double size = 16, double hardness = 1, RasterImage? sampled = null) =>
        new(RetouchKind.Clone, size, 1, hardness, PressureCurve.Normal, Source: (dx, dy), Sampled: sampled);

    [Fact]
    public void Clone_copies_exact_pixels_from_the_source_offset_and_follows_the_stroke()
    {
        var doc = Textured();
        var before = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        // Source 100 px to the left of where we paint.
        Run(doc, Clone(-100, 0), S(140, 50), S(160, 50));
        for (int x = 142; x < 158; x++)
            Assert.Equal(before[50 * 200 + x - 100], doc.ActiveLayer.Image[x, 50]); // whole pixels, no blur
        Assert.Equal(Pixel.Opaque(Grey), doc.ActiveLayer.Image[150, 20]); // outside the brush
        Assert.True(doc.Undo());
        Assert.Equal(before, doc.ActiveLayer.Image.Pixels);
    }

    [Fact]
    public void Clone_reads_the_picture_as_it_was_so_overlapping_strokes_dont_repeat_and_edges_are_safe()
    {
        var doc = Textured();
        var before = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        // Source just 10 px to the left, painting rightwards over it: copies the original, not what was just painted.
        Run(doc, Clone(-10, 0, size: 8), S(20, 50), S(80, 50));
        for (int x = 24; x < 76; x++) Assert.Equal(before[50 * 200 + x - 10], doc.ActiveLayer.Image[x, 50]);
        // A source outside the picture copies nothing.
        Run(doc, Clone(0, -500), S(150, 50), S(152, 50));
        Assert.Equal(Pixel.Opaque(new ColorRgb(10, 10, 10)), doc.ActiveLayer.Image[150, 50]);
    }

    [Fact]
    public void Clone_needs_a_source_respects_selection_transparency_lock_and_palette_lock()
    {
        var doc = Textured();
        var r = new RetouchStroke(doc);
        Assert.True(r.Begin(new RetouchStyle(RetouchKind.Clone, 16, 1, 1, PressureCurve.Normal), S(10, 10)).IsEmpty);
        Assert.False(r.IsActive);

        // From what is seen (another layer), onto a locked palette: only palette colours land.
        var locked = Textured();
        var top = locked.AddLayer();
        var sampled = new RasterImage(200, 100);
        for (int i = 0; i < sampled.Pixels.Length; i++) sampled.Pixels[i] = Pixel.Opaque(new ColorRgb(30, 200, 90));
        locked.LockToPalette(new Palette("P", [Grey, new ColorRgb(10, 10, 10), new ColorRgb(0, 180, 80)]));
        Run(locked, Clone(0, 0, sampled: sampled), S(50, 50), S(60, 50));
        Assert.Equal(Pixel.Opaque(new ColorRgb(0, 180, 80)), top.Image[55, 50]);

        // Transparency lock: nothing appears on the empty layer.
        var alpha = Textured();
        var empty = alpha.AddLayer();
        alpha.SetLayerProperties(empty, empty.Properties with { AlphaLocked = true });
        Run(alpha, Clone(0, 0, sampled: alpha.Flatten()), S(50, 50), S(60, 50));
        Assert.All(empty.Image.Pixels, p => Assert.Equal(0u, p));

        // Selection: only inside.
        var sel = Textured();
        sel.SetSelection(SelectionMask.Rectangle(200, 100, new PixelRect(140, 0, 10, 100)), "Select");
        Run(sel, Clone(-100, 0), S(140, 50), S(160, 50));
        Assert.Equal(Pixel.Opaque(new ColorRgb(10, 10, 10)), sel.ActiveLayer.Image[152, 50]);
        Assert.NotEqual(Pixel.Opaque(new ColorRgb(10, 10, 10)), sel.ActiveLayer.Image[147, 50]);
    }

    // ================================================================== repair

    [Fact]
    public void Repair_without_a_source_fills_a_speck_from_its_surroundings()
    {
        var doc = new Document(120, 80, null, "Wall");
        var img = doc.ActiveLayer.Image;
        for (int y = 0; y < 80; y++)
            for (int x = 0; x < 120; x++)
                img[x, y] = Pixel.Opaque(new ColorRgb((byte)(80 + x), 100, 120)); // a gentle gradient
        for (int y = 38; y < 42; y++)
            for (int x = 58; x < 62; x++)
                img[x, y] = Pixel.Opaque(new ColorRgb(0, 0, 0)); // the speck
        doc.ActiveLayer.Touch(doc.Bounds);
        Run(doc, new RetouchStyle(RetouchKind.Repair, 14, 1, 0.8, PressureCurve.Normal), S(59, 39), S(61, 41));
        var (r, g, b, a) = Pixel.ToStraight(doc.ActiveLayer.Image[60, 40]);
        Assert.InRange(r, 130, 150);       // the gradient's value around x = 60 (140)
        Assert.InRange(g, 95, 105);
        Assert.InRange(b, 115, 125);
        Assert.Equal(255, a);
        Assert.Equal(Pixel.Opaque(new ColorRgb(80, 100, 120)), doc.ActiveLayer.Image[0, 0]);
        Assert.True(doc.Undo());
        Assert.Equal(Pixel.Opaque(new ColorRgb(0, 0, 0)), doc.ActiveLayer.Image[60, 40]);
    }

    [Fact]
    public void Repair_with_a_source_brings_its_texture_in_the_brightness_of_the_destination()
    {
        var doc = Textured();
        // Darker destination area on the right (grey 128), textured source on the left (110/150, average 130).
        // A brighter destination: 200.
        var img = doc.ActiveLayer.Image;
        for (int y = 0; y < 100; y++)
            for (int x = 100; x < 200; x++)
                if (Pixel.R(img[x, y]) == 128) img[x, y] = Pixel.Opaque(new ColorRgb(200, 200, 200));
        doc.ActiveLayer.Touch(doc.Bounds);
        Run(doc, new RetouchStyle(RetouchKind.Repair, 24, 1, 1, PressureCurve.Normal, Source: (-100, 0)), S(140, 50), S(160, 50));
        var values = new List<int>();
        for (int x = 144; x < 157; x++) values.Add(Pixel.R(doc.ActiveLayer.Image[x, 50]));
        double mean = values.Average();
        Assert.InRange(mean, 180, 220);                    // the destination's brightness…
        Assert.True(values.Max() - values.Min() > 25);     // …with the source's texture
        Assert.True(values.Min() > 60);                    // and the dark mark is gone
    }

    [Fact]
    public void Pull_push_fills_a_hole_smoothly_between_its_edges()
    {
        int w = 32, h = 1;
        var values = new float[w * 4];
        var weights = new float[w];
        for (int x = 0; x < w; x++)
        {
            bool known = x < 8 || x >= 24;
            weights[x] = known ? 1 : 0;
            float v = x < 8 ? 0.2f : 0.8f;
            for (int c = 0; c < 4; c++) values[x * 4 + c] = known ? v : 0;
        }
        var filled = RetouchStroke.PullPush(values, weights, w, h);
        Assert.Equal(0.2f, filled[0]);
        Assert.Equal(0.8f, filled[31 * 4]);
        for (int x = 8; x < 24; x++) Assert.InRange(filled[x * 4], 0.19f, 0.81f);
        Assert.True(filled[20 * 4] > filled[10 * 4]); // from one side to the other
    }
}

/// <summary>Mission 010 tools on an animation: they change the current drawing only (a held drawing is the same drawing
/// in every frame it is held, as with painting).</summary>
public class FinishingTouchesAnimationTests
{
    [Fact]
    public void Retouching_and_liquify_change_only_the_current_drawing_and_respect_holds()
    {
        var doc = Document.NewAnimation(120, 80, null, frames: 1);
        var layer = doc.ActiveLayer;
        var engine = new StrokeEngine(doc);
        engine.Begin(Paint(Hard(30), new ColorRgb(200, 40, 40)), S(20, 40));
        engine.Add(S(100, 40));
        engine.End();
        doc.ExtendHold(layer, 0);                 // frames 0 and 1 show the same drawing
        doc.SetCurrentFrame(1);
        doc.NewFrame(NewFrameContent.Duplicate);  // frame 2: an independent copy
        Assert.Equal(3, doc.FrameCount);
        Assert.Same(doc.CelAt(layer, 0), doc.CelAt(layer, 1));
        Assert.NotSame(doc.CelAt(layer, 1), doc.CelAt(layer, 2));
        var held = (uint[])doc.CelAt(layer, 0)!.Image.Pixels.Clone();
        var copy = (uint[])doc.CelAt(layer, 2)!.Image.Pixels.Clone();

        // Smudge on frame 2: only that drawing changes.
        doc.SetCurrentFrame(2);
        var r = new RetouchStroke(doc);
        r.Begin(new RetouchStyle(RetouchKind.Smudge, 20, 0.9, 0.5, PressureCurve.Normal), S(60, 40));
        r.Add([S(60, 70)]);
        r.End();
        Assert.NotEqual(copy, doc.CelAt(layer, 2)!.Image.Pixels);
        Assert.Equal(held, doc.CelAt(layer, 0)!.Image.Pixels);

        // Liquify on frame 1: the held drawing changes, so frames 0 and 1 both show it; frame 2 doesn't.
        doc.SetCurrentFrame(1);
        var smudged = (uint[])doc.CelAt(layer, 2)!.Image.Pixels.Clone();
        var l = Operations.LiquifySession.Begin(doc, layer, Operations.LiquifySampling.Smooth)!;
        l.Size = 50;
        l.Strength = 1;
        l.BeginStroke(60, 40);
        l.StrokeTo(60, 75);
        l.EndStroke();
        Assert.True(l.Apply());
        Assert.NotEqual(held, doc.CelAt(layer, 0)!.Image.Pixels);
        Assert.Same(doc.CelAt(layer, 0), doc.CelAt(layer, 1));
        Assert.Equal(smudged, doc.CelAt(layer, 2)!.Image.Pixels);
        Assert.Equal(3, doc.FrameCount); // timing untouched
        doc.Undo();
        Assert.Equal(held, doc.CelAt(layer, 0)!.Image.Pixels);
    }
}
