using System.Diagnostics;
using System.Text.Json;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Measures (does not guess) what the engine costs per pen event in realistic layered documents: brush work plus
/// recompositing every visible layer under the brush. Results go to tests/DoodleNook.Engine.Tests/out/performance.json
/// and the test log. The assertions are deliberately generous; they only catch gross regressions.
/// </summary>
[Collection(MeasurementCollection.Name)]
public class PerformanceTests
{
    private static Document Layered(int width, int height, int layers)
    {
        var doc = new Document(width, height, ColorRgb.White, "Sketch");
        var colours = new[] { "#9CC07A", "#5F8FA8", "#E9A94D", "#C96F4A", "#8A6FB0", "#3B2F2A" };
        for (int i = 0; i < layers; i++)
        {
            var layer = i == 0 ? doc.ActiveLayer : doc.AddLayer($"Layer {i + 1}");
            var props = layer.Properties with
            {
                Opacity = i % 3 == 1 ? 0.7 : 1,
                Blend = i % 4 == 2 ? BlendMode.Multiply : BlendMode.Normal,
                ClipToBelow = i % 5 == 4,
            };
            doc.SetLayerProperties(layer, props);
            // Each layer gets broad paint across most of the picture, so compositing has real work everywhere.
            var engine = new StrokeEngine(doc);
            var style = new StrokeStyle(new BrushSettings { Size = height / 3.0, Hardness = 0.4, Opacity = 0.8 }, PressureCurve.Normal,
                ColorRgb.Parse(colours[i % colours.Length]), PaintMode.Paint);
            engine.Begin(style, new StrokeSample(0, height * (0.2 + 0.6 * (i % 4) / 3.0)), layer);
            for (int k = 1; k <= 40; k++)
                engine.Add(new StrokeSample(width * k / 40.0, height * (0.5 + 0.35 * Math.Sin(k / 6.0 + i))));
            engine.End();
        }
        doc.SetActiveLayer(doc.Layers[layers / 2]);
        return doc;
    }

    /// <summary>A fast 2000 px curve as 200 events of 2 coalesced samples (how a 200 Hz pen delivers it).</summary>
    private static (double Avg, double Max) StrokeCost(Document doc, BrushSettings brush)
    {
        var engine = new StrokeEngine(doc);
        var style = new StrokeStyle(brush, PressureCurve.Normal, ColorRgb.Parse("#3B2F2A"), PaintMode.Paint);
        double w = doc.Width, h = doc.Height;
        StrokeSample At(double t) => new(w * 0.1 + w * 0.8 * t, h * 0.5 + h * 0.3 * Math.Sin(t * Math.PI * 3), (float)(0.3 + 0.7 * Math.Sin(Math.PI * t)));
        var times = new List<double>(200);
        var sw = new Stopwatch();
        engine.Begin(style, At(0));
        Span<StrokeSample> batch = stackalloc StrokeSample[2];
        for (int e = 0; e < 200; e++)
        {
            batch[0] = At((2 * e + 1) / 400.0);
            batch[1] = At((2 * e + 2) / 400.0);
            sw.Restart();
            engine.Add(batch);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        engine.End();
        return (times.Average(), times.Max());
    }

    private static double Time(Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        return sw.Elapsed.TotalMilliseconds;
    }

    [Fact]
    public void Measure_pen_event_cost_in_layered_documents()
    {
        var results = new List<object>();
        // Warm up the JIT so the first measurement isn't a compile.
        StrokeCost(Layered(400, 300, 2), new BrushSettings { Size = 20 });

        foreach (var (w, h, layers) in new[] { (1920, 1080, 1), (1920, 1080, 6), (2560, 1440, 12) })
        {
            var doc = Layered(w, h, layers);
            foreach (var (name, brush) in new (string, BrushSettings)[]
            {
                ("6 px pencil", new BrushSettings { Size = 6, Hardness = 0.85, PressureSize = true, Grain = 0.55 }),
                ("24 px ink", new BrushSettings { Size = 24, Hardness = 1, PressureSize = true }),
                ("120 px hard", new BrushSettings { Size = 120, Hardness = 0.98 }),
                ("300 px soft", new BrushSettings { Size = 300, Hardness = 0.05, Opacity = 0.6, PressureOpacity = true }),
            })
            {
                var (avg, max) = StrokeCost(doc, brush);
                results.Add(new { document = $"{w}x{h}", layers, brush = name, avgMsPerEvent = Math.Round(avg, 3), maxMsPerEvent = Math.Round(max, 3) });
                if (name == "24 px ink") Assert.True(avg < 50, $"{w}x{h}/{layers} layers: {avg:0.00} ms per event");
            }
            double recomposite = Time(() => doc.RecompositeAll(notify: false));
            double undo = Time(() => doc.Undo());
            double redo = Time(() => doc.Redo());
            using var ms = new MemoryStream();
            double save = Time(() => DnkFile.Write(doc, ms));
            ms.Position = 0;
            Document? back = null;
            double load = Time(() => back = DnkFile.Read(ms));
            Assert.Equal(doc.Layers.Count, back!.Layers.Count);
            results.Add(new
            {
                document = $"{w}x{h}",
                layers,
                fullRecompositeMs = Math.Round(recomposite, 1),
                undoLastStrokeMs = Math.Round(undo, 2),
                redoLastStrokeMs = Math.Round(redo, 2),
                dnkSaveMs = Math.Round(save, 0),
                dnkLoadMs = Math.Round(load, 0),
                dnkBytes = ms.Length,
                undoHistoryBytes = doc.History.TotalBytes,
            });
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
            note = "Engine only: brush work plus recompositing all visible layers under the brush; excludes drawing to the screen.",
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
            File.WriteAllText(Path.Combine(outDir, "performance.json"), json);
        }
    }
}
