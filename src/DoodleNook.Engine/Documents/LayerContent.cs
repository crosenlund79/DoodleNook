using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Documents;

/// <summary>What a layer holds.</summary>
public enum LayerKind
{
    /// <summary>Pixels you paint on (the only kind before Mission 008).</summary>
    Raster,

    /// <summary>A folder of layers, composited together and then onto the picture.</summary>
    Group,

    /// <summary>Editable text, rendered to pixels for display and export.</summary>
    Text,

    /// <summary>An editable rectangle, rounded rectangle, ellipse, line or polygon.</summary>
    Shape,

    /// <summary>A non-destructive adjustment of everything below it in its group.</summary>
    Adjustment,
}

/// <summary>The editable description behind a text, shape or adjustment layer.</summary>
public abstract record LayerContent;

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>An outline around text or a shape.</summary>
public sealed record OutlineEffect(ColorRgb Color, double Width = 2);

/// <summary>A soft drop shadow (offset in pixels, blur radius, opacity 0–1).</summary>
public sealed record ShadowEffect(ColorRgb Color, double OffsetX = 3, double OffsetY = 4, double Blur = 4, double Opacity = 0.45);

/// <summary>
/// Editable text. (X, Y) is the top-left of the text box; it turns by <see cref="Rotation"/> degrees around its centre.
/// <see cref="BoxWidth"/> 0 means "as wide as the longest line"; otherwise lines wrap at that width.
/// The font is remembered by family name; when it isn't installed, a substitute is used and named (see
/// <see cref="TextRenderResult.Substitute"/>), and the layer keeps asking for the original.
/// </summary>
public sealed record TextContent(
    string Text,
    string FontFamily = "Segoe UI",
    double Size = 48,
    ColorRgb Color = default,
    bool Bold = false,
    bool Italic = false,
    TextAlign Align = TextAlign.Left,
    double X = 0,
    double Y = 0,
    double Rotation = 0,
    double LetterSpacing = 0,
    double LineSpacing = 1.2,
    double BoxWidth = 0,
    OutlineEffect? Outline = null,
    ShadowEffect? Shadow = null) : LayerContent;

public enum ShapeType
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Line,
    Polygon,
}

/// <summary>
/// An editable shape: the box (X, Y, Width, Height) before rotation, turned by <see cref="Rotation"/> degrees around its
/// centre. A line runs from the box's top-left to its bottom-right corner. Fill and outline are optional (null = none).
/// </summary>
public sealed record ShapeContent(
    ShapeType Kind,
    double X,
    double Y,
    double Width,
    double Height,
    ColorRgb? Fill,
    ColorRgb? Stroke = null,
    double StrokeWidth = 4,
    double Rotation = 0,
    double CornerRadius = 16,
    int Sides = 6,
    ShadowEffect? Shadow = null) : LayerContent;

/// <summary>A non-destructive adjustment (Brightness/Contrast, Hue/Saturation, Levels or Colour balance).</summary>
public sealed record AdjustmentContent(Adjustment Adjustment) : LayerContent;

/// <summary>How a whole-picture operation moves text and shapes: where a point goes, how sizes scale, and the turn and
/// mirroring applied. Text is never mirrored into back-to-front writing: it stays readable and moves to its mirrored place.</summary>
public sealed record ContentTransform(Func<double, double, (double X, double Y)> Point, double ScaleX = 1, double ScaleY = 1, double Rotate = 0,
    bool MirrorX = false, bool MirrorY = false);

public static class LayerContentExtensions
{
    /// <summary>Text or a shape moved by (dx, dy).</summary>
    public static LayerContent Translated(this LayerContent content, double dx, double dy) => content switch
    {
        TextContent t => t with { X = t.X + dx, Y = t.Y + dy },
        ShapeContent s => s with { X = s.X + dx, Y = s.Y + dy },
        _ => content,
    };

    /// <summary>The content after a whole-picture operation. <paramref name="size"/> is the text box size (text only).</summary>
    public static LayerContent Transformed(this LayerContent content, ContentTransform t, (double Width, double Height) size)
    {
        double k = Math.Sqrt(Math.Abs(t.ScaleX * t.ScaleY));
        static double Norm(double a)
        {
            a %= 360;
            if (a > 180) a -= 360;
            if (a <= -180) a += 360;
            return Math.Round(a, 6);
        }
        ShadowEffect? Shadow(ShadowEffect? s) => s is null ? null : s with { OffsetX = s.OffsetX * k, OffsetY = s.OffsetY * k, Blur = s.Blur * k };
        switch (content)
        {
            case ShapeContent s:
            {
                double cx = s.X + s.Width / 2, cy = s.Y + s.Height / 2;
                var (nx, ny) = t.Point(cx, cy);
                double w = s.Width * t.ScaleX, h = s.Height * t.ScaleY, rot = s.Rotation;
                bool line = s.Kind == ShapeType.Line;
                if (t.MirrorX)
                {
                    rot = -rot;
                    if (line) w = -w;
                }
                if (t.MirrorY)
                {
                    if (line)
                    {
                        rot = -rot;
                        h = -h;
                    }
                    else rot = 180 - rot; // shapes are symmetric left to right: a vertical mirror is a half turn of that
                }
                rot = Norm(rot + t.Rotate);
                return s with
                {
                    X = nx - w / 2, Y = ny - h / 2, Width = w, Height = h, Rotation = rot,
                    StrokeWidth = s.StrokeWidth * k, CornerRadius = s.CornerRadius * k, Shadow = Shadow(s.Shadow),
                };
            }
            case TextContent x:
            {
                double cx = x.X + size.Width / 2, cy = x.Y + size.Height / 2;
                var (nx, ny) = t.Point(cx, cy);
                double rot = x.Rotation;
                if (t.MirrorX || t.MirrorY) rot = -rot;
                rot = Norm(rot + t.Rotate);
                return x with
                {
                    X = nx - size.Width * k / 2, Y = ny - size.Height * k / 2, Rotation = rot, Size = Math.Max(1, x.Size * k),
                    LetterSpacing = x.LetterSpacing * k, BoxWidth = x.BoxWidth * k,
                    Outline = x.Outline is { } o ? o with { Width = o.Width * k } : null, Shadow = Shadow(x.Shadow),
                };
            }
            default:
                return content;
        }
    }

    /// <summary>The same content with every colour passed through <paramref name="map"/> (palette remapping).</summary>
    public static LayerContent WithColours(this LayerContent content, Func<ColorRgb, ColorRgb> map) => content switch
    {
        TextContent t => t with
        {
            Color = map(t.Color),
            Outline = t.Outline is { } o ? o with { Color = map(o.Color) } : null,
            Shadow = t.Shadow is { } sh ? sh with { Color = map(sh.Color) } : null,
        },
        ShapeContent s => s with
        {
            Fill = s.Fill is { } f ? map(f) : null,
            Stroke = s.Stroke is { } st ? map(st) : null,
            Shadow = s.Shadow is { } sh ? sh with { Color = map(sh.Color) } : null,
        },
        _ => content,
    };
}

/// <summary>What drawing a text layer gave.</summary>
/// <param name="Width">Width of the text box (before rotation).</param>
/// <param name="Height">Height of the text box (before rotation).</param>
/// <param name="Bounds">A rectangle containing every pixel drawn (outline and shadow included).</param>
/// <param name="Substitute">The font actually used when the requested one isn't installed, else null.</param>
public sealed record TextRenderResult(double Width, double Height, PixelRect Bounds, string? Substitute = null);

/// <summary>
/// Renders text (fonts come from the operating system, so the app supplies this; the engine references only .NET).
/// </summary>
public interface ITextRenderer
{
    /// <summary>Draws the text into <paramref name="target"/> (document-sized, premultiplied, transparent where it draws).</summary>
    TextRenderResult Render(TextContent content, RasterImage target);
}
