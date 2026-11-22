using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Animation;

/// <summary>
/// Mission 015 — the Live Animation Preview (View › Animation preview). It loops the whole animation on its own clock,
/// independent of the frame being edited, so you can draw on frame 3 while every frame keeps playing. Docked it sits in
/// the corner of Animation Studio's canvas (drag its lower-left corner to resize it); detached it is a window of its own
/// that you can put anywhere, also on a second screen.
/// <list type="bullet">
/// <item>Play/pause, loop, zoom (fit, or 1–4 screen pixels per picture pixel) and a background behind transparent
/// areas (display only, never exported).</item>
/// <item>Live: a frame is drawn again only when something that decides how it looks changed
/// (<see cref="Document.FrameSignature"/>), and never in the middle of a stroke, so drawing stays as responsive as ever.</item>
/// <item>Nothing runs while it is hidden, paused or minimised; its drawings are released when it closes.</item>
/// </list>
/// </summary>
public sealed class AnimationPreview : Border
{
    // Styled like a plain Border (the "float" card), not as a type of its own.
    protected override Type StyleKeyOverride => typeof(Border);

    private const long CacheBudget = 160L * 1024 * 1024;
    private const double ReRenderEveryMs = 250;

    private sealed record Entry(ulong Key, WriteableBitmap Bitmap, long Bytes, double RenderedAt, int Scale);

    private readonly MainWindow _app;
    private readonly Image _image = new() { Stretch = Stretch.Fill, Name = "AnimationPreviewImage" };
    private readonly Border _backdrop = new() { CornerRadius = new CornerRadius(4), ClipToBounds = true };
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<int, Entry> _frames = new();
    private readonly TextBlock _label = new() { Classes = { "dim", "num" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Name = "AnimationPreviewFrame" };
    private readonly Button _play, _loop, _zoom, _background, _detach;
    private readonly Border _grip;
    private long _bytes;
    private double _offsetMs;
    private Document? _watched;
    private bool _pendingRefresh;
    private bool _resizing;
    private Point _resizeStart;
    private double _resizeStartWidth;

    public AnimationPreview(MainWindow app)
    {
        _app = app;
        Name = "AnimationPreview";
        Classes.Add("float");
        Padding = new Thickness(6);
        Dock();

        _play = Small("pause", "Pause the preview (the timeline and your editing frame are not affected)", TogglePlay);
        _play.Name = "PreviewPlay";
        _loop = Small("loop", "Loop: play again from the start", () =>
        {
            Settings.Loop = !Settings.Loop;
            _offsetMs = 0;
            _clock.Restart();
            RefreshButtons();
            Wake();
        });
        _loop.Name = "PreviewLoop";
        _zoom = Menus.MenuButton(new TextBlock { Text = "Fit", FontSize = 12, Name = "PreviewZoomText" }, ZoomMenu, "Zoom: fit the panel, or exact pixels", 200);
        _zoom.Name = "PreviewZoom";
        _zoom.Height = 24;
        _background = Menus.MenuButton(UiKit.Content("paper", null), () => Backdrops.Menu(app, Settings.Backdrop, SetBackdrop),
            "Background behind transparent areas (display only, never exported)", 240);
        _background.Name = "PreviewBackground";
        _background.Height = 24;
        _background.Width = 28;
        _detach = Small("expand", "Open the preview in its own window", () => app.SetPreviewFloating(!Floating));
        _detach.Name = "PreviewDetach";
        var close = Small("close", "Hide the preview (View › Animation preview)", () => app.SetPreviewPanel(false));
        close.Name = "PreviewClose";

        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 4) };
        var rightButtons = UiKit.Row(2, _detach, close);
        DockPanel.SetDock(rightButtons, Avalonia.Controls.Dock.Right);
        head.Children.Add(rightButtons);
        head.Children.Add(UiKit.Row(6, new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, _label));

        var foot = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var right = UiKit.Row(2, _zoom, _background);
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        foot.Children.Add(right);
        foot.Children.Add(UiKit.Row(2, _play, _loop));

        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.None);
        var stage = new Panel { Children = { _backdrop, _image } };
        _scroll = new ScrollViewer
        {
            Content = stage,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        _grip = new Border
        {
            Width = 14, Height = 14, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.BottomLeftCorner), Margin = new Thickness(-6, 0, 0, -6),
            Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M2 4 L10 12 M2 8 L6 12"), Stroke = new SolidColorBrush(DnColors.Text2), StrokeThickness = 1.4 },
        };
        ToolTip.SetTip(_grip, "Drag to resize the preview");
        _grip.PointerPressed += (_, e) =>
        {
            _resizing = true;
            _resizeStart = e.GetPosition(app);
            _resizeStartWidth = Width;
            e.Pointer.Capture(_grip);
            e.Handled = true;
        };
        _grip.PointerMoved += (_, e) =>
        {
            if (!_resizing) return;
            Width = Math.Clamp(_resizeStartWidth - (e.GetPosition(app).X - _resizeStart.X), 160, 900);
            Layout();
        };
        _grip.PointerReleased += (_, e) =>
        {
            _resizing = false;
            e.Pointer.Capture(null);
            app.AnimationState.PreviewWidth = Width;
        };

        var body = new DockPanel();
        DockPanel.SetDock(head, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(foot, Avalonia.Controls.Dock.Bottom);
        body.Children.Add(head);
        body.Children.Add(foot);
        body.Children.Add(_scroll);
        Child = new Panel { Children = { body, _grip } };
        _timer.Tick += (_, _) => Tick();
        app.Session.DocumentReplaced += Watch;
        app.Session.StateChanged += () => Wake(); // a stroke ended, the tool changed…
        _scroll.SizeChanged += (_, _) => Layout();
        Watch();
        RefreshButtons();
        ApplyBackdrop();
    }

    private PreviewSettings Settings => _app.PreviewSettings;

    /// <summary>True while the preview is in its own window.</summary>
    public bool Floating { get; private set; }

    /// <summary>True while it plays (paused: it stays on the frame it showed).</summary>
    public bool Playing { get; private set; } = true;

    /// <summary>The frame shown now (tests).</summary>
    internal int ShownFrame { get; private set; } = -1;

    /// <summary>How many frames were drawn for the preview since it opened (tests: live updates redraw only what changed).</summary>
    internal int Renders { get; private set; }

    /// <summary>Whether the clock is running (tests: nothing runs while hidden or paused).</summary>
    internal bool TimerRunning => _timer.IsEnabled;

    /// <summary>For tests: the preview's clock is moved by hand.</summary>
    internal double? ManualElapsedMs { get; set; }

    /// <summary>A frame that keeps changing (being drawn on) is drawn again at most this often.</summary>
    internal double MinRedrawMs { get; set; } = ReRenderEveryMs;

    private static Button Small(string icon, string tip, Action click)
    {
        var b = UiKit.Button(icon, tip: tip, click: click);
        b.Width = b.Height = b.MinWidth = 24;
        return b;
    }

    // ================================================================== docking

    /// <summary>In the corner of the canvas (docked).</summary>
    internal void Dock()
    {
        Floating = false;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 14, 14, 0);
        Width = Math.Clamp(_app.AnimationState.PreviewWidth, 160, 900);
        CornerRadius = new CornerRadius(12);
        if (_grip is not null) _grip.IsVisible = true;
        RefreshButtons();
    }

    /// <summary>Filling its own window (floating).</summary>
    internal void Float()
    {
        Floating = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Margin = new Thickness(0);
        Width = double.NaN;
        Height = double.NaN;
        CornerRadius = new CornerRadius(0);
        _grip.IsVisible = false;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        if (_play is null) return;
        _play.Content = UiKit.Content(Playing ? "pause" : "play", null);
        ToolTip.SetTip(_play, Playing ? "Pause the preview (the timeline and your editing frame are not affected)" : "Play the preview");
        _loop.Classes.Set("on", Settings.Loop);
        _detach.Content = UiKit.Content(Floating ? "fit" : "expand", null);
        ToolTip.SetTip(_detach, Floating ? "Put the preview back in the corner of the canvas" : "Open the preview in its own window");
        if (_zoom.Content is TextBlock t) t.Text = Settings.Zoom == 0 ? "Fit" : $"{Settings.Zoom}×";
    }

    private IEnumerable<MenuEntry> ZoomMenu()
    {
        yield return new("fit", "Fit the panel", null, () => SetZoom(0), Checked: Settings.Zoom == 0);
        foreach (int z in new[] { 1, 2, 3, 4 })
            yield return new("zoom", z == 1 ? "Actual size (1×)" : $"{z}×", null, () => SetZoom(z), Checked: Settings.Zoom == z);
    }

    /// <summary>0 = fit; 1–4 = screen pixels per picture pixel.</summary>
    public void SetZoom(int zoom)
    {
        Settings.Zoom = Math.Clamp(zoom, 0, 4);
        Release();
        RefreshButtons();
        Layout();
        Wake();
    }

    public void SetBackdrop(BackdropSetting b)
    {
        Settings.Backdrop = b;
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        _backdrop.Background = Backdrops.BrushFor(Settings.Backdrop);
        _background.Content = UiKit.Content(Settings.Backdrop.Kind == BackdropKind.Picture ? "image" : "paper", null);
        ToolTip.SetTip(_background, $"Background behind transparent areas: {Backdrops.Describe(Settings.Backdrop)} (display only, never exported)");
    }

    // ================================================================== playing

    public void TogglePlay()
    {
        if (Playing) Pause();
        else Play();
    }

    public void Play()
    {
        if (Playing) return;
        Playing = true;
        // A finished one-time play starts again from the beginning.
        if (!Settings.Loop && ShownFrame >= _app.Session.Document.FrameCount - 1) _offsetMs = 0;
        _clock.Restart();
        RefreshButtons();
        Wake();
    }

    public void Pause()
    {
        if (!Playing) return;
        _offsetMs = ElapsedMs();
        _clock.Reset();
        Playing = false;
        _timer.Stop();
        RefreshButtons();
    }

    private double ElapsedMs() => ManualElapsedMs ?? _offsetMs + _clock.Elapsed.TotalMilliseconds;

    private bool Showing => IsEffectivelyVisible && (!Floating || TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Minimized });

    /// <summary>Starts the clock when there is something to play and someone to see it.</summary>
    private void Wake()
    {
        if (!Showing) return;
        var doc = _app.Session.Document;
        bool animated = doc.Animation is { FrameCount: > 1 };
        if (Playing && animated && ManualElapsedMs is null)
        {
            if (!_clock.IsRunning) _clock.Start();
            _timer.Start();
        }
        else _timer.Stop();
        if (!_pendingRefresh)
        {
            _pendingRefresh = true;
            Dispatcher.UIThread.Post(() =>
            {
                _pendingRefresh = false;
                Tick();
            }, DispatcherPriority.Background);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;
        if (IsVisible)
        {
            if (!Floating) Width = Math.Clamp(_app.AnimationState.PreviewWidth, 160, 900);
            Layout();
            Wake();
        }
        else
        {
            _timer.Stop();
            Release();
        }
    }

    /// <summary>Lets go of every drawn frame (closing, or a new document).</summary>
    internal void Release()
    {
        _frames.Clear();
        _bytes = 0;
        ShownFrame = -1;
        _image.Source = null;
    }

    private void Watch()
    {
        if (_watched is not null)
        {
            _watched.HistoryChanged -= OnChanged;
            _watched.AnimationChanged -= OnChanged;
            _watched.LayersChanged -= OnChanged;
            _watched.SizeChanged -= OnResized;
        }
        _watched = _app.Session.Document;
        _watched.HistoryChanged += OnChanged;
        _watched.AnimationChanged += OnChanged;
        _watched.LayersChanged += OnChanged;
        _watched.SizeChanged += OnResized;
        Release();
        _offsetMs = 0;
        _clock.Restart();
        if (!Playing) _clock.Reset();
        Layout();
        Wake();
    }

    private void OnResized()
    {
        Release();
        Layout();
        Wake();
    }

    private void OnChanged() => Wake();

    /// <summary>The frame due now on the preview's own clock (-1 past the end of a one-time play).</summary>
    private int FrameDue(Document doc)
    {
        if (doc.Animation is not { FrameCount: > 1 } a) return 0;
        var clock = PlaybackClock.For(a, 0);
        double pass = clock.PingPong ? clock.PassDurationMs * 2 : clock.PassDurationMs;
        double t = ElapsedMs();
        if (Settings.Loop) t %= Math.Max(1, pass);
        else if (t >= pass) return clock.PingPong ? 0 : a.FrameCount - 1;
        int f = clock.FrameAt(t);
        return f < 0 ? a.FrameCount - 1 : f;
    }

    /// <summary>Shows the frame due now, drawing it again only if it changed (also called by tests).</summary>
    internal void Tick()
    {
        if (!Showing) return;
        var doc = _app.Session.Document;
        int frame = Math.Clamp(FrameDue(doc), 0, doc.FrameCount - 1);
        if (!Settings.Loop && Playing && doc.Animation is { FrameCount: > 1 } a && ElapsedMs() >= PlaybackClock.For(a, 0).PassDurationMs * (a.PingPong ? 2 : 1))
        {
            // Played once: stop on the last frame.
            Pause();
        }
        _label.Text = $"{frame + 1}/{doc.FrameCount}";
        ulong key = doc.FrameSignature(frame);
        int scale = RenderScale(doc);
        _frames.TryGetValue(frame, out var entry);
        bool stale = entry is null || entry.Key != key || entry.Scale != scale;
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000;
        // While a stroke is being drawn, the drawing being changed is redrawn when the stroke ends (it can't hold up the pen);
        // after that, at most every quarter second while it keeps changing.
        bool mayRender = entry is null || (!_app.Session.IsStrokeActive && now - entry.RenderedAt >= MinRedrawMs) || entry.Scale != scale;
        if (stale && mayRender)
        {
            entry = Render(doc, frame, key, scale, now);
            Store(frame, entry);
        }
        else if (stale && !_app.Session.IsStrokeActive) Dispatcher.UIThread.Post(Wake, DispatcherPriority.Background); // come back for it
        if (frame == ShownFrame && ReferenceEquals(_image.Source, entry?.Bitmap)) return;
        ShownFrame = frame;
        _image.Source = entry?.Bitmap;
        Layout();
    }

    /// <summary>
    /// How finely the preview draws: exact pixels at 1–4× (a picture pixel never blurs), and in fit mode just enough for
    /// the panel (larger pictures are drawn smaller: 1 = full size, 2 = half, …).
    /// </summary>
    private int RenderScale(Document doc)
    {
        if (Settings.Zoom > 0) return 1;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double want = Math.Max(_scroll.Bounds.Width, 120) * scaling;
        double ratio = doc.Width / Math.Max(1, want);
        return Math.Max(1, (int)Math.Floor(ratio));
    }

    private Entry Render(Document doc, int frame, ulong key, int scale, double now)
    {
        var full = doc.RenderFrame(frame);
        RasterImage shown = full;
        if (scale > 1)
        {
            int w = Math.Max(1, full.Width / scale), h = Math.Max(1, full.Height / scale);
            shown = _app.Session.PixelMode ? Nearest(full, w, h) : Engine.Operations.Resampler.Resize(full, w, h, Engine.Operations.Interpolation.Bilinear);
        }
        Renders++;
        return new Entry(key, BrushPreview.ToBitmap(shown), (long)shown.Width * shown.Height * 4, now, scale);
    }

    private static RasterImage Nearest(RasterImage src, int w, int h)
    {
        var dst = new RasterImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                dst[x, y] = src[Math.Min(src.Width - 1, (int)((x + 0.5) * src.Width / w)), Math.Min(src.Height - 1, (int)((y + 0.5) * src.Height / h))];
        return dst;
    }

    private void Store(int frame, Entry entry)
    {
        if (_frames.Remove(frame, out var old)) _bytes -= old.Bytes;
        // Within the budget: the frames farthest from this one go first.
        while (_bytes + entry.Bytes > CacheBudget && _frames.Count > 0)
        {
            int far = _frames.Keys.MaxBy(k => Math.Abs(k - frame));
            _bytes -= _frames[far].Bytes;
            _frames.Remove(far);
        }
        _frames[frame] = entry;
        _bytes += entry.Bytes;
    }

    /// <summary>Sizes the picture: fit the panel (keeping its shape), or whole screen pixels per picture pixel.</summary>
    private void Layout()
    {
        var doc = _app.Session.Document;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double w, h;
        if (Settings.Zoom > 0)
        {
            w = doc.Width * Settings.Zoom / scaling;
            h = doc.Height * Settings.Zoom / scaling;
            _scroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        }
        else
        {
            double availW = Floating ? Math.Max(60, _scroll.Bounds.Width) : Math.Max(60, (double.IsNaN(Width) ? 240 : Width) - 12);
            double availH = Floating ? Math.Max(60, _scroll.Bounds.Height) : double.PositiveInfinity;
            double s = Math.Min(availW / doc.Width, availH / doc.Height);
            w = Math.Max(1, doc.Width * s);
            h = Math.Max(1, doc.Height * s);
            _scroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        }
        _image.Width = _backdrop.Width = w;
        _image.Height = _backdrop.Height = h;
        // Docked: as tall as the picture, but never more than about half the canvas.
        if (!Floating) _scroll.MaxHeight = Math.Max(80, (Parent as Control)?.Bounds.Height * 0.55 ?? 400);
        else _scroll.MaxHeight = double.PositiveInfinity;
        bool exact = Settings.Zoom > 0 || _app.Session.PixelMode;
        RenderOptions.SetBitmapInterpolationMode(_image, exact ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        // Centred in a floating window.
        _image.HorizontalAlignment = _backdrop.HorizontalAlignment = Floating ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _image.VerticalAlignment = _backdrop.VerticalAlignment = Floating ? VerticalAlignment.Center : VerticalAlignment.Top;
    }
}

/// <summary>The Live Animation Preview in a window of its own (Mission 015). Closing it hides the preview.</summary>
public sealed class PreviewWindow : Window
{
    private readonly MainWindow _app;
    internal bool ClosingFromApp;

    public PreviewWindow(MainWindow app, AnimationPreview preview)
    {
        _app = app;
        Name = "PreviewWindow";
        Title = "Animation preview — DoodleNook";
        ShowInTaskbar = false;
        CanResize = true;
        MinWidth = 220;
        MinHeight = 180;
        Background = new SolidColorBrush(DnColors.Ui);
        var s = app.PreviewSettings;
        Width = Math.Clamp(s.WindowWidth, MinWidth, 3000);
        Height = Math.Clamp(s.WindowHeight, MinHeight, 3000);
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (s.WindowX is { } x && s.WindowY is { } y && OnScreen(app, x, y)) Position = new PixelPoint(x, y);
        else Position = new PixelPoint(app.Position.X + (int)Math.Max(0, app.Bounds.Width - Width - 40), app.Position.Y + 90);
        Content = preview;
        PositionChanged += (_, _) => Remember();
        Resized += (_, _) => Remember();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) preview.Dispatcher.Post(() => preview.InvalidateArrange());
        };
        KeyDown += (_, e) =>
        {
            // Space plays and pauses here too; drawing shortcuts belong to the main window.
            if (e.Key == Key.Space)
            {
                preview.TogglePlay();
                e.Handled = true;
            }
        };
        Closing += (_, _) =>
        {
            Remember();
            Content = null;
            if (!ClosingFromApp) _app.PreviewWindowClosedByUser(); // the × closes the preview (it stays closed)
        };
    }

    private static bool OnScreen(Window owner, int x, int y) =>
        owner.Screens.All.Count == 0 || owner.Screens.All.Any(sc => sc.WorkingArea.Contains(new PixelPoint(x + 40, y + 20)));

    private void Remember()
    {
        var s = _app.PreviewSettings;
        s.WindowX = Position.X;
        s.WindowY = Position.Y;
        if (Width > 0 && Height > 0)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
    }
}
