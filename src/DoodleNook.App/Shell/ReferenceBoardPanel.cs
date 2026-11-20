using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;

namespace DoodleNook.App.Shell;

/// <summary>
/// The reference board: a floating, resizable panel over the canvas with pictures to look at while drawing. Drag a
/// picture to move it, its corner to resize it, the empty board to pan; the wheel zooms. With the eyedropper (or Alt)
/// a click picks a colour from a picture. The pictures are never part of the artwork: not in layers, saves or exports.
/// The board is kept between sessions (in the library folder) and can be saved as a .dnkboard file.
/// </summary>
public sealed class ReferenceBoardPanel : Border
{
    private readonly MainWindow _app;
    private readonly ReferenceBoardView _view;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _moving, _resizing;
    private Point _dragStart;
    private Thickness _startMargin;
    private Size _startSize;

    public ReferenceBoardPanel(MainWindow app)
    {
        _app = app;
        Name = "ReferenceBoard";
        Classes.Add("float");
        Padding = new Thickness(6);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Width = 380;
        Height = 320;
        Board = LoadSaved() ?? new ReferenceBoard();
        _view = new ReferenceBoardView(this) { Name = "ReferenceBoardView" };

        var title = new TextBlock { Text = "References", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
        var add = Small("plus", "Add pictures (they are copied into the board)", () => _ = AddFromFilesAsync());
        add.Name = "ReferenceAdd";
        var fit = Small("fit", "Show all pictures", () => _view.FitAll());
        var more = Menus.MenuButton(new DnIcon { Kind = "dots" }, MoreMenu, "Board: background, open, save", 240);
        more.Classes.Add("sq");
        more.Width = more.Height = more.MinWidth = 28;
        var close = Small("close", "Hide the board (Window › Reference board brings it back)", () => app.SetReferenceBoardVisible(false));
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeAll) };
        var buttons = UiKit.Row(2, add, fit, more, close);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        head.Children.Add(title);
        ToolTip.SetTip(head, "Drag to move the board");
        head.PointerPressed += (_, e) =>
        {
            if (e.Source is not (DockPanel or TextBlock)) return;
            _moving = true;
            _dragStart = e.GetPosition(app);
            _startMargin = Margin;
            e.Pointer.Capture(head);
            e.Handled = true;
        };
        head.PointerMoved += (_, e) =>
        {
            if (!_moving) return;
            var d = e.GetPosition(app) - _dragStart;
            Margin = Clamp(new Thickness(_startMargin.Left + d.X, _startMargin.Top + d.Y, 0, 0), Bounds.Size);
        };
        head.PointerReleased += (_, e) =>
        {
            if (!_moving) return;
            _moving = false;
            e.Pointer.Capture(null);
            RememberPlacement();
        };

        var grip = new Border
        {
            Width = 16, Height = 16, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner), Margin = new Thickness(0, 0, -6, -6), Name = "ReferenceResize",
            Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M14 4 L4 14 M14 9 L9 14"), Stroke = new SolidColorBrush(DnColors.Text2), StrokeThickness = 1.4 },
        };
        ToolTip.SetTip(grip, "Drag to resize the board");
        grip.PointerPressed += (_, e) =>
        {
            _resizing = true;
            _dragStart = e.GetPosition(app);
            _startSize = new Size(Width, Height);
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!_resizing) return;
            var d = e.GetPosition(app) - _dragStart;
            Width = Math.Clamp(_startSize.Width + d.X, 200, 1600);
            Height = Math.Clamp(_startSize.Height + d.Y, 160, 1200);
        };
        grip.PointerReleased += (_, e) =>
        {
            _resizing = false;
            e.Pointer.Capture(null);
            RememberPlacement();
        };

        var stack = new Panel();
        stack.Children.Add(new DockPanel { Children = { WithTop(head), new Border { Child = _view, CornerRadius = new CornerRadius(6), ClipToBounds = true } } });
        stack.Children.Add(grip);
        Child = stack;
        Board.Changed += ScheduleSave;
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
        RestorePlacement();
    }

    /// <summary>The pictures shown.</summary>
    public ReferenceBoard Board { get; private set; }

    internal MainWindow App => _app;

    private static Control WithTop(Control c)
    {
        DockPanel.SetDock(c, Dock.Top);
        return c;
    }

    private static Button Small(string icon, string tip, Action click)
    {
        var b = UiKit.Button(icon, tip: tip, click: click);
        b.Classes.Add("sq");
        b.Width = b.Height = b.MinWidth = 28;
        return b;
    }

    private Thickness Clamp(Thickness m, Size size)
    {
        var host = (Parent as Control)?.Bounds.Size ?? new Size(1000, 800);
        return new Thickness(Math.Clamp(m.Left, -size.Width + 80, Math.Max(0, host.Width - 80)), Math.Clamp(m.Top, 0, Math.Max(0, host.Height - 40)), 0, 0);
    }

    private void RememberPlacement() => _app.Settings.ReferenceBoardPlacement = [Margin.Left, Margin.Top, Width, Height];

    private void RestorePlacement()
    {
        if (_app.Settings.ReferenceBoardPlacement is [var x, var y, var w, var h] && double.IsFinite(x + y + w + h))
        {
            Margin = new Thickness(Math.Max(-200, x), Math.Max(0, y), 0, 0);
            Width = Math.Clamp(w, 200, 1600);
            Height = Math.Clamp(h, 160, 1200);
        }
        else Margin = new Thickness(80, 70, 0, 0);
    }

    // ================================================================== pictures in and out

    public async Task AddFromFilesAsync()
    {
        var files = await _app.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add reference pictures",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.dnk"] }, FilePickerFileTypes.All],
        });
        foreach (var f in files)
        {
            try
            {
                RasterImage img;
                if (f.TryGetLocalPath() is { } path && ImageFiles.IsProject(path)) img = ImageFiles.Open(path).Flatten();
                else
                {
                    await using var s = await f.OpenReadAsync();
                    img = ImageFiles.Decode(s);
                }
                Add(img, Path.GetFileNameWithoutExtension(f.Name));
            }
            catch (Exception ex)
            {
                _app.ShowToast($"Couldn't add {f.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Adds a picture (a copy) and shows it.</summary>
    public ReferenceItem Add(RasterImage image, string name)
    {
        var item = Board.Add(image, name);
        _view.Show(item);
        return item;
    }

    private IEnumerable<MenuEntry> MoreMenu()
    {
        foreach (var (hex, label) in new[] { ("#2B2522", "Dark background"), ("#77706A", "Grey background"), ("#F3ECE4", "Light background") })
        {
            var c = ColorRgb.Parse(hex);
            yield return new(null, label, null, () =>
            {
                Board.Background = c;
                Board.NotifyChanged();
                _view.InvalidateVisual();
            }, Checked: Board.Background == c);
        }
        yield return MenuEntry.Separator;
        yield return new("open", "Open board…", null, () => _ = OpenAsync());
        yield return new("save", "Save board as…", null, () => _ = SaveAsAsync(), Board.Items.Count > 0);
        yield return new("trash", "Remove all pictures", null, () =>
        {
            Board.Items.Clear();
            Board.NotifyChanged();
            _view.InvalidateVisual();
        }, Board.Items.Count > 0);
    }

    private static readonly FilePickerFileType BoardType = new("DoodleNook reference board") { Patterns = ["*" + ReferenceBoard.Extension] };

    private async Task OpenAsync()
    {
        var files = await _app.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open a reference board", FileTypeFilter = [BoardType, FilePickerFileTypes.All] });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            ReplaceBoard(ReferenceBoard.Load(path));
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowMessageAsync("Couldn't open the board", ex.Message);
        }
    }

    private async Task SaveAsAsync()
    {
        var file = await _app.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save reference board",
            SuggestedFileName = "References" + ReferenceBoard.Extension,
            DefaultExtension = ReferenceBoard.Extension.TrimStart('.'),
            FileTypeChoices = [BoardType],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            Board.Save(path);
            _app.ShowToast($"Board saved as {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowMessageAsync("Couldn't save the board", ex.Message);
        }
    }

    public void ReplaceBoard(ReferenceBoard board)
    {
        Board.Changed -= ScheduleSave;
        Board = board;
        Board.Changed += ScheduleSave;
        _view.InvalidateVisual();
        ScheduleSave();
    }

    // ================================================================== kept between sessions

    private string? SavedPath => _app.Session.Library.Root is { } root ? Path.Combine(root, "boards", "current" + ReferenceBoard.Extension) : null;

    private ReferenceBoard? LoadSaved()
    {
        try
        {
            return SavedPath is { } p && File.Exists(p) ? ReferenceBoard.Load(p) : null;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            return null;
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes the board to the library folder (in the background; pictures are never changed after loading).</summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        if (SavedPath is not { } path) return;
        var snapshot = new ReferenceBoard { Background = Board.Background, ViewX = Board.ViewX, ViewY = Board.ViewY, Zoom = Board.Zoom };
        foreach (var i in Board.Items) snapshot.Items.Add(new ReferenceItem(i.Id, i.Name, i.Image, i.X, i.Y, i.Width));
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                snapshot.Save(path);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        });
    }
}

/// <summary>The board's picture area: the pictures at their places, with pan, zoom, move and resize.</summary>
public sealed class ReferenceBoardView : Control
{
    private readonly ReferenceBoardPanel _panel;
    private readonly Dictionary<string, WriteableBitmap> _bitmaps = new();
    private ReferenceItem? _selected, _dragItem;
    private bool _panning, _resizing;
    private Point _last;

    public ReferenceBoardView(ReferenceBoardPanel panel)
    {
        _panel = panel;
        ClipToBounds = true;
        Focusable = false;
    }

    private ReferenceBoard Board => _panel.Board;

    public ReferenceItem? Selected => _selected;

    private Point ToBoard(Point p) => new(Board.ViewX + p.X / Board.Zoom, Board.ViewY + p.Y / Board.Zoom);

    private Rect ToView(ReferenceItem i) => new((i.X - Board.ViewX) * Board.Zoom, (i.Y - Board.ViewY) * Board.Zoom, i.Width * Board.Zoom, i.Height * Board.Zoom);

    private Rect Handle(ReferenceItem i)
    {
        var r = ToView(i);
        return new Rect(r.Right - 14, r.Bottom - 14, 14, 14);
    }

    /// <summary>Scrolls and zooms so one picture fills the view.</summary>
    public void Show(ReferenceItem item)
    {
        _selected = item;
        FitTo(item.X, item.Y, item.Width, item.Height);
    }

    public void FitAll()
    {
        if (Board.Items.Count == 0) return;
        double x0 = Board.Items.Min(i => i.X), y0 = Board.Items.Min(i => i.Y), x1 = Board.Items.Max(i => i.X + i.Width), y1 = Board.Items.Max(i => i.Y + i.Height);
        FitTo(x0, y0, x1 - x0, y1 - y0);
    }

    private void FitTo(double x, double y, double w, double h)
    {
        var size = Bounds.Size.Width > 0 ? Bounds.Size : new Size(360, 270);
        double zoom = Math.Clamp(Math.Min(size.Width / (w + 24), size.Height / (h + 24)), 0.05, 8);
        Board.Zoom = zoom;
        Board.ViewX = x + w / 2 - size.Width / 2 / zoom;
        Board.ViewY = y + h / 2 - size.Height / 2 / zoom;
        Board.NotifyChanged();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bg = Board.Background;
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B)), null, new Rect(Bounds.Size));
        if (Board.Items.Count == 0)
        {
            var hint = new FormattedText("Add pictures with +\nDrag to arrange · wheel to zoom", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Inter"), 13, new SolidColorBrush(DnColors.Text2)) { TextAlignment = TextAlignment.Center, MaxTextWidth = Math.Max(10, Bounds.Width - 20) };
            context.DrawText(hint, new Point(10, Bounds.Height / 2 - hint.Height / 2));
            return;
        }
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        foreach (var i in Board.Items)
        {
            if (!_bitmaps.TryGetValue(i.Id, out var bmp)) _bitmaps[i.Id] = bmp = BrushPreview.ToBitmap(i.Image);
            var r = ToView(i);
            context.DrawImage(bmp, r);
            if (ReferenceEquals(i, _selected))
            {
                context.DrawRectangle(null, new Pen(new SolidColorBrush(DnColors.Accent), 2), r);
                context.DrawRectangle(new SolidColorBrush(DnColors.Accent), null, new RoundedRect(Handle(i), 3));
            }
        }
    }

    private bool Picking(PointerEventArgs e) =>
        _panel.App.Session.Tool == Editor.ToolKind.Eyedropper || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || _panel.App.Canvas.Input?.AltHeld == true;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true; // never reaches the canvas
        var pt = e.GetCurrentPoint(this);
        var p = pt.Position;
        _last = p;
        var b = ToBoard(p);
        var item = Board.ItemAt(b.X, b.Y);
        if (pt.Properties.IsRightButtonPressed)
        {
            if (item is not null)
            {
                _selected = item;
                InvalidateVisual();
                var menu = UiKit.PopFlyout(Menus.Build(ItemMenu(item), () => { }, 220), PlacementMode.Pointer);
                menu.Content = Menus.Build(ItemMenu(item), menu.Hide, 220);
                menu.ShowAt(this, showAtPointer: true);
            }
            return;
        }
        if (item is not null && Picking(e))
        {
            if (Board.ColorAt(b.X, b.Y) is { } c)
            {
                _panel.App.Session.SetForeground(c);
                _panel.App.ShowToast($"Picked {c.ToHex()} from “{item.Name}”", c);
            }
            return;
        }
        if (_selected is not null && Handle(_selected).Contains(p))
        {
            _resizing = true;
            _dragItem = _selected;
        }
        else if (item is not null)
        {
            _selected = item;
            _dragItem = item;
            Board.BringToFront(item);
        }
        else
        {
            _selected = null;
            _panning = true;
        }
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var d = (p - _last) / Board.Zoom;
        _last = p;
        if (_resizing && _dragItem is { } r)
            r.Width = Math.Max(16, r.Width + d.X);
        else if (_dragItem is { } m)
        {
            m.X += d.X;
            m.Y += d.Y;
        }
        else if (_panning)
        {
            Board.ViewX -= d.X;
            Board.ViewY -= d.Y;
        }
        else
        {
            Cursor = _selected is not null && Handle(_selected).Contains(p) ? new Cursor(StandardCursorType.BottomRightCorner)
                : Picking(e) && Board.ItemAt(ToBoard(p).X, ToBoard(p).Y) is not null ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
            return;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragItem is not null || _panning) Board.NotifyChanged();
        _dragItem = null;
        _panning = _resizing = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        var before = ToBoard(p);
        Board.Zoom = Math.Clamp(Board.Zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.05, 8);
        Board.ViewX = before.X - p.X / Board.Zoom;
        Board.ViewY = before.Y - p.Y / Board.Zoom;
        Board.NotifyChanged();
        InvalidateVisual();
        e.Handled = true;
    }

    private IEnumerable<MenuEntry> ItemMenu(ReferenceItem item)
    {
        yield return MenuEntry.Heading(item.Name);
        yield return new("fit", "Show only this picture", null, () => Show(item));
        yield return new("zoom", "Actual size", null, () =>
        {
            item.Width = item.Image.Width;
            Board.NotifyChanged();
            InvalidateVisual();
        });
        yield return new("palette", "Palette from this picture…", null, () => _ = _panel.App.PaletteFromPictureAsync(item.Image));
        yield return new("trash", "Remove from the board", null, () =>
        {
            Board.Remove(item.Id);
            _bitmaps.Remove(item.Id);
            if (ReferenceEquals(_selected, item)) _selected = null;
            InvalidateVisual();
        });
    }
}
