using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.References;

namespace DoodleNook.App.Dialogs;

/// <summary>What the Reference cycle dialog decided: the cycle and how it is placed, and the layer's opacity.</summary>
public sealed record ReferenceCycleChoice(ReferenceCycleSettings Settings, double Opacity);

/// <summary>
/// Mission 013: Animation › Insert reference cycle… (and Adjust reference cycle…). One compact window: the library's
/// cycles by category with their drawing counts on the left; on the right a live preview of the chosen cycle alone on a
/// plain page the size of the canvas, where a drag places the figure's feet; the view and facing, the figure's height, where
/// the cycle starts and how long each drawing lasts, repeating, the opacity and the ground marker. A line says exactly
/// what happens to the timeline before anything changes.
/// </summary>
public sealed class ReferenceCycleDialog
{
    private const double PreviewWidth = 500, PreviewHeight = 270;

    private readonly MainWindow _app;
    private readonly Document _doc;
    private ReferenceCycleSettings _s;
    private double _opacity;
    private readonly bool _adjusting;
    private readonly double _scale;

    private readonly Avalonia.Controls.Canvas _stage = new() { Width = PreviewWidth, Height = PreviewHeight, ClipToBounds = true, Name = "ReferencePreview" };
    private readonly Image _figure = new() { Stretch = Stretch.None, IsHitTestVisible = false, Name = "ReferencePreviewFigure" };
    private readonly StackPanel _list = new() { Spacing = 1, Name = "ReferenceCycleList" };
    private readonly ContentControl _facingHost = new();
    private readonly TextBlock _plan = new() { Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Name = "ReferencePlan" };
    private readonly TextBlock _about = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Name = "ReferenceAbout" };
    private readonly List<Action> _refreshers = new();
    private readonly DispatcherTimer _timer = new();
    private List<Bitmap> _frames = new();
    private int _tick;
    private (double X, double Y) _frameOrigin;

    private ReferenceCycleDialog(MainWindow app, ReferenceCycleSettings initial, double opacity, bool adjusting)
    {
        _app = app;
        _doc = app.Session.Document;
        _s = initial.Normalized();
        _opacity = opacity;
        _adjusting = adjusting;
        _scale = Math.Min(PreviewWidth / _doc.Width, PreviewHeight / _doc.Height);
    }

    /// <summary>Sensible first settings for this picture: a walk, from the side, about two thirds of the canvas tall,
    /// standing near the bottom middle; hard pixel edges for pixel art.</summary>
    public static ReferenceCycleSettings Defaults(Document doc, bool pixelArt, ReferenceCycleSettings? last = null)
    {
        double height = Math.Round(doc.Height * (Math.Max(doc.Width, doc.Height) <= 256 ? 0.75 : 0.6));
        var fresh = new ReferenceCycleSettings("walk", ReferenceView.Side, ReferenceFacing.Right, Math.Max(12, height), Math.Round(doc.Width / 2.0),
            Math.Round(doc.Height * 0.88), HardEdges: pixelArt);
        if (last is null) return fresh;
        // The last choices (cycle, view, facing, timing), placed afresh when they don't suit this picture's size.
        bool fits = last.AnchorX <= doc.Width && last.AnchorY <= doc.Height && last.Height <= doc.Height * 1.5;
        return last with
        {
            StartFrame = 0,
            HardEdges = pixelArt,
            Height = fits ? last.Height : fresh.Height,
            AnchorX = fits ? last.AnchorX : fresh.AnchorX,
            AnchorY = fits ? last.AnchorY : fresh.AnchorY,
        };
    }

    public static async Task<ReferenceCycleChoice?> AskAsync(MainWindow app, ReferenceCycleSettings initial, double opacity, bool adjusting)
    {
        var d = new ReferenceCycleDialog(app, initial, opacity, adjusting);
        var body = d.Build();
        d._timer.Tick += (_, _) => d.Advance();
        d.Restart();
        try
        {
            var scroller = new ScrollViewer { Content = body, MaxHeight = Math.Max(360, app.Bounds.Height - 150), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var r = await app.Dialogs.ShowAsync(adjusting ? "Adjust reference cycle" : "Insert reference cycle", scroller,
                [("cancel", "Cancel", false), ("ok", adjusting ? "Apply" : "Insert", true)], "ok", "cancel", 800);
            return r == "ok" ? new ReferenceCycleChoice(d._s, d._opacity) : null;
        }
        finally
        {
            d._timer.Stop();
        }
    }

    private ReferenceCycle Cycle => ReferenceLibrary.Find(_s.CycleId) ?? ReferenceLibrary.Cycles[0];

    private Control Build()
    {
        // ---- the library (left)
        foreach (var category in ReferenceLibrary.Categories)
        {
            _list.Children.Add(new TextBlock { Text = category, Classes = { "dim" }, FontSize = 11, Margin = new Thickness(6, 6, 0, 2) });
            foreach (var cycle in ReferenceLibrary.Cycles.Where(c => c.Category == category))
            {
                var c = cycle;
                var b = UiKit.Button(null, null, c.Description, () => Choose(c));
                b.Content = new DockPanel
                {
                    Children =
                    {
                        WithDock(new TextBlock { Text = c.Frames.ToString(CultureInfo.InvariantCulture), Classes = { "dim", "num" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }, Dock.Right),
                        new TextBlock { Text = c.Name, VerticalAlignment = VerticalAlignment.Center },
                    },
                };
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                b.Height = 28;
                b.Name = "ReferenceCycle_" + c.Id;
                _list.Children.Add(b);
                _refreshers.Add(() =>
                {
                    b.Classes.Set("on", c.Id == _s.CycleId);
                    b.IsEnabled = c.Supports(_s.View);
                    ToolTip.SetTip(b, c.Supports(_s.View) ? $"{c.Description} ({c.Frames} drawings, made for {c.Fps:0.#} fps)" : $"{c.Name} isn't drawn in {ReferenceInsert.ViewName(_s.View)} view");
                });
            }
        }
        var left = new ScrollViewer { Content = _list, Width = 196, MaxHeight = 560, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // ---- the preview (right): the chosen cycle alone, playing on a plain page the size of the canvas; drag to place
        // the feet. (Mission 016: the scene used to be drawn behind it, so references inserted earlier looked like part
        // of the cycle being chosen.)
        var page = new Border
        {
            Width = _doc.Width * _scale,
            Height = _doc.Height * _scale,
            Background = _doc.Paper is { } paper && _doc.PaperVisible ? new SolidColorBrush(Color.FromRgb(paper.R, paper.G, paper.B)) : Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Name = "ReferencePreviewPage",
        };
        Avalonia.Controls.Canvas.SetLeft(page, (PreviewWidth - page.Width) / 2);
        Avalonia.Controls.Canvas.SetTop(page, (PreviewHeight - page.Height) / 2);
        _stage.Children.Add(page);
        _stage.Children.Add(_figure);
        _stage.Background = UiKit.CheckerBrush();
        RenderOptions.SetBitmapInterpolationMode(_figure, BitmapInterpolationMode.None);
        bool dragging = false;
        void Place(PointerEventArgs e)
        {
            var p = e.GetPosition(_stage);
            double x = (p.X - (PreviewWidth - _doc.Width * _scale) / 2) / _scale, y = (p.Y - (PreviewHeight - _doc.Height * _scale) / 2) / _scale;
            _s = _s with { AnchorX = Math.Round(Math.Clamp(x, 0, _doc.Width)), AnchorY = Math.Round(Math.Clamp(y, 0, _doc.Height)) };
            PositionFigure();
        }
        _stage.PointerPressed += (_, e) =>
        {
            dragging = true;
            e.Pointer.Capture(_stage);
            Place(e);
            e.Handled = true;
        };
        _stage.PointerMoved += (_, e) =>
        {
            if (dragging) Place(e);
        };
        _stage.PointerReleased += (_, e) =>
        {
            dragging = false;
            e.Pointer.Capture(null);
        };
        ToolTip.SetTip(_stage, "Drag to place the figure: the cross is where its feet stand");
        var stageFrame = new Border { Child = _stage, CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };

        // ---- options
        var (view, refreshView) = UiKit.Segmented(
            [(ReferenceView.Side, null, "Side", "Profile, as in platformers"), (ReferenceView.TopDown, null, "Top-down", "Straight down from above"),
             (ReferenceView.ThreeQuarter, null, "Three-quarter", "From above at an angle, as in RPGs")],
            () => _s.View, SetView);
        _refreshers.Add(refreshView);
        RebuildFacing();

        double maxHeight = Math.Max(32, Math.Round(_doc.Height * 1.5));
        var (size, refreshSize) = UiKit.Slider(null, Math.Min(8, maxHeight), maxHeight, () => _s.Height, v =>
        {
            _s = _s with { Height = Math.Round(v) };
            Changed();
        }, v => $"{v:0} px", trackWidth: 170, tip: "How tall the figure stands");
        size.Name = "ReferenceHeight";
        _refreshers.Add(refreshSize);

        int current = _doc.Animation?.CurrentFrame ?? 0;
        var startOptions = new List<(int, string?, string?, string?)> { (0, null, "Frame 1", "Start at the first frame") };
        if (current > 0) startOptions.Add((current, null, $"Frame {current + 1}", "Start at the frame you are on"));
        var (start, refreshStart) = UiKit.Segmented(startOptions, () => _s.StartFrame == current && current > 0 ? current : 0, v =>
        {
            _s = _s with { StartFrame = v };
            Changed(redraw: false);
        });
        _refreshers.Add(refreshStart);
        var (hold, refreshHold) = UiKit.Segmented(
            [(1, null, "1", "Each drawing on one frame"), (2, null, "2", "Each drawing held for two frames (on twos)"), (3, null, "3", "Each drawing held for three frames")],
            () => _s.Hold, v =>
            {
                _s = _s with { Hold = v };
                Changed(redraw: false);
            });
        _refreshers.Add(refreshHold);

        var (repeat, refreshRepeat) = UiKit.Check("Repeat to the end", "Keep playing the cycle until the last frame", () => _s.Repeat, v =>
        {
            _s = _s with { Repeat = v };
            Changed(redraw: false);
        });
        repeat.Name = "ReferenceRepeat";
        _refreshers.Add(refreshRepeat);
        var (extend, refreshExtend) = UiKit.Check("Add frames if needed", "If the animation is shorter than the cycle, add frames at the end (other layers hold their last drawing there)",
            () => _s.Extend, v =>
            {
                _s = _s with { Extend = v };
                Changed(redraw: false);
            });
        extend.Name = "ReferenceExtend";
        _refreshers.Add(refreshExtend);
        var (marker, refreshMarker) = UiKit.Check("Ground marker", "A small cross where the feet stand, on every drawing", () => _s.Marker, v =>
        {
            _s = _s with { Marker = v };
            Changed();
        });
        _refreshers.Add(refreshMarker);
        var (opacity, refreshOpacity) = UiKit.Slider(null, 0.05, 1, () => _opacity, v =>
        {
            _opacity = v;
            _figure.Opacity = v;
            _refreshers.ForEach(r => r());
        }, v => $"{v * 100:0}%", trackWidth: 120, tip: "How strongly the reference shows (you can change it later in the timeline)");
        opacity.Name = "ReferenceOpacity";
        _refreshers.Add(refreshOpacity);

        var right = new StackPanel { Spacing = 8 };
        right.Children.Add(stageFrame);
        right.Children.Add(_about);
        right.Children.Add(UiKit.Row(8, Dim("View", 78), view));
        right.Children.Add(UiKit.Row(8, Dim("Facing", 78), _facingHost));
        right.Children.Add(UiKit.Row(8, Dim("Height", 78), size));
        right.Children.Add(UiKit.Row(8, Dim("Starts at", 78), start, Dim("Each drawing lasts"), hold, Dim("frames")));
        right.Children.Add(UiKit.Row(8, Dim("", 78), repeat, extend));
        right.Children.Add(UiKit.Row(8, Dim("Opacity", 78), opacity, marker));
        right.Children.Add(_plan);
        if (_adjusting)
            right.Children.Add(DialogHost.Text("The layer's drawings are made again from these settings; anything painted on it by hand is replaced."));

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*"), Name = "ReferenceCycleDialog" };
        root.Children.Add(left);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        Changed();
        return root;
    }

    private static T WithDock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static TextBlock Dim(string text, double? width = null)
    {
        var t = new TextBlock { Text = text, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center };
        if (width is { } w) t.Width = w;
        return t;
    }

    private void Choose(ReferenceCycle cycle)
    {
        _s = _s with { CycleId = cycle.Id, Repeat = cycle.Loop };
        Changed();
    }

    private void SetView(ReferenceView view)
    {
        bool fromAbove = view == ReferenceView.TopDown, wasFromAbove = _s.View == ReferenceView.TopDown;
        _s = _s with { View = view, Facing = ReferenceRenderer.DefaultFacing(view) };
        // From straight above the anchor is the middle of the body; otherwise the feet.
        if (fromAbove != wasFromAbove)
            _s = _s with { AnchorY = Math.Round(Math.Clamp(_s.AnchorY + (fromAbove ? -0.45 : 0.45) * _s.Height, 0, _doc.Height)) };
        if (!Cycle.Supports(view)) _s = _s with { CycleId = "walk", Repeat = true };
        RebuildFacing();
        Changed();
    }

    private void RebuildFacing()
    {
        static string Arrow(ReferenceFacing f) => f switch
        {
            ReferenceFacing.Right => "→",
            ReferenceFacing.Left => "←",
            ReferenceFacing.Up => "↑",
            ReferenceFacing.Down => "↓",
            ReferenceFacing.DownRight => "↘",
            ReferenceFacing.DownLeft => "↙",
            ReferenceFacing.UpRight => "↗",
            _ => "↖",
        };
        static string Tip(ReferenceFacing f) => f switch
        {
            ReferenceFacing.Right => "Facing right",
            ReferenceFacing.Left => "Facing left",
            ReferenceFacing.Up => "Facing up",
            ReferenceFacing.Down => "Facing down",
            ReferenceFacing.DownRight => "Facing towards you, to the right",
            ReferenceFacing.DownLeft => "Facing towards you, to the left",
            ReferenceFacing.UpRight => "Facing away, to the right",
            _ => "Facing away, to the left",
        };
        var (facing, refresh) = UiKit.Segmented(ReferenceRenderer.FacingsFor(_s.View).Select(f => (f, (string?)null, (string?)Arrow(f), (string?)Tip(f))),
            () => _s.Facing, f =>
            {
                _s = _s with { Facing = f };
                Changed();
            });
        facing.Name = "ReferenceFacing";
        _facingHost.Content = facing;
        refresh();
        _facingRefresh = refresh;
    }

    private Action? _facingRefresh;

    /// <summary>Settings changed: refresh the controls, the explanation and (when needed) the preview's drawings.</summary>
    private void Changed(bool redraw = true)
    {
        foreach (var r in _refreshers) r();
        _facingRefresh?.Invoke();
        var cycle = Cycle;
        _about.Text = $"{cycle.Name}: {cycle.Description}";
        _plan.Text = PlanText(cycle);
        if (redraw) Redraw();
        else Restart();
    }

    /// <summary>What happens to the timeline, in words, before anything changes.</summary>
    private string PlanText(ReferenceCycle cycle)
    {
        int frames = _doc.Animation?.FrameCount ?? 1;
        var plan = ReferenceInsert.Plan(cycle, _s, frames);
        string text = $"{cycle.Frames} drawings, made for {cycle.Fps:0.#} fps · frames {plan.Start + 1}–{plan.End}";
        if (plan.Plays > 1.01) text += $" · plays {plan.Plays:0.#} times";
        if (!_doc.IsAnimated) text += " · the picture becomes an animation";
        if (plan.AddedFrames > 0)
            text += $" · adds {plan.AddedFrames} frame{(plan.AddedFrames == 1 ? "" : "s")} at the end (other layers hold their last drawing)";
        else if (plan.End - plan.Start < cycle.Frames * _s.Hold)
            text += " · stops at the last frame";
        text += _adjusting ? "." : ". Existing layers and drawings are not changed.";
        return text;
    }

    /// <summary>Draws the cycle's drawings at preview size, around its anchor.</summary>
    private void Redraw()
    {
        foreach (var f in _frames) f.Dispose();
        _frames.Clear();
        double h = Math.Max(4, _s.Height * _scale);
        int w = (int)Math.Ceiling(h * 3.4), ht = (int)Math.Ceiling(h * 3.4);
        _frameOrigin = (w / 2.0, ht * 0.62);
        if (_s.View == ReferenceView.TopDown) _frameOrigin = (w / 2.0, ht / 2.0);
        var framing = _s.Framing with { Height = h, AnchorX = _frameOrigin.X, AnchorY = _frameOrigin.Y };
        var cycle = Cycle;
        for (int i = 0; i < cycle.Frames; i++) _frames.Add(BrushPreview.ToBitmap(ReferenceRenderer.Render(cycle.Pose(i), w, ht, framing)));
        PositionFigure();
        Restart();
    }

    private void PositionFigure()
    {
        double ox = (PreviewWidth - _doc.Width * _scale) / 2, oy = (PreviewHeight - _doc.Height * _scale) / 2;
        Avalonia.Controls.Canvas.SetLeft(_figure, ox + _s.AnchorX * _scale - _frameOrigin.X);
        Avalonia.Controls.Canvas.SetTop(_figure, oy + _s.AnchorY * _scale - _frameOrigin.Y);
    }

    /// <summary>Plays the preview at the animation's frame rate, each drawing held as chosen.</summary>
    private void Restart()
    {
        _tick = 0;
        double fps = _doc.Animation?.Fps ?? Engine.Documents.Animation.DefaultFps;
        _timer.Interval = TimeSpan.FromMilliseconds(1000 / Math.Clamp(fps, 1, 60));
        _figure.Opacity = _opacity;
        Show();
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void Advance()
    {
        _tick++;
        Show();
    }

    private void Show()
    {
        if (_frames.Count == 0) return;
        _figure.Source = _frames[_tick / Math.Max(1, _s.Hold) % _frames.Count];
    }

    /// <summary>The drawing the preview shows now (tests).</summary>
    internal int ShownDrawing => _frames.Count == 0 ? -1 : _tick / Math.Max(1, _s.Hold) % _frames.Count;
}
