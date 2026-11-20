using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// Combines the visible layers of a document (bottom to top) over its paper, honouring opacity, blend modes and
/// clipping. Works on any rectangle so the screen only redraws what changed. Never modifies the layers.
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
        Core(doc, area, target, includePaper, false, l => doc.CelAt(l, frame) is { } cel ? (cel.Image, cel.ContentBounds) : (null, PixelRect.Empty));
    }

    /// <summary>Composes the given layers' drawings at a frame without paper (onion skins of whole frames).</summary>
    internal static void ComposeLayersAt(Document doc, int frame, PixelRect area, RasterImage target) =>
        Core(doc, area, target, false, false, l => doc.CelAt(l, frame) is { } cel ? (cel.Image, cel.ContentBounds) : (null, PixelRect.Empty));

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

        var layers = doc.Layers;
        var floating = includeOverlays ? doc.Floating : null;
        Layer? clipBase = null;
        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (!layer.ClipToBelow) clipBase = layer;
            bool clipped = layer.ClipToBelow && clipBase is not null && !ReferenceEquals(clipBase, layer);
            if (!layer.Visible || (clipped && !clipBase!.Visible)) continue;

            var (image, bounds) = source(layer);
            bool hasFloat = floating is not null && floating.LayerId == layer.Id;
            if (image is null && !hasFloat) continue;
            var region = image is null ? PixelRect.Empty : bounds;
            if (hasFloat) region = region.Union(floating!.Bounds);
            region = region.Intersect(area);
            if (region.IsEmpty) continue;

            var src = image?.Pixels;
            uint[]? baseSrc = null;
            if (clipped)
            {
                var (baseImage, _) = source(clipBase!);
                if (baseImage is null) continue; // nothing to clip to
                baseSrc = baseImage.Pixels;
            }
            int opacity = (int)Math.Round(Math.Clamp(layer.Opacity, 0, 1) * 255);
            if (opacity == 0) continue;
            var mode = layer.Blend;

            for (int y = region.Y; y < region.Bottom; y++)
            {
                int row = y * w;
                for (int x = region.X; x < region.Right; x++)
                {
                    uint s = src is null ? 0 : src[row + x];
                    if (hasFloat)
                    {
                        uint f = floating!.Image.GetOrTransparent(x - floating.X, y - floating.Y);
                        if (f != 0) s = Pixel.Over(f, s);
                    }
                    if (s == 0) continue;
                    if (opacity < 255) s = Blending.Fade(s, opacity);
                    if (baseSrc is not null)
                    {
                        s = Blending.Fade(s, Pixel.A(baseSrc[row + x]));
                        if (s == 0) continue;
                    }
                    dst[row + x] = mode == BlendMode.Normal ? Pixel.Over(s, dst[row + x]) : Blending.Blend(s, dst[row + x], mode);
                }
            }
        }
    }
}
