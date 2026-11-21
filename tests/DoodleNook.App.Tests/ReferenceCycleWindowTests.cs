using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.References;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 013 — the Reference Animation Library in the window: inserting a cycle into an animation that already
/// has work in it, placing it in the preview, adjusting it later, and an empty animation.</summary>
public sealed class ReferenceCycleWindowTests
{
    private static async Task<MainWindow> SampleScene()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        await w.OpenSampleAsync(SampleAnimation.BouncingBall);
        Pump();
        return w;
    }

    private static List<Cel?> Cells(Document doc, Layer l) => Enumerable.Range(0, doc.FrameCount).Select(f => doc.CelAt(l, f)).ToList();

    private static void DragOn(MainWindow w, Control c, Point from, Point to)
    {
        w.CaptureRenderedFrame()?.Dispose();
        var a = c.TranslatePoint(from, w)!.Value;
        var b = c.TranslatePoint(to, w)!.Value;
        w.MouseMove(a);
        w.MouseDown(a, MouseButton.Left);
        w.MouseMove(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), RawInputModifiers.LeftMouseButton);
        w.MouseMove(b, RawInputModifiers.LeftMouseButton);
        w.MouseUp(b, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public async Task A_run_cycle_goes_into_a_started_animation_under_the_layer_you_draw_on_without_changing_anything_else()
    {
        var w = await SampleScene();
        var doc = w.Session.Document;
        var ball = doc.Layers.Single(l => l.Name == "Ball");
        doc.SetActiveLayer(ball);
        int frames = doc.FrameCount;
        var before = doc.Layers.ToDictionary(l => l, l => Cells(doc, l));
        w.CaptureRenderedFrame()?.Dispose();

        Menu(w, "Animation", "Insert reference cycle");
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen && Has(w, "ReferenceCycleDialog"));
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, Find<Button>(w, "ReferenceCycle_run"));
        Assert.Contains("8 drawings, made for 12 fps", Find<TextBlock>(w, "ReferencePlan").Text);
        Assert.Contains("Existing layers and drawings are not changed", Find<TextBlock>(w, "ReferencePlan").Text);
        // Facing left, then drag the feet to the left part of the scene.
        var facing = Find<Border>(w, "ReferenceFacing");
        Click(w, facing.GetVisualDescendants().OfType<Button>().Single(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "←")));
        var stage = Find<Avalonia.Controls.Canvas>(w, "ReferencePreview");
        DragOn(w, stage, new Point(250, 200), new Point(120, 220));
        // The preview plays.
        var figure = Find<Image>(w, "ReferencePreviewFigure");
        var first = figure.Source;
        await ArtistsCompanionWindowTests.Until(() => !ReferenceEquals(figure.Source, first), 3000);
        w.Dialogs.Close("ok");
        await ArtistsCompanionWindowTests.Until(() => doc.Layers.Count == before.Count + 1);

        var layer = doc.Layers.Single(l => l.ReferenceCycle is not null);
        Assert.Equal("Run reference (side)", layer.Name);
        Assert.Equal(doc.Layers.ToList().IndexOf(ball) - 1, doc.Layers.ToList().IndexOf(layer)); // just under the ball
        Assert.Same(ball, doc.ActiveLayer);
        Assert.True(layer.Locked && layer.Visible);
        Assert.Equal(ReferenceInsert.DefaultOpacity, layer.Opacity, 3);
        Assert.Equal(ReferenceFacing.Left, layer.ReferenceCycle!.Facing);
        Assert.True(layer.ReferenceCycle.AnchorX < doc.Width / 2.0, $"placed by the drag: {layer.ReferenceCycle.AnchorX}");
        Assert.Equal(frames, doc.FrameCount);                     // the scene was long enough
        foreach (var (l, cells) in before) Assert.Equal(cells, Cells(doc, l));
        Assert.All(Cells(doc, layer), c => Assert.NotNull(c));     // repeats to the end
        // Drawing on the ball still works; the reference is locked.
        Assert.True(w.Session.CanEditActiveLayer());
        doc.SetActiveLayer(layer);
        Assert.False(w.Session.CanEditActiveLayer());
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Adjusting_from_the_layer_menu_redraws_the_cycle_and_one_undo_takes_it_back()
    {
        var w = await SampleScene();
        var doc = w.Session.Document;
        var layer = ReferenceInsert.Insert(doc, new ReferenceCycleSettings("walk", ReferenceView.Side, ReferenceFacing.Right, 150, 320, 360), below: doc.ActiveLayer);
        var cells = Cells(doc, layer);
        doc.SetActiveLayer(layer);
        Pump();

        var adjust = w.AdjustReferenceCycleAsync();
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen && Has(w, "ReferenceCycleDialog"));
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("The layer's drawings are made again") == true);
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, w.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Three-quarter")));
        w.Dialogs.Close("ok");
        Assert.True(await adjust);
        Assert.Equal(ReferenceView.ThreeQuarter, layer.ReferenceCycle!.View);
        Assert.Equal(ReferenceFacing.DownRight, layer.ReferenceCycle.Facing);
        Assert.Equal("Walk reference (three-quarter)", layer.Name);
        Assert.NotEqual(cells[0]!.Image.Pixels, doc.CelAt(layer, 0)!.Image.Pixels);
        w.Undo();
        Assert.Equal(ReferenceView.Side, layer.ReferenceCycle!.View);
        Assert.Equal(cells, Cells(doc, layer));
        // The entry is on the timeline layer's menu, only for reference cycles.
        Assert.Contains(w.LayerTrackMenu(), e => e.Label == "Adjust reference cycle…" && e.Enabled);
        doc.SetActiveLayer(doc.Layers.Last(l => l.ReferenceCycle is null && l.IsTrack));
        Assert.Contains(w.LayerTrackMenu(), e => e.Label == "Adjust reference cycle…" && !e.Enabled);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_new_empty_animation_gets_the_whole_cycle_and_pixel_art_gets_hard_edges()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.Session.ReplaceDocument(Document.NewAnimation(64, 64, null, 1, 12, "Sprite"), null);
        w.AnimationState.PixelAnimation = true;
        Pump();
        var insert = w.InsertReferenceCycleAsync();
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen && Has(w, "ReferenceCycleDialog"));
        Assert.Contains("adds 7 frames", Find<TextBlock>(w, "ReferencePlan").Text);
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, Find<Button>(w, "ReferenceCycle_idle"));
        Assert.Contains("adds 5 frames", Find<TextBlock>(w, "ReferencePlan").Text);
        w.Dialogs.Close("ok");
        Assert.True(await insert);
        var doc = w.Session.Document;
        Assert.Equal(6, doc.FrameCount);
        var layer = doc.Layers[0];
        Assert.True(layer.ReferenceCycle!.HardEdges);
        Assert.Equal(48, layer.ReferenceCycle.Height); // three quarters of a small canvas
        Assert.All(Cells(doc, layer).OfType<Cel>().SelectMany(c => c.Image.Pixels), p =>
            Assert.True(Engine.Imaging.Pixel.A(p) is 0 or 255 or (>= 59 and <= 61), $"{p:X8}"));
        Assert.Equal("Sprite", doc.ActiveLayer.Name);
        // Playback shows the reference on every frame.
        for (int f = 0; f < doc.FrameCount; f++) Assert.False(doc.RenderFrame(f, includePaper: false).ContentBounds().IsEmpty);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Cancelling_changes_nothing_and_climb_is_not_offered_from_above()
    {
        var w = await SampleScene();
        var doc = w.Session.Document;
        int layers = doc.Layers.Count, undo = doc.History.UndoCount;
        var insert = w.InsertReferenceCycleAsync();
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen && Has(w, "ReferenceCycleDialog"));
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, w.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Top-down")));
        Assert.False(Find<Button>(w, "ReferenceCycle_climb").IsEnabled);
        Assert.True(Find<Button>(w, "ReferenceCycle_walk").IsEnabled);
        w.Dialogs.Close("cancel");
        Assert.False(await insert);
        Assert.Equal(layers, doc.Layers.Count);
        Assert.Equal(undo, doc.History.UndoCount);
        doc.MarkSaved();
        w.Close();
    }
}
