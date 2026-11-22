using DoodleNook.App.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

using SelectionMode = DoodleNook.Engine.Documents.SelectionMode;

namespace DoodleNook.App;

public partial class MainWindow
{
    // ================================================================== history

    public void Undo()
    {
        if (CancelCrop()) return;
        var name = Session.Document.History.NextUndoName;
        long start = PerformanceLog.Now();
        if (!Session.Undo()) return;
        PerformanceLog.Operation("Undo", PerformanceLog.Since(start));
        ShowToast($"Undid {name?.ToLowerInvariant()}");
    }

    public void Redo()
    {
        var name = Session.Document.History.NextRedoName;
        long start = PerformanceLog.Now();
        if (!Session.Redo()) return;
        PerformanceLog.Operation("Redo", PerformanceLog.Since(start));
        ShowToast($"Redid {name?.ToLowerInvariant()}");
    }

    // ================================================================== view

    public void ToggleFlipView()
    {
        Canvas.ToggleFlip();
        ShowToast(Canvas.View.FlipHorizontal ? "View flipped (the picture itself is unchanged)" : "View back to normal");
    }

    public void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen) WindowState = _stateBeforeFullScreen;
        else
        {
            _stateBeforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
            ShowToast("Full screen — F11 or Esc to leave");
        }
    }

    public void ToggleDiagnostics()
    {
        _diagnosticsPanel.IsVisible = !_diagnosticsPanel.IsVisible;
        _diagnostics.SetActive(_diagnosticsPanel.IsVisible);
        UpdateOverlayPositions();
    }

    // ================================================================== selection and transform

    public void FreeTransform()
    {
        Canvas.Input?.CancelCrop();
        if (Session.BeginTransform("Free transform"))
            ShowToast("Free transform · drag the handles · Enter applies, Esc cancels");
    }

    /// <summary>Edit › Liquify…: reshape the active layer by dragging on the canvas (Enter applies, Esc cancels).
    /// Text and shapes are only liquified after the artist agrees to turn them into pixels.</summary>
    public async Task<bool> StartLiquifyAsync()
    {
        Canvas.Input?.CancelCrop();
        Session.CommitTransform();
        var layer = Session.Document.ActiveLayer;
        if (layer.IsObject && !layer.Locked)
        {
            var kind = layer.Kind == Engine.Documents.LayerKind.Text ? "text" : "an editable shape";
            var r = await _dialogs.ShowAsync("Liquify works on pixels",
                DoodleNook.App.Dialogs.DialogHost.Text($"“{layer.Name}” is {kind}. Turn it into pixels to liquify it? It can't be edited as {kind} afterwards (Undo brings it back)."),
                [("cancel", "Cancel", false), ("convert", "Turn into pixels", true)], "convert", "cancel", 440);
            if (r != "convert") return false;
            Session.ConvertToPixels();
        }
        if (!Session.BeginLiquify()) return false;
        ShowToast("Liquify · drag to reshape · Enter applies, Esc cancels · Ctrl+Z takes back a stroke · Alt reverses");
        return true;
    }

    public void ApplyLiquify()
    {
        if (Session.ApplyLiquify()) ShowToast("Liquify applied");
    }

    public void CancelLiquify()
    {
        if (Session.CancelLiquify()) ShowToast("Liquify cancelled");
    }

    /// <summary>Edit › Perspective / Warp: a transform of the selection (or layer) in that mode straight away.</summary>
    public void TransformIn(Engine.Operations.TransformMode mode)
    {
        Canvas.Input?.CancelCrop();
        if (!Session.BeginTransform(mode == Engine.Operations.TransformMode.Warp ? "Warp" : "Perspective")) return;
        Session.SetTransformMode(mode);
        ShowToast(mode == Engine.Operations.TransformMode.Warp
            ? "Warp · drag the grid's points to bend · Enter applies, Esc cancels"
            : "Perspective · drag the corners · Enter applies, Esc cancels");
    }

    public void ApplyTransform()
    {
        if (Session.CommitTransform()) ShowToast("Transform applied");
    }

    public void CancelTransform()
    {
        if (Session.CancelTransform()) ShowToast("Transform cancelled");
    }

    public void FlipSelection(bool horizontal) =>
        Session.QuickTransform(t => t.Flip(horizontal), horizontal ? "Flip horizontal" : "Flip vertical");

    public void RotateSelection(double degrees) =>
        Session.QuickTransform(t => t.Rotate(degrees), "Rotate");

    /// <summary>Arrow keys with the Move tool: nudge the selection (or layer) by whole pixels.</summary>
    public void Nudge(int dx, int dy)
    {
        // Text and shapes move as objects (one undo step per nudge).
        if (Session.ActiveTransform is null && Session.ActiveObject is { } obj)
        {
            Session.Document.MoveObjects([obj], dx, dy, "Nudge");
            return;
        }
        Session.QuickTransform(t => t.SetTransform(t.Transform.Then(Affine.Translation(dx, dy))), "Nudge");
    }

    public bool ApplyCrop()
    {
        if (Canvas.Input?.ApplyCrop() == true)
        {
            ShowToast($"Cropped to {Session.Document.Width} × {Session.Document.Height}");
            return true;
        }
        return false;
    }

    public bool CancelCrop()
    {
        if (Canvas.Input?.PendingCrop is null) return false;
        Canvas.Input.CancelCrop();
        return true;
    }

    public void CropToSelection()
    {
        bool ok = false;
        Session.RunImageOperation(d => ok = ImageOperations.CropToSelection(d));
        ShowToast(ok ? $"Cropped to {Session.Document.Width} × {Session.Document.Height}" : "Select an area first, then crop");
    }

    public void SelectLayerPixels() =>
        Session.ApplySelection(Selections.LayerPixels(Session.Document, Session.Document.ActiveLayer), "Select layer pixels", SelectionMode.Replace);

    public void ClearSelectionOrLayer()
    {
        bool hadSelection = Session.Document.Selection is { IsEmpty: false };
        if (Session.ClearSelectionOrLayer())
            ShowToast(hadSelection ? "Selection cleared — Ctrl+Z brings it back" : $"Layer “{Session.Document.ActiveLayer.Name}” cleared — Ctrl+Z brings it back");
    }

    // ================================================================== image

    public void RotateImage(int quarterTurns)
    {
        Session.RunImageOperation(d => ImageOperations.Rotate(d, quarterTurns));
        ShowToast("Picture rotated");
    }

    public void FlipImage(bool horizontal)
    {
        Session.RunImageOperation(d => ImageOperations.Flip(d, horizontal));
        ShowToast(horizontal ? "Picture flipped left to right" : "Picture flipped upside down");
    }

    public async Task ImageSizeAsync()
    {
        var d = Session.Document;
        var r = await _dialogs.AskImageSizeAsync(d.Width, d.Height, Session.PixelMode);
        if (r is not { } n) return;
        Session.RunImageOperation(doc => ImageOperations.ResizeImage(doc, n.Width, n.Height, n.Mode));
        ShowToast($"Picture is now {n.Width} × {n.Height}");
    }

    public async Task CanvasSizeAsync()
    {
        var d = Session.Document;
        var r = await _dialogs.AskCanvasSizeAsync(d.Width, d.Height);
        if (r is not { } n) return;
        Session.RunImageOperation(doc => ImageOperations.ResizeCanvas(doc, n.Width, n.Height, n.AnchorX, n.AnchorY));
        ShowToast($"Canvas is now {n.Width} × {n.Height}");
    }

    public async Task AdjustAsync(AdjustKind kind)
    {
        Session.EndStroke();
        Session.CommitTransform();
        if (!Session.CanEditActiveLayer()) return;
        var op = new AdjustmentOperation(Session.Document, Session.Document.ActiveLayer);
        if (!op.HasPixels)
        {
            ShowToast("This layer is empty here — nothing to adjust");
            return;
        }
        var result = await _dialogs.AskAdjustmentAsync(kind, a =>
        {
            if (a is null) op.Cancel();
            else op.Preview(a);
        });
        if (result is null)
        {
            op.Cancel();
            return;
        }
        op.Commit(result);
        ShowToast($"{result.Name} applied");
    }

    public void ApplyAdjustment(Adjustment adjustment)
    {
        Session.EndStroke();
        Session.CommitTransform();
        if (!Session.CanEditActiveLayer()) return;
        var op = new AdjustmentOperation(Session.Document, Session.Document.ActiveLayer);
        if (!op.HasPixels)
        {
            ShowToast("This layer is empty here — nothing to adjust");
            return;
        }
        if (op.Commit(adjustment)) ShowToast($"{adjustment.Name} applied");
    }

    public void ClearLayer()
    {
        Session.EndStroke();
        if (!Session.CanEditActiveLayer()) return;
        if (Session.Document.Clear()) ShowToast($"Layer “{Session.Document.ActiveLayer.Name}” cleared — Ctrl+Z brings it back");
    }

    // ================================================================== files

    private static readonly FilePickerFileType DnkType = new("DoodleNook picture (layers)")
    {
        Patterns = ["*.dnk"],
        MimeTypes = [DnkFile.MimeType],
    };

    private static readonly FilePickerFileType PngType = new("PNG image") { Patterns = ["*.png"], MimeTypes = ["image/png"], AppleUniformTypeIdentifiers = ["public.png"] };
    private static readonly FilePickerFileType JpegType = new("JPEG image") { Patterns = ["*.jpg", "*.jpeg"], MimeTypes = ["image/jpeg"], AppleUniformTypeIdentifiers = ["public.jpeg"] };
    private static readonly FilePickerFileType WebPType = new("WebP image") { Patterns = ["*.webp"], MimeTypes = ["image/webp"] };

    private static readonly FilePickerFileType AnyPicture = new("Pictures (.dnk, .png, .jpg, .webp, .bmp, .gif)")
    {
        Patterns = ["*.dnk", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"],
    };

    private static readonly FilePickerFileType AnyImage = new("Images (.png, .jpg, .webp, .bmp, .gif)")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"],
    };

    private static FilePickerFileType TypeFor(ExportFormat f) => f switch
    {
        ExportFormat.Jpeg => JpegType,
        ExportFormat.WebP => WebPType,
        _ => PngType,
    };

    private async Task<bool> ConfirmDiscardAsync(string beforeWhat)
    {
        if (Session.IsStrokeActive) Session.EndStroke();
        Session.CommitTransform();
        if (!Session.Document.IsDirty) return true;
        Canvas.InterruptGesture();
        var choice = await _dialogs.AskSaveChangesAsync(Session.DisplayName, beforeWhat);
        if (choice is null) return false;
        if (choice == "save") return await SaveAsync();
        return true;
    }

    public async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync("starting a new picture")) return;
        if (Workspace.Kind == WorkspaceKind.Animation)
        {
            // In Animation Studio, "new" means a new animation (the dialog has the drawing style too).
            bool pixelArt = PixelAnimation;
            var c = await _dialogs.AskNewAnimationAsync(pixelArt, pixelArt ? 64 : 1280, pixelArt ? 64 : 720);
            if (c is not null) CreateNewAnimation(c);
            return;
        }
        var doc = Session.Document;
        bool pixel = Workspace.Kind == WorkspaceKind.PixelArt;
        int w = pixel && doc.Width > 512 ? 32 : doc.Width, h = pixel && doc.Height > 512 ? 32 : doc.Height;
        var r = await _dialogs.AskNewDocumentAsync(w, h, white: !pixel && (doc.Paper is not null || Session.FilePath is null), pixelArt: pixel);
        if (r is not { } n) return;
        Session.ReplaceDocument(new Document(n.Width, n.Height, n.Background, pixel ? "Sprite" : "Sketch"), null);
        ShowToast($"New picture · {n.Width} × {n.Height}");
    }

    public async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a picture (or a storybook)",
            AllowMultiple = false,
            FileTypeFilter = [AnyPicture, DnkType, PngType, JpegType, WebPType, StorybookType, FilePickerFileTypes.All],
        });
        if (files.Count == 0) return;
        var file = files[0];
        // A storybook opens in Storybook Studio; the picture being edited stays open.
        if (file.TryGetLocalPath() is { } book && book.EndsWith(Engine.Storybook.StorybookProject.Extension, StringComparison.OrdinalIgnoreCase))
        {
            await OpenStorybookFromAsync(book);
            return;
        }
        if (!await ConfirmDiscardAsync("opening another picture")) return;
        if (file.TryGetLocalPath() is { } path)
        {
            await OpenPathAsync(path);
            return;
        }
        try
        {
            long start = PerformanceLog.Now();
            await using var stream = await file.OpenReadAsync();
            var doc = file.Name.EndsWith(DnkFile.Extension, StringComparison.OrdinalIgnoreCase)
                ? DnkFile.Read(stream)
                : Document.FromImage(ImageFiles.Decode(stream), ImageFiles.LayerNameFor(file.Name));
            Animator.Stop();
            Session.ReplaceDocument(doc, null);
            PerformanceLog.Operation("Open", PerformanceLog.Since(start));
            ShowToast($"Opened {file.Name}");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the picture", $"{file.Name}: {ex.Message}");
        }
    }

    /// <summary>Opens a picture from a path (menu, command line or smoke test).</summary>
    public async Task<bool> OpenPathAsync(string path)
    {
        try
        {
            long start = PerformanceLog.Now();
            var doc = ImageFiles.Open(path);
            Animator.Stop();
            Session.ReplaceDocument(doc, path);
            PerformanceLog.Operation("Open", PerformanceLog.Since(start));
            string hint = doc.IsAnimated && Workspace.Kind != WorkspaceKind.Animation ? " · Alt+6 opens Animation Studio" : "";
            ShowToast($"Opened {Path.GetFileName(path)} · {doc.Width} × {doc.Height}{(doc.Layers.Count > 1 ? $" · {doc.Layers.Count} layers" : "")}{(doc.IsAnimated ? $" · {doc.FrameCount} frames" : "")}{hint}");
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the picture", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ctrl+S. A DoodleNook picture is saved in place. A one-layer picture opened from a PNG is saved back to that PNG
    /// (as in Mission 004). Anything with layers is never flattened silently: it goes to "Save as" a .dnk.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (EditingGameImage) return await SaveGameImageAsync(); // the .dnk and the game's PNG
        var path = Session.FilePath;
        if (path is not null && ImageFiles.IsProject(path)) return await SaveToPathAsync(path);
        // Text, shapes, masks, guides… only survive in a .dnk, like layers: never flattened into a PNG by Ctrl+S.
        bool flat = Session.Document.Layers.Count == 1 && !Session.Document.UsesDesignFeatures;
        if (path is not null && ImageFiles.FormatForPath(path) == ExportFormat.Png && flat)
            return await SaveToPathAsync(path);
        if (path is not null && !flat)
            ShowToast("This picture has layers, text or shapes now — save it as a DoodleNook picture (.dnk) to keep them");
        return await SaveAsAsync();
    }

    public async Task<bool> SaveAsAsync()
    {
        if (Session.IsStrokeActive) Session.EndStroke();
        Session.CommitTransform();
        bool single = Session.Document.Layers.Count == 1 && !Session.Document.UsesDesignFeatures;
        var suggested = Path.ChangeExtension(Session.DisplayName, DnkFile.Extension);
        IStorageFolder? folder = null;
        if (Session.FilePath is { } current && Path.GetDirectoryName(current) is { } dir)
            folder = await StorageProvider.TryGetFolderFromPathAsync(dir);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save picture",
            SuggestedFileName = suggested,
            SuggestedStartLocation = folder,
            DefaultExtension = "dnk",
            // PNG is only offered when there is a single layer, so a layered picture is never flattened by saving.
            FileTypeChoices = single ? [DnkType, PngType] : [DnkType],
            ShowOverwritePrompt = true,
        });
        if (file is null) return false; // cancelled: nothing changes
        if (file.TryGetLocalPath() is { } path)
        {
            if (!single || ImageFiles.FormatForPath(path) != ExportFormat.Png)
            {
                if (!ImageFiles.IsProject(path)) path = Path.ChangeExtension(path, DnkFile.Extension);
            }
            return await SaveToPathAsync(path);
        }
        try
        {
            await using var stream = await file.OpenWriteAsync();
            DnkFile.Write(Session.Document, stream, Version);
            Session.Document.MarkSaved();
            ShowToast($"Saved {file.Name}");
            NoteSaved();
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't save", ex.Message);
            return false;
        }
    }

    /// <summary>Saves to a path: .dnk keeps everything; .png is only used for one-layer pictures.</summary>
    public async Task<bool> SaveToPathAsync(string path)
    {
        if (Session.IsStrokeActive) Session.EndStroke();
        Session.CommitTransform();
        try
        {
            if (ImageFiles.IsProject(path))
            {
                await SaveProjectInBackgroundAsync(path);
                ShowToast($"Saved {Path.GetFileName(path)}");
                NoteSaved();
                return true;
            }
            if (Session.Document.Layers.Count == 1 && ImageFiles.FormatForPath(path) is { } f) ImageFiles.Export(Session.Document, path, f);
            else throw new InvalidOperationException("Pictures with layers are saved as .dnk; use File › Export for a flat copy.");
            Session.MarkSaved(path);
            ShowToast($"Saved {Path.GetFileName(path)}");
            NoteSaved();
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't save", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>File › Export: a flattened copy as PNG, JPEG or WebP. The open picture (and its layers) is not changed.</summary>
    public async Task<bool> ExportAsync(ExportFormat format)
    {
        if (Session.IsStrokeActive) Session.EndStroke();
        Session.CommitTransform();
        var doc = Session.Document;
        bool transparent = doc.Paper is null || !doc.PaperVisible;
        var quality = await _dialogs.AskExportAsync(format, doc.Layers.Count, transparent);
        if (quality is not { } q) return false;
        var suggested = Path.ChangeExtension(Session.DisplayName, ImageFiles.Extension(format));
        IStorageFolder? folder = null;
        if (Session.FilePath is { } current && Path.GetDirectoryName(current) is { } dir)
            folder = await StorageProvider.TryGetFolderFromPathAsync(dir);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as {ImageFiles.DisplayName(format)}",
            SuggestedFileName = suggested,
            SuggestedStartLocation = folder,
            DefaultExtension = ImageFiles.Extension(format).TrimStart('.'),
            FileTypeChoices = [TypeFor(format)],
            ShowOverwritePrompt = true,
        });
        if (file is null) return false;
        try
        {
            if (file.TryGetLocalPath() is { } path)
            {
                if (ImageFiles.FormatForPath(path) != format) path += ImageFiles.Extension(format);
                ExportToPath(path, format, q);
            }
            else
            {
                var flat = doc.Flatten();
                if (!ImageFiles.SupportsTransparency(format)) flat = ImageFiles.OnMatte(flat, ColorRgb.White);
                await using var stream = await file.OpenWriteAsync();
                ImageFiles.Encode(flat, stream, format, q);
            }
            ShowToast($"Exported {file.Name}");
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't export", ex.Message);
            return false;
        }
    }

    /// <summary>Exports without asking (smoke test, tests).</summary>
    public void ExportToPath(string path, ExportFormat format, int quality = 92) => ImageFiles.Export(Session.Document, path, format, quality);

    /// <summary>File › Place image: adds an image file as a new layer, scaled down to fit if needed, ready to transform.</summary>
    public async Task PlaceImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Place an image as a new layer",
            AllowMultiple = false,
            FileTypeFilter = [AnyImage, FilePickerFileTypes.All],
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            PlaceImage(ImageFiles.Decode(stream), ImageFiles.LayerNameFor(files[0].Name));
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't place the image", $"{files[0].Name}: {ex.Message}");
        }
    }

    public void PlaceImage(RasterImage image, string name)
    {
        var doc = Session.Document;
        image = FitToCanvas(image);
        Session.PlaceImage(image, name);
        if (Session.BeginTransform("Place image"))
            ShowToast(doc.PaletteLocked
                ? $"Placed “{name}” with its colours matched to “{doc.Palette.Name}” (transparency kept) · Enter to finish"
                : $"Placed “{name}” on a new layer · drag the handles, Enter to finish");
    }

    /// <summary>A picture larger than the canvas, scaled down to fit it (pixel art keeps hard pixels); others as they are.</summary>
    private RasterImage FitToCanvas(RasterImage image)
    {
        var doc = Session.Document;
        if (image.Width <= doc.Width && image.Height <= doc.Height) return image;
        double s = Math.Min((double)doc.Width / image.Width, (double)doc.Height / image.Height);
        return Resampler.Resize(image, Math.Max(1, (int)(image.Width * s)), Math.Max(1, (int)(image.Height * s)),
            Session.PixelMode ? Interpolation.Nearest : Interpolation.Bicubic);
    }
}
