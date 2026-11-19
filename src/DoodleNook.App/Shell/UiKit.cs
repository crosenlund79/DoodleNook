using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using DoodleNook.App.Controls;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Shell;

/// <summary>
/// Builders for the Mission 003 playground controls (".b" buttons, ".wsw" segmented groups, ".sl" sliders, ".sep"
/// dividers), so every bar and panel is assembled from the same pieces and looks consistent.
/// </summary>
public static class UiKit
{
    private static ControlTheme Theme(string key) => (ControlTheme)Application.Current!.FindResource(key)!;

    public static Button Button(string? icon, string? label = null, string? tip = null, Action? click = null, params string[] classes)
    {
        var b = new Button { Theme = Theme("DnButton") };
        foreach (var c in classes) b.Classes.Add(c);
        if (icon is not null && label is null) b.Classes.Add("sq");
        b.Content = Content(icon, label);
        if (tip is not null) ToolTip.SetTip(b, tip);
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    public static Button Chip(Control content, string? tip = null)
    {
        var b = new Button { Theme = Theme("DnChip"), Content = content };
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    public static Control Content(string? icon, string? label, string? shortcut = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null) row.Children.Add(new DnIcon { Kind = icon });
        if (label is not null) row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (shortcut is null) return row;
        var dock = new DockPanel { LastChildFill = true };
        var sc = new TextBlock { Text = shortcut, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
        DockPanel.SetDock(sc, Dock.Right);
        dock.Children.Add(sc);
        dock.Children.Add(row);
        return dock;
    }

    public static Border Sep() => new() { Classes = { "sep" } };

    public static Border HSep() => new() { Classes = { "hsep" } };

    public static TextBlock Label(string text, bool dim = true, double? size = null)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (dim) t.Classes.Add("dim");
        if (size is { } s) t.FontSize = s;
        return t;
    }

    public static TextBlock Heading(string text) => new() { Text = text, Classes = { "h" }, VerticalAlignment = VerticalAlignment.Center };

    public static StackPanel Row(double spacing = 6, params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) row.Children.Add(c);
        return row;
    }

    /// <summary>The playground's ".wsw" segmented group: one option on (accent).</summary>
    public static (Border Root, Action Refresh) Segmented<T>(IEnumerable<(T Value, string? Icon, string? Label, string? Tip)> options, Func<T> current, Action<T> set, bool compact = false)
        where T : notnull
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var buttons = new List<(Button B, T V)>();
        foreach (var (value, icon, label, tip) in options)
        {
            var b = Button(icon, label, tip, () => set(value));
            b.Height = compact ? 26 : 28;
            if (label is not null) b.Padding = new Thickness(8, 0);
            stack.Children.Add(b);
            buttons.Add((b, value));
        }
        var root = new Border
        {
            Child = stack,
            Background = new SolidColorBrush(DnColors.Ui2),
            BorderBrush = new SolidColorBrush(DnColors.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        void Refresh()
        {
            var cur = current();
            foreach (var (b, v) in buttons)
            {
                bool on = EqualityComparer<T>.Default.Equals(v, cur);
                b.Classes.Set("solid", on);
                b.Classes.Set("on", on);
            }
        }
        Refresh();
        return (root, Refresh);
    }

    /// <summary>A toggle (".b.on") bound to a bool.</summary>
    public static (Button Button, Action Refresh) Toggle(string? icon, string? label, string? tip, Func<bool> get, Action<bool> set)
    {
        var b = Button(icon, label, tip);
        b.Click += (_, _) => set(!get());
        void Refresh() => b.Classes.Set("on", get());
        Refresh();
        return (b, Refresh);
    }

    /// <summary>The playground's ".sl" slider: label, track and value text.</summary>
    public static (StackPanel Root, Action Refresh) Slider(string? label, double min, double max, Func<double> get, Action<double> set,
        Func<double, string> format, double trackWidth = 96, bool log = false, Action? finished = null, string? tip = null)
    {
        var slider = new DnSlider { Minimum = min, Maximum = max, IsLogarithmic = log, TrackWidth = trackWidth };
        var value = new TextBlock { Classes = { "num" }, MinWidth = 40, VerticalAlignment = VerticalAlignment.Center };
        slider.UserChanged += (_, v) => set(v);
        if (finished is not null) slider.UserFinished += (_, _) => finished();
        var root = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        if (label is not null) root.Children.Add(new TextBlock { Text = label, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(slider);
        root.Children.Add(value);
        if (tip is not null) ToolTip.SetTip(root, tip);
        void Refresh()
        {
            double v = get();
            slider.Value = v;
            value.Text = format(v);
        }
        Refresh();
        return (root, Refresh);
    }

    public static IBrush Brush(ColorRgb c) => new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));

    public static Border Swatch(ColorRgb? c, double size, double radius, bool outline = false)
    {
        var b = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            BorderBrush = new SolidColorBrush(outline ? DnColors.Text : Color.FromArgb(90, 0, 0, 0)),
            BorderThickness = new Thickness(outline ? 2 : 1),
        };
        b.Background = c is { } col ? Brush(col) : CheckerBrush();
        return b;
    }

    public static IBrush CheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Color.Parse("#EBE5DC")), Geometry = new RectangleGeometry(new Rect(0, 0, 10, 10)) });
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Color.Parse("#CFC7BB")), Geometry = new RectangleGeometry(new Rect(0, 0, 5, 5)) });
        group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(Color.Parse("#CFC7BB")), Geometry = new RectangleGeometry(new Rect(5, 5, 5, 5)) });
        return new DrawingBrush(group) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 10, 10, RelativeUnit.Absolute) };
    }

    private static readonly HashSet<Flyout> Open = new();

    /// <summary>Raised whenever one of DoodleNook's pop-overs opens (the window moves focus out of text boxes).</summary>
    public static event Action<Flyout>? FlyoutOpened;

    /// <summary>The playground's ".pop" pop-over.</summary>
    public static Flyout PopFlyout(Control content, PlacementMode placement = PlacementMode.BottomEdgeAlignedLeft)
    {
        var f = new Flyout
        {
            Content = content,
            Placement = placement,
            VerticalOffset = placement is PlacementMode.BottomEdgeAlignedLeft or PlacementMode.BottomEdgeAlignedRight ? 6 : 0,
            HorizontalOffset = placement is PlacementMode.RightEdgeAlignedTop or PlacementMode.RightEdgeAlignedBottom ? 10
                : placement is PlacementMode.LeftEdgeAlignedTop ? -10 : 0,
        };
        f.Opened += (_, _) =>
        {
            Open.Add(f);
            FlyoutOpened?.Invoke(f);
        };
        f.Closed += (_, _) => Open.Remove(f);
        return f;
    }

    public static bool AnyFlyoutOpen => Open.Count > 0;

    public static void HideOpenFlyouts()
    {
        foreach (var f in Open.ToArray()) f.Hide();
    }

    public static Control Header(string title, Action close)
    {
        var dock = new DockPanel { Margin = new Thickness(0, -4, -4, 0) };
        var x = Button("close", tip: "Close", click: close);
        x.Height = 30;
        x.Width = 30;
        DockPanel.SetDock(x, Dock.Right);
        dock.Children.Add(x);
        dock.Children.Add(Heading(title));
        return dock;
    }
}
