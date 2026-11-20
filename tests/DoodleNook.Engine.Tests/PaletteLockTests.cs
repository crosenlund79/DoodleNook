using System.Diagnostics;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

/// <summary>Palette Lock and Remap to Palette: only RGB is constrained or converted; alpha never changes.</summary>
public class PaletteLockTests
{
    private static readonly ColorRgb Red = ColorRgb.Parse("#C8473E"), Blue = ColorRgb.Parse("#3F6A82"), Cream = ColorRgb.Parse("#F2E3C6"),
        Dark = ColorRgb.Parse("#1A1C2C"), Green = ColorRgb.Parse("#5B8C51");

    private static Palette Small() => new("Small", [Red, Blue, Cream, Dark]);

    private static IEnumerable<uint> Painted(RasterImage img) => img.Pixels.Where(p => p != 0);

    private static void AssertOnlyPalette(Document doc, IReadOnlyList<ColorRgb> colours)
    {
        var c = new PaletteConformer(colours);
        foreach (var (layer, img) in doc.AllImages())
            foreach (var p in Painted(img))
                Assert.True(c.ExactIndex(p) >= 0, $"{layer.Name}: pixel {p:X8} is not a palette colour");
    }

    private static byte[] Alphas(RasterImage img) => img.Pixels.Select(Pixel.A).ToArray();

    // ================================================================== the critical alpha test

    [Fact]
    public void Remapping_keeps_alpha_exactly_rgb_200_100_50_at_73_becomes_40_80_120_at_73()
    {
        var doc = new Document(4, 4, null);
        doc.ActiveLayer.Image[1, 1] = Pixel.FromStraight(200, 100, 50, 73);
        doc.ActiveLayer.Image[2, 2] = Pixel.FromStraight(200, 100, 50, 255);
        var target = new ColorRgb(40, 80, 120);
        var remap = PaletteRemap.Analyse(doc, [target]);
        remap.Apply(doc, "Target", lockAfter: true);

        var img = doc.ActiveLayer.Image;
        Assert.Equal(73, Pixel.A(img[1, 1]));                                  // alpha untouched
        Assert.Equal((40, 80, 120, 73), Unpack(doc.PaletteConformer!.Decode(img[1, 1])));
        Assert.Equal((40, 80, 120, 255), Unpack(doc.PaletteConformer!.Decode(img[2, 2])));
        Assert.Equal(0u, img[0, 0]);                                           // transparent stays transparent

        // …and that is exactly what an export writes.
        var png = PngCodec.Decode(PngCodec.Encode(doc.Flatten()));
        var rgba = PngRgba(doc.Flatten());
        int at = (1 * 4 + 1) * 4; // pixel (1, 1) of a 4-wide image
        Assert.Equal(new byte[] { 40, 80, 120, 73 }, rgba[at..(at + 4)]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, rgba[0..4]);
        Assert.Equal(73, Pixel.A(png[1, 1]));
    }

    private static (byte, byte, byte, byte) Unpack((byte R, byte G, byte B, byte A) c) => (c.R, c.G, c.B, c.A);

    private static byte[] PngRgba(RasterImage image) => image.ToStraightRgba();

    [Fact]
    public void Every_alpha_level_keeps_its_alpha_and_decodes_to_the_palette_colour()
    {
        var doc = new Document(256, 1, null);
        for (int a = 0; a < 256; a++) doc.ActiveLayer.Image[a, 0] = Pixel.FromStraight(200, 100, 50, (byte)a);
        var before = Alphas(doc.ActiveLayer.Image);
        PaletteRemap.Analyse(doc, [new ColorRgb(40, 80, 120), Cream]).Apply(doc, "P", lockAfter: true);
        Assert.Equal(before, Alphas(doc.ActiveLayer.Image));
        var straight = doc.Flatten().ToStraightRgba();
        for (int a = 1; a < 256; a++)
            Assert.Equal(new byte[] { 40, 80, 120, (byte)a }, straight[(a * 4)..(a * 4 + 4)]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, straight[0..4]);
    }

    // ================================================================== painting under the lock

    private static Document Locked(int w = 200, int h = 120, ColorRgb? paper = null)
    {
        var doc = new Document(w, h, paper);
        doc.LockToPalette(Small());
        return doc;
    }

    [Fact]
    public void A_locked_picture_only_gets_palette_colours_and_keeps_the_brush_transparency()
    {
        var soft = new BrushSettings { Size = 30, Hardness = 0.2, Opacity = 0.8, PressureOpacity = true, PressureSize = true };
        var locked = Locked();
        var free = new Document(200, 120, null);
        foreach (var d in new[] { locked, free })
            Stroke(d, Paint(soft, Blue), Line(20, 60, 180, 60, 2, 0.2f, 1f));
        AssertOnlyPalette(locked, Small().Colors);
        // Pressure-dependent transparency and the soft, antialiased edge are exactly as without the lock.
        Assert.Equal(Alphas(free.ActiveLayer.Image), Alphas(locked.ActiveLayer.Image));
        Assert.True(Alphas(locked.ActiveLayer.Image).Distinct().Count() > 40, "soft edges and pressure give many alpha levels");
        // Every painted pixel is exactly the brush colour.
        Assert.All(Painted(locked.ActiveLayer.Image), p => Assert.Equal(Blue, locked.Palette.Colors[locked.PaletteConformer!.ExactIndex(p)]));
    }

    [Fact]
    public void Blending_over_other_colours_snaps_to_palette_colours_and_keeps_alpha()
    {
        var locked = Locked();
        var free = new Document(200, 120, null);
        foreach (var d in new[] { locked, free })
        {
            FloodFill.Apply(d, d.ActiveLayer, 5, 5, Cream, new FillOptions());
            Stroke(d, Paint(new BrushSettings { Size = 40, Hardness = 0.1, Opacity = 0.6 }, Red), Line(20, 60, 180, 60, 2));
        }
        AssertOnlyPalette(locked, Small().Colors);
        Assert.Equal(Alphas(free.ActiveLayer.Image), Alphas(locked.ActiveLayer.Image));
        var colours = Painted(locked.ActiveLayer.Image).Select(p => locked.PaletteConformer!.ExactIndex(p)).Distinct().ToList();
        Assert.Contains(0, colours); // red in the middle
        Assert.Contains(2, colours); // cream around it
    }

    [Fact]
    public void Erasing_keeps_palette_colours_and_lowers_alpha_as_usual()
    {
        var locked = Locked();
        var free = new Document(200, 120, null);
        foreach (var d in new[] { locked, free })
        {
            Stroke(d, Paint(Hard(40), Dark), Line(20, 60, 180, 60, 2));
            Stroke(d, Erase(new BrushSettings { Size = 30, Hardness = 0.3, Opacity = 0.5 }), Line(100, 20, 100, 100, 2));
        }
        AssertOnlyPalette(locked, Small().Colors);
        Assert.Equal(Alphas(free.ActiveLayer.Image), Alphas(locked.ActiveLayer.Image));
    }

    [Fact]
    public void Fills_stamps_patterns_gradients_adjustments_and_imports_keep_to_the_palette()
    {
        var doc = Locked(160, 100);
        // A colour outside the palette: the fill uses the nearest palette colour.
        FloodFill.Apply(doc, doc.ActiveLayer, 5, 5, ColorRgb.Parse("#FF0000"), new FillOptions());
        Assert.Equal(Pixel.Opaque(Red), doc.ActiveLayer.Image[5, 5]);
        var lib = new CreativeLibrary();
        Assert.True(StampsAndPatterns.PlaceStamp(doc, doc.ActiveLayer, lib.StampImage(BuiltInAssets.Prefix + "flower")!, new StampPlacement(40, 40, 0.5, 20, 0.7)));
        Assert.True(StampsAndPatterns.FillSelection(doc, doc.AddLayer(), lib.PatternImage(BuiltInAssets.Prefix + "brick")!, new PatternFillOptions(1.3, Rotation: 15, Opacity: 0.6)));
        var g = new GradientOperation(doc, doc.AddLayer(), Dark, Cream, new GradientOptions(ToTransparent: true));
        g.Update(0, 0, 160, 100);
        Assert.True(g.Commit());
        var imported = new RasterImage(160, 100);
        var rng = new Random(3);
        for (int i = 0; i < imported.Pixels.Length; i++) imported.Pixels[i] = Pixel.FromStraight((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
        var importedAlpha = Alphas(imported);
        var layer = doc.AddLayer(imported, "Imported");
        Assert.Equal(importedAlpha, Alphas(layer.Image)); // converted colours, same transparency
        AssertOnlyPalette(doc, Small().Colors);
        ImageOperations.ResizeImage(doc, 120, 75, Interpolation.Bicubic);
        AssertOnlyPalette(doc, Small().Colors);
    }

    [Fact]
    public void Locking_a_picture_that_already_uses_the_palette_changes_nothing_and_is_one_undo_step()
    {
        var doc = new Document(120, 80, null);
        Stroke(doc, Paint(new BrushSettings { Size = 20, Hardness = 0.3 }, Red), Line(10, 40, 110, 40, 2));
        Assert.True(doc.CheckPalette(Small().Colors).OnlyPaletteColours);
        var other = new Palette("Greens", [Green, Cream]);
        var use = doc.CheckPalette(other.Colors);
        Assert.False(use.OnlyPaletteColours);
        Assert.True(use.ColoursOutside >= 1);

        var pixels = Snapshot(doc);
        int steps = doc.History.UndoCount;
        doc.LockToPalette(Small());
        Assert.True(doc.PaletteLocked);
        Assert.Equal(steps + 1, doc.History.UndoCount);
        Assert.Equal(pixels.Select(Pixel.A), doc.ActiveLayer.Image.Pixels.Select(Pixel.A));
        // Up to premultiplied rounding, nothing changed; every pixel is now exactly red at its own alpha.
        Assert.All(Painted(doc.ActiveLayer.Image), p => Assert.Equal(0, doc.PaletteConformer!.ExactIndex(p)));
        doc.Undo();
        Assert.False(doc.PaletteLocked);
        Assert.Equal(pixels, doc.ActiveLayer.Image.Pixels);
    }

    [Fact]
    public void Adding_an_allowed_colour_is_explicit_undoable_and_recolours_nothing()
    {
        var doc = Locked();
        Stroke(doc, Paint(Hard(20), Blue), Line(10, 60, 190, 60, 2));
        var before = Snapshot(doc);
        // Green isn't allowed yet: it paints as the nearest palette colour.
        Stroke(doc, Paint(Hard(10), Green), S(50, 20), S(60, 20));
        Assert.DoesNotContain(doc.ActiveLayer.Image.Pixels, p => p == Pixel.Opaque(Green));
        doc.Undo();
        Assert.True(doc.AddPaletteColour(Green));
        Assert.Equal(before, Snapshot(doc));
        Stroke(doc, Paint(Hard(10), Green), S(50, 20), S(60, 20));
        Assert.Contains(doc.ActiveLayer.Image.Pixels, p => p == Pixel.Opaque(Green));
        doc.Undo();
        doc.Undo();
        Assert.Equal(4, doc.Palette.Colors.Count);
        Assert.False(doc.AddPaletteColour(Blue)); // already there
    }

    // ================================================================== remapping

    private static Document Sprite()
    {
        var doc = new Document(16, 16, null, "Body");
        string[] rows =
        [
            "....RRRR........",
            "...RRCCRR.......",
            "...RCCCCR.......",
            "...RRRRRR...BBB.",
            "....DDDD...BBBB.",
        ];
        var map = new Dictionary<char, ColorRgb> { ['R'] = Red, ['C'] = Cream, ['B'] = Blue, ['D'] = Dark };
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < 16; x++)
                if (map.TryGetValue(rows[y][x], out var c)) doc.ActiveLayer.Image[x, y + 4] = Pixel.Opaque(c);
        return doc;
    }

    [Fact]
    public void Suggestions_keep_colours_already_in_the_palette_and_pick_perceptually_close_ones()
    {
        var doc = Sprite();
        var target = new[] { ColorRgb.Parse("#2B2B2B"), Red, ColorRgb.Parse("#4A78C8"), ColorRgb.Parse("#FFF4D8"), ColorRgb.Parse("#888888") };
        var remap = PaletteRemap.Analyse(doc, target);
        Assert.False(remap.Grouped);
        Assert.Equal(4, remap.Sources.Count);
        int Row(ColorRgb c) => remap.RowOf(c);
        Assert.Equal(1, remap.TargetOf(Row(Red)));    // already in the palette
        Assert.Equal(2, remap.TargetOf(Row(Blue)));   // blue → the blue, not the grey
        Assert.Equal(3, remap.TargetOf(Row(Cream)));  // cream → the light cream
        Assert.Equal(0, remap.TargetOf(Row(Dark)));   // near-black → the dark grey

        // "Light and dark" stretches the picture's lightness over the palette's: the darkest colour gets the darkest.
        remap.Style = RemapStyle.LightAndDark;
        Assert.Equal(0, remap.TargetOf(Row(Dark)));
        Assert.Equal(3, remap.TargetOf(Row(Cream)));
    }

    [Fact]
    public void Manual_assignments_override_the_suggestion_and_reset_brings_it_back()
    {
        var doc = Sprite();
        var target = new[] { Dark, Green, Cream, ColorRgb.Parse("#E88A9A") };
        var remap = PaletteRemap.Analyse(doc, target);
        int red = remap.RowOf(Red), blue = remap.RowOf(Blue);
        int auto = remap.TargetOf(red);
        remap.Assign(red, 1);   // red → green
        remap.Assign(blue, 1);  // and blue → green too: several colours may share one target
        Assert.True(remap.IsChosen(red));
        var preview = remap.Preview(doc, 0);
        Assert.Equal(Pixel.Opaque(Green), preview[4, 4]);  // red pixel
        Assert.Equal(Pixel.Opaque(Green), preview[12, 7]); // blue pixel
        remap.Reset();
        Assert.Equal(auto, remap.TargetOf(red));
        Assert.False(remap.IsChosen(red));
    }

    [Fact]
    public void Cancelling_leaves_everything_unchanged_and_applying_is_one_undo_step()
    {
        var doc = Sprite();
        doc.AddLayer("Empty");
        var before = doc.Layers.Select(l => (uint[])l.Image.Pixels.Clone()).ToList();
        int steps = doc.History.UndoCount;
        var remap = PaletteRemap.Analyse(doc, [Green, Cream, Dark]);
        _ = remap.Preview(doc, 0);
        // Not applied: nothing changed, no history.
        Assert.Equal(steps, doc.History.UndoCount);
        Assert.Equal(before[0], doc.Layers[0].Image.Pixels);
        Assert.False(doc.PaletteLocked);

        remap.Apply(doc, "Greens", lockAfter: true, libraryId: "u123");
        Assert.Equal(steps + 1, doc.History.UndoCount);
        Assert.True(doc.PaletteLocked);
        Assert.Equal("u123", doc.Palette.LibraryId);
        AssertOnlyPalette(doc, [Green, Cream, Dark]);
        var after = (uint[])doc.Layers[0].Image.Pixels.Clone();
        Assert.True(doc.Undo());
        Assert.Equal(before[0], doc.Layers[0].Image.Pixels);
        Assert.False(doc.PaletteLocked);
        Assert.Equal("Nook 32", doc.Palette.Name);
        Assert.True(doc.Redo());
        Assert.Equal(after, doc.Layers[0].Image.Pixels);
        Assert.True(doc.PaletteLocked);
    }

    [Fact]
    public void Layers_keep_their_order_opacity_and_blend_and_hidden_layers_are_included_only_when_asked()
    {
        var doc = Sprite();
        var top = doc.AddLayer("Top");
        top.Image[0, 0] = Pixel.FromStraight(200, 30, 30, 140);
        doc.SetLayerProperties(top, top.Properties with { Opacity = 0.4, Blend = BlendMode.Multiply });
        var hidden = doc.AddLayer("Hidden");
        hidden.Image[1, 1] = Pixel.Opaque(ColorRgb.Parse("#00FF00"));
        doc.SetLayerProperties(hidden, hidden.Properties with { Visible = false });
        var props = doc.Layers.Select(l => l.Properties).ToList();

        var remap = PaletteRemap.Analyse(doc, [Dark, Cream], includeHidden: false);
        Assert.Equal(-1, remap.RowOf(ColorRgb.Parse("#00FF00")));
        remap.Apply(doc, "Two", includeHidden: false);
        Assert.Equal(Pixel.Opaque(ColorRgb.Parse("#00FF00")), hidden.Image[1, 1]); // untouched
        Assert.Equal(140, Pixel.A(top.Image[0, 0]));
        Assert.Equal(props, doc.Layers.Select(l => l.Properties).ToList());

        doc.Undo();
        PaletteRemap.Analyse(doc, [Dark, Cream], includeHidden: true).Apply(doc, "Two", includeHidden: true);
        Assert.NotEqual(Pixel.Opaque(ColorRgb.Parse("#00FF00")), hidden.Image[1, 1]);
        Assert.Equal(props, doc.Layers.Select(l => l.Properties).ToList());
    }

    [Fact]
    public void Animations_keep_frames_timing_holds_layers_and_alpha()
    {
        var doc = Document.NewAnimation(64, 48, null, 6, 12, "Ground");
        var ground = doc.ActiveLayer;
        Stroke(doc, Paint(new BrushSettings { Size = 8, Hardness = 0.4 }, ColorRgb.Parse("#7A6A5E")), Line(2, 40, 62, 40, 1));
        while (doc.HoldRange(ground, 0).End < 5) doc.ExtendHold(ground, 0);
        var ball = doc.AddLayer("Ball");
        doc.SetLayerProperties(ball, ball.Properties with { Opacity = 0.75 });
        for (int f = 0; f < 6; f++)
        {
            doc.SetCurrentFrame(f);
            Stroke(doc, Paint(new BrushSettings { Size = 10, Hardness = 0.3, PressureOpacity = true }, ColorRgb.Parse("#E0604A")),
                Line(10 + f * 8, 15, 14 + f * 8, 18, 1, 0.3f, 0.9f));
        }
        doc.SetFrameDuration(2, 250);
        var framesBefore = doc.FrameCount;
        var durations = Enumerable.Range(0, doc.FrameCount).Select(f => doc.Animation!.DurationOf(f)).ToList();
        var groundCel = doc.CelAt(ground, 0);
        var alphas = doc.AllImages().Select(t => Alphas(t.Image)).ToList();
        var layerProps = doc.Layers.Select(l => l.Properties).ToList();

        var remap = PaletteRemap.Analyse(doc, Small().Colors);
        var original = doc.RenderFrame(3);
        var preview = remap.Preview(doc, 3);
        Assert.NotEqual(original.Pixels, preview.Pixels);
        Assert.Equal(original.Pixels, doc.RenderFrame(3).Pixels); // the preview changed nothing
        remap.Apply(doc, "Small", lockAfter: true);

        Assert.Equal(framesBefore, doc.FrameCount);
        Assert.Equal(durations, Enumerable.Range(0, doc.FrameCount).Select(f => doc.Animation!.DurationOf(f)).ToList());
        Assert.All(Enumerable.Range(0, 6), f => Assert.Same(groundCel, doc.CelAt(ground, f))); // the held drawing is still one drawing
        Assert.Equal(new[] { "Ground", "Ball" }, doc.Layers.Select(l => l.Name));
        Assert.Equal(layerProps, doc.Layers.Select(l => l.Properties).ToList());
        Assert.Equal(alphas, doc.AllImages().Select(t => Alphas(t.Image)).ToList());
        AssertOnlyPalette(doc, Small().Colors);
        Assert.Equal(preview.Pixels, doc.RenderFrame(3).Pixels); // the preview showed exactly the result

        doc.Undo();
        Assert.Equal(original.Pixels, doc.RenderFrame(3).Pixels);
        Assert.All(Enumerable.Range(0, 6), f => Assert.Same(groundCel, doc.CelAt(ground, f)));
    }

    [Fact]
    public void Many_colours_are_summarised_in_groups_and_still_map_every_colour()
    {
        // A smooth painting-like picture: thousands of colours with soft alpha.
        var doc = new Document(256, 128, null);
        var img = doc.ActiveLayer.Image;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 256; x++)
                img[x, y] = Pixel.FromStraight((byte)x, (byte)(y * 2), (byte)(255 - x), (byte)(55 + (x + y) % 200));
        var alphas = Alphas(img);
        var remap = PaletteRemap.Analyse(doc, Small().Colors);
        Assert.True(remap.Grouped);
        Assert.True(remap.DistinctColours > 5000, remap.DistinctColours.ToString());
        Assert.InRange(remap.Sources.Count, 2, PaletteRemap.GroupRows);
        Assert.Equal(remap.DistinctColours, remap.Sources.Sum(s => s.Members.Count));
        // Choosing for a group sends all its colours there.
        remap.Assign(0, 3);
        Assert.All(remap.Sources[0].Members, m => Assert.Equal(3, remap.TargetIndexOf(m)));
        remap.Apply(doc, "Small", lockAfter: true);
        Assert.Equal(alphas, Alphas(doc.ActiveLayer.Image));
        AssertOnlyPalette(doc, Small().Colors);
    }

    [Fact]
    public void Replacing_a_palette_colour_moves_its_pixels_to_the_new_colour_only_when_applied()
    {
        var doc = Sprite();
        doc.LockToPalette(Small());
        var newBlue = ColorRgb.Parse("#5FA8E8");
        var edited = Small().Colors.ToList();
        edited[1] = newBlue; // old blue → new blue
        var remap = PaletteRemap.Analyse(doc, edited);
        remap.MatchByPosition(doc.Palette.Colors);
        Assert.Equal(1, remap.TargetOf(remap.RowOf(Blue)));
        Assert.Equal(Pixel.Opaque(Blue), doc.ActiveLayer.Image[12, 7]); // nothing yet
        remap.Apply(doc, "Small", lockAfter: true);
        Assert.Equal(Pixel.Opaque(newBlue), doc.ActiveLayer.Image[12, 7]);
        Assert.Equal(Pixel.Opaque(Red), doc.ActiveLayer.Image[4, 4]);
        Assert.Equal(newBlue, doc.Palette.Colors[1]);
    }

    // ================================================================== files

    [Fact]
    public void A_locked_picture_reopens_locked_and_unlocked_files_keep_their_old_versions()
    {
        var doc = Sprite();
        doc.ActiveLayer.Image[0, 0] = Pixel.FromStraight(200, 100, 50, 73);
        PaletteRemap.Analyse(doc, Small().Colors).Apply(doc, "Small", lockAfter: true, libraryId: "u42");
        using var ms = new MemoryStream();
        DnkFile.Write(doc, ms);
        ms.Position = 0;
        var back = DnkFile.Read(ms);
        Assert.True(back.PaletteLocked);
        Assert.Equal(doc.Palette.Colors, back.Palette.Colors);
        Assert.Equal("u42", back.Palette.LibraryId);
        Assert.Equal(doc.ActiveLayer.Image.Pixels, back.ActiveLayer.Image.Pixels);
        Assert.Equal(73, Pixel.A(back.ActiveLayer.Image[0, 0]));
        Assert.True(back.PaletteConformer!.ExactIndex(back.ActiveLayer.Image[0, 0]) >= 0);
        Assert.Equal(DnkFile.PaletteLockVersion, ManifestVersion(doc));

        // Unlocking writes the old versions again (Mission 005/006 builds keep opening those files).
        doc.UnlockPalette();
        Assert.Equal(DnkFile.StillVersion, ManifestVersion(doc));
        Assert.Equal(DnkFile.AnimationVersion, ManifestVersion(Document.NewAnimation(8, 8, null, 2)));
    }

    private static int ManifestVersion(Document doc)
    {
        using var ms = new MemoryStream();
        DnkFile.Write(doc, ms);
        ms.Position = 0;
        using var zip = new System.IO.Compression.ZipArchive(ms);
        using var s = zip.GetEntry("document.json")!.Open();
        return System.Text.Json.JsonDocument.Parse(s).RootElement.GetProperty("version").GetInt32();
    }

    // ================================================================== responsiveness

    [Fact]
    public void Analysis_and_previews_of_large_documents_stay_quick()
    {
        var doc = Document.NewAnimation(1920, 1080, ColorRgb.White, 8, 12, "Back");
        Stroke(doc, Paint(new BrushSettings { Size = 300, Hardness = 0.2, Opacity = 0.8 }, ColorRgb.Parse("#9CC07A")), Line(0, 700, 1920, 800, 20));
        var body = doc.AddLayer("Body");
        for (int f = 0; f < 8; f++)
        {
            doc.SetCurrentFrame(f);
            Stroke(doc, Paint(new BrushSettings { Size = 120, Hardness = 0.3 }, ColorRgb.Parse("#E0604A")), Line(200 + f * 150, 300, 400 + f * 150, 500, 10));
        }
        var sw = Stopwatch.StartNew();
        var remap = PaletteRemap.Analyse(doc, Palette.Default().Colors);
        long analyse = sw.ElapsedMilliseconds;
        sw.Restart();
        _ = remap.Preview(doc, 4);
        long first = sw.ElapsedMilliseconds;
        remap.Assign(0, 5);
        sw.Restart();
        _ = remap.Preview(doc, 4);
        long changed = sw.ElapsedMilliseconds;
        TestContext.Current.TestOutputHelper?.WriteLine($"analyse {analyse} ms, first preview {first} ms, preview after a change {changed} ms");
        Assert.True(analyse < 5000, $"analysis took {analyse} ms");
        Assert.True(changed < 2000, $"a preview took {changed} ms");
    }
}
