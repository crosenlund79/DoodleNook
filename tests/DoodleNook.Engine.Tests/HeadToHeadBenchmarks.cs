using System.Diagnostics;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Mission 011: head-to-head measurements of small, frequent costs that the long <see cref="UnderTheHoodBenchmarks"/> run
/// can't resolve reliably — thin brushes (a few hundredths of a millisecond per pen event) and saving and PNG work in a
/// warmed-up process — so the same file, run on the code before and after the mission, compares like with like.
/// Results go to tests/DoodleNook.Engine.Tests/out/head-to-head.txt. No timing assertions.
/// </summary>
[Collection(MeasurementCollection.Name)]
public class HeadToHeadBenchmarks
{
    static double Run(Document doc, BrushSettings brush)
    {
        var engine = new StrokeEngine(doc);
        double w = doc.Width, h = doc.Height;
        StrokeSample At(double t) => new(w * 0.1 + w * 0.8 * t, h * 0.5 + h * 0.3 * Math.Sin(t * Math.PI * 3), (float)(0.3 + 0.7 * Math.Sin(Math.PI * t)));
        var times = new List<double>();
        var sw = new Stopwatch();
        engine.Begin(new StrokeStyle(brush, PressureCurve.Normal, ColorRgb.Parse("#3B2F2A"), PaintMode.Paint), At(0));
        var batch = new StrokeSample[2];
        for (int e = 0; e < 200; e++)
        {
            batch[0] = At((2 * e + 1) / 400.0); batch[1] = At((2 * e + 2) / 400.0);
            sw.Restart(); engine.Add(batch); times.Add(sw.Elapsed.TotalMilliseconds);
        }
        engine.End(); doc.Undo();
        return times.Average();
    }
    [Fact]
    public void Measure_thin_strokes_and_saving_after_a_long_warm_up()
    {
        var lines = new List<string>();
        var one = UnderTheHoodBenchmarks.Layered(1920, 1080, 1, 0);
        var three = UnderTheHoodBenchmarks.Layered(1920, 1080, 3, 2);
        var ink = new BrushSettings { Size = 24, Hardness = 1, PressureSize = true };
        var pencil = new BrushSettings { Size = 6, Hardness = 0.85, PressureSize = true, Grain = 0.55 };
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 4000) { Run(one, ink); Run(three, pencil); Run(three, ink); Run(one, pencil); }
        foreach (var (name, doc) in new[] { ("1 layer", one), ("3 layers", three) })
            foreach (var (bn, b) in new[] { ("ink", ink), ("pencil", pencil) })
            {
                var t = new List<double>();
                for (int i = 0; i < 15; i++) t.Add(Run(doc, b));
                t.Sort();
                lines.Add($"{name} {bn}: median {t[7]:0.0000} min {t[0]:0.0000}");
            }
        // Saving and PNG, warmed up: an animation's .dnk, a layered picture's .dnk, sprite and picture PNGs.
        var anim = Document.NewAnimation(1920, 1080, ColorRgb.White, 24, 12, "Bg");
        UnderTheHoodBenchmarks.Paint(anim, anim.ActiveLayer, 0);
        var cels = anim.AddLayer("Ink");
        for (int f = 0; f < 24; f++)
        {
            anim.SetCurrentFrame(f);
            UnderTheHoodBenchmarks.Paint(anim, cels, f);
        }
        var pic = UnderTheHoodBenchmarks.Layered(2560, 1440, 6, 3);
        var sprite = new RasterImage(256, 256);
        for (int i = 0; i < sprite.Pixels.Length; i++)
            sprite.Pixels[i] = (i / 256) % 9 == 0 ? 0 : Pixel.Opaque(new ColorRgb((byte)(i % 23 * 11), (byte)(i / 256), 60));
        double T(Action a, int n)
        {
            var t = new List<double>();
            for (int i = 0; i < n; i++)
            {
                var sw = Stopwatch.StartNew();
                a();
                t.Add(sw.Elapsed.TotalMilliseconds);
            }
            t.Sort();
            return t[n / 2];
        }
        for (int w = 0; w < 3; w++)
        {
            DoodleNook.Engine.Files.DnkFile.Write(anim, Stream.Null);
            DoodleNook.Engine.Files.DnkFile.Write(pic, Stream.Null);
            for (int i = 0; i < 40; i++) DoodleNook.Engine.Files.PngCodec.Encode(sprite);
        }
        Thread.Sleep(500);
        var flat = pic.Flatten();
        var spriteBytes = DoodleNook.Engine.Files.PngCodec.Encode(sprite);
        var flatBytes = DoodleNook.Engine.Files.PngCodec.Encode(flat);
        lines.Add($"save .dnk, 1920x1080 animation, 24 drawings: median {T(() => DoodleNook.Engine.Files.DnkFile.Write(anim, Stream.Null), 7):0.0} ms");
        lines.Add($"save .dnk, 2560x1440 picture, 6 layers: median {T(() => DoodleNook.Engine.Files.DnkFile.Write(pic, Stream.Null), 5):0.0} ms");
        lines.Add($"PNG encode 256x256 sprite: median {T(() => DoodleNook.Engine.Files.PngCodec.Encode(sprite), 41):0.000} ms");
        lines.Add($"PNG decode 256x256 sprite: median {T(() => DoodleNook.Engine.Files.PngCodec.Decode(spriteBytes), 41):0.000} ms");
        lines.Add($"PNG encode 2560x1440 picture: median {T(() => DoodleNook.Engine.Files.PngCodec.Encode(flat), 5):0.0} ms");
        lines.Add($"PNG decode 2560x1440 picture: median {T(() => DoodleNook.Engine.Files.PngCodec.Decode(flatBytes), 9):0.0} ms");
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(Environment.NewLine, lines));
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        if (dir is not null)
        {
            var outDir = Path.Combine(dir.FullName, "tests", "DoodleNook.Engine.Tests", "out");
            Directory.CreateDirectory(outDir);
            File.WriteAllLines(Path.Combine(outDir, Environment.GetEnvironmentVariable("THIN_OUT") ?? "head-to-head.txt"), lines);
        }
    }
}
