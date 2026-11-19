using DoodleNook.App.Editor;

namespace DoodleNook.App.Shell;

public enum WorkspaceKind
{
    PureSketch,
    QuickSketch,
    Painting,
    Editing,
    PixelArt,
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
    ];

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
    public static ToolKind? ToolForKey(WorkspaceInfo ws, string letter, bool shift)
    {
        string key = shift ? "Shift+" + letter : letter;
        foreach (var t in ws.Tools)
            if (Tools[t].Shortcut == key) return t;
        // Fall back to tools other workspaces offer for this key (e.g. M in Quick Sketch).
        if (!shift && letter == "M" && ws.Tools.Contains(ToolKind.RectSelect)) return ToolKind.RectSelect;
        return null;
    }
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
}
