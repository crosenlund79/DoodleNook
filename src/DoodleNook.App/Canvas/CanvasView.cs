using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using DoodleNook.App.Diagnostics;
using DoodleNook.App.Editor;
using DoodleNook.App.Input;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Viewing;
using SkiaSharp;

namespace DoodleNook.App.Canvas;

/// <summary>
/// The drawing surface. It shows the document through a <see cref="ViewTransform"/>, draws the brush cursor,
/// and translates Avalonia pointer events (mouse, pen with coalesced samples, touch) into <see cref="PointerInput"/>
/// for the <see cref="CanvasInputController"/>. It never edits pixels itself.
/// </summary>
public sealed class CanvasView : Control, ICustomHitTest
{
    private readonly DocumentTexture _texture = new();
    private readonly List<PointerInput> _inputs = new(64);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private EditorSession? _session;
    private bool _fitMode = true;
    private bool _fitAllowsEnlarging;
    private Size _lastSize;

    public CanvasView()
    {
        ClipToBounds = true;
        Focusable = true; // so keyboard focus can rest here instead of in a text box
        View = new ViewTransform(1, 1);
        View.Changed += () =>
        {
            InvalidateVisual();
            ViewChanged?.Invoke();
        };
    }

    public ViewTransform View { get; }
    public CanvasInputController? Input { get; private set; }
    public InputDiagnostics Diagnostics { get; } = new();

    /// <summary>Raised when zoom, pan or flip changed.</summary>
    public event Action? ViewChanged;

    /// <summary>Raised when the pointer enters/leaves or the gesture changes (used to hide pop-overs and toasts).</summary>
    public event Action<Gesture>? GestureStarted;

    public bool UsesGpu => _texture.LastFrameUsedGpu;
    public long UploadedPixels => _texture.UploadedPixels;
    public long GpuFrames => _texture.GpuFrames;
    public long SoftwareFrames => _texture.SoftwareFrames;

    /// <summary>The whole canvas area takes pointer input, even where nothing hit-testable is drawn (e.g. zoomed in so
    /// the picture fills the view).</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    /// <summary>Zoom as shown to the user: 1 = one picture pixel per physical screen pixel.</summary>
    public double ActualZoom => View.Scale * RenderScaling;

    public void Attach(EditorSession session)
    {
        _session = session;
        Input = new CanvasInputController(session, View);
        Input.OverlayChanged += () =>
        {
            UpdateCursor();
            InvalidateVisual();
        };
        Input.ViewPannedByUser += () => _fitMode = false;
        session.DocumentReplaced += OnDocumentReplaced;
        session.StateChanged += InvalidateVisual;
        OnDocumentReplaced();
    }

    private void OnDocumentReplaced()
    {
        if (_session is null) return;
        var doc = _session.Document;
        doc.PixelsChanged += area =>
        {
            if (!ReferenceEquals(doc, _session.Document)) return;
            _texture.Update(doc.Layer, area);
            InvalidateVisual();
        };
        _texture.Load(doc.Layer);
        View.SetDocumentSize(doc.Width, doc.Height);
        FitToView(enlarge: false);
    }

    // ------------------------------------------------------------------ view commands

    public void FitToView(bool enlarge = true)
    {
        _fitMode = true;
        _fitAllowsEnlarging = enlarge;
        if (Bounds.Width < 2 || Bounds.Height < 2) return;
        double max = enlarge ? 16.0 / RenderScaling : 1.0 / RenderScaling;
        View.Fit(Bounds.Width, Bounds.Height, margin: 24, maxScale: max);
    }

    public void ZoomToActualPixels()
    {
        _fitMode = false;
        View.ZoomCentered(Bounds.Width, Bounds.Height, 1.0 / RenderScaling);
    }

    public void ZoomStep(int direction)
    {
        _fitMode = false;
        double next = ViewTransform.NextStep(ActualZoom, direction);
        View.ZoomCentered(Bounds.Width, Bounds.Height, next / RenderScaling);
    }

    public void ToggleFlip() => View.ToggleFlip(Bounds.Width);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitMode) FitToView(_fitAllowsEnlarging);
        else if (_lastSize.Width > 0)
            View.PanBy((e.NewSize.Width - _lastSize.Width) / 2, (e.NewSize.Height - _lastSize.Height) / 2);
        _lastSize = e.NewSize;
    }

    // ------------------------------------------------------------------ rendering

    private Rect DocumentRect()
    {
        var (x0, y0) = View.DocumentToView(0, 0);
        var (x1, y1) = View.DocumentToView(View.DocumentWidth, View.DocumentHeight);
        return new Rect(new Point(Math.Min(x0, x1), Math.Min(y0, y1)), new Point(Math.Max(x0, x1), Math.Max(y0, y1)));
    }

    public override void Render(DrawingContext context)
    {
        if (_session is null) return;
        var docRect = DocumentRect();
        var visible = new Rect(Bounds.Size);

        // Paper shadow (skipped when the picture fills the view).
        if (!docRect.Contains(visible))
        {
            var shadowRect = docRect.Intersect(visible.Inflate(80));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 21, 19)), null, new RoundedRect(shadowRect),
                new BoxShadows(new BoxShadow { OffsetY = 10, Blur = 34, Color = Color.FromArgb(128, 0, 0, 0) }));
        }

        var bg = _session.Document.Background;
        context.Custom(new DocumentDrawOperation(visible, _texture, docRect, View.FlipHorizontal, ActualZoom,
            bg is { } c ? new SKColor(c.R, c.G, c.B) : null));

        DrawOverlays(context);
    }

    private void DrawOverlays(DrawingContext context)
    {
        if (_session is null || Input is null) return;
        var shape = Input.Cursor;
        var black = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

        if (Input.Gesture == Gesture.ResizeBrush)
        {
            var (ax, ay) = Input.ResizeAnchor;
            double r = Math.Max(1, _session.CurrentPaintSettings.Size * View.Scale / 2);
            var p = new Point(ax, ay);
            context.DrawEllipse(null, new Pen(black, 3), p, r, r);
            context.DrawEllipse(null, new Pen(Brushes.White, 1.5), p, r, r);
            var label = new FormattedText($"{_session.CurrentPaintSettings.Size:0} px", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(new FontFamily("fonts:Inter#Inter, $Default")), 13, Brushes.White);
            var box = new Rect(ax + r + 10, ay - 14, label.Width + 20, 28);
            context.DrawRectangle(new SolidColorBrush(Controls.DnColors.Ui), new Pen(new SolidColorBrush(Controls.DnColors.Line), 1), new RoundedRect(box, 8));
            context.DrawText(label, new Point(box.X + 10, box.Y + (28 - label.Height) / 2));
            return;
        }

        if (Input.Hover is not { } h) return;
        var at = new Point(h.X, h.Y);

        if (shape == CursorShape.Eyedropper)
        {
            var fg = _session.Foreground;
            var fgBrush = new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B));
            if (Input.Gesture == Gesture.Pick)
            {
                context.DrawEllipse(null, new Pen(black, 12), at, 26, 26);
                context.DrawEllipse(null, new Pen(fgBrush, 9), at, 26, 26);
            }
            return;
        }

        if (shape != CursorShape.BrushOutline || _session.Tool is not (ToolKind.Brush or ToolKind.Eraser) && Input.Gesture != Gesture.Paint)
            return;

        double radius = _session.CurrentPaintSettings.Size * View.Scale / 2;
        bool erasing = Input.Gesture == Gesture.Paint ? Input.StrokeErases : _session.Tool == ToolKind.Eraser;
        if (radius >= 3)
        {
            context.DrawEllipse(null, new Pen(black, 2.5), at, radius, radius);
            context.DrawEllipse(null, erasing ? new Pen(Brushes.White, 1.3, new DashStyle([3, 3], 0)) : new Pen(Brushes.White, 1.3), at, radius, radius);
        }
        // A small crosshair marks the exact point (always visible for tiny brushes).
        double arm = radius >= 3 ? 3 : 6;
        var cross = new Pen(Brushes.White, 1.2);
        context.DrawLine(new Pen(black, 3), new Point(at.X - arm, at.Y), new Point(at.X + arm, at.Y));
        context.DrawLine(new Pen(black, 3), new Point(at.X, at.Y - arm), new Point(at.X, at.Y + arm));
        context.DrawLine(cross, new Point(at.X - arm, at.Y), new Point(at.X + arm, at.Y));
        context.DrawLine(cross, new Point(at.X, at.Y - arm), new Point(at.X, at.Y + arm));
    }

    private static readonly Cursor NoCursor = new(StandardCursorType.None);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);

    private void UpdateCursor()
    {
        if (Input is null) return;
        Cursor = Input.Cursor switch
        {
            CursorShape.Hand or CursorShape.Grabbing => HandCursor,
            CursorShape.Eyedropper => CrossCursor,
            CursorShape.Resize => ResizeCursor,
            _ => NoCursor,
        };
    }

    /// <summary>Called by the window when Space/Alt change so the cursor updates without moving the pen.</summary>
    public void RefreshCursor()
    {
        UpdateCursor();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ input

    private static PointerKind KindOf(IPointer pointer) => pointer.Type switch
    {
        PointerType.Pen => PointerKind.Pen,
        PointerType.Touch => PointerKind.Touch,
        _ => PointerKind.Mouse,
    };

    private static PointerInput ToInput(PointerPoint pt, IPointer pointer, KeyModifiers modifiers)
    {
        var pp = pt.Properties;
        return new PointerInput(
            pointer.Id,
            KindOf(pointer),
            pt.Position.X,
            pt.Position.Y,
            pp.Pressure,
            Primary: pp.IsLeftButtonPressed,
            Secondary: pp.IsRightButtonPressed || pp.IsBarrelButtonPressed,
            Middle: pp.IsMiddleButtonPressed,
            Eraser: pp.IsEraser || pp.IsInverted,
            Alt: modifiers.HasFlag(KeyModifiers.Alt));
    }

    private void Observe(PointerEventArgs e, PointerPoint pt, int samples, bool recordPressure)
    {
        var pp = pt.Properties;
        Diagnostics.Record(e.Timestamp, KindOf(e.Pointer), samples, pp.IsLeftButtonPressed || pp.IsRightButtonPressed,
            pp.Pressure, pp.XTilt, pp.YTilt, pp.IsBarrelButtonPressed, pp.IsEraser || pp.IsInverted,
            pp.IsRightButtonPressed, pp.IsMiddleButtonPressed, recordPressure);
        if (_session is not null && Input?.Gesture == Gesture.Paint)
        {
            var settings = Input.StrokeErases ? _session.Eraser : _session.Brush;
            float p = KindOf(e.Pointer) == PointerKind.Pen ? pp.Pressure : CanvasInputController.MousePressure;
            Diagnostics.RecordBrush(_session.Curve.Map(p), settings.Normalized().Evaluate(p, _session.Curve).Diameter);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Input is null) return;
        var pt = e.GetCurrentPoint(this);
        var start = _clock.Elapsed.TotalMilliseconds;
        bool started = Input.Press(ToInput(pt, e.Pointer, e.KeyModifiers));
        if (started)
        {
            e.Pointer.Capture(this);
            e.PreventGestureRecognition();
            e.Handled = true;
            if (Input.Gesture == Gesture.Paint)
            {
                Diagnostics.StrokeStarted();
                Diagnostics.RecordEngineTime(_clock.Elapsed.TotalMilliseconds, _clock.Elapsed.TotalMilliseconds - start);
            }
            GestureStarted?.Invoke(Input.Gesture);
        }
        Observe(e, pt, 1, started && Input.Gesture == Gesture.Paint);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Input is null) return;
        var mods = e.KeyModifiers;
        // Pen events from Windows carry no Alt flag (pointer key states only know Shift and Ctrl), so a pointer event
        // may switch the temporary eyedropper on but never off; the Alt key-up (or losing focus) ends it.
        if (!Input.AltHeld && mods.HasFlag(KeyModifiers.Alt) && Input.Gesture == Gesture.None)
        {
            Input.AltHeld = true;
            UpdateCursor();
        }

        _inputs.Clear();
        PointerPoint current;
        if (Input.Gesture == Gesture.Paint)
        {
            // All samples Windows coalesced into this event (pen history / mouse move points), oldest first.
            var points = e.GetIntermediatePoints(this);
            foreach (var pt in points) _inputs.Add(ToInput(pt, e.Pointer, mods));
            current = points[^1];
        }
        else
        {
            current = e.GetCurrentPoint(this);
            _inputs.Add(ToInput(current, e.Pointer, mods));
        }

        double start = _clock.Elapsed.TotalMilliseconds;
        Input.Move(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_inputs));
        if (Input.Gesture == Gesture.Paint)
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            Diagnostics.RecordEngineTime(now, now - start);
            e.Handled = true;
        }
        Observe(e, current, _inputs.Count, Input.Gesture == Gesture.Paint);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Input is null) return;
        var pt = e.GetCurrentPoint(this);
        bool wasActive = Input.Gesture != Gesture.None;
        Input.Release(ToInput(pt, e.Pointer, e.KeyModifiers));
        if (wasActive && Input.Gesture == Gesture.None)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        Diagnostics.PointerLifted();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Input?.Interrupt();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Input?.Leave();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Input?.Gesture is not (null or Gesture.None)) return;
        double dy = e.Delta.Y;
        if (Math.Abs(dy) < 1e-6) return;
        _fitMode = false;
        var p = e.GetPosition(this);
        View.ZoomAround(p.X, p.Y, View.Scale * Math.Pow(1.2, Math.Clamp(dy, -4, 4)));
        e.Handled = true;
    }

    /// <summary>Cancels any gesture (used when the window loses focus or a dialog opens). Paint so far is kept.</summary>
    public void InterruptGesture()
    {
        Input?.Interrupt();
        if (Input is not null)
        {
            Input.SpaceHeld = false;
            Input.AltHeld = false;
        }
        UpdateCursor();
    }

    /// <summary>Renders the document image with Skia (GPU when available).</summary>
    private sealed class DocumentDrawOperation : ICustomDrawOperation
    {
        private readonly DocumentTexture _texture;
        private readonly Rect _docRect;
        private readonly bool _flip;
        private readonly double _zoom;
        private readonly SKColor? _background;

        public DocumentDrawOperation(Rect bounds, DocumentTexture texture, Rect docRect, bool flip, double zoom, SKColor? background)
        {
            Bounds = bounds;
            _texture = texture;
            _docRect = docRect;
            _flip = flip;
            _zoom = zoom;
            _background = background;
        }

        public Rect Bounds { get; }

        public void Dispose()
        {
        }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var dest = new SKRect((float)_docRect.Left, (float)_docRect.Top, (float)_docRect.Right, (float)_docRect.Bottom);

            canvas.Save();
            if (_background is { } bg)
            {
                using var paint = new SKPaint { Color = bg };
                canvas.DrawRect(dest, paint);
            }
            else
            {
                DrawChecker(canvas, dest);
            }

            // Exact pixels at 100 % and crisp, visible pixels from 200 % (like other paint apps); smooth filtering in
            // between and when zoomed out (with mipmaps, so thin lines don't shimmer).
            var sampling = Math.Abs(_zoom - 1) < 0.001 || _zoom >= 2
                ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
                : _zoom > 1
                    ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)
                    : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            if (_flip) canvas.Scale(-1, 1, dest.MidX, 0);
            _texture.Draw(canvas, lease.GrContext, dest, sampling);
            canvas.Restore();
        }

        private static SKShader? s_checker;

        private static void DrawChecker(SKCanvas canvas, SKRect dest)
        {
            if (s_checker is null)
            {
                using var bmp = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
                var light = new SKColor(0xEB, 0xE5, 0xDC);
                var dark = new SKColor(0xCF, 0xC7, 0xBB);
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        bmp.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? light : dark);
                using var img = SKImage.FromBitmap(bmp);
                s_checker = img.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.Identity);
            }
            using var paint = new SKPaint { Shader = s_checker };
            canvas.DrawRect(dest, paint);
        }
    }
}
