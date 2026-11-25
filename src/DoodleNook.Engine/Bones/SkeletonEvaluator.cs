using DoodleNook.Engine.Geometry;

namespace DoodleNook.Engine.Bones;

/// <summary>One bone at one moment: its pose, where it is on the picture, and which sprite frame it shows.</summary>
public sealed record BonePose(
    Bone Bone,
    double X, double Y, double Rotation, double ScaleX, double ScaleY,
    Affine World,
    BoneAttachment? Attachment,
    int AttachmentFrame)
{
    /// <summary>The bone's start (its joint) on the picture.</summary>
    public (double X, double Y) Start => World.Apply(0, 0);

    /// <summary>The bone's end on the picture.</summary>
    public (double X, double Y) End => World.Apply(Bone.Length, 0);

    /// <summary>The sprite's placement: its pixels to the picture.</summary>
    public Affine SpriteMatrix(int spriteWidth, int spriteHeight) =>
        Affine.Translation(-spriteWidth / 2.0, -spriteHeight / 2.0)
            .Then(Affine.Scale(Attachment?.Scale ?? 1, Attachment?.Scale ?? 1))
            .Then(Affine.Rotation(Attachment?.Rotation ?? 0))
            .Then(Affine.Translation(Attachment?.X ?? 0, Attachment?.Y ?? 0))
            .Then(World);
}

/// <summary>The whole character at one frame.</summary>
public sealed class SkeletonPose
{
    internal SkeletonPose(double frame, Dictionary<string, BonePose> bones) => (Frame, _bones) = (frame, bones);

    private readonly Dictionary<string, BonePose> _bones;

    public double Frame { get; }

    public BonePose? this[string boneId] => _bones.GetValueOrDefault(boneId);

    public IEnumerable<BonePose> Bones => _bones.Values;
}

/// <summary>
/// Works out the character at any frame of the master timeline, from the frame number alone: no playback state is
/// carried from one frame to the next, so scrubbing backwards, jumping, looping and exporting all give exactly the same
/// picture for the same frame.
/// <list type="bullet">
/// <item>Pose keys: position and scale change evenly between keys, rotation the short way round; a key marked smooth
/// eases in and out towards the next. Before the first key and after the last, the nearest key holds. A bone without
/// keys keeps its rest pose.</item>
/// <item>Switch keys: from a key's frame on, the bone shows that sprite (no blending). Before the first switch key, the
/// bone's rest sprite shows, as if switched to at frame 1.</item>
/// <item>Animated sprites run on their own clock: milliseconds since the switch (× speed), so a 8 fps head works under a
/// 12 fps timeline. "Restart" starts it at its first frame at the switch; otherwise it runs from the timeline's start.
/// Looping sprites go round; others play once and keep their last frame. A looping timeline starts afresh every pass,
/// so nothing drifts.</item>
/// </list>
/// </summary>
public static class SkeletonEvaluator
{
    /// <summary>The character at master <paramref name="frame"/> (0-based; fractions fall between keys). Sprite frames need
    /// <paramref name="assets"/> for their timing; without it every sprite shows its first frame.</summary>
    public static SkeletonPose Evaluate(SkeletonProject project, double frame, SkeletonAssets? assets = null)
    {
        frame = Math.Clamp(frame, 0, Math.Max(0, project.Length - 1));
        var result = new Dictionary<string, BonePose>();
        foreach (var bone in project.Ordered())
        {
            var track = project.TrackOf(bone.Id);
            var (x, y, r, sx, sy) = PoseAt(bone, track, frame);
            var local = Pose.Matrix(x, y, r, sx, sy);
            var world = bone.ParentId is not null && result.TryGetValue(bone.ParentId, out var parent) ? local.Then(parent.World) : local;
            var (attachment, since) = AttachmentAt(project, bone, track, frame);
            int spriteFrame = 0;
            if (attachment is not null && assets?.Get(project, attachment) is { IsAnimated: true } sprite)
            {
                double startFrame = attachment.Restart ? since : 0;
                double elapsedMs = (frame - startFrame) / project.Fps * 1000 * attachment.Speed;
                spriteFrame = sprite.FrameAt(elapsedMs, attachment.Loop);
            }
            result[bone.Id] = new BonePose(bone, x, y, r, sx, sy, world, attachment, spriteFrame);
        }
        return new SkeletonPose(frame, result);
    }

    /// <summary>The master frame shown <paramref name="seconds"/> into playback (round and round when the timeline loops,
    /// otherwise stopping on the last frame).</summary>
    public static int FrameAtTime(SkeletonProject project, double seconds)
    {
        int f = (int)Math.Floor(Math.Round(Math.Max(0, seconds) * project.Fps, 6));
        return project.Loop ? f % project.Length : Math.Min(f, project.Length - 1);
    }

    /// <summary>A bone's pose at a frame (its keys, or its rest pose).</summary>
    public static (double X, double Y, double Rotation, double ScaleX, double ScaleY) PoseAt(Bone bone, BoneTrack? track, double frame)
    {
        var keys = track?.Keys;
        if (keys is null || keys.Count == 0) return (bone.X, bone.Y, bone.Rotation, bone.ScaleX, bone.ScaleY);
        if (frame <= keys[0].Frame) return Values(keys[0]);
        if (frame >= keys[^1].Frame) return Values(keys[^1]);
        int i = 0;
        while (keys[i + 1].Frame <= frame) i++;
        var a = keys[i];
        var b = keys[i + 1];
        double u = (frame - a.Frame) / (b.Frame - a.Frame);
        if (a.Smooth) u = u * u * (3 - 2 * u);
        double turn = ShortestTurn(a.Rotation, b.Rotation);
        return (Lerp(a.X, b.X, u), Lerp(a.Y, b.Y, u), a.Rotation + turn * u, Lerp(a.ScaleX, b.ScaleX, u), Lerp(a.ScaleY, b.ScaleY, u));
    }

    /// <summary>The sprite a bone shows at a frame, and the frame it was switched to (0 for the rest sprite).</summary>
    public static (BoneAttachment? Attachment, int Since) AttachmentAt(SkeletonProject project, Bone bone, BoneTrack? track, double frame)
    {
        SwitchKey? last = null;
        if (track is not null)
            foreach (var s in track.Switches)
            {
                if (s.Frame <= frame + 1e-9) last = s;
                else break;
            }
        return last is null ? (project.FindAttachment(bone, bone.Attachment), 0) : (project.FindAttachment(bone, last.Attachment), last.Frame);
    }

    /// <summary>The turn from <paramref name="from"/> to <paramref name="to"/> degrees, the short way round (−180 to 180).</summary>
    public static double ShortestTurn(double from, double to)
    {
        double d = (to - from) % 360;
        if (d > 180) d -= 360;
        else if (d < -180) d += 360;
        return d;
    }

    private static (double, double, double, double, double) Values(PoseKey k) => (k.X, k.Y, k.Rotation, k.ScaleX, k.ScaleY);

    private static double Lerp(double a, double b, double u) => a + (b - a) * u;
}
