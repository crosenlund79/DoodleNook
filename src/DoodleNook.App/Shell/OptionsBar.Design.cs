using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Canvas;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Shell;

/// <summary>
/// Mission 008 options: the Text tool, editable shapes and a chosen text or shape (Move tool). Settings change the
/// chosen text or shape directly (one undo step each; sliders preview while dragging) and are remembered for the next
/// text or shape. Effects (outline, shadow, spacing) sit behind "Effects ▾" so the bar stays short.
/// </summary>
public sealed partial class OptionsBuilder
{
    private static IReadOnlyList<string>? _fonts;

    private string DesignKey(ToolKind tool)
    {
        var s = S;
        var active = s.Document.ActiveLayer;
        string obj = active.IsObject ? $"{active.Id}:{active.Kind}:{(active.Content as ShapeContent)?.Kind}" : "-";
        return tool switch
        {
            ToolKind.Text => obj,
            ToolKind.Shape => $"{s.EditableShapesNow}|{s.DesignShape.Kind}|{obj}",
            ToolKind.Move => obj,
            _ => "",
        };
    }

    private TextContent? ActiveText => S.Document.ActiveLayer is { Kind: LayerKind.Text, Content: TextContent t } ? t : null;
    private ShapeContent? ActiveShape => S.Document.ActiveLayer is { Kind: LayerKind.Shape, Content: ShapeContent c } ? c : null;

    /// <summary>A slider that previews a change of the chosen text or shape while dragging and records one undo step at the end.</summary>
    private Control ObjectSlider<T>(string label, double min, double max, Func<T, double> get, Func<T, double, T> set, Func<double, string> fmt, string history,
        Func<double>? fallback = null, Action<double>? remember = null, bool log = false, double width = 90, string? tip = null) where T : LayerContent
    {
        LayerContent? start = null;
        var (root, refresh) = UiKit.Slider(label, min, max,
            () => S.ActiveObject?.Content is T c ? get(c) : fallback?.Invoke() ?? min,
            v =>
            {
                remember?.Invoke(v);
                if (S.ActiveObject is not { Locked: false } layer || layer.Content is not T c) return;
                start ??= c;
                S.Document.PreviewContent(layer, set(c, v));
            }, fmt, width, log,
            finished: () =>
            {
                if (start is { } from && S.ActiveObject is { } layer) S.Document.CommitContent(layer, from, history);
                start = null;
            }, tip: tip);
        _refresh.Add(refresh);
        return root;
    }

    /// <summary>A colour swatch for part of the chosen text or shape: a click offers the foreground colour or the colour dialog.</summary>
    private Control ColourButton(string what, Func<ColorRgb?> get, Func<LayerContent, ColorRgb, LayerContent> set, string tip)
    {
        var swatch = UiKit.Swatch(null, 18, 5, outline: true);
        var button = Menus.MenuButton(swatch, () =>
        [
            new("fill", "Use the foreground colour", null, () => S.EditObject(c => set(c, S.Allowed(S.Foreground)), what)),
            new("palette", "Choose a colour…", null, () => _ = _app.ChooseObjectColourAsync(what, get() ?? S.Foreground, set)),
        ], tip, 230);
        button.Height = 30;
        _refresh.Add(() => swatch.Background = get() is { } c ? UiKit.Brush(c) : UiKit.CheckerBrush());
        return button;
    }

    // ================================================================== text

    private IEnumerable<Control> TextItems(bool compact)
    {
        var s = S;
        _fonts ??= SkiaTextRenderer.Families();
        var fonts = _fonts.ToList();
        string currentFont = ActiveText?.FontFamily ?? s.Text.Font;
        if (!fonts.Contains(currentFont, StringComparer.OrdinalIgnoreCase)) fonts.Insert(0, currentFont); // a missing font stays chosen
        var font = new ComboBox { ItemsSource = fonts, SelectedItem = fonts.First(f => string.Equals(f, currentFont, StringComparison.OrdinalIgnoreCase)), Width = 170, Height = 30, Name = "FontBox" };
        ToolTip.SetTip(font, "Font (your computer's fonts, and Inter and Varela Round, which come with DoodleNook)");
        font.SelectionChanged += (_, _) =>
        {
            if (font.SelectedItem is not string f) return;
            if (f != s.Text.Font) s.SetTextOptions(s.Text with { Font = f });
            if (ActiveText is { } t && t.FontFamily != f) s.EditObject(c => ((TextContent)c) with { FontFamily = f }, "Font");
        };
        _refresh.Add(() =>
        {
            var f = ActiveText?.FontFamily ?? s.Text.Font;
            if (font.SelectedItem as string != f && fonts.Contains(f)) font.SelectedItem = f;
        });
        yield return font;
        yield return ObjectSlider<TextContent>("Size", 6, 600, t => t.Size, (t, v) => t with { Size = Math.Round(v) }, v => $"{v:0} px", "Text size",
            () => s.Text.Size, v => s.SetTextOptions(s.Text with { Size = Math.Round(v) }), log: true);
        yield return Toggle(null, "B", "Bold", () => ActiveText?.Bold ?? s.Text.Bold, v =>
        {
            s.SetTextOptions(s.Text with { Bold = v });
            if (ActiveText is not null) s.EditObject(c => ((TextContent)c) with { Bold = v }, v ? "Bold" : "Not bold");
        });
        yield return Toggle(null, "I", "Italic", () => ActiveText?.Italic ?? s.Text.Italic, v =>
        {
            s.SetTextOptions(s.Text with { Italic = v });
            if (ActiveText is not null) s.EditObject(c => ((TextContent)c) with { Italic = v }, v ? "Italic" : "Not italic");
        });
        yield return Seg([(TextAlign.Left, "alignl", null, "Align left"), (TextAlign.Center, "alignc", null, "Centre"), (TextAlign.Right, "alignr", null, "Align right")],
            () => ActiveText?.Align ?? s.Text.Align, v =>
            {
                s.SetTextOptions(s.Text with { Align = v });
                if (ActiveText is not null) s.EditObject(c => ((TextContent)c) with { Align = v }, "Text alignment");
            });
        if (ActiveText is not null)
        {
            yield return ColourButton("Text colour", () => ActiveText?.Color, (c, col) => ((TextContent)c) with { Color = col }, "Text colour");
            yield return EffectsButton(text: true);
            yield return Action("text", "Edit text", "Type in the text (or click it with the Text tool)", _app.EditActiveText);
            yield return ObjectMenu();
        }
        else if (!compact) yield return Hint("Click to add text (drag for a text box that wraps) · click text to edit it · colour: the foreground colour");
    }

    // ================================================================== shapes

    private IEnumerable<Control> ShapeModeItems()
    {
        var s = S;
        yield return Seg([(false, null, "Pixels", "Paint the shape into the layer (as before)"), (true, null, "Editable", "Keep shapes changeable: move, resize, turn and recolour them later")],
            () => s.EditableShapesNow, v => s.SetDesignShapeOptions(s.DesignShape with { Editable = v }));
    }

    private IEnumerable<Control> EditableShapeItems(bool compact)
    {
        var s = S;
        yield return Seg([(ShapeType.Line, "line", null, "Line"), (ShapeType.Rectangle, "rect", null, "Rectangle"), (ShapeType.RoundedRectangle, "roundrect", null, "Rounded rectangle"),
                          (ShapeType.Ellipse, "ellipse", null, "Ellipse"), (ShapeType.Polygon, "polygon", null, "Polygon")],
            () => ActiveShape?.Kind ?? s.DesignShape.Kind, v =>
            {
                s.SetDesignShapeOptions(s.DesignShape with { Kind = v });
                if (ActiveShape is { } sh && sh.Kind != v && (sh.Kind == ShapeType.Line) == (v == ShapeType.Line))
                    s.EditObject(c => ((ShapeContent)c) with { Kind = v }, "Shape");
            });
        var kind = ActiveShape?.Kind ?? s.DesignShape.Kind;
        if (kind != ShapeType.Line)
        {
            yield return Toggle("fill", "Fill", "Fill the shape (new shapes: the foreground colour)", () => ActiveShape is { } a ? a.Fill is not null : s.DesignShape.Fill, v =>
            {
                s.SetDesignShapeOptions(s.DesignShape with { Fill = v });
                if (ActiveShape is not null) s.EditObject(c => ((ShapeContent)c) with { Fill = v ? ((ShapeContent)c).Fill ?? s.Allowed(s.Foreground) : null }, v ? "Fill" : "No fill");
            });
            if (ActiveShape is { Fill: not null })
                yield return ColourButton("Fill colour", () => ActiveShape?.Fill, (c, col) => ((ShapeContent)c) with { Fill = col }, "Fill colour");
            yield return Toggle(null, "Outline", "Draw an outline (new shapes: the background colour)", () => ActiveShape is { } a ? a.Stroke is not null : s.DesignShape.Outline, v =>
            {
                s.SetDesignShapeOptions(s.DesignShape with { Outline = v });
                if (ActiveShape is not null) s.EditObject(c => ((ShapeContent)c) with { Stroke = v ? ((ShapeContent)c).Stroke ?? s.Allowed(s.Background) : null }, v ? "Outline" : "No outline");
            });
        }
        if (ActiveShape is { Stroke: not null })
            yield return ColourButton("Outline colour", () => ActiveShape?.Stroke, (c, col) => ((ShapeContent)c) with { Stroke = col }, "Outline colour");
        yield return ObjectSlider<ShapeContent>(kind == ShapeType.Line ? "Width" : "Outline", 0.5, 200, c => c.StrokeWidth, (c, v) => c with { StrokeWidth = Math.Round(v, 1) },
            v => $"{v:0.#} px", "Outline width", () => s.DesignShape.OutlineWidth, v => s.SetDesignShapeOptions(s.DesignShape with { OutlineWidth = Math.Round(v, 1) }), log: true);
        if (kind == ShapeType.RoundedRectangle)
            yield return ObjectSlider<ShapeContent>("Corners", 0, 400, c => c.CornerRadius, (c, v) => c with { CornerRadius = Math.Round(v) }, v => $"{v:0} px", "Corner radius",
                () => s.DesignShape.CornerRadius, v => s.SetDesignShapeOptions(s.DesignShape with { CornerRadius = Math.Round(v) }), log: true);
        if (kind == ShapeType.Polygon)
            yield return ObjectSlider<ShapeContent>("Sides", 3, 16, c => c.Sides, (c, v) => c with { Sides = (int)Math.Round(v) }, v => $"{v:0}", "Polygon sides",
                () => s.DesignShape.Sides, v => s.SetDesignShapeOptions(s.DesignShape with { Sides = (int)Math.Round(v) }));
        if (ActiveShape is not null)
        {
            yield return EffectsButton(text: false);
            yield return ObjectMenu();
        }
        else if (!compact) yield return Hint("Drag to draw · Shift: square or circle · drag the handles later to change it");
    }

    // ================================================================== a chosen text or shape (Move tool)

    private IEnumerable<Control> ObjectItems(bool compact)
    {
        var s = S;
        var layer = s.ActiveObject!;
        yield return new TextBlock
        {
            Text = layer.Name, FontWeight = FontWeight.SemiBold, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (layer.Kind == LayerKind.Text)
            yield return ObjectSlider<TextContent>("Turn", -180, 180, t => t.Rotation, (t, v) => t with { Rotation = Math.Round(v) }, v => $"{v:0}°", "Rotate");
        else
            yield return ObjectSlider<ShapeContent>("Turn", -180, 180, c => c.Rotation, (c, v) => c with { Rotation = Math.Round(v) }, v => $"{v:0}°", "Rotate");
        yield return AlignMenu();
        if (layer.Kind == LayerKind.Text) yield return Action("text", "Edit text", "Type in the text", _app.EditActiveText);
        yield return Action("image", "To pixels", "Turn this into an ordinary pixel layer (Layer › Convert to pixels)", s.ConvertToPixels);
        if (!compact) yield return Hint("Drag inside to move · handles resize · just outside a corner turns · Shift: straight / 15° · Ctrl+click picks another");
    }

    private Control AlignMenu()
    {
        var s = S;
        IEnumerable<MenuEntry> Entries()
        {
            bool below = s.ActiveObject is { } l && s.Document.LayerBelow(l) is not null;
            yield return MenuEntry.Heading("With the canvas");
            yield return new("alignl", "Left", null, () => s.Align(AlignEdge.Left, false));
            yield return new("alignc", "Centre", null, () => s.Align(AlignEdge.CenterX, false));
            yield return new("alignr", "Right", null, () => s.Align(AlignEdge.Right, false));
            yield return new("alignt", "Top", null, () => s.Align(AlignEdge.Top, false));
            yield return new("alignm", "Middle", null, () => s.Align(AlignEdge.CenterY, false));
            yield return new("alignb", "Bottom", null, () => s.Align(AlignEdge.Bottom, false));
            yield return MenuEntry.Heading("With the layer below");
            yield return new("alignc", "Centre on it", null, () =>
            {
                s.Align(AlignEdge.CenterX, true);
                s.Align(AlignEdge.CenterY, true);
            }, below);
            yield return new("alignl", "Left", null, () => s.Align(AlignEdge.Left, true), below);
            yield return new("alignr", "Right", null, () => s.Align(AlignEdge.Right, true), below);
            yield return new("alignt", "Top", null, () => s.Align(AlignEdge.Top, true), below);
            yield return new("alignb", "Bottom", null, () => s.Align(AlignEdge.Bottom, true), below);
        }
        var b = Menus.MenuButton(UiKit.Content("alignc", "Align ▾"), Entries, "Line up with the canvas or with the layer below", 220);
        b.Height = 30;
        b.Name = "AlignButton";
        return b;
    }

    /// <summary>The ⋯ menu of a chosen text or shape: align, duplicate, turn into pixels.</summary>
    private Control ObjectMenu()
    {
        var b = Menus.MenuButton(new DnIcon { Kind = "dots" }, () =>
        [
            new("alignc", "Centre on the canvas", null, () =>
            {
                S.Align(AlignEdge.CenterX, false);
                S.Align(AlignEdge.CenterY, false);
            }),
            new("alignc", "Centre on the layer below", null, () =>
            {
                S.Align(AlignEdge.CenterX, true);
                S.Align(AlignEdge.CenterY, true);
            }, S.ActiveObject is { } l && S.Document.LayerBelow(l) is not null),
            new("copy", "Duplicate", "Ctrl+J", S.DuplicateLayer),
            new("image", "Convert to pixels", null, S.ConvertToPixels),
        ], "More: align, duplicate, convert to pixels", 240);
        b.Classes.Add("sq");
        b.Width = b.Height = b.MinWidth = 30;
        return b;
    }

    // ================================================================== effects

    private Control EffectsButton(bool text)
    {
        var b = UiKit.Button(null, "Effects ▾", text ? "Outline, shadow and spacing" : "Shadow");
        b.Height = 30;
        b.Name = "EffectsButton";
        var flyout = UiKit.PopFlyout(new Panel());
        flyout.Opening += (_, _) => flyout.Content = EffectsPanel(text);
        b.Flyout = flyout;
        return b;
    }

    private Control EffectsPanel(bool text)
    {
        var s = S;
        var panel = new StackPanel { Spacing = 6, Width = 290, Name = "EffectsPanel" };
        var refresh = new List<Action>();
        Control Slider<T>(string label, double min, double max, Func<T, double> get, Func<T, double, T> set, Func<double, string> fmt, string history, bool log = false) where T : LayerContent
        {
            int before = _refresh.Count;
            var c = ObjectSlider(label, min, max, get, set, fmt, history, log: log, width: 120);
            refresh.AddRange(_refresh.Skip(before));
            return c;
        }
        Control Check(string label, string tip, Func<bool> get, Action<bool> set)
        {
            var (b, r) = UiKit.Check(label, tip, get, v =>
            {
                set(v);
                foreach (var x in refresh) x();
            });
            refresh.Add(r);
            return b;
        }
        Control Colour(string what, Func<ColorRgb?> get, Func<LayerContent, ColorRgb, LayerContent> set)
        {
            int before = _refresh.Count;
            var c = ColourButton(what, get, set, what);
            refresh.AddRange(_refresh.Skip(before));
            return c;
        }

        if (text)
        {
            TextContent? T() => s.ActiveObject?.Content as TextContent;
            panel.Children.Add(UiKit.Heading("Outline"));
            panel.Children.Add(UiKit.Row(8,
                Check("Outline", "A line around the letters", () => T()?.Outline is not null,
                    v => s.EditObject(c => ((TextContent)c) with { Outline = v ? new OutlineEffect(s.Allowed(s.Background), 3) : null }, v ? "Outline" : "No outline")),
                Colour("Outline colour", () => T()?.Outline?.Color, (c, col) => ((TextContent)c) with { Outline = (((TextContent)c).Outline ?? new OutlineEffect(col, 3)) with { Color = col } })));
            panel.Children.Add(Slider<TextContent>("Width", 0.5, 40, t => t.Outline?.Width ?? 3, (t, v) => t with { Outline = (t.Outline ?? new OutlineEffect(ColorRgb.White)) with { Width = Math.Round(v, 1) } },
                v => $"{v:0.#} px", "Outline width", log: true));
            panel.Children.Add(UiKit.HSep());
            AddShadow<TextContent>(panel, () => T()?.Shadow, (c, sh) => ((TextContent)c) with { Shadow = sh }, Check, Colour, Slider<TextContent>);
            panel.Children.Add(UiKit.HSep());
            panel.Children.Add(UiKit.Heading("Spacing"));
            panel.Children.Add(Slider<TextContent>("Letters", -20, 100, t => t.LetterSpacing, (t, v) => t with { LetterSpacing = Math.Round(v, 1) }, v => $"{v:0.#} px", "Letter spacing"));
            panel.Children.Add(Slider<TextContent>("Lines", 0.6, 3, t => t.LineSpacing, (t, v) => t with { LineSpacing = Math.Round(v, 2) }, v => $"{v:0.00}×", "Line spacing"));
            panel.Children.Add(Slider<TextContent>("Box width", 0, 4000, t => t.BoxWidth, (t, v) => t with { BoxWidth = Math.Round(v) }, v => v < 1 ? "auto" : $"{v:0} px",
                "Text box width"));
            panel.Children.Add(new TextBlock { Text = "Box width: lines wrap at this width (auto: only where you press Enter). The side handles change it too.", Classes = { "dim" },
                FontSize = 11, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            ShapeContent? C() => s.ActiveObject?.Content as ShapeContent;
            AddShadow<ShapeContent>(panel, () => C()?.Shadow, (c, sh) => ((ShapeContent)c) with { Shadow = sh }, Check, Colour, Slider<ShapeContent>);
        }
        return panel;
    }

    private void AddShadow<T>(StackPanel panel, Func<ShadowEffect?> get, Func<LayerContent, ShadowEffect?, LayerContent> set,
        Func<string, string, Func<bool>, Action<bool>, Control> check, Func<string, Func<ColorRgb?>, Func<LayerContent, ColorRgb, LayerContent>, Control> colour,
        Func<string, double, double, Func<T, double>, Func<T, double, T>, Func<double, string>, string, bool, Control> slider) where T : LayerContent
    {
        var s = S;
        ShadowEffect? Of(T c) => c switch { TextContent t => t.Shadow, ShapeContent sh => sh.Shadow, _ => null };
        T With(T c, ShadowEffect e) => (T)set(c, e);
        panel.Children.Add(UiKit.Heading("Shadow"));
        panel.Children.Add(UiKit.Row(8,
            check("Shadow", "A soft shadow under it", () => get() is not null,
                v => s.EditObject(c => set(c, v ? new ShadowEffect(s.Allowed(ColorRgb.Black)) : null), v ? "Shadow" : "No shadow")),
            colour("Shadow colour", () => get()?.Color, (c, col) => set(c, (get() ?? new ShadowEffect(col)) with { Color = col }))));
        panel.Children.Add(slider("Distance", 0, 80, c => Of(c) is { } e ? Math.Sqrt(e.OffsetX * e.OffsetX + e.OffsetY * e.OffsetY) : 5,
            (c, v) =>
            {
                var e = Of(c) ?? new ShadowEffect(ColorRgb.Black);
                double a = Math.Atan2(e.OffsetY, e.OffsetX);
                if (e.OffsetX == 0 && e.OffsetY == 0) a = Math.PI / 4;
                return With(c, e with { OffsetX = Math.Round(Math.Cos(a) * v, 1), OffsetY = Math.Round(Math.Sin(a) * v, 1) });
            }, v => $"{v:0} px", "Shadow distance", false));
        panel.Children.Add(slider("Softness", 0, 40, c => Of(c)?.Blur ?? 4, (c, v) => With(c, (Of(c) ?? new ShadowEffect(ColorRgb.Black)) with { Blur = Math.Round(v, 1) }),
            v => $"{v:0} px", "Shadow softness", false));
        panel.Children.Add(slider("Strength", 0.05, 1, c => Of(c)?.Opacity ?? 0.45, (c, v) => With(c, (Of(c) ?? new ShadowEffect(ColorRgb.Black)) with { Opacity = Math.Round(v, 2) }),
            v => $"{v * 100:0}%", "Shadow strength", false));
    }

    // ================================================================== mirror button with its ▾

    /// <summary>The mirror toggle with a ▾ that opens the drawing assistants (symmetry centre, radial, perspective, snapping).</summary>
    private Control MirrorItems()
    {
        var s = S;
        var toggle = Toggle("sym", null, "Mirror painting left ↔ right (▾: centre, radial, perspective and snapping)",
            () => (s.PixelMode ? s.Pixel.Symmetry : s.Symmetry) != Symmetry.None, v => s.SetSymmetry(v ? Symmetry.Vertical : Symmetry.None));
        var more = UiKit.Button("chevd", tip: "Drawing assistants: symmetry centre and radial, perspective, snapping");
        more.Width = more.MinWidth = 22;
        more.Height = 30;
        more.Name = "AssistantsButton";
        more.Click += (_, _) => _app.ShowAssistants(more);
        return UiKit.Row(0, toggle, more);
    }
}
