using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;

namespace DoodleNook.App.Tutorials;

/// <summary>One button on the guide.</summary>
public sealed record GuideButton(string Name, string Label, bool Primary, Action Click, bool Enabled = true);

/// <summary>What the guide shows right now.</summary>
public sealed record GuideView(
    string Tutorial,
    string? Counter,
    int Position,
    int Total,
    string Title,
    string Text,
    IReadOnlyList<GuideButton> Buttons)
{
    public string? Progress { get; init; }
    public string? Feedback { get; init; }
    public string? Hint { get; init; }
    public Action? Reveal { get; init; }
}

/// <summary>
/// Mission 014: the small floating guide — the tutorial's title, the step and its progress, a short instruction, and
/// Back / Skip / Next. It floats over the canvas in DoodleNook's warm dark style; drag its title bar to move it. Its
/// buttons never take the keyboard focus, so every drawing shortcut keeps working while it is open.
/// </summary>
public sealed class TutorialGuide : Border
{
    public const double GuideWidth = 320;

    private readonly TextBlock _tutorial = new() { FontSize = 12, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _counter = new() { FontSize = 12, Classes = { "dim", "num" }, VerticalAlignment = VerticalAlignment.Center, Name = "TutorialCounter" };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "TutorialStepTitle" };
    private readonly TextBlock _text = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Name = "TutorialStepText" };
    private readonly TextBlock _progress = new() { FontSize = 12, Classes = { "dim" }, TextWrapping = TextWrapping.Wrap, Name = "TutorialProgress" };
    private readonly TextBlock _feedback = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "TutorialFeedback" };
    private readonly StackPanel _hintRow = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly TextBlock _hint = new() { FontSize = 12, Classes = { "dim" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 200 };
    private readonly Button _reveal;
    private readonly WrapPanel _buttons = new() { ItemsAlignment = WrapPanelItemsAlignment.End };
    private readonly Border _header;
    private Action? _revealAction;
    private bool _dragging;
    private Point _dragStart;

    /// <summary>The guide was dragged by the artist (by this much).</summary>
    public event Action<Vector>? Dragged;

    public event Action? CloseRequested;

    public TutorialGuide()
    {
        Name = "TutorialGuide";
        Width = GuideWidth;
        Background = new SolidColorBrush(DnColors.Ui2);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        BoxShadow = BoxShadows.Parse("0 6 22 0 #66000000");
        _feedback.Foreground = new SolidColorBrush(DnColors.Accent);

        var close = NoFocus(UiKit.Button("close", tip: "Exit the tutorial (your drawing stays)", click: () => CloseRequested?.Invoke()));
        close.Name = "TutorialExit";
        close.Width = close.Height = close.MinWidth = 26;
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(_tutorial);
        _header = new Border
        {
            Child = top,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Padding = new Thickness(12, 6, 6, 0),
        };
        ToolTip.SetTip(_header, "Drag to move the guide");
        _header.PointerPressed += (_, e) =>
        {
            if (e.Source is Button || e.Source is Control c && c.FindAncestorOfType<Button>() is not null) return;
            _dragging = true;
            _dragStart = e.GetPosition(Parent as Visual ?? this);
            e.Pointer.Capture(_header);
            e.Handled = true;
        };
        _header.PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            var p = e.GetPosition(Parent as Visual ?? this);
            Dragged?.Invoke(p - _dragStart);
            _dragStart = p;
        };
        _header.PointerReleased += (_, e) =>
        {
            _dragging = false;
            e.Pointer.Capture(null);
        };

        _reveal = NoFocus(UiKit.Button(null, "Show me", "Bring the control into view", () => _revealAction?.Invoke()));
        _reveal.Name = "TutorialReveal";
        _reveal.Height = 26;
        _hintRow.Children.Add(_hint);
        _hintRow.Children.Add(_reveal);

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(14, 2, 14, 12) };
        body.Children.Add(UiKit.Row(8, _counter, _dots));
        body.Children.Add(_title);
        body.Children.Add(_text);
        body.Children.Add(_progress);
        body.Children.Add(_feedback);
        body.Children.Add(_hintRow);
        body.Children.Add(_buttons);
        Child = new StackPanel { Children = { _header, body } };
    }

    private static Button NoFocus(Button b)
    {
        b.Focusable = false;
        return b;
    }

    public void Show(GuideView v)
    {
        _tutorial.Text = v.Tutorial;
        _counter.Text = v.Counter ?? "";
        _counter.IsVisible = v.Counter is not null;
        _dots.Children.Clear();
        for (int i = 0; i < v.Total; i++)
            _dots.Children.Add(new Border
            {
                Width = i == v.Position ? 14 : 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(i <= v.Position ? DnColors.Accent : DnColors.Line),
            });
        _dots.IsVisible = v.Total > 0;
        _title.Text = v.Title;
        _text.Text = v.Text;
        Update(v.Progress, v.Feedback, v.Hint, v.Reveal);
        _buttons.Children.Clear();
        foreach (var b in v.Buttons)
        {
            var button = NoFocus(UiKit.Button(null, b.Label, null, b.Click, b.Primary ? ["solid", "on"] : []));
            button.Name = b.Name;
            button.Height = 30;
            button.Margin = new Thickness(4, 2, 0, 0);
            button.IsEnabled = b.Enabled;
            _buttons.Children.Add(button);
        }
    }

    /// <summary>Changes only the lines that follow what the artist does (progress, feedback, the reveal hint).</summary>
    public void Update(string? progress, string? feedback, string? hint, Action? reveal)
    {
        _progress.Text = progress ?? "";
        _progress.IsVisible = progress is not null;
        _feedback.Text = feedback ?? "";
        _feedback.IsVisible = feedback is not null;
        _hint.Text = hint ?? "";
        _revealAction = reveal;
        _reveal.IsVisible = reveal is not null;
        _hintRow.IsVisible = hint is not null || reveal is not null;
    }

    internal string FeedbackText => _feedback.IsVisible ? _feedback.Text ?? "" : "";
    internal string ProgressText => _progress.IsVisible ? _progress.Text ?? "" : "";
    internal bool RevealShown => _reveal.IsVisible;
}

internal static class VisualExtensions
{
    public static T? FindAncestorOfType<T>(this Control c) where T : Control
    {
        for (var p = c.Parent as Control; p is not null; p = p.Parent as Control)
            if (p is T t) return t;
        return null;
    }
}
