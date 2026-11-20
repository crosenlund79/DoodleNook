using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// A DoodleNook picture: a stack of raster layers over an optional paper colour, an optional selection and the
/// undo history. The document owns all pixels; workspaces, panels and tools only ask it to change.
/// It keeps a composited "projection" up to date incrementally, which is what the screen shows.
/// </summary>
public sealed partial class Document
{
    /// <summary>Tile size used for undo snapshots and stroke bookkeeping.</summary>
    public const int TileSize = 64;

    private readonly List<Layer> _layers = new();
    private readonly Dictionary<int, RasterImage> _overrides = new();
    private int _nextLayerId = 1;
    private long _savedStateId;
    private Layer _active = null!;
    private FloatingPixels? _floating;

    /// <summary>Creates a picture with one empty layer.</summary>
    public Document(int width, int height, ColorRgb? paper = null, string firstLayerName = "Layer 1")
    {
        RasterImage.Validate(width, height);
        Width = width;
        Height = height;
        Paper = paper;
        Projection = new RasterImage(width, height);
        _active = NewLayer(new LayerProperties(firstLayerName), new RasterImage(width, height));
        _layers.Add(_active);
        RecompositeAll(notify: false);
        _savedStateId = History.CurrentStateId;
    }

    /// <summary>Creates a picture with the given layers (bottom first), e.g. when opening a file.</summary>
    public Document(int width, int height, ColorRgb? paper, bool paperVisible, IEnumerable<(LayerProperties Properties, RasterImage Image)> layers, int activeIndex = -1)
    {
        RasterImage.Validate(width, height);
        Width = width;
        Height = height;
        Paper = paper;
        PaperVisible = paperVisible;
        Projection = new RasterImage(width, height);
        foreach (var (props, image) in layers)
        {
            if (image.Width != width || image.Height != height) throw new ArgumentException("Layer size differs from the document.");
            _layers.Add(NewLayer(props, image));
        }
        if (_layers.Count == 0) _layers.Add(NewLayer(new LayerProperties("Layer 1"), new RasterImage(width, height)));
        _active = _layers[activeIndex >= 0 && activeIndex < _layers.Count ? activeIndex : _layers.Count - 1];
        RecompositeAll(notify: false);
        _savedStateId = History.CurrentStateId;
    }

    /// <summary>A one-layer document holding an image (e.g. an opened PNG). The image keeps its own transparency.</summary>
    public static Document FromImage(RasterImage image, string layerName = "Image") =>
        new(image.Width, image.Height, null, true, [(new LayerProperties(layerName), image)]);

    public int Width { get; private set; }
    public int Height { get; private set; }
    public PixelRect Bounds => new(0, 0, Width, Height);

    /// <summary>Solid paper under all layers (null = transparent canvas).</summary>
    public ColorRgb? Paper { get; private set; }

    /// <summary>The paper can be hidden to check transparency without losing its colour.</summary>
    public bool PaperVisible { get; private set; } = true;

    /// <summary>Layers from bottom to top.</summary>
    public IReadOnlyList<Layer> Layers => _layers;

    public Layer ActiveLayer => _active;
    public int ActiveIndex => _layers.IndexOf(_active);

    /// <summary>Current selection, or null when nothing is selected (then everything is editable).</summary>
    public SelectionMask? Selection { get; private set; }

    /// <summary>All visible layers composited over the paper (what the user sees).</summary>
    public RasterImage Projection { get; private set; }

    public UndoHistory History { get; } = new();

    /// <summary>The colour palette stored with the picture (used by Pixel Art).</summary>
    public Palette Palette { get; set; } = Palette.Default();

    public bool IsDirty => History.CurrentStateId != _savedStateId;

    /// <summary>Raised whenever part of the projection changed (the screen must refresh that area).</summary>
    public event Action<PixelRect>? ProjectionChanged;

    /// <summary>Raised when layers are added, removed, reordered or their properties change.</summary>
    public event Action? LayersChanged;

    /// <summary>Raised when a different layer becomes active.</summary>
    public event Action? ActiveLayerChanged;

    public event Action? SelectionChanged;

    /// <summary>Raised when the canvas size changed (crop, resize, rotate…).</summary>
    public event Action? SizeChanged;

    /// <summary>Raised when undo/redo availability or the unsaved-changes state changes.</summary>
    public event Action? HistoryChanged;

    public void MarkSaved()
    {
        _savedStateId = History.CurrentStateId;
        HistoryChanged?.Invoke();
    }

    /// <summary>Marks the state captured in a save snapshot as saved (later changes keep the picture unsaved).</summary>
    public void MarkSaved(long stateId)
    {
        _savedStateId = stateId;
        HistoryChanged?.Invoke();
    }

    /// <summary>Marks the picture as having unsaved changes (e.g. work restored after a crash).</summary>
    public void MarkUnsaved()
    {
        _savedStateId = -1;
        HistoryChanged?.Invoke();
    }

    public Layer? FindLayer(int id) => _layers.Find(l => l.Id == id);

    public int IndexOf(Layer layer) => _layers.IndexOf(layer);

    // ================================================================ layers

    private Layer NewLayer(LayerProperties props, RasterImage image) => new(_nextLayerId++, props, image);

    private string NextLayerName()
    {
        int n = _layers.Count + 1;
        while (_layers.Any(l => l.Name == $"Layer {n}")) n++;
        return $"Layer {n}";
    }

    /// <summary>Adds an empty layer above the active one (or at <paramref name="index"/>) and makes it active.</summary>
    public Layer AddLayer(string? name = null, int? index = null)
    {
        var layer = NewLayer(new LayerProperties(name ?? NextLayerName()), new RasterImage(Width, Height));
        PrepareNewLayerTrack(layer, hasPixels: false);
        int at = Math.Clamp(index ?? ActiveIndex + 1, 0, _layers.Count);
        InsertLayerInternal(layer, at, makeActive: true);
        Commit(new AddLayerAction("New layer", layer, at));
        return layer;
    }

    /// <summary>Adds a layer holding the given pixels (e.g. a paste or a placed image).</summary>
    public Layer AddLayer(RasterImage image, string name, int? index = null)
    {
        if (image.Width != Width || image.Height != Height) throw new ArgumentException("Layer size differs from the document.");
        var layer = NewLayer(new LayerProperties(name), image);
        PrepareNewLayerTrack(layer, hasPixels: true);
        int at = Math.Clamp(index ?? ActiveIndex + 1, 0, _layers.Count);
        InsertLayerInternal(layer, at, makeActive: true);
        Commit(new AddLayerAction(name, layer, at));
        return layer;
    }

    public Layer DuplicateLayer(Layer source)
    {
        var copy = NewLayer(source.Properties with { Name = source.Name + " copy" }, _animation is null ? source.Image.Clone() : new RasterImage(1, 1));
        if (_animation is not null) CopyTrack(source, copy);
        int at = IndexOf(source) + 1;
        InsertLayerInternal(copy, at, makeActive: true);
        Commit(new AddLayerAction("Duplicate layer", copy, at));
        return copy;
    }

    /// <summary>Deletes a layer. The last remaining layer cannot be deleted.</summary>
    public bool DeleteLayer(Layer layer)
    {
        if (_layers.Count <= 1 || !_layers.Contains(layer)) return false;
        int index = IndexOf(layer);
        RemoveLayerInternal(layer);
        Commit(new RemoveLayerAction("Delete layer", layer, index));
        return true;
    }

    public bool MoveLayer(Layer layer, int newIndex)
    {
        int from = IndexOf(layer);
        newIndex = Math.Clamp(newIndex, 0, _layers.Count - 1);
        if (from < 0 || from == newIndex) return false;
        MoveLayerInternal(layer.Id, newIndex);
        Commit(new MoveLayerAction(layer.Id, from, newIndex));
        return true;
    }

    /// <summary>Merges a layer into the one below, respecting its opacity, blend mode and clipping.</summary>
    public bool MergeDown(Layer layer)
    {
        int index = IndexOf(layer);
        if (index <= 0) return false;
        if (_animation is not null) return MergeDownAnimated(layer, index);
        var below = _layers[index - 1];
        if (below.Locked) return false;
        var edit = BeginEdit(below, "Merge down");
        edit.CaptureAll();
        var area = layer.ContentBounds;
        if (layer.Visible && !area.IsEmpty)
        {
            int factor = (int)Math.Round(layer.Opacity * 255);
            var src = layer.Image.Pixels;
            var dst = below.Image.Pixels;
            var clipBase = layer.ClipToBelow ? below : null;
            for (int y = area.Y; y < area.Bottom; y++)
            {
                int row = y * Width;
                for (int x = area.X; x < area.Right; x++)
                {
                    uint s = Blending.Fade(src[row + x], factor);
                    if (clipBase is not null) s = Blending.Fade(s, Pixel.A(dst[row + x]));
                    if (s == 0) continue;
                    uint d = dst[row + x];
                    uint result = Blending.Blend(s, d, layer.Blend);
                    // The lower layer keeps its own transparency where it was empty for non-normal modes.
                    dst[row + x] = layer.Blend == BlendMode.Normal || d != 0 ? result : s;
                }
            }
            edit.Changed(area);
        }
        var pixels = edit.BuildAction();
        RemoveLayerInternal(layer);
        var actions = new List<IHistoryAction>();
        if (pixels is not null) actions.Add(pixels);
        actions.Add(new RemoveLayerAction("Merge down", layer, index));
        SetActiveLayer(below);
        Commit(new CompositeAction("Merge down", actions));
        return true;
    }

    public void SetActiveLayer(Layer layer)
    {
        if (!_layers.Contains(layer) || ReferenceEquals(layer, _active)) return;
        _active = layer;
        ActiveLayerChanged?.Invoke();
    }

    /// <summary>Changes layer properties as one undo step.</summary>
    public void SetLayerProperties(Layer layer, LayerProperties properties, string? historyName = null)
    {
        var before = layer.Properties;
        if (before == properties) return;
        SetLayerPropertiesInternal(layer.Id, properties);
        Commit(new LayerPropertiesAction(historyName ?? DescribeChange(before, properties), layer.Id, before, properties));
    }

    /// <summary>Changes layer properties without recording history (for live slider drags); call
    /// <see cref="SetLayerProperties"/> with the final value afterwards — pass the starting value as <paramref name="from"/>.</summary>
    public void PreviewLayerProperties(Layer layer, LayerProperties properties) => SetLayerPropertiesInternal(layer.Id, properties);

    /// <summary>Records a change that was previewed with <see cref="PreviewLayerProperties"/>.</summary>
    public void CommitLayerProperties(Layer layer, LayerProperties from, string? historyName = null)
    {
        var to = layer.Properties;
        if (from == to) return;
        Commit(new LayerPropertiesAction(historyName ?? DescribeChange(from, to), layer.Id, from, to));
    }

    private static string DescribeChange(LayerProperties a, LayerProperties b) =>
        a.Name != b.Name ? "Rename layer"
        : a.Visible != b.Visible ? (b.Visible ? "Show layer" : "Hide layer")
        : a.Opacity != b.Opacity ? "Layer opacity"
        : a.Blend != b.Blend ? "Blend mode"
        : a.Locked != b.Locked ? (b.Locked ? "Lock layer" : "Unlock layer")
        : a.AlphaLocked != b.AlphaLocked ? (b.AlphaLocked ? "Lock transparency" : "Unlock transparency")
        : a.ClipToBelow != b.ClipToBelow ? (b.ClipToBelow ? "Clip to layer below" : "Release clipping")
        : "Layer label";

    public void SetPaper(ColorRgb? color, bool visible)
    {
        if (color == Paper && visible == PaperVisible) return;
        var action = new PaperAction(Paper, PaperVisible, color, visible);
        SetPaperInternal(color, visible);
        Commit(action);
    }

    // ================================================================ selection

    public void SetSelection(SelectionMask? mask, string historyName)
    {
        if (mask is not null && (mask.Width != Width || mask.Height != Height)) throw new ArgumentException("Selection size differs from the document.");
        if (mask is { IsEmpty: true }) mask = null;
        if (mask is null && Selection is null) return;
        var before = Selection;
        SetSelectionInternal(mask);
        Commit(new SelectionAction(historyName, before, mask));
    }

    /// <summary>How strongly a pixel is selected (255 when there is no selection).</summary>
    public byte SelectionAt(int x, int y) => Selection is null ? (byte)255 : Selection[x, y];

    // ================================================================ edits and history

    /// <summary>Starts an undoable pixel edit of a layer. Throws if the layer is locked.</summary>
    public LayerEdit BeginEdit(Layer layer, string historyName)
    {
        if (layer.Locked) throw new InvalidOperationException($"Layer \"{layer.Name}\" is locked.");
        return new LayerEdit(this, layer, historyName);
    }

    public void Commit(IHistoryAction action)
    {
        History.Push(action, _animation?.CurrentFrame);
        HistoryChanged?.Invoke();
    }

    /// <summary>In an animation, undo and redo first show the frame where the change was made, so it is visible.</summary>
    private void GoToEntryFrame(HistoryEntry entry)
    {
        if (_animation is { } a && entry.Frame is int f && f < a.FrameCount && f != a.CurrentFrame) ShowFrameInternal(f);
    }

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    /// <summary>Undoes the last step. Returns false if there was nothing to undo.</summary>
    public bool Undo()
    {
        var entry = History.TakeUndo();
        if (entry is null) return false;
        GoToEntryFrame(entry);
        entry.Action.Undo(this);
        HistoryChanged?.Invoke();
        return true;
    }

    public bool Redo()
    {
        var entry = History.TakeRedo();
        if (entry is null) return false;
        GoToEntryFrame(entry);
        entry.Action.Redo(this);
        HistoryChanged?.Invoke();
        return true;
    }

    /// <summary>Makes the whole active layer (or the selected part) transparent, as one undo step.</summary>
    public bool Clear(Layer? layer = null)
    {
        layer ??= _active;
        if (layer.Locked) return false;
        var area = Selection is null ? layer.ContentBounds : layer.ContentBounds.Intersect(Selection.Bounds);
        if (area.IsEmpty) return false;
        var edit = BeginEdit(layer, Selection is null ? "Clear layer" : "Clear selection");
        edit.Capture(area);
        var px = layer.Image.Pixels;
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
            {
                byte s = SelectionAt(x, y);
                if (s == 0) continue;
                int i = y * Width + x;
                px[i] = layer.AlphaLocked ? px[i] : Blending.Fade(px[i], 255 - s);
            }
        edit.Changed(area);
        return edit.Commit();
    }

    // ================================================================ whole-document changes

    /// <summary>The current size, pixels and selection. Images are referenced, not copied: whole-picture operations
    /// replace images with new ones rather than changing them, and undo restores the same objects.</summary>
    public DocumentState CaptureState() => _animation is null
        ? new(Width, Height, _layers.ToDictionary(l => l.Id, l => l.Image), Selection)
        : new(Width, Height, new Dictionary<int, RasterImage>(), Selection, AllCels().ToDictionary(c => c, c => c.Image));

    /// <summary>Every distinct cel of every layer.</summary>
    public IEnumerable<Cel> AllCels() => _layers.Where(l => l.Cells is not null).SelectMany(l => l.Cells!).OfType<Cel>().Distinct();

    /// <summary>Builds the state after applying <paramref name="map"/> to every image of the picture (every layer, or every
    /// cel of an animation) — used by resize, crop, rotate and flip.</summary>
    public DocumentState MapAllImages(int width, int height, Func<RasterImage, RasterImage> map, SelectionMask? selection) =>
        _animation is null
            ? new(width, height, _layers.ToDictionary(l => l.Id, l => map(l.Image)), selection)
            : new(width, height, new Dictionary<int, RasterImage>(), selection, AllCels().ToDictionary(c => c, c => map(c.Image)));

    /// <summary>Replaces size, layer pixels and selection as one undo step (resize, crop, rotate…).</summary>
    public void ApplyState(DocumentState newState, string historyName)
    {
        var before = CaptureState();
        ApplyStateInternal(newState);
        Commit(new DocumentStateAction(historyName, before, newState));
    }

    // ================================================================ compositing

    /// <summary>The composited colour at a pixel (what the user sees, including the paper).</summary>
    public uint CompositeAt(int x, int y) => Projection.GetOrTransparent(x, y);

    /// <summary>A fresh composite of the visible layers (with the paper when it is visible).</summary>
    public RasterImage Flatten()
    {
        var image = new RasterImage(Width, Height);
        Compositor.Compose(this, Bounds, image, includeOverlays: false, includePaper: true);
        return image;
    }

    /// <summary>Replaces a layer's pixels in the projection with a preview image (adjustment previews); null removes it.</summary>
    public void SetLayerOverride(Layer layer, RasterImage? preview, PixelRect area)
    {
        if (preview is null) _overrides.Remove(layer.Id);
        else _overrides[layer.Id] = preview;
        Recomposite(area);
    }

    internal RasterImage ImageFor(Layer layer) => _overrides.TryGetValue(layer.Id, out var o) ? o : layer.Image;

    internal FloatingPixels? Floating => _floating;

    /// <summary>Shows pixels floating above a layer (move/transform preview). Null removes them.</summary>
    public void SetFloating(Layer layer, RasterImage? image, int x, int y)
    {
        var old = _floating?.Bounds ?? PixelRect.Empty;
        _floating = image is null ? null : new FloatingPixels(layer.Id, image, x, y);
        Recomposite(old.Union(_floating?.Bounds ?? PixelRect.Empty));
    }

    public void RecompositeAll(bool notify = true)
    {
        Compositor.Compose(this, Bounds, Projection, includeOverlays: true, includePaper: true);
        if (notify) ProjectionChanged?.Invoke(Bounds);
    }

    public void Recomposite(PixelRect area)
    {
        area = area.Intersect(Bounds);
        if (area.IsEmpty) return;
        Compositor.Compose(this, area, Projection, includeOverlays: true, includePaper: true);
        ProjectionChanged?.Invoke(area);
    }

    internal void OnLayerPixelsChanged(Layer layer, PixelRect area)
    {
        if (layer.Visible || _layers.Any(l => l.ClipToBelow)) Recomposite(area);
    }

    // ================================================================ internals used by history actions

    internal void InsertLayerInternal(Layer layer, int index, bool makeActive)
    {
        _layers.Insert(Math.Clamp(index, 0, _layers.Count), layer);
        if (_animation is not null && layer.Cells is not null) DisplayCell(layer);
        if (makeActive) _active = layer;
        LayersChanged?.Invoke();
        ActiveLayerChanged?.Invoke();
        if (!layer.ContentBounds.IsEmpty || layer.Visible) Recomposite(layer.ContentBounds.IsEmpty ? PixelRect.Empty : layer.ContentBounds);
    }

    internal void RemoveLayerInternal(Layer layer)
    {
        int index = _layers.IndexOf(layer);
        if (index < 0) return;
        _layers.RemoveAt(index);
        if (ReferenceEquals(_active, layer)) _active = _layers[Math.Clamp(index - 1, 0, _layers.Count - 1)];
        LayersChanged?.Invoke();
        ActiveLayerChanged?.Invoke();
        // Clipped layers above may change appearance too; recomposite everything the layer touched.
        RecompositeAll();
    }

    internal void MoveLayerInternal(int layerId, int to)
    {
        var layer = FindLayer(layerId);
        if (layer is null) return;
        _layers.Remove(layer);
        _layers.Insert(Math.Clamp(to, 0, _layers.Count), layer);
        LayersChanged?.Invoke();
        RecompositeAll();
    }

    internal void SetLayerPropertiesInternal(int layerId, LayerProperties properties)
    {
        var layer = FindLayer(layerId);
        if (layer is null) return;
        var before = layer.Properties;
        layer.Properties = properties;
        LayersChanged?.Invoke();
        bool visual = before.Visible != properties.Visible || before.Opacity != properties.Opacity
                      || before.Blend != properties.Blend || before.ClipToBelow != properties.ClipToBelow;
        if (visual) RecompositeAll();
    }

    internal void SetSelectionInternal(SelectionMask? mask)
    {
        Selection = mask;
        SelectionChanged?.Invoke();
    }

    internal void SetPaperInternal(ColorRgb? color, bool visible)
    {
        Paper = color;
        PaperVisible = visible;
        LayersChanged?.Invoke();
        RecompositeAll();
    }

    internal void ApplyStateInternal(DocumentState state)
    {
        bool resized = state.Width != Width || state.Height != Height;
        Width = state.Width;
        Height = state.Height;
        if (state.CelImages is { } cels)
        {
            foreach (var (cel, img) in cels)
            {
                cel.Image = img;
                cel.ContentBounds = img.ContentBounds();
                cel.Revision++;
            }
        }
        else
        {
            foreach (var layer in _layers)
            {
                if (state.Images.TryGetValue(layer.Id, out var img))
                {
                    layer.Image = img;
                    layer.RecalculateBounds();
                }
                else if (resized)
                {
                    layer.Image = new RasterImage(Width, Height);
                    layer.RecalculateBounds();
                }
            }
        }
        _overrides.Clear();
        _floating = null;
        Selection = state.Selection;
        if (resized)
        {
            Projection = new RasterImage(Width, Height);
            _onion = null;
            _onionBounds = PixelRect.Empty;
            ClearFrameCache();
            SizeChanged?.Invoke();
        }
        if (_animation is not null) foreach (var layer in _layers) DisplayCell(layer);
        SelectionChanged?.Invoke();
        LayersChanged?.Invoke();
        RecompositeAll();
    }

    public int TilesX => (Width + TileSize - 1) / TileSize;
    public int TilesY => (Height + TileSize - 1) / TileSize;

    /// <summary>The pixel rectangle of tile (tx, ty), clipped to the document.</summary>
    public PixelRect TileRect(int tx, int ty) =>
        new PixelRect(tx * TileSize, ty * TileSize, TileSize, TileSize).Intersect(Bounds);
}

/// <summary>Pixels lifted off a layer while moving or transforming them.</summary>
internal sealed record FloatingPixels(int LayerId, RasterImage Image, int X, int Y)
{
    public PixelRect Bounds => new(X, Y, Image.Width, Image.Height);
}
