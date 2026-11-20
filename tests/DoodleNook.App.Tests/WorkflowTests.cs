using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Whole workflows through the real window, across workspaces, with real files.</summary>
public sealed class WorkflowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-workflow-" + Guid.NewGuid().ToString("N"));

    public WorkflowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public async Task Sketch_then_paint_on_a_new_layer_save_as_dnk_and_reopen()
    {
        var w = Open(workspace: WorkspaceKind.QuickSketch);
        w.Session.ApplyPreset("pencil");
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 600), (800, 400), (1300, 600));
        var sketch = w.Session.Document.ActiveLayer;

        Key(w, Avalonia.Input.Key.D3, RawInputModifiers.Alt); // Painting
        Assert.Equal(WorkspaceKind.Painting, w.Workspace.Kind);
        Key(w, Avalonia.Input.Key.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        var colour = w.Session.Document.ActiveLayer;
        w.Session.ApplyPreset("wash");
        w.Session.SetForeground(ColorRgb.Parse("#9CC07A"));
        w.Session.SetSize(120);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (200, 800), (1700, 820));
        w.Session.SetLayerProperty(colour, p => p with { Opacity = 0.7, Blend = BlendMode.Multiply, Name = "Colour" });
        w.Session.SetLayerProperty(sketch, p => p with { Name = "Pencil" });
        Assert.True(Pixel.A(colour.Image[900, 810]) > 0);
        Assert.Equal(0, Pixel.A(sketch.Image[900, 810]));

        var path = Path.Combine(_dir, "landscape.dnk");
        Assert.True(await w.SaveToPathAsync(path));
        Assert.False(w.Session.Document.IsDirty);
        Assert.Equal($"DoodleNook — {MainWindow.Milestone} — landscape.dnk", w.Title);
        var flat = w.Session.Document.Flatten();
        w.Close();

        var again = Open(workspace: WorkspaceKind.Editing);
        Assert.True(await again.OpenPathAsync(path));
        var doc = again.Session.Document;
        Assert.Equal(2, doc.Layers.Count);
        Assert.Equal(["Pencil", "Colour"], doc.Layers.Select(l => l.Name).ToArray());
        Assert.Equal(BlendMode.Multiply, doc.Layers[1].Blend);
        Assert.Equal(0.7, doc.Layers[1].Opacity, 2);
        Assert.Equal(flat.Pixels, doc.Flatten().Pixels);
        Assert.False(doc.CanUndo); // a freshly opened picture starts a new history
        again.Close();
    }

    [AvaloniaFact]
    public async Task Open_png_select_transform_adjust_and_export_without_flattening_the_picture()
    {
        // A PNG made elsewhere.
        var img = new RasterImage(400, 300);
        for (int y = 0; y < 300; y++)
            for (int x = 0; x < 400; x++)
                img[x, y] = Pixel.Opaque(new ColorRgb((byte)(x * 255 / 399), (byte)(y * 255 / 299), 120));
        var source = Document.FromImage(img, "Photo");
        var png = Path.Combine(_dir, "photo.png");
        ImageFiles.Export(source, png, ExportFormat.Png);

        var w = Open(workspace: WorkspaceKind.Editing);
        Assert.True(await w.OpenPathAsync(png));
        Assert.Equal("photo", w.Session.Document.ActiveLayer.Name);
        Key(w, Avalonia.Input.Key.M);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (50, 50), (150, 150));
        Key(w, Avalonia.Input.Key.C, RawInputModifiers.Control);
        Key(w, Avalonia.Input.Key.V, RawInputModifiers.Control); // pasted as a new layer
        Assert.Equal(2, w.Session.Document.Layers.Count);
        Key(w, Avalonia.Input.Key.T, RawInputModifiers.Control);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 100), (300, 200));
        Key(w, Avalonia.Input.Key.Enter);
        var pasted = w.Session.Document.ActiveLayer;
        Assert.True(Pixel.A(pasted.Image[300, 200]) == 255);
        Assert.Equal(0, Pixel.A(pasted.Image[60, 60]));
        Key(w, Avalonia.Input.Key.U, RawInputModifiers.Control | RawInputModifiers.Shift); // grayscale the pasted layer
        var gray = pasted.Image[300, 200];
        Assert.Equal(Pixel.R(gray), Pixel.G(gray));

        // Undo the grayscale and the transform, then redo both.
        var coloured = (uint[])pasted.Image.Pixels.Clone();
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.NotEqual(Pixel.R(pasted.Image[300, 200]), Pixel.G(pasted.Image[300, 200]));
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(255, Pixel.A(pasted.Image[100, 100])); // back where it was pasted
        Assert.Equal(0, Pixel.A(pasted.Image[300, 200]));
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Key(w, Avalonia.Input.Key.Y, RawInputModifiers.Control);
        Assert.Equal(coloured, pasted.Image.Pixels);

        // Export: flattened copies; the open picture keeps both layers.
        var outPng = Path.Combine(_dir, "result.png");
        var outJpg = Path.Combine(_dir, "result.jpg");
        w.ExportToPath(outPng, ExportFormat.Png);
        w.ExportToPath(outJpg, ExportFormat.Jpeg, 90);
        Assert.Equal(w.Session.Document.Flatten().Pixels, ImageFiles.Decode(outPng).Pixels);
        Assert.Equal(400, ImageFiles.Decode(outJpg).Width);
        Assert.Equal(2, w.Session.Document.Layers.Count);

        // Ctrl+S never flattens layers into the original PNG.
        var before = File.ReadAllBytes(png);
        Key(w, Avalonia.Input.Key.S, RawInputModifiers.Control);
        Assert.Equal(before, File.ReadAllBytes(png));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Single_layer_png_saves_back_to_the_png_like_mission_004()
    {
        var png = Path.Combine(_dir, "doodle.png");
        ImageFiles.Export(new Document(200, 120, ColorRgb.White), png, ExportFormat.Png);
        var w = Open();
        Assert.True(await w.OpenPathAsync(png));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (20, 60), (180, 60));
        Assert.True(await w.SaveAsync());
        Assert.False(w.Session.Document.IsDirty);
        Assert.Equal(w.Session.Document.Flatten().Pixels, ImageFiles.Decode(png).Pixels);
        w.Close();
    }

    [AvaloniaFact]
    public void Pixel_art_new_sprite_draw_move_and_export_exact_png()
    {
        var w = Open(workspace: WorkspaceKind.PixelArt);
        // New picture with a pixel-art size preset (the dialog offers sprite sizes in this workspace).
        Key(w, Avalonia.Input.Key.N, RawInputModifiers.Control);
        var preset = w.DialogLayer.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "16 × 16"));
        Click(w, preset);
        Key(w, Avalonia.Input.Key.Enter);
        var doc = w.Session.Document;
        Assert.Equal((16, 16), (doc.Width, doc.Height));
        Assert.Null(doc.Paper); // sprites start transparent
        Assert.Equal(Math.Round(w.Canvas.ActualZoom), w.Canvas.ActualZoom, 6);

        w.Session.SetForeground(ColorRgb.Parse("#C0563B"));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (2.5, 2.5), (6.5, 2.5), (6.5, 6.5));
        Key(w, Avalonia.Input.Key.U); // pixel rectangle
        Assert.Equal(ToolKind.PixelRect, w.Session.Tool);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (9.5, 9.5), (13.5, 13.5));
        var layer = doc.ActiveLayer.Image;
        Assert.Equal(Pixel.Opaque(ColorRgb.Parse("#C0563B")), layer[9, 9]);
        Assert.Equal(Pixel.Transparent, layer[11, 11]); // outline only

        // Select the rectangle and move it two pixels left with the move tool.
        Key(w, Avalonia.Input.Key.M);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (9.1, 9.1), (14.9, 14.9));
        Key(w, Avalonia.Input.Key.V);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (11.5, 11.5), (9.6, 11.5));
        Assert.Equal(Pixel.Opaque(ColorRgb.Parse("#C0563B")), layer[7, 9]);
        Assert.Equal(Pixel.Transparent, layer[12, 9]);

        // Everything stays hard-edged: every pixel is fully opaque or fully transparent.
        Assert.All(layer.Pixels, p => Assert.True(Pixel.A(p) is 0 or 255));
        var path = Path.Combine(_dir, "sprite.png");
        w.ExportToPath(path, ExportFormat.Png);
        Assert.Equal(doc.Flatten().Pixels, ImageFiles.Decode(path).Pixels);

        Key(w, Avalonia.Input.Key.OemQuotes, RawInputModifiers.Control);
        Assert.False(w.WsState.ShowGrid);
        w.Close();
    }
}
