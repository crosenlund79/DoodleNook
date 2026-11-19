using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Diagnostics;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Platform;
using DoodleNook.App.Settings;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly DialogHost _dialogs;
    private readonly BrushPanel _brushPanel;
    private readonly ColourPanel _colourPanel;
    private readonly DiagnosticsView _diagnostics;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(1.6) };
    private readonly bool _persistSettings;
    private Document? _watchedDocument;
    private bool _uiHidden;
    private bool _closeConfirmed;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;
    private string? _previewKey;

    public MainWindow()
    {
        InitializeComponent();
        _persistSettings = Program.Options.SmokeTestDirectory is null && !Design.IsDesignMode && !IsHeadless;
        _settings = _persistSettings ? AppSettings.Load() : new AppSettings();
        _settings.ApplyTo(Session);
        if (_persistSettings) RestorePlacement();

        _dialogs = new DialogHost(DialogLayer, DialogCard);
        _brushPanel = new BrushPanel(Session, () => BrushFlyout.Hide());
        _colourPanel = new ColourPanel(Session, () => { ColourFlyout.Hide(); FgFlyout.Hide(); });
        BrushFlyout.Content = _brushPanel;
        ColourFlyout.Opening += (_, _) => { FgFlyout.Content = null; ColourFlyout.Content = _colourPanel; };
        FgFlyout.Opening += (_, _) => { ColourFlyout.Content = null; FgFlyout.Content = _colourPanel; };
        MenuFlyout.Content = BuildMenu();
        // Opening a pop-over must not park keyboard focus in a text box (that would swallow B, E, [ ], …).
        foreach (var f in new[] { ColourFlyout, FgFlyout, BrushFlyout, MenuFlyout })
            f.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (FocusManager?.GetFocusedElement() is TextBox) Canvas.Focus();
            }, DispatcherPriority.Input);

        Canvas.Attach(Session);
        _diagnostics = new DiagnosticsView(Canvas, Session, () => ToggleDiagnostics());
        DiagnosticsPanel.Child = _diagnostics;

        WireToolbar();
        Session.StateChanged += Refresh;
        Session.DocumentReplaced += WatchDocument;
        WatchDocument();
        Canvas.ViewChanged += UpdateZoomLabel;
        Canvas.GestureStarted += _ => HideFlyouts();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Opacity = 0;
        };

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => Canvas.InterruptGesture();
        Closing += OnClosing;
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
                PenSetupStatus = WindowsPen.Configure(handle.Handle);
            UpdateZoomLabel();
        };
        ScalingChanged += (_, _) => UpdateZoomLabel();
        Refresh();
    }

    public EditorSession Session { get; } = new();

    private Flyout MenuFlyout => (Flyout)MenuButton.Flyout!;
    private Flyout BrushFlyout => (Flyout)PresetChip.Flyout!;
    private Flyout ColourFlyout => (Flyout)ColourChip.Flyout!;
    private Flyout FgFlyout => (Flyout)FgChip.Flyout!;

    /// <summary>What happened when the window turned off Windows' pen gestures (shown in diagnostics).</summary>
    public string PenSetupStatus { get; private set; } = "not applied";

    internal static bool IsHeadless => Application.Current?.ApplicationLifetime is null
        || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Avalonia.Headless");

    public static string Version =>
        typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.4";

    // ================================================================== toolbar wiring

    private void WireToolbar()
    {
        BrushTool.Click += (_, _) => Session.SelectTool(ToolKind.Brush);
        EraserTool.Click += (_, _) => Session.SelectTool(ToolKind.Eraser);
        PickerTool.Click += (_, _) => Session.SelectTool(ToolKind.Eyedropper);
        HandTool.Click += (_, _) => Session.SelectTool(ToolKind.Hand);
        EraseButton.Click += (_, _) => Session.ToggleEraser();
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();
        SizeSlider.UserChanged += (_, v) => Session.SetSize(v);
        OpacitySlider.UserChanged += (_, v) => Session.SetOpacity(v);
        BgChip.Click += (_, _) => Session.SwapColors();
        HideUiButton.Click += (_, _) => SetUiHidden(true);
        ShowUiButton.Click += (_, _) => SetUiHidden(false);
        ZoomInButton.Click += (_, _) => Canvas.ZoomStep(+1);
        ZoomOutButton.Click += (_, _) => Canvas.ZoomStep(-1);
        ZoomText.Click += (_, _) => Canvas.ZoomToActualPixels();
        FitButton.Click += (_, _) => Canvas.FitToView();
        FlipButton.Click += (_, _) => ToggleFlipView();
    }

    private void ToggleFlipView()
    {
        Canvas.ToggleFlip();
        FlipButton.Classes.Set("on", Canvas.View.FlipHorizontal);
        ShowToast(Canvas.View.FlipHorizontal ? "View flipped (the picture itself is unchanged)" : "View back to normal");
    }

    private void WatchDocument()
    {
        if (_watchedDocument is not null) _watchedDocument.HistoryChanged -= Refresh;
        _watchedDocument = Session.Document;
        _watchedDocument.HistoryChanged += Refresh;
        Refresh();
    }

    /// <summary>Brings every toolbar control in line with the session state.</summary>
    private void Refresh()
    {
        var s = Session;
        Title = $"{s.DisplayName}{(s.Document.IsDirty ? " •" : "")} — DoodleNook";

        BrushTool.Classes.Set("on", s.Tool == ToolKind.Brush);
        EraserTool.Classes.Set("on", s.Tool == ToolKind.Eraser);
        PickerTool.Classes.Set("on", s.Tool == ToolKind.Eyedropper);
        HandTool.Classes.Set("on", s.Tool == ToolKind.Hand);
        EraseButton.Classes.Set("on", s.Tool == ToolKind.Eraser);

        var paint = s.CurrentPaintSettings;
        SizeSlider.Value = paint.Size;
        SizeText.Text = $"{paint.Size:0} px";
        OpacityGroup.IsVisible = s.PaintTool == ToolKind.Brush;
        OpacitySlider.Value = paint.Opacity;
        OpacityText.Text = $"{paint.Opacity * 100:0}%";
        ToolTip.SetTip(PresetChip, s.PaintTool == ToolKind.Eraser ? "Eraser settings" : $"Brush: {EditorSession.Presets.FirstOrDefault(p => p.Id == s.BrushPresetId)?.Name} — presets and settings");

        string key = $"{s.PaintTool}|{paint}";
        if (key != _previewKey)
        {
            _previewKey = key;
            PresetPreview.Source = BrushPreview.Render(paint, new ColorRgb(DnColors.Text.R, DnColors.Text.G, DnColors.Text.B), 44, 22);
        }

        var fg = Ui.Brush(s.Foreground);
        ColourDot.Fill = fg;
        FgSwatch.Background = fg;
        BgSwatch.Background = Ui.Brush(s.Background);
        UndoButton.IsEnabled = s.Document.CanUndo;
        RedoButton.IsEnabled = s.Document.CanRedo;
        ToolTip.SetTip(UndoButton, s.Document.History.NextUndoName is { } u ? $"Undo {u.ToLowerInvariant()} (Ctrl+Z)" : "Undo (Ctrl+Z)");
        ToolTip.SetTip(RedoButton, s.Document.History.NextRedoName is { } r ? $"Redo {r.ToLowerInvariant()} (Ctrl+Shift+Z)" : "Redo (Ctrl+Shift+Z)");
        FlipButton.Classes.Set("on", Canvas.View.FlipHorizontal);
        UpdateZoomLabel();
    }

    private void UpdateZoomLabel()
    {
        double z = Canvas.ActualZoom * 100;
        ZoomLabel.Text = z < 10 ? $"{z:0.#}%" : $"{z:0}%";
    }

    // ================================================================== commands

    private void Undo()
    {
        var name = Session.Document.History.NextUndoName;
        if (Session.Undo()) ShowToast($"Undid {name?.ToLowerInvariant()}");
    }

    private void Redo()
    {
        var name = Session.Document.History.NextRedoName;
        if (Session.Redo()) ShowToast($"Redid {name?.ToLowerInvariant()}");
    }

    private void ClearCanvas()
    {
        if (Session.ClearCanvas()) ShowToast("Canvas cleared — Ctrl+Z brings it back");
    }

    private void SetUiHidden(bool hidden)
    {
        _uiHidden = hidden;
        QuickBar.IsVisible = !hidden;
        ToolRail.IsVisible = !hidden;
        ViewChip.IsVisible = !hidden;
        ShowUiButton.IsVisible = hidden;
        HideFlyouts();
        UpdateToastPosition();
    }

    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen) WindowState = _stateBeforeFullScreen;
        else
        {
            _stateBeforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
            ShowToast("Full screen — F11 or Esc to leave");
        }
    }

    private void ToggleDiagnostics()
    {
        DiagnosticsPanel.IsVisible = !DiagnosticsPanel.IsVisible;
        _diagnostics.SetActive(DiagnosticsPanel.IsVisible);
        UpdateToastPosition();
    }

    private void HideFlyouts()
    {
        MenuFlyout.Hide();
        BrushFlyout.Hide();
        ColourFlyout.Hide();
        FgFlyout.Hide();
    }

    public void ShowToast(string text, ColorRgb? dot = null)
    {
        ToastText.Text = text;
        ToastDot.IsVisible = dot.HasValue;
        if (dot is { } c) ToastDot.Fill = Ui.Brush(c);
        UpdateToastPosition();
        Toast.IsVisible = true;
        Toast.Opacity = 1;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void UpdateToastPosition()
    {
        double left = 14;
        if (_uiHidden) left += 220;
        Toast.Margin = new Thickness(left, 14, 0, 0);
        DiagnosticsPanel.Margin = new Thickness(14, _uiHidden ? 64 : 60, 0, 0);
    }

    // ================================================================== files

    private static readonly FilePickerFileType PngType = new("PNG image")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
        AppleUniformTypeIdentifiers = ["public.png"],
    };

    private async Task<bool> ConfirmDiscardAsync(string beforeWhat)
    {
        if (Session.IsStrokeActive) Session.EndStroke();
        if (!Session.Document.IsDirty) return true;
        Canvas.InterruptGesture();
        var choice = await _dialogs.AskSaveChangesAsync(Session.DisplayName, beforeWhat);
        if (choice is null) return false;
        if (choice == "save") return await SaveAsync();
        return true;
    }

    public async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync("starting a new picture")) return;
        var doc = Session.Document;
        var r = await _dialogs.AskNewDocumentAsync(doc.Width, doc.Height, doc.Background is not null || Session.FilePath is null);
        if (r is not { } n) return;
        Session.ReplaceDocument(new Document(n.Width, n.Height, n.Background), null);
        ShowToast($"New picture · {n.Width} × {n.Height}");
    }

    public async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync("opening another picture")) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a picture",
            AllowMultiple = false,
            FileTypeFilter = [PngType, FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0) return;
        var file = files[0];
        if (file.TryGetLocalPath() is { } path)
        {
            await OpenPathAsync(path);
            return;
        }
        try
        {
            await using var stream = await file.OpenReadAsync();
            Session.ReplaceDocument(PngFile.Open(stream), null);
            ShowToast($"Opened {file.Name}");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the picture", $"{file.Name}: {ex.Message}");
        }
    }

    /// <summary>Opens a picture from a path (menu, command line or smoke test).</summary>
    public async Task<bool> OpenPathAsync(string path)
    {
        try
        {
            var doc = PngFile.Open(path);
            Session.ReplaceDocument(doc, path);
            ShowToast($"Opened {Path.GetFileName(path)} · {doc.Width} × {doc.Height}");
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the picture", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SaveAsync()
    {
        var path = Session.FilePath;
        if (path is null || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return await SaveAsAsync();
        return await SaveToPathAsync(path);
    }

    public async Task<bool> SaveAsAsync()
    {
        if (Session.IsStrokeActive) Session.EndStroke();
        var suggested = Path.ChangeExtension(Session.DisplayName, ".png");
        IStorageFolder? folder = null;
        if (Session.FilePath is { } current && Path.GetDirectoryName(current) is { } dir)
            folder = await StorageProvider.TryGetFolderFromPathAsync(dir);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save picture as PNG",
            SuggestedFileName = suggested,
            SuggestedStartLocation = folder,
            DefaultExtension = "png",
            FileTypeChoices = [PngType],
            ShowOverwritePrompt = true,
        });
        if (file is null) return false; // cancelled: nothing changes
        if (file.TryGetLocalPath() is { } path)
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
            return await SaveToPathAsync(path);
        }
        try
        {
            await using var stream = await file.OpenWriteAsync();
            PngFile.Encode(Session.Document.Flatten(), stream);
            Session.Document.MarkSaved();
            ShowToast($"Saved {file.Name}");
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't save", ex.Message);
            return false;
        }
    }

    public async Task<bool> SaveToPathAsync(string path)
    {
        if (Session.IsStrokeActive) Session.EndStroke();
        try
        {
            PngFile.Save(Session.Document, path);
            Session.MarkSaved(path);
            ShowToast($"Saved {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't save", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
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
            _settings.CaptureFrom(Session);
            CapturePlacement();
            _settings.Save();
        }
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

    // ================================================================== keyboard

    private bool IsTyping => FocusManager?.GetFocusedElement() is TextBox;

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (_dialogs.IsOpen)
        {
            if (_dialogs.HandleKey(e)) e.Handled = true;
            return;
        }
        if (IsTyping)
        {
            if (e.Key == Key.Escape)
            {
                Canvas.Focus();
                e.Handled = true;
            }
            return;
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool handled = true;
        var input = Canvas.Input!;

        switch (e.Key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Redo();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.N when ctrl:
                _ = NewAsync();
                break;
            case Key.O when ctrl:
                _ = OpenAsync();
                break;
            case Key.S when ctrl && shift:
                _ = SaveAsAsync();
                break;
            case Key.S when ctrl:
                _ = SaveAsync();
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                Canvas.FitToView();
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                Canvas.ZoomToActualPixels();
                break;
            case Key.OemPlus or Key.Add when ctrl:
                Canvas.ZoomStep(+1);
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                Canvas.ZoomStep(-1);
                break;
            case Key.B when !ctrl:
                Session.SelectTool(ToolKind.Brush);
                break;
            case Key.E when !ctrl:
                Session.SelectTool(ToolKind.Eraser);
                break;
            case Key.I when !ctrl:
                Session.SelectTool(ToolKind.Eyedropper);
                break;
            case Key.H when !ctrl:
                Session.SelectTool(ToolKind.Hand);
                break;
            case Key.X when !ctrl:
                Session.SwapColors();
                break;
            case Key.D when !ctrl:
                Session.ResetColors();
                ShowToast("Default colours");
                break;
            case Key.OemOpenBrackets:
                Session.StepSize(-1);
                ShowToast($"Size {Session.CurrentPaintSettings.Size:0} px");
                break;
            case Key.OemCloseBrackets:
                Session.StepSize(+1);
                ShowToast($"Size {Session.CurrentPaintSettings.Size:0} px");
                break;
            case Key.Tab:
                SetUiHidden(!_uiHidden);
                break;
            case Key.F11:
                ToggleFullScreen();
                break;
            case Key.F12:
                ToggleDiagnostics();
                break;
            case Key.Escape when WindowState == WindowState.FullScreen:
                ToggleFullScreen();
                break;
            case Key.Delete when !ctrl:
                ClearCanvas();
                break;
            case Key.Space:
                if (!input.SpaceHeld)
                {
                    input.SpaceHeld = true;
                    Canvas.RefreshCursor();
                }
                break;
            case Key.LeftAlt or Key.RightAlt:
                input.AltHeld = true;
                Canvas.RefreshCursor();
                break;
            default:
                // Non-US keyboards: accept the characters themselves for the brush-size keys.
                if (e.KeySymbol == "[") Session.StepSize(-1);
                else if (e.KeySymbol == "]") Session.StepSize(+1);
                else handled = false;
                break;
        }
        if (handled) e.Handled = true;
    }

    private void OnKeyUpTunnel(object? sender, KeyEventArgs e)
    {
        var input = Canvas.Input!;
        if (e.Key == Key.Space)
        {
            input.SpaceHeld = false;
            Canvas.RefreshCursor();
            e.Handled = true;
        }
        else if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            input.AltHeld = false;
            Canvas.RefreshCursor();
            e.Handled = true; // keep Alt from activating the window menu
        }
    }

    // ================================================================== menu

    private Control BuildMenu()
    {
        var stack = new StackPanel { Spacing = 1, Width = 262 };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 2, 8, 8) };
        brand.Children.Add(new BrandMark());
        brand.Children.Add(new TextBlock { Text = "DoodleNook", Classes = { "brand" }, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = Version, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        stack.Children.Add(brand);

        void Item(string icon, string label, string? shortcut, Action action)
        {
            var b = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                Content = Ui.Content(icon, label, shortcut),
            };
            b.Classes.Add("menu");
            b.Click += (_, _) =>
            {
                MenuFlyout.Hide();
                action();
            };
            stack.Children.Add(b);
        }

        Item("new", "New picture…", "Ctrl+N", () => _ = NewAsync());
        Item("open", "Open…", "Ctrl+O", () => _ = OpenAsync());
        Item("save", "Save", "Ctrl+S", () => _ = SaveAsync());
        Item("saveas", "Save as…", "Ctrl+Shift+S", () => _ = SaveAsAsync());
        stack.Children.Add(Ui.HSep());
        Item("trash", "Clear canvas", "Delete", ClearCanvas);
        stack.Children.Add(Ui.HSep());
        Item("fit", "Fit on screen", "Ctrl+0", () => Canvas.FitToView());
        Item("zoom", "Actual pixels (100%)", "Ctrl+1", Canvas.ZoomToActualPixels);
        Item("flip", "Flip view", null, ToggleFlipView);
        Item("fullscreen", "Full screen", "F11", ToggleFullScreen);
        Item("expand", "Hide interface", "Tab", () => SetUiHidden(true));
        stack.Children.Add(Ui.HSep());
        Item("pulse", "Pen diagnostics", "F12", ToggleDiagnostics);
        Item("keyboard", "Keyboard shortcuts", null, () => _ = ShowShortcutsAsync());
        Item("info", "About DoodleNook", null, () => _ = ShowAboutAsync());
        stack.Children.Add(Ui.HSep());
        Item("exit", "Exit", "Alt+F4", Close);
        return stack;
    }

    private Task ShowShortcutsAsync()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 4, ColumnSpacing = 16 };
        (string Keys, string What)[] rows =
        [
            ("B / E", "Brush / Eraser"), ("I · hold Alt", "Eyedropper / quick pick"), ("H · hold Space", "Hand / pan"),
            ("[ and ]", "Smaller / larger brush"), ("Right-drag", "Resize brush on the canvas"), ("X / D", "Swap / default colours"),
            ("Ctrl+Z / Ctrl+Shift+Z", "Undo / Redo (also Ctrl+Y)"), ("Wheel", "Zoom at the cursor"), ("Middle-drag", "Pan"),
            ("Ctrl+0 / Ctrl+1", "Fit / actual pixels"), ("Ctrl+N / O / S", "New / Open / Save"), ("Tab · F11", "Hide interface · full screen"),
            ("Delete", "Clear canvas (undoable)"), ("F12", "Pen diagnostics"),
        ];
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = new TextBlock { Text = rows[i].Keys, FontWeight = FontWeight.SemiBold };
            var w = new TextBlock { Text = rows[i].What, Classes = { "dim" } };
            Grid.SetRow(k, i);
            Grid.SetRow(w, i);
            Grid.SetColumn(w, 1);
            grid.Children.Add(k);
            grid.Children.Add(w);
        }
        return _dialogs.ShowAsync("Keyboard shortcuts", grid, [("ok", "Close", true)], "ok", "ok");
    }

    private Task ShowAboutAsync() => _dialogs.ShowAsync("About DoodleNook",
        DialogHost.Text($"DoodleNook {Version} — Mission 004 “The Missing Ink”.\nA cosy place to sketch: one round brush, an eraser, undo and PNG files.\n\n" +
                        "Built with .NET, Avalonia and SkiaSharp (MIT licences). Fonts: Inter and Varela Round (SIL Open Font License). " +
                        "See THIRD-PARTY-NOTICES.txt next to DoodleNook.exe."),
        [("ok", "Close", true)], "ok", "ok");
}
