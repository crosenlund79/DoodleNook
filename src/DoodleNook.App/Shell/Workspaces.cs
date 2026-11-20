using DoodleNook.App.Editor;

namespace DoodleNook.App.Shell;

public enum WorkspaceKind
{
    PureSketch,
    QuickSketch,
    Painting,
    Editing,
    PixelArt,
    Animation,
}

/// <summary>A tool as shown in tool strips, menus and tooltips.</summary>
public sealed record ToolInfo(ToolKind Kind, string Name, string Icon, string? Shortcut, string Hint);

/// <summary>One of DoodleNook's five workspaces: which tools and panels it shows (they all share one document).</summary>
public sealed record WorkspaceInfo(WorkspaceKind Kind, string Letter, string Name, string Icon, ToolKind[] Tools, ToolKind DefaultTool, string Description);

public static class Workspaces
{
    public static readonly IReadOnlyList<WorkspaceInfo> All =
    [
        new(WorkspaceKind.PureSketch, "A", "Pure Sketch", "wsA",
            [ToolKind.Brush, ToolKind.Eraser, ToolKind.Eyedropper, ToolKind.Hand], ToolKind.Brush,
            "Just the canvas. Right-click or the pen button opens the quick palette."),
        new(WorkspaceKind.QuickSketch, "B", "Quick Sketch", "wsB",
            [ToolKind.Brush, ToolKind.Eraser, ToolKind.Eyedropper, ToolKind.Lasso, ToolKind.Move, ToolKind.Fill, ToolKind.Hand], ToolKind.Brush,
            "A slim toolbar for spontaneous doodling."),
        new(WorkspaceKind.Painting, "C", "Painting", "wsC",
            [ToolKind.Brush, ToolKind.Eraser, ToolKind.Eyedropper, ToolKind.Fill, ToolKind.Gradient, ToolKind.Shape, ToolKind.Lasso, ToolKind.Move, ToolKind.Hand], ToolKind.Brush,
            "Brushes, colour and layers for finished artwork."),
        new(WorkspaceKind.Editing, "D", "Editing", "wsD",
            [ToolKind.Move, ToolKind.RectSelect, ToolKind.EllipseSelect, ToolKind.Lasso, ToolKind.MagicWand, ToolKind.Crop, ToolKind.Eyedropper,
             ToolKind.Brush, ToolKind.Eraser, ToolKind.Fill, ToolKind.Gradient, ToolKind.Shape, ToolKind.Hand], ToolKind.RectSelect,
            "Selections, transforms, crop, resize and colour adjustments."),
        new(WorkspaceKind.PixelArt, "E", "Pixel Art", "wsE",
            [ToolKind.PixelPencil, ToolKind.PixelEraser, ToolKind.Eyedropper, ToolKind.Fill, ToolKind.PixelLine, ToolKind.PixelRect, ToolKind.PixelEllipse,
             ToolKind.RectSelect, ToolKind.Move, ToolKind.Hand], ToolKind.PixelPencil,
            "Exact pixels, a grid, palettes and whole-number zoom for game art."),
        new(WorkspaceKind.Animation, "F", "Animation Studio", "wsF",
            [ToolKind.Brush, ToolKind.Eraser, ToolKind.Eyedropper, ToolKind.Fill, ToolKind.Lasso, ToolKind.RectSelect, ToolKind.Move, ToolKind.Hand], ToolKind.Brush,
            "Frame-by-frame animation: a timeline, onion skins and playback."),
    ];

    /// <summary>Animation Studio's tools when animating pixel art.</summary>
    public static readonly ToolKind[] PixelAnimationTools =
        [ToolKind.PixelPencil, ToolKind.PixelEraser, ToolKind.Eyedropper, ToolKind.Fill, ToolKind.PixelLine, ToolKind.PixelRect, ToolKind.RectSelect, ToolKind.Move, ToolKind.Hand];

    /// <summary>The tools shown in a workspace (Animation Studio swaps in pixel tools for pixel-art animation).</summary>
    public static ToolKind[] ToolsOf(WorkspaceInfo ws, bool pixelAnimation) =>
        ws.Kind == WorkspaceKind.Animation && pixelAnimation ? PixelAnimationTools : ws.Tools;

    public static WorkspaceInfo Get(WorkspaceKind kind) => All[(int)kind];

    public static readonly IReadOnlyDictionary<ToolKind, ToolInfo> Tools = new Dictionary<ToolKind, ToolInfo>
    {
        [ToolKind.Brush] = new(ToolKind.Brush, "Brush", "brush", "B", "Paint with the current brush · right-drag changes the size"),
        [ToolKind.Eraser] = new(ToolKind.Eraser, "Eraser", "eraser", "E", "Erase to transparent"),
        [ToolKind.Eyedropper] = new(ToolKind.Eyedropper, "Eyedropper", "eyedropper", "I", "Click to pick a colour · hold Alt with any tool"),
        [ToolKind.Hand] = new(ToolKind.Hand, "Hand", "hand", "H", "Drag to pan · hold Space with any tool · hold R and drag to rotate the view"),
        [ToolKind.Fill] = new(ToolKind.Fill, "Paint bucket", "fill", "G", "Click to fill similar colours"),
        [ToolKind.Gradient] = new(ToolKind.Gradient, "Gradient", "gradient", "Shift+G", "Drag from start to end · Shift snaps the angle"),
        [ToolKind.Shape] = new(ToolKind.Shape, "Shapes", "shape", "U", "Drag a line, rectangle or ellipse · Shift constrains"),
        [ToolKind.RectSelect] = new(ToolKind.RectSelect, "Rectangle select", "marquee", "M", "Drag to select · Shift adds, Alt subtracts · click to deselect"),
        [ToolKind.EllipseSelect] = new(ToolKind.EllipseSelect, "Ellipse select", "ellipsesel", "Shift+M", "Drag to select an ellipse · Shift for a circle"),
        [ToolKind.Lasso] = new(ToolKind.Lasso, "Lasso", "lasso", "L", "Draw around what you want to select"),
        [ToolKind.MagicWand] = new(ToolKind.MagicWand, "Magic wand", "wand", "W", "Click to select similar colours"),
        [ToolKind.Move] = new(ToolKind.Move, "Move", "move", "V", "Drag to move the selection or the whole layer"),
        [ToolKind.Crop] = new(ToolKind.Crop, "Crop", "crop", "C", "Drag the area to keep, then press Enter"),
        [ToolKind.PixelPencil] = new(ToolKind.PixelPencil, "Pixel pencil", "pixpencil", "B", "Exact pixels, no smoothing"),
        [ToolKind.PixelEraser] = new(ToolKind.PixelEraser, "Pixel eraser", "eraser", "E", "Clears whole pixels"),
        [ToolKind.PixelLine] = new(ToolKind.PixelLine, "Line", "line", "L", "Drag a pixel line · Shift snaps to 45°"),
        [ToolKind.PixelRect] = new(ToolKind.PixelRect, "Rectangle", "rect", "U", "Drag a pixel rectangle"),
        [ToolKind.PixelEllipse] = new(ToolKind.PixelEllipse, "Ellipse", "ellipse", "Shift+U", "Drag a pixel ellipse"),
    };

    /// <summary>The tool a key selects in a workspace (Photoshop-style letters, resolved within the workspace's tools).</summary>
    public static ToolKind? ToolForKey(WorkspaceInfo ws, string letter, bool shift, bool pixelAnimation = false)
    {
        string key = shift ? "Shift+" + letter : letter;
        var tools = ToolsOf(ws, pixelAnimation);
        foreach (var t in tools)
            if (Tools[t].Shortcut == key) return t;
        // Fall back to tools other workspaces offer for this key (e.g. M in Quick Sketch).
        if (!shift && letter == "M" && tools.Contains(ToolKind.RectSelect)) return ToolKind.RectSelect;
        return null;
    }
}

/// <summary>
/// An optional control or panel the user can show through <em>Customize workspace</em>. Only real, working controls
/// are listed; the defaults keep each workspace minimal (see docs/design/INTERFACE-PHILOSOPHY.md).
/// </summary>
public sealed record CustomOption(string Id, string Label, string Description, WorkspaceKind[] Workspaces, WorkspaceKind[] OnByDefault);

public static class CustomOptions
{
    public const string ViewChip = "view-chip";
    public const string OptionsBar = "options-bar";
    public const string StatusBar = "status-bar";
    public const string LayersChip = "layers-chip";
    public const string PixelPreview = "pixel-preview";
    public const string FrameButtons = "frame-buttons";
    public const string OnionOpacity = "onion-opacity";
    public const string FrameDuration = "frame-duration";
    public const string PreviewPanel = "preview-panel";
    public const string LayersDock = "layers-dock";
    public const string TimelineThumbnails = "timeline-thumbnails";

    private static readonly WorkspaceKind[] None = [];

    public static readonly IReadOnlyList<CustomOption> All =
    [
        new(OptionsBar, "Tool options bar", "Size, opacity and the current tool's settings under the menus",
            [WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.PixelArt, WorkspaceKind.Animation],
            [WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.PixelArt, WorkspaceKind.Animation]),
        new(ViewChip, "View controls on the canvas", "Zoom, fit, rotate and flip buttons in a small chip at the bottom left",
            [WorkspaceKind.QuickSketch, WorkspaceKind.Painting, WorkspaceKind.Animation], [WorkspaceKind.QuickSketch, WorkspaceKind.Painting]),
        new(StatusBar, "Status bar", "Picture size, layer, frame and cursor position along the bottom",
            [WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.PixelArt], [WorkspaceKind.Editing]),
        new(LayersChip, "Layers button", "The layers pop-over button in the bar", [WorkspaceKind.QuickSketch], [WorkspaceKind.QuickSketch]),
        new(PixelPreview, "1:1 preview", "The sprite at real size in the corner of the canvas", [WorkspaceKind.PixelArt], [WorkspaceKind.PixelArt]),
        new(FrameButtons, "Frame navigation buttons", "First, previous, next and last frame buttons in the timeline (otherwise: , and . or the arrow keys)",
            [WorkspaceKind.Animation], None),
        new(OnionOpacity, "Onion-skin opacity slider", "Change onion-skin strength directly in the timeline bar", [WorkspaceKind.Animation], None),
        new(FrameDuration, "Frame duration control", "Set how long the current frame is shown, in the timeline bar", [WorkspaceKind.Animation], None),
        new(PreviewPanel, "Animation preview", "A small looping preview of the whole animation over the canvas", [WorkspaceKind.Animation], None),
        new(LayersDock, "Layers panel", "The full layers panel docked on the right (the timeline already lists the layers)", [WorkspaceKind.Animation], None),
        new(TimelineThumbnails, "Drawing thumbnails in the timeline", "Small pictures of each drawing when the timeline is tall enough",
            [WorkspaceKind.Animation], [WorkspaceKind.Animation]),
    ];

    public static IEnumerable<CustomOption> For(WorkspaceKind kind) => All.Where(o => o.Workspaces.Contains(kind));
}

/// <summary>Remembered interface arrangement of one workspace.</summary>
public sealed class WorkspaceState
{
    public ToolKind? Tool { get; set; }
    public bool UiHidden { get; set; }
    public bool DockCollapsed { get; set; }
    public double DockWidth { get; set; } = 264;
    public List<string> ClosedSections { get; set; } = new();
    public bool ShowGrid { get; set; } = true;
    public bool Tiled { get; set; }
    public bool Preview { get; set; } = true;
    public bool RightClickPaintsBackground { get; set; }

    /// <summary>Customize workspace choices that differ from the defaults (option id → shown). Unknown ids are ignored.</summary>
    public Dictionary<string, bool> Options { get; set; } = new();

    /// <summary>Animation Studio: the timeline's height in layout pixels, and whether it is collapsed to its bar.</summary>
    public double TimelineHeight { get; set; } = 168;
    public bool TimelineCollapsed { get; set; }

    /// <summary>Animation Studio: pixel-art animation (pixel tools, exact display) instead of hand-drawn.</summary>
    public bool PixelAnimation { get; set; }

    /// <summary>Animation Studio: timeline cell width (zoom).</summary>
    public double TimelineCellWidth { get; set; } = 34;

    /// <summary>Animation Studio: the preview panel's width.</summary>
    public double PreviewWidth { get; set; } = 240;

    /// <summary>Whether an optional control is shown in this workspace (the user's choice, or the default).</summary>
    public bool IsShown(string optionId, WorkspaceKind kind)
    {
        var option = CustomOptions.All.FirstOrDefault(o => o.Id == optionId);
        if (option is null || !option.Workspaces.Contains(kind)) return false;
        return Options.TryGetValue(optionId, out bool shown) ? shown : option.OnByDefault.Contains(kind);
    }

    public void SetShown(string optionId, WorkspaceKind kind, bool shown)
    {
        var option = CustomOptions.All.FirstOrDefault(o => o.Id == optionId);
        if (option is null) return;
        if (shown == option.OnByDefault.Contains(kind)) Options.Remove(optionId);
        else Options[optionId] = shown;
    }

    /// <summary>Back to the shipped layout (keeps the current tool and drawing style).</summary>
    public void ResetLayout()
    {
        Options.Clear();
        UiHidden = false;
        DockCollapsed = false;
        DockWidth = 264;
        ClosedSections.Clear();
        ShowGrid = true;
        Tiled = false;
        Preview = true;
        TimelineHeight = 168;
        TimelineCollapsed = false;
        TimelineCellWidth = 34;
        PreviewWidth = 240;
    }
}
