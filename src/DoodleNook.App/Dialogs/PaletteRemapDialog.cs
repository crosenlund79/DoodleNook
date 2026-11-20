using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.App.Dialogs;

/// <summary>What the remap dialog is asked to do.</summary>
/// <param name="Title">Dialog title.</param>
/// <param name="Target">The palette to start with (null: the first library palette).</param>
/// <param name="ForceLock">The picture will be locked to the palette (locking, replacing or removing a palette colour).</param>
/// <param name="ChoosePalette">Whether another palette can be chosen in the dialog.</param>
/// <param name="MatchFrom">An older version of the same palette: its colours go to the new colour at the same position.</param>
/// <param name="Intro">A sentence explaining why the dialog appeared.</param>
public sealed record RemapRequest(string Title, Palette? Target = null, bool ForceLock = false, bool ChoosePalette = true,
    IReadOnlyList<ColorRgb>? MatchFrom = null, string? Intro = null);

/// <summary>
/// Remap to Palette: the picture's colours (or groups of similar colours) on the left, each with the palette colour it
/// will become; the palette below; a before/after preview of the real picture on the right (zoom with the wheel, drag to
/// pan, frames and playback for animations). Click a row, then a palette colour, to change where it goes. Nothing
/// happens to the picture until Apply, which is one undo step.
/// </summary>
public sealed class PaletteRemapDialog : Grid
{
    private readonly MainWindow _app;
    private readonly Document _doc;
    private readonly RemapRequest _request;
    private readonly StackPanel _rows = new() { Spacing = 2, Name = "RemapRows" };
    private readonly WrapPanel _targets = new() { ItemWidth = 30, ItemHeight = 30, Name = "RemapTargets" };
    private readonly TextBlock _summary = new() { Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    // Fixed height: the hint changes when a row is selected, and the palette below must not jump under the pointer.
    private readonly TextBlock _hint = new() { Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Height = 34 };
    private readonly TextBlock _status = new() { Classes = { "dim" }, FontSize = 12, Height = 18 };
    private readonly TextBlock _paletteName = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, MaxWidth = 210, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly RemapPreview _preview = new() { Name = "RemapPreview" };
    private readonly Dictionary<(int Frame, bool After), Bitmap> _cache = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer _player = new();
    private readonly Action _refreshStyle;
    private Action _refreshFrame = () => { };
    private int _selected = -1;
    private int _version;
    private int _frame;
    private bool _after = true;
    private bool _busy;
    private bool _playing;

    private PaletteRemapDialog(MainWindow app, RemapRequest request, Palette target, PaletteRemap remap)
    {
        _app = app;
        _doc = app.Session.Document;
        _request = request;
        Target = target;
        Remap = remap;
        IncludeHidden = true;
        LockAfter = request.ForceLock || _doc.PaletteLocked;
        _frame = _doc.IsAnimated ? _doc.CurrentFrame : 0;
        Name = "PaletteRemapDialog";
        ColumnDefinitions = new ColumnDefinitions("330,18,*");

        // ---------------------------------------------------------------- left: the mapping
        var left = new StackPanel { Spacing = 8 };
        if (request.Intro is not null) left.Children.Add(new TextBlock { Text = request.Intro, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        Control paletteRow = request.ChoosePalette
            ? Menus.MenuButton(UiKit.Row(6, new DnIcon { Kind = "palette" }, _paletteName, new DnIcon { Kind = "chevd", Width = 14, Height = 14 }), PaletteChoices,
                "The palette to remap to", 260)
            : UiKit.Row(6, new DnIcon { Kind = "palette" }, _paletteName);
        if (paletteRow is Button pb)
        {
            pb.Name = "RemapPaletteChooser";
            pb.Height = 30;
            pb.HorizontalAlignment = HorizontalAlignment.Left;
        }
        left.Children.Add(UiKit.Row(8, new TextBlock { Text = "To", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }, paletteRow));
        var (style, refreshStyle) = UiKit.Segmented(
            [(RemapStyle.Closest, null, "Closest colours", "Each colour becomes the palette colour that looks most alike; colours already in the palette stay"),
             (RemapStyle.LightAndDark, null, "Keep light and dark", "Stretches the picture's darkest to lightest over the palette's, then takes the closest: keeps contrast")],
            () => Remap.Style, v =>
            {
                Remap.Style = v;
                Changed();
            }, compact: true);
        _refreshStyle = refreshStyle;
        left.Children.Add(style);
        left.Children.Add(_summary);
        left.Children.Add(new ScrollViewer { Content = _rows, MaxHeight = 214, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        left.Children.Add(_hint);
        left.Children.Add(_targets);
        var reset = UiKit.Button("undo", "Suggested colours", "Back to the automatic suggestion everywhere", () =>
        {
            Remap.Reset();
            Changed();
        });
        reset.Height = 28;
        reset.Name = "RemapReset";
        left.Children.Add(reset);
        if (_doc.Layers.Any(l => !l.Visible))
        {
            var (hidden, refreshHidden) = UiKit.Check("Hidden layers too", "Also recolour layers that are hidden (always when locking)", () => IncludeHidden || LockAfter, v =>
            {
                IncludeHidden = v;
                _ = ReanalyseAsync();
            });
            hidden.IsEnabled = !LockAfter;
            left.Children.Add(hidden);
            _ = refreshHidden;
        }
        var (lockBox, _) = UiKit.Check("Lock the picture to this palette", "Afterwards only these colours can be used (transparency stays free)", () => LockAfter, v =>
        {
            LockAfter = v;
            if (v && !IncludeHidden)
            {
                IncludeHidden = true;
                _ = ReanalyseAsync();
            }
        });
        lockBox.Name = "RemapLock";
        lockBox.IsEnabled = !request.ForceLock;
        left.Children.Add(lockBox);
        Children.Add(left);

        // ---------------------------------------------------------------- right: the preview
        var right = new StackPanel { Spacing = 8 };
        SetColumn(right, 2);
        var (compare, refreshCompare) = UiKit.Segmented([(false, null, "Before", "The picture as it is"), (true, null, "After", "The picture remapped")],
            () => _after, v =>
            {
                _after = v;
                ShowFrame();
            }, compact: true);
        compare.Name = "RemapCompare";
        _ = refreshCompare;
        var fit = UiKit.Button("fit", null, "Show the whole picture (double-click the preview)", _preview.Fit);
        fit.Height = 28;
        right.Children.Add(UiKit.Row(8, compare, fit, new TextBlock { Text = "Wheel zooms · drag pans", Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }));
        right.Children.Add(new Border { Child = _preview, Height = 290, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = new SolidColorBrush(DnColors.Ui2) });
        if (_doc.IsAnimated && _doc.FrameCount > 1)
        {
            var (frame, refreshFrame) = UiKit.Slider("Frame", 1, _doc.FrameCount, () => _frame + 1, v =>
            {
                _frame = (int)Math.Round(v) - 1;
                ShowFrame();
            }, v => $"{Math.Round(v):0}", trackWidth: 220);
            _refreshFrame = refreshFrame;
            var play = UiKit.Button("play", null, "Play the remapped animation");
            play.Name = "RemapPlay";
            play.Height = 28;
            play.Click += (_, _) =>
            {
                _playing = !_playing;
                play.Content = new DnIcon { Kind = _playing ? "pause" : "play" };
                if (_playing)
                {
                    _player.Interval = TimeSpan.FromMilliseconds(Math.Max(40, 1000.0 / Math.Max(1, _doc.Animation!.Fps)));
                    _player.Start();
                }
                else _player.Stop();
            };
            right.Children.Add(UiKit.Row(8, play, frame));
        }
        right.Children.Add(_status);
        Children.Add(right);

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = UpdateAfterAsync(_frame);
        };
        _player.Tick += (_, _) =>
        {
            _frame = (_frame + 1) % _doc.FrameCount;
            _refreshFrame();
            ShowFrame();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _player.Stop();
            _debounce.Stop();
        };
        Rebuild();
        Changed();
    }

    public Palette Target { get; private set; }
    public PaletteRemap Remap { get; private set; }
    public bool IncludeHidden { get; private set; }
    public bool LockAfter { get; private set; }

    /// <summary>The row being edited (-1: none).</summary>
    public int SelectedRow => _selected;

    /// <summary>Shows the dialog; returns true when the remap was applied.</summary>
    public static async Task<bool> ShowAsync(MainWindow app, RemapRequest request)
    {
        var doc = app.Session.Document;
        var target = request.Target ?? DefaultTarget(app);
        app.ShowToast("Looking at the picture's colours…");
        var remap = await Task.Run(() => Analyse(doc, target, true, request.MatchFrom));
        var dialog = new PaletteRemapDialog(app, request, target, remap);
        app.LastRemapDialog = dialog;
        var r = await app.Dialogs.ShowAsync(request.Title, dialog,
            [("cancel", "Cancel", false), ("apply", request.ForceLock || dialog.LockAfter ? "Apply and lock" : "Apply", true)], "apply", "cancel", 880);
        if (r != "apply") return false;
        dialog.Remap.Apply(doc, dialog.Target.Name, dialog.IncludeHidden, dialog.LockAfter, dialog.Target.LibraryId,
            dialog.LockAfter && !doc.PaletteLocked ? "Remap and lock to palette" : "Remap to palette");
        app.ShowToast(dialog.LockAfter ? $"Remapped and locked to “{dialog.Target.Name}”" : $"Remapped to “{dialog.Target.Name}”");
        return true;
    }

    private static Palette DefaultTarget(MainWindow app)
    {
        var lib = app.Session.Library;
        var p = lib.FindPalette(app.Session.ActivePaletteId) ?? lib.Palettes[0];
        return new Palette(p.Name, p.Colors) { LibraryId = p.Id };
    }

    private static PaletteRemap Analyse(Document doc, Palette target, bool includeHidden, IReadOnlyList<ColorRgb>? matchFrom)
    {
        var remap = PaletteRemap.Analyse(doc, target.Colors, includeHidden);
        if (matchFrom is not null) remap.MatchByPosition(matchFrom);
        return remap;
    }

    // ================================================================== mapping editor

    private IEnumerable<MenuEntry> PaletteChoices()
    {
        if (_doc.PaletteLocked)
            yield return new(null, $"The picture's palette ({_doc.Palette.Name})", null, () => _ = ChangeTargetAsync(_doc.Palette.Clone()),
                Checked: Target.LibraryId == _doc.Palette.LibraryId && Target.Colors.SequenceEqual(_doc.Palette.Colors));
        foreach (var group in _app.Session.Library.Palettes.GroupBy(p => p.Collection))
        {
            yield return MenuEntry.Heading(group.Key);
            foreach (var p in group)
            {
                var palette = new Palette(p.Name, p.Colors) { LibraryId = p.Id };
                yield return new(null, $"{p.Name} ({p.Colors.Count})", null, () => _ = ChangeTargetAsync(palette), Checked: Target.LibraryId == p.Id);
            }
        }
    }

    /// <summary>Switches to another palette (tests call this directly).</summary>
    public async Task ChangeTargetAsync(Palette palette)
    {
        Target = palette;
        await ReanalyseAsync();
    }

    private async Task ReanalyseAsync()
    {
        _status.Text = "Looking at the colours…";
        var target = Target;
        bool hidden = IncludeHidden || LockAfter;
        var style = Remap.Style;
        Remap = await Task.Run(() => Analyse(_doc, target, hidden, _request.MatchFrom));
        Remap.Style = style;
        _selected = -1;
        _cache.Clear();
        Rebuild();
        Changed();
    }

    private void Rebuild()
    {
        _paletteName.Text = Target.Name;
        _refreshStyle();
        _summary.Text = Remap.Grouped
            ? $"The picture has {Remap.DistinctColours:N0} colours, shown as {Remap.Sources.Count} groups of similar colours (most used first). Each colour gets its own closest match; choosing a colour for a group sends all of it there."
            : $"The picture has {Remap.DistinctColours} colour{(Remap.DistinctColours == 1 ? "" : "s")} (most used first).";
        if (Remap.Sources.Count == 0) _summary.Text = "The picture has no colours yet: only the palette changes.";
    }

    /// <summary>Selects a row (tests call this; clicking a row does the same).</summary>
    public void SelectRow(int row)
    {
        _selected = row;
        RefreshRows();
    }

    /// <summary>Sends the selected row to a palette colour (tests call this; clicking a palette swatch does the same).</summary>
    public void AssignSelected(int target)
    {
        if (_selected < 0) return;
        Remap.Assign(_selected, target);
        Changed();
    }

    private void Changed()
    {
        _version++;
        foreach (var key in _cache.Keys.Where(k => k.After).ToList()) _cache.Remove(key);
        RefreshRows();
        _debounce.Stop();
        _debounce.Start();
        ShowFrame();
    }

    private void RefreshRows()
    {
        _rows.Children.Clear();
        for (int i = 0; i < Remap.Sources.Count; i++)
        {
            var src = Remap.Sources[i];
            int target = Remap.TargetOf(i);
            var mark = Remap.IsChosen(i) ? "your choice" : Remap.IsMixed(i) ? "mostly" : "";
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("26,Auto,*,22,26,78"), Height = 26 };
            row.Children.Add(UiKit.Swatch(src.Colour, 22, 6, outline: true));
            var label = new TextBlock
            {
                Text = src.IsGroup ? $"{src.Members.Count} colours" : src.Colour.ToHex(), FontSize = 12, Classes = { "dim" },
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0),
            };
            SetColumn(label, 1);
            row.Children.Add(label);
            var arrow = new DnIcon { Kind = "chevr", Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            SetColumn(arrow, 3);
            row.Children.Add(arrow);
            var to = UiKit.Swatch(Remap.Target[target], 22, 6, outline: true);
            SetColumn(to, 4);
            row.Children.Add(to);
            var note = new TextBlock { Text = mark, FontSize = 11, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            if (Remap.IsChosen(i)) note.Foreground = new SolidColorBrush(DnColors.Accent);
            SetColumn(note, 5);
            row.Children.Add(note);
            var b = UiKit.Button(null, null, src.IsGroup
                ? $"{src.Members.Count} similar colours, {Share(src.Weight)} of the picture → {Remap.Target[target].ToHex()}"
                : $"{src.Colour.ToHex()}, {Share(src.Weight)} of the picture → {Remap.Target[target].ToHex()}");
            b.Content = row;
            b.Height = 30;
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.Name = $"RemapRow_{i}";
            if (i == _selected) b.Classes.Add("on");
            int index = i;
            b.Click += (_, _) => SelectRow(index);
            _rows.Children.Add(b);
        }
        _hint.Text = _selected < 0 ? "Click a colour above, then the palette colour it should become:" : "Now click the palette colour it should become:";
        var used = Enumerable.Range(0, Remap.Sources.Count).Select(Remap.TargetOf).ToHashSet();
        _targets.Children.Clear();
        for (int t = 0; t < Remap.Target.Count; t++)
        {
            var c = Remap.Target[t];
            var dot = UiKit.Swatch(c, 24, 6, outline: true);
            var frame = UiKit.Chosen(dot, _selected >= 0 && Remap.TargetOf(_selected) == t, 7);
            frame.Opacity = used.Contains(t) || _selected >= 0 ? 1 : 0.55;
            var b = UiKit.Button(null, null, $"{c.ToHex()}{(used.Contains(t) ? "" : " · not used yet")}");
            b.Content = frame;
            b.Width = 30;
            b.Height = 30;
            b.MinWidth = 30;
            b.Padding = new Thickness(0);
            b.Name = $"RemapTarget_{t}";
            int index = t;
            b.Click += (_, _) => AssignSelected(index);
            _targets.Children.Add(b);
        }
    }

    private string Share(long weight)
    {
        long total = Remap.Sources.Sum(s => s.Weight);
        double pct = total == 0 ? 0 : 100.0 * weight / total;
        return pct < 1 ? "under 1 %" : $"{pct:0} %";
    }

    // ================================================================== preview

    private void ShowFrame()
    {
        int f = _frame;
        if (!_after)
        {
            if (!_cache.TryGetValue((f, false), out var before)) _cache[(f, false)] = before = BrushPreview.ToBitmap(_doc.RenderFrame(f));
            _preview.SetImage(before, _doc.Width, _doc.Height);
            _status.Text = "Before";
            return;
        }
        if (_cache.TryGetValue((f, true), out var after))
        {
            _preview.SetImage(after, _doc.Width, _doc.Height);
            _status.Text = _doc.IsAnimated ? $"After · frame {f + 1}" : "After";
        }
        else if (!_busy) _ = UpdateAfterAsync(f);
    }

    private async Task UpdateAfterAsync(int frame)
    {
        if (_busy || _cache.ContainsKey((frame, true))) return;
        _busy = true;
        int version = _version;
        _status.Text = "Updating the preview…";
        var remap = Remap.Clone();
        bool hidden = IncludeHidden || LockAfter;
        try
        {
            var image = await Task.Run(() => remap.Preview(_doc, frame, hidden));
            if (version == _version)
            {
                // Keep memory modest for long, large animations: forget frames when the cache grows big.
                if (_cache.Count >= 48) foreach (var key in _cache.Keys.Where(k => k.Frame != frame).ToList()) _cache.Remove(key);
                _cache[(frame, true)] = BrushPreview.ToBitmap(image);
            }
        }
        finally
        {
            _busy = false;
        }
        ShowFrame();
    }

    /// <summary>What the preview shows ("Before", "After", "After · frame 3", or "Updating the preview…").</summary>
    internal string PreviewStatus => _status.Text ?? "";

    /// <summary>Selects a frame of an animation in the preview.</summary>
    internal void ShowPreviewFrame(int frame, bool after = true)
    {
        _frame = Math.Clamp(frame, 0, Math.Max(0, _doc.FrameCount - 1));
        _after = after;
        _refreshFrame();
        ShowFrame();
    }

    /// <summary>The remapped frame, computed now (tests compare it with the result).</summary>
    internal RasterImage PreviewNow(int frame) => Remap.Preview(_doc, frame, IncludeHidden || LockAfter);
}

/// <summary>The remap preview: a picture you can zoom (wheel, around the pointer) and pan (drag); double-click fits it.
/// Pixels stay crisp when enlarged.</summary>
public sealed class RemapPreview : Control
{
    private Bitmap? _image;
    private int _w, _h;
    private double _zoom = 1, _x, _y;
    private bool _fitted;
    private Point? _drag;

    public RemapPreview()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void SetImage(Bitmap image, int width, int height)
    {
        bool sizeChanged = width != _w || height != _h;
        _image = image;
        _w = width;
        _h = height;
        if (sizeChanged || !_fitted) Fit();
        InvalidateVisual();
    }

    public void Fit()
    {
        if (_w == 0 || Bounds.Width <= 0) return;
        _zoom = Math.Min(Bounds.Width / _w, Bounds.Height / _h);
        if (_zoom > 1) _zoom = Math.Floor(_zoom); // whole steps keep pixel art crisp
        _x = (Bounds.Width - _w * _zoom) / 2;
        _y = (Bounds.Height - _h * _zoom) / 2;
        _fitted = true;
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Fit();
    }

    public override void Render(DrawingContext context)
    {
        if (_image is null) return;
        var dest = new Rect(_x, _y, _w * _zoom, _h * _zoom);
        context.FillRectangle(UiKit.CheckerBrush(), dest);
        using var quality = context.PushRenderOptions(new RenderOptions
        {
            BitmapInterpolationMode = _zoom >= 1.5 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality,
        });
        context.DrawImage(_image, new Rect(0, 0, _image.Size.Width, _image.Size.Height), dest);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        double factor = e.Delta.Y > 0 ? 1.25 : 0.8;
        double zoom = Math.Clamp(_zoom * factor, 0.05, 64);
        _x = p.X - (p.X - _x) * zoom / _zoom;
        _y = p.Y - (p.Y - _y) * zoom / _zoom;
        _zoom = zoom;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount == 2)
        {
            Fit();
            return;
        }
        _drag = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } d) return;
        var p = e.GetPosition(this);
        _x += p.X - d.X;
        _y += p.Y - d.Y;
        _drag = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }
}
