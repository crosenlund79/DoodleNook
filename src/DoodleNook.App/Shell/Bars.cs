using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Shell;

/// <summary>A menu row: an action with an optional icon and shortcut, or a separator.</summary>
public sealed record MenuEntry(string? Icon, string Label, string? Shortcut = null, Action? Run = null, bool Enabled = true, bool Checked = false)
{
    public static readonly MenuEntry Separator = new(null, "-");
    public bool IsSeparator => Label == "-";

    public static MenuEntry Heading(string text) => new(null, text, Enabled: false) { IsHeading = true };

    public bool IsHeading { get; init; }
}

public static class Menus
{
    /// <summary>Builds the content of a menu pop-over; clicking an entry closes the pop-over and runs it.</summary>
    public static Control Build(IEnumerable<MenuEntry> entries, Action close, double width = 270)
    {
        var stack = new StackPanel { Spacing = 1, Width = width };
        foreach (var e in entries)
        {
            if (e.IsSeparator)
            {
                stack.Children.Add(UiKit.HSep());
                continue;
            }
            if (e.IsHeading)
            {
                stack.Children.Add(new TextBlock { Text = e.Label, Classes = { "dim" }, FontSize = 12, Margin = new Thickness(8, 6, 8, 2) });
                continue;
            }
            var b = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                Content = UiKit.Content(e.Checked ? "check" : e.Icon ?? "blank", e.Label, e.Shortcut),
                IsEnabled = e.Enabled && e.Run is not null,
            };
            b.Classes.Add("menu");
            if (e.Checked) b.Classes.Add("on");
            var run = e.Run;
            b.Click += (_, _) =>
            {
                close();
                run?.Invoke();
            };
            stack.Children.Add(b);
        }
        return stack;
    }

    /// <summary>A button that opens a menu built fresh each time (so enabled states and check marks are current).</summary>
    public static Button MenuButton(Control content, Func<IEnumerable<MenuEntry>> entries, string? tip = null, double width = 270, PlacementMode placement = PlacementMode.BottomEdgeAlignedLeft)
    {
        var b = new Button { Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!, Content = content };
        if (tip is not null) ToolTip.SetTip(b, tip);
        var flyout = UiKit.PopFlyout(new Panel(), placement);
        flyout.Opening += (_, _) => flyout.Content = Build(entries(), flyout.Hide, width);
        b.Flyout = flyout;
        return b;
    }
}

/// <summary>The five workspace buttons (the playground's ".wsw" at the right of the bars). The current one shows its name.</summary>
public sealed class WorkspaceSwitcher : Border
{
    private readonly MainWindow _app;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 2 };

    public WorkspaceSwitcher(MainWindow app)
    {
        _app = app;
        Child = _row;
        Background = new SolidColorBrush(DnColors.Ui2);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(11);
        Padding = new Thickness(2);
        VerticalAlignment = VerticalAlignment.Center;
        app.WorkspaceChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        _row.Children.Clear();
        int n = 1;
        foreach (var ws in Workspaces.All)
        {
            bool on = ws.Kind == _app.Workspace.Kind;
            var kind = ws.Kind;
            var b = UiKit.Button(ws.Icon, on ? ws.Name : null, $"{ws.Name} (Alt+{n++}) — {ws.Description}", () => _app.SetWorkspace(kind));
            b.Height = 30;
            if (!on) b.Width = 34;
            else
            {
                b.Classes.Add("solid");
                b.Classes.Add("on");
                b.Padding = new Thickness(8, 0, 10, 0);
            }
            b.Name = "Ws" + ws.Letter;
            _row.Children.Add(b);
        }
    }
}

/// <summary>Zoom, fit, rotation and flip: floating over the canvas (Quick Sketch, Painting) or in the status bar (Editing).</summary>
public sealed class ViewControls : StackPanel
{
    private readonly MainWindow _app;
    private readonly TextBlock _zoom = new() { Classes = { "num" }, Text = "100%" };
    private readonly TextBlock _angle = new() { Classes = { "num" }, Text = "0°" };
    private readonly Button _flip;

    public ViewControls(MainWindow app, bool compact = false)
    {
        _app = app;
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        double h = compact ? 28 : 30;
        Button Small(string icon, string tip, Action a)
        {
            var b = UiKit.Button(icon, tip: tip, click: a);
            b.Width = h;
            b.Height = h;
            b.MinWidth = h;
            return b;
        }
        Children.Add(Small("minus", "Zoom out (Ctrl+−)", () => app.Canvas.ZoomStep(-1)));
        var zoom = UiKit.Button(null, null, "Actual pixels, 100% (Ctrl+1)", app.Canvas.ZoomToActualPixels);
        zoom.Content = _zoom;
        zoom.Height = h;
        zoom.MinWidth = 52;
        zoom.Padding = new Thickness(4, 0);
        zoom.Foreground = new SolidColorBrush(DnColors.Text2);
        zoom.Name = "ZoomText";
        Children.Add(zoom);
        Children.Add(Small("plus", "Zoom in (Ctrl++)", () => app.Canvas.ZoomStep(+1)));
        var fit = UiKit.Button("fit", "Fit", "Fit on screen (Ctrl+0)", () => app.Canvas.FitToView());
        fit.Height = h;
        Children.Add(fit);
        Children.Add(Small("rotateccw", "Rotate the view left 15° (hold R and drag to rotate freely)", () => app.Canvas.RotateBy(-15)));
        var angle = UiKit.Button(null, null, "View rotation · click to straighten (Esc while holding R)", app.Canvas.ResetRotation);
        angle.Content = _angle;
        angle.Height = h;
        angle.MinWidth = 38;
        angle.Padding = new Thickness(2, 0);
        Children.Add(angle);
        Children.Add(Small("rotate", "Rotate the view right 15°", () => app.Canvas.RotateBy(15)));
        _flip = UiKit.Button("flip", "Flip", "Flip the view (your picture is not changed)", app.ToggleFlipView);
        _flip.Height = h;
        Children.Add(_flip);
        app.Canvas.ViewChanged += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        double z = _app.Canvas.ActualZoom * 100;
        _zoom.Text = z < 10 ? $"{z:0.#}%" : $"{z:0}%";
        double r = _app.Canvas.View.Rotation;
        _angle.Text = $"{Math.Round(r):0}°";
        _flip.Classes.Set("on", _app.Canvas.View.FlipHorizontal);
    }
}

/// <summary>The vertical tool strip (".tools"): the workspace's tools, then the foreground/background chips.</summary>
public sealed class ToolStrip : Border
{
    private readonly MainWindow _app;
    private readonly StackPanel _tools = new() { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Panel _chips;
    private readonly Border _fg, _bg;
    private readonly Button _lockBadge;
    private readonly List<(Button B, ToolKind K)> _buttons = new();

    public ToolStrip(MainWindow app)
    {
        _app = app;
        Width = 50;
        Background = new SolidColorBrush(DnColors.Ui);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(0, 0, 1, 0);

        _fg = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(DnColors.Text) };
        _bg = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(DnColors.Text) };
        var bgChip = new Button
        {
            Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
            Width = 22, Height = 22, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Content = _bg, Name = "BgChip",
        };
        ToolTip.SetTip(bgChip, "Background colour · click to swap (X) · D resets");
        bgChip.Click += (_, _) => app.Session.SwapColors();
        var fgChip = new Button
        {
            Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
            Width = 22, Height = 22, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Content = _fg, Name = "FgChip",
        };
        ToolTip.SetTip(fgChip, "Foreground colour · click to edit");
        var colour = new ColourPanel(app.Session, app: app);
        fgChip.Flyout = UiKit.PopFlyout(colour, PlacementMode.RightEdgeAlignedBottom);
        _chips = new Panel { Width = 36, Height = 36, Margin = new Thickness(0, 0, 0, 2) };
        _chips.Children.Add(bgChip);
        _chips.Children.Add(fgChip);

        // Palette Lock indicator: only shown while the picture is locked; its menu has the lock's actions.
        _lockBadge = Menus.MenuButton(new DnIcon { Kind = "lock", Width = 16, Height = 16 }, () => app.PaletteLockEntries(), null, 290, PlacementMode.RightEdgeAlignedBottom);
        _lockBadge.Name = "PaletteLockBadge";
        _lockBadge.Width = 30;
        _lockBadge.Height = 24;
        _lockBadge.MinWidth = 30;
        _lockBadge.Padding = new Thickness(0);
        _lockBadge.HorizontalAlignment = HorizontalAlignment.Center;
        _lockBadge.Margin = new Thickness(0, 0, 0, 4);
        _lockBadge.Foreground = new SolidColorBrush(DnColors.Accent);
        _lockBadge.IsVisible = false;
        var dock = new DockPanel { Margin = new Thickness(0, 7) };
        DockPanel.SetDock(_chips, Dock.Bottom);
        dock.Children.Add(_chips);
        DockPanel.SetDock(_lockBadge, Dock.Bottom);
        dock.Children.Add(_lockBadge);
        // On a short window (e.g. a tall timeline at 150 %) the tools scroll instead of running into the colour chips.
        dock.Children.Add(new ScrollViewer
        {
            Content = _tools,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            ClipToBounds = true,
        });
        Child = dock;
        app.WorkspaceChanged += Rebuild;
        app.Session.StateChanged += Refresh;
        Rebuild();
    }

    private void Rebuild()
    {
        _tools.Children.Clear();
        _buttons.Clear();
        foreach (var kind in _app.CurrentTools)
        {
            var info = Workspaces.Tools[kind];
            var k = kind;
            var b = UiKit.Button(info.Icon, tip: $"{info.Name}{(info.Shortcut is { } s ? $" ({s})" : "")}\n{info.Hint}", click: () => _app.Session.SelectTool(k), classes: "tool");
            b.Name = "Tool" + kind;
            _tools.Children.Add(b);
            _buttons.Add((b, kind));
        }
        _chips.IsVisible = _app.Workspace.Kind != WorkspaceKind.PixelArt;
        Refresh();
    }

    private void Refresh()
    {
        var s = _app.Session;
        foreach (var (b, k) in _buttons) b.Classes.Set("on", s.Tool == k);
        _fg.Background = UiKit.Brush(s.Foreground);
        _bg.Background = UiKit.Brush(s.Background);
        _lockBadge.IsVisible = s.PaletteLocked;
        if (s.PaletteLocked) ToolTip.SetTip(_lockBadge, $"Locked to “{s.Document.Palette.Name}”: only its colours can be used · click for options");
    }
}

/// <summary>Editing's status bar: file, size, colour mode, layer, cursor and selection; view controls at the right.</summary>
public sealed class StatusBar : Border
{
    private readonly MainWindow _app;
    private readonly TextBlock _text = new() { Classes = { "dim", "num" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

    public StatusBar(MainWindow app)
    {
        _app = app;
        Height = 32;
        Background = new SolidColorBrush(DnColors.Ui);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(0, 1, 0, 0);
        var view = new ViewControls(app, compact: true);
        var dock = new DockPanel { Margin = new Thickness(10, 0, 6, 0) };
        DockPanel.SetDock(view, Dock.Right);
        dock.Children.Add(view);
        dock.Children.Add(_text);
        Child = dock;
        app.Session.StateChanged += Refresh;
        app.Session.DocumentReplaced += Watch;
        app.Canvas.PointerMovedOverCanvas += () =>
        {
            if (!app.Canvas.IsStroking) Refresh(); // nothing to report mid-stroke; keep the pen path light
        };
        Watch();
    }

    private Document? _watched;

    private void Watch()
    {
        if (_watched is not null)
        {
            _watched.LayersChanged -= Refresh;
            _watched.ActiveLayerChanged -= Refresh;
            _watched.SelectionChanged -= Refresh;
            _watched.SizeChanged -= Refresh;
        }
        _watched = _app.Session.Document;
        _watched.LayersChanged += Refresh;
        _watched.ActiveLayerChanged += Refresh;
        _watched.SelectionChanged += Refresh;
        _watched.SizeChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (!IsVisible && Parent is null) return;
        var s = _app.Session;
        var d = s.Document;
        var parts = new List<string> { s.DisplayName, $"{d.Width} × {d.Height} px", "8-bit RGB", $"Layer: {d.ActiveLayer.Name}" };
        if (d.Selection is { IsEmpty: false } sel)
        {
            var b = sel.Bounds;
            parts.Add($"Selection {b.Width} × {b.Height}");
        }
        if (_app.Canvas.PointerDocument is { } p && p.X >= 0 && p.Y >= 0 && p.X < d.Width && p.Y < d.Height) parts.Add($"{p.X}, {p.Y}");
        _text.Text = string.Join("    ", parts);
    }
}

/// <summary>The menu row of Painting, Editing and Pixel Art: brand, menus, file name, workspace switcher.</summary>
public sealed class TitleBar : Border
{
    private readonly MainWindow _app;
    private readonly TextBlock _doc = new() { Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _menus = new() { Orientation = Orientation.Horizontal, Spacing = 0 };

    public TitleBar(MainWindow app)
    {
        _app = app;
        Height = 40;
        Background = new SolidColorBrush(DnColors.Ui);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(0, 0, 0, 1);

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
        brand.Children.Add(new BrandMark());
        brand.Children.Add(new TextBlock { Text = "DoodleNook", Classes = { "brand" }, VerticalAlignment = VerticalAlignment.Center });

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new WorkspaceSwitcher(app));
        var hide = UiKit.Button("expand", tip: "Hide the interface (Tab)", click: () => app.SetUiHidden(true), classes: "ghost");
        right.Children.Add(hide);

        var dock = new DockPanel { Margin = new Thickness(6, 0, 4, 0), LastChildFill = true };
        DockPanel.SetDock(brand, Dock.Left);
        DockPanel.SetDock(_menus, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(brand);
        dock.Children.Add(_menus);
        dock.Children.Add(right);
        dock.Children.Add(_doc);
        Child = dock;
        app.WorkspaceChanged += BuildMenus;
        app.Session.StateChanged += RefreshTitle;
        app.Session.DocumentReplaced += RefreshTitle;
        BuildMenus();
    }

    private void BuildMenus()
    {
        _menus.Children.Clear();
        foreach (var name in _app.MenuNames)
        {
            var n = name;
            var b = Menus.MenuButton(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }, () => _app.MenuFor(n));
            b.Height = 30;
            b.Padding = new Thickness(8, 0);
            b.Name = "Menu" + name;
            _menus.Children.Add(b);
        }
        RefreshTitle();
    }

    private void RefreshTitle()
    {
        var s = _app.Session;
        _doc.Text = $"{s.DisplayName} · {s.Document.Width} × {s.Document.Height}{(s.Document.IsDirty ? " · edited" : "")}";
    }
}

/// <summary>Quick Sketch's single bar (Mission 004's bar, now with layers, the workspace switcher and tool context).</summary>
public sealed class QuickBar : Border
{
    private readonly MainWindow _app;
    private readonly Image _preview = new() { Width = 44, Height = 22 };
    private readonly StackPanel _brushGroup;
    private readonly ContentControl _context = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly List<Action> _refresh = new();
    private readonly Ellipse _dot = new() { Width = 30, Height = 30, Stroke = new SolidColorBrush(DnColors.Text), StrokeThickness = 2 };
    private readonly Border _quickLock = new()
    {
        Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(DnColors.Accent),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -4, -4),
        Child = new DnIcon { Kind = "lock", Width = 11, Height = 11, Foreground = new SolidColorBrush(DnColors.Ui) }, Name = "QuickPaletteLock",
    };
    private readonly Button _erase, _undo, _redo;
    private readonly TextBlock _layerName = new() { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly OptionsBuilder _options;
    private Button? _layersChip;
    private string? _previewKey;
    private string? _contextKey;

    public QuickBar(MainWindow app)
    {
        _app = app;
        _options = new OptionsBuilder(app);
        Height = 40;
        Background = new SolidColorBrush(DnColors.Ui);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(0, 0, 0, 1);
        var s = app.Session;

        var menu = Menus.MenuButton(new DnIcon { Kind = "menu" }, app.QuickMenu, "Menu", 280);
        menu.Classes.Add("sq");
        menu.Name = "MenuButton";

        var chipContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chipContent.Children.Add(_preview);
        chipContent.Children.Add(new DnIcon { Kind = "chevd" });
        var preset = UiKit.Chip(chipContent, "Brush presets and settings");
        preset.Flyout = UiKit.PopFlyout(new BrushPanel(s, () => preset.Flyout?.Hide(), app: app));
        preset.Name = "PresetChip";

        var (size, rs) = UiKit.Slider("Size", 1, 500, () => s.CurrentPaintSettings.Size, s.SetSize, v => $"{v:0} px", log: true, tip: "Brush size ([ and ]) · right-drag on the canvas");
        var (opacity, ro) = UiKit.Slider("Opacity", 0.05, 1, () => s.CurrentPaintSettings.Opacity, s.SetOpacity, v => $"{v * 100:0}%",
            tip: "Brush opacity: a stroke never gets darker than this, even where it crosses itself");
        _refresh.Add(rs);
        _refresh.Add(ro);
        _refresh.Add(() => opacity.IsVisible = s.PaintTool == ToolKind.Brush);

        var colour = UiKit.Button(null, null, "Colour");
        colour.Classes.Add("sq");
        _quickLock.IsVisible = false;
        colour.Content = new Panel { Children = { _dot, _quickLock } };
        colour.Flyout = UiKit.PopFlyout(new ColourPanel(s, () => colour.Flyout?.Hide(), app: app));
        colour.Name = "ColourChip";

        _erase = UiKit.Button("eraser", "Erase", "Eraser (E)", s.ToggleEraser, "solid");
        _erase.Name = "EraseButton";
        _brushGroup = UiKit.Row(6, preset, size, opacity, UiKit.Sep(), colour, _erase);

        _undo = UiKit.Button("undo", tip: "Undo (Ctrl+Z)", click: app.Undo);
        _redo = UiKit.Button("redo", tip: "Redo (Ctrl+Shift+Z)", click: app.Redo);
        _undo.Name = "UndoButton";
        _redo.Name = "RedoButton";

        var layersContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        layersContent.Children.Add(new DnIcon { Kind = "layers" });
        layersContent.Children.Add(_layerName);
        layersContent.Children.Add(new DnIcon { Kind = "chevd" });
        var layers = _layersChip = UiKit.Button(null, null, "Layers: choose the layer you paint on, add, hide, reorder");
        layers.Content = layersContent;
        layers.Name = "LayersChip";
        var layersPanel = new LayersPanel(s, 360) { Width = 300 };
        layers.Flyout = UiKit.PopFlyout(layersPanel);

        var left = UiKit.Row(6, menu, UiKit.Sep(), _brushGroup, _context, _undo, _redo, UiKit.Sep(), layers);
        var right = UiKit.Row(4, new WorkspaceSwitcher(app), UiKit.Button("expand", tip: "Hide the interface (Tab)", click: () => app.SetUiHidden(true), classes: "ghost"));
        var dock = new DockPanel { Margin = new Thickness(6, 0, 4, 0), LastChildFill = false };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(left);
        dock.Children.Add(right);
        Child = dock;
        ClipToBounds = true;

        s.StateChanged += Refresh;
        s.DocumentReplaced += Watch;
        app.Canvas.Input!.OverlayChanged += () =>
        {
            if (!app.Canvas.IsStroking) RefreshContext();
        };
        Watch();
    }

    private Document? _watched;

    /// <summary>Customize workspace can hide the layers button.</summary>
    public void SetLayersChipVisible(bool visible)
    {
        if (_layersChip is not null) _layersChip.IsVisible = visible;
    }

    private void Watch()
    {
        if (_watched is not null)
        {
            _watched.HistoryChanged -= Refresh;
            _watched.ActiveLayerChanged -= Refresh;
            _watched.LayersChanged -= Refresh;
            _watched.SelectionChanged -= RefreshContext;
        }
        _watched = _app.Session.Document;
        _watched.HistoryChanged += Refresh;
        _watched.ActiveLayerChanged += Refresh;
        _watched.LayersChanged += Refresh;
        _watched.SelectionChanged += RefreshContext;
        Refresh();
    }

    private void RefreshContext()
    {
        var s = _app.Session;
        bool brushTool = s.Tool is ToolKind.Brush or ToolKind.Eraser or ToolKind.Eyedropper or ToolKind.Hand;
        _brushGroup.IsVisible = brushTool;
        string key = brushTool ? "brush" : _options.Key(s.Tool);
        if (key != _contextKey)
        {
            _contextKey = key;
            _context.Content = brushTool ? null : _options.Build(s.Tool, compact: true);
        }
        else _options.Refresh();
    }

    private void Refresh()
    {
        var s = _app.Session;
        foreach (var refresh in _refresh) refresh();
        _erase.Classes.Set("on", s.Tool == ToolKind.Eraser);
        _dot.Fill = UiKit.Brush(s.Foreground);
        _quickLock.IsVisible = s.PaletteLocked;
        var paint = s.CurrentPaintSettings;
        string key = $"{s.PaintTool}|{paint}";
        if (key != _previewKey)
        {
            _previewKey = key;
            _preview.Source = BrushPreview.Render(paint, new ColorRgb(DnColors.Text.R, DnColors.Text.G, DnColors.Text.B), 44, 22);
        }
        _undo.IsEnabled = s.Document.CanUndo;
        _redo.IsEnabled = s.Document.CanRedo;
        ToolTip.SetTip(_undo, s.Document.History.NextUndoName is { } u ? $"Undo {u.ToLowerInvariant()} (Ctrl+Z)" : "Undo (Ctrl+Z)");
        ToolTip.SetTip(_redo, s.Document.History.NextRedoName is { } r ? $"Redo {r.ToLowerInvariant()} (Ctrl+Shift+Z)" : "Redo (Ctrl+Shift+Z)");
        _layerName.Text = s.Document.ActiveLayer.Name;
        RefreshContext();
    }
}
