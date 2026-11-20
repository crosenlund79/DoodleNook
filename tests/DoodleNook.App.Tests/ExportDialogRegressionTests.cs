using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using SkiaSharp;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 006 bug: in Export animation, switching from Animated GIF (or PNG sequence) back to Sprite sheet threw
/// "already has a visual parent" from the click handler, which ended the app on Windows ("Not Responding" while the
/// crash was recorded). These tests drive the dialog the same way, with a 1920 × 1080, 21-frame animation.
/// </summary>
public class ExportDialogRegressionTests
{
    private static Button FormatButton(Window w, string label) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible
            && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label));

    private static bool TextShown(Window w, string text) =>
        w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.Contains(text, StringComparison.Ordinal) == true);

    private static MainWindow LargeAnimation()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.CreateNewAnimation(new NewAnimationChoice(1920, 1080, 21, 12, false, ColorRgb.White));
        Pump();
        // Something to export on every frame.
        for (int f = 0; f < 21; f++)
        {
            w.Session.GoToFrame(f);
            Drag(w, MouseButton.Left, RawInputModifiers.None, (100 + f * 60, 500), (160 + f * 60, 560));
        }
        return w;
    }

    [AvaloniaFact]
    public void Switching_formats_back_and_forth_stays_responsive_and_keeps_the_values()
    {
        var w = LargeAnimation();
        var task = w.Dialogs.AskExportAnimationAsync(new AnimationExportSettings(), 21, 1920, 1080, hasPaper: true);
        Pump();
        string[] sequence = ["Animated GIF", "Sprite sheet", "PNG sequence", "Animated GIF", "PNG sequence", "Sprite sheet", "Animated GIF", "Sprite sheet",
                             "Animated GIF", "Sprite sheet"];
        var slowest = 0L;
        foreach (var label in sequence)
        {
            var sw = Stopwatch.StartNew();
            Click(w, FormatButton(w, label)); // threw before the fix
            slowest = Math.Max(slowest, sw.ElapsedMilliseconds);
            Assert.True(TextShown(w, label == "Sprite sheet" ? "Layout" : label == "Animated GIF" ? "Loop forever" : "PNG files of"), label);
        }
        Assert.True(slowest < 2000, $"slowest switch {slowest} ms"); // switching does no animation work at all

        // Values typed into the fresh boxes survive another round trip through GIF.
        var spacing = w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
        spacing.Text = "7";
        Pump();
        Click(w, FormatButton(w, "Animated GIF"));
        Click(w, FormatButton(w, "Sprite sheet"));
        Assert.Equal("7", w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible).Text);
        Assert.True(TextShown(w, "21 frames of 1920 × 1080"));

        w.Dialogs.Close(null); // Cancel
        Pump();
        Assert.True(task.IsCompleted);
        Assert.Null(task.Result);

        // Reopen: still works, and the chosen settings come back from OK.
        var again = w.Dialogs.AskExportAnimationAsync(new AnimationExportSettings(AnimationExportFormat.Gif), 21, 1920, 1080, hasPaper: true);
        Pump();
        Assert.True(TextShown(w, "Loop forever"));
        Click(w, FormatButton(w, "Sprite sheet"));
        Click(w, FormatButton(w, "Animated GIF"));
        w.Dialogs.Close("ok");
        Pump();
        Assert.Equal(AnimationExportFormat.Gif, again.Result!.Format);
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_large_gif_exports_every_frame_with_its_timing_and_failures_leave_no_file()
    {
        var w = LargeAnimation();
        w.Session.SetFrameDuration(5, 250);
        var dir = Path.Combine(Path.GetTempPath(), "doodlenook-gif-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var gif = Path.Combine(dir, "big.gif");
        await w.ExportAnimationToPathAsync(gif, new AnimationExportSettings(AnimationExportFormat.Gif, Background: true));
        using (var codec = SKCodec.Create(gif))
        {
            Assert.Equal(21, codec.FrameCount);
            Assert.Equal((1920, 1080), (codec.Info.Width, codec.Info.Height));
            Assert.InRange(codec.FrameInfo[0].Duration, 80, 90);
            Assert.InRange(codec.FrameInfo[5].Duration, 240, 260);
        }

        // A folder that doesn't exist: the export fails cleanly and leaves nothing behind.
        var missing = Path.Combine(dir, "no-such-folder", "big.gif");
        await Assert.ThrowsAnyAsync<IOException>(() => w.ExportAnimationToPathAsync(missing, new AnimationExportSettings(AnimationExportFormat.Gif)));
        Assert.False(File.Exists(missing));
        Assert.Single(Directory.GetFiles(dir)); // no temporary files left over
        Directory.Delete(dir, true);
        w.Close();
    }
}
