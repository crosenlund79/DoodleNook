using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Animation;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.App.Tutorials;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 014 — The Gentle Guide: the "Your First Animation" tutorial done in the real window.</summary>
public sealed class TutorialWindowTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "dn-tut-" + Guid.NewGuid().ToString("N")[..8]);

    public TutorialWindowTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { Directory.Delete(_temp, true); } catch (IOException) { }
    }

    private static Task Until(Func<bool> done, int ms = 10_000) => ArtistsCompanionWindowTests.Until(done, ms);

    private static void Render(MainWindow w) => w.CaptureRenderedFrame()?.Dispose();

    private static void Tap(MainWindow w, string name)
    {
        Render(w);
        Click(w, Find<Button>(w, name));
    }

    private static Rect InWindow(MainWindow w, Control c) => new(c.TranslatePoint(new Point(0, 0), w)!.Value, c.Bounds.Size);

    /// <summary>The highlight is shown around the named control.</summary>
    private static void AssertHighlighted(MainWindow w, string name)
    {
        Render(w);
        Pump();
        var h = w.Tutorials.Highlight;
        Assert.True(h.IsVisible, $"no highlight for {name}");
        var target = InWindow(w, Find<Control>(w, name));
        var ring = InWindow(w, h);
        Assert.True(ring.Contains(target.Center) && ring.Width >= target.Width && ring.Width <= target.Width + 12, $"{name}: ring {ring}, control {target}");
        Assert.False(w.Tutorials.GuideRect.Intersects(target), $"the guide covers {name}");
    }

    private static async Task<MainWindow> StartedAtStep1(int width = 1280, int height = 800)
    {
        var w = Open(width, height, WorkspaceKind.Painting);
        w.Session.Document.MarkSaved();
        Render(w);
        Menu(w, "Help", "Guided Tutorials");
        await Until(() => w.Dialogs.IsOpen && Has(w, "GuidedTutorials"));
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Your First Animation");
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("Create a simple bouncing-ball animation") == true);
        Tap(w, "StartTutorial_first-animation");
        await Until(() => w.Tutorials.IsRunning && Has(w, "TutorialGuide"));
        Assert.Equal(-1, w.Tutorials.Index);
        Assert.Contains("We're going to make a bouncing ball", Find<TextBlock>(w, "TutorialStepText").Text);
        Tap(w, "TutorialStart");
        Assert.Equal(0, w.Tutorials.Index);
        return w;
    }

    private static void DrawBall(MainWindow w, double cx, double cy, double r = 22)
    {
        var points = Enumerable.Range(0, 25).Select(i => (cx + r * Math.Cos(i * Math.PI / 12), cy + r * Math.Sin(i * Math.PI / 12))).ToArray();
        Drag(w, MouseButton.Left, RawInputModifiers.None, points);
    }

    [AvaloniaFact]
    public async Task Your_first_animation_from_start_to_finish_in_the_real_animation_studio()
    {
        var w = await StartedAtStep1();
        int listeners = w.Session.StateChangedListenerCount;
        // 1. The practice animation: 256 × 256 at 8 fps in Animation Studio, made by the action button.
        Assert.Equal("Step 1 of 8", Find<TextBlock>(w, "TutorialCounter").Text);
        Tap(w, "TutorialAction");
        var doc = w.Session.Document;
        Assert.Equal((256, 256, 8.0), (doc.Width, doc.Height, doc.Animation!.Fps));
        Assert.Equal(WorkspaceKind.Animation, w.Workspace.Kind);
        Assert.Null(w.Session.FilePath);
        Assert.Contains("practice animation is ready", w.Tutorials.Guide.FeedbackText);
        await Until(() => w.Tutorials.Index == 1);

        // 2. Draw the first ball: the brush is pointed at; a stroke finishes the step.
        AssertHighlighted(w, "ToolBrush");
        Tap(w, "ToolBrush");
        Assert.Equal(1, w.Tutorials.Index); // choosing the tool isn't drawing
        DrawBall(w, 128, 50);
        Assert.Contains("first drawing", w.Tutorials.Guide.FeedbackText);
        await Until(() => w.Tutorials.Index == 2);

        // 3. A new frame, with the real New frame button.
        AssertHighlighted(w, "NewFrameButton");
        Tap(w, "NewFrameButton");
        Assert.Equal(2, doc.FrameCount);
        await Until(() => w.Tutorials.Index == 3);

        // 4. Onion skin (on by default in Animation Studio: the guide says so and waits for Next).
        if (w.Session.Onion.Enabled)
        {
            Assert.Contains("already on", w.Tutorials.Guide.FeedbackText);
            Assert.True(Has(w, "TutorialNext"));
            Tap(w, "TutorialNext");
        }
        else
        {
            AssertHighlighted(w, "OnionToggle");
            Tap(w, "OnionToggle");
            await Until(() => w.Tutorials.Index == 4);
        }
        Assert.Equal(4, w.Tutorials.Index);

        // 5. The bounce: drawing on six frames.
        AssertHighlighted(w, "NewFrameButton");
        Assert.Contains("Frames with a ball: 1", w.Tutorials.Guide.ProgressText);
        double[] ys = [90, 140, 200, 140, 90];
        for (int i = 0; i < ys.Length; i++)
        {
            if (i > 0) w.Session.NewFrame(duplicate: false);
            Pump();
            DrawBall(w, 128, ys[i], i == 2 ? 18 : 22);
            if (i < ys.Length - 1) Assert.Equal(4, w.Tutorials.Index);
        }
        Assert.Contains("whole bounce", w.Tutorials.Guide.FeedbackText);
        await Until(() => w.Tutorials.Index == 5);

        // 6. Play: the real Play button; the guide waits for Next while it plays.
        AssertHighlighted(w, "PlayButton");
        Tap(w, "PlayButton");
        Assert.True(w.Animator.IsPlaying);
        Assert.Contains("It moves", w.Tutorials.Guide.FeedbackText);
        await Task.Delay(1500);
        Assert.Equal(5, w.Tutorials.Index);
        Assert.True(w.Animator.IsPlaying); // not interrupted
        Tap(w, "TutorialNext");
        w.Animator.Stop();

        // 7. Save: a real save to a place the artist chose.
        AssertHighlighted(w, "MenuFile");
        var path = Path.Combine(_temp, "bounce.dnk");
        Assert.True(await w.SaveToPathAsync(path));
        await Until(() => w.Tutorials.Index == 7);

        // 8. Export: a real GIF.
        var gif = Path.Combine(_temp, "bounce.gif");
        await w.ExportAnimationToPathAsync(gif, new Settings.AnimationExportSettings { Format = Settings.AnimationExportFormat.Gif });
        await Until(() => w.Tutorials.Index == 8);
        Assert.Equal("You made your first animation!", Find<TextBlock>(w, "TutorialStepTitle").Text);
        Tap(w, "TutorialFinish");

        // The guide is gone, its listeners with it, and the animation stays: saved, exported and still editable.
        Assert.False(w.Tutorials.IsRunning);
        Assert.False(Has(w, "TutorialGuide"));
        Assert.Equal(listeners - 1, w.Session.StateChangedListenerCount);
        Assert.Same(doc, w.Session.Document);
        Assert.Equal(6, doc.FrameCount);
        Assert.Equal(6, Enumerable.Range(0, 6).Count(f => doc.CelAt(doc.ActiveLayer, f) is { ContentBounds.IsEmpty: false }));
        Assert.Equal(6, DnkFile.Load(path).FrameCount);
        Assert.Equal(6, Engine.Files.GifDecoder.Decode(File.ReadAllBytes(gif)).Frames.Count);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Unsaved_work_is_asked_about_first_and_cancel_keeps_it_open()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var mine = w.Session.Document;
        w.Session.SelectTool(ToolKind.Brush);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 100), (300, 200));
        Assert.True(mine.IsDirty);
        w.Tutorials.Start(FirstAnimationTutorial.Create());
        Tap(w, "TutorialStart");
        Tap(w, "TutorialAction");
        await Until(() => w.Dialogs.IsOpen);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Save them before starting the practice animation") == true);
        w.Dialogs.Close("cancel");
        Pump();
        Assert.Same(mine, w.Session.Document);           // nothing replaced
        Assert.Equal(0, w.Tutorials.Index);
        Assert.False(w.Tutorials.StepDone);
        Assert.Null(w.Tutorials.Context!.Practice);
        w.Tutorials.Exit();
        mine.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Back_skip_and_I_have_done_this_never_trap_the_artist_and_exiting_midway_keeps_the_drawing_usable()
    {
        var w = await StartedAtStep1();
        int listeners = w.Session.StateChangedListenerCount;
        Tap(w, "TutorialAction");
        await Until(() => w.Tutorials.Index == 1);
        var doc = w.Session.Document;
        Assert.True(Has(w, "TutorialManual"));          // "I've done this" on a detectable step
        Tap(w, "TutorialManual");
        Assert.Equal(2, w.Tutorials.Index);
        Tap(w, "TutorialBack");
        Assert.Equal(1, w.Tutorials.Index);
        Tap(w, "TutorialSkip");
        Tap(w, "TutorialSkip");
        Assert.Equal(3, w.Tutorials.Index);
        Assert.Equal("Step 4 of 8", Find<TextBlock>(w, "TutorialCounter").Text);

        // Draw a little, then leave in the middle with the ×.
        w.Session.SelectTool(ToolKind.Brush);
        DrawBall(w, 120, 60);
        Tap(w, "TutorialExit");
        Assert.False(w.Tutorials.IsRunning);
        Assert.False(Has(w, "TutorialHighlight") && Find<Border>(w, "TutorialHighlight").IsVisible);
        Assert.Equal(listeners - 1, w.Session.StateChangedListenerCount);
        Assert.Same(doc, w.Session.Document);
        Assert.False(doc.CelAt(doc.ActiveLayer, 0)!.ContentBounds.IsEmpty);
        // Everything still works: new frames, drawing, undo.
        w.Session.NewFrame(duplicate: false);
        DrawBall(w, 120, 120);
        Assert.Equal(2, doc.FrameCount);
        w.Undo();
        Assert.True(doc.CelAt(doc.ActiveLayer, 1) is null or { ContentBounds.IsEmpty: true });
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_highlight_follows_the_control_when_the_window_changes_and_a_hidden_timeline_can_be_shown()
    {
        var w = await StartedAtStep1();
        Tap(w, "TutorialAction");
        await Until(() => w.Tutorials.Index == 1);
        Tap(w, "TutorialManual"); // on to "Add another frame"
        AssertHighlighted(w, "NewFrameButton");
        // A smaller window: the button moves, the ring moves with it.
        w.Width = 1000;
        w.Height = 640;
        Pump();
        AssertHighlighted(w, "NewFrameButton");
        // The interface hidden (Tab): the control is gone, the guide offers to show it again.
        w.SetUiHidden(true);
        Render(w);
        Pump();
        Assert.False(w.Tutorials.Highlight.IsVisible);
        Assert.True(w.Tutorials.Guide.RevealShown);
        Tap(w, "TutorialReveal");
        Assert.False(w.UiHidden);
        AssertHighlighted(w, "NewFrameButton");
        w.Tutorials.Exit();
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task On_a_compact_screen_at_150_percent_the_guide_stays_readable_movable_and_clear_of_the_control()
    {
        var w = Open(1280, 720, WorkspaceKind.Painting);
        w.SetRenderScaling(1.5);
        w.Session.Document.MarkSaved();
        w.Tutorials.Start(FirstAnimationTutorial.Create());
        Tap(w, "TutorialStart");
        Tap(w, "TutorialAction");
        await Until(() => w.Tutorials.Index == 1);
        foreach (var target in new[] { "ToolBrush", "NewFrameButton", "OnionToggle", "PlayButton", "MenuFile" })
        {
            var step = FirstAnimationTutorial.Create().Steps.Select((s, i) => (s, i)).First(t => t.s.Target == target).i;
            while (w.Tutorials.Index < step) w.Tutorials.Skip();
            AssertHighlighted(w, target);
            var guide = InWindow(w, Find<Border>(w, "TutorialGuide"));
            Assert.True(guide.Right <= w.Bounds.Width + 0.5 && guide.Bottom <= w.Bounds.Height + 0.5, $"{guide} is off screen");
        }
        // Drag the guide by its title: it stays where it was put.
        var before = w.Tutorials.GuideRect;
        var header = before.TopLeft + new Point(60, 14);
        w.MouseMove(header);
        w.MouseDown(header, MouseButton.Left);
        w.MouseMove(header + new Point(-200, -120), RawInputModifiers.LeftMouseButton);
        w.MouseUp(header + new Point(-200, -120), MouseButton.Left);
        Pump();
        Render(w);
        Pump();
        Assert.Equal(before.X - 200, w.Tutorials.GuideRect.X, 1);
        Assert.Equal(before.Y - 120, w.Tutorials.GuideRect.Y, 1);
        w.Tutorials.Exit();
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Shortcuts_keep_working_while_the_guide_is_open()
    {
        var w = await StartedAtStep1();
        Tap(w, "TutorialAction");
        await Until(() => w.Tutorials.Index == 1);
        Tap(w, "TutorialManual");
        Assert.Equal(2, w.Tutorials.Index);
        var doc = w.Session.Document;
        Key(w, Avalonia.Input.Key.N);                   // New frame from the keyboard finishes "Add another frame"
        Assert.Equal(2, doc.FrameCount);
        await Until(() => w.Tutorials.Index == 3);
        Assert.All(w.GetVisualDescendants().OfType<Button>().Where(b => b.FindAncestorOfType<TutorialGuide>() is not null), b => Assert.False(b.Focusable));
        Key(w, Avalonia.Input.Key.Escape);              // Escape keeps its usual meaning; the guide stays
        Assert.True(w.Tutorials.IsRunning);
        // Alt+1 still changes workspace: the timeline is gone there, so the guide offers to bring it back.
        Key(w, Avalonia.Input.Key.D1, RawInputModifiers.Alt);
        Render(w);
        Pump();
        Assert.Equal(WorkspaceKind.PureSketch, w.Workspace.Kind);
        Assert.True(w.Tutorials.IsRunning);
        Assert.False(w.Tutorials.Highlight.IsVisible);
        Assert.True(w.Tutorials.Guide.RevealShown);
        Tap(w, "TutorialReveal");
        Assert.Equal(WorkspaceKind.Animation, w.Workspace.Kind);
        AssertHighlighted(w, "OnionToggle");
        w.Tutorials.Exit();
        doc.MarkSaved();
        w.Close();
    }
}
