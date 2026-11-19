using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DoodleNook.App.Editor;
using DoodleNook.App.Input;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Imaging;
using SkiaSharp;

namespace DoodleNook.App.Tests;

/// <summary>
/// Captures real frames of the native app (headless Skia rendering, same code and styles as on Windows; only the
/// text rasteriser differs) for the documentation. Files go to tests/DoodleNook.App.Tests/out/screenshots.
/// </summary>
public class Screenshots
{
    public static string OutDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
            var root = dir?.FullName ?? Path.GetTempPath();
            var path = Path.Combine(root, "tests", "DoodleNook.App.Tests", "out", "screenshots");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static (byte R, byte G, byte B, byte A) ReadPixel(WriteableBitmap bmp, int x, int y)
    {
        using var fb = bmp.Lock();
        unsafe
        {
            byte* p = (byte*)fb.Address + y * fb.RowBytes + x * 4;
            return fb.Format == PixelFormat.Rgba8888 ? (p[0], p[1], p[2], p[3]) : (p[2], p[1], p[0], p[3]);
        }
    }

    private static void Save(TopLevel w, string name)
    {
        WindowTests.Pump();
        using var frame = w.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(OutDir, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Click(Window w, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseMove(p);
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        WindowTests.Pump();
    }

    /// <summary>Draws a small scene through the real input path, as a pen would (with pressure).</summary>
    internal static void DrawDoodle(MainWindow w)
    {
        var s = w.Session;
        var input = w.Canvas.Input!;
        var view = w.Canvas.View;

        void Pen(Func<double, (double X, double Y)> path, int n, Func<double, float>? pressure = null)
        {
            pressure ??= t => (float)(0.2 + 0.8 * Math.Sin(Math.PI * Math.Clamp(t * 1.1, 0, 1)));
            PointerInput At(double t, bool down = true)
            {
                var (x, y) = path(t);
                var (vx, vy) = view.DocumentToView(x, y);
                return new PointerInput(9, PointerKind.Pen, vx, vy, pressure(t), down);
            }
            input.Press(At(0));
            for (int i = 1; i <= n; i += 2) input.Move([At((i - 1) / (double)n), At(i / (double)n)]);
            input.Release(At(1, false) with { Pressure = 0 });
        }

        // Hills: flat wash.
        s.ApplyPreset("wash"); s.SetSize(150); s.SetForeground(ColorRgb.Parse("#9CC07A"));
        Pen(t => (-60 + 2040 * t, 900 - 90 * Math.Sin(t * Math.PI * 1.3)), 120, _ => 1f);
        s.SetSize(260);
        s.SetForeground(ColorRgb.Parse("#7AA66A"));
        Pen(t => (-60 + 2040 * t, 1040 - 50 * Math.Sin(0.6 + t * Math.PI * 1.7)), 120, _ => 1f);

        // Sun: soft round glow, then a marker disc and ink rays.
        s.ApplyPreset("soft"); s.SetSize(300); s.SetForeground(ColorRgb.Parse("#F2C14E"));
        Pen(t => (1520 + 30 * Math.Cos(t * 6.28), 240 + 30 * Math.Sin(t * 6.28)), 40, _ => 0.9f);
        s.ApplyPreset("marker"); s.SetSize(90); s.SetOpacity(0.9); s.SetForeground(ColorRgb.Parse("#E9A94D"));
        Pen(t => (1520 + 20 * Math.Cos(t * 6.28), 240 + 20 * Math.Sin(t * 6.28)), 40, _ => 1f);
        s.ApplyPreset("ink"); s.SetSize(10); s.SetForeground(ColorRgb.Parse("#C96F4A"));
        for (int k = 0; k < 10; k++)
        {
            double a = k * Math.PI / 5 + 0.2;
            Pen(t => (1520 + Math.Cos(a) * (95 + 70 * t), 240 + Math.Sin(a) * (95 + 70 * t)), 16);
        }

        // A little house in ink, sketchy pencil hatching on the roof.
        s.ApplyPreset("ink"); s.SetSize(9); s.SetForeground(ColorRgb.Parse("#3B2F2A"));
        Pen(t => (520, 820 - 300 * t), 30);
        Pen(t => (520 + 340 * t, 520 + 2 * Math.Sin(t * 9)), 40);
        Pen(t => (860, 520 + 300 * t), 30);
        Pen(t => (480 + 210 * t, 545 - 185 * t), 30);
        Pen(t => (690 + 210 * t, 360 + 185 * t), 30);
        Pen(t => (640, 820 - 140 * t), 20);
        Pen(t => (640 + 90 * t, 680), 16);
        Pen(t => (730, 680 + 140 * t), 20);
        s.ApplyPreset("pencil"); s.SetSize(5); s.SetForeground(ColorRgb.Parse("#5F8FA8"));
        for (int k = 0; k < 9; k++)
        {
            double x0 = 560 + k * 30;
            Pen(t => (x0 + 40 * t, 505 - 110 * t + 0.0 * k), 14);
        }
        s.SetForeground(ColorRgb.Parse("#E88A9A"));
        Pen(t => (585 + 30 * Math.Cos(t * 6.28), 600 + 30 * Math.Sin(t * 6.28)), 40, _ => 0.7f); // round window

        // A tree: brown trunk and a soft crown.
        s.ApplyPreset("ink"); s.SetSize(22); s.SetForeground(ColorRgb.Parse("#6B4231"));
        Pen(t => (1150 + 6 * Math.Sin(t * 4), 860 - 260 * t), 30, t => (float)(1 - 0.5 * t));
        s.ApplyPreset("soft"); s.SetSize(190); s.SetForeground(ColorRgb.Parse("#5F8F5A"));
        Pen(t => (1150 + 70 * (1 - t) * Math.Cos(t * 18), 520 + 50 * (1 - t) * Math.Sin(t * 18)), 90, _ => 0.85f);

        // A semi-transparent marker swirl that crosses itself without getting darker.
        s.ApplyPreset("marker"); s.SetSize(34); s.SetOpacity(0.5); s.SetForeground(ColorRgb.Parse("#8A6FB0"));
        Pen(t => (260 + 160 * Math.Cos(t * 9) + 150 * t, 230 + 90 * Math.Sin(t * 9)), 160, _ => 1f);

        // Pencil grass with pressure tapers.
        s.ApplyPreset("pencil"); s.SetSize(8); s.SetForeground(ColorRgb.Parse("#3B2F2A"));
        for (int k = 0; k < 14; k++)
        {
            double x0 = 1300 + k * 34;
            Pen(t => (x0 + 18 * t, 960 - (60 + 25 * Math.Sin(k)) * t), 14, t => (float)(1 - t));
        }
        s.ApplyPreset("pencil");
        s.SetForeground(ColorRgb.Parse("#3B2F2A"));
    }

    /// <summary>The documentation scene as a layered picture: colour washes, ink lines and a pencil sketch on separate layers.</summary>
    internal static void DrawLayeredScene(MainWindow w)
    {
        var s = w.Session;
        var doc = s.Document;
        doc.SetLayerProperties(doc.ActiveLayer, doc.ActiveLayer.Properties with { Name = "Colour" });
        DrawDoodle(w);
        var lines = s.AddLayer();
        doc.SetLayerProperties(lines, lines.Properties with { Name = "Doodles" });
        s.ApplyPreset("ink"); s.SetSize(7); s.SetForeground(ColorRgb.Parse("#3B2F2A"));
        var input = w.Canvas.Input!;
        void Line(double x0, double y0, double x1, double y1)
        {
            PointerInput At(double t, bool down = true)
            {
                var (vx, vy) = w.Canvas.View.DocumentToView(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
                return new PointerInput(9, PointerKind.Pen, vx, vy, (float)(0.3 + 0.7 * Math.Sin(Math.PI * t)), down);
            }
            input.Press(At(0));
            for (int i = 1; i <= 20; i++) input.Move([At(i / 20.0)]);
            input.Release(At(1, false) with { Pressure = 0 });
        }
        for (int k = 0; k < 5; k++) Line(300 + k * 40, 700, 330 + k * 40, 640); // a little fence
        Line(280, 690, 520, 690);
        s.ApplyPreset("pencil");
    }

    /// <summary>A 24 × 24 mug sprite drawn with the pixel tools (as in the Mission 003 Pixel Art mock-up).</summary>
    internal static void DrawSprite(MainWindow w)
    {
        var s = w.Session;
        s.ReplaceDocument(new Engine.Documents.Document(24, 24, null, "Sprite"), null);
        void Px(string hex, params (int X0, int Y0, int X1, int Y1)[] rects)
        {
            s.SetForeground(ColorRgb.Parse(hex));
            foreach (var (x0, y0, x1, y1) in rects)
                for (int y = y0; y <= y1; y++)
                {
                    s.BeginPixelStroke(x0, y, erase: false);
                    s.ContinuePixelStroke(x1, y);
                    s.EndStroke();
                }
        }
        var before = s.Pixel;
        s.SetPixelOptions(before with { PixelPerfect = false, Size = 1 });
        Px("#2E2A3B", (3, 8, 14, 8), (3, 9, 3, 19), (14, 9, 14, 19), (4, 19, 13, 19), (15, 11, 17, 11), (17, 12, 17, 16), (15, 16, 16, 16));
        Px("#6B4231", (4, 9, 13, 9));
        Px("#5F8FA8", (4, 10, 13, 18));
        Px("#8FB8CC", (4, 10, 4, 18));
        Px("#3F6A80", (12, 10, 13, 18), (5, 18, 11, 18));
        Px("#E88A9A", (7, 12, 7, 12), (9, 12, 9, 12), (7, 13, 9, 13), (8, 14, 8, 14));
        Px("#4A3F5C", (2, 20, 15, 20));
        Px("#7A6E8E", (6, 3, 6, 4), (5, 5, 5, 5), (6, 6, 6, 6), (10, 3, 10, 4), (9, 5, 9, 5), (10, 6, 10, 6));
        s.SetPixelOptions(before);
        s.Document.MarkSaved();
    }

    private static void HideFlyouts(MainWindow w)
    {
        UiKit.HideOpenFlyouts();
        WindowTests.Pump();
    }

    /// <summary>Stacks the Mission 003 mock-up (top) and the native app (bottom) into one comparison image.</summary>
    private static void Compare(string mockup, string native, string name)
    {
        var mockPath = Path.Combine(DocsRoot, "docs", "design", "003-workspace-playground", "screenshots", mockup);
        var nativePath = Path.Combine(OutDir, native);
        if (!File.Exists(mockPath) || !File.Exists(nativePath)) return;
        using var a = SKBitmap.Decode(mockPath);
        using var b = SKBitmap.Decode(nativePath);
        const int width = 1600;
        int ha = (int)Math.Round(a.Height * (double)width / a.Width), hb = (int)Math.Round(b.Height * (double)width / b.Width);
        using var surface = SKSurface.Create(new SKImageInfo(width, ha + hb + 12));
        var c = surface.Canvas;
        c.Clear(new SKColor(0x18, 0x15, 0x13));
        using var paint = new SKPaint { IsAntialias = true };
        var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
        using (var ia = SKImage.FromBitmap(a)) c.DrawImage(ia, new SKRect(0, 0, width, ha), sampling, paint);
        using (var ib = SKImage.FromBitmap(b)) c.DrawImage(ib, new SKRect(0, ha + 12, width, ha + 12 + hb), sampling, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), data.ToArray());
    }

    private static string DocsRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? ".";
        }
    }

    private static MainWindow SceneWindow(double width, double height, double scaling, WorkspaceKind ws)
    {
        var w = WindowTests.Open(width, height, ws);
        w.SetRenderScaling(scaling);
        WindowTests.Pump();
        w.Canvas.FitToView(enlarge: false);
        DrawLayeredScene(w);
        w.Session.Document.MarkSaved();
        w.Session.SelectTool(w.Workspace.DefaultTool);
        return w;
    }

    [AvaloniaFact]
    public void Capture_documentation_screenshots()
    {
        foreach (var (name, width, height, scaling) in new[] { ("desktop27", 2560.0, 1440.0, 1.0), ("kamvas13-150", 1280.0, 720.0, 1.5) })
        {
            // A — Pure Sketch, with the quick palette open at the pen.
            var a = SceneWindow(width, height, scaling, WorkspaceKind.PureSketch);
            a.MouseMove(WindowTests.DocToWindow(a, 1000, 700));
            Save(a, $"{name}-A-pure-sketch");
            a.Radial.Open(a.Canvas.Bounds.Width * 0.55, a.Canvas.Bounds.Height * 0.5);
            Save(a, $"{name}-A-quick-palette");
            a.Close();

            // B — Quick Sketch (and its layers pop-over).
            var b = SceneWindow(width, height, scaling, WorkspaceKind.QuickSketch);
            b.MouseMove(WindowTests.DocToWindow(b, 1000, 760));
            Save(b, $"{name}-B-quick-sketch");
            Click(b, WindowTests.Find<Button>(b, "LayersChip"));
            Save(b, $"{name}-B-layers-popover");
            HideFlyouts(b);
            b.Close();

            // C — Painting.
            var c = SceneWindow(width, height, scaling, WorkspaceKind.Painting);
            c.MouseMove(WindowTests.DocToWindow(c, 1000, 760));
            Save(c, $"{name}-C-painting");
            c.Close();

            // D — Editing, with a selection being transformed.
            var d = SceneWindow(width, height, scaling, WorkspaceKind.Editing);
            d.Session.Document.SetActiveLayer(d.Session.Document.Layers[0]);
            d.Session.ApplySelection(Engine.Operations.Selections.Rectangle(d.Session.Document, 1300, 120, 1760, 380), "Rectangle select");
            d.Session.BeginTransform();
            d.Session.ActiveTransform!.Rotate(-12);
            WindowTests.Pump();
            Save(d, $"{name}-D-editing");
            d.Session.CancelTransform();
            d.Close();

            // E — Pixel Art.
            var e = WindowTests.Open(width, height, WorkspaceKind.PixelArt);
            e.SetRenderScaling(scaling);
            WindowTests.Pump();
            DrawSprite(e);
            WindowTests.Pump();
            e.Canvas.FitToView();
            e.MouseMove(WindowTests.DocToWindow(e, 18.5, 6.5));
            Save(e, $"{name}-E-pixel-art");
            e.Close();
        }

        foreach (var ws in new[] { "A-pure-sketch", "B-quick-sketch", "C-painting", "D-editing", "E-pixel-art" })
        {
            Compare($"desktop27-{ws}.png", $"desktop27-{ws}.png", $"compare-desktop27-{ws}");
            Compare($"kamvas13-150-{ws}.png", $"kamvas13-150-{ws}.png", $"compare-kamvas13-150-{ws}");
        }
        Compare("experiment-A-quick-palette-at-pen.png", "kamvas13-150-A-quick-palette.png", "compare-quick-palette");

        // Dialogs and pop-overs at Kamvas 13 / 150 %.
        var p = SceneWindow(1280, 720, 1.5, WorkspaceKind.Painting);
        Click(p, WindowTests.Find<Button>(p, "OptionsPresetChip"));
        Save(p, "popover-brushes");
        HideFlyouts(p);
        Click(p, WindowTests.Find<Button>(p, "MenuFile"));
        Save(p, "menu-file");
        HideFlyouts(p);
        _ = p.ImageSizeAsync();
        Save(p, "dialog-image-size");
        p.Dialogs.Close(null);
        WindowTests.Pump();
        _ = p.AdjustAsync(Dialogs.AdjustKind.HueSaturation);
        Save(p, "dialog-hue-saturation");
        p.Dialogs.Close(null);
        WindowTests.Pump();
        _ = p.ExportAsync(Files.ExportFormat.Jpeg);
        Save(p, "dialog-export-jpeg");
        p.Dialogs.Close(null);
        WindowTests.Pump();
        p.ToggleDiagnostics();
        Save(p, "pen-diagnostics");
        p.Close();

        Assert.True(File.Exists(Path.Combine(OutDir, "desktop27-C-painting.png")));
    }
}
