using Avalonia.Platform.Storage;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App;

/// <summary>
/// Mission 015 — the Sprite Sheet Workshop: File › Import sprite sheet… (a sheet sliced into an animation, plain or as the
/// Mission 012 reference to redraw) and File › Sprite sheet from pictures… (numbered pictures laid out on one sheet).
/// Exporting an animation as a sheet is File › Export animation… (Mission 006). Source files are only ever read.
/// </summary>
public partial class MainWindow
{
    private static readonly FilePickerFileType SheetPictureType = new("Pictures (.png, .gif, .webp, .bmp, .jpg)")
    {
        Patterns = ["*.png", "*.gif", "*.webp", "*.bmp", "*.jpg", "*.jpeg"],
    };

    public async Task ImportSpriteSheetAsync()
    {
        Animator.Pause();
        Session.EndStroke();
        Session.CommitTransform();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a sprite sheet",
            AllowMultiple = false,
            FileTypeFilter = [SheetPictureType, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await ImportSpriteSheetFromAsync(path);
    }

    /// <summary>The sheet's own JSON when there is one beside it (walk.png → walk.json).</summary>
    internal static string? SheetJsonFor(string path)
    {
        var json = Path.ChangeExtension(path, ".json");
        try
        {
            return File.Exists(json) && new FileInfo(json).Length < 8_000_000 ? File.ReadAllText(json) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The whole sprite-sheet import for a chosen file (tests call this directly). True when an animation was opened.</summary>
    public async Task<bool> ImportSpriteSheetFromAsync(string path)
    {
        RasterImage sheet;
        try
        {
            sheet = await Task.Run(() => ImageFiles.Decode(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Couldn't import the sprite sheet", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
        if (!await ConfirmDiscardAsync("importing a sprite sheet")) return false;

        var json = SheetJsonFor(path);
        var fromJson = json is null ? null : SpriteSheetSlicer.FromJson(json, sheet.Width, sheet.Height);
        var suggestion = fromJson ?? SpriteSheetSlicer.Suggest(sheet);
        string? note = fromJson is not null ? $"The grid comes from {Path.GetFileName(Path.ChangeExtension(path, ".json"))} beside the sheet." : null;
        var choice = await SpriteSheetImportDialog.AskAsync(_dialogs, sheet, $"{Path.GetFileName(path)} · {sheet.Width} × {sheet.Height} px", suggestion, note,
            Bounds.Height - 170);
        if (choice is null) return false;

        var all = SpriteSheetSlicer.Slice(sheet, choice.Grid);
        var frames = choice.Keep.Select(i => all[i]).ToList();
        var doc = choice.AsReference ? SpriteSheetImport.ReferenceDocument(frames, choice.Fps) : SpriteSheetImport.NormalDocument(frames, choice.Fps);
        Animator.Stop();
        Session.ReplaceDocument(doc, null);
        AnimationState.PixelAnimation = choice.PixelArt;
        AnimationState.Tool = null;
        if (Workspace.Kind != WorkspaceKind.Animation) SetWorkspace(WorkspaceKind.Animation);
        else ApplyWorkspace();
        _timeline?.ShowRows(doc.Layers.Count);
        if (choice.AsReference) Session.Document.SetActiveLayer(Session.Document.Layers[1]); // Sketch
        Canvas.FitToView();
        string count = $"{doc.FrameCount} frame{(doc.FrameCount == 1 ? "" : "s")} of {doc.Width} × {doc.Height}";
        ShowToast(choice.AsReference
            ? $"Imported {count} · Reference is locked at 35 % · draw on Sketch, Outline and Coloring"
            : $"Imported {count} from {Path.GetFileName(path)}");
        return true;
    }

    // ================================================================== pictures → sheet

    public async Task SpriteSheetFromPicturesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pictures for a sprite sheet: one numbered picture is enough (its sequence is found), or select several",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("PNG pictures") { Patterns = ["*.png"] }, SheetPictureType, FilePickerFileTypes.All],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        await SpriteSheetFromPicturesAsync(paths, null);
    }

    /// <summary>The pictures a choice of files means: one file and its numbered sequence, or the chosen files in natural order.</summary>
    internal static List<string> PicturesFor(IReadOnlyList<string> paths)
    {
        if (paths.Count != 1) return AnimationImport.NaturalSort(paths);
        var folder = Path.GetDirectoryName(Path.GetFullPath(paths[0])) ?? ".";
        return AnimationImport.SequenceOf(paths[0], Directory.EnumerateFiles(folder));
    }

    /// <summary>
    /// Lays out the pictures and saves the sheet (tests pass <paramref name="savePath"/>; otherwise a save dialog asks).
    /// Returns the saved path, or null.
    /// </summary>
    public async Task<string?> SpriteSheetFromPicturesAsync(IReadOnlyList<string> paths, string? savePath)
    {
        List<string> files;
        List<RasterImage> pictures;
        try
        {
            files = PicturesFor(paths);
            pictures = await Task.Run(() => files.Select(f =>
            {
                try
                {
                    return ImageFiles.Decode(f);
                }
                catch (InvalidDataException ex)
                {
                    throw new InvalidDataException($"{Path.GetFileName(f)}: {ex.Message}");
                }
            }).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Couldn't read the pictures", ex.Message);
            return null;
        }
        var names = files.Select(f => Path.GetFileName(f)).ToList();
        var (cw, ch) = PicturesToSheet.CellSize(pictures);
        bool uneven = pictures.Any(p => p.Width != pictures[0].Width || p.Height != pictures[0].Height);
        string summary = (files.Count == 1 ? names[0] : $"{names[0]} … {names[^1]} · {files.Count} pictures") +
                         (uneven ? $" · sizes differ, largest {cw} × {ch}" : $" · {cw} × {ch} px");
        var choice = await PicturesToSheetDialog.AskAsync(_dialogs, pictures, names, summary, Bounds.Height - 170);
        if (choice is null) return null;

        var chosen = choice.Keep.Select(i => pictures[i]).ToList();
        var frames = PicturesToSheet.Frames(chosen, choice.Align);
        SpriteSheet sheet;
        try
        {
            sheet = AnimationExport.BuildSheet(frames, new SpriteSheetOptions(SheetLayout.Grid, choice.Columns, choice.Spacing, 0, choice.Margin));
        }
        catch (ArgumentException ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't make the sprite sheet", ex.Message);
            return null;
        }

        if (savePath is null)
        {
            var baseName = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(files[0]), @"[\s_\-.]*\d+$", "");
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the sprite sheet",
                SuggestedFileName = (string.IsNullOrWhiteSpace(baseName) ? "sheet" : baseName) + "-sheet.png",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
                ShowOverwritePrompt = true,
            });
            savePath = file?.TryGetLocalPath();
            if (savePath is null) return null;
        }
        if (files.Any(f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(savePath), StringComparison.OrdinalIgnoreCase)))
        {
            await _dialogs.ShowMessageAsync("Choose another name", "The sheet would replace one of its own pictures. The pictures are never changed: please choose another file name.");
            return null;
        }
        try
        {
            await Task.Run(() =>
            {
                AnimationExport.WriteAtomically(savePath, PngCodec.Encode(sheet.Image));
                if (choice.Json)
                    AnimationExport.WriteAtomically(Path.ChangeExtension(savePath, ".json"),
                        System.Text.Encoding.UTF8.GetBytes(AnimationExport.SheetJson(sheet, Path.GetFileName(savePath), 10, true)));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't save the sprite sheet", ex.Message);
            return null;
        }
        ShowToast($"Saved {Path.GetFileName(savePath)} · {sheet.Image.Width} × {sheet.Image.Height} · {frames.Count} frames");
        return savePath;
    }
}
