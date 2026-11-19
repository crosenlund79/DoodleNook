using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// A DoodleNook picture: one editable raster layer over an optional solid background.
/// The document owns the pixels and the undo history; views and input code only ask it to change.
/// (Mission 004 uses a single layer; the layer is kept separate from the background so more
/// layers can be added later without changing how strokes or undo work.)
/// </summary>
public sealed class Document
{
    /// <summary>Tile size used for undo snapshots and stroke bookkeeping.</summary>
    public const int TileSize = 64;

    private long _savedStateId;

    public Document(int width, int height, ColorRgb? background = null)
        : this(new RasterImage(width, height), background)
    {
    }

    public Document(RasterImage layer, ColorRgb? background = null)
    {
        Layer = layer;
        Background = background;
        _savedStateId = History.CurrentStateId;
    }

    public int Width => Layer.Width;
    public int Height => Layer.Height;
    public PixelRect Bounds => Layer.Bounds;

    /// <summary>Solid background shown and saved under the layer, or null for a transparent canvas.</summary>
    public ColorRgb? Background { get; }

    /// <summary>The paint layer (premultiplied BGRA). Only engine code should modify it.</summary>
    public RasterImage Layer { get; }

    public UndoHistory History { get; } = new();

    public bool IsDirty => History.CurrentStateId != _savedStateId;

    /// <summary>Raised whenever layer pixels change (strokes, undo, redo, clear).</summary>
    public event Action<PixelRect>? PixelsChanged;

    /// <summary>Raised when undo/redo availability or the unsaved-changes state changes.</summary>
    public event Action? HistoryChanged;

    public void MarkSaved()
    {
        _savedStateId = History.CurrentStateId;
        HistoryChanged?.Invoke();
    }

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    /// <summary>Undoes the last step. Returns the changed area (empty if there was nothing to undo).</summary>
    public PixelRect Undo()
    {
        var entry = History.TakeUndo();
        if (entry is null) return PixelRect.Empty;
        entry.SwapWith(Layer);
        NotifyPixelsChanged(entry.Bounds);
        HistoryChanged?.Invoke();
        return entry.Bounds;
    }

    public PixelRect Redo()
    {
        var entry = History.TakeRedo();
        if (entry is null) return PixelRect.Empty;
        entry.SwapWith(Layer);
        NotifyPixelsChanged(entry.Bounds);
        HistoryChanged?.Invoke();
        return entry.Bounds;
    }

    /// <summary>Makes the whole layer transparent, as one undoable step. Does nothing on an empty layer.</summary>
    public PixelRect Clear()
    {
        var patches = new List<TilePatch>();
        foreach (var tile in Tiles())
        {
            var pixels = Layer.CopyRegion(tile);
            if (Array.TrueForAll(pixels, p => p == Pixel.Transparent)) continue;
            patches.Add(new TilePatch(tile, pixels));
            Layer.WriteRegion(tile, new uint[tile.Area]);
        }
        if (patches.Count == 0) return PixelRect.Empty;
        Commit(History.Create("Clear canvas", patches));
        var changed = patches.Aggregate(PixelRect.Empty, (r, p) => r.Union(p.Rect));
        NotifyPixelsChanged(changed);
        return changed;
    }

    /// <summary>The visible colour at a pixel: the layer over the background (premultiplied).</summary>
    public uint CompositeAt(int x, int y)
    {
        if (!Bounds.Contains(x, y)) return Pixel.Transparent;
        uint p = Layer[x, y];
        return Background is { } bg ? Pixel.Over(p, Pixel.Opaque(bg)) : p;
    }

    /// <summary>A flattened copy of what the user sees (used for saving).</summary>
    public RasterImage Flatten()
    {
        var result = Layer.Clone();
        if (Background is { } bg)
        {
            uint b = Pixel.Opaque(bg);
            var px = result.Pixels;
            for (int i = 0; i < px.Length; i++) px[i] = Pixel.Over(px[i], b);
        }
        return result;
    }

    public int TilesX => (Width + TileSize - 1) / TileSize;
    public int TilesY => (Height + TileSize - 1) / TileSize;

    /// <summary>The pixel rectangle of tile (tx, ty), clipped to the document.</summary>
    public PixelRect TileRect(int tx, int ty) =>
        new PixelRect(tx * TileSize, ty * TileSize, TileSize, TileSize).Intersect(Bounds);

    private IEnumerable<PixelRect> Tiles()
    {
        for (int ty = 0; ty < TilesY; ty++)
            for (int tx = 0; tx < TilesX; tx++)
                yield return TileRect(tx, ty);
    }

    internal void Commit(HistoryEntry entry)
    {
        History.Push(entry);
        HistoryChanged?.Invoke();
    }

    internal void NotifyPixelsChanged(PixelRect area)
    {
        if (!area.IsEmpty) PixelsChanged?.Invoke(area);
    }
}
