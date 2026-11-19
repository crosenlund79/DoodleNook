using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>A small colour tag for organising layers.</summary>
public enum LayerLabel
{
    None,
    Red,
    Orange,
    Yellow,
    Green,
    Blue,
    Purple,
}

/// <summary>Everything about a layer except its pixels (used for undo and saving).</summary>
public sealed record LayerProperties(
    string Name,
    bool Visible = true,
    double Opacity = 1,
    BlendMode Blend = BlendMode.Normal,
    bool Locked = false,
    bool AlphaLocked = false,
    bool ClipToBelow = false,
    LayerLabel Label = LayerLabel.None);

/// <summary>
/// One raster layer: premultiplied pixels the size of the document, plus its properties.
/// Layers are owned by a <see cref="Document"/>; change them through the document so undo and redraws stay correct.
/// </summary>
public sealed class Layer
{
    internal Layer(int id, LayerProperties properties, RasterImage image)
    {
        Id = id;
        Properties = properties;
        Image = image;
        ContentBounds = image.ContentBounds();
    }

    /// <summary>Stable identifier within its document (survives reordering, saving and undo).</summary>
    public int Id { get; }

    public LayerProperties Properties { get; internal set; }

    public RasterImage Image { get; internal set; }

    public string Name => Properties.Name;
    public bool Visible => Properties.Visible;
    public double Opacity => Properties.Opacity;
    public BlendMode Blend => Properties.Blend;
    public bool Locked => Properties.Locked;
    public bool AlphaLocked => Properties.AlphaLocked;
    public bool ClipToBelow => Properties.ClipToBelow;
    public LayerLabel Label => Properties.Label;

    /// <summary>A rectangle that contains all non-transparent pixels (it may be larger; never smaller).</summary>
    public PixelRect ContentBounds { get; internal set; }

    /// <summary>Incremented whenever the pixels change (lets the UI refresh thumbnails cheaply).</summary>
    public int Revision { get; internal set; }

    internal void Touch(PixelRect area)
    {
        ContentBounds = ContentBounds.Union(area.Intersect(Image.Bounds));
        Revision++;
    }

    internal void RecalculateBounds()
    {
        ContentBounds = Image.ContentBounds();
        Revision++;
    }

    public override string ToString() => $"Layer {Id} \"{Name}\"";
}
