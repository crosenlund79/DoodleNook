using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace DoodleNook.App.Controls;

/// <summary>Shared colours for custom-drawn controls (keep in sync with Theme/Tokens.axaml).</summary>
internal static class DnColors
{
    public static readonly Color Ui = Color.Parse("#24201D");
    public static readonly Color Ui2 = Color.Parse("#2C2724");
    public static readonly Color Ui3 = Color.Parse("#37312C");
    public static readonly Color Line = Color.Parse("#3E3732");
    public static readonly Color Text = Color.Parse("#EFE7DD");
    public static readonly Color Text2 = Color.Parse("#A99D91");
    public static readonly Color Accent = Color.Parse("#E9A94D");
    public static readonly Color AccentInk = Color.Parse("#22180C");
    public static readonly Color Surround = Color.Parse("#181513");

    public static readonly IImmutableSolidColorBrush UiBrush = new ImmutableSolidColorBrush(Ui);
    public static readonly IImmutableSolidColorBrush Ui3Brush = new ImmutableSolidColorBrush(Ui3);
    public static readonly IImmutableSolidColorBrush LineBrush = new ImmutableSolidColorBrush(Line);
    public static readonly IImmutableSolidColorBrush TextBrush = new ImmutableSolidColorBrush(Text);
    public static readonly IImmutableSolidColorBrush Text2Brush = new ImmutableSolidColorBrush(Text2);
    public static readonly IImmutableSolidColorBrush AccentBrush = new ImmutableSolidColorBrush(Accent);
}

/// <summary>
/// The playground's slider (".sl-track"): a thin rail, accent fill and a round knob, 36 px tall so it is easy to hit
/// with a pen. Pressing anywhere on the track jumps there; dragging adjusts. Optionally logarithmic (for brush size).
/// </summary>
public sealed class DnSlider : Control
{
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<DnSlider, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<DnSlider, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<DnSlider, double>(nameof(Value), 0,
        defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<bool> IsLogarithmicProperty = AvaloniaProperty.Register<DnSlider, bool>(nameof(IsLogarithmic));
    public static readonly StyledProperty<double> TrackWidthProperty = AvaloniaProperty.Register<DnSlider, double>(nameof(TrackWidth), 96);

    private const double Inset = 8;      // room for the knob at both ends (0.22 × 36)
    private const double KnobRadius = 7.5;

    static DnSlider()
    {
        AffectsRender<DnSlider>(ValueProperty, MinimumProperty, MaximumProperty, IsLogarithmicProperty);
        AffectsMeasure<DnSlider>(TrackWidthProperty);
        FocusableProperty.OverrideDefaultValue<DnSlider>(false);
        CursorProperty.OverrideDefaultValue<DnSlider>(new Cursor(StandardCursorType.Hand));
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsLogarithmic { get => GetValue(IsLogarithmicProperty); set => SetValue(IsLogarithmicProperty, value); }
    public double TrackWidth { get => GetValue(TrackWidthProperty); set => SetValue(TrackWidthProperty, value); }

    /// <summary>Raised when the user changes the value (not when code sets it).</summary>
    public event EventHandler<double>? UserChanged;

    /// <summary>Raised when the user releases the slider.</summary>
    public event EventHandler? UserFinished;

    protected override Size MeasureOverride(Size availableSize) => new(TrackWidth + 2 * Inset, 36);

    internal double Fraction
    {
        get
        {
            double min = Minimum, max = Maximum, v = Math.Clamp(Value, min, max);
            if (max <= min) return 0;
            if (IsLogarithmic && min > 0) return Math.Log(v / min) / Math.Log(max / min);
            return (v - min) / (max - min);
        }
    }

    internal double ValueAt(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        double min = Minimum, max = Maximum;
        return IsLogarithmic && min > 0 ? min * Math.Pow(max / min, fraction) : min + (max - min) * fraction;
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width - 2 * Inset;
        double cy = Bounds.Height / 2;
        double x = Inset + w * Fraction;
        context.DrawRectangle(DnColors.Ui3Brush, null, new RoundedRect(new Rect(Inset, cy - 2, w, 4), 2));
        if (x > Inset) context.DrawRectangle(DnColors.AccentBrush, null, new RoundedRect(new Rect(Inset, cy - 2, x - Inset, 4), 2));
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), null, new Point(x, cy + 1), KnobRadius + 1, KnobRadius + 1);
        context.DrawEllipse(DnColors.TextBrush, null, new Point(x, cy), KnobRadius, KnobRadius);
    }

    private bool _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        e.PreventGestureRecognition();
        Update(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        Update(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        UserFinished?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragging)
        {
            _dragging = false;
            UserFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Update(double x)
    {
        double w = Bounds.Width - 2 * Inset;
        double v = ValueAt((x - Inset) / Math.Max(1, w));
        if (v == Value) return;
        Value = v;
        UserChanged?.Invoke(this, v);
    }
}
