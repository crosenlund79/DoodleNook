using System.Text.RegularExpressions;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>
/// Mission 012 — Import Animation for reconstruction: turns frames read from a GIF, an animated PNG or a numbered PNG
/// sequence into an ordinary, editable DoodleNook animation with four layers, ready to draw on:
/// <list type="number">
/// <item><b>Coloring</b>, <b>Outline</b> and <b>Sketch</b>: empty, 100 % (drawing on any frame makes that frame's drawing).</item>
/// <item><b>Reference</b> (bottom): the imported frames, one drawing per frame (identical neighbouring frames share one
/// drawing as a hold), 35 % opacity, locked. An ordinary layer: it can be shown, hidden, unlocked and saved.</item>
/// </list>
/// Timing from the file is kept exactly: the most common delay becomes the frame rate and other frames get their own
/// durations. The source files are only read, never changed.
/// </summary>
public static class AnimationImport
{
    public const double ReferenceOpacity = 0.35;
    public const string ReferenceName = "Reference", SketchName = "Sketch", OutlineName = "Outline", ColoringName = "Coloring";

    // ------------------------------------------------------------------ image sequences

    /// <summary>Compares file names the way people count: "frame_2" before "frame_10" (case-insensitive).</summary>
    public static int NaturalCompare(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a.AsSpan(si, i - si).TrimStart('0');
                var nb = b.AsSpan(sj, j - sj).TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                int c = na.SequenceCompareTo(nb);
                if (c != 0) return c;
                c = (i - si).CompareTo(j - sj); // "01" after "1" when otherwise equal
                if (c != 0) return c;
                continue;
            }
            int d = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
            if (d != 0) return d;
            i++;
            j++;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    public static List<string> NaturalSort(IEnumerable<string> paths) =>
        paths.OrderBy(p => Path.GetFileName(p), Comparer<string>.Create(NaturalCompare)).ToList();

    private static readonly Regex Numbered = new(@"^(?<prefix>.*?)(?<number>\d+)(?<suffix>\D*)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The numbered sequence a file belongs to: the files in the same folder whose names differ only in the number
    /// (walk_001.png, walk_002.png …; same extension, same text before and after the number), in number order. A name
    /// without a number is a sequence of one. Other PNGs in the folder are never included.
    /// </summary>
    public static List<string> SequenceOf(string path, IEnumerable<string> filesInFolder)
    {
        string name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        var m = Numbered.Match(name);
        if (!m.Success) return [path];
        string prefix = m.Groups["prefix"].Value, suffix = m.Groups["suffix"].Value;
        var pattern = new Regex("^" + Regex.Escape(prefix) + @"(\d+)" + Regex.Escape(suffix) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var members = filesInFolder
            .Where(f => string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase) && pattern.IsMatch(Path.GetFileNameWithoutExtension(f)))
            .ToList();
        if (!members.Any(f => string.Equals(Path.GetFileName(f), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))) members.Add(path);
        return NaturalSort(members);
    }

    /// <summary>Frames of different sizes go on one canvas (the largest width and height), each at the top left, as
    /// plain image files carry no position of their own.</summary>
    public static ImportedAnimation FromImages(IReadOnlyList<RasterImage> images)
    {
        if (images.Count == 0) throw new ArgumentException("No images.", nameof(images));
        int w = images.Max(i => i.Width), h = images.Max(i => i.Height);
        var frames = images.Select(i =>
        {
            if (i.Width == w && i.Height == h) return i;
            var c = new RasterImage(w, h);
            c.Paste(i, 0, 0);
            return c;
        }).ToList();
        var notes = new List<string>();
        if (images.Any(i => i.Width != w || i.Height != h))
            notes.Add($"The pictures have different sizes; each is placed at the top left of a {w} × {h} canvas.");
        return new ImportedAnimation(w, h, frames, Enumerable.Repeat<int?>(null, frames.Count).ToList(), notes);
    }

    // ------------------------------------------------------------------ timing

    /// <summary>The frame rate and per-frame durations that reproduce the source delays exactly: the most common delay
    /// sets the frame rate (1–60 fps); frames with another delay keep it as their own duration (10 ms – 10 s, the
    /// animation model's range).</summary>
    public static (double Fps, List<int?> Durations) Timing(IReadOnlyList<int?> delaysMs, double fpsWhenUntimed)
    {
        if (delaysMs.All(d => d is null))
            return (Math.Clamp(fpsWhenUntimed, Animation.MinFps, Animation.MaxFps), Enumerable.Repeat<int?>(null, delaysMs.Count).ToList());
        var known = delaysMs.Where(d => d is not null).Select(d => Math.Clamp(d!.Value, 10, 10_000)).ToList();
        int common = known.GroupBy(d => d).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        double fps = Math.Clamp(1000.0 / common, Animation.MinFps, Animation.MaxFps);
        double frameMs = 1000.0 / fps;
        var durations = delaysMs.Select(d =>
        {
            if (d is null) return (int?)null;
            int ms = Math.Clamp(d.Value, 10, 10_000);
            return Math.Abs(ms - frameMs) < 0.5 ? null : ms;
        }).ToList();
        return (fps, durations);
    }

    // ------------------------------------------------------------------ the document

    /// <summary>
    /// The four-layer animation from <paramref name="source"/>'s frames (only those in <paramref name="keep"/>, in
    /// order; all when null). Image sequences play at <paramref name="fpsWhenUntimed"/>.
    /// </summary>
    public static Document BuildDocument(ImportedAnimation source, IReadOnlyList<int>? keep = null, double fpsWhenUntimed = Animation.DefaultFps,
        ColorRgb? paper = null)
    {
        var indices = keep ?? Enumerable.Range(0, source.Frames.Count).ToList();
        if (indices.Count == 0) throw new ArgumentException("Choose at least one frame.", nameof(keep));
        int w = source.Width, h = source.Height;
        var cels = new List<Cel?>();
        Cel? previous = null;
        foreach (int i in indices)
        {
            var image = source.Frames[i];
            if (image.Width != w || image.Height != h) throw new ArgumentException("Every frame must be the size of the animation.", nameof(source));
            // A frame identical to the one before is the same drawing held one frame longer (one image in memory).
            if (previous is not null && previous.Image.Pixels.AsSpan().SequenceEqual(image.Pixels)) cels.Add(previous);
            else
            {
                previous = image.ContentBounds().IsEmpty ? null : new Cel(image);
                cels.Add(previous);
            }
        }
        var (fps, durations) = Timing(indices.Select(i => source.DelaysMs[i]).ToList(), fpsWhenUntimed);
        var layers = new List<LayerProperties>
        {
            new(ReferenceName, Opacity: ReferenceOpacity, Locked: true),
            new(SketchName),
            new(OutlineName),
            new(ColoringName),
        };
        var tracks = new List<List<Cel?>> { cels };
        for (int k = 1; k < layers.Count; k++) tracks.Add(Enumerable.Repeat<Cel?>(null, indices.Count).ToList());
        var doc = Document.CreateAnimated(w, h, paper ?? ColorRgb.White, true, layers, tracks, fps, loop: true, pingPong: false, durations,
            currentFrame: 0, activeIndex: 1);
        doc.MarkUnsaved();
        return doc;
    }

    /// <summary>The memory the imported drawings take (the canvas size × the number of distinct frames).</summary>
    public static long EstimateBytes(ImportedAnimation source, IReadOnlyList<int> keep)
    {
        long frame = (long)source.Width * source.Height * 4;
        int distinct = 0;
        RasterImage? last = null;
        foreach (int i in keep)
        {
            var f = source.Frames[i];
            if (last is null || !last.Pixels.AsSpan().SequenceEqual(f.Pixels)) distinct++;
            last = f;
        }
        return frame * (distinct + 6); // plus the composite, the frame cache's first frames and blank drawings
    }
}
