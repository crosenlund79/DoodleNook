using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.App.Tutorials;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 014 screenshots of "Your First Animation" running in the real window
/// (out/screenshots/m014-*.png), at a 1440 × 900 desktop and the Kamvas 13 at 150 %.</summary>
public sealed class TutorialScreenshots
{
    private static void Save(MainWindow w, string name)
    {
        Pump();
        w.CaptureRenderedFrame()?.Dispose();
        Pump();
        using var frame = w.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(Screenshots.OutDir, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Ball(MainWindow w, double cx, double cy, double rx = 26, double ry = 26)
    {
        var points = Enumerable.Range(0, 30).Select(i => (cx + rx * Math.Cos(i * Math.PI / 14), cy + ry * Math.Sin(i * Math.PI / 14))).ToArray();
        Drag(w, MouseButton.Left, RawInputModifiers.None, points);
    }

    [AvaloniaFact]
    public async Task Capture_the_first_animation_tutorial()
    {
        foreach (var (size, width, height, scaling) in new[] { ("desktop", 1440.0, 900.0, 1.0), ("kamvas13-150", 1280.0, 720.0, 1.5) })
        {
            var w = Open(width, height, WorkspaceKind.Painting);
            w.SetRenderScaling(scaling);
            w.Session.Document.MarkSaved();
            Pump();
            var picker = w.ShowGuidedTutorialsAsync();
            await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
            Save(w, $"m014-picker-{size}");
            w.CaptureRenderedFrame()?.Dispose();
            Click(w, Find<Button>(w, "StartTutorial_first-animation"));
            await picker;
            Save(w, $"m014-welcome-{size}");
            w.Tutorials.Next();
            w.CaptureRenderedFrame()?.Dispose();
            Click(w, Find<Button>(w, "TutorialAction"));
            await ArtistsCompanionWindowTests.Until(() => w.Tutorials.Index == 1);
            w.ShowToast("");
            Save(w, $"m014-draw-first-ball-{size}");

            w.Session.SelectTool(ToolKind.Brush);
            w.Session.SetSize(10);
            w.Session.SetForeground(Engine.Imaging.ColorRgb.Parse("#C96F4A"));
            Ball(w, 128, 52);
            await ArtistsCompanionWindowTests.Until(() => w.Tutorials.Index == 2);
            w.ShowToast("");
            Save(w, $"m014-add-frame-{size}");

            w.Tutorials.Next(); // onion skin is on by default
            Pump();
            w.Tutorials.Next();
            double[] ys = [100, 160, 214];
            foreach (var y in ys)
            {
                w.Session.NewFrame(duplicate: false);
                Pump();
                Ball(w, 128, y, y > 200 ? 32 : 26, y > 200 ? 18 : 26);
            }
            w.ShowToast("");
            Save(w, $"m014-bounce-{size}");
            while (w.Tutorials.Index < w.Tutorials.Tutorial!.Steps.Count) w.Tutorials.Next();
            Save(w, $"m014-finished-{size}");
            w.Tutorials.Exit();
            w.Session.Document.MarkSaved();
            w.Close();
            Pump();
        }
    }
}
