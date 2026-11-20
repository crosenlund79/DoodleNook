using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 010 — Perspective and Warp modes of the free transform.</summary>
public class PerspectiveWarpTests
{
    private static readonly ColorRgb Red = new(200, 40, 40), Blue = new(40, 60, 200);

    /// <summary>200 × 160, transparent, with a 60 × 40 red/blue striped "sign" at (40, 40).</summary>
    private static Document Sign()
    {
        var doc = new Document(200, 160, null, "Art");
        var img = doc.ActiveLayer.Image;
        for (int y = 40; y < 80; y++)
            for (int x = 40; x < 100; x++)
                img[x, y] = Pixel.Opaque((x / 10) % 2 == 0 ? Red : Blue);
        doc.ActiveLayer.Touch(doc.Bounds);
        return doc;
    }

    [Fact]
    public void A_homography_puts_the_corners_exactly_where_asked_and_inverts()
    {
        var quad = new (double, double)[] { (10, 20), (110, 5), (130, 90), (0, 70) };
        var h = Homography.FromRect(60, 40, quad)!.Value;
        Assert.Equal((10, 20), Round(h.Apply(0, 0)));
        Assert.Equal((110, 5), Round(h.Apply(60, 0)));
        Assert.Equal((130, 90), Round(h.Apply(60, 40)));
        Assert.Equal((0, 70), Round(h.Apply(0, 40)));
        var inv = h.Invert()!.Value;
        var (u, v) = inv.Apply(h.Apply(25, 13).X, h.Apply(25, 13).Y);
        Assert.Equal(25, u, 6);
        Assert.Equal(13, v, 6);
        // Crossed or flat corners can't be a picture.
        Assert.Null(Homography.FromRect(60, 40, [(0, 0), (60, 0), (0, 40), (60, 40)]));
        Assert.Null(Homography.FromRect(60, 40, [(0, 0), (30, 0), (60, 0), (0, 40)]));
        // A vanishing line through the origin (its inverse has no constant term) still works.
        var trapezoid = Homography.FromRect(60, 40, [(55, 40), (85, 40), (100, 80), (40, 80)])!.Value;
        var back = trapezoid.Invert()!.Value.Apply(70, 60);
        Assert.Equal((70.0, 60.0), Round(trapezoid.Apply(back.X, back.Y)));

        static (double, double) Round((double X, double Y) p) => (Math.Round(p.X, 6), Math.Round(p.Y, 6));
    }

    [Fact]
    public void Perspective_tilts_the_selection_and_apply_is_one_undo_step_with_the_selection_following()
    {
        var doc = Sign();
        var before = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        doc.SetSelection(SelectionMask.Rectangle(200, 160, new PixelRect(40, 40, 60, 40)), "Select");
        var t = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
        t.Interpolation = Interpolation.Nearest;
        t.SetMode(TransformMode.Perspective);
        Assert.Equal((40.0, 40.0), t.Corners[0]);
        Assert.Equal((100.0, 80.0), t.Corners[2]);
        // Pull the top edge in: a sign seen from below.
        Assert.True(t.SetCorners([(55, 40), (85, 40), (100, 80), (40, 80)]));
        Assert.False(t.SetCorners([(85, 40), (55, 40), (100, 80), (40, 80)])); // crossed: refused, the last corners stay
        Assert.True(t.Commit());
        var img = doc.ActiveLayer.Image;
        Assert.Equal(0u, img[45, 42]);                 // the top-left corner is now empty
        Assert.NotEqual(0u, img[70, 42]);              // the top middle still shows the sign
        Assert.NotEqual(0u, img[42, 78]);              // the bottom keeps its width
        Assert.True(doc.Selection!.Bounds.Width <= 61);
        Assert.Equal(0, doc.Selection[45, 42]);        // the selection follows the new shape
        Assert.Equal(255, doc.Selection[70, 60]);
        doc.Undo();
        Assert.Equal(before, img.Pixels);
    }

    [Fact]
    public void An_untouched_warp_grid_changes_nothing_and_cancel_restores()
    {
        var doc = Sign();
        var before = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        doc.SetSelection(SelectionMask.Rectangle(200, 160, new PixelRect(40, 40, 60, 40)), "Select");
        var t = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
        t.Interpolation = Interpolation.Nearest;
        t.SetMode(TransformMode.Warp);
        Assert.Equal(16, t.Warp!.Points.Count);
        Assert.Equal((40.0, 40.0), t.Warp.Points[0]);
        Assert.Equal((100.0, 80.0), t.Warp.Points[15]);
        t.Commit();
        Assert.Equal(before, doc.ActiveLayer.Image.Pixels);

        var t2 = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
        t2.SetMode(TransformMode.Warp);
        t2.SetWarpPoint(5, 60, 70);
        t2.Cancel();
        Assert.Equal(before, doc.ActiveLayer.Image.Pixels);
    }

    [Fact]
    public void Dragging_a_warp_point_bends_the_picture_smoothly_around_it()
    {
        var doc = Sign();
        doc.SetSelection(SelectionMask.Rectangle(200, 160, new PixelRect(40, 40, 60, 40)), "Select");
        var t = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
        t.Interpolation = Interpolation.Nearest;
        t.SetMode(TransformMode.Warp);
        // Pull the two bottom inner points down by 30: the sign's bottom edge sags like a banner.
        t.SetWarpPoint(13, t.Warp!.Points[13].X, t.Warp.Points[13].Y + 30);
        t.SetWarpPoint(14, t.Warp.Points[14].X, t.Warp.Points[14].Y + 30);
        t.Commit();
        var img = doc.ActiveLayer.Image;
        Assert.NotEqual(0u, img[70, 100]);  // the middle now reaches lower
        Assert.Equal(0u, img[41, 100]);     // the corners stay where they were
        Assert.NotEqual(0u, img[42, 78]);
        Assert.Equal(0u, img[70, 115]);     // and it doesn't run past the pulled points
    }

    [Fact]
    public void Pixel_art_keeps_exact_colours_and_a_locked_palette_keeps_its_own()
    {
        var doc = Sign();
        doc.SetSelection(SelectionMask.Rectangle(200, 160, new PixelRect(40, 40, 60, 40)), "Select");
        var t = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
        t.Interpolation = Interpolation.Nearest;
        t.SetMode(TransformMode.Perspective);
        t.SetCorners([(50, 30), (95, 45), (110, 90), (35, 75)]);
        t.Commit();
        var allowed = new HashSet<uint> { 0u, Pixel.Opaque(Red), Pixel.Opaque(Blue) };
        Assert.All(doc.ActiveLayer.Image.Pixels, p => Assert.Contains(p, allowed));

        var locked = Sign();
        locked.LockToPalette(new Palette("P", [Red, Blue]));
        locked.SetSelection(SelectionMask.Rectangle(200, 160, new PixelRect(40, 40, 60, 40)), "Select");
        t = FloatingTransform.Begin(locked, locked.ActiveLayer)!;
        t.Interpolation = Interpolation.Bilinear;
        t.SetMode(TransformMode.Warp);
        t.SetWarpPoint(5, 62, 58);
        var conformer = new PaletteConformer([Red, Blue]);
        Assert.All(locked.Floating!.Image.Pixels, p => Assert.True(conformer.IsPaletteColour(p))); // the preview already keeps to the palette
        t.Commit();
        Assert.All(locked.ActiveLayer.Image.Pixels, p => Assert.True(conformer.IsPaletteColour(p)));
    }
}
