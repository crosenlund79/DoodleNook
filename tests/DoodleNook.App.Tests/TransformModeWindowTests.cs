using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 010 — Perspective and Warp in the transform (Workflow D), driven with the mouse on the canvas.</summary>
public class TransformModeWindowTests
{
    private static MainWindow Sign()
    {
        var w = Open(1280, 800, WorkspaceKind.Editing);
        var img = new RasterImage(400, 300);
        for (int y = 100; y < 180; y++)
            for (int x = 100; x < 260; x++)
                img[x, y] = Pixel.Opaque((x / 20) % 2 == 0 ? new ColorRgb(200, 40, 40) : new ColorRgb(240, 220, 160));
        w.Session.ReplaceDocument(Document.FromImage(img, "Sign"), null);
        w.Canvas.FitToView();
        Pump();
        w.Session.Document.SetSelection(SelectionMask.Rectangle(400, 300, new PixelRect(100, 100, 160, 80)), "Select");
        Pump();
        return w;
    }

    [AvaloniaFact]
    public void Perspective_drags_a_corner_cancel_restores_and_apply_is_one_undo_step()
    {
        var w = Sign();
        var s = w.Session;
        var original = (uint[])s.Document.ActiveLayer.Image.Pixels.Clone();
        int steps = s.Document.History.UndoCount; // the selection
        w.TransformIn(TransformMode.Perspective);
        Pump();
        Assert.Equal(TransformMode.Perspective, s.ActiveTransform!.Mode);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Perspective");
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "↔" && t.IsEffectivelyVisible); // free-only buttons hidden

        // Drag the top-left corner inward: preview only.
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 100), (120, 110), (140, 120));
        Assert.Equal((140.0, 120.0), (Math.Round(s.ActiveTransform!.Corners[0].X), Math.Round(s.ActiveTransform.Corners[0].Y)));
        Key(w, Avalonia.Input.Key.Escape);
        Assert.Null(s.ActiveTransform);
        Assert.Equal(original, s.Document.ActiveLayer.Image.Pixels);
        Assert.Equal(steps, s.Document.History.UndoCount);

        // Again, and apply with Enter.
        w.TransformIn(TransformMode.Perspective);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 100), (140, 120));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (260, 100), (230, 125));
        Key(w, Avalonia.Input.Key.Enter);
        Assert.Null(s.ActiveTransform);
        Assert.Equal(0u, s.Document.ActiveLayer.Image[110, 105]);       // the corner moved away
        Assert.NotEqual(0u, s.Document.ActiveLayer.Image[180, 130]);   // the middle is still the sign
        Assert.NotEqual(0u, s.Document.ActiveLayer.Image[105, 175]);   // the bottom keeps its corners
        Assert.Equal(steps + 1, s.Document.History.UndoCount);
        w.Undo();
        Assert.Equal(original, s.Document.ActiveLayer.Image.Pixels);
        w.Close();
    }

    [AvaloniaFact]
    public void Warp_bends_the_selection_by_its_grid_points_and_moves_it_from_inside()
    {
        var w = Sign();
        var s = w.Session;
        w.TransformIn(TransformMode.Warp);
        var t = s.ActiveTransform!;
        Assert.Equal(TransformMode.Warp, t.Mode);
        var p13 = t.Warp!.Points[13]; // bottom row, second point
        var p14 = t.Warp.Points[14];
        Drag(w, MouseButton.Left, RawInputModifiers.None, (p13.X, p13.Y), (p13.X, p13.Y + 20), (p13.X, p13.Y + 40));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (p14.X, p14.Y), (p14.X, p14.Y + 40));
        Assert.Equal(p13.Y + 40, Math.Round(s.ActiveTransform!.Warp!.Points[13].Y));
        // Reset straightens the grid; Warp → Free → Warp starts again from the plain placement.
        Click(w, w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Reset")));
        Assert.Equal(p13.Y, Math.Round(s.ActiveTransform!.Warp!.Points[13].Y));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (p13.X, p13.Y), (p13.X, p13.Y + 40));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (p14.X, p14.Y), (p14.X, p14.Y + 40));
        Key(w, Avalonia.Input.Key.Enter);
        var img = s.Document.ActiveLayer.Image;
        Assert.NotEqual(0u, img[180, 205]);  // the middle of the bottom edge sags
        Assert.Equal(0u, img[102, 205]);     // the corners stay
        Assert.True(s.Document.CanUndo);
        w.Close();
    }
}
