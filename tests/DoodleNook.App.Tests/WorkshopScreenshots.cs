using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;

namespace DoodleNook.App.Tests;

/// <summary>Mission 007 screenshots (tests/DoodleNook.App.Tests/out/screenshots/workshop-*.png).</summary>
public class WorkshopScreenshots
{
    internal static void Save(TopLevel w, string name)
    {
        WindowTests.Pump();
        using var frame = w.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(Screenshots.OutDir, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Hide()
    {
        UiKit.HideOpenFlyouts();
        WindowTests.Pump();
    }

    internal static MainWindow Painting(double width, double height, double scaling)
    {
        var w = WindowTests.Open(width, height, WorkspaceKind.Painting);
        w.SetRenderScaling(scaling);
        WindowTests.Pump();
        w.Canvas.FitToView(enlarge: false);
        Screenshots.DrawLayeredScene(w);
        w.Session.Document.MarkSaved();
        w.Session.SelectTool(ToolKind.Brush);
        WindowTests.Pump();
        return w;
    }

    private static Engine.Imaging.RasterImage Frame(Engine.Documents.Document d, int f) => d.RenderFrame(f);

    [AvaloniaFact]
    public void Capture_workshop_screenshots()
    {
        // The default Painting workspace after Mission 007, at three sizes: nothing new is in the way.
        foreach (var (name, width, height, scaling) in new[] { ("desktop27", 2560.0, 1440.0, 1.0), ("desktop1080", 1920.0, 1080.0, 1.0), ("kamvas13-150", 1280.0, 720.0, 1.5) })
        {
            var d = Painting(width, height, scaling);
            d.MouseMove(WindowTests.DocToWindow(d, 1000, 760));
            Save(d, $"workshop-{name}-painting");
            d.Close();
        }

        var k = Painting(1280, 720, 1.5);
        // Brush picker and the Brush Laboratory (three sections).
        WindowTests.Click(k, WindowTests.Find<Button>(k, "OptionsPresetChip"));
        Save(k, "workshop-brush-picker");
        Hide();
        _ = k.OpenBrushLabAsync("charcoal");
        WindowTests.Pump();
        k.LastBrushLab!.Playground.ExampleStroke();
        Save(k, "workshop-brush-lab");
        k.Dialogs.Close(null);
        WindowTests.Pump();
        _ = k.OpenBrushLabAsync("watercolor", BrushLab.Section.Texture);
        WindowTests.Pump();
        k.LastBrushLab!.Playground.ExampleStroke();
        Save(k, "workshop-brush-lab-texture");
        k.Dialogs.Close(null);
        WindowTests.Pump();
        _ = k.OpenBrushLabAsync("grass", BrushLab.Section.Scatter);
        WindowTests.Pump();
        k.LastBrushLab!.Playground.Background = Controls.BrushPlayground.Backdrop.Paper;
        k.LastBrushLab!.Playground.ExampleStroke();
        Save(k, "workshop-brush-lab-scatter");
        k.Dialogs.Close(null);
        WindowTests.Pump();

        // Stamps: the picker, then stamps and the grass brush on the picture with the preview under the pointer.
        k.Session.SelectTool(ToolKind.Stamp);
        WindowTests.Pump();
        WindowTests.Click(k, WindowTests.Find<Button>(k, "StampChip"));
        Save(k, "workshop-stamp-picker");
        Hide();
        k.Session.ApplyPreset("grass");
        k.Session.SelectTool(ToolKind.Brush);
        WindowTests.Drag(k, MouseButton.Left, RawInputModifiers.None, (150, 980), (500, 990), (900, 975));
        k.Session.SelectTool(ToolKind.Stamp);
        foreach (var (id, x, y, sc) in new[] { ("builtin:flower", 600.0, 900.0, 0.8), ("builtin:mushroom", 1200.0, 930.0, 0.9), ("builtin:pebble", 1500.0, 960.0, 0.8), ("builtin:cloud", 700.0, 220.0, 1.6) })
        {
            k.Session.SetStampOptions(k.Session.StampOptions with { StampId = id, Scale = sc });
            k.Session.PlaceStamp(x, y);
        }
        k.Session.SetStampOptions(k.Session.StampOptions with { StampId = "builtin:flower", Scale = 0.9, Rotation = 15 });
        k.MouseMove(WindowTests.DocToWindow(k, 900, 880));
        Save(k, "workshop-stamp-preview");

        // Patterns: the bucket's pattern mode with the tiled preview, then a filled area.
        k.Session.SelectTool(ToolKind.Fill);
        k.Session.SetPatternOptions(k.Session.PatternOptions with { UsePattern = true, PatternId = "builtin:brick", Scale = 1.5 });
        WindowTests.Pump();
        WindowTests.Click(k, WindowTests.Find<Button>(k, "PatternChip"));
        Save(k, "workshop-pattern-picker");
        Hide();
        k.Session.AddLayer();
        k.Session.ApplySelection(Engine.Operations.Selections.Rectangle(k.Session.Document, 1350, 520, 1800, 760), "Select");
        k.Session.FillSelectionWithPattern();
        k.Session.Deselect();
        WindowTests.Pump();
        Save(k, "workshop-pattern-fill");

        // Palettes: the manager proposing colours from the picture.
        _ = k.OpenPalettesAsync("builtin:meadow");
        WindowTests.Pump();
        k.LastPaletteManager!.ExtractFrom(k.Session.Document.Flatten(), 10);
        Save(k, "workshop-palette-manager");
        k.Dialogs.Close(null);
        WindowTests.Pump();

        // Reference board with three pictures.
        k.Session.SelectTool(ToolKind.Brush);
        k.SetReferenceBoardVisible(true);
        var ball = Engine.Samples.SampleAnimations.BouncingBall();
        var slime = Engine.Samples.SampleAnimations.PixelSlime();
        k.ReferenceBoard.Add(Frame(ball, 6), "Ball");
        k.ReferenceBoard.Add(Engine.Operations.Resampler.Resize(Frame(slime, 3), 256, 256, Engine.Operations.Interpolation.Nearest), "Slime");
        k.ReferenceBoard.Add(k.Session.Library.PatternImage("builtin:planks")!, "Planks");
        k.ReferenceBoard.Board.Background = Engine.Imaging.ColorRgb.Parse("#77706A");
        WindowTests.Find<ReferenceBoardView>(k, "ReferenceBoardView").FitAll();
        k.MouseMove(WindowTests.DocToWindow(k, 1000, 700));
        Save(k, "workshop-reference-board");
        k.SetReferenceBoardVisible(false);
        k.Close();

        // Pixel Art with a saved palette and a pixel stamp.
        var p = WindowTests.Open(1280, 720, WorkspaceKind.PixelArt);
        p.SetRenderScaling(1.5);
        WindowTests.Pump();
        p.Session.ReplaceDocument(new Engine.Documents.Document(48, 32, null, "Sprite"), null);
        p.UsePalette("builtin:meadow");
        p.Session.SetPatternOptions(p.Session.PatternOptions with { PatternId = "builtin:grassland" });
        p.Session.ApplySelection(Engine.Operations.Selections.Rectangle(p.Session.Document, 0, 24, 48, 32), "Select");
        p.Session.FillSelectionWithPattern();
        p.Session.Deselect();
        p.Canvas.FitToView();
        // A tiny pixel mushroom saved as a stamp (as "Save selection as stamp" would), previewed at 1× over the sprite.
        var mushroom = PixelSprite(
            "..RRRR..",
            ".RWRRWR.",
            "RRRRRRRR",
            "RWRRRRWR",
            "..CCCC..",
            "..CCCC..",
            "..CCCC..");
        var stamp = p.Session.Library.AddStamp("Pixel mushroom", "My stamps", mushroom);
        p.Session.SetStampOptions(p.Session.StampOptions with { StampId = stamp.Id, Scale = 1, Rotation = 0 });
        p.Session.SelectTool(ToolKind.Stamp);
        WindowTests.Pump();
        p.Session.PlaceStamp(10, 20);
        p.MouseMove(WindowTests.DocToWindow(p, 30, 19));
        Save(p, "workshop-pixel-art");
        p.Close();
    }

    private static Engine.Imaging.RasterImage PixelSprite(params string[] rows)
    {
        var colours = new Dictionary<char, (byte R, byte G, byte B)> { ['R'] = (0xC8, 0x4A, 0x3E), ['W'] = (0xF6, 0xEE, 0xE0), ['C'] = (0xE8, 0xD9, 0xBF) };
        int w = rows[0].Length, h = rows.Length;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (colours.TryGetValue(rows[y][x], out var c))
                {
                    int i = (y * w + x) * 4;
                    (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (c.R, c.G, c.B, 255);
                }
        return Engine.Imaging.RasterImage.FromStraightRgba(w, h, rgba);
    }

    [AvaloniaFact]
    public void Capture_brush_lab_at_1080p_and_on_the_27_inch()
    {
        foreach (var (name, width, height) in new[] { ("desktop1080", 1920.0, 1080.0), ("desktop27", 2560.0, 1440.0) })
        {
            var w = Painting(width, height, 1);
            _ = w.OpenBrushLabAsync("dryink");
            WindowTests.Pump();
            w.LastBrushLab!.Playground.ExampleStroke();
            Save(w, $"workshop-{name}-brush-lab");
            w.Dialogs.Close(null);
            WindowTests.Pump();
            w.Close();
        }
    }
}
