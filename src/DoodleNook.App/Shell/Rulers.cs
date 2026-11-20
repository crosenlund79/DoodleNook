using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;

namespace DoodleNook.App.Shell;

/// <summary>
/// A ruler along the top or left edge of the canvas (View › Rulers, Ctrl+R; hidden by default). Numbers are picture
/// pixels. Drag from a ruler onto the picture to add a guide: the top ruler gives horizontal guides, the left one vertical.
/// </summary>
public sealed class RulerBar : Control
{
    public const double Size = 20;

    private static readonly Typeface Face = new(new FontFamily("fonts:Inter#Inter, $Default"));
    private readonly MainWindow _app;
    private readonly bool _horizontal;
    private bool _dragging;

    public RulerBar(MainWindow app, bool horizontal)
    {
        _app = app;
        _horizontal = horizontal;
        Name = horizontal ? "RulerTop" : "RulerLeft";
        if (horizontal)
        {
            Height = Size;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(Size, 0, 0, 0);
        }
        else
        {
            Width = Size;
            HorizontalAlignment = HorizontalAlignment.Left;
            Margin = new Thickness(0, Size, 0, 0);
        }
        Cursor = new Cursor(horizontal ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
        ToolTip.SetTip(this, horizontal ? "Drag down onto the picture to add a horizontal guide" : "Drag right onto the picture to add a vertical guide");
        app.Canvas.View.Changed += InvalidateVisual;
        app.Canvas.ViewChanged += InvalidateVisual;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(235, DnColors.Ui.R, DnColors.Ui.G, DnColors.Ui.B)), bounds);
        var line = new Pen(new SolidColorBrush(DnColors.Line), 1);
        if (_horizontal) context.DrawLine(line, new Point(0, bounds.Height - 0.5), new Point(bounds.Width, bounds.Height - 0.5));
        else context.DrawLine(line, new Point(bounds.Width - 0.5, 0), new Point(bounds.Width - 0.5, bounds.Height));

        var view = _app.Canvas.View;
        double scale = view.Scale;
        if (scale <= 0) return;
        // Ruler offset inside the canvas: this control starts Size units in from the canvas corner.
        double offset = Size;
        double length = _horizontal ? bounds.Width : bounds.Height;
        // Document coordinate at a point along the ruler (rulers follow the unrotated picture axes).
        double DocAt(double along)
        {
            var (x, y) = _horizontal ? view.ViewToDocument(along + offset, Size) : view.ViewToDocument(Size, along + offset);
            return _horizontal ? x : y;
        }
        double ViewAt(double doc)
        {
            var (vx, vy) = _horizontal ? view.DocumentToView(doc, DocAt2()) : view.DocumentToView(DocAt2(), doc);
            return (_horizontal ? vx : vy) - offset;
        }
        double DocAt2()
        {
            var (x, y) = view.ViewToDocument(Size, Size);
            return _horizontal ? y : x;
        }
        double a = DocAt(0), b = DocAt(length);
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        // Minor ticks at least 6 view units apart, labels every 10 minor ticks.
        double[] steps = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000];
        double minor = steps.FirstOrDefault(s => s * scale >= 6, 10000);
        double major = minor * 10;
        var tick = new Pen(new SolidColorBrush(DnColors.Text2), 1);
        var text = new SolidColorBrush(DnColors.Text2);
        long first = (long)Math.Floor(lo / minor), last = (long)Math.Ceiling(hi / minor);
        if (last - first > 4000) return;
        for (long i = first; i <= last; i++)
        {
            double d = i * minor;
            double p = Math.Round(ViewAt(d)) + 0.5;
            if (p < 0 || p > length) continue;
            bool isMajor = Math.Abs(d % major) < 1e-9;
            bool isHalf = !isMajor && Math.Abs(d % (major / 2)) < 1e-9;
            double size = isMajor ? Size - 2 : isHalf ? 8 : 4;
            if (_horizontal) context.DrawLine(tick, new Point(p, Size - size), new Point(p, Size));
            else context.DrawLine(tick, new Point(Size - size, p), new Point(Size, p));
            if (!isMajor) continue;
            var label = new FormattedText(d.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 9, text);
            if (_horizontal) context.DrawText(label, new Point(p + 3, 1));
            else
            {
                using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(1, p - 3)))
                    context.DrawText(label, new Point(0, 0));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var input = _app.Canvas.Input!;
        if (_app.Session.Document.GuidesLocked)
        {
            _app.ShowToast("Guides are locked (View › Lock guides)");
            return;
        }
        input.BeginRulerGuide(vertical: !_horizontal);
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        var p = e.GetPosition(_app.Canvas);
        _app.Canvas.Input!.DragRulerGuide(p.X, p.Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        var p = e.GetPosition(_app.Canvas);
        var size = _app.Canvas.Bounds.Size;
        bool overCanvas = p.X > Size && p.Y > Size && p.X < size.Width && p.Y < size.Height;
        _app.Canvas.Input!.EndRulerGuide(overCanvas);
        if (overCanvas) _app.ShowToast("Guide added · drag it with the Move tool · drag it off the picture to remove it");
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_dragging) return;
        _dragging = false;
        _app.Canvas.Input!.EndRulerGuide(false);
    }
}

/// <summary>The little square where the two rulers meet (click: hide the rulers).</summary>
public sealed class RulerCorner : Border
{
    public RulerCorner(MainWindow app)
    {
        Name = "RulerCorner";
        Width = Height = RulerBar.Size;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(DnColors.Ui);
        BorderBrush = new SolidColorBrush(DnColors.Line);
        BorderThickness = new Thickness(0, 0, 1, 1);
        ToolTip.SetTip(this, "Rulers (Ctrl+R) · numbers are picture pixels");
    }
}
