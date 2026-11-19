using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Operations;

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
        if (IsFinished || !transform.IsInvertible) return;
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
        if (_selectionBefore is not null)
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
