using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Controls;

/// <summary>The saturation/value square of the colour picker (the playground's ".cp-sv").</summary>
public sealed class SaturationValueArea : Control
{
    private bool _dragging;

    public double Hue { get; private set; }
    public double Saturation { get; private set; }
    public double Value { get; private set; }

    /// <summary>Raised while the user drags (saturation, value).</summary>
    public event Action<double, double>? UserChanged;

    public SaturationValueArea()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = false;
    }

    public void Set(double hue, double saturation, double value)
    {
        Hue = hue;
        Saturation = saturation;
        Value = value;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        var rr = new RoundedRect(rect, 6);
        var hue = ColorRgb.FromHsv(Hue, 1, 1);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(hue.R, hue.G, hue.B)), null, rr);
        context.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
        }, null, rr);
        context.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Black, 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1) },
        }, null, rr);

        var c = ColorRgb.FromHsv(Hue, Saturation, Value);
        var p = new Point(Saturation * rect.Width, (1 - Value) * rect.Height);
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), new Pen(new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), 3), p, 8, 8);
        context.DrawEllipse(null, new Pen(Brushes.White, 2), p, 8, 8);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _dragging = true;
        e.Pointer.Capture(this);
        e.PreventGestureRecognition();
        Update(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging) return;
        Update(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => _dragging = false;

    private void Update(Point p)
    {
        Saturation = Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1);
        Value = 1 - Math.Clamp(p.Y / Math.Max(1, Bounds.Height), 0, 1);
        InvalidateVisual();
        UserChanged?.Invoke(Saturation, Value);
    }
}

/// <summary>The hue strip of the colour picker (the playground's ".cp-hue").</summary>
public sealed class HueStrip : Control
{
    private bool _dragging;

    public double Hue { get; private set; }

    public event Action<double>? UserChanged;

    public HueStrip()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = false;
    }

    public void Set(double hue)
    {
        Hue = hue;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size).Deflate(new Thickness(0, 3));
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };
        string[] stops = ["#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#FF0000"];
        for (int i = 0; i < stops.Length; i++) brush.GradientStops.Add(new GradientStop(Color.Parse(stops[i]), i / 6.0));
        context.DrawRectangle(brush, null, new RoundedRect(rect, 6));

        double x = Hue / 360 * rect.Width;
        var knob = new Rect(x - 4, 0, 8, Bounds.Height);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), 3), new RoundedRect(knob, 4));
        context.DrawRectangle(null, new Pen(Brushes.White, 2), new RoundedRect(knob, 4));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _dragging = true;
        e.Pointer.Capture(this);
        e.PreventGestureRecognition();
        Update(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_dragging) return;
        Update(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => _dragging = false;

    private void Update(double x)
    {
        Hue = Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * 359.999;
        InvalidateVisual();
        UserChanged?.Invoke(Hue);
    }
}

/// <summary>The DoodleNook mark: a little nook (arch) with a doodle in it.</summary>
public sealed class BrandMark : Control
{
    private static readonly Geometry Arch = Geometry.Parse("M4.5 19.5V10a7.5 7.5 0 0 1 15 0v9.5z");
    private static readonly Geometry Squiggle = Geometry.Parse("M8 14.5c1.2-2.2 2.6-3.3 3.8-2.6 1.3.8-.9 3.6.6 4 1.3.4 2.2-1.6 3.6-3.4");

    public BrandMark()
    {
        Width = 23;
        Height = 23;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        double s = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        using (context.PushTransform(Matrix.CreateScale(s, s)))
        {
            context.DrawGeometry(DnColors.AccentBrush, null, Arch);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(DnColors.AccentInk), 1.6, lineCap: PenLineCap.Round), Squiggle);
        }
    }
}
