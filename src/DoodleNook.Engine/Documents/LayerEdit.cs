using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// An undoable pixel edit of one layer. Before changing pixels, call <see cref="Capture"/> for the area (tiles are
/// copied once, on first touch). <see cref="Commit"/> stores only the tiles that really changed as one undo step;
/// <see cref="Cancel"/> puts everything back.
/// </summary>
public sealed class LayerEdit
{
    private const int T = Document.TileSize;
    private readonly Dictionary<int, uint[]> _before = new();
    private PixelRect _changed = PixelRect.Empty;
    private bool _finished;

    internal LayerEdit(Document document, Layer layer, string name, bool mask = false)
    {
        Document = document;
        Layer = layer;
        Name = name;
        IsMask = mask && layer.Mask is not null;
        Target = IsMask ? layer.Mask! : layer.Image;
        Cel = IsMask ? null : layer.CurrentCel;
    }

    /// <summary>True when this edit paints on the layer's mask (white shows, black hides) rather than its pixels.</summary>
    public bool IsMask { get; }

    /// <summary>The pixels being edited: the layer's image, or its mask.</summary>
    public RasterImage Target { get; }

    /// <summary>Transparency lock applies to a layer's pixels, never to its mask.</summary>
    public bool AlphaLocked => !IsMask && Layer.AlphaLocked;

    /// <summary>The drawing being edited in an animation (null for an empty cell or a still picture).</summary>
    public Cel? Cel { get; private set; }

    public Document Document { get; }
    public Layer Layer { get; }
    public string Name { get; set; }
    public bool IsFinished => _finished;

    /// <summary>The area changed so far.</summary>
    public PixelRect ChangedArea => _changed;

    public int TileKey(int tx, int ty) => ty * Document.TilesX + tx;

    /// <summary>Saves the original pixels of every tile overlapping <paramref name="area"/> (once).</summary>
    public void Capture(PixelRect area)
    {
        area = area.Intersect(Document.Bounds);
        if (area.IsEmpty) return;
        int tx0 = area.X / T, ty0 = area.Y / T, tx1 = (area.Right - 1) / T, ty1 = (area.Bottom - 1) / T;
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
                BeforeTile(tx, ty);
    }

    public void CaptureAll() => Capture(Document.Bounds);

    /// <summary>The original pixels of tile (tx, ty), stride <see cref="Document.TileSize"/>; captured if needed.</summary>
    public uint[] BeforeTile(int tx, int ty)
    {
        int key = TileKey(tx, ty);
        if (_before.TryGetValue(key, out var tile)) return tile;
        tile = new uint[T * T];
        var rect = Document.TileRect(tx, ty);
        var src = Target.Pixels;
        for (int y = 0; y < rect.Height; y++)
            Array.Copy(src, (rect.Y + y) * Document.Width + rect.X, tile, y * T, rect.Width);
        _before[key] = tile;
        return tile;
    }

    /// <summary>The original value of a pixel (before this edit).</summary>
    public uint BeforePixel(int x, int y)
    {
        if (!Document.Bounds.Contains(x, y)) return 0;
        int tx = x / T, ty = y / T;
        return _before.TryGetValue(TileKey(tx, ty), out var tile) ? tile[(y - ty * T) * T + (x - tx * T)] : Target[x, y];
    }

    /// <summary>Restores the original value of one pixel (if it was captured).</summary>
    public void RestorePixel(int x, int y)
    {
        Target[x, y] = BeforePixel(x, y);
    }

    /// <summary>Tells the document that pixels in <paramref name="area"/> changed (updates the screen).</summary>
    public void Changed(PixelRect area)
    {
        area = area.Intersect(Document.Bounds);
        if (area.IsEmpty) return;
        _changed = _changed.Union(area);
        if (IsMask)
        {
            // A mask is not artwork: no palette, no content bounds; the layer just shows differently.
            Layer.Revision++;
            Document.OnLayerPixelsChanged(Layer, area);
            return;
        }
        // A palette-locked picture keeps only palette colours (same alpha), within this same edit and undo step.
        Document.ConformToPalette(Layer.Image, area);
        Layer.Touch(area);
        Document.OnLayerPixelsChanged(Layer, area);
    }

    /// <summary>Puts every captured tile back (used for live previews before redrawing, and by Cancel).</summary>
    public void RestoreAll()
    {
        var area = PixelRect.Empty;
        foreach (var (key, tile) in _before)
        {
            var rect = Document.TileRect(key % Document.TilesX, key / Document.TilesX);
            WriteTile(rect, tile);
            area = area.Union(rect);
        }
        if (!area.IsEmpty)
        {
            if (IsMask) Layer.Revision++;
            else Layer.Touch(area);
            Document.OnLayerPixelsChanged(Layer, area);
        }
    }

    public void Cancel()
    {
        if (_finished) return;
        RestoreAll();
        _finished = true;
    }

    /// <summary>The undo action for the tiles that differ from their originals (null when nothing changed).</summary>
    public IHistoryAction? BuildAction()
    {
        var patches = new List<TilePatch>();
        var layer = Target.Pixels;
        int w = Document.Width;
        foreach (var (key, before) in _before.OrderBy(kv => kv.Key))
        {
            var rect = Document.TileRect(key % Document.TilesX, key / Document.TilesX);
            bool changed = false;
            for (int y = 0; y < rect.Height && !changed; y++)
            {
                int row = (rect.Y + y) * w + rect.X;
                for (int x = 0; x < rect.Width; x++)
                    if (layer[row + x] != before[y * T + x]) { changed = true; break; }
            }
            if (!changed) continue;
            var packed = new uint[rect.Area];
            for (int y = 0; y < rect.Height; y++) Array.Copy(before, y * T, packed, y * rect.Width, rect.Width);
            patches.Add(new TilePatch(rect, packed));
        }
        _finished = true;
        if (patches.Count == 0) return null;
        if (IsMask) return new LayerPixelsAction(Name, Layer.Id, patches, mask: true);
        if (Document.IsAnimated && Cel is null && Layer.Cells is not null)
        {
            // Drawing on an empty cell: the blank becomes a new drawing in this frame, as part of the same step.
            Cel = Document.AdoptBlank(Layer);
            return new CompositeAction(Name, [new AdoptCelAction(Layer.Id, Document.CurrentFrame, Cel), new LayerPixelsAction(Name, Layer.Id, patches, Cel)]);
        }
        return new LayerPixelsAction(Name, Layer.Id, patches, Cel);
    }

    /// <summary>Records the edit as one undo step. Returns false when no pixel changed (nothing is recorded).</summary>
    public bool Commit()
    {
        if (_finished) return false;
        var action = BuildAction();
        if (action is null) return false;
        Document.Commit(action);
        return true;
    }

    private void WriteTile(PixelRect rect, uint[] tile)
    {
        var dst = Target.Pixels;
        for (int y = 0; y < rect.Height; y++)
            Array.Copy(tile, y * T, dst, (rect.Y + y) * Document.Width + rect.X, rect.Width);
    }
}
