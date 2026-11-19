using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Editor;

public enum ToolKind
{
    Brush,
    Eraser,
    Eyedropper,
    Hand,
}

public enum PressureFeel
{
    Soft,
    Normal,
    Firm,
}

/// <summary>A named starting point for the round brush (the Quick Sketch presets from Mission 003 that one round brush can honestly do).</summary>
public sealed record BrushPreset(string Id, string Name, BrushSettings Settings);

/// <summary>
/// The editing state behind the window: the open document, the current tool, brush and eraser settings and colours.
/// It has no UI-framework dependencies, so it can be driven by tests, the smoke test or the real window alike.
/// </summary>
public sealed class EditorSession
{
    public static readonly IReadOnlyList<BrushPreset> Presets =
    [
        new("pencil", "Sketch pencil", new BrushSettings { Size = 6, Hardness = 0.85, Opacity = 0.85, PressureSize = true, PressureOpacity = true }),
        new("ink", "Ink pen", new BrushSettings { Size = 9, Hardness = 1, Opacity = 1, PressureSize = true, PressureOpacity = false }),
        new("liner", "Fine liner", new BrushSettings { Size = 3, Hardness = 1, Opacity = 1, PressureSize = false, PressureOpacity = false }),
        new("marker", "Marker", new BrushSettings { Size = 26, Hardness = 0.8, Opacity = 0.55, PressureSize = false, PressureOpacity = false }),
        new("soft", "Soft round", new BrushSettings { Size = 70, Hardness = 0.05, Opacity = 0.6, PressureSize = true, PressureOpacity = true }),
        new("wash", "Flat wash", new BrushSettings { Size = 48, Hardness = 0.95, Opacity = 0.7, PressureSize = false, PressureOpacity = true }),
    ];

    public static readonly ColorRgb DefaultForeground = ColorRgb.Parse("#3B2F2A");
    public static readonly ColorRgb DefaultBackground = ColorRgb.White;
    public static readonly ColorRgb[] DefaultRecent =
        new[] { "#3B2F2A", "#5F8FA8", "#C96F4A", "#F2C14E", "#7AA66A", "#E88A9A", "#8A6FB0", "#F8F4EC" }
        .Select(ColorRgb.Parse).ToArray();

    public const int DefaultWidth = 1920;
    public const int DefaultHeight = 1080;

    private StrokeEngine _engine;

    public EditorSession()
        : this(new Document(DefaultWidth, DefaultHeight, ColorRgb.White))
    {
    }

    public EditorSession(Document document)
    {
        Document = document;
        _engine = new StrokeEngine(document);
        BrushPresetId = "pencil";
        Brush = Presets[0].Settings;
    }

    public Document Document { get; private set; }

    /// <summary>Where the document was opened from or last saved to (null for a new, unsaved picture).</summary>
    public string? FilePath { get; private set; }

    public string DisplayName => FilePath is null ? "Untitled" : Path.GetFileName(FilePath);

    public ToolKind Tool { get; private set; } = ToolKind.Brush;

    /// <summary>The brush or eraser: the tool whose size the toolbar shows (the last painting tool used).</summary>
    public ToolKind PaintTool { get; private set; } = ToolKind.Brush;

    public BrushSettings Brush { get; private set; }

    public BrushSettings Eraser { get; private set; } = new() { Size = 30, Hardness = 0.9, Opacity = 1, PressureSize = true, PressureOpacity = false };

    public string BrushPresetId { get; private set; }

    public PressureFeel PressureFeel { get; private set; } = PressureFeel.Normal;

    public PressureCurve Curve => PressureFeel switch
    {
        PressureFeel.Soft => PressureCurve.Soft,
        PressureFeel.Firm => PressureCurve.Firm,
        _ => PressureCurve.Normal,
    };

    public ColorRgb Foreground { get; private set; } = DefaultForeground;
    public ColorRgb Background { get; private set; } = DefaultBackground;
    public List<ColorRgb> RecentColors { get; } = [.. DefaultRecent];

    public bool IsStrokeActive => _engine.IsActive;
    public StrokeEngine Engine => _engine;

    /// <summary>Settings of the painting tool currently shown in the toolbar.</summary>
    public BrushSettings CurrentPaintSettings => PaintTool == ToolKind.Eraser ? Eraser : Brush;

    /// <summary>Raised when tool, brush settings or colours change.</summary>
    public event Action? StateChanged;

    /// <summary>Raised after a new document has been created or opened.</summary>
    public event Action? DocumentReplaced;

    // ---------------------------------------------------------------- documents

    public void ReplaceDocument(Document document, string? filePath)
    {
        if (_engine.IsActive) _engine.End();
        Document = document;
        FilePath = filePath;
        _engine = new StrokeEngine(document);
        DocumentReplaced?.Invoke();
        StateChanged?.Invoke();
    }

    public void MarkSaved(string filePath)
    {
        FilePath = filePath;
        Document.MarkSaved();
        StateChanged?.Invoke();
    }

    // ---------------------------------------------------------------- tools and settings

    public void SelectTool(ToolKind tool)
    {
        if (_engine.IsActive) EndStroke();
        Tool = tool;
        if (tool is ToolKind.Brush or ToolKind.Eraser) PaintTool = tool;
        StateChanged?.Invoke();
    }

    /// <summary>The "Erase" toggle: switches between brush and eraser.</summary>
    public void ToggleEraser() => SelectTool(Tool == ToolKind.Eraser ? ToolKind.Brush : ToolKind.Eraser);

    public void SetSize(double size) => UpdatePaintSettings(s => s with { Size = Math.Round(Math.Clamp(size, BrushSettings.MinSize, BrushSettings.MaxSize), size < 10 ? 1 : 0) });

    /// <summary>The [ and ] keys: about 15 % per step, at least 1 px.</summary>
    public void StepSize(int direction)
    {
        double s = CurrentPaintSettings.Size;
        double next = direction > 0 ? Math.Max(s + 1, s * 1.15) : Math.Min(s - 1, s / 1.15);
        SetSize(Math.Round(next));
    }

    public void SetOpacity(double opacity) => UpdatePaintSettings(s => s with { Opacity = Math.Clamp(opacity, 0.01, 1) });

    public void SetHardness(double hardness) => UpdatePaintSettings(s => s with { Hardness = Math.Clamp(hardness, 0, 1) });

    public void SetSmoothing(double smoothing) => UpdatePaintSettings(s => s with { Smoothing = Math.Clamp(smoothing, 0, 1) });

    public void SetPressureSize(bool on) => UpdatePaintSettings(s => s with { PressureSize = on });

    public void SetPressureOpacity(bool on) => UpdatePaintSettings(s => s with { PressureOpacity = on });

    public void SetPressureFeel(PressureFeel feel)
    {
        PressureFeel = feel;
        StateChanged?.Invoke();
    }

    public void ApplyPreset(string id)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == id);
        if (preset is null) return;
        BrushPresetId = id;
        Brush = preset.Settings with { Smoothing = Brush.Smoothing };
        if (Tool != ToolKind.Brush) SelectTool(ToolKind.Brush);
        else StateChanged?.Invoke();
    }

    /// <summary>Restores settings saved from an earlier session.</summary>
    public void RestoreSettings(BrushSettings brush, BrushSettings eraser, string presetId, PressureFeel feel, ColorRgb fg, ColorRgb bg, IEnumerable<ColorRgb>? recent)
    {
        Brush = brush.Normalized();
        Eraser = eraser.Normalized();
        BrushPresetId = Presets.Any(p => p.Id == presetId) ? presetId : Presets[0].Id;
        PressureFeel = feel;
        Foreground = fg;
        Background = bg;
        if (recent is not null)
        {
            var list = recent.Take(10).ToList();
            if (list.Count > 0)
            {
                RecentColors.Clear();
                RecentColors.AddRange(list);
            }
        }
        StateChanged?.Invoke();
    }

    private void UpdatePaintSettings(Func<BrushSettings, BrushSettings> change)
    {
        if (PaintTool == ToolKind.Eraser) Eraser = change(Eraser).Normalized();
        else Brush = change(Brush).Normalized();
        StateChanged?.Invoke();
    }

    // ---------------------------------------------------------------- colours

    public void SetForeground(ColorRgb color)
    {
        if (color == Foreground) return;
        Foreground = color;
        StateChanged?.Invoke();
    }

    public void SwapColors()
    {
        (Foreground, Background) = (Background, Foreground);
        StateChanged?.Invoke();
    }

    public void ResetColors()
    {
        Foreground = DefaultForeground;
        Background = DefaultBackground;
        StateChanged?.Invoke();
    }

    /// <summary>Picks the visible colour at a document position. Returns false over empty, transparent areas.</summary>
    public bool PickColor(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        uint p = Document.CompositeAt(ix, iy);
        var (r, g, b, a) = Pixel.ToStraight(p);
        if (a == 0) return false;
        SetForeground(new ColorRgb(r, g, b));
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

    // ---------------------------------------------------------------- strokes

    /// <summary>Starts a stroke with the current painting tool (or the eraser when the pen's eraser end is used).</summary>
    public void BeginStroke(StrokeSample first, bool forceEraser = false)
    {
        bool erase = forceEraser || Tool == ToolKind.Eraser;
        var settings = erase ? Eraser : Brush;
        if (!erase) RememberColor(Foreground);
        _engine.Begin(new StrokeStyle(settings, Curve, Foreground, erase ? PaintMode.Erase : PaintMode.Paint), first);
    }

    public void ContinueStroke(ReadOnlySpan<StrokeSample> samples) => _engine.Add(samples);

    public void EndStroke() => _engine.End();

    public void CancelStroke() => _engine.Cancel();

    public bool Undo()
    {
        if (_engine.IsActive) _engine.End();
        return !Document.Undo().IsEmpty;
    }

    public bool Redo()
    {
        if (_engine.IsActive) _engine.End();
        return !Document.Redo().IsEmpty;
    }

    public bool ClearCanvas()
    {
        if (_engine.IsActive) _engine.End();
        return !Document.Clear().IsEmpty;
    }
}
