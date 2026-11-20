using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

/// <summary>How a free transform reshapes the pixels (Mission 010 adds Perspective and Warp).</summary>
public enum TransformMode
{
    /// <summary>Move, scale, rotate, flip (an affine transform).</summary>
    Free,

    /// <summary>Drag the four corners anywhere (a perspective view of the picture).</summary>
    Perspective,

    /// <summary>Drag the points of a 4 × 4 grid: the picture bends smoothly with them.</summary>
    Warp,
}

/// <summary>
/// Moves, scales, rotates or flips the selected pixels (or the whole layer). The pixels are lifted off the layer and
/// float above it while the transform changes (the screen shows them in place, in the layer's stacking order);
/// <see cref="Commit"/> drops them back with the chosen interpolation as one undo step, and the selection follows.
/// </summary>
public sealed class FloatingTransform
{
    private readonly Document _doc;
    private readonly LayerEdit _edit;
    private readonly SelectionMask? _selectionBefore;

    private FloatingTransform(Document doc, Layer layer, LayerEdit edit, RasterImage source, PixelRect bounds, SelectionMask? selection)
    {
        _doc = doc;
        Layer = layer;
        _edit = edit;
        Source = source;
        SourceBounds = bounds;
        _selectionBefore = selection;
        Transform = Affine.Translation(bounds.X, bounds.Y);
    }

    public Layer Layer { get; }

    /// <summary>The lifted pixels (cropped to <see cref="SourceBounds"/>).</summary>
    public RasterImage Source { get; }

    /// <summary>Where the pixels came from, in document coordinates.</summary>
    public PixelRect SourceBounds { get; }

    /// <summary>Source-local → document transform. Starts as a translation to <see cref="SourceBounds"/>.</summary>
    public Affine Transform { get; private set; }

    public Interpolation Interpolation { get; set; } = Interpolation.Bilinear;

    // ------------------------------------------------------------------ Mission 010: perspective and warp

    public TransformMode Mode { get; private set; } = TransformMode.Free;

    /// <summary>Perspective: where the corners go (top left, top right, bottom right, bottom left), in document coordinates.</summary>
    public (double X, double Y)[] Corners { get; private set; } = [];

    /// <summary>Warp: the grid the picture follows.</summary>
    public WarpGrid? Warp { get; private set; }

    private (double X, double Y)[] _cornersStart = [];
    private WarpGrid? _warpStart;
    private bool _draft, _drawnAsDraft;

    /// <summary>While a Perspective or Warp handle is dragged: quick draft previews (nearest pixels, one sample per block);
    /// switching it off draws the real preview again.</summary>
    public bool Draft
    {
        get => _draft;
        set
        {
            if (_draft == value) return;
            _draft = value;
            if (!value && _drawnAsDraft && !IsFinished) Render();
        }
    }

    /// <summary>Where the picture's own point (u, v) is now, in document coordinates (for drawing frames and grids).</summary>
    public (double X, double Y) Map(double u, double v) => Mode switch
    {
        TransformMode.Perspective when Homography.FromRect(Source.Width, Source.Height, Corners) is { } h => h.Apply(u, v),
        TransformMode.Warp when Warp is { } g => g.Map(u, v),
        _ => Transform.Apply(u, v),
    };

    /// <summary>Switches between Free, Perspective and Warp. Perspective and Warp start from the current placement;
    /// going back to Free leaves the picture as the free transform had it.</summary>
    public void SetMode(TransformMode mode)
    {
        if (IsFinished || mode == Mode) return;
        int w = Source.Width, h = Source.Height;
        switch (mode)
        {
            case TransformMode.Perspective:
                Corners = Mode == TransformMode.Warp && Warp is { } g
                    ? [g.Map(0, 0), g.Map(w, 0), g.Map(w, h), g.Map(0, h)]
                    : [Transform.Apply(0, 0), Transform.Apply(w, 0), Transform.Apply(w, h), Transform.Apply(0, h)];
                if (!Homography.IsUsable(Corners)) Corners = [Transform.Apply(0, 0), Transform.Apply(w, 0), Transform.Apply(w, h), Transform.Apply(0, h)];
                _cornersStart = Corners;
                break;
            case TransformMode.Warp:
                var before = Mode;
                Mode = mode; // Map() below still uses the previous placement through the captured values
                Warp = WarpGrid.Over(w, h, before == TransformMode.Perspective && Homography.FromRect(w, h, Corners) is { } hp ? hp.Apply : Transform.Apply);
                _warpStart = Warp;
                break;
        }
        Mode = mode;
        Render();
    }

    /// <summary>Perspective: moves the corners. Returns false (and keeps the old ones) for crossed or flat corners.</summary>
    public bool SetCorners((double X, double Y)[] corners)
    {
        if (IsFinished || Mode != TransformMode.Perspective || !Homography.IsUsable(corners)) return false;
        Corners = ((double, double)[])corners.Clone();
        Render();
        return true;
    }

    /// <summary>Warp: moves one grid point (0–15, row by row).</summary>
    public void SetWarpPoint(int index, double x, double y)
    {
        if (IsFinished || Mode != TransformMode.Warp || Warp is null || (uint)index >= WarpGrid.N * WarpGrid.N) return;
        Warp = Warp.With(index, x, y);
        Render();
    }

    /// <summary>Warp: replaces the whole grid (moving every point).</summary>
    public void SetWarp(WarpGrid grid)
    {
        if (IsFinished || Mode != TransformMode.Warp) return;
        Warp = grid;
        Render();
    }

    /// <summary>Moves the whole picture in Perspective or Warp mode.</summary>
    public void MoveBy(double dx, double dy)
    {
        if (IsFinished) return;
        switch (Mode)
        {
            case TransformMode.Perspective:
                Corners = Corners.Select(c => (c.X + dx, c.Y + dy)).ToArray();
                Render();
                break;
            case TransformMode.Warp when Warp is not null:
                Warp = Warp.Moved(dx, dy);
                Render();
                break;
            default:
                SetTransform(Transform.Then(Affine.Translation(dx, dy)));
                break;
        }
    }

    /// <summary>Back to how Perspective or Warp started (the corners or the even grid).</summary>
    public void ResetMode()
    {
        if (IsFinished) return;
        if (Mode == TransformMode.Perspective) Corners = _cornersStart;
        else if (Mode == TransformMode.Warp) Warp = _warpStart;
        Render();
    }

    /// <summary>Draws the preview for the current mode.</summary>
    private void Render()
    {
        if (Mode == TransformMode.Free)
        {
            SetTransform(Transform);
            return;
        }
        int step = 1;
        var mode = Interpolation;
        if (_draft)
        {
            var (l, t, r, b) = Transform.TransformBounds(0, 0, Source.Width, Source.Height);
            step = WarpRendering.DraftStep(PixelRect.FromBounds(l, t, r, b).Union(SourceBounds));
            if (step > 1) mode = Interpolation.Nearest;
        }
        _drawnAsDraft = step > 1;
        var (image, at) = Mode == TransformMode.Perspective && Homography.FromRect(Source.Width, Source.Height, Corners) is { } h
            ? WarpRendering.Perspective(Source, h, _doc.Bounds, mode, step)
            : WarpRendering.Warp(Source, Warp!, _doc.Bounds, mode, step);
        _doc.PaletteConformer?.Conform(image, image.Bounds); // a locked picture previews what Apply will give
        _doc.SetFloating(Layer, image, at.X, at.Y);
    }

    /// <summary>The selection carried along by Perspective or Warp (its coverage drawn through the same placement).</summary>
    private SelectionMask? MappedSelection(SelectionMask before)
    {
        var crop = new RasterImage(SourceBounds.Width, SourceBounds.Height);
        for (int y = 0; y < crop.Height; y++)
            for (int x = 0; x < crop.Width; x++)
            {
                byte v = before[SourceBounds.X + x, SourceBounds.Y + y];
                crop.Pixels[y * crop.Width + x] = Pixel.Pack(v, v, v, v);
            }
        var (image, at) = Mode == TransformMode.Perspective && Homography.FromRect(crop.Width, crop.Height, Corners) is { } h
            ? WarpRendering.Perspective(crop, h, _doc.Bounds, Interpolation == Interpolation.Nearest ? Interpolation.Nearest : Interpolation.Bilinear)
            : WarpRendering.Warp(crop, Warp!, _doc.Bounds, Interpolation == Interpolation.Nearest ? Interpolation.Nearest : Interpolation.Bilinear);
        var moved = new SelectionMask(_doc.Width, _doc.Height);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int dx = at.X + x, dy = at.Y + y;
                if ((uint)dx < (uint)_doc.Width && (uint)dy < (uint)_doc.Height) moved.Data[dy * _doc.Width + dx] = Pixel.A(image.Pixels[y * image.Width + x]);
            }
        moved.UpdateBounds();
        return moved.IsEmpty ? null : moved;
    }

    public bool IsFinished { get; private set; }

    /// <summary>Lifts the selected pixels (or all pixels) of a layer. Returns null if there is nothing to move or the layer is locked.</summary>
    public static FloatingTransform? Begin(Document doc, Layer layer, string historyName = "Transform")
    {
        if (layer.Locked) return null;
        var selection = doc.Selection;
        var bounds = layer.ContentBounds.Intersect(doc.Bounds);
        if (selection is not null) bounds = bounds.Intersect(selection.Bounds);
        bounds = layer.Image.ContentBounds(bounds);
        if (bounds.IsEmpty) return null;

        var edit = doc.BeginEdit(layer, historyName);
        edit.Capture(bounds);
        var source = layer.Image.Crop(bounds);
        var px = layer.Image.Pixels;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                int s = selection is null ? 255 : selection[x, y];
                int i = y * doc.Width + x, j = (y - bounds.Y) * bounds.Width + (x - bounds.X);
                source.Pixels[j] = Blending.Fade(source.Pixels[j], s);
                px[i] = Blending.Fade(px[i], 255 - s);
            }
        edit.Changed(bounds);
        var ft = new FloatingTransform(doc, layer, edit, source, bounds, selection?.Clone());
        doc.SetFloating(layer, source, bounds.X, bounds.Y);
        return ft;
    }

    /// <summary>Sets the transform (source-local → document) and updates the preview.</summary>
    public void SetTransform(Affine transform)
    {
        if (IsFinished || !transform.IsInvertible || Mode != TransformMode.Free) return;
        Transform = transform;
        if (transform.IsIntegerTranslation)
        {
            _doc.SetFloating(Layer, Source, (int)transform.Dx, (int)transform.Dy);
            return;
        }
        var target = Resampler.TransformedBounds(transform, Source.Width, Source.Height).Intersect(_doc.Bounds);
        if (target.IsEmpty)
        {
            _doc.SetFloating(Layer, new RasterImage(1, 1), 0, 0);
            return;
        }
        var rendered = Resampler.Render(Source, transform, target, Interpolation);
        _doc.SetFloating(Layer, rendered, target.X, target.Y);
    }

    /// <summary>Moves by whole pixels (pixel-exact).</summary>
    public void MoveTo(int dx, int dy) => SetTransform(Affine.Translation(SourceBounds.X + dx, SourceBounds.Y + dy));

    /// <summary>Drops the pixels at their new place and records one undo step. Returns false if nothing changed.</summary>
    public bool Commit()
    {
        if (IsFinished) return false;
        Draft = false; // apply the real thing, not a draft
        IsFinished = true;
        var floating = _doc.Floating;
        _doc.SetFloating(Layer, null, 0, 0);
        if (floating is not null)
        {
            var area = floating.Bounds.Intersect(_doc.Bounds);
            _edit.Capture(area);
            var px = Layer.Image.Pixels;
            for (int y = area.Y; y < area.Bottom; y++)
                for (int x = area.X; x < area.Right; x++)
                {
                    uint f = floating.Image.GetOrTransparent(x - floating.X, y - floating.Y);
                    if (f == 0) continue;
                    int i = y * _doc.Width + x;
                    px[i] = Pixel.Over(f, px[i]);
                }
            _edit.Changed(area);
        }
        var pixels = _edit.BuildAction();
        var actions = new List<IHistoryAction>();
        if (pixels is not null) actions.Add(pixels);
        if (_selectionBefore is not null && Mode != TransformMode.Free)
        {
            var after = MappedSelection(_selectionBefore);
            _doc.SetSelectionInternal(after);
            actions.Add(new SelectionAction("Transform", _selectionBefore, after));
        }
        else if (_selectionBefore is not null)
        {
            var moved = new SelectionMask(_doc.Width, _doc.Height);
            var start = Affine.Translation(SourceBounds.X, SourceBounds.Y).Invert().Then(Transform);
            var data = Resampler.RenderMask(_selectionBefore.Data, _doc.Width, _doc.Height, start, smooth: Interpolation != Interpolation.Nearest);
            Array.Copy(data, moved.Data, data.Length);
            moved.UpdateBounds();
            var after = moved.IsEmpty ? null : moved;
            _doc.SetSelectionInternal(after);
            actions.Add(new SelectionAction("Transform", _selectionBefore, after));
        }
        if (actions.Count == 0) return false;
        _doc.Commit(actions.Count == 1 ? actions[0] : new CompositeAction(_edit.Name, actions));
        return true;
    }

    /// <summary>Puts the pixels back where they were.</summary>
    public void Cancel()
    {
        if (IsFinished) return;
        IsFinished = true;
        _doc.SetFloating(Layer, null, 0, 0);
        _edit.Cancel();
    }

    // ------------------------------------------------------------------ helpers for common transforms

    /// <summary>The centre of the floating pixels in source-local coordinates.</summary>
    public (double X, double Y) LocalCenter => (Source.Width / 2.0, Source.Height / 2.0);

    /// <summary>Flips the current transform around the pixels' own centre.</summary>
    public void Flip(bool horizontal)
    {
        var (cx, cy) = LocalCenter;
        var flip = Affine.Translation(-cx, -cy).Then(Affine.Scale(horizontal ? -1 : 1, horizontal ? 1 : -1)).Then(Affine.Translation(cx, cy));
        SetTransform(flip.Then(Transform));
    }

    /// <summary>Rotates the current transform around the pixels' own centre.</summary>
    public void Rotate(double degrees)
    {
        var (cx, cy) = LocalCenter;
        var rot = Affine.Translation(-cx, -cy).Then(Affine.Rotation(degrees)).Then(Affine.Translation(cx, cy));
        SetTransform(rot.Then(Transform));
    }
}
