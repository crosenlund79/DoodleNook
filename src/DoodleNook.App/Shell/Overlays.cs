using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

using PixelRect = DoodleNook.Engine.Imaging.PixelRect;

namespace DoodleNook.App.Shell;

/// <summary>
/// The quick palette (the playground's ".radial"), opened at the pen by right-click or the pen's barrel button:
/// recent colours around the edge, the most used tools and undo/redo inside, and the brush size in the centre
/// (drag it to change the size). Esc or a click outside closes it.
/// </summary>
public sealed class RadialPalette : Panel
{
    public const double Diameter = 264;
    private readonly MainWindow _app;
    private readonly Avalonia.Controls.Canvas _ring = new() { Width = Diameter, Height = Diameter };
    private TextBlock _size = new();
    private readonly LayersPanel _layers;
    private readonly Flyout _layersFlyout;
    private Point? _dragStart;
    private double _dragStartSize;

    public RadialPalette(MainWindow app)
    {
        _app = app;
        IsVisible = false;
        Background = Brushes.Transparent; // a click anywhere else closes the palette
        PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this)) Close();
        };
        Children.Add(_ring);
        _layers = new LayersPanel(app.Session, 320) { Width = 290 };
        _layersFlyout = UiKit.PopFlyout(_layers, PlacementMode.RightEdgeAlignedTop);
        app.Session.StateChanged += () => { if (IsVisible) Build(); };
    }

    public bool IsOpen => IsVisible;

    public void Open(double x, double y)
    {
        double r = Diameter / 2;
        // While hidden this panel has no size of its own: use the canvas area it covers.
        var area = (Parent as Control)?.Bounds.Size ?? Bounds.Size;
        x = Math.Clamp(x, r + 4, Math.Max(r + 4, area.Width - r - 4));
        y = Math.Clamp(y, r + 4, Math.Max(r + 4, area.Height - r - 4));
        _ring.Margin = new Thickness(x - r, y - r, 0, 0);
        _ring.HorizontalAlignment = HorizontalAlignment.Left;
        _ring.VerticalAlignment = VerticalAlignment.Top;
        Build();
        IsVisible = true;
    }

    public void Close()
    {
        IsVisible = false;
        _layersFlyout.Hide();
    }

    private void Build()
    {
        var s = _app.Session;
        _ring.Children.Clear();
        const double c = Diameter / 2;
        var disc = new Ellipse
        {
            Width = Diameter,
            Height = Diameter,
            Fill = new SolidColorBrush(Color.FromArgb(242, 0x24, 0x20, 0x1D)),
            Stroke = new SolidColorBrush(DnColors.Line),
            StrokeThickness = 1,
        };
        _ring.Children.Add(disc);

        // Recent colours around the edge.
        var colours = s.RecentColors.Concat(s.Swatches).Distinct().Take(10).ToList();
        for (int i = 0; i < colours.Count; i++)
        {
            double a = -Math.PI / 2 + (i + 0.5) * 2 * Math.PI / colours.Count;
            var col = colours[i];
            var dot = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                Width = 30, Height = 30, MinWidth = 30, Padding = new Thickness(0), CornerRadius = new CornerRadius(15),
                Content = new Ellipse
                {
                    Width = 22, Height = 22, Fill = UiKit.Brush(col),
                    Stroke = new SolidColorBrush(col == s.Foreground ? DnColors.Accent : Color.FromArgb(120, 0, 0, 0)),
                    StrokeThickness = col == s.Foreground ? 2.5 : 1,
                },
            };
            ToolTip.SetTip(dot, col.ToHex());
            dot.Click += (_, _) =>
            {
                s.SetForeground(col);
                Close();
            };
            Place(dot, c + Math.Cos(a) * 112, c + Math.Sin(a) * 112);
        }

        // Tools and actions on the inner ring (clockwise from the top).
        bool pixel = s.PixelMode;
        var brush = pixel ? ToolKind.PixelPencil : ToolKind.Brush;
        var eraser = pixel ? ToolKind.PixelEraser : ToolKind.Eraser;
        (string Icon, string Tip, Action Run, bool On, bool Stay)[] items =
        [
            ("undo", "Undo (Ctrl+Z)", _app.Undo, false, true),
            (pixel ? "pixpencil" : "brush", pixel ? "Pixel pencil (B)" : "Brush (B)", () => s.SelectTool(brush), s.Tool == brush, false),
            ("eraser", "Eraser (E)", () => s.SelectTool(eraser), s.Tool == eraser, false),
            ("expand", _app.Workspace.Kind == WorkspaceKind.PureSketch ? "Full screen (F11)" : "Hide the interface (Tab)",
                () => { if (_app.Workspace.Kind == WorkspaceKind.PureSketch) _app.ToggleFullScreen(); else _app.SetUiHidden(!_app.UiHidden); }, false, false),
            ("redo", "Redo (Ctrl+Shift+Z)", _app.Redo, false, true),
            ("layers", $"Layers · painting on “{s.Document.ActiveLayer.Name}”", () => { }, false, true),
            ("eyedropper", "Eyedropper (I)", () => s.SelectTool(ToolKind.Eyedropper), s.Tool == ToolKind.Eyedropper, false),
            ("swap", "Swap colours (X)", s.SwapColors, false, true),
        ];
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            double a = -Math.PI / 2 + i * 2 * Math.PI / items.Length;
            var b = UiKit.Button(it.Icon, tip: it.Tip);
            b.Width = 40;
            b.Height = 40;
            b.MinWidth = 40;
            b.CornerRadius = new CornerRadius(20);
            b.Background = new SolidColorBrush(DnColors.Ui3);
            if (it.On)
            {
                b.Classes.Add("on");
                b.BorderBrush = new SolidColorBrush(DnColors.Accent);
                b.BorderThickness = new Thickness(2);
            }
            if (it.Icon == "layers")
            {
                b.Flyout = _layersFlyout;
            }
            else
            {
                var run = it.Run;
                bool stay = it.Stay;
                b.Click += (_, _) =>
                {
                    run();
                    if (!stay) Close();
                };
            }
            b.Name = "Radial" + it.Icon;
            Place(b, c + Math.Cos(a) * 66, c + Math.Sin(a) * 66);
        }

        // Size in the centre: drag to change.
        _size = new TextBlock { FontSize = 17, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "num" }, Text = $"{s.CurrentSize:0}" };
        var centre = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32),
            Background = new SolidColorBrush(DnColors.Ui2),
            BorderBrush = new SolidColorBrush(DnColors.Line), BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _size, new TextBlock { Text = "px · drag", Classes = { "dim" }, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center } },
            },
        };
        ToolTip.SetTip(centre, "Drag up or right for a bigger brush, down or left for smaller");
        centre.PointerPressed += (_, e) =>
        {
            _dragStart = e.GetPosition(this);
            _dragStartSize = s.CurrentSize;
            e.Pointer.Capture(centre);
            e.Handled = true;
        };
        centre.PointerMoved += (_, e) =>
        {
            if (_dragStart is not { } start) return;
            var p = e.GetPosition(this);
            double d = (p.X - start.X) - (p.Y - start.Y);
            double size = s.PixelMode ? _dragStartSize + d / 20 : _dragStartSize * Math.Pow(2, d / 80);
            s.SetSize(size);
            _size.Text = $"{s.CurrentSize:0}";
        };
        centre.PointerReleased += (_, e) =>
        {
            _dragStart = null;
            e.Pointer.Capture(null);
        };
        Place(centre, c, c);
    }

    private void Place(Control control, double cx, double cy)
    {
        double w = double.IsNaN(control.Width) ? 40 : control.Width, h = double.IsNaN(control.Height) ? 40 : control.Height;
        Avalonia.Controls.Canvas.SetLeft(control, cx - w / 2);
        Avalonia.Controls.Canvas.SetTop(control, cy - h / 2);
        _ring.Children.Add(control);
    }
}

/// <summary>Pixel Art's 1:1 preview (the playground's floating sprite preview): the picture at real size, updated live.
/// Click to cycle 1×, 2× and 3×.</summary>
public sealed class PixelPreview : Border
{
    // Styled like a plain Border (the "float" card), not as a type of its own.
    protected override Type StyleKeyOverride => typeof(Border);

    private readonly MainWindow _app;
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private Document? _watched;
    private int _scale;
    private bool _userScale;

    public PixelPreview(MainWindow app)
    {
        _app = app;
        Classes.Add("float");
        Padding = new Thickness(6);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 14, 14, 0);
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.None);
        Child = new Border { Background = UiKit.CheckerBrush(), Child = _image };
        ToolTip.SetTip(this, "1:1 preview · click for 1×, 2×, 3×");
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, e) =>
        {
            _scale = _scale % 3 + 1;
            _userScale = true;
            Update();
            e.Handled = true;
        };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Update();
        };
        app.Session.DocumentReplaced += Watch;
        Watch();
    }

    private void Watch()
    {
        if (_watched is not null)
        {
            _watched.ProjectionChanged -= OnChanged;
            _watched.SizeChanged -= Update;
        }
        _watched = _app.Session.Document;
        _watched.ProjectionChanged += OnChanged;
        _watched.SizeChanged += Update;
        Update();
    }

    private void OnChanged(PixelRect _)
    {
        if (IsVisible && !_timer.IsEnabled) _timer.Start();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible) Update();
    }

    public void Update()
    {
        if (!IsVisible) return;
        var doc = _app.Session.Document;
        const int max = 256;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int w = doc.Width, h = doc.Height;
        double fit = Math.Min(1.0, Math.Min((double)max / w, (double)max / h));
        int bw = Math.Max(1, (int)(w * fit)), bh = Math.Max(1, (int)(h * fit));
        if (!_userScale) _scale = Math.Clamp((int)Math.Ceiling(64.0 / Math.Max(bw, bh)), 1, 3); // small sprites start bigger
        var img = new RasterImage(bw, bh);
        var src = doc.Projection;
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
                img[x, y] = src[Math.Min(w - 1, (int)(x / fit)), Math.Min(h - 1, (int)(y / fit))];
        _image.Source = BrushPreview.ToBitmap(img);
        // One sprite pixel per screen pixel at 1× (physical pixels, not layout units).
        _image.Width = bw * _scale / scaling;
        _image.Height = bh * _scale / scaling;
    }
}
