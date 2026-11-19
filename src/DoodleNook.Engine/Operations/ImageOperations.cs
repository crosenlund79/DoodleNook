using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>Whole-picture changes: resize, canvas size, crop, rotate and flip. Each is one undo step.</summary>
public static class ImageOperations
{
    public static void ResizeImage(Document doc, int width, int height, Interpolation mode)
    {
        RasterImage.Validate(width, height);
        if (width == doc.Width && height == doc.Height) return;
        var images = doc.Layers.ToDictionary(l => l.Id, l => Resampler.Resize(l.Image, width, height, mode));
        SelectionMask? selection = null;
        if (doc.Selection is { } sel) selection = ResizeMask(sel.Data, sel.Width, sel.Height, width, height);
        doc.ApplyState(new DocumentState(width, height, images, selection), "Image size");
    }

    /// <summary>Changes the canvas size without scaling. <paramref name="anchorX"/>/<paramref name="anchorY"/> (0–1) say
    /// where the old picture sits: 0.5, 0.5 keeps it centred.</summary>
    public static void ResizeCanvas(Document doc, int width, int height, double anchorX = 0.5, double anchorY = 0.5)
    {
        RasterImage.Validate(width, height);
        if (width == doc.Width && height == doc.Height) return;
        int ox = (int)Math.Round((width - doc.Width) * Math.Clamp(anchorX, 0, 1));
        int oy = (int)Math.Round((height - doc.Height) * Math.Clamp(anchorY, 0, 1));
        Reframe(doc, width, height, ox, oy, "Canvas size");
    }

    public static void Crop(Document doc, PixelRect rect)
    {
        rect = rect.Intersect(doc.Bounds);
        if (rect.IsEmpty || rect == doc.Bounds) return;
        Reframe(doc, rect.Width, rect.Height, -rect.X, -rect.Y, "Crop");
    }

    public static bool CropToSelection(Document doc)
    {
        if (doc.Selection is not { } sel || sel.IsEmpty) return false;
        Crop(doc, sel.Bounds);
        return true;
    }

    private static void Reframe(Document doc, int width, int height, int offsetX, int offsetY, string name)
    {
        var images = new Dictionary<int, RasterImage>();
        foreach (var layer in doc.Layers)
        {
            var img = new RasterImage(width, height);
            img.Paste(layer.Image, offsetX, offsetY);
            images[layer.Id] = img;
        }
        SelectionMask? selection = null;
        if (doc.Selection is { } sel)
        {
            var m = new SelectionMask(width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    m.Data[y * width + x] = sel[x - offsetX, y - offsetY];
            m.UpdateBounds();
            selection = m.IsEmpty ? null : m;
        }
        doc.ApplyState(new DocumentState(width, height, images, selection), name);
    }

    /// <summary>Rotates the whole picture by quarter turns clockwise (1 = 90°, 2 = 180°, 3 = 90° anticlockwise).</summary>
    public static void Rotate(Document doc, int quarterTurns)
    {
        quarterTurns = ((quarterTurns % 4) + 4) % 4;
        if (quarterTurns == 0) return;
        int w = doc.Width, h = doc.Height;
        int nw = quarterTurns % 2 == 1 ? h : w, nh = quarterTurns % 2 == 1 ? w : h;
        (int X, int Y) Map(int x, int y) => quarterTurns switch
        {
            1 => (h - 1 - y, x),
            2 => (w - 1 - x, h - 1 - y),
            _ => (y, w - 1 - x),
        };
        Remap(doc, nw, nh, Map, quarterTurns switch { 1 => "Rotate 90° clockwise", 2 => "Rotate 180°", _ => "Rotate 90° anticlockwise" });
    }

    public static void Flip(Document doc, bool horizontal)
    {
        int w = doc.Width, h = doc.Height;
        Remap(doc, w, h, (x, y) => horizontal ? (w - 1 - x, y) : (x, h - 1 - y), horizontal ? "Flip horizontal" : "Flip vertical");
    }

    private static void Remap(Document doc, int nw, int nh, Func<int, int, (int X, int Y)> map, string name)
    {
        int w = doc.Width, h = doc.Height;
        var images = new Dictionary<int, RasterImage>();
        foreach (var layer in doc.Layers)
        {
            var img = new RasterImage(nw, nh);
            var src = layer.Image.Pixels;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var (tx, ty) = map(x, y);
                    img.Pixels[ty * nw + tx] = src[y * w + x];
                }
            images[layer.Id] = img;
        }
        SelectionMask? selection = null;
        if (doc.Selection is { } sel)
        {
            var m = new SelectionMask(nw, nh);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var (tx, ty) = map(x, y);
                    m.Data[ty * nw + tx] = sel.Data[y * w + x];
                }
            m.UpdateBounds();
            selection = m;
        }
        doc.ApplyState(new DocumentState(nw, nh, images, selection), name);
    }

    private static SelectionMask ResizeMask(byte[] data, int w, int h, int nw, int nh)
    {
        var m = new SelectionMask(nw, nh);
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int sx = Math.Min(w - 1, (int)((x + 0.5) * w / nw)), sy = Math.Min(h - 1, (int)((y + 0.5) * h / nh));
                m.Data[y * nw + x] = data[sy * w + sx];
            }
        m.UpdateBounds();
        return m;
    }
}
