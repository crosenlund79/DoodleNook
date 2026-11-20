using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>What a newly inserted frame contains on the active layer (other layers keep showing their drawing).</summary>
public enum NewFrameContent
{
    /// <summary>An empty cell, ready for a new drawing.</summary>
    Blank,

    /// <summary>An independent copy of the current drawing, ready to be changed.</summary>
    Duplicate,
}

/// <summary>
/// Frame-by-frame animation. Every layer is a track with one cell per frame; a cell holds a <see cref="Cel"/> (or
/// nothing). <see cref="Layer.Image"/> always shows the current frame's cel, so all painting and editing tools work
/// unchanged on "the current drawing". Frame and cel commands are undoable; navigating frames is not.
/// </summary>
public sealed partial class Document
{
    private Animation? _animation;
    private readonly Dictionary<int, (ulong Key, RasterImage Image)> _frameCache = new();
    private long _frameCacheBytes;
    private RasterImage? _onion;
    private PixelRect _onionBounds = PixelRect.Empty;

    /// <summary>Timing and playback settings; null for a still picture.</summary>
    public Animation? Animation => _animation;

    public bool IsAnimated => _animation is not null;

    /// <summary>The frame shown and edited (0 for a still picture).</summary>
    public int CurrentFrame => _animation?.CurrentFrame ?? 0;

    public int FrameCount => _animation?.FrameCount ?? 1;

    /// <summary>Memory the frame cache uses now (diagnostics and measurements).</summary>
    public long FrameCacheBytes => _frameCacheBytes;

    /// <summary>Memory the frame cache may use for fast frame switching and playback.</summary>
    public long FrameCacheBudget { get; set; } = 512L * 1024 * 1024;

    /// <summary>Raised when the current frame changes (navigation, playback, or a frame command).</summary>
    public event Action? FrameChanged;

    /// <summary>Raised when frames, cels or timing change.</summary>
    public event Action? AnimationChanged;

    internal RasterImage? OnionImage => _onion;

    /// <summary>True while onion skins are shown.</summary>
    public bool OnionVisible => _onion is not null;
    internal PixelRect OnionBounds => _onionBounds;

    // ================================================================ conversion

    /// <summary>Turns a still picture into a one-frame animation (each layer's pixels become its first cel).</summary>
    public bool MakeAnimation(double fps = Animation.DefaultFps)
    {
        if (_animation is not null) return false;
        var first = FirstCels();
        CreateAnimationInternal(fps, first);
        Commit(new MakeAnimationAction(fps, first));
        return true;
    }

    private Dictionary<int, Cel?> FirstCels() =>
        _layers.Where(l => l.IsPaintable).ToDictionary(l => l.Id, l => l.ContentBounds.IsEmpty ? null : new Cel(l.Image, l.ContentBounds));

    internal void CreateAnimationInternal(double fps, IReadOnlyDictionary<int, Cel?> firstCels)
    {
        _animation = new Animation(fps);
        // Raster layers become tracks; text, shapes, groups and adjustment layers stay the same on every frame.
        foreach (var l in _layers) l.Cells = l.IsPaintable ? [firstCels.GetValueOrDefault(l.Id)] : null;
        ClearFrameCache();
        ShowFrameInternal(0);
        AnimationChanged?.Invoke();
        LayersChanged?.Invoke();
    }

    internal void RemoveAnimationInternal()
    {
        if (_animation is null) return;
        foreach (var l in _layers.Where(l => l.IsTrack))
        {
            var cel = l.Cells is { Count: > 0 } c ? c[0] : null;
            l.Image = cel?.Image ?? new RasterImage(Width, Height);
            l.ContentBounds = cel?.ContentBounds ?? PixelRect.Empty;
            l.CurrentCel = null;
            l.Cells = null;
            l.Revision++;
        }
        _animation = null;
        _onion = null;
        _onionBounds = PixelRect.Empty;
        ClearFrameCache();
        RecompositeAll();
        FrameChanged?.Invoke();
        AnimationChanged?.Invoke();
        LayersChanged?.Invoke();
    }

    /// <summary>A brand-new animation document with <paramref name="frames"/> empty frames.</summary>
    public static Document NewAnimation(int width, int height, ColorRgb? paper, int frames = 1, double fps = Animation.DefaultFps, string firstLayerName = "Drawing")
    {
        var doc = new Document(width, height, paper, firstLayerName);
        doc.CreateAnimationInternal(fps, new Dictionary<int, Cel?>());
        if (frames > 1)
        {
            foreach (var l in doc._layers) l.Cells?.AddRange(Enumerable.Repeat<Cel?>(null, frames - 1));
            doc._animation!.Durations.AddRange(Enumerable.Repeat<int?>(null, frames - 1));
        }
        doc.History.Clear();
        doc._savedStateId = doc.History.CurrentStateId;
        return doc;
    }

    /// <summary>Builds an animation read from a file (layers bottom to top, each with one cel or null per frame).</summary>
    internal static Document CreateAnimated(int width, int height, ColorRgb? paper, bool paperVisible, IReadOnlyList<LayerProperties> layers,
        IReadOnlyList<List<Cel?>> tracks, double fps, bool loop, bool pingPong, IReadOnlyList<int?> durations, int currentFrame, int activeIndex)
    {
        var doc = new Document(width, height, paper, paperVisible, layers.Select(p => (p, new RasterImage(width, height))), activeIndex);
        doc._animation = new Animation(fps, durations.Count) { Loop = loop, PingPong = pingPong };
        doc._animation.Durations = durations.Select(d => d is { } ms ? Math.Clamp(ms, 10, 10_000) : (int?)null).ToList();
        for (int i = 0; i < doc._layers.Count; i++) doc._layers[i].Cells = tracks[i];
        doc.ShowFrameInternal(Math.Clamp(currentFrame, 0, durations.Count - 1));
        return doc;
    }

    // ================================================================ navigation and display

    /// <summary>Shows another frame (not an undo step). The current drawing of every layer changes with it.</summary>
    public void SetCurrentFrame(int frame)
    {
        if (_animation is null) return;
        frame = Math.Clamp(frame, 0, _animation.FrameCount - 1);
        if (frame == _animation.CurrentFrame) return;
        ShowFrameInternal(frame);
    }

    internal void ShowFrameInternal(int frame)
    {
        var a = _animation!;
        a.CurrentFrame = Math.Clamp(frame, 0, a.FrameCount - 1);
        _overrides.Clear();
        _floating = null;
        foreach (var l in _layers) DisplayCell(l);
        RecompositeFrame();
        FrameChanged?.Invoke();
    }

    private void DisplayCell(Layer l)
    {
        if (!l.IsTrack) return; // the same on every frame
        var a = _animation!;
        var cel = l.Cells is { } cells && a.CurrentFrame < cells.Count ? cells[a.CurrentFrame] : null;
        l.CurrentCel = cel;
        l.Image = cel?.Image ?? l.BlankFor(Width, Height);
        l.ContentBounds = cel?.ContentBounds ?? PixelRect.Empty;
        l.Revision++;
    }

    /// <summary>The cel a layer shows at a frame (null for an empty cell, or for a still picture).</summary>
    public Cel? CelAt(Layer layer, int frame) =>
        layer.Cells is { } c && frame >= 0 && frame < c.Count ? c[frame] : null;

    /// <summary>The frames over which the cell at <paramref name="frame"/> is held (its run of the same cel).
    /// For an empty cell, the run of empty cells.</summary>
    public (int Start, int End) HoldRange(Layer layer, int frame)
    {
        if (layer.Cells is not { } c || frame < 0 || frame >= c.Count) return (frame, frame);
        var cel = c[frame];
        int s = frame, e = frame;
        while (s > 0 && ReferenceEquals(c[s - 1], cel)) s--;
        while (e < c.Count - 1 && ReferenceEquals(c[e + 1], cel)) e++;
        return (s, e);
    }

    /// <summary>Composites any frame without changing the current one (exports, preview, playback cache).</summary>
    public RasterImage RenderFrame(int frame, bool includePaper = true)
    {
        var image = RenderFrameCore(frame, includePaper);
        image.ExactPalette = PaletteConformer; // exports give exact palette colours for a locked picture
        return image;
    }

    private RasterImage RenderFrameCore(int frame, bool includePaper)
    {
        var image = new RasterImage(Width, Height);
        if (_animation is null)
        {
            Compositor.Compose(this, Bounds, image, includeOverlays: false, includePaper: includePaper);
            return image;
        }
        frame = Math.Clamp(frame, 0, _animation.FrameCount - 1);
        if (includePaper && _frameCache.TryGetValue(frame, out var e) && e.Key == FrameKey(frame))
        {
            Array.Copy(e.Image.Pixels, image.Pixels, image.Pixels.Length);
            return image;
        }
        Compositor.ComposeFrame(this, frame, Bounds, image, includePaper);
        if (includePaper) StoreInCache(frame, image);
        return image;
    }

    private void RecompositeFrame()
    {
        int f = CurrentFrame;
        bool cacheable = _onion is null && _floating is null && _overrides.Count == 0;
        if (cacheable && _frameCache.TryGetValue(f, out var e) && e.Key == FrameKey(f))
        {
            Array.Copy(e.Image.Pixels, Projection.Pixels, Projection.Pixels.Length);
        }
        else
        {
            Compositor.Compose(this, Bounds, Projection, includeOverlays: true, includePaper: true);
            if (cacheable) StoreInCache(f, Projection);
        }
        ProjectionChanged?.Invoke(Bounds);
    }

    private void StoreInCache(int frame, RasterImage image)
    {
        long size = (long)image.Pixels.Length * 4;
        if (size > FrameCacheBudget) return;
        if (_frameCache.Remove(frame, out var old)) _frameCacheBytes -= (long)old.Image.Pixels.Length * 4;
        // Make room by dropping the frames farthest from this one.
        while (_frameCacheBytes + size > FrameCacheBudget && _frameCache.Count > 0)
        {
            int far = _frameCache.Keys.MaxBy(k => Math.Abs(k - frame));
            _frameCacheBytes -= (long)_frameCache[far].Image.Pixels.Length * 4;
            _frameCache.Remove(far);
        }
        _frameCache[frame] = (FrameKey(frame), image.Clone());
        _frameCacheBytes += size;
    }

    internal void ClearFrameCache()
    {
        _frameCache.Clear();
        _frameCacheBytes = 0;
    }

    /// <summary>Identifies everything that decides how a frame looks (drawings and their revisions, layer settings, paper).</summary>
    private ulong FrameKey(int frame)
    {
        ulong h = 14695981039346656037UL;
        void Add(long v)
        {
            unchecked
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
        }
        Add(Width);
        Add(Height);
        Add(Paper is { } p ? (p.R << 16 | p.G << 8 | p.B) + 1 : 0);
        Add(PaperVisible ? 1 : 0);
        foreach (var l in _layers)
        {
            Add(l.Id);
            Add(l.Properties.GetHashCode());
            Add(l.MaskRevision);
            if (!l.IsTrack)
            {
                Add(l.Revision); // text, shapes and adjustments: the same on every frame
                continue;
            }
            var cel = CelAt(l, frame);
            Add(cel?.Id ?? 0);
            Add(cel?.Revision ?? 0);
        }
        return h;
    }

    // ================================================================ onion skins

    /// <summary>Shows tinted neighbouring drawings under the layers (display only; never in exports or pixels).</summary>
    public void SetOnionSkin(RasterImage? overlay, PixelRect bounds)
    {
        var old = _onionBounds;
        _onion = overlay;
        _onionBounds = overlay is null ? PixelRect.Empty : bounds.Intersect(Bounds);
        var area = old.Union(_onionBounds);
        if (!area.IsEmpty) Recomposite(area);
    }

    // ================================================================ frame commands

    private (AnimationStructure Before, IHistoryAction? Conversion) BeginStructure()
    {
        IHistoryAction? conversion = null;
        if (_animation is null)
        {
            var first = FirstCels();
            CreateAnimationInternal(Animation.DefaultFps, first);
            conversion = new MakeAnimationAction(Animation.DefaultFps, first);
        }
        return (CaptureStructure(), conversion);
    }

    private void EndStructure(string name, (AnimationStructure Before, IHistoryAction? Conversion) start, int showFrame)
    {
        ShowFrameInternal(showFrame);
        var action = new AnimationStructureAction(name, start.Before, CaptureStructure());
        Commit(start.Conversion is null ? action : new CompositeAction(name, [start.Conversion, action]));
        AnimationChanged?.Invoke();
        LayersChanged?.Invoke();
    }

    internal AnimationStructure CaptureStructure()
    {
        var a = _animation!;
        return new AnimationStructure
        {
            Fps = a.Fps,
            Loop = a.Loop,
            PingPong = a.PingPong,
            CurrentFrame = a.CurrentFrame,
            Durations = new List<int?>(a.Durations),
            Tracks = _layers.Where(l => l.IsTrack).ToDictionary(l => l.Id, l => new List<Cel?>(l.Cells!)),
        };
    }

    internal void ApplyStructureInternal(AnimationStructure s)
    {
        var a = _animation!;
        a.Fps = s.Fps;
        a.Loop = s.Loop;
        a.PingPong = s.PingPong;
        a.Durations = new List<int?>(s.Durations);
        foreach (var l in _layers)
            if (s.Tracks.TryGetValue(l.Id, out var t)) l.Cells = new List<Cel?>(t);
        ShowFrameInternal(s.CurrentFrame);
        AnimationChanged?.Invoke();
        LayersChanged?.Invoke();
    }

    /// <summary>Inserts a frame after the current one and shows it. The active layer gets an empty cell (draw the next
    /// pose) or a copy of its drawing; the other layers keep showing theirs (a hold). Turns a still picture into an animation.</summary>
    public void NewFrame(NewFrameContent content = NewFrameContent.Blank) => InsertFrame(CurrentFrame + 1, content);

    /// <summary>Inserts a frame at <paramref name="at"/>; see <see cref="NewFrame"/>.</summary>
    public void InsertFrame(int at, NewFrameContent content)
    {
        var start = BeginStructure();
        var a = _animation!;
        int source = a.CurrentFrame;
        at = Math.Clamp(at, 0, a.FrameCount);
        foreach (var l in _layers.Where(l => l.IsTrack))
        {
            var cells = l.Cells!;
            var cel = cells[source];
            Cel? inserted = ReferenceEquals(l, _active)
                ? content == NewFrameContent.Duplicate && cel is not null ? cel.CloneCel() : null
                : cel;
            cells.Insert(at, inserted);
        }
        a.Durations.Insert(at, null);
        EndStructure(content == NewFrameContent.Duplicate ? "Duplicate frame" : "New frame", start, at);
    }

    /// <summary>Deletes a frame from every layer. The last frame cannot be deleted.</summary>
    public bool DeleteFrame(int frame)
    {
        if (_animation is not { FrameCount: > 1 } a || frame < 0 || frame >= a.FrameCount) return false;
        var start = BeginStructure();
        foreach (var l in _layers) l.Cells?.RemoveAt(frame);
        a.Durations.RemoveAt(frame);
        EndStructure("Delete frame", start, Math.Min(frame, a.FrameCount - 1));
        return true;
    }

    /// <summary>Moves a frame (with all its layers' cells) to another position.</summary>
    public bool MoveFrame(int from, int to)
    {
        if (_animation is not { } a || from < 0 || from >= a.FrameCount) return false;
        to = Math.Clamp(to, 0, a.FrameCount - 1);
        if (from == to) return false;
        var start = BeginStructure();
        foreach (var l in _layers.Where(l => l.IsTrack))
        {
            var c = l.Cells!;
            var cel = c[from];
            c.RemoveAt(from);
            c.Insert(to, cel);
        }
        var d = a.Durations[from];
        a.Durations.RemoveAt(from);
        a.Durations.Insert(to, d);
        EndStructure("Move frame", start, to);
        return true;
    }

    /// <summary>Sets one frame's own duration (null = use the frame rate).</summary>
    public bool SetFrameDuration(int frame, int? durationMs)
    {
        if (_animation is not { } a || frame < 0 || frame >= a.FrameCount) return false;
        if (durationMs is { } ms) durationMs = Math.Clamp(ms, 10, 10_000);
        if (a.Durations[frame] == durationMs) return false;
        var start = BeginStructure();
        a.Durations[frame] = durationMs;
        EndStructure("Frame duration", start, a.CurrentFrame);
        return true;
    }

    /// <summary>Changes the frame rate, looping and ping-pong (one undo step). Turns a still picture into an animation.</summary>
    public bool SetTiming(double fps, bool loop, bool pingPong)
    {
        fps = Math.Clamp(Math.Round(fps, 2), Animation.MinFps, Animation.MaxFps);
        if (_animation is { } cur && cur.Fps == fps && cur.Loop == loop && cur.PingPong == pingPong) return false;
        var start = BeginStructure();
        var a = _animation!;
        a.Fps = fps;
        a.Loop = loop;
        a.PingPong = pingPong;
        EndStructure("Playback timing", start, a.CurrentFrame);
        return true;
    }

    // ================================================================ cel commands

    private bool ValidCell(Layer layer, int frame) =>
        _animation is not null && layer.IsTrack && _layers.Contains(layer) && frame >= 0 && frame < _animation.FrameCount;

    /// <summary>Removes the drawing from one cell (the cell becomes empty).</summary>
    public bool DeleteCel(Layer layer, int frame)
    {
        if (!ValidCell(layer, frame) || layer.Cells![frame] is null) return false;
        var start = BeginStructure();
        layer.Cells[frame] = null;
        EndStructure("Delete drawing", start, CurrentFrame);
        return true;
    }

    /// <summary>A new, empty drawing from <paramref name="frame"/> to the end of its hold (so the frames that showed
    /// the old drawing there now show the new one).</summary>
    public bool NewBlankCel(Layer layer, int frame)
    {
        if (!ValidCell(layer, frame)) return false;
        var (_, end) = HoldRange(layer, frame);
        var start = BeginStructure();
        if (end == frame) layer.Cells![frame] = null;
        else
        {
            var blank = new Cel(new RasterImage(Width, Height), PixelRect.Empty);
            for (int f = frame; f <= end; f++) layer.Cells![f] = blank;
        }
        EndStructure("New drawing", start, frame);
        return true;
    }

    /// <summary>Breaks a hold: from <paramref name="frame"/> to the end of the hold, the layer shows an independent copy
    /// of the drawing, so changing it no longer changes the earlier frames.</summary>
    public bool MakeIndependent(Layer layer, int frame)
    {
        if (!ValidCell(layer, frame) || layer.Cells![frame] is not { } cel) return false;
        var (s, e) = HoldRange(layer, frame);
        if (s == e) return false;
        var start = BeginStructure();
        var copy = cel.CloneCel();
        for (int f = frame; f <= e; f++) layer.Cells[f] = copy;
        EndStructure("Make drawing independent", start, frame);
        return true;
    }

    /// <summary>Holds a drawing (or empty cell) one frame longer: this layer's later cells move one frame later. An empty
    /// cell pushed past the end is simply dropped; otherwise the animation grows by a frame (other layers keep showing
    /// their last drawing there).</summary>
    public bool ExtendHold(Layer layer, int frame)
    {
        if (!ValidCell(layer, frame)) return false;
        var start = BeginStructure();
        var a = _animation!;
        var (_, e) = HoldRange(layer, frame);
        var c = layer.Cells!;
        c.Insert(e + 1, c[frame]);
        if (c.Count > a.FrameCount && c[^1] is null)
        {
            // Only an empty cell was pushed past the end: the hold simply fills the empty cell after it.
            c.RemoveAt(c.Count - 1);
        }
        else if (c.Count > a.FrameCount)
        {
            foreach (var other in _layers)
                if (!ReferenceEquals(other, layer) && other.Cells is { } oc) oc.Add(oc[^1]);
            a.Durations.Add(null);
        }
        EndStructure("Hold longer", start, a.CurrentFrame);
        return true;
    }

    /// <summary>Holds a drawing one frame less: this layer's later cells move one frame earlier. A trailing frame that
    /// no longer shows anything new is removed.</summary>
    public bool ShortenHold(Layer layer, int frame)
    {
        if (!ValidCell(layer, frame)) return false;
        var (s, e) = HoldRange(layer, frame);
        if (s == e) return false;
        var start = BeginStructure();
        var a = _animation!;
        var c = layer.Cells!;
        c.RemoveAt(e);
        c.Add(null);
        int last = a.FrameCount - 1;
        bool trailingAddsNothing = last > 0 && _layers.Where(l => l.IsTrack).All(l => l.Cells![last] is null || ReferenceEquals(l.Cells[last], l.Cells[last - 1]));
        if (trailingAddsNothing && c[last] is null)
        {
            foreach (var l in _layers) l.Cells?.RemoveAt(last);
            a.Durations.RemoveAt(last);
        }
        EndStructure("Hold shorter", start, Math.Min(a.CurrentFrame, a.FrameCount - 1));
        return true;
    }

    /// <summary>Moves (swaps) a cell to another frame of the same layer.</summary>
    public bool MoveCel(Layer layer, int from, int to)
    {
        if (!ValidCell(layer, from) || !ValidCell(layer, to) || from == to) return false;
        var start = BeginStructure();
        var c = layer.Cells!;
        (c[from], c[to]) = (c[to], c[from]);
        EndStructure("Move drawing", start, to);
        return true;
    }

    /// <summary>Puts an independent copy of a drawing into a cell (paste).</summary>
    public bool PasteCel(Layer layer, int frame, Cel source)
    {
        if (!ValidCell(layer, frame) || source.Image.Width != Width || source.Image.Height != Height) return false;
        var start = BeginStructure();
        var copy = source.CloneCel();
        ConformToPalette(copy.Image, copy.Image.Bounds);
        layer.Cells![frame] = copy;
        EndStructure("Paste drawing", start, frame);
        return true;
    }

    /// <summary>A copy of a cel that stays valid whatever happens to the document (for the cel clipboard).</summary>
    public static Cel CopyOf(Cel cel) => cel.CloneCel();

    internal void SetCellInternal(int layerId, int frame, Cel? cel)
    {
        var layer = FindLayer(layerId);
        if (layer?.Cells is not { } c || frame < 0 || frame >= c.Count) return;
        c[frame] = cel;
        if (frame == CurrentFrame)
        {
            DisplayCell(layer);
            Recomposite(Bounds);
        }
        AnimationChanged?.Invoke();
    }

    /// <summary>Called when an edit changed the pixels of an empty cell: the blank becomes a real cel there.</summary>
    internal Cel AdoptBlank(Layer layer)
    {
        var cel = new Cel(layer.Image, layer.ContentBounds);
        layer.ReleaseBlank();
        layer.Cells![CurrentFrame] = cel;
        layer.CurrentCel = cel;
        AnimationChanged?.Invoke();
        return cel;
    }

    // ================================================================ layers in animations

    private List<Cel?> EmptyTrack() => Enumerable.Repeat<Cel?>(null, FrameCount).ToList();

    private void PrepareNewLayerTrack(Layer layer, bool hasPixels)
    {
        if (_animation is null || !layer.IsPaintable) return;
        layer.Cells = EmptyTrack();
        if (hasPixels)
        {
            var cel = new Cel(layer.Image, layer.ContentBounds);
            layer.Cells[CurrentFrame] = cel;
            layer.CurrentCel = cel;
        }
        else
        {
            layer.CurrentCel = null;
            layer.Image = layer.BlankFor(Width, Height);
            layer.ContentBounds = PixelRect.Empty;
        }
    }

    private void CopyTrack(Layer source, Layer copy)
    {
        if (!source.IsTrack)
        {
            copy.Image = source.HasPixels ? source.Image.Clone() : Placeholder();
            copy.ContentBounds = source.ContentBounds;
            return;
        }
        var map = new Dictionary<Cel, Cel>();
        copy.Cells = source.Cells!.Select(c => c is null ? null : map.TryGetValue(c, out var m) ? m : map[c] = c.CloneCel()).ToList();
        var current = copy.Cells[CurrentFrame];
        copy.CurrentCel = current;
        copy.Image = current?.Image ?? copy.BlankFor(Width, Height);
        copy.ContentBounds = current?.ContentBounds ?? PixelRect.Empty;
    }

    /// <summary>Merge down for animations: frame by frame, reusing one merged drawing wherever the same pair of drawings
    /// is shown (holds stay holds).</summary>
    private bool MergeDownAnimated(Layer layer, Layer below, int index)
    {
        if (below.Locked) return false;
        var still = layer.IsTrack ? null : new Cel(layer.Image, layer.ContentBounds); // text or a shape: every frame
        var start = (Before: CaptureStructure(), Conversion: (IHistoryAction?)null);
        var merged = new Dictionary<(Cel?, Cel?), Cel?>();
        var newCells = new List<Cel?>();
        for (int f = 0; f < FrameCount; f++)
        {
            var key = (below.Cells![f], still ?? layer.Cells![f]);
            if (!merged.TryGetValue(key, out var result))
            {
                var (lo, up) = key;
                if (up is null || !layer.Visible || up.IsEmpty) result = lo;
                else
                {
                    var img = lo?.Image.Clone() ?? new RasterImage(Width, Height);
                    MergePixels(layer, up.Image, up.ContentBounds, img, layer.ClipToBelow);
                    ConformToPalette(img, up.ContentBounds);
                    result = new Cel(img, (lo?.ContentBounds ?? PixelRect.Empty).Union(up.ContentBounds));
                }
                merged[key] = result;
            }
            newCells.Add(result);
        }
        below.Cells = newCells;
        RemoveLayerInternal(layer);
        _active = below;
        var structure = new AnimationStructureAction("Merge down", start.Before, CaptureStructure());
        ShowFrameInternal(CurrentFrame);
        // Undo runs in reverse: the merged cells are restored first, then the upper layer comes back.
        Commit(new CompositeAction("Merge down", [new RemoveLayerAction("Merge down", layer, index), structure]));
        ActiveLayerChanged?.Invoke();
        AnimationChanged?.Invoke();
        return true;
    }

    private void MergePixels(Layer layer, RasterImage src, PixelRect area, RasterImage dstImage, bool clip)
    {
        int factor = (int)Math.Round(layer.Opacity * 255);
        var s = src.Pixels;
        var d = dstImage.Pixels;
        var mask = layer.HasMask && layer.MaskEnabled ? layer.Mask!.Pixels : null;
        area = area.Intersect(Bounds);
        for (int y = area.Y; y < area.Bottom; y++)
        {
            int row = y * Width;
            for (int x = area.X; x < area.Right; x++)
            {
                uint p = Blending.Fade(s[row + x], factor);
                if (mask is not null) p = Blending.Fade(p, Layer.MaskValue(mask[row + x]));
                if (clip) p = Blending.Fade(p, Pixel.A(d[row + x]));
                if (p == 0) continue;
                uint q = d[row + x];
                uint r = Blending.Blend(p, q, layer.Blend);
                d[row + x] = layer.Blend == BlendMode.Normal || q != 0 ? r : p;
            }
        }
    }
}
