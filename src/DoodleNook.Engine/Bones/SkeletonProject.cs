using System.Text.Json;
using System.Text.Json.Serialization;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Geometry;

namespace DoodleNook.Engine.Bones;

/// <summary>
/// Mission 019 "Bare Bones": a 2D skeletal animation project (<c>.dnkbones</c>). Three parts, kept apart:
/// <list type="bullet">
/// <item><b>Skeleton</b>: <see cref="Bones"/> in a parent/child hierarchy, each with its rest (setup) pose.</item>
/// <item><b>Attachments</b>: each bone's sprites (<see cref="Bone.Attachments"/>), linked by path, static or animated
/// (an animated DoodleNook drawing). One is shown at a time.</item>
/// <item><b>Animation</b>: the master timeline (<see cref="Fps"/>, <see cref="Length"/>, <see cref="Loop"/>) with per-bone
/// pose keys and attachment-switch keys (<see cref="Tracks"/>).</item>
/// </list>
/// The project only links its artwork (relative to its own folder), so source drawings are never changed by it, and
/// the folder can be moved as a whole. See <see cref="SkeletonEvaluator"/> for how a frame is worked out.
/// </summary>
public sealed class SkeletonProject
{
    public const string FormatName = "DoodleNook skeletal animation";
    public const int CurrentVersion = 1;
    public const string Extension = ".dnkbones";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;

    /// <summary>The picture size the character is assembled and exported at.</summary>
    public int Width { get; set; } = 512;
    public int Height { get; set; } = 512;

    /// <summary>Exact pixels (nearest neighbour) instead of smooth sampling, for pixel art.</summary>
    public bool PixelArt { get; set; }

    public double Fps { get; set; } = 12;

    /// <summary>The master timeline's length in frames.</summary>
    public int Length { get; set; } = 24;

    public bool Loop { get; set; } = true;

    public List<Bone> Bones { get; set; } = new();

    /// <summary>Bone ids, back to front: whose sprite is drawn first. Bones missing here are drawn last.</summary>
    public List<string> DrawOrder { get; set; } = new();

    public List<BoneTrack> Tracks { get; set; } = new();

    [JsonIgnore] public string? FilePath { get; private set; }

    [JsonIgnore] public string? Folder => FilePath is null ? null : Path.GetDirectoryName(FilePath);

    // ================================================================== skeleton

    public Bone? Find(string? id) => id is null ? null : Bones.FirstOrDefault(b => b.Id == id);

    public IEnumerable<Bone> ChildrenOf(string? parentId) => Bones.Where(b => b.ParentId == parentId);

    /// <summary>Bones with every parent before its children (the order transforms are worked out in).</summary>
    public List<Bone> Ordered()
    {
        var result = new List<Bone>(Bones.Count);
        void Visit(string? parent, int depth)
        {
            if (depth > 256) return;
            foreach (var b in ChildrenOf(parent))
            {
                result.Add(b);
                Visit(b.Id, depth + 1);
            }
        }
        Visit(null, 0);
        // Bones whose parent is missing (a damaged file) still count, as roots.
        foreach (var b in Bones)
            if (!result.Contains(b)) result.Add(b);
        return result;
    }

    public int Depth(Bone bone)
    {
        int d = 0;
        for (var p = Find(bone.ParentId); p is not null && d < 256; p = Find(p.ParentId)) d++;
        return d;
    }

    /// <summary>True when <paramref name="candidate"/> is <paramref name="bone"/> or one of its descendants.</summary>
    public bool IsSelfOrDescendant(Bone bone, Bone candidate)
    {
        for (var b = candidate; b is not null; b = Find(b.ParentId))
            if (b.Id == bone.Id) return true;
        return false;
    }

    /// <summary>Adds a bone (a root when <paramref name="parent"/> is null), placed by its rest pose.</summary>
    public Bone AddBone(string name, Bone? parent, double x, double y, double rotation = 0, double length = 60)
    {
        var bone = new Bone { Id = NewId(), Name = UniqueName(name), ParentId = parent?.Id, X = x, Y = y, Rotation = rotation, Length = length };
        Bones.Add(bone);
        DrawOrder.Add(bone.Id);
        return bone;
    }

    public string UniqueName(string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Bone" : name.Trim();
        if (Bones.All(b => b.Name != name)) return name;
        for (int n = 2; ; n++)
            if (Bones.All(b => b.Name != $"{name} {n}")) return $"{name} {n}";
    }

    /// <summary>Gives <paramref name="bone"/> a new parent (null: a root), keeping where it is in the rest pose. Refuses a
    /// parent that is the bone itself or one of its own children (that would be a loop).</summary>
    public bool Reparent(Bone bone, Bone? parent)
    {
        if (parent is not null && IsSelfOrDescendant(bone, parent)) return false;
        if (bone.ParentId == parent?.Id) return true;
        // Old parent space → new parent space, from the rest pose: the bone stays put on screen.
        var oldParent = RestWorld(Find(bone.ParentId));
        var newParent = RestWorld(parent);
        var change = oldParent.Then(newParent.Invert());
        bone.SetLocal(bone.LocalMatrix.Then(change));
        foreach (var key in TrackOf(bone.Id)?.Keys ?? [])
            key.SetLocal(key.LocalMatrix.Then(change));
        bone.ParentId = parent?.Id;
        return true;
    }

    /// <summary>Deletes a bone. Its children stay, joined to its parent where they were; its sprites and keys go (the
    /// drawings they linked to are not touched).</summary>
    public void DeleteBone(Bone bone)
    {
        var parent = Find(bone.ParentId);
        foreach (var child in ChildrenOf(bone.Id).ToList()) Reparent(child, parent);
        Bones.Remove(bone);
        DrawOrder.Remove(bone.Id);
        Tracks.RemoveAll(t => t.BoneId == bone.Id);
    }

    /// <summary>The rest-pose world transform of a bone (identity for "no bone").</summary>
    public Affine RestWorld(Bone? bone)
    {
        var m = Affine.Identity;
        for (int guard = 0; bone is not null && guard < 256; bone = Find(bone.ParentId), guard++) m = m.Then(bone.LocalMatrix);
        return m;
    }

    /// <summary>Bones back to front (draw order), every bone once.</summary>
    public List<Bone> BackToFront()
    {
        var list = DrawOrder.Select(Find).Where(b => b is not null).Cast<Bone>().Distinct().ToList();
        foreach (var b in Bones)
            if (!list.Contains(b)) list.Add(b);
        return list;
    }

    /// <summary>Moves a bone's sprite one step towards the front (+1) or the back (−1).</summary>
    public bool MoveInDrawOrder(Bone bone, int direction)
    {
        var order = BackToFront();
        int i = order.IndexOf(bone), j = i + Math.Sign(direction);
        if (i < 0 || j < 0 || j >= order.Count) return false;
        (order[i], order[j]) = (order[j], order[i]);
        DrawOrder = order.Select(b => b.Id).ToList();
        return true;
    }

    // ================================================================== attachments

    public BoneAttachment? FindAttachment(Bone bone, string? id) => id is null ? null : bone.Attachments.FirstOrDefault(a => a.Id == id);

    /// <summary>Adds a sprite to a bone, centred halfway along it, and makes it the bone's sprite if it had none.</summary>
    public BoneAttachment AddAttachment(Bone bone, string source, string? name = null)
    {
        var a = new BoneAttachment
        {
            Id = NewId(),
            Name = UniqueAttachmentName(bone, name ?? Path.GetFileNameWithoutExtension(source)),
            Source = Relate(source),
            X = bone.Length / 2,
        };
        bone.Attachments.Add(a);
        bone.Attachment ??= a.Id;
        return a;
    }

    public string UniqueAttachmentName(Bone bone, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Sprite" : name.Trim();
        if (bone.Attachments.All(a => a.Name != name)) return name;
        for (int n = 2; ; n++)
            if (bone.Attachments.All(a => a.Name != $"{name} {n}")) return $"{name} {n}";
    }

    /// <summary>Removes a sprite from a bone, with the switch keys that chose it.</summary>
    public void RemoveAttachment(Bone bone, BoneAttachment attachment)
    {
        bone.Attachments.Remove(attachment);
        if (bone.Attachment == attachment.Id) bone.Attachment = bone.Attachments.FirstOrDefault()?.Id;
        TrackOf(bone.Id)?.Switches.RemoveAll(s => s.Attachment == attachment.Id);
    }

    // ================================================================== animation

    public BoneTrack? TrackOf(string boneId) => Tracks.FirstOrDefault(t => t.BoneId == boneId);

    public BoneTrack Track(string boneId)
    {
        var t = TrackOf(boneId);
        if (t is null)
        {
            t = new BoneTrack { BoneId = boneId };
            Tracks.Add(t);
        }
        return t;
    }

    /// <summary>Sets a bone's pose key at a frame (replacing one there). A bone's first key at a later frame also keys
    /// frame 1 with the rest pose, so the frames before it keep the pose they had.</summary>
    public PoseKey SetPoseKey(Bone bone, int frame, double x, double y, double rotation, double scaleX, double scaleY, bool? smooth = null)
    {
        frame = Math.Clamp(frame, 0, Math.Max(0, Length - 1));
        var track = Track(bone.Id);
        if (track.Keys.Count == 0 && frame > 0)
            track.Keys.Add(new PoseKey { Frame = 0, X = bone.X, Y = bone.Y, Rotation = bone.Rotation, ScaleX = bone.ScaleX, ScaleY = bone.ScaleY });
        var key = track.Keys.FirstOrDefault(k => k.Frame == frame);
        if (key is null)
        {
            key = new PoseKey { Frame = frame };
            track.Keys.Add(key);
            track.Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }
        key.X = x;
        key.Y = y;
        key.Rotation = rotation;
        key.ScaleX = scaleX;
        key.ScaleY = scaleY;
        if (smooth is { } s) key.Smooth = s;
        return key;
    }

    public bool RemovePoseKey(Bone bone, int frame) => TrackOf(bone.Id)?.Keys.RemoveAll(k => k.Frame == frame) > 0;

    /// <summary>From <paramref name="frame"/> on, the bone shows <paramref name="attachment"/> (null: nothing).</summary>
    public SwitchKey SetSwitch(Bone bone, int frame, BoneAttachment? attachment)
    {
        frame = Math.Clamp(frame, 0, Math.Max(0, Length - 1));
        var track = Track(bone.Id);
        var key = track.Switches.FirstOrDefault(s => s.Frame == frame);
        if (key is null)
        {
            key = new SwitchKey { Frame = frame };
            track.Switches.Add(key);
            track.Switches.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }
        key.Attachment = attachment?.Id;
        return key;
    }

    public bool RemoveSwitch(Bone bone, int frame) => TrackOf(bone.Id)?.Switches.RemoveAll(s => s.Frame == frame) > 0;

    /// <summary>Frames that have any key (pose or switch) on <paramref name="bone"/>, or on any bone when null.</summary>
    public SortedSet<int> KeyFrames(Bone? bone = null)
    {
        var set = new SortedSet<int>();
        foreach (var t in Tracks.Where(t => bone is null || t.BoneId == bone.Id))
        {
            foreach (var k in t.Keys) set.Add(k.Frame);
            foreach (var s in t.Switches) set.Add(s.Frame);
        }
        return set;
    }

    /// <summary>Changes the timeline's length; keys past the end are removed.</summary>
    public void SetLength(int frames)
    {
        Length = Math.Clamp(frames, 1, 10_000);
        foreach (var t in Tracks)
        {
            t.Keys.RemoveAll(k => k.Frame >= Length);
            t.Switches.RemoveAll(s => s.Frame >= Length);
        }
    }

    // ================================================================== paths

    /// <summary>The full path of a linked file.</summary>
    public string? Resolve(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var native = source.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(native)) return Path.GetFullPath(native);
        return Folder is null ? Path.GetFullPath(native) : Path.GetFullPath(Path.Combine(Folder, native));
    }

    /// <summary>How a linked file is stored: relative to the project's folder (absolute only on another drive, or before
    /// the project is saved).</summary>
    public string Relate(string path)
    {
        var full = Path.GetFullPath(path);
        if (Folder is null) return full;
        var relative = Path.GetRelativePath(Folder, full);
        return Path.IsPathRooted(relative) ? full : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    // ================================================================== the file

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Saves safely (a temporary file, then replaced). Links made before the first save become relative.</summary>
    public void Save(string? path = null)
    {
        path ??= FilePath ?? throw new InvalidOperationException("Where should the skeletal animation be saved?");
        var full = Path.GetFullPath(path);
        if (FilePath != full)
        {
            // Links are kept relative to wherever the file now lives.
            var resolved = Bones.SelectMany(b => b.Attachments).Select(a => (a, Resolve(a.Source))).ToList();
            FilePath = full;
            foreach (var (a, p) in resolved)
                if (p is not null) a.Source = Relate(p);
        }
        Version = CurrentVersion;
        AnimationExport.WriteAtomically(FilePath, System.Text.Encoding.UTF8.GetBytes(ToJson()));
    }

    public static SkeletonProject Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>A project from its JSON (a file's contents, or an undo snapshot), belonging at <paramref name="path"/>
    /// (null: not saved yet).</summary>
    public static SkeletonProject Parse(string json, string? path)
    {
        SkeletonProject? p;
        try
        {
            p = JsonSerializer.Deserialize<SkeletonProject>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new SkeletonFormatException("This skeletal animation file is damaged: " + ex.Message);
        }
        if (p is null || p.Format != FormatName) throw new SkeletonFormatException("This file isn't a DoodleNook skeletal animation.");
        if (p.Version > CurrentVersion) throw new SkeletonFormatException($"This skeletal animation was saved by a newer DoodleNook (version {p.Version}). Please update DoodleNook to open it.");
        p.FilePath = path is null ? null : Path.GetFullPath(path);
        p.Width = Math.Clamp(p.Width, 1, 8192);
        p.Height = Math.Clamp(p.Height, 1, 8192);
        p.Fps = double.IsFinite(p.Fps) ? Math.Clamp(p.Fps, 1, 60) : 12;
        p.Length = Math.Clamp(p.Length, 1, 10_000);
        p.Bones ??= new();
        p.DrawOrder ??= new();
        p.Tracks ??= new();
        var ids = new HashSet<string>();
        foreach (var b in p.Bones)
        {
            if (string.IsNullOrWhiteSpace(b.Id) || !ids.Add(b.Id)) b.Id = NewId();
            b.Name ??= "Bone";
            b.Attachments ??= new();
            if (b.ScaleX == 0) b.ScaleX = 1;
            if (b.ScaleY == 0) b.ScaleY = 1;
            foreach (var a in b.Attachments)
            {
                if (string.IsNullOrWhiteSpace(a.Id)) a.Id = NewId();
                a.Name ??= "Sprite";
                if (a.Scale == 0) a.Scale = 1;
                if (!double.IsFinite(a.Speed) || a.Speed <= 0) a.Speed = 1;
            }
            if (b.Attachment is not null && b.Attachments.All(a => a.Id != b.Attachment)) b.Attachment = b.Attachments.FirstOrDefault()?.Id;
        }
        // A parent that is missing, or a loop in a damaged file, makes the bone a root.
        foreach (var b in p.Bones)
        {
            if (b.ParentId is not null && p.Find(b.ParentId) is null) b.ParentId = null;
            var seen = new HashSet<string>();
            for (var x = b; x is not null; x = p.Find(x.ParentId))
                if (!seen.Add(x.Id))
                {
                    b.ParentId = null;
                    break;
                }
        }
        p.Tracks.RemoveAll(t => t is null || p.Find(t.BoneId) is null);
        foreach (var t in p.Tracks)
        {
            t.Keys ??= new();
            t.Switches ??= new();
            t.Keys.RemoveAll(k => k is null || k.Frame < 0 || k.Frame >= p.Length);
            t.Switches.RemoveAll(s => s is null || s.Frame < 0 || s.Frame >= p.Length);
            t.Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            t.Switches.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }
        return p;
    }

    /// <summary>An independent copy (undo snapshots, the evaluator in tests).</summary>
    public SkeletonProject Clone() => Parse(ToJson(), FilePath);

    internal static string NewId() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>Sets where the project lives without saving (a new project placed in a folder, tests).</summary>
    public void PlaceAt(string path) => FilePath = Path.GetFullPath(path);
}

/// <summary>One bone: where it sits in its parent (rest pose), how it is turned and scaled, how long it is drawn, and the
/// sprites it can show. Rotation is in degrees, clockwise on screen; the bone points along its own x axis.</summary>
public sealed class Bone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Bone";
    public string? ParentId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public double Length { get; set; } = 60;

    /// <summary>The sprites this bone can show (switch between them with keys). Only one shows at a time.</summary>
    public List<BoneAttachment> Attachments { get; set; } = new();

    /// <summary>The sprite shown in the rest pose and until the first switch key (an id in <see cref="Attachments"/>).</summary>
    public string? Attachment { get; set; }

    [JsonIgnore]
    public Affine LocalMatrix => Pose.Matrix(X, Y, Rotation, ScaleX, ScaleY);

    internal void SetLocal(Affine m) => (X, Y, Rotation, ScaleX, ScaleY) = Pose.Decompose(m);
}

/// <summary>A sprite on a bone: a linked picture or animated drawing, placed by its centre in the bone's own
/// coordinates (the bone's start, its joint, is the point it turns around).</summary>
public sealed class BoneAttachment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Sprite";

    /// <summary>The linked file (.dnk, still or animated, or a picture), relative to the project's folder.</summary>
    public string Source { get; set; } = "";

    /// <summary>Where the sprite's centre is, along the bone (x) and across it (y).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public double Scale { get; set; } = 1;

    /// <summary>Animated sprites: play round and round (true) or once, then hold the last frame.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Animated sprites: 1 = the drawing's own timing.</summary>
    public double Speed { get; set; } = 1;

    /// <summary>Animated sprites: start from the first frame when a key switches to it (true), or carry on as if it
    /// had been playing since the start of the timeline.</summary>
    public bool Restart { get; set; } = true;
}

/// <summary>A bone's keys on the master timeline.</summary>
public sealed class BoneTrack
{
    public string BoneId { get; set; } = "";
    public List<PoseKey> Keys { get; set; } = new();
    public List<SwitchKey> Switches { get; set; } = new();
}

/// <summary>A bone's pose at a frame (in its parent's coordinates, like the rest pose).</summary>
public sealed class PoseKey
{
    public int Frame { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;

    /// <summary>Ease in and out towards the next key (otherwise an even, linear change).</summary>
    public bool Smooth { get; set; }

    [JsonIgnore]
    public Affine LocalMatrix => Pose.Matrix(X, Y, Rotation, ScaleX, ScaleY);

    internal void SetLocal(Affine m) => (X, Y, Rotation, ScaleX, ScaleY) = Pose.Decompose(m);
}

/// <summary>From <see cref="Frame"/> on, the bone shows this sprite (null: none). Not blended: it simply changes.</summary>
public sealed class SwitchKey
{
    public int Frame { get; set; }
    public string? Attachment { get; set; }
}

public sealed class SkeletonFormatException(string message) : Exception(message);

/// <summary>Turning a pose (position, rotation, scale) into a matrix and back.</summary>
public static class Pose
{
    /// <summary>Scale, then turn, then move: a point in the bone's own coordinates to its parent's.</summary>
    public static Affine Matrix(double x, double y, double rotation, double scaleX, double scaleY) =>
        Affine.Scale(scaleX, scaleY).Then(Affine.Rotation(rotation)).Then(Affine.Translation(x, y));

    /// <summary>The pose of a matrix built by <see cref="Matrix"/> (any shear from uneven scaling is dropped).</summary>
    public static (double X, double Y, double Rotation, double ScaleX, double ScaleY) Decompose(Affine m)
    {
        double sx = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
        double rotation = Math.Atan2(m.M12, m.M11) * 180 / Math.PI;
        double sy = sx < 1e-12 ? 1 : m.Determinant / sx;
        return (m.Dx, m.Dy, rotation, sx < 1e-12 ? 1 : sx, Math.Abs(sy) < 1e-12 ? 1 : sy);
    }
}
