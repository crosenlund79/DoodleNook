using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

public enum Interpolation
{
    /// <summary>Exact pixels (pixel art).</summary>
    Nearest,

    /// <summary>Smooth, fast.</summary>
    Bilinear,

    /// <summary>Smoothest and sharpest (Catmull-Rom).</summary>
    Bicubic,
}

/// <summary>Draws images through affine transforms and resizes them. Works on premultiplied pixels, so edges stay clean.</summary>
public static class Resampler
{
    /// <summary>Renders <paramref name="source"/> transformed by <paramref name="sourceToTarget"/> into a new image covering
    /// <paramref name="targetBounds"/> (in target coordinates).</summary>
    public static RasterImage Render(RasterImage source, Affine sourceToTarget, PixelRect targetBounds, Interpolation mode)
    {
        var dst = new RasterImage(Math.Max(1, targetBounds.Width), Math.Max(1, targetBounds.Height));
        var inv = sourceToTarget.Invert();
        int w = targetBounds.Width;
        for (int y = 0; y < targetBounds.Height; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var (u, v) = inv.Apply(targetBounds.X + x + 0.5, targetBounds.Y + y + 0.5);
                dst.Pixels[y * w + x] = Sample(source, u, v, mode);
            }
        }
        return dst;
    }

    /// <summary>Bounds (whole pixels) of a transformed w × h image.</summary>
    public static PixelRect TransformedBounds(Affine m, int w, int h)
    {
        var (l, t, r, b) = m.TransformBounds(0, 0, w, h);
        return PixelRect.FromBounds(l + 1e-6, t + 1e-6, r - 1e-6, b - 1e-6);
    }

    /// <summary>Samples at continuous source coordinates (pixel centres at +0.5).</summary>
    public static uint Sample(RasterImage src, double u, double v, Interpolation mode)
    {
        switch (mode)
        {
            case Interpolation.Nearest:
                return src.GetOrTransparent((int)Math.Floor(u), (int)Math.Floor(v));
            case Interpolation.Bilinear:
            {
                double fx = u - 0.5, fy = v - 0.5;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                double tx = fx - x0, ty = fy - y0;
                if (x0 < -1 || y0 < -1 || x0 >= src.Width || y0 >= src.Height) return 0;
                uint a = src.GetOrTransparent(x0, y0), b = src.GetOrTransparent(x0 + 1, y0);
                uint c = src.GetOrTransparent(x0, y0 + 1), d = src.GetOrTransparent(x0 + 1, y0 + 1);
                if ((a | b | c | d) == 0) return 0;
                double wa = (1 - tx) * (1 - ty), wb = tx * (1 - ty), wc = (1 - tx) * ty, wd = tx * ty;
                return Pack(
                    Pixel.A(a) * wa + Pixel.A(b) * wb + Pixel.A(c) * wc + Pixel.A(d) * wd,
                    Pixel.R(a) * wa + Pixel.R(b) * wb + Pixel.R(c) * wc + Pixel.R(d) * wd,
                    Pixel.G(a) * wa + Pixel.G(b) * wb + Pixel.G(c) * wc + Pixel.G(d) * wd,
                    Pixel.B(a) * wa + Pixel.B(b) * wb + Pixel.B(c) * wc + Pixel.B(d) * wd);
            }
            default:
            {
                double fx = u - 0.5, fy = v - 0.5;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                if (x0 < -2 || y0 < -2 || x0 > src.Width + 1 || y0 > src.Height + 1) return 0;
                double tx = fx - x0, ty = fy - y0;
                Span<double> wx = stackalloc double[4];
                Span<double> wy = stackalloc double[4];
                CatmullRom(tx, wx);
                CatmullRom(ty, wy);
                double A = 0, R = 0, G = 0, B = 0;
                for (int j = 0; j < 4; j++)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        uint p = src.GetOrTransparent(x0 - 1 + i, y0 - 1 + j);
                        if (p == 0) continue;
                        double k = wx[i] * wy[j];
                        A += Pixel.A(p) * k; R += Pixel.R(p) * k; G += Pixel.G(p) * k; B += Pixel.B(p) * k;
                    }
                }
                return Pack(A, R, G, B);
            }
        }
    }

    private static void CatmullRom(double t, Span<double> w)
    {
        double t2 = t * t, t3 = t2 * t;
        w[0] = 0.5 * (-t3 + 2 * t2 - t);
        w[1] = 0.5 * (3 * t3 - 5 * t2 + 2);
        w[2] = 0.5 * (-3 * t3 + 4 * t2 + t);
        w[3] = 0.5 * (t3 - t2);
    }

    private static uint Pack(double a, double r, double g, double b)
    {
        int ia = Math.Clamp((int)(a + 0.5), 0, 255);
        if (ia == 0) return 0;
        return Pixel.Pack((byte)ia, (byte)Math.Clamp((int)(r + 0.5), 0, ia), (byte)Math.Clamp((int)(g + 0.5), 0, ia), (byte)Math.Clamp((int)(b + 0.5), 0, ia));
    }

    /// <summary>Transforms a selection mask the same way as pixels.</summary>
    public static byte[] RenderMask(byte[] mask, int width, int height, Affine m, bool smooth)
    {
        var result = new byte[width * height];
        var inv = m.Invert();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (u, v) = inv.Apply(x + 0.5, y + 0.5);
                if (!smooth)
                {
                    int sx = (int)Math.Floor(u), sy = (int)Math.Floor(v);
                    result[y * width + x] = (uint)sx < (uint)width && (uint)sy < (uint)height ? mask[sy * width + sx] : (byte)0;
                    continue;
                }
                double fx = u - 0.5, fy = v - 0.5;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                double tx = fx - x0, ty = fy - y0;
                double At(int xx, int yy) => (uint)xx < (uint)width && (uint)yy < (uint)height ? mask[yy * width + xx] : 0;
                double val = At(x0, y0) * (1 - tx) * (1 - ty) + At(x0 + 1, y0) * tx * (1 - ty) + At(x0, y0 + 1) * (1 - tx) * ty + At(x0 + 1, y0 + 1) * tx * ty;
                result[y * width + x] = (byte)Math.Clamp((int)(val + 0.5), 0, 255);
            }
        return result;
    }

    /// <summary>Resizes an image. Shrinking averages all covered pixels (no aliasing); enlarging interpolates.</summary>
    public static RasterImage Resize(RasterImage src, int width, int height, Interpolation mode)
    {
        if (width == src.Width && height == src.Height) return src.Clone();
        if (mode == Interpolation.Nearest || width > src.Width || height > src.Height)
        {
            var m = Affine.Scale((double)width / src.Width, (double)height / src.Height);
            return Render(src, m, new PixelRect(0, 0, width, height), mode);
        }
        // Shrinking: average all covered pixels, separably (horizontally, then vertically).
        var tmp = AreaResample(src.Pixels, src.Width, src.Height, width, horizontal: true);
        return RasterImage.FromPixels(width, height, AreaResample(tmp, width, src.Height, height, horizontal: false));
    }

    private static uint[] AreaResample(uint[] src, int w, int h, int newSize, bool horizontal)
    {
        int oldSize = horizontal ? w : h;
        int outW = horizontal ? newSize : w, outH = horizontal ? h : newSize;
        var dst = new uint[outW * outH];
        double ratio = (double)oldSize / newSize;
        int lines = horizontal ? h : w;
        var acc = new double[4];
        for (int line = 0; line < lines; line++)
        {
            for (int i = 0; i < newSize; i++)
            {
                double start = i * ratio, end = start + ratio;
                Array.Clear(acc);
                double total = 0;
                for (int s = (int)Math.Floor(start); s < Math.Min(oldSize, (int)Math.Ceiling(end)); s++)
                {
                    double weight = Math.Min(end, s + 1) - Math.Max(start, s);
                    if (weight <= 0) continue;
                    uint p = horizontal ? src[line * w + s] : src[s * w + line];
                    acc[0] += Pixel.A(p) * weight; acc[1] += Pixel.R(p) * weight; acc[2] += Pixel.G(p) * weight; acc[3] += Pixel.B(p) * weight;
                    total += weight;
                }
                uint v = total > 0 ? Pack(acc[0] / total, acc[1] / total, acc[2] / total, acc[3] / total) : 0;
                if (horizontal) dst[line * outW + i] = v;
                else dst[i * outW + line] = v;
            }
        }
        return dst;
    }
}
