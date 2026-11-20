using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Input;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App;

/// <summary>
/// "DoodleNook.exe --smoke-test &lt;folder&gt;": starts the real application window, drives it through its own input
/// path (pen-like and mouse strokes, eraser, undo/redo, layers, every workspace, a transform, pixel art, saving and
/// reopening a .dnk, PNG/JPEG/WebP export), saves screenshots and a JSON report into the folder, then exits with code 0 (all checks passed) or 1. Used by CI on a Windows runner; it
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
        report["startWorkspace"] = window.Workspace.Name;
        Check("window opened with a canvas", canvas.Bounds.Width > 100 && canvas.Bounds.Height > 100, $"{canvas.Bounds.Width:0} x {canvas.Bounds.Height:0}");

        (double X, double Y) V(double x, double y) => canvas.View.DocumentToView(x, y);
        PointerInput Pen(double dx, double dy, float p, bool down = true)
        {
            var (x, y) = V(dx, dy);
            return new PointerInput(7, PointerKind.Pen, x, y, p, Primary: down);
        }
        PointerInput Mouse(double dx, double dy, bool down = true)
        {
            var (x, y) = V(dx, dy);
            return new PointerInput(1, PointerKind.Mouse, x, y, 0.5f, Primary: down);
        }
        void Drag(double x0, double y0, double x1, double y1)
        {
            input.Press(Mouse(x0, y0));
            input.Move([Mouse((x0 + x1) / 2, (y0 + y1) / 2), Mouse(x1, y1)]);
            input.Release(Mouse(x1, y1, down: false));
        }
        async Task Shot(string name)
        {
            await Task.Delay(500);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            double scaling = window.RenderScaling;
            var size = new PixelSize((int)Math.Round(window.ClientSize.Width * scaling), (int)Math.Round(window.ClientSize.Height * scaling));
            using var rtb = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            rtb.Render(window);
            rtb.Save(Path.Combine(dir, name), new PngBitmapEncoderOptions());
        }

        // 1. Quick Sketch: a pen stroke with a pressure swell, sent in coalesced batches like Windows Ink delivers them.
        window.SetWorkspace(WorkspaceKind.QuickSketch);
        session.SelectTool(ToolKind.Brush);
        session.ApplyPreset("ink");
        session.SetSize(24);
        var sketch = doc.ActiveLayer;
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
        Check("pen stroke paints pixels", Pixel.A(sketch.Image[900, 700]) > 0 || Pixel.A(sketch.Image[900, 699]) > 0);
        Check("pen stroke is one undo step", doc.History.UndoCount == 1);
        Check("light pressure gives a thinner line", Width(sketch, doc.Height, 330) < Width(sketch, doc.Height, 900), $"{Width(sketch, doc.Height, 330)} vs {Width(sketch, doc.Height, 900)} px");

        // 2. A mouse stroke (constant pressure), the eraser, undo and redo.
        input.Press(Mouse(200, 900));
        input.Move([Mouse(1700, 900)]);
        input.Release(Mouse(1700, 900, down: false));
        Check("mouse stroke paints a continuous line", Enumerable.Range(220, 1460).All(x => Pixel.A(sketch.Image[x, 900]) > 0));
        uint beforeErase = sketch.Image[950, 900];
        session.SelectTool(ToolKind.Eraser);
        input.Press(Pen(950, 800, 1f));
        input.Move([Pen(950, 1000, 1f)]);
        input.Release(Pen(950, 1000, 0f, down: false));
        Check("eraser makes pixels transparent", sketch.Image[950, 900] == Pixel.Transparent);
        session.Undo();
        Check("undo restores erased pixels", sketch.Image[950, 900] == beforeErase && beforeErase != Pixel.Transparent);
        session.Redo();
        Check("redo erases again", sketch.Image[950, 900] == Pixel.Transparent);
        await Shot("smoke-B-quick-sketch.png");

        // 3. Painting: a new layer, a different brush, colour on top of the sketch.
        window.SetWorkspace(WorkspaceKind.Painting);
        Check("switching workspace keeps the same document", ReferenceEquals(doc, session.Document));
        var colour = session.AddLayer();
        session.ApplyPreset("chalk");
        session.SetForeground(ColorRgb.Parse("#5F8FA8"));
        session.SetSize(60);
        input.Press(Pen(400, 500, 0.8f));
        input.Move([Pen(800, 520, 0.9f), Pen(1200, 540, 0.9f)]);
        input.Release(Pen(1200, 540, 0f, down: false));
        Check("painting goes on the new layer", Pixel.A(colour.Image[800, 520]) > 0 && doc.Layers.Count == 2);
        await Shot("smoke-C-painting.png");

        // 4. Editing: select, free transform, adjust.
        window.SetWorkspace(WorkspaceKind.Editing);
        session.SelectTool(ToolKind.RectSelect);
        Drag(300, 400, 1300, 650);
        Check("rectangle selection", doc.Selection is { IsEmpty: false } sel && sel.Bounds.Width > 900, doc.Selection?.Bounds.ToString());
        window.FlipSelection(true);
        Check("flip selection is one undo step", doc.History.NextUndoName is not null);
        window.ApplyAdjustment(new Engine.Operations.Invert());
        session.Deselect();
        await Shot("smoke-D-editing.png");

        // 5. Save as .dnk (layers kept) and reopen.
        var dnk = Path.Combine(dir, "smoke-picture.dnk");
        bool saved = await window.SaveToPathAsync(dnk);
        Check(".dnk saved", saved && File.Exists(dnk), File.Exists(dnk) ? new FileInfo(dnk).Length : 0);
        Check("document is clean after saving", !doc.IsDirty);
        var flat = doc.Flatten();
        bool opened = await window.OpenPathAsync(dnk);
        var reopened = session.Document;
        Check(".dnk reopens with its layers", opened && reopened.Layers.Count == 2 && reopened.Layers[1].Name == colour.Name);
        Check("reopened picture matches", reopened.Flatten().Pixels.AsSpan().SequenceEqual(flat.Pixels));

        // 6. Exports (flattened copies).
        foreach (var f in new[] { ExportFormat.Png, ExportFormat.Jpeg, ExportFormat.WebP })
        {
            var path = Path.Combine(dir, "smoke-export" + ImageFiles.Extension(f));
            window.ExportToPath(path, f);
            bool ok = File.Exists(path) && ImageFiles.Decode(path) is { } img && img.Width == reopened.Width;
            Check($"export {ImageFiles.DisplayName(f)}", ok, File.Exists(path) ? new FileInfo(path).Length : 0);
        }

        // 7. Pixel Art: a small sprite with the pixel pencil.
        window.SetWorkspace(WorkspaceKind.PixelArt);
        session.ReplaceDocument(new Engine.Documents.Document(24, 24, null, "Sprite"), null);
        session.SelectTool(ToolKind.PixelPencil);
        session.SetForeground(ColorRgb.Parse("#2E2A3B"));
        Drag(3.5, 3.5, 20.5, 3.5);
        var sprite = session.Document.ActiveLayer.Image;
        Check("pixel pencil draws exact opaque pixels", Enumerable.Range(3, 18).All(x => sprite[x, 3] == Pixel.Opaque(ColorRgb.Parse("#2E2A3B"))) && sprite[3, 4] == Pixel.Transparent);
        Check("pixel art zoom is a whole number", Math.Abs(canvas.ActualZoom - Math.Round(canvas.ActualZoom)) < 1e-6, canvas.ActualZoom);
        await Shot("smoke-E-pixel-art.png");
        var spritePng = Path.Combine(dir, "smoke-sprite.png");
        window.ExportToPath(spritePng, ExportFormat.Png);
        var back = ImageFiles.Decode(spritePng);
        Check("sprite PNG keeps exact pixels and transparency", back.Pixels.AsSpan().SequenceEqual(session.Document.Flatten().Pixels));

        // 8. Pure Sketch.
        window.SetWorkspace(WorkspaceKind.PureSketch);
        await Shot("smoke-A-pure-sketch.png");
        window.ReturnFromPureSketch();
        Check("Tab from Pure Sketch returns to the previous workspace", window.Workspace.Kind == WorkspaceKind.PixelArt);

        // 9. Animation Studio: the sample, playback, onion skin, a new frame, save/reopen and the three exports.
        session.Document.MarkSaved(); // nothing to ask about: the smoke test's sprite is disposable
        await window.OpenSampleAsync(SampleAnimation.BouncingBall);
        var anim = session.Document;
        Check("Animation Studio opens the bouncing-ball sample", window.Workspace.Kind == WorkspaceKind.Animation && anim.FrameCount == 12);
        window.Animator.Play();
        var played = Stopwatch.StartNew();
        await Task.Delay(600);
        int playedTo = anim.CurrentFrame;
        window.Animator.Stop();
        Check("playback advances with time (about 7 frames in 0.6 s at 12 fps)", playedTo >= 3, $"frame {playedTo + 1} after {played.ElapsedMilliseconds} ms");
        session.GoToFrame(4);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Check("onion skin shows next to the current frame", anim.OnionVisible);
        await Shot("smoke-F-animation-studio.png");
        session.SelectTool(ToolKind.Brush);
        session.NewFrame(duplicate: true);
        Drag(100, 60, 200, 60);
        Check("new frame and drawing on it", anim.FrameCount == 13 && anim.CelAt(anim.ActiveLayer, 5) is { IsEmpty: false });
        var animPath = Path.Combine(dir, "smoke-animation.dnk");
        bool animSaved = await window.SaveToPathAsync(animPath);
        var animBack = DnkFile.Load(animPath);
        Check("animation saves and reopens (.dnk version 2)", animSaved && animBack.FrameCount == 13 && animBack.RenderFrame(5).Pixels.AsSpan().SequenceEqual(anim.RenderFrame(5).Pixels));
        foreach (var format in new[] { AnimationExportFormat.SpriteSheet, AnimationExportFormat.PngSequence, AnimationExportFormat.Gif })
        {
            var target = Path.Combine(dir, format == AnimationExportFormat.Gif ? "smoke-animation.gif" : $"smoke-{format}.png".ToLowerInvariant());
            var swExport = Stopwatch.StartNew();
            var what = await window.ExportAnimationToPathAsync(target, new AnimationExportSettings(format, Background: true));
            bool exists = format == AnimationExportFormat.PngSequence ? File.Exists(Path.Combine(dir, "smoke-pngsequence_0013.png")) : File.Exists(target);
            Check($"export animation: {format}", exists, $"{what} in {swExport.ElapsedMilliseconds} ms");
        }

        report["canvasFramesFromGpuTexture"] = canvas.GpuFrames;
        report["canvasFramesFromMemory"] = canvas.SoftwareFrames;
        report["uploadedPixels"] = canvas.UploadedPixels;
        await Shot("smoke-window.png");
        Check("screenshots written", File.Exists(Path.Combine(dir, "smoke-window.png")) && File.Exists(Path.Combine(dir, "smoke-C-painting.png")));
        session.ReplaceDocument(new Engine.Documents.Document(800, 600, ColorRgb.White, "Sketch"), null);
        window.SetWorkspace(WorkspaceKind.QuickSketch);
        return all;
    }

    private static int Width(Engine.Documents.Layer layer, int height, int x) =>
        Enumerable.Range(0, height).Count(y => Pixel.A(layer.Image[x, y]) > 0);
}
