namespace DoodleNook.Engine.Imaging;

/// <summary>An integer rectangle in document pixels. Right and bottom are exclusive.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static readonly PixelRect Empty = new(0, 0, 0, 0);

    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        right <= left || bottom <= top ? Empty : new PixelRect(left, top, right - left, bottom - top);

    /// <summary>The smallest pixel rectangle that fully contains the given floating-point bounds.</summary>
    public static PixelRect FromBounds(double left, double top, double right, double bottom) =>
        FromEdges((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right), (int)Math.Ceiling(bottom));

    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromEdges(Math.Min(X, other.X), Math.Min(Y, other.Y), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    public PixelRect Intersect(PixelRect other)
    {
        if (IsEmpty || other.IsEmpty) return Empty;
        return FromEdges(Math.Max(X, other.X), Math.Max(Y, other.Y), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
    }

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public override string ToString() => IsEmpty ? "(empty)" : $"({X},{Y} {Width}×{Height})";
}
