using System.IO.Compression;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public sealed class FileFormatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dnk-tests-" + Guid.NewGuid().ToString("N"));

    public FileFormatTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Png_codec_round_trips_exactly_including_transparency()
    {
        var rng = new Random(5);
        var img = new RasterImage(37, 23);
        for (int i = 0; i < img.Pixels.Length; i++)
            img.Pixels[i] = Pixel.FromStraight((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
        var back = PngCodec.Decode(PngCodec.Encode(img));
        Assert.Equal(img.Pixels, back.Pixels);
        Assert.Throws<InvalidDataException>(() => PngCodec.Decode(new byte[] { 1, 2, 3 }));
    }

    private static Document Sample()
    {
        var doc = new Document(120, 80, new ColorRgb(250, 245, 235));
        Stroke(doc, Paint(Hard(10), new ColorRgb(200, 0, 0)), S(10, 10), S(110, 70));
        var b = doc.AddLayer("Ink");
        Stroke(doc, Paint(Hard(6, opacity: 0.5), new ColorRgb(0, 0, 200)), S(10, 70), S(110, 10));
        doc.SetLayerProperties(b, b.Properties with { Opacity = 0.75, Blend = BlendMode.Multiply, AlphaLocked = true, Label = LayerLabel.Blue });
        var hidden = doc.AddLayer("Hidden notes");
        Stroke(doc, Paint(Hard(4)), S(60, 40));
        doc.SetLayerProperties(hidden, hidden.Properties with { Visible = false, Locked = true, ClipToBelow = true });
        doc.SetActiveLayer(b);
        doc.Palette = new Palette("Mine", [ColorRgb.Black, new ColorRgb(9, 8, 7)]);
        return doc;
    }

    [Fact]
    public void Dnk_round_trip_keeps_layers_pixels_properties_paper_and_palette()
    {
        var doc = Sample();
        var path = Path.Combine(_dir, "art.dnk");
        DnkFile.Save(doc, path, "0.5.0");
        var back = DnkFile.Load(path);

        Assert.Equal((doc.Width, doc.Height), (back.Width, back.Height));
        Assert.Equal(doc.Paper, back.Paper);
        Assert.Equal(doc.Layers.Count, back.Layers.Count);
        for (int i = 0; i < doc.Layers.Count; i++)
        {
            Assert.Equal(doc.Layers[i].Properties, back.Layers[i].Properties);
            Assert.Equal(doc.Layers[i].Image.Pixels, back.Layers[i].Image.Pixels);
        }
        Assert.Equal(1, back.ActiveIndex);
        Assert.Equal("Mine", back.Palette.Name);
        Assert.Equal(doc.Palette.Colors, back.Palette.Colors);
        Assert.Equal(doc.Flatten().Pixels, back.Flatten().Pixels);
        Assert.False(back.IsDirty);
        Assert.Single(Directory.GetFiles(_dir)); // no temporary files left behind
    }

    [Fact]
    public void Dnk_is_an_inspectable_zip_with_a_versioned_manifest()
    {
        var path = Path.Combine(_dir, "art.dnk");
        DnkFile.Save(Sample(), path);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Contains(zip.Entries, e => e.FullName == "document.json");
        Assert.Contains(zip.Entries, e => e.FullName == "thumbnail.png");
        Assert.Equal(3, zip.Entries.Count(e => e.FullName.StartsWith("layers/")));
        using var reader = new StreamReader(zip.GetEntry("document.json")!.Open());
        var json = reader.ReadToEnd();
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"blend\": \"Multiply\"", json);
    }

    [Fact]
    public void Newer_versions_and_damaged_files_are_refused_with_clear_messages()
    {
        var path = Path.Combine(_dir, "future.dnk");
        DnkFile.Save(Sample(), path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("document.json")!;
            string json;
            using (var r = new StreamReader(entry.Open())) json = r.ReadToEnd();
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry("document.json").Open());
            w.Write(json.Replace("\"version\": 1", "\"version\": 7"));
        }
        var ex = Assert.Throws<DnkFormatException>(() => DnkFile.Load(path));
        Assert.Contains("newer DoodleNook", ex.Message);

        var junk = Path.Combine(_dir, "junk.dnk");
        File.WriteAllText(junk, "not a zip");
        Assert.Throws<DnkFormatException>(() => DnkFile.Load(junk));

        var missing = Path.Combine(_dir, "missing.dnk");
        DnkFile.Save(Sample(), missing);
        using (var zip = ZipFile.Open(missing, ZipArchiveMode.Update)) zip.GetEntry("layers/layer-002.png")!.Delete();
        Assert.Contains("missing", Assert.Throws<DnkFormatException>(() => DnkFile.Load(missing)).Message);
    }

    [Fact]
    public void Failed_save_leaves_the_previous_file_untouched()
    {
        var path = Path.Combine(_dir, "keep.dnk");
        DnkFile.Save(Sample(), path);
        var before = File.ReadAllBytes(path);
        Assert.ThrowsAny<Exception>(() => DnkFile.Save(Sample(), Path.Combine(_dir, "no-such-folder", "x.dnk")));
        if (OperatingSystem.IsWindows())
        {
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // The target is in use: the save must fail without corrupting it (Windows file locking reports
                // this as IOException or UnauthorizedAccessException, depending on how the file is held).
                var ex = Assert.ThrowsAny<Exception>(() => DnkFile.Save(new Document(10, 10), path));
                Assert.True(ex is IOException or UnauthorizedAccessException, ex.GetType().Name);
                Assert.Single(Directory.GetFiles(_dir)); // no temporary file left behind
            }
        }
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>The Mission 005 brief's multi-tool scenario, end to end.</summary>
    [Fact]
    public void Paint_two_layers_transform_undo_redo_save_and_reopen()
    {
        var doc = new Document(200, 120, ColorRgb.White);
        var a = doc.ActiveLayer;
        Stroke(doc, Paint(Hard(12), new ColorRgb(200, 50, 40)), S(20, 20), S(180, 20));          // 1. paint on A
        var b = doc.AddLayer("B");                                                                  // 2. layer B
        Stroke(doc, Paint(Hard(10), new ColorRgb(40, 60, 200)), S(20, 80), S(100, 80));            // 3. paint on B
        doc.SetSelection(SelectionMask.Rectangle(200, 120, new PixelRect(10, 70, 60, 20)), "Select");
        var ft = FloatingTransform.Begin(doc, b, "Move")!;                                          // 4. transform part of B
        ft.MoveTo(0, 30);
        ft.Commit();
        var final = doc.Flatten().Pixels;
        var aPixels = (uint[])a.Image.Pixels.Clone();
        var bPixels = (uint[])b.Image.Pixels.Clone();

        for (int i = 0; i < 3; i++) Assert.True(doc.Undo());                                        // 5. undo several
        Assert.True(b.Image.IsBlank());
        Assert.NotEqual(final, doc.Flatten().Pixels);
        while (doc.Redo()) { }                                                                      // 6. redo
        Assert.Equal(final, doc.Flatten().Pixels);

        var path = Path.Combine(_dir, "workflow.dnk");
        DnkFile.Save(doc, path);                                                                    // 7. save
        var back = DnkFile.Load(path);                                                              // 8. reopen
        Assert.Equal(aPixels, back.Layers[0].Image.Pixels);                                         // 9. content correct
        Assert.Equal(bPixels, back.Layers[1].Image.Pixels);
        Assert.Equal(final, back.Flatten().Pixels);
        Assert.Equal("B", back.Layers[1].Name);
    }
}
