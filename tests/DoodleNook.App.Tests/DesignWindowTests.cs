using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 008 through the real window: text, editable shapes, handles, guides, groups, masks, adjustment layers
/// and the drawing assistants.</summary>
public sealed class DesignWindowTests : IDisposable
{
    internal static readonly ColorRgb Red = ColorRgb.Parse("#C8473E"), Blue = ColorRgb.Parse("#3F6A82"), Gold = ColorRgb.Parse("#E9A94D");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-design-" + Guid.NewGuid().ToString("N")[..8]);

    public DesignWindowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MainWindow Window(WorkspaceKind ws = WorkspaceKind.Painting, int width = 400, int height = 300)
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

    private static void ClickAt(MainWindow w, double x, double y, RawInputModifiers mods = RawInputModifiers.None) =>
        Drag(w, MouseButton.Left, mods, (x, y));

    private static (byte R, byte G, byte B, byte A) Shown(MainWindow w, int x, int y) => Pixel.ToStraight(w.Session.Document.Flatten()[x, y]);

    private static string? LastStep(MainWindow w) => w.Session.Document.History.NextUndoName;

    /// <summary>Clicks an entry of the sub-menu that is open now.</summary>
    internal static void SubMenu(MainWindow w, string entry)
    {
        Pump();
        var item = w.GetVisualDescendants().OfType<Button>().LastOrDefault(b => b.Classes.Contains("menu") && b.IsEffectivelyVisible
            && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith(entry, StringComparison.Ordinal) == true))
            ?? throw new InvalidOperationException($"No sub-menu entry {entry}");
        Click(w, item);
    }

    // ================================================================== text

    [AvaloniaFact]
    public void The_text_tool_adds_text_with_one_undo_step_and_clicking_text_edits_it()
    {
        var w = Window();
        var doc = w.Session.Document;
        w.Session.SelectTool(ToolKind.Text);
        w.Session.SetForeground(Red);
        w.Session.SetTextOptions(w.Session.Text with { Font = "Inter", Size = 40 });
        ClickAt(w, 60, 120);
        Assert.True(w.TextEditor.IsOpen);
        var box = Find<TextBox>(w, "TextEditorBox");
        box.Text = "Hello";
        Pump();
        var layer = doc.ActiveLayer;
        Assert.Equal(LayerKind.Text, layer.Kind);
        Assert.Equal("Hello", ((TextContent)layer.Content!).Text);
        Assert.False(layer.ContentBounds.IsEmpty); // drawn while typing
        w.TextEditor.Finish();
        Pump();
        Assert.Equal("Hello", layer.Name);
        Assert.Equal("Add text", LastStep(w));
        int steps = doc.History.UndoCount;

        // Click the text: the editor opens with it; the change is one "Edit text" step.
        var (cx, cy) = doc.BoxOf(layer)!.Value.Center;
        ClickAt(w, cx, cy);
        Assert.True(w.TextEditor.IsOpen);
        Assert.Same(layer, w.TextEditor.Layer);
        box.Text = "Hello there";
        Pump();
        w.TextEditor.Finish();
        Assert.Equal("Edit text", LastStep(w));
        Assert.Equal(steps + 1, doc.History.UndoCount);

        w.Session.Undo();
        Assert.Equal("Hello", ((TextContent)layer.Content!).Text);
        w.Session.Undo();
        Assert.DoesNotContain(doc.Layers, l => l.Kind == LayerKind.Text);

        // A new text left empty disappears without a trace.
        int before = doc.History.UndoCount;
        ClickAt(w, 300, 250);
        Assert.True(w.TextEditor.IsOpen);
        w.TextEditor.Finish();
        Assert.DoesNotContain(doc.Layers, l => l.Kind == LayerKind.Text);
        Assert.Equal(before, doc.History.UndoCount);
        w.Close();
    }

    [AvaloniaFact]
    public void Text_options_change_the_chosen_text_and_text_is_never_painted_on()
    {
        var w = Window();
        var doc = w.Session.Document;
        var layer = doc.AddTextLayer(new TextContent("Title", "Inter", 30, Blue, X: 20, Y: 20));
        w.Session.SelectTool(ToolKind.Text);
        Pump();
        Assert.True(Has(w, "FontBox"));
        Assert.True(Has(w, "EffectsButton"));
        w.Session.EditObject(c => ((TextContent)c) with { Bold = true, Align = TextAlign.Center }, "Bold");
        Assert.True(((TextContent)layer.Content!).Bold);

        // A brush stroke on a text layer is refused with a hint; nothing is painted.
        w.Session.SelectTool(ToolKind.Brush);
        var pixels = (uint[])layer.Image.Pixels.Clone();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (10, 150), (200, 150));
        Assert.Equal(pixels, layer.Image.Pixels);
        Assert.Contains("is text", w.LastToast);
        w.Close();
    }

    // ================================================================== handles

    [AvaloniaFact]
    public void The_move_tool_moves_resizes_and_turns_text_and_shapes_with_one_undo_step_each()
    {
        var w = Window();
        var doc = w.Session.Document;
        var shape = doc.AddShapeLayer(new ShapeContent(ShapeType.Rectangle, 100, 100, 100, 50, Gold));
        w.Session.SelectTool(ToolKind.Move);
        Pump();
        Assert.Same(shape, w.Canvas.Input!.FramedObject);

        Drag(w, MouseButton.Left, RawInputModifiers.None, (150, 125), (170, 135), (190, 145));
        var moved = (ShapeContent)shape.Content!;
        Assert.Equal(140, moved.X, 0);
        Assert.Equal(120, moved.Y, 0);
        Assert.Equal("Move", LastStep(w));

        // Bottom-right handle: bigger (the top-left corner stays put).
        Drag(w, MouseButton.Left, RawInputModifiers.None, (240, 170), (260, 180), (290, 200));
        var resized = (ShapeContent)shape.Content!;
        Assert.Equal(140, resized.X, 0);
        Assert.Equal(120, resized.Y, 0);
        Assert.Equal(150, resized.Width, 0);
        Assert.Equal(80, resized.Height, 0);
        Assert.Equal("Resize", LastStep(w));

        // Just outside the top-right corner: turn (Shift: 15° steps).
        var box = doc.BoxOf(shape)!.Value;
        var (cx, cy) = box.Center;
        Drag(w, MouseButton.Left, RawInputModifiers.Shift, (box.X + box.Width + 8, box.Y - 8), (cx + 60, cy + 60), (cx + 10, cy + 90));
        Assert.Equal("Rotate", LastStep(w));
        double rotation = ((ShapeContent)shape.Content!).Rotation;
        Assert.True(Math.Abs(rotation) > 10 && Math.Abs(rotation % 15) < 1e-6, $"rotation {rotation}");

        w.Session.Undo();
        w.Session.Undo();
        w.Session.Undo();
        Assert.Equal(100, ((ShapeContent)shape.Content!).X);

        // Arrow keys nudge objects; Ctrl+click picks another object.
        Key(w, Avalonia.Input.Key.Right);
        Assert.Equal(101, ((ShapeContent)shape.Content!).X);
        var text = doc.AddTextLayer(new TextContent("Pick me", "Inter", 24, Blue, X: 250, Y: 30));
        doc.SetActiveLayer(shape);
        ClickAt(w, 270, 40, RawInputModifiers.Control);
        Assert.Same(text, doc.ActiveLayer);
        w.Close();
    }

    [AvaloniaFact]
    public void Editable_shapes_are_drawn_with_the_shape_tool_and_snap_to_guides()
    {
        var w = Window();
        var doc = w.Session.Document;
        w.Session.SelectTool(ToolKind.Shape);
        w.Session.SetDesignShapeOptions(w.Session.DesignShape with { Editable = true, Kind = ShapeType.Ellipse, Fill = true, Outline = false });
        w.Session.SetForeground(Red);
        int steps = doc.History.UndoCount;
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 100), (150, 130), (200, 160));
        var layer = doc.ActiveLayer;
        Assert.Equal(LayerKind.Shape, layer.Kind);
        var c = (ShapeContent)layer.Content!;
        Assert.Equal((ShapeType.Ellipse, 100.0, 100.0, 100.0, 60.0), (c.Kind, Math.Round(c.X), Math.Round(c.Y), Math.Round(c.Width), Math.Round(c.Height)));
        Assert.Equal(steps + 1, doc.History.UndoCount); // the whole drag is one "Add shape"
        Assert.Equal((200, 71, 62, 255), Shown(w, 150, 130));
        Assert.True(Has(w, "EffectsButton")); // the options bar shows the chosen shape's options

        // Snapping: a guide at x = 300 catches a drag that ends near it.
        w.Session.SetAssistOptions(w.Session.Assist with { Snap = true });
        doc.SetGuides([new Guide(true, 300)], "Guide");
        Drag(w, MouseButton.Left, RawInputModifiers.None, (220, 200), (260, 230), (298, 260));
        var snapped = (ShapeContent)doc.ActiveLayer.Content!;
        Assert.Equal(300, snapped.X + snapped.Width, 1);

        w.Session.Undo();
        w.Session.Undo();
        w.Session.Undo();
        Assert.DoesNotContain(doc.Layers, l => l.Kind == LayerKind.Shape);

        // Pixels mode paints as before (no new layer).
        w.Session.SetDesignShapeOptions(w.Session.DesignShape with { Editable = false });
        doc.SetActiveLayer(doc.Layers[0]);
        int count = doc.Layers.Count;
        Drag(w, MouseButton.Left, RawInputModifiers.None, (50, 50), (90, 80));
        Assert.Equal(count, doc.Layers.Count);
        w.Close();
    }

    // ================================================================== rulers and guides

    [AvaloniaFact]
    public void Guides_come_from_the_rulers_move_with_the_move_tool_and_go_away_when_dragged_off()
    {
        var w = Window(WorkspaceKind.Editing);
        var doc = w.Session.Document;
        Assert.False(Has(w, "RulerTop")); // hidden by default
        Key(w, Avalonia.Input.Key.R, RawInputModifiers.Control);
        Assert.True(Has(w, "RulerTop"));
        var ruler = Find<RulerBar>(w, "RulerTop");
        var start = ruler.TranslatePoint(new Point(200, 10), w)!.Value;
        var target = DocToWindow(w, 200, 120);
        w.MouseMove(start);
        w.MouseDown(start, MouseButton.Left);
        w.MouseMove(new Point(target.X, (start.Y + target.Y) / 2), RawInputModifiers.LeftMouseButton);
        w.MouseMove(target, RawInputModifiers.LeftMouseButton);
        w.MouseUp(target, MouseButton.Left);
        Pump();
        var guide = Assert.Single(doc.Guides);
        Assert.False(guide.Vertical);
        Assert.Equal(120, guide.Position, 0);
        Assert.Equal("Add guide", LastStep(w));
        Assert.Equal(doc.Flatten().Pixels, new Document(400, 300, ColorRgb.White).Flatten().Pixels); // never in the picture

        w.Session.SelectTool(ToolKind.Move);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 120), (100, 150), (100, 180));
        Assert.Equal(180, doc.Guides[0].Position, 0);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 180), (100, 100), (100, -40));
        Assert.Empty(doc.Guides);
        w.Session.Undo();
        Assert.Single(doc.Guides);
        w.Session.ToggleGuidesLocked();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 180), (100, 100));
        Assert.Equal(180, doc.Guides[0].Position, 0); // locked guides stay
        w.Close();
    }

    // ================================================================== groups and the layers panel

    [AvaloniaFact]
    public void Groups_are_made_folded_and_filled_from_the_layers_panel()
    {
        var w = Window(WorkspaceKind.Editing);
        var doc = w.Session.Document;
        var a = w.Session.AddLayer();
        Key(w, Avalonia.Input.Key.G, RawInputModifiers.Control);
        var group = doc.Layers.Single(l => l.IsGroup);
        Assert.Equal(group.Id, a.ParentId);
        Pump();
        Assert.True(Has(w, "Fold_" + group.Id));
        Click(w, Find<Button>(w, "Fold_" + group.Id));
        Assert.True(group.Properties.Collapsed);
        Click(w, Find<Button>(w, "Fold_" + group.Id));
        Assert.False(group.Properties.Collapsed);

        // Drag the background row onto the group row: it goes inside.
        var rows = w.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is Layer && b.IsEffectivelyVisible).ToList();
        var bgRow = rows.First(r => ReferenceEquals(r.Tag, doc.Layers[0]));
        var groupRow = rows.First(r => ReferenceEquals(r.Tag, group));
        var from = bgRow.TranslatePoint(new Point(120, bgRow.Bounds.Height / 2), w)!.Value;
        var to = groupRow.TranslatePoint(new Point(120, groupRow.Bounds.Height / 2), w)!.Value;
        w.MouseMove(from);
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(new Point(from.X, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
        w.MouseMove(to, RawInputModifiers.LeftMouseButton);
        w.MouseUp(to, MouseButton.Left);
        Pump();
        Assert.Equal(group.Id, doc.Layers.First(l => l.Name == "Background").ParentId);

        Key(w, Avalonia.Input.Key.G, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.DoesNotContain(doc.Layers, l => l.IsGroup);
        w.Close();
    }

    // ================================================================== masks

    [AvaloniaFact]
    public void A_mask_is_added_painted_switched_and_turned_off_from_the_layers_panel()
    {
        var w = Window(WorkspaceKind.Editing);
        var doc = w.Session.Document;
        var layer = w.Session.AddLayer();
        new FloodFillHelper(doc, layer, Red).Fill();
        Pump();
        Click(w, Find<Button>(w, "AddMaskButton"));
        Assert.True(layer.HasMask);
        Assert.True(doc.EditingMask);
        Assert.True(Has(w, "MaskChip"));

        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new BrushSettings { Size = 20, Hardness = 1, Opacity = 1 }, "pencil");
        w.Session.SetForeground(ColorRgb.Black);
        var pixels = (uint[])layer.Image.Pixels.Clone();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (50, 150), (200, 150), (350, 150));
        Assert.Equal((255, 255, 255, 255), Shown(w, 200, 150)); // hidden there…
        Assert.Equal(pixels, layer.Image.Pixels);                // …without erasing

        Click(w, Find<Border>(w, "LayerThumb_" + layer.Id));
        Assert.False(doc.EditingMask);
        Assert.False(Has(w, "MaskChip"));
        Click(w, Find<Border>(w, "MaskThumb_" + layer.Id));
        Assert.True(doc.EditingMask);
        var maskThumb = Find<Border>(w, "MaskThumb_" + layer.Id);
        var p = maskThumb.TranslatePoint(new Point(10, 10), w)!.Value;
        w.MouseMove(p, RawInputModifiers.Shift);
        w.MouseDown(p, MouseButton.Left, RawInputModifiers.Shift);
        w.MouseUp(p, MouseButton.Left, RawInputModifiers.Shift);
        Pump();
        Assert.False(layer.MaskEnabled);
        Assert.Equal((200, 71, 62, 255), Shown(w, 200, 150));
        w.Close();
    }

    /// <summary>Fills a layer with a colour (test setup).</summary>
    private sealed class FloodFillHelper(Document doc, Layer layer, ColorRgb colour)
    {
        public void Fill() => FloodFill.Apply(doc, layer, 1, 1, colour, new FillOptions { Contiguous = false, Tolerance = 255 });
    }

    // ================================================================== adjustment layers

    [AvaloniaFact]
    public async Task An_adjustment_layer_is_added_from_the_layer_menu_and_cancel_leaves_nothing()
    {
        var w = Window();
        var doc = w.Session.Document;
        new FloodFillHelper(doc, doc.ActiveLayer, Blue).Fill();
        int steps = doc.History.UndoCount;
        var cancelled = w.NewAdjustmentLayerAsync(AdjustKind.Levels);
        Pump();
        Assert.True(w.Dialogs.IsOpen);
        Assert.Contains(doc.Layers, l => l.Kind == LayerKind.Adjustment);
        w.Dialogs.Close("cancel");
        await cancelled;
        Assert.DoesNotContain(doc.Layers, l => l.Kind == LayerKind.Adjustment);
        Assert.Equal(steps, doc.History.UndoCount);

        Menu(w, "Layer", "New adjustment layer");
        SubMenu(w, "Hue / Saturation");
        Assert.True(w.Dialogs.IsOpen);
        w.Dialogs.Close("ok");
        Pump();
        var adj = Assert.Single(doc.Layers, l => l.Kind == LayerKind.Adjustment);
        Assert.Equal("Add adjustment layer", LastStep(w));
        doc.SetContent(adj, new AdjustmentContent(new HueSaturation(0, -100, 0)), "Edit adjustment");
        var (r, g, b, _) = Shown(w, 10, 10);
        Assert.True(Math.Abs(r - g) <= 1 && Math.Abs(g - b) <= 1);
        Assert.Equal(Pixel.Opaque(Blue), doc.Layers[0].Image[10, 10]); // pixels untouched
        w.Close();
    }

    [AvaloniaFact]
    public async Task Brightness_slider_reaches_its_whole_range_regression()
    {
        // Mission 005–007 passed −1…1 to an engine expecting −100…100: "+100" brightened by half a percent.
        var w = Window();
        var doc = w.Session.Document;
        new FloodFillHelper(doc, doc.ActiveLayer, new ColorRgb(100, 100, 100)).Fill();
        var task = w.AdjustAsync(AdjustKind.BrightnessContrast);
        Pump();
        var slider = w.GetVisualDescendants().OfType<DnSlider>().First(s => s.IsEffectivelyVisible && s.Maximum == 100);
        var end = slider.TranslatePoint(new Point(slider.Bounds.Width - 1, slider.Bounds.Height / 2), w)!.Value;
        var mid = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), w)!.Value;
        w.MouseMove(mid);
        w.MouseDown(mid, MouseButton.Left);
        w.MouseMove(end, RawInputModifiers.LeftMouseButton);
        w.MouseUp(end, MouseButton.Left);
        Pump();
        w.Dialogs.Close("ok");
        await task;
        Assert.True(Pixel.R(doc.ActiveLayer.Image[10, 10]) > 200, $"brightness +100 gave {Pixel.R(doc.ActiveLayer.Image[10, 10])}");
        w.Close();
    }

    // ================================================================== assistants

    [AvaloniaFact]
    public void Shift_draws_straight_brush_lines_and_shift_click_continues_from_the_last_stroke()
    {
        var w = Window();
        var doc = w.Session.Document;
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new BrushSettings { Size = 6, Hardness = 1, Opacity = 1 }, "pencil");
        w.Session.SetForeground(ColorRgb.Black);
        Drag(w, MouseButton.Left, RawInputModifiers.Shift, (50, 50), (150, 120), (250, 50));
        var img = doc.ActiveLayer.Image;
        Assert.True(Pixel.A(img[150, 50]) > 0);  // on the straight line
        Assert.Equal(0, Pixel.A(img[150, 120])); // not where the pointer wandered
        Drag(w, MouseButton.Left, RawInputModifiers.Shift, (250, 200));
        Assert.True(Pixel.A(img[250, 125]) > 0); // from the end of the last line
        w.Close();
    }

    [AvaloniaFact]
    public void Strokes_follow_the_perspective_line_nearest_their_direction()
    {
        var w = Window();
        var doc = w.Session.Document;
        w.Session.SetPerspective(1);
        doc.SetPerspective(new PerspectiveGuide(1, 200, 40, 0, 0));
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new BrushSettings { Size = 4, Hardness = 1, Opacity = 1 }, "pencil");
        w.Session.SetForeground(ColorRgb.Black);
        // Roughly towards the vanishing point, wobbling.
        Drag(w, MouseButton.Left, RawInputModifiers.None, (60, 250), (75, 220), (110, 210), (120, 170), (150, 160), (160, 120));
        var img = doc.ActiveLayer.Image;
        // On the line from (60, 250) to (200, 40): at y = 160, x = 120.
        Assert.True(Pixel.A(img[120, 160]) > 0);
        Assert.Equal(0, Pixel.A(img[150, 160]));
        Assert.Equal(0, Pixel.A(img[110, 210]));
        w.Close();
    }

    [AvaloniaFact]
    public void The_symmetry_centre_and_vanishing_points_move_with_the_move_tool()
    {
        var w = Window();
        var doc = w.Session.Document;
        w.Session.SetSymmetry(Symmetry.Vertical);
        w.Session.SelectTool(ToolKind.Move);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (200, 150), (150, 150), (120, 100));
        Assert.Equal(120, w.Session.Assist.OriginX ?? 0, 0);
        Assert.Equal(100, w.Session.Assist.OriginY ?? 0, 0);

        w.Session.SetPerspective(2);
        var v2 = doc.Perspective;
        Drag(w, MouseButton.Left, RawInputModifiers.None, (v2.V2X, v2.V2Y), (300, 60), (320, 40));
        Assert.Equal((320.0, 40.0), (Math.Round(doc.Perspective.V2X), Math.Round(doc.Perspective.V2Y)));

        // Painting mirrors around the moved centre (x' = 2 · 120 − x).
        w.Session.SetPerspective(0);
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new BrushSettings { Size = 6, Hardness = 1, Opacity = 1 }, "pencil");
        Drag(w, MouseButton.Left, RawInputModifiers.None, (40, 200), (60, 200));
        Assert.True(Pixel.A(doc.ActiveLayer.Image[190, 200]) > 0);
        w.Close();
    }

    // ================================================================== files

    [AvaloniaFact]
    public async Task A_missing_font_is_named_when_a_picture_opens_and_the_text_keeps_asking_for_it()
    {
        var w = Window();
        var doc = w.Session.Document;
        doc.AddTextLayer(new TextContent("Old poster", "No Such Font 8008", 30, Red, X: 10, Y: 10));
        var path = Path.Combine(_dir, "poster.dnk");
        DnkFile.Save(doc, path);
        var open = w.OpenPathAsync(path);
        Pump();
        await open;
        Pump();
        Assert.True(w.Dialogs.IsOpen);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("No Such Font 8008") == true && t.Text.Contains("shown in"));
        w.Dialogs.Close("ok");
        var text = w.Session.Document.Layers.Single(l => l.Kind == LayerKind.Text);
        Assert.Equal("No Such Font 8008", ((TextContent)text.Content!).FontFamily);
        Assert.NotNull(text.SubstituteFont);
        Assert.False(text.ContentBounds.IsEmpty); // still shown, in the substitute
        w.Close();
    }
}
