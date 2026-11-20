using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Animation;

/// <summary>
/// A small looping preview of the whole animation, floating at the top right of the canvas (optional: Animation menu or
/// Customize workspace). It plays on its own, independent of the timeline's playhead, with exact pixels for pixel art
/// and smooth scaling otherwise, on a checkerboard. Drag its lower-left corner to resize it.
/// </summary>
public sealed class AnimationPreview : Border
{
    // Styled like a plain Border (the "float" card), not as a type of its own.
    protected override Type StyleKeyOverride => typeof(Border);

    private readonly MainWindow _app;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<int, WriteableBitmap> _frames = new();
    private readonly TextBlock _label = new() { Classes = { "dim", "num" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private long _stateId = -1;
    private int _shown = -1;
    private bool _resizing;
    private Point _resizeStart;
    private double _resizeStartWidth;

    public AnimationPreview(MainWindow app)
    {
        _app = app;
        Name = "AnimationPreview";
        Classes.Add("float");
        Padding = new Thickness(6);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 14, 14, 0);
        Width = 240;

        var close = UiKit.Button("close", tip: "Hide the preview", click: () => app.SetPreviewPanel(false));
        close.Width = 24;
        close.Height = 24;
        close.MinWidth = 24;
        var head = new DockPanel { Margin = new Thickness(2, 0, 0, 4) };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(UiKit.Row(6, new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, _label));

        var grip = new Border
        {
            Width = 14, Height = 14, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.BottomLeftCorner), Margin = new Thickness(-6, 0, 0, -6),
            Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M2 4 L10 12 M2 8 L6 12"), Stroke = new SolidColorBrush(DnColors.Text2), StrokeThickness = 1.4 },
        };
        ToolTip.SetTip(grip, "Drag to resize the preview");
        grip.PointerPressed += (_, e) =>
        {
            _resizing = true;
            _resizeStart = e.GetPosition(app);
            _resizeStartWidth = Width;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!_resizing) return;
            Width = Math.Clamp(_resizeStartWidth - (e.GetPosition(app).X - _resizeStart.X), 120, 640);
        };
        grip.PointerReleased += (_, e) =>
        {
            _resizing = false;
            e.Pointer.Capture(null);
            app.AnimationState.PreviewWidth = Width;
        };

        var picture = new Border { Background = UiKit.CheckerBrush(), Child = _image, ClipToBounds = true, CornerRadius = new CornerRadius(4) };
        var stack = new Panel();
        stack.Children.Add(new DockPanel { Children = { WithTop(head), picture } });
        stack.Children.Add(grip);
        Child = stack;
        _timer.Tick += (_, _) => Tick();
    }

    private static Control WithTop(Control c)
    {
        DockPanel.SetDock(c, Dock.Top);
        return c;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;
        if (IsVisible)
        {
            Width = Math.Clamp(_app.AnimationState.PreviewWidth, 120, 640);
            _clock.Restart();
            _timer.Start();
            Tick();
        }
        else
        {
            _timer.Stop();
            _frames.Clear();
        }
    }

    private void Tick()
    {
        var doc = _app.Session.Document;
        if (doc.History.CurrentStateId != _stateId || _frames.Count > doc.FrameCount)
        {
            // Something changed: rebuild the cached frames lazily.
            _frames.Clear();
            _stateId = doc.History.CurrentStateId;
            _shown = -1;
        }
        int frame = 0;
        if (doc.Animation is { } a && a.FrameCount > 1)
        {
            var clock = PlaybackClock.For(a, 0);
            frame = clock.FrameAt(_clock.Elapsed.TotalMilliseconds % Math.Max(1, clock.PingPong ? clock.PassDurationMs * 2 : clock.PassDurationMs));
            if (frame < 0) frame = a.FrameCount - 1;
        }
        _label.Text = $"{frame + 1}/{doc.FrameCount}";
        if (frame == _shown) return;
        _shown = frame;
        if (!_frames.TryGetValue(frame, out var bmp)) _frames[frame] = bmp = Render(doc, frame);
        _image.Source = bmp;
        RenderOptions.SetBitmapInterpolationMode(_image, _app.Session.PixelMode ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        _image.Height = Math.Max(40, (Width - 12) * doc.Height / doc.Width);
    }

    private static WriteableBitmap Render(Document doc, int frame)
    {
        var full = doc.RenderFrame(frame);
        const int max = 640;
        if (full.Width <= max && full.Height <= max) return BrushPreview.ToBitmap(full);
        double s = Math.Min((double)max / full.Width, (double)max / full.Height);
        int w = Math.Max(1, (int)(full.Width * s)), h = Math.Max(1, (int)(full.Height * s));
        var small = new RasterImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                small[x, y] = full[Math.Min(full.Width - 1, (int)((x + 0.5) / s)), Math.Min(full.Height - 1, (int)((y + 0.5) / s))];
        return BrushPreview.ToBitmap(small);
    }
}
