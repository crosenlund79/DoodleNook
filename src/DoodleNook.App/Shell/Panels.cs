using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Shell;

/// <summary>The colour panel: saturation/value square, hue strip, hex, RGB and HSV values, saved swatches and recent colours.</summary>
public sealed class ColourPanel : StackPanel
{
    private readonly EditorSession _session;
    private readonly SaturationValueArea _sv = new() { Height = 150 };
    private readonly HueStrip _hue = new() { Height = 24 };
    private readonly Border _current = UiKit.Swatch(null, 26, 13);
    private readonly TextBox _hex = new() { Width = 92, FontFeatures = FontFeatureCollection.Parse("+tnum") };
    private readonly TextBox[] _numbers = new TextBox[6];
    private readonly WrapPanel _swatches = new() { ItemWidth = 29, ItemHeight = 29 };
    private readonly WrapPanel _recent = new() { ItemWidth = 29, ItemHeight = 29 };
    private double _h, _s, _v;
    private ColorRgb _shown;
    private bool _editing;

    /// <param name="compact">Docked version: a smaller square, the numbers behind a toggle, no separate recent row.</param>
    public ColourPanel(EditorSession session, Action? close = null, bool compact = false)
    {
        _session = session;
        Width = 252;
        Spacing = 7;
        if (close is not null) Children.Add(UiKit.Header("Colour", close));
        if (compact) _sv.Height = 112;
        Children.Add(_sv);
        Children.Add(_hue);

        var left = UiKit.Row(8, _current, _hex);
        Children.Add(left);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*,8,14,*,8,14,*"), RowDefinitions = new RowDefinitions("Auto,4,Auto") };
        string[] labels = ["R", "G", "B", "H", "S", "V"];
        for (int i = 0; i < 6; i++)
        {
            var label = new TextBlock { Text = labels[i], Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox { MinHeight = 28, Padding = new Thickness(5, 3), FontSize = 12, FontFeatures = FontFeatureCollection.Parse("+tnum") };
            int index = i;
            box.GotFocus += (_, _) => _editing = true;
            box.LostFocus += (_, _) => { _editing = false; CommitNumber(index); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitNumber(index); e.Handled = true; } };
            _numbers[i] = box;
            int row = i < 3 ? 0 : 2, col = (i % 3) * 3;
            Grid.SetRow(label, row);
            Grid.SetColumn(label, col);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, col + 1);
            grid.Children.Add(label);
            grid.Children.Add(box);
        }
        Children.Add(grid);
        if (compact)
        {
            grid.IsVisible = false;
            var values = UiKit.Button(null, "RGB · HSV", "Show the colour as numbers");
            values.Height = 28;
            values.FontSize = 12;
            values.Click += (_, _) =>
            {
                grid.IsVisible = !grid.IsVisible;
                values.Classes.Set("on", grid.IsVisible);
            };
            left.Children.Add(values);
        }

        var swatchHead = new DockPanel();
        var add = UiKit.Button("plus", tip: "Save the current colour as a swatch", click: () => session.AddSwatch(session.Foreground));
        add.Width = 26;
        add.Height = 26;
        add.MinWidth = 26;
        DockPanel.SetDock(add, Dock.Right);
        swatchHead.Children.Add(add);
        swatchHead.Children.Add(UiKit.Label("Swatches · right-click removes", size: 12));
        Children.Add(swatchHead);
        Children.Add(_swatches);
        if (!compact)
        {
            Children.Add(UiKit.Label("Recent", size: 12));
            Children.Add(_recent);
        }

        _sv.UserChanged += (s, v) => { _s = s; _v = v; Push(); };
        _hue.UserChanged += h => { _h = h; _sv.Set(_h, _s, _v); Push(); };
        _hex.GotFocus += (_, _) => _editing = true;
        _hex.LostFocus += (_, _) => { _editing = false; CommitHex(); };
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitHex(); e.Handled = true; } };
        session.StateChanged += Refresh;
        Refresh();
    }

    private void CommitHex()
    {
        if (ColorRgb.TryParse(_hex.Text, out var c)) _session.SetForeground(c);
        else _hex.Text = _session.Foreground.ToHex();
    }

    private void CommitNumber(int i)
    {
        if (!double.TryParse(_numbers[i].Text, out double v)) { Refresh(); return; }
        var c = _session.Foreground;
        if (i < 3)
        {
            byte b = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
            c = i switch { 0 => c with { R = b }, 1 => c with { G = b }, _ => c with { B = b } };
        }
        else
        {
            double h = _h, s = _s, val = _v;
            if (i == 3) h = Math.Clamp(v, 0, 360);
            else if (i == 4) s = Math.Clamp(v / 100, 0, 1);
            else val = Math.Clamp(v / 100, 0, 1);
            c = ColorRgb.FromHsv(h, s, val);
        }
        _session.SetForeground(c);
    }

    private void Push()
    {
        var c = ColorRgb.FromHsv(_h, _s, _v);
        _shown = c;
        _session.SetForeground(c);
    }

    public void Refresh()
    {
        var fg = _session.Foreground;
        if (fg != _shown)
        {
            var (h, s, v) = fg.ToHsv();
            if (s > 0.001 && v > 0.001) _h = h;
            if (v > 0.001) _s = s;
            _v = v;
            _shown = fg;
            _sv.Set(_h, _s, _v);
            _hue.Set(_h);
        }
        _current.Background = UiKit.Brush(fg);
        if (!_editing)
        {
            _hex.Text = fg.ToHex();
            double[] values = [fg.R, fg.G, fg.B, Math.Round(_h), Math.Round(_s * 100), Math.Round(_v * 100)];
            for (int i = 0; i < 6; i++) _numbers[i].Text = values[i].ToString("0");
        }
        Fill(_swatches, _session.Swatches, removable: true);
        Fill(_recent, _session.RecentColors.Take(8), removable: false);
    }

    private void Fill(WrapPanel panel, IEnumerable<ColorRgb> colors, bool removable)
    {
        panel.Children.Clear();
        var fg = _session.Foreground;
        foreach (var c in colors)
        {
            var sw = UiKit.Button(null, null, c.ToHex());
            sw.Width = 29;
            sw.Height = 29;
            sw.MinWidth = 29;
            sw.Padding = new Thickness(0);
            var dot = UiKit.Swatch(c, 24, 12);
            if (c == fg)
            {
                dot.BorderBrush = new SolidColorBrush(DnColors.Accent);
                dot.BorderThickness = new Thickness(2);
            }
            sw.Content = dot;
            var colour = c;
            sw.Click += (_, _) => _session.SetForeground(colour);
            if (removable)
                sw.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton == MouseButton.Right) _session.RemoveSwatch(colour);
                };
            panel.Children.Add(sw);
        }
    }
}

/// <summary>The brush panel: recognisable previews of every preset, then the settings of the current brush or eraser.</summary>
public sealed class BrushPanel : StackPanel
{
    private readonly EditorSession _session;
    private readonly WrapPanel _tiles = new() { ItemWidth = 120, ItemHeight = 54 };
    private readonly TextBlock _title = UiKit.Heading("Brushes");
    private readonly List<Action> _refresh = new();
    private readonly Control _grainRow, _flowRow;
    private string? _tilesFor;

    /// <param name="compact">Docked version: the presets, with the settings behind a toggle.</param>
    public BrushPanel(EditorSession session, Action? close = null, bool compact = false)
    {
        _session = session;
        Width = 252;
        Spacing = 6;
        if (close is not null)
        {
            var header = (DockPanel)UiKit.Header("Brushes", close);
            header.Children.RemoveAt(1);
            header.Children.Add(_title);
            Children.Add(header);
        }
        Children.Add(_tiles);
        var settings = new StackPanel { Spacing = 6 };
        if (compact)
        {
            var more = UiKit.Button("props", "Brush settings", "Hardness, smoothing, pressure, mirror");
            more.Height = 28;
            more.Click += (_, _) =>
            {
                settings.IsVisible = !settings.IsVisible;
                more.Classes.Set("on", settings.IsVisible);
            };
            settings.IsVisible = false;
            Children.Add(more);
        }
        Children.Add(settings);

        Control SliderRow(string label, double min, double max, Func<BrushSettings, double> get, Action<double> set, Func<double, string> fmt)
        {
            var (row, refresh) = UiKit.Slider(null, min, max, () => get(session.CurrentPaintSettings), set, fmt, trackWidth: 116);
            _refresh.Add(refresh);
            var dock = new DockPanel();
            var l = UiKit.Label(label);
            l.Width = 72;
            DockPanel.SetDock(l, Dock.Left);
            dock.Children.Add(l);
            dock.Children.Add(row);
            return dock;
        }

        settings.Children.Add(SliderRow("Hardness", 0, 1, s => s.Hardness, session.SetHardness, v => $"{v * 100:0}%"));
        settings.Children.Add(SliderRow("Smoothing", 0, 1, s => s.Smoothing, session.SetSmoothing, v => $"{v * 100:0}%"));
        _grainRow = SliderRow("Grain", 0, 1, s => s.Grain, session.SetGrain, v => $"{v * 100:0}%");
        settings.Children.Add(_grainRow);
        _flowRow = SliderRow("Flow", 0.005, 0.3, s => s.Flow, session.SetFlow, v => $"{v * 100:0}%");
        settings.Children.Add(_flowRow);

        var (pSize, rs) = UiKit.Toggle("pressure", "Size", "Pen pressure controls the size", () => session.CurrentPaintSettings.PressureSize, session.SetPressureSize);
        var (pOpacity, ro) = UiKit.Toggle("pressure", "Opacity", "Pen pressure controls the opacity", () => session.CurrentPaintSettings.PressureOpacity, session.SetPressureOpacity);
        _refresh.Add(rs);
        _refresh.Add(ro);
        var pressureRow = UiKit.Row(4, UiKit.Label("Pressure"), pSize, pOpacity);
        settings.Children.Add(pressureRow);

        var (feel, rf) = UiKit.Segmented(
            [(PressureFeel.Soft, null, "Soft", "A light touch gives more ink"), (PressureFeel.Normal, null, "Normal", "Pressure maps straight to size/opacity"), (PressureFeel.Firm, null, "Firm", "Needs a firmer hand")],
            () => session.PressureFeel, session.SetPressureFeel, compact: true);
        _refresh.Add(rf);
        settings.Children.Add(UiKit.Row(6, UiKit.Label("Feel"), feel));

        var (sym, rsym) = UiKit.Segmented(
            [(Symmetry.None, null, "Off", "No mirroring"), (Symmetry.Vertical, "sym", null, "Mirror left ↔ right"), (Symmetry.Horizontal, null, "↕", "Mirror top ↕ bottom"), (Symmetry.Both, null, "4×", "Mirror both ways")],
            () => session.Symmetry, session.SetSymmetry, compact: true);
        _refresh.Add(rsym);
        settings.Children.Add(UiKit.Row(6, UiKit.Label("Mirror"), sym));

        session.StateChanged += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        bool eraser = _session.PaintTool == ToolKind.Eraser;
        var s = _session.CurrentPaintSettings;
        _title.Text = eraser ? "Eraser" : "Brushes";
        _tiles.IsVisible = !eraser;
        _grainRow.IsVisible = !eraser && s.Grain > 0;
        _flowRow.IsVisible = !eraser && s.BuildUp;
        foreach (var r in _refresh) r();
        if (eraser || _tilesFor == _session.BrushPresetId) return;
        _tilesFor = _session.BrushPresetId;
        _tiles.Children.Clear();
        var ink = new ColorRgb(DnColors.Text.R, DnColors.Text.G, DnColors.Text.B);
        foreach (var p in EditorSession.Presets)
        {
            bool on = p.Id == _session.BrushPresetId;
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(new Image { Source = BrushPreview.Render(p.Settings, ink, 102, 22), Width = 102, Height = 22, HorizontalAlignment = HorizontalAlignment.Left });
            stack.Children.Add(new TextBlock { Text = p.Name, FontSize = 12, Foreground = new SolidColorBrush(on ? DnColors.Text : DnColors.Text2) });
            var tile = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                Height = 50,
                Width = 116,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = new Border
                {
                    Child = stack,
                    Padding = new Thickness(6, 4),
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(on ? Color.Parse("#2BE9A94D") : DnColors.Ui),
                    BorderBrush = new SolidColorBrush(on ? DnColors.Accent : Colors.Transparent),
                    BorderThickness = new Thickness(1),
                },
            };
            var id = p.Id;
            tile.Click += (_, _) => _session.ApplyPreset(id);
            ToolTip.SetTip(tile, $"{p.Name}: {p.Description}");
            _tiles.Children.Add(tile);
        }
    }
}

/// <summary>Editing's history panel: every step, oldest first; click a step to go back (or forward) to it.</summary>
public sealed class HistoryPanel : StackPanel
{
    private readonly EditorSession _session;
    private Document? _watched;

    public HistoryPanel(EditorSession session)
    {
        _session = session;
        Spacing = 2;
        session.DocumentReplaced += Watch;
        Watch();
    }

    private void Watch()
    {
        if (_watched is not null) _watched.HistoryChanged -= Rebuild;
        _watched = _session.Document;
        _watched.HistoryChanged += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        var h = _session.Document.History;
        Children.Add(Item("history", "Start", 0, current: h.UndoCount == 0, future: false));
        foreach (var e in h.UndoEntries.TakeLast(60)) Children.Add(Item(IconFor(e.Name), e.Name, e.Id, current: ReferenceEquals(e, h.UndoEntries[^1]), future: false));
        foreach (var e in h.RedoEntries.Take(30)) Children.Add(Item(IconFor(e.Name), e.Name, e.Id, current: false, future: true));
    }

    private Control Item(string icon, string label, long id, bool current, bool future)
    {
        var b = UiKit.Button(icon, label, future ? "Redo up to here" : "Go back to here", () => _session.JumpToHistory(id), "menu");
        b.Height = 30;
        if (current) b.Classes.Add("on");
        if (future) b.Opacity = 0.5;
        return b;
    }

    private static string IconFor(string name) => name switch
    {
        _ when name.Contains("Erase") || name.Contains("eraser") => "eraser",
        _ when name.Contains("stroke") || name.Contains("pencil") => "brush",
        _ when name.Contains("select") || name.Contains("Select") || name.Contains("Lasso") || name.Contains("wand") => "marquee",
        _ when name.Contains("layer") || name.Contains("Merge") => "layers",
        _ when name.Contains("Fill") => "fill",
        _ when name.Contains("Gradient") => "gradient",
        _ when name.Contains("Move") || name.Contains("Transform") || name.Contains("Flip") || name.Contains("Rotate") => "move",
        _ when name.Contains("Crop") || name.Contains("size") => "crop",
        _ => "props",
    };
}

/// <summary>Pixel Art's palette column (the playground's ".pal-col"): big foreground/background swatches and the palette.</summary>
public sealed class PaletteColumn : StackPanel
{
    private readonly EditorSession _session;
    private readonly WrapPanel _grid = new() { ItemWidth = 30, ItemHeight = 30 };
    private readonly Border _fg, _bg;
    private readonly TextBlock _name = UiKit.Label("Palette");
    private Document? _watched;

    public PaletteColumn(EditorSession session, Func<Control> colourEditor, Action<bool> setRightPaintsBackground, Func<bool> rightPaintsBackground)
    {
        _session = session;
        Width = 136;
        Spacing = 10;
        Margin = new Thickness(7);

        _fg = UiKit.Swatch(session.Foreground, 38, 6, outline: true);
        _bg = UiKit.Swatch(session.Background, 38, 6, outline: true);
        var fgButton = new Button { Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!, Padding = new Thickness(0), Width = 38, Height = 38, MinWidth = 38, Content = _fg };
        ToolTip.SetTip(fgButton, "Foreground colour · click to edit");
        fgButton.Flyout = UiKit.PopFlyout(colourEditor(), Avalonia.Controls.PlacementMode.LeftEdgeAlignedTop);
        var bgButton = new Button { Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!, Padding = new Thickness(0), Width = 38, Height = 38, MinWidth = 38, Content = _bg };
        ToolTip.SetTip(bgButton, "Background colour · click to swap (X)");
        bgButton.Click += (_, _) => session.SwapColors();
        var pair = new Avalonia.Controls.Canvas { Height = 62, Width = 70 };
        Avalonia.Controls.Canvas.SetLeft(bgButton, 24);
        Avalonia.Controls.Canvas.SetTop(bgButton, 22);
        pair.Children.Add(bgButton);
        pair.Children.Add(fgButton);
        Children.Add(pair);

        var menu = UiKit.Button("dots", tip: "Palette options");
        menu.Width = 28;
        menu.Height = 28;
        menu.MinWidth = 28;
        var menuItems = new StackPanel { Spacing = 1, Width = 220 };
        void Item(string icon, string label, Action a) => menuItems.Children.Add(UiKit.Button(icon, label, null, () => { ((Avalonia.Controls.Flyout)menu.Flyout!).Hide(); a(); }, "menu"));
        Item("plus", "Add the foreground colour", () => Change(p => { if (!p.Colors.Contains(session.Foreground)) p.Colors.Add(session.Foreground); }));
        Item("trash", "Remove the foreground colour", () => Change(p => p.Colors.Remove(session.Foreground)));
        Item("image", "Palette from the picture", () => session.Document.Palette = Palette.FromImage(session.Document.Flatten(), 64));
        Item("palette", "Reset to Nook 32", () => session.Document.Palette = Palette.Default());
        menu.Flyout = UiKit.PopFlyout(menuItems);
        menu.Flyout.Closed += (_, _) => Refresh();
        var head = new DockPanel();
        DockPanel.SetDock(menu, Dock.Right);
        head.Children.Add(menu);
        head.Children.Add(_name);
        Children.Add(head);
        Children.Add(_grid);
        Children.Add(new TextBlock { Text = "Left-click a swatch for foreground, right-click for background.", Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });

        Children.Add(new TextBlock { Text = "Right-click on canvas:", Classes = { "dim" }, FontSize = 12, Margin = new Thickness(0, 12, 0, 0) });
        var (choice, refreshChoice) = UiKit.Segmented(
            [(false, null, "Palette", "Right-click opens the quick palette"), (true, null, "Paint BG", "Right-click paints with the background colour (like Aseprite)")],
            rightPaintsBackground, v => setRightPaintsBackground(v), compact: true);
        Children.Add(choice);

        session.StateChanged += () => { refreshChoice(); Refresh(); };
        session.DocumentReplaced += Refresh;
        Refresh();

        void Change(Action<Palette> f)
        {
            var p = session.Document.Palette.Clone();
            f(p);
            session.Document.Palette = p;
            Refresh();
        }
    }

    public void Refresh()
    {
        if (!ReferenceEquals(_watched, _session.Document)) _watched = _session.Document;
        _fg.Background = UiKit.Brush(_session.Foreground);
        _bg.Background = UiKit.Brush(_session.Background);
        var palette = _session.Document.Palette;
        _name.Text = palette.Name;
        _grid.Children.Clear();
        foreach (var c in palette.Colors)
        {
            var colour = c;
            var b = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(4),
                Background = UiKit.Brush(c),
                BorderBrush = new SolidColorBrush(c == _session.Foreground ? DnColors.Accent : c == _session.Background ? DnColors.Text : Color.FromArgb(100, 0, 0, 0)),
                BorderThickness = new Thickness(c == _session.Foreground || c == _session.Background ? 2 : 1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(b, $"{c.ToHex()} · left: foreground · right: background");
            b.PointerPressed += (_, e) =>
            {
                var props = e.GetCurrentPoint(b).Properties;
                if (props.IsRightButtonPressed) _session.SetBackground(colour);
                else _session.SetForeground(colour);
                e.Handled = true;
            };
            _grid.Children.Add(b);
        }
    }
}
