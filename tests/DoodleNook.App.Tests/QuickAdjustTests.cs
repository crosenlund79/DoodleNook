using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 018 — the owner's notebook: hold a key for a small card at the pen with zoom, brush size, opacity and
/// smoothing, "once you let go of the key the change is made"; and a size read-out the pen can't hide.
/// </summary>
public sealed class QuickAdjustTests
{
    private static MainWindow Painting(WorkspaceKind kind = WorkspaceKind.Painting)
    {
        var w = Open(1280, 800, kind);
        w.Session.ReplaceDocument(new Document(800, 600, ColorRgb.White, "Background"), null);
        w.Session.AddLayer();
        w.Canvas.FitToView();
        Pump();
        return w;
    }

    /// <summary>The pen hovering over the canvas at (x, y) in the canvas's coordinates.</summary>
    private static void Hover(MainWindow w, double x, double y)
    {
        var p = w.Canvas.TranslatePoint(new Point(x, y), w)!.Value;
        w.MouseMove(p);
        Pump();
    }

    private static void Hold(MainWindow w, Key key)
    {
        w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
        w.OpenQuickAdjustIfHeld(); // the hold delay has passed
        Pump();
    }

    private static void Release(MainWindow w, Key key)
    {
        w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
    }

    private static List<string> Rows(MainWindow w) =>
        w.GetVisualDescendants().OfType<DnSlider>().Where(s => s.IsEffectivelyVisible && s.Name?.StartsWith("QuickAdjust") == true)
            .Select(s => s.Name!["QuickAdjust".Length..]).ToList();

    private static string Value(MainWindow w, string row) => Find<TextBlock>(w, "QuickAdjust" + row + "Value").Text!;

    /// <summary>Drags a slider of the card from one fraction of its track to another, as the pen would.</summary>
    private static void DragSlider(MainWindow w, string row, double from, double to)
    {
        var s = Find<DnSlider>(w, "QuickAdjust" + row);
        double y = s.Bounds.Height / 2;
        var a = s.TranslatePoint(new Point(s.Bounds.Width * from, y), w)!.Value;
        var b = s.TranslatePoint(new Point(s.Bounds.Width * to, y), w)!.Value;
        w.MouseMove(a);
        w.MouseDown(a, MouseButton.Left);
        w.MouseMove(b, RawInputModifiers.LeftMouseButton);
        w.MouseUp(b, MouseButton.Left);
        Pump();
    }

    [AvaloniaFact]
    public void A_tap_of_B_or_E_just_picks_the_tool()
    {
        var w = Painting();
        w.Session.SelectTool(ToolKind.Eyedropper);
        Key(w, Avalonia.Input.Key.B); // press and let go
        w.OpenQuickAdjustIfHeld();   // the delay passing after the key is let go
        Pump();
        Assert.Equal(ToolKind.Brush, w.Session.Tool);
        Assert.False(w.QuickAdjust.IsOpen);
        Key(w, Avalonia.Input.Key.E);
        w.OpenQuickAdjustIfHeld();
        Assert.Equal(ToolKind.Eraser, w.Session.Tool);
        Assert.False(w.QuickAdjust.IsOpen);
        w.Close();
    }

    [AvaloniaFact]
    public void Holding_B_opens_the_card_above_the_pen_and_letting_go_keeps_the_change()
    {
        var w = Painting();
        w.Session.SelectTool(ToolKind.Eyedropper);
        Hover(w, 600, 450);
        Hold(w, Avalonia.Input.Key.B);
        Assert.True(w.QuickAdjust.IsOpen);
        Assert.Equal(ToolKind.Brush, w.Session.Tool); // B picked the brush at once
        Assert.Equal(["Zoom", "Size", "Opacity", "Smoothing"], Rows(w));
        // Above the pen, clear of the pen tip and hand.
        var card = w.QuickAdjust.CardBounds;
        Assert.True(card.Bottom <= 450 - PenReadout.Gap + 0.5, $"card {card}");
        Assert.True(card.Left <= 600 && card.Right >= 600);

        // Size with the pen on the slider; the value shows on the left of it.
        double before = w.Session.CurrentSize;
        DragSlider(w, "Size", 0.5, 0.9);
        double after = w.Session.CurrentSize;
        Assert.True(after > before, $"{before} → {after}");
        Assert.Equal($"{after:0.#} px", Value(w, "Size"));
        var value = Find<TextBlock>(w, "QuickAdjustSizeValue");
        var slider = Find<DnSlider>(w, "QuickAdjustSize");
        Assert.True(value.TranslatePoint(new Point(value.Bounds.Width, 0), w)!.Value.X <= slider.TranslatePoint(new Point(0, 0), w)!.Value.X);

        // Opacity and smoothing.
        DragSlider(w, "Opacity", 0.9, 0.3);
        Assert.InRange(w.Session.Brush.Opacity, 0.2, 0.5);
        DragSlider(w, "Smoothing", 0.1, 0.8);
        Assert.InRange(w.Session.Brush.Smoothing, 0.6, 0.95);

        Release(w, Avalonia.Input.Key.B);
        Assert.False(w.QuickAdjust.IsOpen);
        Assert.Equal(after, w.Session.CurrentSize); // kept
        Assert.InRange(w.Session.Brush.Opacity, 0.2, 0.5);
        w.Close();
    }

    [AvaloniaFact]
    public void Holding_E_adjusts_the_eraser_and_dragging_anywhere_changes_its_size()
    {
        var w = Painting();
        Hover(w, 500, 500);
        Hold(w, Avalonia.Input.Key.E);
        Assert.Equal(ToolKind.Eraser, w.Session.Tool);
        Assert.Equal(["Zoom", "Size", "Smoothing"], Rows(w)); // the eraser has no opacity
        double before = w.Session.CurrentSize;
        double brush = w.Session.Brush.Size;
        // Drag anywhere outside the card: right is bigger.
        var a = w.Canvas.TranslatePoint(new Point(300, 650), w)!.Value;
        w.MouseMove(a);
        w.MouseDown(a, MouseButton.Left);
        w.MouseMove(a + new Point(200, 0), RawInputModifiers.LeftMouseButton);
        w.MouseUp(a + new Point(200, 0), MouseButton.Left);
        Pump();
        Assert.True(w.Session.CurrentSize > before * 1.5, $"{before} → {w.Session.CurrentSize}");
        Assert.Equal(brush, w.Session.Brush.Size); // the brush's own size is untouched
        Release(w, Avalonia.Input.Key.E);
        w.Close();
    }

    [AvaloniaFact]
    public void Holding_Z_zooms_around_the_pen_and_Esc_puts_everything_back()
    {
        var w = Painting();
        w.Session.SelectTool(ToolKind.Move);
        Hover(w, 520, 380);
        var (ux, uy) = w.Canvas.View.ViewToDocument(520, 380);
        double zoom = w.Canvas.ActualZoom;
        Hold(w, Avalonia.Input.Key.Z);
        Assert.Equal(ToolKind.Move, w.Session.Tool); // Z keeps the tool
        Assert.Equal(["Zoom"], Rows(w));
        // Drag anywhere: up and right zooms in, around the spot under the pen.
        var a = w.Canvas.TranslatePoint(new Point(300, 650), w)!.Value;
        w.MouseMove(a);
        w.MouseDown(a, MouseButton.Left);
        w.MouseMove(a + new Point(160, -40), RawInputModifiers.LeftMouseButton);
        w.MouseUp(a + new Point(160, -40), MouseButton.Left);
        Pump();
        Assert.True(w.Canvas.ActualZoom > zoom * 1.8, $"{zoom} → {w.Canvas.ActualZoom}");
        var (vx, vy) = w.Canvas.View.DocumentToView(ux, uy);
        Assert.InRange(vx, 519, 521);
        Assert.InRange(vy, 379, 381);
        Assert.Equal($"{w.Canvas.ActualZoom * 100:0}%", Value(w, "Zoom"));

        // Esc (key still held): back as it was, and the held key doesn't bring the card back.
        Key(w, Avalonia.Input.Key.Escape);
        Assert.False(w.QuickAdjust.IsOpen);
        Assert.Equal(zoom, w.Canvas.ActualZoom, 3);
        w.KeyPress(Avalonia.Input.Key.Z, RawInputModifiers.None, PhysicalKey.None, null); // the key repeating
        w.OpenQuickAdjustIfHeld();
        Assert.False(w.QuickAdjust.IsOpen);
        Release(w, Avalonia.Input.Key.Z);
        w.Close();
    }

    [AvaloniaFact]
    public void Nothing_is_painted_while_the_key_is_held()
    {
        var w = Painting();
        w.Session.SelectTool(ToolKind.Brush);
        Hover(w, 600, 400);
        Hold(w, Avalonia.Input.Key.B);
        var pixels = w.Session.Document.ActiveLayer.Image.Pixels.ToArray();
        int steps = w.Session.Document.History.UndoCount;
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 400), (500, 450)); // outside the card
        Assert.Equal(pixels, w.Session.Document.ActiveLayer.Image.Pixels);
        Assert.Equal(steps, w.Session.Document.History.UndoCount);
        Release(w, Avalonia.Input.Key.B);
        // Afterwards the brush paints as usual.
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 400), (500, 450));
        Assert.Equal(steps + 1, w.Session.Document.History.UndoCount);
        w.Close();
    }

    [AvaloniaFact]
    public void The_card_waits_while_a_stroke_is_being_drawn()
    {
        var w = Painting();
        w.Session.SelectTool(ToolKind.Brush);
        var p = DocToWindow(w, 200, 200);
        w.MouseMove(p);
        w.MouseDown(p, MouseButton.Left);
        w.MouseMove(DocToWindow(w, 260, 230), RawInputModifiers.LeftMouseButton);
        Hold(w, Avalonia.Input.Key.E); // pressed mid-stroke
        Assert.False(w.QuickAdjust.IsOpen);
        w.MouseUp(DocToWindow(w, 260, 230), MouseButton.Left);
        Release(w, Avalonia.Input.Key.E);
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public void In_Pixel_Art_B_holds_the_pixel_pencil_with_whole_pixel_sizes()
    {
        var w = Painting(WorkspaceKind.PixelArt);
        Hover(w, 600, 450);
        Hold(w, Avalonia.Input.Key.B);
        Assert.Equal(ToolKind.PixelPencil, w.Session.Tool);
        Assert.Equal(["Zoom", "Size"], Rows(w));
        DragSlider(w, "Size", 0.02, 0.2);
        Assert.Equal(Math.Round(w.Session.CurrentSize), w.Session.CurrentSize);
        Assert.True(w.Session.CurrentSize > 1);
        Release(w, Avalonia.Input.Key.B);
        w.Close();
    }

    [AvaloniaFact]
    public void Size_read_outs_sit_above_the_pen()
    {
        // Above the pen tip, centred; near the top edge, beside the pen instead; always inside the area.
        var (x, y) = PenReadout.Place(400, 300, 60, 28, 1000, 700);
        Assert.Equal((370.0, 300 - PenReadout.Gap - 28), (x, y));
        (x, y) = PenReadout.Place(400, 30, 60, 28, 1000, 700);
        Assert.True(x + 60 <= 400 - PenReadout.Gap + 0.01 && y < 30 && y + 28 > 30); // to the left, level with the pen
        (x, y) = PenReadout.Place(10, 20, 60, 28, 1000, 700);
        Assert.Equal(10 + PenReadout.Gap, x); // no room on the left either: on the right

        // The quick palette: while its centre is dragged, the size also shows above the pen.
        var w = Painting();
        w.Session.SelectTool(ToolKind.Brush);
        w.Radial.Open(600, 400);
        Pump();
        var c = w.Radial.TranslatePoint(w.Radial.Centre, w)!.Value;
        w.MouseMove(c);
        w.MouseDown(c, MouseButton.Left);
        var pen = c + new Point(60, 0);
        w.MouseMove(pen, RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.True(w.Radial.Readout is { } r && r.Text == $"{w.Session.CurrentSize:0.#} px", "no read-out while dragging");
        var penInRadial = w.TranslatePoint(pen, w.Radial)!.Value;
        Assert.True(w.Radial.Readout!.Value.Bounds.Bottom <= penInRadial.Y - PenReadout.Gap + 0.5, $"read-out {w.Radial.Readout!.Value.Bounds} pen {penInRadial} radial {w.Radial.Bounds}");
        w.MouseUp(pen, MouseButton.Left);
        Pump();
        Assert.Null(w.Radial.Readout);
        w.Close();
    }

    [AvaloniaFact]
    public void On_the_Kamvas_at_150_percent_the_card_stays_inside_the_canvas_in_every_corner()
    {
        var w = Open(1280, 720, WorkspaceKind.Painting);
        w.SetRenderScaling(1.5);
        Pump();
        var area = ((Control)w.QuickAdjust.Parent!).Bounds; // the canvas area the card lives in
        foreach (var (x, y) in new[] { (12.0, 12.0), (area.Width - 12, 12.0), (12.0, area.Height - 12), (area.Width - 12, area.Height - 12), (area.Width / 2, area.Height / 2) })
        {
            w.QuickAdjust.Open(Avalonia.Input.Key.B, x, y);
            Pump();
            var card = w.QuickAdjust.CardBounds;
            Assert.True(card.Left >= 0 && card.Top >= 0 && card.Right <= area.Width + 0.5 && card.Bottom <= area.Height + 0.5, $"pen {x},{y}: card {card} in {area}");
            Assert.False(card.Contains(new Point(x, y)), $"pen {x},{y} is under the card {card}");
            w.QuickAdjust.Close(keep: true);
        }
        w.Close();
    }
}
