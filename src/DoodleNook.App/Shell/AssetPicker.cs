using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DoodleNook.App.Controls;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;

namespace DoodleNook.App.Shell;

public enum PickerKind
{
    Stamps,
    Patterns,
}

/// <summary>
/// The stamp or pattern picker (opened from the stamp tool's or the pattern fill's chip): thumbnails by collection,
/// favourites first, a tiled preview for patterns, and a right-click menu for everything else (favourite, rename,
/// collection, use as brush, export, delete). New items come from the selection or a PNG.
/// </summary>
public sealed class AssetPicker : StackPanel
{
    private static readonly Dictionary<string, WriteableBitmap> Thumbs = new();
    private readonly MainWindow _app;
    private readonly PickerKind _kind;
    private readonly Func<string?> _current;
    private readonly Action<string> _choose;
    private readonly WrapPanel _collections = new();
    private readonly WrapPanel _grid = new() { ItemWidth = 72, ItemHeight = 72 };
    private readonly Image _tiled = new() { Width = 252, Height = 96, Stretch = Stretch.None };
    private readonly TextBlock _name = new() { Classes = { "dim" }, FontSize = 12 };
    private string _filter = "";

    public AssetPicker(MainWindow app, PickerKind kind, Func<string?> current, Action<string> choose)
    {
        _app = app;
        _kind = kind;
        _current = current;
        _choose = choose;
        Width = 300;
        Spacing = 8;
        Name = kind == PickerKind.Stamps ? "StampPicker" : "PatternPicker";
        Children.Add(UiKit.Heading(kind == PickerKind.Stamps ? "Stamps" : "Patterns"));
        Children.Add(_collections);
        if (kind == PickerKind.Patterns)
        {
            var preview = new Border { Child = _tiled, CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(preview, "The pattern repeated: check that the edges meet without a seam");
            Children.Add(preview);
        }
        Children.Add(_name);
        Children.Add(new ScrollViewer { Content = _grid, MaxHeight = 236, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var fromSelection = UiKit.Button("plus", "From selection", kind == PickerKind.Stamps
            ? "Save the selected artwork (or the whole layer) as a new stamp"
            : "Save the selected artwork as a repeating tile", () => _ = app.SaveSelectionAsAssetAsync(kind));
        var import = UiKit.Button("open", "PNG…", "Import a picture", () => _ = app.ImportAssetPictureAsync(kind));
        var more = UiKit.Button("dots", null, "Library: import or export packs, manage collections", () => _ = app.OpenLibraryAsync(kind));
        foreach (var b in new[] { fromSelection, import, more }) b.Height = 30;
        Children.Add(UiKit.Row(4, fromSelection, import, more));
        app.Session.LibraryChanged += Refresh;
        Refresh();
    }

    private IEnumerable<(string Id, string Name, string Collection, bool BuiltIn)> Items => _kind == PickerKind.Stamps
        ? _app.Session.Library.Stamps.Select(s => (s.Id, s.Name, s.Collection, s.BuiltIn))
        : _app.Session.Library.Patterns.Select(p => (p.Id, p.Name, p.Collection, p.BuiltIn));

    private bool IsFavourite(string id) => _kind == PickerKind.Stamps ? _app.Session.Library.IsFavoriteStamp(id) : _app.Session.Library.IsFavoritePattern(id);

    private RasterImage? ImageOf(string id) => _kind == PickerKind.Stamps ? _app.Session.Library.StampImage(id) : _app.Session.Library.PatternImage(id);

    public void Refresh()
    {
        var items = Items.ToList();
        // Collections: All, favourites (if any), then each collection.
        _collections.Children.Clear();
        var names = new List<string> { "" };
        if (items.Any(i => IsFavourite(i.Id))) names.Add("★");
        names.AddRange(items.Select(i => i.Collection).Distinct());
        if (!names.Contains(_filter)) _filter = "";
        foreach (var n in names)
        {
            var b = UiKit.Button(null, n.Length == 0 ? "All" : n);
            b.Height = 26;
            b.Margin = new Thickness(0, 0, 4, 4);
            b.Classes.Set("on", n == _filter);
            var value = n;
            b.Click += (_, _) =>
            {
                _filter = value;
                Refresh();
            };
            _collections.Children.Add(b);
        }
        _grid.Children.Clear();
        string? current = _current();
        foreach (var item in items.Where(i => _filter.Length == 0 || _filter == "★" && IsFavourite(i.Id) || i.Collection == _filter)
                     .OrderByDescending(i => IsFavourite(i.Id)))
        {
            var thumb = Thumb(item.Id);
            var content = new Panel();
            content.Children.Add(new Border { Background = UiKit.CheckerBrush(), CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = new Image { Source = thumb, Stretch = Stretch.Uniform, Margin = new Thickness(4) } });
            if (IsFavourite(item.Id))
                content.Children.Add(new DnIcon { Kind = "star", Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2), Foreground = new SolidColorBrush(DnColors.Accent) });
            var b = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                Width = 66, Height = 66, Padding = new Thickness(2), Content = content,
                BorderBrush = new SolidColorBrush(item.Id == current ? DnColors.Accent : Colors.Transparent), BorderThickness = new Thickness(2),
                Name = "Asset_" + item.Name.Replace(' ', '_'),
            };
            ToolTip.SetTip(b, $"{item.Name} · {item.Collection}{(item.BuiltIn ? " · built in" : "")}\nRight-click for more");
            var id = item.Id;
            b.Click += (_, _) =>
            {
                _choose(id);
                Refresh();
            };
            var menu = UiKit.PopFlyout(new Panel(), PlacementMode.Pointer);
            menu.Opening += (_, _) => menu.Content = Menus.Build(_app.AssetMenu(_kind, id), menu.Hide, 240);
            b.ContextFlyout = menu;
            _grid.Children.Add(b);
        }
        var cur = items.FirstOrDefault(i => i.Id == current);
        _name.Text = cur.Id is null ? "Nothing chosen yet" : $"{cur.Name} · {cur.Collection}";
        if (_kind == PickerKind.Patterns && current is not null && ImageOf(current) is { } tile) _tiled.Source = TiledPreview(tile, 252, 96);
    }

    private WriteableBitmap? Thumb(string id)
    {
        string key = _kind + ":" + id;
        if (Thumbs.TryGetValue(key, out var t)) return t;
        if (ImageOf(id) is not { } img) return null;
        var small = _kind == PickerKind.Patterns ? Tiled(img, Math.Min(img.Width * 2, 120), Math.Min(img.Height * 2, 120)) : Fit(img, 120);
        return Thumbs[key] = BrushPreview.ToBitmap(small);
    }

    /// <summary>Forget cached thumbnails of an asset (after it changed).</summary>
    public static void Forget(string id)
    {
        foreach (var k in Thumbs.Keys.Where(k => k.EndsWith(":" + id, StringComparison.Ordinal)).ToList()) Thumbs.Remove(k);
    }

    public static WriteableBitmap TiledPreview(RasterImage tile, int w, int h) => BrushPreview.ToBitmap(Tiled(tile, w, h));

    private static RasterImage Tiled(RasterImage tile, int w, int h)
    {
        // Small tiles are shown bigger (whole-number zoom) so their pixels are visible.
        int zoom = Math.Max(1, Math.Min(4, 32 / Math.Max(tile.Width, tile.Height)));
        var img = new RasterImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = tile[x / zoom % tile.Width, y / zoom % tile.Height];
        return img;
    }

    public static RasterImage Fit(RasterImage img, int max)
    {
        if (img.Width <= max && img.Height <= max) return img;
        double s = (double)max / Math.Max(img.Width, img.Height);
        return Engine.Operations.Resampler.Resize(img, Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s)), Engine.Operations.Interpolation.Bilinear);
    }
}
