using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DoodleNook.App.Canvas;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Diagnostics;
using DoodleNook.App.Editor;
using DoodleNook.App.Platform;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App;

/// <summary>
/// The DoodleNook window: one document, one canvas and six workspaces (Pure Sketch, Quick Sketch, Painting, Editing,
/// Pixel Art, Animation Studio) that only change the tools and panels around the canvas. This file sets the window up and owns the
/// overlays; the workspace layouts, commands, files, keyboard and menus are in the other MainWindow.*.cs files.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly DialogHost _dialogs;
    private readonly DiagnosticsView _diagnostics;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(1.8) };
    private readonly bool _persistSettings;
    private readonly Border _toast;
    private readonly TextBlock _toastText = new() { Classes = { "num" } };
    private readonly Avalonia.Controls.Shapes.Ellipse _toastDot = new() { Width = 12, Height = 12, Stroke = new SolidColorBrush(Color.Parse("#66000000")), StrokeThickness = 1, IsVisible = false };
    private readonly Border _diagnosticsPanel;
    private readonly Border _viewChip;
    private readonly Button _showUi;
    private readonly Button _corner;
    private readonly PixelPreview _pixelPreview;
    private readonly RadialPalette _radial;
    private Document? _watchedDocument;
    private bool _closeConfirmed;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        _persistSettings = Program.Options.SmokeTestDirectory is null && !Design.IsDesignMode && !IsHeadless;
        _settings = _persistSettings ? AppSettings.Load() : new AppSettings();
        InitLibrary(); // before the settings: the remembered brush may be one of the user's own
        _settings.ApplyTo(Session);
        if (_persistSettings) RestorePlacement();

        _dialogs = new DialogHost(DialogLayer, DialogCard);

        // ---- the shared canvas and its overlays (the same controls in every workspace)
        Canvas = new CanvasView { Name = "Canvas" };
        CanvasHost.Children.Add(Canvas);
        Canvas.Attach(Session);

        _viewChip = new Border { Classes = { "float" }, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12), Padding = new Thickness(2), Name = "ViewChip" };
        _viewChip.Child = new ViewControls(this);
        CanvasHost.Children.Add(_viewChip);

        _pixelPreview = new PixelPreview(this) { Name = "PixelPreview", IsVisible = false };
        CanvasHost.Children.Add(_pixelPreview);

        _corner = Menus.MenuButton(new DnIcon { Kind = "radial" }, PureMenu, "Workspace menu", 260);
        _corner.Classes.Add("sq");
        _corner.Name = "PureMenuButton";
        _corner.HorizontalAlignment = HorizontalAlignment.Left;
        _corner.VerticalAlignment = VerticalAlignment.Top;
        _corner.Margin = new Thickness(14);
        _corner.Width = 40;
        _corner.Height = 40;
        _corner.CornerRadius = new CornerRadius(20);
        _corner.Background = (IBrush)this.FindResource("DnFloat")!;
        _corner.BorderBrush = new SolidColorBrush(DnColors.Line);
        _corner.BorderThickness = new Thickness(1);
        _corner.Opacity = 0.75;
        CanvasHost.Children.Add(_corner);

        _showUi = UiKit.Button("expand", "Show interface (Tab)", null, () => SetUiHidden(false));
        _showUi.Name = "ShowUiButton";
        _showUi.IsVisible = false;
        _showUi.HorizontalAlignment = HorizontalAlignment.Left;
        _showUi.VerticalAlignment = VerticalAlignment.Top;
        _showUi.Margin = new Thickness(14);
        _showUi.Padding = new Thickness(12, 0);
        _showUi.CornerRadius = new CornerRadius(999);
        _showUi.Background = (IBrush)this.FindResource("DnFloat")!;
        _showUi.BorderBrush = new SolidColorBrush(DnColors.Line);
        _showUi.BorderThickness = new Thickness(1);
        CanvasHost.Children.Add(_showUi);

        _toast = new Border
        {
            Classes = { "float" }, CornerRadius = new CornerRadius(999), Padding = new Thickness(14, 6), IsVisible = false, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14), Name = "Toast",
            Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(0.35) }],
            Child = UiKit.Row(8, _toastDot, _toastText),
        };
        CanvasHost.Children.Add(_toast);

        _diagnosticsPanel = new Border
        {
            Classes = { "float" }, IsVisible = false, Padding = new Thickness(12, 10), Width = 300,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 60, 0, 0), Name = "DiagnosticsPanel",
        };
        _diagnostics = new DiagnosticsView(Canvas, Session, () => ToggleDiagnostics());
        _diagnosticsPanel.Child = _diagnostics;
        CanvasHost.Children.Add(_diagnosticsPanel);

        InitAnimation();
        InitDesign();
        InitGameEdit();

        _radial = new RadialPalette(this) { Name = "RadialPalette" };
        CanvasHost.Children.Add(_radial);

        // ---- events
        UiKit.FlyoutOpened += OnFlyoutOpened;
        Closed += (_, _) => UiKit.FlyoutOpened -= OnFlyoutOpened;
        Session.StateChanged += RefreshTitle;
        Session.DocumentReplaced += WatchDocument;
        Session.Message += m => ShowToast(m);
        WatchDocument();
        Canvas.GestureStarted += _ =>
        {
            HideFlyouts();
            _radial.Close();
        };
        Canvas.Input!.RadialRequested += (x, y) =>
        {
            HideFlyouts();
            _radial.Open(x, y);
        };
        DockSplitter.DragCompleted += (_, _) =>
        {
            var col = MainGrid.ColumnDefinitions[3];
            if (col.Width.IsAbsolute) WsState.DockWidth = Math.Round(RightHost.Bounds.Width);
        };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast.Opacity = 0;
        };

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => Canvas.InterruptGesture();
        Closing += OnClosing;
        Opened += async (_, _) =>
        {
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
                PenSetupStatus = WindowsPen.Configure(handle.Handle);
            if (_persistSettings)
            {
                try
                {
                    StartRecovery(new Files.RecoveryStore());
                }
                catch (Exception)
                {
                    // Without a recovery folder DoodleNook still works; it just can't keep recovery copies.
                }
                await OfferRecoveryAsync();
            }
        };

        Workspace = Workspaces.Get(_settings.LastWorkspace);
        _previousWorkspace = Workspace.Kind == WorkspaceKind.PureSketch ? WorkspaceKind.QuickSketch : Workspace.Kind;
        ApplyWorkspace();
        RefreshTitle();
    }

    public EditorSession Session { get; } = new();

    /// <summary>The one canvas all workspaces share.</summary>
    public CanvasView Canvas { get; }

    /// <summary>What happened when the window turned off Windows' pen gestures (shown in diagnostics).</summary>
    public string PenSetupStatus { get; private set; } = "not applied";

    internal DialogHost Dialogs => _dialogs;

    internal RadialPalette Radial => _radial;

    internal AppSettings Settings => _settings;

    internal static bool IsHeadless => Application.Current?.ApplicationLifetime is null
        || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Avalonia.Headless");

    public static string Version =>
        typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.5";

    private void WatchDocument()
    {
        if (_watchedDocument is not null) _watchedDocument.HistoryChanged -= RefreshTitle;
        _watchedDocument = Session.Document;
        _watchedDocument.HistoryChanged += RefreshTitle;
        RefreshTitle();
    }

    /// <summary>The milestone shown in the window title ("M009"), from Directory.Build.props (one place to change).</summary>
    public static string Milestone =>
        typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Milestone")?.Value ?? "";

    /// <summary>"DoodleNook — M009", then the picture's name once it has one or has changes ("DoodleNook — M009 — player.dnk •").</summary>
    internal string WindowTitle
    {
        get
        {
            string app = Milestone.Length > 0 ? $"DoodleNook — {Milestone}" : "DoodleNook";
            bool dirty = Session.Document.IsDirty;
            if (Session.FilePath is null && !dirty) return app;
            return $"{app} — {Session.DisplayName}{(dirty ? " •" : "")}";
        }
    }

    private void RefreshTitle() => Title = WindowTitle;

    private void OnFlyoutOpened(Flyout flyout)
    {
        if (flyout.Target is not { } target || !ReferenceEquals(TopLevel.GetTopLevel(target), this)) return;
        // Opening a pop-over must not park keyboard focus in a text box (that would swallow B, E, [ ], …).
        Dispatcher.UIThread.Post(() =>
        {
            if (FocusManager?.GetFocusedElement() is TextBox) Canvas.Focus();
        }, DispatcherPriority.Input);
    }

    internal void HideFlyouts() => UiKit.HideOpenFlyouts();

    // ================================================================== toast

    public void ShowToast(string text, ColorRgb? dot = null)
    {
        _toastText.Text = text;
        _toastDot.IsVisible = dot.HasValue;
        if (dot is { } c) _toastDot.Fill = UiKit.Brush(c);
        UpdateOverlayPositions();
        _toast.IsVisible = true;
        _toast.Opacity = 1;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>The last toast text (tests and smoke test).</summary>
    internal string? LastToast => _toastText.Text;

    private void UpdateOverlayPositions()
    {
        double left = 14, top = 14;
        if (Workspace.Kind == WorkspaceKind.PureSketch) left = 14 + 54;
        else if (UiHidden) left = 14 + 200;
        if (RulersShown)
        {
            // Keep clear of the rulers.
            left += RulerBar.Size;
            top += RulerBar.Size;
        }
        _toast.Margin = new Thickness(left, top, 0, 0);
        _viewChip.Margin = new Thickness(RulersShown ? 12 + RulerBar.Size : 12, 12, 12, 12);
        _diagnosticsPanel.Margin = new Thickness(14, 64, 0, 0);
    }

    // ================================================================== window placement

    private void RestorePlacement()
    {
        try
        {
            if (_settings.WindowWidth is { } w && _settings.WindowHeight is { } h && w >= MinWidth && h >= MinHeight)
            {
                Width = w;
                Height = h;
            }
            if (_settings.WindowX is { } x && _settings.WindowY is { } y)
            {
                var p = new PixelPoint(x, y);
                bool onScreen = Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(p.X + 40, p.Y + 20)));
                if (onScreen)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Position = p;
                }
            }
            if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
        }
        catch (Exception)
        {
            // Placement is a convenience only.
        }
    }

    private void CapturePlacement()
    {
        var state = WindowState == WindowState.FullScreen ? _stateBeforeFullScreen : WindowState;
        _settings.WindowMaximized = state == WindowState.Maximized;
        if (state == WindowState.Normal)
        {
            _settings.WindowX = Position.X;
            _settings.WindowY = Position.Y;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_saveTask is { IsCompleted: false })
        {
            // A save is still being written: finish it first, then close.
            e.Cancel = true;
            try { await _saveTask; }
            catch (Exception) { /* its caller reported it */ }
            Close();
            return;
        }
        if (!_closeConfirmed && Program.Options.SmokeTestDirectory is null && Session.Document.IsDirty)
        {
            e.Cancel = true;
            if (_dialogs.IsOpen) return;
            if (await ConfirmDiscardAsync("closing DoodleNook"))
            {
                _closeConfirmed = true;
                Close();
            }
            return;
        }
        if (_persistSettings)
        {
            CaptureWorkspaceState();
            _settings.CaptureFrom(Session);
            _settings.LastWorkspace = Workspace.Kind;
            CapturePlacement();
            _settings.Save();
        }
        Animator.Pause();
        _referenceBoard?.SaveNow();
        _autosaveTimer.Stop();
        _recovery?.EndSession();
    }
}
