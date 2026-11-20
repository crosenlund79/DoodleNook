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
/// <param name="ParentId">The group the layer belongs to (0: the picture itself).</param>
/// <param name="Collapsed">For a group: its layers are folded away in the layers panel.</param>
/// <param name="MaskEnabled">The layer's mask is used (switch it off to see the whole layer).</param>
public sealed record LayerProperties(
    string Name,
    bool Visible = true,
    double Opacity = 1,
    BlendMode Blend = BlendMode.Normal,
    bool Locked = false,
    bool AlphaLocked = false,
    bool ClipToBelow = false,
    LayerLabel Label = LayerLabel.None,
    int ParentId = 0,
    bool Collapsed = false,
    bool MaskEnabled = true);

/// <summary>
/// One raster layer: premultiplied pixels the size of the document, plus its properties.
/// Layers are owned by a <see cref="Document"/>; change them through the document so undo and redraws stay correct.
/// In an animation the layer is a <em>track</em>: one cel (or nothing) per frame, and <see cref="Image"/> is the
/// drawing shown at the current frame, so every tool keeps working on "the layer".
/// </summary>
public sealed class Layer
{
    private RasterImage? _blank;

    internal Layer(int id, LayerProperties properties, RasterImage image, LayerKind kind = LayerKind.Raster)
    {
        Id = id;
        Properties = properties;
        Image = image;
        Kind = kind;
        ContentBounds = image.ContentBounds();
    }

    /// <summary>Raster (paint), group, text, shape or adjustment.</summary>
    public LayerKind Kind { get; }

    /// <summary>The editable text, shape or adjustment (null for raster layers and groups).</summary>
    public LayerContent? Content { get; internal set; }

    /// <summary>The rendered text box's size (before rotation), for handles and alignment.</summary>
    public (double Width, double Height) ContentSize { get; internal set; }

    /// <summary>The font used instead of a missing one (text layers), else null.</summary>
    public string? SubstituteFont { get; internal set; }

    /// <summary>
    /// A layer mask: how much of the layer shows at each pixel, without erasing anything. Stored as a picture: white shows,
    /// black hides (paint with black to hide, white or the eraser to reveal). Null: no mask.
    /// </summary>
    public RasterImage? Mask { get; internal set; }

    public bool HasMask => Mask is not null;
    public bool MaskEnabled => Properties.MaskEnabled;
    public int ParentId => Properties.ParentId;
    public bool IsGroup => Kind == LayerKind.Group;

    /// <summary>True for layers you paint pixels on (raster layers).</summary>
    public bool IsPaintable => Kind == LayerKind.Raster;

    /// <summary>True for layers that hold pixels (raster, text and shape layers). Groups and adjustment layers have none:
    /// their <see cref="Image"/> is a 1 × 1 placeholder.</summary>
    public bool HasPixels => Kind is LayerKind.Raster or LayerKind.Text or LayerKind.Shape;

    /// <summary>Incremented whenever the mask changes (frame cache keys).</summary>
    public int MaskRevision { get; internal set; }

    /// <summary>True for text and shape layers (moved, resized and turned as objects).</summary>
    public bool IsObject => Kind is LayerKind.Text or LayerKind.Shape;

    /// <summary>In an animation: true when the layer has a drawing per frame (raster layers); text, shapes, adjustments
    /// and groups are the same on every frame.</summary>
    public bool IsTrack => Cells is not null;

    /// <summary>How much of the layer shows through its mask at a mask pixel (255 = all).</summary>
    public static byte MaskValue(uint maskPixel) => (byte)Math.Min(255, Pixel.R(maskPixel) + (255 - Pixel.A(maskPixel)));

    /// <summary>Stable identifier within its document (survives reordering, saving and undo).</summary>
    public int Id { get; }

    public LayerProperties Properties { get; internal set; }

    /// <summary>The pixels shown now: the layer's image, or in an animation the current frame's cel (or a blank).</summary>
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

    /// <summary>Animation track: the cel exposed at each frame (null = empty cell). Null for still pictures.</summary>
    internal List<Cel?>? Cells { get; set; }

    /// <summary>The cels of this track, one per frame (empty cells are null). Empty for still pictures.</summary>
    public IReadOnlyList<Cel?> Track => (IReadOnlyList<Cel?>?)Cells ?? Array.Empty<Cel?>();

    /// <summary>The cel shown at the current frame (null for an empty cell or a still picture).</summary>
    public Cel? CurrentCel { get; internal set; }

    /// <summary>A transparent image used while the current cell is empty; drawing on it turns it into a new cel.</summary>
    internal RasterImage BlankFor(int width, int height)
    {
        // Invariant: the blank stays transparent; an edit that changes it adopts it as a cel (see LayerEdit).
        if (_blank is null || _blank.Width != width || _blank.Height != height) _blank = new RasterImage(width, height);
        return _blank;
    }

    /// <summary>Forgets the blank scratch image (after it became a cel).</summary>
    internal void ReleaseBlank() => _blank = null;

    internal void Touch(PixelRect area)
    {
        ContentBounds = ContentBounds.Union(area.Intersect(Image.Bounds));
        Revision++;
        if (CurrentCel is { } cel && ReferenceEquals(cel.Image, Image))
        {
            cel.ContentBounds = ContentBounds;
            cel.Revision++;
        }
    }

    internal void RecalculateBounds()
    {
        ContentBounds = Image.ContentBounds();
        Revision++;
        if (CurrentCel is { } cel && ReferenceEquals(cel.Image, Image))
        {
            cel.ContentBounds = ContentBounds;
            cel.Revision++;
        }
    }

    public override string ToString() => $"Layer {Id} \"{Name}\"";
}
