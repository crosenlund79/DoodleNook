using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Shell;

/// <summary>A dock section: its id (remembered when closed), title, icon and content.</summary>
public sealed record DockSection(string Id, string Title, string Icon, Control Content);

/// <summary>
/// The right-hand dock of Painting and Editing (the playground's ".dock"): a "Panels" header that collapses the
/// dock to an icon strip, and collapsible sections. The width is changed by dragging the splitter beside it.
/// </summary>
public sealed class DockArea : Border
{
    private readonly MainWindow _app;
    private readonly IReadOnlyList<DockSection> _sections;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _strip = new() { Spacing = 4, Margin = new Thickness(0, 6), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Control _expanded;

    public DockArea(MainWindow app, IReadOnlyList<DockSection> sections)
    {
        _app = app;
        _sections = sections;
        Background = new SolidColorBrush(DnColors.Ui);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(1, 0, 0, 0);

        var head = new DockPanel { Margin = new Thickness(10, 6, 4, 2) };
        var collapse = UiKit.Button("chevr", tip: "Collapse the panels to icons", click: () => SetCollapsed(true));
        collapse.Width = 28;
        collapse.Height = 28;
        collapse.MinWidth = 28;
        DockPanel.SetDock(collapse, Dock.Right);
        head.Children.Add(collapse);
        head.Children.Add(new TextBlock { Text = "Panels", Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        _scroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var expanded = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        expanded.Children.Add(head);
        expanded.Children.Add(_scroll);
        _expanded = expanded;

        var expand = UiKit.Button("chevl", tip: "Show the panels", click: () => SetCollapsed(false));
        expand.Width = 34;
        expand.Height = 30;
        _strip.Children.Add(expand);
        foreach (var sec in sections)
        {
            var id = sec.Id;
            var b = UiKit.Button(sec.Icon, tip: sec.Title, click: () =>
            {
                _app.WsState.ClosedSections.Remove(id);
                SetCollapsed(false);
            });
            b.Width = 34;
            _strip.Children.Add(b);
        }

        foreach (var sec in sections) _list.Children.Add(Section(sec));
        Refresh();
    }

    public bool Collapsed => _app.WsState.DockCollapsed;

    public event Action? CollapsedChanged;

    public void SetCollapsed(bool collapsed)
    {
        _app.WsState.DockCollapsed = collapsed;
        Refresh();
        CollapsedChanged?.Invoke();
    }

    public void Refresh()
    {
        Child = Collapsed ? _strip : _expanded;
        Width = Collapsed ? 46 : double.NaN;
        _list.Children.Clear();
        foreach (var sec in _sections) _list.Children.Add(Section(sec));
    }

    private Control Section(DockSection sec)
    {
        bool open = !_app.WsState.ClosedSections.Contains(sec.Id);
        var head = new DockPanel { Height = 34, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        var chevron = new DnIcon { Kind = open ? "chevd" : "chevr", Width = 16, Height = 16, Foreground = new SolidColorBrush(DnColors.Text2) };
        DockPanel.SetDock(chevron, Dock.Right);
        head.Children.Add(chevron);
        var title = UiKit.Row(7, new DnIcon { Kind = sec.Icon, Width = 16, Height = 16, Foreground = new SolidColorBrush(DnColors.Text2) },
            new TextBlock { Text = sec.Title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(title);
        ToolTip.SetTip(head, open ? $"Hide {sec.Title}" : $"Show {sec.Title}");
        head.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(head).Properties.IsLeftButtonPressed) return;
            if (open) _app.WsState.ClosedSections.Add(sec.Id);
            else _app.WsState.ClosedSections.Remove(sec.Id);
            Refresh();
            e.Handled = true;
        };

        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(10, 0, 10, 10) };
        stack.Children.Add(head);
        if (sec.Content.Parent is Panel oldParent) oldParent.Children.Remove(sec.Content);
        if (open) stack.Children.Add(sec.Content);
        return new Border
        {
            Child = stack,
            BorderBrush = new SolidColorBrush(DnColors.Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Name = "Section" + sec.Id,
        };
    }
}

/// <summary>Editing's properties panel: the active layer, colour adjustments and the picture's size.</summary>
public sealed class PropertiesPanel : StackPanel
{
    private readonly MainWindow _app;
    private readonly TextBlock _layer = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _image = new() { Classes = { "dim", "num" }, FontSize = 12 };
    private readonly Button _alpha, _lock;
    private Document? _watched;

    public PropertiesPanel(MainWindow app)
    {
        _app = app;
        Spacing = 4;
        var s = app.Session;
        Children.Add(_layer);
        _alpha = UiKit.Button("alphalock", "Lock alpha", "Paint only where the layer already has paint", () => s.SetLayerProperty(s.Document.ActiveLayer, p => p with { AlphaLocked = !p.AlphaLocked }));
        _lock = UiKit.Button("lock", "Lock", "Protect the layer from changes", () => s.SetLayerProperty(s.Document.ActiveLayer, p => p with { Locked = !p.Locked }));
        _alpha.Height = 30;
        _lock.Height = 30;
        Children.Add(UiKit.Row(4, _alpha, _lock));
        Children.Add(UiKit.Label("Adjust colours (selection or layer)", size: 12));
        var adjust = new WrapPanel();
        void Adj(string label, string tip, Action a)
        {
            var b = UiKit.Button(null, label, tip, a);
            b.Height = 30;
            b.Background = new SolidColorBrush(DnColors.Ui2);
            b.Margin = new Thickness(0, 0, 4, 4);
            adjust.Children.Add(b);
        }
        Adj("Brightness…", "Brightness and contrast, with preview", () => _ = app.AdjustAsync(AdjustKind.BrightnessContrast));
        Adj("Hue/Sat…", "Hue, saturation and lightness, with preview", () => _ = app.AdjustAsync(AdjustKind.HueSaturation));
        Adj("Blur…", "Soften, with preview", () => _ = app.AdjustAsync(AdjustKind.Blur));
        Adj("Sharpen…", "Crisper edges, with preview", () => _ = app.AdjustAsync(AdjustKind.Sharpen));
        Adj("Grayscale", "Remove the colour", () => app.ApplyAdjustment(new Engine.Operations.Desaturate()));
        Adj("Invert", "Negative colours", () => app.ApplyAdjustment(new Engine.Operations.Invert()));
        Children.Add(adjust);
        Children.Add(UiKit.HSep());
        Children.Add(_image);
        var size = UiKit.Button(null, "Image size…", "Scale the whole picture", () => _ = app.ImageSizeAsync());
        var canvas = UiKit.Button(null, "Canvas size…", "Add or remove room around the picture", () => _ = app.CanvasSizeAsync());
        size.Height = 30;
        canvas.Height = 30;
        Children.Add(UiKit.Row(4, size, canvas));
        s.StateChanged += Refresh;
        s.DocumentReplaced += Watch;
        Watch();
    }

    private void Watch()
    {
        if (_watched is not null)
        {
            _watched.LayersChanged -= Refresh;
            _watched.ActiveLayerChanged -= Refresh;
            _watched.SizeChanged -= Refresh;
        }
        _watched = _app.Session.Document;
        _watched.LayersChanged += Refresh;
        _watched.ActiveLayerChanged += Refresh;
        _watched.SizeChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        var d = _app.Session.Document;
        var l = d.ActiveLayer;
        _layer.Text = $"{l.Name} · {Blending.DisplayName(l.Blend)} · {l.Opacity * 100:0}%";
        _alpha.Classes.Set("on", l.AlphaLocked);
        _lock.Classes.Set("on", l.Locked);
        _image.Text = $"Picture {d.Width} × {d.Height} px · {d.Layers.Count} layer{(d.Layers.Count == 1 ? "" : "s")}";
    }
}
