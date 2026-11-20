using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

public enum GradientShape
{
    Linear,
    Radial,
}

/// <summary>From the start colour to the end colour, or to transparent.</summary>
public sealed record GradientOptions(GradientShape Shape = GradientShape.Linear, bool ToTransparent = false, double Opacity = 1);

/// <summary>
/// An interactive gradient: <see cref="Update"/> redraws it for new start/end points (live preview),
/// <see cref="Commit"/> records it as one undo step. Works inside the selection, lightly dithered against banding.
/// </summary>
public sealed class GradientOperation
{
    private static readonly int[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
    private readonly Document _doc;
    private readonly LayerEdit _edit;
    private readonly GradientOptions _options;
    private readonly ColorRgb _from, _to;
    private readonly PixelRect _area;

    public GradientOperation(Document doc, Layer layer, ColorRgb from, ColorRgb to, GradientOptions options)
    {
        _doc = doc;
        _options = options;
        _from = from;
        _to = to;
        _edit = doc.BeginEdit(layer, "Gradient");
        _area = doc.Selection?.Bounds ?? doc.Bounds;
        _edit.Capture(_area);
    }

    public void Update(double x0, double y0, double x1, double y1)
    {
        var px = _edit.Target.Pixels;
        int w = _doc.Width;
        double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy, len = Math.Sqrt(len2);
        int op = (int)Math.Round(Math.Clamp(_options.Opacity, 0, 1) * 255);
        bool alphaLock = _edit.AlphaLocked;
        for (int y = _area.Y; y < _area.Bottom; y++)
        {
            for (int x = _area.X; x < _area.Right; x++)
            {
                int i = y * w + x;
                uint baseline = _edit.BeforePixel(x, y);
                int s = _doc.SelectionAt(x, y);
                if (s == 0) { px[i] = baseline; continue; }
                double t;
                double cx = x + 0.5 - x0, cy = y + 0.5 - y0;
                if (len2 < 1e-9) t = 1;
                else if (_options.Shape == GradientShape.Linear) t = (cx * dx + cy * dy) / len2;
                else t = Math.Sqrt(cx * cx + cy * cy) / len;
                t = Math.Clamp(t, 0, 1);
                double dither = (Bayer[(y & 3) * 4 + (x & 3)] + 0.5) / 16.0 - 0.5;
                byte r, g, b, a;
                if (_options.ToTransparent)
                {
                    r = _from.R; g = _from.G; b = _from.B;
                    a = ToByte((1 - t) * 255 + dither);
                }
                else
                {
                    r = ToByte(_from.R + (_to.R - _from.R) * t + dither);
                    g = ToByte(_from.G + (_to.G - _from.G) * t + dither);
                    b = ToByte(_from.B + (_to.B - _from.B) * t + dither);
                    a = 255;
                }
                int alpha = a * op / 255 * s / 255;
                if (alphaLock)
                {
                    byte ba = Pixel.A(baseline);
                    if (ba == 0) { px[i] = baseline; continue; }
                    var (br, bg, bb, _) = Pixel.ToStraight(baseline);
                    px[i] = Pixel.FromStraight((byte)(br + (r - br) * alpha / 255), (byte)(bg + (g - bg) * alpha / 255), (byte)(bb + (b - bb) * alpha / 255), ba);
                }
                else px[i] = Pixel.Over(Pixel.FromStraight(r, g, b, (byte)alpha), baseline);
            }
        }
        _edit.Changed(_area);
    }

    public bool Commit() => _edit.Commit();

    public void Cancel() => _edit.Cancel();

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
}
