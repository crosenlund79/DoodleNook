using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace DoodleNook.App.Controls;

/// <summary>
/// DoodleNook's line icons (24 × 24, round 1.7 px strokes), the same drawings as the Mission 003 playground.
/// Circles and rectangles from the original SVGs are written as path arcs.
/// </summary>
public static class Icons
{
    public static readonly IReadOnlyDictionary<string, string> Data = new Dictionary<string, string>
    {
        ["menu"] = "M4 7h16M4 12h16M4 17h16",
        ["brush"] = "M20 4l-8.3 8.3 M11.9 12.1l-1.9-1.9c-1.5 0-2.7.6-3.3 1.9-.7 1.5-.3 3.4-2.6 4.6 2.7 1 5.7.3 6.8-1.5.6-.9.8-2 .8-3.1z",
        ["eraser"] = "M9 20h11 M5.2 15.2l8.9-8.9a2 2 0 0 1 2.8 0l2.7 2.7a2 2 0 0 1 0 2.8L11.6 20H8.3z M9.7 10.7l5.5 5.5",
        ["eyedropper"] = "M14.6 6.4l2.9-2.9a2.1 2.1 0 0 1 3 3l-2.9 2.9 M13 8l-7.5 7.5L5 19l3.5-.5L16 11 M12 7l5 5",
        ["hand"] = "M8 13V6.6a1.5 1.5 0 0 1 3 0V12 M11 11.6V5.1a1.5 1.5 0 0 1 3 0v6.5 M14 11.6V6.6a1.5 1.5 0 0 1 3 0V14c0 3.6-2.4 6-6 6-2.4 0-3.9-1.2-5-3l-1.8-3a1.4 1.4 0 0 1 2.3-1.6L8 14",
        ["undo"] = "M9 14L4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 0 11H11",
        ["redo"] = "M15 14l5-5-5-5 M20 9H9.5a5.5 5.5 0 0 0 0 11H13",
        ["chevd"] = "M6 9l6 6 6-6",
        ["close"] = "M6 6l12 12M18 6L6 18",
        ["plus"] = "M12 5v14M5 12h14",
        ["minus"] = "M5 12h14",
        ["fit"] = "M6 6h12a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z M8 10.5V9h1.5M16 10.5V9h-1.5M8 13.5V15h1.5M16 13.5V15h-1.5",
        ["flip"] = "M12 3v18 M9 7L4 17h5z M15 7l5 10h-5z",
        ["expand"] = "M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5",
        ["pressure"] = "M4 20l1-3.5L15.5 6l2.5 2.5L7.5 19z M14 20c1.6-1.2 3.4-1.2 5 0",
        ["swap"] = "M7 7h11l-3-3M17 17H6l3 3",
        ["check"] = "M5 12.5l4.5 4.5L19 7.5",
        ["new"] = "M6 3.5h8l4 4v13H6z M14 3.5v4h4 M12 11v6M9 14h6",
        ["open"] = "M3.5 7a1.5 1.5 0 0 1 1.5-1.5h4l2 2h8a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5H5a1.5 1.5 0 0 1-1.5-1.5z",
        ["save"] = "M5 4h11l3 3v13H5z M8 4v5h7V4 M8 20v-6h8v6",
        ["saveas"] = "M5 4h11l3 3v5 M5 4v16h6 M8 4v5h7V4 M13.5 20.5l.6-2.6 4.6-4.6a1.3 1.3 0 0 1 1.9 1.9l-4.6 4.6z",
        ["trash"] = "M4 7h16M9 7V4.5h6V7M6.5 7l1 13h9l1-13",
        ["zoom"] = "M4.5 10.5a6 6 0 1 0 12 0a6 6 0 1 0-12 0z M15 15l5 5",
        ["fullscreen"] = "M3.5 8V3.5H8 M16 3.5h4.5V8 M20.5 16v4.5H16 M8 20.5H3.5V16",
        ["pulse"] = "M3 12h4l2-5 4 10 2-5h6",
        ["info"] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0-17 0z M12 11v5 M12 7.7v.2",
        ["exit"] = "M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4 M10 16l-4-4 4-4 M6 12h10",
        ["keyboard"] = "M4.5 6.5h15a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 16V8a1.5 1.5 0 0 1 1.5-1.5z M7 10h.01M10 10h.01M13 10h.01M16 10h.01M8 14h8",
        ["palette"] = "M12 3.5a8.5 8.5 0 1 0 0 17c1.4 0 2-1 1.5-2.2-.6-1.4.3-2.8 1.8-2.8H17a3.5 3.5 0 0 0 3.5-3.5c0-4.7-3.8-8.5-8.5-8.5z",
    };

    private static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry? Get(string? name)
    {
        if (name is null || !Data.TryGetValue(name, out var path)) return null;
        if (!Cache.TryGetValue(name, out var g))
        {
            g = Geometry.Parse(path);
            Cache[name] = g;
        }
        return g;
    }
}

/// <summary>A line icon that takes its colour from the surrounding text colour (like CSS currentColor).</summary>
public sealed class DnIcon : Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<DnIcon, string?>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<DnIcon>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<DnIcon, double>(nameof(StrokeThickness), 1.7);

    static DnIcon()
    {
        AffectsRender<DnIcon>(KindProperty, ForegroundProperty, StrokeThicknessProperty);
        WidthProperty.OverrideDefaultValue<DnIcon>(19);
        HeightProperty.OverrideDefaultValue<DnIcon>(19);
        IsHitTestVisibleProperty.OverrideDefaultValue<DnIcon>(false);
    }

    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var geometry = Icons.Get(Kind);
        if (geometry is null || Foreground is null) return;
        double s = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var pen = new Pen(Foreground, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation((Bounds.Width - 24 * s) / 2, (Bounds.Height - 24 * s) / 2)))
            context.DrawGeometry(null, pen, geometry);
    }
}
