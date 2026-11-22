using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Storybook;

/// <summary>
/// Mission 015 — Storybook pages as pictures: the page colour, each panel's drawing placed as its panel says (fit,
/// fill and crop, or actual size; never stretched), and the panel borders. The drawings themselves are only read.
/// </summary>
public static class StorybookRenderer
{
    /// <summary>
    /// Where a drawing of <paramref name="artWidth"/> × <paramref name="artHeight"/> goes for a panel (in the panel's pixel
    /// space): the rectangle it covers, which can be larger than the panel (it is cropped to the panel). The drawing keeps
    /// its shape. <paramref name="projectScale"/> is page pixels per project pixel (1 for a full-size page).
    /// </summary>
    public static (double X, double Y, double Width, double Height) Placement(PixelRect panel, int artWidth, int artHeight, PanelFit fit, double projectScale = 1)
    {
        double s = fit switch
        {
            PanelFit.Fit => Math.Min((double)panel.Width / artWidth, (double)panel.Height / artHeight),
            PanelFit.Fill => Math.Max((double)panel.Width / artWidth, (double)panel.Height / artHeight),
            _ => projectScale,
        };
        double w = artWidth * s, h = artHeight * s;
        return (panel.X + (panel.Width - w) / 2, panel.Y + (panel.Height - h) / 2, w, h);
    }

    /// <summary>
    /// The whole page. <paramref name="artwork"/> gives a drawing's flattened picture for its full path (null: missing).
    /// <paramref name="scale"/> below 1 draws a smaller copy (page thumbnails); exports use 1 (the project's resolution).
    /// </summary>
    public static RasterImage RenderPage(StorybookProject project, StoryPage page, Func<string, RasterImage?> artwork, double scale = 1)
    {
        var (pw, ph) = project.PagePixels;
        int w = Math.Max(1, (int)Math.Round(pw * scale)), h = Math.Max(1, (int)Math.Round(ph * scale));
        var image = new RasterImage(w, h);
        image.Fill(Pixel.Opaque(project.PaperColour));
        var rects = project.PanelRects(page);
        int border = project.BorderMm <= 0 ? 0 : Math.Max(1, (int)Math.Round(project.Pixels(project.BorderMm) * scale));
        for (int i = 0; i < rects.Count && i < page.Panels.Count; i++)
        {
            var full = project.PanelPixels(rects[i]);
            var panel = Scale(full, scale);
            if (project.Resolve(page.Panels[i].Artwork) is { } path && artwork(path) is { } art)
                DrawArt(image, art, panel, page.Panels[i].Fit, scale);
            if (border > 0) Frame(image, panel, border, BorderColour);
        }
        return image;
    }

    /// <summary>The border colour: a soft near-black, like ink.</summary>
    public static readonly uint BorderColour = Pixel.Opaque(new ColorRgb(0x1E, 0x1A, 0x18));

    private static PixelRect Scale(PixelRect r, double s)
    {
        if (s == 1) return r;
        int x0 = (int)Math.Round(r.X * s), y0 = (int)Math.Round(r.Y * s), x1 = (int)Math.Round(r.Right * s), y1 = (int)Math.Round(r.Bottom * s);
        return new PixelRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    /// <summary>Draws a drawing into its panel: scaled once with good resampling (averaging when it shrinks), then
    /// cropped to the panel and laid over the page (transparent parts of the drawing show the page).</summary>
    public static void DrawArt(RasterImage page, RasterImage art, PixelRect panel, PanelFit fit, double projectScale = 1)
    {
        var (x, y, w, h) = Placement(panel, art.Width, art.Height, fit, projectScale);
        int tw = Math.Max(1, (int)Math.Round(w)), th = Math.Max(1, (int)Math.Round(h));
        int left = (int)Math.Round(x), top = (int)Math.Round(y);
        // Only the part inside the panel (and the page) is needed.
        var visible = new PixelRect(left, top, tw, th).Intersect(panel).Intersect(page.Bounds);
        if (visible.IsEmpty) return;
        if (tw == art.Width && th == art.Height)
        {
            Blend(page, art, visible, visible.X - left, visible.Y - top);
            return;
        }
        if (tw <= art.Width && th <= art.Height)
        {
            // Shrinking: every covered pixel is averaged.
            Blend(page, Resampler.Resize(art, tw, th, Interpolation.Bilinear), visible, visible.X - left, visible.Y - top);
            return;
        }
        // Enlarging: only the visible part is sampled, from a copy whose edge pixels are repeated outwards, so the
        // drawing's border stays as solid as the drawing (no fading into the page).
        const int pad = 3;
        var padded = Padded(art, pad);
        var m = Geometry.Affine.Translation(-pad, -pad).Then(Geometry.Affine.Scale((double)tw / art.Width, (double)th / art.Height));
        var part = Resampler.Render(padded, m, new PixelRect(visible.X - left, visible.Y - top, visible.Width, visible.Height), Interpolation.Bicubic);
        Blend(page, part, visible, 0, 0);
    }

    /// <summary>A copy with <paramref name="pad"/> extra pixels on every side, repeating the edge pixels.</summary>
    private static RasterImage Padded(RasterImage src, int pad)
    {
        var dst = new RasterImage(src.Width + 2 * pad, src.Height + 2 * pad);
        for (int y = 0; y < dst.Height; y++)
        {
            int sy = Math.Clamp(y - pad, 0, src.Height - 1);
            var row = dst.Row(y);
            var from = src.Row(sy);
            for (int x = 0; x < dst.Width; x++) row[x] = from[Math.Clamp(x - pad, 0, src.Width - 1)];
        }
        return dst;
    }

    private static void Blend(RasterImage page, RasterImage src, PixelRect area, int sx, int sy)
    {
        for (int yy = 0; yy < area.Height; yy++)
        {
            var dst = page.Row(area.Y + yy).Slice(area.X, area.Width);
            var from = src.Row(sy + yy).Slice(sx, area.Width);
            for (int i = 0; i < dst.Length; i++)
            {
                uint p = from[i];
                if (p >> 24 == 255) dst[i] = p;
                else if (p != 0) dst[i] = Pixel.Over(p, dst[i]);
            }
        }
    }

    /// <summary>A border just inside the panel's edges.</summary>
    public static void Frame(RasterImage image, PixelRect r, int thickness, uint colour)
    {
        var bounds = r.Intersect(image.Bounds);
        if (bounds.IsEmpty) return;
        thickness = Math.Min(thickness, Math.Max(1, Math.Min(r.Width, r.Height) / 2));
        void Fill(PixelRect area)
        {
            area = area.Intersect(image.Bounds);
            for (int y = area.Y; y < area.Bottom; y++) image.Row(y).Slice(area.X, area.Width).Fill(colour);
        }
        Fill(new PixelRect(r.X, r.Y, r.Width, thickness));
        Fill(new PixelRect(r.X, r.Bottom - thickness, r.Width, thickness));
        Fill(new PixelRect(r.X, r.Y, thickness, r.Height));
        Fill(new PixelRect(r.Right - thickness, r.Y, thickness, r.Height));
    }

    /// <summary>A drawing as it appears in a panel: the frame it shows (for an animation, the frame it was saved on).</summary>
    public static RasterImage Flatten(Documents.Document doc) => doc.IsAnimated ? doc.RenderFrame(doc.CurrentFrame) : doc.Flatten();

    /// <summary>Reads a drawing for a page: a .dnk (flattened) or any picture file the engine reads (PNG).</summary>
    public static RasterImage? LoadArtwork(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (path.EndsWith(DnkFile.Extension, StringComparison.OrdinalIgnoreCase)) return Flatten(DnkFile.Load(path));
            return PngCodec.DecodeAny(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DnkFormatException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>The page file names for "all pages": story.png → story_01.png, story_02.png …</summary>
    public static IReadOnlyList<string> PagePaths(string chosen, int count)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(chosen)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(chosen);
        if (string.IsNullOrWhiteSpace(name)) name = "page";
        int digits = Math.Max(2, count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        return Enumerable.Range(1, count).Select(i => Path.Combine(dir, $"{name}_{i.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0')}.png")).ToList();
    }
}
