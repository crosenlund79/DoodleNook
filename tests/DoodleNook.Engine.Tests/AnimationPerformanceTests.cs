using System.Diagnostics;
using System.Text.Json;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Measures (does not guess) Animation Studio's engine costs for three typical documents: a 64 × 64 sprite with 32
/// frames, a 256 × 256 animation with 60 frames and a 1920 × 1080 hand-drawn animation with 24 frames, each with a held
/// background and drawings on every frame. Results go to tests/DoodleNook.Engine.Tests/out/animation-performance.json.
/// Assertions are generous: they only catch gross regressions.
/// </summary>
public class AnimationPerformanceTests
{
    private static Document Build(int width, int height, int frames, bool pixel)
    {
        var doc = Document.NewAnimation(width, height, pixel ? null : ColorRgb.White, frames, 12, "Background");
        var background = doc.ActiveLayer;
        var body = doc.AddLayer("Character");
        var effects = doc.AddLayer("Effects");
        double s = Math.Min(width, height);
        Paint(doc, background, pixel, 0, [(0.05, 0.85), (0.95, 0.85)], s * 0.04, "#7A6A5E");
        Paint(doc, background, pixel, 0, [(0.1, 0.2), (0.3, 0.1), (0.5, 0.25), (0.8, 0.12)], s * 0.08, "#9CC07A");
        while (doc.HoldRange(background, 0).End < frames - 1) doc.ExtendHold(background, 0);
        for (int f = 0; f < frames; f++)
        {
            doc.SetCurrentFrame(f);
            double t = f / (double)frames;
            double x = 0.15 + 0.7 * t, y = 0.55 - 0.2 * Math.Abs(Math.Sin(t * Math.PI * 4));
            Paint(doc, body, pixel, f, [(x, y), (x + 0.05, y + 0.1), (x - 0.03, y + 0.2)], s * 0.12, "#E0604A");
            Paint(doc, body, pixel, f, [(x - 0.05, y + 0.25), (x + 0.06, y + 0.25)], s * 0.05, "#3B2F2A");
            if (f % 2 == 0) Paint(doc, effects, pixel, f, [(x + 0.1, y - 0.1), (x + 0.15, y - 0.05)], s * 0.03, "#5F8FA8");
        }
        doc.SetActiveLayer(body);
        doc.SetCurrentFrame(0);
        return doc;
    }

    private static void Paint(Document doc, Layer layer, bool pixel, int frame, (double X, double Y)[] path, double size, string colour)
    {
        doc.SetActiveLayer(layer);
        int w = doc.Width, h = doc.Height;
        if (pixel)
        {
            var stroke = new PixelStroke(doc);
            int px(double v, int max) => Math.Clamp((int)(v * max), 0, max - 1);
            stroke.Begin(new PixelStrokeStyle(ColorRgb.Parse(colour), Size: Math.Max(1, (int)(size / 4))), px(path[0].X, w), px(path[0].Y, h), layer);
            foreach (var p in path.Skip(1)) stroke.Add(px(p.X, w), px(p.Y, h));
            stroke.End();
            return;
        }
        var engine = new StrokeEngine(doc);
        engine.Begin(new StrokeStyle(new BrushSettings { Size = size, Hardness = 0.7 }, PressureCurve.Normal, ColorRgb.Parse(colour), PaintMode.Paint),
            new StrokeSample(path[0].X * w, path[0].Y * h), layer);
        for (int i = 1; i < path.Length; i++)
            for (int k = 1; k <= 8; k++)
            {
                double u = k / 8.0;
                engine.Add(new StrokeSample((path[i - 1].X + (path[i].X - path[i - 1].X) * u) * w, (path[i - 1].Y + (path[i].Y - path[i - 1].Y) * u) * h, 0.8f));
            }
        engine.End();
    }

    private static (double Avg, double Max) Each(int count, Action<int> action)
    {
        var times = new List<double>(count);
        var sw = new Stopwatch();
        for (int i = 0; i < count; i++)
        {
            sw.Restart();
            action(i);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        return (Math.Round(times.Average(), 3), Math.Round(times.Max(), 3));
    }

    private static double Time(Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        return Math.Round(sw.Elapsed.TotalMilliseconds, 1);
    }

    /// <summary>Cost of one pen event (2 coalesced samples, 24 px ink) while drawing on the current frame's cel.</summary>
    private static double StrokeOnCel(Document doc)
    {
        var engine = new StrokeEngine(doc);
        var style = new StrokeStyle(new BrushSettings { Size = Math.Max(2, doc.Width / 80.0), Hardness = 1 }, PressureCurve.Normal, ColorRgb.Parse("#3B2F2A"), PaintMode.Paint);
        double w = doc.Width, h = doc.Height;
        StrokeSample At(double t) => new(w * (0.1 + 0.8 * t), h * (0.5 + 0.3 * Math.Sin(t * 9)), 0.8f);
        engine.Begin(style, At(0));
        var times = new List<double>();
        var sw = new Stopwatch();
        for (int e = 0; e < 100; e++)
        {
            sw.Restart();
            engine.Add([At((2 * e + 1) / 200.0), At((2 * e + 2) / 200.0)]);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        engine.End();
        return Math.Round(times.Average(), 3);
    }

    [Fact]
    public void Measure_animation_documents()
    {
        Build(32, 32, 4, pixel: true); // warm up the JIT
        var results = new List<object>();
        foreach (var (w, h, frames, pixel) in new[] { (64, 64, 32, true), (256, 256, 60, true), (1920, 1080, 24, false) })
        {
            GC.Collect();
            long before = GC.GetTotalMemory(true);
            Document? doc = null;
            double build = Time(() => doc = Build(w, h, frames, pixel));
            GC.Collect();
            long managed = GC.GetTotalMemory(true) - before;
            long history = doc!.History.TotalBytes;
            int cels = doc.Layers.SelectMany(l => l.Track).Where(c => c is not null).Distinct().Count();

            // Frame switching as the timeline and playback do it: first visit (composited) and again (frame cache).
            var first = Each(frames, f => doc.SetCurrentFrame(f));
            var again = Each(frames, f => doc.SetCurrentFrame(f));
            var render = Each(frames, f => doc.RenderFrame(f));
            doc.SetCurrentFrame(frames / 2);
            var onion = Each(10, _ => OnionSkin.Build(doc, OnionSkinSettings.Default with { Before = 2, After = 2 }));
            double pen = StrokeOnCel(doc);

            double snapshot = 0, write = 0, load = 0;
            long bytes = 0;
            DnkSnapshot? snap = null;
            snapshot = Time(() => snap = DnkFile.Snapshot(doc));
            using (var ms = new MemoryStream())
            {
                write = Time(() => DnkFile.Write(snap!, ms));
                bytes = ms.Length;
                ms.Position = 0;
                Document? back = null;
                load = Time(() => back = DnkFile.Read(ms));
                Assert.Equal(frames, back!.FrameCount);
            }

            List<ExportFrame>? exported = null;
            double renderAll = Time(() => exported = AnimationExport.RenderFrames(doc, includePaper: true));
            double sheet = Time(() => AnimationExport.BuildSheet(exported!, new SpriteSheetOptions(SheetLayout.Grid, 8)));
            double gif = Time(() =>
            {
                using var ms = new MemoryStream();
                GifEncoder.Write(exported!, ms, new GifOptions(Dither: !pixel));
            });

            double budget = 1000.0 / 12;
            results.Add(new
            {
                document = $"{w}x{h}",
                frames,
                layers = doc.Layers.Count,
                style = pixel ? "pixel art" : "hand-drawn",
                drawings = cels,
                buildMs = build,
                memoryAfterDrawingMB = Math.Round(managed / 1048576.0, 1),
                ofWhichUndoHistoryMB = Math.Round(history / 1048576.0, 1),
                frameCacheMB = Math.Round(doc.FrameCacheBytes / 1048576.0, 1),
                frameSwitchFirstVisitAvgMs = first.Avg,
                frameSwitchFirstVisitMaxMs = first.Max,
                frameSwitchCachedAvgMs = again.Avg,
                frameSwitchCachedMaxMs = again.Max,
                renderFrameAvgMs = render.Avg,
                onionBuild2Before2AfterAvgMs = onion.Avg,
                penEventOnCelAvgMs = pen,
                saveSnapshotOnUiThreadMs = snapshot,
                saveWriteInBackgroundMs = write,
                dnkBytes = bytes,
                loadMs = load,
                exportRenderAllFramesMs = renderAll,
                exportSpriteSheetMs = sheet,
                exportGifMs = gif,
                playbackBudgetAt12FpsMs = Math.Round(budget, 1),
            });
            // Cached frame switching must fit easily within one frame at 12 fps.
            Assert.True(again.Avg < budget, $"{w}x{h}: {again.Avg} ms per cached frame switch");
        }

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
            note = "Engine only (no screen drawing). Frame switch = showing another frame in the document, as scrubbing and playback do.",
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
            File.WriteAllText(Path.Combine(outDir, "animation-performance.json"), json);
        }
    }
}
