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

    [AvaloniaFact]
    public void Capture_workshop_screenshots()
    {
        var k = Painting(1280, 720, 1.5);
        WindowTests.Click(k, WindowTests.Find<Button>(k, "OptionsPresetChip"));
        Save(k, "workshop-brush-picker");
        Hide();
        _ = k.OpenBrushLabAsync("pencil");
        WindowTests.Pump();
        k.LastBrushLab!.Playground.ExampleStroke();
        Save(k, "workshop-brush-lab");
        k.Dialogs.Close(null);
        WindowTests.Pump();
        k.Close();
    }
}
