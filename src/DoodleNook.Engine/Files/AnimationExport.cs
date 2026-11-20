using System.Text.Json;
using System.Text.Json.Serialization;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Files;

/// <summary>One rendered animation frame for export: the flattened picture and how long it is shown.</summary>
public sealed record ExportFrame(RasterImage Image, int DurationMs, int Index);

public enum SheetLayout
{
    /// <summary>All frames in one row.</summary>
    Horizontal,

    /// <summary>All frames in one column.</summary>
    Vertical,

    /// <summary>Rows of <see cref="SpriteSheetOptions.Columns"/> frames.</summary>
    Grid,
}

/// <summary>Sprite-sheet layout. Spacing is between cells, padding is transparent space inside each cell around the
/// frame, margin is around the whole sheet.</summary>
public sealed record SpriteSheetOptions(SheetLayout Layout = SheetLayout.Horizontal, int Columns = 4, int Spacing = 0, int Padding = 0, int Margin = 0)
{
    public SpriteSheetOptions Normalized() => this with
    {
        Columns = Math.Clamp(Columns, 1, 256),
        Spacing = Math.Clamp(Spacing, 0, 256),
        Padding = Math.Clamp(Padding, 0, 256),
        Margin = Math.Clamp(Margin, 0, 256),
    };
}

/// <summary>A sprite sheet and where each frame is on it.</summary>
public sealed record SpriteSheet(RasterImage Image, IReadOnlyList<PixelRect> FrameRects, IReadOnlyList<int> DurationsMs, int Columns, int Rows);

/// <summary>Renders animations for game engines and the web: frames, sprite sheets, PNG sequences and GIFs.
/// Exports never include onion skins or other display-only overlays.</summary>
public static class AnimationExport
{
    /// <summary>Every frame of the animation (one frame for a still picture), flattened and optionally scaled by a whole
    /// number. <paramref name="smoothScaling"/> uses bicubic resampling (hand-drawn work); otherwise pixels stay exact.</summary>
    public static List<ExportFrame> RenderFrames(Document doc, bool includePaper, int scale = 1, bool smoothScaling = false)
    {
        scale = Math.Clamp(scale, 1, 16);
        var frames = new List<ExportFrame>();
        int count = doc.FrameCount;
        for (int f = 0; f < count; f++)
        {
            var image = doc.RenderFrame(f, includePaper);
            if (scale > 1)
            {
                var exact = image.ExactPalette;
                image = smoothScaling
                    ? Resampler.Resize(image, image.Width * scale, image.Height * scale, Interpolation.Bicubic)
                    : ScaleNearest(image, scale);
                if (!smoothScaling) image.ExactPalette = exact; // whole pixels: still exactly the palette's
            }
            frames.Add(new ExportFrame(image, doc.Animation?.DurationOf(f) ?? 100, f));
        }
        return frames;
    }

    /// <summary>Exact pixel enlargement (each pixel becomes a scale × scale block).</summary>
    public static RasterImage ScaleNearest(RasterImage src, int scale)
    {
        var dst = new RasterImage(src.Width * scale, src.Height * scale);
        for (int y = 0; y < dst.Height; y++)
        {
            int sy = y / scale;
            for (int x = 0; x < dst.Width; x++) dst.Pixels[y * dst.Width + x] = src.Pixels[sy * src.Width + x / scale];
        }
        return dst;
    }

    /// <summary>Lays frames out on a sheet (frames keep their exact pixels; the background is transparent).</summary>
    public static SpriteSheet BuildSheet(IReadOnlyList<ExportFrame> frames, SpriteSheetOptions options)
    {
        if (frames.Count == 0) throw new ArgumentException("There are no frames to export.", nameof(frames));
        var o = options.Normalized();
        int fw = frames[0].Image.Width, fh = frames[0].Image.Height;
        int n = frames.Count;
        int cols = o.Layout switch { SheetLayout.Horizontal => n, SheetLayout.Vertical => 1, _ => Math.Min(o.Columns, n) };
        int rows = (n + cols - 1) / cols;
        int cellW = fw + 2 * o.Padding, cellH = fh + 2 * o.Padding;
        int width = 2 * o.Margin + cols * cellW + (cols - 1) * o.Spacing;
        int height = 2 * o.Margin + rows * cellH + (rows - 1) * o.Spacing;
        RasterImage.Validate(width, height);
        var sheet = new RasterImage(width, height);
        var rects = new List<PixelRect>();
        for (int i = 0; i < n; i++)
        {
            int col = i % cols, row = i / cols;
            int x = o.Margin + col * (cellW + o.Spacing) + o.Padding;
            int y = o.Margin + row * (cellH + o.Spacing) + o.Padding;
            sheet.Paste(frames[i].Image, x, y);
            rects.Add(new PixelRect(x, y, fw, fh));
        }
        sheet.ExactPalette = frames[0].Image.ExactPalette;
        return new SpriteSheet(sheet, rects, frames.Select(f => f.DurationMs).ToList(), cols, rows);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// Sprite-sheet metadata as JSON (documented in docs/iterations/006-make-it-move/README.md):
    /// <c>{ "format", "version", "image", "size": {w,h}, "frameSize": {w,h}, "columns", "rows", "fps", "loop",
    /// "frames": [ { "index", "x", "y", "w", "h", "durationMs" } ] }</c>. Coordinates are in sheet pixels, top-left origin.
    /// </summary>
    public static string SheetJson(SpriteSheet sheet, string imageFileName, double fps, bool loop)
    {
        var frameSize = sheet.FrameRects[0];
        var data = new SheetData
        {
            Image = imageFileName,
            Size = new SizeData { W = sheet.Image.Width, H = sheet.Image.Height },
            FrameSize = new SizeData { W = frameSize.Width, H = frameSize.Height },
            Columns = sheet.Columns,
            Rows = sheet.Rows,
            Fps = fps,
            Loop = loop,
            Frames = sheet.FrameRects.Select((r, i) => new FrameData { Index = i, X = r.X, Y = r.Y, W = r.Width, H = r.Height, DurationMs = sheet.DurationsMs[i] }).ToList(),
        };
        return JsonSerializer.Serialize(data, Json);
    }

    /// <summary>File names for a numbered PNG sequence: <c>walk.png</c> → <c>walk_0001.png</c>, <c>walk_0002.png</c>…</summary>
    public static IReadOnlyList<string> SequencePaths(string chosenPath, int count)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(chosenPath)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(chosenPath);
        if (string.IsNullOrWhiteSpace(name)) name = "frame";
        int digits = Math.Max(4, count.ToString().Length);
        return Enumerable.Range(1, count).Select(i => Path.Combine(dir, $"{name}_{i.ToString().PadLeft(digits, '0')}.png")).ToList();
    }

    /// <summary>Writes each frame as its own PNG (each written safely: temporary file, then replace).</summary>
    public static void WriteSequence(IReadOnlyList<ExportFrame> frames, IReadOnlyList<string> paths)
    {
        if (paths.Count != frames.Count) throw new ArgumentException("One file name per frame is needed.", nameof(paths));
        var encoded = new byte[frames.Count][];
        Parallel.For(0, frames.Count, i => encoded[i] = PngCodec.Encode(frames[i].Image));
        for (int i = 0; i < frames.Count; i++) WriteAtomically(paths[i], encoded[i]);
    }

    public static void WriteAtomically(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(full) ?? ".", $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private sealed class SheetData
    {
        [JsonPropertyName("format")] public string Format { get; set; } = "DoodleNook sprite sheet";
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("image")] public string? Image { get; set; }
        [JsonPropertyName("size")] public SizeData? Size { get; set; }
        [JsonPropertyName("frameSize")] public SizeData? FrameSize { get; set; }
        [JsonPropertyName("columns")] public int Columns { get; set; }
        [JsonPropertyName("rows")] public int Rows { get; set; }
        [JsonPropertyName("fps")] public double Fps { get; set; }
        [JsonPropertyName("loop")] public bool Loop { get; set; }
        [JsonPropertyName("frames")] public List<FrameData> Frames { get; set; } = new();
    }

    private sealed class SizeData
    {
        [JsonPropertyName("w")] public int W { get; set; }
        [JsonPropertyName("h")] public int H { get; set; }
    }

    private sealed class FrameData
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("w")] public int W { get; set; }
        [JsonPropertyName("h")] public int H { get; set; }
        [JsonPropertyName("durationMs")] public int DurationMs { get; set; }
    }
}
