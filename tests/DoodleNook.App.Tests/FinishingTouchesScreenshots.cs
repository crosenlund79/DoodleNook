using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 010 screenshots (out/screenshots/m010-*.png) at the Kamvas 13 (150 %) and a 1080p desktop.</summary>
public class FinishingTouchesScreenshots
{
    internal static readonly (string Name, double Width, double Height, double Scaling)[] Sizes =
        [("kamvas13-150", 1280, 720, 1.5), ("desktop1080", 1920, 1080, 1.0)];

    /// <summary>Closes a screenshot window for real (nothing pending, nothing unsaved), so it stops drawing.</summary>
    internal static void CloseNow(MainWindow w)
    {
        w.CancelTransform();
        w.CancelLiquify();
        w.Session.Document.MarkSaved();
        w.Close();
        Pump();
    }

    /// <summary>A small painted scene to work on: sky, a hill, a sun and a few strokes.</summary>
    internal static void PaintScene(MainWindow w)
    {
        var s = w.Session;
        var img = new RasterImage(1920, 1080);
        for (int y = 0; y < img.Height; y++)
        {
            uint px = y < img.Height * 0.55 ? Pixel.Opaque(new ColorRgb((byte)(0x8F - y / 20), (byte)(0xB8 - y / 30), 0xCC)) : Pixel.Opaque(ColorRgb.Parse("#7FA36B"));
            for (int x = 0; x < img.Width; x++) img.Pixels[y * img.Width + x] = px;
        }
        s.ReplaceDocument(Document.FromImage(img, "Scene"), null);
        w.Canvas.FitToView();
        Pump();
        s.UseBrush(new BrushSettings { Size = 120, Hardness = 0.9, Opacity = 1 }, "pencil");
        s.SetForeground(ColorRgb.Parse("#F2C14E"));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (1400, 260), (1402, 262));
        s.UseBrush(new BrushSettings { Size = 46, Hardness = 0.7, Opacity = 1 }, "pencil");
        s.SetForeground(ColorRgb.Parse("#5B3A6B"));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 720), (600, 640), (900, 700), (1200, 620));
        s.SetForeground(ColorRgb.Parse("#C96F4A"));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 820), (700, 760), (1100, 840), (1500, 780));
        s.ApplyPreset("pencil");
        Pump();
    }

    [AvaloniaFact]
    public void Capture_quick_palette()
    {
        foreach (var (name, width, height, scaling) in Sizes)
        {
            var w = Open(width, height, WorkspaceKind.PureSketch);
            w.SetRenderScaling(scaling);
            Pump();
            PaintScene(w);
            var area = w.Canvas.Bounds;
            w.Radial.Open(area.Width * 0.45, area.Height * 0.45);
            Pump();
            WorkshopScreenshots.Save(w, $"m010-quick-palette-{name}");

            // Sizing: the drag in progress, the real size drawn around the centre.
            var centre = Find<Border>(w, "RadialSize");
            var start = centre.TranslatePoint(new Point(centre.Bounds.Width / 2, centre.Bounds.Height / 2), w)!.Value;
            w.MouseMove(start);
            w.MouseDown(start, MouseButton.Left);
            w.MouseMove(new Point(start.X + 160, start.Y - 30), RawInputModifiers.LeftMouseButton);
            Pump();
            WorkshopScreenshots.Save(w, $"m010-quick-palette-size-{name}");
            w.MouseUp(new Point(start.X + 160, start.Y - 30), MouseButton.Left);
            Pump();

            // Choosing a brush in the wheel.
            w.CaptureRenderedFrame()?.Dispose();
            Click(w, Find<Button>(w, "RadialBrush"));
            Pump();
            WorkshopScreenshots.Save(w, $"m010-quick-palette-brushes-{name}");
            CloseNow(w);
        }
    }

    [AvaloniaFact]
    public async Task Capture_liquify_and_retouch()
    {
        foreach (var (name, width, height, scaling) in Sizes)
        {
            var w = Open(width, height, WorkspaceKind.Painting);
            w.SetRenderScaling(scaling);
            Pump();
            PaintScene(w);
            WorkshopScreenshots.Save(w, $"m010-painting-default-{name}");
            Assert.True(await w.StartLiquifyAsync());
            w.Session.SetLiquifyOptions(w.Session.Liquify with { Mode = Engine.Operations.LiquifyMode.Push, Size = 260, Strength = 1 });
            Drag(w, MouseButton.Left, RawInputModifiers.None, (700, 700), (740, 640), (780, 580), (820, 520));
            var hover = DocToWindow(w, 900, 640);
            w.MouseMove(hover);
            Pump();
            WorkshopScreenshots.Save(w, $"m010-liquify-{name}");
            w.ApplyLiquify();

            // Smudge across the purple and orange strokes.
            w.Session.SelectTool(Editor.ToolKind.Smudge);
            w.Session.SetRetouchOptions(w.Session.Retouch with { SmudgeSize = 60, SmudgeStrength = 0.85 });
            Drag(w, MouseButton.Left, RawInputModifiers.None, (420, 690), (440, 740), (460, 790), (480, 840), (500, 880));
            Drag(w, MouseButton.Left, RawInputModifiers.None, (1000, 680), (1030, 730), (1060, 780), (1090, 830));
            w.MouseMove(DocToWindow(w, 1150, 760));
            Pump();
            WorkshopScreenshots.Save(w, $"m010-smudge-{name}");

            // Clone the sun into the sky, showing the source marker.
            w.Session.SelectTool(Editor.ToolKind.Clone);
            w.Session.SetRetouchOptions(w.Session.Retouch with { CloneSize = 140, CloneSoftness = 0.3 });
            w.Session.SetCloneSource(1400, 260);
            Drag(w, MouseButton.Left, RawInputModifiers.None, (500, 230), (520, 240), (540, 260));
            w.MouseMove(DocToWindow(w, 560, 280));
            Pump();
            WorkshopScreenshots.Save(w, $"m010-clone-{name}");
            CloseNow(w);
        }
    }

    [AvaloniaFact]
    public void Capture_perspective_and_warp()
    {
        foreach (var (name, width, height, scaling) in Sizes)
        {
            var w = Open(width, height, WorkspaceKind.Painting);
            w.SetRenderScaling(scaling);
            Pump();
            PaintScene(w);
            // A little painted sign to transform.
            var s = w.Session;
            s.SetForeground(ColorRgb.Parse("#F6E7C8"));
            s.UseBrush(new BrushSettings { Size = 30, Hardness = 1, Opacity = 1 }, "pencil");
            for (int y = 300; y <= 480; y += 20) Drag(w, MouseButton.Left, RawInputModifiers.None, (420, y), (900, y));
            s.SetForeground(ColorRgb.Parse("#5B3A6B"));
            s.UseBrush(new BrushSettings { Size = 14, Hardness = 1, Opacity = 1 }, "pencil");
            Drag(w, MouseButton.Left, RawInputModifiers.None, (470, 340), (470, 440), (540, 440));
            Drag(w, MouseButton.Left, RawInputModifiers.None, (600, 340), (600, 440));
            Drag(w, MouseButton.Left, RawInputModifiers.None, (660, 440), (700, 340), (740, 440));
            Drag(w, MouseButton.Left, RawInputModifiers.None, (800, 340), (800, 440), (860, 440));
            s.Document.SetSelection(SelectionMask.Rectangle(s.Document.Width, s.Document.Height, new DoodleNook.Engine.Imaging.PixelRect(400, 280, 520, 220)), "Select");
            Pump();

            w.TransformIn(Engine.Operations.TransformMode.Perspective);
            s.ActiveTransform!.SetCorners([(470, 250), (860, 300), (900, 520), (420, 470)]);
            w.Canvas.Input!.RefreshTransformFrame();
            Pump();
            WorkshopScreenshots.Save(w, $"m010-perspective-{name}");
            w.CancelTransform();

            w.TransformIn(Engine.Operations.TransformMode.Warp);
            var g = s.ActiveTransform!.Warp!;
            var bent = g.With(5, g.Points[5].X, g.Points[5].Y - 40).With(6, g.Points[6].X, g.Points[6].Y - 40).With(9, g.Points[9].X, g.Points[9].Y + 35)
                .With(10, g.Points[10].X, g.Points[10].Y + 35).With(1, g.Points[1].X, g.Points[1].Y - 45).With(2, g.Points[2].X, g.Points[2].Y - 45)
                .With(13, g.Points[13].X, g.Points[13].Y + 40).With(14, g.Points[14].X, g.Points[14].Y + 40);
            s.ActiveTransform.SetWarp(bent);
            w.Canvas.Input.RefreshTransformFrame();
            Pump();
            WorkshopScreenshots.Save(w, $"m010-warp-{name}");
            CloseNow(w);
        }
    }
}
