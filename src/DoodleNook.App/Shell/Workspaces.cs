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
            [ToolKind.Brush, ToolKind.Eraser, ToolKind.Smudge, ToolKind.Eyedropper, ToolKind.Fill, ToolKind.Gradient, ToolKind.Shape, ToolKind.Text, ToolKind.Stamp, ToolKind.Lasso, ToolKind.Move, ToolKind.Hand], ToolKind.Brush,
            "Brushes, colour and layers for finished artwork."),
        new(WorkspaceKind.Editing, "D", "Editing", "wsD",
            [ToolKind.Move, ToolKind.RectSelect, ToolKind.EllipseSelect, ToolKind.Lasso, ToolKind.MagicWand, ToolKind.Crop, ToolKind.Eyedropper,
             ToolKind.Brush, ToolKind.Eraser, ToolKind.Smudge, ToolKind.Fill, ToolKind.Gradient, ToolKind.Shape, ToolKind.Text, ToolKind.Hand], ToolKind.RectSelect,
            "Selections, transforms, crop, resize and colour adjustments."),
        new(WorkspaceKind.PixelArt, "E", "Pixel Art", "wsE",
            [ToolKind.PixelPencil, ToolKind.PixelEraser, ToolKind.Eyedropper, ToolKind.Fill, ToolKind.PixelLine, ToolKind.PixelRect, ToolKind.PixelEllipse,
             ToolKind.Stamp, ToolKind.RectSelect, ToolKind.Move, ToolKind.Hand], ToolKind.PixelPencil,
            "Exact pixels, a grid, palettes and whole-number zoom for game art."),
        new(WorkspaceKind.Animation, "F", "Animation Studio", "wsF",
            [ToolKind.Brush, ToolKind.Eraser, ToolKind.Eyedropper, ToolKind.Fill, ToolKind.Lasso, ToolKind.RectSelect, ToolKind.Move, ToolKind.Hand], ToolKind.Brush,
            "Frame-by-frame animation: a timeline, onion skins and playback."),
    ];

    /// <summary>Animation Studio's tools when animating pixel art.</summary>
    public static readonly ToolKind[] PixelAnimationTools =
        [ToolKind.PixelPencil, ToolKind.PixelEraser, ToolKind.Eyedropper, ToolKind.Fill, ToolKind.PixelLine, ToolKind.PixelRect, ToolKind.RectSelect, ToolKind.Move, ToolKind.Hand];

    /// <summary>The tools shown in a workspace (Animation Studio swaps in pixel tools for pixel-art animation; the
    /// stamp tool can be added through Customize workspace where it isn't there by default).</summary>
    public static ToolKind[] ToolsOf(WorkspaceInfo ws, bool pixelAnimation, bool optionalStamp = false, bool optionalText = false, bool optionalRetouch = false)
    {
        var tools = ws.Kind == WorkspaceKind.Animation && pixelAnimation ? PixelAnimationTools : ws.Tools;
        // Mission 010: the retouch group (Smudge, Clone, Repair) after the eraser, where it isn't there by default.
        if (optionalRetouch && !tools.Contains(ToolKind.Smudge) && !(ws.Kind == WorkspaceKind.Animation && pixelAnimation) && ws.Kind != WorkspaceKind.PixelArt)
        {
            int at = Array.IndexOf(tools, ToolKind.Eraser) + 1;
            tools = [.. tools[..at], ToolKind.Smudge, .. tools[at..]];
        }
        // Optional tools go before the Hand (always last).
        if (optionalText && !tools.Contains(ToolKind.Text))
            tools = [.. tools.Where(t => t != ToolKind.Hand), ToolKind.Text, .. tools.Where(t => t == ToolKind.Hand)];
        if (optionalStamp && !tools.Contains(ToolKind.Stamp))
            tools = [.. tools.Where(t => t != ToolKind.Hand), ToolKind.Stamp, .. tools.Where(t => t == ToolKind.Hand)];
        return tools;
    }

    public static WorkspaceInfo Get(WorkspaceKind kind) => All[(int)kind];

    /// <summary>Tool groups: one button in the tool strip shows the group's last-used tool; right-click (or the pen's
    /// button) lists the others. The key is the tool named in the workspace lists.</summary>
    public static readonly IReadOnlyDictionary<ToolKind, ToolKind[]> Groups = new Dictionary<ToolKind, ToolKind[]>
    {
        [ToolKind.Smudge] = [ToolKind.Smudge, ToolKind.Clone, ToolKind.Repair],
    };

    /// <summary>The group a tool belongs to (its key), or the tool itself.</summary>
    public static ToolKind GroupOf(ToolKind t)
    {
        foreach (var (key, members) in Groups)
            if (members.Contains(t)) return key;
        return t;
    }

    public static readonly IReadOnlyDictionary<ToolKind, ToolInfo> Tools = new Dictionary<ToolKind, ToolInfo>
    {
        [ToolKind.Brush] = new(ToolKind.Brush, "Brush", "brush", "B", "Paint with the current brush · right-drag changes the size"),
        [ToolKind.Eraser] = new(ToolKind.Eraser, "Eraser", "eraser", "E", "Erase to transparent"),
        [ToolKind.Eyedropper] = new(ToolKind.Eyedropper, "Eyedropper", "eyedropper", "I", "Click to pick a colour · hold Alt with any tool"),
        [ToolKind.Hand] = new(ToolKind.Hand, "Hand", "hand", "H", "Drag to pan · hold Space with any tool · hold R and drag to rotate the view"),
        [ToolKind.Fill] = new(ToolKind.Fill, "Paint bucket", "fill", "G", "Click to fill similar colours"),
        [ToolKind.Gradient] = new(ToolKind.Gradient, "Gradient", "gradient", "Shift+G", "Drag from start to end · Shift snaps the angle"),
        [ToolKind.Shape] = new(ToolKind.Shape, "Shapes", "shape", "U", "Drag a line, rectangle or ellipse · Shift constrains · Editable keeps shapes changeable"),
        [ToolKind.RectSelect] = new(ToolKind.RectSelect, "Rectangle select", "marquee", "M", "Drag to select · Shift adds, Alt subtracts · click to deselect"),
        [ToolKind.EllipseSelect] = new(ToolKind.EllipseSelect, "Ellipse select", "ellipsesel", "Shift+M", "Drag to select an ellipse · Shift for a circle"),
        [ToolKind.Lasso] = new(ToolKind.Lasso, "Lasso", "lasso", "L", "Draw around what you want to select"),
        [ToolKind.MagicWand] = new(ToolKind.MagicWand, "Magic wand", "wand", "W", "Click to select similar colours"),
        [ToolKind.Move] = new(ToolKind.Move, "Move", "move", "V", "Drag to move the selection or the whole layer · text and shapes get handles · Ctrl+click picks text or a shape"),
        [ToolKind.Crop] = new(ToolKind.Crop, "Crop", "crop", "C", "Drag the area to keep, then press Enter"),
        [ToolKind.PixelPencil] = new(ToolKind.PixelPencil, "Pixel pencil", "pixpencil", "B", "Exact pixels, no smoothing"),
        [ToolKind.PixelEraser] = new(ToolKind.PixelEraser, "Pixel eraser", "eraser", "E", "Clears whole pixels"),
        [ToolKind.PixelLine] = new(ToolKind.PixelLine, "Line", "line", "L", "Drag a pixel line · Shift snaps to 45°"),
        [ToolKind.PixelRect] = new(ToolKind.PixelRect, "Rectangle", "rect", "U", "Drag a pixel rectangle"),
        [ToolKind.PixelEllipse] = new(ToolKind.PixelEllipse, "Ellipse", "ellipse", "Shift+U", "Drag a pixel ellipse"),
        [ToolKind.Stamp] = new(ToolKind.Stamp, "Stamp", "stamp", "S", "Click to place the current stamp · [ and ] size · pick stamps in the options bar"),
        [ToolKind.Text] = new(ToolKind.Text, "Text", "text", "T", "Click to add text · click text to edit it · drag its handles to size and turn it"),
        [ToolKind.Smudge] = new(ToolKind.Smudge, "Smudge", "smudge", "Shift+B", "Drag to push and blend the paint · right-click for Clone and Repair"),
        [ToolKind.Clone] = new(ToolKind.Clone, "Clone", "clone", "Shift+S", "Alt+click where to copy from, then paint where it should go"),
        [ToolKind.Repair] = new(ToolKind.Repair, "Repair", "repair", "J", "Paint over a speck or mark: it is filled from around it (or from a source: Alt+click)"),
    };

    /// <summary>The tool a key selects in a workspace (Photoshop-style letters, resolved within the workspace's tools).</summary>
    public static ToolKind? ToolForKey(WorkspaceInfo ws, string letter, bool shift, bool pixelAnimation = false) =>
        ToolForKey(ToolsOf(ws, pixelAnimation), letter, shift);

    /// <summary>The tool a key selects among the tools shown now.</summary>
    public static ToolKind? ToolForKey(IReadOnlyList<ToolKind> tools, string letter, bool shift)
    {
        string key = shift ? "Shift+" + letter : letter;
        foreach (var t in tools)
        {
            if (Tools[t].Shortcut == key) return t;
            if (Groups.TryGetValue(t, out var members))
                foreach (var m in members)
                    if (Tools[m].Shortcut == key) return m;
        }
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
    public const string StampTool = "stamp-tool";
    public const string FavouriteBrushes = "favourite-brushes";
    public const string ReferenceBoard = "reference-board";
    public const string TextTool = "text-tool";
    public const string RetouchTools = "retouch-tools";

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
        new(PreviewPanel, "Animation preview", "The live animation preview: it loops the whole animation while you draw (in the canvas corner or its own window)", [WorkspaceKind.Animation], None),
        new(LayersDock, "Layers panel", "The full layers panel docked on the right (the timeline already lists the layers)", [WorkspaceKind.Animation], None),
        new(TimelineThumbnails, "Drawing thumbnails in the timeline", "Small pictures of each drawing when the timeline is tall enough",
            [WorkspaceKind.Animation], [WorkspaceKind.Animation]),
        new(StampTool, "Stamp tool", "Place stamps from your library (Painting and Pixel Art always have it)",
            [WorkspaceKind.QuickSketch, WorkspaceKind.Editing, WorkspaceKind.Animation], None),
        new(FavouriteBrushes, "Favourite brushes", "Your starred brushes as one-click buttons next to the brush picker",
            [WorkspaceKind.QuickSketch, WorkspaceKind.Painting, WorkspaceKind.Animation], None),
        new(TextTool, "Text tool", "Editable text (Painting and Editing always have it)",
            [WorkspaceKind.QuickSketch, WorkspaceKind.PixelArt, WorkspaceKind.Animation], None),
        new(RetouchTools, "Smudge, Clone and Repair", "The retouching tools as one button in the tool strip (Painting and Editing always have them)",
            [WorkspaceKind.QuickSketch, WorkspaceKind.Animation], None),
        new(ReferenceBoard, "Reference board", "Your reference pictures in a floating panel (also Window › Reference board)",
            [WorkspaceKind.QuickSketch, WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.PixelArt, WorkspaceKind.Animation], None),
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

    /// <summary>Rulers along the canvas (View › Rulers; hidden by default).</summary>
    public bool ShowRulers { get; set; }

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
