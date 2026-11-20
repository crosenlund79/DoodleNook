using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Animation;

/// <summary>
/// Animation Studio's timeline along the bottom. Everyday controls only (Level 1): play/pause, the frame counter, new
/// frame, duplicate frame, onion-skin toggle and the frame rate. Onion and playback settings open from their controls
/// (Level 2); frame and cel commands are in the right-click menus, the ⋯ menu and the Animation menu (Level 3).
/// Drag the top edge to resize; the chevron collapses it to its bar.
/// </summary>
public sealed class TimelinePanel : Border
{
    public const double BarHeight = 42;
    public const double MinTimelineHeight = 120;

    private readonly MainWindow _app;
    private readonly TimelineGrid _grid;
    private readonly ScrollBar _scroll = new() { Orientation = Orientation.Horizontal, Height = 10, AllowAutoHide = false };
    private readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _play;
    private readonly TextBlock _counter = new() { Classes = { "num" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 92, TextAlignment = TextAlignment.Center };
    private readonly Button _onion;
    private readonly Button _fps;
    private readonly Button _hold;
    private readonly Button _collapse;
    private readonly Button _onionMore;
    private readonly Border _gridHost;
    private readonly List<Action> _refreshers = new();
    private Document? _watched;
    private bool _resizing;
    private double _resizeStartY, _resizeStartHeight;
    private bool _updatingScroll;

    public TimelinePanel(MainWindow app)
    {
        _app = app;
        Name = "Timeline";
        Background = new SolidColorBrush(DnColors.Ui);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(0, 1, 0, 0);
        _grid = new TimelineGrid(app) { Name = "TimelineGrid" };

        // ---- the resize grip (top edge)
        var grip = new Border
        {
            Height = 7,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
            Name = "TimelineGrip",
            Child = new Border { Width = 36, Height = 3, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(DnColors.Line), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        ToolTip.SetTip(grip, "Drag to make the timeline taller or shorter");
        grip.PointerPressed += (_, e) =>
        {
            if (_app.AnimationState.TimelineCollapsed) SetCollapsed(false);
            _resizing = true;
            _resizeStartY = e.GetPosition(_app).Y;
            _resizeStartHeight = Bounds.Height;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!_resizing) return;
            double h = _resizeStartHeight - (e.GetPosition(_app).Y - _resizeStartY);
            SetTimelineHeight(h);
        };
        grip.PointerReleased += (_, e) =>
        {
            _resizing = false;
            e.Pointer.Capture(null);
        };

        // ---- the bar
        _play = UiKit.Button("play", tip: "Play / pause (Enter)", click: () => _app.Animator.Toggle(), classes: "solid");
        _play.Name = "PlayButton";
        var newFrame = UiKit.Button("newframe", "New frame", "New blank frame after this one (N) — draw the next pose", () => _app.Session.NewFrame(duplicate: false));
        newFrame.Name = "NewFrameButton";
        var duplicate = UiKit.Button("dupframe", "Duplicate", "Copy this frame's drawing into a new frame to change it (Shift+N)", () => _app.Session.NewFrame(duplicate: true));
        duplicate.Name = "DuplicateFrameButton";

        _onion = UiKit.Button("onion", "Onion skin", "Show the previous and next drawings faintly (O)", () => _app.Session.ToggleOnion());
        _onion.Name = "OnionToggle";
        var onionMore = UiKit.Button("chevd", tip: "Onion-skin settings: how many drawings, strength, colours");
        onionMore.Width = 26;
        onionMore.MinWidth = 26;
        onionMore.Name = "OnionSettingsButton";
        onionMore.Flyout = UiKit.PopFlyout(new OnionSettingsPanel(app.Session), PlacementMode.TopEdgeAlignedLeft);
        _onionMore = onionMore;

        _fps = UiKit.Button(null, "12 fps", "Frame rate, looping and ping-pong");
        _fps.Name = "FpsChip";
        _fps.Flyout = UiKit.PopFlyout(new PlaybackSettingsPanel(app), PlacementMode.TopEdgeAlignedLeft);

        _hold = UiKit.Button(null, "", "This drawing is held over several frames: changing it changes all of them. Click to make this frame's drawing independent.",
            () => _app.Session.MakeIndependent(_app.Session.Document.ActiveLayer, _app.Session.Document.CurrentFrame));
        _hold.Name = "HoldHint";
        _hold.Background = new SolidColorBrush(DnColors.Ui2);

        var more = Menus.MenuButton(new DnIcon { Kind = "dots" }, () => _app.TimelineMoreMenu(), "More frame and drawing commands", 270, PlacementMode.TopEdgeAlignedRight);
        more.Classes.Add("sq");
        more.Name = "TimelineMore";
        _collapse = UiKit.Button("chevd", tip: "Collapse the timeline to this bar", click: () => SetCollapsed(!_app.AnimationState.TimelineCollapsed));
        _collapse.Name = "TimelineCollapse";

        var right = UiKit.Row(2, more, _collapse);
        var scroller = new ScrollViewer
        {
            Content = _left,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroller.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            double max = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
            scroller.Offset = new Vector(Math.Clamp(scroller.Offset.X - e.Delta.Y * 48, 0, max), 0);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var bar = new DockPanel { Height = BarHeight - 7, Margin = new Thickness(8, 0, 6, 0) };
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(scroller);

        _gridHost = new Border { Child = new DockPanel { Children = { WithDock(_scroll, Dock.Bottom), _grid } } };
        var root = new DockPanel();
        DockPanel.SetDock(grip, Dock.Top);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(grip);
        root.Children.Add(bar);
        root.Children.Add(_gridHost);
        Child = root;

        _scroll.Scroll += (_, _) =>
        {
            if (!_updatingScroll) _grid.SetScrollX(_scroll.Value);
        };
        _grid.ScrollChanged += SyncScroll;

        var onionRow = UiKit.Row(0, _onion, onionMore);

        void Rebuild()
        {
            _left.Children.Clear();
            _refreshers.Clear();
            _left.Children.Add(_play);
            bool nav = _app.AnimationState.IsShown(CustomOptions.FrameButtons, WorkspaceKind.Animation);
            if (nav) _left.Children.Add(Small("first", "First frame (Home)", () => _app.Session.GoToFrame(0), "FirstFrameButton"));
            if (nav) _left.Children.Add(Small("chevl", "Previous frame (, or ←)", () => _app.Session.StepFrame(-1), "PreviousFrameButton"));
            _left.Children.Add(_counter);
            if (nav) _left.Children.Add(Small("chevr", "Next frame (. or →)", () => _app.Session.StepFrame(+1), "NextFrameButton"));
            if (nav) _left.Children.Add(Small("last", "Last frame (End)", () => _app.Session.GoToFrame(_app.Session.Document.FrameCount - 1), "LastFrameButton"));
            _left.Children.Add(UiKit.Sep());
            _left.Children.Add(newFrame);
            _left.Children.Add(duplicate);
            _left.Children.Add(UiKit.Sep());
            _left.Children.Add(onionRow);
            if (_app.AnimationState.IsShown(CustomOptions.OnionOpacity, WorkspaceKind.Animation))
            {
                var (slider, refresh) = UiKit.Slider(null, 0.05, 1, () => _app.Session.Onion.Opacity, v => _app.Session.SetOnion(_app.Session.Onion with { Opacity = v }),
                    v => $"{v * 100:0}%", trackWidth: 80, tip: "Onion-skin strength");
                slider.Name = "OnionOpacitySlider";
                _left.Children.Add(slider);
                _refreshers.Add(refresh);
            }
            _left.Children.Add(UiKit.Sep());
            _left.Children.Add(_fps);
            if (_app.AnimationState.IsShown(CustomOptions.FrameDuration, WorkspaceKind.Animation)) _left.Children.Add(DurationControl());
            _left.Children.Add(_hold);
            Refresh();
        }

        app.WorkspaceChanged += Rebuild;
        app.CustomizationChanged += Rebuild;
        app.Session.DocumentReplaced += Watch;
        app.Session.OnionChanged += Refresh;
        app.Session.StateChanged += Refresh;
        app.Animator.PlaybackChanged += Refresh;
        Watch();
        Rebuild();
    }

    private static Control WithDock(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private static Button Small(string icon, string tip, Action click, string? name = null)
    {
        var b = UiKit.Button(icon, tip: tip, click: click);
        b.Name = name;
        b.Width = 30;
        b.MinWidth = 30;
        return b;
    }

    private Control DurationControl()
    {
        var text = new TextBlock { Classes = { "num" }, MinWidth = 64, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Document D() => _app.Session.Document;
        int Current() => D().Animation is { } a ? a.DurationOf(a.CurrentFrame) : 83;
        void Set(int? ms) => _app.Session.SetFrameDuration(D().CurrentFrame, ms);
        var minus = Small("minus", "Show this frame shorter", () => Set(Math.Max(10, Current() - 10)));
        var plus = Small("plus", "Show this frame longer", () => Set(Current() + 10));
        var auto = UiKit.Button(null, "Auto", "Use the frame rate for this frame", () => Set(null));
        auto.Height = 30;
        _refreshers.Add(() =>
        {
            var a = D().Animation;
            text.Text = a is null ? "—" : $"{a.DurationOf(a.CurrentFrame)} ms";
            auto.Classes.Set("on", a is not null && a.DurationOverride(a.CurrentFrame) is null);
        });
        var row = UiKit.Row(2, UiKit.Label("Duration", size: 12), minus, text, plus, auto);
        row.Name = "FrameDurationControl";
        ToolTip.SetTip(row, "How long this frame is shown (Auto follows the frame rate)");
        return row;
    }

    private void Watch()
    {
        if (_watched is not null)
        {
            _watched.FrameChanged -= Refresh;
            _watched.AnimationChanged -= Refresh;
            _watched.ActiveLayerChanged -= Refresh;
            _watched.HistoryChanged -= Refresh;
        }
        _watched = _app.Session.Document;
        _watched.FrameChanged += Refresh;
        _watched.AnimationChanged += Refresh;
        _watched.ActiveLayerChanged += Refresh;
        _watched.HistoryChanged += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        var doc = _app.Session.Document;
        var a = doc.Animation;
        bool playing = _app.Animator.IsPlaying;
        _play.Content = UiKit.Content(playing ? "pause" : "play", null);
        _play.Classes.Set("on", playing);
        ToolTip.SetTip(_play, playing ? "Pause (Enter)" : doc.FrameCount > 1 ? "Play (Enter)" : "Play (Enter) — add a frame first");
        _counter.Text = $"Frame {doc.CurrentFrame + 1} / {doc.FrameCount}";
        var onion = _app.Session.Onion;
        _onion.Classes.Set("on", onion.Enabled);
        double fps = a?.Fps ?? Engine.Documents.Animation.DefaultFps;
        _fps.Content = UiKit.Content(a is { Loop: true } || a is null ? "loop" : null, $"{fps:0.##} fps");
        var (s, e) = doc.HoldRange(doc.ActiveLayer, doc.CurrentFrame);
        bool held = a is not null && doc.CelAt(doc.ActiveLayer, doc.CurrentFrame) is not null && e > s;
        _hold.IsVisible = held && !playing;
        if (held) _hold.Content = UiKit.Content(null, $"Held {e - s + 1} frames · Make independent");
        foreach (var r in _refreshers) r();
        _collapse.Content = UiKit.Content(_app.AnimationState.TimelineCollapsed ? "chevu" : "chevd", null);
        ToolTip.SetTip(_collapse, _app.AnimationState.TimelineCollapsed ? "Show the timeline" : "Collapse the timeline to this bar");
        _grid.InvalidateVisual();
    }

    private void SyncScroll()
    {
        _updatingScroll = true;
        _scroll.Maximum = Math.Max(0, _grid.ContentWidth - _grid.ViewportWidth);
        _scroll.ViewportSize = Math.Max(1, _grid.ViewportWidth);
        _scroll.Value = _grid.ScrollX;
        _scroll.IsVisible = _scroll.Maximum > 0.5;
        _updatingScroll = false;
    }

    /// <summary>Applies the remembered height (or the collapsed bar).</summary>
    public void ApplyLayout()
    {
        bool collapsed = _app.AnimationState.TimelineCollapsed;
        _gridHost.IsVisible = !collapsed;
        Height = collapsed ? BarHeight : Math.Clamp(_app.AnimationState.TimelineHeight, MinTimelineHeight, MaxHeightNow);
        Refresh();
    }

    private double MaxHeightNow => Math.Max(MinTimelineHeight + 40, (_app.Bounds.Height > 0 ? _app.Bounds.Height : 800) * 0.6);

    public void SetTimelineHeight(double height)
    {
        _app.AnimationState.TimelineHeight = Math.Clamp(height, MinTimelineHeight, MaxHeightNow);
        _app.AnimationState.TimelineCollapsed = false;
        ApplyLayout();
    }

    /// <summary>Opens the onion-skin settings pop-over (Animation menu).</summary>
    public void OpenOnionSettings()
    {
        if (_onionMore.IsEffectivelyVisible) _onionMore.Flyout?.ShowAt(_onionMore);
    }

    /// <summary>Opens the frame rate and looping pop-over (Animation menu).</summary>
    public void OpenPlaybackSettings()
    {
        if (_fps.IsEffectivelyVisible) _fps.Flyout?.ShowAt(_fps);
    }

    public void SetCollapsed(bool collapsed)
    {
        _app.AnimationState.TimelineCollapsed = collapsed;
        ApplyLayout();
    }
}

/// <summary>Onion-skin settings (opened from the ▾ next to the onion toggle).</summary>
public sealed class OnionSettingsPanel : StackPanel
{
    private static readonly ColorRgb[] BeforeTints = [OnionSkinSettings.DefaultBeforeTint, new(232, 150, 60), new(190, 80, 160), new(120, 110, 100)];
    private static readonly ColorRgb[] AfterTints = [OnionSkinSettings.DefaultAfterTint, new(80, 170, 110), new(60, 170, 180), new(120, 110, 100)];
    private readonly Editor.EditorSession _session;
    private readonly List<Action> _refresh = new();

    public OnionSettingsPanel(Editor.EditorSession session)
    {
        _session = session;
        Width = 270;
        Spacing = 8;
        Name = "OnionSettings";
        Children.Add(UiKit.Heading("Onion skin"));
        OnionSkinSettings S() => session.Onion;
        void Set(Func<OnionSkinSettings, OnionSkinSettings> f) => session.SetOnion(f(S()));

        var (enabled, r0) = UiKit.Toggle("onion", "Show onion skins", "On/off (O)", () => S().Enabled, v => Set(s => s with { Enabled = v }));
        _refresh.Add(r0);
        Children.Add(enabled);
        Children.Add(Stepper("Before", "Previous drawings to show", () => S().Before, v => Set(s => s with { Before = v })));
        Children.Add(Stepper("After", "Next drawings to show", () => S().After, v => Set(s => s with { After = v })));
        var (opacity, r1) = UiKit.Slider(null, 0.05, 1, () => S().Opacity, v => Set(s => s with { Opacity = v }), v => $"{v * 100:0}%", trackWidth: 120);
        _refresh.Add(r1);
        Children.Add(UiKit.Row(6, Label("Strength"), opacity));
        Children.Add(Tints("Before tint", BeforeTints, () => S().BeforeTint, c => Set(s => s with { BeforeTint = c })));
        Children.Add(Tints("After tint", AfterTints, () => S().AfterTint, c => Set(s => s with { AfterTint = c })));
        var (falloff, r2) = UiKit.Check("Fade distant drawings", "Drawings further away are fainter", () => S().Falloff, v => Set(s => s with { Falloff = v }));
        var (all, r3) = UiKit.Check("Whole frames (all layers)", "Show every layer of the neighbouring frames, not just the active layer", () => S().AllLayers, v => Set(s => s with { AllLayers = v }));
        _refresh.Add(r2);
        _refresh.Add(r3);
        Children.Add(falloff);
        Children.Add(all);
        var reset = UiKit.Button(null, "Reset to defaults", null, () => session.SetOnion(session.PixelMode ? OnionSkinSettings.Default with { Opacity = 0.3, Falloff = false } : OnionSkinSettings.Default));
        reset.Background = new SolidColorBrush(DnColors.Ui3);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(reset);
        Children.Add(new TextBlock { Text = "Hand-drawn and pixel-art animation keep their own onion settings.", Classes = { "dim" }, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        session.OnionChanged += Refresh;
        session.StateChanged += Refresh;
        Refresh();
    }

    private static TextBlock Label(string s)
    {
        var t = UiKit.Label(s, size: 12);
        t.Width = 74;
        return t;
    }

    private Control Stepper(string label, string tip, Func<int> get, Action<int> set)
    {
        var value = new TextBlock { Classes = { "num" }, Width = 24, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var minus = UiKit.Button("minus", tip: $"Fewer ({tip.ToLowerInvariant()})", click: () => set(Math.Max(0, get() - 1)));
        var plus = UiKit.Button("plus", tip: $"More ({tip.ToLowerInvariant()})", click: () => set(Math.Min(5, get() + 1)));
        foreach (var b in new[] { minus, plus })
        {
            b.Width = 28;
            b.MinWidth = 28;
            b.Height = 28;
        }
        _refresh.Add(() => value.Text = get().ToString());
        var row = UiKit.Row(4, Label(label), minus, value, plus);
        ToolTip.SetTip(row, tip);
        return row;
    }

    private Control Tints(string label, ColorRgb[] options, Func<ColorRgb> get, Action<ColorRgb> set)
    {
        var row = UiKit.Row(4, Label(label));
        foreach (var c in options)
        {
            var colour = c;
            var b = UiKit.Button(null, null, colour.ToHex(), () => set(colour));
            b.Width = 28;
            b.MinWidth = 28;
            b.Height = 28;
            b.Padding = new Thickness(0);
            var dot = UiKit.Swatch(c, 20, 10);
            b.Content = dot;
            _refresh.Add(() =>
            {
                bool on = get() == colour;
                dot.BorderBrush = new SolidColorBrush(on ? DnColors.Text : Color.FromArgb(90, 0, 0, 0));
                dot.BorderThickness = new Thickness(on ? 2 : 1);
            });
            row.Children.Add(b);
        }
        return row;
    }

    private void Refresh()
    {
        foreach (var r in _refresh) r();
    }
}

/// <summary>Frame rate, looping and ping-pong (opened from the fps chip). Changes are undoable.</summary>
public sealed class PlaybackSettingsPanel : StackPanel
{
    private readonly MainWindow _app;
    private readonly List<Action> _refresh = new();
    private double _sliderFps = -1;

    public PlaybackSettingsPanel(MainWindow app)
    {
        _app = app;
        Width = 280;
        Spacing = 8;
        Name = "PlaybackSettings";
        Children.Add(UiKit.Heading("Playback"));
        Engine.Documents.Animation? A() => app.Session.Document.Animation;
        double Fps() => _sliderFps > 0 ? _sliderFps : A()?.Fps ?? Engine.Documents.Animation.DefaultFps;
        bool Loop() => A()?.Loop ?? true;
        bool PingPong() => A()?.PingPong ?? false;
        void Apply(double fps, bool loop, bool ping) => app.Session.SetTiming(fps, loop, ping);

        var (presets, r0) = UiKit.Segmented(
            [(6.0, null, "6", "6 frames per second"), (8.0, null, "8", "8 fps"), (12.0, null, "12", "12 fps — classic hand-drawn timing"),
             (15.0, null, "15", "15 fps"), (24.0, null, "24", "24 fps — film"), (30.0, null, "30", "30 fps")],
            () => Math.Round(Fps()), v => Apply(v, Loop(), PingPong()), compact: true);
        _refresh.Add(r0);
        Children.Add(presets);
        var slider = new DnSlider { Minimum = 1, Maximum = 60, TrackWidth = 150 };
        var value = new TextBlock { Classes = { "num" }, MinWidth = 50, VerticalAlignment = VerticalAlignment.Center };
        slider.UserChanged += (_, v) =>
        {
            _sliderFps = Math.Round(v);
            value.Text = $"{_sliderFps:0} fps";
        };
        slider.UserFinished += (_, _) =>
        {
            double fps = _sliderFps;
            _sliderFps = -1;
            if (fps > 0) Apply(fps, Loop(), PingPong());
        };
        _refresh.Add(() =>
        {
            slider.Value = Fps();
            value.Text = $"{Fps():0.##} fps";
        });
        Children.Add(UiKit.Row(4, slider, value));
        var (loop, r1) = UiKit.Check("Loop", "Play again from the start", Loop, v => Apply(Fps(), v, PingPong()));
        var (ping, r2) = UiKit.Check("Ping-pong", "Play forwards, then backwards", PingPong, v => Apply(Fps(), Loop(), v));
        _refresh.Add(r1);
        _refresh.Add(r2);
        Children.Add(UiKit.Row(4, loop, ping));
        var info = new TextBlock { Classes = { "dim" }, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        _refresh.Add(() =>
        {
            var a = A();
            info.Text = a is null ? "Add a frame to start animating." : $"{a.FrameCount} frames · {a.TotalDurationMs / 1000.0:0.##} s per pass. Hold drawings longer by dragging a block's end in the timeline.";
        });
        Children.Add(info);
        app.Session.DocumentReplaced += Watch;
        Watch();

        void Watch()
        {
            app.Session.Document.AnimationChanged += Refresh;
            Refresh();
        }
    }

    private void Refresh()
    {
        foreach (var r in _refresh) r();
    }
}
