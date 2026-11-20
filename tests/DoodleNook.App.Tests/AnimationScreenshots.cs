using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using Avalonia.Controls.Primitives;

namespace DoodleNook.App.Tests;

/// <summary>Animation Studio screenshots for the documentation (tests/DoodleNook.App.Tests/out/screenshots/animation-*.png).</summary>
public class AnimationScreenshots
{
    private static void Save(TopLevel w, string name)
    {
        WindowTests.Pump();
        using var frame = w.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(Screenshots.OutDir, name + ".png"), new PngBitmapEncoderOptions());
    }

    internal static MainWindow Studio(double width, double height, double scaling, SampleAnimation sample)
    {
        var w = WindowTests.Open(width, height, WorkspaceKind.Animation);
        w.SetRenderScaling(scaling);
        WindowTests.Pump();
        w.OpenSampleAsync(sample).GetAwaiter().GetResult();
        WindowTests.Pump();
        return w;
    }

    private static void Hide()
    {
        UiKit.HideOpenFlyouts();
        WindowTests.Pump();
    }

    [AvaloniaFact]
    public void Capture_animation_studio_screenshots()
    {
        foreach (var (name, width, height, scaling) in new[] { ("desktop27", 2560.0, 1440.0, 1.0), ("kamvas13-150", 1280.0, 720.0, 1.5), ("kamvas13-125", 1536.0, 864.0, 1.25) })
        {
            var w = Studio(width, height, scaling, SampleAnimation.BouncingBall);
            w.Session.GoToFrame(2);
            w.MouseMove(WindowTests.DocToWindow(w, 470, 120));
            Save(w, $"{name}-F-animation-studio");
            w.Close();
        }

        // Kamvas 13 at 150 %: the timeline states, pop-overs and dialogs.
        var k = Studio(1280, 720, 1.5, SampleAnimation.BouncingBall);
        k.Session.GoToFrame(3);
        k.MouseMove(WindowTests.DocToWindow(k, 470, 120));
        k.Timeline!.SetCollapsed(true);
        Save(k, "animation-timeline-collapsed");
        k.Timeline.SetTimelineHeight(300);
        Save(k, "animation-timeline-expanded");
        k.Timeline.SetTimelineHeight(168);

        WindowTests.Click(k, WindowTests.Find<Button>(k, "OnionSettingsButton"));
        Save(k, "animation-onion-settings");
        Hide();
        WindowTests.Click(k, WindowTests.Find<Button>(k, "FpsChip"));
        Save(k, "animation-playback-settings");
        Hide();
        WindowTests.Click(k, WindowTests.Find<Button>(k, "MenuAnimation"));
        Save(k, "animation-menu");
        Hide();

        k.SetPreviewPanel(true);
        WindowTests.Pump();
        Save(k, "animation-preview-panel");
        k.SetPreviewPanel(false);

        _ = k.CustomizeWorkspaceAsync();
        Save(k, "animation-customize-workspace");
        k.Dialogs.Close("ok");
        WindowTests.Pump();
        _ = k.ExportAnimationAsync();
        Save(k, "animation-export-sprite-sheet");
        k.Dialogs.Close(null);
        WindowTests.Pump();
        _ = k.NewAnimationAsync();
        Save(k, "animation-new-animation");
        k.Dialogs.Close(null);
        WindowTests.Pump();
        k.Close();

        // The pixel-art sample with its onion skin.
        var p = Studio(1280, 720, 1.5, SampleAnimation.PixelSlime);
        p.Session.GoToFrame(3);
        p.MouseMove(WindowTests.DocToWindow(p, 2, 2));
        Save(p, "animation-pixel-sample");
        p.Close();

        Assert.True(File.Exists(Path.Combine(Screenshots.OutDir, "animation-pixel-sample.png")));
    }
}
