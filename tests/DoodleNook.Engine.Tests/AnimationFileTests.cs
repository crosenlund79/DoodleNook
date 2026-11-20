using System.IO.Compression;
using System.Text.Json.Nodes;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public sealed class AnimationFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-anim-" + Guid.NewGuid().ToString("N"));

    public AnimationFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "DoodleNook.Engine.Tests", "Fixtures", name);
    }

    private static void Dot(Document doc, double x, double y, ColorRgb? c = null) =>
        Stroke(doc, Paint(Hard(8), c ?? Ink), S(x, y), S(x + 1, y));

    private static Document SampleAnimation()
    {
        var doc = Document.NewAnimation(80, 60, ColorRgb.White, firstLayerName: "Background");
        Dot(doc, 5, 55, new ColorRgb(120, 160, 90));
        var ink = doc.AddLayer("Ink");
        doc.SetLayerProperties(ink, ink.Properties with { Opacity = 0.8, Label = LayerLabel.Blue });
        Dot(doc, 10, 10);
        doc.ExtendHold(ink, 0);              // frames 0-1 hold one drawing
        doc.SetCurrentFrame(1);
        doc.NewFrame(NewFrameContent.Duplicate);
        Dot(doc, 40, 30);                     // frame 2 is an independent changed copy
        doc.NewFrame();                        // frame 3: empty cell on Ink
        doc.NewBlankCel(doc.Layers[0], 2);     // frames 2-3 of the background: one new, empty drawing
        doc.SetFrameDuration(2, 250);
        doc.SetTiming(8, loop: true, pingPong: true);
        doc.SetCurrentFrame(2);
        return doc;
    }

    [Fact]
    public void A_mission_005_file_still_opens_exactly()
    {
        var doc = DnkFile.Load(Fixture("mission005-two-layers.dnk"));
        Assert.False(doc.IsAnimated);
        Assert.Equal(2, doc.Layers.Count);
        Assert.Equal(["Sketch", "Layer 2"], doc.Layers.Select(l => l.Name).ToArray());
        var expected = PngCodec.Decode(File.ReadAllBytes(Fixture("mission005-two-layers-flat.png")));
        Assert.Equal(expected.Pixels, doc.Flatten().Pixels);
    }

    [Fact]
    public void Still_pictures_are_still_written_in_version_1()
    {
        var doc = new Document(30, 20, ColorRgb.White);
        Dot(doc, 10, 10);
        var path = Path.Combine(_dir, "still.dnk");
        DnkFile.Save(doc, path);
        using var zip = ZipFile.OpenRead(path);
        var manifest = JsonNode.Parse(zip.GetEntry("document.json")!.Open())!;
        Assert.Equal(1, (int)manifest["version"]!);
        Assert.Null(manifest["animation"]);
        Assert.Equal(doc.Flatten().Pixels, DnkFile.Load(path).Flatten().Pixels);
    }

    [Fact]
    public void Animations_round_trip_with_holds_timing_and_empty_cells()
    {
        var doc = SampleAnimation();
        var path = Path.Combine(_dir, "anim.dnk");
        DnkFile.Save(doc, path);
        var back = DnkFile.Load(path);

        Assert.True(back.IsAnimated);
        Assert.Equal(doc.FrameCount, back.FrameCount);
        Assert.Equal(2, back.CurrentFrame);
        Assert.Equal(8, back.Animation!.Fps);
        Assert.True(back.Animation.PingPong);
        Assert.Equal(250, back.Animation.DurationOverride(2));
        Assert.Null(back.Animation.DurationOverride(0));
        var ink = back.Layers[1];
        Assert.Equal(0.8, ink.Opacity, 3);
        Assert.Equal(LayerLabel.Blue, ink.Label);
        Assert.Same(back.CelAt(ink, 0), back.CelAt(ink, 1));    // still one held drawing
        Assert.NotSame(back.CelAt(ink, 1), back.CelAt(ink, 2));
        Assert.Null(back.CelAt(ink, 3));                          // empty cell
        var emptyDrawing = back.CelAt(back.Layers[0], 3);          // an empty drawing is kept as a drawing
        Assert.NotNull(emptyDrawing);
        Assert.True(emptyDrawing!.IsEmpty);
        Assert.Same(emptyDrawing, back.CelAt(back.Layers[0], 2));
        for (int f = 0; f < doc.FrameCount; f++)
            Assert.Equal(doc.RenderFrame(f).Pixels, back.RenderFrame(f).Pixels);
        Assert.False(back.CanUndo);
        Assert.False(back.IsDirty);
    }

    [Fact]
    public void Drawings_are_stored_cropped_to_their_painted_area()
    {
        var doc = SampleAnimation();
        var path = Path.Combine(_dir, "crop.dnk");
        DnkFile.Save(doc, path);
        using var zip = ZipFile.OpenRead(path);
        var manifest = JsonNode.Parse(zip.GetEntry("document.json")!.Open())!;
        Assert.Equal(2, (int)manifest["version"]!);
        var cels = zip.Entries.Where(e => e.FullName.StartsWith("cels/")).ToList();
        Assert.NotEmpty(cels);
        foreach (var e in cels)
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var img = PngCodec.Decode(ms.ToArray());
            Assert.True(img.Width < doc.Width || img.Height < doc.Height, e.FullName);
        }
    }

    [Theory]
    [InlineData("version", "4", "newer DoodleNook")] // one above the newest version (3 = palette lock)
    [InlineData("tracks", "[[1]]", "don't match")]
    [InlineData("missingCel", "", "missing")]
    public void Damaged_or_newer_animation_files_are_refused_clearly(string what, string value, string message)
    {
        var path = Path.Combine(_dir, "bad.dnk");
        DnkFile.Save(SampleAnimation(), path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("document.json")!;
            JsonNode manifest;
            using (var s = entry.Open()) manifest = JsonNode.Parse(s)!;
            entry.Delete();
            switch (what)
            {
                case "version": manifest["version"] = int.Parse(value); break;
                case "tracks": manifest["animation"]!["tracks"] = JsonNode.Parse(value); break;
                case "missingCel": manifest["animation"]!["tracks"]![1]![0] = 999; break;
            }
            using var w = new StreamWriter(zip.CreateEntry("document.json").Open());
            w.Write(manifest.ToJsonString());
        }
        var ex = Assert.Throws<DnkFormatException>(() => DnkFile.Load(path));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public async Task A_snapshot_is_unaffected_by_edits_made_while_it_is_written()
    {
        var doc = SampleAnimation();
        var expected = Enumerable.Range(0, doc.FrameCount).Select(f => doc.RenderFrame(f).Pixels).ToList();
        var snapshot = DnkFile.Snapshot(doc, "test");
        long state = snapshot.StateId;

        // Keep drawing (on a held drawing and an empty cell) before the background write happens.
        doc.SetCurrentFrame(0);
        Dot(doc, 70, 50);
        doc.SetCurrentFrame(3);
        Dot(doc, 60, 10);
        var path = Path.Combine(_dir, "snap.dnk");
        await Task.Run(() => DnkFile.Save(snapshot, path), TestContext.Current.CancellationToken);

        var back = DnkFile.Load(path);
        for (int f = 0; f < back.FrameCount; f++) Assert.Equal(expected[f], back.RenderFrame(f).Pixels);
        doc.MarkSaved(state);
        Assert.True(doc.IsDirty); // the later strokes are still unsaved
    }

    [Fact]
    public void A_failed_animation_save_leaves_the_previous_file_intact()
    {
        var path = Path.Combine(_dir, "keep.dnk");
        DnkFile.Save(SampleAnimation(), path);
        var before = File.ReadAllBytes(path);
        Assert.ThrowsAny<Exception>(() => DnkFile.Save(SampleAnimation(), Path.Combine(_dir, "missing-folder", "x.dnk")));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(_dir));
    }
}
