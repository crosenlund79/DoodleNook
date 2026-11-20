using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

using PixelRect = DoodleNook.Engine.Imaging.PixelRect;

namespace DoodleNook.App.Shell;

/// <summary>
/// The shared layers panel (the playground's ".layers"): visibility, thumbnail, name (double-click to rename),
/// opacity, lock badges; drag rows to reorder; the footer has opacity, blend mode, locks, clipping and layer
/// actions; the paper sits at the bottom. The same panel appears in Quick Sketch's pop-over and in the docks.
/// </summary>
public sealed partial class LayersPanel : StackPanel
{
    private readonly EditorSession _session;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _thumbTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Dictionary<int, (int Revision, WriteableBitmap Bitmap)> _thumbs = new();
    private readonly Dictionary<int, Image> _thumbImages = new();
    private bool _dirty;
    private readonly List<Action> _footerRefresh = new();
    private Document? _watched;
    private LayerProperties? _opacityDragStart;
    private Layer? _dragLayer;
    private double _dragStartY;
    private bool _dragging;

    public LayersPanel(EditorSession session, double maxListHeight = 300)
    {
        _session = session;
        Spacing = 6;
        _scroll = new ScrollViewer { Content = _rows, MaxHeight = maxListHeight, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Children.Add(_scroll);
        Children.Add(BuildFooter());
        _thumbTimer.Tick += (_, _) =>
        {
            _thumbTimer.Stop();
            RefreshThumbnails();
        };
        session.DocumentReplaced += Watch;
        session.StateChanged += RefreshFooter;
        Watch();
    }

    private void Watch()
    {
        if (_watched is not null)
        {
            _watched.LayersChanged -= Rebuild;
            _watched.ActiveLayerChanged -= Rebuild;
            _watched.ProjectionChanged -= OnPixels;
        }
        _watched = _session.Document;
        _watched.LayersChanged += Rebuild;
        _watched.ActiveLayerChanged += Rebuild;
        _watched.ProjectionChanged += OnPixels;
        _thumbs.Clear();
        Rebuild();
    }

    private void OnPixels(PixelRect _)
    {
        if (!_thumbTimer.IsEnabled) _thumbTimer.Start();
    }

    // ------------------------------------------------------------------ rows

    private bool Shown => TopLevel.GetTopLevel(this) is not null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_dirty) Rebuild();
        else RefreshThumbnails();
    }

    /// <summary>Updates only the thumbnails whose layer changed (cheap; runs a moment after painting).</summary>
    private void RefreshThumbnails()
    {
        if (!Shown) return;
        var doc = _session.Document;
        foreach (var layer in doc.Layers)
            if (layer.HasPixels && _thumbImages.TryGetValue(layer.Id, out var image)) image.Source = Thumbnail(doc, layer);
        RefreshMaskThumbnails();
    }

    public void Rebuild()
    {
        if (!Shown)
        {
            _dirty = true; // a hidden panel (closed pop-over, other workspace) rebuilds when it is shown again
            return;
        }
        _dirty = false;
        var doc = _session.Document;
        _rows.Children.Clear();
        _thumbImages.Clear();
        _maskImages.Clear();
        AddRows(doc, 0, 0);
        _rows.Children.Add(PaperRow(doc));
        RefreshFooter();
    }

    /// <summary>The rows of a group (or the picture), top layer first; open groups show their layers indented below them.</summary>
    private void AddRows(Document doc, int parentId, int depth)
    {
        var children = doc.ChildrenOf(parentId);
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var layer = children[i];
            _rows.Children.Add(Row(doc, layer, depth));
            if (layer.IsGroup && !layer.Properties.Collapsed && depth < 16) AddRows(doc, layer.Id, depth + 1);
        }
    }

    private Control Row(Document doc, Layer layer, int depth = 0)
    {
        bool active = ReferenceEquals(layer, doc.ActiveLayer);
        var eye = UiKit.Button(layer.Visible ? "eye" : "eyeoff", tip: layer.Visible ? $"Hide {layer.Name}" : $"Show {layer.Name}",
            click: () => _session.SetLayerProperty(layer, p => p with { Visible = !p.Visible }));
        eye.Width = 32;
        eye.Height = 32;
        eye.MinWidth = 32;
        if (!layer.Visible) eye.Opacity = 0.55;

        var thumb = LayerThumb(doc, layer, active);

        var name = new TextBlock { Text = layer.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        foreach (var badge in DesignBadges(doc, layer)) badges.Children.Add(badge);
        if (layer.ClipToBelow) badges.Children.Add(new DnIcon { Kind = "clip", Width = 14, Height = 14, Foreground = new SolidColorBrush(DnColors.Text2) });
        if (layer.AlphaLocked) badges.Children.Add(new DnIcon { Kind = "alphalock", Width = 14, Height = 14, Foreground = new SolidColorBrush(DnColors.Text2) });
        if (layer.Locked) badges.Children.Add(new DnIcon { Kind = "lock", Width = 14, Height = 14, Foreground = new SolidColorBrush(DnColors.Text2) });
        var opacity = new TextBlock
        {
            Text = layer.Blend == BlendMode.Normal ? $"{layer.Opacity * 100:0}%" : $"{Blending.DisplayName(layer.Blend)} {layer.Opacity * 100:0}%",
            Classes = { "dim", "num" },
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(badges);
        right.Children.Add(opacity);

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(eye, Dock.Left);
        DockPanel.SetDock(thumb, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(eye);
        if (Expander(layer) is { } expander)
        {
            DockPanel.SetDock(expander, Dock.Left);
            dock.Children.Add(expander);
        }
        dock.Children.Add(thumb);
        if (MaskThumb(doc, layer, active) is { } maskThumb)
        {
            DockPanel.SetDock(maskThumb, Dock.Left);
            dock.Children.Add(maskThumb);
        }
        dock.Children.Add(right);
        name.Margin = new Thickness(8, 0, 6, 0);
        dock.Children.Add(name);

        var row = new Border
        {
            Child = dock,
            MinHeight = 36,
            Padding = new Thickness(2, 2, 6, 2),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            BorderBrush = active ? new SolidColorBrush(Color.FromArgb(140, DnColors.Accent.R, DnColors.Accent.G, DnColors.Accent.B)) : Brushes.Transparent,
            Background = active ? new SolidColorBrush(Color.Parse("#2BE9A94D")) : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = layer,
            Margin = new Thickness(depth * 14, 0, 0, 0),
        };
        if (layer.Label != LayerLabel.None)
            row.BorderBrush = new SolidColorBrush(LabelColor(layer.Label));
        ToolTip.SetTip(row, RowTip(layer));

        row.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(row).Properties;
            if (props.IsRightButtonPressed)
            {
                _session.Document.SetActiveLayer(layer);
                ShowRowMenu(row, dock, name, layer);
                e.Handled = true;
                return;
            }
            if (!props.IsLeftButtonPressed) return;
            if (e.ClickCount == 2)
            {
                // Adjustment layers: double-click opens their settings (the name still renames from the menu).
                if (layer.Kind == LayerKind.Adjustment && !name.IsPointerOver) _ = (TopLevel.GetTopLevel(this) as MainWindow)?.EditAdjustmentLayerAsync(layer);
                else BeginRename(dock, name, layer);
                e.Handled = true;
                return;
            }
            _session.Document.SetActiveLayer(layer);
            _dragLayer = layer;
            _dragStartY = e.GetPosition(_rows).Y;
            _dragging = false;
            e.Pointer.Capture(row);
            e.Handled = true;
        };
        row.PointerMoved += (_, e) =>
        {
            if (_dragLayer is null || !ReferenceEquals(_dragLayer, layer)) return;
            double y = e.GetPosition(_rows).Y;
            if (Math.Abs(y - _dragStartY) > 6) _dragging = true;
            if (_dragging) row.Opacity = 0.6;
        };
        row.PointerReleased += (_, e) =>
        {
            if (_dragLayer is null) return;
            var dragged = _dragLayer;
            _dragLayer = null;
            e.Pointer.Capture(null);
            row.Opacity = 1;
            if (!_dragging) return;
            Drop(dragged, e.GetPosition(_rows).Y);
        };
        return row;
    }

    /// <summary>Right-click on a layer: rename, duplicate, merge, delete and a colour label.</summary>
    private void ShowRowMenu(Control row, DockPanel dock, TextBlock name, Layer layer)
    {
        var flyout = UiKit.PopFlyout(new Panel(), Avalonia.Controls.PlacementMode.Pointer);
        var doc = _session.Document;
        int index = doc.IndexOf(layer);
        List<MenuEntry> entries =
        [
            new(null, "Rename…", "double-click", () => BeginRename(dock, name, layer)),
            new("copy", "Duplicate", "Ctrl+J", _session.DuplicateLayer),
            new("merge", "Merge down", "Ctrl+E", _session.MergeDown, doc.MergeDownProblem(layer) is null),
            new("trash", "Delete", null, _session.DeleteLayer, doc.Layers.Count > 1),
            MenuEntry.Separator,
            .. DesignRowEntries(layer),
            new("clip", "Clip to layer below", null, () => _session.SetLayerProperty(layer, p => p with { ClipToBelow = !p.ClipToBelow }), index > 0, layer.ClipToBelow),
            new("alphalock", "Lock transparency", null, () => _session.SetLayerProperty(layer, p => p with { AlphaLocked = !p.AlphaLocked }), Checked: layer.AlphaLocked),
            new("lock", "Lock layer", null, () => _session.SetLayerProperty(layer, p => p with { Locked = !p.Locked }), Checked: layer.Locked),
            MenuEntry.Separator,
            MenuEntry.Heading("Colour label"),
        ];
        var menu = (StackPanel)Menus.Build(entries, flyout.Hide, 230);
        var labels = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 2, 8, 4) };
        foreach (var label in Enum.GetValues<LayerLabel>())
        {
            var l = label;
            var b = UiKit.Button(label == LayerLabel.None ? "close" : null, null, label == LayerLabel.None ? "No label" : $"{label} label", () =>
            {
                flyout.Hide();
                _session.SetLayerProperty(layer, p => p with { Label = l }, "Layer label");
            });
            b.Width = 26;
            b.Height = 26;
            b.MinWidth = 26;
            if (label != LayerLabel.None)
                b.Content = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(LabelColor(label)) };
            if (label == layer.Label) b.Classes.Add("on");
            labels.Children.Add(b);
        }
        menu.Children.Add(labels);
        flyout.Content = menu;
        flyout.ShowAt(row, showAtPointer: true);
    }

    private void BeginRename(DockPanel dock, TextBlock name, Layer layer)
    {
        var box = new TextBox { Text = layer.Name, MinHeight = 28, Padding = new Thickness(6, 2), Margin = new Thickness(6, 0) };
        int index = dock.Children.IndexOf(name);
        dock.Children[index] = box;
        void Done(bool keep)
        {
            if (!dock.Children.Contains(box)) return;
            var text = box.Text?.Trim();
            dock.Children[dock.Children.IndexOf(box)] = name;
            if (keep && !string.IsNullOrEmpty(text) && text != layer.Name)
                _session.SetLayerProperty(layer, p => p with { Name = text }, "Rename layer");
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Done(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Done(false); e.Handled = true; }
        };
        box.LostFocus += (_, _) => Done(true);
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    private Control PaperRow(Document doc)
    {
        bool has = doc.Paper is not null;
        var eye = UiKit.Button(doc.PaperVisible && has ? "eye" : "eyeoff", tip: "Show or hide the paper (to see transparency)",
            click: () => doc.SetPaper(doc.Paper ?? ColorRgb.White, !(doc.PaperVisible && doc.Paper is not null)));
        eye.Width = 32;
        eye.Height = 32;
        eye.MinWidth = 32;
        var swatch = UiKit.Swatch(has ? doc.Paper : null, 0, 4);
        swatch.Width = 46;
        swatch.Height = 30;
        var setPaper = UiKit.Button("paper", tip: "Make the paper your background colour (the secondary swatch)",
            click: () => doc.SetPaper(_session.Background, true));
        setPaper.Width = 28;
        setPaper.Height = 28;
        setPaper.MinWidth = 28;
        var dock = new DockPanel();
        DockPanel.SetDock(eye, Dock.Left);
        DockPanel.SetDock(swatch, Dock.Left);
        DockPanel.SetDock(setPaper, Dock.Right);
        dock.Children.Add(eye);
        dock.Children.Add(swatch);
        dock.Children.Add(setPaper);
        dock.Children.Add(new TextBlock { Text = has ? "Paper" : "Paper (none)", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) });
        return new Border { Child = dock, Padding = new Thickness(2, 2, 2, 2), Opacity = 0.9 };
    }

    private WriteableBitmap Thumbnail(Document doc, Layer layer)
    {
        if (_thumbs.TryGetValue(layer.Id, out var t) && t.Revision == layer.Revision && t.Bitmap.PixelSize.Width > 0) return t.Bitmap;
        const int tw = 92, th = 60;
        double s = Math.Min((double)tw / doc.Width, (double)th / doc.Height);
        int w = Math.Max(1, (int)(doc.Width * s)), h = Math.Max(1, (int)(doc.Height * s));
        var img = new RasterImage(w, h);
        var src = layer.Image;
        // Point-sample a small grid per thumbnail pixel (fast; good enough for a 46 px preview).
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(doc.Width - 1, (int)((x + 0.5) / s)), sy = Math.Min(doc.Height - 1, (int)((y + 0.5) / s));
                img[x, y] = src[sx, sy];
            }
        var bmp = BrushPreview.ToBitmap(img);
        _thumbs[layer.Id] = (layer.Revision, bmp);
        return bmp;
    }

    internal static Color LabelColor(LayerLabel label) => label switch
    {
        LayerLabel.Red => Color.Parse("#D9625A"),
        LayerLabel.Orange => Color.Parse("#E8954A"),
        LayerLabel.Yellow => Color.Parse("#E8C34A"),
        LayerLabel.Green => Color.Parse("#7AA66A"),
        LayerLabel.Blue => Color.Parse("#5F8FA8"),
        LayerLabel.Purple => Color.Parse("#8A6FB0"),
        _ => Colors.Transparent,
    };

    // ------------------------------------------------------------------ footer

    private Control BuildFooter()
    {
        var footer = new StackPanel { Spacing = 4 };
        var (opacity, refreshOpacity) = UiKit.Slider(null, 0, 1,
            () => _session.Document.ActiveLayer.Opacity,
            v =>
            {
                var layer = _session.Document.ActiveLayer;
                _opacityDragStart ??= layer.Properties;
                _session.Document.PreviewLayerProperties(layer, layer.Properties with { Opacity = Math.Round(v, 2) });
            },
            v => $"{v * 100:0}%", trackWidth: 120,
            finished: () =>
            {
                if (_opacityDragStart is { } from) _session.Document.CommitLayerProperties(_session.Document.ActiveLayer, from, "Layer opacity");
                _opacityDragStart = null;
            },
            tip: "Layer opacity");
        _footerRefresh.Add(refreshOpacity);

        var blend = UiKit.Button(null, "Normal", "Blend mode: how this layer mixes with the layers below");
        blend.Height = 30;
        var blendMenu = new StackPanel { Spacing = 1, Width = 170 };
        foreach (var mode in Enum.GetValues<BlendMode>())
        {
            var item = UiKit.Button(null, Blending.DisplayName(mode), null, () =>
            {
                ((Avalonia.Controls.Flyout)blend.Flyout!).Hide();
                var layer = _session.Document.ActiveLayer;
                _session.SetLayerProperty(layer, p => p with { Blend = mode }, "Blend mode");
            }, "menu");
            blendMenu.Children.Add(item);
        }
        blend.Flyout = UiKit.PopFlyout(blendMenu);
        _footerRefresh.Add(() => blend.Content = UiKit.Content(null, Blending.DisplayName(_session.Document.ActiveLayer.Blend) + " ▾"));

        var row1 = new DockPanel();
        DockPanel.SetDock(blend, Dock.Right);
        row1.Children.Add(blend);
        row1.Children.Add(opacity);
        footer.Children.Add(row1);

        Layer A() => _session.Document.ActiveLayer;
        var (lockB, r1) = UiKit.Toggle("lock", null, "Lock the layer (no edits)", () => A().Locked, v => _session.SetLayerProperty(A(), p => p with { Locked = v }));
        var (alphaB, r2) = UiKit.Toggle("alphalock", null, "Lock transparency: paint only where there already is paint", () => A().AlphaLocked, v => _session.SetLayerProperty(A(), p => p with { AlphaLocked = v }));
        var (clipB, r3) = UiKit.Toggle("clip", null, "Clip to the layer below (colour inside the lines)", () => A().ClipToBelow, v => _session.SetLayerProperty(A(), p => p with { ClipToBelow = v }));
        _footerRefresh.Add(r1);
        _footerRefresh.Add(r2);
        _footerRefresh.Add(r3);
        var add = UiKit.Button("plus", tip: "New layer (Ctrl+Shift+N)", click: () => _session.AddLayer());
        var dup = UiKit.Button("copy", tip: "Duplicate layer (Ctrl+J)", click: _session.DuplicateLayer);
        var merge = UiKit.Button("merge", tip: "Merge down (Ctrl+E)", click: _session.MergeDown);
        var up = UiKit.Button("chevu", tip: "Move layer up", click: () => _session.MoveLayer(+1));
        var down = UiKit.Button("chevd", tip: "Move layer down", click: () => _session.MoveLayer(-1));
        var del = UiKit.Button("trash", tip: "Delete layer", click: _session.DeleteLayer);
        var (group, mask, adjust) = DesignFooterButtons();
        foreach (var b in new[] { lockB, alphaB, clipB, add, group, mask, adjust, dup, merge, up, down, del })
        {
            b.Width = 25;
            b.Height = 28;
            b.MinWidth = 25;
        }
        var row2 = new WrapPanel();
        foreach (var b in new Control[] { lockB, alphaB, clipB, UiKit.Sep(), add, group, mask, adjust, dup, merge, up, down, del }) row2.Children.Add(b);
        footer.Children.Add(row2);
        return footer;
    }

    private void RefreshFooter()
    {
        foreach (var r in _footerRefresh) r();
    }
}
