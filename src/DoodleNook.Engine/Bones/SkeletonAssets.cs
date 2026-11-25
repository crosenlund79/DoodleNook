using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Bones;

/// <summary>A sprite's pictures and timing: one frame for a still picture, every frame of an animated drawing.</summary>
public sealed class SpriteFrames
{
    public SpriteFrames(IReadOnlyList<RasterImage> frames, IReadOnlyList<int> durationsMs)
    {
        if (frames.Count == 0) throw new ArgumentException("A sprite needs at least one picture.", nameof(frames));
        Frames = frames;
        DurationsMs = durationsMs.Count == frames.Count ? durationsMs : Enumerable.Repeat(83, frames.Count).ToList();
        var starts = new double[frames.Count + 1];
        for (int i = 0; i < frames.Count; i++) starts[i + 1] = starts[i] + Math.Max(1, DurationsMs[i]);
        _starts = starts;
    }

    private readonly double[] _starts;

    public IReadOnlyList<RasterImage> Frames { get; }
    public IReadOnlyList<int> DurationsMs { get; }
    public bool IsAnimated => Frames.Count > 1;
    public int Width => Frames[0].Width;
    public int Height => Frames[0].Height;

    /// <summary>One pass of the animation, in milliseconds.</summary>
    public double TotalMs => _starts[^1];

    /// <summary>The frame showing <paramref name="elapsedMs"/> after the sprite started: round and round when
    /// <paramref name="loop"/>, otherwise once, then the last frame stays. Worked out from time alone, so scrubbing,
    /// loops and exports always agree.</summary>
    public int FrameAt(double elapsedMs, bool loop)
    {
        if (Frames.Count == 1) return 0;
        double e = Math.Max(0, Math.Round(elapsedMs, 6));
        if (loop) e %= TotalMs;
        else if (e >= TotalMs) return Frames.Count - 1;
        // The last start that is not after e.
        int lo = 0, hi = Frames.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_starts[mid] <= e + 1e-6) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }
}

/// <summary>
/// The pictures a skeletal project shows, loaded from its linked files on first use and kept while the project is open
/// (reloaded when a file changes on disk, e.g. after editing it in the drawing workspaces). DoodleNook drawings
/// (.dnk, still or animated, without their paper) and PNG are read here; <see cref="DecodeImage"/> can add other
/// picture formats. A missing or unreadable file just shows nothing; <see cref="Problem"/> says why.
/// </summary>
public sealed class SkeletonAssets
{
    private readonly Dictionary<string, (DateTime Stamp, SpriteFrames? Sprite, string? Problem)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Decodes picture files other than .dnk and PNG (the app adds JPEG, WebP, …).</summary>
    public Func<string, RasterImage?>? DecodeImage { get; init; }

    public SpriteFrames? Get(SkeletonProject project, BoneAttachment? attachment)
    {
        if (attachment is null || project.Resolve(attachment.Source) is not { } path) return null;
        return Load(path).Sprite;
    }

    /// <summary>Why a sprite shows nothing (missing file, unreadable), or null when it is fine.</summary>
    public string? Problem(SkeletonProject project, BoneAttachment attachment)
    {
        if (project.Resolve(attachment.Source) is not { } path) return "No file is linked.";
        return Load(path).Problem;
    }

    private (DateTime Stamp, SpriteFrames? Sprite, string? Problem) Load(string path)
    {
        var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_cache.TryGetValue(path, out var entry) && entry.Stamp == stamp) return entry;
        entry = (stamp, null, null);
        if (stamp == DateTime.MinValue) entry.Problem = $"{Path.GetFileName(path)} is missing (it was at {path}).";
        else
        {
            try
            {
                entry.Sprite = Read(path);
                if (entry.Sprite is null) entry.Problem = $"{Path.GetFileName(path)} isn't a picture DoodleNook can read.";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or NotSupportedException or ArgumentException or System.Text.Json.JsonException)
            {
                entry.Problem = $"{Path.GetFileName(path)} couldn't be read: {ex.Message}";
            }
        }
        _cache[path] = entry;
        return entry;
    }

    private SpriteFrames? Read(string path)
    {
        if (path.EndsWith(DnkFile.Extension, StringComparison.OrdinalIgnoreCase)) return FromDocument(DnkFile.Load(path));
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G')
            return new SpriteFrames([PngCodec.DecodeAny(bytes)], [0]);
        return DecodeImage?.Invoke(path) is { } image ? new SpriteFrames([image], [0]) : null;
    }

    /// <summary>A drawing's frames without its paper (a sprite is cut out), with the drawing's own timing.</summary>
    public static SpriteFrames FromDocument(Document doc)
    {
        if (!doc.IsAnimated) return new SpriteFrames([doc.RenderFrame(0, includePaper: false)], [0]);
        var anim = doc.Animation!;
        var frames = new List<RasterImage>(doc.FrameCount);
        var durations = new List<int>(doc.FrameCount);
        for (int f = 0; f < doc.FrameCount; f++)
        {
            frames.Add(doc.RenderFrame(f, includePaper: false));
            durations.Add(anim.DurationOf(f));
        }
        return new SpriteFrames(frames, durations);
    }

    /// <summary>Forgets every loaded picture (the project closed, or files changed in a way the dates don't show).</summary>
    public void Clear() => _cache.Clear();

    /// <summary>How many files are loaded (tests, diagnostics).</summary>
    public int Count => _cache.Count;
}
