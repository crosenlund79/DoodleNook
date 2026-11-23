using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DoodleNook.App.Editor;
using DoodleNook.App.Input;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 017 — a long drawing session in one window: does DoodleNook slow down, keep growing, or hold on to pictures
/// it has closed? Rounds of real strokes through the window in four workspaces, undo and redo, layers added and merged,
/// saves, autosaves and reopening the file. Measured, not just passed: <c>out/long-session.json</c>. Runs with the
/// benchmarks (its own CI step); <c>DN_SOAK_ROUNDS</c> makes it longer (the default is a short session).
/// </summary>
public sealed class LongSessionBenchmarks : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-soak-" + Guid.NewGuid().ToString("N"));

    public LongSessionBenchmarks() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static double HeapMB()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return Math.Round(GC.GetTotalMemory(true) / 1048576.0, 1);
    }

    private static int Listeners(object target, string eventName)
    {
        var field = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        return (field?.GetValue(target) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    private static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s[s.Count / 2];
    }

    private static (double X, double Y)[] Wave(int i, int width, int height)
    {
        var rng = new Random(i);
        double x0 = 60 + rng.NextDouble() * (width - 520), y0 = 60 + rng.NextDouble() * (height - 160);
        return Enumerable.Range(0, 16).Select(k => (x0 + k * 28.0, y0 + Math.Sin(k * 0.7 + i) * 40)).ToArray();
    }

    [AvaloniaFact]
    public async Task Measure_a_long_drawing_session()
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("DN_SOAK_ROUNDS"), out var r) ? Math.Max(12, r) : 16;
        const int strokesPerRound = 25;
        var kinds = new[] { WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.PixelArt, WorkspaceKind.QuickSketch };
        var w = Open(1440, 900, WorkspaceKind.Painting);
        var recovery = new RecoveryStore(Path.Combine(_dir, "recovery"));
        w.StartRecovery(recovery);
        var path = Path.Combine(_dir, "session.dnk");
        DnkFile.Save(new Document(1920, 1080, ColorRgb.White, "Background"), path);
        Assert.True(await w.OpenPathAsync(path));
        var closed = new List<WeakReference<Document>>();
        var heapAfterReopen = new List<double>();
        var log = new List<object>();
        var strokeRounds = new List<double>();
        int strokes = 0;
        double heapWarm = 0;
        object? listenersWarm = null;
        object Fanout() => new
        {
            stateChanged = Listeners(w.Session, "StateChanged"),
            documentReplaced = Listeners(w.Session, "DocumentReplaced"),
            historyChanged = Listeners(w.Session.Document, "HistoryChanged"),
            layersChanged = Listeners(w.Session.Document, "LayersChanged"),
        };
        var total = Stopwatch.StartNew();
        // Next to the other benchmarks' results (tests/DoodleNook.App.Tests/out, uploaded by CI).
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "DoodleNook.slnx"))) repo = repo.Parent;
        var outDir = repo is not null ? Path.Combine(repo.FullName, "tests", "DoodleNook.App.Tests", "out") : Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);
        var progress = Path.Combine(outDir, "long-session.progress.txt");
        File.WriteAllText(progress, "");

        for (int round = 0; round < rounds; round++)
        {
            var kind = kinds[round % kinds.Length];
            w.SetWorkspace(kind);
            w.Canvas.FitToView();
            Pump();
            if (kind == WorkspaceKind.PixelArt) w.Session.SelectTool(ToolKind.PixelPencil);
            else
            {
                w.Session.SelectTool(ToolKind.Brush);
                w.Session.UseBrush(new BrushSettings { Size = 18, Hardness = 0.7f, Opacity = 0.9f }, "soak");
            }
            w.Session.SetForeground(new ColorRgb((byte)(40 + round * 37 % 200), (byte)(90 + round * 53 % 150), (byte)(60 + round * 11 % 180)));

            // Each stroke goes through the canvas's pen input with all the window's listeners (as Mission 011's
            // benchmarks do), and its end runs the work it queues (history, panels). The screen catches up every five
            // strokes, timed separately. (Window mouse events in the headless test window repaint the whole window in
            // software for every event, 150-200 ms at this size, which would measure the test harness, not drawing.)
            var times = new List<double>();
            var frames = new List<double>();
            var sw = new Stopwatch();
            var input = w.Canvas.Input!;
            for (int s = 0; s < strokesPerRound; s++)
            {
                var pts = Wave(strokes++, w.Session.Document.Width, w.Session.Document.Height);
                var view = w.Canvas.View;
                PointerInput At(int i, bool down = true)
                {
                    var (vx, vy) = view.DocumentToView(pts[i].X, pts[i].Y);
                    return new PointerInput(7, PointerKind.Pen, vx, vy, (float)(0.4 + 0.5 * Math.Sin(Math.PI * i / (pts.Length - 1))), down, false, false, false, false);
                }
                sw.Restart();
                input.Press(At(0));
                for (int i = 1; i < pts.Length; i++) input.Move([At(i)]);
                input.Release(At(pts.Length - 1, down: false));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (s % 5 == 4)
                {
                    sw.Restart();
                    Pump();
                    frames.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            sw.Restart();
            for (int u = 0; u < 5; u++) w.Session.Undo();
            for (int u = 0; u < 5; u++) w.Session.Redo();
            Pump();
            double undoRedo = sw.Elapsed.TotalMilliseconds;

            // Layers come and go (the picture keeps about the same size of work).
            if (round % 4 == 0) w.Session.AddLayer();
            if (round % 4 == 2 && w.Session.Document.Layers.Count > 1) w.Session.MergeDown();

            double? save = null;
            if (round % 5 == 4)
            {
                sw.Restart();
                Assert.True(await w.SaveToPathAsync(path));
                save = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
                Assert.True(await w.AutosaveNowAsync() || !w.Session.Document.IsDirty);
            }
            if (round % 4 == 3)
            {
                // Reopen the saved file (as after a break): the old picture must be released.
                Assert.True(await w.SaveToPathAsync(path));
                closed.Add(new WeakReference<Document>(w.Session.Document));
                Assert.True(await w.OpenPathAsync(path));
                // Like for like: just reopened, no undo history, the same layers. Growth here would be a leak.
                heapAfterReopen.Add(HeapMB());
            }

            double median = Median(times);
            strokeRounds.Add(median);
            bool measureHeap = round % 5 == 3 || round == rounds - 1;
            double? heap = measureHeap ? HeapMB() : null;
            if (round == 3)
            {
                heapWarm = heap!.Value;
                listenersWarm = Fanout();
            }
            File.AppendAllText(progress, $"round {round} {kind} strokes {strokes} stroke {median:0.0} ms frame {Median(frames):0} ms heap {heap} at {total.Elapsed.TotalSeconds:0} s\n");
            log.Add(new
            {
                round,
                workspace = kind.ToString(),
                strokes,
                strokeMedianMs = Math.Round(median, 1),
                strokeMaxMs = Math.Round(times.Max(), 1),
                screenCatchUpMs = Math.Round(Median(frames), 0),
                undoRedo10Ms = Math.Round(undoRedo, 1),
                saveMs = save,
                layers = w.Session.Document.Layers.Count,
                undoEntries = w.Session.Document.History.UndoCount,
                heapMB = heap,
                workingSetMB = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1048576.0, 0),
            });
        }

        var listenersEnd = Fanout();
        // The end of the session: a small new picture. Everything from the session should be collectable.
        w.Session.Document.MarkSaved();
        closed.Add(new WeakReference<Document>(w.Session.Document));
        w.Session.ReplaceDocument(new Document(64, 64, ColorRgb.White, "Background"), null);
        await w.AutosaveNowAsync();
        Pump();
        HeapMB();
        int aliveRightAfter = closed.Count(c => c.TryGetTarget(out _));
        // A hidden workspace's dock keeps its last-rendered copy, and with it the picture it last showed, until the
        // workspace is shown again: visit each once, as you would, then count again.
        foreach (var kind in Enum.GetValues<WorkspaceKind>())
        {
            w.SetWorkspace(kind);
            Pump();
            Pump();
        }
        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        double heapEnd = HeapMB();
        int stillAlive = closed.Count(c => c.TryGetTarget(out _));

        double early = Median(strokeRounds.Skip(4).Take(8).ToList());
        double late = Median(strokeRounds.Skip(strokeRounds.Count - 8).ToList());
        var result = new
        {
            measuredAt = DateTimeOffset.UtcNow,
            os = Environment.OSVersion.ToString(),
            cpu = Environment.ProcessorCount + " logical processors",
            picture = "1920 × 1080, window 1440 × 900",
            rounds,
            strokes,
            minutes = Math.Round(total.Elapsed.TotalMinutes, 1),
            strokeMedianMs = new { early = Math.Round(early, 1), late = Math.Round(late, 1) },
            heapMB = new { warm = heapWarm, afterEachReopen = heapAfterReopen, endAfterNewPicture = heapEnd },
            listeners = new { warm = listenersWarm, end = listenersEnd },
            closedPictures = closed.Count,
            closedPicturesInMemoryRightAfter = aliveRightAfter,
            closedPicturesStillInMemory = stillAlive,
            rounds_ = log,
        };
        File.WriteAllText(Path.Combine(outDir, Environment.GetEnvironmentVariable("DN_SOAK_NAME") ?? "long-session.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

        // Only clear regressions fail: the numbers themselves are the result.
        // Closed pictures are released once each workspace has been shown (a hidden dock holds at most the one it last
        // showed): nothing may build up with the pictures opened.
        Assert.True(stillAlive <= 3, $"{stillAlive} of {closed.Count} closed pictures still in memory after visiting each workspace");
        Assert.Equal(JsonSerializer.Serialize(listenersWarm), JsonSerializer.Serialize(listenersEnd)); // no listener pile-up
        Assert.True(late <= early * 2 + 10, $"strokes slowed down over the session: {early:0.0} ms → {late:0.0} ms");
        Assert.True(heapAfterReopen[^1] <= heapAfterReopen[1] + 48,
            $"memory grew over the session: {heapAfterReopen[1]} MB after the second reopen, {heapAfterReopen[^1]} MB after the last");
        recovery.EndSession();
        w.Close();
    }
}
