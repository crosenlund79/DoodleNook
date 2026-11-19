using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

public enum ShapeKind
{
    Line,
    Rectangle,
    Ellipse,
}

/// <summary>Shape tool options. <see cref="AntiAlias"/> off gives pixel-exact shapes (pixel art).</summary>
public sealed record ShapeOptions(ShapeKind Kind = ShapeKind.Line, bool Filled = false, double Width = 4, double Opacity = 1, bool AntiAlias = true);

/// <summary>An interactive line, rectangle or ellipse: <see cref="Update"/> redraws it while dragging, <see cref="Commit"/> keeps it.</summary>
public sealed class ShapeOperation
{
    private readonly Document _doc;
    private readonly LayerEdit _edit;
    private readonly ColorRgb _color;
    private readonly ShapeOptions _o;
    private PixelRect _last = PixelRect.Empty;

    public ShapeOperation(Document doc, Layer layer, ColorRgb color, ShapeOptions options)
    {
        _doc = doc;
        _color = color;
        _o = options;
        _edit = doc.BeginEdit(layer, options.Kind switch { ShapeKind.Line => "Line", ShapeKind.Rectangle => "Rectangle", _ => "Ellipse" });
    }

    public void Update(double x0, double y0, double x1, double y1)
    {
        // Undo the previous preview, then draw the new shape.
        if (!_last.IsEmpty)
        {
            var px = _edit.Layer.Image.Pixels;
            for (int y = _last.Y; y < _last.Bottom; y++)
                for (int x = _last.X; x < _last.Right; x++)
                    px[y * _doc.Width + x] = _edit.BeforePixel(x, y);
        }
        var area = _o.AntiAlias ? DrawSmooth(x0, y0, x1, y1) : DrawPixels((int)Math.Floor(x0), (int)Math.Floor(y0), (int)Math.Floor(x1), (int)Math.Floor(y1));
        _edit.Changed(_last.Union(area));
        _last = area;
    }

    public bool Commit() => _edit.Commit();

    public void Cancel() => _edit.Cancel();

    private PixelRect DrawPixels(int x0, int y0, int x1, int y1)
    {
        var area = PixelRect.Empty;
        uint value = Pixel.Opaque(_color);
        var layer = _edit.Layer;
        void Plot(int x, int y)
        {
            if (!_doc.Bounds.Contains(x, y) || _doc.SelectionAt(x, y) < 128) return;
            _edit.Capture(new PixelRect(x, y, 1, 1));
            if (layer.AlphaLocked)
            {
                byte a = Pixel.A(_edit.BeforePixel(x, y));
                if (a == 0) return;
                layer.Image[x, y] = Pixel.FromStraight(_color.R, _color.G, _color.B, a);
            }
            else layer.Image[x, y] = value;
            area = area.Union(new PixelRect(x, y, 1, 1));
        }
        int n = Math.Max(1, (int)Math.Round(_o.Width));
        switch (_o.Kind)
        {
            case ShapeKind.Line:
                Rasterize.PixelLine(x0, y0, x1, y1, (x, y) =>
                {
                    for (int dy = 0; dy < n; dy++)
                        for (int dx = 0; dx < n; dx++)
                            Plot(x - (n - 1) / 2 + dx, y - (n - 1) / 2 + dy);
                });
                break;
            case ShapeKind.Rectangle:
                Rasterize.PixelRectangle(x0, y0, x1, y1, _o.Filled, Plot);
                break;
            default:
                Rasterize.PixelEllipse(x0, y0, x1, y1, _o.Filled, Plot);
                break;
        }
        return area;
    }

    private PixelRect DrawSmooth(double x0, double y0, double x1, double y1)
    {
        var polys = new List<IReadOnlyList<(double X, double Y)>>();
        double w = Math.Max(0.5, _o.Width), hw = w / 2;
        double l = Math.Min(x0, x1), t = Math.Min(y0, y1), r = Math.Max(x0, x1), b = Math.Max(y0, y1);
        switch (_o.Kind)
        {
            case ShapeKind.Line:
                polys.Add(Capsule(x0, y0, x1, y1, hw));
                break;
            case ShapeKind.Rectangle:
                if (_o.Filled) polys.Add(Rasterize.RectanglePoints(l, t, r, b));
                else
                {
                    polys.Add(Rasterize.RectanglePoints(l - hw, t - hw, r + hw, b + hw));
                    if (r - l > w && b - t > w) polys.Add(Rasterize.RectanglePoints(l + hw, t + hw, r - hw, b - hw));
                }
                break;
            default:
                if (_o.Filled) polys.Add(Rasterize.EllipsePoints(l, t, r, b));
                else
                {
                    polys.Add(Rasterize.EllipsePoints(l - hw, t - hw, r + hw, b + hw));
                    if (r - l > w && b - t > w) polys.Add(Rasterize.EllipsePoints(l + hw, t + hw, r - hw, b - hw));
                }
                break;
        }
        var cov = Rasterize.Polygon(polys, _doc.Bounds);
        var area = cov.Bounds;
        if (area.IsEmpty) return area;
        _edit.Capture(area);
        var layer = _edit.Layer;
        var px = layer.Image.Pixels;
        int op = (int)Math.Round(Math.Clamp(_o.Opacity, 0, 1) * 255);
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                int c = cov[x, y];
                if (c == 0) continue;
                int a = c * op / 255 * _doc.SelectionAt(x, y) / 255;
                if (a == 0) continue;
                int i = y * _doc.Width + x;
                uint baseline = px[i];
                if (layer.AlphaLocked)
                {
                    if (Pixel.A(baseline) == 0) continue;
                    var (br, bg, bb, ba) = Pixel.ToStraight(baseline);
                    px[i] = Pixel.FromStraight((byte)(br + (_color.R - br) * a / 255), (byte)(bg + (_color.G - bg) * a / 255), (byte)(bb + (_color.B - bb) * a / 255), ba);
                }
                else px[i] = Pixel.Over(Pixel.FromStraight(_color.R, _color.G, _color.B, (byte)a), baseline);
            }
        return area;
    }

    /// <summary>A thick line with round ends, as a polygon.</summary>
    internal static List<(double X, double Y)> Capsule(double x0, double y0, double x1, double y1, double radius)
    {
        double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
        double a = len < 1e-9 ? 0 : Math.Atan2(dy, dx);
        int n = Math.Clamp((int)(radius * 2), 8, 64);
        var pts = new List<(double, double)>(2 * n + 2);
        for (int i = 0; i <= n; i++)
        {
            double t = a - Math.PI / 2 + Math.PI * i / n;
            pts.Add((x1 + radius * Math.Cos(t), y1 + radius * Math.Sin(t)));
        }
        for (int i = 0; i <= n; i++)
        {
            double t = a + Math.PI / 2 + Math.PI * i / n;
            pts.Add((x0 + radius * Math.Cos(t), y0 + radius * Math.Sin(t)));
        }
        return pts;
    }
}
