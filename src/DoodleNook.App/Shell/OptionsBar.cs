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
public sealed partial class OptionsBuilder
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
        return $"{tool}|{sel}|{crop}|{transform}|{s.PixelMode}|{_app.Workspace.Kind}|{(tool == ToolKind.Shape ? s.Shape.Kind : 0)}|{(tool == ToolKind.Brush ? s.Brush.BuildUp : false)}" +
               $"|{(tool == ToolKind.Fill && s.PatternOptions.UsePattern)}|{(tool == ToolKind.Brush ? FavouritesKey() : "")}|{DesignKey(tool)}" +
               (EditorSession.IsRetouchTool(tool) ? $"|{s.CloneSource is not null}|{s.Retouch.RepairFromSource}" : "");
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
            case ToolKind.Smudge:
            case ToolKind.Clone:
            case ToolKind.Repair:
                items.AddRange(RetouchItems(tool, compact));
                break;
            case ToolKind.Hand:
                items.Add(Hint("Drag to pan · hold R and drag to rotate the view · wheel zooms"));
                break;
            case ToolKind.Stamp:
                items.AddRange(StampItems(compact));
                break;
            case ToolKind.Fill:
                items.Add(Seg([(false, "fill", "Colour", "Fill with the foreground colour"), (true, "pattern", "Pattern", "Fill with a repeating pattern from your library")],
                    () => s.PatternOptions.UsePattern, v => s.SetPatternOptions(s.PatternOptions with { UsePattern = v })));
                if (s.PatternOptions.UsePattern) items.AddRange(PatternItems());
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
            case ToolKind.Text:
                items.AddRange(TextItems(compact));
                break;
            case ToolKind.Shape when s.EditableShapesNow:
                items.AddRange(ShapeModeItems());
                items.AddRange(EditableShapeItems(compact));
                break;
            case ToolKind.Shape:
                if (!s.PixelMode) items.AddRange(ShapeModeItems());
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
            case ToolKind.Move when s.ActiveObject is not null:
                items.AddRange(ObjectItems(compact));
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

    // ================================================================== stamps and patterns (Mission 007)

    private Control AssetChip(PickerKind kind, Func<string?> current, Action<string> choose, string tip)
    {
        var thumb = new Image { Width = 26, Height = 26, Stretch = Stretch.Uniform };
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis };
        var chip = UiKit.Chip(UiKit.Row(6, new Border { Background = UiKit.CheckerBrush(), CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = thumb }, name,
            new DnIcon { Kind = "chevd" }), tip);
        chip.Name = kind == PickerKind.Stamps ? "StampChip" : "PatternChip";
        var picker = new AssetPicker(_app, kind, current, id =>
        {
            choose(id);
            chip.Flyout?.Hide();
        });
        var flyout = UiKit.PopFlyout(picker);
        flyout.Opening += (_, _) => picker.Refresh();
        chip.Flyout = flyout;
        string? shown = null;
        _refresh.Add(() =>
        {
            var id = current();
            var lib = S.Library;
            name.Text = kind == PickerKind.Stamps ? lib.FindStamp(id)?.Name ?? "Choose a stamp" : lib.FindPattern(id)?.Name ?? "Choose a pattern";
            if (id == shown) return;
            shown = id;
            var img = id is null ? null : kind == PickerKind.Stamps ? lib.StampImage(id) : lib.PatternImage(id);
            thumb.Source = img is null ? null : kind == PickerKind.Patterns ? AssetPicker.TiledPreview(img, 26, 26) : BrushPreview.ToBitmap(AssetPicker.Fit(img, 52));
        });
        return chip;
    }

    private IEnumerable<Control> StampItems(bool compact)
    {
        var s = S;
        yield return AssetChip(PickerKind.Stamps, () => s.StampOptions.StampId, id => s.SetStampOptions(s.StampOptions with { StampId = id }), "Choose the stamp");
        if (s.PixelMode)
        {
            yield return Seg([(1.0, null, "1×", "Actual pixels"), (2.0, null, "2×", "Twice as big"), (3.0, null, "3×", "Three times"), (4.0, null, "4×", "Four times")],
                () => Math.Max(1, Math.Round(s.StampOptions.Scale)), v => s.SetStampOptions(s.StampOptions with { Scale = v }));
            yield return Seg([(0.0, null, "0°", "Upright"), (90.0, null, "90°", "Quarter turn"), (180.0, null, "180°", "Upside down"), (-90.0, null, "270°", "Three quarter turns")],
                () => Math.Round(s.StampOptions.Rotation / 90) * 90 is var r && r == -180 ? 180 : Math.Round(s.StampOptions.Rotation / 90) * 90,
                v => s.SetStampOptions(s.StampOptions with { Rotation = v }));
        }
        else
        {
            yield return Slider("Size", 0.05, 4, () => s.StampOptions.Scale, v => s.SetStampOptions(s.StampOptions with { Scale = v }), v => $"{v * 100:0}%", log: true,
                tip: "Stamp size ([ and ])");
            yield return Slider("Rotate", -180, 180, () => s.StampOptions.Rotation, v => s.SetStampOptions(s.StampOptions with { Rotation = Math.Round(v) }), v => $"{v:0}°",
                tip: "Turn the stamp");
            yield return Slider("Opacity", 0.05, 1, () => s.StampOptions.Opacity, v => s.SetStampOptions(s.StampOptions with { Opacity = v }), v => $"{v * 100:0}%");
        }
        yield return Toggle("flip", null, "Mirror left ↔ right", () => s.StampOptions.FlipX, v => s.SetStampOptions(s.StampOptions with { FlipX = v }));
        yield return Toggle(null, "↕", "Mirror top ↕ bottom", () => s.StampOptions.FlipY, v => s.SetStampOptions(s.StampOptions with { FlipY = v }));
        if (s.Document.Selection is { IsEmpty: false })
            yield return Action("plus", "Selection → stamp", "Save the selected artwork as a new stamp", () => _ = _app.SaveSelectionAsAssetAsync(PickerKind.Stamps));
        else if (!compact) yield return Hint("Click to place · [ and ] size");
    }

    private IEnumerable<Control> PatternItems()
    {
        var s = S;
        yield return AssetChip(PickerKind.Patterns, () => s.PatternOptions.PatternId, id => s.SetPatternOptions(s.PatternOptions with { PatternId = id }), "Choose the pattern");
        if (s.PixelMode)
            yield return Seg([(1.0, null, "1×", "Actual pixels"), (2.0, null, "2×", "Twice as big"), (3.0, null, "3×", "Three times"), (4.0, null, "4×", "Four times")],
                () => Math.Max(1, Math.Round(s.PatternOptions.Scale)), v => s.SetPatternOptions(s.PatternOptions with { Scale = v }));
        else
        {
            yield return Slider("Scale", 0.1, 8, () => s.PatternOptions.Scale, v => s.SetPatternOptions(s.PatternOptions with { Scale = v }), v => $"{v * 100:0}%", log: true);
            yield return Slider("Rotate", -180, 180, () => s.PatternOptions.Rotation, v => s.SetPatternOptions(s.PatternOptions with { Rotation = Math.Round(v) }), v => $"{v:0}°");
        }
        double TileW() => S.Library.PatternImage(S.PatternOptions.PatternId ?? "")?.Width ?? 64;
        double TileH() => S.Library.PatternImage(S.PatternOptions.PatternId ?? "")?.Height ?? 64;
        yield return Slider("Offset", 0, 1, () => s.PatternOptions.OffsetX / (TileW() * s.PatternOptions.Scale) % 1, v =>
                s.SetPatternOptions(s.PatternOptions with { OffsetX = Math.Round(v * TileW() * s.PatternOptions.Scale), OffsetY = Math.Round(v * TileH() * s.PatternOptions.Scale) }),
            v => $"{v * 100:0}%", tip: "Shift the pattern so its repeat lines up where you want");
    }

    private string FavouritesKey() => _app.WsState.IsShown(CustomOptions.FavouriteBrushes, _app.Workspace.Kind)
        ? string.Join(",", S.Library.Brushes.Where(b => S.Library.IsFavoriteBrush(b.Id)).Select(b => b.Id)) : "";

    /// <summary>Starred brushes as one-click buttons (Customize workspace › Favourite brushes).</summary>
    private IEnumerable<Control> FavouriteBrushItems()
    {
        if (!_app.WsState.IsShown(CustomOptions.FavouriteBrushes, _app.Workspace.Kind)) yield break;
        var s = S;
        var ink = new ColorRgb(DnColors.Text.R, DnColors.Text.G, DnColors.Text.B);
        foreach (var b in s.Library.Brushes.Where(b => s.Library.IsFavoriteBrush(b.Id)).Take(8))
        {
            var id = b.Id;
            var button = UiKit.Button(null, null, b.Name + " (favourite)", () => s.ApplyPreset(id));
            button.Content = new Image { Source = BrushPreview.Render(b.Settings, ink, 36, 18, resources: s.Library.Resolve(b.Settings)), Width = 36, Height = 18 };
            button.Name = "Favourite_" + id;
            _refresh.Add(() => button.Classes.Set("on", s.BrushPresetId == id));
            yield return button;
        }
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
            var favourites = new List<Control>();
            chip.Flyout = UiKit.PopFlyout(new BrushPanel(s, () => chip.Flyout?.Hide(), app: _app));
            chip.Name = "OptionsPresetChip";
            foreach (var f in FavouriteBrushItems()) favourites.Add(f);
            string? key = null;
            _refresh.Add(() =>
            {
                var k = s.Brush.ToString();
                if (k == key) return;
                key = k;
                preview.Source = BrushPreview.Render(s.Brush, new ColorRgb(DnColors.Text.R, DnColors.Text.G, DnColors.Text.B), 44, 22, resources: s.Library.Resolve(s.Brush));
                name.Text = s.CurrentPreset?.Name ?? "Brush";
            });
            yield return chip;
            foreach (var f in favourites) yield return f;
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
        yield return MirrorItems();
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

    /// <summary>Smudge, Clone and Repair (Mission 010): a few plain controls; the source is chosen on the canvas (Alt+click).</summary>
    private IEnumerable<Control> RetouchItems(ToolKind tool, bool compact)
    {
        var s = S;
        var r = () => s.Retouch;
        yield return Slider("Size", 1, 500, () => s.CurrentSize, s.SetSize, v => $"{v:0} px", log: true, tip: "Size ([ and ]) · right-drag on the canvas");
        switch (tool)
        {
            case ToolKind.Smudge:
                yield return Slider("Strength", 0.05, 1, () => r().SmudgeStrength, v => s.SetRetouchOptions(r() with { SmudgeStrength = v }), v => $"{v * 100:0}%",
                    tip: "How far the paint is carried: low blends gently, high drags colour a long way · pen pressure lightens it");
                yield return Slider("Softness", 0, 1, () => r().SmudgeSoftness, v => s.SetRetouchOptions(r() with { SmudgeSoftness = v }), v => $"{v * 100:0}%",
                    tip: "Soft edges blend into their surroundings; 0 % is a hard edge");
                yield return Toggle("brush", "Finger painting", "Each smear starts with the foreground colour, mixed into the paint it meets",
                    () => r().FingerPaint, v => s.SetRetouchOptions(r() with { FingerPaint = v }));
                if (!compact) yield return Hint("Drag to push and blend the paint");
                break;
            case ToolKind.Clone:
                yield return Slider("Opacity", 0.05, 1, () => r().CloneOpacity, v => s.SetRetouchOptions(r() with { CloneOpacity = v }), v => $"{v * 100:0}%",
                    tip: "How much of the copy covers what is there · pen pressure lightens it");
                yield return Slider("Softness", 0, 1, () => r().CloneSoftness, v => s.SetRetouchOptions(r() with { CloneSoftness = v }), v => $"{v * 100:0}%",
                    tip: "Soft edges blend the copy in; 0 % is a hard edge");
                yield return Toggle(null, "All layers", "Copy what you see (every visible layer) onto this layer · off: copy from this layer only",
                    () => r().SampleAll, v => s.SetRetouchOptions(r() with { SampleAll = v }));
                foreach (var c in SourceItems("Alt+click where to copy from, then paint")) yield return c;
                break;
            default:
                yield return Slider("Softness", 0, 1, () => r().RepairSoftness, v => s.SetRetouchOptions(r() with { RepairSoftness = v }), v => $"{v * 100:0}%",
                    tip: "Soft edges blend the repair in; 0 % is a hard edge");
                yield return Seg([(false, null, "Surroundings", "Fill the spot from the colours around it: for specks, marks and small gaps"),
                                  (true, "clone", "Source", "Bring in the texture of another area (Alt+click), matched to the colours around the spot")],
                    () => r().RepairFromSource, v => s.SetRetouchOptions(r() with { RepairFromSource = v }));
                if (r().RepairFromSource)
                {
                    yield return Toggle(null, "All layers", "Take the texture from what you see (every visible layer)",
                        () => r().SampleAll, v => s.SetRetouchOptions(r() with { SampleAll = v }));
                    foreach (var c in SourceItems("Alt+click where to take the texture from")) yield return c;
                }
                else if (!compact) yield return Hint("Paint over a speck or mark");
                break;
        }
    }

    private IEnumerable<Control> SourceItems(string none)
    {
        var s = S;
        if (s.CloneSource is null)
        {
            yield return Hint(none);
            yield break;
        }
        yield return Hint("Source chosen · Alt+click to change");
        yield return Action("close", "Clear source", "Forget the source (Alt+click to choose a new one)", s.ClearCloneSource);
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
        {
            yield return Seg([(Symmetry.None, "sym", "Off", "No mirroring"), (Symmetry.Vertical, null, "↔", "Mirror left ↔ right"), (Symmetry.Horizontal, null, "↕", "Mirror top ↕ bottom"), (Symmetry.Both, null, "4×", "Mirror both ways")],
                () => s.Pixel.Symmetry, v => s.SetSymmetry(v));
            var more = UiKit.Button("chevd", tip: "Drawing assistants: symmetry centre and radial, perspective, snapping");
            more.Width = more.MinWidth = 22;
            more.Height = 30;
            more.Click += (_, _) => _app.ShowAssistants(more);
            yield return more;
        }
        yield return UiKit.Sep();
        bool animation = _app.Workspace.Kind == WorkspaceKind.Animation;
        yield return Toggle("grid", "Grid", "Show the pixel grid when zoomed in (Ctrl+')", () => _app.WsState.ShowGrid, _app.SetShowGrid);
        if (!animation)
            yield return Toggle("tiled", "Tiled", "Repeat the sprite around itself to check seamless tiles", () => _app.WsState.Tiled, _app.SetTiled);
        var zoom = new ViewZoomGroup(_app);
        _refresh.Add(zoom.Refresh);
        yield return zoom;
        if (animation)
            yield return Toggle("film", "Preview", "A small looping preview of the whole animation",
                () => _app.AnimationState.IsShown(CustomOptions.PreviewPanel, WorkspaceKind.Animation), _app.SetPreviewPanel);
        else
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
        if (_watched is not null)
        {
            _watched.SelectionChanged -= Refresh;
            _watched.ActiveLayerChanged -= Refresh;
            _watched.LayersChanged -= Refresh;
            _watched.ContentChanged -= OnContent;
        }
        _watched = _app.Session.Document;
        _watched.SelectionChanged += Refresh;
        _watched.ActiveLayerChanged += Refresh; // text and shape options follow the chosen layer
        _watched.LayersChanged += Refresh;
        _watched.ContentChanged += OnContent;
        Refresh();
    }

    private void OnContent(Layer _) => Refresh();

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
