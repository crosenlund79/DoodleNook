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

    private byte Alpha(int x, int y) => Pixel.A(_session.Document.Layer[x, y]);

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
        Assert.Equal(Pixel.Transparent, _session.Document.Layer[200, 100]);
        Assert.Equal(ToolKind.Brush, _session.Tool);
    }

    [Fact]
    public void Eraser_tool_erases()
    {
        Stroke(At(50, 100), At(350, 100));
        _session.SelectTool(ToolKind.Eraser);
        Stroke(At(200, 60), At(200, 140));
        Assert.Equal(Pixel.Transparent, _session.Document.Layer[200, 100]);
        Assert.True(Alpha(100, 100) > 0);
    }

    [Fact]
    public void Space_or_hand_tool_pans_instead_of_painting()
    {
        double before = _view.OffsetX;
        _input.SpaceHeld = true;
        Stroke(At(50, 100), At(150, 100));
        Assert.Equal(before + 200, _view.OffsetX, 6); // 100 document px at 2× zoom
        Assert.False(_session.Document.CanUndo);

        _input.SpaceHeld = false;
        _session.SelectTool(ToolKind.Hand);
        Stroke(At(50, 100), At(50, 150));
        Assert.False(_session.Document.CanUndo);
    }

    [Fact]
    public void Middle_button_and_touch_pan()
    {
        double y0 = _view.OffsetY;
        Stroke(At(10, 10, PointerKind.Mouse, primary: false, middle: true), At(10, 30, PointerKind.Mouse, primary: false, middle: true));
        Assert.Equal(y0 + 40, _view.OffsetY, 6);
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
