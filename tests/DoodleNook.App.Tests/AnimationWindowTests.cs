using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Animation;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using SkiaSharp;
using K = Avalonia.Input.Key;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Animation Studio driven through the real window: layout, keys, timeline, playback, onion skins,
/// Customize workspace, exports and crash recovery.</summary>
public class AnimationWindowTests
{
    private static MainWindow Studio(int width = 200, int height = 120, int frames = 1, bool pixel = false, ColorRgb? paper = null)
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.CreateNewAnimation(new NewAnimationChoice(width, height, frames, 12, pixel, paper));
        Pump();
        return w;
    }

    private static void Line(MainWindow w, double x0, double y0, double x1, double y1) =>
        Drag(w, MouseButton.Left, RawInputModifiers.None, (x0, y0), ((x0 + x1) / 2, (y0 + y1) / 2), (x1, y1));

    private static byte A(Cel? cel, int x, int y) => cel is null ? (byte)0 : Pixel.A(cel.Image[x, y]);

    private static Cel? CelAt(MainWindow w, int frame) => w.Session.Document.CelAt(w.Session.Document.ActiveLayer, frame);

    private static TimelineGrid Grid(MainWindow w) => Find<TimelineGrid>(w, "TimelineGrid");

    private static Point GridToWindow(MainWindow w, Point p) => Grid(w).TranslatePoint(p, w)!.Value;

    private static bool TextShown(Window w, string text) =>
        w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.Contains(text, StringComparison.Ordinal) == true);

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "doodlenook-anim-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ================================================================== layout

    [AvaloniaFact]
    public void Animation_studio_opens_with_a_large_canvas_a_compact_timeline_and_few_controls()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        Assert.True(Has(w, "TitleBar") && Has(w, "ToolStrip") && Has(w, "OptionsBar") && Has(w, "Timeline") && Has(w, "MenuAnimation"));
        Assert.True(Has(w, "PlayButton") && Has(w, "NewFrameButton") && Has(w, "DuplicateFrameButton") && Has(w, "OnionToggle") && Has(w, "FpsChip"));
        // Optional controls stay hidden until the user asks for them.
        foreach (var hidden in new[] { "AnimationDock", "PaintDock", "ViewChip", "StatusBar", "AnimationPreview", "OnionOpacitySlider", "FrameDurationControl", "FirstFrameButton" })
            Assert.False(Has(w, hidden), hidden);
        Assert.Equal(8, w.CurrentTools.Length);
        Assert.True(w.Animator.Active);
        // Canvas first: most of the window is drawing space.
        Assert.True(w.Canvas.Bounds.Height >= 0.6 * w.ClientSize.Height, $"canvas {w.Canvas.Bounds.Height} of {w.ClientSize.Height}");
        Assert.Equal(168, Find<TimelinePanel>(w, "Timeline").Bounds.Height, 1);

        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        Assert.False(Has(w, "Timeline"));
        Assert.False(w.Animator.Active);
        Assert.DoesNotContain("Animation", w.MenuNames);
        Key(w, K.D6, RawInputModifiers.Alt);
        Assert.Equal(WorkspaceKind.Animation, w.Workspace.Kind);
        Assert.True(Has(w, "Timeline"));
        w.Close();
    }

    [AvaloniaFact]
    public void Pixel_art_animation_uses_pixel_tools_exact_display_and_its_own_onion_settings()
    {
        var w = Studio(32, 32, 2, pixel: true);
        Assert.True(w.PixelAnimation);
        Assert.True(w.Session.PixelMode && w.Canvas.PixelExact && w.Canvas.IntegerZoom);
        Assert.Equal(ToolKind.PixelPencil, w.Session.Tool);
        Assert.Contains(ToolKind.PixelLine, w.CurrentTools);
        Assert.Equal(0.3, w.Session.Onion.Opacity, 3);
        Assert.False(w.Session.Onion.Falloff);

        Line(w, 3.5, 3.5, 10.5, 3.5);
        var cel = CelAt(w, 0)!;
        for (int x = 3; x <= 10; x++) Assert.Equal(255, A(cel, x, 3));
        Assert.Equal(0, A(cel, 3, 4)); // exact pixels, no soft edge

        w.SetPixelAnimation(false);
        Pump();
        Assert.False(w.Session.PixelMode);
        Assert.Equal(ToolKind.Brush, w.Session.Tool);
        Assert.Equal(OnionSkinSettings.Default.Opacity, w.Session.Onion.Opacity, 3);
        w.Close();
    }

    // ================================================================== keys and frames

    [AvaloniaFact]
    public void New_frame_keys_turn_a_still_picture_into_an_animation_and_undo_restores_it()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        var doc = w.Session.Document;
        Assert.False(doc.IsAnimated);
        Line(w, 100, 100, 300, 100);
        Key(w, K.N);
        Assert.True(doc.IsAnimated);
        Assert.Equal(2, doc.FrameCount);
        Assert.Equal(1, doc.CurrentFrame);
        Assert.Null(CelAt(w, 1));
        Assert.True(A(CelAt(w, 0), 200, 100) > 0);

        Key(w, K.Z, RawInputModifiers.Control);
        Assert.False(doc.IsAnimated);
        Assert.True(Pixel.A(doc.ActiveLayer.Image[200, 100]) > 0); // the picture itself is untouched
        Key(w, K.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.True(doc.IsAnimated);

        Key(w, K.OemComma);
        Assert.Equal(0, doc.CurrentFrame);
        Key(w, K.N, RawInputModifiers.Shift); // duplicate: an independent copy of the drawing
        Assert.Equal(3, doc.FrameCount);
        Assert.Equal(1, doc.CurrentFrame);
        Assert.NotSame(CelAt(w, 0), CelAt(w, 1));
        Assert.True(A(CelAt(w, 1), 200, 100) > 0);
        w.Close();
    }

    [AvaloniaFact]
    public void Frame_navigation_keys_never_replace_existing_shortcuts()
    {
        var w = Studio(frames: 5);
        var doc = w.Session.Document;
        Key(w, K.OemPeriod);
        Key(w, K.Right);
        Assert.Equal(2, doc.CurrentFrame);
        Key(w, K.Left);
        Assert.Equal(1, doc.CurrentFrame);
        Key(w, K.End);
        Assert.Equal(4, doc.CurrentFrame);
        Key(w, K.OemPeriod); // wraps around
        Assert.Equal(0, doc.CurrentFrame);
        Key(w, K.Home);
        Assert.Equal(0, doc.CurrentFrame);

        // With the Move tool the arrows still nudge.
        Line(w, 40, 40, 80, 40);
        Key(w, K.V);
        Assert.Equal(ToolKind.Move, w.Session.Tool);
        Key(w, K.Right);
        Assert.Equal(0, doc.CurrentFrame);
        Assert.Equal("Nudge", doc.History.NextUndoName);

        // During a transform, Enter applies it instead of playing.
        w.FreeTransform();
        Pump();
        Assert.NotNull(w.Session.ActiveTransform);
        Key(w, K.Enter);
        Assert.Null(w.Session.ActiveTransform);
        Assert.False(w.Animator.IsPlaying);

        // O toggles onion skins only here; in Painting the animation keys do nothing.
        bool onion = w.Session.Onion.Enabled;
        Key(w, K.O);
        Assert.NotEqual(onion, w.Session.Onion.Enabled);
        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        int frames = doc.FrameCount;
        Key(w, K.N);
        Key(w, K.Enter);
        Key(w, K.OemPeriod);
        Assert.Equal(frames, doc.FrameCount);
        Assert.False(w.Animator.IsPlaying);
        Assert.Equal(0, doc.CurrentFrame);
        w.Close();
    }

    [AvaloniaFact]
    public void Drawing_on_a_new_frame_leaves_the_others_alone_and_undo_returns_to_that_frame()
    {
        var w = Studio();
        var doc = w.Session.Document;
        Line(w, 20, 30, 180, 30);
        Key(w, K.N);
        Line(w, 20, 80, 180, 80);
        Assert.True(A(CelAt(w, 0), 100, 30) > 0);
        Assert.Equal(0, A(CelAt(w, 0), 100, 80));
        Assert.True(A(CelAt(w, 1), 100, 80) > 0);
        Assert.Equal(0, A(CelAt(w, 1), 100, 30));

        Key(w, K.Home);
        Assert.Equal(0, doc.CurrentFrame);
        Key(w, K.Z, RawInputModifiers.Control);
        Assert.Equal(1, doc.CurrentFrame); // undo shows where the change happens
        Assert.Equal(0, A(CelAt(w, 1), 100, 80));
        Assert.True(A(CelAt(w, 0), 100, 30) > 0);
        w.Close();
    }

    [AvaloniaFact]
    public void A_held_drawing_says_so_and_make_independent_separates_it()
    {
        var w = Studio();
        var doc = w.Session.Document;
        var layer = doc.ActiveLayer;
        Line(w, 20, 30, 180, 30);
        w.Session.ExtendHold(layer, 0);
        Pump();
        Assert.Equal(2, doc.FrameCount);
        Key(w, K.OemPeriod);
        Assert.Same(CelAt(w, 0), CelAt(w, 1));
        Assert.True(Has(w, "HoldHint"));
        Assert.True(TextShown(w, "Held 2 frames"));

        Click(w, Find<Button>(w, "HoldHint"));
        Assert.NotSame(CelAt(w, 0), CelAt(w, 1));
        Assert.False(Has(w, "HoldHint"));
        Line(w, 20, 90, 180, 90);
        Assert.True(A(CelAt(w, 1), 100, 90) > 0);
        Assert.Equal(0, A(CelAt(w, 0), 100, 90)); // the first frame's drawing is unchanged
        w.Close();
    }

    // ================================================================== timeline

    [AvaloniaFact]
    public void Timeline_clicks_scrub_drag_holds_and_opens_context_menus()
    {
        var w = Studio(frames: 4);
        var doc = w.Session.Document;
        var layer = doc.ActiveLayer;
        Line(w, 20, 30, 180, 30);
        var grid = Grid(w);

        Click(w, grid, MouseButton.Left); // anywhere on the grid is fine; now pick an exact cell
        var cell = GridToWindow(w, grid.CellCenter(layer, 2));
        w.MouseDown(cell, MouseButton.Left);
        w.MouseUp(cell, MouseButton.Left);
        Pump();
        Assert.Equal(2, doc.CurrentFrame);

        // Scrub along the ruler.
        var r0 = GridToWindow(w, grid.RulerPoint(0));
        w.MouseMove(r0);
        w.MouseDown(r0, MouseButton.Left);
        Assert.Equal(0, doc.CurrentFrame);
        w.MouseMove(GridToWindow(w, grid.RulerPoint(3)), RawInputModifiers.LeftMouseButton);
        Assert.Equal(3, doc.CurrentFrame);
        w.MouseUp(GridToWindow(w, grid.RulerPoint(3)), MouseButton.Left);
        Pump();

        // Drag the first drawing's hold handle two frames further.
        var h = GridToWindow(w, grid.HoldHandlePoint(layer, 0));
        double cw = grid.CellWidthNow;
        w.MouseMove(h);
        w.MouseDown(h, MouseButton.Left);
        for (int i = 1; i <= 8; i++) w.MouseMove(h + new Vector(cw * 2 * i / 8, 0), RawInputModifiers.LeftMouseButton);
        w.MouseUp(h + new Vector(cw * 2, 0), MouseButton.Left);
        Pump();
        Assert.Equal((0, 2), doc.HoldRange(layer, 0));
        Assert.Equal(4, doc.FrameCount); // the hold filled empty cells; the animation did not grow

        // Right-click a cell: drawing and frame commands.
        var c1 = GridToWindow(w, grid.CellCenter(layer, 1));
        w.MouseMove(c1);
        w.MouseDown(c1, MouseButton.Right);
        w.MouseUp(c1, MouseButton.Right);
        Pump();
        Pump();
        Assert.True(TextShown(w, "Make independent"));
        Assert.True(TextShown(w, "Insert blank frame before"));
        UiKit.HideOpenFlyouts();
        Pump();
        w.Close();
    }

    [AvaloniaFact]
    public void Timeline_collapses_resizes_and_remembers_its_size()
    {
        var w = Studio(frames: 3);
        var panel = Find<TimelinePanel>(w, "Timeline");
        Click(w, Find<Button>(w, "TimelineCollapse"));
        Assert.True(w.AnimationState.TimelineCollapsed);
        Assert.Equal(TimelinePanel.BarHeight, panel.Bounds.Height, 1);
        Assert.False(Has(w, "TimelineGrid"));
        Assert.True(Has(w, "PlayButton")); // the everyday controls stay

        Click(w, Find<Button>(w, "TimelineCollapse"));
        Assert.False(w.AnimationState.TimelineCollapsed);
        panel.SetTimelineHeight(260);
        Pump();
        Assert.Equal(260, panel.Bounds.Height, 1);
        w.SetWorkspace(WorkspaceKind.Editing);
        w.SetWorkspace(WorkspaceKind.Animation);
        Pump();
        Assert.Equal(260, Find<TimelinePanel>(w, "Timeline").Bounds.Height, 1);
        w.Close();
    }

    // ================================================================== playback and onion skins

    [AvaloniaFact]
    public void Playback_follows_the_clock_pauses_for_edits_and_stop_returns_to_the_start()
    {
        var w = Studio(frames: 6);
        w.Animator.ManualClock = true; // the test plays the clock
        var doc = w.Session.Document;
        Line(w, 20, 30, 60, 30);
        w.Session.GoToFrame(2);
        Key(w, K.Enter);
        Assert.True(w.Animator.IsPlaying);
        Assert.False(doc.OnionVisible); // never onion skins while playing
        w.Animator.Tick(0);
        Assert.Equal(2, doc.CurrentFrame);
        w.Animator.Tick(90);
        Assert.Equal(3, doc.CurrentFrame);
        w.Animator.Tick(250); // three frames of 83 ms: frames are skipped, never queued
        Assert.Equal(5, doc.CurrentFrame);
        w.Animator.Tick(420);
        Assert.Equal(1, doc.CurrentFrame); // looped
        Key(w, K.Escape);
        Assert.False(w.Animator.IsPlaying);
        Assert.Equal(2, doc.CurrentFrame);

        // Touching the canvas pauses on the frame being shown; the stroke goes there.
        Key(w, K.Enter);
        w.Animator.Tick(170);
        Assert.Equal(4, doc.CurrentFrame);
        Line(w, 20, 90, 180, 90);
        Assert.False(w.Animator.IsPlaying);
        Assert.Equal(4, doc.CurrentFrame);
        Assert.True(A(CelAt(w, 4), 100, 90) > 0);

        // Without looping, playback ends on the last frame.
        w.Session.SetTiming(12, loop: false, pingPong: false);
        w.Session.GoToFrame(0);
        w.Animator.Play();
        w.Animator.Tick(100000);
        Assert.False(w.Animator.IsPlaying);
        Assert.Equal(5, doc.CurrentFrame);
        w.Close();
    }

    [AvaloniaFact]
    public void Onion_skins_show_only_in_animation_studio_and_never_reach_exports()
    {
        var w = Studio(paper: null);
        var doc = w.Session.Document;
        Line(w, 20, 30, 180, 30);
        Key(w, K.N);
        Pump();
        Assert.True(doc.OnionVisible);
        Key(w, K.O);
        Pump();
        Assert.False(doc.OnionVisible);
        Key(w, K.O);
        Pump();
        Assert.True(doc.OnionVisible);

        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        Assert.False(doc.OnionVisible);
        w.SetWorkspace(WorkspaceKind.Animation);
        Pump();
        Assert.True(doc.OnionVisible);

        // Frame 2 is empty: exporting it gives a transparent frame although the onion shows frame 1 under it.
        var frames = AnimationExport.RenderFrames(doc, includePaper: false);
        Assert.Equal(0, Pixel.A(frames[1].Image[100, 30]));
        Assert.Equal(0, Pixel.A(doc.ActiveLayer.Image[100, 30])); // and nothing was painted into the drawing
        Assert.True(Pixel.A(frames[0].Image[100, 30]) > 0);
        w.Close();
    }

    // ================================================================== customize workspace

    [AvaloniaFact]
    public void Customize_workspace_adds_optional_controls_remembers_them_and_resets()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        var dialog = w.CustomizeWorkspaceAsync();
        Pump();
        Click(w, Find<Button>(w, "Customize_frame-buttons"));
        Click(w, Find<Button>(w, "Customize_preview-panel"));
        Click(w, Find<Button>(w, "Customize_layers-dock"));
        Click(w, Find<Button>(w, "Customize_options-bar"));
        w.Dialogs.Close("ok");
        Pump();
        Assert.True(dialog.IsCompleted);
        Assert.True(Has(w, "FirstFrameButton") && Has(w, "NextFrameButton"));
        Assert.True(Has(w, "AnimationPreview"));
        Assert.True(Has(w, "AnimationDock"));
        Assert.False(Has(w, "OptionsBar"));
        Assert.True(w.WsState.Options["frame-buttons"]);

        // Each workspace has its own choices.
        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        Assert.True(Has(w, "OptionsBar"));
        Assert.True(Has(w, "ViewChip"));
        w.WsState.SetShown(CustomOptions.ViewChip, WorkspaceKind.Painting, false);
        w.ApplyCustomization();
        Pump();
        Assert.False(Has(w, "ViewChip"));
        w.SetWorkspace(WorkspaceKind.Animation);
        Pump();
        Assert.True(Has(w, "FirstFrameButton"));

        // Reset to default.
        var again = w.CustomizeWorkspaceAsync();
        Pump();
        w.Dialogs.Close("reset");
        Pump();
        w.Dialogs.Close("ok");
        Pump();
        Assert.True(again.IsCompleted);
        Assert.Empty(w.WsState.Options);
        Assert.False(Has(w, "FirstFrameButton") || Has(w, "AnimationPreview") || Has(w, "AnimationDock"));
        Assert.True(Has(w, "OptionsBar"));
        w.Close();
    }

    [Fact]
    public void Customize_choices_survive_a_restart_and_unknown_options_are_ignored()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "settings.json");
        var s = new AppSettings();
        s.StateFor(WorkspaceKind.Animation).SetShown(CustomOptions.FrameDuration, WorkspaceKind.Animation, true);
        s.StateFor(WorkspaceKind.Animation).SetShown("not-a-real-option", WorkspaceKind.Animation, true);
        s.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.True(loaded.StateFor(WorkspaceKind.Animation).IsShown(CustomOptions.FrameDuration, WorkspaceKind.Animation));

        // A settings file from a newer DoodleNook may name options this version doesn't know.
        var json = File.ReadAllText(path).Replace("\"frame-duration\": true", "\"frame-duration\": true, \"hologram-panel\": true");
        File.WriteAllText(path, json);
        var future = AppSettings.Load(path).StateFor(WorkspaceKind.Animation);
        Assert.False(future.IsShown("hologram-panel", WorkspaceKind.Animation));
        Assert.True(future.IsShown(CustomOptions.FrameDuration, WorkspaceKind.Animation));
        Assert.False(future.IsShown(CustomOptions.FrameDuration, WorkspaceKind.Painting)); // not offered there
        Directory.Delete(dir, true);
    }

    // ================================================================== exports

    private static MainWindow ThreeFrameSprite()
    {
        var w = Studio(16, 16, 3, pixel: true);
        for (int f = 0; f < 3; f++)
        {
            w.Session.GoToFrame(f);
            Line(w, 2.5 + 3 * f, 2.5 + 3 * f, 3.5 + 3 * f, 2.5 + 3 * f);
        }
        return w;
    }

    [AvaloniaFact]
    public async Task Export_animation_writes_a_sprite_sheet_with_json_a_png_sequence_and_a_gif()
    {
        var w = ThreeFrameSprite();
        var dir = TempDir();
        var sheetPath = Path.Combine(dir, "walk_sheet.png");
        await w.ExportAnimationToPathAsync(sheetPath, new AnimationExportSettings(AnimationExportFormat.SpriteSheet, SheetLayout.Horizontal, Json: true));
        var sheet = PngCodec.Decode(File.ReadAllBytes(sheetPath));
        Assert.Equal((48, 16), (sheet.Width, sheet.Height));
        Assert.Equal(255, Pixel.A(sheet[2, 2]));
        Assert.Equal(255, Pixel.A(sheet[16 + 5, 5]));
        Assert.Equal(255, Pixel.A(sheet[32 + 8, 8]));
        Assert.Equal(0, Pixel.A(sheet[16 + 2, 2])); // frames don't bleed into each other
        using (var meta = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(sheetPath, ".json"))))
        {
            var frames = meta.RootElement.GetProperty("frames");
            Assert.Equal(3, frames.GetArrayLength());
            Assert.Equal(16, frames[1].GetProperty("x").GetInt32());
            Assert.Equal("walk_sheet.png", meta.RootElement.GetProperty("image").GetString());
        }

        await w.ExportAnimationToPathAsync(sheetPath, new AnimationExportSettings(AnimationExportFormat.SpriteSheet, SheetLayout.Grid, Columns: 2, Spacing: 1, Scale: 2, Json: false));
        var grid = PngCodec.Decode(File.ReadAllBytes(sheetPath));
        Assert.Equal((65, 65), (grid.Width, grid.Height));

        var seq = Path.Combine(dir, "walk.png");
        await w.ExportAnimationToPathAsync(seq, new AnimationExportSettings(AnimationExportFormat.PngSequence));
        for (int i = 1; i <= 3; i++) Assert.True(File.Exists(Path.Combine(dir, $"walk_000{i}.png")));
        Assert.False(File.Exists(seq));

        var gif = Path.Combine(dir, "walk.gif");
        await w.ExportAnimationToPathAsync(gif, new AnimationExportSettings(AnimationExportFormat.Gif, Scale: 4));
        using (var codec = SKCodec.Create(gif))
        {
            Assert.Equal(3, codec.FrameCount);
            Assert.Equal(64, codec.Info.Width);
            Assert.InRange(codec.FrameInfo[0].Duration, 80, 90);
        }
        Directory.Delete(dir, true);
        w.Close();
    }

    [AvaloniaFact]
    public void Export_dialog_shows_only_the_chosen_formats_options_and_returns_them()
    {
        var w = ThreeFrameSprite();
        var task = w.Dialogs.AskExportAnimationAsync(new AnimationExportSettings(), 3, 16, 16, hasPaper: false);
        Pump();
        Assert.True(TextShown(w, "Layout") && TextShown(w, "JSON metadata"));
        Assert.False(TextShown(w, "Loop forever"));
        var gifButton = w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible
            && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Animated GIF"));
        Click(w, gifButton);
        Assert.False(TextShown(w, "Layout"));
        Assert.True(TextShown(w, "Loop forever"));
        Assert.True(TextShown(w, "One GIF of 16 × 16 with 3 frames"));
        w.Dialogs.Close("ok");
        Pump();
        Assert.True(task.IsCompleted);
        Assert.Equal(AnimationExportFormat.Gif, task.Result!.Format);
        w.Close();
    }

    // ================================================================== saving and recovery

    [AvaloniaFact]
    public async Task Autosave_and_crash_recovery_bring_back_unsaved_frames()
    {
        var dir = TempDir();
        var w = Studio();
        var store = new RecoveryStore(dir);
        w.StartRecovery(store);
        Assert.False(await w.AutosaveNowAsync()); // nothing unsaved yet
        Line(w, 20, 30, 180, 30);
        Key(w, K.N);
        Line(w, 20, 80, 180, 80);
        Assert.True(await w.AutosaveNowAsync());
        Assert.False(await w.AutosaveNowAsync()); // nothing new since the last copy
        store.SimulateCrash(); // DoodleNook "crashes": the lock goes, the copy stays

        var w2 = Open(1280, 800, WorkspaceKind.QuickSketch);
        var store2 = new RecoveryStore(dir);
        w2.StartRecovery(store2);
        Assert.Single(store2.FindRecoverable());
        var offer = w2.OfferRecoveryAsync();
        Pump();
        Assert.True(TextShown(w2, "Unsaved work was kept"));
        w2.Dialogs.Close("recover");
        await offer;
        var doc = w2.Session.Document;
        Assert.Equal(2, doc.FrameCount);
        Assert.True(doc.IsDirty);
        Assert.Equal(WorkspaceKind.Animation, w2.Workspace.Kind);
        Assert.True(Pixel.A(doc.CelAt(doc.ActiveLayer, 1)!.Image[100, 80]) > 0);
        Assert.Empty(store2.FindRecoverable()); // the old copy is gone; this session keeps its own

        // Saving removes this session's copy too.
        var path = Path.Combine(dir, "recovered.dnk");
        Assert.True(await w2.SaveToPathAsync(path));
        Assert.False(File.Exists(store2.OwnDnk));
        store.Dispose();
        store2.EndSession();
        Directory.Delete(dir, true);
        w2.Close();
    }

    [AvaloniaFact]
    public async Task Discarding_a_recovery_copy_removes_it()
    {
        var dir = TempDir();
        var w = Studio();
        var store = new RecoveryStore(dir);
        w.StartRecovery(store);
        Line(w, 20, 30, 180, 30);
        Assert.True(await w.AutosaveNowAsync());
        store.SimulateCrash();

        var w2 = Open(1280, 800, WorkspaceKind.Painting);
        var store2 = new RecoveryStore(dir);
        w2.StartRecovery(store2);
        var offer = w2.OfferRecoveryAsync();
        Pump();
        w2.Dialogs.Close("discard");
        await offer;
        Assert.Empty(store2.FindRecoverable());
        Assert.False(w2.Session.Document.IsDirty);
        store2.EndSession();
        Directory.Delete(dir, true);
        w2.Close();
    }

    // ================================================================== samples

    [AvaloniaFact]
    public async Task Sample_animations_open_ready_to_play_in_their_style()
    {
        var w = Open(1280, 800, WorkspaceKind.QuickSketch);
        await w.OpenSampleAsync(SampleAnimation.BouncingBall);
        Assert.Equal(WorkspaceKind.Animation, w.Workspace.Kind);
        Assert.False(w.PixelAnimation);
        Assert.Equal(12, w.Session.Document.FrameCount);
        Assert.False(w.Session.Document.IsDirty);
        Assert.Null(w.Session.FilePath); // saving asks for a name: the sample stays as shipped

        await w.OpenSampleAsync(SampleAnimation.PixelSlime);
        Assert.True(w.PixelAnimation);
        Assert.Equal(8, w.Session.Document.FrameCount);
        Key(w, K.Enter);
        Assert.True(w.Animator.IsPlaying);
        Key(w, K.Enter);
        Assert.False(w.Animator.IsPlaying);
        w.Close();
    }
}
