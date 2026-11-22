using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 015 — Palettes › Import folder… in the real window: one summary, the palettes in the library and
/// usable at once.</summary>
public sealed class PaletteFolderWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-palfolder-" + Guid.NewGuid().ToString("N"), "Game palettes");

    public PaletteFolderWindowTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static Task Until(Func<bool> done, int ms = 10_000) => ArtistsCompanionWindowTests.Until(done, ms);

    internal static void WriteSamplePalettes(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "dusk-harbour.gpl"),
            "GIMP Palette\n#Palette Name: Dusk Harbour\n#Description: Warm lamps over cold water.\n#Colors: 6\n" +
            "26 28 44\t1a1c2c\n93 39 93\t5d275d\n177 62 83\tb13e53\n239 125 87\tef7d57\n255 205 117\tffcd75\n167 240 112\ta7f070\n");
        File.WriteAllText(Path.Combine(dir, "dusk-harbour.hex"), "1a1c2c\n5d275d\nb13e53\nef7d57\nffcd75\na7f070\n"); // the same palette again
        File.WriteAllText(Path.Combine(dir, "moss.pal"), "JASC-PAL\n0100\n4\n20 32 24\n52 86 52\n118 150 82\n210 222 160\n");
        File.WriteAllText(Path.Combine(dir, "candy.txt"), ";paint.net Palette File\n;Palette Name: Candy Shop\nFFFF77A8\nFFFFCCAA\nFF83769C\nFF29ADFF\nFF00E436\n");
        File.WriteAllText(Path.Combine(dir, "README.md"), "Palettes for the game.\n");
    }

    [AvaloniaFact]
    public async Task A_folder_of_palettes_imports_with_one_summary_and_is_ready_to_use()
    {
        WriteSamplePalettes(_dir);
        var w = Open(1280, 800, WorkspaceKind.PixelArt);
        int before = w.Session.Library.Palettes.Count;
        var show = PaletteManager.ShowAsync(w);
        await Until(() => w.Dialogs.IsOpen);
        Pump();
        Assert.True(Has(w, "PaletteImportFolder"));
        var manager = w.LastPaletteManager!;
        manager.PrepareFolderImport(_dir);
        Pump();
        Assert.StartsWith("“Game palettes” holds 4 palette files and 1 other file", Find<TextBlock>(w, "PaletteFolderText").Text);

        var summary = await manager.RunFolderImportAsync();
        Pump();
        Assert.NotNull(summary);
        Assert.Equal("4 palette files found · 3 imported · 1 already in your library (skipped) · 1 other file skipped.", Find<TextBlock>(w, "PaletteFolderText").Text);
        Assert.Equal(before + 3, w.Session.Library.Palettes.Count);
        Assert.True(Has(w, "Palette_Dusk_Harbour"));
        Assert.True(Has(w, "Palette_Candy_Shop"));
        // The first palette imported is chosen: the .gpl (named palette files come first, so the named copy is kept).
        Assert.Contains("Imported from dusk-harbour.gpl", Find<TextBlock>(w, "PaletteCredit").Text);
        Assert.Contains("Warm lamps over cold water.", Find<TextBlock>(w, "PaletteCredit").Text);

        // Importing the same folder again adds nothing.
        manager.PrepareFolderImport(_dir);
        Assert.Equal(0, (await manager.RunFolderImportAsync())!.ImportedCount);

        w.Dialogs.Close("use");
        var chosen = await show;
        var dusk = w.Session.Library.Palettes.Single(p => p.Name == "Dusk Harbour");
        Assert.Equal(dusk.Id, chosen);
        Assert.Equal(dusk.Id, w.Session.ActivePaletteId);
        Assert.Equal(ColorRgb.Parse("#1A1C2C"), dusk.Colors[0]);
        w.Close();
    }
}
