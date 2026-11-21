using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using DoodleNook.App.Editor;
using DoodleNook.App.Input;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 011 (Under the Hood): measurements of the application around the engine, in the real window on Avalonia's
/// headless platform (Skia drawing in memory, no GPU): how many listeners react to every change of tool, size or colour,
/// and whether that grows the longer DoodleNook runs; frame times with and without a selection; pen events through the
/// canvas input on a large layered picture; undo; replacing documents; a Game Edit catalog with hundreds of PNGs.
/// Uses only what existed in Mission 010 (and reflection to count event listeners), so it measures before and after.
/// Results go to tests/DoodleNook.App.Tests/out/under-the-hood-app.json. No timing assertions.
/// </summary>
public class UnderTheHoodAppBenchmarks
{
    private static int Listeners(object target, string eventName)
    {
        var field = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        return (field?.GetValue(target) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    private static double HeapMB()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return Math.Round(GC.GetTotalMemory(true) / 1048576.0, 1);
    }

    private static object Stats(List<double> times)
    {
        var s = times.OrderBy(t => t).ToList();
        double P(double q) => s[Math.Clamp((int)Math.Ceiling(q * s.Count) - 1, 0, s.Count - 1)];
        return new { avg = Math.Round(times.Average(), 3), p50 = Math.Round(P(0.5), 3), p95 = Math.Round(P(0.95), 3), max = Math.Round(s[^1], 3) };
    }

    /// <summary>The average time of one size change (what dragging the size slider or the quick palette's centre does).</summary>
    private static double SizeChange(MainWindow w)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) w.Session.SetSize(10 + i % 40);
        double ms = sw.Elapsed.TotalMilliseconds / 200;
        Pump();
        return Math.Round(ms, 4);
    }

    private static object Fanout(MainWindow w) => new
    {
        stateChangedListeners = Listeners(w.Session, "StateChanged"),
        libraryChangedListeners = Listeners(w.Session, "LibraryChanged"),
        documentReplacedListeners = Listeners(w.Session, "DocumentReplaced"),
        sizeChangeMs = SizeChange(w),
    };

    private static Document Layered(int width, int height, int layers)
    {
        var doc = new Document(width, height, ColorRgb.White, "Sketch");
        var colours = new[] { "#9CC07A", "#5F8FA8", "#E9A94D", "#C96F4A", "#8A6FB0", "#3B2F2A" };
        for (int i = 0; i < layers; i++)
        {
            var layer = i == 0 ? doc.ActiveLayer : doc.AddLayer($"Layer {i + 1}");
            doc.SetLayerProperties(layer, layer.Properties with { Opacity = i % 3 == 1 ? 0.7 : 1, Blend = i % 4 == 2 ? BlendMode.Multiply : BlendMode.Normal });
            var engine = new StrokeEngine(doc);
            var style = new StrokeStyle(new BrushSettings { Size = height / 3.0, Hardness = 0.4, Opacity = 0.8 }, PressureCurve.Normal,
                ColorRgb.Parse(colours[i % colours.Length]), PaintMode.Paint);
            engine.Begin(style, new StrokeSample(0, height * (0.2 + 0.6 * (i % 4) / 3.0)), layer);
            for (int k = 1; k <= 40; k++) engine.Add(new StrokeSample(width * k / 40.0, height * (0.5 + 0.35 * Math.Sin(k / 6.0 + i))));
            engine.End();
        }
        doc.SetActiveLayer(doc.Layers[layers / 2]);
        doc.MarkSaved();
        return doc;
    }

    private static double FrameMs(MainWindow w, int frames = 8)
    {
        Pump();
        w.CaptureRenderedFrame()?.Dispose();
        var times = new List<double>();
        var sw = new Stopwatch();
        for (int i = 0; i < frames; i++)
        {
            w.Canvas.InvalidateVisual();
            sw.Restart();
            w.CaptureRenderedFrame()?.Dispose();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return Math.Round(times[times.Count / 2], 1);
    }

    /// <summary>A pen stroke through the canvas input (200 events of 2 samples), with all the window's listeners; then Pump.</summary>
    private static object PenStroke(MainWindow w, double size, double hardness)
    {
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.ApplyPreset("soft");
        w.Session.SetSize(size);
        w.Session.SetHardness(hardness);
        Pump();
        var doc = w.Session.Document;
        var view = w.Canvas.View;
        PointerInput At(double t, bool down = true)
        {
            double x = doc.Width * 0.1 + doc.Width * 0.8 * t, y = doc.Height * 0.5 + doc.Height * 0.3 * Math.Sin(t * Math.PI * 3);
            var (vx, vy) = view.DocumentToView(x, y);
            return new PointerInput(7, PointerKind.Pen, vx, vy, (float)(0.3 + 0.7 * Math.Sin(Math.PI * t)), down, false, false, false, false);
        }
        var input = w.Canvas.Input!;
        var times = new List<double>();
        var sw = new Stopwatch();
        long alloc = GC.GetAllocatedBytesForCurrentThread();
        input.Press(At(0));
        var batch = new PointerInput[2];
        for (int e = 0; e < 200; e++)
        {
            batch[0] = At((2 * e + 1) / 400.0);
            batch[1] = At((2 * e + 2) / 400.0);
            sw.Restart();
            input.Move(batch);
            times.Add(sw.Elapsed.TotalMilliseconds);
            if (e % 4 == 3) Pump(); // the screen catches up every few events, as it would at 60 Hz
        }
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        sw.Restart();
        input.Release(At(1, down: false));
        double release = sw.Elapsed.TotalMilliseconds;
        Pump();
        sw.Restart();
        w.Undo();
        double undo = sw.Elapsed.TotalMilliseconds;
        Pump();
        sw.Restart();
        w.Session.Redo();
        double redo = sw.Elapsed.TotalMilliseconds;
        Pump();
        w.Undo();
        Pump();
        return new { perEvent = Stats(times), releaseMs = Math.Round(release, 1), undoMs = Math.Round(undo, 1), redoMs = Math.Round(redo, 1),
            allocatedKBPerEvent = Math.Round(alloc / 200.0 / 1024, 1) };
    }

    [AvaloniaFact]
    public async Task Measure_application_responsiveness()
    {
        var results = new Dictionary<string, object>();
        double heap0 = HeapMB();
        var sw = Stopwatch.StartNew();
        var w = Open(1280, 800, WorkspaceKind.Painting);
        results["start window (Painting)"] = new { firstWindowMs = Math.Round(sw.Elapsed.TotalMilliseconds, 0), heapMB = HeapMB() - heap0 };

        // 1. Who listens to every change, and does it grow with use?
        results["listeners · at start"] = Fanout(w);
        var tools = w.CurrentTools;
        for (int i = 0; i < 120; i++)
        {
            w.Session.SelectTool(tools[i % tools.Length]);
            Pump();
        }
        w.Session.SelectTool(ToolKind.Brush);
        Pump();
        double heapTools = HeapMB();
        results["listeners · after 120 tool changes"] = Fanout(w);
        var kinds = Enum.GetValues<WorkspaceKind>();
        var switches = new List<double>();
        for (int round = 0; round < 3; round++)
            foreach (var k in kinds)
            {
                sw.Restart();
                w.SetWorkspace(k);
                Pump();
                switches.Add(sw.Elapsed.TotalMilliseconds);
            }
        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        results["workspace switch (each of 6, 3 rounds)"] = Stats(switches);
        results["listeners · after 18 workspace switches"] = Fanout(w);
        for (int i = 0; i < 60; i++)
        {
            w.Session.SelectTool(tools[i % tools.Length]);
            Pump();
        }
        w.Session.SelectTool(ToolKind.Brush);
        Pump();
        results["listeners · after 60 more tool changes"] = Fanout(w);
        results["managed heap growth over 180 tool changes"] = Math.Round(HeapMB() - heapTools, 1);

        // 2. Frame times (software Skia, 1280 × 800 window, 1920 × 1080 picture with three layers).
        w.Session.ReplaceDocument(Layered(1920, 1080, 3), null);
        w.Canvas.FitToView();
        Pump();
        double plain = FrameMs(w);
        var doc = w.Session.Document;
        doc.SetSelection(SelectionMask.Rectangle(doc.Width, doc.Height, new PixelRect(300, 200, 800, 500)), "Select");
        Pump();
        double rect = FrameMs(w);
        doc.SetSelection(Selections.Ellipse(doc, 300, 200, 1500, 900, antialias: true), "Select");
        Pump();
        double ellipse = FrameMs(w);
        doc.SetSelection(null, "Deselect");
        Pump();
        results["frame time · 1920x1080 picture in a 1280x800 window"] = new { plainMs = plain, rectangleSelectionMs = rect, ellipseSelectionMs = ellipse };

        // 3. Pen events through the canvas input, and undo, on a 2560 × 1440 picture with 12 layers.
        w.Session.ReplaceDocument(Layered(2560, 1440, 12), null);
        w.Canvas.FitToView();
        Pump();
        results["pen stroke in the window · 2560x1440, 12 layers · 24 px"] = PenStroke(w, 24, 0.9);
        results["pen stroke in the window · 2560x1440, 12 layers · 300 px soft"] = PenStroke(w, 300, 0.05);
        w.Session.ReplaceDocument(Layered(1920, 1080, 3), null);
        w.Canvas.FitToView();
        Pump();
        results["pen stroke in the window · 1920x1080, 3 layers · 24 px"] = PenStroke(w, 24, 0.9);

        // 4. Replacing the document many times: is the old one released?
        double heapDocs0 = HeapMB();
        for (int i = 0; i < 12; i++)
        {
            w.Session.ReplaceDocument(Layered(1920, 1080, 2), null);
            Pump();
        }
        w.Session.ReplaceDocument(new Document(800, 600, ColorRgb.White), null);
        Pump();
        double heapDocs12 = HeapMB();
        for (int i = 0; i < 12; i++)
        {
            w.Session.ReplaceDocument(Layered(1920, 1080, 2), null);
            Pump();
        }
        w.Session.ReplaceDocument(new Document(800, 600, ColorRgb.White), null);
        Pump();
        results["documents replaced · managed heap change"] = new
        {
            after12MB = Math.Round(heapDocs12 - heapDocs0, 1), after24MB = Math.Round(HeapMB() - heapDocs0, 1), listeners = Fanout(w),
        };

        // 5. Game Edit Mode with 300 PNGs.
        var game = Path.Combine(Path.GetTempPath(), "dn-game-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            for (int i = 0; i < 300; i++)
            {
                int size = i % 10 == 0 ? 512 : i % 3 == 0 ? 128 : 32;
                var img = new RasterImage(size, size);
                for (int p = 0; p < img.Pixels.Length; p++) img.Pixels[p] = Pixel.Opaque(new ColorRgb((byte)(p * 7 + i), (byte)(p / size), (byte)i));
                var folder = Path.Combine(game, "assets", $"set{i / 50}");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, $"sprite_{i:000}.png"), PngCodec.Encode(img));
            }
            double heapGame0 = HeapMB();
            sw.Restart();
            var opening = w.OpenGameFolderAsync(game);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!w.Dialogs.IsOpen && !opening.IsCompleted && DateTime.UtcNow < deadline)
            {
                Pump();
                await Task.Delay(5);
            }
            if (w.Dialogs.IsOpen) w.Dialogs.Close("start");
            Assert.True(await opening);
            Pump();
            double opened = sw.Elapsed.TotalMilliseconds;
            // Thumbnails arrive in the background. To time the window's share of the work, mark every PNG as changed and
            // load the thumbnails again, pumping the UI thread until all 300 have arrived.
            var catalog = typeof(MainWindow).GetProperty("Catalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            var thumbs = (System.Collections.IDictionary)catalog.GetType().GetField("_thumbs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(catalog)!;
            int Loaded(DateTime since)
            {
                lock (thumbs)
                {
                    int n = 0;
                    foreach (var v in thumbs.Values)
                        if (v is ValueTuple<DateTime, Avalonia.Media.Imaging.Bitmap> t && t.Item1 >= since) n++;
                    return n;
                }
            }
            var stamp = DateTime.UtcNow.AddMinutes(1);
            foreach (var png in Directory.EnumerateFiles(game, "*.png", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(png, stamp);
            var pumps = new List<double>();
            var pumpWatch = new Stopwatch();
            sw.Restart();
            catalog.GetType().GetMethod("LoadThumbnails")!.Invoke(catalog, null);
            deadline = DateTime.UtcNow.AddSeconds(60);
            while (Loaded(stamp) < 300 && DateTime.UtcNow < deadline)
            {
                pumpWatch.Restart();
                Pump();
                pumps.Add(pumpWatch.Elapsed.TotalMilliseconds);
                Thread.Sleep(2);
            }
            double reload = sw.Elapsed.TotalMilliseconds;
            results["game edit · 300 PNGs"] = new
            {
                openedWithAllThumbnailsMs = Math.Round(opened, 0), reloadAllThumbnailsMs = Math.Round(reload, 0), thumbnails = Loaded(stamp),
                uiThreadBusyWhileReloadingMs = Math.Round(pumps.Sum(), 0), longestUiPumpMs = Math.Round(pumps.DefaultIfEmpty(0).Max(), 1),
                heapMB = Math.Round(HeapMB() - heapGame0, 1),
            };
            w.LeaveGameEditMode();
            Pump();
        }
        finally
        {
            w.Session.Document.MarkSaved();
            w.Close();
            Pump();
            try { Directory.Delete(game, true); } catch (IOException) { }
        }

        var json = JsonSerializer.Serialize(new
        {
            measuredAt = DateTimeOffset.UtcNow, os = Environment.OSVersion.ToString(), cpu = Environment.ProcessorCount + " logical processors",
            configuration =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            note = "Headless Avalonia with Skia drawing in memory (no GPU). Times in milliseconds.",
            results,
        }, new JsonSerializerOptions { WriteIndented = true });
        TestContext.Current.TestOutputHelper?.WriteLine(json);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        if (dir is not null)
        {
            var outDir = Path.Combine(dir.FullName, "tests", "DoodleNook.App.Tests", "out");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, Environment.GetEnvironmentVariable("DN_BENCH_NAME") ?? "under-the-hood-app.json"), json);
        }
    }
}
