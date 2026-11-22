using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Storybook;
using DnCanvas = Avalonia.Controls.Canvas;

namespace DoodleNook.App.Shell;

/// <summary>
/// Mission 015 — Storybook Studio: a storybook's pages over the canvas (like Game Edit Mode's catalog). A strip of page
/// thumbnails on the left, the page in the middle, and on the right what the selected panel needs: its size and resolution,
/// its drawing and how it sits. Double-click a panel to draw in it with the normal editor; "Back to the storybook" returns
/// here and the panel shows the new drawing. Every change is saved straight away (safely) and can be undone.
/// </summary>
public sealed class StorybookView : Border
{
    private const double StripWidth = 150, InspectorWidth = 270;

    private readonly MainWindow _app;
    private readonly StorybookThumbnails _thumbs;
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Name = "StorybookTitle" };
    private readonly Button _settings;
    private readonly Button _undo, _redo, _layout;
    private readonly StackPanel _strip = new() { Spacing = 8, Name = "StorybookPages" };
    private readonly DnCanvas _page = new() { Name = "StorybookPage" };
    private readonly Border _pageFrame;
    private readonly Panel _pageArea = new() { Name = "StorybookPageArea" };
    private readonly TextBlock _pageLabel = new() { Classes = { "dim", "num" }, VerticalAlignment = VerticalAlignment.Center, Name = "StorybookPageLabel" };
    private readonly StackPanel _inspector = new() { Spacing = 8, Name = "StorybookInspector" };
    private readonly List<string> _undoStack = new(), _redoStack = new();
    private StorybookProject? _book;
    private int _pageIndex;
    private int? _slot;

    public StorybookView(MainWindow app)
    {
        _app = app;
        Name = "StorybookView";
        Background = new SolidColorBrush(DnColors.Surround);
        Padding = new Thickness(16, 12, 12, 10);
        ClipToBounds = true;
        IsVisible = false;
        _thumbs = new StorybookThumbnails(app, () => Dispatcher.UIThread.Post(RefreshPictures, DispatcherPriority.Background));

        _settings = UiKit.Button(null, "A4 · 300 dpi", "Page size and resolution: every panel uses the same pixels per centimetre", () => _ = app.StorybookSettingsAsync());
        _settings.Name = "StorybookSettings";
        _undo = UiKit.Button("undo", tip: "Undo the last storybook change (Ctrl+Z)", click: Undo);
        _redo = UiKit.Button("redo", tip: "Redo (Ctrl+Shift+Z)", click: Redo);
        _undo.Name = "StorybookUndo";
        _layout = Menus.MenuButton(UiKit.Content("grid", "Layout"), LayoutMenu, "This page's panel layout", 270);
        _layout.Name = "StorybookLayout";
        var export = Menus.MenuButton(UiKit.Content("export", "Export"), ExportMenu, "Pages as PNG (or PDF) at the project's resolution", 260);
        export.Name = "StorybookExport";
        var more = Menus.MenuButton(UiKit.Content("dots", null), MoreMenu, "More", 240);
        var back = UiKit.Button("brush", "Back to drawing", "Return to the drawing you had open (Esc)", app.HideStorybook);
        back.Name = "StorybookBack";
        foreach (var b in new[] { _settings, _undo, _redo, _layout, export, more, back }) b.Height = 32;
        var right = UiKit.Row(4, _undo, _redo, UiKit.Sep(), _layout, export, more, back);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(UiKit.Row(10, new DnIcon { Kind = "book", Width = 22, Height = 22 }, _title, _settings));

        // Left: the pages.
        var add = Menus.MenuButton(UiKit.Content("plus", "Add page"), () => StoryLayouts.All.Select(l => new MenuEntry(null, l.Name, null, () => AddPage(l.Id))),
            "A new page after this one, with the layout you choose", 260);
        add.Name = "StorybookAddPage";
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        var stripScroll = new ScrollViewer { Content = _strip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var left = new DockPanel { Width = StripWidth, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(add, Dock.Bottom);
        add.Margin = new Thickness(0, 8, 8, 0);
        left.Children.Add(add);
        left.Children.Add(stripScroll);

        // Middle: the page, with previous / next.
        _pageFrame = new Border
        {
            Child = _page,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 24, Color = Color.FromArgb(110, 0, 0, 0) }),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _pageArea.Children.Add(_pageFrame);
        _pageArea.SizeChanged += (_, _) => DrawPage();
        var prev = UiKit.Button("chevl", "Previous", "Previous page (Page Up)", () => GoTo(_pageIndex - 1));
        var next = UiKit.Button("chevr", "Next", "Next page (Page Down)", () => GoTo(_pageIndex + 1));
        prev.Name = "StorybookPrevious";
        next.Name = "StorybookNext";
        var nav = UiKit.Row(10, prev, _pageLabel, next);
        nav.HorizontalAlignment = HorizontalAlignment.Center;
        nav.Margin = new Thickness(0, 8, 0, 0);
        var middle = new DockPanel();
        DockPanel.SetDock(nav, Dock.Bottom);
        middle.Children.Add(nav);
        middle.Children.Add(_pageArea);

        // Right: the selected panel.
        var inspectorScroll = new ScrollViewer
        {
            Content = _inspector, Width = InspectorWidth, Margin = new Thickness(12, 0, 0, 0),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var body = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(inspectorScroll, Dock.Right);
        body.Children.Add(left);
        body.Children.Add(inspectorScroll);
        body.Children.Add(middle);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(body);
        Child = root;
        Focusable = true;
        KeyDown += OnKey;
    }

    public StorybookProject? Project => _book;
    public int PageIndex => _pageIndex;
    public int? SelectedSlot => _slot;
    internal StorybookThumbnails Thumbnails => _thumbs;
    public bool CanUndo => _undoStack.Count > 0;

    private StoryPage? Page => _book is { } b && _pageIndex < b.Pages.Count ? b.Pages[_pageIndex] : null;

    /// <summary>Shows a storybook (from its first page, or the page and panel given).</summary>
    public void Open(StorybookProject book, int page = 0, int? slot = null)
    {
        if (!ReferenceEquals(book, _book))
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
        _book = book;
        _pageIndex = Math.Clamp(page, 0, book.Pages.Count - 1);
        _slot = slot;
        Rebuild();
    }

    /// <summary>Everything again (after a change, or coming back from the editor: drawings may have changed).</summary>
    public void Rebuild()
    {
        if (_book is not { } book) return;
        _title.Text = book.Name;
        _settings.Content = UiKit.Content(null, $"{PaperName(book)} · {book.Dpi} dpi");
        _undo.IsEnabled = _undoStack.Count > 0;
        _redo.IsEnabled = _redoStack.Count > 0;
        _pageIndex = Math.Clamp(_pageIndex, 0, book.Pages.Count - 1);
        if (_slot is { } s && (Page is null || s >= Page.Panels.Count)) _slot = null;
        FillStrip();
        DrawPage();
        FillInspector();
    }

    /// <summary>Only the pictures (a thumbnail arrived).</summary>
    private void RefreshPictures()
    {
        if (!IsVisible || _book is null) return;
        FillStrip();
        DrawPage();
        FillInspector();
    }

    public static string PaperName(StorybookProject b)
    {
        foreach (var (name, w, h) in PaperSizes)
        {
            if (Math.Abs(b.PageWidthMm - w) < 0.5 && Math.Abs(b.PageHeightMm - h) < 0.5) return name;
            if (Math.Abs(b.PageWidthMm - h) < 0.5 && Math.Abs(b.PageHeightMm - w) < 0.5) return name + " landscape";
        }
        return $"{b.PageWidthMm / 10:0.#} × {b.PageHeightMm / 10:0.#} cm";
    }

    public static readonly (string Name, double Width, double Height)[] PaperSizes =
        [("A4", 210, 297), ("A5", 148, 210), ("A3", 297, 420), ("Letter", 215.9, 279.4), ("Square 20 cm", 200, 200)];

    // ================================================================== changes (saved at once, undoable)

    /// <summary>Changes the storybook: remembered for undo, then saved safely. Returns false if it couldn't be saved.</summary>
    public bool Edit(Action<StorybookProject> change)
    {
        if (_book is not { } book) return false;
        var before = book.ToJson();
        change(book);
        if (!Save()) return false;
        _undoStack.Add(before);
        if (_undoStack.Count > 100) _undoStack.RemoveAt(0);
        _redoStack.Clear();
        Rebuild();
        return true;
    }

    private bool Save()
    {
        try
        {
            _book!.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.ShowToast($"Couldn't save the storybook: {ex.Message}");
            return false;
        }
    }

    public void Undo() => Restore(_undoStack, _redoStack);

    public void Redo() => Restore(_redoStack, _undoStack);

    private void Restore(List<string> from, List<string> to)
    {
        if (_book is not { } book || from.Count == 0) return;
        var json = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(book.ToJson());
        _book = StorybookProject.Parse(json, book.FilePath!);
        _app.StorybookReplaced(_book);
        Save();
        Rebuild();
    }

    // ================================================================== pages

    public void GoTo(int page)
    {
        if (_book is null) return;
        int p = Math.Clamp(page, 0, _book.Pages.Count - 1);
        if (p == _pageIndex) return;
        _pageIndex = p;
        _slot = null;
        Rebuild();
    }

    public void AddPage(string layout)
    {
        Edit(b => b.AddPage(layout, _pageIndex + 1));
        GoTo(_pageIndex + 1);
    }

    private IEnumerable<MenuEntry> PageMenu(int index)
    {
        int n = _book?.Pages.Count ?? 0;
        yield return new("copy", "Duplicate page", null, () =>
        {
            Edit(b => b.DuplicatePage(index));
            GoTo(index + 1);
            _app.ShowToast("Duplicated: the new page shows the same drawings. To change one panel alone, use “Make a copy for this panel”.");
        });
        yield return new("chevu", "Move page earlier", null, () =>
        {
            Edit(b => b.MovePage(index, index - 1));
            _pageIndex = Math.Max(0, index - 1);
            Rebuild();
        }, index > 0);
        yield return new("chevd", "Move page later", null, () =>
        {
            Edit(b => b.MovePage(index, index + 1));
            _pageIndex = Math.Min(n - 1, index + 1);
            Rebuild();
        }, index < n - 1);
        yield return MenuEntry.Separator;
        yield return new("trash", "Delete page…", null, () => _ = DeletePageAsync(index), n > 1);
    }

    /// <summary>Deletes a page after asking (its drawings stay in their files; the page can be undone).</summary>
    public async Task DeletePageAsync(int index)
    {
        if (_book is null) return;
        var r = await _app.Dialogs.ShowAsync("Delete page?", DialogHost.Text(
                $"Page {index + 1} is removed from the storybook. Its drawings stay where they are (in “{Path.GetFileName(_book.ArtFolder)}”), and Undo brings the page back."),
            [("cancel", "Cancel", false), ("delete", "Delete page", true)], "delete", "cancel", 420);
        if (r != "delete") return;
        Edit(b => b.DeletePage(index));
        _pageIndex = Math.Min(index, _book.Pages.Count - 1);
        Rebuild();
    }

    private IEnumerable<MenuEntry> LayoutMenu()
    {
        if (Page is not { } page) yield break;
        foreach (var l in StoryLayouts.All)
        {
            var id = l.Id;
            yield return new(null, $"{l.Name} ({l.Count})", null, () => SetLayout(id), Checked: page.Layout == id);
        }
    }

    public void SetLayout(string layout)
    {
        if (Page is not { } page || page.Layout == layout) return;
        int before = page.Panels.Count(p => p.Artwork is not null);
        Edit(b => b.SetLayout(b.Pages[_pageIndex], layout));
        if (Page is { Unplaced.Count: > 0 } now && now.Unplaced.Any(u => u.Artwork is not null))
            _app.ShowToast($"{now.Unplaced.Count(u => u.Artwork is not null)} drawing(s) are waiting beside the page: select a panel, then click one to place it.");
        _ = before;
    }

    // ================================================================== panels

    public void Select(int? slot)
    {
        _slot = slot;
        DrawPage();
        FillInspector();
    }

    private IEnumerable<MenuEntry> ExportMenu()
    {
        yield return new("export", "This page as PNG…", null, () => _ = _app.ExportStorybookAsync(StorybookExport.ThisPage));
        yield return new("export", "All pages as PNG files…", null, () => _ = _app.ExportStorybookAsync(StorybookExport.AllPng));
        yield return new("export", "All pages as one PDF…", null, () => _ = _app.ExportStorybookAsync(StorybookExport.Pdf));
    }

    private IEnumerable<MenuEntry> MoreMenu()
    {
        yield return new("settings", "Page size and resolution…", null, () => _ = _app.StorybookSettingsAsync());
        yield return new("folder", "Show the drawings folder", null, () => _app.ShowToast(_book?.ArtFolder ?? ""), _book?.ArtFolder is not null);
        yield return MenuEntry.Separator;
        yield return new("close", "Close the storybook", null, _app.CloseStorybook);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.PageDown) GoTo(_pageIndex + 1);
        else if (e.Key == Key.PageUp) GoTo(_pageIndex - 1);
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); }
        else if (e.Key == Key.Escape) _app.HideStorybook();
        else return;
        e.Handled = true;
    }

    // ================================================================== drawing the page

    private void DrawPage()
    {
        _page.Children.Clear();
        if (_book is not { } book || Page is not { } page) return;
        _pageLabel.Text = $"Page {_pageIndex + 1} of {book.Pages.Count}";
        double availW = Math.Max(100, _pageArea.Bounds.Width - 16), availH = Math.Max(100, _pageArea.Bounds.Height - 16);
        double scale = Math.Min(availW / book.PageWidthMm, availH / book.PageHeightMm); // view pixels per millimetre
        _page.Width = book.PageWidthMm * scale;
        _page.Height = book.PageHeightMm * scale;
        _pageFrame.Background = UiKit.Brush(book.PaperColour);
        var rects = book.PanelRects(page);
        for (int i = 0; i < rects.Count; i++)
        {
            var panel = PanelControl(book, page, i, rects[i], scale, large: true);
            int slot = i;
            panel.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed) return;
                Select(slot);
                if (e.ClickCount >= 2) _ = _app.OpenStorybookPanelAsync(_pageIndex, slot);
                e.Handled = true;
            };
            panel.Name = $"StorybookPanel{i + 1}";
            DnCanvas.SetLeft(panel, rects[i].X * scale);
            DnCanvas.SetTop(panel, rects[i].Y * scale);
            _page.Children.Add(panel);
        }
        // Clicking the page outside the panels clears the selection.
        _page.Background = Brushes.Transparent;
        _page.PointerPressed -= OnPageBackground;
        _page.PointerPressed += OnPageBackground;
    }

    private void OnPageBackground(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        Select(null);
    }

    /// <summary>One panel as it will print: its drawing placed as chosen, the border over it (as in the export), and (on the
    /// big page) the selection.</summary>
    private Panel PanelControl(StorybookProject book, StoryPage page, int i, RectMm r, double scale, bool large)
    {
        double w = r.Width * scale, h = r.Height * scale;
        var holder = new DnCanvas { Width = w, Height = h, ClipToBounds = true };
        var p = page.Panels[i];
        var path = book.Resolve(p.Artwork);
        bool selected = large && _slot == i;
        if (path is not null)
        {
            var info = _thumbs.Get(path, large);
            if (info is { Missing: false, Bitmap: { } bmp })
            {
                var (x, y, iw, ih) = Placement(book, w, h, info.Width, info.Height, p.Fit, scale);
                var img = new Image { Source = bmp, Width = iw, Height = ih, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
                DnCanvas.SetLeft(img, x);
                DnCanvas.SetTop(img, y);
                holder.Children.Add(img);
            }
            else if (large && info is { Missing: true })
                holder.Children.Add(Centred(w, h, "Missing drawing", "Select the panel to find it again", Color.Parse("#C0504A")));
        }
        else if (large)
            holder.Children.Add(Centred(w, h, "Empty", "Double-click to draw here", Color.FromArgb(150, 90, 80, 72)));
        double border = Math.Max(1, book.BorderMm * scale);
        var frame = new Border
        {
            Width = w,
            Height = h,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x18)),
            BorderThickness = new Thickness(book.BorderMm > 0 ? border : large ? 0.5 : 0),
            IsHitTestVisible = false,
        };
        var root = new Panel { Width = w, Height = h, Background = Brushes.Transparent, Cursor = large ? new Cursor(StandardCursorType.Hand) : null };
        root.Children.Add(holder);
        root.Children.Add(frame);
        if (selected)
            root.Children.Add(new Border
            {
                Width = w + 6, Height = h + 6, Margin = new Thickness(-3), BorderBrush = new SolidColorBrush(DnColors.Accent), BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(2), IsHitTestVisible = false, Name = "StorybookSelection",
            });
        return root;
    }

    /// <summary>The same placement as the export (<see cref="StorybookRenderer.Placement"/>), in view units.</summary>
    private static (double X, double Y, double W, double H) Placement(StorybookProject book, double w, double h, int artW, int artH, PanelFit fit, double scale)
    {
        double s = fit switch
        {
            PanelFit.Fit => Math.Min(w / artW, h / artH),
            PanelFit.Fill => Math.Max(w / artW, h / artH),
            _ => 25.4 / book.Dpi * scale, // one drawing pixel = one project pixel
        };
        return ((w - artW * s) / 2, (h - artH * s) / 2, artW * s, artH * s);
    }

    private static Control Centred(double w, double h, string title, string hint, Color colour)
    {
        var text = new StackPanel
        {
            Width = w,
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = title, Foreground = new SolidColorBrush(colour), FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = hint, Foreground = new SolidColorBrush(colour), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            },
        };
        DnCanvas.SetTop(text, Math.Max(0, h / 2 - 18));
        return text;
    }

    // ================================================================== the page strip

    private void FillStrip()
    {
        _strip.Children.Clear();
        if (_book is not { } book) return;
        double tileW = StripWidth - 18;
        double scale = tileW / book.PageWidthMm;
        for (int i = 0; i < book.Pages.Count; i++)
        {
            int index = i;
            var page = book.Pages[i];
            var mini = new DnCanvas { Width = book.PageWidthMm * scale, Height = book.PageHeightMm * scale, Background = UiKit.Brush(book.PaperColour), ClipToBounds = true };
            var rects = book.PanelRects(page);
            for (int k = 0; k < rects.Count; k++)
            {
                var c = PanelControl(book, page, k, rects[k], scale, large: false);
                DnCanvas.SetLeft(c, rects[k].X * scale);
                DnCanvas.SetTop(c, rects[k].Y * scale);
                mini.Children.Add(c);
            }
            bool current = i == _pageIndex;
            var tile = new Border
            {
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        mini,
                        new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture) + (page.Unplaced.Any(u => u.Artwork is not null) ? " ·  drawings waiting" : ""),
                            FontSize = 11, Classes = { "dim" }, HorizontalAlignment = HorizontalAlignment.Center },
                    },
                },
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = current ? new SolidColorBrush(DnColors.Accent) : Brushes.Transparent,
                Background = current ? new SolidColorBrush(DnColors.Ui2) : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Name = $"StorybookPageTile{i + 1}",
                ContextMenu = null,
            };
            ToolTip.SetTip(tile, $"Page {i + 1} · right-click for duplicate, move and delete");
            tile.PointerPressed += (_, e) =>
            {
                var props = e.GetCurrentPoint(tile).Properties;
                if (props.IsRightButtonPressed)
                {
                    GoTo(index);
                    Menus.ShowAt(tile, PageMenu(index).ToList(), 240);
                }
                else GoTo(index);
                e.Handled = true;
            };
            _strip.Children.Add(tile);
        }
    }

    // ================================================================== the inspector

    private void FillInspector()
    {
        _inspector.Children.Clear();
        if (_book is not { } book || Page is not { } page) return;
        if (_slot is not { } slot)
        {
            _inspector.Children.Add(Heading($"Page {_pageIndex + 1}"));
            _inspector.Children.Add(Dim($"{StoryLayouts.Get(page.Layout).Name}. {PaperName(book)} at {book.Dpi} dpi: every panel has {book.Dpi / 2.54:0} pixels per centimetre."));
            _inspector.Children.Add(Dim("Click a panel to select it. Double-click a panel to draw in it."));
            AddUnplaced(book, page);
            return;
        }
        var rect = book.PanelRects(page)[slot];
        var (pw, ph) = book.SuggestedCanvas(rect);
        var panel = page.Panels[slot];
        _inspector.Children.Add(Heading($"Panel {slot + 1}"));
        _inspector.Children.Add(Dim($"{rect.Width / 10:0.0} × {rect.Height / 10:0.0} cm · {pw} × {ph} px at {book.Dpi} dpi", "StorybookPanelSize"));
        var path = book.Resolve(panel.Artwork);
        if (path is null)
        {
            var create = UiKit.Button("plus", "New drawing", $"A blank {pw} × {ph} drawing for this panel, opened in the editor", () => _ = _app.NewStorybookDrawingAsync(_pageIndex, slot));
            create.Name = "StorybookNewDrawing";
            var link = UiKit.Button("open", "Link a drawing…", "Show one of your drawings here (.dnk, or a picture: it becomes an editable copy)", () => _ = _app.LinkStorybookDrawingAsync(_pageIndex, slot));
            link.Name = "StorybookLinkDrawing";
            _inspector.Children.Add(Stack(create, link));
        }
        else
        {
            var info = _thumbs.Get(path, true);
            _inspector.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "StorybookArtworkName" });
            int shared = book.Usage().TryGetValue(path, out var uses) ? uses.Count : 1;
            if (shared > 1) _inspector.Children.Add(Dim($"Shared: shown in {shared} panels. Editing it changes all of them.", "StorybookShared"));
            if (info is { Missing: true })
            {
                _inspector.Children.Add(Warn($"The file isn't there any more ({panel.Artwork}). The panel is kept: find the drawing again, or link another one."));
                var relink = UiKit.Button("open", "Find it…", "Choose where the drawing is now", () => _ = _app.LinkStorybookDrawingAsync(_pageIndex, slot));
                relink.Name = "StorybookRelink";
                _inspector.Children.Add(Stack(relink, UiKit.Button("close", "Remove from panel", "The panel becomes empty (the file isn't touched)", () => Edit(b => b.Pages[_pageIndex].Panels[slot].Artwork = null))));
            }
            else
            {
                if (info is { Width: > 0 } i)
                {
                    var check = book.Check(rect, i.Width, i.Height, panel.Fit);
                    _inspector.Children.Add(Dim($"{i.Width} × {i.Height} px", "StorybookArtworkSize"));
                    if (check.BelowTarget)
                    {
                        _inspector.Children.Add(Warn($"Prints at {check.EffectiveDpi:0} dpi here, below the project's {book.Dpi} dpi: it will look softer than the other panels."));
                        var bigger = UiKit.Button("expand", $"Make a {pw} × {ph} copy", "A resized copy for this panel (the original stays as it is). Enlarging doesn't add detail: it gives the right size to paint the detail in.",
                            () => _ = _app.ResizedStorybookCopyAsync(_pageIndex, slot));
                        bigger.Name = "StorybookResizedCopy";
                        _inspector.Children.Add(bigger);
                    }
                    else _inspector.Children.Add(Dim($"Prints at {Math.Min(check.EffectiveDpi, 9999):0} dpi here ✓", "StorybookResolution"));
                }
                var open = UiKit.Button("brush", "Draw in it", "Open the drawing in the editor (double-click the panel)", () => _ = _app.OpenStorybookPanelAsync(_pageIndex, slot));
                open.Name = "StorybookOpenDrawing";
                open.Classes.Add("solid");
                _inspector.Children.Add(open);
                Action refreshFit = () => { };
                var (fit, rf) = UiKit.Segmented(
                    [(PanelFit.Fit, null, "Fit", "The whole drawing, as large as fits"),
                     (PanelFit.Fill, null, "Fill", "Fill the panel; what sticks out is cropped"),
                     (PanelFit.Actual, null, "Actual size", "At the project's resolution, centred")],
                    () => panel.Fit, v => Edit(b => b.Pages[_pageIndex].Panels[slot].Fit = v), compact: true);
                refreshFit = rf;
                _inspector.Children.Add(fit);
                var copy = UiKit.Button("copy", "Make a copy for this panel", "Change this panel without changing the others that share the drawing",
                    () => _ = _app.CopyStorybookDrawingAsync(_pageIndex, slot));
                copy.Name = "StorybookCopyForPanel";
                var change = UiKit.Button("open", "Link another…", "Show a different drawing in this panel", () => _ = _app.LinkStorybookDrawingAsync(_pageIndex, slot));
                var remove = UiKit.Button("close", "Remove from panel", "The panel becomes empty (the drawing's file isn't touched)", () => Edit(b => b.Pages[_pageIndex].Panels[slot].Artwork = null));
                remove.Name = "StorybookRemove";
                _inspector.Children.Add(Stack(copy, change, remove));
            }
        }
        // A short note: what happens in this panel (storyboards).
        var note = new TextBox { Text = panel.Note ?? "", PlaceholderText = "A note for this panel (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, Name = "StorybookNote" };
        note.LostFocus += (_, _) =>
        {
            var text = string.IsNullOrWhiteSpace(note.Text) ? null : note.Text.Trim();
            if (text != (Page?.Panels[slot].Note)) Edit(b => b.Pages[_pageIndex].Panels[slot].Note = text);
        };
        _inspector.Children.Add(note);
        AddUnplaced(book, page);
    }

    /// <summary>Drawings waiting beside the page (after a layout with fewer panels): click one to put it in the selected panel.</summary>
    private void AddUnplaced(StorybookProject book, StoryPage page)
    {
        var waiting = page.Unplaced.Select((u, i) => (u, i)).Where(x => x.u.Artwork is not null).ToList();
        if (waiting.Count == 0) return;
        _inspector.Children.Add(Heading("Beside the page"));
        _inspector.Children.Add(Dim(_slot is null ? "Kept when the layout changed. Select a panel, then click a drawing to put it there."
            : $"Click one to put it in panel {_slot + 1} (what is there now moves here)."));
        var wrap = new WrapPanel { Name = "StorybookUnplaced" };
        foreach (var (u, index) in waiting)
        {
            var path = book.Resolve(u.Artwork)!;
            var info = _thumbs.Get(path, false);
            var tile = UiKit.Button(null, null, Path.GetFileName(path));
            tile.Content = new Image { Source = info?.Bitmap, Width = 64, Height = 48, Stretch = Stretch.Uniform };
            tile.Height = 58;
            tile.Margin = new Thickness(0, 0, 4, 4);
            tile.IsEnabled = _slot is not null;
            int idx = index;
            tile.Click += (_, _) =>
            {
                if (_slot is { } s) Edit(b => b.Place(b.Pages[_pageIndex], idx, s));
            };
            wrap.Children.Add(tile);
        }
        _inspector.Children.Add(wrap);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    private static TextBlock Dim(string text, string? name = null) => new() { Text = text, Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Name = name };

    private static Border Warn(string text) => new()
    {
        Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#F2C14E")) },
        Background = new SolidColorBrush(Color.FromArgb(40, 242, 193, 78)),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(8, 6),
        Name = "StorybookWarning",
    };

    private static StackPanel Stack(params Control[] items)
    {
        var s = new StackPanel { Spacing = 4 };
        foreach (var c in items)
        {
            if (c is Button b) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            s.Children.Add(c);
        }
        return s;
    }
}

/// <summary>What Export does: this page, every page as PNG files, or every page in one PDF.</summary>
public enum StorybookExport
{
    ThisPage,
    AllPng,
    Pdf,
}

/// <summary>
/// Pictures of a storybook's drawings for the screen, made in the background: first the small preview stored in each
/// .dnk (no layers are read), then for the page being shown a sharper one. Kept by file time, so a drawing saved in
/// the editor shows its new version; a drawing open in the editor with unsaved changes shows those.
/// </summary>
public sealed class StorybookThumbnails(MainWindow app, Action ready)
{
    public sealed record Info(int Width, int Height, Bitmap? Bitmap, bool Missing, bool Large);

    private sealed class Entry
    {
        public DateTime Stamp;
        public int Width, Height;
        public Bitmap? Small, Large;
        public bool Missing, LoadingSmall, LoadingLarge;
    }

    private readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private (string Path, long State, Bitmap Bitmap, int W, int H)? _live;

    /// <summary>How many pictures are kept (Help › Diagnostics, tests).</summary>
    public int Count => _cache.Count;

    public void Clear()
    {
        _cache.Clear();
        _live = null;
    }

    public Info? Get(string path, bool large)
    {
        // The drawing open in the editor, with changes not saved yet: shown as it is now.
        var doc = app.Session.Document;
        if (app.Session.FilePath is { } open && string.Equals(Path.GetFullPath(open), path, StringComparison.OrdinalIgnoreCase) && doc.IsDirty)
        {
            long state = doc.History.CurrentStateId;
            if (_live is not { } live || live.Path != path || live.State != state)
            {
                var flat = StorybookRenderer.Flatten(doc);
                _live = (path, state, BrushPreview.ToBitmap(Shrink(flat, 900)), doc.Width, doc.Height);
            }
            return new Info(_live.Value.W, _live.Value.H, _live.Value.Bitmap, false, true);
        }
        DateTime stamp;
        try
        {
            if (!File.Exists(path)) return new Info(0, 0, null, true, false);
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Info(0, 0, null, true, false);
        }
        if (!_cache.TryGetValue(path, out var e) || e.Stamp != stamp)
        {
            e = new Entry { Stamp = stamp };
            _cache[path] = e;
        }
        if (e.Small is null && !e.LoadingSmall && !e.Missing) LoadSmall(path, e);
        if (large && e.Large is null && !e.LoadingLarge && !e.Missing) LoadLarge(path, e);
        return new Info(e.Width, e.Height, large ? e.Large ?? e.Small : e.Small ?? e.Large, e.Missing, e.Large is not null);
    }

    private void LoadSmall(string path, Entry e)
    {
        e.LoadingSmall = true;
        _ = Task.Run(() =>
        {
            try
            {
                if (path.EndsWith(DnkFile.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    var (w, h, thumb) = DnkFile.ReadSummary(path);
                    return (w, h, thumb, ok: true);
                }
                var img = StorybookRenderer.LoadArtwork(path);
                return img is null ? (0, 0, (RasterImage?)null, ok: false) : (img.Width, img.Height, Shrink(img, 256), ok: true);
            }
            catch (Exception)
            {
                return (0, 0, (RasterImage?)null, ok: false);
            }
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            e.LoadingSmall = false;
            var (w, h, thumb, ok) = t.Result;
            e.Missing = !ok;
            e.Width = w;
            e.Height = h;
            if (thumb is not null) e.Small = BrushPreview.ToBitmap(thumb);
            ready();
        }));
    }

    private void LoadLarge(string path, Entry e)
    {
        e.LoadingLarge = true;
        _ = Task.Run<(int Width, int Height, RasterImage Picture)?>(() =>
        {
            var img = StorybookRenderer.LoadArtwork(path);
            return img is null ? null : (img.Width, img.Height, Shrink(img, 900));
        }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            e.LoadingLarge = false;
            if (t.Result is { } r)
            {
                e.Width = r.Width;
                e.Height = r.Height;
                e.Large = BrushPreview.ToBitmap(r.Picture);
            }
            // Keep memory in check: only the sharper pictures of recent pages.
            var larges = _cache.Where(kv => kv.Value.Large is not null).ToList();
            if (larges.Count > 40)
                foreach (var kv in larges.Take(larges.Count - 40)) kv.Value.Large = null;
            ready();
        }));
    }

    private static RasterImage Shrink(RasterImage img, int max)
    {
        int m = Math.Max(img.Width, img.Height);
        if (m <= max) return img;
        double s = (double)max / m;
        return Engine.Operations.Resampler.Resize(img, Math.Max(1, (int)Math.Round(img.Width * s)), Math.Max(1, (int)Math.Round(img.Height * s)), Engine.Operations.Interpolation.Bilinear);
    }
}
