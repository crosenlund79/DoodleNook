using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// Builds the onion-skin overlay for the current frame: the previous and next <em>drawings</em> (a held drawing counts
/// once, and the current drawing is never repeated), tinted and faded. Only the active layer is used by default, so a
/// held background doesn't fill the view with colour; <see cref="OnionSkinSettings.AllLayers"/> shows whole frames.
/// The overlay is display-only (see <see cref="Document.SetOnionSkin"/>).
/// </summary>
public static class OnionSkin
{
    /// <summary>The frames whose drawings are shown before and after the current frame, nearest first.</summary>
    public static (List<int> Before, List<int> After) Frames(Document doc, OnionSkinSettings settings)
    {
        var before = new List<int>();
        var after = new List<int>();
        if (doc.Animation is not { } a || !settings.Enabled) return (before, after);
        int cur = a.CurrentFrame;
        var layer = doc.ActiveLayer;

        if (!settings.AllLayers)
        {
            var current = doc.CelAt(layer, cur);
            var seen = new HashSet<Cel>();
            for (int f = cur - 1; f >= 0 && before.Count < settings.Before; f--)
                if (doc.CelAt(layer, f) is { IsEmpty: false } c && !ReferenceEquals(c, current) && seen.Add(c)) before.Add(f);
            seen.Clear();
            for (int f = cur + 1; f < a.FrameCount && after.Count < settings.After; f++)
                if (doc.CelAt(layer, f) is { IsEmpty: false } c && !ReferenceEquals(c, current) && seen.Add(c)) after.Add(f);
            return (before, after);
        }

        // Whole frames: a frame counts when its combination of drawings differs from the ones already used.
        string Key(int f) => string.Join(",", doc.Layers.Where(l => l.Visible).Select(l => doc.CelAt(l, f)?.Id ?? 0));
        var keys = new HashSet<string> { Key(cur) };
        for (int f = cur - 1; f >= 0 && before.Count < settings.Before; f--)
            if (keys.Add(Key(f))) before.Add(f);
        keys = [Key(cur)];
        for (int f = cur + 1; f < a.FrameCount && after.Count < settings.After; f++)
            if (keys.Add(Key(f))) after.Add(f);
        return (before, after);
    }

    /// <summary>The overlay image and the area it covers, or (null, empty) when there is nothing to show.</summary>
    public static (RasterImage? Image, PixelRect Bounds) Build(Document doc, OnionSkinSettings settings)
    {
        settings = settings.Normalized();
        var (before, after) = Frames(doc, settings);
        if (before.Count == 0 && after.Count == 0) return (null, PixelRect.Empty);
        var overlay = new RasterImage(doc.Width, doc.Height);
        var bounds = PixelRect.Empty;
        // Farthest first, so the nearest drawings end up on top; later frames under earlier ones.
        for (int i = after.Count - 1; i >= 0; i--) bounds = bounds.Union(Draw(doc, after[i], i, settings.AfterTint, settings, overlay));
        for (int i = before.Count - 1; i >= 0; i--) bounds = bounds.Union(Draw(doc, before[i], i, settings.BeforeTint, settings, overlay));
        return bounds.IsEmpty ? (null, PixelRect.Empty) : (overlay, bounds);
    }

    private static PixelRect Draw(Document doc, int frame, int distance, ColorRgb tint, OnionSkinSettings s, RasterImage overlay)
    {
        RasterImage source;
        PixelRect area;
        if (s.AllLayers)
        {
            area = doc.Layers.Where(l => l.Visible).Aggregate(PixelRect.Empty, (r, l) => r.Union(doc.CelAt(l, frame)?.ContentBounds ?? PixelRect.Empty));
            if (area.IsEmpty) return area;
            source = new RasterImage(doc.Width, doc.Height);
            Compositor.ComposeLayersAt(doc, frame, area, source);
        }
        else
        {
            if (doc.CelAt(doc.ActiveLayer, frame) is not { } cel) return PixelRect.Empty;
            source = cel.Image;
            area = cel.ContentBounds.Intersect(doc.Bounds);
        }
        double fade = s.Opacity * (s.Falloff ? Math.Pow(0.6, distance) : 1);
        // Mission 012: the active layer's own drawings fade with the layer's opacity, like the layer itself (whole frames
        // are composed with every layer's opacity already).
        if (!s.AllLayers) fade *= Math.Clamp(doc.ActiveLayer.Opacity, 0, 1);
        int w = doc.Width;
        var src = source.Pixels;
        var dst = overlay.Pixels;
        for (int y = area.Y; y < area.Bottom; y++)
        {
            int row = y * w;
            for (int x = area.X; x < area.Right; x++)
            {
                byte a = Pixel.A(src[row + x]);
                if (a == 0) continue;
                int oa = (int)Math.Round(a * fade);
                if (oa == 0) continue;
                // A tinted silhouette: the drawing's shape in the tint colour (premultiplied).
                uint tinted = Pixel.Pack((byte)oa, Pixel.Mul(tint.R, (byte)oa), Pixel.Mul(tint.G, (byte)oa), Pixel.Mul(tint.B, (byte)oa));
                dst[row + x] = Pixel.Over(tinted, dst[row + x]);
            }
        }
        return area;
    }
}
