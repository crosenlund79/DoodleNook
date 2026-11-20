using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 010 — Liquify: a displacement field over a stable copy of the layer.</summary>
public class LiquifyTests
{
    private static readonly ColorRgb Red = new(200, 40, 40), Blue = new(40, 60, 200);

    /// <summary>A 200 × 120 transparent picture with a red bar at x 50–59 (full height) and a blue square at 130–149, 50–69.</summary>
    private static Document Picture()
    {
        var doc = new Document(200, 120, null, "Art");
        var img = doc.ActiveLayer.Image;
        for (int y = 0; y < 120; y++)
            for (int x = 50; x < 60; x++)
                img[x, y] = Pixel.Opaque(Red);
        for (int y = 50; y < 70; y++)
            for (int x = 130; x < 150; x++)
                img[x, y] = Pixel.Opaque(Blue);
        doc.ActiveLayer.Touch(doc.Bounds);
        return doc;
    }

    private static LiquifySession Start(Document doc, LiquifyMode mode, double size = 60, double strength = 1, LiquifySampling sampling = LiquifySampling.Smooth)
    {
        var l = LiquifySession.Begin(doc, doc.ActiveLayer, sampling)!;
        l.Mode = mode;
        l.Size = size;
        l.Strength = strength;
        return l;
    }

    private static void Drag(LiquifySession l, params (double X, double Y)[] points)
    {
        l.BeginStroke(points[0].X, points[0].Y);
        foreach (var p in points.Skip(1)) l.StrokeTo(p.X, p.Y);
        l.EndStroke();
    }

    private static int Count(Document doc, ColorRgb c, PixelRect area)
    {
        int n = 0;
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                var (r, g, b, a) = Pixel.ToStraight(doc.ActiveLayer.Image[x, y]);
                if (a > 128 && Math.Abs(r - c.R) < 40 && Math.Abs(g - c.G) < 40 && Math.Abs(b - c.B) < 40) n++;
            }
        return n;
    }

    [Fact]
    public void Push_moves_the_picture_with_the_brush_and_one_apply_is_one_undo_step()
    {
        var doc = Picture();
        var original = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        var l = Start(doc, LiquifyMode.Push);
        Drag(l, (55, 60), (65, 60), (75, 60), (85, 60));
        // The bar's middle has been pushed right; its ends (far from the brush) stay put.
        Assert.True(Count(doc, Red, new PixelRect(70, 55, 20, 10)) > 50);
        Assert.Equal(Pixel.Opaque(Red), doc.ActiveLayer.Image[55, 2]);
        Assert.True(l.FieldBytes > 0);
        Assert.True(l.Apply());
        Assert.Equal(0, l.FieldBytes); // the working copies are released
        Assert.True(doc.CanUndo);
        doc.Undo();
        Assert.Equal(original, doc.ActiveLayer.Image.Pixels);
        Assert.False(doc.CanUndo);
        doc.Redo();
        Assert.True(Count(doc, Red, new PixelRect(70, 55, 20, 10)) > 50);
    }

    [Fact]
    public void The_result_follows_the_gesture_not_the_number_of_input_events()
    {
        var few = Picture();
        var many = Picture();
        var a = Start(few, LiquifyMode.Push);
        Drag(a, (40, 60), (100, 70));
        a.Apply();
        var b = Start(many, LiquifyMode.Push);
        var pts = Enumerable.Range(0, 61).Select(i => (40.0 + i, 60.0 + i / 6.0)).ToArray();
        Drag(b, pts);
        b.Apply();
        int worst = 0;
        for (int i = 0; i < few.ActiveLayer.Image.Pixels.Length; i++)
            worst = Math.Max(worst, Math.Abs(Pixel.A(few.ActiveLayer.Image.Pixels[i]) - Pixel.A(many.ActiveLayer.Image.Pixels[i])));
        Assert.True(worst <= 40, $"largest alpha difference {worst}");
    }

    [Fact]
    public void Inflate_grows_pinch_shrinks_and_twirl_turns_while_the_pen_rests()
    {
        var region = new PixelRect(110, 30, 60, 60);
        int before = Count(Picture(), Blue, region);
        var big = Picture();
        var l = Start(big, LiquifyMode.Inflate, size: 70, strength: 0.6);
        l.BeginStroke(140, 60);
        l.Hold(900);
        l.EndStroke();
        Assert.True(Count(big, Blue, region) > before * 1.3, $"{Count(big, Blue, region)} vs {before}");

        var small = Picture();
        l = Start(small, LiquifyMode.Pinch, size: 70, strength: 0.6);
        l.BeginStroke(140, 60);
        l.Hold(900);
        l.EndStroke();
        Assert.True(Count(small, Blue, region) < before * 0.8, $"{Count(small, Blue, region)} vs {before}");

        var turned = Picture();
        l = Start(turned, LiquifyMode.Twirl, size: 70, strength: 0.6);
        l.BeginStroke(140, 60);
        l.Hold(600);
        l.EndStroke();
        // The square's corner region changes as it turns; the amount of blue stays about the same.
        Assert.NotEqual(Picture().ActiveLayer.Image[148, 51], turned.ActiveLayer.Image[148, 51]);
        Assert.InRange(Count(turned, Blue, region), before * 0.85, before * 1.15);
    }

    [Fact]
    public void Restore_brings_the_original_back_locally_and_reset_and_cancel_undo_everything()
    {
        var doc = Picture();
        var original = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        var l = Start(doc, LiquifyMode.Push);
        Drag(l, (55, 30), (80, 30));
        Drag(l, (55, 90), (80, 90));
        // A strong push carries the bar along (about 22 px for this 25 px drag).
        Assert.Equal(0u, original[30 * 200 + 76]);
        Assert.NotEqual(0u, doc.ActiveLayer.Image[76, 30]);
        // Restore only the top push.
        l.Mode = LiquifyMode.Restore;
        l.Strength = 1;
        l.Size = 90;
        l.BeginStroke(65, 30);
        l.Hold(3000);
        l.EndStroke();
        Assert.Equal(original[30 * 200 + 76], doc.ActiveLayer.Image[76, 30]);
        Assert.NotEqual(original[90 * 200 + 76], doc.ActiveLayer.Image[76, 90]);
        // Reset: everything back, the session goes on.
        l.Reset();
        Assert.Equal(original, doc.ActiveLayer.Image.Pixels);
        l.Mode = LiquifyMode.Push;
        Drag(l, (55, 60), (85, 60));
        // Cancel: the layer as it was, nothing recorded.
        l.Cancel();
        Assert.Equal(original, doc.ActiveLayer.Image.Pixels);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void Liquify_stays_inside_the_selection_and_a_transparency_lock_keeps_the_shape()
    {
        var doc = Picture();
        doc.SetSelection(SelectionMask.Rectangle(200, 120, new PixelRect(0, 0, 200, 50)), "Select");
        var l = Start(doc, LiquifyMode.Push);
        Drag(l, (55, 50), (85, 50));
        l.Apply();
        Assert.Equal(0u, doc.ActiveLayer.Image[83, 55]);  // below the selection: untouched
        Assert.NotEqual(0u, doc.ActiveLayer.Image[83, 45]); // inside: the bar was pushed here

        var locked = Picture();
        locked.SetLayerProperties(locked.ActiveLayer, locked.ActiveLayer.Properties with { AlphaLocked = true });
        var alpha = locked.ActiveLayer.Image.Pixels.Select(Pixel.A).ToArray();
        l = Start(locked, LiquifyMode.Push);
        Drag(l, (40, 60), (140, 60));
        l.Apply();
        Assert.Equal(alpha, locked.ActiveLayer.Image.Pixels.Select(Pixel.A).ToArray());
    }

    [Fact]
    public void Pixel_art_keeps_exact_pixels_and_a_locked_palette_keeps_its_colours_with_soft_transparency()
    {
        var doc = Picture();
        var l = Start(doc, LiquifyMode.Push, sampling: LiquifySampling.Pixels);
        Drag(l, (40, 60), (75, 66));
        l.Apply();
        var allowed = new HashSet<uint> { 0u, Pixel.Opaque(Red), Pixel.Opaque(Blue) };
        Assert.All(doc.ActiveLayer.Image.Pixels, p => Assert.Contains(p, allowed)); // no new colours, no half pixels

        var locked = Picture();
        locked.LockToPalette(new Palette("P", [Red, Blue]));
        l = Start(locked, LiquifyMode.Twirl, size: 80, sampling: LiquifySampling.PaletteSafe);
        l.BeginStroke(140, 60);
        l.Hold(500);
        l.EndStroke();
        l.Apply();
        var conformer = new PaletteConformer([Red, Blue]);
        Assert.All(locked.ActiveLayer.Image.Pixels, p => Assert.True(conformer.IsPaletteColour(p)));
        Assert.Contains(locked.ActiveLayer.Image.Pixels, p => Pixel.A(p) is > 0 and < 255); // edges stay soft
    }

    [Fact]
    public void Text_layers_and_locked_layers_are_not_liquified()
    {
        var doc = Picture();
        doc.SetLayerProperties(doc.ActiveLayer, doc.ActiveLayer.Properties with { Locked = true });
        Assert.Null(LiquifySession.Begin(doc, doc.ActiveLayer, LiquifySampling.Smooth));
        var text = doc.AddTextLayer(new TextContent("Hi", "Varela Round", 20, Red, X: 10, Y: 10));
        Assert.Null(LiquifySession.Begin(doc, text, LiquifySampling.Smooth));
    }
}
