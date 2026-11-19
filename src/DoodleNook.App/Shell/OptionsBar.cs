using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

using SelectionMode = DoodleNook.Engine.Documents.SelectionMode;

namespace DoodleNook.App.Shell;

/// <summary>
/// Builds the context options of a tool (the playground's ".opts" bar): what the current tool can do right now,
/// including the "next step" buttons when a selection exists, the free-transform bar and the crop confirmation.
/// </summary>
public sealed class OptionsBuilder
{
    private readonly MainWindow _app;
    private readonly List<Action> _refresh = new();

    public OptionsBuilder(MainWindow app) => _app = app;

    private EditorSession S => _app.Session;

    /// <summary>Changes when the bar's layout must change (not for value changes).</summary>
    public string Key(ToolKind tool)
    {
        var s = S;
        bool sel = s.Document.Selection is { IsEmpty: false };
        bool crop = _app.Canvas.Input?.PendingCrop is not null;
        bool transform = s.ActiveTransform is not null;
        return $"{tool}|{sel}|{crop}|{transform}|{s.PixelMode}|{_app.Workspace.Kind}|{(tool == ToolKind.Shape ? s.Shape.Kind : 0)}|{(tool == ToolKind.Brush ? s.Brush.BuildUp : false)}";
    }

    public void Refresh()
    {
        foreach (var r in _refresh) r();
    }

    private Control Slider(string label, double min, double max, Func<double> get, Action<double> set, Func<double, string> fmt, bool log = false, string? tip = null, double width = 96)
    {
        var (root, refresh) = UiKit.Slider(label, min, max, get, set, fmt, width, log, tip: tip);
        _refresh.Add(refresh);
        return root;
    }

    private Control Toggle(string? icon, string? label, string tip, Func<bool> get, Action<bool> set)
    {
        var (b, refresh) = UiKit.Toggle(icon, label, tip, get, set);
        _refresh.Add(refresh);
        return b;
    }

    private Control Seg<T>(IEnumerable<(T, string?, string?, string?)> options, Func<T> get, Action<T> set) where T : notnull
    {
        var (root, refresh) = UiKit.Segmented(options, get, set, compact: true);
        _refresh.Add(refresh);
        return root;
    }

    private static Control Hint(string text) =>
        new TextBlock { Text = text, Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    private static Button Action(string? icon, string label, string tip, Action a, bool primary = false)
    {
        var b = UiKit.Button(icon, label, tip, a);
        b.Height = 30;
        if (primary)
        {
            b.Classes.Add("solid");
            b.Classes.Add("on");
        }
        return b;
    }

    /// <summary>The options of <paramref name="tool"/>. Compact (Quick Sketch) leaves out the tool name and hints.</summary>
    public Control Build(ToolKind tool, bool compact = false)
    {
        _refresh.Clear();
        var s = S;
        var items = new List<Control>();
        var info = Workspaces.Tools[tool];

        if (s.ActiveTransform is not null)
        {
            items.AddRange(TransformItems(compact));
            return Wrap(items, "transform", "Free transform", compact);
        }
        if (_app.Canvas.Input?.PendingCrop is { } crop)
        {
            items.Add(Hint($"Crop to {crop.Width} × {crop.Height} px"));
            items.Add(Action("check", "Crop", "Apply the crop (Enter)", () => _app.ApplyCrop(), primary: true));
            items.Add(Action("close", "Cancel", "Cancel (Esc)", () => _app.CancelCrop()));
            return Wrap(items, info.Icon, info.Name, compact);
        }

        switch (tool)
        {
            case ToolKind.Brush:
            case ToolKind.Eraser:
                items.AddRange(BrushItems(tool, compact));
                break;
            case ToolKind.Eyedropper:
                items.Add(Hint("Click to pick · right-click picks the background colour · Alt+click with any tool"));
                break;
            case ToolKind.Hand:
                items.Add(Hint("Drag to pan · hold R and drag to rotate the view · wheel zooms"));
                break;
            case ToolKind.Fill:
                if (!s.PixelMode)
                    items.Add(Slider("Tolerance", 0, 255, () => s.Fill.Tolerance, v => s.SetFillOptions(s.Fill with { Tolerance = (int)Math.Round(v) }), v => $"{v:0}",
                        tip: "How different a colour may be and still be filled"));
                items.Add(Toggle(null, "Contiguous", "Only fill connected areas (off: every matching pixel)", () => s.Fill.Contiguous, v => s.SetFillOptions(s.Fill with { Contiguous = v })));
                items.Add(Toggle(null, "All layers", "Find edges using all visible layers, fill on this layer", () => s.Fill.SampleMerged, v => s.SetFillOptions(s.Fill with { SampleMerged = v })));
                if (!s.PixelMode)
                    items.Add(Slider("Opacity", 0.05, 1, () => s.Fill.Opacity, v => s.SetFillOptions(s.Fill with { Opacity = v }), v => $"{v * 100:0}%"));
                if (s.Document.Selection is { IsEmpty: false }) items.AddRange(NextStepItems(compact));
                break;
            case ToolKind.Gradient:
                items.Add(Seg([(GradientShape.Linear, null, "Linear", "Straight gradient"), (GradientShape.Radial, null, "Radial", "From a centre outwards")],
                    () => s.Gradient.Shape, v => s.SetGradientOptions(s.Gradient with { Shape = v })));
                items.Add(Toggle(null, "To transparent", "Fade from the foreground colour to nothing (off: foreground to background)", () => s.Gradient.ToTransparent, v => s.SetGradientOptions(s.Gradient with { ToTransparent = v })));
                items.Add(Slider("Opacity", 0.05, 1, () => s.Gradient.Opacity, v => s.SetGradientOptions(s.Gradient with { Opacity = v }), v => $"{v * 100:0}%"));
                if (!compact) items.Add(Hint("Drag from start to end · Shift snaps to 45°"));
                break;
            case ToolKind.Shape:
                items.Add(Seg([(ShapeKind.Line, "line", null, "Line"), (ShapeKind.Rectangle, "rect", null, "Rectangle"), (ShapeKind.Ellipse, "ellipse", null, "Ellipse")],
                    () => s.Shape.Kind, v => s.SetShapeOptions(s.Shape with { Kind = v })));
                if (s.Shape.Kind != ShapeKind.Line)
                    items.Add(Toggle(null, "Filled", "Fill the shape instead of outlining it", () => s.Shape.Filled, v => s.SetShapeOptions(s.Shape with { Filled = v })));
                items.Add(Slider("Width", 1, 100, () => s.Shape.Width, v => s.SetShapeOptions(s.Shape with { Width = Math.Round(v) }), v => $"{v:0} px", log: true));
                items.Add(Slider("Opacity", 0.05, 1, () => s.Shape.Opacity, v => s.SetShapeOptions(s.Shape with { Opacity = v }), v => $"{v * 100:0}%"));
                items.Add(Toggle(null, "Smooth", "Anti-aliased edges (off: hard pixel edges)", () => s.Shape.AntiAlias, v => s.SetShapeOptions(s.Shape with { AntiAlias = v })));
                break;
            case ToolKind.RectSelect:
            case ToolKind.EllipseSelect:
            case ToolKind.Lasso:
            case ToolKind.MagicWand:
                items.AddRange(SelectionItems(tool, compact));
                break;
            case ToolKind.Move:
                items.Add(Action("transform", "Free transform", "Scale and rotate with handles (Ctrl+T)", () => _app.FreeTransform()));
                items.Add(Action("flip", "Flip ↔", "Flip the selection or layer left to right", () => _app.FlipSelection(true)));
                items.Add(Action(null, "Flip ↕", "Flip the selection or layer upside down", () => _app.FlipSelection(false)));
                items.Add(Action("rotate", "90°", "Rotate the selection or layer 90° clockwise", () => _app.RotateSelection(90)));
                if (!compact) items.Add(Hint(s.Document.Selection is { IsEmpty: false } ? "Drag to move the selection · arrow keys nudge" : "Drag to move the whole layer · arrow keys nudge"));
                break;
            case ToolKind.Crop:
                items.Add(Action("crop", "Crop to selection", "Crop the picture to the selection's bounds", () => _app.CropToSelection()));
                if (!compact) items.Add(Hint("Drag the area to keep, then press Enter"));
                break;
            case ToolKind.PixelPencil:
            case ToolKind.PixelEraser:
            case ToolKind.PixelLine:
            case ToolKind.PixelRect:
            case ToolKind.PixelEllipse:
                items.AddRange(PixelItems(tool));
                break;
        }
        return Wrap(items, info.Icon, info.Name, compact);
    }

    private Control Wrap(List<Control> items, string icon, string name, bool compact)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (!compact)
        {
            var title = UiKit.Row(6, new DnIcon { Kind = icon }, new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            title.Margin = new Thickness(4, 0, 6, 0);
            title.MinWidth = 120;
            row.Children.Add(title);
        }
        foreach (var c in items) row.Children.Add(c);
        Refresh();
        return row;
    }

    private IEnumerable<Control> BrushItems(ToolKind tool, bool compact)
    {
        var s = S;
        if (tool == ToolKind.Brush)
        {
            var preview = new Image { Width = 44, Height = 22 };
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var content = UiKit.Row(8, preview, name, new DnIcon { Kind = "chevd" });
            var chip = UiKit.Chip(content, "Brush presets and settings");
            chip.Flyout = UiKit.PopFlyout(new BrushPanel(s, () => chip.Flyout?.Hide()));
            chip.Name = "OptionsPresetChip";
            string? key = null;
            _refresh.Add(() =>
            {
                var k = s.Brush.ToString();
                if (k == key) return;
                key = k;
                preview.Source = BrushPreview.Render(s.Brush, new ColorRgb(DnColors.Text.R, DnColors.Text.G, DnColors.Text.B), 44, 22);
                name.Text = EditorSession.Presets.FirstOrDefault(p => p.Id == s.BrushPresetId)?.Name ?? "Brush";
            });
            yield return chip;
        }
        yield return Slider("Size", 1, 500, () => s.CurrentPaintSettings.Size, s.SetSize, v => $"{v:0} px", log: true, tip: "Size ([ and ]) · right-drag on the canvas");
        if (tool == ToolKind.Brush)
        {
            yield return Slider("Opacity", 0.05, 1, () => s.Brush.Opacity, s.SetOpacity, v => $"{v * 100:0}%", tip: "A stroke never gets darker than this, even where it crosses itself");
            if (s.Brush.BuildUp) yield return Slider("Flow", 0.005, 0.3, () => s.Brush.Flow, s.SetFlow, v => $"{v * 100:0}%", tip: "How fast the airbrush builds up while you hold still");
        }
        yield return Slider("Smoothing", 0, 1, () => s.CurrentPaintSettings.Smoothing, s.SetSmoothing, v => $"{v * 100:0}%", tip: "Steadies wobbly lines (more = smoother, slightly behind the pen)");
        yield return Toggle("pressure", "Size", "Pen pressure controls the size", () => s.CurrentPaintSettings.PressureSize, s.SetPressureSize);
        if (tool == ToolKind.Brush) yield return Toggle("pressure", "Opacity", "Pen pressure controls the opacity", () => s.CurrentPaintSettings.PressureOpacity, s.SetPressureOpacity);
        yield return Toggle("sym", null, "Mirror painting left ↔ right around the picture's centre", () => s.Symmetry != Symmetry.None, v => s.SetSymmetry(v ? Symmetry.Vertical : Symmetry.None));
    }

    private IEnumerable<Control> SelectionItems(ToolKind tool, bool compact)
    {
        var s = S;
        yield return Seg([(SelectionMode.Replace, null, "New", "Replace the selection"), (SelectionMode.Add, null, "Add", "Add to the selection (hold Shift)"),
                          (SelectionMode.Subtract, null, "Subtract", "Take away from the selection (hold Alt)"), (SelectionMode.Intersect, null, "Intersect", "Keep only the overlap")],
            () => s.Selection.Mode, v => s.SetSelectionOptions(s.Selection with { Mode = v }));
        if (tool is ToolKind.EllipseSelect or ToolKind.Lasso)
            yield return Toggle(null, "Anti-alias", "Soft edges (off: hard pixel edges)", () => s.Selection.AntiAlias, v => s.SetSelectionOptions(s.Selection with { AntiAlias = v }));
        if (tool == ToolKind.MagicWand)
        {
            yield return Slider("Tolerance", 0, 255, () => s.Selection.WandTolerance, v => s.SetSelectionOptions(s.Selection with { WandTolerance = (int)Math.Round(v) }), v => $"{v:0}");
            yield return Toggle(null, "Contiguous", "Only connected areas", () => s.Selection.WandContiguous, v => s.SetSelectionOptions(s.Selection with { WandContiguous = v }));
            yield return Toggle(null, "All layers", "Look at all visible layers", () => s.Selection.SampleMerged, v => s.SetSelectionOptions(s.Selection with { SampleMerged = v }));
        }
        if (s.Document.Selection is { IsEmpty: false })
        {
            foreach (var c in NextStepItems(compact)) yield return c;
        }
        else
        {
            if (!compact) yield return Hint(tool switch
            {
                ToolKind.Lasso => "Draw around what you want to select",
                ToolKind.MagicWand => "Click a colour to select it",
                _ => "Drag on the canvas to make a selection",
            });
            yield return Action(null, "Select all", "Select everything (Ctrl+A)", s.SelectAll);
        }
    }

    /// <summary>The playground's "next-step bar": what you usually do with a selection.</summary>
    private IEnumerable<Control> NextStepItems(bool compact)
    {
        var s = S;
        yield return UiKit.Sep();
        yield return Action("transform", compact ? "Transform" : "Transform", "Move, scale and rotate the selected pixels (Ctrl+T)", () => _app.FreeTransform());
        yield return Action("fill", "Fill", "Fill the selection with the foreground colour (Alt+Backspace)", () => s.FillSelection());
        yield return Action("trash", "Clear", "Erase the selected pixels (Delete)", () => s.ClearSelectionOrLayer());
        if (!compact) yield return Action("crop", "Crop", "Crop the picture to the selection", () => _app.CropToSelection());
        yield return Action("invert", "Invert", "Select everything else (Ctrl+Shift+I)", s.InvertSelection);
        yield return Action("close", "Deselect", "Deselect (Ctrl+D)", s.Deselect);
    }

    private IEnumerable<Control> TransformItems(bool compact)
    {
        var s = S;
        if (!s.PixelMode)
            yield return Seg([(Interpolation.Nearest, null, "Pixels", "Nearest neighbour: hard pixels, for pixel art"),
                              (Interpolation.Bilinear, null, "Smooth", "Bilinear: smooth and fast"),
                              (Interpolation.Bicubic, null, "Sharp", "Bicubic: smoothest edges, keeps detail")],
                () => s.TransformInterpolation, s.SetTransformInterpolation);
        yield return Action("flip", "↔", "Flip left to right", () => _app.FlipSelection(true));
        yield return Action(null, "↕", "Flip upside down", () => _app.FlipSelection(false));
        yield return Action("rotateccw", "90°", "Rotate 90° anticlockwise", () => _app.RotateSelection(-90));
        yield return Action("rotate", "90°", "Rotate 90° clockwise", () => _app.RotateSelection(90));
        if (!compact) yield return Hint("Drag inside to move · corners scale (Shift keeps proportions) · outside a corner rotates");
        yield return Action("check", "Apply", "Apply the transform (Enter)", () => _app.ApplyTransform(), primary: true);
        yield return Action("close", "Cancel", "Cancel (Esc)", () => _app.CancelTransform());
    }

    private IEnumerable<Control> PixelItems(ToolKind tool)
    {
        var s = S;
        yield return Seg([(1, null, "1 px", "One pixel"), (2, null, "2 px", "Two pixels"), (3, null, "3 px", "Three pixels")],
            () => s.Pixel.Size, v => s.SetPixelOptions(s.Pixel with { Size = v }));
        if (tool is ToolKind.PixelPencil or ToolKind.PixelEraser)
            yield return Toggle("check", "Pixel-perfect", "Removes the extra corner pixels that make 1 px lines look jagged",
                () => s.Pixel.PixelPerfect, v => s.SetPixelOptions(s.Pixel with { PixelPerfect = v }));
        if (tool is ToolKind.PixelRect or ToolKind.PixelEllipse)
            yield return Toggle(null, "Filled", "Fill the shape", () => s.Pixel.FilledShapes, v => s.SetPixelOptions(s.Pixel with { FilledShapes = v }));
        if (tool is ToolKind.PixelPencil or ToolKind.PixelEraser)
            yield return Seg([(Symmetry.None, "sym", "Off", "No mirroring"), (Symmetry.Vertical, null, "↔", "Mirror left ↔ right"), (Symmetry.Horizontal, null, "↕", "Mirror top ↕ bottom"), (Symmetry.Both, null, "4×", "Mirror both ways")],
                () => s.Pixel.Symmetry, v => s.SetSymmetry(v));
        yield return UiKit.Sep();
        yield return Toggle("grid", "Grid", "Show the pixel grid when zoomed in (Ctrl+')", () => _app.WsState.ShowGrid, _app.SetShowGrid);
        yield return Toggle("tiled", "Tiled", "Repeat the sprite around itself to check seamless tiles", () => _app.WsState.Tiled, _app.SetTiled);
        var zoom = new ViewZoomGroup(_app);
        _refresh.Add(zoom.Refresh);
        yield return zoom;
        yield return Toggle("image", "Preview", "Show a 1:1 preview of the sprite", () => _app.WsState.Preview, _app.SetPreview);
    }
}

/// <summary>Pixel Art's zoom controls in the options bar (whole-number steps).</summary>
public sealed class ViewZoomGroup : StackPanel
{
    private readonly MainWindow _app;
    private readonly TextBlock _text = new() { Classes = { "num" }, MinWidth = 52, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public ViewZoomGroup(MainWindow app)
    {
        _app = app;
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        var minus = UiKit.Button("minus", tip: "Zoom out (Ctrl+−)", click: () => app.Canvas.ZoomStep(-1));
        var plus = UiKit.Button("plus", tip: "Zoom in (Ctrl++)", click: () => app.Canvas.ZoomStep(+1));
        var fit = UiKit.Button("fit", tip: "Fit (Ctrl+0)", click: () => app.Canvas.FitToView());
        foreach (var b in new[] { minus, plus, fit })
        {
            b.Width = 30;
            b.Height = 30;
            b.MinWidth = 30;
        }
        Children.Add(minus);
        Children.Add(_text);
        Children.Add(plus);
        Children.Add(fit);
        app.Canvas.ViewChanged += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        double z = _app.Canvas.ActualZoom * 100;
        _text.Text = z < 10 ? $"{z:0.#}%" : $"{z:0}%";
    }
}

/// <summary>The options bar under the menu row (Painting, Editing, Pixel Art).</summary>
public sealed class OptionsBar : Border
{
    private readonly MainWindow _app;
    private readonly OptionsBuilder _builder;
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };
    private string? _key;
    private Document? _watched;

    public OptionsBar(MainWindow app)
    {
        _app = app;
        _builder = new OptionsBuilder(app);
        Height = 40;
        Background = new SolidColorBrush(DnColors.Ui);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(6, 0);
        Child = _scroll;
        // In a narrow window the options may not all fit: the mouse wheel scrolls the bar sideways.
        _scroll.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            double delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            double max = Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width);
            _scroll.Offset = new Vector(Math.Clamp(_scroll.Offset.X - delta * 48, 0, max), 0);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        app.Session.StateChanged += Refresh;
        app.Session.DocumentReplaced += Watch;
        app.WorkspaceChanged += Refresh;
        app.Canvas.Input!.OverlayChanged += () =>
        {
            if (!app.Canvas.IsStroking) Refresh(); // the bar only changes for crops and transforms
        };
        Watch();
    }

    private void Watch()
    {
        if (_watched is not null) _watched.SelectionChanged -= Refresh;
        _watched = _app.Session.Document;
        _watched.SelectionChanged += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        if (Parent is null) return;
        var tool = _app.Session.Tool;
        var key = _builder.Key(tool);
        if (key != _key)
        {
            _key = key;
            _scroll.Content = _builder.Build(tool);
        }
        else _builder.Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _key = null;
        Refresh();
    }
}
