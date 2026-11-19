using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Controls;

/// <summary>Small factory helpers for the playground-styled controls built in code.</summary>
internal static class Ui
{
    public static Button Button(string? icon, string? label = null, string? tip = null, params string[] classes)
    {
        var b = new Button { Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")! };
        foreach (var c in classes) b.Classes.Add(c);
        if (icon is not null && label is null) b.Classes.Add("sq");
        b.Content = Content(icon, label);
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    public static Control Content(string? icon, string? label, string? shortcut = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null) row.Children.Add(new DnIcon { Kind = icon });
        if (label is not null) row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (shortcut is null) return row;
        var grid = new DockPanel { LastChildFill = true };
        var sc = new TextBlock { Text = shortcut, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
        DockPanel.SetDock(sc, Dock.Right);
        grid.Children.Add(sc);
        grid.Children.Add(row);
        return grid;
    }

    public static TextBlock Heading(string text) => new() { Text = text, Classes = { "h" }, VerticalAlignment = VerticalAlignment.Center };

    public static Border HSep() => new() { Classes = { "hsep" } };

    /// <summary>A pop-over header: title on the left, close button on the right (the playground's ".pop h4").</summary>
    public static Control Header(string title, Action close)
    {
        var dock = new DockPanel { Margin = new Thickness(0, -4, -4, 0) };
        var x = Button("close", tip: "Close");
        x.Height = 30;
        x.Width = 30;
        x.Click += (_, _) => close();
        DockPanel.SetDock(x, Dock.Right);
        dock.Children.Add(x);
        dock.Children.Add(Heading(title));
        return dock;
    }

    public static IBrush Brush(ColorRgb c) => new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
}

/// <summary>The brush pop-over: preset tiles, then the settings of the round brush.</summary>
public sealed class BrushPanel : StackPanel
{
    private readonly EditorSession _session;
    private readonly WrapPanel _tiles = new() { ItemWidth = 124, ItemHeight = 54 };
    private readonly DnSlider _hardness = new() { Minimum = 0, Maximum = 1, TrackWidth = 116 };
    private readonly DnSlider _smoothing = new() { Minimum = 0, Maximum = 1, TrackWidth = 116 };
    private readonly TextBlock _hardnessText = new() { Classes = { "num" }, MinWidth = 38, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _smoothingText = new() { Classes = { "num" }, MinWidth = 38, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _pSize = Ui.Button("pressure", "Size", "Pen pressure controls the size");
    private readonly Button _pOpacity = Ui.Button("pressure", "Opacity", "Pen pressure controls the opacity");
    private readonly Button[] _feel;
    private readonly TextBlock _title = Ui.Heading("Brush");

    public BrushPanel(EditorSession session, Action close)
    {
        _session = session;
        Width = 252;
        Spacing = 8;
        var header = (DockPanel)Ui.Header("Brush presets", close);
        header.Children.RemoveAt(1);
        header.Children.Add(_title);
        Children.Add(header);
        Children.Add(_tiles);

        Children.Add(Row("Hardness", _hardness, _hardnessText));
        Children.Add(Row("Smoothing", _smoothing, _smoothingText));
        _hardness.UserChanged += (_, v) => session.SetHardness(v);
        _smoothing.UserChanged += (_, v) => session.SetSmoothing(v);

        var pressureRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pressureRow.Children.Add(new TextBlock { Text = "Pen pressure", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Width = 82 });
        pressureRow.Children.Add(_pSize);
        pressureRow.Children.Add(_pOpacity);
        _pSize.Click += (_, _) => session.SetPressureSize(!session.CurrentPaintSettings.PressureSize);
        _pOpacity.Click += (_, _) => session.SetPressureOpacity(!session.CurrentPaintSettings.PressureOpacity);
        Children.Add(pressureRow);

        var feelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        feelRow.Children.Add(new TextBlock { Text = "Pressure feel", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Width = 86 });
        _feel = new[] { (PressureFeel.Soft, "Soft"), (PressureFeel.Normal, "Normal"), (PressureFeel.Firm, "Firm") }.Select(f =>
        {
            var b = Ui.Button(null, f.Item2, f.Item1 switch
            {
                PressureFeel.Soft => "A light touch gives more ink",
                PressureFeel.Firm => "Needs a firmer hand for full size",
                _ => "Pressure maps straight to size/opacity",
            });
            b.Height = 30;
            b.Padding = new Thickness(9, 0);
            b.Tag = f.Item1;
            b.Click += (_, _) => session.SetPressureFeel(f.Item1);
            feelRow.Children.Add(b);
            return b;
        }).ToArray();
        Children.Add(feelRow);

        session.StateChanged += Refresh;
        Refresh();
    }

    private static Control Row(string label, Control slider, Control value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(new TextBlock { Text = label, Classes = { "dim" }, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(slider);
        row.Children.Add(value);
        return row;
    }

    private string? _tilesFor;

    public void Refresh()
    {
        bool eraser = _session.PaintTool == ToolKind.Eraser;
        var s = _session.CurrentPaintSettings;
        _title.Text = eraser ? "Eraser" : "Brush presets";
        _tiles.IsVisible = !eraser;
        _hardness.Value = s.Hardness;
        _smoothing.Value = s.Smoothing;
        _hardnessText.Text = $"{s.Hardness * 100:0}%";
        _smoothingText.Text = $"{s.Smoothing * 100:0}%";
        _pSize.Classes.Set("on", s.PressureSize);
        _pOpacity.Classes.Set("on", s.PressureOpacity);
        _pOpacity.IsVisible = !eraser;
        foreach (var b in _feel) b.Classes.Set("on", (PressureFeel)b.Tag! == _session.PressureFeel);

        if (!eraser && _tilesFor != _session.BrushPresetId)
        {
            _tilesFor = _session.BrushPresetId;
            _tiles.Children.Clear();
            foreach (var p in EditorSession.Presets)
            {
                bool on = p.Id == _session.BrushPresetId;
                var preview = new Image
                {
                    Source = BrushPreview.Render(p.Settings, new ColorRgb(DnColors.Text.R, DnColors.Text.G, DnColors.Text.B), 108, 22),
                    Width = 108,
                    Height = 22,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                var stack = new StackPanel { Spacing = 1 };
                stack.Children.Add(preview);
                stack.Children.Add(new TextBlock { Text = p.Name, FontSize = 12, Foreground = new SolidColorBrush(on ? DnColors.Text : DnColors.Text2) });
                // The playground's ".btile": a dark tile; the chosen one gets an accent outline and a soft accent fill.
                var tile = new Button
                {
                    Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                    Height = 50,
                    Width = 120,
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
                tile.Click += (_, _) => _session.ApplyPreset(p.Id);
                ToolTip.SetTip(tile, $"{p.Name}: {p.Settings.Size:0} px, {p.Settings.Opacity * 100:0}% opacity");
                _tiles.Children.Add(tile);
            }
        }
    }
}

/// <summary>The colour pop-over: saturation/value square, hue strip, hex value and recent colours.</summary>
public sealed class ColourPanel : StackPanel
{
    private readonly EditorSession _session;
    private readonly SaturationValueArea _sv = new() { Height = 174 };
    private readonly HueStrip _hue = new() { Height = 26 };
    private readonly Border _current = new() { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), BorderThickness = new Thickness(1) };
    private readonly TextBox _hex = new() { Width = 96, FontFeatures = FontFeatureCollection.Parse("+tnum") };
    private readonly WrapPanel _swatches = new() { ItemWidth = 31, ItemHeight = 31 };
    private double _h, _s, _v;
    private ColorRgb _shown;
    private bool _editingHex;

    public ColourPanel(EditorSession session, Action close)
    {
        _session = session;
        Width = 252;
        Spacing = 8;
        Children.Add(Ui.Header("Colour", close));
        Children.Add(_sv);
        Children.Add(_hue);
        var row = new DockPanel();
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(_current);
        left.Children.Add(_hex);
        DockPanel.SetDock(left, Dock.Left);
        row.Children.Add(left);
        row.Children.Add(new TextBlock { Text = "Recent", Classes = { "dim" }, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(row);
        Children.Add(_swatches);

        _sv.UserChanged += (s, v) =>
        {
            _s = s;
            _v = v;
            Push();
        };
        _hue.UserChanged += h =>
        {
            _h = h;
            _sv.Set(_h, _s, _v);
            Push();
        };
        _hex.GotFocus += (_, _) => _editingHex = true;
        _hex.LostFocus += (_, _) =>
        {
            _editingHex = false;
            CommitHex();
        };
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitHex();
                e.Handled = true;
            }
        };
        session.StateChanged += Refresh;
        Refresh();
    }

    private void CommitHex()
    {
        if (ColorRgb.TryParse(_hex.Text, out var c)) _session.SetForeground(c);
        else _hex.Text = _session.Foreground.ToHex();
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
            // Only re-derive HSV when the colour changed from outside, so hue survives greys and black.
            var (h, s, v) = fg.ToHsv();
            if (s > 0.001 && v > 0.001) _h = h;
            if (v > 0.001) _s = s;
            _v = v;
            _shown = fg;
            _sv.Set(_h, _s, _v);
            _hue.Set(_h);
        }
        _current.Background = Ui.Brush(fg);
        if (!_editingHex) _hex.Text = fg.ToHex();

        _swatches.Children.Clear();
        foreach (var c in _session.RecentColors)
        {
            var sw = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                Width = 31,
                Height = 31,
                Padding = new Thickness(0),
                Content = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = Ui.Brush(c),
                    BorderBrush = new SolidColorBrush(c == fg ? DnColors.Accent : Color.FromArgb(90, 0, 0, 0)),
                    BorderThickness = new Thickness(c == fg ? 2 : 1),
                },
            };
            ToolTip.SetTip(sw, c.ToHex());
            var colour = c;
            sw.Click += (_, _) => _session.SetForeground(colour);
            _swatches.Children.Add(sw);
        }
    }
}
