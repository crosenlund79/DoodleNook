using System.Diagnostics;
using System.Text.Json;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Measures (does not guess) what Mission 007's creative tools cost in the engine: pen events for the new brushes
/// (image tips, textures, scatter, stamp brushes) next to the old ones, placing a stamp, pattern fills, palette
/// extraction, brush-preview rendering and loading the library. Results go to
/// tests/DoodleNook.Engine.Tests/out/workshop-performance.json. Assertions are generous: they only catch gross regressions.
/// </summary>
public class WorkshopPerformanceTests
{
    private static readonly CreativeLibrary Lib = new();

    private static BrushSettings Preset(string id) => BuiltInAssets.Brushes.First(b => b.Id == id).Settings;

    private static Document Painted(int width, int height, int layers, bool animation = false)
    {
        var doc = animation ? Document.NewAnimation(width, height, ColorRgb.White, 12, 12, "Background") : new Document(width, height, ColorRgb.White, "Sketch");
        var colours = new[] { "#9CC07A", "#5F8FA8", "#E9A94D", "#C96F4A", "#8A6FB0", "#3B2F2A" };
        for (int i = 0; i < layers; i++)
        {
            var layer = i == 0 ? doc.ActiveLayer : doc.AddLayer($"Layer {i + 1}");
            var engine = new StrokeEngine(doc);
            var style = new StrokeStyle(new BrushSettings { Size = height / 3.0, Hardness = 0.4, Opacity = 0.8 }, PressureCurve.Normal,
                ColorRgb.Parse(colours[i % colours.Length]), PaintMode.Paint);
            engine.Begin(style, new StrokeSample(0, height * (0.2 + 0.6 * (i % 4) / 3.0)), layer);
            for (int k = 1; k <= 30; k++)
                engine.Add(new StrokeSample(width * k / 30.0, height * (0.5 + 0.35 * Math.Sin(k / 6.0 + i))));
            engine.End();
        }
        doc.SetActiveLayer(doc.Layers[layers / 2]);
        if (animation) doc.SetCurrentFrame(5);
        return doc;
    }

    /// <summary>A fast 2000 px curve as 200 events of 2 coalesced samples (how a 200 Hz pen delivers it).</summary>
    private static (double Avg, double Max, double Whole) StrokeCost(Document doc, BrushSettings brush)
    {
        var engine = new StrokeEngine(doc);
        var style = new StrokeStyle(brush, PressureCurve.Normal, ColorRgb.Parse("#3B2F2A"), PaintMode.Paint, Symmetry.None, Lib.Resolve(brush));
        double w = doc.Width, h = doc.Height;
        StrokeSample At(double t) => new(w * 0.1 + w * 0.8 * t, h * 0.5 + h * 0.3 * Math.Sin(t * Math.PI * 3), (float)(0.3 + 0.7 * Math.Sin(Math.PI * t)));
        var times = new List<double>(200);
        var sw = new Stopwatch();
        var whole = Stopwatch.StartNew();
        engine.Begin(style, At(0));
        var batch = new StrokeSample[2];
        for (int e = 0; e < 200; e++)
        {
            batch[0] = At((2 * e + 1) / 400.0);
            batch[1] = At((2 * e + 2) / 400.0);
            sw.Restart();
            engine.Add(batch);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        engine.End();
        return (Math.Round(times.Average(), 3), Math.Round(times.Max(), 3), Math.Round(whole.Elapsed.TotalMilliseconds, 1));
    }

    private static double Time(Action a, int repeat = 1)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < repeat; i++) a();
        return Math.Round(sw.Elapsed.TotalMilliseconds / repeat, 3);
    }

    /// <summary>What the brush picker does for one tile: the S-curve preview at 2× (same work as BrushPreview.Render, minus the bitmap copy).</summary>
    private static void Preview(BrushSettings brush, int width = 120, int height = 36, double scale = 2)
    {
        var doc = new Document((int)(width * scale), (int)(height * scale));
        var b = brush.Normalized() with { Size = Math.Clamp(brush.Size / 4, 2, height * 0.42) * scale, Smoothing = 0 };
        var engine = new StrokeEngine(doc);
        engine.Begin(new StrokeStyle(b, PressureCurve.Normal, ColorRgb.Parse("#3B2F2A"), PaintMode.Paint, Symmetry.None, Lib.Resolve(brush)),
            new StrokeSample(8, height * scale - 8));
        for (int i = 1; i <= 64; i++)
        {
            double t = i / 64.0;
            engine.Add(new StrokeSample(8 + (width * scale - 16) * t, height * scale / 2 + Math.Sin(t * Math.PI * 2) * height * scale * 0.3, (float)(0.45 + 0.55 * Math.Sin(Math.PI * t))));
        }
        engine.End();
    }

    [Fact]
    public void Measure_creative_tools()
    {
        var brushes = new[] { "pencil", "ink", "soft", "graphite", "charcoal", "chalk", "dryink", "paint", "watercolor", "spatter", "grass", "leaves" };
        // Warm up the JIT and the tip/texture caches so the first measurement isn't a compile.
        var warm = Painted(400, 300, 1);
        foreach (var id in brushes) StrokeCost(warm, Preset(id));

        var results = new List<object>();
        foreach (var (w, h, layers, animation) in new[] { (1920, 1080, 1, false), (1920, 1080, 6, false), (2560, 1440, 12, false), (1920, 1080, 3, true) })
        {
            var doc = Painted(w, h, layers, animation);
            string where = $"{w}x{h}, {layers} layer{(layers == 1 ? "" : "s")}{(animation ? ", animation frame 6 of 12" : "")}";
            foreach (var id in brushes)
            {
                var brush = Preset(id);
                var (avg, max, whole) = StrokeCost(doc, brush);
                results.Add(new { document = where, brush = id, avgMsPerPenEvent = avg, maxMsPerPenEvent = max, wholeStrokeMs = whole });
                // A pen at 200 Hz delivers an event every 5 ms; anything near 50 ms per event would feel broken.
                Assert.True(avg < 50, $"{where} / {id}: {avg} ms per event");
            }

            // Stamps: one click (paint into the layer + undo record + recomposite under it).
            var grass = Lib.StampImage("builtin:grass")!;
            var flower = Lib.StampImage("builtin:flower")!;
            var layer = doc.ActiveLayer;
            double stampSmall = Time(() => StampsAndPatterns.PlaceStamp(doc, layer, flower, new StampPlacement(w * 0.4, h * 0.6, 0.5, 15)), 10);
            double stampLarge = Time(() => StampsAndPatterns.PlaceStamp(doc, layer, grass, new StampPlacement(w * 0.5, h * 0.7, 4, 0)), 5);
            double undoStamp = Time(() => doc.Undo());

            // Pattern fills: the bucket in an enclosed area, and filling a big selection.
            var brick = Lib.PatternImage("builtin:brick")!;
            var bucketDoc = new Document(w, h, ColorRgb.White, "Fill");
            double bucket = Time(() => StampsAndPatterns.FillAt(bucketDoc, bucketDoc.ActiveLayer, w / 2, h / 2, brick, new PatternFillOptions(1.5), new FillOptions()));
            // For comparison: the ordinary colour bucket on the same empty layer (Mission 005's fill).
            var colourDoc = new Document(w, h, ColorRgb.White, "Fill");
            double colourBucket = Time(() => FloodFill.Apply(colourDoc, colourDoc.ActiveLayer, w / 2, h / 2, ColorRgb.Parse("#C96F4A"), new FillOptions()));
            doc.SetSelection(Selections.Rectangle(doc, w * 0.1, h * 0.1, w * 0.9, h * 0.9), "Select");
            double selectionFill = Time(() => StampsAndPatterns.FillSelection(doc, layer, brick, new PatternFillOptions(1.5, Rotation: 20, Opacity: 0.8)));
            double undoFill = Time(() => doc.Undo());
            doc.SetSelection(null, "Deselect");

            results.Add(new
            {
                document = where,
                stampSmallFlowerMs = stampSmall,
                stampLargeGrass4xMs = stampLarge,
                undoStampMs = undoStamp,
                patternBucketFillWholeCanvasMs = bucket,
                colourBucketFillWholeCanvasMs = colourBucket,
                patternFill80PercentSelectionRotatedMs = selectionFill,
                undoPatternFillMs = undoFill,
            });
            Assert.True(stampSmall < 200, $"{where}: stamp {stampSmall} ms");
        }

        // Pixel art: exact stamps and pattern fill in a 64 × 64 sprite.
        var sprite = new Document(64, 64, null, "Sprite");
        var mushroom = Lib.StampImage("builtin:mushroom")!;
        double pixelStamp = Time(() => StampsAndPatterns.PlaceStamp(sprite, sprite.ActiveLayer, mushroom, new StampPlacement(32, 32).PixelExact(mushroom), pixelExact: true), 20);
        double pixelPattern = Time(() => StampsAndPatterns.FillAt(sprite, sprite.ActiveLayer, 2, 2, Lib.PatternImage("builtin:grassland")!, new PatternFillOptions(), new FillOptions(), pixelExact: true), 20);

        // Palette from a picture: the 1920 × 1080 painted document, 8 and 32 colours.
        var picture = Painted(1920, 1080, 6).Flatten();
        double palette8 = Time(() => ColorQuantizer.Extract(picture, 8));
        double palette32 = Time(() => ColorQuantizer.Extract(picture, 32));

        // The brush picker's previews and the library.
        var previewTimes = brushes.ToDictionary(id => id, id => Time(() => Preview(Preset(id)), 5));
        double allPreviews = Time(() => { foreach (var b in BuiltInAssets.Brushes) Preview(b.Settings); });
        double libraryLoad = Time(() => new CreativeLibrary().Resolve(Preset("charcoal")));
        // First use of each built-in picture (drawn by code on first use, then kept in memory).
        var fresh = new CreativeLibrary();
        var firstUse = new Dictionary<string, double>();
        foreach (var s in fresh.Stamps) firstUse[s.Id] = Time(() => fresh.StampImage(s.Id));
        foreach (var p in fresh.Patterns) firstUse[p.Id] = Time(() => fresh.PatternImage(p.Id));
        double allThumbnails = Math.Round(firstUse.Values.Sum(), 1);

        results.Add(new
        {
            pixelArtExactStamp64Ms = pixelStamp,
            pixelArtPatternBucket64Ms = pixelPattern,
            paletteFromPicture1080p8ColoursMs = palette8,
            paletteFromPicture1080p32ColoursMs = palette32,
            brushPreviewTileMs = previewTimes,
            allBuiltInBrushPreviewsMs = allPreviews,
            newLibraryAndFirstResolveMs = libraryLoad,
            allBuiltInStampAndPatternImagesFirstUseMs = allThumbnails,
            builtInPictureFirstUseMs = firstUse,
        });

        var report = new
        {
            measuredAt = DateTimeOffset.UtcNow,
            os = Environment.OSVersion.ToString(),
            cpu = Environment.ProcessorCount + " logical processors",
            configuration =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            note = "Engine only (no screen drawing). Pen event = 2 coalesced samples of a fast 2000 px curve, as a 200 Hz pen delivers them.",
            results,
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        TestContext.Current.TestOutputHelper?.WriteLine(json);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        if (dir is not null)
        {
            var outDir = Path.Combine(dir.FullName, "tests", "DoodleNook.Engine.Tests", "out");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "workshop-performance.json"), json);
        }
    }
}
