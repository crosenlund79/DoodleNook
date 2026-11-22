using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Storybook;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 016 — the UI review at the Kamvas 13's 150 % (1280 × 720 at 1.5): every workspace and the main dialogs and
/// views, captured for a look (out/screenshots/m016-ui), with what can be checked automatically checked: every dialog
/// fits the window (nothing cut off at the edges) and Esc closes it without changing anything.
/// </summary>
public sealed class UiReviewScreenshots
{
    private static string Dir
    {
        get
        {
            var d = Path.Combine(Screenshots.OutDir, "m016-ui");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static void Shot(MainWindow w, string name)
    {
        Pump();
        using var frame = w.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(Dir, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static MainWindow Kamvas(WorkspaceKind kind)
    {
        var w = Open(1280, 720, kind);
        w.SetRenderScaling(1.5);
        Pump();
        return w;
    }

    /// <summary>The open dialog's card lies inside the window, and Esc closes it.</summary>
    private static async Task FitsAndEscCloses(MainWindow w, Task pending, string name)
    {
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
        Pump();
        Shot(w, name);
        // The buttons must be on screen (Mission 016: Customize's Done was cut off at 150 %), and so the whole card.
        foreach (var name2 in new[] { "DialogCard", "DialogButtons" })
        {
            var c = w.GetVisualDescendants().OfType<Control>().Last(b => b.IsEffectivelyVisible && b.Name == name2);
            var topLeft = c.TranslatePoint(new Point(0, 0), w)!.Value;
            Assert.True(topLeft.X >= 0 && topLeft.Y >= 0 && topLeft.X + c.Bounds.Width <= w.ClientSize.Width + 0.5 && topLeft.Y + c.Bounds.Height <= w.ClientSize.Height + 0.5,
                $"{name}: {name2} ({topLeft.X:0},{topLeft.Y:0} {c.Bounds.Width:0}×{c.Bounds.Height:0}) isn't inside {w.ClientSize}");
        }
        var buttons = w.GetVisualDescendants().OfType<Control>().Last(b => b.IsEffectivelyVisible && b.Name == "DialogButtons");
        var card = w.GetVisualDescendants().OfType<Control>().Last(b => b.IsEffectivelyVisible && b.Name == "DialogCard");
        var bottom = buttons.TranslatePoint(new Point(0, buttons.Bounds.Height), card)!.Value.Y;
        Assert.True(bottom <= card.Bounds.Height, $"{name}: the buttons are cut off inside the dialog ({bottom:0} of {card.Bounds.Height:0})");
        Key(w, Avalonia.Input.Key.Escape);
        await ArtistsCompanionWindowTests.Until(() => pending.IsCompleted);
        Assert.False(w.Dialogs.IsOpen, $"{name}: Esc didn't close it");
    }

    [AvaloniaFact]
    public async Task Capture_every_workspace_and_the_main_dialogs_at_150_percent()
    {
        foreach (var kind in Enum.GetValues<WorkspaceKind>())
        {
            var w = Kamvas(kind);
            if (kind is WorkspaceKind.Animation)
            {
                await w.OpenSampleAsync(SampleAnimation.BouncingBall);
                Pump();
            }
            Shot(w, $"workspace-{kind}");
            w.Session.Document.MarkSaved();
            w.Close();
        }

        var a = Kamvas(WorkspaceKind.Animation);
        await a.OpenSampleAsync(SampleAnimation.BouncingBall);
        Pump();
        await FitsAndEscCloses(a, a.NewAsync(), "dialog-new-picture");
        await FitsAndEscCloses(a, a.ExportAnimationAsync(), "dialog-export-animation");
        await FitsAndEscCloses(a, a.InsertReferenceCycleAsync(), "dialog-reference-cycle");
        await FitsAndEscCloses(a, a.ExportAsync(ExportFormat.Png), "dialog-export-png");
        await FitsAndEscCloses(a, a.PreferencesAsync(), "dialog-preferences");
        await FitsAndEscCloses(a, a.CustomizeWorkspaceAsync(), "dialog-customize");
        await FitsAndEscCloses(a, PaletteManager.ShowAsync(a), "dialog-palettes");
        var sheet = Path.Combine(Path.GetTempPath(), "dn-ui-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        await a.ExportAnimationToPathAsync(sheet, new Settings.AnimationExportSettings(Settings.AnimationExportFormat.SpriteSheet, Json: false));
        await FitsAndEscCloses(a, a.ImportSpriteSheetFromAsync(sheet), "dialog-sprite-sheet");
        File.Delete(sheet);
        a.Session.Document.MarkSaved();
        a.Close();

        var s = Kamvas(WorkspaceKind.Painting);
        var book = Path.Combine(Path.GetTempPath(), "dn-ui-" + Guid.NewGuid().ToString("N")[..8], "Review.donkbook");
        Directory.CreateDirectory(Path.GetDirectoryName(book)!);
        Assert.True(await s.CreateStorybookAsync(book, 210, 297, 300, StoryLayouts.Comic));
        s.StorybookView!.Select(1);
        Shot(s, "view-storybook");
        await FitsAndEscCloses(s, s.NewStorybookDrawingAsync(0, 1), "dialog-storybook-new-drawing");
        Key(s, Avalonia.Input.Key.Escape); // back to drawing
        Assert.False(s.StorybookShown);
        s.Close();
        Directory.Delete(Path.GetDirectoryName(book)!, true);
    }
}
