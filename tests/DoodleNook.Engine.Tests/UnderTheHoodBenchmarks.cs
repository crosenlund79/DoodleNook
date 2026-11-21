using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Mission 011 (Under the Hood): repeatable measurements of the engine on realistic documents — pen-event latency
/// (average and high percentiles) and allocations per event, compositing, masks and adjustments, palette lock, pixel art,
/// animation (frame changes, onion skins, exports), saving and loading, undo memory and a long session. It uses only
/// public APIs that existed in Mission 010, so the very same file measures the code before and after the mission.
/// Results go to tests/DoodleNook.Engine.Tests/out/under-the-hood.json. "Cold" is the first run in the process (it includes
/// compiling the code); "warm" is the median of the following runs. The only assertions catch gross regressions.
/// </summary>
[Collection(MeasurementCollection.Name)]
public class UnderTheHoodBenchmarks
{
    private static readonly string[] Colours = ["#9CC07A", "#5F8FA8", "#E9A94D", "#C96F4A", "#8A6FB0", "#3B2F2A"];

    // ------------------------------------------------------------------ documents

    /// <summary>Layers with broad paint across the picture, a few at reduced opacity, in Multiply or clipped (like PerformanceTests).</summary>
    internal static Document Layered(int width, int height, int layers, int active, bool variety = true)
    {
        var doc = new Document(width, height, ColorRgb.White, "Sketch");
        for (int i = 0; i < layers; i++)
        {
            var layer = i == 0 ? doc.ActiveLayer : doc.AddLayer($"Layer {i + 1}");
            if (variety)
                doc.SetLayerProperties(layer, layer.Properties with
                {
                    Opacity = i % 3 == 1 ? 0.7 : 1,
                    Blend = i % 4 == 2 ? BlendMode.Multiply : BlendMode.Normal,
                    ClipToBelow = i % 5 == 4,
                });
            Paint(doc, layer, i);
        }
        doc.SetActiveLayer(doc.Layers[Math.Clamp(active, 0, layers - 1)]);
        return doc;
    }

    internal static void Paint(Document doc, Layer layer, int i)
    {
        int width = doc.Width, height = doc.Height;
        var engine = new StrokeEngine(doc);
        var style = new StrokeStyle(new BrushSettings { Size = height / 3.0, Hardness = 0.4, Opacity = 0.8 }, PressureCurve.Normal,
            ColorRgb.Parse(Colours[i % Colours.Length]), PaintMode.Paint);
        engine.Begin(style, new StrokeSample(0, height * (0.2 + 0.6 * (i % 4) / 3.0)), layer);
        for (int k = 1; k <= 40; k++)
            engine.Add(new StrokeSample(width * k / 40.0, height * (0.5 + 0.35 * Math.Sin(k / 6.0 + i))));
        engine.End();
    }

    /// <summary>1920 × 1080: four painted layers, two of them in a group, a mask on the top one (hiding a band) and a
    /// Hue/Saturation adjustment above everything.</summary>
    private static Document MasksAndAdjustments()
    {
        var doc = Layered(1920, 1080, 4, 3, variety: false);
        doc.GroupLayers([doc.Layers[1], doc.Layers[2]], "Group");
        var top = doc.Layers.Last(l => l.Kind == LayerKind.Raster);
        doc.SetActiveLayer(top);
        doc.AddMask(top);
        doc.SetEditingMask(true);
        var engine = new StrokeEngine(doc);
        engine.Begin(new StrokeStyle(new BrushSettings { Size = 260, Hardness = 0.3 }, PressureCurve.Normal, ColorRgb.Black, PaintMode.Paint),
            new StrokeSample(100, 540), top);
        for (int k = 1; k <= 30; k++) engine.Add(new StrokeSample(100 + 1700 * k / 30.0, 540 + 200 * Math.Sin(k / 5.0)));
        engine.End();
        doc.SetEditingMask(false);
        doc.AddAdjustmentLayer(new HueSaturation(10, 15, 0));
        doc.SetActiveLayer(top);
        return doc;
    }

    /// <summary>A picture locked to the 32-colour default palette, painted after locking (so every stroke is conformed).</summary>
    private static Document PaletteLocked(int width, int height, int layers)
    {
        var doc = new Document(width, height, ColorRgb.White, "Sketch");
        doc.ChangePalette(Palette.Default(), true, null, "Lock palette");
        for (int i = 0; i < layers; i++) Paint(doc, i == 0 ? doc.ActiveLayer : doc.AddLayer($"Layer {i + 1}"), i);
        return doc;
    }

    /// <summary>An animation: a background drawing held over every frame and a character layer with its own drawing per frame.</summary>
    private static Document Animation(int width, int height, int frames, int onEvery = 1)
    {
        var doc = Document.NewAnimation(width, height, ColorRgb.White, frames, 12, "Background");
        var bg = doc.ActiveLayer;
        Paint(doc, bg, 0);
        for (int f = 0; f < frames - 1; f++) doc.ExtendHold(bg, 0);
        var character = doc.AddLayer("Character");
        for (int f = 0; f < frames; f += onEvery)
        {
            doc.SetCurrentFrame(f);
            var engine = new StrokeEngine(doc);
            var style = new StrokeStyle(new BrushSettings { Size = height / 8.0, Hardness = 0.7 }, PressureCurve.Normal, ColorRgb.Parse(Colours[f % 6]), PaintMode.Paint);
            double cx = width * (0.2 + 0.6 * f / Math.Max(1, frames - 1)), cy = height * 0.55;
            engine.Begin(style, new StrokeSample(cx, cy - height * 0.15), character);
            for (int k = 1; k <= 12; k++)
                engine.Add(new StrokeSample(cx + width * 0.05 * Math.Sin(k / 2.0), cy - height * 0.15 + height * 0.3 * k / 12.0));
            engine.End();
            if (onEvery > 1 && f + 1 < frames)
                for (int h = 1; h < onEvery && f + h < frames; h++) doc.ExtendHold(character, f);
        }
        doc.SetCurrentFrame(0);
        return doc;
    }

    // ------------------------------------------------------------------ measuring

    private static StrokeSample At(Document d, double t) =>
        new(d.Width * 0.1 + d.Width * 0.8 * t, d.Height * 0.5 + d.Height * 0.3 * Math.Sin(t * Math.PI * 3), (float)(0.3 + 0.7 * Math.Sin(Math.PI * t)));

    private static object Stats(List<double> times, string unit = "ms")
    {
        var s = times.OrderBy(t => t).ToList();
        double P(double q) => s[Math.Clamp((int)Math.Ceiling(q * s.Count) - 1, 0, s.Count - 1)];
        return new { avg = Math.Round(times.Average(), 3), p50 = Math.Round(P(0.5), 3), p95 = Math.Round(P(0.95), 3), p99 = Math.Round(P(0.99), 3), max = Math.Round(s[^1], 3), unit };
    }

    /// <summary>The same stroke three times (each undone): the run with the median average is reported, so one noisy run
    /// on a shared machine doesn't decide the result.</summary>
    private static object Stroke(Document doc, BrushSettings brush, BrushResources? resources = null, Layer? layer = null)
    {
        var runs = Enumerable.Range(0, 3).Select(_ => StrokeOnce(doc, brush, resources, layer)).ToList();
        var median = runs.OrderBy(r => r.Avg).ElementAt(1);
        return median.Result;
    }

    /// <summary>A fast curve across the picture as 200 pen events of 2 coalesced samples (a 200 Hz pen), then End and Undo.</summary>
    private static (double Avg, object Result) StrokeOnce(Document doc, BrushSettings brush, BrushResources? resources, Layer? layer)
    {
        var engine = new StrokeEngine(doc);
        var style = new StrokeStyle(brush, PressureCurve.Normal, ColorRgb.Parse("#3B2F2A"), PaintMode.Paint, Resources: resources);
        var times = new List<double>(200);
        var sw = new Stopwatch();
        var batch = new StrokeSample[2];
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        engine.Begin(style, At(doc, 0), layer);
        double begin = sw.Elapsed.TotalMilliseconds;
        for (int e = 0; e < 200; e++)
        {
            batch[0] = At(doc, (2 * e + 1) / 400.0);
            batch[1] = At(doc, (2 * e + 2) / 400.0);
            sw.Restart();
            engine.Add(batch);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        long allocEvents = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        sw.Restart();
        engine.End();
        double end = sw.Elapsed.TotalMilliseconds;
        long undoBytes = doc.History.UndoEntries.Count > 0 ? doc.History.UndoEntries[^1].ByteSize : 0;
        sw.Restart();
        doc.Undo();
        double undo = sw.Elapsed.TotalMilliseconds;
        return (times.Average(), new
        {
            perEvent = Stats(times), beginMs = Math.Round(begin, 2), endMs = Math.Round(end, 2), undoMs = Math.Round(undo, 2),
            allocatedKBPerEvent = Math.Round(allocEvents / 200.0 / 1024, 2), undoStepKB = undoBytes / 1024,
        });
    }

    /// <summary>The first run (cold) and the median of the next <paramref name="warm"/> runs.</summary>
    private static object ColdWarm(Action run, int warm = 5)
    {
        var sw = Stopwatch.StartNew();
        run();
        double cold = sw.Elapsed.TotalMilliseconds;
        var times = new List<double>();
        for (int i = 0; i < warm; i++)
        {
            sw.Restart();
            run();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return new { coldMs = Math.Round(cold, 1), warmMedianMs = Math.Round(times[times.Count / 2], 1), warmMinMs = Math.Round(times[0], 1) };
    }

    private static double Median(int runs, Action run)
    {
        var times = new List<double>();
        var sw = new Stopwatch();
        for (int i = 0; i < runs; i++)
        {
            sw.Restart();
            run();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return Math.Round(times[times.Count / 2], 2);
    }

    private static double HeapMB()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return Math.Round(GC.GetTotalMemory(true) / 1048576.0, 1);
    }

    private static readonly (string Name, BrushSettings Brush)[] Brushes =
    [
        ("6 px pencil (grain)", new BrushSettings { Size = 6, Hardness = 0.85, PressureSize = true, Grain = 0.55 }),
        ("24 px ink", new BrushSettings { Size = 24, Hardness = 1, PressureSize = true }),
        ("120 px hard", new BrushSettings { Size = 120, Hardness = 0.98 }),
        ("300 px soft", new BrushSettings { Size = 300, Hardness = 0.05, Opacity = 0.6, PressureOpacity = true }),
    ];

    // ------------------------------------------------------------------ the measurement

    [Fact]
    public void Measure_under_the_hood()
    {
        var results = new Dictionary<string, object>();
        double startHeap = HeapMB();
        // Warm up the JIT for strokes and compositing.
        var warmup = Layered(400, 300, 2, 1);
        foreach (var (_, b) in Brushes) Stroke(warmup, b);
        warmup = null;
        // Each part runs in its own method, so its documents are released before the next part measures memory.
        LayeredStrokes(results);
        TipBrushes(results);
        MasksAndAdjustmentsPart(results);
        PaletteLockPart(results);
        PixelArt(results);
        Animation21(results);
        AnimationOnTwos(results);
        Files(results);
        UndoMemory(results);
        SketchUndoMemory(results);
        LiquifyMemory(results);
        LongSession(results);
        Library(results);
        results["process"] = new
        {
            managedHeapAtStartMB = startHeap, managedHeapAtEndMB = HeapMB(), workingSetMB = Math.Round(Environment.WorkingSet / 1048576.0, 0),
            peakWorkingSetMB = Math.Round(System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0, 0),
            gcCounts = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
        };
        Save(results);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void LayeredStrokes(Dictionary<string, object> results)
    {
        // 1. Pen events in layered pictures.
        foreach (var (w, h, layers, active, label) in new[]
                 {
                     (1920, 1080, 1, 0, "1920x1080, 1 layer"),
                     (1920, 1080, 3, 2, "1920x1080, 3 layers (top active)"),
                     (2560, 1440, 12, 6, "2560x1440, 12 layers (middle active)"),
                     (2560, 1440, 12, 11, "2560x1440, 12 layers (top active)"),
                 })
        {
            var doc = Layered(w, h, layers, active);
            foreach (var (name, brush) in Brushes)
            {
                if (layers == 12 && name.StartsWith("6 px")) continue;
                results[$"stroke · {label} · {name}"] = Stroke(doc, brush);
            }
            results[$"recomposite everything · {label}"] = ColdWarm(() => doc.RecompositeAll(notify: false), 5);
            results[$"flatten · {label}"] = ColdWarm(() => doc.Flatten(), 3);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void TipBrushes(Dictionary<string, object> results)
    {
        // Image-tip and colour-tip brushes from the library (charcoal, leaves).
        {
            var lib = new CreativeLibrary();
            var doc = Layered(1920, 1080, 3, 2);
            foreach (var id in new[] { "charcoal", "leaves" })
            {
                var preset = lib.FindBrush(id)!;
                results[$"stroke · 1920x1080, 3 layers · {preset.Name} ({id})"] = Stroke(doc, preset.Settings, lib.Resolve(preset.Settings));
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void MasksAndAdjustmentsPart(Dictionary<string, object> results)
    {
        // 2. Masks, a group and an adjustment layer.
        {
            var doc = MasksAndAdjustments();
            results["stroke · masks+group+adjustment 1920x1080 · 24 px ink"] = Stroke(doc, Brushes[1].Brush);
            results["stroke · masks+group+adjustment 1920x1080 · 300 px soft"] = Stroke(doc, Brushes[3].Brush);
            results["recomposite everything · masks+group+adjustment 1920x1080"] = ColdWarm(() => doc.RecompositeAll(notify: false), 5);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void PaletteLockPart(Dictionary<string, object> results)
    {
        // 3. Palette lock (every change is conformed to the palette).
        {
            var doc = PaletteLocked(1920, 1080, 3);
            results["stroke · palette-locked 1920x1080, 3 layers · 24 px ink"] = Stroke(doc, Brushes[1].Brush);
            results["stroke · palette-locked 1920x1080, 3 layers · 300 px soft"] = Stroke(doc, Brushes[3].Brush);
            var free = Layered(1920, 1080, 3, 2);
            var sw = Stopwatch.StartNew();
            var remap = PaletteRemap.Analyse(free, Palette.Default().Colors);
            double analyse = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            remap.Apply(free, "Nook 32", lockAfter: true);
            double apply = sw.Elapsed.TotalMilliseconds;
            results["palette remap · 1920x1080, 3 layers → Nook 32"] = new { analyseMs = Math.Round(analyse, 1), applyAndLockMs = Math.Round(apply, 1), sources = remap.Sources.Count };
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void PixelArt(Dictionary<string, object> results)
    {
        // 4. Pixel art.
        foreach (var size in new[] { 64, 256 })
        {
            var doc = new Document(size, size, null, "Sprite");
            doc.AddLayer("Ink");
            var times = new List<double>();
            var sw = new Stopwatch();
            for (int s = 0; s < 20; s++)
            {
                var stroke = new PixelStroke(doc);
                stroke.Begin(new PixelStrokeStyle(ColorRgb.Parse(Colours[s % 6]), Size: 1 + s % 3), s % size, 0);
                for (int e = 1; e <= 50; e++)
                {
                    int x = (s * 7 + e * 3) % size, y = e * size / 51;
                    sw.Restart();
                    stroke.Add(x, y);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                stroke.End();
            }
            byte[] png = [];
            results[$"pixel art {size}x{size} · pixel pencil event"] = Stats(times);
            results[$"pixel art {size}x{size} · undo + redo"] = Median(9, () => { doc.Undo(); doc.Redo(); });
            results[$"pixel art {size}x{size} · PNG encode"] = ColdWarm(() => png = PngCodec.Encode(doc.Flatten()), 5);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Animation21(Dictionary<string, object> results)
    {
        // 5. Animation: 1920 × 1080, 21 frames (a held background and a character drawn on every frame).
        {
            var sw = Stopwatch.StartNew();
            var doc = Animation(1920, 1080, 21);
            double build = sw.Elapsed.TotalMilliseconds;
            Assert.Equal(21, doc.FrameCount);
            var cold = new List<double>();
            for (int f = 1; f < 21; f++)
            {
                sw.Restart();
                doc.SetCurrentFrame(f);
                cold.Add(sw.Elapsed.TotalMilliseconds);
            }
            var warm = new List<double>();
            for (int round = 0; round < 3; round++)
                for (int f = 0; f < 21; f++)
                {
                    sw.Restart();
                    doc.SetCurrentFrame(f);
                    warm.Add(sw.Elapsed.TotalMilliseconds);
                }
            doc.SetCurrentFrame(10);
            var onionSettings = new OnionSkinSettings(Enabled: true, Before: 2, After: 2);
            var onion = ColdWarm(() =>
            {
                var (img, bounds) = OnionSkin.Build(doc, onionSettings);
                doc.SetOnionSkin(img, bounds);
            }, 5);
            doc.SetOnionSkin(null, PixelRect.Empty);
            var strokeOnFrame = Stroke(doc, Brushes[1].Brush);
            List<ExportFrame>? frames = null;
            var render = ColdWarm(() => frames = AnimationExport.RenderFrames(doc, includePaper: true), 2);
            using var gif = new MemoryStream();
            sw.Restart();
            GifEncoder.Write(frames!, gif, new GifOptions());
            double gifMs = sw.Elapsed.TotalMilliseconds;
            results["animation 1920x1080 × 21 frames"] = new
            {
                buildMs = Math.Round(build, 0),
                frameChangeFirstVisit = Stats(cold),
                frameChangeCached = Stats(warm),
                frameCacheMB = Math.Round(doc.FrameCacheBytes / 1048576.0, 1),
                onionSkin2plus2 = onion,
                strokeOnFrame,
                renderAllFramesForExport = render,
                gifEncodeMs = Math.Round(gifMs, 0),
                gifBytes = gif.Length,
            };
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void AnimationOnTwos(Dictionary<string, object> results)
    {
        // 6. A longer animation with holds: 1920 × 1080, 48 frames, the character on twos.
        {
            double before = HeapMB();
            var sw = Stopwatch.StartNew();
            var doc = Animation(1920, 1080, 48, onEvery: 2);
            double build = sw.Elapsed.TotalMilliseconds;
            double after = HeapMB();
            var cold = new List<double>();
            for (int f = 1; f < doc.FrameCount; f++)
            {
                sw.Restart();
                doc.SetCurrentFrame(f);
                cold.Add(sw.Elapsed.TotalMilliseconds);
            }
            var cels = doc.AllCels().Count();
            var snapshot = ColdWarm(() => DnkFile.Snapshot(doc), 3);
            using var ms = new MemoryStream();
            sw.Restart();
            DnkFile.Write(doc, ms);
            double write = sw.Elapsed.TotalMilliseconds;
            ms.Position = 0;
            sw.Restart();
            var back = DnkFile.Read(ms);
            double read = sw.Elapsed.TotalMilliseconds;
            Assert.Equal(doc.FrameCount, back.FrameCount);
            results["animation 1920x1080 × 48 frames on twos"] = new
            {
                frames = doc.FrameCount, drawings = cels, buildMs = Math.Round(build, 0), managedHeapGrowthMB = Math.Round(after - before, 1),
                frameChangeFirstVisit = Stats(cold), frameCacheMB = Math.Round(doc.FrameCacheBytes / 1048576.0, 1),
                saveSnapshotOnUiThread = snapshot, dnkWriteMs = Math.Round(write, 0), dnkReadMs = Math.Round(read, 0), dnkMB = Math.Round(ms.Length / 1048576.0, 1),
            };
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Files(Dictionary<string, object> results)
    {
        // 7. Saving and loading a 2560 × 1440, 12-layer picture.
        {
            var doc = Layered(2560, 1440, 12, 6);
            var dir = Path.Combine(Path.GetTempPath(), "dn-bench-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "picture.dnk");
                var snapshot = ColdWarm(() => DnkFile.Snapshot(doc), 3);
                var save = ColdWarm(() => DnkFile.Save(doc, path), 3);
                Document? back = null;
                var load = ColdWarm(() => back = DnkFile.Load(path), 3);
                Assert.Equal(12, back!.Layers.Count);
                var flat = doc.Flatten();
                var png = ColdWarm(() => PngCodec.Encode(flat), 3);
                byte[] bytes = PngCodec.Encode(flat);
                var decode = ColdWarm(() => PngCodec.Decode(bytes), 3);
                results["files · 2560x1440, 12 layers"] = new
                {
                    saveSnapshotOnUiThread = snapshot, saveToDisk = save, load, dnkMB = Math.Round(new FileInfo(path).Length / 1048576.0, 1),
                    pngEncodeFlattened = png, pngDecode = decode, pngMB = Math.Round(bytes.Length / 1048576.0, 1),
                };
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void UndoMemory(Dictionary<string, object> results)
    {
        // 8. Undo memory and speed: 30 large strokes on a 2560 × 1440, 12-layer picture.
        {
            var doc = Layered(2560, 1440, 12, 6);
            var engine = new StrokeEngine(doc);
            for (int s = 0; s < 30; s++)
            {
                engine.Begin(new StrokeStyle(Brushes[2].Brush, PressureCurve.Normal, ColorRgb.Parse(Colours[s % 6]), PaintMode.Paint), At(doc, 0), doc.Layers[s % 12]);
                for (int e = 1; e <= 60; e++) engine.Add(At(doc, e / 60.0) with { Y = At(doc, e / 60.0).Y + s * 10 });
                engine.End();
            }
            long bytes = doc.History.TotalBytes;
            var sw = Stopwatch.StartNew();
            while (doc.CanUndo) doc.Undo();
            double undoAll = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            while (doc.CanRedo) doc.Redo();
            double redoAll = sw.Elapsed.TotalMilliseconds;
            results["undo · 30 strokes of 120 px, 2560x1440, 12 layers"] = new
            {
                historyMB = Math.Round(bytes / 1048576.0, 1), steps = doc.History.UndoCount, undoAllMs = Math.Round(undoAll, 0), redoAllMs = Math.Round(redoAll, 0),
            };
        }
    }

    /// <summary>Line art on a new transparent layer over a painted picture: what 60 ink strokes cost in undo memory.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void SketchUndoMemory(Dictionary<string, object> results)
    {
        var doc = Layered(1920, 1080, 3, 2);
        var ink = doc.AddLayer("Ink");
        long before = doc.History.TotalBytes;
        double heap0 = HeapMB();
        var engine = new StrokeEngine(doc);
        for (int s = 0; s < 60; s++)
        {
            var brush = new BrushSettings { Size = 6 + s % 4 * 6, Hardness = 1, PressureSize = true };
            double y0 = 60 + s * 16;
            engine.Begin(new StrokeStyle(brush, PressureCurve.Normal, ColorRgb.Parse("#3B2F2A"), PaintMode.Paint), new StrokeSample(100, y0, 0.3f), ink);
            for (int e = 1; e <= 80; e++) engine.Add(new StrokeSample(100 + e * 21, y0 + 30 * Math.Sin(e / 7.0 + s), (float)(0.3 + 0.7 * Math.Sin(Math.PI * e / 80))));
            engine.End();
        }
        long bytes = doc.History.TotalBytes - before;
        results["undo memory · 60 ink strokes on a new layer, 1920x1080"] = new
        {
            historyMB = Math.Round(bytes / 1048576.0, 1), perStrokeKB = bytes / 60 / 1024, managedHeapGrowthMB = Math.Round(HeapMB() - heap0, 1),
        };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void LiquifyMemory(Dictionary<string, object> results)
    {
        // 9. Liquify: memory of a session, released after Cancel.
        {
            var doc = Layered(2560, 1440, 3, 1);
            double before = HeapMB();
            long field = 0;
            for (int round = 0; round < 5; round++)
            {
                var l = LiquifySession.Begin(doc, doc.ActiveLayer, LiquifySampling.Smooth)!;
                l.Size = 300;
                l.BeginStroke(500, 700);
                for (int e = 1; e <= 60; e++) l.StrokeTo(500 + e * 20, 700 + 100 * Math.Sin(e / 8.0), 1);
                l.EndStroke();
                field = l.FieldBytes;
                l.Cancel();
            }
            double after = HeapMB();
            results["liquify · 5 sessions on 2560x1440, cancelled"] = new { fieldMB = Math.Round(field / 1048576.0, 1), heapGrowthAfterCancelMB = Math.Round(after - before, 1) };
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void LongSession(Dictionary<string, object> results)
    {
        // 10. A long session: 400 strokes with undo/redo and layers added and removed; the managed heap at checkpoints.
        {
            var doc = Layered(1920, 1080, 3, 2);
            var checkpoints = new List<object> { new { strokes = 0, heapMB = HeapMB(), historyMB = Math.Round(doc.History.TotalBytes / 1048576.0, 1) } };
            var engine = new StrokeEngine(doc);
            var times = new List<double>();
            var sw = new Stopwatch();
            for (int s = 1; s <= 400; s++)
            {
                var (_, brush) = Brushes[s % Brushes.Length];
                sw.Restart();
                engine.Begin(new StrokeStyle(brush, PressureCurve.Normal, ColorRgb.Parse(Colours[s % 6]), PaintMode.Paint), At(doc, 0));
                for (int e = 1; e <= 40; e++)
                {
                    var p = At(doc, e / 40.0);
                    engine.Add(p with { X = (p.X + s * 37) % doc.Width, Y = (p.Y + s * 23) % doc.Height });
                }
                engine.End();
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (s % 10 == 0)
                {
                    doc.Undo();
                    doc.Undo();
                    doc.Redo();
                }
                if (s % 50 == 0)
                {
                    var extra = doc.AddLayer("Extra");
                    Paint(doc, extra, s);
                    doc.DeleteLayer(doc.Layers[1]);
                    doc.SetActiveLayer(doc.Layers[^1]);
                }
                if (s % 100 == 0) checkpoints.Add(new { strokes = s, heapMB = HeapMB(), historyMB = Math.Round(doc.History.TotalBytes / 1048576.0, 1) });
            }
            results["long session · 400 strokes, 1920x1080"] = new
            {
                wholeStroke = Stats(times), checkpoints, historySteps = doc.History.UndoCount,
            };
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Library(Dictionary<string, object> results)
    {
        // 11. A creative library with many brushes, stamps and patterns.
        {
            var root = Path.Combine(Path.GetTempPath(), "dn-lib-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var lib = new CreativeLibrary(root);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 200; i++) lib.AddBrush($"Brush {i}", Brushes[i % Brushes.Length].Brush with { Size = 4 + i });
                var stamp = new RasterImage(96, 96);
                for (int i = 0; i < stamp.Pixels.Length; i++) stamp.Pixels[i] = Pixel.Opaque(new ColorRgb((byte)i, (byte)(i >> 3), 90));
                for (int i = 0; i < 100; i++) lib.AddStamp($"Stamp {i}", "Mine", stamp);
                for (int i = 0; i < 60; i++) lib.AddPattern($"Pattern {i}", "Mine", stamp);
                double fill = sw.Elapsed.TotalMilliseconds;
                CreativeLibrary? loaded = null;
                var load = ColdWarm(() => loaded = new CreativeLibrary(root), 3);
                string last = loaded!.Brushes[^1].Id;
                double find = Median(5, () => { for (int i = 0; i < 1000; i++) loaded.FindBrush(last); }) / 1000;
                double list = Median(5, () => { for (int i = 0; i < 1000; i++) _ = loaded.Brushes.Count; }) / 1000;
                results["library · 200 brushes, 100 stamps, 60 patterns"] = new
                {
                    addAllMs = Math.Round(fill, 0), load, brushes = loaded.Brushes.Count, findBrushMs = Math.Round(find, 4), listBrushesMs = Math.Round(list, 4),
                };
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }

    private static void Save(Dictionary<string, object> results)
    {
        var report = new
        {
            measuredAt = DateTimeOffset.UtcNow,
            os = Environment.OSVersion.ToString(),
            runtime = Environment.Version.ToString(),
            cpu = Environment.ProcessorCount + " logical processors",
            serverGc = GCSettings.IsServerGC,
            configuration =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            note = "Engine only (no screen drawing). Pen event = 2 coalesced samples of a fast curve across the picture (200 events). " +
                   "Times in milliseconds; cold = first run in the process, warm = median of the following runs.",
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
            File.WriteAllText(Path.Combine(outDir, Environment.GetEnvironmentVariable("DN_BENCH_NAME") ?? "under-the-hood.json"), json);
        }
    }
}
