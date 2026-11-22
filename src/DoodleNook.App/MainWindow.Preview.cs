using DoodleNook.App.Animation;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;

namespace DoodleNook.App;

/// <summary>
/// Mission 015 — the Live Animation Preview's place (docked in the corner of Animation Studio's canvas, or floating in
/// its own window) and the background shown behind transparent areas of the canvas. Both are remembered.
/// </summary>
public partial class MainWindow
{
    private PreviewWindow? _previewWindow;

    internal PreviewSettings PreviewSettings => _settings.Preview;

    /// <summary>The preview's own window while it floats (tests).</summary>
    internal PreviewWindow? PreviewWindow => _previewWindow;

    /// <summary>Whether the preview is open (View › Animation preview), docked or floating.</summary>
    public bool PreviewOpen => AnimationState.IsShown(CustomOptions.PreviewPanel, WorkspaceKind.Animation);

    /// <summary>Puts the preview in its own window, or back in the corner of the canvas.</summary>
    public void SetPreviewFloating(bool floating)
    {
        PreviewSettings.Floating = floating;
        // Docking it outside Animation Studio, where it has no corner, takes you there.
        if (!floating && PreviewOpen && Workspace.Kind != WorkspaceKind.Animation) SetWorkspace(WorkspaceKind.Animation);
        else ApplyChromeVisibility();
    }

    /// <summary>Shows the preview where it belongs now (called whenever the workspace or its chrome changes).</summary>
    private void PlacePreview(bool uiHidden, WorkspaceKind kind)
    {
        bool open = PreviewOpen;
        if (open && PreviewSettings.Floating)
        {
            if (_previewWindow is null)
            {
                CanvasHost.Children.Remove(_animationPreview);
                _animationPreview.Float();
                _previewWindow = new PreviewWindow(this, _animationPreview);
                _previewWindow.Show(this);
            }
            _animationPreview.IsVisible = true;
            return;
        }
        CloseFloatingPreview();
        _animationPreview.IsVisible = open && !uiHidden && kind == WorkspaceKind.Animation;
    }

    /// <summary>The preview window's × was clicked: the preview closes and goes back to its corner (hidden) for next time.</summary>
    internal void PreviewWindowClosedByUser()
    {
        if (_previewWindow is null) return;
        _previewWindow = null;
        _animationPreview.Dock();
        if (_animationPreview.Parent is null) CanvasHost.Children.Add(_animationPreview);
        PreviewSettings.Floating = true; // it was floating: next time it opens in its own window again
        SetPreviewPanel(false);
    }

    private void CloseFloatingPreview()
    {
        if (_previewWindow is not { } window) return;
        _previewWindow = null;
        window.ClosingFromApp = true;
        window.Content = null;
        window.Close();
        _animationPreview.Dock();
        if (_animationPreview.Parent is null) CanvasHost.Children.Add(_animationPreview);
    }

    // ================================================================== the canvas background

    public BackdropSetting CanvasBackdrop => _settings.CanvasBackdrop;

    /// <summary>What the canvas shows behind transparent areas (display only: never part of the picture or an export).</summary>
    public void SetCanvasBackdrop(BackdropSetting backdrop)
    {
        _settings.CanvasBackdrop = backdrop;
        Canvas.SetBackdrop(Backdrops.SolidColour(backdrop), Backdrops.PicturePixels(backdrop));
        ShowToast($"Behind transparent areas: {Backdrops.Describe(backdrop)} (display only)");
    }

    private void ApplyCanvasBackdrop() =>
        Canvas.SetBackdrop(Backdrops.SolidColour(_settings.CanvasBackdrop), Backdrops.PicturePixels(_settings.CanvasBackdrop));

    /// <summary>The View menu's preview and background entries.</summary>
    private IEnumerable<MenuEntry> PreviewViewEntries()
    {
        yield return new("film", "Animation preview", null, () => SetPreviewPanel(!PreviewOpen), Checked: PreviewOpen);
        yield return new(null, "Preview in its own window", null, () =>
        {
            if (!PreviewOpen)
            {
                PreviewSettings.Floating = true;
                SetPreviewPanel(true);
            }
            else SetPreviewFloating(!PreviewSettings.Floating);
        }, Checked: PreviewOpen && PreviewSettings.Floating);
        yield return MenuEntry.Sub("paper", $"Behind transparent areas: {Backdrops.Describe(_settings.CanvasBackdrop)}",
            Backdrops.Menu(this, _settings.CanvasBackdrop, SetCanvasBackdrop).ToList());
    }
}
