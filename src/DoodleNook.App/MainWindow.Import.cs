using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App;

/// <summary>
/// Mission 012 — File › Import Animation…: an animated GIF, an animated PNG, or numbered PNGs (choose one and its sequence
/// is found; or choose several), opened as a new four-layer animation ready for redrawing (Reference, Sketch, Outline,
/// Coloring). The source files are only read.
/// </summary>
public partial class MainWindow
{
    private static readonly FilePickerFileType AnimationSourceType = new("Animations and numbered pictures (.gif, .png, .apng)")
    {
        Patterns = ["*.gif", "*.png", "*.apng"],
        MimeTypes = ["image/gif", "image/png", "image/apng"],
    };

    public async Task ImportAnimationAsync()
    {
        Animator.Pause();
        Session.EndStroke();
        Session.CommitTransform();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import an animation: a GIF, an animated PNG, or numbered PNGs (one is enough, or select several)",
            AllowMultiple = true,
            FileTypeFilter = [AnimationSourceType, FilePickerFileTypes.All],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        await ImportAnimationFromAsync(paths);
    }

    /// <summary>What a choice of files means: one GIF or APNG, or a sequence of pictures (in natural order), with a summary.</summary>
    internal static (string Kind, IReadOnlyList<string> Files, string Summary) PlanImport(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1)
        {
            string p = paths[0], ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext == ".gif") return ("gif", paths, Path.GetFileName(p));
            byte[] head = new byte[4096];
            int read;
            using (var s = File.OpenRead(p)) read = s.Read(head, 0, head.Length);
            if (GifDecoder.IsGif(head.AsSpan(0, read))) return ("gif", paths, Path.GetFileName(p));
            if (ApngDecoder.IsAnimated(File.ReadAllBytes(p))) return ("apng", paths, Path.GetFileName(p) + " (animated PNG)");
            var folder = Path.GetDirectoryName(Path.GetFullPath(p)) ?? ".";
            var seq = AnimationImport.SequenceOf(p, Directory.EnumerateFiles(folder));
            return ("sequence", seq, SequenceSummary(seq));
        }
        var ordered = AnimationImport.NaturalSort(paths);
        return ("sequence", ordered, SequenceSummary(ordered));
    }

    private static string SequenceSummary(IReadOnlyList<string> files) =>
        files.Count == 1 ? Path.GetFileName(files[0]) + " (one picture: a one-frame animation)"
            : $"{Path.GetFileName(files[0])} … {Path.GetFileName(files[^1])} · {files.Count} pictures";

    /// <summary>The whole import for chosen files (tests call this directly). Returns true when an animation was opened.</summary>
    public async Task<bool> ImportAnimationFromAsync(IReadOnlyList<string> paths)
    {
        (string Kind, IReadOnlyList<string> Files, string Summary) plan;
        try
        {
            if (paths.Count > 1 && paths.Any(p => !Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase)))
            {
                await _dialogs.ShowMessageAsync("Import animation", "Choose one GIF or animated PNG, or one or more numbered PNG pictures.");
                return false;
            }
            plan = PlanImport(paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't import the animation", ex.Message);
            return false;
        }
        if (!await ConfirmDiscardAsync("importing an animation")) return false;

        var source = await ReadAnimationAsync(plan.Kind, plan.Files);
        if (source is null) return false;
        double fps = Session.Document.Animation?.Fps ?? Engine.Documents.Animation.DefaultFps;
        string summary = $"{plan.Summary} · {source.Frames.Count} frame{(source.Frames.Count == 1 ? "" : "s")} · {source.Width} × {source.Height}";
        var choice = await _dialogs.AskImportAnimationAsync(source, summary, fps);
        if (choice is null) return false;

        var doc = AnimationImport.BuildDocument(source, choice.Keep, choice.Fps);
        Animator.Stop();
        Session.ReplaceDocument(doc, null);
        AnimationState.PixelAnimation = choice.PixelArt;
        AnimationState.Tool = null;
        if (Workspace.Kind != WorkspaceKind.Animation) SetWorkspace(WorkspaceKind.Animation);
        else ApplyWorkspace();
        _timeline?.ShowRows(doc.Layers.Count);
        Session.Document.SetActiveLayer(Session.Document.Layers[1]); // Sketch
        Canvas.FitToView();
        ShowToast($"Imported {doc.FrameCount} frame{(doc.FrameCount == 1 ? "" : "s")} · Reference is locked at 35 % · draw on Sketch, Outline and Coloring");
        return true;
    }

    /// <summary>Reads the frames in the background. A progress card (with Cancel) appears if it takes more than a moment.</summary>
    private async Task<ImportedAnimation?> ReadAnimationAsync(string kind, IReadOnlyList<string> files)
    {
        using var cts = new CancellationTokenSource();
        var status = new TextBlock { Text = "Reading…", Name = "ImportProgressText" };
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, files.Count), Height = 6, Name = "ImportProgress" };
        var progress = new Progress<int>(n =>
        {
            status.Text = kind == "sequence" ? $"Reading picture {n} of {files.Count}…" : $"Reading frame {n}…";
            if (kind == "sequence") bar.Value = n;
            else bar.IsIndeterminate = true;
        });
        var work = Task.Run(() =>
        {
            switch (kind)
            {
                case "gif":
                    return GifDecoder.Decode(File.ReadAllBytes(files[0]), progress, cts.Token);
                case "apng":
                    return ApngDecoder.Decode(File.ReadAllBytes(files[0]), progress, cts.Token);
                default:
                {
                    var images = new List<RasterImage>(files.Count);
                    foreach (var f in files)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        try
                        {
                            images.Add(ImageFiles.Decode(f));
                        }
                        catch (InvalidDataException ex)
                        {
                            throw new InvalidDataException($"{Path.GetFileName(f)}: {ex.Message}");
                        }
                        ((IProgress<int>)progress).Report(images.Count);
                    }
                    return AnimationImport.FromImages(images);
                }
            }
        }, cts.Token);

        Task<string?>? card = null;
        if (await Task.WhenAny(work, Task.Delay(300)) != work)
        {
            var body = new StackPanel { Spacing = 10, Width = 360 };
            body.Children.Add(status);
            body.Children.Add(bar);
            card = _dialogs.ShowAsync("Import animation", body, [("cancel", "Cancel", false)], null, "cancel");
            if (await Task.WhenAny(work, card) == card && !work.IsCompleted) cts.Cancel();
        }
        try
        {
            var result = await work;
            if (card is { IsCompleted: false }) _dialogs.Close("done");
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (card is { IsCompleted: false }) _dialogs.Close("done");
            await _dialogs.ShowMessageAsync("Couldn't import the animation", ex.Message);
            return null;
        }
    }
}
