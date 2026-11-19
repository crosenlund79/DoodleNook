using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Editor;

public enum ToolKind
{
    Brush,
    Eraser,
    Eyedropper,
    Hand,
    Fill,
    Gradient,
    Shape,
    RectSelect,
    EllipseSelect,
    Lasso,
    MagicWand,
    Move,
    Crop,
    PixelPencil,
    PixelEraser,
    PixelLine,
    PixelRect,
    PixelEllipse,
}

public enum PressureFeel
{
    Soft,
    Normal,
    Firm,
}

/// <summary>A named starting point for the brush engine.</summary>
public sealed record BrushPreset(string Id, string Name, string Description, BrushSettings Settings);

/// <summary>Options of the selection tools.</summary>
public sealed record SelectionToolOptions(SelectionMode Mode = SelectionMode.Replace, bool AntiAlias = true, int WandTolerance = 32, bool WandContiguous = true, bool SampleMerged = false);

/// <summary>Options of the pixel-art tools.</summary>
public sealed record PixelToolOptions(int Size = 1, bool PixelPerfect = true, Symmetry Symmetry = Symmetry.None, bool Grid = true, bool Tiled = false, bool Preview = true, bool FilledShapes = false);

/// <summary>
/// The editing state behind the window: the open document, tools and their options, brushes, colours, the clipboard
/// and an in-progress transform. It has no UI-framework dependencies, so it can be driven by tests, the smoke test
/// or the real window alike. All five workspaces share one session (and so one document and one undo history).
/// </summary>
public sealed class EditorSession
{
    public static readonly IReadOnlyList<BrushPreset> Presets =
    [
        new("pencil", "Sketch pencil", "Grainy graphite: light pressure lets the paper show",
            new BrushSettings { Size = 6, Hardness = 0.85, Opacity = 0.9, PressureSize = true, PressureOpacity = true, Grain = 0.55 }),
        new("ink", "Ink pen", "Crisp ink with a strong pressure taper",
            new BrushSettings { Size = 9, Hardness = 1, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.02 }),
        new("liner", "Fine liner", "Even, constant line",
            new BrushSettings { Size = 3, Hardness = 1, Opacity = 1, PressureSize = false }),
        new("hard", "Hard round", "Solid colour blocking",
            new BrushSettings { Size = 30, Hardness = 0.98, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.25 }),
        new("soft", "Soft round", "Soft shading and glows",
            new BrushSettings { Size = 70, Hardness = 0.05, Opacity = 0.6, PressureSize = true, PressureOpacity = true }),
        new("marker", "Marker", "Flat chisel tip: thick one way, thin the other",
            new BrushSettings { Size = 26, Hardness = 0.9, Opacity = 0.55, PressureSize = false, Roundness = 0.3, Angle = 35 }),
        new("air", "Airbrush", "Builds up while you hold it, even standing still",
            new BrushSettings { Size = 120, Hardness = 0, Opacity = 1, PressureSize = false, PressureOpacity = true, BuildUp = true, Flow = 0.04, Spray = true }),
        new("chalk", "Chalk", "Rough, textured strokes on grainy paper",
            new BrushSettings { Size = 32, Hardness = 0.55, Opacity = 0.9, PressureSize = true, PressureOpacity = true, Grain = 0.9, GrainScale = 1.6 }),
        new("wash", "Flat wash", "Even layers of translucent colour",
            new BrushSettings { Size = 48, Hardness = 0.95, Opacity = 0.7, PressureSize = false, PressureOpacity = true }),
    ];

    public static readonly ColorRgb DefaultForeground = ColorRgb.Parse("#3B2F2A");
    public static readonly ColorRgb DefaultBackground = ColorRgb.White;
    public static readonly ColorRgb[] DefaultRecent =
        new[] { "#3B2F2A", "#5F8FA8", "#C96F4A", "#F2C14E", "#7AA66A", "#E88A9A", "#8A6FB0", "#F8F4EC" }.Select(ColorRgb.Parse).ToArray();

    public const int DefaultWidth = 1920;
    public const int DefaultHeight = 1080;

    private StrokeEngine _engine;
    private PixelStroke _pixel;
    private FloatingTransform? _transform;

    public EditorSession()
        : this(new Document(DefaultWidth, DefaultHeight, ColorRgb.White, "Sketch"))
    {
    }

    public EditorSession(Document document)
    {
        Document = document;
        _engine = new StrokeEngine(document);
        _pixel = new PixelStroke(document);
        BrushPresetId = "pencil";
        Brush = Presets[0].Settings;
    }

    public Document Document { get; private set; }

    /// <summary>Where the document was opened from or last saved to (null for a new, unsaved picture).</summary>
    public string? FilePath { get; private set; }

    public string DisplayName => FilePath is null ? "Untitled" : Path.GetFileName(FilePath);

    /// <summary>True when the file on disk is a layered DoodleNook project (.dnk).</summary>
    public bool IsProjectFile => FilePath?.EndsWith(DoodleNook.Engine.Files.DnkFile.Extension, StringComparison.OrdinalIgnoreCase) == true;

    public ToolKind Tool { get; private set; } = ToolKind.Brush;

    /// <summary>The brush or eraser: the tool whose size the toolbar shows (the last painting tool used).</summary>
    public ToolKind PaintTool { get; private set; } = ToolKind.Brush;

    /// <summary>Pixel Art workspace rules: exact colours, no anti-aliasing, nearest-neighbour transforms.</summary>
    public bool PixelMode { get; private set; }

    public BrushSettings Brush { get; private set; }
    public BrushSettings Eraser { get; private set; } = new() { Size = 30, Hardness = 0.9, Opacity = 1, PressureSize = true, PressureOpacity = false };
    public string BrushPresetId { get; private set; }
    public PressureFeel PressureFeel { get; private set; } = PressureFeel.Normal;
    public Symmetry Symmetry { get; private set; } = Symmetry.None;

    public FillOptions Fill { get; private set; } = new();
    public GradientOptions Gradient { get; private set; } = new();
    public ShapeOptions Shape { get; private set; } = new(ShapeKind.Line, Width: 6);
    public SelectionToolOptions Selection { get; private set; } = new();
    public PixelToolOptions Pixel { get; private set; } = new();
    public Interpolation TransformInterpolation { get; private set; } = Interpolation.Bilinear;

    public PressureCurve Curve => PressureFeel switch
    {
        PressureFeel.Soft => PressureCurve.Soft,
        PressureFeel.Firm => PressureCurve.Firm,
        _ => PressureCurve.Normal,
    };

    public ColorRgb Foreground { get; private set; } = DefaultForeground;
    public ColorRgb Background { get; private set; } = DefaultBackground;
    public List<ColorRgb> RecentColors { get; } = [.. DefaultRecent];

    /// <summary>The user's saved swatches (kept between sessions).</summary>
    public List<ColorRgb> Swatches { get; } = [.. DefaultRecent];

    public bool IsStrokeActive => _engine.IsActive || _pixel.IsActive;
    public StrokeEngine Engine => _engine;

    /// <summary>Settings of the painting tool currently shown in the toolbar.</summary>
    public BrushSettings CurrentPaintSettings => PaintTool == ToolKind.Eraser ? Eraser : Brush;

    /// <summary>A free transform in progress (Ctrl+T), or null.</summary>
    public FloatingTransform? ActiveTransform => _transform is { IsFinished: false } ? _transform : null;

    /// <summary>Pixels copied with Copy/Cut (document-sized, transparent around the copied part).</summary>
    public RasterImage? Clipboard { get; private set; }

    public event Action? StateChanged;
    public event Action? DocumentReplaced;
    public event Action<string>? Message;

    private void Changed() => StateChanged?.Invoke();

    private void Tell(string message) => Message?.Invoke(message);

    // ================================================================ documents

    public void ReplaceDocument(Document document, string? filePath)
    {
        EndStroke();
        CommitTransform();
        Document = document;
        FilePath = filePath;
        _engine = new StrokeEngine(document);
        _pixel = new PixelStroke(document);
        DocumentReplaced?.Invoke();
        Changed();
    }

    public void MarkSaved(string filePath)
    {
        FilePath = filePath;
        Document.MarkSaved();
        Changed();
    }

    // ================================================================ tools and settings

    public void SetPixelMode(bool on)
    {
        if (PixelMode == on) return;
        PixelMode = on;
        Changed();
    }

    public void SelectTool(ToolKind tool)
    {
        EndStroke();
        if (tool != ToolKind.Move) CommitTransform();
        Tool = tool;
        if (tool is ToolKind.Brush or ToolKind.Eraser) PaintTool = tool;
        Changed();
    }

    /// <summary>The "Erase" toggle: switches between brush and eraser.</summary>
    public void ToggleEraser() => SelectTool(Tool == ToolKind.Eraser ? ToolKind.Brush : ToolKind.Eraser);

    public bool IsPixelTool(ToolKind t) => t is ToolKind.PixelPencil or ToolKind.PixelEraser or ToolKind.PixelLine or ToolKind.PixelRect or ToolKind.PixelEllipse;

    /// <summary>Size shown in the toolbar: brush/eraser diameter, or the pixel brush size in Pixel Art.</summary>
    public double CurrentSize => IsPixelTool(Tool) ? Pixel.Size : CurrentPaintSettings.Size;

    public void SetSize(double size)
    {
        if (IsPixelTool(Tool))
        {
            SetPixelOptions(Pixel with { Size = (int)Math.Clamp(Math.Round(size), 1, 64) });
            return;
        }
        UpdatePaintSettings(s => s with { Size = Math.Round(Math.Clamp(size, BrushSettings.MinSize, BrushSettings.MaxSize), size < 10 ? 1 : 0) });
    }

    /// <summary>The [ and ] keys: about 15 % per step, at least 1 px.</summary>
    public void StepSize(int direction)
    {
        double s = CurrentSize;
        double next = direction > 0 ? Math.Max(s + 1, s * 1.15) : Math.Min(s - 1, s / 1.15);
        SetSize(Math.Round(next));
    }

    public void SetOpacity(double opacity) => UpdatePaintSettings(s => s with { Opacity = Math.Clamp(opacity, 0.01, 1) });
    public void SetHardness(double hardness) => UpdatePaintSettings(s => s with { Hardness = Math.Clamp(hardness, 0, 1) });
    public void SetSmoothing(double smoothing) => UpdatePaintSettings(s => s with { Smoothing = Math.Clamp(smoothing, 0, 1) });
    public void SetPressureSize(bool on) => UpdatePaintSettings(s => s with { PressureSize = on });
    public void SetPressureOpacity(bool on) => UpdatePaintSettings(s => s with { PressureOpacity = on });
    public void SetGrain(double grain) => UpdatePaintSettings(s => s with { Grain = Math.Clamp(grain, 0, 1) });
    public void SetFlow(double flow) => UpdatePaintSettings(s => s with { Flow = Math.Clamp(flow, 0.005, 1) });

    public void SetPressureFeel(PressureFeel feel)
    {
        PressureFeel = feel;
        Changed();
    }

    public void SetSymmetry(Symmetry symmetry)
    {
        Symmetry = symmetry;
        if (PixelMode) Pixel = Pixel with { Symmetry = symmetry };
        Changed();
    }

    public void SetFillOptions(FillOptions o) { Fill = o; Changed(); }
    public void SetGradientOptions(GradientOptions o) { Gradient = o; Changed(); }
    public void SetShapeOptions(ShapeOptions o) { Shape = o; Changed(); }
    public void SetSelectionOptions(SelectionToolOptions o) { Selection = o; Changed(); }
    public void SetPixelOptions(PixelToolOptions o) { Pixel = o; Changed(); }

    public void SetTransformInterpolation(Interpolation i)
    {
        TransformInterpolation = i;
        if (ActiveTransform is { } t)
        {
            t.Interpolation = i;
            t.SetTransform(t.Transform);
        }
        Changed();
    }

    public void ApplyPreset(string id)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null) return;
        BrushPresetId = id;
        Brush = preset.Settings with { Smoothing = Brush.Smoothing };
        if (Tool != ToolKind.Brush) SelectTool(ToolKind.Brush);
        else Changed();
    }

    /// <summary>Restores settings saved from an earlier session.</summary>
    public void RestoreSettings(BrushSettings brush, BrushSettings eraser, string presetId, PressureFeel feel, ColorRgb fg, ColorRgb bg,
        IEnumerable<ColorRgb>? recent, IEnumerable<ColorRgb>? swatches)
    {
        Brush = brush.Normalized();
        Eraser = eraser.Normalized();
        BrushPresetId = Presets.Any(p => p.Id == presetId) ? presetId : Presets[0].Id;
        PressureFeel = feel;
        Foreground = fg;
        Background = bg;
        Replace(RecentColors, recent);
        Replace(Swatches, swatches);
        Changed();

        static void Replace(List<ColorRgb> target, IEnumerable<ColorRgb>? source)
        {
            var list = source?.Take(48).ToList();
            if (list is not { Count: > 0 }) return;
            target.Clear();
            target.AddRange(list);
        }
    }

    private void UpdatePaintSettings(Func<BrushSettings, BrushSettings> change)
    {
        if (PaintTool == ToolKind.Eraser) Eraser = change(Eraser).Normalized();
        else Brush = change(Brush).Normalized();
        Changed();
    }

    // ================================================================ colours

    public void SetForeground(ColorRgb color)
    {
        if (color == Foreground) return;
        Foreground = color;
        Changed();
    }

    public void SetBackground(ColorRgb color)
    {
        if (color == Background) return;
        Background = color;
        Changed();
    }

    public void SwapColors()
    {
        (Foreground, Background) = (Background, Foreground);
        Changed();
    }

    public void ResetColors()
    {
        Foreground = DefaultForeground;
        Background = DefaultBackground;
        Changed();
    }

    public void AddSwatch(ColorRgb c)
    {
        Swatches.Remove(c);
        Swatches.Add(c);
        Changed();
    }

    public void RemoveSwatch(ColorRgb c)
    {
        if (Swatches.Remove(c)) Changed();
    }

    /// <summary>Picks the visible colour at a document position (or the active layer's, with <paramref name="layerOnly"/>).
    /// Returns false over empty, transparent areas.</summary>
    public bool PickColor(double x, double y, bool layerOnly = false, bool toBackground = false)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (!Document.Bounds.Contains(ix, iy)) return false;
        uint p = layerOnly ? Document.ActiveLayer.Image[ix, iy] : Document.CompositeAt(ix, iy);
        var (r, g, b, a) = DoodleNook.Engine.Imaging.Pixel.ToStraight(p);
        if (a == 0) return false;
        if (toBackground) SetBackground(new ColorRgb(r, g, b));
        else SetForeground(new ColorRgb(r, g, b));
        return true;
    }

    private void RememberColor(ColorRgb c)
    {
        int i = RecentColors.IndexOf(c);
        if (i == 0) return;
        if (i > 0) RecentColors.RemoveAt(i);
        RecentColors.Insert(0, c);
        if (RecentColors.Count > 10) RecentColors.RemoveRange(10, RecentColors.Count - 10);
    }

    // ================================================================ strokes

    /// <summary>Checks whether the active layer can be painted on; tells the user why not.</summary>
    public bool CanEditActiveLayer()
    {
        var layer = Document.ActiveLayer;
        if (layer.Locked)
        {
            Tell($"“{layer.Name}” is locked");
            return false;
        }
        if (!layer.Visible)
        {
            Tell($"“{layer.Name}” is hidden — show it to paint on it");
            return false;
        }
        return true;
    }

    /// <summary>Starts a brush stroke with the current painting tool (or the eraser when the pen's eraser end is used).</summary>
    public void BeginStroke(StrokeSample first, bool forceEraser = false)
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return;
        bool erase = forceEraser || Tool == ToolKind.Eraser;
        var settings = erase ? Eraser : Brush;
        if (!erase) RememberColor(Foreground);
        _engine.Begin(new StrokeStyle(settings, Curve, Foreground, erase ? PaintMode.Erase : PaintMode.Paint, Symmetry), first);
    }

    public void ContinueStroke(ReadOnlySpan<StrokeSample> samples) => _engine.Add(samples);

    public void Spray(double ms) => _engine.Spray(ms);

    public void EndStroke()
    {
        if (_engine.IsActive) _engine.End();
        if (_pixel.IsActive) _pixel.End();
    }

    public void CancelStroke()
    {
        _engine.Cancel();
        _pixel.Cancel();
    }

    /// <summary>Starts a pixel-pencil or pixel-eraser stroke at a pixel.</summary>
    public void BeginPixelStroke(int x, int y, bool erase, bool background = false)
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return;
        var color = background ? Background : Foreground;
        if (!erase) RememberColor(color);
        _pixel.Begin(new PixelStrokeStyle(color, erase, Pixel.Size, Pixel.PixelPerfect, Pixel.Symmetry, Pixel.Tiled), x, y);
    }

    public void ContinuePixelStroke(int x, int y) => _pixel.Add(x, y);

    // ================================================================ history

    public bool Undo()
    {
        EndStroke();
        if (CancelTransform()) return true;
        return Document.Undo();
    }

    public bool Redo()
    {
        EndStroke();
        CommitTransform();
        return Document.Redo();
    }

    /// <summary>Undoes or redoes until the history entry with <paramref name="entryId"/> is the latest applied step (0 = the start).</summary>
    public void JumpToHistory(long entryId)
    {
        EndStroke();
        CommitTransform();
        var history = Document.History;
        if (entryId == 0 || history.UndoEntries.Any(e => e.Id == entryId))
        {
            while (history.UndoCount > 0 && history.UndoEntries[^1].Id != entryId) Document.Undo();
        }
        else if (history.RedoEntries.Any(e => e.Id == entryId))
        {
            while (history.CanRedo && (history.UndoCount == 0 || history.UndoEntries[^1].Id != entryId)) Document.Redo();
        }
    }

    // ================================================================ layers

    public Layer AddLayer() => Do(() => Document.AddLayer());

    public void DuplicateLayer() => Do(() => Document.DuplicateLayer(Document.ActiveLayer));

    public void DeleteLayer()
    {
        CommitTransform();
        if (!Document.DeleteLayer(Document.ActiveLayer)) Tell("A picture always keeps at least one layer");
    }

    public void MergeDown()
    {
        CommitTransform();
        if (Document.ActiveIndex == 0) Tell("There is no layer below to merge into");
        else if (!Document.MergeDown(Document.ActiveLayer)) Tell("The layer below is locked");
    }

    public void MoveLayer(int direction)
    {
        CommitTransform();
        Document.MoveLayer(Document.ActiveLayer, Document.ActiveIndex + direction);
    }

    public void SetLayerProperty(Layer layer, Func<LayerProperties, LayerProperties> change, string? name = null)
    {
        CommitTransform();
        Document.SetLayerProperties(layer, change(layer.Properties), name);
    }

    private T Do<T>(Func<T> action)
    {
        EndStroke();
        CommitTransform();
        return action();
    }

    // ================================================================ selection and clipboard

    public void SelectAll() => Document.SetSelection(SelectionMask.All(Document.Width, Document.Height), "Select all");

    public void Deselect()
    {
        CommitTransform();
        Document.SetSelection(null, "Deselect");
    }

    public void InvertSelection()
    {
        var current = Document.Selection;
        Document.SetSelection(current is null ? SelectionMask.All(Document.Width, Document.Height) : current.Inverted(), "Invert selection");
    }

    public void ApplySelection(SelectionMask mask, string name, SelectionMode? mode = null) =>
        Document.SetSelection(SelectionMask.Combine(Document.Selection, mask, mode ?? Selection.Mode), name);

    public bool Copy(bool cut = false)
    {
        CommitTransform();
        var layer = Document.ActiveLayer;
        var area = Document.Selection?.Bounds ?? layer.ContentBounds;
        area = area.Intersect(layer.ContentBounds);
        if (area.IsEmpty)
        {
            Tell("Nothing to copy on this layer");
            return false;
        }
        var clip = new RasterImage(Document.Width, Document.Height);
        for (int y = area.Y; y < area.Bottom; y++)
            for (int x = area.X; x < area.Right; x++)
                clip[x, y] = Blending.Fade(layer.Image[x, y], Document.SelectionAt(x, y));
        Clipboard = clip;
        if (cut) Document.Clear(layer);
        Tell(cut ? "Cut" : "Copied");
        return true;
    }

    /// <summary>Pastes the clipboard as a new layer (at its original place) and selects the pasted pixels.</summary>
    public bool Paste()
    {
        if (Clipboard is not { } clip) return false;
        CommitTransform();
        var image = clip.Width == Document.Width && clip.Height == Document.Height ? clip.Clone() : Fit(clip);
        Document.AddLayer(image, "Pasted");
        Tell("Pasted as a new layer");
        return true;

        RasterImage Fit(RasterImage src)
        {
            var img = new RasterImage(Document.Width, Document.Height);
            img.Paste(src, (Document.Width - src.Width) / 2, (Document.Height - src.Height) / 2);
            return img;
        }
    }

    /// <summary>Places an external image (opened file or system clipboard) as a new, centred layer.</summary>
    public void PlaceImage(RasterImage image, string name)
    {
        CommitTransform();
        var img = new RasterImage(Document.Width, Document.Height);
        img.Paste(image, (Document.Width - image.Width) / 2, (Document.Height - image.Height) / 2);
        Document.AddLayer(img, name);
    }

    public bool ClearSelectionOrLayer()
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return false;
        return Document.Clear();
    }

    public bool FillSelection(bool background = false)
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return false;
        return FloodFill.FillSelection(Document, Document.ActiveLayer, background ? Background : Foreground);
    }

    // ================================================================ transforms

    /// <summary>Starts a free transform of the selection (or the layer). Returns false if there is nothing to transform.</summary>
    public bool BeginTransform(string name = "Transform")
    {
        if (ActiveTransform is not null) return true;
        EndStroke();
        if (!CanEditActiveLayer()) return false;
        _transform = FloatingTransform.Begin(Document, Document.ActiveLayer, name);
        if (_transform is null)
        {
            Tell("Nothing to transform on this layer");
            return false;
        }
        _transform.Interpolation = PixelMode ? Interpolation.Nearest : TransformInterpolation;
        Changed();
        return true;
    }

    public bool CommitTransform()
    {
        if (ActiveTransform is not { } t) return false;
        t.Commit();
        _transform = null;
        Changed();
        return true;
    }

    public bool CancelTransform()
    {
        if (ActiveTransform is not { } t) return false;
        t.Cancel();
        _transform = null;
        Changed();
        return true;
    }

    /// <summary>Flips or rotates the selection (or layer) in one step.</summary>
    public void QuickTransform(Action<FloatingTransform> change, string name)
    {
        bool wasActive = ActiveTransform is not null;
        if (!BeginTransform(name)) return;
        change(_transform!);
        if (!wasActive) CommitTransform();
    }

    // ================================================================ whole picture

    public void RunImageOperation(Action<Document> op)
    {
        EndStroke();
        CommitTransform();
        op(Document);
    }
}
