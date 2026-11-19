using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>Changed tiles of one layer (strokes, fills, transforms, adjustments…).</summary>
public sealed class LayerPixelsAction : IHistoryAction
{
    private readonly int _layerId;
    private readonly IReadOnlyList<TilePatch> _patches;

    public LayerPixelsAction(string name, int layerId, IReadOnlyList<TilePatch> patches)
    {
        Name = name;
        _layerId = layerId;
        _patches = patches;
        Bounds = patches.Aggregate(PixelRect.Empty, (r, p) => r.Union(p.Rect));
        ByteSize = patches.Sum(p => (long)p.Pixels.Length * sizeof(uint)) + 64;
    }

    public string Name { get; }
    public long ByteSize { get; }
    public PixelRect Bounds { get; }
    public int LayerId => _layerId;

    public void Undo(Document d) => Swap(d);
    public void Redo(Document d) => Swap(d);

    private void Swap(Document d)
    {
        var layer = d.FindLayer(_layerId) ?? throw new InvalidOperationException("Layer missing for undo.");
        foreach (var p in _patches) p.SwapWith(layer.Image);
        layer.Touch(Bounds);
        d.OnLayerPixelsChanged(layer, Bounds);
    }
}

/// <summary>A layer was added (Undo removes it, Redo puts it back at the same place).</summary>
public sealed class AddLayerAction(string name, Layer layer, int index) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize { get; } = (long)layer.Image.Pixels.Length * 4 + 64;
    public void Undo(Document d) => d.RemoveLayerInternal(layer);
    public void Redo(Document d) => d.InsertLayerInternal(layer, index, makeActive: true);
}

/// <summary>A layer was deleted.</summary>
public sealed class RemoveLayerAction(string name, Layer layer, int index) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize { get; } = (long)layer.Image.Pixels.Length * 4 + 64;
    public void Undo(Document d) => d.InsertLayerInternal(layer, index, makeActive: true);
    public void Redo(Document d) => d.RemoveLayerInternal(layer);
}

/// <summary>A layer moved within the stack.</summary>
public sealed class MoveLayerAction(int layerId, int from, int to) : IHistoryAction
{
    public string Name => "Move layer";
    public long ByteSize => 64;
    public void Undo(Document d) => d.MoveLayerInternal(layerId, from);
    public void Redo(Document d) => d.MoveLayerInternal(layerId, to);
}

/// <summary>Layer name, visibility, opacity, blend mode, locks, clipping or label changed.</summary>
public sealed class LayerPropertiesAction(string name, int layerId, LayerProperties before, LayerProperties after) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize => 128;
    public int LayerId => layerId;
    public LayerProperties After { get; internal set; } = after;
    public void Undo(Document d) => d.SetLayerPropertiesInternal(layerId, before);
    public void Redo(Document d) => d.SetLayerPropertiesInternal(layerId, After);
}

/// <summary>The selection changed.</summary>
public sealed class SelectionAction(string name, SelectionMask? before, SelectionMask? after) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize { get; } = (before?.ByteSize ?? 0) + (after?.ByteSize ?? 0) + 64;
    public void Undo(Document d) => d.SetSelectionInternal(before);
    public void Redo(Document d) => d.SetSelectionInternal(after);
}

/// <summary>Paper colour or visibility changed.</summary>
public sealed class PaperAction(ColorRgb? beforeColor, bool beforeVisible, ColorRgb? afterColor, bool afterVisible) : IHistoryAction
{
    public string Name => "Paper";
    public long ByteSize => 64;
    public void Undo(Document d) => d.SetPaperInternal(beforeColor, beforeVisible);
    public void Redo(Document d) => d.SetPaperInternal(afterColor, afterVisible);
}

/// <summary>The whole document changed shape (resize, crop, canvas size, rotate, flip).</summary>
public sealed class DocumentStateAction : IHistoryAction
{
    private readonly DocumentState _before;
    private readonly DocumentState _after;

    public DocumentStateAction(string name, DocumentState before, DocumentState after)
    {
        Name = name;
        _before = before;
        _after = after;
        ByteSize = before.ByteSize + after.ByteSize;
    }

    public string Name { get; }
    public long ByteSize { get; }
    public void Undo(Document d) => d.ApplyStateInternal(_before);
    public void Redo(Document d) => d.ApplyStateInternal(_after);
}

/// <summary>Size, layer pixels and selection of a document at one moment.</summary>
public sealed class DocumentState
{
    public DocumentState(int width, int height, IReadOnlyDictionary<int, RasterImage> images, SelectionMask? selection)
    {
        Width = width;
        Height = height;
        Images = images;
        Selection = selection;
    }

    public int Width { get; }
    public int Height { get; }
    public IReadOnlyDictionary<int, RasterImage> Images { get; }
    public SelectionMask? Selection { get; }
    public long ByteSize => Images.Values.Sum(i => (long)i.Pixels.Length * 4) + (Selection?.ByteSize ?? 0) + 64;
}

/// <summary>Several actions recorded as one step (e.g. merge down, paste, transform).</summary>
public sealed class CompositeAction(string name, IReadOnlyList<IHistoryAction> actions) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize { get; } = actions.Sum(a => a.ByteSize) + 64;
    public IReadOnlyList<IHistoryAction> Actions => actions;

    public void Undo(Document d)
    {
        for (int i = actions.Count - 1; i >= 0; i--) actions[i].Undo(d);
    }

    public void Redo(Document d)
    {
        foreach (var a in actions) a.Redo(d);
    }
}
