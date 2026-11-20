using System.Text.Json.Nodes;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

public sealed class AnimationExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-export-" + Guid.NewGuid().ToString("N"));

    public AnimationExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly ColorRgb Ink = new(40, 30, 60);

    /// <summary>A 3-frame 8 × 6 pixel animation: a pixel moving right, transparent background.</summary>
    private static Document PixelAnimation()
    {
        var doc = Document.NewAnimation(8, 6, null, frames: 1, fps: 10);
        var pencil = new PixelStroke(doc);
        for (int f = 0; f < 3; f++)
        {
            if (f > 0) doc.NewFrame();
            pencil.Begin(new PixelStrokeStyle(Ink, false, 1, false, Symmetry.None, false, "Pixel"), 1 + 2 * f, 2);
            pencil.End();
        }
        doc.SetFrameDuration(1, 250);
        return doc;
    }

    [Fact]
    public void Frames_render_exactly_with_durations_and_integer_scaling()
    {
        var doc = PixelAnimation();
        var frames = AnimationExport.RenderFrames(doc, includePaper: false);
        Assert.Equal(3, frames.Count);
        Assert.Equal([100, 250, 100], frames.Select(f => f.DurationMs).ToArray());
        Assert.Equal(Pixel.Opaque(Ink), frames[2].Image[5, 2]);
        Assert.Equal(0u, frames[2].Image[1, 2]);

        var big = AnimationExport.RenderFrames(doc, includePaper: false, scale: 4);
        Assert.Equal(32, big[0].Image.Width);
        for (int y = 8; y < 12; y++)
            for (int x = 4; x < 8; x++) Assert.Equal(Pixel.Opaque(Ink), big[0].Image[x, y]);
        Assert.Equal(0u, big[0].Image[8, 8]);
    }

    [Theory]
    [InlineData(SheetLayout.Horizontal, 2, 0, 0, 24, 6)]
    [InlineData(SheetLayout.Vertical, 2, 0, 0, 8, 18)]
    [InlineData(SheetLayout.Grid, 2, 0, 0, 16, 12)]
    [InlineData(SheetLayout.Grid, 2, 3, 1, 3 + 2 * 10 + 3 + 3, 3 + 2 * 8 + 3 + 3)]
    public void Sprite_sheets_have_exact_sizes_and_frame_positions(SheetLayout layout, int columns, int margin, int padding, int width, int height)
    {
        var o = new SpriteSheetOptions(layout, columns, Spacing: margin, Padding: padding, Margin: margin);
        var sheet = AnimationExport.BuildSheet(AnimationExport.RenderFrames(PixelAnimation(), false), o);
        Assert.Equal(width, sheet.Image.Width);
        Assert.Equal(height, sheet.Image.Height);
        for (int i = 0; i < 3; i++)
        {
            var r = sheet.FrameRects[i];
            Assert.Equal((8, 6), (r.Width, r.Height));
            Assert.Equal(Pixel.Opaque(Ink), sheet.Image[r.X + 1 + 2 * i, r.Y + 2]); // each frame's own pixel, in order
        }
        Assert.Equal(0u, sheet.Image[0, 0]); // transparent background
    }

    [Fact]
    public void Sprite_sheet_json_describes_every_frame()
    {
        var sheet = AnimationExport.BuildSheet(AnimationExport.RenderFrames(PixelAnimation(), false), new SpriteSheetOptions(SheetLayout.Grid, 2, Spacing: 1));
        var json = JsonNode.Parse(AnimationExport.SheetJson(sheet, "walk.png", 10, true))!;
        Assert.Equal("walk.png", (string)json["image"]!);
        Assert.Equal(sheet.Image.Width, (int)json["size"]!["w"]!);
        Assert.Equal(8, (int)json["frameSize"]!["w"]!);
        var frames = json["frames"]!.AsArray();
        Assert.Equal(3, frames.Count);
        Assert.Equal(9, (int)frames[1]!["x"]!);
        Assert.Equal(7, (int)frames[2]!["y"]!);
        Assert.Equal(250, (int)frames[1]!["durationMs"]!);
    }

    [Fact]
    public void Png_sequences_are_numbered_in_order_and_exact()
    {
        var frames = AnimationExport.RenderFrames(PixelAnimation(), false);
        var paths = AnimationExport.SequencePaths(Path.Combine(_dir, "walk.png"), frames.Count);
        Assert.Equal(["walk_0001.png", "walk_0002.png", "walk_0003.png"], paths.Select(p => Path.GetFileName(p)!).ToArray());
        AnimationExport.WriteSequence(frames, paths);
        for (int i = 0; i < 3; i++)
            Assert.Equal(frames[i].Image.Pixels, PngCodec.Decode(File.ReadAllBytes(paths[i])).Pixels);
        Assert.Equal(3, Directory.GetFiles(_dir).Length); // no temporary files left
    }

    [Fact]
    public void Gif_keeps_exact_pixel_art_colours_transparency_timing_and_loops()
    {
        var frames = AnimationExport.RenderFrames(PixelAnimation(), false);
        var path = Path.Combine(_dir, "walk.gif");
        GifEncoder.Save(frames, path, new GifOptions(Loop: true));
        var gif = TinyGif.Decode(File.ReadAllBytes(path));
        Assert.True(gif.Loops);
        Assert.Equal(3, gif.Frames.Count);
        Assert.Equal([10, 25, 10], gif.Frames.Select(f => f.DelayCs).ToArray());
        Assert.All(gif.Frames, f => Assert.Equal(2, f.Disposal));
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal((40, 30, 60, 255), gif.Frames[i].Rgba(1 + 2 * i, 2));
            Assert.Equal(0, gif.Frames[i].Rgba(0, 0).A);
        }
    }

    [Fact]
    public void Gif_merges_held_frames_and_reduces_many_colours()
    {
        // A smooth gradient (thousands of colours) held for two frames, then a different frame.
        var a = new RasterImage(64, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 64; x++) a[x, y] = Pixel.Opaque(new ColorRgb((byte)(x * 4), (byte)(y * 8), (byte)((x + y) * 2)));
        var b = a.Clone();
        b[0, 0] = Pixel.Opaque(ColorRgb.White); // (0,0) of the gradient is black already
        var frames = new List<ExportFrame> { new(a, 40, 0), new(a.Clone(), 40, 1), new(b, 40, 2) };
        using var ms = new MemoryStream();
        GifEncoder.Write(frames, ms, new GifOptions(Loop: false, Dither: true));
        var gif = TinyGif.Decode(ms.ToArray());
        Assert.False(gif.Loops);
        Assert.Equal(2, gif.Frames.Count);
        Assert.Equal(8, gif.Frames[0].DelayCs); // 80 ms held
        Assert.Equal(1, gif.Frames[0].Disposal);
        var (r, g, bl, alpha) = gif.Frames[0].Rgba(40, 20);
        Assert.Equal(255, alpha);
        Assert.InRange(r, 160 - 40, 160 + 40);
        Assert.InRange(g, 160 - 40, 160 + 40);
        Assert.InRange(bl, 120 - 40, 120 + 40);
    }

    [Fact]
    public void Gif_delays_do_not_drift_over_long_animations()
    {
        var img = new RasterImage(2, 2);
        var frames = Enumerable.Range(0, 24).Select(i => new ExportFrame(i % 2 == 0 ? img : Changed(img), 1000 / 24, i)).ToList();
        using var ms = new MemoryStream();
        GifEncoder.Write(frames, ms, new GifOptions());
        int total = TinyGif.Decode(ms.ToArray()).Frames.Sum(f => f.DelayCs);
        Assert.InRange(total, 98, 99); // 24 × 41 ms = 0.984 s; rounding every frame to 4 cs would give 0.96 s

        static RasterImage Changed(RasterImage i)
        {
            var c = i.Clone();
            c[0, 0] = Pixel.Opaque(ColorRgb.White);
            return c;
        }
    }

    [Fact]
    public void Gif_lzw_round_trips_long_and_repetitive_data()
    {
        for (int n = 1; n < 300; n++) // every short length, so the final code lands on each code-width boundary
        {
            var shortData = Enumerable.Range(0, n).Select(i => (byte)(i * 7 % 3)).ToArray();
            Assert.Equal(shortData, TinyGif.LzwDecode(GifEncoder.Lzw(shortData, 2), 2, n));
        }
        var rnd = new Random(7);
        var data = Enumerable.Range(0, 70_000).Select(i => (byte)(i % 300 < 150 ? rnd.Next(4) : i % 4)).ToArray();
        var packed = GifEncoder.Lzw(data, 2);
        Assert.Equal(data, TinyGif.LzwDecode(packed, 2, data.Length));
    }
}

/// <summary>A minimal, independent GIF decoder used only to check the encoder.</summary>
internal sealed class TinyGif
{
    public bool Loops { get; private set; }
    public List<GifFrame> Frames { get; } = new();

    public sealed record GifFrame(int Width, int[] Palette, byte[] Indices, int Transparent, int DelayCs, int Disposal)
    {
        public (int R, int G, int B, int A) Rgba(int x, int y)
        {
            int i = Indices[y * Width + x];
            if (i == Transparent) return (0, 0, 0, 0);
            int c = Palette[i];
            return ((c >> 16) & 255, (c >> 8) & 255, c & 255, 255);
        }
    }

    public static TinyGif Decode(byte[] d)
    {
        var gif = new TinyGif();
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(d, 0, 6));
        int w = d[6] | d[7] << 8, h = d[8] | d[9] << 8;
        int flags = d[10];
        int p = 13;
        int[] palette = [];
        if ((flags & 0x80) != 0)
        {
            int n = 1 << ((flags & 7) + 1);
            palette = new int[n];
            for (int i = 0; i < n; i++, p += 3) palette[i] = d[p] << 16 | d[p + 1] << 8 | d[p + 2];
        }
        int transparent = -1, delay = 0, disposal = 0;
        while (true)
        {
            byte b = d[p++];
            if (b == 0x3B) break;
            if (b == 0x21)
            {
                byte label = d[p++];
                if (label == 0xF9)
                {
                    int packed = d[p + 1];
                    disposal = (packed >> 2) & 7;
                    delay = d[p + 2] | d[p + 3] << 8;
                    transparent = (packed & 1) != 0 ? d[p + 4] : -1;
                }
                if (label == 0xFF && System.Text.Encoding.ASCII.GetString(d, p + 1, 11) == "NETSCAPE2.0") gif.Loops = true;
                while (d[p] != 0) p += d[p] + 1;
                p++;
                continue;
            }
            Assert.Equal(0x2C, b);
            int fw = d[p + 4] | d[p + 5] << 8, fh = d[p + 6] | d[p + 7] << 8;
            Assert.Equal((w, h), (fw, fh));
            p += 9;
            int minCode = d[p++];
            var data = new List<byte>();
            while (d[p] != 0)
            {
                data.AddRange(d.AsSpan(p + 1, d[p]).ToArray());
                p += d[p] + 1;
            }
            p++;
            gif.Frames.Add(new GifFrame(w, palette, LzwDecode(data.ToArray(), minCode, w * h), transparent, delay, disposal));
        }
        return gif;
    }

    public static byte[] LzwDecode(byte[] data, int minCode, int count)
    {
        int clear = 1 << minCode, eoi = clear + 1;
        var table = new List<byte[]>();
        void Reset()
        {
            table.Clear();
            for (int i = 0; i < clear; i++) table.Add([(byte)i]);
            table.Add([]);
            table.Add([]);
        }
        Reset();
        int size = minCode + 1, bitPos = 0;
        var output = new List<byte>(count);
        byte[]? prev = null;
        while (true)
        {
            int code = 0;
            for (int i = 0; i < size; i++, bitPos++)
                if ((data[bitPos >> 3] >> (bitPos & 7) & 1) != 0) code |= 1 << i;
            if (code == clear)
            {
                Reset();
                size = minCode + 1;
                prev = null;
                continue;
            }
            if (code == eoi) break;
            byte[] entry = code < table.Count ? table[code] : [.. prev!, prev![0]];
            output.AddRange(entry);
            if (prev is not null && table.Count < 4096) table.Add([.. prev, entry[0]]);
            prev = entry;
            if (table.Count >= 1 << size && size < 12) size++;
        }
        return output.ToArray();
    }
}
