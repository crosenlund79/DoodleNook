using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;

namespace DoodleNook.App.Shell;

/// <summary>
/// Mission 018, from the owner's notebook: "When either [key] is held a popup appears where you can adjust: zoom, brush
/// size, opacity, smoothing… once you let go of the key the change is made."
/// <para>Hold <b>B</b> (brush), <b>E</b> (eraser) or <b>Z</b> (zoom) and this small card appears just above the pen.
/// Its rows are the settings the tool has (zoom always; size, opacity and smoothing where they apply), each with its
/// value on the left, where the pen and hand don't cover it. Changes show at once; letting go of the key keeps them and
/// closes the card, and Esc (key still held) puts everything back. Dragging anywhere outside the card changes the key's
/// main value: size for B and E, zoom for Z (right or up is more). Nothing is painted while the key is held.</para>
/// </summary>
public sealed class QuickAdjustPanel : Panel
{
    /// <summary>How long a key is held before the card appears (a quicker tap of B or E just picks the tool).</summary>
    public const int HoldDelayMs = 250;

    private const double TrackWidth = 150;
    private readonly MainWindow _app;
    private readonly Border _card;
    private readonly StackPanel _rows = new() { Spacing = 6 };
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { Classes = { "dim" }, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
    private readonly Ellipse _sizeRing = new() { IsHitTestVisible = false, IsVisible = false, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly List<Action> _refresh = new();
    private Point _pivot;
    private Key _key;
    private bool _hasSize;
    private bool _refreshing;
    // What it was when the card opened (Esc puts it back).
    private double _startScale, _startSize, _startOpacity, _startSmoothing;
    // A drag outside the card.
    private Point? _dragStart;
    private double _dragStartValue;

    public QuickAdjustPanel(MainWindow app)
    {
        _app = app;
        Name = "QuickAdjust";
        IsVisible = false;
        Background = Brushes.Transparent; // the pen doesn't reach the canvas while the key is held
        _card = new Border
        {
            Classes = { "float" }, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 12), Name = "QuickAdjustCard",
            // Opaque: it sits over the artwork, which mustn't show through the sliders and numbers.
            Background = new SolidColorBrush(DnColors.Ui),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Spacing = 8, Children = { _title, _rows, _hint } },
        };
        Children.Add(_sizeRing);
        Children.Add(_card);
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += (_, e) => EndDrag(e.Pointer);
        PointerCaptureLost += (_, _) => _dragStart = null;
    }

    public bool IsOpen => IsVisible;

    /// <summary>The key that opened the card.</summary>
    public Key Key => _key;

    /// <summary>Opens the card for <paramref name="key"/> with the pen at (<paramref name="x"/>, <paramref name="y"/>) in
    /// the canvas's coordinates.</summary>
    public void Open(Key key, double x, double y)
    {
        var s = _app.Session;
        _key = key;
        _pivot = new Point(x, y);
        _startScale = _app.Canvas.View.Scale;
        _startSize = s.CurrentSize;
        _startOpacity = s.Brush.Opacity;
        _startSmoothing = s.CurrentPaintSettings.Smoothing;
        _dragStart = null;
        Build();
        IsVisible = true;
        Place();
        s.StateChanged += Refresh;
        _app.Canvas.View.Changed += Refresh;
    }

    /// <summary>Closes the card. <paramref name="keep"/> false (Esc) puts zoom and the settings back as they were.</summary>
    public void Close(bool keep)
    {
        if (!IsVisible) return;
        var s = _app.Session;
        s.StateChanged -= Refresh;
        _app.Canvas.View.Changed -= Refresh;
        if (!keep)
        {
            _app.Canvas.View.ZoomAround(_pivot.X, _pivot.Y, _startScale);
            if (_hasSize) s.SetSize(_startSize);
            if (HasOpacity(s)) s.SetOpacity(_startOpacity);
            if (HasSmoothing(s)) s.SetSmoothing(_startSmoothing);
        }
        _dragStart = null;
        _sizeRing.IsVisible = false;
        IsVisible = false;
    }

    private static bool HasSize(EditorSession s) =>
        s.ActiveLiquify is not null || s.Tool is ToolKind.Brush or ToolKind.Eraser || s.IsPixelTool(s.Tool) || EditorSession.IsRetouchTool(s.Tool);

    private static bool HasOpacity(EditorSession s) => s.Tool == ToolKind.Brush && s.ActiveLiquify is null;

    private static bool HasSmoothing(EditorSession s) => s.Tool is ToolKind.Brush or ToolKind.Eraser && s.ActiveLiquify is null;

    private bool Pixels => _app.Session.IsPixelTool(_app.Session.Tool);

    private void Build()
    {
        var s = _app.Session;
        _rows.Children.Clear();
        _refresh.Clear();
        string keyName = _key.ToString();
        _title.Text = _key switch
        {
            Key.B when s.Tool == ToolKind.PixelPencil => "Pixel pencil",
            Key.B => "Brush",
            Key.E => "Eraser",
            _ => "Zoom",
        };
        _hasSize = HasSize(s);
        // Zoom: 5 % to 3200 %, around the spot under the pen.
        Row("Zoom", 0.05, 32, log: true, () => _app.Canvas.ActualZoom, v => _app.Canvas.ZoomAroundTo(_pivot.X, _pivot.Y, v), v => $"{v * 100:0}%");
        if (_hasSize)
        {
            if (Pixels) Row("Size", 1, 64, log: false, () => s.CurrentSize, v => s.SetSize(Math.Round(v)), v => $"{v:0} px");
            else Row("Size", 1, 500, log: true, () => s.CurrentSize, s.SetSize, v => $"{v:0.#} px");
        }
        if (HasOpacity(s)) Row("Opacity", 0.05, 1, log: false, () => s.Brush.Opacity, s.SetOpacity, v => $"{v * 100:0}%");
        if (HasSmoothing(s)) Row("Smoothing", 0, 1, log: false, () => s.CurrentPaintSettings.Smoothing, s.SetSmoothing, v => $"{v * 100:0}%");
        string drag = _key == Key.Z || !_hasSize ? "zoom" : "size";
        _hint.Text = $"Drag anywhere: {drag} · let go of {keyName} to keep · Esc undoes";
        Refresh();
    }

    /// <summary>A row: the name, the value (left of the slider, clear of the pen and hand) and the slider.</summary>
    private void Row(string name, double min, double max, bool log, Func<double> get, Action<double> set, Func<double, string> format)
    {
        var label = new TextBlock { Text = name, Classes = { "dim" }, Width = 72, VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Classes = { "num" }, FontWeight = FontWeight.SemiBold, Width = 62, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Name = "QuickAdjust" + name + "Value" };
        var slider = new DnSlider { Minimum = min, Maximum = max, IsLogarithmic = log, TrackWidth = TrackWidth, Name = "QuickAdjust" + name, Margin = new Thickness(12, 0, 0, 0) };
        slider.UserChanged += (_, v) =>
        {
            set(v);
            Refresh();
        };
        _refresh.Add(() =>
        {
            double v = get();
            slider.Value = Math.Clamp(v, min, max);
            value.Text = format(v);
        });
        _rows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { label, value, slider } });
    }

    private void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            foreach (var r in _refresh) r();
            ShowSizeRing();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>The brush at its real size on screen, where the pen was (the card sits above it).</summary>
    private void ShowSizeRing()
    {
        if (!_hasSize)
        {
            _sizeRing.IsVisible = false;
            return;
        }
        double d = Math.Max(2, _app.Session.CurrentSize * _app.Canvas.View.Scale);
        _sizeRing.Width = d;
        _sizeRing.Height = d;
        _sizeRing.Stroke = new SolidColorBrush(DnColors.Accent);
        _sizeRing.Fill = new SolidColorBrush(Color.FromArgb(28, 0xE9, 0xA9, 0x4D));
        _sizeRing.Margin = new Thickness(_pivot.X - d / 2, _pivot.Y - d / 2, 0, 0);
        _sizeRing.IsVisible = true;
    }

    /// <summary>Above the pen (the same rule as the size read-outs), inside the canvas.</summary>
    private void Place()
    {
        _card.Margin = default; // the size alone (DesiredSize includes the margin, which is the position)
        _card.Measure(Size.Infinity);
        var area = (Parent as Control)?.Bounds.Size ?? Bounds.Size;
        var (x, y) = PenReadout.Place(_pivot.X, _pivot.Y, _card.DesiredSize.Width, _card.DesiredSize.Height, area.Width, area.Height);
        _card.Margin = new Thickness(x, y, 0, 0);
    }

    // ------------------------------------------------------------------ drag anywhere

    private double MainValue => _key == Key.Z || !_hasSize ? _app.Canvas.ActualZoom : _app.Session.CurrentSize;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true; // never reaches the canvas
        if (!ReferenceEquals(e.Source, this)) return;
        _dragStart = e.GetPosition(this);
        _dragStartValue = MainValue;
        e.Pointer.Capture(this);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var p = e.GetPosition(this);
        double d = (p.X - start.X) - (p.Y - start.Y); // right or up is more
        bool fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_key == Key.Z || !_hasSize)
        {
            double zoom = _dragStartValue * Math.Pow(2, (fine ? d * 0.25 : d) / 160);
            _app.Canvas.ZoomAroundTo(_pivot.X, _pivot.Y, Math.Clamp(zoom, 0.05, 32));
        }
        else _app.Session.SetSize(RadialPalette.SizeForDrag(_dragStartValue, d, Pixels, fine));
        Refresh();
    }

    private void EndDrag(IPointer pointer)
    {
        if (_dragStart is null) return;
        _dragStart = null;
        pointer.Capture(null);
    }

    /// <summary>For tests: the card's place in the canvas's coordinates.</summary>
    internal Rect CardBounds => _card.Bounds;

    /// <summary>For tests: the point the card opened at (where the pen was).</summary>
    internal Point Pivot => _pivot;
}
