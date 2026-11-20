using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Painting;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 010 — the quick palette (the Alt+1 wheel): comfortable brush sizing and choosing brushes in it.</summary>
public sealed class QuickPaletteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-quick-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // Nothing to clean up.
        }
    }

    private MainWindow Window(WorkspaceKind ws = WorkspaceKind.PureSketch)
    {
        var w = Open(1280, 800, ws);
        w.UseLibrary(new CreativeLibrary(Path.Combine(_dir, "Library")));
        Pump();
        return w;
    }

    /// <summary>Draws a frame, so controls built just now can be hit by the pointer (headless hit-testing uses the last frame).</summary>
    private static void Render(MainWindow w)
    {
        Pump();
        w.CaptureRenderedFrame()?.Dispose();
    }

    private static Point CentreOf(MainWindow w, Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;

    [Fact]
    public void Size_drag_is_gentle_for_short_moves_and_strong_for_long_ones()
    {
        // A short move is a fine correction…
        double small = RadialPalette.SizeForDrag(20, 15, pixels: false);
        Assert.InRange(small, 21, 22.5);
        // …a long one a big change, in both directions, and Shift is finer still.
        Assert.True(RadialPalette.SizeForDrag(20, 300, pixels: false) > 150);
        Assert.True(RadialPalette.SizeForDrag(20, -300, pixels: false) < 3);
        Assert.InRange(RadialPalette.SizeForDrag(20, 15, pixels: false, fine: true), 20, 20.6);
        // Pixel sizes move in whole pixels, one per 18 view units.
        Assert.Equal(3, RadialPalette.SizeForDrag(1, 40, pixels: true));
        // Monotonic: further always means bigger.
        double last = 0;
        for (int d = -400; d <= 400; d += 10)
        {
            double v = RadialPalette.SizeForDrag(20, d, pixels: false);
            Assert.True(v > last);
            last = v;
        }
    }

    [AvaloniaFact]
    public void Dragging_the_centre_sizes_the_brush_smoothly_even_beyond_the_wheel()
    {
        var w = Window();
        w.Session.SetSize(20);
        w.Radial.Open(500, 400);
        Pump();
        var centre = Find<Border>(w, "RadialSize");
        var start = CentreOf(w, centre);
        w.MouseMove(start);
        w.MouseDown(start, MouseButton.Left);
        // A 2-unit wobble is not a drag.
        w.MouseMove(new Point(start.X + 2, start.Y), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.Equal(20, w.Session.CurrentSize);
        // Small moves fine-tune; the control keeps receiving the drag (it isn't rebuilt under the pointer).
        w.MouseMove(new Point(start.X + 12, start.Y), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.InRange(w.Session.CurrentSize, 21, 22);
        Assert.Same(centre, Find<Border>(w, "RadialSize"));
        // Far outside the wheel: a big change, with the size drawn at its real size and the number in the centre.
        w.MouseMove(new Point(start.X + 330, start.Y - 20), RawInputModifiers.LeftMouseButton);
        Pump();
        double big = w.Session.CurrentSize;
        Assert.True(big > 200, $"size {big}");
        Assert.Equal($"{big:0.#} px", Find<TextBlock>(w, "RadialSizeText").Text);
        w.MouseMove(new Point(start.X - 60, start.Y), RawInputModifiers.LeftMouseButton);
        Pump();
        Assert.InRange(w.Session.CurrentSize, 14, 17);
        w.MouseUp(new Point(start.X - 60, start.Y), MouseButton.Left);
        Pump();
        Assert.True(w.Radial.IsOpen); // sizing doesn't close the wheel
        Assert.False(w.Session.Document.CanUndo); // and changes nothing in the picture
        w.Close();
    }

    [AvaloniaFact]
    public void The_wheel_stays_whole_on_screen_near_the_edges_and_never_takes_the_keyboard()
    {
        var w = Window();
        var area = w.Canvas.Bounds;
        foreach (var (x, y) in new[] { (2.0, 2.0), (area.Width - 2, area.Height - 2), (2.0, area.Height - 2), (area.Width - 2, 2.0) })
        {
            w.Radial.Open(x, y);
            Pump();
            foreach (var name in new[] { "Radialundo", "Radialredo", "Radialeraser", "RadialBrush", "RadialSize" })
            {
                var c = w.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);
                var tl = c.TranslatePoint(new Point(0, 0), w.Canvas)!.Value;
                Assert.True(tl.X >= 0 && tl.Y >= 0 && tl.X + c.Bounds.Width <= area.Width + 0.5 && tl.Y + c.Bounds.Height <= area.Height + 0.5,
                    $"{name} at {tl} outside {area} (opened at {x},{y})");
            }
            w.Radial.Close();
        }
        w.Radial.Open(400, 400);
        Pump();
        Click(w, Find<Button>(w, "Radialswap"));
        Assert.True(w.Radial.IsOpen);
        Assert.IsNotType<Button>(w.FocusManager?.GetFocusedElement());
        w.Close();
    }

    [AvaloniaFact]
    public void Brushes_are_chosen_in_the_wheel_including_your_own_and_each_keeps_its_size()
    {
        var w = Window();
        var s = w.Session;
        var mine = s.Library.AddBrush("My chalky ink", s.Library.FindBrush("ink")!.Settings with { Grain = 0.4 }, "Test brush");
        s.ApplyPreset("pencil");
        s.SetSize(33);

        // Alt+1 wheel → the brush pill → choose a brush: the wheel stays open for sizing.
        w.Radial.Open(500, 380);
        Pump();
        Render(w);
        Click(w, Find<Button>(w, "RadialBrush"));
        Assert.True(Has(w, "RadialBrushPicker"));
        var tile = Find<Button>(w, "BrushTile_" + mine.Id);
        tile.BringIntoView();
        Render(w);
        Click(w, tile);
        Assert.Equal(mine.Id, s.BrushPresetId);
        Assert.Equal(ToolKind.Brush, s.Tool);
        Assert.True(w.Radial.IsOpen);
        Assert.False(Has(w, "RadialBrushPicker"));
        Assert.Equal(mine.Settings.Size, s.Brush.Size); // first use: the brush's own size
        Assert.Equal(0.4, s.Brush.Grain);            // and all its preset settings
        s.SetSize(61);

        // Back to the pencil from "Recent": it comes back at the size it was used at.
        Render(w);
        Click(w, Find<Button>(w, "RadialBrush"));
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Recent");
        var pencil = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BrushTile_pencil" && b.IsEffectivelyVisible);
        pencil.BringIntoView();
        Render(w);
        Click(w, pencil);
        Assert.Equal("pencil", s.BrushPresetId);
        Assert.Equal(33, s.Brush.Size);
        s.ApplyPreset(mine.Id);
        Assert.Equal(61, s.Brush.Size);
        Assert.Equal([mine.Id, "pencil"], s.RecentBrushes.Take(2));

        // Remembered across restarts (settings.json).
        var settings = new Settings.AppSettings();
        settings.CaptureFrom(s);
        var again = new EditorSession();
        again.SetLibrary(s.Library);
        settings.ApplyTo(again);
        Assert.Equal(mine.Id, again.BrushPresetId);
        again.ApplyPreset("pencil");
        Assert.Equal(33, again.Brush.Size);
        Assert.Equal("pencil", again.RecentBrushes[0]);
        w.Close();
    }
}
