using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Shell;

/// <summary>
/// Mission 008 in the layers panel: groups (indented, with a fold arrow), masks (a second thumbnail; the one you paint on
/// has the accent ring), text, shape and adjustment layers, drag a layer onto a group to put it inside.
/// </summary>
public sealed partial class LayersPanel
{
    private readonly Dictionary<int, (int Revision, WriteableBitmap Bitmap)> _maskThumbs = new();
    private readonly Dictionary<int, Image> _maskImages = new();

    private MainWindow? App => TopLevel.GetTopLevel(this) as MainWindow;

    private static readonly IBrush Ring = new SolidColorBrush(DnColors.Accent);
    private static readonly IBrush NoRing = new SolidColorBrush(Color.FromArgb(64, 0, 0, 0));

    /// <summary>The layer's own thumbnail: its pixels, or a folder / adjustment symbol. The accent ring shows that painting
    /// goes to the layer (rather than its mask).</summary>
    private Control LayerThumb(Document doc, Layer layer, bool active)
    {
        Control child = layer.HasPixels
            ? _thumbImages[layer.Id] = new Image { Source = Thumbnail(doc, layer), Stretch = Stretch.Uniform }
            : new DnIcon { Kind = layer.IsGroup ? "folder" : "adjust", Width = 20, Height = 20, Foreground = new SolidColorBrush(DnColors.Text2) };
        bool target = active && layer.HasMask && !doc.EditingMask;
        var thumb = new Border
        {
            Width = 46,
            Height = 30,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Background = layer.HasPixels ? UiKit.CheckerBrush() : new SolidColorBrush(DnColors.Ui),
            BorderBrush = target ? Ring : NoRing,
            BorderThickness = new Thickness(target ? 2 : 1),
            Child = child,
            Name = "LayerThumb_" + layer.Id,
        };
        if (layer.HasMask)
        {
            ToolTip.SetTip(thumb, "Click to paint on the layer");
            thumb.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(thumb).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
                doc.SetActiveLayer(layer);
                _session.SetEditingMask(false);
            };
        }
        return thumb;
    }

    /// <summary>The mask thumbnail (white shows, black hides). Click: paint on the mask; Shift+click: turn the mask off/on.</summary>
    private Control? MaskThumb(Document doc, Layer layer, bool active)
    {
        if (layer.Mask is null) return null;
        bool target = active && doc.EditingMask;
        var image = _maskImages[layer.Id] = new Image { Source = MaskThumbnail(doc, layer), Stretch = Stretch.Uniform };
        var grid = new Grid();
        grid.Children.Add(image);
        if (!layer.MaskEnabled)
            grid.Children.Add(new DnIcon { Kind = "close", Width = 22, Height = 22, Foreground = new SolidColorBrush(Color.Parse("#D9625A")) });
        var thumb = new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Background = Brushes.White,
            BorderBrush = target ? Ring : NoRing,
            BorderThickness = new Thickness(target ? 2 : 1),
            Child = grid,
            Name = "MaskThumb_" + layer.Id,
        };
        ToolTip.SetTip(thumb, layer.MaskEnabled
            ? "Mask: click to paint on it (black hides, white shows, the eraser shows again) · Shift+click turns it off"
            : "Mask (off): Shift+click turns it on");
        thumb.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(thumb).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
            doc.SetActiveLayer(layer);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _session.ToggleMaskEnabled();
            else _session.SetEditingMask(true);
            e.Handled = true;
        };
        return thumb;
    }

    private WriteableBitmap MaskThumbnail(Document doc, Layer layer)
    {
        if (_maskThumbs.TryGetValue(layer.Id, out var t) && t.Revision == layer.MaskRevision && t.Bitmap.PixelSize.Width > 0) return t.Bitmap;
        const int tw = 60, th = 60;
        double s = Math.Min((double)tw / doc.Width, (double)th / doc.Height);
        int w = Math.Max(1, (int)(doc.Width * s)), h = Math.Max(1, (int)(doc.Height * s));
        var img = new RasterImage(w, h);
        var mask = layer.Mask!;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(doc.Width - 1, (int)((x + 0.5) / s)), sy = Math.Min(doc.Height - 1, (int)((y + 0.5) / s));
                byte v = Layer.MaskValue(mask[sx, sy]);
                img[x, y] = Pixel.Pack(255, v, v, v);
            }
        var bmp = BrushPreview.ToBitmap(img);
        _maskThumbs[layer.Id] = (layer.MaskRevision, bmp);
        return bmp;
    }

    /// <summary>The fold arrow of a group.</summary>
    private Control? Expander(Layer layer)
    {
        if (!layer.IsGroup) return null;
        var b = UiKit.Button(layer.Properties.Collapsed ? "chevr" : "chevd", tip: layer.Properties.Collapsed ? "Show the group's layers" : "Fold the group",
            click: () => _session.Document.PreviewLayerProperties(layer, layer.Properties with { Collapsed = !layer.Properties.Collapsed }));
        b.Width = b.MinWidth = 22;
        b.Height = 30;
        b.Name = "Fold_" + layer.Id;
        return b;
    }

    /// <summary>Small symbols after the name: text, shape, adjustment, a missing font, painting on the mask.</summary>
    private IEnumerable<Control> DesignBadges(Document doc, Layer layer)
    {
        DnIcon Icon(string kind, string tip, IBrush? brush = null)
        {
            var i = new DnIcon { Kind = kind, Width = 14, Height = 14, Foreground = brush ?? new SolidColorBrush(DnColors.Text2) };
            ToolTip.SetTip(i, tip);
            return i;
        }
        if (layer.SubstituteFont is { } used && layer.Content is TextContent t)
            yield return Icon("info", $"The font “{t.FontFamily}” isn't installed: shown in {used} until it is", new SolidColorBrush(Color.Parse("#E8C34A")));
        if (ReferenceEquals(layer, doc.ActiveLayer) && doc.EditingMask) yield return Icon("mask", "Painting on the mask", Ring);
        switch (layer.Kind)
        {
            case LayerKind.Text:
                yield return Icon("text", "Text layer: edit it with the Text tool or double-click its text on the canvas");
                break;
            case LayerKind.Shape:
                yield return Icon("shape", "Editable shape: change it with the Shapes or Move tool");
                break;
            case LayerKind.Adjustment:
                yield return Icon("adjust", "Adjustment layer: changes everything below it · double-click to change it");
                break;
        }
    }

    private static string RowTip(Layer layer) => layer.Kind switch
    {
        LayerKind.Group => $"{layer.Name} · a group: drag layers onto it to put them inside · ▸ folds it · double-click to rename",
        LayerKind.Text => $"{layer.Name} · text · drag to reorder · double-click to rename",
        LayerKind.Shape => $"{layer.Name} · editable shape · drag to reorder · double-click to rename",
        LayerKind.Adjustment => $"{layer.Name} · adjustment layer · double-click its symbol to change it · drag to reorder",
        _ => $"{layer.Name} · click to paint on it · drag to reorder · double-click to rename",
    };

    /// <summary>A dragged row dropped at a y position: above or below the row there, or into a group (its middle).</summary>
    private void Drop(Layer dragged, double y)
    {
        var doc = _session.Document;
        double top = 0;
        foreach (var child in _rows.Children)
        {
            double h = child.Bounds.Height + _rows.Spacing;
            if (child is Border { Tag: Layer target } && y < top + h)
            {
                if (ReferenceEquals(target, dragged)) return;
                double f = (y - top) / Math.Max(1, h);
                if (target.IsGroup && f is > 0.25 and < 0.75)
                {
                    if (!doc.MoveIntoGroup(dragged, target)) App?.ShowToast("A group can't go inside itself");
                    return;
                }
                doc.MoveNextTo(dragged, target, above: f < 0.5);
                return;
            }
            top += h;
        }
        // Below the last row: the bottom of the picture.
        var bottom = doc.ChildrenOf(0).FirstOrDefault();
        if (bottom is not null && !ReferenceEquals(bottom, dragged)) doc.MoveNextTo(dragged, bottom, above: false);
    }

    private IEnumerable<MenuEntry> DesignRowEntries(Layer layer)
    {
        var s = _session;
        var doc = s.Document;
        var app = App;
        var list = new List<MenuEntry>();
        if (layer.IsGroup)
        {
            list.Add(new(null, layer.Properties.Collapsed ? "Unfold group" : "Fold group", null,
                () => doc.PreviewLayerProperties(layer, layer.Properties with { Collapsed = !layer.Properties.Collapsed })));
            list.Add(new(null, "Ungroup", "Ctrl+Shift+G", s.Ungroup));
        }
        else list.Add(new("folder", "Group layer", "Ctrl+G", s.GroupActiveLayer));
        if (layer.ParentId != 0) list.Add(new(null, "Move out of group", null, s.MoveOutOfGroup));
        if (layer.Kind == LayerKind.Text && app is not null) list.Add(new("text", "Edit text", null, app.EditActiveText));
        if (layer.Kind == LayerKind.Adjustment && app is not null) list.Add(new("props", "Edit adjustment…", null, () => _ = app.EditAdjustmentLayerAsync(layer)));
        if (layer.IsObject) list.Add(new("image", "Convert to pixels", null, s.ConvertToPixels));
        list.Add(MenuEntry.Separator);
        if (!layer.HasMask)
        {
            list.Add(new("mask", "Add mask", null, () => s.AddMask()));
            list.Add(new("mask", "Mask from selection", null, () => s.AddMask(fromSelection: true), doc.Selection is not null));
        }
        else
        {
            list.Add(new("mask", doc.EditingMask ? "Paint on the layer" : "Paint on the mask", null, () => s.SetEditingMask(!doc.EditingMask)));
            list.Add(new(null, "Use mask", "Shift+click", s.ToggleMaskEnabled, Checked: layer.MaskEnabled));
            list.Add(new("invert", "Invert mask", null, s.InvertMask));
            list.Add(new("check", "Apply mask", null, s.ApplyMask, layer.IsPaintable));
            list.Add(new("trash", "Delete mask", null, s.DeleteMask));
        }
        list.Add(MenuEntry.Separator);
        return list;
    }

    /// <summary>Footer buttons: new group, add a mask, new adjustment layer (▾).</summary>
    private (Button Group, Button Mask, Button Adjust) DesignFooterButtons()
    {
        var group = UiKit.Button("folder", tip: "New group (Ctrl+G puts the chosen layer in a group)", click: _session.NewGroup);
        group.Name = "NewGroupButton";
        var mask = UiKit.Button("mask", tip: "Add a mask to the layer (from the selection if there is one) · black hides, white shows", click: () =>
            _session.AddMask(fromSelection: _session.Document.Selection is not null && !_session.Document.ActiveLayer.HasMask));
        mask.Name = "AddMaskButton";
        var adjust = Menus.MenuButton(new DnIcon { Kind = "adjust" }, () =>
        [
            MenuEntry.Heading("New adjustment layer"),
            new(null, "Brightness / Contrast", null, () => _ = App?.NewAdjustmentLayerAsync(AdjustKind.BrightnessContrast)),
            new(null, "Hue / Saturation", null, () => _ = App?.NewAdjustmentLayerAsync(AdjustKind.HueSaturation)),
            new(null, "Levels", null, () => _ = App?.NewAdjustmentLayerAsync(AdjustKind.Levels)),
            new(null, "Colour balance", null, () => _ = App?.NewAdjustmentLayerAsync(AdjustKind.ColourBalance)),
        ], "New adjustment layer: changes the layers below without changing their pixels", 220);
        adjust.Name = "AdjustmentButton";
        return (group, mask, adjust);
    }

    /// <summary>Refreshes the mask thumbnails whose mask changed.</summary>
    private void RefreshMaskThumbnails()
    {
        var doc = _session.Document;
        foreach (var layer in doc.Layers)
            if (layer.HasMask && _maskImages.TryGetValue(layer.Id, out var image)) image.Source = MaskThumbnail(doc, layer);
    }
}
