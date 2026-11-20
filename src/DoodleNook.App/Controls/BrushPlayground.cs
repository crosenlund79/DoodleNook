using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Controls;

/// <summary>
/// The Brush Laboratory's test area: a small canvas of its own (its own document, never the artwork, never the undo
/// history) to try a brush with the mouse or pen while its settings change. Paper, dark or checkerboard backgrounds
/// show opacity and texture.
/// </summary>
public sealed class BrushPlayground : Control
{
    public enum Backdrop
    {
        Paper,
        Dark,
        Checker,
    }

    private readonly Func<StrokeStyle> _style;
    private Document _doc;
    private StrokeEngine _engine;
    private WriteableBitmap? _bitmap;
    private bool _drawing;
    private bool _dirty = true;

    public BrushPlayground(int width, int height, Func<StrokeStyle> style)
    {
        _style = style;
        Width = width;
        Height = height;
        _doc = new Document(width * 2, height * 2, null);
        _engine = new StrokeEngine(_doc);
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        Name = "BrushPlayground";
    }

    /// <summary>The test area's own picture (tests check it, and that the real document never changes).</summary>
    public Document TestDocument => _doc;

    public Backdrop Background { get; set; } = Backdrop.Paper;

    public void Clear()
    {
        _doc = new Document(_doc.Width, _doc.Height, null);
        _engine = new StrokeEngine(_doc);
        _dirty = true;
        InvalidateVisual();
    }

    /// <summary>Draws a sample S-stroke with a pressure swell (light → firm → light).</summary>
    public void ExampleStroke()
    {
        double w = _doc.Width, h = _doc.Height;
        StrokeSample At(int i)
        {
            double t = i / 80.0;
            return new StrokeSample(w * (0.08 + 0.84 * t), h * (0.5 + 0.28 * Math.Sin(t * Math.PI * 2)), (float)(0.15 + 0.85 * Math.Sin(Math.PI * t)));
        }
        _engine.Begin(_style(), At(0));
        for (int i = 1; i <= 80; i++) _engine.Add(At(i));
        _engine.End();
        _dirty = true;
        InvalidateVisual();
    }

    private StrokeSample Sample(PointerEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        float pressure = p.Pointer.Type == PointerType.Pen ? p.Properties.Pressure : 1f;
        return new StrokeSample(p.Position.X * 2, p.Position.Y * 2, pressure);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _engine.Begin(_style(), Sample(e));
        _drawing = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        _dirty = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drawing) return;
        foreach (var p in e.GetIntermediatePoints(this))
        {
            float pressure = p.Pointer.Type == PointerType.Pen ? p.Properties.Pressure : 1f;
            _engine.Add(new StrokeSample(p.Position.X * 2, p.Position.Y * 2, pressure));
        }
        _dirty = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drawing) return;
        _engine.End();
        _drawing = false;
        e.Pointer.Capture(null);
        _dirty = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        switch (Background)
        {
            case Backdrop.Paper:
                context.DrawRectangle(new SolidColorBrush(Color.Parse("#F8F4EC")), null, r);
                break;
            case Backdrop.Dark:
                context.DrawRectangle(new SolidColorBrush(Color.Parse("#2B2522")), null, r);
                break;
            default:
                context.DrawRectangle(Shell.UiKit.CheckerBrush(), null, r);
                break;
        }
        if (_dirty || _bitmap is null)
        {
            _bitmap = BrushPreview.ToBitmap(_doc.ActiveLayer.Image);
            _dirty = false;
        }
        context.DrawImage(_bitmap, r);
    }
}
