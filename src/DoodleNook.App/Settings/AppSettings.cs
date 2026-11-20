using System.Text.Json;
using System.Text.Json.Serialization;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Settings;

/// <summary>
/// Remembers brush settings, colours, swatches, tool options, the last workspace, each workspace's interface
/// arrangement and the window position between sessions, in %APPDATA%\DoodleNook\settings.json. Purely local;
/// a missing or broken file just means defaults.
/// </summary>
public sealed class AppSettings
{
    public BrushSettings? Brush { get; set; }
    public BrushSettings? Eraser { get; set; }
    public string? BrushPreset { get; set; }
    public PressureFeel PressureFeel { get; set; } = PressureFeel.Normal;
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public List<string>? RecentColors { get; set; }
    public List<string>? Swatches { get; set; }

    public FillOptions? Fill { get; set; }
    public GradientOptions? Gradient { get; set; }
    public ShapeOptions? Shape { get; set; }
    public SelectionToolOptions? Selection { get; set; }
    public PixelToolOptions? Pixel { get; set; }
    public Interpolation TransformInterpolation { get; set; } = Interpolation.Bilinear;

    public OnionSkinSettings? OnionRaster { get; set; }
    public OnionSkinSettings? OnionPixel { get; set; }

    public StampToolOptions? Stamp { get; set; }

    // Mission 008
    public TextToolOptions? TextTool { get; set; }
    public DesignShapeOptions? DesignShape { get; set; }
    public AssistantOptions? Assistants { get; set; }
    public bool ShowGuides { get; set; } = true;

    // Mission 009: the game graphics folder last used in Game Edit Mode.
    public string? GameFolder { get; set; }
    public PatternToolOptions? PatternFill { get; set; }
    public string? ActivePalette { get; set; }

    /// <summary>The reference board panel: where it was, and its size (window pixels).</summary>
    public double[]? ReferenceBoardPlacement { get; set; }

    /// <summary>Recovery copies of unsaved work every this many seconds (0 = off).</summary>
    public int AutosaveSeconds { get; set; } = 60;

    /// <summary>The last Export animation choices.</summary>
    public AnimationExportSettings AnimationExport { get; set; } = new();

    public WorkspaceKind LastWorkspace { get; set; } = WorkspaceKind.QuickSketch;
    public Dictionary<WorkspaceKind, WorkspaceState> Workspaces { get; set; } = new();

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DoodleNook", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception)
        {
            // Settings are a convenience; never let them break the app.
        }
    }

    public WorkspaceState StateFor(WorkspaceKind kind)
    {
        if (!Workspaces.TryGetValue(kind, out var state))
        {
            state = new WorkspaceState();
            if (kind == WorkspaceKind.Painting || kind == WorkspaceKind.Editing) state.DockWidth = 264;
            Workspaces[kind] = state;
        }
        return state;
    }

    public void ApplyTo(EditorSession session)
    {
        if (Fill is not null) session.SetFillOptions(Fill with { Tolerance = Math.Clamp(Fill.Tolerance, 0, 255), Opacity = Math.Clamp(Fill.Opacity, 0.01, 1) });
        if (Gradient is not null) session.SetGradientOptions(Gradient with { Opacity = Math.Clamp(Gradient.Opacity, 0.01, 1) });
        if (Shape is not null) session.SetShapeOptions(Shape with { Width = Math.Clamp(Shape.Width, 1, 200), Opacity = Math.Clamp(Shape.Opacity, 0.01, 1) });
        if (Selection is not null) session.SetSelectionOptions(Selection with { WandTolerance = Math.Clamp(Selection.WandTolerance, 0, 255) });
        if (Pixel is not null) session.SetPixelOptions(Pixel with { Size = Math.Clamp(Pixel.Size, 1, 64) });
        session.SetTransformInterpolation(TransformInterpolation);
        session.RestoreOnion(OnionRaster, OnionPixel);
        if (Stamp is not null) session.SetStampOptions(Stamp);
        if (PatternFill is not null) session.SetPatternOptions(PatternFill);
        if (TextTool is not null) session.SetTextOptions(TextTool);
        if (DesignShape is not null) session.SetDesignShapeOptions(DesignShape);
        if (Assistants is not null) session.SetAssistOptions(Assistants with { OriginX = null, OriginY = null }); // a new picture starts centred
        session.SetShowGuides(ShowGuides);
        session.SetActivePalette(ActivePalette);
        if (Brush is null || Eraser is null) return;
        var fg = ColorRgb.TryParse(Foreground, out var f) ? f : EditorSession.DefaultForeground;
        var bg = ColorRgb.TryParse(Background, out var b) ? b : EditorSession.DefaultBackground;
        session.RestoreSettings(Brush, Eraser, BrushPreset ?? "pencil", PressureFeel, fg, bg, Parse(RecentColors), Parse(Swatches));
    }

    private static IEnumerable<ColorRgb>? Parse(List<string>? list) =>
        list?.Select(s => ColorRgb.TryParse(s, out var c) ? (ColorRgb?)c : null).OfType<ColorRgb>();

    public void CaptureFrom(EditorSession session)
    {
        Brush = session.Brush;
        Eraser = session.Eraser;
        BrushPreset = session.BrushPresetId;
        PressureFeel = session.PressureFeel;
        Foreground = session.Foreground.ToHex();
        Background = session.Background.ToHex();
        RecentColors = session.RecentColors.Select(c => c.ToHex()).ToList();
        Swatches = session.Swatches.Select(c => c.ToHex()).ToList();
        Fill = session.Fill;
        Gradient = session.Gradient;
        Shape = session.Shape;
        Selection = session.Selection;
        Pixel = session.Pixel;
        TransformInterpolation = session.TransformInterpolation;
        Stamp = session.StampOptions;
        PatternFill = session.PatternOptions;
        ActivePalette = session.ActivePaletteId;
        OnionRaster = session.OnionRaster;
        OnionPixel = session.OnionPixel;
        TextTool = session.Text;
        DesignShape = session.DesignShape;
        Assistants = session.Assist;
        ShowGuides = session.ShowGuides;
    }
}

public enum AnimationExportFormat
{
    SpriteSheet,
    PngSequence,
    Gif,
}

/// <summary>Remembered choices of the Export animation dialog.</summary>
public sealed record AnimationExportSettings(
    AnimationExportFormat Format = AnimationExportFormat.SpriteSheet,
    SheetLayout Layout = SheetLayout.Horizontal,
    int Columns = 4,
    int Spacing = 0,
    int Padding = 0,
    int Margin = 0,
    int Scale = 1,
    bool Json = true,
    bool Loop = true,
    bool Dither = true,
    bool Background = false);
