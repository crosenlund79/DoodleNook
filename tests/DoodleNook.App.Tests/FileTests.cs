using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Tests;

public sealed class FileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-tests-" + Guid.NewGuid().ToString("N"));

    public FileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Document Painted(ColorRgb? paper)
    {
        var doc = new Document(120, 80, paper);
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
        var doc = Painted(paper: null);
        var path = Path.Combine(_dir, "transparent.png");
        ImageFiles.Export(doc, path, ExportFormat.Png);
        var back = ImageFiles.Open(path);
        Assert.Equal(doc.Width, back.Width);
        Assert.Equal(doc.Height, back.Height);
        Assert.Null(back.Paper);
        Assert.Single(back.Layers);
        Assert.Equal("transparent", back.Layers[0].Name);
        Assert.Equal(doc.Layers[0].Image.Pixels, back.Layers[0].Image.Pixels);
        Assert.Equal(Pixel.Transparent, back.Layers[0].Image[0, 0]);
        Assert.InRange(Pixel.A(back.Layers[0].Image[60, 40]), 100, 140); // the half-transparent stroke stays half-transparent
    }

    [Fact]
    public void Png_with_white_paper_is_exported_flattened_and_opaque()
    {
        var doc = Painted(ColorRgb.White);
        var path = Path.Combine(_dir, "white.png");
        ImageFiles.Export(doc, path, ExportFormat.Png);
        var back = ImageFiles.Decode(path);
        Assert.Equal(doc.Flatten().Pixels, back.Pixels);
        Assert.All(back.Pixels, p => Assert.Equal(255, Pixel.A(p)));
    }

    [Fact]
    public void Jpeg_export_flattens_onto_white_and_webp_keeps_transparency()
    {
        var doc = Painted(paper: null);
        doc.AddLayer("Empty");
        var jpg = Path.Combine(_dir, "flat.jpg");
        var webp = Path.Combine(_dir, "flat.webp");
        ImageFiles.Export(doc, jpg, ExportFormat.Jpeg, 95);
        ImageFiles.Export(doc, webp, ExportFormat.WebP, 100);
        var j = ImageFiles.Decode(jpg);
        Assert.Equal(255, Pixel.A(j[0, 0]));
        Assert.True(Pixel.R(j[0, 0]) > 245 && Pixel.G(j[0, 0]) > 245, "transparent areas become white in JPEG");
        var w = ImageFiles.Decode(webp);
        Assert.Equal(0, Pixel.A(w[0, 0]));
        Assert.True(Pixel.A(w[60, 40]) > 90);
        Assert.Equal(2, doc.Layers.Count); // exporting never changes the open picture
    }

    [Fact]
    public void Dnk_project_keeps_layers_and_opens_through_the_same_helper()
    {
        var doc = Painted(ColorRgb.White);
        var top = doc.AddLayer("Ink");
        doc.SetLayerProperties(top, top.Properties with { Opacity = 0.5, Blend = BlendMode.Multiply });
        var path = Path.Combine(_dir, "layers.dnk");
        ImageFiles.SaveProject(doc, path, "test");
        Assert.True(ImageFiles.IsProject(path));
        var back = ImageFiles.Open(path);
        Assert.Equal(2, back.Layers.Count);
        Assert.Equal("Ink", back.Layers[1].Name);
        Assert.Equal(BlendMode.Multiply, back.Layers[1].Blend);
        Assert.Equal(doc.Flatten().Pixels, back.Flatten().Pixels);
    }

    [Fact]
    public void Saving_over_an_existing_file_replaces_it_and_leaves_no_temp_files()
    {
        var path = Path.Combine(_dir, "picture.png");
        ImageFiles.Export(new Document(10, 10, ColorRgb.White), path, ExportFormat.Png);
        ImageFiles.Export(Painted(null), path, ExportFormat.Png);
        Assert.Equal(120, ImageFiles.Decode(path).Width);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Opening_a_non_image_gives_a_readable_error()
    {
        var path = Path.Combine(_dir, "notes.png");
        File.WriteAllText(path, "this is not a picture");
        var ex = Assert.Throws<InvalidDataException>(() => ImageFiles.Open(path));
        Assert.Contains("isn't an image", ex.Message);
        var dnk = Path.Combine(_dir, "notes.dnk");
        File.WriteAllText(dnk, "this is not a project");
        Assert.Throws<DnkFormatException>(() => ImageFiles.Open(dnk));
    }

    [Fact]
    public void Export_formats_have_the_right_extensions()
    {
        Assert.Equal(ExportFormat.Jpeg, ImageFiles.FormatForPath("a.JPEG"));
        Assert.Equal(ExportFormat.WebP, ImageFiles.FormatForPath("a.webp"));
        Assert.Null(ImageFiles.FormatForPath("a.dnk"));
        Assert.False(ImageFiles.SupportsTransparency(ExportFormat.Jpeg));
        Assert.Equal(".jpg", ImageFiles.Extension(ExportFormat.Jpeg));
    }

    [Fact]
    public void Settings_round_trip_brushes_swatches_options_and_workspaces()
    {
        var path = Path.Combine(_dir, "settings.json");
        var session = new EditorSession();
        session.ApplyPreset("marker");
        session.SetSize(33);
        session.SetForeground(new ColorRgb(1, 2, 3));
        session.SetPressureFeel(PressureFeel.Soft);
        session.AddSwatch(new ColorRgb(9, 8, 7));
        session.SetFillOptions(session.Fill with { Tolerance = 77, Contiguous = false });
        session.SetPixelOptions(session.Pixel with { Size = 3, PixelPerfect = false });
        session.SetTransformInterpolation(Interpolation.Bicubic);
        var s = new AppSettings();
        s.CaptureFrom(session);
        s.LastWorkspace = WorkspaceKind.Editing;
        s.StateFor(WorkspaceKind.Painting).DockWidth = 333;
        s.StateFor(WorkspaceKind.Painting).ClosedSections.Add("colour");
        s.StateFor(WorkspaceKind.PixelArt).Tiled = true;
        s.StateFor(WorkspaceKind.PixelArt).Tool = ToolKind.PixelLine;
        s.WindowX = 100;
        s.Save(path);

        var loaded = AppSettings.Load(path);
        var other = new EditorSession();
        loaded.ApplyTo(other);
        Assert.Equal(33, other.Brush.Size);
        Assert.Equal("marker", other.BrushPresetId);
        Assert.Equal(new ColorRgb(1, 2, 3), other.Foreground);
        Assert.Equal(PressureFeel.Soft, other.PressureFeel);
        Assert.Contains(new ColorRgb(9, 8, 7), other.Swatches);
        Assert.Equal(77, other.Fill.Tolerance);
        Assert.False(other.Fill.Contiguous);
        Assert.Equal(3, other.Pixel.Size);
        Assert.Equal(Interpolation.Bicubic, other.TransformInterpolation);
        Assert.Equal(WorkspaceKind.Editing, loaded.LastWorkspace);
        Assert.Equal(333, loaded.StateFor(WorkspaceKind.Painting).DockWidth);
        Assert.Contains("colour", loaded.StateFor(WorkspaceKind.Painting).ClosedSections);
        Assert.True(loaded.StateFor(WorkspaceKind.PixelArt).Tiled);
        Assert.Equal(ToolKind.PixelLine, loaded.StateFor(WorkspaceKind.PixelArt).Tool);
        Assert.Contains("\"Editing\"", File.ReadAllText(path)); // enums are readable names

        File.WriteAllText(path, "{ not json");
        Assert.Null(AppSettings.Load(path).Brush);
    }
}
