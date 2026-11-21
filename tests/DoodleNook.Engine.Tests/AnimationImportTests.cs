using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 012 — importing animations for reconstruction: GIF and APNG frames (made by an independent encoder,
/// Pillow) composed exactly as Chromium shows them, numbered PNG sequences in natural order, timing kept, and the
/// four-layer document.</summary>
public class AnimationImportTests
{
    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "DoodleNook.Engine.Tests", "Fixtures", "animation", name);
    }

    private static ImportedAnimation Read(string name)
    {
        var bytes = File.ReadAllBytes(Fixture(name));
        return name.EndsWith(".gif") ? GifDecoder.Decode(bytes) : ApngDecoder.Decode(bytes);
    }

    /// <summary>Each frame equals the frame Chromium shows (decoded once with its WebCodecs ImageDecoder, see
    /// Fixtures/animation/make_expected_chromium.js; straight RGBA).</summary>
    private static void AssertFramesMatchChromium(string file)
    {
        var anim = Read(file);
        var line = File.ReadAllLines(Fixture("chromium-frames.txt")).Single(l => l.StartsWith(file + " ", StringComparison.Ordinal)).Split(' ');
        int n = int.Parse(line[1]), w = int.Parse(line[2]), h = int.Parse(line[3]);
        Assert.Equal(n, anim.Frames.Count);
        Assert.Equal((w, h), (anim.Width, anim.Height));
        string name = Path.GetFileNameWithoutExtension(file);
        for (int i = 0; i < n; i++)
        {
            var expected = RasterImage.FromStraightRgba(w, h, File.ReadAllBytes(Fixture($"chromium-{name}-{i:000}.rgba")));
            var got = anim.Frames[i];
            for (int p = 0; p < expected.Pixels.Length; p++)
            {
                uint e = expected.Pixels[p], g = got.Pixels[p];
                Assert.True(e == g, $"{file} frame {i} pixel ({p % w}, {p / w}): Chromium {e:X8}, DoodleNook {g:X8}");
            }
        }
    }

    [Theory]
    [InlineData("walk-dispose1.gif")]
    [InlineData("walk-dispose2.gif")]
    [InlineData("walk-dispose3.gif")]
    [InlineData("walk-opaque-interlaced.gif")]
    [InlineData("walk-apng-rgba.png")]
    [InlineData("walk-apng-over.png")]
    [InlineData("walk-apng-previous.png")]
    [InlineData("walk-apng-palette.png")]
    [InlineData("walk-apng-grey.png")]
    public void Every_frame_is_composed_exactly_as_a_browser_shows_it(string file) => AssertFramesMatchChromium(file);

    [Fact]
    public void Gif_and_apng_timing_is_kept_and_short_gif_delays_play_like_browsers()
    {
        Assert.Equal(new int?[] { 80, 80, 120, 80, 200, 80 }, Read("walk-dispose2.gif").DelaysMs);
        Assert.Equal(new int?[] { 80, 80, 120, 80, 200, 80 }, Read("walk-apng-rgba.png").DelaysMs);
        // 0 and 10 ms: browsers show these frames for 100 ms.
        Assert.Equal(new int?[] { 100, 100, 50, 70, 70 }, Read("walk-opaque-interlaced.gif").DelaysMs);

        var (fps, durations) = AnimationImport.Timing([80, 80, 120, 80, 200, 80], 12);
        Assert.Equal(12.5, fps);
        Assert.Equal(new int?[] { null, null, 120, null, 200, null }, durations);
        var (seqFps, seqDurations) = AnimationImport.Timing([null, null, null], 8);
        Assert.Equal(8, seqFps);
        Assert.All(seqDurations, d => Assert.Null(d));
    }

    [Fact]
    public void A_damaged_file_is_refused_with_a_message_and_a_still_png_is_not_an_animation()
    {
        Assert.Throws<InvalidDataException>(() => GifDecoder.Decode([1, 2, 3]));
        var still = PngCodec.Encode(new RasterImage(4, 4));
        Assert.False(ApngDecoder.IsAnimated(still));
        Assert.Throws<InvalidDataException>(() => ApngDecoder.Decode(still));
        Assert.True(ApngDecoder.IsAnimated(File.ReadAllBytes(Fixture("walk-apng-rgba.png"))));
        // A truncated GIF keeps the frames it has.
        var gif = File.ReadAllBytes(Fixture("walk-dispose1.gif"));
        var cut = GifDecoder.Decode(gif[..(gif.Length * 2 / 3)]);
        Assert.InRange(cut.Frames.Count, 1, 6);
    }

    [Fact]
    public void Our_own_gif_export_reads_back_exactly()
    {
        var anim = Document.NewAnimation(40, 30, null, 3, 10, "Ink");
        for (int f = 0; f < 3; f++)
        {
            anim.SetCurrentFrame(f);
            var e = new StrokeEngine(anim);
            e.Begin(new StrokeStyle(new BrushSettings { Size = 6, Hardness = 1 }, PressureCurve.Normal, ColorRgb.Parse(f == 1 ? "#C83030" : "#3050C8"), PaintMode.Paint),
                new StrokeSample(5 + f * 8, 5));
            e.Add(new StrokeSample(5 + f * 8, 25));
            e.End();
        }
        var frames = AnimationExport.RenderFrames(anim, includePaper: false);
        using var ms = new MemoryStream();
        GifEncoder.Write(frames, ms, new GifOptions(AlphaThreshold: 1));
        var back = GifDecoder.Decode(ms.ToArray());
        Assert.Equal(3, back.Frames.Count);
        Assert.All(back.DelaysMs, d => Assert.Equal(100, d));
        for (int f = 0; f < 3; f++)
            for (int p = 0; p < back.Frames[f].Pixels.Length; p++)
                Assert.Equal(Pixel.A(frames[f].Image.Pixels[p]) > 0, Pixel.A(back.Frames[f].Pixels[p]) > 0);
    }

    [Fact]
    public void Numbered_files_sort_naturally_and_only_the_same_sequence_is_picked()
    {
        Assert.True(AnimationImport.NaturalCompare("frame_2.png", "frame_10.png") < 0);
        Assert.True(AnimationImport.NaturalCompare("frame_10.png", "frame_9.png") > 0);
        Assert.Equal(0, AnimationImport.NaturalCompare("Walk_3.PNG", "walk_3.png"));
        var folder = new[]
        {
            "/g/walk_10.png", "/g/walk_2.png", "/g/walk_1.png", "/g/walk_3.png", "/g/run_1.png", "/g/walk_1_shadow.png",
            "/g/walk_2.gif", "/g/walk.png", "/g/WALK_4.png", "/g/background.png",
        };
        var seq = AnimationImport.SequenceOf("/g/walk_2.png", folder);
        Assert.Equal(new[] { "walk_1.png", "walk_2.png", "walk_3.png", "WALK_4.png", "walk_10.png" }, seq.Select(Path.GetFileName));
        Assert.Equal(new[] { "/g/background.png" }, AnimationImport.SequenceOf("/g/background.png", folder));
        Assert.Equal(new[] { "frame_2.png", "frame_10.png" },
            AnimationImport.NaturalSort(["/x/frame_10.png", "/x/frame_2.png"]).Select(Path.GetFileName));
    }

    [Fact]
    public void Frames_of_different_sizes_share_one_canvas_at_the_top_left()
    {
        var a = new RasterImage(10, 8);
        a.Fill(Pixel.Opaque(ColorRgb.Parse("#FF0000")));
        var b = new RasterImage(16, 6);
        b.Fill(Pixel.Opaque(ColorRgb.Parse("#00FF00")));
        var anim = AnimationImport.FromImages([a, b]);
        Assert.Equal((16, 8), (anim.Width, anim.Height));
        Assert.Equal(Pixel.Opaque(ColorRgb.Parse("#FF0000")), anim.Frames[0][9, 7]);
        Assert.Equal(0u, anim.Frames[0][12, 2]);
        Assert.Equal(0u, anim.Frames[1][3, 7]);
        Assert.Single(anim.Notes);
        Assert.All(anim.DelaysMs, d => Assert.Null(d));
    }

    [Fact]
    public void The_imported_animation_has_four_layers_with_one_reference_drawing_per_frame()
    {
        var source = Read("walk-dispose2.gif");
        var doc = AnimationImport.BuildDocument(source);
        Assert.Equal(6, doc.FrameCount);
        Assert.Equal(new[] { "Reference", "Sketch", "Outline", "Coloring" }, doc.Layers.Select(l => l.Name)); // bottom to top
        var reference = doc.Layers[0];
        Assert.True(reference.Locked);
        Assert.True(reference.Visible);
        Assert.Equal(0.35, reference.Opacity, 3);
        Assert.All(doc.Layers.Skip(1), l => Assert.True(!l.Locked && l.Visible && l.Opacity == 1));
        Assert.Same(doc.Layers[1], doc.ActiveLayer); // Sketch: ready to draw
        Assert.Equal(48, doc.Width);
        Assert.Equal(32, doc.Height);
        for (int f = 0; f < 6; f++)
        {
            Assert.Equal(source.Frames[f].Pixels, doc.CelAt(reference, f)!.Image.Pixels);
            foreach (var l in doc.Layers.Skip(1)) Assert.Null(doc.CelAt(l, f));
        }
        Assert.Equal(6, doc.Layers[0].Track.Distinct().Count()); // every frame its own drawing (none repeated)
        Assert.Equal(12.5, doc.Animation!.Fps);
        Assert.Equal(120, doc.Animation.DurationOverride(2));
        Assert.True(doc.IsDirty);
        // Transparency is kept.
        Assert.Equal(0, Pixel.A(doc.CelAt(reference, 0)!.Image[47, 31]));
    }

    [Fact]
    public void Identical_neighbouring_frames_become_a_hold_and_excluded_frames_are_left_out()
    {
        var red = new RasterImage(8, 8);
        red.Fill(Pixel.Opaque(ColorRgb.Parse("#C03030")));
        var blue = new RasterImage(8, 8);
        blue.Fill(Pixel.Opaque(ColorRgb.Parse("#3030C0")));
        var source = new ImportedAnimation(8, 8, [red, red.Clone(), blue, red.Clone()], [100, 100, 100, 100], []);
        var doc = AnimationImport.BuildDocument(source);
        var track = doc.Layers[0].Track;
        Assert.Same(track[0], track[1]);
        Assert.NotSame(track[1], track[2]);
        Assert.NotSame(track[0], track[3]);
        Assert.Equal(red.Pixels, doc.CelAt(doc.Layers[0], 3)!.Image.Pixels);

        var some = AnimationImport.BuildDocument(source, keep: [2, 3]);
        Assert.Equal(2, some.FrameCount);
        Assert.Equal(blue.Pixels, some.CelAt(some.Layers[0], 0)!.Image.Pixels);
        Assert.Equal(red.Pixels, some.CelAt(some.Layers[0], 1)!.Image.Pixels);
    }

    [Fact]
    public void Drawing_on_the_new_layers_works_frame_by_frame_and_survives_saving_with_opacity()
    {
        var doc = AnimationImport.BuildDocument(Read("walk-apng-rgba.png"));
        var sketch = doc.Layers[1];
        // The reference is locked: a stroke on it does nothing.
        var engine = new StrokeEngine(doc);
        Assert.True(engine.Begin(new StrokeStyle(new BrushSettings { Size = 4 }, PressureCurve.Normal, ColorRgb.Black, PaintMode.Paint), new StrokeSample(10, 10), doc.Layers[0]).IsEmpty);
        foreach (int f in new[] { 0, 3 })
        {
            doc.SetCurrentFrame(f);
            engine.Begin(new StrokeStyle(new BrushSettings { Size = 4, Hardness = 1 }, PressureCurve.Normal, ColorRgb.Black, PaintMode.Paint), new StrokeSample(5 + f, 5), sketch);
            engine.Add(new StrokeSample(30, 20));
            engine.End();
        }
        Assert.NotNull(doc.CelAt(sketch, 0));
        Assert.Null(doc.CelAt(sketch, 1));
        Assert.NotNull(doc.CelAt(sketch, 3));
        Assert.NotSame(doc.CelAt(sketch, 0), doc.CelAt(sketch, 3));
        doc.SetLayerProperties(sketch, sketch.Properties with { Opacity = 0.25 });

        using var ms = new MemoryStream();
        DnkFile.Write(doc, ms);
        ms.Position = 0;
        var back = DnkFile.Read(ms);
        Assert.Equal(6, back.FrameCount);
        Assert.Equal(new[] { "Reference", "Sketch", "Outline", "Coloring" }, back.Layers.Select(l => l.Name));
        Assert.Equal(0.35, back.Layers[0].Opacity, 3);
        Assert.True(back.Layers[0].Locked);
        Assert.Equal(0.25, back.Layers[1].Opacity, 3);
        for (int f = 0; f < 6; f++) Assert.Equal(doc.RenderFrame(f).Pixels, back.RenderFrame(f).Pixels);
        Assert.Equal(doc.Animation!.DurationOverride(4), back.Animation!.DurationOverride(4));
    }

    [Fact]
    public void Exports_follow_the_layers_visibility_and_opacity_on_every_frame()
    {
        var source = Read("walk-dispose1.gif");
        var doc = AnimationImport.BuildDocument(source, paper: null);
        var reference = doc.Layers[0];
        // Reference at full opacity and nothing else: the export is the source.
        doc.SetLayerProperties(reference, reference.Properties with { Opacity = 1 });
        var full = AnimationExport.RenderFrames(doc, includePaper: false);
        for (int f = 0; f < 6; f++) Assert.Equal(source.Frames[f].Pixels, full[f].Image.Pixels);
        // At 35 % every frame is fainter, not just the current one.
        doc.SetLayerProperties(reference, reference.Properties with { Opacity = 0.35 });
        var faint = AnimationExport.RenderFrames(doc, includePaper: false);
        for (int f = 0; f < 6; f++)
        {
            int x = 10 + f * 6, y = 16; // inside the body on that frame
            Assert.Equal(255, Pixel.A(full[f].Image[x, y]));
            Assert.InRange(Pixel.A(faint[f].Image[x, y]), 85, 95);
        }
        var sheet = AnimationExport.BuildSheet(faint, new SpriteSheetOptions());
        Assert.InRange(Pixel.A(sheet.Image[10, 16]), 85, 95);
        // Hidden: the export has none of it.
        doc.SetLayerProperties(reference, reference.Properties with { Visible = false });
        Assert.All(AnimationExport.RenderFrames(doc, includePaper: false), f => Assert.All(f.Image.Pixels, p => Assert.Equal(0u, p)));
    }

    [Fact]
    public void Onion_skins_of_the_active_layer_follow_its_opacity()
    {
        var doc = AnimationImport.BuildDocument(Read("walk-dispose1.gif"), paper: null);
        var reference = doc.Layers[0];
        doc.SetActiveLayer(reference);
        doc.SetLayerProperties(reference, reference.Properties with { Opacity = 1 });
        doc.SetCurrentFrame(2);
        var settings = new OnionSkinSettings(Enabled: true, Before: 1, After: 0, Opacity: 0.5, Falloff: false);
        var (strong, _) = OnionSkin.Build(doc, settings);
        doc.SetLayerProperties(reference, reference.Properties with { Opacity = 0.5 });
        var (weak, _) = OnionSkin.Build(doc, settings);
        int x = 10 + 6, y = 16; // frame 1's body
        Assert.InRange(Pixel.A(weak![x, y]), Pixel.A(strong![x, y]) / 2 - 2, Pixel.A(strong[x, y]) / 2 + 2);
    }
}
