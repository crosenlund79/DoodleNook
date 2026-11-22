using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DoodleNook.App.Dialogs;
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
/// reopening a .dnk, PNG/JPEG/WebP export, animation, and the creative tools: a custom brush tip, stamps, pattern
/// fill, palettes, the Brush Laboratory and the reference board; palette lock; text, shapes, groups, masks, adjustment
/// layers, guides and perspective; Game Edit Mode), saves screenshots and a JSON report into the folder, then exits with code 0 (all checks passed) or 1. Used by CI on a Windows runner; it
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
            report["error"] = "timed out after 180 s";
            Write(directory, report);
            Environment.Exit(2);
        }, null, TimeSpan.FromSeconds(180), Timeout.InfiniteTimeSpan);

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

        // 10. Mission 006 bug: switching formats in Export animation with a large animation must stay responsive
        //     (it used to throw when going back to Sprite sheet, which ended the app).
        session.Document.MarkSaved();
        window.CreateNewAnimation(new Dialogs.NewAnimationChoice(1920, 1080, 21, 12, false, ColorRgb.White));
        for (int f = 0; f < 21; f++) // a different drawing on every frame (identical neighbours would be merged in the GIF)
        {
            session.GoToFrame(f);
            Drag(200 + f * 50, 500, 300 + f * 50, 600);
        }
        var exportDialog = window.Dialogs.AskExportAnimationAsync(new AnimationExportSettings(), 21, 1920, 1080, hasPaper: true);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        long slowestSwitch = 0;
        bool switched = true;
        foreach (var label in new[] { "Animated GIF", "Sprite sheet", "PNG sequence", "Animated GIF", "Sprite sheet", "Animated GIF", "PNG sequence", "Sprite sheet" })
        {
            var button = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => b.IsEffectivelyVisible
                && b.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Any(t => t.Text == label));
            if (button is null) { switched = false; break; }
            var swSwitch = Stopwatch.StartNew();
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            slowestSwitch = Math.Max(slowestSwitch, swSwitch.ElapsedMilliseconds);
        }
        window.Dialogs.Close(null);
        bool cancelled = await exportDialog is null;
        Check("export dialog: switching formats with a 1920×1080, 21-frame animation stays responsive", switched && cancelled && slowestSwitch < 2000, $"slowest switch {slowestSwitch} ms");
        var bigGif = Path.Combine(dir, "smoke-1080p.gif");
        var swGif = Stopwatch.StartNew();
        await window.ExportAnimationToPathAsync(bigGif, new AnimationExportSettings(AnimationExportFormat.Gif, Background: true));
        using (var codec = SkiaSharp.SKCodec.Create(bigGif))
            Check("1080p GIF export has all 21 frames with their timing", codec is not null && codec.FrameCount == 21 && codec.FrameInfo[0].Duration is >= 80 and <= 90,
                $"{codec?.FrameCount} frames in {swGif.ElapsedMilliseconds} ms, {new FileInfo(bigGif).Length / 1024} KB");

        // 11. Mission 007: the creative tools, with a library in a folder of its own (as %APPDATA% would hold it).
        session.Document.MarkSaved();
        var libraryDir = Path.Combine(dir, "smoke-library");
        var library = new Engine.Library.CreativeLibrary(libraryDir);
        window.UseLibrary(library);
        window.SetWorkspace(WorkspaceKind.Painting);
        session.ReplaceDocument(new Engine.Documents.Document(1600, 900, ColorRgb.White, "Sketch"), null);
        var art = session.Document;
        //     A brush tip made from the artwork: a few marks, selected, stored as a tip and painted with.
        session.SelectTool(ToolKind.Brush);
        session.ApplyPreset("ink");
        session.SetSize(10);
        Drag(100, 100, 160, 130);
        Drag(110, 140, 150, 110);
        session.ApplySelection(Engine.Operations.Selections.Rectangle(art, 80, 80, 180, 160), "Select");
        var tipPicture = session.SelectionPicture();
        session.Deselect();
        var customBrush = library.AddBrush("Smoke tip", new Engine.Painting.BrushSettings
        {
            Size = 40, Opacity = 1, PressureSize = true, TipId = library.AddTip(tipPicture!), Spacing = 0.3, RotationJitter = 0.5,
        });
        session.UseBrush(customBrush.Settings, customBrush.Id);
        var workLayer = session.AddLayer();
        int undoBefore = art.History.UndoCount;
        input.Press(Pen(300, 450, 0.7f));
        input.Move([Pen(600, 470, 0.8f), Pen(900, 450, 0.8f)]);
        input.Release(Pen(900, 450, 0f, down: false));
        var painted = workLayer.Image.ContentBounds();
        Check("brush with a tip made from the artwork paints, as one undo step", painted.Width > 500 && art.History.UndoCount == undoBefore + 1, painted.ToString());
        //     A stamp, undone and redone.
        session.SelectTool(ToolKind.Stamp);
        session.SetStampOptions(session.StampOptions with { StampId = "builtin:flower", Scale = 1, Rotation = 0, Opacity = 1 });
        undoBefore = art.History.UndoCount;
        input.Press(Mouse(1200, 300));
        input.Release(Mouse(1200, 300, down: false));
        bool stamped = Pixel.A(workLayer.Image[1200, 300]) > 0 && art.History.UndoCount == undoBefore + 1;
        session.Undo();
        bool stampUndone = workLayer.Image[1200, 300] == Pixel.Transparent;
        session.Redo();
        Check("stamp places a flower as one undo step (undo and redo)", stamped && stampUndone && Pixel.A(workLayer.Image[1200, 300]) > 0);
        //     The paint bucket's pattern mode, inside a selection.
        var patternLayer = session.AddLayer();
        session.SelectTool(ToolKind.Fill);
        session.SetPatternOptions(session.PatternOptions with { UsePattern = true, PatternId = "builtin:brick", Scale = 1, Rotation = 0, Opacity = 1 });
        session.ApplySelection(Engine.Operations.Selections.Rectangle(art, 1000, 500, 1500, 850), "Select");
        undoBefore = art.History.UndoCount;
        input.Press(Mouse(1200, 600));
        input.Release(Mouse(1200, 600, down: false));
        int fillSteps = art.History.UndoCount - undoBefore;
        session.Deselect();
        int brickColours = Enumerable.Range(1010, 480).Select(x => patternLayer.Image[x, 600]).Distinct().Count();
        Check("pattern fill tiles the pattern inside the selection only, as one undo step",
            Pixel.A(patternLayer.Image[1200, 600]) == 255 && patternLayer.Image[900, 600] == Pixel.Transparent && brickColours > 2 && fillSteps == 1,
            $"{brickColours} colours along a row");
        //     A palette from the picture.
        var extracted = ColorQuantizer.Extract(art.Flatten(), 6);
        var palette = library.AddPalette("From the smoke test", "My palettes", extracted);
        window.UsePalette(palette.Id);
        Check("palette from the picture becomes the swatches", extracted.Count == 6 && session.ActivePaletteColors.SequenceEqual(extracted), string.Join(" ", extracted));
        //     The Brush Laboratory's playground never touches the picture.
        int historyBefore = art.History.UndoCount;
        var flatBefore = art.Flatten();
        var lab = window.OpenBrushLabAsync("charcoal", BrushLab.Section.Texture);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        window.LastBrushLab?.Playground.ExampleStroke();
        await Shot("smoke-G-brush-lab.png");
        window.Dialogs.Close(null);
        await lab;
        Check("Brush Laboratory playground leaves the picture and its undo history alone",
            window.LastBrushLab is not null && art.History.UndoCount == historyBefore && art.Flatten().Pixels.AsSpan().SequenceEqual(flatBefore.Pixels));
        //     The reference board is on screen but never in the artwork or its exports.
        window.SetReferenceBoardVisible(true);
        window.ReferenceBoard.Add(Engine.Samples.SampleAnimations.BouncingBall().RenderFrame(3), "Ball");
        await Shot("smoke-H-reference-board.png");
        var withBoard = Path.Combine(dir, "smoke-with-board.png");
        window.ExportToPath(withBoard, ExportFormat.Png);
        Check("reference board is shown but not part of the export", window.ReferenceBoard.IsVisible && art.Layers.Count == 3
            && ImageFiles.Decode(withBoard).Pixels.AsSpan().SequenceEqual(art.Flatten().Pixels));
        window.SetReferenceBoardVisible(false);
        //     Everything new is in the library folder, and a fresh library reads it back.
        var reread = new Engine.Library.CreativeLibrary(libraryDir);
        Check("creative library is saved and reads back", File.Exists(Path.Combine(libraryDir, "library.json")) && reread.FindBrush(customBrush.Id)?.Name == "Smoke tip"
            && reread.Resolve(customBrush.Settings)?.Tip is not null && reread.FindPalette(palette.Id)?.Colors.Count == 6);

        // 12. Palette Lock and Remap to Palette: through the real dialog, then the exact-alpha rule in an exported PNG.
        session.Document.MarkSaved();
        session.ReplaceDocument(new Engine.Documents.Document(400, 300, null, "Sketch"), null);
        var locked = session.Document;
        session.SelectTool(ToolKind.Brush);
        session.ApplyPreset("ink");
        session.SetSize(24);
        session.SetForeground(ColorRgb.Parse("#00C060"));
        Drag(40, 150, 360, 150);
        locked.ActiveLayer.Image[10, 10] = Pixel.FromStraight(200, 100, 50, 73); // the brief's critical pixel
        var lockTarget = new Engine.Documents.Palette("Smoke", [new ColorRgb(40, 80, 120), ColorRgb.Parse("#F2E3C6"), ColorRgb.Parse("#1A1C2C")]);
        var lockTask = window.LockToPaletteAsync(lockTarget);
        for (int i = 0; i < 200 && window.LastRemapDialog is null; i++) await Task.Delay(25);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        bool askedFirst = window.LastRemapDialog is not null && !locked.PaletteLocked;
        await Shot("smoke-I-palette-remap.png");
        window.Dialogs.Close("apply");
        bool applied = await lockTask;
        var lockConformer = locked.PaletteConformer;
        bool onlyPalette = lockConformer is not null && locked.ActiveLayer.Image.Pixels.All(p => p == 0 || lockConformer.ExactIndex(p) >= 0);
        Check("palette lock asks first (remap preview), then remaps and locks as one undo step",
            askedFirst && applied && locked.PaletteLocked && onlyPalette && locked.History.NextUndoName is "Remap and lock to palette");
        var lockedPng = Path.Combine(dir, "smoke-locked.png");
        window.ExportToPath(lockedPng, ExportFormat.Png);
        using (var codec = SkiaSharp.SKCodec.Create(lockedPng))
        {
            var info = new SkiaSharp.SKImageInfo(codec.Info.Width, codec.Info.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
            using var bitmap = new SkiaSharp.SKBitmap(info);
            codec.GetPixels(info, bitmap.GetPixels());
            var px = bitmap.GetPixel(10, 10);
            Check("remapping keeps alpha exactly: RGBA(200,100,50,73) exports as RGBA(40,80,120,73)",
                px.Red == 40 && px.Green == 80 && px.Blue == 120 && px.Alpha == 73, $"{px.Red},{px.Green},{px.Blue},{px.Alpha}");
        }
        session.SetForeground(ColorRgb.Parse("#FF00FF"));
        input.Press(Pen(60, 60, 0.3f));
        input.Move([Pen(200, 80, 0.9f), Pen(340, 60, 0.5f)]);
        input.Release(Pen(340, 60, 0f, down: false));
        Check("painting while locked uses only palette colours (a picked colour snaps to the palette)",
            locked.Palette.Colors.Contains(session.Foreground) && locked.ActiveLayer.Image.Pixels.All(p => p == 0 || lockConformer!.ExactIndex(p) >= 0));
        var lockedDnk = Path.Combine(dir, "smoke-locked.dnk");
        bool lockedSaved = await window.SaveToPathAsync(lockedDnk);
        var lockedBack = DnkFile.Load(lockedDnk);
        Check("a locked picture reopens locked (.dnk version 3)", lockedSaved && lockedBack.PaletteLocked && lockedBack.Palette.Colors.SequenceEqual(lockTarget.Colors));

        // 13. Mission 008: text with the computer's fonts, an editable shape, a group, a mask, an adjustment layer,
        //     guides (never exported), a stroke held to a perspective line and a version 4 file.
        session.Document.MarkSaved();
        session.SetDesignShapeOptions(session.DesignShape with { Editable = true, Kind = Engine.Documents.ShapeType.RoundedRectangle, Fill = true, Outline = true });
        session.SetAssistOptions(session.Assist with { Snap = false });
        window.SetWorkspace(WorkspaceKind.Painting);
        session.ReplaceDocument(new Engine.Documents.Document(1200, 800, ColorRgb.White, "Background"), null);
        var design = session.Document;
        bool segoe = SkiaSharp.SKFontManager.Default.MatchFamily("Segoe UI") is { } face && face.FamilyName == "Segoe UI";
        var winText = design.AddTextLayer(new Engine.Documents.TextContent("Segoe UI text", "Segoe UI", 48, ColorRgb.Parse("#2B2230"), X: 60, Y: 40));
        Check("text with a Windows font is drawn (no substitute when the font is installed)",
            !winText.ContentBounds.IsEmpty && (winText.SubstituteFont is null || !segoe), $"Segoe UI installed: {segoe}, substitute: {winText.SubstituteFont ?? "none"}");
        session.SelectTool(ToolKind.Text);
        undoBefore = design.History.UndoCount;
        input.Press(Mouse(80, 300));
        input.Release(Mouse(80, 300, down: false));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        bool editorOpened = window.TextEditor.IsOpen;
        var typed = window.TextEditor.Layer;
        window.TextEditor.Type("Typed with the Text tool");
        window.TextEditor.Finish();
        Check("Text tool: a click opens the text editor and the new text is one undo step",
            editorOpened && typed is { Kind: Engine.Documents.LayerKind.Text } && design.History.UndoCount == undoBefore + 1 && design.History.NextUndoName == "Add text");
        session.SetForeground(ColorRgb.Parse("#E9A94D"));
        session.SelectTool(ToolKind.Shape);
        undoBefore = design.History.UndoCount;
        Drag(400, 400, 800, 520);
        var buttonLayer = design.ActiveLayer;
        Check("an editable shape is drawn with the Shapes tool as one undo step",
            buttonLayer.Kind == Engine.Documents.LayerKind.Shape && design.History.UndoCount == undoBefore + 1 && Pixel.A(buttonLayer.Image[600, 460]) == 255);
        session.SelectTool(ToolKind.Move);
        Drag(600, 460, 650, 480);
        var moved = (Engine.Documents.ShapeContent)buttonLayer.Content!;
        Check("the Move tool moves the shape by its handles (still editable)", Math.Abs(moved.X - 450) < 1.5 && Math.Abs(moved.Y - 420) < 1.5, $"{moved.X:0.#}, {moved.Y:0.#}");
        window.HandleKey(Avalonia.Input.Key.G, Avalonia.Input.KeyModifiers.Control);
        var groupLayer = design.Layers.FirstOrDefault(l => l.IsGroup);
        var painted2 = session.AddLayer();
        session.SelectTool(ToolKind.Brush);
        session.ApplyPreset("ink");
        session.SetSize(40);
        session.SetForeground(ColorRgb.Parse("#3F6A82"));
        Drag(100, 650, 1100, 650);
        session.AddMask();
        session.SetForeground(ColorRgb.Black);
        Drag(500, 650, 700, 650);
        bool maskHides = design.Flatten()[600, 650] == Pixel.Opaque(ColorRgb.White) && Pixel.A(painted2.Image[600, 650]) == 255;
        session.SetEditingMask(false);
        var adjustmentLayer = session.AddAdjustmentLayer(new Engine.Operations.HueSaturation(0, -100, 0));
        var grey = Pixel.ToStraight(design.Flatten()[300, 650]);
        Check("the screen shows the adjustment layer at once", design.Projection.Pixels.AsSpan().SequenceEqual(design.Flatten().Pixels));
        Check("group, mask (hides without erasing) and adjustment layer (changes the look, not the pixels)",
            groupLayer is not null && buttonLayer.ParentId == groupLayer.Id && maskHides && Math.Abs(grey.R - grey.B) <= 1 && painted2.Image[300, 650] == Pixel.Opaque(ColorRgb.Parse("#3F6A82")));
        design.SetGuides([new Engine.Documents.Guide(true, 300), new Engine.Documents.Guide(false, 200)], "Add guide");
        window.SetShowRulers(true);
        await Shot("smoke-J-design.png");
        window.SetShowRulers(false);
        var designPng = Path.Combine(dir, "smoke-design.png");
        window.ExportToPath(designPng, ExportFormat.Png);
        Check("guides are shown but never exported", ImageFiles.Decode(designPng).Pixels.AsSpan().SequenceEqual(design.Flatten().Pixels));
        design.DeleteLayer(adjustmentLayer);
        var lines = session.AddLayer();
        design.SetPerspective(new Engine.Documents.PerspectiveGuide(1, 600, 100, 0, 0));
        session.SetAssistOptions(session.Assist with { PerspectiveAssist = true, ShowPerspective = true });
        input.Press(Pen(200, 500, 0.8f));
        input.Move([Pen(260, 470, 0.8f), Pen(330, 460, 0.8f), Pen(400, 400, 0.8f)]);
        input.Release(Pen(400, 400, 0f, down: false));
        // On the line from (200, 500) to (600, 100): x = 330 → y = 370; the hand was at y = 460 there.
        Check("a stroke follows the perspective line towards the vanishing point", Pixel.A(lines.Image[330, 370]) > 0 && Pixel.A(lines.Image[330, 460]) == 0);
        design.SetPerspective(Engine.Documents.PerspectiveGuide.Off);
        var designDnk = Path.Combine(dir, "smoke-design.dnk");
        bool designSaved = await window.SaveToPathAsync(designDnk);
        var designBack = DnkFile.Load(designDnk);
        int designVersion;
        using (var zip = System.IO.Compression.ZipFile.OpenRead(designDnk))
        using (var json = zip.GetEntry("document.json")!.Open())
            designVersion = JsonDocument.Parse(json).RootElement.GetProperty("version").GetInt32();
        Check("text, shapes, groups, masks and guides save and reopen (.dnk version 4)", designSaved && designVersion == 4
            && designBack.Flatten().Pixels.AsSpan().SequenceEqual(design.Flatten().Pixels) && designBack.Guides.Count == 2
            && designBack.Layers.Count(l => l.Kind == Engine.Documents.LayerKind.Text) == 2, $"version {designVersion}");

        // 14. Mission 009 — Game Edit Mode: a game folder with PNGs in subfolders; open one, paint, Ctrl+S.
        session.Document.MarkSaved();
        var gameRoot = Path.Combine(dir, "smoke-game", "assets");
        var spritePath = Path.Combine(gameRoot, "characters", "hero.png");
        Directory.CreateDirectory(Path.GetDirectoryName(spritePath)!);
        var heroPixels = new RasterImage(48, 32);
        for (int y = 8; y < 24; y++)
            for (int x = 8; x < 40; x++)
                heroPixels[x, y] = Pixel.Opaque(ColorRgb.Parse("#C8473E"));
        File.WriteAllBytes(spritePath, PngCodec.Encode(heroPixels));
        File.WriteAllBytes(Path.Combine(gameRoot, "logo.png"), PngCodec.Encode(new RasterImage(16, 16)));
        var heroOriginal = File.ReadAllBytes(spritePath);
        GameProject.Open(gameRoot).SetBackupOriginals(true); // as if chosen before (no first-time question)
        bool gameOpened = await window.OpenGameFolderAsync(gameRoot);
        var hero = window.Game?.Find("characters/hero.png");
        Check("Game Edit Mode finds the PNGs in subfolders", gameOpened && window.Game!.Images.Count == 2 && hero is not null);
        if (hero is not null && await window.OpenGameImageAsync(hero))
        {
            session.SelectTool(ToolKind.Brush);
            session.ApplyPreset("ink");
            session.SetSize(4);
            session.SetForeground(ColorRgb.Parse("#1A1C2C"));
            Drag(10, 28, 38, 28);
            bool gameSaved = await window.SaveAsync();
            var written = ImageFiles.Decode(spritePath);
            Check("Ctrl+S saves the editable .dnk and replaces the game's PNG in place",
                gameSaved && File.Exists(hero.DnkPath) && written.Width == 48 && written.Height == 32 && written[0, 0] == 0
                && written.Pixels.AsSpan().SequenceEqual(session.Document.Flatten().Pixels),
                Path.GetRelativePath(gameRoot, hero.DnkPath));
            Check("the original PNG is backed up byte for byte, and the image is In progress (not complete)",
                File.Exists(hero.BackupPath) && File.ReadAllBytes(hero.BackupPath).AsSpan().SequenceEqual(heroOriginal) && hero.Status == GameImageStatus.InProgress);
            await Shot("smoke-K-game-edit.png");
            window.LeaveGameEditMode();
        }
        // 15. Mission 010 — The Finishing Touches: smudge, clone, liquify, perspective and the quick palette's brush memory.
        {
            var two = new RasterImage(400, 300);
            for (int y = 0; y < 300; y++)
                for (int x = 0; x < 400; x++)
                    two[x, y] = Pixel.Opaque(x < 200 ? ColorRgb.Parse("#C8473E") : ColorRgb.Parse("#3F6A82"));
            session.ReplaceDocument(Engine.Documents.Document.FromImage(two, "Finishing"), null);
            window.Canvas.FitToView();
            await Task.Delay(50);
            var fdoc = session.Document;
            session.SelectTool(ToolKind.Smudge);
            session.SetRetouchOptions(session.Retouch with { SmudgeSize = 40, SmudgeStrength = 0.8 });
            Drag(170, 150, 260, 150);
            Check("Smudge carries paint along the stroke (one undo step)", Pixel.R(fdoc.ActiveLayer.Image[225, 150]) > 100 && fdoc.History.UndoCount == 1,
                $"R {Pixel.R(fdoc.ActiveLayer.Image[225, 150])}");
            session.SelectTool(ToolKind.Clone);
            session.SetRetouchOptions(session.Retouch with { CloneSize = 30, CloneSoftness = 0 });
            session.SetCloneSource(100, 100);
            Drag(300, 220, 302, 220);
            Check("Clone copies from the Alt+click source", fdoc.ActiveLayer.Image[301, 220] == Pixel.Opaque(ColorRgb.Parse("#C8473E")) && fdoc.History.UndoCount == 2);
            session.BeginLiquify();
            session.SetLiquifyOptions(session.Liquify with { Mode = Engine.Operations.LiquifyMode.Push, Size = 80, Strength = 1 });
            Drag(190, 60, 240, 60);
            bool liquified = fdoc.ActiveLayer.Image[215, 60] != Pixel.Opaque(ColorRgb.Parse("#3F6A82"));
            session.ApplyLiquify();
            Check("Liquify reshapes and Apply is one undo step", liquified && session.ActiveLiquify is null && fdoc.History.UndoCount == 3);
            fdoc.SetSelection(Engine.Documents.SelectionMask.Rectangle(400, 300, new Engine.Imaging.PixelRect(150, 200, 100, 60)), "Select");
            window.TransformIn(Engine.Operations.TransformMode.Perspective);
            var corners = session.ActiveTransform?.Corners;
            bool tilted = corners is { Length: 4 } && session.ActiveTransform!.SetCorners([(corners[0].X + 30, corners[0].Y), (corners[1].X - 30, corners[1].Y), corners[2], corners[3]]);
            window.ApplyTransform();
            Check("Perspective tilts the selected pixels", tilted && session.ActiveTransform is null && Pixel.A(fdoc.ActiveLayer.Image[155, 202]) == 0);
            session.ApplyPreset("pencil");
            session.SetSize(33);
            session.ApplyPreset("ink");
            session.ApplyPreset("pencil");
            Check("each brush keeps the size it was last used at (quick palette)", session.Brush.Size == 33 && session.RecentBrushes.Take(2).SequenceEqual(["pencil", "ink"]));
            await Shot("smoke-L-finishing.png");
            fdoc.MarkSaved();
        }

        // 16. Mission 011 — Under the Hood: exact compositing with the painting cache, quick undo, steady listeners, diagnostics.
        {
            var udoc = new Engine.Documents.Document(1600, 1000, ColorRgb.White, "Back");
            for (int i = 1; i < 6; i++) udoc.AddLayer($"Layer {i}");
            session.ReplaceDocument(udoc, null);
            window.Canvas.FitToView();
            await Task.Delay(50);
            session.SelectTool(ToolKind.Brush);
            session.ApplyPreset("soft");
            session.SetSize(260);
            Drag(100, 200, 1400, 800);
            session.SetSize(12);
            Drag(100, 800, 1400, 200);
            var full = udoc.Flatten();
            Check("painting over five layers shows exactly the full composite", udoc.Projection.Pixels.AsSpan().SequenceEqual(full.Pixels) && udoc.StrokeCacheBytes > 0);
            int steps = udoc.History.UndoCount;
            long undoStart = Diagnostics.PerformanceLog.Now();
            window.Undo();
            double undoMs = Diagnostics.PerformanceLog.Since(undoStart);
            report["undoThinStrokeOn6LayersMs"] = Math.Round(undoMs, 2);
            Check("undo puts back exactly the earlier composite", udoc.Projection.Pixels.AsSpan().SequenceEqual(udoc.Flatten().Pixels) && udoc.History.UndoCount == steps - 1,
                $"{undoMs:0.0} ms");
            int listeners = session.StateChangedListenerCount;
            foreach (var t in new[] { ToolKind.Eraser, ToolKind.Fill, ToolKind.Brush, ToolKind.Smudge, ToolKind.Brush })
            {
                session.SelectTool(t);
                await Task.Delay(20);
            }
            Check("changing tools adds no listeners", session.StateChangedListenerCount == listeners, $"{listeners} → {session.StateChangedListenerCount}");
            var sections = Diagnostics.DiagnosticsReport.Build(window);
            string text = Diagnostics.DiagnosticsReport.ToText(sections);
            Check("Diagnostics reports the picture and memory, without folders", text.Contains("1600 × 1000 px", StringComparison.Ordinal) && text.Contains("Working set", StringComparison.Ordinal)
                && !text.Contains(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) && !text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
            if (Diagnostics.PerformanceLog.RecentFrames() is { } frames)
            {
                report["canvasFrameAverageMs"] = Math.Round(frames.AverageMs, 2);
                report["canvasFrameMaxMs"] = Math.Round(frames.MaxMs, 2);
            }
            udoc.MarkSaved();
        }

        // 17. Mission 012 — The Artist's Companion: import an animation for reconstruction, layer opacity on every frame,
        // pictures pasted from the Windows clipboard (PNG, a bitmap, a copied file), Paste as reference, the bundled
        // palettes and the built-in Quick Start guide.
        {
            static RasterImage Box(int w, int h, int x0, int y0, int bw, int bh, uint colour)
            {
                var img = new RasterImage(w, h);
                for (int y = y0; y < y0 + bh; y++)
                    for (int x = x0; x < x0 + bw; x++)
                        img[x, y] = colour;
                return img;
            }
            var walk = Enumerable.Range(0, 4).Select(f => new ExportFrame(Box(64, 48, 8 + f * 10, 12, 14, 20, Pixel.Opaque(new ColorRgb(200, (byte)(60 + f * 40), 80))), 125, f)).ToList();
            var gifPath = Path.Combine(dir, "smoke-walk.gif");
            using (var fs = File.Create(gifPath))
                GifEncoder.Write(walk, fs, new GifOptions(AlphaThreshold: 1));
            var import = window.ImportAnimationFromAsync([gifPath]);
            for (int i = 0; i < 200 && !window.Dialogs.IsOpen; i++) await Task.Delay(20);
            window.Dialogs.Close("import");
            bool imported = await import;
            var adoc = session.Document;
            Check("a GIF imports as four layers with the reference at 35 % and locked", imported && adoc.FrameCount == 4
                && adoc.Layers.Select(l => l.Name).SequenceEqual(["Reference", "Sketch", "Outline", "Coloring"])
                && Math.Abs(adoc.Layers[0].Opacity - 0.35) < 1e-9 && adoc.Layers[0].Locked && adoc.ActiveLayer.Name == "Sketch",
                $"{adoc.FrameCount} frames, {string.Join("/", adoc.Layers.Select(l => l.Name))}");
            session.SetLayerProperty(adoc.Layers[0], p => p with { Opacity = 1 });
            var opaque = AnimationExport.RenderFrames(adoc, includePaper: false)[2].Image[20 + 8, 20];
            session.SetLayerProperty(adoc.Layers[0], p => p with { Opacity = 0.35 });
            var faint = AnimationExport.RenderFrames(adoc, includePaper: false)[2].Image[20 + 8, 20];
            Check("layer opacity applies on every frame of an export", Pixel.A(opaque) == 255 && Pixel.A(faint) is > 80 and < 100, $"{Pixel.A(opaque)} → {Pixel.A(faint)}");

            if (OperatingSystem.IsWindows())
            {
                var pic = new RasterImage(10, 6);
                for (int y = 0; y < 6; y++)
                    for (int x = 0; x < 10; x++)
                        pic[x, y] = x < 5 ? Pixel.Opaque(new ColorRgb(20, 140, 220)) : Pixel.FromStraight(240, 90, 40, 128);
                window.SystemClipboard = ClipboardPictures.ForWindow(window);
                int layers = adoc.Layers.Count;
                bool put = Platform.WindowsClipboard.Put(("PNG", 0, PngCodec.Encode(pic)));
                bool pasted = put && await window.PasteAsync(asReference: false);
                session.CommitTransform();
                var layer = adoc.ActiveLayer;
                int px = (adoc.Width - 10) / 2, py = (adoc.Height - 6) / 2;
                Check("a PNG copied in another program pastes with its transparency (Windows clipboard)", pasted && adoc.Layers.Count == layers + 1
                    && layer.Image[px + 1, py + 1] == pic[1, 1] && layer.Image[px + 7, py + 1] == pic[7, 1], put ? window.LastToast : "the clipboard couldn't be set");

                // A 32-bit bitmap with all alpha bytes zero (as Paint copies): opaque.
                var dib = new byte[40 + 4 * 4];
                BitConverter.GetBytes(40).CopyTo(dib, 0);
                BitConverter.GetBytes(2).CopyTo(dib, 4);
                BitConverter.GetBytes(2).CopyTo(dib, 8);
                BitConverter.GetBytes((short)1).CopyTo(dib, 12);
                BitConverter.GetBytes((short)32).CopyTo(dib, 14);
                for (int i = 0; i < 4; i++) new byte[] { 40, 120, 200, 0 }.CopyTo(dib, 40 + i * 4);
                put = Platform.WindowsClipboard.Put((null, Platform.WindowsClipboard.CfDib, dib));
                pasted = put && await window.PasteAsync(asReference: false);
                session.CommitTransform();
                Check("a bitmap with empty alpha pastes opaque (Windows clipboard)", pasted
                    && adoc.ActiveLayer.Image[(adoc.Width - 2) / 2, (adoc.Height - 2) / 2] == Pixel.Opaque(new ColorRgb(200, 120, 40)), window.LastToast);

                // A picture file copied in Explorer, pasted as a reference: 35 %, locked once placed, this frame only.
                var file = Path.Combine(dir, "smoke-reference.png");
                File.WriteAllBytes(file, PngCodec.Encode(pic));
                var names = System.Text.Encoding.Unicode.GetBytes(file + "\0\0");
                var drop = new byte[20 + names.Length];
                BitConverter.GetBytes(20).CopyTo(drop, 0);
                BitConverter.GetBytes(1).CopyTo(drop, 16);
                names.CopyTo(drop, 20);
                session.GoToFrame(1);
                put = Platform.WindowsClipboard.Put((null, Platform.WindowsClipboard.CfHdrop, drop));
                pasted = put && await window.PasteAsync(asReference: true);
                var reference = adoc.ActiveLayer;
                bool positioning = session.ActiveTransform is not null && !reference.Locked;
                session.CommitTransform();
                Check("Paste as reference: a copied file at 35 %, locked once placed, on this frame only (Windows clipboard)", pasted && positioning
                    && reference.Locked && Math.Abs(reference.Opacity - 0.35) < 1e-9 && adoc.CelAt(reference, 1) is not null && adoc.CelAt(reference, 0) is null,
                    window.LastToast);
                await Shot("smoke-M-companion.png");

                // Mission 016: DoodleNook's own Copy reaches other programs through the real Windows clipboard — PNG with
                // transparency, a 32-bit bitmap with alpha, a 24-bit one on white — and Ctrl+V still pastes its own copy.
                session.Deselect();
                session.PlaceImage(pic, "Copy me");
                session.CommitTransform();
                uint before = window.SystemClipboard.ChangeCount ?? 0;
                bool copied = await window.CopyAsync(cut: false);
                var formats = Platform.WindowsClipboard.ReadFormats();
                RasterImage? fromPng = formats.TryGetValue("PNG", out var pngBytes) ? PngCodec.Decode(pngBytes) : null;
                RasterImage? fromV5 = formats.TryGetValue("CF_DIBV5", out var v5) ? DibDecoder.Decode(v5) : null;
                RasterImage? fromDib = formats.TryGetValue("CF_DIB", out var dib24) ? DibDecoder.Decode(dib24) : null;
                Check("Copy puts the picture on the Windows clipboard with its transparency (PNG and 32-bit bitmap)", copied
                    && window.LastCopyProblem is null && window.SystemClipboard.ChangeCount != before
                    && fromPng is not null && fromPng.Pixels.AsSpan().SequenceEqual(pic.Pixels)
                    && fromV5 is not null && fromV5.Pixels.AsSpan().SequenceEqual(pic.Pixels),
                    window.LastCopyProblem ?? $"formats: {string.Join(", ", formats.Keys)}");
                Check("Copy also gives programs without transparency a bitmap on white", fromDib is not null && fromDib.Width == 10
                    && fromDib[1, 1] == pic[1, 1] && Pixel.A(fromDib[7, 1]) == 255 && Pixel.ToStraight(fromDib[7, 1]).G > 150,
                    fromDib is null ? "no CF_DIB" : $"{Pixel.ToStraight(fromDib[7, 1])}");
                int count = adoc.Layers.Count;
                pasted = await window.PasteAsync(asReference: false);
                Check("after DoodleNook's own Copy, Ctrl+V pastes DoodleNook's copy in place (Windows clipboard)", pasted
                    && adoc.Layers.Count == count + 1 && adoc.ActiveLayer.Name == "Pasted", window.LastToast);
            }
            else report["windowsClipboard"] = "not checked (not Windows)";

            Check("ten palettes by other artists are bundled with their credit", session.Library.Palettes.Count(p => p.Credit is not null) == 10
                && session.Library.Palettes.Any(p => p.Name == "PICO-8 palette" && p.Colors.Count == 16));
            var guide = QuickStartGuide.Markdown();
            var pictures = guide is null ? [] : QuickStartGuide.Parse(guide).Where(b => b.Kind == "image").ToList();
            Check("the Quick Start guide and its pictures are built in", pictures.Count >= 5 && pictures.All(b => QuickStartGuide.Resource(b.Text) is { Length: > 1000 }),
                $"{pictures.Count} pictures");
            adoc.MarkSaved();
        }

        // 18. Mission 013 — the Reference Animation Library: a walk cycle into the animation from section 17 (which already
        // has an imported reference, drawings and pasted layers), placed under the layer being drawn on, nothing else
        // changed; undo and redo; saved and reopened with its settings.
        {
            var rdoc = session.Document;
            var drawOn = rdoc.Layers.First(l => l.Name == "Sketch");
            rdoc.SetActiveLayer(drawOn);
            var before = rdoc.Layers.ToDictionary(l => l, l => Enumerable.Range(0, rdoc.FrameCount).Select(f => rdoc.CelAt(l, f)).ToList());
            int frames = rdoc.FrameCount, steps = rdoc.History.UndoCount;
            var insert = window.InsertReferenceCycleAsync();
            for (int i = 0; i < 200 && !window.Dialogs.IsOpen; i++) await Task.Delay(20);
            await Task.Delay(300); // the preview plays
            await Shot("smoke-N-reference-dialog.png");
            window.Dialogs.Close("ok");
            bool inserted = await insert;
            var refLayer = rdoc.Layers.FirstOrDefault(l => l.ReferenceCycle is not null);
            bool untouched = before.All(kv => rdoc.Layers.Contains(kv.Key)
                && kv.Value.SequenceEqual(Enumerable.Range(0, kv.Value.Count).Select(f => rdoc.CelAt(kv.Key, f))));
            Check("a reference cycle goes into a started animation under the drawing layer, locked and faint, changing nothing else",
                inserted && refLayer is not null && refLayer.Locked && Math.Abs(refLayer.Opacity - 0.35) < 1e-9 && ReferenceEquals(rdoc.ActiveLayer, drawOn)
                && rdoc.Layers.ToList().IndexOf(refLayer) == rdoc.Layers.ToList().IndexOf(drawOn) - 1 && untouched
                && rdoc.FrameCount == Math.Max(frames, 8) && rdoc.History.UndoCount == steps + 1,
                $"{refLayer?.Name}, {frames} → {rdoc.FrameCount} frames");
            await Shot("smoke-O-reference-cycle.png");
            window.Undo();
            bool undone = rdoc.Layers.All(l => l.ReferenceCycle is null) && rdoc.FrameCount == frames;
            window.Redo();
            Check("one undo takes the reference cycle away and redo brings it back", undone && rdoc.Layers.Count(l => l.ReferenceCycle is not null) == 1);
            var path = Path.Combine(dir, "smoke-reference.dnk");
            Engine.Files.DnkFile.Save(rdoc, path);
            var cycleFile = Engine.Files.DnkFile.Load(path);
            var backLayer = cycleFile.Layers.FirstOrDefault(l => l.ReferenceCycle is not null);
            Check("saved and reopened with the cycle's settings and drawings", backLayer is not null && backLayer.ReferenceCycle == refLayer!.ReferenceCycle
                && Enumerable.Range(0, cycleFile.FrameCount).All(f => cycleFile.CelAt(backLayer, f)?.Image.Pixels.AsSpan().SequenceEqual(rdoc.CelAt(refLayer, f)!.Image.Pixels) == true));
            report["referenceCycles"] = Engine.References.ReferenceLibrary.Cycles.Count;
            rdoc.MarkSaved();
        }

        // 19. Mission 014 — The Gentle Guide: Help › Guided Tutorials, "Your First Animation" in the real window: the
        // practice animation, the highlighted controls, a drawing noticed, a skipped step, Exit, and the drawing still usable.
        {
            static void Press(Avalonia.Controls.Button b) => b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            Avalonia.Controls.Button? Named(string name) => window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().FirstOrDefault(b => b.Name == name && b.IsEffectivelyVisible);
            bool Rings(string name)
            {
                var target = window.GetVisualDescendants().OfType<Avalonia.Controls.Control>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible);
                var ring = window.Tutorials.Highlight;
                if (target is null || !ring.IsVisible || target.TranslatePoint(new Point(0, 0), window) is not { } t || ring.TranslatePoint(new Point(0, 0), window) is not { } r) return false;
                return new Rect(r, ring.Bounds.Size).Contains(new Rect(t, target.Bounds.Size).Center) && !window.Tutorials.GuideRect.Intersects(new Rect(t, target.Bounds.Size));
            }
            async Task Wait(Func<bool> done)
            {
                for (int i = 0; i < 250 && !done(); i++) await Task.Delay(20);
            }
            session.Document.MarkSaved();
            int listeners = session.StateChangedListenerCount;
            var picker = window.ShowGuidedTutorialsAsync();
            await Wait(() => window.Dialogs.IsOpen);
            window.Dialogs.Close("start:" + Tutorials.FirstAnimationTutorial.Id);
            await picker;
            var runner = window.Tutorials;
            Check("Help › Guided Tutorials starts “Your First Animation”", runner.IsRunning && runner.Index == -1 && runner.Tutorial?.Steps.Count == 8);
            Press(Named("TutorialStart")!);
            Press(Named("TutorialAction")!);
            await Wait(() => runner.Index == 1);
            var practice = session.Document;
            Check("the tutorial makes a fresh practice animation in Animation Studio (256 × 256, 8 fps, unsaved)",
                runner.Index == 1 && practice.Width == 256 && practice.Height == 256 && practice.Animation?.Fps == 8 && session.FilePath is null
                && window.Workspace.Kind == WorkspaceKind.Animation, $"step {runner.Index + 1}");
            await Task.Delay(100);
            bool brush = Rings("ToolBrush");
            session.SelectTool(ToolKind.Brush);
            session.SetSize(10);
            Drag(90, 40, 150, 70);
            await Wait(() => runner.Index == 2);
            await Task.Delay(100);
            await Shot("smoke-P-tutorial.png");
            Check("the guide points at the real controls and notices a drawing", brush && runner.Index == 2 && Rings("NewFrameButton"), $"step {runner.Index + 1}");
            Press(Named("TutorialSkip")!);
            Check("a step can be skipped", runner.Index == 3);
            Press(Named("TutorialExit")!);
            bool usable = !runner.IsRunning && ReferenceEquals(session.Document, practice) && session.StateChangedListenerCount == listeners
                && practice.CelAt(practice.ActiveLayer, 0) is { ContentBounds.IsEmpty: false };
            session.NewFrame(duplicate: false);
            Drag(90, 120, 150, 150);
            Check("Exit closes the guide and its listeners; the practice drawing stays and keeps working", usable && practice.FrameCount == 2
                && practice.CelAt(practice.ActiveLayer, 1) is { ContentBounds.IsEmpty: false }, $"{practice.FrameCount} frames");
            practice.MarkSaved();
        }

        // 20. Mission 015 — The Game Artist's Workshop: a sprite sheet round trip through the import dialog, the live preview
        // on its own clock (docked and in its own window), a palette folder in one go, and the sample storybook with a page
        // exported at its print size.
        {
            async Task Wait(Func<bool> done)
            {
                for (int i = 0; i < 250 && !done(); i++) await Task.Delay(20);
            }
            var work = Path.Combine(dir, "m015");
            Directory.CreateDirectory(work);
            var walk = Engine.References.ReferenceLibrary.Find("walk")!;
            var framing = new Engine.References.ReferenceFraming(Engine.References.ReferenceView.Side, Engine.References.ReferenceFacing.Right, 54, 32, 60, HardEdges: true, Marker: false);
            var frames = Enumerable.Range(0, walk.Frames)
                .Select(d => new Engine.Files.ExportFrame(Engine.References.ReferenceRenderer.Render(walk.Pose(d), 64, 64, framing), 100, d)).ToList();
            var sheet = Engine.Files.AnimationExport.BuildSheet(frames, new Engine.Files.SpriteSheetOptions(Engine.Files.SheetLayout.Grid, 4, 2, 1, 3));
            var sheetPath = Path.Combine(work, "walk-sheet.png");
            File.WriteAllBytes(sheetPath, Engine.Files.PngCodec.Encode(sheet.Image));
            File.WriteAllText(Path.ChangeExtension(sheetPath, ".json"), Engine.Files.AnimationExport.SheetJson(sheet, "walk-sheet.png", 8, true));
            session.Document.MarkSaved();
            var import = window.ImportSpriteSheetFromAsync(sheetPath);
            await Wait(() => window.Dialogs.IsOpen);
            await Shot("smoke-Q-sprite-sheet.png");
            window.Dialogs.Close("import");
            bool imported = await import;
            var sdoc = session.Document;
            bool same = imported && sdoc.FrameCount == 8 && Enumerable.Range(0, 8).All(f =>
                sdoc.RenderFrame(f, includePaper: false).Pixels.AsSpan().SequenceEqual(Engine.Files.PngCodec.Decode(Engine.Files.PngCodec.Encode(frames[f].Image)).Pixels));
            Check("a sprite sheet exported from an animation imports back pixel for pixel (8 frames, with its JSON)", same, $"{sdoc.FrameCount} frames");

            session.GoToFrame(5);
            window.SetPreviewPanel(true);
            var preview = window.AnimationPreviewPanel;
            preview.ManualElapsedMs = 260;
            preview.Tick();
            bool independent = preview.ShownFrame == 2 && sdoc.CurrentFrame == 5;
            window.SetPreviewFloating(true);
            bool floating = window.PreviewWindow is { IsVisible: true };
            window.SetPreviewFloating(false);
            bool docked = window.PreviewWindow is null && preview.IsVisible;
            window.SetPreviewPanel(false);
            preview.ManualElapsedMs = null;
            Check("the live preview plays on its own clock (editing frame 6, showing frame 3) and opens in its own window", independent && floating && docked,
                $"shown {preview.ShownFrame + 1}, editing {sdoc.CurrentFrame + 1}");
            sdoc.MarkSaved();

            var pals = Path.Combine(work, "palettes");
            Directory.CreateDirectory(pals);
            File.WriteAllText(Path.Combine(pals, "smoke.gpl"), "GIMP Palette\n#Palette Name: Smoke Test\n10 20 30\n40 50 60\n200 210 220\n");
            File.WriteAllText(Path.Combine(pals, "smoke.hex"), "0a141e\n28323c\nc8d2dc\n");
            File.WriteAllText(Path.Combine(pals, "readme.txt"), "Palettes.\n");
            var scratchLibrary = new Engine.Library.CreativeLibrary(); // in memory: the smoke test never changes the user's library
            var pal = Engine.Library.PaletteFolderImport.Import(scratchLibrary, pals, includeSubfolders: true);
            Check("a folder of palettes imports in one go: one imported, its duplicate skipped, the text file left alone",
                pal.ImportedCount == 1 && pal.Duplicates == 1 && pal.Unsupported == 1, Engine.Library.PaletteFolderImport.Describe(pal));

            await window.OpenSampleStorybookAsync(Path.Combine(work, "story"));
            var book = window.Storybook;
            if (book is not null && window.StorybookView is { } sv)
                await Wait(() => book.Usage().Keys.All(k => sv.Thumbnails.Get(k, false) is { Bitmap: not null }));
            await Shot("smoke-R-storybook.png");
            var written = await window.ExportStorybookToAsync(StorybookExport.ThisPage, Path.Combine(work, "story-page.png"));
            var pageImage = written.Count == 1 ? Engine.Files.PngCodec.Decode(File.ReadAllBytes(written[0])) : null;
            Check("the sample storybook opens (2 pages, 6 linked drawings, one shared) and a page exports at A4, 300 dpi: 2480 × 3508",
                book is { Pages.Count: 2 } && book.Usage().Count == 6 && pageImage is { Width: 2480, Height: 3508 },
                pageImage is null ? "no page" : $"{pageImage.Width} × {pageImage.Height}");
            report["storybookPageExport"] = pageImage is null ? null : $"{pageImage.Width} x {pageImage.Height}";
            window.CloseStorybook();
        }

        report["windowTitle"] = window.Title;
        Check("the window title shows the milestone", window.Title?.StartsWith($"DoodleNook — {MainWindow.Milestone}", StringComparison.Ordinal) == true
            && MainWindow.Milestone == "M016", window.Title);

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
