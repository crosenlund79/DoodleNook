using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Palette Lock and Remap screenshots (out/screenshots/palette-*.png).</summary>
public class PaletteLockScreenshots
{
    private static async Task Until(Func<bool> done, int ms = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Pump();
            await Task.Delay(20);
            Pump();
        }
    }

    private static Palette Library(MainWindow w, string id)
    {
        var p = w.Session.Library.FindPalette(BuiltInAssets.Prefix + id)!;
        return new Palette(p.Name, p.Colors) { LibraryId = p.Id };
    }

    private static async Task PreviewReady(PaletteRemapDialog d) => await Until(() => d.PreviewStatus.StartsWith("After", StringComparison.Ordinal) || d.PreviewStatus == "Before");

    [AvaloniaFact]
    public async Task Capture_palette_lock_and_remap_screenshots()
    {
        // A painting (thousands of soft colours) locked to Dusk: the remap preview groups similar colours.
        foreach (var (name, width, height, scaling) in new[] { ("kamvas13-150", 1280.0, 720.0, 1.5), ("desktop1080", 1920.0, 1080.0, 1.0) })
        {
            var k = WorkshopScreenshots.Painting(width, height, scaling);
            var lockTask = k.LockToPaletteAsync(Library(k, "dusk"));
            await Until(() => Has(k, "PaletteRemapDialog"));
            await PreviewReady(k.LastRemapDialog!);
            WorkshopScreenshots.Save(k, $"palette-remap-painting-{name}");
            if (name == "kamvas13-150")
            {
                k.LastRemapDialog!.ShowPreviewFrame(0, after: false);
                await PreviewReady(k.LastRemapDialog!);
                WorkshopScreenshots.Save(k, "palette-remap-painting-before");
            }
            k.Dialogs.Close("apply");
            await lockTask;
            Pump();
            k.MouseMove(DocToWindow(k, 1000, 760));
            WorkshopScreenshots.Save(k, $"palette-lock-painting-{name}");
            k.Close();
        }

        // A pixel sprite remapped to another palette, with one colour chosen by hand.
        var p = Open(1280, 720, WorkspaceKind.PixelArt);
        p.SetRenderScaling(1.5);
        Pump();
        var sample = Engine.Samples.SampleAnimations.PixelSlime();
        p.Session.ReplaceDocument(Document.FromImage(sample.RenderFrame(2, includePaper: false), "Slime"), null);
        p.Canvas.FitToView();
        Pump();
        var remapTask = p.RemapToPaletteAsync(Library(p, "seaside"));
        await Until(() => Has(p, "PaletteRemapDialog"));
        var dialog = p.LastRemapDialog!;
        dialog.SelectRow(0);
        dialog.AssignSelected(3);
        await PreviewReady(dialog);
        WorkshopScreenshots.Save(p, "palette-remap-sprite");
        p.Dialogs.Close("apply");
        await remapTask;
        p.Session.Document.LockToPalette(p.Session.Document.Palette.Clone());
        p.Session.SelectTool(ToolKind.PixelPencil);
        Pump();
        WorkshopScreenshots.Save(p, "palette-lock-pixel-art");
        p.Close();

        // An animation: the preview has frames and playback.
        var a = Open(1280, 720, WorkspaceKind.Animation);
        a.SetRenderScaling(1.5);
        Pump();
        a.Session.ReplaceDocument(Engine.Samples.SampleAnimations.BouncingBall(), null);
        a.Canvas.FitToView();
        Pump();
        var animTask = a.RemapToPaletteAsync(Library(a, "meadow"));
        await Until(() => Has(a, "PaletteRemapDialog"));
        a.LastRemapDialog!.ShowPreviewFrame(6);
        await PreviewReady(a.LastRemapDialog!);
        WorkshopScreenshots.Save(a, "palette-remap-animation");
        a.Dialogs.Close("cancel");
        await animTask;
        a.Close();
    }
}
