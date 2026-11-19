using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>Builds selection masks (rectangle, ellipse, lasso, magic wand, layer pixels).</summary>
public static class Selections
{
    public static SelectionMask Rectangle(Document doc, double x0, double y0, double x1, double y1)
    {
        // Rectangles snap to whole pixels, like other image editors.
        var r = PixelRect.FromEdges(
            (int)Math.Round(Math.Min(x0, x1)), (int)Math.Round(Math.Min(y0, y1)),
            (int)Math.Round(Math.Max(x0, x1)), (int)Math.Round(Math.Max(y0, y1)));
        return SelectionMask.Rectangle(doc.Width, doc.Height, r);
    }

    public static SelectionMask Ellipse(Document doc, double x0, double y0, double x1, double y1, bool antialias)
    {
        if (!antialias)
        {
            var m = new SelectionMask(doc.Width, doc.Height);
            Rasterize.PixelEllipse((int)Math.Floor(Math.Min(x0, x1)), (int)Math.Floor(Math.Min(y0, y1)),
                (int)Math.Ceiling(Math.Max(x0, x1)) - 1, (int)Math.Ceiling(Math.Max(y0, y1)) - 1, filled: true, (x, y) =>
                {
                    if (doc.Bounds.Contains(x, y)) m.Data[y * doc.Width + x] = 255;
                });
            m.UpdateBounds();
            return m;
        }
        var pts = Rasterize.EllipsePoints(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
        return SelectionMask.FromCoverage(doc.Width, doc.Height, Rasterize.Polygon(pts, doc.Bounds));
    }

    public static SelectionMask Lasso(Document doc, IReadOnlyList<(double X, double Y)> points, bool antialias)
    {
        if (points.Count < 3) return new SelectionMask(doc.Width, doc.Height);
        return SelectionMask.FromCoverage(doc.Width, doc.Height, Rasterize.Polygon(points, doc.Bounds, antialias));
    }

    /// <summary>Selects pixels similar in colour to the one clicked.</summary>
    public static SelectionMask MagicWand(Document doc, Layer layer, int x, int y, int tolerance, bool contiguous, bool sampleMerged)
    {
        var source = sampleMerged ? doc.Flatten() : layer.Image;
        var region = FloodFill.Region(source, x, y, tolerance, contiguous);
        var m = new SelectionMask(doc.Width, doc.Height);
        Array.Copy(region, m.Data, region.Length);
        m.UpdateBounds();
        return m;
    }

    /// <summary>Selects the visible pixels of a layer (by their opacity).</summary>
    public static SelectionMask LayerPixels(Document doc, Layer layer)
    {
        var m = new SelectionMask(doc.Width, doc.Height);
        var px = layer.Image.Pixels;
        for (int i = 0; i < px.Length; i++) m.Data[i] = Pixel.A(px[i]);
        m.UpdateBounds();
        return m;
    }
}
