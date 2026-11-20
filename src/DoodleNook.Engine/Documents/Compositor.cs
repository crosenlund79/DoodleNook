using System.Buffers;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// Combines the visible layers of a document (bottom to top) over its paper, honouring opacity, blend modes, clipping,
/// masks, groups and adjustment layers. Works on any rectangle so the screen only redraws what changed. Never modifies
/// the layers.
/// <para>
/// A group composites its own layers into a buffer first and then goes onto the picture like one layer (its opacity,
/// blend mode and mask apply to the result). An adjustment layer changes the colours of everything below it in the same
/// group (or the whole picture at the top level), faded by its opacity and mask; in a palette-locked picture its result
/// keeps to the palette, alpha unchanged.
/// </para>
/// </summary>
public static class Compositor
{
    /// <summary>Composes what is shown now. <paramref name="includeOverlays"/> adds display-only extras: floating
    /// (move/transform) pixels and onion skins. Exports and saving never include them.</summary>
    public static void Compose(Document doc, PixelRect area, RasterImage target, bool includeOverlays, bool includePaper = true) =>
        Core(doc, area, target, includePaper, includeOverlays, l => (doc.ImageFor(l), l.ContentBounds));

    /// <summary>Composes any frame of an animation (without overlays), whatever frame is current.</summary>
    public static void ComposeFrame(Document doc, int frame, PixelRect area, RasterImage target, bool includePaper)
    {
        if (frame == doc.CurrentFrame)
        {
            Core(doc, area, target, includePaper, false, l => (l.Image, l.ContentBounds));
            return;
        }
        Core(doc, area, target, includePaper, false, l => AtFrame(doc, l, frame));
    }

    /// <summary>Composes the given layers' drawings at a frame without paper (onion skins of whole frames).</summary>
    internal static void ComposeLayersAt(Document doc, int frame, PixelRect area, RasterImage target) =>
        Core(doc, area, target, false, false, l => AtFrame(doc, l, frame));

    /// <summary>A layer's pixels at a frame: its drawing there, or (text, shapes) the same on every frame.</summary>
    private static (RasterImage? Image, PixelRect Bounds) AtFrame(Document doc, Layer l, int frame) =>
        !l.IsTrack ? (l.Image, l.ContentBounds) : doc.CelAt(l, frame) is { } cel ? (cel.Image, cel.ContentBounds) : (null, PixelRect.Empty);

    private static void Core(Document doc, PixelRect area, RasterImage target, bool includePaper, bool includeOverlays,
        Func<Layer, (RasterImage? Image, PixelRect Bounds)> source)
    {
        area = area.Intersect(doc.Bounds).Intersect(target.Bounds);
        if (area.IsEmpty) return;
        int w = doc.Width;
        var dst = target.Pixels;

        uint paper = includePaper && doc.Paper is { } p && doc.PaperVisible ? Pixel.Opaque(p) : 0u;
        for (int y = area.Y; y < area.Bottom; y++) Array.Fill(dst, paper, y * w + area.X, area.Width);

        // Onion skins: tinted neighbouring drawings between the paper and the layers.
        if (includeOverlays && doc.OnionImage is { } onion)
        {
            var region = doc.OnionBounds.Intersect(area);
            var o = onion.Pixels;
            for (int y = region.Y; y < region.Bottom; y++)
            {
                int row = y * w;
                for (int x = region.X; x < region.Right; x++)
                {
                    uint s = o[row + x];
                    if (s != 0) dst[row + x] = Pixel.Over(s, dst[row + x]);
                }
            }
        }

        var surface = new Surface(dst, w, 0, 0);
        Stack(doc, 0, area, surface, source, includeOverlays ? doc.Floating : null, 0);
    }

    /// <summary>Pixels addressed in document coordinates: index = (y − OriginY) · Stride + (x − OriginX).</summary>
    private readonly record struct Surface(uint[] Pixels, int Stride, int OriginX, int OriginY)
    {
        public int Index(int x, int y) => (y - OriginY) * Stride + (x - OriginX);
    }

    private static void Stack(Document doc, int parentId, PixelRect area, Surface dst, Func<Layer, (RasterImage? Image, PixelRect Bounds)> source,
        FloatingPixels? floating, int depth)
    {
        var layers = doc.Layers;
        Layer? clipBase = null;
        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer.ParentId != parentId) continue;
            if (!layer.ClipToBelow) clipBase = layer;
            bool clipped = layer.ClipToBelow && clipBase is not null && !ReferenceEquals(clipBase, layer) && !clipBase.IsGroup && clipBase.Kind != LayerKind.Adjustment;
            if (!layer.Visible || (clipped && !clipBase!.Visible)) continue;
            int opacity = (int)Math.Round(Math.Clamp(layer.Opacity, 0, 1) * 255);
            if (opacity == 0) continue;
            uint[]? mask = layer.HasMask && layer.MaskEnabled ? layer.Mask!.Pixels : null;
            uint[]? baseSrc = null;
            if (clipped)
            {
                var (baseImage, _) = source(clipBase!);
                if (baseImage is null) continue; // nothing to clip to
                baseSrc = baseImage.Pixels;
            }

            switch (layer.Kind)
            {
                case LayerKind.Group when depth < 32:
                {
                    int n = area.Width * area.Height;
                    var buffer = ArrayPool<uint>.Shared.Rent(n);
                    try
                    {
                        Array.Clear(buffer, 0, n);
                        var inner = new Surface(buffer, area.Width, area.X, area.Y);
                        Stack(doc, layer.Id, area, inner, source, floating, depth + 1);
                        BlendBuffer(doc, area, dst, inner, opacity, mask, baseSrc, layer.Blend);
                    }
                    finally
                    {
                        ArrayPool<uint>.Shared.Return(buffer);
                    }
                    break;
                }
                case LayerKind.Adjustment:
                    if (layer.Content is AdjustmentContent { Adjustment: { MapsColours: true } adjustment })
                        Adjust(doc, area, dst, adjustment, opacity, mask, baseSrc);
                    break;
                case LayerKind.Group:
                    break;
                default:
                {
                    var (image, bounds) = source(layer);
                    bool hasFloat = floating is not null && floating.LayerId == layer.Id;
                    if (image is null && !hasFloat) continue;
                    var region = image is null ? PixelRect.Empty : bounds;
                    if (hasFloat) region = region.Union(floating!.Bounds);
                    region = region.Intersect(area);
                    if (region.IsEmpty) continue;
                    BlendLayer(doc, region, dst, image?.Pixels, hasFloat ? floating : null, opacity, mask, baseSrc, layer.Blend);
                    break;
                }
            }
        }
    }

    /// <summary>A layer's pixels (document-sized) onto the destination: the hot path, kept as a plain loop.</summary>
    private static void BlendLayer(Document doc, PixelRect region, Surface dst, uint[]? src, FloatingPixels? floating, int opacity, uint[]? mask,
        uint[]? baseSrc, BlendMode mode)
    {
        int w = doc.Width;
        var d = dst.Pixels;
        for (int y = region.Y; y < region.Bottom; y++)
        {
            int row = y * w;
            int di = dst.Index(region.X, y);
            for (int x = region.X; x < region.Right; x++, di++)
            {
                uint s = src is null ? 0 : src[row + x];
                if (floating is not null)
                {
                    uint f = floating.Image.GetOrTransparent(x - floating.X, y - floating.Y);
                    if (f != 0) s = Pixel.Over(f, s);
                }
                if (s == 0) continue;
                if (opacity < 255) s = Blending.Fade(s, opacity);
                if (mask is not null)
                {
                    s = Blending.Fade(s, Layer.MaskValue(mask[row + x]));
                    if (s == 0) continue;
                }
                if (baseSrc is not null)
                {
                    s = Blending.Fade(s, Pixel.A(baseSrc[row + x]));
                    if (s == 0) continue;
                }
                d[di] = mode == BlendMode.Normal ? Pixel.Over(s, d[di]) : Blending.Blend(s, d[di], mode);
            }
        }
    }

    /// <summary>A group's composited buffer onto the destination.</summary>
    private static void BlendBuffer(Document doc, PixelRect area, Surface dst, Surface buffer, int opacity, uint[]? mask, uint[]? baseSrc, BlendMode mode)
    {
        int w = doc.Width;
        var d = dst.Pixels;
        var b = buffer.Pixels;
        for (int y = area.Y; y < area.Bottom; y++)
        {
            int row = y * w;
            int di = dst.Index(area.X, y), bi = buffer.Index(area.X, y);
            for (int x = area.X; x < area.Right; x++, di++, bi++)
            {
                uint s = b[bi];
                if (s == 0) continue;
                if (opacity < 255) s = Blending.Fade(s, opacity);
                if (mask is not null)
                {
                    s = Blending.Fade(s, Layer.MaskValue(mask[row + x]));
                    if (s == 0) continue;
                }
                if (baseSrc is not null)
                {
                    s = Blending.Fade(s, Pixel.A(baseSrc[row + x]));
                    if (s == 0) continue;
                }
                d[di] = mode == BlendMode.Normal ? Pixel.Over(s, d[di]) : Blending.Blend(s, d[di], mode);
            }
        }
    }

    private static void Adjust(Document doc, PixelRect area, Surface dst, Adjustment adjustment, int opacity, uint[]? mask, uint[]? baseSrc)
    {
        int w = doc.Width;
        var d = dst.Pixels;
        var conformer = doc.PaletteConformer;
        for (int y = area.Y; y < area.Bottom; y++)
        {
            int row = y * w;
            int di = dst.Index(area.X, y);
            for (int x = area.X; x < area.Right; x++, di++)
            {
                uint p = d[di];
                if (p == 0) continue;
                int k = opacity;
                if (mask is not null) k = k * Layer.MaskValue(mask[row + x]) / 255;
                if (baseSrc is not null) k = k * Pixel.A(baseSrc[row + x]) / 255;
                if (k == 0) continue;
                uint m = adjustment.MapPixel(p);
                if (k < 255) m = Mix(p, m, k);
                // A palette-locked picture: adjustments give palette colours too (alpha unchanged).
                d[di] = conformer is null ? m : conformer.Conform(m);
            }
        }
    }

    /// <summary>Premultiplied a → b by k/255.</summary>
    private static uint Mix(uint a, uint b, int k)
    {
        static byte L(byte x, byte y, int k) => (byte)(x + ((y - x) * k + (y >= x ? 127 : -127)) / 255);
        return Pixel.Pack(L(Pixel.A(a), Pixel.A(b), k), L(Pixel.R(a), Pixel.R(b), k), L(Pixel.G(a), Pixel.G(b), k), L(Pixel.B(a), Pixel.B(b), k));
    }
}
