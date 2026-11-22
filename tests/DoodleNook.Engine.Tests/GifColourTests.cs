using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.References;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Mission 016 — regression fixtures for the animated GIF colour defect: anti-aliased artwork on white paper came out
/// slate-coloured (an orange #E89A5E shirt became #3C4B61) because the palette's median split never separated the paper
/// from the artwork. Every GIF here is read back with the independent <see cref="TinyGif"/> decoder and compared with
/// the source pixels, frame by frame; the engine's own importer must agree with it pixel for pixel.
/// </summary>
public sealed class GifColourTests
{
    private static readonly ColorRgb Paper = ColorRgb.White;
    private static readonly ColorRgb Orange = new(0xE8, 0x9A, 0x5E);
    private static readonly ColorRgb Slate = new(0x3C, 0x4B, 0x61);

    private sealed record Result(TinyGif Gif, int MaxError, double MeanError, int TransparencyMismatches);

    /// <summary>Exports, decodes independently, and measures the largest per-channel error of every opaque pixel.</summary>
    private static Result Export(IReadOnlyList<RasterImage> images, GifOptions? options = null)
    {
        var frames = images.Select((im, i) => new ExportFrame(im, 100 + 20 * i, i)).ToList();
        using var ms = new MemoryStream();
        GifEncoder.Write(frames, ms, options ?? new GifOptions());
        var bytes = ms.ToArray();
        var gif = TinyGif.Decode(bytes);
        Assert.Equal(images.Count, gif.Frames.Count);

        // The engine's importer (used by Import animation…) must read exactly what the independent decoder reads.
        var imported = GifDecoder.Decode(bytes);
        Assert.Equal(images.Count, imported.Frames.Count);

        int max = 0, mismatches = 0;
        double sum = 0;
        long n = 0;
        for (int f = 0; f < images.Count; f++)
        {
            var src = images[f];
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    var d = gif.Frames[f].Rgba(x, y);
                    var e = Pixel.ToStraight(imported.Frames[f][x, y]);
                    Assert.Equal(d.A == 0, e.A == 0);
                    if (d.A != 0) Assert.Equal((d.R, d.G, d.B), (e.R, e.G, e.B));

                    uint p = src[x, y];
                    if (Pixel.A(p) < 128)
                    {
                        if (d.A != 0) mismatches++;
                        continue;
                    }
                    if (d.A == 0)
                    {
                        mismatches++;
                        continue;
                    }
                    var (r, g, b, _) = Pixel.ToStraight(p);
                    int err = Math.Max(Math.Abs(r - d.R), Math.Max(Math.Abs(g - d.G), Math.Abs(b - d.B)));
                    max = Math.Max(max, err);
                    sum += err;
                    n++;
                }
        }
        return new Result(gif, max, sum / Math.Max(1, n), mismatches);
    }

    private static RasterImage OnPaper(RasterImage art)
    {
        var img = new RasterImage(art.Width, art.Height);
        img.Fill(Pixel.Opaque(Paper));
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Pixel.Over(art.Pixels[i], img.Pixels[i]);
        return img;
    }

    private static List<RasterImage> WalkFigures()
    {
        var walk = ReferenceLibrary.Find("walk")!;
        var framing = new ReferenceFraming(ReferenceView.Side, ReferenceFacing.Right, 160, 100, 190, HardEdges: false, Marker: false);
        return Enumerable.Range(0, walk.Frames).Select(d => ReferenceRenderer.Render(walk.Pose(d), 200, 200, framing)).ToList();
    }

    private static int Distance(ColorRgb a, (int R, int G, int B, int A) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    [Fact]
    public void Anti_aliased_artwork_on_white_paper_keeps_its_colours_in_every_frame()
    {
        // The reported case: the walk reference over white paper (about 900 colours from soft edges and the shadow).
        var images = WalkFigures().Select(OnPaper).ToList();
        var result = Export(images);
        Assert.True(result.MaxError <= 12, $"largest error {result.MaxError}");
        Assert.True(result.MeanError < 0.5, $"mean error {result.MeanError:F2}");

        int orange = 0;
        for (int f = 0; f < images.Count; f++)
            for (int y = 0; y < 200; y++)
                for (int x = 0; x < 200; x++)
                {
                    var s = Pixel.ToStraight(images[f][x, y]);
                    var d = result.Gif.Frames[f].Rgba(x, y);
                    if (s.R == 255 && s.G == 255 && s.B == 255) Assert.Equal((255, 255, 255, 255), d); // the paper stays white
                    if (Distance(Orange, (s.R, s.G, s.B, 255)) <= 4)
                    {
                        orange++;
                        Assert.True(Distance(Orange, d) <= 6, $"orange became {d} in frame {f} at {x},{y}");
                    }
                }
        Assert.True(orange > 1000); // the limbs really are orange in the source
    }

    [Fact]
    public void Orange_and_slate_strokes_on_paper_stay_orange_and_slate()
    {
        // A soft-edged orange disc, shaded underneath, with a slate ring and a soft grey shadow on white paper: well over
        // 256 colours, the paper by far the most common one.
        var img = new RasterImage(240, 180);
        img.Fill(Pixel.Opaque(Paper));
        for (int y = 0; y < 180; y++)
            for (int x = 0; x < 240; x++)
            {
                double shadow = Math.Clamp(1 - Math.Sqrt((x - 130) * (x - 130) / 9.0 + (y - 160) * (y - 160)) / 25, 0, 1) * 0.7;
                double d = Math.Sqrt((x - 120) * (x - 120) + (y - 80) * (y - 80));
                double disc = Math.Clamp(50.5 - d, 0, 1), ring = Math.Clamp(Math.Min(d - 49.5, 56.5 - d), 0, 1);
                var c = Mix(Paper, new ColorRgb(80, 80, 90), shadow);
                var shaded = Mix(Orange, new ColorRgb(150, 70, 40), Math.Clamp((y - 90) / 40.0, 0, 1)); // painted shading below
                c = Mix(c, shaded, disc);
                c = Mix(c, Slate, ring);
                img[x, y] = Pixel.Opaque(c);
            }
        Assert.True(img.Pixels.Distinct().Count() > 256);

        var result = Export([img]);
        Assert.True(result.MaxError <= 12, $"largest error {result.MaxError}");
        var frame = result.Gif.Frames[0];
        Assert.Equal((255, 255, 255, 255), frame.Rgba(5, 5));
        Assert.True(Distance(Orange, frame.Rgba(120, 80)) <= 2, $"orange became {frame.Rgba(120, 80)}");
        Assert.True(Distance(Slate, frame.Rgba(173, 80)) <= 2, $"slate became {frame.Rgba(173, 80)}");

        static ColorRgb Mix(ColorRgb a, ColorRgb b, double t) =>
            new((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    [Fact]
    public void Transparent_artwork_keeps_its_shape_colours_and_restore_to_background_disposal()
    {
        var images = WalkFigures();
        var result = Export(images);
        Assert.Equal(0, result.TransparencyMismatches);
        Assert.True(result.MaxError <= 12, $"largest error {result.MaxError}");
        Assert.All(result.Gif.Frames, f => Assert.Equal(2, f.Disposal));
        Assert.All(result.Gif.Frames, f => Assert.True(f.Transparent >= 0));
        Assert.Equal(Enumerable.Range(0, images.Count).Select(i => 10 + 2 * i).ToArray(), result.Gif.Frames.Select(f => f.DelayCs).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Soft_gradients_with_thousands_of_colours_stay_smooth(bool dither)
    {
        var images = Enumerable.Range(0, 4).Select(f =>
        {
            var img = new RasterImage(256, 160);
            for (int y = 0; y < 160; y++)
                for (int x = 0; x < 256; x++)
                {
                    double t = y / 159.0, glow = Math.Max(0, 1 - Math.Sqrt((x - 80 - f * 30) * (x - 80 - f * 30) + (y - 80) * (y - 80)) / 70.0);
                    double r = 90 + 160 * t, g = 150 + 60 * t, b = 230 - 80 * t;
                    r += (255 - r) * glow * glow;
                    g += (230 - g) * glow * glow;
                    b += (120 - b) * glow * glow;
                    img[x, y] = Pixel.Opaque(new ColorRgb((byte)r, (byte)g, (byte)b));
                }
            return img;
        }).ToList();
        var result = Export(images, new GifOptions(Dither: dither));
        Assert.True(result.MeanError < (dither ? 1.5 : 1.0), $"mean error {result.MeanError:F2}");
        Assert.True(result.MaxError <= (dither ? 16 : 8), $"largest error {result.MaxError}");
    }

    [Fact]
    public void Limited_palette_pixel_art_keeps_exact_colours_in_every_frame()
    {
        int[] palette = [0x1a1c2c, 0x5d275d, 0xb13e53, 0xef7d57, 0xffcd75, 0xa7f070, 0x38b764, 0x257179, 0x29366f, 0x3b5dc9, 0x41a6f6, 0x73eff7];
        var images = Enumerable.Range(0, 4).Select(f =>
        {
            var img = new RasterImage(32, 32);
            for (int y = 4; y < 28; y++)
                for (int x = 4; x < 28; x++)
                    if ((x - 16) * (x - 16) + (y - 16) * (y - 16) < 140)
                    {
                        int c = palette[(x / 3 + y / 4 + f) % palette.Length];
                        img[x, y] = Pixel.Opaque(new ColorRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c));
                    }
            return img;
        }).ToList();
        foreach (bool dither in new[] { false, true }) // dithering never touches pictures whose colours fit exactly
        {
            var result = Export(images, new GifOptions(Dither: dither));
            Assert.Equal(0, result.MaxError);
            Assert.Equal(0, result.TransparencyMismatches);
        }
    }

    [Fact]
    public void More_than_256_colours_without_a_dominant_one_are_approximated_evenly()
    {
        var images = Enumerable.Range(0, 2).Select(f =>
        {
            var img = new RasterImage(200, 200);
            var rnd = new Random(5 + f);
            for (int y = 0; y < 200; y++)
                for (int x = 0; x < 200; x++)
                {
                    double a = Math.Atan2(y - 100, x - 100) + f * 0.3, rad = Math.Min(1, Math.Sqrt((x - 100) * (x - 100) + (y - 100) * (y - 100)) / 100);
                    byte C(double ph) => (byte)Math.Clamp(128 + 127 * rad * Math.Sin(a + ph) + rnd.Next(-6, 7), 0, 255);
                    img[x, y] = Pixel.Opaque(new ColorRgb(C(0), C(2.094), C(4.189)));
                }
            return img;
        }).ToList();
        var result = Export(images);
        Assert.True(result.MeanError < 9, $"mean error {result.MeanError:F2}");
        Assert.True(result.MaxError <= 32, $"largest error {result.MaxError}");
    }

    [Fact]
    public void Colours_that_only_appear_in_a_later_frame_are_kept_even_after_a_very_colourful_first_frame()
    {
        // Frame 1 has 120,000 different colours (the palette counts them in groups past 65,536); frame 2 adds a green
        // area that no earlier pixel had. Earlier versions stopped counting colours after the first 100,000.
        var first = new RasterImage(400, 300);
        for (int i = 0; i < first.Pixels.Length; i++) first.Pixels[i] = Pixel.Opaque(new ColorRgb((byte)(i % 256), (byte)(i / 256 % 256), (byte)(80 + i / 65536 * 40)));
        var second = first.Clone();
        for (int y = 100; y < 200; y++)
            for (int x = 100; x < 200; x++) second[x, y] = Pixel.Opaque(new ColorRgb(20, 200, 40));
        var result = Export([first, second]);
        Assert.True(Distance(new ColorRgb(20, 200, 40), result.Gif.Frames[1].Rgba(150, 150)) <= 8, $"green became {result.Gif.Frames[1].Rgba(150, 150)}");
    }
}
