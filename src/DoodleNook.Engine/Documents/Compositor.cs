using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// Combines the visible layers of a document (bottom to top) over its paper, honouring opacity, blend modes and
/// clipping. Works on any rectangle so the screen only redraws what changed. Never modifies the layers.
/// </summary>
public static class Compositor
{
    public static void Compose(Document doc, PixelRect area, RasterImage target, bool includeFloating)
    {
        area = area.Intersect(doc.Bounds).Intersect(target.Bounds);
        if (area.IsEmpty) return;
        int w = doc.Width;
        var dst = target.Pixels;

        uint paper = doc.Paper is { } p && doc.PaperVisible ? Pixel.Opaque(p) : 0u;
        for (int y = area.Y; y < area.Bottom; y++) Array.Fill(dst, paper, y * w + area.X, area.Width);

        var layers = doc.Layers;
        var floating = includeFloating ? doc.Floating : null;
        Layer? clipBase = null;
        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (!layer.ClipToBelow) clipBase = layer;
            bool clipped = layer.ClipToBelow && clipBase is not null && !ReferenceEquals(clipBase, layer);
            if (!layer.Visible || (clipped && !clipBase!.Visible)) continue;

            var region = layer.ContentBounds;
            bool hasFloat = floating is not null && floating.LayerId == layer.Id;
            if (hasFloat) region = region.Union(floating!.Bounds);
            region = region.Intersect(area);
            if (region.IsEmpty) continue;

            var src = doc.ImageFor(layer).Pixels;
            var baseSrc = clipped ? doc.ImageFor(clipBase!).Pixels : null;
            int opacity = (int)Math.Round(Math.Clamp(layer.Opacity, 0, 1) * 255);
            if (opacity == 0) continue;
            var mode = layer.Blend;

            for (int y = region.Y; y < region.Bottom; y++)
            {
                int row = y * w;
                for (int x = region.X; x < region.Right; x++)
                {
                    uint s = src[row + x];
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
