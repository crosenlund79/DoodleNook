using Avalonia.Input;
using Avalonia.Threading;
using DoodleNook.App.Input;
using DoodleNook.App.Shell;

namespace DoodleNook.App;

/// <summary>
/// Mission 018 — the quick adjust card (see <see cref="QuickAdjustPanel"/>): hold B, E or Z. A tap of B or E still picks
/// the tool at once; holding it a moment longer opens the card at the pen; letting go closes it and keeps the change.
/// </summary>
public partial class MainWindow
{
    private QuickAdjustPanel? _quickAdjust;
    private Key? _quickKey;
    private Key? _quickCancelled; // Esc closed the card: the still-held key stays quiet until it is let go
    private DispatcherTimer? _quickTimer;

    internal QuickAdjustPanel QuickAdjust => _quickAdjust!;

    private void InitQuickAdjust()
    {
        _quickAdjust = new QuickAdjustPanel(this);
        CanvasHost.Children.Add(_quickAdjust);
        _quickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(QuickAdjustPanel.HoldDelayMs) };
        _quickTimer.Tick += (_, _) => OpenQuickAdjustIfHeld();
        Deactivated += (_, _) => EndQuickAdjust(keep: true); // the key-up may never arrive
    }

    /// <summary>B, E or Z pressed (no modifiers). Returns true when the press is used up here: a repeat while the key is
    /// held (it mustn't pick the tool again and again), or Z, which has no other meaning on its own.</summary>
    private bool QuickAdjustKeyDown(Key key)
    {
        if (_quickKey == key || _quickCancelled == key) return true;
        if (_quickAdjust!.IsOpen) return true; // another of the keys while the card is open: ignored
        _quickKey = key;
        _quickTimer!.Stop();
        _quickTimer.Start();
        return key == Key.Z;
    }

    /// <summary>The key has been held long enough: opens the card at the pen (not during a stroke or a dialog).</summary>
    internal void OpenQuickAdjustIfHeld()
    {
        _quickTimer?.Stop();
        if (_quickKey is not { } key || _quickAdjust!.IsOpen) return;
        if (Canvas.Input?.Gesture is not (null or Gesture.None)) return;
        if (_dialogs.IsOpen || DedicatedViewShown || _radial.IsOpen || IsTyping) return;
        var (x, y) = Canvas.Input?.Hover ?? (Canvas.Bounds.Width / 2, Canvas.Bounds.Height / 2);
        HideFlyouts();
        _quickAdjust.Open(key, x, y);
    }

    private void QuickAdjustKeyUp(Key key)
    {
        if (_quickCancelled == key) _quickCancelled = null;
        if (_quickKey != key) return;
        EndQuickAdjust(keep: true);
    }

    /// <summary>Closes the card (if open) and forgets the held key.</summary>
    internal void EndQuickAdjust(bool keep)
    {
        if (!keep && _quickKey is { } held) _quickCancelled = held;
        _quickKey = null;
        _quickTimer?.Stop();
        _quickAdjust?.Close(keep);
    }
}
