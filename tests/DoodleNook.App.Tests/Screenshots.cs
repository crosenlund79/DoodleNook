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
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Tests;

/// <summary>
/// Captures real frames of the native app (headless Skia rendering, same code and styles as on Windows; only the
/// text rasteriser differs) for the Mission 004 documentation. Files go to tests/DoodleNook.App.Tests/out/screenshots.
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

    [AvaloniaFact]
    public void Capture_documentation_screenshots()
    {
        // Kamvas 13 at Windows 150 %: 1280 × 720 logical pixels (full screen, F11).
        var w = WindowTests.Open(1280, 720);
        w.SetRenderScaling(1.5);
        WindowTests.Pump();
        w.Canvas.FitToView(enlarge: false);
        DrawDoodle(w);
        // Show the brush cursor over the canvas.
        var hover = WindowTests.DocToWindow(w, 1000, 760);
        w.MouseMove(hover);
        Save(w, "01-quick-sketch-kamvas13-150");

        Click(w, w.PresetChip);
        Save(w, "02-brush-popover");
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        ((Flyout)w.PresetChip.Flyout!).Hide();

        Click(w, w.ColourChip);
        Save(w, "03-colour-popover");
        ((Flyout)w.ColourChip.Flyout!).Hide();

        Click(w, w.MenuButton);
        Save(w, "04-menu");
        ((Flyout)w.MenuButton.Flyout!).Hide();

        w.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
        // A pen-like stroke so the diagnostics have something to show.
        var input = w.Canvas.Input!;
        var (ax, ay) = w.Canvas.View.DocumentToView(1300, 300);
        input.Press(new PointerInput(5, PointerKind.Pen, ax, ay, 0.15f, true));
        for (int i = 1; i <= 60; i++)
        {
            var (x, y) = w.Canvas.View.DocumentToView(1300 + i * 6, 300 + 40 * Math.Sin(i / 9.0));
            input.Move([new PointerInput(5, PointerKind.Pen, x, y, (float)(0.15 + 0.8 * Math.Sin(Math.PI * i / 60.0)), true)]);
        }
        input.Release(new PointerInput(5, PointerKind.Pen, ax + 300, ay, 0f, false));
        w.Session.Undo();
        Save(w, "05-pen-diagnostics");
        w.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);

        w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        w.MouseMove(new Point(-10, -10));
        w.Canvas.Input!.Leave();
        Save(w, "06-canvas-only-tab");
        w.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);

        w.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e");
        w.MouseMove(WindowTests.DocToWindow(w, 700, 640));
        Save(w, "07-eraser");
        w.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
        w.Close();

        // A 27-inch 2560 × 1440 desktop monitor at 100 %.
        var d = WindowTests.Open(2560, 1440);
        d.Canvas.FitToView(enlarge: false);
        DrawDoodle(d);
        d.MouseMove(WindowTests.DocToWindow(d, 1000, 760));
        Save(d, "08-desktop-2560x1440");
        d.Close();

        // The New picture dialog and the unsaved-changes question.
        var n = WindowTests.Open(1280, 720);
        n.SetRenderScaling(1.5);
        _ = n.NewAsync();
        Save(n, "09-new-picture-dialog");
        n.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        n.Close();

        Assert.True(File.Exists(Path.Combine(OutDir, "01-quick-sketch-kamvas13-150.png")));
    }
}
