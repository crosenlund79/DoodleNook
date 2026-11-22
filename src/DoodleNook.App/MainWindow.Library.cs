using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App;

/// <summary>The creative library in the window: stamps, patterns, packs and the asset menus.</summary>
public partial class MainWindow
{
    internal static readonly FilePickerFileType PackType = new("DoodleNook asset pack") { Patterns = ["*" + AssetPack.Extension] };

    /// <summary>Opens the user's library (or an in-memory one for tests) and tells about load problems once.</summary>
    private void InitLibrary()
    {
        CreativeLibrary library;
        try
        {
            library = _persistSettings ? new CreativeLibrary(CreativeLibrary.DefaultRoot) : new CreativeLibrary();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            library = new CreativeLibrary();
        }
        Session.SetLibrary(library);
        if (library.LoadProblems.Count > 0)
            Opened += async (_, _) => await _dialogs.ShowMessageAsync("Your creative library", string.Join("\n\n", library.LoadProblems));
    }

    /// <summary>The Brush Laboratory shown last (tests and screenshots drive it).</summary>
    internal BrushLab? LastBrushLab { get; set; }

    /// <summary>Opens the Brush Laboratory for the current brush (or another).</summary>
    public Task<bool> OpenBrushLabAsync(string? presetId = null, BrushLab.Section section = BrushLab.Section.Tip)
    {
        HideFlyouts();
        Canvas.InterruptGesture();
        return BrushLab.ShowAsync(this, presetId, section);
    }

    internal async Task RenameBrushAsync(string id)
    {
        var b = Session.Library.FindBrush(id);
        if (b is null || b.BuiltIn) return;
        var name = await _dialogs.AskTextAsync("Rename brush", "Name", b.Name);
        if (name is not null) Session.Library.RenameBrush(id, name);
    }

    internal async Task DeleteBrushAsync(string id)
    {
        var b = Session.Library.FindBrush(id);
        if (b is null || b.BuiltIn) return;
        if (!await _dialogs.ConfirmAsync($"Delete “{b.Name}”?", "The brush is removed from My brushes. Pictures painted with it stay as they are.", "Delete")) return;
        Session.Library.DeleteBrush(id);
        if (Session.BrushPresetId == id) Session.ApplyPreset("pencil");
    }

    internal PaletteManager? LastPaletteManager { get; set; }

    /// <summary>Window › Palettes.</summary>
    public Task<string?> OpenPalettesAsync(string? paletteId = null)
    {
        HideFlyouts();
        return PaletteManager.ShowAsync(this, paletteId);
    }

    /// <summary>Uses a palette: it fills the colour panel's swatches, and in Pixel Art (or pixel-art animation) becomes the
    /// picture's palette (exact colours).</summary>
    public void UsePalette(string? paletteId)
    {
        // A locked picture changes palette only through the remap preview.
        if (Session.Document.PaletteLocked && paletteId is not null && Session.Library.FindPalette(paletteId) is { } lp
            && !lp.Colors.SequenceEqual(Session.Document.Palette.Colors))
        {
            _ = RemapToPaletteAsync(new Engine.Documents.Palette(lp.Name, lp.Colors) { LibraryId = lp.Id }, forceLock: true);
            return;
        }
        Session.SetActivePalette(paletteId);
        if (paletteId is not null && Session.Library.FindPalette(paletteId) is { } p && Session.PixelMode && !Session.Document.PaletteLocked)
            Session.Document.Palette = new Engine.Documents.Palette(p.Name, p.Colors) { LibraryId = p.Id };
        ShowToast(paletteId is null ? "Swatches: your own swatches" : $"Palette: {Session.Library.FindPalette(paletteId)?.Name}");
    }

    private ReferenceBoardPanel? _referenceBoard;

    /// <summary>The reference board panel (created when first shown).</summary>
    internal ReferenceBoardPanel ReferenceBoard
    {
        get
        {
            if (_referenceBoard is null)
            {
                _referenceBoard = new ReferenceBoardPanel(this) { IsVisible = false };
                CanvasHost.Children.Insert(CanvasHost.Children.IndexOf(_radial), _referenceBoard);
            }
            return _referenceBoard;
        }
    }

    /// <summary>Window › Reference board: shown per workspace (remembered like Customize workspace's options).</summary>
    public void SetReferenceBoardVisible(bool visible)
    {
        if (Workspace.Kind == WorkspaceKind.PureSketch) return;
        WsState.SetShown(CustomOptions.ReferenceBoard, Workspace.Kind, visible);
        ApplyReferenceBoard();
        if (!visible) _referenceBoard?.SaveNow();
    }

    private void ApplyReferenceBoard()
    {
        bool show = !UiHidden && !StorybookShown && WsState.IsShown(CustomOptions.ReferenceBoard, Workspace.Kind);
        if (show) ReferenceBoard.IsVisible = true;
        else if (_referenceBoard is not null) _referenceBoard.IsVisible = false;
    }

    /// <summary>Opens the palette manager with colours proposed from a picture (reference board, tests).</summary>
    public async Task PaletteFromPictureAsync(RasterImage picture)
    {
        HideFlyouts();
        var task = PaletteManager.ShowAsync(this, Session.ActivePaletteId);
        LastPaletteManager?.ExtractFrom(picture, 8);
        await task;
    }

    /// <summary>Replaces the library (tests use a temporary folder to check persistence).</summary>
    internal void UseLibrary(CreativeLibrary library) => Session.SetLibrary(library);

    // ================================================================== new assets

    /// <summary>Asks for a name and collection, showing the picture (tiled for patterns, to check the seams).</summary>
    private async Task<(string Name, string Collection)?> AskAssetDetailsAsync(string title, RasterImage picture, bool tiled, string name, string collection,
        IEnumerable<string> collections)
    {
        var nameBox = new TextBox { Text = name, Width = 220 };
        var collectionBox = new TextBox { Text = collection, Width = 220 };
        var preview = new Image
        {
            Source = tiled ? AssetPicker.TiledPreview(picture, 300, 140) : BrushPreview.ToBitmap(AssetPicker.Fit(picture, 160)),
            Stretch = Stretch.None,
        };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new Border { Background = UiKit.CheckerBrush(), Child = preview, CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left });
        if (tiled) body.Children.Add(DialogHost.Text($"The tile ({picture.Width} × {picture.Height} px), repeated. If its edges don't meet, adjust the artwork (Pixel Art's tiled view helps) and save it again."));
        body.Children.Add(UiKit.Row(8, new TextBlock { Text = "Name", Classes = { "dim" }, Width = 80, VerticalAlignment = VerticalAlignment.Center }, nameBox));
        body.Children.Add(UiKit.Row(8, new TextBlock { Text = "Collection", Classes = { "dim" }, Width = 80, VerticalAlignment = VerticalAlignment.Center }, collectionBox));
        var chips = new WrapPanel();
        foreach (var c in collections.Distinct().Take(10))
        {
            var b = UiKit.Button(null, c);
            b.Height = 26;
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Background = new SolidColorBrush(DnColors.Ui3);
            var value = c;
            b.Click += (_, _) => collectionBox.Text = value;
            chips.Children.Add(b);
        }
        body.Children.Add(chips);
        var r = await _dialogs.ShowAsync(title, body, [("cancel", "Cancel", false), ("ok", "Save", true)], "ok", "cancel", 420);
        if (r != "ok") return null;
        return (string.IsNullOrWhiteSpace(nameBox.Text) ? name : nameBox.Text.Trim(), string.IsNullOrWhiteSpace(collectionBox.Text) ? collection : collectionBox.Text.Trim());
    }

    /// <summary>Edit › Save selection as stamp / pattern: the active layer's selected pixels become a library asset.
    /// The artwork itself is not changed.</summary>
    public async Task<bool> SaveSelectionAsAssetAsync(PickerKind kind)
    {
        HideFlyouts();
        var picture = Session.SelectionPicture();
        if (picture is null)
        {
            ShowToast("Select some artwork first (or draw on this layer) — that becomes the " + (kind == PickerKind.Stamps ? "stamp" : "pattern"));
            return false;
        }
        var lib = Session.Library;
        var details = kind == PickerKind.Stamps
            ? await AskAssetDetailsAsync("Save as stamp", picture, false, "My stamp", "My stamps", lib.Stamps.Where(s => !s.BuiltIn).Select(s => s.Collection))
            : await AskAssetDetailsAsync("Save as pattern", picture, true, "My pattern", "My patterns", lib.Patterns.Where(p => !p.BuiltIn).Select(p => p.Collection));
        if (details is not { } d) return false;
        return AddAsset(kind, picture, d.Name, d.Collection);
    }

    /// <summary>Adds a picture as a stamp or pattern and makes it the current one (tests and the dialogs use this).</summary>
    public bool AddAsset(PickerKind kind, RasterImage picture, string name, string collection)
    {
        try
        {
            if (kind == PickerKind.Stamps)
            {
                var s = Session.Library.AddStamp(name, collection, picture);
                Session.SetStampOptions(Session.StampOptions with { StampId = s.Id });
                ShowToast($"Stamp “{s.Name}” saved · press S for the stamp tool");
            }
            else
            {
                var p = Session.Library.AddPattern(name, collection, picture);
                Session.SetPatternOptions(Session.PatternOptions with { PatternId = p.Id });
                ShowToast($"Pattern “{p.Name}” saved · paint bucket › Pattern, or Edit › Fill with pattern");
            }
            return true;
        }
        catch (ArgumentException ex)
        {
            ShowToast(ex.Message);
            return false;
        }
    }

    public async Task ImportAssetPictureAsync(PickerKind kind)
    {
        HideFlyouts();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = kind == PickerKind.Stamps ? "Import a picture as a stamp" : "Import a picture as a pattern tile",
            AllowMultiple = true,
            FileTypeFilter = [PngType, AnyImage, FilePickerFileTypes.All],
        });
        int added = 0;
        foreach (var f in files)
        {
            try
            {
                await using var stream = await f.OpenReadAsync();
                var img = ImageFiles.Decode(stream);
                if (AddAsset(kind, img, Path.GetFileNameWithoutExtension(f.Name), "Imported")) added++;
            }
            catch (Exception ex)
            {
                await _dialogs.ShowMessageAsync("Couldn't import the picture", $"{f.Name}: {ex.Message}");
            }
        }
        if (added > 1) ShowToast($"{added} {(kind == PickerKind.Stamps ? "stamps" : "patterns")} imported into “Imported”");
    }

    // ================================================================== asset menus

    internal IEnumerable<MenuEntry> AssetMenu(PickerKind kind, string id)
    {
        var lib = Session.Library;
        bool stamp = kind == PickerKind.Stamps;
        var (name, builtIn) = stamp ? (lib.FindStamp(id)?.Name ?? "", lib.FindStamp(id)?.BuiltIn ?? true) : (lib.FindPattern(id)?.Name ?? "", lib.FindPattern(id)?.BuiltIn ?? true);
        bool fav = stamp ? lib.IsFavoriteStamp(id) : lib.IsFavoritePattern(id);
        var list = new List<MenuEntry>
        {
            MenuEntry.Heading(name),
            new("star", fav ? "Remove from favourites" : "Add to favourites", null, () =>
            {
                if (stamp) lib.SetFavoriteStamp(id, !fav);
                else lib.SetFavoritePattern(id, !fav);
            }),
        };
        if (stamp) list.Add(new("brush", "Make a brush from this stamp", null, () => MakeStampBrush(id)));
        else list.Add(new("brush", "Use as the brush texture", null, () => UsePatternAsTexture(id)));
        list.Add(MenuEntry.Separator);
        list.Add(new(null, "Rename…", null, () => _ = RenameAssetAsync(kind, id), !builtIn));
        list.Add(new(null, "Move to collection…", null, () => _ = MoveAssetAsync(kind, id), !builtIn));
        list.Add(new("export", "Export as asset pack…", null, () => _ = ExportPackAsync(name, stampIds: stamp ? [id] : null, patternIds: stamp ? null : [id]), !builtIn));
        list.Add(new("trash", "Delete…", null, () => _ = DeleteAssetAsync(kind, id), !builtIn));
        return list;
    }

    /// <summary>A new brush in My brushes that paints this stamp along the stroke, varied in size and spacing.</summary>
    public BrushPreset MakeStampBrush(string stampId)
    {
        var lib = Session.Library;
        var stamp = lib.FindStamp(stampId)!;
        var img = lib.StampImage(stampId);
        double size = Math.Clamp(Math.Max(img?.Width ?? 48, img?.Height ?? 48), 16, 160);
        var brush = lib.AddBrush(stamp.Name + " brush", new BrushSettings
        {
            Size = size, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.5, TipId = "stamp:" + stampId, TipColors = true,
            Spacing = 0.6, Scatter = 0.3, SizeJitter = 0.3, RotationJitter = 0.05,
        }, $"Paints the {stamp.Name} stamp along the stroke");
        Session.UseBrush(brush.Settings, brush.Id);
        ShowToast($"New brush “{brush.Name}” in My brushes · fine-tune it in the Brush Laboratory");
        return brush;
    }

    private void UsePatternAsTexture(string patternId)
    {
        var b = Session.Brush;
        Session.UseBrush(b with { TextureId = "pattern:" + patternId, Grain = Math.Max(b.Grain, 0.6) }, Session.BrushPresetId);
        ShowToast("The brush now uses this pattern as its texture (save it in the Brush Laboratory to keep it)");
    }

    private async Task RenameAssetAsync(PickerKind kind, string id)
    {
        var lib = Session.Library;
        string current = kind == PickerKind.Stamps ? lib.FindStamp(id)?.Name ?? "" : lib.FindPattern(id)?.Name ?? "";
        var name = await _dialogs.AskTextAsync("Rename", "Name", current);
        if (name is null) return;
        if (kind == PickerKind.Stamps) lib.RenameStamp(id, name);
        else lib.RenamePattern(id, name);
    }

    private async Task MoveAssetAsync(PickerKind kind, string id)
    {
        var lib = Session.Library;
        var existing = kind == PickerKind.Stamps ? lib.Stamps.Select(s => s.Collection) : lib.Patterns.Select(p => p.Collection);
        string current = kind == PickerKind.Stamps ? lib.FindStamp(id)?.Collection ?? "" : lib.FindPattern(id)?.Collection ?? "";
        var c = await _dialogs.AskTextAsync("Move to collection", "Collection", current, "Move", existing.ToList());
        if (c is null) return;
        if (kind == PickerKind.Stamps) lib.MoveStamp(id, c);
        else lib.MovePattern(id, c);
    }

    private async Task DeleteAssetAsync(PickerKind kind, string id)
    {
        var lib = Session.Library;
        string name = kind == PickerKind.Stamps ? lib.FindStamp(id)?.Name ?? "" : lib.FindPattern(id)?.Name ?? "";
        var users = lib.BrushesUsing(id);
        string extra = users.Count == 0 ? "" : $"\n\n{users.Count} brush{(users.Count == 1 ? " uses" : "es use")} it ({string.Join(", ", users.Select(u => u.Name))}); they will paint with a round tip instead.";
        if (!await _dialogs.ConfirmAsync($"Delete “{name}”?", $"It is removed from your library. Pictures already painted with it stay as they are.{extra}", "Delete")) return;
        if (kind == PickerKind.Stamps) lib.DeleteStamp(id);
        else lib.DeletePattern(id);
        AssetPicker.Forget(id);
    }

    // ================================================================== packs and the library dialog

    internal async Task ExportPackAsync(string suggestedName, IEnumerable<string>? brushIds = null, IEnumerable<string>? stampIds = null,
        IEnumerable<string>? patternIds = null, IEnumerable<string>? paletteIds = null)
    {
        HideFlyouts();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export asset pack",
            SuggestedFileName = suggestedName + AssetPack.Extension,
            DefaultExtension = AssetPack.Extension.TrimStart('.'),
            FileTypeChoices = [PackType],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            var contents = AssetPack.Export(Session.Library, path, brushIds, stampIds, patternIds, paletteIds);
            ShowToast($"Exported {contents} to {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't export", ex.Message);
        }
    }

    public async Task ImportPackAsync()
    {
        HideFlyouts();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import an asset pack",
            AllowMultiple = false,
            FileTypeFilter = [PackType, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            var contents = AssetPack.Import(Session.Library, path);
            ShowToast($"Imported {contents}");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't import the pack", ex.Message);
        }
    }

    /// <summary>Window › Creative library: where it lives, what's in it, packs in and out.</summary>
    public async Task OpenLibraryAsync(PickerKind? focus = null)
    {
        HideFlyouts();
        var lib = Session.Library;
        int brushes = lib.Brushes.Count(b => !b.BuiltIn), stamps = lib.Stamps.Count(s => !s.BuiltIn), patterns = lib.Patterns.Count(p => !p.BuiltIn),
            palettes = lib.Palettes.Count(p => !p.BuiltIn);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(DialogHost.Text($"Your own assets: {brushes} brush{(brushes == 1 ? "" : "es")}, {stamps} stamp{(stamps == 1 ? "" : "s")}, " +
                                          $"{patterns} pattern{(patterns == 1 ? "" : "s")} and {palettes} palette{(palettes == 1 ? "" : "s")}, next to DoodleNook's built-in ones."));
        body.Children.Add(DialogHost.Text(lib.Root is null
            ? "This session keeps the library in memory only."
            : $"They are kept in {lib.Root} (safe from updates of DoodleNook; your pictures are copied there, so moving the originals breaks nothing)."));
        if (lib.ReadOnly) body.Children.Add(DialogHost.Text("The library was saved by a newer DoodleNook and is read-only here."));
        body.Children.Add(DialogHost.Text("An asset pack (.dnkpack) holds brushes (with their tips and textures), stamps, patterns and palettes, to keep a backup or share with another computer. Importing never replaces anything."));
        var r = await _dialogs.ShowAsync("Creative library", body,
            [("export", "Export everything…", false), ("import", "Import a pack…", false), ("ok", "Close", true)], "ok", "ok", 480);
        if (r == "import") await ImportPackAsync();
        else if (r == "export")
            await ExportPackAsync("My DoodleNook assets", lib.Brushes.Where(b => !b.BuiltIn).Select(b => b.Id), lib.Stamps.Where(s => !s.BuiltIn).Select(s => s.Id),
                lib.Patterns.Where(p => !p.BuiltIn).Select(p => p.Id), lib.Palettes.Where(p => !p.BuiltIn).Select(p => p.Id));
    }
}
