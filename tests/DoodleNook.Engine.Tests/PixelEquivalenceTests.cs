using System.Security.Cryptography;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Mission 011: the optimisations must not change a single pixel. These scenarios (brushes of every kind, erasing,
/// transparency lock, opacity, blend modes, clipping, groups, masks, adjustments, a palette lock, undo and redo, animation
/// frames and onion skins, smudge, clone, repair, liquify, transforms, PNG, GIF) are hashed, and the hashes must equal
/// the ones recorded from the Mission 010 engine (commit 702aa2f) before any optimisation was made.
/// </summary>
public class PixelEquivalenceTests
{
    /// <summary>Recorded with the Mission 010 engine. Regenerate only for a deliberate change of how pictures look.</summary>
    private static readonly Dictionary<string, string> Expected = new()
    {
        ["six layers · flatten"] = "191A90264E0548FD3D4E9043",
        ["six layers · projection"] = "191A90264E0548FD3D4E9043",
        ["brushes · layer"] = "9975654001E8388747C50D09",
        ["brushes · flatten"] = "00285F04591BBF8BA76E7A19",
        ["brushes · projection"] = "00285F04591BBF8BA76E7A19",
        ["transparency lock · layer"] = "B99E5ACE02F65E1FD64E12CB",
        ["selection · flatten"] = "BD2E5B1516714F6075D335E4",
        ["undo ×5 · projection"] = "00285F04591BBF8BA76E7A19",
        ["undo ×5 · flatten"] = "00285F04591BBF8BA76E7A19",
        ["redo ×3 · projection"] = "B08623AF7C3DD1AB629B5AEA",
        ["redo ×3 · flatten"] = "B08623AF7C3DD1AB629B5AEA",
        ["design · flatten"] = "FC6860441630BB5368659B9B",
        ["design · undo ×2 · projection"] = "DBDFB8BAD307AA5D6B576D04",
        ["palette lock · flatten"] = "6D5DE89A44D2397423D99FF6",
        ["palette remap · flatten"] = "B4F618CAA7C6FDD364FA407F",
        ["animation · frame 0"] = "FF30877DC2C9E9F89797C913",
        ["animation · frame 1"] = "79108A0BAEAAC0D25AA78F60",
        ["animation · frame 2"] = "6D84E1F3FDBA2E60AB68D112",
        ["animation · frame 3"] = "E57AACD434E3E3FB4A870718",
        ["animation · frame 4"] = "8E5736C9150EB1218B5B200D",
        ["animation · onion projection"] = "055A488AF7BA536DA6428471",
        ["animation · gif bytes"] = "BC9A720F879933D7A9E4E6B4",
        ["animation · sprite sheet"] = "221BF4286586F1E72950094C",
        ["retouch · Smudge"] = "803C578A2AE0285F45BA9989",
        ["retouch · Clone"] = "ECED76CD5827DA7AF0DE4781",
        ["retouch · Repair"] = "44C6F5522C9968AC7EAAA652",
        ["liquify · layer"] = "C962D0F63E09B95FF5320F2F",
        ["perspective · projection"] = "17E21A08324931E5165FD7BD",
        ["perspective · layer"] = "5270973B42621F3ECD1456F4",
        ["png · picture bytes"] = "8D790F025EEF803B65C9D610",
        ["png · picture decoded"] = "B08623AF7C3DD1AB629B5AEA",
        ["png · transparent sprite bytes"] = "68FF3DF8BA144EA6109FC493",
        ["png · transparent sprite decoded"] = "63015DC0C86D7724D7CAA975",
    };

    private static string Hash(RasterImage image)
    {
        var bytes = new byte[image.Pixels.Length * 4];
        Buffer.BlockCopy(image.Pixels, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes))[..24];
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..24];

    private static readonly string[] Colours = ["#9CC07A", "#5F8FA8", "#E9A94D", "#C96F4A", "#8A6FB0", "#3B2F2A"];

    private static StrokeSample At(Document d, double t, double phase = 0) =>
        new(d.Width * 0.08 + d.Width * 0.84 * t, d.Height * 0.5 + d.Height * 0.32 * Math.Sin(t * Math.PI * 3 + phase), (float)(0.25 + 0.75 * Math.Sin(Math.PI * t)));

    private static void Stroke(Document doc, BrushSettings brush, ColorRgb colour, double phase, PaintMode mode = PaintMode.Paint,
        BrushResources? resources = null, Layer? layer = null, Symmetry symmetry = Symmetry.None)
    {
        var engine = new StrokeEngine(doc);
        engine.Begin(new StrokeStyle(brush, PressureCurve.Normal, colour, mode, symmetry, resources), At(doc, 0, phase), layer);
        var batch = new StrokeSample[2];
        for (int e = 0; e < 60; e++)
        {
            batch[0] = At(doc, (2 * e + 1) / 120.0, phase);
            batch[1] = At(doc, (2 * e + 2) / 120.0, phase);
            engine.Add(batch);
        }
        engine.End();
    }

    /// <summary>Every scenario's hash, in a fixed order.</summary>
    internal static List<(string Name, string Hash)> Scenarios()
    {
        var list = new List<(string, string)>();
        var lib = new CreativeLibrary();

        // 1. Six layers with opacity, Multiply, Screen, Overlay, clipping and a transparency lock; every kind of brush.
        var doc = new Document(640, 400, ColorRgb.White, "Base");
        var modes = new[] { BlendMode.Normal, BlendMode.Multiply, BlendMode.Screen, BlendMode.Overlay, BlendMode.Normal, BlendMode.SoftLight };
        for (int i = 0; i < 6; i++)
        {
            var layer = i == 0 ? doc.ActiveLayer : doc.AddLayer($"L{i}");
            doc.SetLayerProperties(layer, layer.Properties with { Opacity = i % 3 == 1 ? 0.7 : 1, Blend = modes[i], ClipToBelow = i == 4 });
            Stroke(doc, new BrushSettings { Size = 120, Hardness = 0.4, Opacity = 0.8 }, ColorRgb.Parse(Colours[i]), i, layer: layer);
        }
        list.Add(("six layers · flatten", Hash(doc.Flatten())));
        list.Add(("six layers · projection", Hash(doc.Projection)));
        var target = doc.Layers[3];
        doc.SetActiveLayer(target);
        Stroke(doc, new BrushSettings { Size = 6, Hardness = 0.85, PressureSize = true, Grain = 0.55 }, ColorRgb.Parse("#3B2F2A"), 0.3);
        Stroke(doc, new BrushSettings { Size = 24, Hardness = 1, PressureSize = true }, ColorRgb.Parse("#1D3557"), 0.9);
        Stroke(doc, new BrushSettings { Size = 90, Hardness = 0.05, Opacity = 0.6, PressureOpacity = true }, ColorRgb.Parse("#E63946"), 1.7);
        Stroke(doc, new BrushSettings { Size = 40, Hardness = 0.5, BuildUp = true, Flow = 0.2 }, ColorRgb.Parse("#2A9D8F"), 2.2, symmetry: Symmetry.Vertical);
        Stroke(doc, new BrushSettings { Size = 30, Hardness = 0.9, WetEdges = 0.6 }, ColorRgb.Parse("#F4A261"), 2.9);
        foreach (var id in new[] { "charcoal", "chalk", "leaves", "spatter" })
        {
            var preset = lib.FindBrush(id)!;
            Stroke(doc, preset.Settings, ColorRgb.Parse("#264653"), id.Length, resources: lib.Resolve(preset.Settings));
        }
        Stroke(doc, new BrushSettings { Size = 50, Hardness = 0.6 }, ColorRgb.White, 0.5, PaintMode.Erase);
        list.Add(("brushes · layer", Hash(target.Image)));
        list.Add(("brushes · flatten", Hash(doc.Flatten())));
        list.Add(("brushes · projection", Hash(doc.Projection)));
        doc.SetLayerProperties(target, target.Properties with { AlphaLocked = true });
        Stroke(doc, new BrushSettings { Size = 70, Hardness = 0.7 }, ColorRgb.Parse("#FFB703"), 1.1);
        list.Add(("transparency lock · layer", Hash(target.Image)));
        // Selection-limited painting.
        doc.SetSelection(SelectionMask.Rectangle(doc.Width, doc.Height, new PixelRect(100, 60, 300, 200)), "Select");
        Stroke(doc, new BrushSettings { Size = 60, Hardness = 0.3 }, ColorRgb.Parse("#8338EC"), 0.2, layer: doc.Layers[5]);
        doc.SetSelection(null, "Deselect");
        list.Add(("selection · flatten", Hash(doc.Flatten())));
        // Undo and redo (the projection must follow exactly).
        for (int i = 0; i < 5; i++) doc.Undo();
        list.Add(("undo ×5 · projection", Hash(doc.Projection)));
        list.Add(("undo ×5 · flatten", Hash(doc.Flatten())));
        for (int i = 0; i < 3; i++) doc.Redo();
        list.Add(("redo ×3 · projection", Hash(doc.Projection)));
        list.Add(("redo ×3 · flatten", Hash(doc.Flatten())));

        // 2. A group, a mask and adjustment layers.
        var design = new Document(480, 320, ColorRgb.Parse("#FDF6E3"), "Back");
        for (int i = 0; i < 4; i++)
        {
            var l = i == 0 ? design.ActiveLayer : design.AddLayer($"D{i}");
            Stroke(design, new BrushSettings { Size = 80, Hardness = 0.5, Opacity = 0.9 }, ColorRgb.Parse(Colours[i + 1]), i * 1.3, layer: l);
        }
        var group = design.GroupLayers([design.Layers[1], design.Layers[2]], "Group");
        design.SetLayerProperties(group, group.Properties with { Opacity = 0.8, Blend = BlendMode.Multiply });
        var top = design.Layers.Last(l => l.Kind == LayerKind.Raster);
        design.SetActiveLayer(top);
        design.AddMask(top);
        design.SetEditingMask(true);
        Stroke(design, new BrushSettings { Size = 70, Hardness = 0.3 }, ColorRgb.Black, 0.4, layer: top);
        design.SetEditingMask(false);
        design.AddAdjustmentLayer(new HueSaturation(25, 20, -5));
        design.AddAdjustmentLayer(new Levels(10, 240, 1.2));
        design.AddAdjustmentLayer(new BrightnessContrast(8, 12));
        list.Add(("design · flatten", Hash(design.Flatten())));
        design.Undo();
        design.Undo();
        list.Add(("design · undo ×2 · projection", Hash(design.Projection)));

        // 3. A palette-locked picture.
        var locked = new Document(320, 240, ColorRgb.White, "Locked");
        locked.ChangePalette(Palette.Default(), true, null, "Lock");
        for (int i = 0; i < 3; i++)
            Stroke(locked, new BrushSettings { Size = 50 + i * 20, Hardness = 0.3 + 0.2 * i, Opacity = 0.85 }, ColorRgb.Parse(Colours[i]), i, layer: i == 0 ? locked.ActiveLayer : locked.AddLayer($"P{i}"));
        list.Add(("palette lock · flatten", Hash(locked.Flatten())));
        var free = new Document(320, 240, ColorRgb.White, "Free");
        Stroke(free, new BrushSettings { Size = 90, Hardness = 0.2 }, ColorRgb.Parse("#C96F4A"), 0.7);
        var remap = PaletteRemap.Analyse(free, Palette.Default().Colors);
        remap.Apply(free, "Nook 32", lockAfter: true);
        list.Add(("palette remap · flatten", Hash(free.Flatten())));

        // 4. Animation: frames, a hold, onion skins.
        var anim = Document.NewAnimation(320, 200, ColorRgb.White, 5, 12, "Bg");
        Stroke(anim, new BrushSettings { Size = 60, Hardness = 0.4 }, ColorRgb.Parse("#9CC07A"), 0);
        for (int f = 0; f < 4; f++) anim.ExtendHold(anim.ActiveLayer, 0);
        var ink = anim.AddLayer("Ink");
        for (int f = 0; f < 5; f++)
        {
            anim.SetCurrentFrame(f);
            Stroke(anim, new BrushSettings { Size = 14, Hardness = 1 }, ColorRgb.Parse(Colours[f]), f * 0.8, layer: ink);
        }
        for (int f = 0; f < 5; f++) list.Add(($"animation · frame {f}", Hash(anim.RenderFrame(f))));
        anim.SetCurrentFrame(2);
        var (onion, onionBounds) = OnionSkin.Build(anim, new OnionSkinSettings(Enabled: true, Before: 2, After: 2));
        anim.SetOnionSkin(onion, onionBounds);
        list.Add(("animation · onion projection", Hash(anim.Projection)));
        anim.SetOnionSkin(null, PixelRect.Empty);
        var frames = AnimationExport.RenderFrames(anim, includePaper: true);
        using (var gif = new MemoryStream())
        {
            GifEncoder.Write(frames, gif, new GifOptions());
            list.Add(("animation · gif bytes", Hash(gif.ToArray())));
        }
        var sheet = AnimationExport.BuildSheet(frames, new SpriteSheetOptions(SheetLayout.Horizontal, Spacing: 2, Padding: 1));
        list.Add(("animation · sprite sheet", Hash(sheet.Image)));

        // 5. Mission 010 tools: smudge, clone, repair, liquify, perspective and warp.
        var retouch = new Document(400, 300, ColorRgb.White, "R");
        Stroke(retouch, new BrushSettings { Size = 100, Hardness = 0.6 }, ColorRgb.Parse("#5F8FA8"), 0);
        Stroke(retouch, new BrushSettings { Size = 80, Hardness = 0.6 }, ColorRgb.Parse("#E9A94D"), 1.5);
        foreach (var (kind, source) in new[] { (RetouchKind.Smudge, ((int, int)?)null), (RetouchKind.Clone, (-60, 10)), (RetouchKind.Repair, ((int, int)?)null) })
        {
            var r = new RetouchStroke(retouch);
            r.Begin(new RetouchStyle(kind, 40, 0.7, 0.5, PressureCurve.Normal, Source: source), At(retouch, 0, 0.4));
            for (int e = 1; e <= 60; e++) r.Add([At(retouch, e / 60.0, 0.4)]);
            r.End();
            list.Add(($"retouch · {kind}", Hash(retouch.ActiveLayer.Image)));
        }
        var lq = LiquifySession.Begin(retouch, retouch.ActiveLayer, LiquifySampling.Smooth)!;
        lq.Size = 120;
        lq.Strength = 0.7;
        lq.BeginStroke(100, 150);
        for (int e = 1; e <= 30; e++) lq.StrokeTo(100 + e * 6, 150 + 20 * Math.Sin(e / 5.0), 1);
        lq.EndStroke();
        lq.Apply();
        list.Add(("liquify · layer", Hash(retouch.ActiveLayer.Image)));
        retouch.SetSelection(SelectionMask.Rectangle(retouch.Width, retouch.Height, new PixelRect(80, 60, 200, 150)), "Select");
        var t = FloatingTransform.Begin(retouch, retouch.ActiveLayer)!;
        t.Interpolation = Interpolation.Bilinear;
        t.SetMode(TransformMode.Perspective);
        var c = ((double X, double Y)[])t.Corners.Clone();
        c[1] = (c[1].X + 30, c[1].Y - 20);
        t.SetCorners(c);
        list.Add(("perspective · projection", Hash(retouch.Projection)));
        t.Commit();
        list.Add(("perspective · layer", Hash(retouch.ActiveLayer.Image)));

        // 6. PNG bytes (straight RGBA, filters chosen per row) and decoding them back.
        var flat = doc.Flatten();
        var png = PngCodec.Encode(flat);
        list.Add(("png · picture bytes", Hash(png)));
        list.Add(("png · picture decoded", Hash(PngCodec.Decode(png))));
        var sprite = target.Image.Crop(new PixelRect(40, 30, 200, 150));
        var spritePng = PngCodec.Encode(sprite);
        list.Add(("png · transparent sprite bytes", Hash(spritePng)));
        list.Add(("png · transparent sprite decoded", Hash(PngCodec.Decode(spritePng))));
        return list;
    }

    [Fact]
    public void Every_scenario_matches_the_mission_010_engine_pixel_for_pixel()
    {
        var actual = Scenarios();
        var lines = actual.Select(a => $"        [\"{a.Name}\"] = \"{a.Hash}\",");
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(Environment.NewLine, lines));
        if (Environment.GetEnvironmentVariable("DN_GOLDEN_OUT") is { Length: > 0 } outPath) File.WriteAllLines(outPath, lines);
        Assert.NotEmpty(Expected);
        foreach (var (name, hash) in actual)
        {
            Assert.True(Expected.TryGetValue(name, out var expected), $"No recorded hash for \"{name}\"");
            Assert.True(expected == hash, $"\"{name}\" differs from Mission 010: {hash} instead of {expected}");
        }
        Assert.Equal(Expected.Count, actual.Count);
    }
}
