using DoodleNook.App.Editor;
using DoodleNook.App.Input;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using DoodleNook.Engine.Viewing;

namespace DoodleNook.App.Tests;

/// <summary>The input controller with synthetic pointer events (no UI framework involved).</summary>
public class InputControllerTests
{
    private readonly EditorSession _session = new(new Document(400, 300, ColorRgb.White));
    private readonly ViewTransform _view = new(400, 300);
    private readonly CanvasInputController _input;

    public InputControllerTests()
    {
        _view.Set(2, 10, 20); // zoomed in and offset: view coordinates differ from document coordinates
        _input = new CanvasInputController(_session, _view);
        _session.ApplyPreset("ink");
        _session.SetSize(10);
    }

    private PointerInput At(double docX, double docY, PointerKind kind = PointerKind.Pen, float p = 1f, bool primary = true,
        bool secondary = false, bool middle = false, bool eraser = false, bool alt = false, long id = 1)
    {
        var (x, y) = _view.DocumentToView(docX, docY);
        return new PointerInput(id, kind, x, y, p, primary, secondary, middle, eraser, alt);
    }

    private byte Alpha(int x, int y) => Pixel.A(_session.Document.ActiveLayer.Image[x, y]);

    private int Column(int x) => Enumerable.Range(0, 300).Count(y => Alpha(x, y) > 0);

    private void Stroke(params PointerInput[] points)
    {
        Assert.True(_input.Press(points[0]));
        foreach (var p in points.Skip(1)) _input.Move([p]);
        _input.Release(points[^1] with { Primary = false, Secondary = false, Middle = false });
    }

    [Fact]
    public void Pen_strokes_land_on_document_coordinates_and_use_pressure()
    {
        Stroke(At(50, 100, p: 0.2f), At(150, 100, p: 0.2f), At(250, 100, p: 1f), At(350, 100, p: 1f));
        Assert.Equal(Gesture.None, _input.Gesture);
        Assert.True(Alpha(100, 100) > 0);
        Assert.Equal(0, Alpha(100, 140));
        Assert.True(Column(100) < Column(300), "light pressure should paint thinner");
        Assert.Equal(1, _session.Document.History.UndoCount);
    }

    [Fact]
    public void Mouse_uses_full_pressure_whatever_the_device_reports()
    {
        Stroke(At(50, 100, PointerKind.Mouse, p: 0.5f), At(350, 100, PointerKind.Mouse, p: 0.5f));
        Assert.Equal(10, Column(200)); // the full 10 px brush, not the ~5 px that pressure 0.5 would give
    }

    [Fact]
    public void Coalesced_samples_are_all_used()
    {
        Assert.True(_input.Press(At(50, 50)));
        // One event carrying a curve's worth of history samples.
        var batch = Enumerable.Range(1, 40).Select(i => At(50 + i * 5, 50 + Math.Sin(i / 6.0) * 40)).ToArray();
        _input.Move(batch);
        _input.Release(batch[^1] with { Primary = false });
        foreach (var p in batch)
        {
            var (dx, dy) = _view.ViewToDocument(p.X, p.Y);
            Assert.True(Alpha((int)dx, (int)dy) > 0, $"history sample at ({dx:0},{dy:0}) was skipped");
        }
    }

    [Fact]
    public void A_second_pointer_cannot_disturb_a_pen_stroke()
    {
        Assert.True(_input.Press(At(50, 100, id: 7)));
        Assert.False(_input.Press(At(200, 250, PointerKind.Mouse, id: 1)));
        _input.Move([At(200, 250, PointerKind.Mouse, id: 1)]); // e.g. a stray mouse
        _input.Move([At(150, 100, id: 7)]);
        _input.Release(At(150, 100, id: 7, primary: false));
        Assert.Equal(0, Alpha(200, 250));
        Assert.True(Alpha(100, 100) > 0);
        Assert.Equal(1, _session.Document.History.UndoCount);
    }

    [Fact]
    public void Pen_eraser_end_erases_with_the_brush_selected()
    {
        Stroke(At(50, 100), At(350, 100));
        Stroke(At(200, 60, eraser: true), At(200, 140, eraser: true));
        Assert.Equal(Pixel.Transparent, _session.Document.ActiveLayer.Image[200, 100]);
        Assert.Equal(ToolKind.Brush, _session.Tool);
    }

    [Fact]
    public void Eraser_tool_erases()
    {
        Stroke(At(50, 100), At(350, 100));
        _session.SelectTool(ToolKind.Eraser);
        Stroke(At(200, 60), At(200, 140));
        Assert.Equal(Pixel.Transparent, _session.Document.ActiveLayer.Image[200, 100]);
        Assert.True(Alpha(100, 100) > 0);
    }

    [Fact]
    public void Space_or_hand_tool_pans_instead_of_painting()
    {
        double before = _view.DocumentToView(0, 0).X;
        _input.SpaceHeld = true;
        Stroke(At(50, 100), At(150, 100));
        Assert.Equal(before + 200, _view.DocumentToView(0, 0).X, 6); // 100 document px at 2× zoom
        Assert.False(_session.Document.CanUndo);

        _input.SpaceHeld = false;
        _session.SelectTool(ToolKind.Hand);
        Stroke(At(50, 100), At(50, 150));
        Assert.False(_session.Document.CanUndo);
    }

    [Fact]
    public void Middle_button_and_touch_pan()
    {
        double y0 = _view.DocumentToView(0, 0).Y;
        Stroke(At(10, 10, PointerKind.Mouse, primary: false, middle: true), At(10, 30, PointerKind.Mouse, primary: false, middle: true));
        Assert.Equal(y0 + 40, _view.DocumentToView(0, 0).Y, 6);
        Stroke(At(10, 10, PointerKind.Touch), At(10, 20, PointerKind.Touch));
        Assert.False(_session.Document.CanUndo, "a finger must never paint");
    }

    [Fact]
    public void Right_drag_or_barrel_drag_resizes_the_brush()
    {
        _session.SetSize(20);
        Stroke(At(100, 100, secondary: true), At(110, 100, secondary: true)); // 10 doc px to the right: radius +10
        Assert.Equal(40, _session.Brush.Size);
        Assert.False(_session.Document.CanUndo);
    }

    [Fact]
    public void Alt_or_eyedropper_picks_the_visible_colour()
    {
        _session.SetForeground(new ColorRgb(200, 30, 30));
        Stroke(At(50, 100), At(350, 100));
        _session.SetForeground(ColorRgb.Black);
        Stroke(At(200, 100, alt: true), At(200, 100, alt: true));
        Assert.Equal(new ColorRgb(200, 30, 30), _session.Foreground);

        _session.SelectTool(ToolKind.Eyedropper);
        Stroke(At(10, 10), At(10, 10));
        Assert.Equal(ColorRgb.White, _session.Foreground); // the white background
        Assert.Equal(1, _session.Document.History.UndoCount);
    }

    [Fact]
    public void Interrupted_stroke_keeps_what_was_drawn()
    {
        Assert.True(_input.Press(At(50, 100)));
        _input.Move([At(150, 100)]);
        _input.Interrupt(); // capture lost, window deactivated…
        Assert.Equal(Gesture.None, _input.Gesture);
        Assert.False(_session.IsStrokeActive);
        Assert.True(Alpha(100, 100) > 0);
        Assert.True(_session.Document.CanUndo);
    }

    [Fact]
    public void Hovering_tracks_the_cursor_without_painting()
    {
        _input.Move([At(120, 80, primary: false)]);
        Assert.NotNull(_input.Hover);
        Assert.Equal(CursorShape.BrushOutline, _input.Cursor);
        Assert.False(_session.Document.CanUndo);
        _input.Leave();
        Assert.Null(_input.Hover);
    }

    [Fact]
    public void Lift_off_with_zero_pressure_tapers_instead_of_cutting_off()
    {
        _session.ApplyPreset("ink"); // size by pressure
        _session.SetSize(20);
        Assert.True(_input.Press(At(50, 100, p: 1f)));
        _input.Move([At(200, 100, p: 1f)]);
        _input.Release(At(300, 100, p: 0f, primary: false));
        Assert.True(Column(250) < Column(150));
        Assert.True(Alpha(290, 100) > 0);
    }

    // ------------------------------------------------------------------ Mission 005 tools

    private void Drag(ToolKind tool, (double X, double Y) from, (double X, double Y) to, bool shift = false)
    {
        _session.SelectTool(tool);
        var a = At(from.X, from.Y, PointerKind.Mouse) with { Shift = shift };
        var m = At((from.X + to.X) / 2, (from.Y + to.Y) / 2, PointerKind.Mouse) with { Shift = shift };
        var b = At(to.X, to.Y, PointerKind.Mouse) with { Shift = shift };
        Assert.True(_input.Press(a));
        _input.Move([m]);
        _input.Move([b]);
        _input.Release(b with { Primary = false });
    }

    private void Click(ToolKind tool, double x, double y)
    {
        _session.SelectTool(tool);
        _input.Press(At(x, y, PointerKind.Mouse));
        _input.Release(At(x, y, PointerKind.Mouse, primary: false));
    }

    [Fact]
    public void Rectangle_select_shift_adds_and_a_click_deselects()
    {
        Drag(ToolKind.RectSelect, (20, 20), (100, 80));
        var sel = _session.Document.Selection!;
        Assert.Equal(new PixelRect(20, 20, 80, 60), sel.Bounds);
        Drag(ToolKind.RectSelect, (200, 200), (250, 250), shift: true);
        Assert.Equal(255, _session.Document.Selection![30, 30]);
        Assert.Equal(255, _session.Document.Selection![220, 220]);
        Click(ToolKind.RectSelect, 300, 100);
        Assert.Null(_session.Document.Selection);
        Assert.Equal(3, _session.Document.History.UndoCount); // selections are undoable steps
    }

    [Fact]
    public void Painting_stays_inside_the_selection()
    {
        Drag(ToolKind.RectSelect, (0, 0), (200, 300));
        _session.SelectTool(ToolKind.Brush);
        Stroke(At(50, 100), At(350, 100));
        Assert.True(Alpha(150, 100) > 0);
        Assert.Equal(0, Alpha(250, 100));
    }

    [Fact]
    public void Lasso_and_magic_wand_select_what_they_should()
    {
        _session.SelectTool(ToolKind.Lasso);
        Assert.True(_input.Press(At(50, 50, PointerKind.Mouse)));
        _input.Move([At(150, 50, PointerKind.Mouse)]);
        _input.Move([At(150, 150, PointerKind.Mouse)]);
        _input.Move([At(50, 150, PointerKind.Mouse)]);
        _input.Release(At(50, 150, PointerKind.Mouse, primary: false));
        Assert.True(_session.Document.Selection![100, 100] > 200);
        Assert.Equal(0, _session.Document.Selection![200, 200]);

        _session.Deselect();
        _session.SetForeground(new ColorRgb(200, 0, 0));
        _session.SetSize(30);
        _session.SelectTool(ToolKind.Brush);
        Stroke(At(50, 200), At(350, 200));
        _session.SetSelectionOptions(_session.Selection with { WandTolerance = 10, WandContiguous = true });
        Click(ToolKind.MagicWand, 200, 200);
        var wand = _session.Document.Selection!;
        Assert.True(wand[200, 200] > 0);
        Assert.Equal(0, wand[200, 50]);
    }

    [Fact]
    public void Move_tool_moves_the_layer_by_whole_pixels_in_one_step()
    {
        _session.SetSize(10);
        _session.SelectTool(ToolKind.Brush);
        Stroke(At(50, 100), At(150, 100));
        int before = _session.Document.History.UndoCount;
        Drag(ToolKind.Move, (100, 100), (100, 160));
        Assert.Equal(0, Alpha(100, 100));
        Assert.True(Alpha(100, 160) > 0);
        Assert.Equal(before + 1, _session.Document.History.UndoCount);
        _session.Undo();
        Assert.True(Alpha(100, 100) > 0);
    }

    [Fact]
    public void Gradient_shape_fill_and_crop_tools_work_through_input()
    {
        var layer = _session.Document.ActiveLayer;
        _session.SetForeground(new ColorRgb(0, 0, 255));
        _session.SetBackground(new ColorRgb(255, 0, 0));
        Drag(ToolKind.Gradient, (0, 150), (400, 150));
        Assert.True(Pixel.B(layer.Image[5, 10]) > 200 && Pixel.R(layer.Image[395, 10]) > 200);
        Assert.Equal("Gradient", _session.Document.History.NextUndoName);

        var top = _session.AddLayer();
        _session.SetShapeOptions(new Engine.Operations.ShapeOptions(Engine.Operations.ShapeKind.Rectangle, Filled: true));
        Drag(ToolKind.Shape, (100, 100), (200, 200));
        Assert.Equal(255, Pixel.A(top.Image[150, 150]));
        Assert.Equal(0, Pixel.A(top.Image[250, 150]));

        var third = _session.AddLayer();
        _session.SetForeground(new ColorRgb(0, 200, 0));
        Click(ToolKind.Fill, 10, 10); // an empty layer: the bucket fills all of it
        Assert.Equal(255, Pixel.A(third.Image[300, 280]));

        Drag(ToolKind.Crop, (50, 40), (250, 140));
        Assert.Equal(new PixelRect(50, 40, 200, 100), _input.PendingCrop);
        Assert.True(_input.ApplyCrop());
        Assert.Equal(200, _session.Document.Width);
        Assert.Equal(100, _session.Document.Height);
        _session.Undo();
        Assert.Equal(400, _session.Document.Width);
    }

    [Fact]
    public void Free_transform_drags_and_commits_as_one_step()
    {
        _session.SelectTool(ToolKind.Brush);
        Stroke(At(50, 100), At(150, 100));
        Assert.True(_session.BeginTransform());
        _input.RefreshTransformFrame();
        Assert.NotNull(_input.Overlay.TransformFrame);
        Assert.True(_input.Press(At(100, 100, PointerKind.Mouse)));
        Assert.Equal(Gesture.Transform, _input.Gesture);
        _input.Move([At(100, 140, PointerKind.Mouse)]);
        _input.Release(At(100, 140, PointerKind.Mouse, primary: false));
        Assert.True(_session.CommitTransform());
        Assert.True(Alpha(100, 140) > 0);
        Assert.Equal(0, Alpha(100, 100));
        _session.Undo();
        Assert.True(Alpha(100, 100) > 0);
    }

    [Fact]
    public void Right_click_tap_asks_for_the_quick_palette()
    {
        (double X, double Y)? requested = null;
        _input.RadialRequested += (x, y) => requested = (x, y);
        var p = At(120, 90, PointerKind.Mouse, primary: false, secondary: true);
        Assert.True(_input.Press(p));
        _input.Release(p with { Secondary = false });
        Assert.NotNull(requested);
        Assert.Equal(p.X, requested!.Value.X, 6);
        Assert.False(_session.Document.CanUndo);
    }

    [Fact]
    public void Pixel_pencil_paints_exact_pixels_and_right_button_can_paint_the_background()
    {
        _session.ReplaceDocument(new Document(32, 32), null);
        _view.SetDocumentSize(32, 32);
        _view.Set(8, 16, 16);
        _session.SetPixelMode(true);
        _session.SetForeground(new ColorRgb(10, 20, 30));
        _session.SetBackground(new ColorRgb(250, 240, 230));
        Drag(ToolKind.PixelPencil, (2.5, 2.5), (10.5, 2.5));
        var img = _session.Document.ActiveLayer.Image;
        Assert.All(Enumerable.Range(2, 9), x => Assert.Equal(Pixel.Opaque(new ColorRgb(10, 20, 30)), img[x, 2]));
        Assert.Equal(Pixel.Transparent, img[2, 3]);

        _input.PixelRightPaintsBackground = true;
        var a = At(5.5, 8.5, PointerKind.Mouse, primary: false, secondary: true);
        Assert.True(_input.Press(a));
        _input.Release(a with { Secondary = false });
        Assert.Equal(Pixel.Opaque(new ColorRgb(250, 240, 230)), _session.Document.ActiveLayer.Image[5, 8]);
    }

    [Fact]
    public void Locked_layers_refuse_paint_and_say_why()
    {
        string? message = null;
        _session.Message += m => message = m;
        _session.SetLayerProperty(_session.Document.ActiveLayer, p => p with { Locked = true });
        _session.SelectTool(ToolKind.Brush);
        Assert.False(_input.Press(At(50, 100)));
        Assert.False(_session.IsStrokeActive);
        Assert.NotNull(message);
        Assert.Equal(0, Alpha(50, 100));
    }

    [Fact]
    public void Holding_r_and_dragging_rotates_the_view_not_the_picture()
    {
        _input.Viewport = (800, 600);
        _input.RotateHeld = true;
        Assert.True(_input.Press(new PointerInput(1, PointerKind.Mouse, 700, 300, 0.5f, Primary: true)));
        _input.Move([new PointerInput(1, PointerKind.Mouse, 400, 50, 0.5f, Primary: true)]);
        _input.Release(new PointerInput(1, PointerKind.Mouse, 400, 50, 0.5f, Primary: false));
        Assert.NotEqual(0, _view.Rotation);
        Assert.False(_session.Document.CanUndo);
    }
}

public class ArchitectureTests
{
    [Fact]
    public void Input_controller_does_not_own_document_state()
    {
        var fieldTypes = typeof(CanvasInputController)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Select(f => f.FieldType)
            .ToArray();
        Assert.DoesNotContain(typeof(Document), fieldTypes);
        Assert.DoesNotContain(typeof(RasterImage), fieldTypes);
        Assert.DoesNotContain(typeof(StrokeEngine), fieldTypes);
        Assert.DoesNotContain(fieldTypes, t => t.FullName!.StartsWith("Avalonia"));
    }

    [Fact]
    public void Editor_session_has_no_ui_framework_dependencies()
    {
        var members = typeof(EditorSession).GetMembers().SelectMany(m => m switch
        {
            System.Reflection.PropertyInfo p => new[] { p.PropertyType },
            System.Reflection.MethodInfo mi => mi.GetParameters().Select(x => x.ParameterType).Append(mi.ReturnType),
            _ => Array.Empty<Type>(),
        });
        Assert.DoesNotContain(members, t => t.FullName?.StartsWith("Avalonia") == true);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact] // geometry objects belong to the UI thread
    public void All_icons_parse()
    {
        foreach (var name in Controls.Icons.Data.Keys)
            Assert.NotNull(Controls.Icons.Get(name));
    }
}
