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
        // Scale the brush into the little preview (the playground drew 1.5–9 px wide strokes), and keep it legible:
        // a little more opacity than the real brush, like the playground's preview.
        double size = Math.Clamp(brush.Size / 4, 2, height * 0.42) * scale;
        double margin = size / 2 / scale + 1;
        var b = brush.Normalized() with
        {
            Size = size,
            Smoothing = 0,
            Opacity = Math.Clamp(brush.Opacity + 0.15, 0.35, 1),
        };
        var engine = new StrokeEngine(doc);
        const int n = 64;
        StrokeSample At(int i)
        {
            double t = (double)i / n;
            // A cubic Bezier S-curve from left to right, kept inside the image by the brush radius.
            double x0 = margin + 1, x1 = width * 0.3, x2 = width * 0.55, x3 = width - margin - 1;
            double top = margin, bottom = height - margin;
            double y0 = bottom, y1 = top - (bottom - top) * 0.55, y2 = bottom + (bottom - top) * 0.55, y3 = top;
            double u = 1 - t;
            double x = u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3;
            double y = u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3;
            float p = (float)(0.45 + 0.55 * Math.Sin(Math.PI * t));
            return new StrokeSample(x * scale, y * scale, p);
        }
        engine.Begin(new StrokeStyle(b, PressureCurve.Normal, color, PaintMode.Paint), At(0));
        for (int i = 1; i <= n; i++) engine.Add(At(i));
        engine.End();
        // 96 DPI on purpose: the Image controls showing previews have a fixed size and scale the 2× bitmap down.
        return ToBitmap(doc.Layer);
    }

    public static WriteableBitmap ToBitmap(RasterImage image)
    {
        var bmp = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
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
