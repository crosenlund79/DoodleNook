using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Library;

namespace DoodleNook.App;

/// <summary>
/// Game Edit Mode's project palette: the catalog's "Project palette…" chooses a palette for the game's graphics and can
/// lock them all to it; each image can follow it, use its own palette, or have no lock. The rule is applied when an image
/// is opened, with the existing document Palette Lock, and colours outside the palette always go through the existing
/// remap preview first. Nothing is ever recoloured without Apply, and Ctrl+S never saves an image as if it conformed
/// when it doesn't.
/// </summary>
public partial class MainWindow
{
    private static bool LockedTo(Document doc, Palette palette) => doc.PaletteLocked && doc.Palette.Colors.SequenceEqual(palette.Colors);

    /// <summary>Applies an image's palette rule to the picture just read (before it is shown): locked at once when its
    /// colours fit, unlocked for "No palette lock". Returns the palette to offer the remap preview for, or null.</summary>
    private static async Task<Palette?> PrepareGamePaletteAsync(GameProject game, GameImage image, Document doc)
    {
        if (image.PaletteChoice == GamePaletteChoice.None)
        {
            if (doc.PaletteLocked) doc.TrySetPaletteAsOpened(doc.Palette, locked: false);
            return null;
        }
        if (game.LockPaletteFor(image) is not { } palette || LockedTo(doc, palette)) return null;
        return await Task.Run(() => doc.TrySetPaletteAsOpened(palette.Clone(), locked: true) ? null : palette);
    }

    /// <summary>The picture has colours outside the palette it should keep to: the usual remap preview (Cancel leaves it as it is).</summary>
    private async Task<bool> OfferGameRemapAsync(GameImage image, Palette palette)
    {
        string whose = image.PaletteChoice == GamePaletteChoice.Custom ? "its own" : "the project";
        bool applied = await PaletteRemapDialog.ShowAsync(this, new RemapRequest($"Remap {image.FileName} to “{palette.Name}”", palette.Clone(), ForceLock: true,
            ChoosePalette: false,
            Intro: $"{image.FileName} has colours outside {whose} palette. Nothing changes until Apply; Cancel leaves it as it is."));
        if (!applied) ShowToast($"{image.FileName} isn't locked to “{palette.Name}” · Ctrl+S asks what to do");
        return applied;
    }

    /// <summary>Before Ctrl+S of a game image: an image that should keep to a palette but has other colours is remapped
    /// (previewed) or saved as an exception with no palette lock — never saved as if it conformed.</summary>
    private async Task<bool> ResolveGamePaletteBeforeSaveAsync(GameProject game, GameImage image)
    {
        var doc = Session.Document;
        if (game.LockPaletteFor(image) is not { } palette || LockedTo(doc, palette)) return true;
        var use = await Task.Run(() => doc.CheckPalette(palette.Colors));
        if (use.OnlyPaletteColours) return true;
        var choice = await _dialogs.ShowAsync($"“{image.FileName}” has colours outside “{palette.Name}”",
            DialogHost.Text($"{use.ColoursOutside} colour{(use.ColoursOutside == 1 ? " isn't" : "s aren't")} in the palette. Remap them first (with a preview), " +
                            "or save the image as a palette exception with no palette lock?"),
            [("cancel", "Cancel", false), ("none", "Save without palette lock", false), ("remap", "Remap…", true)], "remap", "cancel", 500);
        if (choice == "remap") return await OfferGameRemapAsync(image, palette);
        if (choice != "none") return false;
        try
        {
            game.SetPaletteChoice(image, GamePaletteChoice.None);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't save", $"The palette choice couldn't be saved: {ex.Message}");
            return false;
        }
        RefreshGameStatus();
        return true;
    }

    /// <summary>After a palette rule changed: the open game image follows it now, with the usual undoable lock (and the
    /// remap preview when colours would change).</summary>
    private async Task ApplyGamePaletteToOpenImageAsync()
    {
        if (Game is not { } game || GameImage is not { } image || !EditingGameImage) return;
        var doc = Session.Document;
        if (image.PaletteChoice == GamePaletteChoice.None)
        {
            UnlockPalette();
            return;
        }
        if (game.LockPaletteFor(image) is not { } palette || LockedTo(doc, palette)) return;
        var use = await Task.Run(() => doc.CheckPalette(palette.Colors));
        if (use.OnlyPaletteColours) doc.LockToPalette(palette.Clone());
        else await OfferGameRemapAsync(image, palette);
    }

    // ================================================================== the catalog's "Project palette…"

    /// <summary>Chooses (or imports) the project palette and whether all game graphics are locked to it.</summary>
    public async Task<bool> ShowProjectPaletteAsync()
    {
        if (Game is not { } game) return false;
        var chosen = game.ProjectPalette?.Clone() ?? DefaultLibraryPalette();
        bool lockAll = game.ProjectPalette is null || game.LockAllToPalette; // choosing one is usually for locking to it
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = Avalonia.Media.FontWeight.SemiBold, MaxWidth = 300, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        var swatches = new WrapPanel { Name = "ProjectPaletteSwatches", ItemWidth = 17, ItemHeight = 17 };
        void Show()
        {
            name.Text = $"{chosen.Name} ({chosen.Colors.Count} colour{(chosen.Colors.Count == 1 ? "" : "s")})";
            swatches.Children.Clear();
            foreach (var c in chosen.Colors.Take(128)) swatches.Children.Add(UiKit.Swatch(c, 14, 3, outline: true));
        }
        Show();
        var chooser = Menus.MenuButton(UiKit.Row(6, new DnIcon { Kind = "palette" }, name, new DnIcon { Kind = "chevd", Width = 14, Height = 14 }),
            () => LibraryPaletteEntries(chosen, p =>
            {
                chosen = p;
                Show();
            }), "Choose one of your palettes, or import a palette file", 300);
        chooser.Name = "ProjectPaletteChooser";
        chooser.Height = 32;
        chooser.HorizontalAlignment = HorizontalAlignment.Left;
        var (check, _) = UiKit.Check("Lock all game graphics to this palette", "Each image is locked to it when you open it; other colours go through the remap preview first",
            () => lockAll, v => lockAll = v);
        check.Name = "ProjectPaletteLock";
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(DialogHost.Text($"The palette for the graphics in “{game.Name}”."));
        body.Children.Add(chooser);
        body.Children.Add(swatches);
        body.Children.Add(check);
        body.Children.Add(DialogHost.Text("Nothing is recoloured now. When you open an image it is locked to the palette; if it has other colours, " +
                                          "the remap preview comes first and nothing changes until Apply. An image's palette button in the catalog gives it " +
                                          "its own palette or no lock."));
        var r = await _dialogs.ShowAsync("Project palette", body, [("cancel", "Cancel", false), ("save", "Save", true)], "save", "cancel", 500);
        if (r != "save") return false;
        try
        {
            game.SetProjectPalette(chosen, lockAll);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't save the project palette", ex.Message);
            return false;
        }
        RefreshGameStatus();
        _catalog?.LoadThumbnails(); // checks the PNGs against the palette in the background
        ShowToast(game.LockAllToPalette
            ? $"Project palette “{chosen.Name}” · nothing was recoloured: images with other colours are marked “Not on palette” and get the remap preview when opened"
            : $"Project palette “{chosen.Name}” saved · game graphics aren't locked to it");
        await ApplyGamePaletteToOpenImageAsync();
        return true;
    }

    private Palette DefaultLibraryPalette()
    {
        var lib = Session.Library;
        var p = lib.FindPalette(Session.ActivePaletteId) ?? lib.Palettes[0];
        return new Palette(p.Name, p.Colors) { LibraryId = p.Id };
    }

    /// <summary>Your palettes (by collection), then "Import a palette file…".</summary>
    private IEnumerable<MenuEntry> LibraryPaletteEntries(Palette? current, Action<Palette> pick)
    {
        foreach (var group in Session.Library.Palettes.GroupBy(p => p.Collection))
        {
            yield return MenuEntry.Heading(group.Key);
            foreach (var p in group)
            {
                var palette = new Palette(p.Name, p.Colors) { LibraryId = p.Id };
                yield return new(null, $"{p.Name} ({p.Colors.Count})", null, () => pick(palette), Checked: current?.LibraryId == p.Id);
            }
        }
        yield return MenuEntry.Separator;
        yield return new("open", "Import a palette file (.gpl, .hex)…", null, async () =>
        {
            if (await ImportPaletteFileAsync() is { } imported) pick(imported);
        });
    }

    /// <summary>Imports a .gpl or .hex palette into your palettes (as the palette manager does) and returns it.</summary>
    private async Task<Palette?> ImportPaletteFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a palette",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Palettes (.gpl, .hex)") { Patterns = ["*.gpl", "*.hex"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return null;
        try
        {
            var (paletteName, colors) = PaletteFiles.Read(path);
            if (colors.Count == 0) throw new InvalidDataException("It has no colours.");
            var asset = Session.Library.AddPalette(paletteName, "Imported", colors);
            return new Palette(asset.Name, asset.Colors) { LibraryId = asset.Id };
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't import {files[0].Name}: {ex.Message}");
            return null;
        }
    }

    // ================================================================== one image's palette (the tile's palette button)

    /// <summary>The tile's palette menu: follow the project palette, a custom palette, or no palette lock.</summary>
    internal IEnumerable<MenuEntry> GamePaletteEntries(GameImage image)
    {
        if (Game is not { } game) yield break;
        yield return MenuEntry.Heading($"Palette for {image.FileName}");
        yield return new("lock", game.LockAllToPalette ? $"Project palette (“{game.ProjectPalette!.Name}”)" : "Project palette (not locked)", null,
            () => _ = SetGamePaletteChoiceAsync(image, GamePaletteChoice.Project), Checked: image.PaletteChoice == GamePaletteChoice.Project);
        yield return MenuEntry.Sub("palette", image.CustomPalette is { } own ? $"Custom palette (“{own.Name}”)" : "Custom palette",
            LibraryPaletteEntries(image.CustomPalette, p => _ = SetGamePaletteChoiceAsync(image, GamePaletteChoice.Custom, p)).ToList())
            with { Checked = image.PaletteChoice == GamePaletteChoice.Custom };
        yield return new("unlock", "No palette lock", null, () => _ = SetGamePaletteChoiceAsync(image, GamePaletteChoice.None),
            Checked: image.PaletteChoice == GamePaletteChoice.None);
    }

    /// <summary>Changes one image's palette rule (saved in catalog.json). The open image follows at once; others when opened.</summary>
    public async Task SetGamePaletteChoiceAsync(GameImage image, GamePaletteChoice choice, Palette? custom = null)
    {
        if (Game is not { } game) return;
        try
        {
            game.SetPaletteChoice(image, choice, custom);
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't save the palette choice: {ex.Message}");
            return;
        }
        RefreshGameStatus();
        _catalog?.LoadThumbnails();
        ShowToast(choice switch
        {
            GamePaletteChoice.Custom => $"{image.FileName} uses its own palette “{custom!.Name}”",
            GamePaletteChoice.None => $"{image.FileName}: no palette lock",
            _ => $"{image.FileName} follows the project palette",
        });
        if (ReferenceEquals(GameImage, image)) await ApplyGamePaletteToOpenImageAsync();
    }
}
