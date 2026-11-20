using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// Palette Lock: the whole picture (every layer, every drawing of an animation) may only contain the colours of the
/// document's palette. Transparency is independent: the lock never changes a pixel's alpha.
/// <para>
/// The palette is a snapshot stored in the document, so editing a palette in the library never recolours a picture.
/// Every change of palette or lock is an undo step. While locked, every pixel edit is conformed to the palette as part
/// of the same step (<see cref="LayerEdit.Changed"/>), and so are new layers, pasted drawings and whole-picture
/// operations.
/// </para>
/// </summary>
public sealed partial class Document
{
    private PaletteConformer? _conformer;

    /// <summary>True when the picture is locked to <see cref="Palette"/>.</summary>
    public bool PaletteLocked { get; private set; }

    /// <summary>The conformer for the locked palette (null when not locked).</summary>
    public PaletteConformer? PaletteConformer => PaletteLocked ? _conformer : null;

    /// <summary>Raised when the palette or the lock changed (including by undo and redo).</summary>
    public event Action? PaletteChanged;

    internal void SetPaletteInternal(Palette palette, bool locked)
    {
        Palette = palette.Clone();
        PaletteLocked = locked && palette.Colors.Count > 0;
        _conformer = PaletteLocked ? new PaletteConformer(Palette.Colors) : null;
        PaletteChanged?.Invoke();
    }

    /// <summary>Conforms pixels changed while locked (no-op otherwise). Called by edits before they are recorded.</summary>
    internal void ConformToPalette(RasterImage image, PixelRect area)
    {
        if (PaletteConformer is { } c) c.Conform(image, area);
    }

    /// <summary>How the picture's colours relate to a palette.</summary>
    public PaletteUse CheckPalette(IReadOnlyList<ColorRgb> colors, bool includeHidden = true)
    {
        var conformer = new PaletteConformer(colors);
        long outside = 0;
        var outsideColours = new HashSet<int>();
        foreach (var (_, image) in AllImages(includeHidden))
        {
            var area = image.ContentBounds();
            if (area.IsEmpty) continue;
            var px = image.Pixels;
            for (int y = area.Y; y < area.Bottom; y++)
                for (int i = y * Width + area.X, end = i + area.Width; i < end; i++)
                {
                    uint p = px[i];
                    if (p == 0 || conformer.IsPaletteColour(p)) continue;
                    outside++;
                    if (outsideColours.Count < 100_000)
                    {
                        var (r, g, b, _) = Pixel.ToStraight(p);
                        outsideColours.Add(r << 16 | g << 8 | b);
                    }
                }
        }
        return new PaletteUse(outside, outsideColours.Count);
    }

    /// <summary>Every image of the picture with its layer: each layer of a still picture, or each distinct drawing of an
    /// animation (a held drawing is listed once).</summary>
    public IEnumerable<(Layer Layer, RasterImage Image)> AllImages(bool includeHidden = true)
    {
        foreach (var layer in _layers)
        {
            if (!includeHidden && !layer.Visible) continue;
            if (_animation is null) yield return (layer, layer.Image);
            else
                foreach (var cel in layer.Cells!.OfType<Cel>().Distinct())
                    yield return (layer, cel.Image);
        }
    }

    /// <summary>
    /// The whole-picture state after mapping images; <paramref name="map"/> returns the same object to leave an image
    /// unchanged (for example a hidden layer that isn't included).
    /// </summary>
    public DocumentState MapImages(Func<Layer, RasterImage, RasterImage> map)
    {
        if (_animation is null)
            return new(Width, Height, _layers.ToDictionary(l => l.Id, l => map(l, l.Image)), Selection);
        var cels = new Dictionary<Cel, RasterImage>();
        foreach (var layer in _layers)
            foreach (var cel in layer.Cells!.OfType<Cel>().Distinct())
                if (!cels.ContainsKey(cel)) cels[cel] = map(layer, cel.Image);
        return new(Width, Height, new Dictionary<int, RasterImage>(), Selection, cels);
    }

    /// <summary>Locks the picture to a palette as one undo step. Pixels are conformed (nearest palette colour, same
    /// alpha); call <see cref="CheckPalette"/> first and offer a remap when colours would visibly change.</summary>
    public void LockToPalette(Palette palette, string historyName = "Lock to palette")
    {
        var conformer = new PaletteConformer(palette.Colors);
        bool any = false;
        var state = MapImages((_, img) =>
        {
            var copy = img.Clone();
            if (conformer.Conform(copy, copy.Bounds) == 0) return img;
            any = true;
            return copy;
        });
        ChangePalette(palette, locked: true, any ? state : null, historyName);
    }

    /// <summary>Removes the lock (the palette stays with the picture) as one undo step.</summary>
    public void UnlockPalette()
    {
        if (!PaletteLocked) return;
        ChangePalette(Palette, locked: false, null, "Unlock palette");
    }

    /// <summary>Adds a colour to the document's palette (one undo step). Existing artwork is not changed.</summary>
    public bool AddPaletteColour(ColorRgb color)
    {
        if (Palette.Colors.Contains(color)) return false;
        var next = Palette.Clone();
        next.Colors.Add(color);
        ChangePalette(next, PaletteLocked, null, "Add palette colour");
        return true;
    }

    /// <summary>Changes palette and lock, optionally with new pixels (a remap), as a single undo step.</summary>
    public void ChangePalette(Palette palette, bool locked, DocumentState? pixels, string historyName)
    {
        var actions = new List<IHistoryAction>();
        if (pixels is not null)
        {
            var before = CaptureState();
            ApplyStateInternal(pixels);
            actions.Add(new DocumentStateAction(historyName, before, pixels));
        }
        var change = new PaletteAction(historyName, Palette.Clone(), PaletteLocked, palette.Clone(), locked);
        change.Redo(this);
        actions.Add(change);
        Commit(actions.Count == 1 ? actions[0] : new CompositeAction(historyName, actions));
    }
}

/// <summary>How much of a picture lies outside a palette.</summary>
public readonly record struct PaletteUse(long PixelsOutside, int ColoursOutside)
{
    public bool OnlyPaletteColours => PixelsOutside == 0;
}

/// <summary>Palette and lock before and after (undo/redo).</summary>
public sealed class PaletteAction(string name, Palette before, bool beforeLocked, Palette after, bool afterLocked) : IHistoryAction
{
    public string Name { get; } = name;
    public long ByteSize => 64 + 4L * (before.Colors.Count + after.Colors.Count);
    public void Undo(Document d) => d.SetPaletteInternal(before, beforeLocked);
    public void Redo(Document d) => d.SetPaletteInternal(after, afterLocked);
}
