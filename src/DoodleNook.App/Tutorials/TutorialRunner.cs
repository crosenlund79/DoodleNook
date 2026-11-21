using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DoodleNook.App.Controls;
using DoodleNook.Engine.Documents;

namespace DoodleNook.App.Tutorials;

/// <summary>
/// Mission 014: runs a tutorial inside the real window.
/// <list type="bullet">
/// <item><b>Highlight:</b> a soft pulsing outline drawn over the actual control the step names, looked up by its name in
/// the window and positioned from its real place after every layout pass, so it follows the control when the window is
/// resized, panels move or the timeline grows. It never takes clicks. When the control is hidden, the guide offers
/// "Show me".</item>
/// <item><b>Progress:</b> each step's check reads application state when something actually happens: a change to the
/// picture, a frame, the onion skin, playback starting, a save or an export finishing. Nothing is polled; nothing scans
/// the canvas.</item>
/// <item><b>Placement:</b> the guide sits in a corner of the canvas area that doesn't cover the highlighted control, and
/// stays where the artist drags it.</item>
/// </list>
/// Everything it subscribes to is unsubscribed when the tutorial ends; the drawing stays the artist's.
/// </summary>
public sealed class TutorialRunner
{
    private readonly MainWindow _app;
    private readonly Avalonia.Controls.Canvas _layer;
    private readonly TutorialGuide _guide = new();
    private readonly Border _highlight;
    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly DispatcherTimer _advance = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    private Tutorial? _tutorial;
    private TutorialContext? _ctx;
    private int _index;
    private bool _done, _already;
    private Document? _watched;
    private Control? _target;
    private Point? _userPosition;
    private Point _placed;

    public TutorialRunner(MainWindow app, Avalonia.Controls.Canvas layer)
    {
        _app = app;
        _layer = layer;
        _highlight = new Border
        {
            Name = "TutorialHighlight",
            BorderBrush = new SolidColorBrush(DnColors.Accent),
            BorderThickness = new Thickness(2.5),
            CornerRadius = new CornerRadius(10),
            IsHitTestVisible = false,
            IsVisible = false,
            Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(650) }],
        };
        _pulse.Tick += (_, _) => _highlight.Opacity = _highlight.Opacity > 0.75 ? 0.4 : 1;
        _advance.Tick += (_, _) =>
        {
            _advance.Stop();
            if (_tutorial is not null && _done && _index < _tutorial.Steps.Count) Next();
        };
        _guide.CloseRequested += Exit;
        _guide.Dragged += d =>
        {
            _userPosition = (_userPosition ?? _placed) + d;
            Place();
        };
    }

    public bool IsRunning => _tutorial is not null;
    public Tutorial? Tutorial => _tutorial;

    /// <summary>-1: the introduction; 0…n−1: the steps; n: the closing card.</summary>
    public int Index => _index;

    public TutorialStep? Step => _tutorial is { } t && _index >= 0 && _index < t.Steps.Count ? t.Steps[_index] : null;
    public bool StepDone => _done;
    public TutorialContext? Context => _ctx;
    internal TutorialGuide Guide => _guide;
    internal Border Highlight => _highlight;
    internal Control? Target => _target;

    public void Start(Tutorial tutorial)
    {
        if (IsRunning) Exit();
        _tutorial = tutorial;
        _ctx = new TutorialContext(_app);
        _userPosition = null;
        _layer.Children.Add(_highlight);
        _layer.Children.Add(_guide);
        _app.Session.StateChanged += OnChanged;
        _app.Session.DocumentReplaced += OnDocumentReplaced;
        _app.Session.OnionChanged += OnChanged;
        _app.Animator.PlaybackChanged += OnPlayback;
        _app.SaveCompleted += OnChanged;
        _app.AnimationExported += OnChanged;
        _app.LayoutUpdated += OnLayout;
        Watch(_app.Session.Document);
        GoTo(-1);
    }

    /// <summary>Closes the guide. The drawing, the document and its history stay exactly as they are.</summary>
    public void Exit()
    {
        if (_tutorial is null) return;
        _advance.Stop();
        _pulse.Stop();
        _app.Session.StateChanged -= OnChanged;
        _app.Session.DocumentReplaced -= OnDocumentReplaced;
        _app.Session.OnionChanged -= OnChanged;
        _app.Animator.PlaybackChanged -= OnPlayback;
        _app.SaveCompleted -= OnChanged;
        _app.AnimationExported -= OnChanged;
        _app.LayoutUpdated -= OnLayout;
        Watch(null);
        _layer.Children.Remove(_guide);
        _layer.Children.Remove(_highlight);
        _highlight.IsVisible = false;
        _target = null;
        _tutorial = null;
        _ctx = null;
    }

    public void Next()
    {
        if (_tutorial is null) return;
        GoTo(Math.Min(_index + 1, _tutorial.Steps.Count));
    }

    public void Back()
    {
        if (_tutorial is null || _index <= 0) return;
        GoTo(_index - 1);
    }

    public void Skip() => Next();

    public void Restart()
    {
        if (_tutorial is { } t) Start(t);
    }

    private void GoTo(int index)
    {
        _advance.Stop();
        _index = index;
        _ctx!.Snapshot();
        _target = null;
        _done = _already = false;
        if (Step is { Done: { } done, AlreadyText: not null } && done(_ctx))
            _done = _already = true; // e.g. onion skin was already on: say so, keep pointing at it, wait for Next
        Render();
        Evaluate();
        _app.InvalidateArrange();
    }

    private async Task RunAction(TutorialStep step)
    {
        if (step.Action is null || _ctx is null) return;
        bool ok = await step.Action(_ctx);
        if (ok && ReferenceEquals(Step, step)) Evaluate();
    }

    // ------------------------------------------------------------------ noticing progress

    private void OnChanged() => Evaluate();

    private void OnPlayback()
    {
        if (_ctx is not null && _app.Animator.IsPlaying) _ctx.Plays++;
        Evaluate();
    }

    private void OnDocumentReplaced()
    {
        Watch(_app.Session.Document);
        Evaluate();
    }

    private void Watch(Document? doc)
    {
        if (_watched is not null)
        {
            _watched.AnimationChanged -= OnChanged;
            _watched.HistoryChanged -= OnChanged;
        }
        _watched = doc;
        if (doc is not null)
        {
            doc.AnimationChanged += OnChanged;
            doc.HistoryChanged += OnChanged; // once per finished stroke, undo or redo (not per pen event)
        }
    }

    private void Evaluate()
    {
        if (_ctx is null || Step is not { } step) return;
        if (!_done && step.Done is { } done && done(_ctx))
        {
            _done = true;
            Render();
            if (step.AutoAdvance) _advance.Start();
            return;
        }
        _guide.Update(step.Progress?.Invoke(_ctx), Feedback(step), Hint(step), RevealFor(step));
    }

    private string? Feedback(TutorialStep step) => !_done ? null : _already ? step.AlreadyText : step.DoneText;

    // ------------------------------------------------------------------ the guide

    private void Render()
    {
        var t = _tutorial!;
        int n = t.Steps.Count;
        if (_index < 0)
        {
            _guide.Show(new GuideView(t.Title, null, -1, 0, t.Title, t.IntroText,
                [new("TutorialNotNow", "Not now", false, Exit), new("TutorialStart", "Start", true, Next)]));
            return;
        }
        if (_index >= n)
        {
            _guide.Show(new GuideView(t.Title, null, n, n, t.FinishTitle, t.FinishText,
            [
                new("TutorialRestart", "Restart tutorial", false, Restart),
                new("TutorialReturn", "Return to animation", false, ReturnToAnimation),
                new("TutorialFinish", "Finish", true, Finish),
            ]));
            return;
        }
        var step = t.Steps[_index];
        var buttons = new List<GuideButton> { new("TutorialBack", "Back", false, Back, _index > 0) };
        if (!_done) buttons.Add(new("TutorialSkip", "Skip step", false, Skip));
        if (step.Action is not null && !_done) buttons.Add(new("TutorialAction", step.ActionLabel ?? "Do it", true, () => _ = RunAction(step)));
        else if (_done || step.Done is null) buttons.Add(new("TutorialNext", _index == n - 1 ? "Finish" : "Next", true, Next));
        else buttons.Add(new("TutorialManual", "I've done this", false, Next));
        _guide.Show(new GuideView(t.Title, $"Step {_index + 1} of {n}", _index, n, step.Title, step.Text, buttons)
        {
            Progress = step.Progress?.Invoke(_ctx!),
            Feedback = Feedback(step),
            Hint = Hint(step),
            Reveal = RevealFor(step),
        });
    }

    private void Finish()
    {
        Exit();
        _app.ShowToast("Tutorial finished · your animation stays open: keep drawing, save it, or try another frame rate");
    }

    /// <summary>Back to work: the guide closes, frame 1 and the drawing layer are ready.</summary>
    private void ReturnToAnimation()
    {
        Exit();
        _app.Animator.Stop();
        if (_app.Session.Document.IsAnimated) _app.Session.GoToFrame(0);
    }

    // ------------------------------------------------------------------ the highlight

    private Control? FindTarget(string name)
    {
        if (_target is { } t && t.Name == name && t.IsEffectivelyVisible && TopLevel.GetTopLevel(t) is not null) return t;
        _target = _app.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible && c.Bounds.Width > 0);
        return _target;
    }

    private bool TargetShown => Step?.Target is { } name && FindTarget(name) is not null;

    /// <summary>The step still points at its control: not done yet, or already done when it began (onion skin on).</summary>
    private bool Pointing => !_done || _already;

    private string? Hint(TutorialStep step) => step.Target is not null && step.Reveal is not null && Pointing && !TargetShown
        ? "Can't see it? DoodleNook can bring it back."
        : null;

    private Action? RevealFor(TutorialStep step) => step.Target is not null && step.Reveal is not null && Pointing && !TargetShown
        ? () =>
        {
            step.Reveal(_ctx!);
            Evaluate();
        }
        : null;

    private void OnLayout(object? sender, EventArgs e)
    {
        if (_tutorial is null) return;
        var target = Step is { Target: { } name } && Pointing ? FindTarget(name) : null;
        if (target?.TranslatePoint(new Point(0, 0), _layer) is { } p)
        {
            var rect = new Rect(p, target.Bounds.Size).Inflate(4);
            if (!_highlight.IsVisible || _highlight.Bounds != rect)
            {
                Avalonia.Controls.Canvas.SetLeft(_highlight, rect.X);
                Avalonia.Controls.Canvas.SetTop(_highlight, rect.Y);
                _highlight.Width = rect.Width;
                _highlight.Height = rect.Height;
            }
            if (!_highlight.IsVisible)
            {
                _highlight.IsVisible = true;
                _pulse.Start();
            }
        }
        else if (_highlight.IsVisible)
        {
            _highlight.IsVisible = false;
            _pulse.Stop();
        }
        // The control appeared or disappeared (the interface hidden, another workspace…): offer to show it, or stop offering.
        bool shown = target is not null;
        if (shown != _targetShown)
        {
            _targetShown = shown;
            if (Step is { } step) _guide.Update(step.Progress?.Invoke(_ctx!), Feedback(step), Hint(step), RevealFor(step));
        }
        Place();
    }

    private bool _targetShown = true;

    /// <summary>The canvas area in the tutorial layer's coordinates.</summary>
    private Rect Area()
    {
        var host = _app.TutorialArea;
        return host.TranslatePoint(new Point(0, 0), _layer) is { } p ? new Rect(p, host.Bounds.Size) : new Rect(_layer.Bounds.Size);
    }

    /// <summary>The first corner of the canvas area (bottom-right, top-right, bottom-left, top-left) that keeps the guide
    /// clear of the highlighted control.</summary>
    private Point DefaultPlace()
    {
        var area = Area();
        var size = _guide.Bounds.Size.Width > 0 ? _guide.Bounds.Size : new Size(TutorialGuide.GuideWidth, 220);
        const double m = 14;
        Point[] corners =
        [
            new(area.Right - size.Width - m, area.Bottom - size.Height - m),
            new(area.Right - size.Width - m, area.Top + m),
            new(area.X + m, area.Bottom - size.Height - m),
            new(area.X + m, area.Top + m),
        ];
        var avoid = _highlight.IsVisible ? new Rect(Avalonia.Controls.Canvas.GetLeft(_highlight), Avalonia.Controls.Canvas.GetTop(_highlight), _highlight.Width, _highlight.Height).Inflate(10) : default;
        foreach (var c in corners)
            if (!_highlight.IsVisible || !new Rect(c, size).Intersects(avoid))
                return c;
        return corners[0];
    }

    private void Place()
    {
        var p = _userPosition ?? DefaultPlace();
        var size = _guide.Bounds.Size;
        // Always fully on screen.
        p = new Point(Math.Clamp(p.X, 0, Math.Max(0, _layer.Bounds.Width - size.Width)), Math.Clamp(p.Y, 0, Math.Max(0, _layer.Bounds.Height - size.Height)));
        if (p == _placed && Avalonia.Controls.Canvas.GetLeft(_guide) == p.X) return;
        _placed = p;
        Avalonia.Controls.Canvas.SetLeft(_guide, p.X);
        Avalonia.Controls.Canvas.SetTop(_guide, p.Y);
    }

    /// <summary>The guide's place in the window (tests).</summary>
    internal Rect GuideRect => new(_placed, _guide.Bounds.Size);
}
