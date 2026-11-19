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

    /// <summary>The workspace being shown.</summary>
    public WorkspaceInfo Workspace { get; private set; } = Workspaces.Get(WorkspaceKind.QuickSketch);

    /// <summary>The remembered interface state of the current workspace.</summary>
    public WorkspaceState WsState => _settings.StateFor(Workspace.Kind);

    /// <summary>Raised after the workspace changed (bars rebuild their tools and menus).</summary>
    public event Action? WorkspaceChanged;

    /// <summary>Whether the bars are hidden (Tab). Pure Sketch has no bars to hide.</summary>
    public bool UiHidden => Workspace.Kind != WorkspaceKind.PureSketch && WsState.UiHidden;

    /// <summary>Switches workspace. The document, its history, the view and the colours stay exactly as they are.</summary>
    public void SetWorkspace(WorkspaceKind kind)
    {
        if (kind == Workspace.Kind) return;
        Canvas.InterruptGesture();
        HideFlyouts();
        _radial.Close();
        Canvas.Input?.CancelCrop();
        Session.CommitTransform();
        CaptureWorkspaceState();
        _previousWorkspace = Workspace.Kind;
        Workspace = Workspaces.Get(kind);
        _settings.LastWorkspace = kind;
        ApplyWorkspace();
        if (kind == WorkspaceKind.PureSketch)
            ShowToast($"Pure Sketch · right-click or the pen button for the quick palette · Tab returns to {Workspaces.Get(_previousWorkspace).Name}");
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
        bool pixel = ws.Kind == WorkspaceKind.PixelArt;

        Session.SetPixelMode(pixel);
        Canvas.PixelExact = pixel;
        Canvas.IntegerZoom = pixel;
        Canvas.Input!.PixelRightPaintsBackground = pixel && st.RightClickPaintsBackground;
        var tool = st.Tool is { } t && ws.Tools.Contains(t) ? t : ws.DefaultTool;
        Session.SelectTool(tool);

        TopHost.Child = ws.Kind switch
        {
            WorkspaceKind.PureSketch => null,
            WorkspaceKind.QuickSketch => _quickBar ??= new QuickBar(this) { Name = "QuickBar" },
            _ => _titleBar ??= new TitleBar(this) { Name = "TitleBar" },
        };
        OptionsHost.Child = ws.Kind is WorkspaceKind.Painting or WorkspaceKind.Editing or WorkspaceKind.PixelArt
            ? _optionsBar ??= new OptionsBar(this) { Name = "OptionsBar" }
            : null;
        ToolHost.Child = ws.Kind == WorkspaceKind.PureSketch ? null : _toolStrip ??= new ToolStrip(this) { Name = "ToolStrip" };
        RightHost.Child = ws.Kind switch
        {
            WorkspaceKind.Painting => _paintDock ??= BuildPaintDock(),
            WorkspaceKind.Editing => _editDock ??= BuildEditDock(),
            WorkspaceKind.PixelArt => _paletteHost ??= BuildPaletteHost(),
            _ => null,
        };
        BottomHost.Child = ws.Kind == WorkspaceKind.Editing ? _statusBar ??= new StatusBar(this) { Name = "StatusBar" } : null;
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
            new DockSection("colour", "Colour", "palette", new ColourPanel(Session, compact: true) { Width = double.NaN }),
            new DockSection("brushes", "Brushes", "brush", new BrushPanel(Session, compact: true) { Width = double.NaN }),
            new DockSection("layers", "Layers", "layers", new LayersPanel(Session, 380)),
        ]) { Name = "PaintDock" };
        dock.CollapsedChanged += ApplyChromeVisibility;
        return dock;
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
        var column = new PaletteColumn(Session, () => new ColourPanel(Session),
            v =>
            {
                WsState.RightClickPaintsBackground = v;
                Canvas.Input!.PixelRightPaintsBackground = v && Workspace.Kind == WorkspaceKind.PixelArt;
                ShowToast(v ? "Right-click paints with the background colour" : "Right-click opens the quick palette");
            },
            () => WsState.RightClickPaintsBackground);
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
        _viewChip.IsVisible = !hidden && kind is WorkspaceKind.QuickSketch or WorkspaceKind.Painting;
        _showUi.IsVisible = hidden;
        _corner.IsVisible = pure;
        _pixelPreview.IsVisible = kind == WorkspaceKind.PixelArt && WsState.Preview;
        if (_pixelPreview.IsVisible) _pixelPreview.Update();

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
        bool pixel = Workspace.Kind == WorkspaceKind.PixelArt;
        Canvas.ShowGrid = pixel && WsState.ShowGrid;
        Canvas.Tiled = pixel && WsState.Tiled;
        Canvas.InvalidateVisual();
    }

    public void SetShowGrid(bool on)
    {
        WsState.ShowGrid = on;
        ApplyViewOptions();
        _optionsBar?.Refresh();
        if (Workspace.Kind != WorkspaceKind.PixelArt) ShowToast("The pixel grid shows in Pixel Art");
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
