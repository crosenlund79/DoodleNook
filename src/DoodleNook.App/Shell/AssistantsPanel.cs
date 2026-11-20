using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Shell;

/// <summary>
/// The drawing assistants in one small pop-over (View › Drawing assistants, or the ▾ beside the mirror button):
/// symmetry and where it is centred, the perspective assistant, and snapping. Changes apply at once.
/// </summary>
public sealed class AssistantsPanel : StackPanel
{
    private readonly List<Action> _refresh = new();

    public AssistantsPanel(MainWindow app)
    {
        var s = app.Session;
        Width = 300;
        Spacing = 7;
        Name = "AssistantsPanel";

        Children.Add(UiKit.Heading("Symmetry"));
        bool pixel = s.PixelMode;
        var (sym, r1) = UiKit.Segmented(
            [(Symmetry.None, null, "Off", "No mirroring"), (Symmetry.Vertical, "sym", null, "Mirror left ↔ right"), (Symmetry.Horizontal, null, "↕", "Mirror top ↕ bottom"),
             (Symmetry.Both, null, "4×", "Mirror both ways"), (Symmetry.Radial, null, "✳", "Repeat around the centre (radial)")],
            () => pixel ? s.Pixel.Symmetry : s.Symmetry, s.SetSymmetry, compact: true);
        _refresh.Add(r1);
        Children.Add(sym);
        var (spokes, r2) = UiKit.Slider("Repeats", 2, 16, () => s.Assist.Spokes, v => s.SetAssistOptions(s.Assist with { Spokes = (int)Math.Round(v) }), v => $"{v:0}",
            trackWidth: 120, tip: "How many times radial symmetry repeats each stroke");
        _refresh.Add(r2);
        Children.Add(spokes);
        var centre = UiKit.Button(null, "Centre", "Put the symmetry centre back in the middle of the picture",
            () => s.SetAssistOptions(s.Assist with { OriginX = null, OriginY = null }));
        centre.Height = 28;
        Children.Add(UiKit.Row(8, centre, Hint("Move tool: drag the ✛ to move the centre")));
        _refresh.Add(() => spokes.IsVisible = (pixel ? s.Pixel.Symmetry : s.Symmetry) == Symmetry.Radial);

        Children.Add(UiKit.HSep());
        Children.Add(UiKit.Heading("Perspective"));
        var (persp, r3) = UiKit.Segmented([(0, null, "Off", "No perspective lines"), (1, null, "1-point", "One vanishing point"), (2, null, "2-point", "Two vanishing points")],
            () => s.Document.Perspective.Points, v => s.SetPerspective(v), compact: true);
        _refresh.Add(r3);
        Children.Add(persp);
        var (hold, r4) = UiKit.Check("Hold strokes to the lines", "Brush strokes follow the perspective line nearest their direction",
            () => s.Assist.PerspectiveAssist, v => s.SetAssistOptions(s.Assist with { PerspectiveAssist = v }));
        var (show, r5) = UiKit.Check("Show the lines", "Show the perspective lines (hidden lines don't guide strokes)",
            () => s.Assist.ShowPerspective, v => s.SetAssistOptions(s.Assist with { ShowPerspective = v }));
        _refresh.Add(r4);
        _refresh.Add(r5);
        Children.Add(hold);
        Children.Add(show);
        Children.Add(Hint("Move tool: drag the numbered points"));

        Children.Add(UiKit.HSep());
        Children.Add(UiKit.Heading("Snapping"));
        var (snap, r6) = UiKit.Check("Snap to canvas, guides and shapes", "Moving text and shapes, drawing shapes and guides stick to edges and centres (Ctrl+Shift+;)",
            () => s.Assist.Snap, v => s.SetAssistOptions(s.Assist with { Snap = v }));
        var (grid, r7) = UiKit.Check("Snap to a grid", "Also stick to a grid of this size", () => s.Assist.SnapGrid, v => s.SetAssistOptions(s.Assist with { SnapGrid = v }));
        var (size, r8) = UiKit.Slider("Grid", 2, 256, () => s.Assist.GridSize, v => s.SetAssistOptions(s.Assist with { GridSize = Math.Round(v) }), v => $"{v:0} px",
            trackWidth: 120, log: true);
        _refresh.Add(r6);
        _refresh.Add(r7);
        _refresh.Add(r8);
        Children.Add(snap);
        Children.Add(grid);
        Children.Add(size);
        _refresh.Add(() => size.IsVisible = s.Assist.SnapGrid);

        s.StateChanged += Refresh;
        DetachedFromVisualTree += (_, _) => s.StateChanged -= Refresh;
        Refresh();
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text, Classes = { "dim" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private void Refresh()
    {
        foreach (var r in _refresh) r();
    }
}
