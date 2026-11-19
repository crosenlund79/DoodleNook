using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>A rectangle of pixels saved for undo. Undo and redo simply swap it with the layer.</summary>
public sealed class TilePatch
{
    public TilePatch(PixelRect rect, uint[] pixels)
    {
        if (pixels.Length != rect.Area) throw new ArgumentException("Patch size mismatch.", nameof(pixels));
        Rect = rect;
        Pixels = pixels;
    }

    public PixelRect Rect { get; }

    /// <summary>Tightly packed premultiplied pixels for <see cref="Rect"/>.</summary>
    public uint[] Pixels { get; private set; }

    /// <summary>Swaps the stored pixels with the image's pixels in <see cref="Rect"/>.</summary>
    internal void SwapWith(RasterImage image)
    {
        var current = image.CopyRegion(Rect);
        image.WriteRegion(Rect, Pixels);
        Pixels = current;
    }
}

/// <summary>One undoable step, e.g. a whole brush stroke.</summary>
public sealed class HistoryEntry
{
    internal HistoryEntry(long id, string name, IReadOnlyList<TilePatch> patches)
    {
        Id = id;
        Name = name;
        Patches = patches;
        Bounds = patches.Aggregate(PixelRect.Empty, (r, p) => r.Union(p.Rect));
        ByteSize = patches.Sum(p => (long)p.Pixels.Length * sizeof(uint)) + 64;
    }

    public long Id { get; }
    public string Name { get; }
    public IReadOnlyList<TilePatch> Patches { get; }
    public PixelRect Bounds { get; }
    public long ByteSize { get; }

    internal void SwapWith(RasterImage image)
    {
        foreach (var patch in Patches) patch.SwapWith(image);
    }
}

/// <summary>
/// Undo/redo stacks. Each entry stores only the pixels a step changed (in tiles), and the
/// oldest entries are dropped when the history exceeds its count or memory budget.
/// </summary>
public sealed class UndoHistory
{
    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();
    private long _nextId = 1;
    private long _baseStateId; // the state at the bottom of the undo stack

    public int MaxEntries { get; set; } = 200;
    public long MaxBytes { get; set; } = 768L * 1024 * 1024;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public string? NextUndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? NextRedoName => _redo.Count > 0 ? _redo[^1].Name : null;
    public long TotalBytes => _undo.Sum(e => e.ByteSize) + _redo.Sum(e => e.ByteSize);

    /// <summary>Identifies the current document state; used to know whether there are unsaved changes.</summary>
    public long CurrentStateId => _undo.Count > 0 ? _undo[^1].Id : _baseStateId;

    internal HistoryEntry Create(string name, IReadOnlyList<TilePatch> patches) => new(_nextId++, name, patches);

    internal void Push(HistoryEntry entry)
    {
        _redo.Clear();
        _undo.Add(entry);
        Trim();
    }

    internal HistoryEntry? TakeUndo()
    {
        if (_undo.Count == 0) return null;
        var e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(e);
        return e;
    }

    internal HistoryEntry? TakeRedo()
    {
        if (_redo.Count == 0) return null;
        var e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(e);
        return e;
    }

    private void Trim()
    {
        long bytes = TotalBytes;
        while (_undo.Count > 1 && (_undo.Count > MaxEntries || bytes > MaxBytes))
        {
            bytes -= _undo[0].ByteSize;
            _baseStateId = _undo[0].Id;
            _undo.RemoveAt(0);
        }
    }
}
