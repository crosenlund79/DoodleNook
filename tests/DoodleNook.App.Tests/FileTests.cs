using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Settings;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Tests;

public sealed class FileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-tests-" + Guid.NewGuid().ToString("N"));

    public FileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Document Painted(ColorRgb? background)
    {
        var doc = new Document(120, 80, background);
        var engine = new StrokeEngine(doc);
        var style = new StrokeStyle(new BrushSettings { Size = 20, Opacity = 0.5, Hardness = 0.3 }, PressureCurve.Normal, new ColorRgb(30, 90, 200), PaintMode.Paint);
        engine.Begin(style, new StrokeSample(10, 40));
        engine.Add(new StrokeSample(110, 40));
        engine.End();
        return doc;
    }

    [Fact]
    public void Png_round_trip_preserves_transparency_exactly()
    {
        var doc = Painted(background: null);
        var path = Path.Combine(_dir, "transparent.png");
        PngFile.Save(doc, path);
        var back = PngFile.Open(path);
        Assert.Equal(doc.Width, back.Width);
        Assert.Equal(doc.Height, back.Height);
        Assert.Null(back.Background);
        Assert.Equal(doc.Layer.Pixels, back.Layer.Pixels);
        Assert.Equal(Pixel.Transparent, back.Layer[0, 0]);
        Assert.InRange(Pixel.A(back.Layer[60, 40]), 100, 140); // the half-transparent stroke stays half-transparent
    }

    [Fact]
    public void Png_with_white_background_is_saved_flattened_and_opaque()
    {
        var doc = Painted(ColorRgb.White);
        var path = Path.Combine(_dir, "white.png");
        PngFile.Save(doc, path);
        var back = PngFile.Open(path);
        Assert.Equal(doc.Flatten().Pixels, back.Layer.Pixels);
        Assert.All(back.Layer.Pixels, p => Assert.Equal(255, Pixel.A(p)));
    }

    [Fact]
    public void Saving_over_an_existing_file_replaces_it_and_leaves_no_temp_files()
    {
        var path = Path.Combine(_dir, "picture.png");
        PngFile.Save(new Document(10, 10, ColorRgb.White), path);
        PngFile.Save(Painted(null), path);
        Assert.Equal(120, PngFile.Open(path).Width);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Opening_a_non_image_gives_a_readable_error()
    {
        var path = Path.Combine(_dir, "notes.png");
        File.WriteAllText(path, "this is not a picture");
        var ex = Assert.Throws<InvalidDataException>(() => PngFile.Open(path));
        Assert.Contains("isn't an image", ex.Message);
    }

    [Fact]
    public void Settings_round_trip_and_survive_a_broken_file()
    {
        var path = Path.Combine(_dir, "settings.json");
        var session = new EditorSession();
        session.ApplyPreset("marker");
        session.SetSize(33);
        session.SetForeground(new ColorRgb(1, 2, 3));
        session.SetPressureFeel(PressureFeel.Soft);
        var s = new AppSettings();
        s.CaptureFrom(session);
        s.WindowX = 100;
        s.Save(path);

        var other = new EditorSession();
        AppSettings.Load(path).ApplyTo(other);
        Assert.Equal(33, other.Brush.Size);
        Assert.Equal("marker", other.BrushPresetId);
        Assert.Equal(new ColorRgb(1, 2, 3), other.Foreground);
        Assert.Equal(PressureFeel.Soft, other.PressureFeel);

        File.WriteAllText(path, "{ not json");
        Assert.Null(AppSettings.Load(path).Brush);
    }
}
