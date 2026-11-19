using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Painting;

/// <summary>How a pixel-art stroke paints: exact colours on whole pixels, never anti-aliased.</summary>
public readonly record struct PixelStrokeStyle(
    ColorRgb Color,
    bool Erase = false,
    int Size = 1,
    bool PixelPerfect = true,
    Symmetry Symmetry = Symmetry.None,
    bool Wrap = false,
    string Name = "Pixel pencil");

/// <summary>
/// The pixel pencil: joins input positions with Bresenham lines and sets whole pixels to the exact colour.
/// "Pixel-perfect" removes the doubled corner pixels freehand lines produce (L-shapes), like dedicated pixel editors.
/// With <see cref="PixelStrokeStyle.Wrap"/> the canvas wraps around (seamless textures).
/// </summary>
public sealed class PixelStroke
{
    private readonly Document _doc;
    private readonly List<(int X, int Y)> _path = new();
    private LayerEdit? _edit;
    private PixelStrokeStyle _style;
    private uint _value;
    private int _lastX, _lastY;

    public PixelStroke(Document document)
    {
        _doc = document;
    }

    public bool IsActive => _edit is not null;

    public bool Begin(PixelStrokeStyle style, int x, int y, Layer? layer = null)
    {
        if (IsActive) End();
        layer ??= _doc.ActiveLayer;
        if (layer.Locked) return false;
        _style = style with { Size = Math.Clamp(style.Size, 1, 64) };
        _value = style.Erase ? 0u : Pixel.Opaque(style.Color);
        _edit = _doc.BeginEdit(layer, style.Erase ? "Pixel eraser" : style.Name);
        _path.Clear();
        _lastX = x;
        _lastY = y;
        Visit(x, y);
        return true;
    }

    public void Add(int x, int y)
    {
        if (_edit is null || (x == _lastX && y == _lastY)) return;
        bool first = true;
        Rasterize.PixelLine(_lastX, _lastY, x, y, (px, py) =>
        {
            if (first) { first = false; return; } // the start point was already drawn
            Visit(px, py);
        });
        _lastX = x;
        _lastY = y;
    }

    public bool End()
    {
        if (_edit is null) return false;
        bool changed = _edit.Commit();
        _edit = null;
        return changed;
    }

    public void Cancel()
    {
        _edit?.Cancel();
        _edit = null;
    }

    private void Visit(int x, int y)
    {
        _path.Add((x, y));
        PlotWithMirrors(x, y);
        if (_style.PixelPerfect && _style.Size == 1 && _path.Count >= 3)
        {
            var a = _path[^3];
            var b = _path[^2];
            var c = _path[^1];
            bool corner = (a.X == b.X || a.Y == b.Y) && (b.X == c.X || b.Y == c.Y) && a.X != c.X && a.Y != c.Y;
            if (corner)
            {
                RestoreWithMirrors(b.X, b.Y);
                _path.RemoveAt(_path.Count - 2);
            }
        }
    }

    private IEnumerable<(int X, int Y)> Mirrors(int x, int y)
    {
        yield return (x, y);
        int mx = _doc.Width - 1 - x, my = _doc.Height - 1 - y;
        switch (_style.Symmetry)
        {
            case Symmetry.Vertical: yield return (mx, y); break;
            case Symmetry.Horizontal: yield return (x, my); break;
            case Symmetry.Both:
                yield return (mx, y);
                yield return (x, my);
                yield return (mx, my);
                break;
        }
    }

    private void PlotWithMirrors(int x, int y)
    {
        foreach (var (px, py) in Mirrors(x, y)) PlotBrush(px, py, restore: false);
    }

    private void RestoreWithMirrors(int x, int y)
    {
        foreach (var (px, py) in Mirrors(x, y)) PlotBrush(px, py, restore: true);
    }

    private void PlotBrush(int cx, int cy, bool restore)
    {
        int n = _style.Size, half = (n - 1) / 2;
        var changed = PixelRect.Empty;
        for (int dy = 0; dy < n; dy++)
            for (int dx = 0; dx < n; dx++)
            {
                int x = cx - half + dx, y = cy - half + dy;
                if (_style.Wrap)
                {
                    x = ((x % _doc.Width) + _doc.Width) % _doc.Width;
                    y = ((y % _doc.Height) + _doc.Height) % _doc.Height;
                }
                else if (!_doc.Bounds.Contains(x, y)) continue;
                if (_doc.SelectionAt(x, y) < 128) continue;
                var rect = new PixelRect(x, y, 1, 1);
                _edit!.Capture(rect);
                var image = _edit.Layer.Image;
                if (restore) _edit.RestorePixel(x, y);
                else if (_edit.Layer.AlphaLocked)
                {
                    uint baseline = image[x, y];
                    byte a = Pixel.A(baseline);
                    if (a == 0 || _style.Erase) continue;
                    image[x, y] = Pixel.FromStraight(_style.Color.R, _style.Color.G, _style.Color.B, a);
                }
                else image[x, y] = _value;
                changed = changed.Union(rect);
            }
        if (!changed.IsEmpty) _edit!.Changed(changed);
    }
}
