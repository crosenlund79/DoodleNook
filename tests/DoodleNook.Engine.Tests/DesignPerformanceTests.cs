using System.Diagnostics;
using System.Text.Json;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Measures (does not guess) what Mission 008 costs: pen events while groups, masks and adjustment layers are in the
/// picture (next to the same picture without them), whole-picture compositing, dragging an editable shape, and saving
/// and opening a version 4 file. Results go to tests/DoodleNook.Engine.Tests/out/design-performance.json. Assertions are
/// generous: they only catch gross regressions.
/// </summary>
[Collection(MeasurementCollection.Name)]
public class DesignPerformanceTests
{
    private static Document Picture(int width, int height, bool designed)
    {
        var doc = new Document(width, height, ColorRgb.White, "Background");
        var colours = new[] { "#9CC07A", "#5F8FA8", "#E9A94D", "#C96F4A", "#8A6FB0", "#3B2F2A" };
        var layers = new List<Layer> { doc.ActiveLayer };
        for (int i = 0; i < 6; i++)
        {
            var layer = i == 0 ? doc.ActiveLayer : doc.AddLayer($"Layer {i + 1}");
            if (i > 0) layers.Add(layer);
            var engine = new StrokeEngine(doc);
            var style = new StrokeStyle(new BrushSettings { Size = height / 3.0, Hardness = 0.4, Opacity = 0.8 }, PressureCurve.Normal,
                ColorRgb.Parse(colours[i]), PaintMode.Paint);
            engine.Begin(style, new StrokeSample(0, height * (0.2 + 0.6 * (i % 4) / 3.0)), layer);
            for (int k = 1; k <= 30; k++) engine.Add(new StrokeSample(width * k / 30.0, height * (0.5 + 0.35 * Math.Sin(k / 6.0 + i))));
            engine.End();
        }
        if (designed)
        {
            // Three layers in a group (with its own opacity), a mask on one of them, an adjustment layer over everything.
            var group = doc.GroupLayers([layers[2], layers[3], layers[4]], "Character");
            doc.SetLayerProperties(group, group.Properties with { Opacity = 0.9 });
            doc.AddMask(layers[3]);
            doc.SetEditingMask(false);
            doc.SetActiveLayer(layers[5]);
            doc.AddAdjustmentLayer(new HueSaturation(10, 15, 0));
            doc.AddShapeLayer(new ShapeContent(ShapeType.RoundedRectangle, width * 0.3, height * 0.3, width * 0.3, height * 0.15, ColorRgb.Parse("#E9A94D"),
                ColorRgb.Parse("#2B2230"), 6, 0, 30, Shadow: new ShadowEffect(ColorRgb.Black, 0, 8, 8, 0.4)));
        }
        // Paint on a layer inside the group when there is one.
        doc.SetActiveLayer(designed ? layers[3] : layers[3]);
        return doc;
    }

    /// <summary>A fast 2000 px curve as 200 events of 2 coalesced samples (how a 200 Hz pen delivers it), compositing included.</summary>
    private static (double Avg, double Max) StrokeCost(Document doc)
    {
        var engine = new StrokeEngine(doc);
        var style = new StrokeStyle(new BrushSettings { Size = 24, Hardness = 0.8, Opacity = 1 }, PressureCurve.Normal, ColorRgb.Parse("#3B2F2A"), PaintMode.Paint);
        double w = doc.Width, h = doc.Height;
        StrokeSample At(double t) => new(w * 0.1 + w * 0.8 * t, h * 0.5 + h * 0.3 * Math.Sin(t * Math.PI * 3), (float)(0.3 + 0.7 * Math.Sin(Math.PI * t)));
        var times = new List<double>(200);
        var sw = new Stopwatch();
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
        return (Math.Round(times.Average(), 3), Math.Round(times.Max(), 3));
    }

    private static double Time(Action a, int repeat = 1)
    {
        a(); // warm up
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < repeat; i++) a();
        return Math.Round(sw.Elapsed.TotalMilliseconds / repeat, 2);
    }

    [Fact]
    public void Measure_the_designers_toolkit()
    {
        var results = new List<object>();
        foreach (var (name, w, h) in new[] { ("1920x1080", 1920, 1080), ("2560x1440", 2560, 1440) })
        {
            var plain = Picture(w, h, designed: false);
            var designed = Picture(w, h, designed: true);
            StrokeCost(plain); // warm up
            var (plainAvg, plainMax) = StrokeCost(plain);
            var (designAvg, designMax) = StrokeCost(designed);
            double plainFull = Time(() => plain.RecompositeAll(notify: false), 3);
            double designFull = Time(() => designed.RecompositeAll(notify: false), 3);

            // Dragging an editable shape: one preview per pointer event.
            var shape = designed.Layers.First(l => l.Kind == LayerKind.Shape);
            var start = (ShapeContent)shape.Content!;
            int step = 0;
            double drag = Time(() => designed.PreviewContent(shape, start with { X = start.X + ++step * 3, Y = start.Y + step }), 20);

            // Saving and opening a version 4 file (in memory).
            var ms = new MemoryStream();
            double save = Time(() =>
            {
                ms.SetLength(0);
                DnkFile.Write(designed, ms);
            });
            double open = Time(() =>
            {
                ms.Position = 0;
                DnkFile.Read(ms);
            });

            results.Add(new
            {
                canvas = name,
                penEventPlainMs = plainAvg,
                penEventPlainMaxMs = plainMax,
                penEventWithGroupMaskAdjustmentMs = designAvg,
                penEventWithGroupMaskAdjustmentMaxMs = designMax,
                fullCompositePlainMs = plainFull,
                fullCompositeWithGroupMaskAdjustmentMs = designFull,
                shapeDragPreviewMs = drag,
                saveVersion4Ms = save,
                openVersion4Ms = open,
                fileKB = ms.Length / 1024,
            });
            Assert.True(designAvg < 50, $"{name}: {designAvg} ms per pen event with a group, mask and adjustment layer");
            Assert.True(drag < 250, $"{name}: {drag} ms per shape drag step");
        }

        var report = new
        {
            machine = Environment.MachineName.Length > 0 ? "test runner" : "",
            os = Environment.OSVersion.ToString(),
            cores = Environment.ProcessorCount,
            configuration =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            note = "Engine only (no screen drawing). Pen event = 2 coalesced samples of a fast 2000 px curve; compositing of the changed area included. " +
                   "Designed picture: 6 painted layers, 3 of them in a group at 90% with a mask on one, a Hue/Saturation adjustment layer and an editable shape with a shadow.",
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
            File.WriteAllText(Path.Combine(outDir, "design-performance.json"), json);
        }
    }
}
