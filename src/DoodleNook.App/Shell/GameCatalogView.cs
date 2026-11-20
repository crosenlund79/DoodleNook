using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.App.Shell;

/// <summary>
/// Game Edit Mode's catalog: the game folder's PNGs with thumbnails and progress, over the canvas while it is shown.
/// Click an image to edit it; Mark complete / Mark in progress on each tile. Thumbnails load in the background.
/// </summary>
public sealed class GameCatalogView : Border
{
    private const double TileWidth = 176, ThumbWidth = 160, ThumbHeight = 108;

    private static readonly Color NotStartedColour = DnColors.Text2, InProgressColour = Color.Parse("#E9A94D"), CompleteColour = Color.Parse("#7AA66A");

    private readonly MainWindow _app;
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _percent = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Name = "GameProgressText" };
    private readonly TextBlock _counts = new() { Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Name = "GameCounts" };
    private readonly Border _barFill = new() { Background = new SolidColorBrush(CompleteColour), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _bar;
    private readonly WrapPanel _tiles = new() { Name = "GameTiles" };
    private readonly TextBlock _empty = new() { Classes = { "dim" }, Margin = new Thickness(4, 20), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBox _search = new() { Watermark = "Find a file", Width = 220, Name = "GameSearch" };
    private readonly Button _backToEditing;
    private readonly Dictionary<string, (DateTime Written, Bitmap Thumb)> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action> _refresh = new();
    private readonly List<Action> _tileRefresh = new();
    private GameProject? _project;
    private GameImageStatus? _filter;
    private CancellationTokenSource? _loading;

    public GameCatalogView(MainWindow app)
    {
        _app = app;
        Name = "GameCatalog";
        Background = new SolidColorBrush(DnColors.Surround);
        Padding = new Thickness(20, 16, 12, 0);
        ClipToBounds = true;

        var other = UiKit.Button("open", "Open another folder…", "Choose another game graphics folder", () => _ = app.ChooseGameFolderAsync());
        var rescan = UiKit.Button(null, "Rescan", "Look for added or removed PNG files", () => app.ShowCatalog());
        _backToEditing = UiKit.Button("brush", "Back to editing", "Return to the image you were editing (Esc)", app.HideCatalog);
        _backToEditing.Name = "GameBackToEditing";
        var leave = UiKit.Button("close", "Leave Game Edit Mode", "Close the catalog; Ctrl+S saves pictures as usual again", app.LeaveGameEditMode);
        foreach (var b in new[] { other, rescan, _backToEditing, leave }) b.Height = 32;
        var buttons = UiKit.Row(4, _backToEditing, other, rescan, leave);
        var header = new DockPanel { Margin = new Thickness(0, 0, 8, 8) };
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(UiKit.Row(10, new DnIcon { Kind = "image", Width = 22, Height = 22 }, _title));

        _bar = new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(DnColors.Ui3),
            Child = _barFill,
            Name = "GameProgressBar",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _bar.SizeChanged += (_, _) => UpdateBar();
        var progress = new DockPanel { Margin = new Thickness(0, 0, 8, 6) };
        DockPanel.SetDock(_percent, Dock.Right);
        _percent.Margin = new Thickness(12, 0, 0, 0);
        progress.Children.Add(_percent);
        progress.Children.Add(_bar);

        var (filter, refreshFilter) = UiKit.Segmented(
            [((GameImageStatus?)null, null, "All", "Every image"), (GameImageStatus.NotStarted, null, "Not started", "Not yet saved in Game Edit Mode"),
             (GameImageStatus.InProgress, null, "In progress", "Saved, not yet marked complete"), (GameImageStatus.Complete, null, "Complete", "Marked complete")],
            () => _filter, v =>
            {
                _filter = v;
                ApplyFilter();
            }, compact: true);
        _refresh.Add(refreshFilter);
        _search.TextChanged += (_, _) => ApplyFilter();
        var tools = UiKit.Row(12, filter, _search);
        tools.Margin = new Thickness(0, 4, 0, 10);

        var top = new StackPanel { Children = { header, progress, _counts, tools } };
        _counts.Margin = new Thickness(0, 0, 0, 4);
        var scroll = new ScrollViewer
        {
            Content = new StackPanel { Children = { _tiles, _empty } },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(scroll);
        Child = dock;
        IsVisible = false;
    }

    /// <summary>Shows the catalog of a game folder (after a scan).</summary>
    public void Show(GameProject project)
    {
        bool rebuild = !ReferenceEquals(project, _project) || _tiles.Children.Count != project.Images.Count;
        _project = project;
        IsVisible = true;
        if (rebuild) Rebuild();
        else Refresh();
    }

    private void Rebuild()
    {
        _tiles.Children.Clear();
        _tileRefresh.Clear();
        if (_project is null) return;
        foreach (var image in _project.Images) _tiles.Children.Add(Tile(image));
        Refresh();
        LoadThumbnails();
    }

    /// <summary>Counts, progress bar, the tiles' statuses and the filter (cheap; after any status change).</summary>
    public void Refresh()
    {
        if (_project is not { } p) return;
        var progress = p.Progress;
        _title.Text = $"{p.Name} — {progress.Total} image{(progress.Total == 1 ? "" : "s")}";
        ToolTip.SetTip(_title, p.Root);
        _percent.Text = $"Completion {progress.Percent:0.#}%";
        _counts.Text = $"Not started: {progress.NotStarted}   ·   In progress: {progress.InProgress}   ·   Complete: {progress.Complete}";
        _backToEditing.IsVisible = _app.EditingGameImage;
        UpdateBar();
        foreach (var r in _refresh) r();
        foreach (var r in _tileRefresh) r();
        ApplyFilter();
    }

    private void UpdateBar()
    {
        var progress = _project?.Progress ?? default;
        _barFill.Width = progress.Total == 0 ? 0 : _bar.Bounds.Width * progress.Complete / progress.Total;
    }

    private void ApplyFilter()
    {
        string query = _search.Text?.Trim() ?? "";
        int shown = 0;
        foreach (var child in _tiles.Children)
        {
            if (child.Tag is not GameImage image) continue;
            bool show = (_filter is null || image.Status == _filter)
                        && (query.Length == 0 || image.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase));
            child.IsVisible = show;
            if (show) shown++;
        }
        _empty.IsVisible = shown == 0;
        _empty.Text = _project is { Images.Count: 0 } ? "There are no PNG images in this folder or its subfolders." : "No image matches.";
    }

    private Control Tile(GameImage image)
    {
        var thumb = new Image { Width = ThumbWidth, Height = ThumbHeight, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(thumb, BitmapInterpolationMode.None);
        lock (_thumbs)
            if (_thumbs.TryGetValue(image.PngPath, out var cached)) thumb.Source = cached.Thumb;
        var thumbFrame = new Border
        {
            Width = ThumbWidth,
            Height = ThumbHeight,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Background = UiKit.CheckerBrush(),
            Child = thumb,
        };
        var name = new TextBlock { Text = image.FileName, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = ThumbWidth };
        var folder = new TextBlock
        {
            Text = image.Folder.Length == 0 ? "(top folder)" : image.Folder, Classes = { "dim" }, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = ThumbWidth,
        };
        ToolTip.SetTip(name, image.RelativePath);
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
        var status = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var mark = UiKit.Button(null, "", null);
        mark.Height = 26;
        mark.FontSize = 12;
        mark.Padding = new Thickness(8, 0);
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        mark.Margin = new Thickness(-4, 2, 0, 0);
        mark.Click += (_, _) =>
        {
            if (_project is not { } p) return;
            try
            {
                if (image.Status == GameImageStatus.Complete) p.MarkInProgress(image);
                else p.MarkComplete(image);
            }
            catch (Exception ex)
            {
                _app.ShowToast($"Couldn't save the progress: {ex.Message}");
            }
            _app.RefreshGameStatus();
        };
        var statusRow = UiKit.Row(5, dot, status);
        statusRow.Margin = new Thickness(0, 4, 0, 0);

        var tile = new Border
        {
            Width = TileWidth,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(DnColors.Ui),
            BorderThickness = new Thickness(2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = image,
            Name = "GameTile_" + image.RelativePath,
            Child = new StackPanel { Spacing = 2, Children = { thumbFrame, name, folder, statusRow, mark } },
        };
        ToolTip.SetTip(thumbFrame, $"{image.RelativePath} · click to edit");
        tile.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed || mark.IsPointerOver) return;
            e.Handled = true;
            _ = _app.OpenGameImageAsync(image);
        };
        void Update()
        {
            var colour = image.Status switch
            {
                GameImageStatus.Complete => CompleteColour,
                GameImageStatus.InProgress => InProgressColour,
                _ => NotStartedColour,
            };
            dot.Fill = new SolidColorBrush(colour);
            status.Text = MainWindow.StatusText(image.Status);
            mark.Content = image.Status == GameImageStatus.Complete ? "Mark in progress" : "Mark complete";
            ToolTip.SetTip(mark, image.Status == GameImageStatus.Complete ? "Not finished after all: back to In progress" : "Mark this image as finished");
            bool current = ReferenceEquals(_app.GameImage, image) && _app.EditingGameImage;
            tile.BorderBrush = current ? new SolidColorBrush(DnColors.Accent) : Brushes.Transparent;
            lock (_thumbs)
                if (_thumbs.TryGetValue(image.PngPath, out var t)) thumb.Source = t.Thumb;
        }
        _tileRefresh.Add(Update);
        Update();
        return tile;
    }

    // ================================================================== thumbnails

    /// <summary>Decodes thumbnails of the current PNGs in the background, one at a time (the window stays responsive).
    /// A thumbnail is made again when its PNG has changed.</summary>
    public void LoadThumbnails()
    {
        _loading?.Cancel();
        if (_project is null) return;
        var cts = new CancellationTokenSource();
        _loading = cts;
        var images = _project.Images.ToList();
        _ = Task.Run(() =>
        {
            foreach (var image in images)
            {
                if (cts.IsCancellationRequested) return;
                DateTime written;
                try
                {
                    written = File.GetLastWriteTimeUtc(image.PngPath);
                }
                catch (Exception)
                {
                    continue;
                }
                bool have;
                lock (_thumbs) have = _thumbs.TryGetValue(image.PngPath, out var t) && t.Written == written;
                if (have) continue;
                RasterImage? small = null;
                try
                {
                    small = Shrink(ImageFiles.Decode(image.PngPath));
                }
                catch (Exception)
                {
                    // An unreadable PNG keeps an empty thumbnail; opening it says why.
                }
                if (small is null) continue;
                Dispatcher.UIThread.Post(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    lock (_thumbs) _thumbs[image.PngPath] = (written, BrushPreview.ToBitmap(small));
                    foreach (var r in _tileRefresh) r();
                });
            }
        }, cts.Token);
    }

    /// <summary>Thumbnail size; small pixel art is enlarged by whole numbers so it stays crisp.</summary>
    private static RasterImage Shrink(RasterImage image)
    {
        double s = Math.Min(ThumbWidth * 2 / image.Width, ThumbHeight * 2 / image.Height);
        if (s >= 1)
        {
            int k = Math.Max(1, (int)Math.Floor(Math.Min(s, 16)));
            return k == 1 ? image : Engine.Files.AnimationExport.ScaleNearest(image, k);
        }
        int w = Math.Max(1, (int)Math.Round(image.Width * s)), h = Math.Max(1, (int)Math.Round(image.Height * s));
        return Resampler.Resize(image, w, h, Interpolation.Bilinear);
    }
}
