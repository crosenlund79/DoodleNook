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
            w.Close();
        }
    }
}
