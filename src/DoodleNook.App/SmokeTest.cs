using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Input;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App;

/// <summary>
/// "DoodleNook.exe --smoke-test &lt;folder&gt;": starts the real application window, drives it through its own input
/// path (pen-like and mouse strokes, eraser, undo/redo, save and reopen a PNG), saves a screenshot of the window and a
/// JSON report into the folder, then exits with code 0 (all checks passed) or 1. Used by CI on a Windows runner; it
/// proves the packaged app starts and works, not how the pen feels.
/// </summary>
internal static class SmokeTest
{
    /// <summary>Process exit code chosen by the smoke test (Avalonia resets Environment.ExitCode on shutdown).</summary>
    public static int? ExitCode { get; private set; }

    public static void Attach(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var report = new SortedDictionary<string, object?>();
        var checks = new List<object>();
        report["checks"] = checks;
        var watchdog = new Timer(_ =>
        {
            report["error"] = "timed out after 120 s";
            Write(directory, report);
            Environment.Exit(2);
        }, null, TimeSpan.FromSeconds(120), Timeout.InfiniteTimeSpan);

        window.Opened += async (_, _) =>
        {
            bool ok = false;
            try
            {
                await Task.Delay(1500); // let a few frames render
                ok = await RunAsync(window, directory, report, checks);
            }
            catch (Exception ex)
            {
                report["error"] = ex.ToString();
            }
            report["passed"] = ok;
            Write(directory, report);
            watchdog.Dispose();
            ExitCode = ok ? 0 : 1;
            window.Close();
        };
    }

    private static void Write(string directory, object report) =>
        File.WriteAllText(Path.Combine(directory, "smoke-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

    private static async Task<bool> RunAsync(MainWindow window, string dir, SortedDictionary<string, object?> report, List<object> checks)
    {
        bool all = true;
        void Check(string name, bool passed, object? detail = null)
        {
            checks.Add(new { name, passed, detail });
            all &= passed;
        }

        var session = window.Session;
        var canvas = window.Canvas;
        var input = canvas.Input!;
        var doc = session.Document;

        report["os"] = Environment.OSVersion.ToString();
        report["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        report["version"] = MainWindow.Version;
        report["renderScaling"] = window.RenderScaling;
        report["clientSize"] = $"{window.ClientSize.Width} x {window.ClientSize.Height}";
        report["screens"] = window.Screens.All.Select(s => $"{s.Bounds.Width}x{s.Bounds.Height} @ {s.Scaling * 100:0}%").ToArray();
        report["document"] = $"{doc.Width} x {doc.Height}";
        report["penWindowSetup"] = window.PenSetupStatus;
        Check("window opened with a canvas", canvas.Bounds.Width > 100 && canvas.Bounds.Height > 100, $"{canvas.Bounds.Width:0} x {canvas.Bounds.Height:0}");

        (double X, double Y) V(double x, double y) => canvas.View.DocumentToView(x, y);
        PointerInput Pen(double dx, double dy, float p, bool down = true)
        {
            var (x, y) = V(dx, dy);
            return new PointerInput(7, PointerKind.Pen, x, y, p, Primary: down);
        }

        // 1. A pen stroke with a pressure swell, sent in coalesced batches like Windows Ink delivers them.
        session.SelectTool(ToolKind.Brush);
        session.SetSize(24);
        var sw = Stopwatch.StartNew();
        input.Press(Pen(300, 300, 0.1f));
        var batch = new List<PointerInput>();
        for (int i = 1; i <= 400; i++)
        {
            double t = i / 400.0;
            batch.Add(Pen(300 + 1200 * t, 300 + 400 * Math.Sin(t * Math.PI), (float)Math.Sin(t * Math.PI) * 0.9f + 0.1f));
            if (batch.Count == 3)
            {
                input.Move(batch.ToArray());
                batch.Clear();
            }
        }
        input.Move(batch.ToArray());
        input.Release(Pen(1500, 300, 0f, down: false));
        sw.Stop();
        report["penStroke400SamplesMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
        Check("pen stroke paints pixels", Pixel.A(doc.Layer[900, 700]) > 0 || Pixel.A(doc.Layer[900, 699]) > 0);
        Check("pen stroke is one undo step", doc.History.UndoCount == 1);
        Check("light pressure gives a thinner line", Width(doc, 330) < Width(doc, 900), $"{Width(doc, 330)} vs {Width(doc, 900)} px");

        // 2. A mouse stroke (constant pressure).
        var mouseStart = V(200, 900);
        var mouseEnd = V(1700, 900);
        input.Press(new PointerInput(1, PointerKind.Mouse, mouseStart.X, mouseStart.Y, 0.5f, Primary: true));
        input.Move([new PointerInput(1, PointerKind.Mouse, mouseEnd.X, mouseEnd.Y, 0.5f, Primary: true)]);
        input.Release(new PointerInput(1, PointerKind.Mouse, mouseEnd.X, mouseEnd.Y, 0.5f, Primary: false));
        Check("mouse stroke paints a continuous line", Enumerable.Range(220, 1460).All(x => Pixel.A(doc.Layer[x, 900]) > 0));

        // 3. Eraser across the mouse line.
        uint beforeErase = doc.Layer[950, 900];
        session.SelectTool(ToolKind.Eraser);
        input.Press(Pen(950, 800, 1f));
        input.Move([Pen(950, 1000, 1f)]);
        input.Release(Pen(950, 1000, 0f, down: false));
        Check("eraser makes pixels transparent", doc.Layer[950, 900] == Pixel.Transparent);

        // 4. Undo / redo.
        session.Undo();
        Check("undo restores erased pixels", doc.Layer[950, 900] == beforeErase && beforeErase != Pixel.Transparent);
        session.Redo();
        Check("redo erases again", doc.Layer[950, 900] == Pixel.Transparent);
        Check("history has three steps", doc.History.UndoCount == 3);
        session.SelectTool(ToolKind.Brush);

        // 5. Save as PNG and reopen.
        var path = Path.Combine(dir, "smoke-drawing.png");
        bool saved = await window.SaveToPathAsync(path);
        Check("PNG saved", saved && File.Exists(path), File.Exists(path) ? new FileInfo(path).Length : 0);
        Check("document is clean after saving", !doc.IsDirty);
        var reopened = PngFile.Open(path);
        Check("reopened PNG matches the picture", reopened.Layer.Pixels.AsSpan().SequenceEqual(doc.Flatten().Pixels));

        // 6. Let the window render the result, then capture it.
        await Task.Delay(800);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        report["canvasFramesFromGpuTexture"] = canvas.GpuFrames;
        report["canvasFramesFromMemory"] = canvas.SoftwareFrames;
        double scaling = window.RenderScaling;
        var size = new PixelSize((int)Math.Round(window.ClientSize.Width * scaling), (int)Math.Round(window.ClientSize.Height * scaling));
        using (var rtb = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling)))
        {
            rtb.Render(window);
            rtb.Save(Path.Combine(dir, "smoke-window.png"), new PngBitmapEncoderOptions());
        }
        report["uploadedPixels"] = canvas.UploadedPixels;
        Check("screenshot written", File.Exists(Path.Combine(dir, "smoke-window.png")));
        return all;
    }

    private static int Width(Engine.Documents.Document doc, int x) =>
        Enumerable.Range(0, doc.Height).Count(y => Pixel.A(doc.Layer[x, y]) > 0);
}
