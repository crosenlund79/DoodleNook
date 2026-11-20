using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;

namespace DoodleNook.App;

/// <summary>
/// Game Edit Mode (Mission 009): choose a game graphics folder, browse its PNGs in a catalog, edit one in the normal
/// workspaces, and Ctrl+S saves the editable .dnk (in _DoodleNook) and then replaces the game's PNG in place. Progress is
/// Not started → In progress (after the first complete save) → Complete (only when the artist says so).
/// When no game image is open and the catalog is closed, DoodleNook is exactly as before.
/// </summary>
public partial class MainWindow
{
    private GameCatalogView? _catalog;
    private Border _gameChip = null!;
    private TextBlock _gameChipText = null!;
    private Button _gameChipComplete = null!;
    private Button _gameChipRetry = null!;
    private Document? _gameDocument;
    private bool _openingGameImage;
    private bool _pngPending;
    private bool _templateInPlace; // "No — Save normally" was chosen for the open template_ image

    /// <summary>The game folder open in Game Edit Mode, or null.</summary>
    public GameProject? Game { get; private set; }

    /// <summary>The game image being edited, or null.</summary>
    public GameImage? GameImage { get; private set; }

    /// <summary>True while the open picture is a game image (Ctrl+S saves the .dnk and replaces the PNG).</summary>
    public bool EditingGameImage => GameImage is { } img && ReferenceEquals(Session.Document, _gameDocument)
                                    && string.Equals(Session.FilePath, img.DnkPath, StringComparison.OrdinalIgnoreCase);

    internal GameCatalogView? Catalog => _catalog;

    public bool CatalogShown => _catalog is { IsVisible: true };

    private void InitGameEdit()
    {
        _gameChipText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis };
        _gameChipComplete = UiKit.Button("check", "Mark complete", "Mark this image as finished (saving never does that by itself)", ToggleCurrentComplete);
        _gameChipRetry = UiKit.Button(null, "Try again", "Replace the game's PNG now", () => _ = RetryPngAsync());
        var catalog = UiKit.Button("image", "Catalog", "Back to the catalog of game images", ShowCatalog);
        foreach (var b in new[] { _gameChipComplete, _gameChipRetry, catalog })
        {
            b.Height = 26;
            b.FontSize = 12;
        }
        _gameChipComplete.Name = "GameMarkComplete";
        catalog.Name = "GameBackToCatalog";
        _gameChip = new Border
        {
            Classes = { "float" },
            Name = "GameChip",
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0),
            Padding = new Thickness(10, 3, 4, 3),
            CornerRadius = new CornerRadius(999),
            Child = UiKit.Row(6, _gameChipText, _gameChipRetry, _gameChipComplete, catalog),
        };
        CanvasHost.Children.Add(_gameChip);
        Session.DocumentReplaced += () =>
        {
            if (_openingGameImage) return;
            // Another picture was opened or started: that isn't a game image.
            GameImage = null;
            _gameDocument = null;
            _pngPending = false;
            RefreshGameChip();
        };
        Session.StateChanged += RefreshGameChip;
    }

    private void RefreshGameChip()
    {
        if (_gameChip is null) return;
        bool on = EditingGameImage && !CatalogShown;
        _gameChip.IsVisible = on;
        if (!on || GameImage is not { } img) return;
        _gameChipText.Text = _pngPending ? $"{img.FileName} — PNG not updated" : $"{img.FileName} — {StatusText(img.Status)}";
        _gameChipRetry.IsVisible = _pngPending;
        _gameChipComplete.Content = UiKit.Content(img.Status == GameImageStatus.Complete ? "undo" : "check",
            img.Status == GameImageStatus.Complete ? "Mark in progress" : "Mark complete");
        _gameChip.Margin = new Thickness(0, RulersShown ? 12 + RulerBar.Size : 12, 12, 0);
    }

    public static string StatusText(GameImageStatus s) => s switch
    {
        GameImageStatus.InProgress => "In progress",
        GameImageStatus.Complete => "Complete",
        _ => "Not started",
    };

    private void ToggleCurrentComplete()
    {
        if (Game is not { } game || GameImage is not { } img) return;
        try
        {
            if (img.Status == GameImageStatus.Complete) game.MarkInProgress(img);
            else game.MarkComplete(img);
            ShowToast($"{img.FileName} — {StatusText(img.Status)}");
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't save the progress: {ex.Message}");
        }
        RefreshGameStatus();
    }

    /// <summary>After a status change: the chip and the catalog.</summary>
    internal void RefreshGameStatus()
    {
        RefreshGameChip();
        _catalog?.Refresh();
    }

    // ================================================================== entering

    /// <summary>File › Game Edit Mode…: the remembered game folder's catalog, or a folder to choose.</summary>
    public async Task EnterGameEditModeAsync()
    {
        if (Game is not null)
        {
            ShowCatalog();
            return;
        }
        if (_settings.GameFolder is { } remembered && Directory.Exists(remembered) && GameProject.IsKnown(remembered))
        {
            await OpenGameFolderAsync(remembered);
            return;
        }
        await ChooseGameFolderAsync();
    }

    /// <summary>Picks a game graphics folder (also the catalog's "Open another folder…").</summary>
    public async Task ChooseGameFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder with your game's graphics",
            AllowMultiple = false,
            SuggestedStartLocation = _settings.GameFolder is { } last && Directory.Exists(last) ? await StorageProvider.TryGetFolderFromPathAsync(last) : null,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        await OpenGameFolderAsync(path);
    }

    /// <summary>Opens a game folder (asking about backups the first time) and shows its catalog.</summary>
    public async Task<bool> OpenGameFolderAsync(string path)
    {
        GameProject project;
        try
        {
            project = GameProject.Open(path);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the game folder", ex.Message);
            return false;
        }
        if (!GameProject.IsKnown(path))
        {
            bool backup = true;
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(DialogHost.Text(
                $"DoodleNook found {project.Images.Count} PNG image{(project.Images.Count == 1 ? "" : "s")} in “{project.Name}” and its subfolders.\n\n" +
                $"Ctrl+S saves your editable version in a “{GameProject.WorkFolderName}” folder inside it and replaces the game's PNG in place, " +
                "so your game uses the new picture straight away."));
            var (check, _) = UiKit.Check("Back up original PNG files before replacing them", "Each original is copied once, just before it is first replaced",
                () => backup, v => backup = v);
            check.Name = "GameBackupCheck";
            body.Children.Add(check);
            body.Children.Add(DialogHost.Text($"Originals are kept in {GameProject.WorkFolderName}/{GameProject.OriginalsFolderName}, in the same folders as the game's."));
            var r = await _dialogs.ShowAsync("Game Edit Mode", body, [("cancel", "Cancel", false), ("start", "Start", true)], "start", "cancel", 460);
            if (r != "start") return false;
            try
            {
                project.SetBackupOriginals(backup);
            }
            catch (Exception ex)
            {
                await _dialogs.ShowMessageAsync("Couldn't start Game Edit Mode", $"DoodleNook couldn't write its folder inside “{project.Name}”: {ex.Message}");
                return false;
            }
        }
        if (project.LoadProblem is { } problem) await _dialogs.ShowMessageAsync("Progress file", problem);
        Game = project;
        _settings.GameFolder = project.Root;
        if (GameImage is { } open)
        {
            if (project.Find(open.RelativePath) is { } same && string.Equals(open.PngPath, same.PngPath, StringComparison.OrdinalIgnoreCase))
                GameImage = same; // the same folder again: keep editing, with this project's image
            else
            {
                GameImage = null; // another folder: the open picture is no longer one of its images
                _gameDocument = null;
            }
        }
        ShowCatalog();
        return true;
    }

    /// <summary>Leaves Game Edit Mode: the catalog closes and Ctrl+S saves as usual again (the picture stays open).</summary>
    public void LeaveGameEditMode()
    {
        HideCatalog();
        Game = null;
        GameImage = null;
        _gameDocument = null;
        _pngPending = false;
        RefreshGameChip();
        ShowToast("Game Edit Mode closed · File › Game Edit Mode… opens it again");
    }

    // ================================================================== the catalog

    public void ShowCatalog()
    {
        if (Game is null) return;
        Session.EndStroke();
        Session.CommitTransform();
        if (_catalog is null)
        {
            _catalog = new GameCatalogView(this);
            CanvasHost.Children.Add(_catalog);
        }
        Game.Rescan();
        _catalog.Show(Game);
        RefreshGameChip();
    }

    public void HideCatalog()
    {
        if (_catalog is null) return;
        _catalog.IsVisible = false;
        RefreshGameChip();
        Canvas.Focus();
    }

    // ================================================================== opening an image

    /// <summary>Opens a game image: its editable .dnk when there is one, otherwise the PNG as a new picture.</summary>
    public async Task<bool> OpenGameImageAsync(GameImage image)
    {
        if (Game is not { } game) return false;
        if (EditingGameImage && ReferenceEquals(GameImage, image))
        {
            HideCatalog();
            return true;
        }
        if (!await ConfirmDiscardAsync("opening another image")) return false;
        bool fromDnk = image.HasDnk;
        if (fromDnk && game.ChangedOutside(image))
        {
            var choice = await _dialogs.ShowAsync($"“{image.FileName}” was changed elsewhere",
                DialogHost.Text($"The PNG was changed by another program after you last saved it here, so your editable version ({Path.GetFileName(image.DnkPath)}) " +
                                "doesn't have those changes.\n\nStart from the changed PNG (saving then replaces your editable version), or open your editable version?"),
                [("cancel", "Cancel", false), ("png", "Open the PNG", false), ("dnk", "Open my editable version", true)], "dnk", "cancel", 480);
            if (choice is null or "cancel") return false;
            fromDnk = choice == "dnk";
        }
        Document doc;
        try
        {
            doc = fromDnk ? DnkFile.Load(image.DnkPath) : Document.FromImage(ImageFiles.Decode(image.PngPath), ImageFiles.LayerNameFor(image.PngPath));
        }
        catch (Exception ex) when (fromDnk)
        {
            var choice = await _dialogs.ShowAsync("Couldn't open the editable version",
                DialogHost.Text($"{Path.GetFileName(image.DnkPath)} couldn't be read: {ex.Message}\n\nYou can open the PNG instead. The unreadable file is only replaced when you save."),
                [("cancel", "Cancel", false), ("png", "Open the PNG", true)], "png", "cancel", 460);
            if (choice != "png") return false;
            try
            {
                doc = Document.FromImage(ImageFiles.Decode(image.PngPath), ImageFiles.LayerNameFor(image.PngPath));
            }
            catch (Exception ex2)
            {
                await _dialogs.ShowMessageAsync("Couldn't open the image", $"{image.FileName}: {ex2.Message}");
                return false;
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the image", $"{image.FileName}: {ex.Message}");
            return false;
        }
        var remapTo = await PrepareGamePaletteAsync(game, image, doc); // the palette rule, before the picture is shown
        Animator.Stop();
        _openingGameImage = true;
        try
        {
            GameImage = image;
            _gameDocument = doc;
            _pngPending = false;
            _templateInPlace = false;
            Session.ReplaceDocument(doc, image.DnkPath);
        }
        finally
        {
            _openingGameImage = false;
        }
        HideCatalog();
        ShowToast($"{image.RelativePath} · {(fromDnk ? "your editable version" : "the game's PNG")} · {doc.Width} × {doc.Height} · Ctrl+S saves both");
        if (remapTo is not null) await OfferGameRemapAsync(image, remapTo);
        return true;
    }

    // ================================================================== saving

    /// <summary>Ctrl+S for a game image: the editable .dnk first, then (backing up the original the first time) the PNG
    /// in place. Only a complete save moves Not started to In progress.</summary>
    private async Task<bool> SaveGameImageAsync()
    {
        if (Game is not { } game || GameImage is not { } image) return false;
        if (Session.IsStrokeActive) Session.EndStroke();
        Session.CommitTransform();
        if (!await ResolveGamePaletteBeforeSaveAsync(game, image)) return false; // never saved as if it kept to its palette
        if (!_templateInPlace && game.ReplacementFor(image) is { } replacement)
        {
            var answer = await _dialogs.ShowAsync("Create a replacement without the 'template_' prefix?",
                DialogHost.Text($"Yes keeps {image.FileName} as it is (the game's fallback) and saves your picture as {replacement.FileName} in the same folder; " +
                                $"later saves go there.\n\nNo saves as usual and replaces {image.FileName}."),
                [("cancel", "Cancel", false), ("no", "No — Save normally", false), ("yes", "Yes — Create replacement", true)], "yes", "cancel", 520);
            if (answer is null or "cancel") return false;
            if (answer == "yes") return await SaveAsReplacementAsync(game, image, replacement);
            _templateInPlace = true; // not asked again while this image is open
        }
        bool replacePng = true;
        if (game.ChangedOutside(image))
        {
            var choice = await _dialogs.ShowAsync($"“{image.FileName}” was changed elsewhere",
                DialogHost.Text("The PNG was changed by another program after you last saved it here. Replacing it loses those changes."),
                [("cancel", "Cancel", false), ("dnk", "Save my editable version only", false), ("replace", "Replace the PNG", true)], "replace", "cancel", 480);
            if (choice is null or "cancel") return false;
            replacePng = choice == "replace";
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(image.DnkPath)!);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't save", $"The folder for {Path.GetFileName(image.DnkPath)} couldn't be made: {ex.Message}");
            return false;
        }
        if (!await SaveToPathAsync(image.DnkPath)) return false; // reports its own problem; nothing else happens
        if (!replacePng)
        {
            ShowToast($"Saved {Path.GetFileName(image.DnkPath)} · {image.FileName} was left as it is");
            return true;
        }
        return await ReplaceGamePngAsync(game, image);
    }

    /// <summary>"Yes — Create replacement": the template_ PNG is left exactly as it is; the editable version is saved as the
    /// replacement's .dnk and the picture written to the replacement's PNG (asking first if either exists, and backing up an
    /// existing PNG by the usual rule). From then on this picture is the replacement, so Ctrl+S simply saves it.</summary>
    private async Task<bool> SaveAsReplacementAsync(GameProject game, GameImage template, GameImage replacement)
    {
        bool png = File.Exists(replacement.PngPath), dnk = replacement.HasDnk;
        if (png || dnk)
        {
            string dnkName = Path.GetFileName(replacement.DnkPath);
            var overwrite = await _dialogs.ShowAsync($"“{(png ? replacement.FileName : dnkName)}” already exists",
                DialogHost.Text("Replace it with this picture?" + (png && dnk ? $" Its editable version ({dnkName}) is replaced too." : "") +
                                (png && game.BackupOriginals && !File.Exists(replacement.BackupPath) ? $" The current {replacement.FileName} is backed up first." : "") +
                                $"\n\n{template.FileName} stays as it is."),
                [("cancel", "Cancel", false), ("replace", "Replace", true)], "replace", "cancel", 460);
            if (overwrite != "replace") return false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(replacement.DnkPath)!);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't save", $"The folder for {Path.GetFileName(replacement.DnkPath)} couldn't be made: {ex.Message}");
            return false;
        }
        if (!await SaveToPathAsync(replacement.DnkPath)) return false; // still the template; nothing else happens
        GameImage = replacement;
        _pngPending = false;
        RefreshGameChip();
        if (!await ReplaceGamePngAsync(game, replacement)) return false;
        ShowToast($"Created {replacement.FileName} · {template.FileName} stays as it is · {StatusText(replacement.Status)}");
        return true;
    }

    private async Task<bool> ReplaceGamePngAsync(GameProject game, GameImage image)
    {
        while (true)
        {
            string step = "back up the original";
            try
            {
                game.BackUpOriginal(image);
                step = "write the PNG";
                ImageFiles.Export(Session.Document, image.PngPath, ExportFormat.Png); // a temporary file, then replace
                step = "update the progress file";
                game.RecordSaved(image);
                _pngPending = false;
                RefreshGameStatus();
                _catalog?.LoadThumbnails(); // the new picture
                ShowToast($"Saved {image.FileName} and its editable version · {StatusText(image.Status)}");
                return true;
            }
            catch (Exception ex)
            {
                _pngPending = step != "update the progress file";
                RefreshGameChip();
                var choice = await _dialogs.ShowAsync($"Couldn't replace {image.FileName}",
                    DialogHost.Text($"Your editable version ({Path.GetFileName(image.DnkPath)}) is saved. DoodleNook couldn't {step}: {ex.Message}" +
                                    (step == "back up the original" ? "\n\nThe game's PNG was not changed (it is only replaced after its original is backed up)." :
                                        step == "write the PNG" ? "\n\nThe game's PNG was not changed." : "")),
                    [("later", "Later", false), ("retry", "Try again", true)], "retry", "later", 480);
                if (choice != "retry") return false;
            }
        }
    }

    private async Task RetryPngAsync()
    {
        if (Game is { } game && GameImage is { } image && EditingGameImage) await ReplaceGamePngAsync(game, image);
    }
}
