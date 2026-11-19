using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Controls;

/// <summary>
/// Renders brush previews with the real DoodleNook engine (a short S-curve with a pressure swell), so what the
/// preset tiles show is exactly what the brush paints.
/// </summary>
public static class BrushPreview
{
    public static WriteableBitmap Render(BrushSettings brush, ColorRgb color, int width, int height, double scale = 2)
    {
        int w = (int)Math.Round(width * scale), h = (int)Math.Round(height * scale);
        var doc = new Document(w, h);
        // Scale the brush into the little preview, like the playground did (stroke width 1.5–9 px).
        double size = Math.Clamp(brush.Size / 5, 1.5, height * 0.38) * scale;
        var b = brush.Normalized() with { Size = size, Smoothing = 0 };
        var engine = new StrokeEngine(doc);
        const int n = 48;
        StrokeSample At(int i)
        {
            double t = (double)i / n;
            // Cubic Bezier from left to right with a gentle S.
            double x0 = 3, x1 = width * 0.3, x2 = width * 0.55, x3 = width - 3;
            double y0 = height * 0.7, y1 = height * 0.05, y2 = height * 0.95, y3 = height * 0.3;
            double u = 1 - t;
            double x = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            double y = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;
            float p = (float)(0.25 + 0.75 * Math.Sin(Math.PI * Math.Min(1, t * 1.15)));
            return new StrokeSample(x * scale, y * scale, Math.Clamp(p, 0.15f, 1f));
        }
        engine.Begin(new StrokeStyle(b, PressureCurve.Normal, color, PaintMode.Paint), At(0));
        for (int i = 1; i <= n; i++) engine.Add(At(i));
        engine.End();
        return ToBitmap(doc.Layer, scale);
    }

    public static WriteableBitmap ToBitmap(RasterImage image, double scale = 1)
    {
        var bmp = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96 * scale, 96 * scale),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        unsafe
        {
            fixed (uint* src = image.Pixels)
            {
                for (int y = 0; y < image.Height; y++)
                    Buffer.MemoryCopy(src + (long)y * image.Width, (byte*)fb.Address + (long)y * fb.RowBytes, fb.RowBytes, image.Width * 4L);
            }
        }
        return bmp;
    }
}
