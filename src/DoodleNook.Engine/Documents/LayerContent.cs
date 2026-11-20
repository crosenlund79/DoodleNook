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

/// <summary>The pixels of rendered text, and the text box's size.</summary>
/// <param name="Image">Document-sized, premultiplied.</param>
/// <param name="Width">Width of the text box (before rotation).</param>
/// <param name="Height">Height of the text box (before rotation).</param>
/// <param name="Substitute">The font actually used when the requested one isn't installed, else null.</param>
public sealed record TextRenderResult(RasterImage Image, double Width, double Height, string? Substitute = null);

/// <summary>
/// Renders text (fonts come from the operating system, so the app supplies this; the engine references only .NET).
/// </summary>
public interface ITextRenderer
{
    TextRenderResult Render(TextContent content, int width, int height);
}
