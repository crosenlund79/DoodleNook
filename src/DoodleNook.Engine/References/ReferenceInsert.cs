using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.References;

/// <summary>
/// How a library reference cycle sits in an animation: which cycle, the view and facing, the figure's height and the
/// point its feet stand on, the frame it starts at, how many frames each drawing lasts, whether it repeats to the end,
/// whether the animation may grow so the cycle fits, the anchor marker, and hard pixel edges. Saved with the layer so
/// the cycle can be adjusted later.
/// </summary>
public sealed record ReferenceCycleSettings(
    string CycleId,
    ReferenceView View,
    ReferenceFacing Facing,
    double Height,
    double AnchorX,
    double AnchorY,
    int StartFrame = 0,
    int Hold = 1,
    bool Repeat = true,
    bool Extend = true,
    bool Marker = true,
    bool HardEdges = false)
{
    public ReferenceFraming Framing => new(View, Facing, Height, AnchorX, AnchorY, HardEdges, Marker);

    /// <summary>The settings made safe: a known view and a facing that view offers, sizes and frames in range.</summary>
    public ReferenceCycleSettings Normalized()
    {
        var view = Enum.IsDefined(View) ? View : ReferenceView.Side;
        var facing = ReferenceRenderer.FacingsFor(view).Contains(Facing) ? Facing : ReferenceRenderer.DefaultFacing(view);
        return this with
        {
            View = view,
            Facing = facing,
            Height = double.IsFinite(Height) ? Math.Clamp(Height, 8, 4096) : 128,
            AnchorX = double.IsFinite(AnchorX) ? AnchorX : 0,
            AnchorY = double.IsFinite(AnchorY) ? AnchorY : 0,
            StartFrame = Math.Max(0, StartFrame),
            Hold = Math.Clamp(Hold, 1, 8),
        };
    }
}

/// <summary>Where a cycle lands in the timeline: the frames it covers (<see cref="Start"/> up to, not including,
/// <see cref="End"/>), the animation's length afterwards and how many frames that adds.</summary>
public sealed record ReferencePlan(int Start, int End, int TotalFrames, int AddedFrames, int Drawings, int Hold)
{
    public int Covered => End - Start;

    /// <summary>How many times the whole cycle plays (may be partial).</summary>
    public double Plays => Drawings * Hold == 0 ? 0 : (double)Covered / (Drawings * Hold);
}

/// <summary>
/// Mission 013: turns a library cycle into an animation layer's drawings. Each drawing is rendered once at the chosen
/// size; frames that show the same drawing (holds and repeats) share it, so a long repeat costs no extra memory.
/// </summary>
public static class ReferenceInsert
{
    public const double DefaultOpacity = 0.35;

    /// <summary>
    /// The timeline rules. The cycle starts at the chosen frame; each drawing lasts <c>Hold</c> frames. A repeating cycle
    /// fills the animation to its end; one that plays once covers its own length. If the animation is shorter than that
    /// and growing is allowed, frames are added at the end (the other layers hold their last drawing there); otherwise
    /// the cycle stops at the last frame. Existing frames are never removed or changed.
    /// </summary>
    public static ReferencePlan Plan(ReferenceCycle cycle, ReferenceCycleSettings settings, int documentFrames)
    {
        var s = settings.Normalized();
        int frames = Math.Max(1, documentFrames);
        int start = Math.Clamp(s.StartFrame, 0, frames - 1);
        int length = cycle.Frames * s.Hold;
        int end = s.Repeat ? Math.Max(frames, start + length) : start + length;
        if (!s.Extend) end = Math.Min(end, frames);
        int total = Math.Max(frames, end);
        return new ReferencePlan(start, end, total, total - frames, cycle.Frames, s.Hold);
    }

    /// <summary>The cycle's drawings, one per drawing of the cycle (null where a drawing is empty), at canvas size.</summary>
    public static List<Cel?> Drawings(ReferenceCycle cycle, ReferenceCycleSettings settings, int width, int height)
    {
        var s = settings.Normalized();
        var cels = new List<Cel?>(cycle.Frames);
        for (int d = 0; d < cycle.Frames; d++)
        {
            var img = ReferenceRenderer.Render(cycle.Pose(d), width, height, s.Framing);
            var bounds = img.ContentBounds();
            cels.Add(bounds.IsEmpty ? null : new Cel(img, bounds));
        }
        return cels;
    }

    /// <summary>The layer's cell on every frame of <paramref name="plan"/> (empty before the start and after the end).</summary>
    public static List<Cel?> Track(ReferencePlan plan, IReadOnlyList<Cel?> drawings)
    {
        var track = new List<Cel?>(plan.TotalFrames);
        for (int f = 0; f < plan.TotalFrames; f++)
            track.Add(f >= plan.Start && f < plan.End ? drawings[(f - plan.Start) / plan.Hold % drawings.Count] : null);
        return track;
    }

    /// <summary>The layer name for a cycle, e.g. "Walk reference (side)".</summary>
    public static string LayerName(ReferenceCycle cycle, ReferenceView view) => $"{cycle.Name} reference ({ViewName(view)})";

    public static string ViewName(ReferenceView view) => view switch
    {
        ReferenceView.Side => "side",
        ReferenceView.TopDown => "top-down",
        _ => "three-quarter",
    };

    /// <summary>Adds the cycle to <paramref name="doc"/> as a new locked reference layer, just below
    /// <paramref name="below"/> (the layer being drawn on) or at the bottom, as one undo step. Returns the layer.</summary>
    public static Layer Insert(Document doc, ReferenceCycleSettings settings, double opacity = DefaultOpacity, Layer? below = null)
    {
        var s = settings.Normalized();
        var cycle = ReferenceLibrary.Find(s.CycleId) ?? throw new ArgumentException($"There is no reference cycle “{s.CycleId}”.", nameof(settings));
        var plan = Plan(cycle, s, doc.Animation?.FrameCount ?? 1);
        var track = Track(plan, Drawings(cycle, s, doc.Width, doc.Height));
        var props = new LayerProperties(LayerName(cycle, s.View), Opacity: Math.Clamp(opacity, 0, 1), Locked: true);
        return doc.AddReferenceCycle(props, s, track, below);
    }

    /// <summary>Draws an existing reference layer's cycle again with new settings (another cycle, view, size, place or
    /// timing), as one undo step. Its visibility, opacity and lock stay as they are.</summary>
    public static void Update(Document doc, Layer layer, ReferenceCycleSettings settings)
    {
        var s = settings.Normalized();
        var cycle = ReferenceLibrary.Find(s.CycleId) ?? throw new ArgumentException($"There is no reference cycle “{s.CycleId}”.", nameof(settings));
        var plan = Plan(cycle, s, doc.Animation?.FrameCount ?? 1);
        var track = Track(plan, Drawings(cycle, s, doc.Width, doc.Height));
        doc.UpdateReferenceCycle(layer, s, track, LayerName(cycle, s.View));
    }
}
