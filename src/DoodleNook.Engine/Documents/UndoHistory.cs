using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>One undoable change. Actions must be able to undo and redo any number of times, in order.</summary>
public interface IHistoryAction
{
    string Name { get; }
    long ByteSize { get; }
    void Undo(Document document);
    void Redo(Document document);
}

/// <summary>A rectangle of pixels saved for undo. Undo and redo simply swap it with the image.</summary>
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

    internal void SwapWith(RasterImage image)
    {
        var current = image.CopyRegion(Rect);
        image.WriteRegion(Rect, Pixels);
        Pixels = current;
    }
}

/// <summary>One entry in the history list.</summary>
public sealed class HistoryEntry
{
    internal HistoryEntry(long id, IHistoryAction action, int? frame = null)
    {
        Id = id;
        Action = action;
        Frame = frame;
    }

    public long Id { get; }

    /// <summary>The animation frame shown when the change was made (null for still pictures).</summary>
    public int? Frame { get; }
    public IHistoryAction Action { get; }
    public string Name => Action.Name;
    public long ByteSize => Action.ByteSize;
}

/// <summary>
/// Undo/redo stacks of <see cref="IHistoryAction"/>s. Pixel actions store only the tiles that changed, and the
/// oldest entries are dropped when the history exceeds its count or memory budget.
/// </summary>
public sealed class UndoHistory
{
    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();
    private long _nextId = 1;
    private long _baseStateId;

    public int MaxEntries { get; set; } = 200;
    public long MaxBytes { get; set; } = 1024L * 1024 * 1024;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public string? NextUndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? NextRedoName => _redo.Count > 0 ? _redo[^1].Name : null;
    public long TotalBytes => _undo.Sum(e => e.ByteSize) + _redo.Sum(e => e.ByteSize);

    /// <summary>Oldest first: the steps that can be undone.</summary>
    public IReadOnlyList<HistoryEntry> UndoEntries => _undo;

    /// <summary>Most recently undone first: the steps that can be redone.</summary>
    public IEnumerable<HistoryEntry> RedoEntries => Enumerable.Reverse(_redo);

    /// <summary>Identifies the current document state; used to know whether there are unsaved changes.</summary>
    public long CurrentStateId => _undo.Count > 0 ? _undo[^1].Id : _baseStateId;

    internal void Push(IHistoryAction action, int? frame = null)
    {
        _redo.Clear();
        _undo.Add(new HistoryEntry(_nextId++, action, frame));
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

    /// <summary>Forgets everything (e.g. after loading a file).</summary>
    internal void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _baseStateId = _nextId++;
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
