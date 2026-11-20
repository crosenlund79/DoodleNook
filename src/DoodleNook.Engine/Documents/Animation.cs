using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// One drawing in an animation. A cel can be exposed on several consecutive frames of its layer's track: that is a
/// <em>hold</em>, and editing it changes every frame of the hold (it is one drawing). Making an independent copy is
/// an explicit command (break hold / duplicate drawing).
/// </summary>
public sealed class Cel
{
    private static int _nextId;

    internal Cel(RasterImage image, PixelRect? bounds = null)
    {
        Id = Interlocked.Increment(ref _nextId);
        Image = image;
        ContentBounds = bounds ?? image.ContentBounds();
    }

    /// <summary>Unique within this run of the program (files store their own numbering).</summary>
    public int Id { get; }

    public RasterImage Image { get; internal set; }

    /// <summary>Contains all painted pixels (may be larger, never smaller).</summary>
    public PixelRect ContentBounds { get; internal set; }

    /// <summary>Incremented when the pixels change (thumbnails and the frame cache use it).</summary>
    public int Revision { get; internal set; }

    public bool IsEmpty => ContentBounds.IsEmpty;

    internal Cel CloneCel() => new(Image.Clone(), ContentBounds);

    public override string ToString() => $"Cel {Id}";
}

/// <summary>Frame timing and playback settings of an animated document. The frames themselves are columns of the
/// layers' tracks (<see cref="Layer.Track"/>).</summary>
public sealed class Animation
{
    public const double DefaultFps = 12;
    public const double MinFps = 1;
    public const double MaxFps = 60;

    internal Animation(double fps, int frames = 1)
    {
        Fps = Math.Clamp(fps, MinFps, MaxFps);
        Durations = Enumerable.Repeat<int?>(null, Math.Max(1, frames)).ToList();
    }

    /// <summary>Frames per second; a frame lasts 1/fps unless it has its own duration.</summary>
    public double Fps { get; internal set; }

    /// <summary>Playback repeats from the start (or bounces with <see cref="PingPong"/>).</summary>
    public bool Loop { get; internal set; } = true;

    /// <summary>Playback runs forwards then backwards.</summary>
    public bool PingPong { get; internal set; }

    /// <summary>Per-frame duration overrides in milliseconds (null = 1/fps).</summary>
    internal List<int?> Durations { get; set; }

    public int FrameCount => Durations.Count;

    /// <summary>The frame shown and edited (the playhead).</summary>
    public int CurrentFrame { get; internal set; }

    public int DefaultDurationMs => (int)Math.Round(1000.0 / Fps);

    /// <summary>The frame's own duration override, or null when it uses the frame rate.</summary>
    public int? DurationOverride(int frame) => Durations[frame];

    /// <summary>How long a frame is shown, in milliseconds.</summary>
    public int DurationOf(int frame) => Durations[frame] ?? DefaultDurationMs;

    /// <summary>Total running time of one pass.</summary>
    public int TotalDurationMs => Enumerable.Range(0, FrameCount).Sum(DurationOf);
}

/// <summary>Everything about an animation's structure (frames, timing, tracks) at one moment — cheap, because cels are
/// referenced, not copied. Frame and cel commands are undone by swapping these.</summary>
internal sealed class AnimationStructure
{
    public required double Fps { get; init; }
    public required bool Loop { get; init; }
    public required bool PingPong { get; init; }
    public required int CurrentFrame { get; init; }
    public required List<int?> Durations { get; init; }
    public required Dictionary<int, List<Cel?>> Tracks { get; init; }

    public IEnumerable<Cel> AllCels => Tracks.Values.SelectMany(t => t).OfType<Cel>().Distinct();
}

/// <summary>A frame/cel/timing change, recorded as before and after structures.</summary>
internal sealed class AnimationStructureAction : IHistoryAction
{
    private readonly AnimationStructure _before;
    private readonly AnimationStructure _after;

    public AnimationStructureAction(string name, AnimationStructure before, AnimationStructure after)
    {
        Name = name;
        _before = before;
        _after = after;
        var old = before.AllCels.ToHashSet();
        // Memory held by drawings this step created (copies, pasted cels).
        ByteSize = after.AllCels.Where(c => !old.Contains(c)).Sum(c => (long)c.Image.Pixels.Length * 4) + 256
                   + before.Tracks.Values.Sum(t => t.Count) * 16L;
    }

    public string Name { get; }
    public long ByteSize { get; }
    public void Undo(Document d) => d.ApplyStructureInternal(_before);
    public void Redo(Document d) => d.ApplyStructureInternal(_after);
}

/// <summary>Turning a still picture into an animation (each layer's pixels become its first cel).</summary>
internal sealed class MakeAnimationAction(double fps, IReadOnlyDictionary<int, Cel?> firstCels) : IHistoryAction
{
    public string Name => "Start animation";
    public long ByteSize => 256;
    public void Undo(Document d) => d.RemoveAnimationInternal();
    public void Redo(Document d) => d.CreateAnimationInternal(fps, firstCels);
}

/// <summary>Drawing on an empty cell made a new cel there.</summary>
internal sealed class AdoptCelAction(int layerId, int frame, Cel cel) : IHistoryAction
{
    public string Name => "New drawing";
    public long ByteSize => 64; // the pixels are counted by the paint step it belongs to
    public void Undo(Document d) => d.SetCellInternal(layerId, frame, null);
    public void Redo(Document d) => d.SetCellInternal(layerId, frame, cel);
}

/// <summary>Onion-skin display settings (a preference, not part of the document).</summary>
public sealed record OnionSkinSettings(
    bool Enabled = true,
    int Before = 1,
    int After = 1,
    double Opacity = 0.35,
    ColorRgb BeforeTint = default,
    ColorRgb AfterTint = default,
    bool Falloff = true,
    bool AllLayers = false)
{
    public static readonly ColorRgb DefaultBeforeTint = new(214, 92, 74);
    public static readonly ColorRgb DefaultAfterTint = new(72, 132, 204);

    public static OnionSkinSettings Default => new(BeforeTint: DefaultBeforeTint, AfterTint: DefaultAfterTint);

    /// <summary>Clamps out-of-range values (e.g. from an edited settings file).</summary>
    public OnionSkinSettings Normalized() => this with
    {
        Before = Math.Clamp(Before, 0, 5),
        After = Math.Clamp(After, 0, 5),
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.05, 1) : 0.35,
    };
}

/// <summary>
/// Turns elapsed playback time into a frame index from the frame durations. Time is measured from the start of
/// playback, so frame timing never drifts and nothing queues up if the screen is slow: a late redraw simply shows
/// the frame that is due now.
/// </summary>
public sealed class PlaybackClock
{
    private readonly int[] _durations;
    private readonly int[] _starts;

    public PlaybackClock(IReadOnlyList<int> durationsMs, bool loop, bool pingPong, int startFrame = 0)
    {
        if (durationsMs.Count == 0) throw new ArgumentException("An animation has at least one frame.", nameof(durationsMs));
        _durations = durationsMs.Select(d => Math.Max(1, d)).ToArray();
        _starts = new int[_durations.Length];
        for (int i = 1; i < _durations.Length; i++) _starts[i] = _starts[i - 1] + _durations[i - 1];
        Loop = loop;
        PingPong = pingPong && _durations.Length > 1;
        StartFrame = Math.Clamp(startFrame, 0, _durations.Length - 1);
    }

    public static PlaybackClock For(Animation a, int startFrame) =>
        new(Enumerable.Range(0, a.FrameCount).Select(a.DurationOf).ToList(), a.Loop, a.PingPong, startFrame);

    public bool Loop { get; }
    public bool PingPong { get; }
    public int StartFrame { get; }
    public int FrameCount => _durations.Length;
    public int PassDurationMs => _starts[^1] + _durations[^1];

    /// <summary>The frame to show <paramref name="elapsedMs"/> after playback started, or -1 when a non-looping
    /// animation has finished.</summary>
    public int FrameAt(double elapsedMs)
    {
        double t = Math.Max(0, elapsedMs) + _starts[StartFrame];
        int pass = PassDurationMs;
        if (!PingPong)
        {
            if (t >= pass)
            {
                if (!Loop) return -1;
                t %= pass;
            }
            return Locate(t);
        }
        // Ping-pong: forwards over all frames, then backwards over the inner frames (no doubled end frames).
        int back = pass - _durations[^1] - _durations[0];
        int cycle = pass + back;
        if (t >= cycle && !Loop) return -1;
        t %= cycle;
        if (t < pass) return Locate(t);
        double r = t - pass; // walking back from the second-last frame
        for (int i = _durations.Length - 2; i >= 1; i--)
        {
            if (r < _durations[i]) return i;
            r -= _durations[i];
        }
        return 0;
    }

    private int Locate(double t)
    {
        int lo = 0, hi = _starts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_starts[mid] <= t) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }
}
