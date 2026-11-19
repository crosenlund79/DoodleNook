using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Tests;

/// <summary>
/// Drives the real DoodleNook window headlessly: real layout, styles, event routing and canvas code,
/// with mouse and keyboard input injected at the window (no pen hardware involved).
/// </summary>
public class WindowTests
{
    internal static MainWindow Open(double width = 1280, double height = 800)
    {
        var w = new MainWindow { Width = width, Height = height };
        w.Show();
        Pump();
        return w;
    }

    internal static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    internal static Point DocToWindow(MainWindow w, double x, double y)
    {
        var (vx, vy) = w.Canvas.View.DocumentToView(x, y);
        return w.Canvas.TranslatePoint(new Point(vx, vy), w)!.Value;
    }

    internal static void Drag(MainWindow w, MouseButton button, RawInputModifiers mods, params (double X, double Y)[] docPoints)
    {
        var first = DocToWindow(w, docPoints[0].X, docPoints[0].Y);
        w.MouseMove(first, mods);
        w.MouseDown(first, button, mods);
        foreach (var p in docPoints.Skip(1)) w.MouseMove(DocToWindow(w, p.X, p.Y), mods | ButtonMod(button));
        var last = DocToWindow(w, docPoints[^1].X, docPoints[^1].Y);
        w.MouseUp(last, button, mods);
        Pump();
    }

    private static RawInputModifiers ButtonMod(MouseButton b) => b switch
    {
        MouseButton.Left => RawInputModifiers.LeftMouseButton,
        MouseButton.Right => RawInputModifiers.RightMouseButton,
        MouseButton.Middle => RawInputModifiers.MiddleMouseButton,
        _ => RawInputModifiers.None,
    };

    private static void Key(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None, string? symbol = null)
    {
        w.KeyPress(key, mods, PhysicalKey.None, symbol);
        w.KeyRelease(key, mods, PhysicalKey.None, symbol);
        Pump();
    }

    private static byte Alpha(MainWindow w, int x, int y) => Pixel.A(w.Session.Document.Layer[x, y]);

    private static (int Left, int Top, int Right, int Bottom) Bounds(uint[] px, int width)
    {
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i] == 0) continue;
            int x = i % width, y = i / width;
            l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y);
        }
        return (l, t, r, b);
    }

    [AvaloniaFact]
    public void Window_opens_in_the_quick_sketch_layout()
    {
        var w = Open();
        Assert.True(w.QuickBar.IsVisible);
        Assert.True(w.ToolRail.IsVisible);
        Assert.True(w.Canvas.Bounds.Width > 1000 && w.Canvas.Bounds.Height > 600);
        Assert.Equal("Untitled — DoodleNook", w.Title);
        Assert.Equal(ToolKind.Brush, w.Session.Tool);
        Assert.Contains("on", w.BrushTool.Classes);
        Assert.Equal(1920, w.Session.Document.Width);
        Assert.InRange(w.Canvas.ActualZoom, 0.3, 1.0); // fitted
        w.Close();
    }

    [AvaloniaFact]
    public void Drawing_with_the_mouse_paints_and_marks_the_picture_unsaved()
    {
        var w = Open();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 500), (700, 520), (1200, 480), (1500, 500));
        Assert.True(Alpha(w, 700, 520) > 0);
        Assert.True(Alpha(w, 1000, 495) > 0 || Alpha(w, 1000, 494) > 0 || Alpha(w, 1000, 496) > 0);
        Assert.Equal(0, Alpha(w, 700, 700));
        Assert.Equal(1, w.Session.Document.History.UndoCount);
        Assert.Equal("Untitled • — DoodleNook", w.Title);
        Assert.True(w.UndoButton.IsEnabled);
        w.Close();
    }

    [AvaloniaFact]
    public void Keyboard_undo_and_redo()
    {
        var w = Open();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 300), (900, 300));
        var painted = (uint[])w.Session.Document.Layer.Pixels.Clone();
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(0, Alpha(w, 600, 300));
        Assert.False(w.UndoButton.IsEnabled);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(painted, w.Session.Document.Layer.Pixels);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Key(w, Avalonia.Input.Key.Y, RawInputModifiers.Control);
        Assert.Equal(painted, w.Session.Document.Layer.Pixels);
        w.Close();
    }

    [AvaloniaFact]
    public void Tool_shortcuts_and_the_erase_toggle()
    {
        var w = Open();
        Key(w, Avalonia.Input.Key.E);
        Assert.Equal(ToolKind.Eraser, w.Session.Tool);
        Assert.Contains("on", w.EraseButton.Classes);
        Assert.False(w.OpacityGroup.IsVisible);
        Key(w, Avalonia.Input.Key.B);
        Assert.Equal(ToolKind.Brush, w.Session.Tool);
        Assert.True(w.OpacityGroup.IsVisible);
        Key(w, Avalonia.Input.Key.I);
        Assert.Equal(ToolKind.Eyedropper, w.Session.Tool);
        Key(w, Avalonia.Input.Key.H);
        Assert.Equal(ToolKind.Hand, w.Session.Tool);
        w.Close();
    }

    [AvaloniaFact]
    public void Erasing_through_the_window_and_undoing_it()
    {
        var w = Open();
        w.Session.SetSize(40);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 500), (1500, 500));
        var painted = (uint[])w.Session.Document.Layer.Pixels.Clone();
        Key(w, Avalonia.Input.Key.E);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (900, 400), (900, 600));
        Assert.Equal(Pixel.Transparent, w.Session.Document.Layer[900, 500]);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(painted, w.Session.Document.Layer.Pixels);
        w.Close();
    }

    [AvaloniaFact]
    public void Bracket_keys_and_slider_change_brush_size()
    {
        var w = Open();
        w.Session.SetSize(20);
        Key(w, Avalonia.Input.Key.OemCloseBrackets);
        Assert.Equal(23, w.Session.Brush.Size);
        Key(w, Avalonia.Input.Key.OemOpenBrackets);
        Key(w, Avalonia.Input.Key.OemOpenBrackets);
        Assert.True(w.Session.Brush.Size < 20);

        // Click near the right end of the (logarithmic) size slider.
        var slider = w.SizeSlider;
        var p = slider.TranslatePoint(new Point(slider.Bounds.Width - 10, slider.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Pump();
        Assert.True(w.Session.Brush.Size > 300, $"size {w.Session.Brush.Size}");
        Assert.Equal($"{w.Session.Brush.Size:0} px", w.SizeText.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void Tab_hides_and_restores_the_interface()
    {
        var w = Open();
        double before = w.Canvas.Bounds.Width;
        Key(w, Avalonia.Input.Key.Tab);
        Assert.False(w.QuickBar.IsVisible);
        Assert.False(w.ToolRail.IsVisible);
        Assert.True(w.ShowUiButton.IsVisible);
        Assert.True(w.Canvas.Bounds.Width > before);
        Key(w, Avalonia.Input.Key.Tab);
        Assert.True(w.QuickBar.IsVisible);
        Assert.False(w.ShowUiButton.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Wheel_zooms_at_the_cursor_and_drawing_stays_on_the_same_pixels()
    {
        var w = Open();
        double z0 = w.Canvas.ActualZoom;
        var at = DocToWindow(w, 960, 540);
        w.MouseWheel(at, new Vector(0, 3));
        Pump();
        Assert.True(w.Canvas.ActualZoom > z0 * 1.5);
        var after = DocToWindow(w, 960, 540);
        Assert.Equal(at.X, after.X, 3);
        Assert.Equal(at.Y, after.Y, 3);

        // Same document-space stroke at a different zoom: same pixels as at the fitted zoom.
        Drag(w, MouseButton.Left, RawInputModifiers.None, (900, 520), (1020, 560));
        var zoomed = (uint[])w.Session.Document.Layer.Pixels.Clone();
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Key(w, Avalonia.Input.Key.D0, RawInputModifiers.Control);
        Assert.Equal(z0, w.Canvas.ActualZoom, 6);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (900, 520), (1020, 560));
        // Exact zoom independence is tested in the engine; here the injected mouse positions are whole screen pixels,
        // so allow the stroke to move by a fraction of a document pixel, but it must cover the same place.
        var a = Bounds(zoomed, w.Session.Document.Width);
        var b = Bounds(w.Session.Document.Layer.Pixels, w.Session.Document.Width);
        Assert.True(a.Right >= 0 && b.Right >= 0, $"zoomed stroke {a}, fitted stroke {b}, undo count {w.Session.Document.History.UndoCount}");
        Assert.InRange(Math.Abs(a.Left - b.Left), 0, 2);
        Assert.InRange(Math.Abs(a.Top - b.Top), 0, 2);
        Assert.InRange(Math.Abs(a.Right - b.Right), 0, 2);
        Assert.InRange(Math.Abs(a.Bottom - b.Bottom), 0, 2);
        w.Close();
    }

    [AvaloniaFact]
    public void Space_drag_pans_without_painting()
    {
        var w = Open();
        double x0 = w.Canvas.View.OffsetX;
        var p = DocToWindow(w, 500, 500);
        w.KeyPress(Avalonia.Input.Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(new Point(p.X + 120, p.Y), RawInputModifiers.LeftMouseButton);
        w.MouseUp(new Point(p.X + 120, p.Y), MouseButton.Left);
        w.KeyRelease(Avalonia.Input.Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Pump();
        Assert.Equal(x0 + 120, w.Canvas.View.OffsetX, 3);
        Assert.False(w.Session.Document.CanUndo);
        w.Close();
    }

    [AvaloniaFact]
    public void New_picture_asks_before_discarding_unsaved_work()
    {
        var w = Open();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 300), (600, 300));
        Key(w, Avalonia.Input.Key.N, RawInputModifiers.Control);
        Assert.True(w.DialogLayer.IsVisible); // "Save “Untitled”?"
        Key(w, Avalonia.Input.Key.Escape);     // Cancel
        Assert.False(w.DialogLayer.IsVisible);
        Assert.True(w.Session.Document.CanUndo); // nothing was discarded
        w.Close();
    }

    [AvaloniaFact]
    public void Colour_swap_and_reset_shortcuts()
    {
        var w = Open();
        var fg = w.Session.Foreground;
        Key(w, Avalonia.Input.Key.X);
        Assert.Equal(ColorRgb.White, w.Session.Foreground);
        Assert.Equal(fg, w.Session.Background);
        w.Session.SetForeground(new ColorRgb(1, 2, 3));
        Key(w, Avalonia.Input.Key.D);
        Assert.Equal(EditorSession.DefaultForeground, w.Session.Foreground);
        w.Close();
    }

    [AvaloniaFact]
    public void Canvas_renders_painted_pixels_on_screen()
    {
        var w = Open();
        w.Session.SetForeground(new ColorRgb(200, 30, 30));
        w.Session.SetSize(60);
        w.Session.SetOpacity(1);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (400, 540), (1500, 540));
        using var frame = w.CaptureRenderedFrame()!;
        var at = DocToWindow(w, 950, 540);
        var px = Screenshots.ReadPixel(frame, (int)at.X, (int)at.Y);
        Assert.True(px.R > 150 && px.G < 90 && px.B < 90, $"screen pixel was {px}");
        var blank = DocToWindow(w, 950, 200);
        var white = Screenshots.ReadPixel(frame, (int)blank.X, (int)blank.Y);
        Assert.True(white.R > 240 && white.G > 240 && white.B > 240, $"background pixel was {white}");
        w.Close();
    }

    [AvaloniaFact]
    public void Opening_the_colour_popover_keeps_shortcuts_working()
    {
        var w = Open();
        var chip = w.ColourChip;
        var p = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Pump();
        Assert.False(w.FocusManager?.GetFocusedElement() is TextBox, "the hex box should not grab focus");
        Key(w, Avalonia.Input.Key.E);
        Assert.Equal(ToolKind.Eraser, w.Session.Tool);
        w.Close();
    }

    [AvaloniaFact]
    public void Holding_alt_picks_a_colour_even_when_pointer_events_lack_the_alt_flag()
    {
        var w = Open();
        w.Session.SetForeground(new ColorRgb(10, 120, 200));
        w.Session.SetSize(40);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 500), (1500, 500));
        w.Session.SetForeground(ColorRgb.Black);

        w.KeyPress(Avalonia.Input.Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        // Like Windows pen input: the pointer events themselves report no Alt modifier.
        Drag(w, MouseButton.Left, RawInputModifiers.None, (900, 500), (905, 500));
        w.KeyRelease(Avalonia.Input.Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Pump();

        var picked = w.Session.Foreground;
        Assert.True(picked.B > 150 && picked.R < 80, $"picked {picked}");
        Assert.Equal(1, w.Session.Document.History.UndoCount); // no stroke was painted while Alt was held
        Assert.False(w.Canvas.Input!.AltHeld);
        w.Close();
    }
}
