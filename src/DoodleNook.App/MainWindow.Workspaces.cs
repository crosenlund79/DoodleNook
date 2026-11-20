using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;

namespace DoodleNook.App;

public partial class MainWindow
{
    private WorkspaceKind _previousWorkspace;
    private QuickBar? _quickBar;
    private TitleBar? _titleBar;
    private OptionsBar? _optionsBar;
    private ToolStrip? _toolStrip;
    private StatusBar? _statusBar;
    private DockArea? _paintDock;
    private DockArea? _editDock;
    private Border? _paletteHost;
    private DockArea? _animationDock;
    private Animation.TimelinePanel? _timeline;

    /// <summary>The workspace being shown.</summary>
    public WorkspaceInfo Workspace { get; private set; } = Workspaces.Get(WorkspaceKind.QuickSketch);

    /// <summary>The remembered interface state of the current workspace.</summary>
    public WorkspaceState WsState => _settings.StateFor(Workspace.Kind);

    /// <summary>Raised after the workspace changed (bars rebuild their tools and menus).</summary>
    public event Action? WorkspaceChanged;

    /// <summary>Raised when Customize workspace changed which optional controls are shown.</summary>
    public event Action? CustomizationChanged;

    /// <summary>True when Animation Studio animates pixel art (pixel tools, exact display).</summary>
    public bool PixelAnimation => Workspace.Kind == WorkspaceKind.Animation && WsState.PixelAnimation;

    /// <summary>The tools of the current workspace (and drawing style).</summary>
    public ToolKind[] CurrentTools => Workspaces.ToolsOf(Workspace, PixelAnimation, WsState.IsShown(CustomOptions.StampTool, Workspace.Kind));

    internal Animation.TimelinePanel? Timeline => _timeline;

    /// <summary>Whether the bars are hidden (Tab). Pure Sketch has no bars to hide.</summary>
    public bool UiHidden => Workspace.Kind != WorkspaceKind.PureSketch && WsState.UiHidden;

    /// <summary>Switches workspace. The document, its history, the view and the colours stay exactly as they are.</summary>
    public void SetWorkspace(WorkspaceKind kind)
    {
        if (kind == Workspace.Kind) return;
        Canvas.InterruptGesture();
        HideFlyouts();
        _radial.Close();
        Animator.Pause();
        Canvas.Input?.CancelCrop();
        Session.CommitTransform();
        CaptureWorkspaceState();
        _previousWorkspace = Workspace.Kind;
        Workspace = Workspaces.Get(kind);
        _settings.LastWorkspace = kind;
        ApplyWorkspace();
        if (kind == WorkspaceKind.PureSketch)
            ShowToast($"Pure Sketch · right-click or the pen button for the quick palette · Tab returns to {Workspaces.Get(_previousWorkspace).Name}");
        else if (kind == WorkspaceKind.Animation && !Session.Document.IsAnimated)
            ShowToast("Animation Studio · press N (New frame) to start animating this picture");
        else ShowToast(Workspace.Name);
    }

    /// <summary>Tab in Pure Sketch: back to the workspace you came from.</summary>
    public void ReturnFromPureSketch() => SetWorkspace(_previousWorkspace == WorkspaceKind.PureSketch ? WorkspaceKind.QuickSketch : _previousWorkspace);

    private void CaptureWorkspaceState()
    {
        var st = WsState;
        st.Tool = Session.Tool;
        var col = MainGrid.ColumnDefinitions[3];
        if (col.Width.IsAbsolute && RightHost.Bounds.Width > 100 && (Workspace.Kind is WorkspaceKind.Painting or WorkspaceKind.Editing) && !st.DockCollapsed)
            st.DockWidth = Math.Round(RightHost.Bounds.Width);
    }

    private void ApplyWorkspace()
    {
        var ws = Workspace;
        var st = WsState;
        bool pixel = ws.Kind == WorkspaceKind.PixelArt || PixelAnimation;
        bool animation = ws.Kind == WorkspaceKind.Animation;

        Session.SetPixelMode(pixel);
        Canvas.PixelExact = pixel;
        Canvas.IntegerZoom = pixel;
        Canvas.Input!.PixelRightPaintsBackground = ws.Kind == WorkspaceKind.PixelArt && st.RightClickPaintsBackground;
        var tools = CurrentTools;
        var tool = st.Tool is { } t && tools.Contains(t) ? t : tools.Contains(ws.DefaultTool) ? ws.DefaultTool : tools[0];
        Session.SelectTool(tool);
        Animator.Active = animation;

        TopHost.Child = ws.Kind switch
        {
            WorkspaceKind.PureSketch => null,
            WorkspaceKind.QuickSketch => _quickBar ??= new QuickBar(this) { Name = "QuickBar" },
            _ => _titleBar ??= new TitleBar(this) { Name = "TitleBar" },
        };
        OptionsHost.Child = ws.Kind is WorkspaceKind.Painting or WorkspaceKind.Editing or WorkspaceKind.PixelArt or WorkspaceKind.Animation
                            && st.IsShown(CustomOptions.OptionsBar, ws.Kind)
            ? _optionsBar ??= new OptionsBar(this) { Name = "OptionsBar" }
            : null;
        ToolHost.Child = ws.Kind == WorkspaceKind.PureSketch ? null : _toolStrip ??= new ToolStrip(this) { Name = "ToolStrip" };
        RightHost.Child = ws.Kind switch
        {
            WorkspaceKind.Painting => _paintDock ??= BuildPaintDock(),
            WorkspaceKind.Editing => _editDock ??= BuildEditDock(),
            WorkspaceKind.PixelArt => _paletteHost ??= BuildPaletteHost(),
            WorkspaceKind.Animation when st.IsShown(CustomOptions.LayersDock, ws.Kind) => _animationDock ??= BuildAnimationDock(),
            _ => null,
        };
        BottomHost.Child = animation ? _timeline ??= new Animation.TimelinePanel(this)
            : st.IsShown(CustomOptions.StatusBar, ws.Kind) ? _statusBar ??= new StatusBar(this) { Name = "StatusBar" } : null;
        (RightHost.Child as DockArea)?.Refresh();

        WorkspaceChanged?.Invoke();
        ApplyChromeVisibility();
        ApplyViewOptions();
        _optionsBar?.Refresh();
    }

    private DockArea BuildPaintDock()
    {
        var dock = new DockArea(this,
        [
            new DockSection("colour", "Colour", "palette", new ColourPanel(Session, compact: true, app: this) { Width = double.NaN }),
            new DockSection("brushes", "Brushes", "brush", new BrushPanel(Session, compact: true, app: this) { Width = double.NaN }),
            new DockSection("layers", "Layers", "layers", new LayersPanel(Session, 380)),
        ]) { Name = "PaintDock" };
        dock.CollapsedChanged += ApplyChromeVisibility;
        return dock;
    }

    private DockArea BuildAnimationDock()
    {
        var dock = new DockArea(this, [new DockSection("layers", "Layers", "layers", new LayersPanel(Session, 520))]) { Name = "AnimationDock" };
        dock.CollapsedChanged += ApplyChromeVisibility;
        return dock;
    }

    /// <summary>Applies Customize workspace choices without leaving the workspace.</summary>
    internal void ApplyCustomization()
    {
        CaptureWorkspaceState();
        ApplyWorkspace();
        CustomizationChanged?.Invoke();
    }

    /// <summary>Shows or hides Animation Studio's preview panel (remembered, like a pinned option).</summary>
    public void SetPreviewPanel(bool shown)
    {
        _settings.StateFor(WorkspaceKind.Animation).SetShown(CustomOptions.PreviewPanel, WorkspaceKind.Animation, shown);
        ApplyChromeVisibility();
        _optionsBar?.Refresh();
        CustomizationChanged?.Invoke();
    }

    /// <summary>Switches Animation Studio between hand-drawn and pixel-art animation.</summary>
    public void SetPixelAnimation(bool pixel)
    {
        var st = _settings.StateFor(WorkspaceKind.Animation);
        if (st.PixelAnimation == pixel) return;
        st.PixelAnimation = pixel;
        if (Workspace.Kind == WorkspaceKind.Animation)
        {
            st.Tool = null;
            ApplyWorkspace();
            ShowToast(pixel ? "Pixel-art animation: pixel tools, exact pixels" : "Hand-drawn animation: brushes and pressure");
        }
    }

    private DockArea BuildEditDock()
    {
        var dock = new DockArea(this,
        [
            new DockSection("layers", "Layers", "layers", new LayersPanel(Session, 320)),
            new DockSection("properties", "Properties", "props", new PropertiesPanel(this)),
            new DockSection("history", "History", "history", new HistoryPanel(Session)),
        ]) { Name = "EditDock" };
        dock.CollapsedChanged += ApplyChromeVisibility;
        return dock;
    }

    private Border BuildPaletteHost()
    {
        var column = new PaletteColumn(Session, () => new ColourPanel(Session, app: this),
            v =>
            {
                WsState.RightClickPaintsBackground = v;
                Canvas.Input!.PixelRightPaintsBackground = v && Workspace.Kind == WorkspaceKind.PixelArt;
                ShowToast(v ? "Right-click paints with the background colour" : "Right-click opens the quick palette");
            },
            () => WsState.RightClickPaintsBackground, this);
        return new Border
        {
            Name = "PaletteColumn",
            Child = new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            Background = new SolidColorBrush(DnColors.Ui),
            BorderBrush = new SolidColorBrush(DnColors.Line),
            BorderThickness = new Thickness(1, 0, 0, 0),
        };
    }

    /// <summary>Shows or hides the bars (Tab). In Pure Sketch there are none.</summary>
    public void SetUiHidden(bool hidden)
    {
        if (Workspace.Kind == WorkspaceKind.PureSketch) return;
        WsState.UiHidden = hidden;
        HideFlyouts();
        ApplyChromeVisibility();
    }

    private void ApplyChromeVisibility()
    {
        var kind = Workspace.Kind;
        bool hidden = UiHidden;
        bool pure = kind == WorkspaceKind.PureSketch;
        TopHost.IsVisible = !hidden && TopHost.Child is not null;
        OptionsHost.IsVisible = !hidden && OptionsHost.Child is not null;
        ToolHost.IsVisible = !hidden && ToolHost.Child is not null;
        RightHost.IsVisible = !hidden && RightHost.Child is not null;
        BottomHost.IsVisible = !hidden && BottomHost.Child is not null;
        _viewChip.IsVisible = !hidden && WsState.IsShown(CustomOptions.ViewChip, kind);
        _showUi.IsVisible = hidden;
        _corner.IsVisible = pure;
        _pixelPreview.IsVisible = kind == WorkspaceKind.PixelArt && WsState.Preview;
        if (_pixelPreview.IsVisible) _pixelPreview.Update();
        _animationPreview.IsVisible = !hidden && kind == WorkspaceKind.Animation && WsState.IsShown(CustomOptions.PreviewPanel, kind);
        _quickBar?.SetLayersChipVisible(_settings.StateFor(WorkspaceKind.QuickSketch).IsShown(CustomOptions.LayersChip, WorkspaceKind.QuickSketch));
        if (kind == WorkspaceKind.Animation) _timeline?.ApplyLayout();
        ApplyReferenceBoard();

        // The dock column: a fixed, draggable width when expanded; just the icon strip when collapsed.
        var col = MainGrid.ColumnDefinitions[3];
        bool resizable = !hidden && RightHost.Child is DockArea { Collapsed: false };
        if (resizable)
        {
            col.MinWidth = 220;
            col.MaxWidth = 520;
            col.Width = new GridLength(Math.Clamp(WsState.DockWidth, 220, 520));
        }
        else
        {
            col.MinWidth = 0;
            col.MaxWidth = double.PositiveInfinity;
            col.Width = GridLength.Auto;
        }
        DockSplitter.IsVisible = resizable;
        UpdateOverlayPositions();
    }

    private void ApplyViewOptions()
    {
        bool pixel = Workspace.Kind == WorkspaceKind.PixelArt || PixelAnimation;
        Canvas.ShowGrid = pixel && WsState.ShowGrid;
        Canvas.Tiled = Workspace.Kind == WorkspaceKind.PixelArt && WsState.Tiled;
        Canvas.InvalidateVisual();
    }

    public void SetShowGrid(bool on)
    {
        WsState.ShowGrid = on;
        ApplyViewOptions();
        _optionsBar?.Refresh();
        if (Workspace.Kind != WorkspaceKind.PixelArt && !PixelAnimation) ShowToast("The pixel grid shows in Pixel Art and pixel-art animation");
    }

    public void SetTiled(bool on)
    {
        WsState.Tiled = on;
        ApplyViewOptions();
        _optionsBar?.Refresh();
    }

    public void SetPreview(bool on)
    {
        WsState.Preview = on;
        ApplyChromeVisibility();
        _optionsBar?.Refresh();
    }

    /// <summary>The dock section ids of the current workspace (for the Window menu).</summary>
    internal IReadOnlyList<(string Id, string Title)> DockSectionsHere => Workspace.Kind switch
    {
        WorkspaceKind.Painting => [("colour", "Colour"), ("brushes", "Brushes"), ("layers", "Layers")],
        WorkspaceKind.Editing => [("layers", "Layers"), ("properties", "Properties"), ("history", "History")],
        _ => [],
    };

    internal void ToggleDockSection(string id)
    {
        var closed = WsState.ClosedSections;
        if (!closed.Remove(id)) closed.Add(id);
        if (RightHost.Child is DockArea dock)
        {
            if (dock.Collapsed) dock.SetCollapsed(false);
            else dock.Refresh();
        }
    }

    internal void ToggleDockCollapsed()
    {
        if (RightHost.Child is DockArea dock) dock.SetCollapsed(!dock.Collapsed);
    }
}
