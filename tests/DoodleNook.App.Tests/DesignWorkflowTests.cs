using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using DoodleNook.Engine.Samples;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 008 workflows A–E, end to end through the window: a game menu button, a perspective sketch, a character
/// built from groups, masks and an adjustment layer, a palette-locked design, and adding a title to an existing animation.
/// </summary>
public sealed class DesignWorkflowTests : IDisposable
{
    private static readonly ColorRgb Gold = ColorRgb.Parse("#E9A94D"), Ink = ColorRgb.Parse("#2B2230"), Skin = ColorRgb.Parse("#E8B996"),
        Shirt = ColorRgb.Parse("#3F6A82");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-flow-" + Guid.NewGuid().ToString("N")[..8]);

    public DesignWorkflowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MainWindow Window(WorkspaceKind ws, int width, int height)
    {
        var w = Open(1280, 800, ws);
        w.UseLibrary(new CreativeLibrary(Path.Combine(_dir, "Library")));
        w.Session.ReplaceDocument(new Document(width, height, ColorRgb.White, "Background"), null);
        w.Session.SetAssistOptions(w.Session.Assist with { Snap = false });
        Pump();
        w.Canvas.FitToView();
        Pump();
        return w;
    }

    private Document SaveAndReopen(MainWindow w, string name)
    {
        var path = Path.Combine(_dir, name + ".dnk");
        DnkFile.Save(w.Session.Document, path);
        return DnkFile.Load(path);
    }

    private static Button Entry(MainWindow w, string text) =>
        w.GetVisualDescendants().OfType<Button>().Last(b => b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

    private static void Line(MainWindow w, ColorRgb colour, double size, params (double X, double Y)[] points)
    {
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new BrushSettings { Size = size, Hardness = 1, Opacity = 1 }, "pencil");
        w.Session.SetForeground(colour);
        Drag(w, MouseButton.Left, RawInputModifiers.None, points);
    }

    // ================================================================== A — a game menu button

    [AvaloniaFact]
    public async Task Workflow_A_game_menu_button()
    {
        var w = Window(WorkspaceKind.Painting, 800, 600);
        var doc = w.Session.Document;
        var s = w.Session;
        // A rounded, outlined button.
        s.SetForeground(Gold);
        s.SetBackground(Ink);
        s.SelectTool(ToolKind.Shape);
        s.SetDesignShapeOptions(s.DesignShape with { Editable = true, Kind = ShapeType.RoundedRectangle, Fill = true, Outline = true, OutlineWidth = 6, CornerRadius = 30 });
        Drag(w, MouseButton.Left, RawInputModifiers.None, (250, 250), (400, 300), (550, 350));
        var button = doc.ActiveLayer;
        Assert.Equal(LayerKind.Shape, button.Kind);
        // A soft shadow from the Effects pop-over.
        Click(w, Find<Button>(w, "EffectsButton"));
        Click(w, Entry(w, "Shadow"));
        Assert.NotNull(((ShapeContent)button.Content!).Shadow);
        UiKit.HideOpenFlyouts();
        Pump();

        // The label: typed with the Text tool, then centred on the button.
        s.SetForeground(Ink);
        s.SelectTool(ToolKind.Text);
        s.SetTextOptions(s.Text with { Font = "Inter", Size = 48, Bold = true });
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 290));
        Find<TextBox>(w, "TextEditorBox").Text = "PLAY";
        Pump();
        w.TextEditor.Finish();
        var label = doc.ActiveLayer;
        Assert.Equal("PLAY", label.Name);
        Menu(w, "Layer", "Text");
        DesignWindowTests.SubMenu(w, "Centre on the layer below");
        var buttonBox = doc.BoxOf(button)!.Value;
        var labelBox = doc.BoxOf(label)!.Value;
        Assert.Equal(buttonBox.Center.X, labelBox.Center.X, 1);
        Assert.Equal(buttonBox.Center.Y, labelBox.Center.Y, 1);

        // Both in a "Menu" group (a layer dragged onto a group goes on top inside it, so the label goes in last).
        doc.SetActiveLayer(button);
        Key(w, Avalonia.Input.Key.G, RawInputModifiers.Control);
        var group = doc.Layers.Single(l => l.IsGroup);
        doc.MoveIntoGroup(label, group);
        Assert.Equal(label, doc.ChildrenOf(group.Id)[^1]);
        Assert.Equal(group.Id, label.ParentId);
        Assert.Equal(group.Id, button.ParentId);

        // 6–7. Save the project and reopen it.
        var path = Path.Combine(_dir, "menu.dnk");
        Assert.True(await w.SaveToPathAsync(path));
        var flatBefore = doc.Flatten().Pixels;
        Assert.True(await w.OpenPathAsync(path));
        Pump();
        var reopened = w.Session.Document;
        Assert.NotSame(doc, reopened);
        Assert.Equal(flatBefore, reopened.Flatten().Pixels);
        var label2 = reopened.Layers.Single(l => l.Name == "PLAY");
        Assert.Equal(LayerKind.Text, label2.Kind);
        Assert.NotNull(((ShapeContent)reopened.Layers.Single(l => l.Kind == LayerKind.Shape).Content!).Shadow);

        // 8. Modify the text: click it with the Text tool and type.
        w.Canvas.FitToView();
        Pump();
        s.SelectTool(ToolKind.Text);
        var (lx, ly) = reopened.BoxOf(label2)!.Value.Center;
        Drag(w, MouseButton.Left, RawInputModifiers.None, (lx, ly));
        Assert.Same(label2, w.TextEditor.Layer);
        Find<TextBox>(w, "TextEditorBox").Text = "START";
        Pump();
        w.TextEditor.Finish();
        Assert.Equal("START", ((TextContent)label2.Content!).Text);
        Assert.Equal("Edit text", reopened.History.NextUndoName);

        // 9. Export PNG: a flat picture with the gold button and the dark label.
        var png = Path.Combine(_dir, "menu.png");
        w.ExportToPath(png, DoodleNook.App.Files.ExportFormat.Png);
        var flat = DoodleNook.App.Files.ImageFiles.Decode(png);
        Assert.Equal(reopened.Flatten().Pixels, flat.Pixels);
        Assert.Equal((233, 169, 77, 255), Pixel.ToStraight(flat[270, 300]));
        var labelBox2 = reopened.BoxOf(label2)!.Value;
        Assert.Contains(Enumerable.Range((int)labelBox2.X, (int)labelBox2.Width), x => Pixel.ToStraight(flat[x, (int)labelBox2.Center.Y]) is (43, 34, 48, 255));
        w.Close();
    }

    // ================================================================== B — a perspective sketch

    [AvaloniaFact]
    public void Workflow_B_perspective_sketch()
    {
        var w = Window(WorkspaceKind.Painting, 600, 400);
        var doc = w.Session.Document;
        var s = w.Session;
        s.SelectTool(ToolKind.Brush);
        Pump();
        // Two-point perspective from the assistants pop-over.
        Click(w, Find<Button>(w, "AssistantsButton"));
        Click(w, Entry(w, "2-point"));
        Assert.Equal(2, doc.Perspective.Points);
        UiKit.HideOpenFlyouts();
        Pump();
        // Put the horizon where the sketch needs it (Move tool, numbered points).
        s.SelectTool(ToolKind.Move);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (doc.Perspective.V1X, doc.Perspective.V1Y), (40, 120), (20, 100));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (doc.Perspective.V2X, doc.Perspective.V2Y), (560, 120), (580, 100));
        Assert.Equal((20.0, 100.0, 580.0, 100.0), (Math.Round(doc.Perspective.V1X), Math.Round(doc.Perspective.V1Y), Math.Round(doc.Perspective.V2X), Math.Round(doc.Perspective.V2Y)));

        // A box corner: a vertical edge, then edges towards each vanishing point — all wobbly by hand.
        Line(w, Ink, 3, (300, 180), (303, 220), (298, 260), (301, 300));
        Line(w, Ink, 3, (300, 300), (250, 270), (200, 265), (150, 240));
        Line(w, Ink, 3, (300, 300), (350, 275), (400, 262), (450, 238));
        var img = doc.ActiveLayer.Image;
        Assert.True(Pixel.A(img[300, 260]) > 0);                     // the vertical held to x = 300
        Assert.Equal(0, Pixel.A(img[298 - 5, 260]));
        // The line from (300, 300) towards (20, 100): at x = 220, y ≈ 243 (not where the hand wobbled: 220, 268).
        Assert.True(Pixel.A(img[220, 243]) > 0);
        Assert.Equal(0, Pixel.A(img[220, 262]));
        // …and towards (580, 100): at x = 380, y ≈ 243.
        Assert.True(Pixel.A(img[380, 243]) > 0);
        Assert.Equal(0, Pixel.A(img[380, 262]));

        // 4–6. Turn perspective off: freehand drawing follows the hand again, nothing is held to a line.
        s.SelectTool(ToolKind.Brush);
        Pump();
        Click(w, Find<Button>(w, "AssistantsButton"));
        Click(w, Entry(w, "Off"));
        UiKit.HideOpenFlyouts();
        Pump();
        Assert.Equal(0, doc.Perspective.Points);
        var free = s.AddLayer();
        Line(w, Ink, 3, (100, 350), (150, 330), (200, 360), (250, 330), (300, 360));
        Assert.True(Pixel.A(free.Image[200, 360]) > 0); // the wobble is drawn as drawn
        Assert.True(Pixel.A(free.Image[150, 330]) > 0);
        Line(w, Ink, 3, (400, 380), (420, 330), (440, 380));
        Assert.True(Pixel.A(free.Image[420, 330]) > 0);

        // A guide from the ruler; neither guides nor perspective lines are part of the picture.
        doc.SetGuides([new Guide(false, 300)], "Add guide");
        var flat = doc.Flatten();
        Assert.Equal((255, 255, 255, 255), Pixel.ToStraight(flat[100, 300]));
        var back = SaveAndReopen(w, "perspective");
        Assert.Equal(0, back.Perspective.Points);
        Assert.Equal(doc.Guides, back.Guides);
        w.Close();
    }

    // ================================================================== C — a character from groups, masks and an adjustment layer

    [AvaloniaFact]
    public void Workflow_C_character_layers()
    {
        var w = Window(WorkspaceKind.Editing, 300, 400);
        var doc = w.Session.Document;
        var s = w.Session;
        var body = s.AddLayer();
        doc.SetLayerProperties(body, body.Properties with { Name = "Body" });
        Line(w, Shirt, 60, (150, 200), (150, 300), (150, 380));
        var head = s.AddLayer();
        doc.SetLayerProperties(head, head.Properties with { Name = "Head" });
        Line(w, Skin, 70, (150, 120), (151, 121));

        // Group them as "Character".
        Key(w, Avalonia.Input.Key.G, RawInputModifiers.Control);
        var group = doc.Layers.Single(l => l.IsGroup);
        doc.MoveIntoGroup(body, group);
        doc.SetLayerProperties(group, group.Properties with { Name = "Character" });

        // A mask hides the legs without erasing them (made from a selection).
        doc.SetActiveLayer(body);
        doc.SetSelection(SelectionMask.Rectangle(300, 400, new DoodleNook.Engine.Imaging.PixelRect(0, 0, 300, 330)), "Select");
        Menu(w, "Layer", "Mask");
        DesignWindowTests.SubMenu(w, "Mask from selection");
        Assert.True(body.HasMask);
        Assert.Equal((255, 255, 255, 255), Pixel.ToStraight(doc.Flatten()[150, 360]));
        Assert.NotEqual(0u, body.Image[150, 360]);

        // A warmer shirt: Hue/Saturation at the top of the group changes the character, not the background.
        doc.SetActiveLayer(doc.ChildrenOf(group.Id)[^1]);
        var adj = s.AddAdjustmentLayer(new HueSaturation(40, 0, 0));
        Assert.Equal(group.Id, adj.ParentId);
        var shirt = Pixel.ToStraight(doc.Flatten()[150, 250]);
        Assert.NotEqual((63, 106, 130, 255), shirt);
        Assert.Equal((255, 255, 255, 255), Pixel.ToStraight(doc.Flatten()[20, 20]));

        // Group visibility and opacity act on the whole character.
        var withAdjustment = doc.Flatten().Pixels;
        s.SetLayerProperty(group, p => p with { Visible = false });
        Assert.Equal((255, 255, 255, 255), Pixel.ToStraight(doc.Flatten()[150, 250]));
        s.SetLayerProperty(group, p => p with { Visible = true });
        s.SetLayerProperty(group, p => p with { Opacity = 0.5 });
        var half = Pixel.ToStraight(doc.Flatten()[150, 250]);
        Assert.True(half.R > shirt.R && half.R < 255);
        var finished = doc.Flatten().Pixels;

        // Undo and redo through everything: each step comes back exactly.
        var states = new List<uint[]>();
        int steps = doc.History.UndoCount;
        for (int i = 0; i < steps; i++)
        {
            states.Add(doc.Flatten().Pixels);
            Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        }
        Assert.DoesNotContain(doc.Layers, l => l.IsGroup || l.HasMask || l.Kind == LayerKind.Adjustment);
        for (int i = steps - 1; i >= 0; i--)
        {
            Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(states[i], doc.Flatten().Pixels);
        }
        Assert.Equal(finished, doc.Flatten().Pixels);
        Assert.NotEqual(withAdjustment, finished);

        var back = SaveAndReopen(w, "character");
        Assert.Equal(doc.Flatten().Pixels, back.Flatten().Pixels);
        Assert.True(back.Layers.Single(l => l.Name == "Body").HasMask);
        w.Close();
    }

    // ================================================================== D — a palette-locked design

    [AvaloniaFact]
    public void Workflow_D_palette_locked_design()
    {
        var w = Window(WorkspaceKind.Painting, 400, 300);
        var doc = w.Session.Document;
        var s = w.Session;
        var palette = new Palette("Four", [ColorRgb.White, Ink, Gold, Shirt]);
        doc.LockToPalette(palette);
        var c = doc.PaletteConformer!;
        s.SetForeground(ColorRgb.Parse("#D9A050")); // not a palette colour
        s.SelectTool(ToolKind.Shape);
        s.SetDesignShapeOptions(s.DesignShape with { Editable = true, Kind = ShapeType.Ellipse, Fill = true, Outline = false });
        Drag(w, MouseButton.Left, RawInputModifiers.None, (50, 50), (150, 100), (250, 150));
        var shape = doc.ActiveLayer;
        Assert.Equal(Gold, ((ShapeContent)shape.Content!).Fill); // the shape asks for the nearest palette colour
        doc.AddTextLayer(new TextContent("Locked", "Inter", 40, ColorRgb.Parse("#405F80"), X: 20, Y: 200));
        doc.AddAdjustmentLayer(new Levels(0, 255, 1.4, 0, 255));
        var flat = doc.Flatten();
        Assert.All(flat.Pixels, p => Assert.True(c.IsPaletteColour(p), $"{p:X8} is not a palette colour"));
        Assert.Contains(shape.Image.Pixels, p => Pixel.A(p) is > 0 and < 255); // soft edges keep their alpha
        // Alpha is exactly what the same shape has without the lock.
        var free = ShapeRasterizer.Render((ShapeContent)shape.Content!, doc.Width, doc.Height).Image;
        Assert.Equal(free.Pixels.Select(Pixel.A), shape.Image.Pixels.Select(Pixel.A));

        // The export decodes to exact palette colours.
        var png = PngCodec.Decode(PngCodec.Encode(flat));
        var allowed = palette.Colors.Select(x => (x.R, x.G, x.B)).ToHashSet();
        Assert.All(png.Pixels.Where(p => Pixel.A(p) == 255), p => Assert.Contains((Pixel.R(p), Pixel.G(p), Pixel.B(p)), allowed));

        var back = SaveAndReopen(w, "locked");
        Assert.True(back.PaletteLocked);
        Assert.Equal(doc.Flatten().Pixels, back.Flatten().Pixels);
        w.Close();
    }

    // ================================================================== E — a title on an existing animation

    [AvaloniaFact]
    public async Task Workflow_E_title_on_an_existing_animation()
    {
        // 1. A Mission 006/007 animation: the bouncing ball as a version 2 file, opened from disk.
        var old = Path.Combine(_dir, "bounce-v2.dnk");
        DnkFile.Save(SampleAnimations.BouncingBall(), old);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(old))
        using (var json = zip.GetEntry("document.json")!.Open())
            Assert.Equal(2, System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("version").GetInt32());
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.UseLibrary(new CreativeLibrary(Path.Combine(_dir, "Library")));
        w.Animator.ManualClock = true;
        Assert.True(await w.OpenPathAsync(old));
        Pump();
        var doc = w.Session.Document;
        int frames = doc.FrameCount;
        var timing = Enumerable.Range(0, frames).Select(doc.Animation!.DurationOverride).ToList();
        var before = Enumerable.Range(0, frames).Select(f => doc.RenderFrame(f).Pixels).ToList();
        var holds = Holds(doc);

        // 2. Playback.
        w.Session.GoToFrame(0);
        w.Animator.Play();
        w.Animator.Tick(1000 / doc.Animation.Fps * 1.5);
        Assert.Equal(1, doc.CurrentFrame);
        w.Animator.Stop();

        // 3. A compatible new layer: a title with the Text tool (one Customize-workspace tick in Animation Studio).
        Assert.DoesNotContain(ToolKind.Text, w.CurrentTools);
        w.WsState.SetShown(CustomOptions.TextTool, WorkspaceKind.Animation, true);
        w.ApplyCustomization();
        Assert.Contains(ToolKind.Text, w.CurrentTools);
        w.Session.SelectTool(ToolKind.Text);
        w.Session.SetForeground(Ink);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (40, 60));
        Find<TextBox>(w, "TextEditorBox").Text = "Bounce!";
        Pump();
        w.TextEditor.Finish();
        var title = doc.ActiveLayer;
        Assert.Equal(LayerKind.Text, title.Kind);
        Assert.False(title.IsTrack); // the same on every frame
        var area = title.ContentBounds;
        for (int f = 0; f < frames; f++)
        {
            var frame = doc.RenderFrame(f).Pixels;
            bool shown = false;
            for (int y = area.Y; y < area.Bottom && !shown; y++)
                for (int x = area.X; x < area.Right; x++)
                    if (frame[y * doc.Width + x] != before[f][y * doc.Width + x])
                    {
                        shown = true;
                        break;
                    }
            Assert.True(shown, $"frame {f} has no title");
        }
        Assert.Equal(timing, Enumerable.Range(0, frames).Select(doc.Animation!.DurationOverride).ToList());

        // 4. Export: an animated GIF with every frame and its timing.
        var gif = Path.Combine(_dir, "bounce.gif");
        GifEncoder.Save(AnimationExport.RenderFrames(doc, includePaper: true), gif, new GifOptions());
        using (var codec = SkiaSharp.SKCodec.Create(gif))
            Assert.Equal(frames, codec.FrameCount);

        // 5–6. Save and reopen: frame timing, the frames, and which frames share a drawing (holds) are preserved.
        var saved = Path.Combine(_dir, "bounce-v4.dnk");
        Assert.True(await w.SaveToPathAsync(saved));
        Assert.True(await w.OpenPathAsync(saved));
        Pump();
        var back = w.Session.Document;
        Assert.Equal(frames, back.FrameCount);
        Assert.Equal(timing, Enumerable.Range(0, frames).Select(back.Animation!.DurationOverride).ToList());
        Assert.Equal(holds, Holds(back));
        for (int f = 0; f < frames; f++) Assert.Equal(doc.RenderFrame(f).Pixels, back.RenderFrame(f).Pixels);
        back.DeleteLayer(back.Layers.Single(l => l.Kind == LayerKind.Text));
        for (int f = 0; f < frames; f++) Assert.Equal(before[f], back.RenderFrame(f).Pixels);
        w.Close();
    }

    /// <summary>For each drawing layer, which frames show the same drawing as the frame before (holds) or nothing.</summary>
    private static List<string> Holds(Document doc) =>
        doc.Layers.Where(l => l.IsTrack).Select(l => string.Concat(Enumerable.Range(0, doc.FrameCount).Select(f =>
            l.Track[f] is null ? '.' : f > 0 && ReferenceEquals(l.Track[f], l.Track[f - 1]) ? '=' : '#'))).ToList();

}
