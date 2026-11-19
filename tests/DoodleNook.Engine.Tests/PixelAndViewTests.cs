using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using DoodleNook.Engine.Viewing;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public class PixelTests
{
    [Fact]
    public void Premultiply_then_unpremultiply_then_premultiply_is_lossless()
    {
        for (int a = 0; a < 256; a++)
        {
            for (int c = 0; c <= a; c++)
            {
                uint p = Pixel.Pack((byte)a, (byte)c, (byte)(a - c), (byte)(c / 2));
                var (r, g, b, aa) = Pixel.ToStraight(p);
                Assert.Equal(p, Pixel.FromStraight(r, g, b, aa));
            }
        }
    }

    [Fact]
    public void Straight_rgba_round_trip_preserves_transparency()
    {
        byte[] rgba =
        [
            255, 0, 0, 255, // opaque red
            0, 255, 0, 128, // half-transparent green
            0, 0, 255, 1,   // almost invisible blue
            9, 9, 9, 0,     // fully transparent
        ];
        var img = RasterImage.FromStraightRgba(2, 2, rgba);
        var back = img.ToStraightRgba();
        Assert.Equal(rgba[..12], back[..12]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, back[12..]); // colour of invisible pixels is not kept
        Assert.Equal(Pixel.Transparent, img[1, 1]);
    }

    [Fact]
    public void Over_composites_correctly()
    {
        uint red = Pixel.Pack(255, 255, 0, 0);
        uint halfBlue = Pixel.FromStraight(0, 0, 255, 128);
        Assert.Equal(red, Pixel.Over(red, halfBlue));
        Assert.Equal(halfBlue, Pixel.Over(Pixel.Transparent, halfBlue));
        uint mix = Pixel.Over(halfBlue, red);
        Assert.Equal(255, Pixel.A(mix));
        Assert.InRange(Pixel.R(mix), 125, 129);
        Assert.InRange(Pixel.B(mix), 126, 130);
    }

    [Fact]
    public void Region_copy_and_write_round_trip()
    {
        var img = new RasterImage(10, 10);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = (uint)i;
        var r = new PixelRect(2, 3, 4, 5);
        var copy = img.CopyRegion(r);
        Assert.Equal(20, copy.Length);
        Assert.Equal((uint)(3 * 10 + 2), copy[0]);
        var other = new RasterImage(10, 10);
        other.WriteRegion(r, copy);
        Assert.Equal(img[5, 7], other[5, 7]);
        Assert.Equal(0u, other[1, 1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => other.WriteRegion(new PixelRect(8, 8, 4, 4), new uint[16]));
    }

    [Fact]
    public void Colours_parse_format_and_convert()
    {
        Assert.Equal(new ColorRgb(0x3b, 0x2f, 0x2a), ColorRgb.Parse("#3B2F2A"));
        Assert.Equal(new ColorRgb(255, 170, 0), ColorRgb.Parse("fa0"));
        Assert.False(ColorRgb.TryParse("#12345", out _));
        Assert.False(ColorRgb.TryParse("zzzzzz", out _));
        Assert.Equal("#E9A94D", new ColorRgb(0xe9, 0xa9, 0x4d).ToHex());
        foreach (var hex in new[] { "#000000", "#FFFFFF", "#E9A94D", "#3B2F2A", "#5F8FA8", "#FF0000", "#00FF7F" })
        {
            var c = ColorRgb.Parse(hex);
            var (h, s, v) = c.ToHsv();
            Assert.Equal(c, ColorRgb.FromHsv(h, s, v));
        }
    }

    [Fact]
    public void Rect_union_and_intersection()
    {
        var a = new PixelRect(0, 0, 10, 10);
        var b = new PixelRect(5, 5, 10, 10);
        Assert.Equal(new PixelRect(0, 0, 15, 15), a.Union(b));
        Assert.Equal(new PixelRect(5, 5, 5, 5), a.Intersect(b));
        Assert.True(a.Intersect(new PixelRect(20, 20, 2, 2)).IsEmpty);
        Assert.Equal(a, PixelRect.Empty.Union(a));
        Assert.Equal(new PixelRect(-1, 2, 3, 2), PixelRect.FromBounds(-0.5, 2.0, 1.2, 3.5));
    }
}

public class ViewTransformTests
{
    [Theory]
    [InlineData(1.0, 0, 0, false)]
    [InlineData(0.37, 120.5, -33, false)]
    [InlineData(4.0, -800, 20, true)]
    [InlineData(12.5, 3, 7, true)]
    public void View_and_document_coordinates_round_trip(double scale, double ox, double oy, bool flip)
    {
        var v = new ViewTransform(1920, 1080);
        v.Set(scale, ox, oy);
        if (flip) v.ToggleFlip(1000);
        foreach (var (x, y) in new[] { (0.0, 0.0), (17.25, 900.5), (1919.9, 1079.9) })
        {
            var (vx, vy) = v.DocumentToView(x, y);
            var (dx, dy) = v.ViewToDocument(vx, vy);
            Assert.Equal(x, dx, 9);
            Assert.Equal(y, dy, 9);
        }
    }

    [Fact]
    public void Zooming_keeps_the_point_under_the_cursor_still()
    {
        var v = new ViewTransform(2000, 1000);
        v.Set(0.5, 40, 60);
        var before = v.ViewToDocument(300, 200);
        v.ZoomAround(300, 200, 3.0);
        var after = v.ViewToDocument(300, 200);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
        Assert.Equal(3.0, v.Scale);
    }

    [Fact]
    public void Fit_centres_the_whole_document()
    {
        var v = new ViewTransform(1600, 1000);
        v.Fit(800, 800, margin: 20, maxScale: 1);
        Assert.Equal(760.0 / 1600, v.Scale, 9);
        var (l, t) = v.DocumentToView(0, 0);
        var (r, b) = v.DocumentToView(1600, 1000);
        Assert.Equal(20, l, 6);
        Assert.Equal(800 - 20, r, 6);
        Assert.Equal(800 - b, t, 6);
    }

    [Fact]
    public void Fit_never_enlarges_a_small_document_beyond_the_limit()
    {
        var v = new ViewTransform(100, 50);
        v.Fit(1000, 800, 20, maxScale: 1);
        Assert.Equal(1, v.Scale);
    }

    [Fact]
    public void Flip_mirrors_the_view_around_the_viewport_centre()
    {
        var v = new ViewTransform(100, 100);
        v.Set(2, 50, 0); // document spans view x 50..250
        v.ToggleFlip(400);
        Assert.True(v.FlipHorizontal);
        Assert.Equal(350, v.DocumentToView(0, 0).X, 9);  // left edge now on the right
        Assert.Equal(150, v.DocumentToView(100, 0).X, 9);
        v.ToggleFlip(400);
        Assert.Equal(50, v.DocumentToView(0, 0).X, 9);
    }

    [Theory]
    [InlineData(1.0, 1, 1.5)]
    [InlineData(1.0, -1, 0.75)]
    [InlineData(0.9, 1, 1.0)]
    [InlineData(32, 1, 32)]
    [InlineData(0.0625, -1, 0.0625)]
    public void Zoom_steps(double current, int dir, double expected) =>
        Assert.Equal(expected, ViewTransform.NextStep(current, dir));

    [Fact]
    public void Drawing_lands_on_the_same_pixels_at_any_zoom_or_flip()
    {
        // The same document-space gesture, entered through views with different zoom/pan/flip,
        // must produce identical pixels: the view only changes what you see.
        uint[]? reference = null;
        foreach (var (scale, flip) in new[] { (1.0, false), (0.25, false), (3.0, true), (7.5, false), (0.6, true) })
        {
            var view = new ViewTransform(300, 200);
            view.Set(scale, 37, -12);
            if (flip) view.ToggleFlip(640);
            var docPoints = new[] { (20.0, 30.0), (150.25, 120.5), (280.0, 40.0) };
            var samples = docPoints.Select(p =>
            {
                var (vx, vy) = view.DocumentToView(p.Item1, p.Item2); // where the pen would be on screen
                var (dx, dy) = view.ViewToDocument(vx, vy);           // what the canvas maps it back to
                return new StrokeSample(dx, dy, 0.8f);
            }).ToArray();
            var doc = new Document(300, 200);
            Stroke(doc, Paint(Hard(9, pressureSize: true)), samples);
            if (reference is null) reference = Snapshot(doc);
            else Assert.Equal(reference, doc.Layer.Pixels);
        }
    }
}
