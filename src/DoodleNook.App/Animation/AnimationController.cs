using System.Diagnostics;
using Avalonia.Threading;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Animation;

/// <summary>
/// Playback and onion skins for Animation Studio.
/// <list type="bullet">
/// <item>Playback is time-based: each tick shows the frame that is due according to the frame durations
/// (<see cref="PlaybackClock"/>), so a slow redraw never queues up frames or drifts.</item>
/// <item>Editing during playback is predictable: touching the canvas, undoing or changing frames pauses playback on
/// the frame being shown, and the edit applies to that frame.</item>
/// <item>Stop pauses and returns to the frame where playback started.</item>
/// <item>Onion skins are shown only in Animation Studio and never while playing.</item>
/// </list>
/// </summary>
public sealed class AnimationController
{
    private readonly EditorSession _session;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(5) };
    private readonly Stopwatch _clock = new();
    private PlaybackClock? _playback;
    private int _startFrame;
    private bool _active;
    private bool _onionPending;
    private bool _settingFrame;
    private Document? _watched;

    public AnimationController(EditorSession session)
    {
        _session = session;
        _timer.Tick += (_, _) => Tick();
        session.DocumentReplaced += Watch;
        session.OnionChanged += RequestOnion;
        session.StateChanged += () =>
        {
            // Pixel and hand-drawn styles have their own onion settings.
            RequestOnion();
        };
        Watch();
    }

    public bool IsPlaying { get; private set; }

    /// <summary>For tests: playback advances only through <see cref="Tick"/> with a simulated time.</summary>
    internal bool ManualClock { get; set; }

    /// <summary>Raised when playback starts, pauses or stops.</summary>
    public event Action? PlaybackChanged;

    /// <summary>True while Animation Studio is the workspace (onion skins are shown only there).</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (!value) Pause();
            RequestOnion();
        }
    }

    private void Watch()
    {
        Pause();
        if (_watched is not null)
        {
            _watched.FrameChanged -= OnFrameChanged;
            _watched.AnimationChanged -= RequestOnion;
            _watched.ActiveLayerChanged -= RequestOnion;
            _watched.HistoryChanged -= OnHistoryChanged;
            _watched.LayersChanged -= RequestOnion;
        }
        _watched = _session.Document;
        _watched.FrameChanged += OnFrameChanged;
        _watched.AnimationChanged += RequestOnion;
        _watched.ActiveLayerChanged += RequestOnion;
        _watched.HistoryChanged += OnHistoryChanged;
        _watched.LayersChanged += RequestOnion;
        RequestOnion();
    }

    private void OnFrameChanged()
    {
        if (IsPlaying && !_settingFrame) Pause(); // someone else changed the frame (undo, timeline click…)
        RequestOnion();
    }

    private void OnHistoryChanged()
    {
        if (IsPlaying) Pause();
        RequestOnion();
    }

    // ------------------------------------------------------------------ playback

    public void Toggle()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        var doc = _session.Document;
        if (IsPlaying || doc.Animation is not { FrameCount: > 1 } a) return;
        _session.EndStroke();
        _session.CommitTransform();
        _startFrame = a.CurrentFrame;
        // A finished, non-looping animation starts again from the beginning.
        int from = !a.Loop && a.CurrentFrame == a.FrameCount - 1 ? 0 : a.CurrentFrame;
        _playback = PlaybackClock.For(a, from);
        IsPlaying = true;
        doc.SetOnionSkin(null, PixelRect.Empty);
        _clock.Restart();
        if (!ManualClock) _timer.Start();
        PlaybackChanged?.Invoke();
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        _timer.Stop();
        _clock.Stop();
        PlaybackChanged?.Invoke();
        RequestOnion();
    }

    /// <summary>Stops and goes back to the frame where playback started.</summary>
    public void Stop()
    {
        bool was = IsPlaying;
        Pause();
        if (was) _session.Document.SetCurrentFrame(_startFrame);
    }

    /// <summary>Advances playback to the frame due now (also called by tests with a simulated time).</summary>
    internal void Tick(double? elapsedMs = null)
    {
        if (!IsPlaying || _playback is null) return;
        int frame = _playback.FrameAt(elapsedMs ?? _clock.Elapsed.TotalMilliseconds);
        var doc = _session.Document;
        if (frame < 0)
        {
            // A non-looping animation ends on its last frame (ping-pong: back on the first).
            int last = doc.Animation is { PingPong: true } ? 0 : doc.FrameCount - 1;
            _settingFrame = true;
            try
            {
                doc.SetCurrentFrame(last);
            }
            finally
            {
                _settingFrame = false;
            }
            Pause();
            return;
        }
        if (frame == doc.CurrentFrame) return;
        _settingFrame = true;
        try
        {
            doc.SetCurrentFrame(frame);
        }
        finally
        {
            _settingFrame = false;
        }
    }

    // ------------------------------------------------------------------ onion skins

    private void RequestOnion()
    {
        if (_onionPending) return;
        _onionPending = true;
        Dispatcher.UIThread.Post(UpdateOnion, DispatcherPriority.Background);
    }

    /// <summary>Rebuilds the onion-skin overlay now (display only).</summary>
    public void UpdateOnion()
    {
        _onionPending = false;
        var doc = _session.Document;
        if (!_active || IsPlaying || !doc.IsAnimated || !_session.Onion.Enabled)
        {
            if (doc.OnionVisible) doc.SetOnionSkin(null, PixelRect.Empty);
            return;
        }
        var (image, bounds) = OnionSkin.Build(doc, _session.Onion);
        doc.SetOnionSkin(image, bounds);
    }
}
