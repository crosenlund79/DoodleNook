using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using PixelRect = DoodleNook.Engine.Imaging.PixelRect;

namespace DoodleNook.App.Dialogs;

/// <summary>What the Import sprite sheet dialog decided: the grid, which of its frames (indexes into the grid's frames,
/// in order), whether to make the four-layer reference, the frame rate and the style.</summary>
public sealed record SpriteSheetImportChoice(SliceGrid Grid, IReadOnlyList<int> Keep, bool AsReference, double Fps, bool PixelArt);

/// <summary>
/// Mission 015 — File › Import sprite sheet…: one compact dialog. The sheet is shown with the proposed grid over it, so
/// what is imported is exactly what is outlined; drag on the sheet to move the grid, drag the first frame's corner to
/// size it, or type the numbers. Below, the frames it gives (click one to leave it out; empty cells start left out).
/// </summary>
public sealed class SpriteSheetImportDialog
{
    private const double StageWidth = 600, StageHeight = 260, ThumbSize = 52;

    private readonly RasterImage _sheet;
    private SliceGrid _g;
    private readonly Dictionary<int, bool> _toggled = new(); // cell index → included, where the artist clicked
    private readonly Avalonia.Controls.Canvas _overlay = new() { Background = Brushes.Transparent, ClipToBounds = true, Name = "SheetGrid", Cursor = new Cursor(StandardCursorType.SizeAll) };
    private readonly WrapPanel _strip = new() { Name = "SheetFilmstrip" };
    private readonly TextBlock _status = new() { Classes = { "dim", "num" }, VerticalAlignment = VerticalAlignment.Center, Name = "SheetCount", TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
    private readonly TextBlock _note = new() { Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Name = "SheetNote" };
    private readonly TextBlock _error = new() { Foreground = new SolidColorBrush(Color.Parse("#FF8A7A")), TextWrapping = TextWrapping.Wrap, Name = "SheetProblem" };
    private readonly TextBox _fw, _fh, _cols, _rows, _ml, _mt, _sx, _sy, _first, _count, _fps;
    private readonly DispatcherTimer _stripTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly double _scale;
    private bool _syncing;
    private bool _asReference;
    private bool _pixel;
    private Point _dragStart;
    private SliceGrid? _dragGrid;
    private bool _dragCorner;

    private SpriteSheetImportDialog(RasterImage sheet, SliceGrid suggestion)
    {
        _sheet = sheet;
        _g = suggestion;
        _pixel = Math.Max(suggestion.FrameWidth, suggestion.FrameHeight) <= 256;
        double fit = Math.Min(StageWidth / sheet.Width, StageHeight / sheet.Height);
        _scale = fit >= 1 ? Math.Min(8, Math.Floor(fit)) : fit;
        TextBox Box(string name, double width = 56) => new() { Width = width, Name = name };
        _fw = Box("SheetFrameWidth");
        _fh = Box("SheetFrameHeight");
        _cols = Box("SheetColumns", 48);
        _rows = Box("SheetRows", 48);
        _ml = Box("SheetMarginLeft", 48);
        _mt = Box("SheetMarginTop", 48);
        _sx = Box("SheetSpacingX", 48);
        _sy = Box("SheetSpacingY", 48);
        _first = Box("SheetFirst", 48);
        _count = Box("SheetHowMany", 48);
        _fps = Box("SheetFps", 52);
        _fps.Text = SpriteSheetSlicer.DefaultFps(suggestion).ToString("0.##", CultureInfo.CurrentCulture);
        _stripTimer.Tick += (_, _) =>
        {
            _stripTimer.Stop();
            FillStrip();
        };
    }

    /// <summary>The current grid (tests).</summary>
    internal SliceGrid Grid => _g;

    public static async Task<SpriteSheetImportChoice?> AskAsync(DialogHost host, RasterImage sheet, string summary, SliceGrid suggestion, string? suggestionNote,
        double maxHeight = 2000)
    {
        var d = new SpriteSheetImportDialog(sheet, suggestion);
        // On a compact screen the dialog scrolls; its buttons stay in view.
        var body = new ScrollViewer { Content = d.Build(summary, suggestionNote), MaxHeight = Math.Max(320, maxHeight), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        d.Sync();
        d.Refresh();
        d.FillStrip();
        try
        {
            while (true)
            {
                var r = await host.ShowAsync("Import sprite sheet", body, [("cancel", "Cancel", false), ("import", "Import", true)], "import", "cancel", 640);
                if (r != "import") return null;
                var keep = d.Keep();
                if (SpriteSheetSlicer.Problem(d._g, sheet.Width, sheet.Height) is { } problem) d._error.Text = problem;
                else if (keep.Count == 0) d._error.Text = "Choose at least one frame (click a frame below to bring it back).";
                else if (!double.TryParse(d._fps.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double fps)
                         || fps < Engine.Documents.Animation.MinFps || fps > Engine.Documents.Animation.MaxFps)
                    d._error.Text = $"Please enter a frame rate from {Engine.Documents.Animation.MinFps} to {Engine.Documents.Animation.MaxFps}.";
                else return new SpriteSheetImportChoice(d._g, keep, d._asReference, fps, d._pixel);
                d._error.IsVisible = true;
            }
        }
        finally
        {
            d._stripTimer.Stop();
        }
    }

    // ================================================================== layout

    private Control Build(string summary, string? suggestionNote)
    {
        var image = new Image
        {
            Source = BrushPreview.ToBitmap(_sheet),
            Width = _sheet.Width * _scale,
            Height = _sheet.Height * _scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapInterpolationMode(image, _scale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        _overlay.Width = image.Width;
        _overlay.Height = image.Height;
        var stage = new Panel { Width = image.Width, Height = image.Height, HorizontalAlignment = HorizontalAlignment.Center };
        stage.Children.Add(new Border { Background = UiKit.CheckerBrush(), Child = image });
        stage.Children.Add(_overlay);
        _overlay.PointerPressed += OnPress;
        _overlay.PointerMoved += OnMove;
        _overlay.PointerReleased += (_, e) =>
        {
            _dragGrid = null;
            e.Pointer.Capture(null);
        };
        ToolTip.SetTip(_overlay, "Drag to move the grid · drag the corner of the first frame to change the frame size");

        foreach (var box in new[] { _fw, _fh, _ml, _mt, _sx, _sy, _first, _count }) box.TextChanged += (_, _) => FromBoxes();
        _cols.TextChanged += (_, _) => FromColumnsRows(columns: true);
        _rows.TextChanged += (_, _) => FromColumnsRows(columns: false);

        var (order, refreshOrder) = UiKit.Segmented(
            [(FrameOrder.AcrossRows, null, "Across rows", "Left to right, then the next row (the usual order)"),
             (FrameOrder.DownColumns, null, "Down columns", "Top to bottom, then the next column")],
            () => _g.Order, v =>
            {
                Change(_g with { Order = v });
            });
        _refreshOrder = refreshOrder;
        Action refreshMode = () => { }, refreshStyle = () => { };
        var (mode, rm) = UiKit.Segmented(
            [(false, "film", "Animation", "A regular animation you can edit: each frame of the sheet is a drawing"),
             (true, "layers", "Reference to redraw", "Four layers: these frames locked at 35 % on Reference, then Sketch, Outline and Coloring")],
            () => _asReference, v =>
            {
                _asReference = v;
                refreshMode();
            });
        refreshMode = rm;
        var (style, rs) = UiKit.Segmented(
            [(false, "brush", "Hand-drawn", "Brushes with pressure, soft edges"), (true, "pixpencil", "Pixel art", "Exact pixels, whole-number zoom")],
            () => _pixel, v =>
            {
                _pixel = v;
                refreshStyle();
            });
        refreshStyle = rs;

        var body = new StackPanel { Spacing = 9, Name = "SpriteSheetDialog" };
        body.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Name = "SheetSummary" });
        body.Children.Add(new ScrollViewer
        {
            Content = stage, MaxHeight = StageHeight + 4, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        if (suggestionNote is not null) body.Children.Add(DialogHost.Text(suggestionNote));
        body.Children.Add(UiKit.Row(8, Dim("Frame", 64), _fw, Dim("×"), _fh, Dim("px"), Dim("   Split into"), _cols, Dim("×"), _rows));
        body.Children.Add(UiKit.Row(8, Dim("Margin", 64), Dim("left"), _ml, Dim("top"), _mt, Dim("   Spacing"), _sx, _sy, Dim("px")));
        body.Children.Add(UiKit.Row(8, Dim("Order", 64), order));
        body.Children.Add(UiKit.Row(8, Dim("Frames", 64), Dim("from"), _first, Dim("how many"), _count, _status));
        body.Children.Add(new ScrollViewer { Content = _strip, MaxHeight = 150, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        body.Children.Add(UiKit.Row(8, Dim("Import as", 64), mode));
        body.Children.Add(UiKit.Row(8, Dim("Speed", 64), _fps, Dim("frames per second"), Dim("   "), style));
        body.Children.Add(_note);
        _error.IsVisible = false;
        body.Children.Add(_error);
        body.Children.Add(DialogHost.Text("The sheet is only read, never changed. Frames are copied pixel for pixel."));
        return body;
    }

    private Action _refreshOrder = () => { };

    private static TextBlock Dim(string text, double? width = null)
    {
        var t = new TextBlock { Text = text, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center };
        if (width is { } w) t.Width = w;
        return t;
    }

    // ================================================================== changing the grid

    private static int? Int(TextBox b) => int.TryParse(b.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    private void FromBoxes()
    {
        if (_syncing) return;
        var g = _g with
        {
            FrameWidth = Int(_fw) ?? _g.FrameWidth,
            FrameHeight = Int(_fh) ?? _g.FrameHeight,
            MarginLeft = Int(_ml) ?? _g.MarginLeft,
            MarginTop = Int(_mt) ?? _g.MarginTop,
            SpacingX = Int(_sx) ?? _g.SpacingX,
            SpacingY = Int(_sy) ?? _g.SpacingY,
            FirstFrame = Int(_first) ?? _g.FirstFrame,
            Count = string.IsNullOrWhiteSpace(_count.Text) ? null : Int(_count) ?? _g.Count,
        };
        bool cellsChanged = g with { FirstFrame = _g.FirstFrame, Count = _g.Count } != _g;
        Change(g, syncBoxes: false, cellsChanged);
        SyncColumnsRows();
    }

    /// <summary>"Split into columns × rows": the frame size that divides the sheet (after the margins and spacing) evenly.</summary>
    private void FromColumnsRows(bool columns)
    {
        if (_syncing) return;
        // Text changes can arrive after the boxes were filled in from the grid: only a different number is a request.
        var (curCols, curRows) = SpriteSheetSlicer.CellCount(_g, _sheet.Width, _sheet.Height);
        if (columns && Int(_cols) is { } c && c > 0 && c != curCols)
        {
            int fw = (_sheet.Width - _g.MarginLeft - (c - 1) * _g.SpacingX) / c;
            if (fw > 0) Change(_g with { FrameWidth = fw, Count = null }, syncBoxes: true, cellsChanged: true, skipColumnsRows: true);
        }
        else if (!columns && Int(_rows) is { } r && r > 0 && r != curRows)
        {
            int fh = (_sheet.Height - _g.MarginTop - (r - 1) * _g.SpacingY) / r;
            if (fh > 0) Change(_g with { FrameHeight = fh, Count = null }, syncBoxes: true, cellsChanged: true, skipColumnsRows: true);
        }
    }

    private void Change(SliceGrid g, bool syncBoxes = true, bool cellsChanged = true, bool skipColumnsRows = false)
    {
        if (g == _g) return;
        if (cellsChanged || g.Order != _g.Order) _toggled.Clear(); // other cells: the artist's clicks no longer apply
        _g = g;
        if (syncBoxes) Sync(skipColumnsRows);
        Refresh();
        _stripTimer.Stop();
        _stripTimer.Start();
    }

    /// <summary>Writes the grid into the number boxes (without reacting to it).</summary>
    private void Sync(bool skipColumnsRows = false)
    {
        _syncing = true;
        try
        {
            void Set(TextBox b, int v)
            {
                var s = v.ToString(CultureInfo.InvariantCulture);
                if (b.Text != s) b.Text = s;
            }
            Set(_fw, _g.FrameWidth);
            Set(_fh, _g.FrameHeight);
            Set(_ml, _g.MarginLeft);
            Set(_mt, _g.MarginTop);
            Set(_sx, _g.SpacingX);
            Set(_sy, _g.SpacingY);
            Set(_first, _g.FirstFrame);
            if (_g.Count is { } n) Set(_count, n);
            else
            {
                var layout = SpriteSheetSlicer.Layout(_g, _sheet.Width, _sheet.Height);
                _count.Text = layout.Frames.Count > 0 ? layout.Frames.Count.ToString(CultureInfo.InvariantCulture) : "";
            }
            _refreshOrder();
        }
        finally
        {
            _syncing = false;
        }
        if (!skipColumnsRows) SyncColumnsRows();
    }

    private void SyncColumnsRows()
    {
        _syncing = true;
        try
        {
            var (c, r) = SpriteSheetSlicer.CellCount(_g, _sheet.Width, _sheet.Height);
            _cols.Text = c.ToString(CultureInfo.InvariantCulture);
            _rows.Text = r.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _syncing = false;
        }
    }

    // ================================================================== dragging the grid

    private Point SheetPoint(PointerEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        return new Point(p.X / _scale, p.Y / _scale);
    }

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        double cx = (_g.MarginLeft + _g.FrameWidth) * _scale, cy = (_g.MarginTop + _g.FrameHeight) * _scale;
        _dragCorner = Math.Abs(p.X - cx) <= 9 && Math.Abs(p.Y - cy) <= 9;
        _dragStart = SheetPoint(e);
        _dragGrid = _g;
        e.Pointer.Capture(_overlay);
        e.Handled = true;
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(_overlay);
        if (_dragGrid is not { } start)
        {
            double cx = (_g.MarginLeft + _g.FrameWidth) * _scale, cy = (_g.MarginTop + _g.FrameHeight) * _scale;
            _overlay.Cursor = new Cursor(Math.Abs(p.X - cx) <= 9 && Math.Abs(p.Y - cy) <= 9 ? StandardCursorType.BottomRightCorner : StandardCursorType.SizeAll);
            return;
        }
        var now = SheetPoint(e);
        int dx = (int)Math.Round(now.X - _dragStart.X), dy = (int)Math.Round(now.Y - _dragStart.Y);
        Change(_dragCorner
            ? start with { FrameWidth = Math.Max(1, start.FrameWidth + dx), FrameHeight = Math.Max(1, start.FrameHeight + dy), Count = null }
            : start with { MarginLeft = Math.Max(0, start.MarginLeft + dx), MarginTop = Math.Max(0, start.MarginTop + dy), Count = null });
    }

    /// <summary>For tests: drags the grid by whole sheet pixels, or the first frame's corner.</summary>
    internal void DragBy(int dx, int dy, bool corner)
    {
        var start = _g;
        Change(corner
            ? start with { FrameWidth = Math.Max(1, start.FrameWidth + dx), FrameHeight = Math.Max(1, start.FrameHeight + dy), Count = null }
            : start with { MarginLeft = Math.Max(0, start.MarginLeft + dx), MarginTop = Math.Max(0, start.MarginTop + dy), Count = null });
    }

    // ================================================================== what is shown

    private bool Included(int cell, PixelRect rect) =>
        _toggled.TryGetValue(cell, out bool on) ? on : !SpriteSheetSlicer.IsEmpty(_sheet, rect);

    /// <summary>The chosen frames' positions in the grid's frame list, in order.</summary>
    private List<int> Keep()
    {
        var layout = SpriteSheetSlicer.Layout(_g, _sheet.Width, _sheet.Height);
        var keep = new List<int>();
        for (int i = 0; i < layout.Frames.Count; i++)
            if (Included(layout.FirstIndex + i, layout.Frames[i])) keep.Add(i);
        return keep;
    }

    private void Refresh()
    {
        _overlay.Children.Clear();
        var problem = SpriteSheetSlicer.Problem(_g, _sheet.Width, _sheet.Height);
        _error.Text = problem ?? "";
        _error.IsVisible = problem is not null;
        if (problem is not null)
        {
            _status.Text = "";
            _note.Text = "";
            return;
        }
        var layout = SpriteSheetSlicer.Layout(_g, _sheet.Width, _sheet.Height);
        var chosen = new HashSet<PixelRect>(layout.Frames);
        var accent = new SolidColorBrush(DnColors.Accent);
        var dim = new SolidColorBrush(Color.FromArgb(150, 120, 110, 100));
        int shown = 0;
        for (int i = 0; i < layout.Cells.Count && shown < 1500; i++, shown++)
        {
            var c = layout.Cells[i];
            bool isChosen = chosen.Contains(c);
            bool included = isChosen && Included(i, c);
            var rect = new Rectangle
            {
                Width = Math.Max(1, c.Width * _scale),
                Height = Math.Max(1, c.Height * _scale),
                Stroke = included ? accent : dim,
                StrokeThickness = included ? 1.5 : 1,
                Fill = included ? null : new SolidColorBrush(Color.FromArgb(isChosen ? (byte)90 : (byte)60, 20, 16, 14)),
                IsHitTestVisible = false,
            };
            Avalonia.Controls.Canvas.SetLeft(rect, c.X * _scale);
            Avalonia.Controls.Canvas.SetTop(rect, c.Y * _scale);
            _overlay.Children.Add(rect);
            if (layout.Cells.Count <= 200 && c.Width * _scale >= 18 && c.Height * _scale >= 14)
            {
                var label = new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = 10, Foreground = included ? accent : dim,
                    IsHitTestVisible = false, Margin = new Thickness(2, 0, 0, 0),
                };
                Avalonia.Controls.Canvas.SetLeft(label, c.X * _scale);
                Avalonia.Controls.Canvas.SetTop(label, c.Y * _scale);
                _overlay.Children.Add(label);
            }
        }
        // The corner that sizes the frames.
        var handle = new Rectangle { Width = 8, Height = 8, Fill = accent, IsHitTestVisible = false, Name = "SheetCornerHandle" };
        Avalonia.Controls.Canvas.SetLeft(handle, (_g.MarginLeft + _g.FrameWidth) * _scale - 4);
        Avalonia.Controls.Canvas.SetTop(handle, (_g.MarginTop + _g.FrameHeight) * _scale - 4);
        _overlay.Children.Add(handle);

        int keep = Keep().Count, empty = 0;
        for (int i = 0; i < layout.Frames.Count; i++)
            if (SpriteSheetSlicer.IsEmpty(_sheet, layout.Frames[i])) empty++;
        _status.Text = $"{keep} of {layout.Cells.Count} frames · {layout.Columns} × {layout.Rows} grid" +
                       (empty > 0 ? $" · {empty} empty" : "");
        var (right, bottom) = SpriteSheetSlicer.Leftover(_g, _sheet.Width, _sheet.Height);
        var notes = new List<string>();
        if (right > 0 || bottom > 0)
            notes.Add($"{(right > 0 ? $"{right} px at the right" : "")}{(right > 0 && bottom > 0 ? " and " : "")}{(bottom > 0 ? $"{bottom} px at the bottom" : "")} " +
                      "aren't part of any frame: if that's unexpected, check the frame size, margin and spacing.");
        if (empty > 0) notes.Add("Empty cells start left out; click one below to bring it back.");
        _note.Text = string.Join(" ", notes);
    }

    private void FillStrip()
    {
        _strip.Children.Clear();
        if (SpriteSheetSlicer.Problem(_g, _sheet.Width, _sheet.Height) is not null) return;
        var layout = SpriteSheetSlicer.Layout(_g, _sheet.Width, _sheet.Height);
        int max = Math.Min(layout.Frames.Count, 240);
        for (int i = 0; i < max; i++)
        {
            int cell = layout.FirstIndex + i;
            var rect = layout.Frames[i];
            bool included = Included(cell, rect);
            var crop = _sheet.Crop(rect);
            double s = Math.Min(ThumbSize / rect.Width, ThumbSize / rect.Height);
            if (s >= 1) s = Math.Floor(s);
            var img = new Image { Width = Math.Max(1, rect.Width * s), Height = Math.Max(1, rect.Height * s), Stretch = Stretch.Fill, Source = BrushPreview.ToBitmap(crop) };
            RenderOptions.SetBitmapInterpolationMode(img, s >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
            var tile = UiKit.Button(null, null, $"Frame {cell + 1}: click to leave it out of the import (or bring it back)");
            tile.Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new Border { Child = img, Background = UiKit.CheckerBrush(), CornerRadius = new CornerRadius(3), Width = ThumbSize, Height = ThumbSize },
                    new TextBlock { Text = (cell + 1).ToString(CultureInfo.InvariantCulture), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } },
                },
            };
            tile.Padding = new Thickness(3);
            tile.Height = ThumbSize + 24;
            tile.Margin = new Thickness(0, 0, 4, 4);
            tile.Opacity = included ? 1 : 0.3;
            tile.Classes.Set("on", included);
            tile.Name = $"SheetFrame{cell + 1}";
            tile.Click += (_, _) =>
            {
                _toggled[cell] = !Included(cell, rect);
                Refresh();
                FillStrip();
            };
            _strip.Children.Add(tile);
        }
        if (layout.Frames.Count > max)
            _strip.Children.Add(new TextBlock { Text = $"… and {layout.Frames.Count - max} more", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center });
    }
}

/// <summary>What the Sprite sheet from pictures dialog decided.</summary>
public sealed record PicturesToSheetChoice(IReadOnlyList<int> Keep, int Columns, int Spacing, int Margin, CellAlignment Align, bool Json);

/// <summary>
/// Mission 015 — File › Sprite sheet from pictures…: numbered pictures (in natural order) laid out on one sheet. A
/// filmstrip to leave pictures out, the columns (rows follow), spacing, margin, and where smaller pictures sit when
/// the sizes differ. The pictures are only read.
/// </summary>
public static class PicturesToSheetDialog
{
    private const double ThumbSize = 52;

    public static async Task<PicturesToSheetChoice?> AskAsync(DialogHost host, IReadOnlyList<RasterImage> pictures, IReadOnlyList<string> names, string summary,
        double maxHeight = 2000)
    {
        int n = pictures.Count;
        var excluded = new bool[n];
        int columns = PicturesToSheet.DefaultColumns(n), spacing = 0, margin = 0;
        bool json = false;
        var align = CellAlignment.BottomCenter;
        bool uneven = pictures.Any(p => p.Width != pictures[0].Width || p.Height != pictures[0].Height);
        var result = new TextBlock { Classes = { "dim", "num" }, TextWrapping = TextWrapping.Wrap, Name = "PicturesSheetSize" };
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#FF8A7A")), IsVisible = false, TextWrapping = TextWrapping.Wrap };

        List<int> Keep() => Enumerable.Range(0, n).Where(i => !excluded[i]).ToList();

        void Summarise()
        {
            var keep = Keep();
            if (keep.Count == 0)
            {
                result.Text = "No pictures chosen.";
                return;
            }
            var (w, h) = PicturesToSheet.CellSize(keep.Select(i => pictures[i]).ToList());
            int cols = Math.Clamp(columns, 1, keep.Count), rows = (keep.Count + cols - 1) / cols;
            int sw = 2 * margin + cols * w + (cols - 1) * spacing, sh = 2 * margin + rows * h + (rows - 1) * spacing;
            result.Text = $"{keep.Count} frames of {w} × {h} in {cols} × {rows} → {sw} × {sh} px{(json ? " + .json" : "")}";
        }

        var strip = new WrapPanel { Name = "PicturesFilmstrip" };
        var tiles = new List<Button>();
        for (int i = 0; i < n; i++)
        {
            int index = i;
            var p = pictures[i];
            double s = Math.Min(ThumbSize / p.Width, ThumbSize / p.Height);
            if (s >= 1) s = Math.Floor(s);
            var img = new Image { Width = Math.Max(1, p.Width * s), Height = Math.Max(1, p.Height * s), Stretch = Stretch.Fill, Source = BrushPreview.ToBitmap(p) };
            RenderOptions.SetBitmapInterpolationMode(img, s >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
            var tile = UiKit.Button(null, null, $"{names[i]} ({p.Width} × {p.Height}): click to leave it out (or bring it back)");
            tile.Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new Border { Child = img, Background = UiKit.CheckerBrush(), CornerRadius = new CornerRadius(3), Width = ThumbSize, Height = ThumbSize },
                    new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } },
                },
            };
            tile.Padding = new Thickness(3);
            tile.Height = ThumbSize + 24;
            tile.Margin = new Thickness(0, 0, 4, 4);
            tile.Classes.Add("on");
            tile.Name = $"Picture{i + 1}";
            tile.Click += (_, _) =>
            {
                excluded[index] = !excluded[index];
                tiles[index].Opacity = excluded[index] ? 0.3 : 1;
                tiles[index].Classes.Set("on", !excluded[index]);
                Summarise();
            };
            tiles.Add(tile);
            strip.Children.Add(tile);
        }

        TextBox Number(int value, string name, Action<int> set, int min, int max)
        {
            var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), Width = 56, Name = name };
            box.TextChanged += (_, _) =>
            {
                if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) set(Math.Clamp(v, min, max));
                Summarise();
            };
            return box;
        }

        var body = new StackPanel { Spacing = 10, Name = "PicturesToSheetDialog" };
        body.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new ScrollViewer { Content = strip, MaxHeight = 200, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        body.Children.Add(UiKit.Row(8, new TextBlock { Text = "Columns", Classes = { "dim" }, Width = 70, VerticalAlignment = VerticalAlignment.Center },
            Number(columns, "PicturesColumns", v => columns = v, 1, 999),
            new TextBlock { Text = "   Spacing", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center },
            Number(spacing, "PicturesSpacing", v => spacing = v, 0, 256),
            new TextBlock { Text = "Margin", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center },
            Number(margin, "PicturesMargin", v => margin = v, 0, 256),
            new TextBlock { Text = "px", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }));
        if (uneven)
        {
            Action refreshPlacement = () => { };
            var (placement, rp) = UiKit.Segmented(
                [(CellAlignment.BottomCenter, null, "Bottom centre", "Feet on one line: right for characters"),
                 (CellAlignment.Center, null, "Centre", "In the middle of each cell"),
                 (CellAlignment.TopLeft, null, "Top left", "As the files are, from the top-left corner")],
                () => align, v =>
                {
                    align = v;
                    refreshPlacement();
                });
            refreshPlacement = rp;
            body.Children.Add(UiKit.Row(8, new TextBlock { Text = "Place", Classes = { "dim" }, Width = 70, VerticalAlignment = VerticalAlignment.Center }, placement));
            body.Children.Add(DialogHost.Text("The pictures have different sizes: every cell is as large as the largest, and smaller pictures are placed in it unscaled, with transparent space around them."));
        }
        var (jsonCheck, _) = UiKit.Check("JSON metadata", "A .json file next to the PNG with every frame's position", () => json, v =>
        {
            json = v;
            Summarise();
        });
        body.Children.Add(UiKit.Row(8, new TextBlock { Width = 70 }, jsonCheck));
        body.Children.Add(result);
        body.Children.Add(DialogHost.Text("The pictures are only read. Nothing is scaled or smoothed; transparency is kept."));
        body.Children.Add(error);
        Summarise();
        while (true)
        {
            var scroller = new ScrollViewer { Content = body, MaxHeight = Math.Max(320, maxHeight), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            var r = await host.ShowAsync("Sprite sheet from pictures", scroller, [("cancel", "Cancel", false), ("save", "Save sprite sheet…", true)], "save", "cancel", 600);
            if (r != "save") return null;
            var keep = Keep();
            if (keep.Count > 0) return new PicturesToSheetChoice(keep, Math.Clamp(columns, 1, keep.Count), spacing, margin, align, json);
            error.Text = "Choose at least one picture.";
            error.IsVisible = true;
        }
    }
}
