using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Tests;

/// <summary>
/// Drives the real DoodleNook window headlessly: real layout, styles, event routing and canvas code,
/// with mouse and keyboard input injected at the window (no pen hardware involved).
/// </summary>
public class WindowTests
{
    internal static MainWindow Open(double width = 1280, double height = 800, WorkspaceKind workspace = WorkspaceKind.QuickSketch)
    {
        var w = new MainWindow { Width = width, Height = height };
        w.Show();
        Pump();
        w.SetWorkspace(workspace);
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

    /// <summary>A named control anywhere in the window, including open pop-overs (they live in the overlay layer).</summary>
    internal static T Find<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible)
        ?? throw new InvalidOperationException($"No visible {typeof(T).Name} named {name}");

    internal static bool Has(Window w, string name) =>
        w.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name && c.IsEffectivelyVisible);

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

    internal static void Click(Window w, Control c, MouseButton button = MouseButton.Left)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseMove(p);
        w.MouseDown(p, button);
        w.MouseUp(p, button);
        Pump();
    }

    private static RawInputModifiers ButtonMod(MouseButton b) => b switch
    {
        MouseButton.Left => RawInputModifiers.LeftMouseButton,
        MouseButton.Right => RawInputModifiers.RightMouseButton,
        MouseButton.Middle => RawInputModifiers.MiddleMouseButton,
        _ => RawInputModifiers.None,
    };

    internal static void Key(MainWindow w, Key key, RawInputModifiers mods = RawInputModifiers.None, string? symbol = null)
    {
        w.KeyPress(key, mods, PhysicalKey.None, symbol);
        w.KeyRelease(key, mods, PhysicalKey.None, symbol);
        Pump();
    }

    private static byte Alpha(MainWindow w, int x, int y) => Pixel.A(w.Session.Document.ActiveLayer.Image[x, y]);

    /// <summary>Clicks a menu-bar menu and then the entry whose text starts with <paramref name="entry"/>.</summary>
    internal static void Menu(MainWindow w, string menu, string entry)
    {
        Click(w, Find<Button>(w, "Menu" + menu));
        var item = w.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("menu") && b.IsEffectivelyVisible && Texts(b).Any(t => t.StartsWith(entry, StringComparison.Ordinal)))
            ?? throw new InvalidOperationException($"No menu entry {entry} in {menu}");
        Click(w, item);
    }

    private static IEnumerable<string> Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

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

    // ================================================================== Quick Sketch (Mission 004 behaviour kept)

    [AvaloniaFact]
    public void Window_opens_in_the_quick_sketch_layout()
    {
        var w = Open();
        Assert.True(Has(w, "QuickBar"));
        Assert.True(Has(w, "ToolStrip"));
        Assert.False(Has(w, "TitleBar"));
        Assert.True(w.Canvas.Bounds.Width > 1000 && w.Canvas.Bounds.Height > 600);
        Assert.Equal("Untitled — DoodleNook", w.Title);
        Assert.Equal(ToolKind.Brush, w.Session.Tool);
        Assert.Contains("on", Find<Button>(w, "ToolBrush").Classes);
        Assert.Equal(1920, w.Session.Document.Width);
        Assert.Equal("Sketch", w.Session.Document.ActiveLayer.Name);
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
        Assert.True(Find<Button>(w, "UndoButton").IsEnabled);
        w.Close();
    }

    [AvaloniaFact]
    public void Keyboard_undo_and_redo()
    {
        var w = Open();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 300), (900, 300));
        var painted = (uint[])w.Session.Document.ActiveLayer.Image.Pixels.Clone();
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(0, Alpha(w, 600, 300));
        Assert.False(Find<Button>(w, "UndoButton").IsEnabled);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(painted, w.Session.Document.ActiveLayer.Image.Pixels);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Key(w, Avalonia.Input.Key.Y, RawInputModifiers.Control);
        Assert.Equal(painted, w.Session.Document.ActiveLayer.Image.Pixels);
        w.Close();
    }

    [AvaloniaFact]
    public void Tool_shortcuts_and_the_erase_toggle()
    {
        var w = Open();
        Key(w, Avalonia.Input.Key.E);
        Assert.Equal(ToolKind.Eraser, w.Session.Tool);
        Assert.Contains("on", Find<Button>(w, "EraseButton").Classes);
        Key(w, Avalonia.Input.Key.B);
        Assert.Equal(ToolKind.Brush, w.Session.Tool);
        Key(w, Avalonia.Input.Key.I);
        Assert.Equal(ToolKind.Eyedropper, w.Session.Tool);
        Key(w, Avalonia.Input.Key.H);
        Assert.Equal(ToolKind.Hand, w.Session.Tool);
        Key(w, Avalonia.Input.Key.L);
        Assert.Equal(ToolKind.Lasso, w.Session.Tool);
        Key(w, Avalonia.Input.Key.V);
        Assert.Equal(ToolKind.Move, w.Session.Tool);
        Key(w, Avalonia.Input.Key.G);
        Assert.Equal(ToolKind.Fill, w.Session.Tool);
        Key(w, Avalonia.Input.Key.W); // not a Quick Sketch tool: nothing happens
        Assert.Equal(ToolKind.Fill, w.Session.Tool);
        w.Close();
    }

    [AvaloniaFact]
    public void Erasing_through_the_window_and_undoing_it()
    {
        var w = Open();
        w.Session.SetSize(40);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 500), (1500, 500));
        var painted = (uint[])w.Session.Document.ActiveLayer.Image.Pixels.Clone();
        Key(w, Avalonia.Input.Key.E);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (900, 400), (900, 600));
        Assert.Equal(Pixel.Transparent, w.Session.Document.ActiveLayer.Image[900, 500]);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(painted, w.Session.Document.ActiveLayer.Image.Pixels);
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

        // Click near the right end of the (logarithmic) size slider in the Quick Sketch bar.
        var slider = Find<QuickBar>(w, "QuickBar").GetVisualDescendants().OfType<DnSlider>().First();
        var p = slider.TranslatePoint(new Point(slider.Bounds.Width - 10, slider.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Pump();
        Assert.True(w.Session.Brush.Size > 300, $"size {w.Session.Brush.Size}");
        w.Close();
    }

    [AvaloniaFact]
    public void Tab_hides_and_restores_the_interface()
    {
        var w = Open();
        double before = w.Canvas.Bounds.Width;
        Key(w, Avalonia.Input.Key.Tab);
        Assert.False(Has(w, "QuickBar"));
        Assert.False(Has(w, "ToolStrip"));
        Assert.True(Has(w, "ShowUiButton"));
        Assert.True(w.Canvas.Bounds.Width > before);
        Key(w, Avalonia.Input.Key.Tab);
        Assert.True(Has(w, "QuickBar"));
        Assert.False(Has(w, "ShowUiButton"));
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

        Drag(w, MouseButton.Left, RawInputModifiers.None, (900, 520), (1020, 560));
        var zoomed = (uint[])w.Session.Document.ActiveLayer.Image.Pixels.Clone();
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Key(w, Avalonia.Input.Key.D0, RawInputModifiers.Control);
        Assert.Equal(z0, w.Canvas.ActualZoom, 6);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (900, 520), (1020, 560));
        var a = Bounds(zoomed, w.Session.Document.Width);
        var b = Bounds(w.Session.Document.ActiveLayer.Image.Pixels, w.Session.Document.Width);
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
        double x0 = w.Canvas.View.DocumentToView(0, 0).X;
        var p = DocToWindow(w, 500, 500);
        w.KeyPress(Avalonia.Input.Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(new Point(p.X + 120, p.Y), RawInputModifiers.LeftMouseButton);
        w.MouseUp(new Point(p.X + 120, p.Y), MouseButton.Left);
        w.KeyRelease(Avalonia.Input.Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Pump();
        Assert.Equal(x0 + 120, w.Canvas.View.DocumentToView(0, 0).X, 3);
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
        Click(w, Find<Button>(w, "ColourChip"));
        Assert.True(UiKit.AnyFlyoutOpen);
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
        Drag(w, MouseButton.Left, RawInputModifiers.None, (900, 500), (905, 500));
        w.KeyRelease(Avalonia.Input.Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Pump();

        var picked = w.Session.Foreground;
        Assert.True(picked.B > 150 && picked.R < 80, $"picked {picked}");
        Assert.Equal(1, w.Session.Document.History.UndoCount);
        Assert.False(w.Canvas.Input!.AltHeld);
        w.Close();
    }

    // ================================================================== workspaces

    [AvaloniaFact]
    public void Each_workspace_shows_its_own_chrome_around_the_same_canvas()
    {
        var w = Open();
        var canvas = w.Canvas;
        var doc = w.Session.Document;

        w.SetWorkspace(WorkspaceKind.PureSketch);
        Pump();
        Assert.False(Has(w, "QuickBar") || Has(w, "TitleBar") || Has(w, "ToolStrip") || Has(w, "OptionsBar"));
        Assert.True(Has(w, "PureMenuButton"));
        Assert.Equal(w.ClientSize.Width, canvas.Bounds.Width, 1);

        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        Assert.True(Has(w, "TitleBar") && Has(w, "OptionsBar") && Has(w, "ToolStrip") && Has(w, "PaintDock") && Has(w, "ViewChip"));
        Assert.True(Has(w, "Sectioncolour") && Has(w, "Sectionbrushes") && Has(w, "Sectionlayers"));
        Assert.False(Has(w, "StatusBar"));

        w.SetWorkspace(WorkspaceKind.Editing);
        Pump();
        Assert.True(Has(w, "EditDock") && Has(w, "StatusBar"));
        Assert.True(Has(w, "Sectionlayers") && Has(w, "Sectionproperties") && Has(w, "Sectionhistory"));
        Assert.False(Has(w, "ViewChip")); // view controls are in the status bar here
        Assert.Equal(ToolKind.RectSelect, w.Session.Tool);

        w.SetWorkspace(WorkspaceKind.PixelArt);
        Pump();
        Assert.True(Has(w, "PaletteColumn") && Has(w, "PixelPreview") && Has(w, "MenuSprite"));
        Assert.True(w.Session.PixelMode);
        Assert.True(w.Canvas.IntegerZoom && w.Canvas.PixelExact);
        Assert.Equal(ToolKind.PixelPencil, w.Session.Tool);

        Assert.Same(canvas, w.Canvas);
        Assert.Same(doc, w.Session.Document);
        w.Close();
    }

    [AvaloniaFact]
    public void Workspace_switcher_buttons_and_alt_numbers_switch_workspaces()
    {
        var w = Open();
        Click(w, Find<Button>(w, "WsC"));
        Assert.Equal(WorkspaceKind.Painting, w.Workspace.Kind);
        Key(w, Avalonia.Input.Key.D4, RawInputModifiers.Alt);
        Assert.Equal(WorkspaceKind.Editing, w.Workspace.Kind);
        Key(w, Avalonia.Input.Key.D5, RawInputModifiers.Alt);
        Assert.Equal(WorkspaceKind.PixelArt, w.Workspace.Kind);
        Key(w, Avalonia.Input.Key.D1, RawInputModifiers.Alt);
        Assert.Equal(WorkspaceKind.PureSketch, w.Workspace.Kind);
        Key(w, Avalonia.Input.Key.Tab); // back to where we came from
        Assert.Equal(WorkspaceKind.PixelArt, w.Workspace.Kind);
        Assert.False(w.Canvas.Input!.AltHeld);
        w.Close();
    }

    [AvaloniaFact]
    public void Each_workspace_remembers_its_tool_and_panel_layout()
    {
        var w = Open(workspace: WorkspaceKind.Painting);
        Key(w, Avalonia.Input.Key.G);
        Assert.Equal(ToolKind.Fill, w.Session.Tool);
        w.ToggleDockSection("colour"); // close the colour section
        Pump();
        Assert.False(w.GetVisualDescendants().OfType<ColourPanel>().Any(p => p.IsEffectivelyVisible));

        w.SetWorkspace(WorkspaceKind.Editing);
        Key(w, Avalonia.Input.Key.W);
        Assert.Equal(ToolKind.MagicWand, w.Session.Tool);
        w.ToggleDockCollapsed();
        Pump();

        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        Assert.Equal(ToolKind.Fill, w.Session.Tool);
        Assert.Contains("colour", w.WsState.ClosedSections);
        Assert.True(Has(w, "Sectionlayers"));

        w.SetWorkspace(WorkspaceKind.Editing);
        Pump();
        Assert.Equal(ToolKind.MagicWand, w.Session.Tool);
        Assert.True(w.WsState.DockCollapsed);
        Assert.False(Has(w, "Sectionlayers"));
        w.Close();
    }

    [AvaloniaFact]
    public void Repeated_workspace_switching_keeps_document_history_and_view()
    {
        var w = Open();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 300), (900, 300));
        var doc = w.Session.Document;
        var pixels = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        for (int i = 0; i < 40; i++)
        {
            w.SetWorkspace((WorkspaceKind)(i % 5));
            Pump();
        }
        Assert.Same(doc, w.Session.Document);
        Assert.Equal(pixels, doc.ActiveLayer.Image.Pixels);
        Assert.Equal(1, doc.History.UndoCount);
        w.SetWorkspace(WorkspaceKind.Painting);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(0, Alpha(w, 600, 300));
        w.Close();
    }

    // ================================================================== quick palette

    [AvaloniaFact]
    public void Right_click_opens_the_quick_palette_with_colours_tools_and_size()
    {
        var w = Open(workspace: WorkspaceKind.PureSketch);
        var p = DocToWindow(w, 960, 540);
        w.MouseMove(p);
        w.MouseDown(p, MouseButton.Right);
        w.MouseUp(p, MouseButton.Right);
        Pump();
        Assert.True(w.Radial.IsOpen);
        var eraser = Find<Button>(w, "Radialeraser");
        var centre = eraser.TranslatePoint(new Point(0, 0), w)!.Value;
        Assert.InRange(Math.Abs(centre.X - p.X), 0, RadialPalette.Diameter / 2); // opens at the pen, not in a corner
        Assert.InRange(Math.Abs(centre.Y - p.Y), 0, RadialPalette.Diameter / 2);

        Click(w, eraser);
        Assert.Equal(ToolKind.Eraser, w.Session.Tool);
        Assert.False(w.Radial.IsOpen);

        w.MouseDown(p, MouseButton.Right);
        w.MouseUp(p, MouseButton.Right);
        Pump();
        Assert.True(w.Radial.IsOpen);
        Key(w, Avalonia.Input.Key.Escape);
        Assert.False(w.Radial.IsOpen);
        Assert.False(w.Session.Document.CanUndo);
        w.Close();
    }

    [AvaloniaFact]
    public void Pure_sketch_corner_menu_switches_workspace()
    {
        var w = Open(workspace: WorkspaceKind.PureSketch);
        Click(w, Find<Button>(w, "PureMenuButton"));
        var item = w.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("menu") && b.IsEffectivelyVisible && Texts(b).Contains("Editing"));
        Click(w, item);
        Assert.Equal(WorkspaceKind.Editing, w.Workspace.Kind);
        w.Close();
    }

    // ================================================================== layers

    [AvaloniaFact]
    public void Layers_panel_adds_selects_hides_and_paints_on_the_chosen_layer()
    {
        var w = Open(workspace: WorkspaceKind.Painting);
        var doc = w.Session.Document;
        Key(w, Avalonia.Input.Key.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(2, doc.Layers.Count);
        var top = doc.ActiveLayer;
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 300), (900, 300));
        Assert.True(Pixel.A(top.Image[600, 300]) > 0);
        Assert.Equal(0, Pixel.A(doc.Layers[0].Image[600, 300]));

        Menu(w, "Layer", "Hide layer");
        Assert.False(top.Visible);
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.Projection[600, 300]); // hidden: only the paper shows
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.True(top.Visible);

        Key(w, Avalonia.Input.Key.J, RawInputModifiers.Control);
        Assert.Equal(3, doc.Layers.Count);
        Key(w, Avalonia.Input.Key.E, RawInputModifiers.Control); // merge down
        Assert.Equal(2, doc.Layers.Count);

        var panel = w.GetVisualDescendants().OfType<LayersPanel>().First(p => p.IsEffectivelyVisible);
        var rows = panel.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is Engine.Documents.Layer).ToList();
        Assert.Equal(2, rows.Count);
        rows[^1].BringIntoView();
        Pump();
        Click(w, rows[^1]); // the bottom row: the sketch layer
        Assert.Same(doc.Layers[0], doc.ActiveLayer);
        w.Close();
    }

    [AvaloniaFact]
    public void Layer_rows_rename_by_double_click_and_take_a_colour_label()
    {
        var w = Open(workspace: WorkspaceKind.Editing);
        var layer = w.Session.Document.ActiveLayer;
        Border Row() => w.GetVisualDescendants().OfType<LayersPanel>().First(p => p.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<Border>().First(b => ReferenceEquals(b.Tag, layer));
        var row = Row();
        var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), w)!.Value;
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);
        Pump();
        var box = row.GetVisualDescendants().OfType<TextBox>().Single();
        box.Text = "Line art";
        box.Focus();
        Key(w, Avalonia.Input.Key.Enter);
        Assert.Equal("Line art", layer.Name);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal("Sketch", layer.Name);

        Click(w, Row(), MouseButton.Right);
        var blue = w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && ToolTip.GetTip(b) as string == "Blue label");
        Click(w, blue);
        Assert.Equal(Engine.Documents.LayerLabel.Blue, layer.Label);
        w.Close();
    }

    [AvaloniaFact]
    public void Quick_sketch_layers_popover_works()
    {
        var w = Open();
        Click(w, Find<Button>(w, "LayersChip"));
        var panel = w.GetVisualDescendants().OfType<LayersPanel>().First(p => p.IsEffectivelyVisible);
        var add = panel.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) is string t && t.StartsWith("New layer"));
        Click(w, add);
        Assert.Equal(2, w.Session.Document.Layers.Count);
        w.Close();
    }

    // ================================================================== editing

    [AvaloniaFact]
    public void Editing_select_transform_with_keys_and_undo()
    {
        var w = Open(workspace: WorkspaceKind.Editing);
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.SetSize(20);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 300), (700, 300));
        Key(w, Avalonia.Input.Key.M);
        Assert.Equal(ToolKind.RectSelect, w.Session.Tool);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (250, 250), (500, 350));
        Assert.NotNull(w.Session.Document.Selection);

        Key(w, Avalonia.Input.Key.T, RawInputModifiers.Control);
        Assert.NotNull(w.Session.ActiveTransform);
        Assert.True(Has(w, "OptionsBar"));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (400, 300), (400, 500));
        Key(w, Avalonia.Input.Key.Enter);
        Assert.Null(w.Session.ActiveTransform);
        Assert.True(Alpha(w, 400, 500) > 0);
        Assert.Equal(0, Alpha(w, 400, 300));
        Assert.True(Alpha(w, 650, 300) > 0); // outside the selection: not moved

        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.True(Alpha(w, 400, 300) > 0);
        w.Close();
    }

    [AvaloniaFact]
    public void Escape_cancels_a_free_transform()
    {
        var w = Open(workspace: WorkspaceKind.Editing);
        w.Session.SelectTool(ToolKind.Brush);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 300), (700, 300));
        var before = (uint[])w.Session.Document.ActiveLayer.Image.Pixels.Clone();
        Key(w, Avalonia.Input.Key.T, RawInputModifiers.Control);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (500, 300), (500, 600));
        Key(w, Avalonia.Input.Key.Escape);
        Assert.Null(w.Session.ActiveTransform);
        Assert.Equal(before, w.Session.Document.ActiveLayer.Image.Pixels);
        Assert.Equal(1, w.Session.Document.History.UndoCount);
        w.Close();
    }

    [AvaloniaFact]
    public void Image_size_dialog_resizes_every_layer_and_undoes()
    {
        var w = Open(workspace: WorkspaceKind.Editing);
        w.Session.AddLayer();
        _ = w.ImageSizeAsync();
        Pump();
        Assert.True(w.DialogLayer.IsVisible);
        var width = w.DialogLayer.GetVisualDescendants().OfType<TextBox>().First();
        width.Text = "960";
        Pump();
        Key(w, Avalonia.Input.Key.Enter);
        Assert.False(w.DialogLayer.IsVisible);
        Assert.Equal(960, w.Session.Document.Width);
        Assert.Equal(540, w.Session.Document.Height); // kept proportions
        Assert.All(w.Session.Document.Layers, l => Assert.Equal(960, l.Image.Width));
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(1920, w.Session.Document.Width);
        w.Close();
    }

    [AvaloniaFact]
    public void Adjustment_dialog_previews_and_cancel_restores()
    {
        var w = Open(workspace: WorkspaceKind.Editing);
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.SetForeground(new ColorRgb(200, 40, 40));
        w.Session.SetSize(40);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 300), (700, 300));
        uint original = w.Session.Document.Projection[500, 300];
        _ = w.AdjustAsync(Dialogs.AdjustKind.HueSaturation);
        Pump();
        var slider = w.DialogLayer.GetVisualDescendants().OfType<DnSlider>().First(); // hue
        var p = slider.TranslatePoint(new Point(slider.Bounds.Width * 0.85, slider.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Pump();
        Assert.NotEqual(original, w.Session.Document.Projection[500, 300]); // live preview on screen
        Assert.Equal(1, w.Session.Document.History.UndoCount);              // but nothing applied yet
        Key(w, Avalonia.Input.Key.Escape);
        Assert.Equal(original, w.Session.Document.Projection[500, 300]);
        Assert.Equal(1, w.Session.Document.History.UndoCount);
        w.Close();
    }

    [AvaloniaFact]
    public void Menus_run_real_commands()
    {
        var w = Open(workspace: WorkspaceKind.Editing);
        Menu(w, "Select", "Select all");
        Assert.NotNull(w.Session.Document.Selection);
        Menu(w, "Select", "Deselect");
        Assert.Null(w.Session.Document.Selection);
        Menu(w, "Image", "Rotate picture 90° clockwise");
        Assert.Equal(1080, w.Session.Document.Width);
        Menu(w, "Edit", "Undo");
        Assert.Equal(1920, w.Session.Document.Width);
        Menu(w, "Layer", "New layer");
        Assert.Equal(2, w.Session.Document.Layers.Count);
        Menu(w, "Window", "Pixel Art");
        Assert.Equal(WorkspaceKind.PixelArt, w.Workspace.Kind);
        Menu(w, "View", "Pixel grid");
        Assert.False(w.WsState.ShowGrid);
        w.Close();
    }

    [AvaloniaFact]
    public void A_narrow_window_can_scroll_the_options_bar_with_the_wheel()
    {
        var w = Open(900, 700, WorkspaceKind.Painting);
        var scroll = Find<OptionsBar>(w, "OptionsBar").GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width, "the options don't fit at 900 px");
        var p = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), w)!.Value;
        w.MouseWheel(p, new Vector(0, -3));
        Pump();
        Assert.True(scroll.Offset.X > 0, "the wheel should reveal the rest of the bar");
        w.Close();
    }

    [AvaloniaFact]
    public void Editing_status_bar_shows_size_and_layer()
    {
        var w = Open(workspace: WorkspaceKind.Editing);
        var text = string.Join(" ", Texts(Find<StatusBar>(w, "StatusBar")));
        Assert.Contains("1920 × 1080 px", text);
        Assert.Contains("Layer: Sketch", text);
        w.Close();
    }

    // ================================================================== pixel art

    [AvaloniaFact]
    public void Pixel_art_draws_exact_pixels_at_whole_number_zoom()
    {
        var w = Open(workspace: WorkspaceKind.PixelArt);
        w.Session.ReplaceDocument(new Engine.Documents.Document(32, 32, null, "Sprite"), null);
        Pump();
        Assert.Equal(Math.Round(w.Canvas.ActualZoom), w.Canvas.ActualZoom, 6);
        Assert.True(w.Canvas.ActualZoom >= 8);
        w.Session.SetForeground(new ColorRgb(46, 42, 59));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (4.5, 4.5), (20.5, 4.5));
        var img = w.Session.Document.ActiveLayer.Image;
        Assert.All(Enumerable.Range(4, 17), x => Assert.Equal(Pixel.Opaque(new ColorRgb(46, 42, 59)), img[x, 4]));
        Assert.Equal(Pixel.Transparent, img[4, 5]);
        Key(w, Avalonia.Input.Key.OemPlus, RawInputModifiers.Control);
        Assert.Equal(Math.Round(w.Canvas.ActualZoom), w.Canvas.ActualZoom, 6);
        Key(w, Avalonia.Input.Key.L); // pixel line
        Assert.Equal(ToolKind.PixelLine, w.Session.Tool);
        Key(w, Avalonia.Input.Key.OemQuotes, RawInputModifiers.Control);
        Assert.False(w.Canvas.ShowGrid);
        w.Close();
    }

    [AvaloniaFact]
    public void Palette_column_picks_foreground_and_background()
    {
        var w = Open(workspace: WorkspaceKind.PixelArt);
        var palette = w.Session.Document.Palette.Colors;
        var column = w.GetVisualDescendants().OfType<PaletteColumn>().First();
        var swatches = column.GetVisualDescendants().OfType<Border>().Where(b => b.Cursor is not null && b.Width == 28).ToList();
        Assert.Equal(palette.Count, swatches.Count);
        Click(w, swatches[5]);
        Assert.Equal(palette[5], w.Session.Foreground);
        swatches = column.GetVisualDescendants().OfType<Border>().Where(b => b.Cursor is not null && b.Width == 28).ToList(); // rebuilt
        Click(w, swatches[9], MouseButton.Right);
        Assert.Equal(palette[9], w.Session.Background);
        w.Close();
    }
}
