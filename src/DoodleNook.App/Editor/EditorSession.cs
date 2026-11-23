using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Library;
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
    Stamp,

    /// <summary>Editable text (Mission 008).</summary>
    Text,

    // Mission 010: retouching (one group in the tool strip).
    Smudge,
    Clone,
    Repair,
}

public enum PressureFeel
{
    Soft,
    Normal,
    Firm,
}

/// <summary>Options of the stamp tool: which stamp, its size (scale), rotation, opacity and mirroring.</summary>
public sealed record StampToolOptions(string? StampId = "builtin:grass", double Scale = 1, double Rotation = 0, double Opacity = 1, bool FlipX = false, bool FlipY = false);

/// <summary>Pattern fill (the paint bucket's pattern mode, and Edit › Fill with pattern).</summary>
public sealed record PatternToolOptions(bool UsePattern = false, string? PatternId = "builtin:brick", double Scale = 1, double OffsetX = 0, double OffsetY = 0,
    double Rotation = 0, double Opacity = 1);

/// <summary>Options of the selection tools.</summary>
public sealed record SelectionToolOptions(SelectionMode Mode = SelectionMode.Replace, bool AntiAlias = true, int WandTolerance = 32, bool WandContiguous = true, bool SampleMerged = false);

/// <summary>Options of the retouching tools (Mission 010). Softness is the opposite of hardness: 0 a hard edge, 1 a soft one.</summary>
public sealed record RetouchToolOptions(
    double SmudgeSize = 40, double SmudgeStrength = 0.6, double SmudgeSoftness = 0.6, bool FingerPaint = false,
    double CloneSize = 50, double CloneOpacity = 1, double CloneSoftness = 0.5, bool SampleAll = false,
    double RepairSize = 24, double RepairSoftness = 0.4, bool RepairFromSource = false);

/// <summary>Options of the pixel-art tools.</summary>
public sealed record PixelToolOptions(int Size = 1, bool PixelPerfect = true, Symmetry Symmetry = Symmetry.None, bool Grid = true, bool Tiled = false, bool Preview = true, bool FilledShapes = false);

/// <summary>
/// The editing state behind the window: the open document, tools and their options, brushes, colours, the clipboard
/// and an in-progress transform. It has no UI-framework dependencies, so it can be driven by tests, the smoke test
/// or the real window alike. All five workspaces share one session (and so one document and one undo history).
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>DoodleNook's built-in brushes (the user's own brushes are in <see cref="Library"/>).</summary>
    public static IReadOnlyList<BrushPreset> Presets => BuiltInAssets.Brushes;

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
        document.PaletteChanged += OnPaletteChanged;
        _engine = new StrokeEngine(document);
        _pixel = new PixelStroke(document);
        _retouch = new RetouchStroke(document);
        BrushPresetId = "pencil";
        Brush = Presets[0].Settings;
        RecentBrushes.Add(BrushPresetId);
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

    public bool IsStrokeActive => _engine.IsActive || _pixel.IsActive || _retouch.IsActive;
    public StrokeEngine Engine => _engine;

    /// <summary>Settings of the painting tool currently shown in the toolbar.</summary>
    public BrushSettings CurrentPaintSettings => PaintTool == ToolKind.Eraser ? Eraser : Brush;

    /// <summary>A free transform in progress (Ctrl+T), or null.</summary>
    public FloatingTransform? ActiveTransform => _transform is { IsFinished: false } ? _transform : null;

    /// <summary>Pixels copied with Copy/Cut (document-sized, transparent around the copied part).</summary>
    public RasterImage? Clipboard { get; private set; }

    /// <summary>Where the copied pixels are in <see cref="Clipboard"/>.</summary>
    public PixelRect ClipboardArea { get; private set; }

    /// <summary>Just the copied pixels (what other programs get when you copy), or null.</summary>
    public RasterImage? ClipboardPicture() => Clipboard is { } clip && !ClipboardArea.IsEmpty ? clip.Crop(ClipboardArea) : null;

    public event Action? StateChanged;
    public event Action? DocumentReplaced;
    public event Action<string>? Message;

    private void Changed() => StateChanged?.Invoke();

    /// <summary>How many listeners react to every change (the smoke test checks that it doesn't grow).</summary>
    internal int StateChangedListenerCount => StateChanged?.GetInvocationList().Length ?? 0;

    private void Tell(string message) => Message?.Invoke(message);

    // ================================================================ documents

    public void ReplaceDocument(Document document, string? filePath)
    {
        EndStroke();
        CommitTransform();
        if (CelClipboard is { } clip && (clip.Image.Width != document.Width || clip.Image.Height != document.Height)) CelClipboard = null;
        Document.PaletteChanged -= OnPaletteChanged;
        Document = document;
        document.PaletteChanged += OnPaletteChanged;
        FilePath = filePath;
        _engine = new StrokeEngine(document);
        _pixel = new PixelStroke(document);
        _retouch = new RetouchStroke(document);
        _liquify?.Cancel(); // a Liquify session belongs to its picture
        _liquify = null;
        CloneSource = null; // a source belongs to its picture
        CloneOffset = null;
        Foreground = Allowed(Foreground);
        Background = Allowed(Background);
        DocumentReplaced?.Invoke();
        Changed();
    }

    public void MarkSaved(string filePath)
    {
        FilePath = filePath;
        Document.MarkSaved();
        Changed();
    }

    /// <summary>A background save of <paramref name="saved"/> finished: only the state captured then counts as saved.</summary>
    public void MarkSaved(Document saved, string filePath, long stateId)
    {
        if (!ReferenceEquals(saved, Document)) return; // another picture was opened meanwhile
        FilePath = filePath;
        Document.MarkSaved(stateId);
        Changed();
    }

    /// <summary>Opens recovered work: unsaved, with the original file name so Save puts it back where it belongs.</summary>
    public void ReplaceWithRecovered(Document document, string? originalPath)
    {
        ReplaceDocument(document, originalPath);
        document.MarkUnsaved();
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
        if (IsRetouchTool(tool)) LastRetouchTool = tool;
        Changed();
    }

    // ================================================================ retouching (Mission 010)

    private RetouchStroke _retouch;
    private (Document Doc, long State, int Frame, RasterImage Image)? _sampled;

    public RetouchToolOptions Retouch { get; private set; } = new();

    /// <summary>The retouching tool shown by the tool strip's retouch button (the last one used).</summary>
    public ToolKind LastRetouchTool { get; private set; } = ToolKind.Smudge;

    public static bool IsRetouchTool(ToolKind t) => t is ToolKind.Smudge or ToolKind.Clone or ToolKind.Repair;

    /// <summary>Where Clone (and Repair from a source) copies from, chosen with Alt+click (document coordinates).</summary>
    public (double X, double Y)? CloneSource { get; private set; }

    /// <summary>The source's offset from the brush, fixed by the first stroke after choosing the source and kept for later
    /// strokes (so separate strokes continue the same copy).</summary>
    public (int Dx, int Dy)? CloneOffset { get; private set; }

    /// <summary>True when the current tool copies from a source (Clone, or Repair set to use one).</summary>
    public bool ToolUsesSource => Tool == ToolKind.Clone || (Tool == ToolKind.Repair && Retouch.RepairFromSource);

    public void SetRetouchOptions(RetouchToolOptions o)
    {
        Retouch = o with
        {
            SmudgeSize = Math.Clamp(o.SmudgeSize, 1, BrushSettings.MaxSize), CloneSize = Math.Clamp(o.CloneSize, 1, BrushSettings.MaxSize),
            RepairSize = Math.Clamp(o.RepairSize, 1, BrushSettings.MaxSize), SmudgeStrength = Math.Clamp(o.SmudgeStrength, 0.05, 1),
            CloneOpacity = Math.Clamp(o.CloneOpacity, 0.05, 1), SmudgeSoftness = Math.Clamp(o.SmudgeSoftness, 0, 1),
            CloneSoftness = Math.Clamp(o.CloneSoftness, 0, 1), RepairSoftness = Math.Clamp(o.RepairSoftness, 0, 1),
        };
        Changed();
    }

    /// <summary>Alt+click with Clone or Repair: copy from here (the next stroke fixes the offset).</summary>
    public void SetCloneSource(double x, double y)
    {
        CloneSource = (Math.Clamp(x, 0, Document.Width - 1), Math.Clamp(y, 0, Document.Height - 1));
        CloneOffset = null;
        if (Tool == ToolKind.Repair && !Retouch.RepairFromSource) Retouch = Retouch with { RepairFromSource = true }; // Alt+click asks for a source
        Tell($"Copying from {CloneSource.Value.X:0}, {CloneSource.Value.Y:0} · now paint where it should go");
        Changed();
    }

    public void ClearCloneSource()
    {
        CloneSource = null;
        CloneOffset = null;
        Changed();
    }

    /// <summary>The size of the current retouching tool, and setting it.</summary>
    private double RetouchSize => Tool switch { ToolKind.Clone => Retouch.CloneSize, ToolKind.Repair => Retouch.RepairSize, _ => Retouch.SmudgeSize };

    private void SetRetouchSize(double size)
    {
        size = Math.Round(Math.Clamp(size, 1, BrushSettings.MaxSize), size < 10 ? 1 : 0);
        SetRetouchOptions(Tool switch
        {
            ToolKind.Clone => Retouch with { CloneSize = size },
            ToolKind.Repair => Retouch with { RepairSize = size },
            _ => Retouch with { SmudgeSize = size },
        });
    }

    /// <summary>What the picture looks like (all visible layers), for copying "what you see"; kept until the picture changes.</summary>
    private RasterImage SampledPicture()
    {
        var doc = Document;
        long state = doc.History.CurrentStateId;
        if (_sampled is { } c && ReferenceEquals(c.Doc, doc) && c.State == state && c.Frame == doc.CurrentFrame) return c.Image;
        // What is on screen is already composited: copy it (fast). With onion skins showing it would include them, so then
        // compose a clean copy instead.
        var image = doc.IsAnimated && Onion.Enabled ? doc.Flatten() : doc.Projection.Clone();
        _sampled = (doc, state, doc.CurrentFrame, image);
        return image;
    }

    private void BeginRetouch(StrokeSample first)
    {
        var o = Retouch;
        (int Dx, int Dy)? source = null;
        if (ToolUsesSource)
        {
            if (CloneSource is not { } src)
            {
                Tell(Tool == ToolKind.Clone ? "Alt+click where to copy from, then paint" : "Alt+click where to take the texture from, then paint");
                return;
            }
            CloneOffset ??= ((int)Math.Round(src.X - first.X), (int)Math.Round(src.Y - first.Y));
            source = CloneOffset;
        }
        RasterImage? sampled = source is not null && o.SampleAll ? SampledPicture() : null;
        var style = Tool switch
        {
            ToolKind.Smudge => new RetouchStyle(RetouchKind.Smudge, o.SmudgeSize, o.SmudgeStrength, 1 - o.SmudgeSoftness, Curve, FingerPaint: o.FingerPaint ? Foreground : null),
            ToolKind.Clone => new RetouchStyle(RetouchKind.Clone, o.CloneSize, o.CloneOpacity, 1 - o.CloneSoftness, Curve, Source: source, Sampled: sampled),
            _ => new RetouchStyle(RetouchKind.Repair, o.RepairSize, 1, 1 - o.RepairSoftness, Curve, Source: source, Sampled: sampled),
        };
        _retouch.Begin(style, first);
    }

    /// <summary>The "Erase" toggle: switches between brush and eraser.</summary>
    public void ToggleEraser() => SelectTool(Tool == ToolKind.Eraser ? ToolKind.Brush : ToolKind.Eraser);

    public bool IsPixelTool(ToolKind t) => t is ToolKind.PixelPencil or ToolKind.PixelEraser or ToolKind.PixelLine or ToolKind.PixelRect or ToolKind.PixelEllipse;

    /// <summary>Size shown in the toolbar: brush/eraser diameter, or the pixel brush size in Pixel Art.</summary>
    public double CurrentSize => ActiveLiquify is not null ? Liquify.Size : IsPixelTool(Tool) ? Pixel.Size : IsRetouchTool(Tool) ? RetouchSize : CurrentPaintSettings.Size;

    public void SetSize(double size)
    {
        if (ActiveLiquify is not null)
        {
            SetLiquifyOptions(Liquify with { Size = Math.Round(size) });
            return;
        }
        if (IsPixelTool(Tool))
        {
            SetPixelOptions(Pixel with { Size = (int)Math.Clamp(Math.Round(size), 1, 64) });
            return;
        }
        if (IsRetouchTool(Tool))
        {
            SetRetouchSize(size);
            return;
        }
        UpdatePaintSettings(s => s with { Size = Math.Round(Math.Clamp(size, BrushSettings.MinSize, BrushSettings.MaxSize), size < 10 ? 1 : 0) });
    }

    /// <summary>The [ and ] keys: about 15 % per step, at least 1 px.</summary>
    public void StepSize(int direction)
    {
        if (Tool == ToolKind.Stamp)
        {
            // Stamps: 15 % per step (whole steps in pixel art).
            double sc = StampOptions.Scale;
            double ns = PixelMode ? Math.Max(1, Math.Round(sc) + direction) : direction > 0 ? sc * 1.15 : sc / 1.15;
            SetStampOptions(StampOptions with { Scale = ns });
            return;
        }
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

    // Mission 010: each brush keeps the size it was last used at, and the last few brushes are at hand (quick palette).
    private readonly Dictionary<string, double> _brushSizes = new();

    /// <summary>The size each brush was last used at (a brush's other preset settings are never changed by switching).</summary>
    public IReadOnlyDictionary<string, double> BrushSizes => _brushSizes;

    /// <summary>The most recently chosen brushes, newest first (the current one included).</summary>
    public List<string> RecentBrushes { get; } = new();

    /// <summary>Chooses a brush: its preset settings, at the size it was last used at (the preset's own size the first
    /// time). The brush being left keeps its current size for next time.</summary>
    public void ApplyPreset(string id)
    {
        var preset = Library.FindBrush(id);
        if (preset is null) return;
        _brushSizes[BrushPresetId] = Brush.Size;
        BrushPresetId = id;
        Brush = preset.Settings with
        {
            Smoothing = Brush.Smoothing,
            Size = _brushSizes.TryGetValue(id, out var size) ? Math.Clamp(size, BrushSettings.MinSize, BrushSettings.MaxSize) : preset.Settings.Size,
        };
        RememberBrush(id);
        if (Tool != ToolKind.Brush) SelectTool(ToolKind.Brush);
        else Changed();
    }

    private void RememberBrush(string id)
    {
        RecentBrushes.Remove(id);
        RecentBrushes.Insert(0, id);
        if (RecentBrushes.Count > 8) RecentBrushes.RemoveRange(8, RecentBrushes.Count - 8);
    }

    /// <summary>Restores the remembered brush sizes and recent brushes (unknown brushes are skipped).</summary>
    public void RestoreBrushMemory(IReadOnlyDictionary<string, double>? sizes, IEnumerable<string>? recent)
    {
        _brushSizes.Clear();
        foreach (var (id, size) in sizes ?? new Dictionary<string, double>())
            if (double.IsFinite(size)) _brushSizes[id] = Math.Clamp(size, BrushSettings.MinSize, BrushSettings.MaxSize);
        RecentBrushes.Clear();
        RecentBrushes.AddRange((recent ?? []).Where(r => Library.FindBrush(r) is not null).Distinct().Take(8));
        if (!RecentBrushes.Contains(BrushPresetId)) RecentBrushes.Insert(0, BrushPresetId);
    }

    /// <summary>Restores settings saved from an earlier session.</summary>
    public void RestoreSettings(BrushSettings brush, BrushSettings eraser, string presetId, PressureFeel feel, ColorRgb fg, ColorRgb bg,
        IEnumerable<ColorRgb>? recent, IEnumerable<ColorRgb>? swatches)
    {
        Brush = brush.Normalized();
        Eraser = eraser.Normalized();
        BrushPresetId = Library.FindBrush(presetId) is not null ? presetId : Presets[0].Id;
        PressureFeel = feel;
        Foreground = Allowed(fg);
        Background = Allowed(bg);
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

    /// <summary>True when the picture is locked to its palette (colours snap to it; the colour picker shows only it).</summary>
    public bool PaletteLocked => Document.PaletteLocked;

    /// <summary>The colour that may be used: itself, or the nearest palette colour while the picture is locked.</summary>
    public ColorRgb Allowed(ColorRgb color) => Document.PaletteConformer?.Nearest(color) ?? color;

    private void OnPaletteChanged()
    {
        Foreground = Allowed(Foreground);
        Background = Allowed(Background);
        Changed();
    }

    public void SetForeground(ColorRgb color)
    {
        color = Allowed(color); // a locked palette is never expanded by picking or the eyedropper
        if (color == Foreground) return;
        Foreground = color;
        Changed();
    }

    public void SetBackground(ColorRgb color)
    {
        color = Allowed(color);
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
        Foreground = Allowed(DefaultForeground);
        Background = Allowed(DefaultBackground);
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
        if (!Document.IsShown(layer))
        {
            Tell(layer.Visible ? $"“{layer.Name}” is in a hidden group — show the group to paint on it" : $"“{layer.Name}” is hidden — show it to paint on it");
            return false;
        }
        if (!layer.IsPaintable && !Document.EditingMask)
        {
            Tell(layer.Kind switch
            {
                LayerKind.Text => $"“{layer.Name}” is text — edit it with the Text tool, or Layer › Convert to pixels",
                LayerKind.Shape => $"“{layer.Name}” is an editable shape — change it in the options bar, or Layer › Convert to pixels",
                LayerKind.Group => $"“{layer.Name}” is a group — choose a layer inside it to paint on",
                _ => $"“{layer.Name}” is an adjustment layer — paint on its mask, or choose another layer",
            });
            return false;
        }
        return true;
    }

    /// <summary>Starts a brush stroke with the current painting tool (or the eraser when the pen's eraser end is used).</summary>
    public void BeginStroke(StrokeSample first, bool forceEraser = false)
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return;
        if (IsRetouchTool(Tool) && !forceEraser)
        {
            BeginRetouch(first);
            return;
        }
        bool erase = forceEraser || Tool == ToolKind.Eraser;
        var settings = erase ? Eraser : Brush;
        if (!erase) RememberColor(Foreground);
        _engine.Begin(new StrokeStyle(settings, Curve, Foreground, erase ? PaintMode.Erase : PaintMode.Paint, Symmetry, Library.Resolve(settings), Assist.Placement), first);
    }

    public void ContinueStroke(ReadOnlySpan<StrokeSample> samples)
    {
        if (_retouch.IsActive) _retouch.Add(samples);
        else _engine.Add(samples);
    }

    public void Spray(double ms) => _engine.Spray(ms);

    public void EndStroke()
    {
        if (_engine.IsActive) _engine.End();
        if (_pixel.IsActive) _pixel.End();
        if (_retouch.IsActive) _retouch.End();
    }

    public void CancelStroke()
    {
        _engine.Cancel();
        _pixel.Cancel();
        _retouch.Cancel();
    }

    /// <summary>Starts a pixel-pencil or pixel-eraser stroke at a pixel.</summary>
    public void BeginPixelStroke(int x, int y, bool erase, bool background = false)
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return;
        var color = background ? Background : Foreground;
        if (!erase) RememberColor(color);
        _pixel.Begin(new PixelStrokeStyle(color, erase, Pixel.Size, Pixel.PixelPerfect, Pixel.Symmetry, Pixel.Tiled, Placement: Assist.Placement), x, y);
    }

    public void ContinuePixelStroke(int x, int y) => _pixel.Add(x, y);

    // ================================================================ history

    public bool Undo()
    {
        EndStroke();
        if (ActiveLiquify is not null) return UndoLiquifyStroke(); // inside Liquify: its last stroke
        if (CancelTransform()) return true;
        return Document.Undo();
    }

    public bool Redo()
    {
        EndStroke();
        if (ActiveLiquify is not null) return false;
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
        if (Document.MergeDownProblem(Document.ActiveLayer) is { } problem) Tell(problem);
        else Document.MergeDown(Document.ActiveLayer);
    }

    public void MoveLayer(int direction)
    {
        CommitTransform();
        var layer = Document.ActiveLayer;
        if (Document.Layers.Any(l => l.IsGroup))
        {
            // With groups, a layer moves among the layers of its own group (Layer › Move out of group leaves it).
            if (!Document.MoveAmongSiblings(layer, direction) && layer.ParentId != 0)
                Tell(direction > 0 ? "Top of the group — Layer › Move out of group to leave it" : "Bottom of the group — Layer › Move out of group to leave it");
            return;
        }
        Document.MoveLayer(layer, Document.ActiveIndex + direction);
    }

    public void SetLayerProperty(Layer layer, Func<LayerProperties, LayerProperties> change, string? name = null)
    {
        CommitTransform();
        Document.SetLayerProperties(layer, change(layer.Properties), name);
    }

    /// <summary>Changes the palette of a picture that isn't locked to it, as one undo step. Mission 017: the palette was
    /// set directly, so choosing one in Pixel Art couldn't be undone and the picture didn't count as changed (closing
    /// didn't ask, and the choice was lost). A locked picture changes palette only through the remap preview.</summary>
    public bool ChangePalette(Palette palette, string historyName)
    {
        var doc = Document;
        if (doc.PaletteLocked) return false;
        if (doc.Palette.Name == palette.Name && doc.Palette.LibraryId == palette.LibraryId && doc.Palette.Colors.SequenceEqual(palette.Colors)) return false;
        Do(() =>
        {
            doc.ChangePalette(palette, locked: false, pixels: null, historyName);
            return true;
        });
        Changed();
        return true;
    }

    private T Do<T>(Func<T> action)
    {
        EndStroke();
        CommitTransform();
        return action();
    }

    // ================================================================ animation

    /// <summary>Onion-skin settings for hand-drawn and for pixel-art animation (kept separately).</summary>
    public OnionSkinSettings OnionRaster { get; private set; } = OnionSkinSettings.Default;
    public OnionSkinSettings OnionPixel { get; private set; } = OnionSkinSettings.Default with { Opacity = 0.3, Falloff = false };

    /// <summary>The onion-skin settings that apply now (pixel or hand-drawn).</summary>
    public OnionSkinSettings Onion => PixelMode ? OnionPixel : OnionRaster;

    /// <summary>A copied drawing (Copy drawing / Paste drawing in the timeline).</summary>
    public Cel? CelClipboard { get; private set; }

    public event Action? OnionChanged;

    public void SetOnion(OnionSkinSettings settings)
    {
        settings = settings.Normalized();
        if (PixelMode) OnionPixel = settings;
        else OnionRaster = settings;
        OnionChanged?.Invoke();
    }

    public void ToggleOnion() => SetOnion(Onion with { Enabled = !Onion.Enabled });

    public void RestoreOnion(OnionSkinSettings? raster, OnionSkinSettings? pixel)
    {
        if (raster is not null) OnionRaster = raster.Normalized();
        if (pixel is not null) OnionPixel = pixel.Normalized();
        OnionChanged?.Invoke();
    }

    private void Prepare()
    {
        EndStroke();
        CommitTransform();
    }

    /// <summary>Shows another frame (stroke and transform are finished first).</summary>
    public void GoToFrame(int frame)
    {
        Prepare();
        Document.SetCurrentFrame(frame);
    }

    /// <summary>Previous/next frame, wrapping around the ends (handy for flipping between drawings).</summary>
    public void StepFrame(int direction)
    {
        if (!Document.IsAnimated) return;
        int n = Document.FrameCount;
        GoToFrame(((Document.CurrentFrame + direction) % n + n) % n);
    }

    /// <summary>New frame after the current one: an empty cell to draw the next pose, or a copy of the drawing.</summary>
    public void NewFrame(bool duplicate)
    {
        Prepare();
        if (!Document.IsAnimated && duplicate && Document.ActiveLayer.ContentBounds.IsEmpty) duplicate = false;
        Document.NewFrame(duplicate ? NewFrameContent.Duplicate : NewFrameContent.Blank);
    }

    public void InsertFrame(int at, bool duplicate)
    {
        Prepare();
        Document.InsertFrame(at, duplicate ? NewFrameContent.Duplicate : NewFrameContent.Blank);
    }

    public void DeleteFrame(int frame)
    {
        Prepare();
        if (!Document.DeleteFrame(frame)) Tell("An animation always keeps at least one frame");
    }

    public void MoveFrame(int from, int to)
    {
        Prepare();
        Document.MoveFrame(from, to);
    }

    public void MakeAnimation()
    {
        Prepare();
        if (Document.MakeAnimation()) Tell("This picture is now a one-frame animation");
    }

    public void SetFrameDuration(int frame, int? durationMs)
    {
        Prepare();
        Document.SetFrameDuration(frame, durationMs);
    }

    public void SetTiming(double fps, bool loop, bool pingPong)
    {
        Prepare();
        Document.SetTiming(fps, loop, pingPong);
    }

    public bool ExtendHold(Layer layer, int frame)
    {
        Prepare();
        return Document.ExtendHold(layer, frame);
    }

    public bool ShortenHold(Layer layer, int frame)
    {
        Prepare();
        return Document.ShortenHold(layer, frame);
    }

    public void MakeIndependent(Layer layer, int frame)
    {
        Prepare();
        if (Document.MakeIndependent(layer, frame)) Tell("This drawing is now independent: changing it no longer changes the earlier frames");
    }

    public void NewBlankDrawing(Layer layer, int frame)
    {
        Prepare();
        Document.NewBlankCel(layer, frame);
    }

    public void DeleteDrawing(Layer layer, int frame)
    {
        Prepare();
        Document.DeleteCel(layer, frame);
    }

    public bool MoveDrawing(Layer layer, int from, int to)
    {
        Prepare();
        return Document.MoveCel(layer, from, to);
    }

    public void CopyDrawing(Layer layer, int frame)
    {
        if (Document.CelAt(layer, frame) is not { } cel)
        {
            Tell("This cell is empty — nothing to copy");
            return;
        }
        CelClipboard = Document.CopyOf(cel);
        Tell("Drawing copied");
        Changed();
    }

    public void PasteDrawing(Layer layer, int frame)
    {
        if (CelClipboard is not { } cel) return;
        Prepare();
        if (!Document.IsAnimated) Document.MakeAnimation();
        if (!Document.PasteCel(layer, frame, cel)) Tell("The copied drawing is a different size from this picture");
    }

    // ================================================================ creative library (Mission 007)

    /// <summary>The artist's brushes, stamps, patterns and palettes (built-in ones included).</summary>
    public CreativeLibrary Library { get; private set; } = new();

    /// <summary>Raised when the library changes (a brush saved, a stamp added…).</summary>
    public event Action? LibraryChanged;

    public StampToolOptions StampOptions { get; private set; } = new();
    public PatternToolOptions PatternOptions { get; private set; } = new();

    /// <summary>The palette shown in the colour panel and Pixel Art's palette column (null = the picture's own palette).</summary>
    public string? ActivePaletteId { get; private set; }

    public void SetLibrary(CreativeLibrary library)
    {
        Library.Changed -= OnLibraryChanged;
        Library = library;
        library.Changed += OnLibraryChanged;
        OnLibraryChanged();
    }

    private void OnLibraryChanged()
    {
        LibraryRevision++;
        LibraryChanged?.Invoke();
        Changed();
    }

    /// <summary>Counts library changes (panels that were hidden compare it to know whether to rebuild their tiles).</summary>
    public int LibraryRevision { get; private set; }

    /// <summary>The preset the current brush came from (null if it no longer exists).</summary>
    public BrushPreset? CurrentPreset => Library.FindBrush(BrushPresetId);

    /// <summary>Uses brush settings directly (Brush Laboratory), remembering which preset they belong to.</summary>
    public void UseBrush(BrushSettings settings, string presetId)
    {
        BrushPresetId = presetId;
        Brush = settings.Normalized();
        if (Tool != ToolKind.Brush) SelectTool(ToolKind.Brush);
        else Changed();
    }

    public void SetStampOptions(StampToolOptions o)
    {
        StampOptions = o with
        {
            Scale = Math.Clamp(double.IsFinite(o.Scale) ? o.Scale : 1, 0.05, 20),
            Rotation = double.IsFinite(o.Rotation) ? (o.Rotation % 360 + 540) % 360 - 180 : 0,
            Opacity = Math.Clamp(double.IsFinite(o.Opacity) ? o.Opacity : 1, 0.05, 1),
        };
        Changed();
    }

    public void SetPatternOptions(PatternToolOptions o)
    {
        PatternOptions = o with
        {
            Scale = Math.Clamp(double.IsFinite(o.Scale) ? o.Scale : 1, 0.1, 16),
            OffsetX = double.IsFinite(o.OffsetX) ? o.OffsetX : 0,
            OffsetY = double.IsFinite(o.OffsetY) ? o.OffsetY : 0,
            Rotation = double.IsFinite(o.Rotation) ? o.Rotation % 360 : 0,
            Opacity = Math.Clamp(double.IsFinite(o.Opacity) ? o.Opacity : 1, 0.05, 1),
        };
        Changed();
    }

    public void SetActivePalette(string? id)
    {
        ActivePaletteId = id is not null && Library.FindPalette(id) is null ? null : id;
        Changed();
    }

    /// <summary>The colours of the active palette (or the picture's own palette).</summary>
    public IReadOnlyList<ColorRgb> ActivePaletteColors => Library.FindPalette(ActivePaletteId)?.Colors ?? Document.Palette.Colors;

    /// <summary>The stamp tool's placement at a point (pixel-exact in pixel mode).</summary>
    public StampPlacement StampPlacementAt(double x, double y) =>
        new(x, y, StampOptions.Scale, StampOptions.Rotation, StampOptions.Opacity, StampOptions.FlipX, StampOptions.FlipY);

    /// <summary>Places the current stamp centred at a point on the active layer (or animation drawing), as one undo step.</summary>
    public bool PlaceStamp(double x, double y)
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return false;
        if (StampOptions.StampId is not { } id || Library.StampImage(id) is not { } img)
        {
            Tell("Choose a stamp first (the stamp button in the options bar)");
            return false;
        }
        return StampsAndPatterns.PlaceStamp(Document, Document.ActiveLayer, img, StampPlacementAt(x, y), PixelMode);
    }

    private PatternFillOptions PatternFillFor() => new(PatternOptions.Scale, PatternOptions.OffsetX, PatternOptions.OffsetY, PatternOptions.Rotation, PatternOptions.Opacity);

    /// <summary>The paint bucket in pattern mode.</summary>
    public bool PatternFillAt(int x, int y)
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return false;
        if (PatternOptions.PatternId is not { } id || Library.PatternImage(id) is not { } tile)
        {
            Tell("Choose a pattern first");
            return false;
        }
        var region = PixelMode ? Fill with { Exact = true, Tolerance = 0 } : Fill;
        return StampsAndPatterns.FillAt(Document, Document.ActiveLayer, x, y, tile, PatternFillFor(), region, PixelMode);
    }

    /// <summary>Edit › Fill with pattern: the selection, or the whole layer.</summary>
    public bool FillSelectionWithPattern()
    {
        CommitTransform();
        if (!CanEditActiveLayer()) return false;
        if (PatternOptions.PatternId is not { } id || Library.PatternImage(id) is not { } tile) return false;
        return StampsAndPatterns.FillSelection(Document, Document.ActiveLayer, tile, PatternFillFor(), PixelMode);
    }

    /// <summary>The active layer's pixels inside the selection (or all of its painting), cropped: for new stamps,
    /// patterns and brush tips. The artwork itself is not changed. Null when there is nothing there.</summary>
    public RasterImage? SelectionPicture(bool mergedLayers = false)
    {
        CommitTransform();
        var layer = Document.ActiveLayer;
        var source = mergedLayers ? Document.RenderFrame(Document.CurrentFrame, includePaper: false) : layer.Image;
        var area = Document.Selection?.Bounds ?? (mergedLayers ? Document.Bounds : layer.ContentBounds);
        area = area.Intersect(Document.Bounds);
        if (area.IsEmpty) return null;
        var img = new RasterImage(area.Width, area.Height);
        bool any = false;
        for (int y = 0; y < area.Height; y++)
            for (int x = 0; x < area.Width; x++)
            {
                uint p = Blending.Fade(source[x + area.X, y + area.Y], Document.SelectionAt(x + area.X, y + area.Y));
                img[x, y] = p;
                any |= DoodleNook.Engine.Imaging.Pixel.A(p) > 0;
            }
        return any ? img : null;
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
        ClipboardArea = area;
        if (cut && layer.Locked) Tell($"Copied · “{layer.Name}” is locked, so nothing was cut");
        else
        {
            if (cut) Document.Clear(layer);
            Tell(cut ? "Cut" : "Copied");
        }
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
        Document.AddLayer(Centred(image), name);
    }

    private RasterImage Centred(RasterImage image)
    {
        if (image.Width == Document.Width && image.Height == Document.Height) return image.Clone();
        var img = new RasterImage(Document.Width, Document.Height);
        img.Paste(image, (Document.Width - image.Width) / 2, (Document.Height - image.Height) / 2);
        return img;
    }

    // ================================================================ pasted references (Mission 012)

    private Layer? _lockWhenPlaced, _drawOnAfterPlacing;

    /// <summary>The pasted reference still being positioned (it locks when its transform finishes).</summary>
    public Layer? PlacingReference => _lockWhenPlaced;

    /// <summary>
    /// Edit › Paste as reference: the picture on a new layer just below the one you draw on, at 35 % and visible, with
    /// transform handles so it can be moved and scaled into place. When the transform finishes (Enter, another tool,
    /// any other command) the layer locks, so drawing can't land on it by accident, and the layer you were drawing on is
    /// active again. In an animation the picture is the current frame's drawing only; other layers (an imported
    /// Reference included) are untouched.
    /// </summary>
    public Layer PlaceReference(RasterImage image, string name = "Pasted reference")
    {
        EndStroke();
        CommitTransform();
        var drawOn = Document.ActiveLayer;
        int? below = drawOn.IsGroup ? null : Document.ActiveIndex;
        var layer = Document.AddLayer(Centred(image), new LayerProperties(name, Opacity: AnimationImport.ReferenceOpacity, ParentId: drawOn.ParentId), below);
        _lockWhenPlaced = layer;
        _drawOnAfterPlacing = drawOn;
        if (!BeginTransform("Position reference")) LockPlacedReference();
        return layer;
    }

    private void LockPlacedReference()
    {
        if (_lockWhenPlaced is not { } layer) return;
        var back = _drawOnAfterPlacing;
        _lockWhenPlaced = _drawOnAfterPlacing = null;
        if (!Document.Layers.Contains(layer)) return;
        if (!layer.Locked) Document.SetLayerProperties(layer, layer.Properties with { Locked = true }, "Lock reference");
        if (back is not null && Document.Layers.Contains(back) && ReferenceEquals(Document.ActiveLayer, layer)) Document.SetActiveLayer(back);
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
        if (Document.ActiveLayer.IsObject)
        {
            Tell("Text and shapes have their own handles — choose the Move tool (V) and drag them");
            return false;
        }
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
        if (ActiveLiquify is not null) return ApplyLiquify(); // finishing pending work applies Liquify, like a transform
        if (ActiveTransform is not { } t)
        {
            LockPlacedReference();
            return false;
        }
        t.Commit();
        _transform = null;
        LockPlacedReference();
        Changed();
        return true;
    }

    /// <summary>Free, Perspective or Warp for the running transform (Mission 010).</summary>
    public void SetTransformMode(TransformMode mode)
    {
        if (ActiveTransform is not { } t) return;
        t.SetMode(mode);
        Changed();
    }

    /// <summary>Perspective or Warp back to where they started.</summary>
    public void ResetTransformMode()
    {
        ActiveTransform?.ResetMode();
        Changed();
    }

    public bool CancelTransform()
    {
        if (ActiveTransform is not { } t) return false;
        t.Cancel();
        _transform = null;
        LockPlacedReference();
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
