using System.Diagnostics;
using System.Text.Json;
using DoodleNook.App.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using DoodleNook.App.Animation;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 015 — the Live Animation Preview in the real window: its own clock, live updates that redraw only what
/// changed, docking and floating, backgrounds that never reach the picture, and nothing running when it isn't seen.</summary>
public sealed class PreviewWindowTests
{
    private static readonly ColorRgb Ink = new(40, 30, 60);

    /// <summary>A 4-frame, 100 ms-per-frame animation with a mark that moves (frame f has its mark at x = 10 + 20 f).</summary>
    private static MainWindow FourFrames()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        var frames = new List<RasterImage>();
        for (int f = 0; f < 4; f++)
        {
            var img = new RasterImage(120, 60);
            for (int y = 20; y < 40; y++)
                for (int x = 10 + 20 * f; x < 25 + 20 * f; x++) img[x, y] = Pixel.Opaque(Ink);
            frames.Add(img);
        }
        var doc = SpriteSheetImport.NormalDocument(frames, 10);
        doc.MarkSaved();
        w.Session.ReplaceDocument(doc, null);
        w.ApplyCustomization();
        Pump();
        return w;
    }

    [AvaloniaFact]
    public void The_preview_plays_every_frame_while_you_stay_on_the_frame_you_draw_on()
    {
        var w = FourFrames();
        w.SetPreviewPanel(true);
        Pump();
        var p = w.AnimationPreviewPanel;
        Assert.True(Has(w, "AnimationPreview"));
        Assert.False(p.Floating);
        w.Session.GoToFrame(2); // drawing on frame 3
        foreach (var (ms, frame) in new[] { (0.0, 0), (150.0, 1), (250.0, 2), (350.0, 3), (450.0, 0), (1150.0, 3) })
        {
            p.ManualElapsedMs = ms;
            p.Tick();
            Assert.Equal(frame, p.ShownFrame);
            Assert.Equal(2, w.Session.Document.CurrentFrame); // the editing frame never moves
        }
        Assert.Equal("4/4", Find<TextBlock>(w, "AnimationPreviewFrame").Text);

        // Loop off: it plays once and stops on the last frame.
        p.ManualElapsedMs = null;
        Click(w, Find<Button>(w, "PreviewLoop"));
        Assert.False(w.PreviewSettings.Loop);
        p.ManualElapsedMs = 2000;
        p.Tick();
        Assert.Equal(3, p.ShownFrame);
        Assert.False(p.Playing);
        Assert.False(p.TimerRunning);
        Click(w, Find<Button>(w, "PreviewLoop"));

        // Pause and play.
        p.ManualElapsedMs = null;
        p.Play();
        Click(w, Find<Button>(w, "PreviewPlay"));
        Assert.False(p.Playing);
        Assert.False(p.TimerRunning); // nothing runs while paused
        Click(w, Find<Button>(w, "PreviewPlay"));
        Assert.True(p.Playing);
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public void Edits_show_in_the_preview_and_only_changed_frames_are_drawn_again()
    {
        var w = FourFrames();
        w.SetPreviewPanel(true);
        Pump();
        var p = w.AnimationPreviewPanel;
        p.MinRedrawMs = 0; // the test runs in milliseconds
        for (int f = 0; f < 4; f++)
        {
            p.ManualElapsedMs = f * 100 + 50;
            p.Tick();
        }
        int first = p.Renders;
        Assert.Equal(4, first);
        // Around again: all four are kept, nothing is drawn again.
        for (int f = 0; f < 4; f++)
        {
            p.ManualElapsedMs = f * 100 + 50;
            p.Tick();
        }
        Assert.Equal(first, p.Renders);

        // Draw on frame 2 (while the preview shows frame 4): only frame 2 is drawn again, when it comes round.
        w.Session.GoToFrame(1);
        w.Session.SelectTool(Editor.ToolKind.Brush);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (60, 5), (80, 8), (100, 10));
        Pump();
        p.ManualElapsedMs = 350;
        p.Tick();
        Assert.Equal(first, p.Renders);
        p.ManualElapsedMs = 150;
        p.Tick();
        Assert.Equal(first + 1, p.Renders);
        Assert.Equal(1, p.ShownFrame);

        // Undo, layer opacity and visibility, a new frame and new timing all reach the preview.
        var doc = w.Session.Document;
        ulong before = doc.FrameSignature(1);
        w.Undo();
        Assert.NotEqual(before, doc.FrameSignature(1));
        foreach (var change in new Action[]
                 {
                     () => doc.SetLayerProperties(doc.ActiveLayer, doc.ActiveLayer.Properties with { Opacity = 0.5 }),
                     () => doc.SetLayerProperties(doc.ActiveLayer, doc.ActiveLayer.Properties with { Visible = false }),
                 })
        {
            int renders = p.Renders;
            change();
            p.Tick();
            Assert.Equal(renders + 1, p.Renders);
        }
        w.Session.NewFrame(duplicate: false);
        Pump();
        Assert.Equal(5, doc.FrameCount);
        p.ManualElapsedMs = 450;
        p.Tick();
        Assert.Equal(4, p.ShownFrame);
        doc.SetTiming(5, true, false); // 200 ms per frame
        p.ManualElapsedMs = 450;
        p.Tick();
        Assert.Equal(2, p.ShownFrame);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public void Hidden_it_stops_and_lets_go_of_its_drawings()
    {
        var w = FourFrames();
        w.SetPreviewPanel(true);
        Pump();
        var p = w.AnimationPreviewPanel;
        p.ManualElapsedMs = 50;
        p.Tick();
        Assert.True(p.Renders > 0);
        w.SetPreviewPanel(false);
        Pump();
        Assert.False(Has(w, "AnimationPreview"));
        Assert.False(p.TimerRunning);
        Assert.Equal(-1, p.ShownFrame); // released
        // In another workspace the docked preview isn't shown.
        w.SetPreviewPanel(true);
        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        Assert.False(p.IsEffectivelyVisible);
        Assert.False(p.TimerRunning);
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public void It_floats_in_its_own_window_comes_back_and_closing_it_closes_it()
    {
        var w = FourFrames();
        w.SetPreviewPanel(true);
        Pump();
        Click(w, Find<Button>(w, "PreviewDetach"));
        Pump();
        var window = w.PreviewWindow;
        Assert.NotNull(window);
        Assert.True(window!.IsVisible);
        Assert.True(w.AnimationPreviewPanel.Floating);
        Assert.False(Has(w, "AnimationPreview")); // no longer in the canvas corner
        Assert.True(w.PreviewSettings.Floating);
        // Floating, it stays open in other workspaces.
        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        Assert.Same(window, w.PreviewWindow);
        w.SetWorkspace(WorkspaceKind.Animation);
        // Back in the corner.
        w.SetPreviewFloating(false);
        Pump();
        Assert.Null(w.PreviewWindow);
        Assert.True(Has(w, "AnimationPreview"));
        // Float again, then close the window: the preview is closed, and stays closed.
        w.SetPreviewFloating(true);
        Pump();
        w.PreviewWindow!.Close();
        Pump();
        Assert.False(w.PreviewOpen);
        Assert.Null(w.PreviewWindow);
        Assert.True(w.PreviewSettings.Floating); // next time it opens in its own window again
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public void Backgrounds_are_display_only()
    {
        var w = FourFrames();
        var doc = w.Session.Document;
        var exported = AnimationExport.RenderFrames(doc, includePaper: true).Select(f => f.Image.Pixels).ToList();
        w.SetPreviewPanel(true);
        Pump();
        w.AnimationPreviewPanel.SetBackdrop(new BackdropSetting { Kind = BackdropKind.Black });
        w.SetCanvasBackdrop(new BackdropSetting { Kind = BackdropKind.Colour, Colour = "#335577" });
        Pump();
        Assert.True(w.Canvas.HasBackdrop);
        Assert.Equal(BackdropKind.Black, w.PreviewSettings.Backdrop.Kind);
        var after = AnimationExport.RenderFrames(doc, includePaper: true).Select(f => f.Image.Pixels).ToList();
        for (int f = 0; f < 4; f++) Assert.Equal(exported[f], after[f]);
        Assert.False(doc.IsDirty); // the picture didn't change
        w.SetCanvasBackdrop(new BackdropSetting());
        Assert.False(w.Canvas.HasBackdrop);
        w.Close();
    }

    [AvaloniaFact]
    public void Preview_settings_are_remembered_and_older_settings_files_still_load()
    {
        var s = new AppSettings();
        s.Preview.Floating = true;
        s.Preview.WindowX = 40;
        s.Preview.WindowY = 60;
        s.Preview.WindowWidth = 500;
        s.Preview.Zoom = 2;
        s.Preview.Loop = false;
        s.Preview.Backdrop = new BackdropSetting { Kind = BackdropKind.Colour, Colour = "#102030" };
        s.CanvasBackdrop = new BackdropSetting { Kind = BackdropKind.Black };
        var path = Path.Combine(Path.GetTempPath(), "dn-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            s.Save(path);
            var back = AppSettings.Load(path);
            Assert.True(back.Preview.Floating);
            Assert.Equal((40, 60, 500.0, 2, false), (back.Preview.WindowX, back.Preview.WindowY, back.Preview.WindowWidth, back.Preview.Zoom, back.Preview.Loop));
            Assert.Equal(("#102030", BackdropKind.Colour), (back.Preview.Backdrop.Colour, back.Preview.Backdrop.Kind));
            Assert.Equal(BackdropKind.Black, back.CanvasBackdrop.Kind);
            File.WriteAllText(path, "{ \"AutosaveSeconds\": 30 }"); // a Mission 014 settings file
            var old = AppSettings.Load(path);
            Assert.False(old.Preview.Floating);
            Assert.True(old.Preview.Loop);
            Assert.Equal(BackdropKind.Checker, old.CanvasBackdrop.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Median and 95th-percentile time to process pen events while drawing on a 1920 × 1080 animation.</summary>
    private static (double Median, double P95) Stroke(MainWindow w, int seed)
    {
        w.Session.SelectTool(Editor.ToolKind.Brush);
        w.Session.SetSize(24);
        Pump();
        var doc = w.Session.Document;
        var view = w.Canvas.View;
        PointerInput At(double t, bool down = true)
        {
            double x = doc.Width * 0.1 + doc.Width * 0.8 * t, y = doc.Height * (0.3 + 0.1 * seed) + doc.Height * 0.2 * Math.Sin(t * Math.PI * 3);
            var (vx, vy) = view.DocumentToView(x, y);
            return new PointerInput(7, PointerKind.Pen, vx, vy, 0.8f, down, false, false, false, false);
        }
        var input = w.Canvas.Input!;
        var times = new List<double>();
        var sw = new Stopwatch();
        input.Press(At(0));
        var batch = new PointerInput[2];
        for (int e = 0; e < 160; e++)
        {
            batch[0] = At((2 * e + 1) / 320.0);
            batch[1] = At((2 * e + 2) / 320.0);
            sw.Restart();
            input.Move(batch);
            times.Add(sw.Elapsed.TotalMilliseconds);
            if (e % 4 == 3) Pump(); // the screen, and the preview's clock, catch up every few events
        }
        input.Release(At(1, down: false));
        Pump();
        times.Sort();
        return (times[times.Count / 2], times[(int)(times.Count * 0.95)]);
    }

    /// <summary>
    /// Mission 015 measurement: drawing with the live preview open and playing is as responsive as with it closed (the
    /// preview never redraws the frame being drawn during a stroke, and the other frames come from its cache). The numbers
    /// go to out/measurements/m015-preview.json; the check only catches a gross slowdown.
    /// </summary>
    [AvaloniaFact]
    public void Drawing_with_the_preview_playing_is_as_quick_as_without()
    {
        var w = Open(1600, 1000, WorkspaceKind.Animation);
        var frames = Enumerable.Range(0, 6).Select(f =>
        {
            var img = new RasterImage(1920, 1080);
            for (int y = 400; y < 700; y++)
                for (int x = 200 + 200 * f; x < 400 + 200 * f; x++) img[x, y] = Pixel.Opaque(Ink);
            return img;
        }).ToList();
        var doc = SpriteSheetImport.NormalDocument(frames, 12);
        w.Session.ReplaceDocument(doc, null);
        w.ApplyCustomization();
        Pump();
        Stroke(w, 0); // warm up
        var closed = new List<(double, double)>();
        var open = new List<(double, double)>();
        for (int round = 0; round < 3; round++)
        {
            w.SetPreviewPanel(false);
            Pump();
            closed.Add(Stroke(w, round));
            w.SetPreviewPanel(true);
            Pump();
            open.Add(Stroke(w, round));
        }
        double closedMedian = closed.Select(c => c.Item1).Order().ElementAt(1), openMedian = open.Select(c => c.Item1).Order().ElementAt(1);
        double closedP95 = closed.Select(c => c.Item2).Order().ElementAt(1), openP95 = open.Select(c => c.Item2).Order().ElementAt(1);
        var outDir = Path.Combine(Screenshots.OutDir, "..", "measurements");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "m015-preview.json"), JsonSerializer.Serialize(new
        {
            document = "1920 x 1080, 6 frames, brush 24 px, 160 pen events (2 samples each)",
            closedMedianMs = Math.Round(closedMedian, 3), openMedianMs = Math.Round(openMedian, 3),
            closedP95Ms = Math.Round(closedP95, 3), openP95Ms = Math.Round(openP95, 3),
            previewRenders = w.AnimationPreviewPanel.Renders,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(openMedian <= closedMedian * 1.5 + 0.5, $"open {openMedian:0.000} ms vs closed {closedMedian:0.000} ms");
        w.SetPreviewPanel(false);
        w.Session.Document.MarkSaved();
        w.Close();
    }
}
