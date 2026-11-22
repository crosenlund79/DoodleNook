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

    /// <summary>Mission 016: the preview showed the scene behind the figure, so references inserted earlier (and the
    /// rest of the artwork) looked like part of the chosen cycle. It now shows the canvas as plain paper with only the
    /// chosen cycle on it; dragging still places the feet on the canvas.</summary>
    [AvaloniaFact]
    public async Task The_preview_shows_only_the_chosen_reference_not_the_scene_or_earlier_references()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        // A scene: a magenta block (the artwork) on white paper, eight frames.
        var frames = Enumerable.Range(0, 8).Select(_ =>
        {
            var img = new DoodleNook.Engine.Imaging.RasterImage(640, 360);
            for (int y = 40; y < 140; y++)
                for (int x = 470; x < 620; x++) img[x, y] = DoodleNook.Engine.Imaging.Pixel.Opaque(new DoodleNook.Engine.Imaging.ColorRgb(255, 0, 255));
            return img;
        }).ToList();
        var doc = DoodleNook.Engine.Files.SpriteSheetImport.NormalDocument(frames, 12);
        doc.SetPaper(DoodleNook.Engine.Imaging.ColorRgb.White, true);
        w.Session.ReplaceDocument(doc, null);
        Pump();
        // A reference inserted earlier, on the left, fully opaque so it would be obvious.
        ReferenceInsert.Insert(doc, new ReferenceCycleSettings("walk", ReferenceView.Side, ReferenceFacing.Right, 250, 90, 330), opacity: 1);
        Assert.Equal(2, doc.Layers.Count);
        Pump();

        var insert = w.InsertReferenceCycleAsync();
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen && Has(w, "ReferenceCycleDialog"));
        Pump();
        var stage = Find<Avalonia.Controls.Canvas>(w, "ReferencePreview");
        using (var shot = w.CaptureRenderedFrame()!)
        {
            shot.Save(Path.Combine(Screenshots.OutDir, "m016-reference-preview.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            // The stage shows the 640 × 360 canvas at 0.75: 480 × 270, centred in 500 × 270.
            int magenta = 0, earlier = 0, paper = 0;
            var origin = stage.TranslatePoint(new Point(10, 0), w)!.Value;
            for (int y = 4; y < 266; y += 2) // inside the page's thin outline
                for (int x = 4; x < 476; x += 2)
                {
                    var (r, g, b, _) = Screenshots.ReadPixel(shot, (int)origin.X + x, (int)origin.Y + y);
                    if (r > 200 && g < 80 && b > 200) magenta++;
                    // Left of the middle figure (which stands at x = 320 of 640): only paper is expected there.
                    if (x < 140) { if (r < 235 || g < 235 || b < 235) earlier++; else paper++; }
                }
            Assert.Equal(0, magenta);  // no scene artwork
            Assert.Equal(0, earlier);  // no earlier reference
            Assert.True(paper > 4000); // the paper itself is shown
        }
        // Only the page and the chosen figure are on the stage; dragging still places the feet on the canvas.
        Assert.DoesNotContain(stage.Children.OfType<Image>(), i => i.Name != "ReferencePreviewFigure");
        DragOn(w, stage, new Point(250, 200), new Point(130, 240));
        w.Dialogs.Close("ok");
        Assert.True(await insert);
        var added = doc.Layers.Last(l => l.ReferenceCycle is not null && l.ReferenceCycle.AnchorX != 90);
        Assert.InRange(added.ReferenceCycle!.AnchorX, 150, 175); // (130 − 10) / 0.75 = 160
        Assert.Equal(3, doc.Layers.Count);
        Assert.Equal(2, doc.Layers.Count(l => l.ReferenceCycle is not null)); // both references are in the document
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
