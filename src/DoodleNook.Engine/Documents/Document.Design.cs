using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Documents;

/// <summary>A guide line (vertical: at x = Position; horizontal: at y = Position). Guides are never exported.</summary>
public readonly record struct Guide(bool Vertical, double Position);

/// <summary>The perspective assistant: 0 (off), 1 or 2 vanishing points, in document coordinates.</summary>
public sealed record PerspectiveGuide(int Points, double V1X, double V1Y, double V2X, double V2Y)
{
    public static readonly PerspectiveGuide Off = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Mission 008: layer groups, masks, text, shape and adjustment layers, guides and the perspective assistant.
/// Every change is one undo step; groups and object layers are the same on every frame of an animation.
/// </summary>
public sealed partial class Document
{
    private List<Guide> _guides = new();
    private bool _editingMask;

    /// <summary>Renders text layers (the app supplies one that uses the system's fonts). Without it text is blank.</summary>
    public static ITextRenderer? TextRenderer { get; set; }

    /// <summary>Raised when guides or the perspective assistant change.</summary>
    public event Action? GuidesChanged;

    // ================================================================== mask editing target

    /// <summary>True when painting goes to the active layer's mask (white shows, black hides) instead of its pixels.</summary>
    public bool EditingMask => _editingMask && _active.HasMask;

    public void SetEditingMask(bool mask)
    {
        if (_editingMask == mask) return;
        _editingMask = mask;
        ActiveLayerChanged?.Invoke();
    }

    /// <summary>True when an edit of <paramref name="layer"/> goes to its mask.</summary>
    public bool EditsMask(Layer layer) => _editingMask && ReferenceEquals(layer, _active) && layer.HasMask;

    /// <summary>The image tools read and change for a layer: its mask while mask editing, otherwise its pixels.</summary>
    public RasterImage EditTargetOf(Layer layer) => EditsMask(layer) ? layer.Mask! : layer.Image;

    // ================================================================== structure (groups) — one snapshot per change

    internal sealed record Structure(List<(Layer Layer, LayerProperties Properties)> Layers, int ActiveId);

    internal Structure CaptureStructure2() => new(_layers.Select(l => (l, l.Properties)).ToList(), _active.Id);

    internal void ApplyStructure(Structure s)
    {
        _layers.Clear();
        foreach (var (layer, props) in s.Layers)
        {
            layer.Properties = props;
            _layers.Add(layer);
            if (_animation is not null && layer.IsTrack) DisplayCell(layer);
        }
        _active = FindLayer(s.ActiveId) ?? _layers[^1];
        LayersChanged?.Invoke();
        ActiveLayerChanged?.Invoke();
        RecompositeAll();
    }

    private void CommitStructure(string name, Structure before)
    {
        var after = CaptureStructure2();
        LayersChanged?.Invoke();
        ActiveLayerChanged?.Invoke();
        RecompositeAll();
        Commit(new LayerStructureAction(name, before, after));
    }

    /// <summary>The layers directly inside a group (or at the top level for 0), bottom first.</summary>
    public IReadOnlyList<Layer> ChildrenOf(int parentId) => _layers.Where(l => l.ParentId == parentId).ToList();

    /// <summary>A layer and everything inside it (for a group), bottom first.</summary>
    public IReadOnlyList<Layer> Subtree(Layer layer)
    {
        var result = new List<Layer>();
        void Add(Layer l)
        {
            foreach (var c in _layers.Where(c => c.ParentId == l.Id)) Add(c);
            result.Add(l);
        }
        Add(layer);
        return result.OrderBy(_layers.IndexOf).ToList();
    }

    /// <summary>How deep a layer sits in groups (0 at the top level).</summary>
    public int DepthOf(Layer layer)
    {
        int depth = 0;
        for (var p = FindLayer(layer.ParentId); p is not null && depth < 64; p = FindLayer(p.ParentId)) depth++;
        return depth;
    }

    /// <summary>True when <paramref name="layer"/> is shown: it and every group around it are visible.</summary>
    public bool IsShown(Layer layer)
    {
        for (Layer? l = layer; l is not null; l = FindLayer(l.ParentId))
            if (!l.Visible) return false;
        return true;
    }

    private Layer NewLayer(LayerProperties props, RasterImage image, LayerKind kind) => new(_nextLayerId++, props, image, kind);

    /// <summary>The 1 × 1 stand-in image of a group or adjustment layer (they hold no pixels).</summary>
    internal static RasterImage Placeholder() => new(1, 1);

    /// <summary>Puts layers (of the same group) into a new group, placed where the topmost of them was.</summary>
    public Layer GroupLayers(IReadOnlyList<Layer> members, string? name = null)
    {
        if (members.Count == 0) throw new ArgumentException("Nothing to group.", nameof(members));
        int parent = members[0].ParentId;
        var before = CaptureStructure2();
        var group = NewLayer(new LayerProperties(name ?? NextGroupName(), ParentId: parent), Placeholder(), LayerKind.Group);
        int top = members.Max(IndexOf);
        _layers.Insert(top + 1, group);
        foreach (var m in members) m.Properties = m.Properties with { ParentId = group.Id };
        _active = group;
        CommitStructure("Group layers", before);
        return group;
    }

    /// <summary>A new, empty group above the active layer (in the active layer's group).</summary>
    public Layer AddGroup(string? name = null)
    {
        var before = CaptureStructure2();
        var group = NewLayer(new LayerProperties(name ?? NextGroupName(), ParentId: _active.ParentId), Placeholder(), LayerKind.Group);
        _layers.Insert(IndexOf(_active) + 1, group);
        _active = group;
        CommitStructure("New group", before);
        return group;
    }

    private string NextGroupName()
    {
        int n = 1;
        while (_layers.Any(l => l.IsGroup && l.Name == $"Group {n}")) n++;
        return $"Group {n}";
    }

    /// <summary>Removes a group but keeps its layers (they move up one level, in place).</summary>
    public bool Ungroup(Layer group)
    {
        if (!group.IsGroup) return false;
        var before = CaptureStructure2();
        int at = IndexOf(group);
        var children = _layers.Where(l => l.ParentId == group.Id).ToList();
        foreach (var c in children)
        {
            c.Properties = c.Properties with { ParentId = group.ParentId };
            _layers.Remove(c);
        }
        at = IndexOf(group);
        _layers.Remove(group);
        _layers.InsertRange(Math.Clamp(at, 0, _layers.Count), children);
        _active = children.Count > 0 ? children[^1] : _layers[Math.Clamp(at - 1, 0, _layers.Count - 1)];
        CommitStructure("Ungroup", before);
        return true;
    }

    /// <summary>Moves a layer into a group, as its top layer.</summary>
    public bool MoveIntoGroup(Layer layer, Layer group)
    {
        if (!group.IsGroup || ReferenceEquals(layer, group) || Subtree(layer).Contains(group)) return false;
        var before = CaptureStructure2();
        var block = Subtree(layer);
        foreach (var l in block) _layers.Remove(l);
        layer.Properties = layer.Properties with { ParentId = group.Id };
        _layers.InsertRange(IndexOf(group), block);
        CommitStructure("Move into group", before);
        return true;
    }

    /// <summary>Moves a layer out of its group, just above the group.</summary>
    public bool MoveOutOfGroup(Layer layer)
    {
        if (FindLayer(layer.ParentId) is not { } group) return false;
        var before = CaptureStructure2();
        var block = Subtree(layer);
        foreach (var l in block) _layers.Remove(l);
        layer.Properties = layer.Properties with { ParentId = group.ParentId };
        _layers.InsertRange(IndexOf(group) + 1, block);
        CommitStructure("Move out of group", before);
        return true;
    }

    /// <summary>Moves a layer (with everything inside it) one step up (+1) or down (−1) among the layers of its group.</summary>
    public bool MoveAmongSiblings(Layer layer, int direction)
    {
        var siblings = ChildrenOf(layer.ParentId);
        int i = siblings.ToList().IndexOf(layer), j = i + Math.Sign(direction);
        if (i < 0 || j < 0 || j >= siblings.Count) return false;
        var before = CaptureStructure2();
        var block = Subtree(layer);
        var other = Subtree(siblings[j]);
        foreach (var l in block) _layers.Remove(l);
        int at = direction > 0 ? IndexOf(other[^1]) + 1 : IndexOf(other[0]);
        _layers.InsertRange(at, block);
        CommitStructure(direction > 0 ? "Move layer up" : "Move layer down", before);
        return true;
    }

    /// <summary>Moves a layer (with everything inside it) just above or below <paramref name="target"/>, into the target's group.</summary>
    public bool MoveNextTo(Layer layer, Layer target, bool above)
    {
        if (ReferenceEquals(layer, target)) return false;
        var block = Subtree(layer);
        if (block.Contains(target)) return false;
        var before = CaptureStructure2();
        foreach (var l in block) _layers.Remove(l);
        layer.Properties = layer.Properties with { ParentId = target.ParentId };
        var targetBlock = Subtree(target);
        int at = above ? IndexOf(targetBlock[^1]) + 1 : IndexOf(targetBlock[0]);
        _layers.InsertRange(at, block);
        if (_layers.Select(l => l.Id).SequenceEqual(before.Layers.Select(e => e.Layer.Id)) && layer.Properties == before.Layers.First(e => ReferenceEquals(e.Layer, layer)).Properties)
        {
            ApplyStructure(before); // nothing changed
            return false;
        }
        CommitStructure("Move layer", before);
        return true;
    }

    private Layer DuplicateGroup(Layer group)
    {
        var before = CaptureStructure2();
        var block = Subtree(group);
        var ids = new Dictionary<int, int>();
        var copies = new List<Layer>();
        foreach (var l in block)
        {
            var c = CloneLayer(l, l.Properties);
            ids[l.Id] = c.Id;
            copies.Add(c);
        }
        foreach (var c in copies)
            if (ids.TryGetValue(c.ParentId, out var parent)) c.Properties = c.Properties with { ParentId = parent };
        var top = copies[^1];
        top.Properties = top.Properties with { Name = group.Name + " copy" };
        _layers.InsertRange(IndexOf(group) + 1, copies);
        _active = top;
        CommitStructure("Duplicate group", before);
        return top;
    }

    /// <summary>Deletes a group with everything inside it (one undo step).</summary>
    public bool DeleteGroup(Layer group)
    {
        var block = Subtree(group);
        if (block.Count >= _layers.Count) return false; // a picture keeps at least one layer
        var before = CaptureStructure2();
        int at = IndexOf(block[0]);
        foreach (var l in block) _layers.Remove(l);
        _active = _layers[Math.Clamp(at - 1, 0, _layers.Count - 1)];
        CommitStructure("Delete group", before);
        return true;
    }

    // ================================================================== masks

    /// <summary>Adds a mask that shows everything, or only the selection when <paramref name="fromSelection"/>.</summary>
    public bool AddMask(Layer layer, bool fromSelection = false)
    {
        if (layer.HasMask) return false;
        var mask = new RasterImage(Width, Height);
        uint white = Pixel.Opaque(ColorRgb.White), black = Pixel.Opaque(ColorRgb.Black);
        if (fromSelection && Selection is { } sel)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    byte v = sel[x, y];
                    mask.Pixels[y * Width + x] = Pixel.Pack(255, v, v, v);
                }
        }
        else mask.Fill(white);
        _ = black;
        ChangeMask(layer, mask, "Add mask");
        _editingMask = true;
        ActiveLayerChanged?.Invoke();
        return true;
    }

    public bool DeleteMask(Layer layer)
    {
        if (!layer.HasMask) return false;
        ChangeMask(layer, null, "Delete mask");
        return true;
    }

    /// <summary>Swaps what the mask shows and hides.</summary>
    public bool InvertMask(Layer layer)
    {
        if (layer.Mask is not { } m) return false;
        var inv = new RasterImage(Width, Height);
        for (int i = 0; i < m.Pixels.Length; i++)
        {
            byte v = (byte)(255 - Layer.MaskValue(m.Pixels[i]));
            inv.Pixels[i] = Pixel.Pack(255, v, v, v);
        }
        ChangeMask(layer, inv, "Invert mask");
        return true;
    }

    /// <summary>Bakes the mask into the layer's pixels (every drawing of an animation layer) and removes it.</summary>
    public bool ApplyMask(Layer layer)
    {
        if (layer.Mask is not { } m || !layer.IsPaintable) return false;
        var state = MapImages((l, img) =>
        {
            if (!ReferenceEquals(l, layer)) return img;
            var copy = img.Clone();
            for (int i = 0; i < copy.Pixels.Length; i++)
            {
                uint p = copy.Pixels[i];
                if (p != 0) copy.Pixels[i] = Blending.Fade(p, Layer.MaskValue(m.Pixels[i]));
            }
            return copy;
        });
        var before = CaptureState();
        ApplyStateInternal(state);
        var maskAction = new MaskAction("Apply mask", layer.Id, m, null);
        maskAction.Redo(this);
        Commit(new CompositeAction("Apply mask", [new DocumentStateAction("Apply mask", before, state), maskAction]));
        return true;
    }

    private void ChangeMask(Layer layer, RasterImage? mask, string name)
    {
        var action = new MaskAction(name, layer.Id, layer.Mask, mask);
        action.Redo(this);
        Commit(action);
    }

    internal void SetMaskInternal(int layerId, RasterImage? mask)
    {
        if (FindLayer(layerId) is not { } layer) return;
        layer.Mask = mask;
        layer.Revision++;
        layer.MaskRevision++;
        if (mask is null && ReferenceEquals(layer, _active)) _editingMask = false;
        LayersChanged?.Invoke();
        ActiveLayerChanged?.Invoke();
        RecompositeAll();
    }

    // ================================================================== text, shape and adjustment layers

    /// <summary>Adds a text layer above the active one and makes it active.</summary>
    public Layer AddTextLayer(TextContent text, string? name = null) =>
        AddObjectLayer(LayerKind.Text, text, name ?? LayerNameFor(text));

    /// <summary>Adds a shape layer above the active one and makes it active.</summary>
    public Layer AddShapeLayer(ShapeContent shape, string? name = null) =>
        AddObjectLayer(LayerKind.Shape, shape, name ?? shape.Kind switch
        {
            ShapeType.RoundedRectangle => "Rounded rectangle",
            ShapeType.Ellipse => "Ellipse",
            ShapeType.Line => "Line",
            ShapeType.Polygon => "Polygon",
            _ => "Rectangle",
        });

    /// <summary>Adds an adjustment layer (it changes everything below it in its group).</summary>
    public Layer AddAdjustmentLayer(Adjustment adjustment, string? name = null)
    {
        if (!adjustment.MapsColours) throw new ArgumentException("Only colour adjustments can be adjustment layers.", nameof(adjustment));
        return AddObjectLayer(LayerKind.Adjustment, new AdjustmentContent(adjustment), name ?? adjustment.Name);
    }

    private static string LayerNameFor(TextContent t)
    {
        var first = t.Text.Split('\n')[0].Trim();
        return first.Length == 0 ? "Text" : first.Length > 24 ? first[..24] + "…" : first;
    }

    private Layer AddObjectLayer(LayerKind kind, LayerContent content, string name)
    {
        var layer = NewLayer(new LayerProperties(name, ParentId: _active.ParentId), kind == LayerKind.Adjustment ? Placeholder() : new RasterImage(Width, Height), kind);
        layer.Content = content;
        Render(layer);
        int at = IndexOf(_active) + 1;
        InsertLayerInternal(layer, at, makeActive: true);
        Commit(new AddLayerAction(kind switch { LayerKind.Text => "Add text", LayerKind.Shape => "Add shape", _ => "Add adjustment layer" }, layer, at));
        return layer;
    }

    /// <summary>Changes a text, shape or adjustment layer's content as one undo step.</summary>
    public bool SetContent(Layer layer, LayerContent content, string historyName)
    {
        if (layer.Content is null || layer.Content == content || layer.Content.GetType() != content.GetType()) return false;
        var action = new ContentAction(historyName, layer.Id, layer.Content, content);
        action.Redo(this);
        Commit(action);
        return true;
    }

    internal void SetContentInternal(int layerId, LayerContent content)
    {
        if (FindLayer(layerId) is not { } layer) return;
        var old = layer.ContentBounds;
        layer.Content = content;
        layer.Revision++;
        Render(layer);
        LayersChanged?.Invoke();
        // Text and shapes only change where they were and are; an adjustment changes everything below it.
        if (layer.IsObject && !_layers.Any(l => l.ClipToBelow)) Recomposite(old.Union(layer.ContentBounds));
        else RecompositeAll();
    }

    /// <summary>Shows a content change without recording it (live dragging and sliders); finish with <see cref="CommitContent"/>.</summary>
    public void PreviewContent(Layer layer, LayerContent content)
    {
        if (layer.Content is null || layer.Content.GetType() != content.GetType() || layer.Content == content) return;
        SetContentInternal(layer.Id, content);
    }

    /// <summary>Records a change shown with <see cref="PreviewContent"/>, starting from <paramref name="from"/>.</summary>
    public void CommitContent(Layer layer, LayerContent from, string historyName)
    {
        if (layer.Content is not { } to || to == from) return;
        Commit(new ContentAction(historyName, layer.Id, from, to));
    }

    /// <summary>Draws a text or shape layer's pixels from its content (palette-locked pictures: palette colours, same alpha).
    /// The layer's image is reused when it has the picture's size, so dragging an object doesn't allocate.</summary>
    internal void Render(Layer layer)
    {
        if (!layer.IsObject) return;
        var image = layer.Image;
        if (image.Width != Width || image.Height != Height) image = new RasterImage(Width, Height);
        else image.Clear(layer.ContentBounds);
        PixelRect bounds = PixelRect.Empty;
        switch (layer.Content)
        {
            case TextContent text when TextRenderer is { } renderer:
            {
                var r = renderer.Render(text, image);
                bounds = r.Bounds;
                layer.ContentSize = (r.Width, r.Height);
                layer.SubstituteFont = r.Substitute;
                break;
            }
            case TextContent text:
                layer.ContentSize = (Math.Max(1, text.Text.Split('\n').Max(l => l.Length)) * text.Size * 0.55, text.Size * text.LineSpacing * Math.Max(1, text.Text.Split('\n').Length));
                break;
            case ShapeContent shape:
                bounds = ShapeRasterizer.RenderInto(shape, image);
                layer.ContentSize = (Math.Abs(shape.Width), Math.Abs(shape.Height));
                break;
        }
        bounds = bounds.Intersect(Bounds);
        ConformToPalette(image, bounds);
        layer.Image = image;
        layer.ContentBounds = bounds;
        layer.Revision++;
    }

    /// <summary>The frame of a text or shape layer (for handles, moving and alignment).</summary>
    public ObjectBox? BoxOf(Layer layer) => layer.Content switch
    {
        TextContent t => new ObjectBox(t.X, t.Y, layer.ContentSize.Width, layer.ContentSize.Height, t.Rotation),
        ShapeContent s => ShapeRasterizer.Box(s),
        _ => null,
    };

    /// <summary>Moves text and shape layers by (dx, dy) as one undo step.</summary>
    public bool MoveObjects(IReadOnlyList<Layer> layers, double dx, double dy, string historyName = "Move")
    {
        var actions = new List<IHistoryAction>();
        foreach (var l in layers.Where(l => l.IsObject && !l.Locked))
        {
            var from = l.Content!;
            var to = from.Translated(dx, dy);
            if (to == from) continue;
            SetContentInternal(l.Id, to);
            actions.Add(new ContentAction(historyName, l.Id, from, to));
        }
        if (actions.Count == 0) return false;
        Commit(actions.Count == 1 ? actions[0] : new CompositeAction(historyName, actions));
        return true;
    }

    /// <summary>Lines up text and shape layers: with each other (the box around them all) or, for a single layer or when
    /// <paramref name="toCanvas"/>, with the canvas. One undo step.</summary>
    public bool AlignObjects(IReadOnlyList<Layer> layers, AlignEdge edge, bool toCanvas)
    {
        var objects = layers.Where(l => l.IsObject && !l.Locked && BoxOf(l) is not null).ToList();
        if (objects.Count == 0) return false;
        (double, double, double, double) reference = (0, 0, Width, Height);
        if (!toCanvas && objects.Count > 1)
        {
            var e = objects.Select(l => BoxOf(l)!.Value.Extent()).ToList();
            reference = (e.Min(x => x.Left), e.Min(x => x.Top), e.Max(x => x.Right), e.Max(x => x.Bottom));
        }
        var actions = new List<IHistoryAction>();
        string name = $"Align {edge switch { AlignEdge.Left => "left", AlignEdge.CenterX => "centres", AlignEdge.Right => "right", AlignEdge.Top => "top", AlignEdge.CenterY => "middles", _ => "bottom" }}";
        foreach (var l in objects)
        {
            var (dx, dy) = Alignment.Offset(BoxOf(l)!.Value, reference, edge);
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) continue;
            var from = l.Content!;
            var to = from.Translated(dx, dy);
            SetContentInternal(l.Id, to);
            actions.Add(new ContentAction(name, l.Id, from, to));
        }
        if (actions.Count == 0) return false;
        Commit(actions.Count == 1 ? actions[0] : new CompositeAction(name, actions));
        return true;
    }

    /// <summary>Text and shape layers whose fonts are missing on this computer (layer name → font used instead).</summary>
    public IReadOnlyList<(string Layer, string Requested, string Used)> MissingFonts =>
        _layers.Where(l => l.Content is TextContent && l.SubstituteFont is not null)
            .Select(l => (l.Name, ((TextContent)l.Content!).FontFamily, l.SubstituteFont!)).ToList();

    /// <summary>Turns a text or shape layer into an ordinary pixel layer (one undo step).</summary>
    public Layer? ConvertToPixels(Layer layer)
    {
        if (!layer.IsObject) return null;
        var before = CaptureStructure2();
        var raster = NewLayer(layer.Properties, layer.Image.Clone(), LayerKind.Raster);
        raster.Mask = layer.Mask?.Clone();
        if (_animation is not null) PrepareNewLayerTrack(raster, hasPixels: true);
        int at = IndexOf(layer);
        _layers[at] = raster;
        if (_animation is not null) DisplayCell(raster);
        _active = raster;
        CommitStructure("Convert to pixels", before);
        return raster;
    }

    /// <summary>A throwaway copy of one frame for previews (remapping): the same layers, groups, masks and adjustments,
    /// with each pixel layer's image passed through <paramref name="map"/>.</summary>
    internal Document PreviewCopy(int frame, Func<Layer, RasterImage, RasterImage> map)
    {
        var copy = new Document(Width, Height, Paper, PaperVisible, []);
        copy._layers.Clear();
        foreach (var l in _layers)
        {
            RasterImage image = !l.HasPixels ? Placeholder()
                : l.IsTrack ? CelAt(l, frame)?.Image ?? new RasterImage(Width, Height)
                : l.Image;
            if (l.HasPixels) image = map(l, image);
            copy._layers.Add(new Layer(l.Id, l.Properties, image, l.Kind) { Content = l.Content, Mask = l.Mask });
        }
        copy._active = copy._layers[^1];
        copy.RecompositeAll(notify: false);
        return copy;
    }

    // ================================================================== saving and opening

    /// <summary>True when the picture uses anything from Mission 008 (groups, masks, text, shapes, adjustment layers,
    /// guides or perspective): it is then saved in format version 4.</summary>
    public bool UsesDesignFeatures =>
        _layers.Any(l => l.Kind != LayerKind.Raster || l.HasMask || l.ParentId != 0 || l.Properties.Collapsed || !l.MaskEnabled)
        || _guides.Count > 0 || GuidesLocked || Perspective.Points > 0;

    /// <summary>While opening a file: gives the layer at <paramref name="index"/> its kind, content, mask and group.</summary>
    internal void PrepareLoadedLayer(int index, LayerKind kind, LayerContent? content, RasterImage? mask, int parentIndex)
    {
        var layer = _layers[index];
        if (kind != LayerKind.Raster)
        {
            var replacement = new Layer(layer.Id, layer.Properties, kind is LayerKind.Group or LayerKind.Adjustment ? Placeholder() : new RasterImage(Width, Height), kind)
            {
                Content = content,
            };
            _layers[index] = replacement;
            if (ReferenceEquals(_active, layer)) _active = replacement;
            layer = replacement;
        }
        if (mask is not null && mask.Width == Width && mask.Height == Height) layer.Mask = mask;
        // A group must be above its layers (later in the list) and be a group; anything else stays at the top level.
        int parent = parentIndex > index && parentIndex < _layers.Count ? _layers[parentIndex].Id : 0;
        layer.Properties = layer.Properties with { ParentId = parent };
    }

    /// <summary>While opening a file: guides, perspective, then draws text and shapes and composites the picture.</summary>
    internal void FinishLoad(IEnumerable<Guide> guides, bool guidesLocked, PerspectiveGuide perspective)
    {
        // Parents that aren't groups (a damaged file) are dropped.
        foreach (var l in _layers)
            if (l.ParentId != 0 && FindLayer(l.ParentId) is not { IsGroup: true })
                l.Properties = l.Properties with { ParentId = 0 };
        _guides = guides.Where(g => double.IsFinite(g.Position)).ToList();
        GuidesLocked = guidesLocked;
        Perspective = perspective;
        foreach (var l in _layers.Where(l => l.IsObject)) Render(l);
        ClearFrameCache();
        RecompositeAll(notify: false);
    }

    // ================================================================== guides and perspective

    public IReadOnlyList<Guide> Guides => _guides;

    /// <summary>Guides can't be moved or removed while locked.</summary>
    public bool GuidesLocked { get; set; }

    /// <summary>The perspective assistant's vanishing points (saved with the picture).</summary>
    public PerspectiveGuide Perspective { get; private set; } = PerspectiveGuide.Off;

    public void SetGuides(IEnumerable<Guide> guides, string historyName)
    {
        var next = guides.Where(g => double.IsFinite(g.Position)).ToList();
        if (next.SequenceEqual(_guides)) return;
        var action = new GuidesAction(historyName, _guides.ToList(), next);
        action.Redo(this);
        Commit(action);
    }

    internal void SetGuidesInternal(List<Guide> guides)
    {
        _guides = guides;
        GuidesChanged?.Invoke();
    }

    /// <summary>Sets the perspective assistant (a drawing aid, not part of the picture: not an undo step).</summary>
    public void SetPerspective(PerspectiveGuide perspective)
    {
        Perspective = perspective;
        GuidesChanged?.Invoke();
    }
}

/// <summary>Layer order, groups and properties before and after (grouping, ungrouping, moving in and out of groups).</summary>
public sealed class LayerStructureAction : IHistoryAction
{
    private readonly Document.Structure _before;
    private readonly Document.Structure _after;

    internal LayerStructureAction(string name, Document.Structure before, Document.Structure after)
    {
        Name = name;
        _before = before;
        _after = after;
    }

    public string Name { get; }
    public long ByteSize => 64 + 32L * (_before.Layers.Count + _after.Layers.Count);
    public void Undo(Document d) => d.ApplyStructure(_before);
    public void Redo(Document d) => d.ApplyStructure(_after);
}

/// <summary>A layer mask added, changed or removed.</summary>
public sealed class MaskAction(string name, int layerId, RasterImage? before, RasterImage? after) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize { get; } = 64 + 4L * ((before?.Pixels.Length ?? 0) + (after?.Pixels.Length ?? 0));
    public void Undo(Document d) => d.SetMaskInternal(layerId, before);
    public void Redo(Document d) => d.SetMaskInternal(layerId, after);
}

/// <summary>A text, shape or adjustment layer's content before and after.</summary>
public sealed class ContentAction(string name, int layerId, LayerContent before, LayerContent after) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize => 256;
    public int LayerId => layerId;
    public void Undo(Document d) => d.SetContentInternal(layerId, before);
    public void Redo(Document d) => d.SetContentInternal(layerId, after);
}

/// <summary>Guides before and after.</summary>
public sealed class GuidesAction(string name, List<Guide> before, List<Guide> after) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize => 64 + 16L * (before.Count + after.Count);
    public void Undo(Document d) => d.SetGuidesInternal(before.ToList());
    public void Redo(Document d) => d.SetGuidesInternal(after.ToList());
}
