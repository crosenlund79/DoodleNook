using System.Diagnostics;
using System.Text.Json;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Measures (does not guess) what Mission 010's tools cost in the engine, on 1920 × 1080 and 2560 × 1440 painted
/// pictures with six layers: Smudge, Clone and Repair strokes per pen event, Liquify per event and while the pen rests,
/// its memory, Apply and Cancel, and Perspective / Warp updates while dragging a handle. Results go to
/// tests/DoodleNook.Engine.Tests/out/finishing-performance.json. The assertions only catch gross regressions.
/// </summary>
[Collection(MeasurementCollection.Name)]
public class FinishingTouchesPerformanceTests
{
    private static Document Painted(int width, int height)
    {
        var doc = new Document(width, height, ColorRgb.White, "Sky");
        var colours = new[] { "#9CC07A", "#5F8FA8", "#E9A94D", "#C96F4A", "#8A6FB0", "#3B2F2A" };
        for (int i = 0; i < 6; i++)
        {
            var layer = i == 0 ? doc.ActiveLayer : doc.AddLayer($"Layer {i + 1}");
            var engine = new StrokeEngine(doc);
            var style = new StrokeStyle(new BrushSettings { Size = height / 3.0, Hardness = 0.4, Opacity = 0.85 }, PressureCurve.Normal,
                ColorRgb.Parse(colours[i]), PaintMode.Paint);
            engine.Begin(style, new StrokeSample(0, height * (0.2 + 0.6 * (i % 4) / 3.0)), layer);
            for (int k = 1; k <= 30; k++) engine.Add(new StrokeSample(width * k / 30.0, height * (0.5 + 0.35 * Math.Sin(k / 6.0 + i))));
            engine.End();
        }
        doc.SetActiveLayer(doc.Layers[3]);
        return doc;
    }

    private static StrokeSample At(Document d, double t) =>
        new(d.Width * 0.1 + d.Width * 0.8 * t, d.Height * 0.5 + d.Height * 0.3 * Math.Sin(t * Math.PI * 3), (float)(0.4 + 0.6 * Math.Sin(Math.PI * t)));

    /// <summary>A fast curve across the picture as 200 events of 2 coalesced samples (a 200 Hz pen): per-event times and End.</summary>
    private static object RetouchCost(Document doc, RetouchStyle style)
    {
        var r = new RetouchStroke(doc);
        var times = new List<double>();
        var sw = new Stopwatch();
        var whole = Stopwatch.StartNew();
        sw.Restart();
        r.Begin(style, At(doc, 0));
        double begin = sw.Elapsed.TotalMilliseconds;
        var batch = new StrokeSample[2];
        for (int e = 0; e < 200; e++)
        {
            batch[0] = At(doc, (2 * e + 1) / 400.0);
            batch[1] = At(doc, (2 * e + 2) / 400.0);
            sw.Restart();
            r.Add(batch);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        sw.Restart();
        r.End();
        double end = sw.Elapsed.TotalMilliseconds;
        doc.Undo();
        return new { beginMs = Math.Round(begin, 2), avgEventMs = Math.Round(times.Average(), 3), maxEventMs = Math.Round(times.Max(), 2), endMs = Math.Round(end, 2),
            wholeStrokeMs = Math.Round(whole.Elapsed.TotalMilliseconds, 1) };
    }

    [Fact]
    public void Measure_mission_010_tools()
    {
        var results = new Dictionary<string, object>();
        double worstEvent = 0;
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1440) })
        {
            var doc = Painted(w, h);
            string k = $"{w}x{h}";
            var curve = PressureCurve.Normal;
            foreach (var size in new[] { 40.0, 150.0 })
            {
                dynamic smudge = RetouchCost(doc, new RetouchStyle(RetouchKind.Smudge, size, 0.7, 0.4, curve));
                results[$"smudge {size:0}px {k}"] = smudge;
                worstEvent = Math.Max(worstEvent, (double)smudge.avgEventMs);
            }
            results[$"clone 60px, this layer {k}"] = RetouchCost(doc, new RetouchStyle(RetouchKind.Clone, 60, 1, 0.5, curve, Source: (-200, 0)));
            var sampleWatch = Stopwatch.StartNew();
            var seen = doc.Flatten();
            double flatten = sampleWatch.Elapsed.TotalMilliseconds;
            results[$"clone 60px, all layers {k}"] = new { flattenAtStrokeStartMs = Math.Round(flatten, 1),
                stroke = RetouchCost(doc, new RetouchStyle(RetouchKind.Clone, 60, 1, 0.5, curve, Source: (-200, 0), Sampled: seen)) };
            results[$"repair 24px, from surroundings {k}"] = RetouchCost(doc, new RetouchStyle(RetouchKind.Repair, 24, 1, 0.6, curve));
            results[$"repair 60px, with a source {k}"] = RetouchCost(doc, new RetouchStyle(RetouchKind.Repair, 60, 1, 0.6, curve, Source: (-200, 0)));

            foreach (var size in new[] { 150.0, 400.0 })
            {
                var sw = Stopwatch.StartNew();
                var l = LiquifySession.Begin(doc, doc.ActiveLayer, LiquifySampling.Smooth)!;
                double begin = sw.Elapsed.TotalMilliseconds;
                l.Size = size;
                l.Strength = 0.7;
                var times = new List<double>();
                l.BeginStroke(At(doc, 0).X, At(doc, 0).Y);
                for (int e = 0; e < 200; e++)
                {
                    var p = At(doc, (e + 1) / 200.0);
                    sw.Restart();
                    l.StrokeTo(p.X, p.Y, p.Pressure);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                l.EndStroke();
                l.Mode = LiquifyMode.Inflate;
                l.BeginStroke(w / 2.0, h / 2.0);
                sw.Restart();
                l.Hold(300); // 10 dabs
                double hold = sw.Elapsed.TotalMilliseconds / 10;
                l.EndStroke();
                long field = l.FieldBytes;
                sw.Restart();
                bool cancel = size == 400;
                if (cancel) l.Cancel();
                else l.Apply();
                double finish = sw.Elapsed.TotalMilliseconds;
                if (!cancel) doc.Undo();
                results[$"liquify push {size:0}px {k}"] = new
                {
                    beginMs = Math.Round(begin, 1), avgEventMs = Math.Round(times.Average(), 2), maxEventMs = Math.Round(times.Max(), 2),
                    inflateDabWhileRestingMs = Math.Round(hold, 2), fieldMemoryMB = Math.Round(field / 1048576.0, 1),
                    layerCopyMB = Math.Round(4.0 * w * h / 1048576, 1), finish = cancel ? "cancel" : "apply", finishMs = Math.Round(finish, 1),
                };
                worstEvent = Math.Max(worstEvent, times.Average());
            }

            // Perspective and Warp: dragging a handle of a large selection (each update draws the preview again).
            foreach (var (label, sel) in new[] { ("selection 800x500", new PixelRect(w / 4, h / 4, 800, 500)), ("whole layer", doc.Bounds) })
            {
                doc.SetSelection(SelectionMask.Rectangle(w, h, sel), "Select");
                var t = FloatingTransform.Begin(doc, doc.ActiveLayer)!;
                t.Interpolation = Interpolation.Bilinear;
                t.SetMode(TransformMode.Perspective);
                var c0 = t.Corners;
                var sw = new Stopwatch();
                List<double> Run(bool draft, Action<int> update)
                {
                    t.Draft = draft;
                    var times = new List<double>();
                    for (int i = 1; i <= 8; i++)
                    {
                        sw.Restart();
                        update(i);
                        times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    t.Draft = false;
                    return times;
                }
                void Corner(int i)
                {
                    var c = ((double X, double Y)[])c0.Clone();
                    c[0] = (c[0].X + 6 * i, c[0].Y + 4 * i);
                    t.SetCorners(c);
                }
                var perspDraft = Run(true, Corner);
                var persp = Run(false, Corner);
                t.SetMode(TransformMode.Warp);
                var g = t.Warp!;
                void Point(int i) => t.SetWarpPoint(5, g.Points[5].X + 5 * i, g.Points[5].Y - 3 * i);
                var warpDraft = Run(true, Point);
                var warp = Run(false, Point);
                sw.Restart();
                t.Commit();
                double commit = sw.Elapsed.TotalMilliseconds;
                doc.Undo();
                doc.Undo(); // the selection
                results[$"transform {label} {k}"] = new
                {
                    perspectiveWhileDraggingMs = Math.Round(perspDraft.Skip(1).Average(), 1), perspectiveFullPreviewMs = Math.Round(persp.Skip(1).Average(), 1),
                    warpWhileDraggingMs = Math.Round(warpDraft.Skip(1).Average(), 1), warpFullPreviewMs = Math.Round(warp.Skip(1).Average(), 1),
                    applyMs = Math.Round(commit, 1),
                };
            }
        }
        Save(results);
        Assert.True(worstEvent < 60, $"a pen event took {worstEvent} ms on average");
    }

    private static void Save(Dictionary<string, object> results)
    {
        var report = new
        {
            configuration =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            note = "Engine only (no screen drawing). Pen event = 2 coalesced samples of a fast curve across the picture; Liquify event = one move of a " +
                   "200-move curve. Six painted layers. Transform updates = drawing the preview again after a handle moves (a quick draft while dragging, the full preview when let go).",
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
            File.WriteAllText(Path.Combine(outDir, "finishing-performance.json"), json);
        }
    }
}
