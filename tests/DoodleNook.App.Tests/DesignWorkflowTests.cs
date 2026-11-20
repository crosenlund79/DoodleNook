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
    public void Workflow_A_game_menu_button()
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

        var back = SaveAndReopen(w, "menu");
        Assert.Equal(doc.Flatten().Pixels, back.Flatten().Pixels);
        Assert.Equal(LayerKind.Text, back.Layers.Single(l => l.Name == "PLAY").Kind);
        // The export (a flat picture): gold button face, dark label, text still editable in the project.
        var flat = back.Flatten();
        Assert.Equal((233, 169, 77, 255), Pixel.ToStraight(flat[270, 300]));
        Assert.Contains(Enumerable.Range((int)labelBox.X, (int)labelBox.Width), x => Pixel.ToStraight(flat[x, (int)labelBox.Center.Y]) is (43, 34, 48, 255));
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

        // A guide from the ruler; neither guides nor perspective lines are part of the picture.
        doc.SetGuides([new Guide(false, 300)], "Add guide");
        var flat = doc.Flatten();
        Assert.Equal((255, 255, 255, 255), Pixel.ToStraight(flat[100, 300]));
        var back = SaveAndReopen(w, "perspective");
        Assert.Equal(doc.Perspective, back.Perspective);
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

        // Group visibility and opacity act on the whole character; undo puts it back.
        s.SetLayerProperty(group, p => p with { Visible = false });
        Assert.Equal((255, 255, 255, 255), Pixel.ToStraight(doc.Flatten()[150, 250]));
        w.Session.Undo();
        s.SetLayerProperty(group, p => p with { Opacity = 0.5 });
        var half = Pixel.ToStraight(doc.Flatten()[150, 250]);
        Assert.True(half.R > shirt.R && half.R < 255);

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
    public void Workflow_E_title_on_an_existing_animation()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.UseLibrary(new CreativeLibrary(Path.Combine(_dir, "Library")));
        w.Session.ReplaceDocument(SampleAnimations.BouncingBall(), null);
        Pump();
        var doc = w.Session.Document;
        int frames = doc.FrameCount;
        var timing = Enumerable.Range(0, frames).Select(doc.Animation!.DurationOverride).ToList();
        var before = Enumerable.Range(0, frames).Select(f => doc.RenderFrame(f).Pixels).ToList();

        // The Text tool is one Customize-workspace tick away in Animation Studio.
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
        Pump();

        // Every frame shows it; timing and drawings are untouched; frames still work.
        var titleArea = title.ContentBounds;
        for (int f = 0; f < frames; f++)
        {
            var frame = doc.RenderFrame(f).Pixels;
            int w0 = doc.Width;
            bool titleShown = false;
            for (int y = titleArea.Y; y < titleArea.Bottom && !titleShown; y++)
                for (int x = titleArea.X; x < titleArea.Right; x++)
                    if (frame[y * w0 + x] != before[f][y * w0 + x])
                    {
                        titleShown = true;
                        break;
                    }
            Assert.True(titleShown, $"frame {f} has no title");
        }
        Assert.Equal(timing, Enumerable.Range(0, frames).Select(doc.Animation!.DurationOverride).ToList());
        w.Session.NewFrame(false);
        Assert.Equal(frames + 1, doc.FrameCount);
        w.Session.Undo();

        var back = SaveAndReopen(w, "animated");
        Assert.Equal(frames, back.FrameCount);
        Assert.Equal(timing, Enumerable.Range(0, frames).Select(back.Animation!.DurationOverride).ToList());
        for (int f = 0; f < frames; f++) Assert.Equal(doc.RenderFrame(f).Pixels, back.RenderFrame(f).Pixels);
        // Removing the title gives back exactly the original frames.
        back.DeleteLayer(back.Layers.Single(l => l.Kind == LayerKind.Text));
        for (int f = 0; f < frames; f++) Assert.Equal(before[f], back.RenderFrame(f).Pixels);
        w.Close();
    }
}
