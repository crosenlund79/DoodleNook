using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Samples;
using DoodleNook.Engine.Storybook;
using SkiaSharp;

namespace DoodleNook.App;

/// <summary>
/// Mission 015 — Storybook Studio: File › New storybook… / Open storybook…, the storybook view over the canvas, and the
/// trip from a panel to the normal editor and back. A storybook links ordinary .dnk drawings; it never paints itself.
/// </summary>
public partial class MainWindow
{
    private static readonly FilePickerFileType StorybookType = new("DoodleNook storybook (.donkbook)") { Patterns = ["*" + StorybookProject.Extension] };

    private StorybookView? _storybookView;
    private Border? _storybookChip;
    private TextBlock? _storybookChipText;
    private (int Page, int Slot, string Path)? _storybookEditing;

    /// <summary>The storybook open now, or null.</summary>
    public StorybookProject? Storybook { get; private set; }

    internal StorybookView? StorybookView => _storybookView;

    public bool StorybookShown => _storybookView is { IsVisible: true };

    // ================================================================== new, open, close

    public async Task NewStorybookAsync()
    {
        var choice = await AskStorybookPageAsync("New storybook", null);
        if (choice is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the new storybook (its drawings go in a folder beside it)",
            SuggestedFileName = "My story" + StorybookProject.Extension,
            DefaultExtension = StorybookProject.Extension.TrimStart('.'),
            FileTypeChoices = [StorybookType],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await CreateStorybookAsync(path, choice.Value.Width, choice.Value.Height, choice.Value.Dpi, choice.Value.Layout);
    }

    /// <summary>Creates and opens a storybook at <paramref name="path"/> (tests call this directly).</summary>
    public async Task<bool> CreateStorybookAsync(string path, double widthMm, double heightMm, int dpi, string layout)
    {
        var book = StorybookProject.Create(widthMm, heightMm, dpi, layout);
        try
        {
            book.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't create the storybook", ex.Message);
            return false;
        }
        OpenStorybook(book);
        return true;
    }

    public async Task OpenStorybookAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a storybook",
            AllowMultiple = false,
            FileTypeFilter = [StorybookType, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await OpenStorybookFromAsync(path);
    }

    public async Task<bool> OpenStorybookFromAsync(string path)
    {
        StorybookProject book;
        try
        {
            book = StorybookProject.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StorybookFormatException)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the storybook", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
        OpenStorybook(book);
        int missing = book.Usage().Keys.Count(p => !File.Exists(p));
        if (missing > 0) ShowToast($"{missing} drawing{(missing == 1 ? " is" : "s are")} missing: their panels are kept; select one to find it again.");
        return true;
    }

    /// <summary>Help › Samples › A little walk: the sample storybook in Documents/DoodleNook/Samples (made once, then reopened).</summary>
    public async Task OpenSampleStorybookAsync(string? folder = null)
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DoodleNook", "Samples", SampleStorybook.Name);
        string path;
        try
        {
            path = await Task.Run(() => SampleStorybook.Create(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't make the sample storybook", ex.Message);
            return;
        }
        await OpenStorybookFromAsync(path);
    }

    private void OpenStorybook(StorybookProject book)
    {
        Storybook = book;
        _storybookEditing = null;
        _storybookView?.Thumbnails.Clear();
        ShowStorybook();
        _storybookView!.Open(book);
    }

    /// <summary>After an undo the view holds a new project object.</summary>
    internal void StorybookReplaced(StorybookProject book) => Storybook = book;

    public void ShowStorybook()
    {
        if (Storybook is null) return;
        if (_storybookView is null)
        {
            _storybookView = new StorybookView(this);
            CanvasHost.Children.Add(_storybookView);
            InitStorybookChip();
        }
        if (CatalogShown) HideCatalog();
        Animator.Pause();
        Session.EndStroke();
        Session.CommitTransform();
        _storybookView.IsVisible = true;
        if (!ReferenceEquals(_storybookView.Project, Storybook)) _storybookView.Open(Storybook);
        else _storybookView.Rebuild();
        _storybookView.Focus();
        RefreshStorybookChip();
    }

    public void HideStorybook()
    {
        if (_storybookView is null) return;
        _storybookView.IsVisible = false;
        RefreshStorybookChip();
        Canvas.Focus();
    }

    public void CloseStorybook()
    {
        HideStorybook();
        Storybook = null;
        _storybookEditing = null;
        _storybookView?.Thumbnails.Clear();
        RefreshStorybookChip();
    }

    // ================================================================== a panel's drawing in the editor

    private void InitStorybookChip()
    {
        _storybookChipText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis };
        var back = UiKit.Button("book", "Back to the storybook", "See the page with this drawing", ShowStorybook);
        back.Height = 26;
        back.FontSize = 12;
        back.Name = "StorybookBackToBook";
        _storybookChip = new Border
        {
            Classes = { "float" },
            Name = "StorybookChip",
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(10, 3, 4, 3),
            CornerRadius = new CornerRadius(999),
            Child = UiKit.Row(6, _storybookChipText, back),
        };
        CanvasHost.Children.Add(_storybookChip);
        Session.DocumentReplaced += RefreshStorybookChip;
        Session.StateChanged += RefreshStorybookChip;
    }

    /// <summary>While a storybook drawing is open in the editor: a chip back to its page.</summary>
    private void RefreshStorybookChip()
    {
        if (_storybookChip is null || _storybookChipText is null) return;
        bool on = Storybook is not null && !StorybookShown && _storybookEditing is { } ed && Session.FilePath is { } open
                  && string.Equals(Path.GetFullPath(open), ed.Path, StringComparison.OrdinalIgnoreCase);
        _storybookChip.IsVisible = on;
        if (on && _storybookEditing is { } e)
            _storybookChipText.Text = $"{Storybook!.Name} · page {e.Page + 1}, panel {e.Slot + 1}{(Session.Document.IsDirty ? " · not saved yet" : "")}";
    }

    /// <summary>Double-click a panel: its drawing in the normal editor (a new one for an empty panel).</summary>
    public async Task OpenStorybookPanelAsync(int page, int slot)
    {
        if (Storybook is not { } book) return;
        var path = book.Resolve(book.Pages[page].Panels[slot].Artwork);
        if (path is null)
        {
            await NewStorybookDrawingAsync(page, slot);
            return;
        }
        if (!File.Exists(path))
        {
            ShowToast("This drawing is missing: “Find it…” links it again.");
            return;
        }
        await OpenStorybookDrawingAsync(page, slot, path);
    }

    private async Task<bool> OpenStorybookDrawingAsync(int page, int slot, string path)
    {
        bool already = Session.FilePath is { } open && string.Equals(Path.GetFullPath(open), path, StringComparison.OrdinalIgnoreCase);
        if (!already)
        {
            if (!await ConfirmDiscardAsync("opening the panel's drawing")) return false;
            if (!await OpenPathAsync(path)) return false;
            if (Workspace.Kind is WorkspaceKind.Animation or WorkspaceKind.PixelArt && !Session.Document.IsAnimated) SetWorkspace(WorkspaceKind.Painting);
        }
        _storybookEditing = (page, slot, path);
        HideStorybook();
        Canvas.FitToView();
        ShowToast("Draw as usual, save with Ctrl+S, then “Back to the storybook”.");
        return true;
    }

    /// <summary>A blank drawing for an empty panel, at the panel's size at the project's resolution.</summary>
    public async Task NewStorybookDrawingAsync(int page, int slot)
    {
        if (Storybook is not { } book || _storybookView is null) return;
        var rect = book.PanelRects(book.Pages[page])[slot];
        var (w, h) = book.SuggestedCanvas(rect);
        var name = new TextBox { Text = $"page {page + 1:00} panel {slot + 1}", Width = 240, Name = "StorybookDrawingName" };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(DialogHost.Text($"Panel {slot + 1} is {rect.Width / 10:0.0} × {rect.Height / 10:0.0} cm. At the storybook's {book.Dpi} dpi that is {w} × {h} pixels: " +
                                          "the same pixels per centimetre as every other panel."));
        body.Children.Add(UiKit.Row(8, new TextBlock { Text = "Name", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Width = 50 }, name));
        body.Children.Add(DialogHost.Text($"It is saved in “{Path.GetFileName(book.ArtFolder)}” as an ordinary DoodleNook drawing and opens in the editor."));
        var r = await _dialogs.ShowAsync("New drawing for the panel", body, [("cancel", "Cancel", false), ("create", $"Create {w} × {h}", true)], "create", "cancel", 460);
        if (r != "create") return;
        if (!await ConfirmDiscardAsync("drawing in the panel")) return;
        string path;
        try
        {
            path = book.NewArtworkPath(string.IsNullOrWhiteSpace(name.Text) ? $"page {page + 1:00} panel {slot + 1}" : name.Text);
            DnkFile.Save(new Document(w, h, ColorRgb.White), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Couldn't create the drawing", ex.Message);
            return;
        }
        _storybookView.Edit(b => b.Pages[page].Panels[slot].Artwork = b.Relate(path));
        await OpenStorybookDrawingAsync(page, slot, path);
    }

    /// <summary>Links a drawing to a panel: a .dnk as it is, or a picture as a new editable copy; too small, it offers a resized copy.</summary>
    public async Task LinkStorybookDrawingAsync(int page, int slot)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "A drawing for the panel (.dnk), or a picture to make an editable copy of",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Drawings and pictures") { Patterns = ["*.dnk", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await LinkStorybookFileAsync(page, slot, path);
    }

    /// <summary>The link itself (tests call this with a file). <paramref name="resizeIfSmall"/>: answer the low-resolution question.</summary>
    public async Task<bool> LinkStorybookFileAsync(int page, int slot, string path, bool? resizeIfSmall = null)
    {
        if (Storybook is not { } book || _storybookView is null) return false;
        string link = path;
        int artW, artH;
        try
        {
            if (!ImageFiles.IsProject(path))
            {
                // A picture becomes an editable drawing in the art folder; the picture itself isn't changed.
                var doc = await Task.Run(() => ImageFiles.Open(path));
                link = book.NewArtworkPath(Path.GetFileNameWithoutExtension(path));
                await Task.Run(() => DnkFile.Save(doc, link));
                ShowToast($"Made an editable copy of {Path.GetFileName(path)} in “{Path.GetFileName(book.ArtFolder)}” (the picture isn't changed).");
            }
            (artW, artH, _) = await Task.Run(() => DnkFile.ReadSummary(link));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DnkFormatException or InvalidDataException or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Couldn't use that drawing", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
        var panel = book.Pages[page].Panels[slot];
        var rect = book.PanelRects(book.Pages[page])[slot];
        var check = book.Check(rect, artW, artH, panel.Fit);
        _storybookView.Edit(b => b.Pages[page].Panels[slot].Artwork = b.Relate(link));
        if (check.BelowTarget)
        {
            bool resize = resizeIfSmall ?? await AskResizedCopyAsync(book, rect, artW, artH, check);
            if (resize) return await ResizedStorybookCopyAsync(page, slot);
        }
        return true;
    }

    private async Task<bool> AskResizedCopyAsync(StorybookProject book, RectMm rect, int artW, int artH, ResolutionCheck check)
    {
        var (w, h) = book.SuggestedCanvas(rect);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(DialogHost.Text($"This drawing is {artW} × {artH} px. In this {rect.Width / 10:0.0} × {rect.Height / 10:0.0} cm panel it prints at {check.EffectiveDpi:0} dpi, " +
                                          $"below the storybook's {book.Dpi} dpi, so it will look softer than the other panels."));
        body.Children.Add(DialogHost.Text($"A resized copy ({w} × {h}) has the panel's resolution, but enlarging doesn't add detail: it gives you the right size to paint the detail in. " +
                                          "The original is never changed."));
        var r = await _dialogs.ShowAsync("Lower resolution", body, [("keep", "Use it as it is", false), ("resize", "Make a resized copy", true)], "keep", "keep", 460);
        return r == "resize";
    }

    /// <summary>A copy of the panel's drawing at the panel's size at the project's resolution (the original isn't touched).</summary>
    public async Task<bool> ResizedStorybookCopyAsync(int page, int slot)
    {
        if (Storybook is not { } book || _storybookView is null || book.Resolve(book.Pages[page].Panels[slot].Artwork) is not { } path) return false;
        var rect = book.PanelRects(book.Pages[page])[slot];
        var fit = book.Pages[page].Panels[slot].Fit;
        try
        {
            var copy = await Task.Run(() =>
            {
                var doc = DnkFile.Load(path);
                // Keep the drawing's shape: the panel's resolution on the side that sets its size in the panel.
                double s = fit == PanelFit.Fill
                    ? Math.Max(book.Pixels(rect.Width) / (double)doc.Width, book.Pixels(rect.Height) / (double)doc.Height)
                    : Math.Min(book.Pixels(rect.Width) / (double)doc.Width, book.Pixels(rect.Height) / (double)doc.Height);
                int w = Math.Max(1, (int)Math.Round(doc.Width * s)), h = Math.Max(1, (int)Math.Round(doc.Height * s));
                ImageOperations.ResizeImage(doc, w, h, Interpolation.Bicubic);
                var target = book.NewArtworkPath(Path.GetFileNameWithoutExtension(path) + $" {w}x{h}");
                DnkFile.Save(doc, target);
                return target;
            });
            _storybookView.Edit(b => b.Pages[page].Panels[slot].Artwork = b.Relate(copy));
            ShowToast($"Made {Path.GetFileName(copy)} for this panel; {Path.GetFileName(path)} is unchanged.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DnkFormatException or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Couldn't make the copy", ex.Message);
            return false;
        }
    }

    /// <summary>"Make a copy for this panel": the panel gets its own copy of a shared drawing.</summary>
    public async Task<bool> CopyStorybookDrawingAsync(int page, int slot)
    {
        if (Storybook is not { } book || _storybookView is null || book.Resolve(book.Pages[page].Panels[slot].Artwork) is not { } path) return false;
        try
        {
            var copy = book.NewArtworkPath(Path.GetFileNameWithoutExtension(path) + " copy");
            await Task.Run(() => File.Copy(path, copy));
            _storybookView.Edit(b => b.Pages[page].Panels[slot].Artwork = b.Relate(copy));
            ShowToast($"Panel {slot + 1} now has its own copy: {Path.GetFileName(copy)}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't copy the drawing", ex.Message);
            return false;
        }
    }

    // ================================================================== page size and resolution

    /// <summary>Asks for the page size, resolution and (for a new storybook) the first layout.</summary>
    private async Task<(double Width, double Height, int Dpi, string Layout)?> AskStorybookPageAsync(string title, StorybookProject? current)
    {
        string paper = "A4";
        bool landscape = false;
        int dpi = current?.Dpi ?? 300;
        string layout = StoryLayouts.Comic;
        if (current is not null)
        {
            var match = StorybookView.PaperSizes.FirstOrDefault(p => Math.Abs(p.Width - Math.Min(current.PageWidthMm, current.PageHeightMm)) < 0.5
                                                                     && Math.Abs(p.Height - Math.Max(current.PageWidthMm, current.PageHeightMm)) < 0.5);
            paper = match.Name ?? "Custom";
            landscape = current.PageWidthMm > current.PageHeightMm;
        }
        var widthBox = new TextBox { Text = ((current?.PageWidthMm ?? 210) / 10).ToString("0.#", CultureInfo.CurrentCulture), Width = 64 };
        var heightBox = new TextBox { Text = ((current?.PageHeightMm ?? 297) / 10).ToString("0.#", CultureInfo.CurrentCulture), Width = 64 };
        var summary = new TextBlock { Classes = { "dim" }, TextWrapping = TextWrapping.Wrap, Name = "StorybookPageSummary" };
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#FF8A7A")), IsVisible = false, TextWrapping = TextWrapping.Wrap };

        (double W, double H)? Size()
        {
            if (paper == "Custom")
            {
                if (!double.TryParse(widthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double cw) ||
                    !double.TryParse(heightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double ch) || cw < 2 || ch < 2 || cw > 200 || ch > 200) return null;
                return (cw * 10, ch * 10);
            }
            var (_, pw, ph) = StorybookView.PaperSizes.First(p => p.Name == paper);
            return landscape ? (ph, pw) : (pw, ph);
        }
        void Summarise()
        {
            if (Size() is not { } s)
            {
                summary.Text = "Enter a page size from 2 to 200 cm.";
                return;
            }
            int pxW = (int)Math.Round(s.W / 25.4 * dpi), pxH = (int)Math.Round(s.H / 25.4 * dpi);
            summary.Text = $"{s.W / 10:0.#} × {s.H / 10:0.#} cm at {dpi} dpi: pages of {pxW} × {pxH} px. Every panel gets {dpi / 2.54:0} pixels per centimetre, " +
                           "so a bigger panel simply has more pixels.";
        }
        Action refreshPaper = () => { }, refreshOrient = () => { }, refreshDpi = () => { }, refreshLayout = () => { };
        var custom = UiKit.Row(6, widthBox, new TextBlock { Text = "×", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }, heightBox,
            new TextBlock { Text = "cm", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center });
        custom.IsVisible = paper == "Custom";
        var (paperSeg, rp) = UiKit.Segmented(StorybookView.PaperSizes.Select(p => (p.Name, (string?)null, (string?)p.Name, (string?)$"{p.Width / 10:0.#} × {p.Height / 10:0.#} cm"))
                .Append(("Custom", null, "Custom", "Your own size")),
            () => paper, v =>
            {
                paper = v;
                custom.IsVisible = v == "Custom";
                refreshPaper();
                Summarise();
            }, compact: true);
        refreshPaper = rp;
        var (orient, ro) = UiKit.Segmented([(false, null, "Portrait", "Taller than wide"), (true, null, "Landscape", "Wider than tall")], () => landscape, v =>
        {
            landscape = v;
            refreshOrient();
            Summarise();
        }, compact: true);
        refreshOrient = ro;
        var (dpiSeg, rd) = UiKit.Segmented([(300, null, "300 dpi", "Print quality (the usual choice)"), (150, null, "150 dpi", "Screen and quick storyboards: lighter files"),
                (600, null, "600 dpi", "Fine line art for print")],
            () => dpi, v =>
            {
                dpi = v;
                refreshDpi();
                Summarise();
            }, compact: true);
        refreshDpi = rd;
        widthBox.TextChanged += (_, _) => Summarise();
        heightBox.TextChanged += (_, _) => Summarise();
        var body = new StackPanel { Spacing = 10, Name = "StorybookPageDialog" };
        body.Children.Add(UiKit.Row(8, Label("Page"), paperSeg));
        body.Children.Add(UiKit.Row(8, Label(""), orient, custom));
        body.Children.Add(UiKit.Row(8, Label("Resolution"), dpiSeg));
        if (current is null)
        {
            var (layoutSeg, rl) = UiKit.Segmented(StoryLayouts.All.Select(l => (l.Id, (string?)null, (string?)ShortName(l), (string?)l.Name)), () => layout, v =>
            {
                layout = v;
                refreshLayout();
            }, compact: true);
            refreshLayout = rl;
            body.Children.Add(UiKit.Row(8, Label("First page"), layoutSeg));
        }
        else body.Children.Add(DialogHost.Text("Drawings are never resized by this: panels show how each one comes out at the new size."));
        body.Children.Add(summary);
        body.Children.Add(error);
        Summarise();
        while (true)
        {
            var r = await _dialogs.ShowAsync(title, body, [("cancel", "Cancel", false), ("ok", current is null ? "Create…" : "Apply", true)], "ok", "cancel", 560);
            if (r != "ok") return null;
            if (Size() is { } s) return (s.W, s.H, dpi, layout);
            error.Text = "Please enter a page size from 2 to 200 cm.";
            error.IsVisible = true;
        }
    }

    private static string ShortName(StoryLayout l) => l.Id switch
    {
        StoryLayouts.Single => "One",
        StoryLayouts.TwoStacked => "Two",
        StoryLayouts.Four => "Four",
        StoryLayouts.Six => "Six",
        StoryLayouts.Cinematic => "Cinematic",
        _ => "Comic",
    };

    private static TextBlock Label(string text) => new() { Text = text, Classes = { "dim" }, Width = 80, VerticalAlignment = VerticalAlignment.Center };

    public async Task StorybookSettingsAsync()
    {
        if (Storybook is not { } book || _storybookView is null) return;
        var choice = await AskStorybookPageAsync("Page size and resolution", book);
        if (choice is not { } c) return;
        _storybookView.Edit(b =>
        {
            b.PageWidthMm = c.Width;
            b.PageHeightMm = c.Height;
            b.Dpi = c.Dpi;
        });
    }

    // ================================================================== export

    public async Task ExportStorybookAsync(StorybookExport kind)
    {
        if (Storybook is not { } book || _storybookView is null) return;
        string suggested = kind switch
        {
            StorybookExport.ThisPage => $"{book.Name} page {_storybookView.PageIndex + 1:00}.png",
            StorybookExport.AllPng => $"{book.Name}.png",
            _ => $"{book.Name}.pdf",
        };
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = kind switch
            {
                StorybookExport.ThisPage => "Export this page as PNG",
                StorybookExport.AllPng => "Export all pages as PNG (numbered: name_01.png, name_02.png …)",
                _ => "Export all pages as one PDF",
            },
            SuggestedFileName = suggested,
            DefaultExtension = kind == StorybookExport.Pdf ? "pdf" : "png",
            FileTypeChoices = kind == StorybookExport.Pdf ? [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }] : [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await ExportStorybookToAsync(kind, path);
    }

    /// <summary>Writes the export (tests call this with a path). Returns the files written.</summary>
    public async Task<IReadOnlyList<string>> ExportStorybookToAsync(StorybookExport kind, string path)
    {
        if (Storybook is not { } book || _storybookView is null) return [];
        var pages = kind == StorybookExport.ThisPage ? [book.Pages[_storybookView.PageIndex]] : book.Pages.ToList();
        // A drawing open in the editor with unsaved changes is exported as it is now.
        var live = new Dictionary<string, RasterImage>(StringComparer.OrdinalIgnoreCase);
        if (Session.FilePath is { } open && Session.Document.IsDirty) live[Path.GetFullPath(open)] = Engine.Storybook.StorybookRenderer.Flatten(Session.Document);
        var progress = new TextBlock { Text = "Exporting…" };
        Task<string?>? card = null;
        var work = Task.Run(() =>
        {
            var cache = new Dictionary<string, RasterImage?>(StringComparer.OrdinalIgnoreCase);
            RasterImage? Art(string p) => live.TryGetValue(p, out var l) ? l : cache.TryGetValue(p, out var c) ? c : cache[p] = StorybookRenderer.LoadArtwork(p);
            var written = new List<string>();
            if (kind == StorybookExport.Pdf)
            {
                WritePdf(book, pages, Art, path, i => Avalonia.Threading.Dispatcher.UIThread.Post(() => progress.Text = $"Page {i} of {pages.Count}…"));
                written.Add(path);
            }
            else
            {
                var paths = kind == StorybookExport.ThisPage ? [path] : StorybookRenderer.PagePaths(path, pages.Count);
                for (int i = 0; i < pages.Count; i++)
                {
                    int n = i + 1;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => progress.Text = $"Page {n} of {pages.Count}…");
                    var image = StorybookRenderer.RenderPage(book, pages[i], Art);
                    AnimationExport.WriteAtomically(paths[i], PngCodec.Encode(image, book.Dpi));
                    written.Add(paths[i]);
                }
            }
            return (IReadOnlyList<string>)written;
        });
        if (await Task.WhenAny(work, Task.Delay(400)) != work)
            card = _dialogs.ShowAsync("Exporting the storybook", progress, [], null, null, 320);
        try
        {
            var written = await work;
            if (card is { IsCompleted: false }) _dialogs.Close("done");
            var (pw, ph) = book.PagePixels;
            ShowToast(written.Count == 1
                ? $"Exported {Path.GetFileName(written[0])} · {pw} × {ph} px at {book.Dpi} dpi"
                : $"Exported {written.Count} pages ({Path.GetFileName(written[0])} …) · {pw} × {ph} px at {book.Dpi} dpi");
            return written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (card is { IsCompleted: false }) _dialogs.Close("done");
            await _dialogs.ShowMessageAsync("Couldn't export the storybook", ex.Message);
            return [];
        }
    }

    /// <summary>
    /// One PDF with every page at its physical size, each page the rendered picture at the project's resolution (Skia's PDF
    /// writer, already part of DoodleNook; written to a temporary file first, then moved into place).
    /// </summary>
    internal static void WritePdf(StorybookProject book, IReadOnlyList<StoryPage> pages, Func<string, RasterImage?> art, string path, Action<int>? progress = null)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(full) ?? ".", $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new SKFileWStream(temp))
            using (var pdf = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
                   {
                       Title = book.Name, Creator = "DoodleNook", Producer = "DoodleNook", RasterDpi = book.Dpi, EncodingQuality = 101,
                   }))
            {
                float wPt = (float)(book.PageWidthMm / 25.4 * 72), hPt = (float)(book.PageHeightMm / 25.4 * 72);
                for (int i = 0; i < pages.Count; i++)
                {
                    progress?.Invoke(i + 1);
                    var image = StorybookRenderer.RenderPage(book, pages[i], art);
                    using var bmp = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                    System.Runtime.InteropServices.Marshal.Copy((int[])(object)image.Pixels, 0, bmp.GetPixels(), image.Pixels.Length);
                    var canvas = pdf.BeginPage(wPt, hPt);
                    using var paint = new SKPaint { IsAntialias = true };
                    using var img = SKImage.FromBitmap(bmp);
                    canvas.DrawImage(img, new SKRect(0, 0, wPt, hPt), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
                    pdf.EndPage();
                }
                pdf.Close();
            }
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
