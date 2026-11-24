using Avalonia.Controls;
using Avalonia.Input;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Operations;

namespace DoodleNook.App;

/// <summary>Keyboard shortcuts (Photoshop-style where there is an equivalent).</summary>
public partial class MainWindow
{
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
        if (StorybookShown && HandleStorybookKey(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }
        if (HandleKey(e.Key, e.KeyModifiers, e.KeySymbol)) e.Handled = true;
    }

    /// <summary>While the storybook is shown, undo and redo are the storybook's (the drawing underneath is left alone),
    /// Page Up / Page Down turn pages and Esc goes back to the drawing. Mission 016: no other shortcut reaches the
    /// drawing out of sight (Delete, Ctrl+V or Ctrl+J used to change it unseen); New and Open go back to drawing first,
    /// Ctrl+S says the storybook saves itself, and Help, full screen and Tab work as everywhere.</summary>
    internal bool HandleStorybookKey(Key key, KeyModifiers mods)
    {
        bool ctrl = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        var view = _storybookView!;
        switch (key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                view.Redo();
                return true;
            case Key.Z when ctrl:
                view.Undo();
                return true;
            case Key.PageDown:
                view.GoTo(view.PageIndex + 1);
                return true;
            case Key.PageUp:
                view.GoTo(view.PageIndex - 1);
                return true;
            case Key.Escape:
                HideStorybook();
                return true;
            case Key.N or Key.O when ctrl:
                HideStorybook(); // a new or another picture: shown, not hidden behind the storybook
                return false;
            case Key.S when ctrl:
                ShowToast("The storybook is saved as you go · Esc goes back to the drawing");
                return true;
            case Key.F1 or Key.F11 or Key.F12 or Key.Tab:
                return false;
            default:
                return true; // nothing else may change the drawing out of sight
        }
    }

    /// <summary>Runs the shortcut for a key. Returns false when the key isn't a shortcut here.</summary>
    internal bool HandleKey(Key key, KeyModifiers mods, string? symbol = null)
    {
        bool ctrl = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        var input = Canvas.Input!;
        var s = Session;

        // Workspaces: Alt+1 … Alt+6.
        if (alt && !ctrl && key is >= Key.D1 and <= Key.D6)
        {
            input.AltHeld = false;
            SetWorkspace((WorkspaceKind)(key - Key.D1));
            return true;
        }

        if (_radial.IsOpen && key == Key.Escape)
        {
            _radial.Close();
            return true;
        }

        // Mission 018: hold B, E or Z for the quick adjust card. Esc while it is open puts everything back.
        if (_quickAdjust!.IsOpen && key == Key.Escape)
        {
            EndQuickAdjust(keep: false);
            return true;
        }
        if (!ctrl && !alt && !shift && key is Key.B or Key.E or Key.Z && QuickAdjustKeyDown(key)) return true;

        if (Workspace.Kind == WorkspaceKind.Animation && HandleAnimationKey(key, ctrl, shift, alt, symbol)) return true;

        switch (key)
        {
            // ---------------------------------------------------------- history and files
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Redo();
                return true;
            case Key.Z when ctrl:
                Undo();
                return true;
            case Key.N when ctrl && shift:
                s.AddLayer();
                ShowToast("New layer");
                return true;
            case Key.N when ctrl:
                _ = NewAsync();
                return true;
            case Key.O when ctrl:
                _ = OpenAsync();
                return true;
            case Key.S when ctrl && shift:
                _ = SaveAsAsync();
                return true;
            case Key.S when ctrl:
                _ = SaveAsync();
                return true;

            // ---------------------------------------------------------- view
            case Key.D0 or Key.NumPad0 when ctrl:
                Canvas.FitToView();
                return true;
            case Key.D1 or Key.NumPad1 when ctrl:
                Canvas.ZoomToActualPixels();
                return true;
            case Key.OemPlus or Key.Add when ctrl:
                Canvas.ZoomStep(+1);
                return true;
            case Key.OemMinus or Key.Subtract when ctrl:
                Canvas.ZoomStep(-1);
                return true;
            case Key.OemQuotes when ctrl:
                SetShowGrid(!WsState.ShowGrid);
                return true;
            case Key.Tab:
                if (Workspace.Kind == WorkspaceKind.PureSketch) ReturnFromPureSketch();
                else SetUiHidden(!UiHidden);
                return true;
            case Key.F1:
                _ = ShowShortcutsAsync();
                return true;
            case Key.F11:
                ToggleFullScreen();
                return true;
            case Key.F12:
                ToggleDiagnostics();
                return true;

            // ---------------------------------------------------------- confirm / cancel
            case Key.Enter when s.ActiveLiquify is not null:
                ApplyLiquify();
                return true;
            case Key.Escape when s.ActiveLiquify is not null:
                CancelLiquify();
                return true;
            case Key.X when ctrl && shift:
                _ = StartLiquifyAsync();
                return true;
            case Key.Enter:
                if (s.ActiveTransform is not null) ApplyTransform();
                else if (!ApplyCrop()) return false;
                return true;
            case Key.Escape when CatalogShown && EditingGameImage:
                HideCatalog();
                return true;
            case Key.Escape:
                if (s.ActiveTransform is not null) CancelTransform();
                else if (CancelCrop()) { }
                else if (WindowState == WindowState.FullScreen) ToggleFullScreen();
                else if (UiKit.AnyFlyoutOpen) HideFlyouts();
                else return false;
                return true;

            // ---------------------------------------------------------- selection and editing
            case Key.I when ctrl && alt:
                _ = ImageSizeAsync();
                return true;
            case Key.C when ctrl && alt:
                _ = CanvasSizeAsync();
                return true;
            case Key.A when ctrl:
                s.SelectAll();
                return true;
            case Key.D when ctrl:
                s.Deselect();
                return true;
            case Key.I when ctrl && shift:
                s.InvertSelection();
                return true;
            case Key.T when ctrl:
                FreeTransform();
                return true;
            case Key.C when ctrl:
                Copy(cut: false);
                return true;
            case Key.X when ctrl:
                Copy(cut: true);
                return true;
            case Key.V when ctrl:
                Paste();
                return true;
            case Key.J when ctrl:
                s.DuplicateLayer();
                ShowToast("Layer duplicated");
                return true;
            case Key.G when ctrl && shift:
                s.Ungroup();
                return true;
            case Key.G when ctrl:
                s.GroupActiveLayer();
                ShowToast("Grouped · drag layers onto the group to add them");
                return true;
            case Key.R when ctrl:
                if (Workspace.Kind != WorkspaceKind.PureSketch) SetShowRulers(!WsState.ShowRulers);
                return true;
            case Key.OemSemicolon when ctrl && shift:
                s.SetAssistOptions(s.Assist with { Snap = !s.Assist.Snap });
                ShowToast(s.Assist.Snap ? "Snapping on" : "Snapping off");
                return true;
            case Key.OemSemicolon when ctrl:
                s.SetShowGuides(!s.ShowGuides);
                ShowToast(s.ShowGuides ? "Guides shown" : "Guides hidden");
                return true;
            case Key.L when ctrl:
                _ = AdjustAsync(AdjustKind.Levels);
                return true;
            case Key.B when ctrl:
                _ = AdjustAsync(AdjustKind.ColourBalance);
                return true;
            case Key.E when ctrl:
                s.MergeDown();
                return true;
            case Key.OemOpenBrackets when ctrl:
                s.MoveLayer(-1);
                return true;
            case Key.OemCloseBrackets when ctrl:
                s.MoveLayer(+1);
                return true;
            case Key.U when ctrl && shift:
                ApplyAdjustment(new Desaturate());
                return true;
            case Key.U when ctrl:
                _ = AdjustAsync(AdjustKind.HueSaturation);
                return true;
            case Key.I when ctrl:
                ApplyAdjustment(new Invert());
                return true;
            case Key.Back when alt:
                s.FillSelection();
                return true;
            case Key.Back when ctrl:
                s.FillSelection(background: true);
                return true;
            case Key.Delete or Key.Back when !ctrl:
                ClearSelectionOrLayer();
                return true;
            case Key.Left or Key.Right or Key.Up or Key.Down when s.Tool == ToolKind.Move || s.ActiveTransform is not null:
            {
                int step = shift ? 10 : 1;
                int dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
                int dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
                Nudge(dx, dy);
                return true;
            }

            // ---------------------------------------------------------- colours and size
            case Key.X when !ctrl:
                s.SwapColors();
                return true;
            case Key.D when !ctrl:
                s.ResetColors();
                ShowToast("Default colours");
                return true;
            case Key.OemOpenBrackets:
                s.StepSize(-1);
                ShowSizeToast();
                return true;
            case Key.OemCloseBrackets:
                s.StepSize(+1);
                ShowSizeToast();
                return true;

            // ---------------------------------------------------------- held keys
            case Key.Space:
                if (!input.SpaceHeld)
                {
                    input.SpaceHeld = true;
                    Canvas.RefreshCursor();
                }
                return true;
            case Key.R when !ctrl:
                if (!input.RotateHeld)
                {
                    input.RotateHeld = true;
                    Canvas.RefreshCursor();
                }
                return true;
            case Key.LeftAlt or Key.RightAlt:
                input.AltHeld = true;
                Canvas.RefreshCursor();
                return true;
        }

        // ---------------------------------------------------------- tool letters
        if (!ctrl && !alt && key is >= Key.A and <= Key.Z)
        {
            var letter = key.ToString();
            if (Workspaces.ToolForKey(CurrentTools, letter, shift) is { } tool)
            {
                s.SelectTool(tool);
                return true;
            }
            return false;
        }

        // Non-US keyboards: accept the characters themselves for the brush-size keys.
        if (symbol == "[")
        {
            s.StepSize(-1);
            ShowSizeToast();
            return true;
        }
        if (symbol == "]")
        {
            s.StepSize(+1);
            ShowSizeToast();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Animation Studio's keys. They only apply in Animation Studio and never replace an existing shortcut: Enter still
    /// applies a transform or crop first, and the arrows still nudge with the Move tool or during a transform.
    /// </summary>
    private bool HandleAnimationKey(Key key, bool ctrl, bool shift, bool alt, string? symbol)
    {
        var s = Session;
        bool busy = s.ActiveTransform is not null || Canvas.Input?.PendingCrop is not null;
        if (ctrl || alt) return false;
        switch (key)
        {
            case Key.Enter when !busy:
                Animator.Toggle();
                return true;
            case Key.Escape when Animator.IsPlaying && !busy:
                Animator.Stop();
                return true;
            case Key.OemComma:
            case Key.Left when !busy && s.Tool != ToolKind.Move:
                Animator.Pause();
                s.StepFrame(-1);
                return true;
            case Key.OemPeriod:
            case Key.Right when !busy && s.Tool != ToolKind.Move:
                Animator.Pause();
                s.StepFrame(+1);
                return true;
            case Key.Home:
                Animator.Pause();
                s.GoToFrame(0);
                return true;
            case Key.End:
                Animator.Pause();
                s.GoToFrame(s.Document.FrameCount - 1);
                return true;
            case Key.N:
                Animator.Pause();
                s.NewFrame(duplicate: shift);
                return true;
            case Key.O when !shift:
                s.ToggleOnion();
                ShowToast(s.Onion.Enabled ? "Onion skin on" : "Onion skin off");
                return true;
        }
        // Non-US keyboards: the characters themselves.
        if (symbol == "," || symbol == ".")
        {
            Animator.Pause();
            s.StepFrame(symbol == "," ? -1 : +1);
            return true;
        }
        return false;
    }

    private void OnKeyUpTunnel(object? sender, KeyEventArgs e)
    {
        var input = Canvas.Input!;
        QuickAdjustKeyUp(e.Key);
        if (e.Key == Key.Space)
        {
            input.SpaceHeld = false;
            Canvas.RefreshCursor();
            e.Handled = true;
        }
        else if (e.Key == Key.R)
        {
            input.RotateHeld = false;
            Canvas.RefreshCursor();
        }
        else if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            input.AltHeld = false;
            Canvas.RefreshCursor();
            e.Handled = true; // keep Alt from activating the window menu
        }
    }

    /// <summary>What [ and ] changed. Mission 017: the stamp tool's size is its own (it used to report the brush's).</summary>
    private void ShowSizeToast()
    {
        var s = Session;
        if (s.Tool == ToolKind.Stamp)
            ShowToast(s.PixelMode ? $"Stamp size {Math.Max(1, Math.Round(s.StampOptions.Scale)):0}×" : $"Stamp size {s.StampOptions.Scale * 100:0}%");
        else ShowToast($"Size {s.CurrentSize:0} px");
    }
}
