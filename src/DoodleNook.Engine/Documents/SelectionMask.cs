using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>How a new selection combines with the existing one.</summary>
public enum SelectionMode
{
    Replace,
    Add,
    Subtract,
    Intersect,
}

/// <summary>
/// Which pixels are selected, 0 (not) to 255 (fully), with soft values on anti-aliased edges.
/// A document without a selection has <c>null</c> instead of a mask: everything is editable.
/// </summary>
public sealed class SelectionMask
{
    public SelectionMask(int width, int height)
    {
        Width = width;
        Height = height;
        Data = new byte[width * height];
    }

    private SelectionMask(int width, int height, byte[] data, PixelRect bounds)
    {
        Width = width;
        Height = height;
        Data = data;
        Bounds = bounds;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    /// <summary>Tight bounds of the selected pixels.</summary>
    public PixelRect Bounds { get; private set; }

    public bool IsEmpty => Bounds.IsEmpty;

    public byte this[int x, int y] => (uint)x < (uint)Width && (uint)y < (uint)Height ? Data[y * Width + x] : (byte)0;

    public SelectionMask Clone() => new(Width, Height, (byte[])Data.Clone(), Bounds);

    public long ByteSize => Data.Length + 64;

    public void UpdateBounds()
    {
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (int y = 0; y < Height; y++)
        {
            int row = y * Width, first = -1, last = -1;
            for (int x = 0; x < Width; x++)
            {
                if (Data[row + x] == 0) continue;
                if (first < 0) first = x;
                last = x;
            }
            if (first < 0) continue;
            l = Math.Min(l, first); r = Math.Max(r, last); t = Math.Min(t, y); b = y;
        }
        Bounds = r < 0 ? PixelRect.Empty : PixelRect.FromEdges(l, t, r + 1, b + 1);
    }

    public static SelectionMask All(int width, int height)
    {
        var m = new SelectionMask(width, height);
        Array.Fill(m.Data, (byte)255);
        m.Bounds = new PixelRect(0, 0, width, height);
        return m;
    }

    public static SelectionMask FromCoverage(int width, int height, CoverageMask coverage)
    {
        var m = new SelectionMask(width, height);
        var b = coverage.Bounds.Intersect(new PixelRect(0, 0, width, height));
        for (int y = b.Y; y < b.Bottom; y++)
            for (int x = b.X; x < b.Right; x++)
                m.Data[y * width + x] = coverage[x, y];
        m.UpdateBounds();
        return m;
    }

    public static SelectionMask Rectangle(int width, int height, PixelRect rect)
    {
        var m = new SelectionMask(width, height);
        rect = rect.Intersect(new PixelRect(0, 0, width, height));
        for (int y = rect.Y; y < rect.Bottom; y++) Array.Fill(m.Data, (byte)255, y * width + rect.X, rect.Width);
        m.Bounds = rect;
        return m;
    }

    /// <summary>Combines <paramref name="current"/> (may be null = nothing selected yet) with <paramref name="added"/>.</summary>
    public static SelectionMask? Combine(SelectionMask? current, SelectionMask added, SelectionMode mode)
    {
        if (mode == SelectionMode.Replace || current is null)
        {
            if (mode is SelectionMode.Subtract or SelectionMode.Intersect && current is null) return null;
            return added.IsEmpty ? null : added;
        }
        var result = current.Clone();
        var d = result.Data;
        var a = added.Data;
        for (int i = 0; i < d.Length; i++)
        {
            d[i] = mode switch
            {
                SelectionMode.Add => Math.Max(d[i], a[i]),
                SelectionMode.Subtract => (byte)(d[i] * (255 - a[i]) / 255),
                SelectionMode.Intersect => (byte)(d[i] * a[i] / 255),
                _ => a[i],
            };
        }
        result.UpdateBounds();
        return result.IsEmpty ? null : result;
    }

    public SelectionMask Inverted()
    {
        var m = new SelectionMask(Width, Height);
        for (int i = 0; i < Data.Length; i++) m.Data[i] = (byte)(255 - Data[i]);
        m.UpdateBounds();
        return m;
    }

    /// <summary>Boundary segments between selected (≥ 128) and unselected pixels, for drawing "marching ants".
    /// Each segment is axis-aligned on pixel edges, in document coordinates.</summary>
    public List<(int X0, int Y0, int X1, int Y1)> Outline()
    {
        var segs = new List<(int, int, int, int)>();
        var b = Bounds.Inflate(1).Intersect(new PixelRect(-1, -1, Width + 2, Height + 2));
        bool In(int x, int y) => this[x, y] >= 128;
        // Horizontal edges (between rows y-1 and y), merged into runs.
        for (int y = b.Y + 1; y <= b.Bottom; y++)
        {
            int start = -1;
            for (int x = b.X; x <= b.Right; x++)
            {
                bool edge = x < b.Right && In(x, y - 1) != In(x, y);
                if (edge && start < 0) start = x;
                else if (!edge && start >= 0) { segs.Add((start, y, x, y)); start = -1; }
            }
        }
        // Vertical edges (between columns x-1 and x).
        for (int x = b.X + 1; x <= b.Right; x++)
        {
            int start = -1;
            for (int y = b.Y; y <= b.Bottom; y++)
            {
                bool edge = y < b.Bottom && In(x - 1, y) != In(x, y);
                if (edge && start < 0) start = y;
                else if (!edge && start >= 0) { segs.Add((x, start, x, y)); start = -1; }
            }
        }
        return segs;
    }
}
