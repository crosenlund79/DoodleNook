using DoodleNook.App.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DoodleNook.App.Animation;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;

namespace DoodleNook.App;

/// <summary>
/// Animation Studio's commands and menus, Customize workspace, background saving, autosave and crash recovery.
/// </summary>
public partial class MainWindow
{
    private AnimationPreview _animationPreview = null!;
    private RecoveryStore? _recovery;
    private readonly DispatcherTimer _autosaveTimer = new(DispatcherPriority.Background);
    private Task? _saveTask;
    private Task? _autosaveTask;
    private Document? _autosavedDocument;
    private long _autosavedState = -1;

    /// <summary>Playback and onion skins (Animation Studio).</summary>
    public AnimationController Animator { get; private set; } = null!;

    /// <summary>Animation Studio's remembered layout (the timeline and preview read it from any workspace).</summary>
    internal WorkspaceState AnimationState => _settings.StateFor(WorkspaceKind.Animation);

    internal AnimationPreview AnimationPreviewPanel => _animationPreview;

    /// <summary>Called by the constructor before the first workspace is applied.</summary>
    private void InitAnimation()
    {
        Animator = new AnimationController(Session);
        _animationPreview = new AnimationPreview(this) { IsVisible = false };
        CanvasHost.Children.Add(_animationPreview);
        Canvas.GestureStarted += _ => Animator.Pause();
        _autosaveTimer.Tick += async (_, _) => await AutosaveNowAsync();
    }

    // ================================================================== menus

    private IEnumerable<MenuEntry> AnimationMenu()
    {
        var s = Session;
        var d = s.Document;
        var a = d.Animation;
        bool animated = a is not null;
        int f = d.CurrentFrame, n = d.FrameCount;
        var layer = d.ActiveLayer;
        var (hs, he) = d.HoldRange(layer, f);
        bool held = d.CelAt(layer, f) is not null && he > hs;
        bool playing = Animator.IsPlaying;
        bool here = Workspace.Kind == WorkspaceKind.Animation;
        return
        [
            new(playing ? "pause" : "play", playing ? "Pause" : "Play", "Enter", Animator.Toggle, animated && n > 1 || playing),
            new("stop", "Stop (back to the start frame)", "Esc", Animator.Stop, playing),
            new("chevl", "Previous frame", ", or ←", () => s.StepFrame(-1), animated && n > 1),
            new("chevr", "Next frame", ". or →", () => s.StepFrame(+1), animated && n > 1),
            MenuEntry.Separator,
            new("newframe", "New frame", "N", () => s.NewFrame(duplicate: false)),
            new("dupframe", "Duplicate frame", "Shift+N", () => s.NewFrame(duplicate: true)),
            new("plus", "Insert blank frame before", null, () => s.InsertFrame(f, duplicate: false)),
            new("trash", "Delete frame", null, () => s.DeleteFrame(f), animated && n > 1),
            new("clock", "Frame duration…", null, () => _ = FrameDurationAsync(f)),
            new("copy", "Make drawing independent", null, () => s.MakeIndependent(layer, f), held),
            new(null, "More: right-click the timeline", null, null, false),
            MenuEntry.Separator,
            new("onion", "Onion skin", "O", s.ToggleOnion, Checked: s.Onion.Enabled),
            new(null, "Onion-skin settings…", null, () => _timeline?.OpenOnionSettings(), here),
            new("loop", "Frame rate and looping…", null, () => _timeline?.OpenPlaybackSettings(), here),
            new("pixpencil", "Pixel-art animation", null, () => SetPixelAnimation(!AnimationState.PixelAnimation), Checked: AnimationState.PixelAnimation),
            new("film", "Preview panel", null, () => SetPreviewPanel(!AnimationState.IsShown(CustomOptions.PreviewPanel, WorkspaceKind.Animation)),
                Checked: AnimationState.IsShown(CustomOptions.PreviewPanel, WorkspaceKind.Animation)),
            MenuEntry.Separator,
            new("figure", "Insert reference cycle…", null, () => _ = InsertReferenceCycleAsync()),
            new(null, "Adjust reference cycle…", null, () => _ = AdjustReferenceCycleAsync(), layer.ReferenceCycle is not null),
            MenuEntry.Separator,
            new("new", "New animation…", null, () => _ = NewAnimationAsync()),
            new("open", "Import animation…", null, () => _ = ImportAnimationAsync()),
            new("export", "Export animation…", null, () => _ = ExportAnimationAsync()),
        ];
    }

    /// <summary>Insert, delete, move and time frames (shared by the menus).</summary>
    private IEnumerable<MenuEntry> FrameCommands(int frame, bool includeNew)
    {
        var s = Session;
        var d = s.Document;
        var a = d.Animation;
        int n = d.FrameCount;
        if (includeNew)
        {
            yield return new("newframe", "Insert blank frame after", "N", () => { s.GoToFrame(frame); s.NewFrame(duplicate: false); });
            yield return new("dupframe", "Duplicate frame", "Shift+N", () => { s.GoToFrame(frame); s.NewFrame(duplicate: true); });
        }
        yield return new("plus", "Insert blank frame before", null, () => s.InsertFrame(frame, duplicate: false));
        yield return new("trash", "Delete frame", null, () => s.DeleteFrame(frame), a is not null && n > 1);
        yield return new("chevl", "Move frame earlier", null, () => s.MoveFrame(frame, frame - 1), a is not null && frame > 0);
        yield return new("chevr", "Move frame later", null, () => s.MoveFrame(frame, frame + 1), a is not null && frame < n - 1);
        yield return new("clock", a is not null && a.DurationOverride(frame) is { } ms ? $"Frame duration… ({ms} ms)" : "Frame duration…", null,
            () => _ = FrameDurationAsync(frame));
    }

    /// <summary>Commands for one layer's drawing at one frame (holds, independence, copy/paste).</summary>
    private IEnumerable<MenuEntry> DrawingCommands(Layer layer, int frame, Cel? cel, int holdStart, int holdEnd)
    {
        var s = Session;
        bool held = cel is not null && holdEnd > holdStart;
        if (cel is null)
        {
            yield return new("brush", "Empty: draw here to start a drawing", null, null, false);
        }
        else
        {
            yield return new("copy", "Make independent (stop sharing with other frames)", null, () => s.MakeIndependent(layer, frame), held);
            yield return new("plus", "New blank drawing here", null, () => s.NewBlankDrawing(layer, frame));
        }
        yield return new("chevr", "Hold one frame longer", null, () => s.ExtendHold(layer, frame));
        yield return new("chevl", "Hold one frame shorter", null, () => s.ShortenHold(layer, frame), holdEnd > holdStart);
        yield return new("copy", "Copy drawing", null, () => s.CopyDrawing(layer, frame), cel is not null);
        yield return new("paste", "Paste drawing", null, () => s.PasteDrawing(layer, frame), s.CelClipboard is not null);
        yield return new("trash", "Clear this cell", null, () => s.DeleteDrawing(layer, frame), cel is not null);
    }

    /// <summary>Right-click on a timeline cell.</summary>
    internal IEnumerable<MenuEntry> CelMenu(Layer layer, int frame)
    {
        var d = Session.Document;
        var cel = d.CelAt(layer, frame);
        var (hs, he) = d.HoldRange(layer, frame);
        var list = new List<MenuEntry>
        {
            MenuEntry.Heading(cel is null ? $"{layer.Name} · frame {frame + 1} (empty)"
                : he > hs ? $"{layer.Name} · frames {hs + 1}–{he + 1} (one drawing)" : $"{layer.Name} · frame {frame + 1}"),
        };
        list.AddRange(DrawingCommands(layer, frame, cel, hs, he));
        list.Add(MenuEntry.Separator);
        list.Add(MenuEntry.Heading($"Frame {frame + 1}"));
        list.AddRange(FrameCommands(frame, includeNew: true));
        return list;
    }

    /// <summary>Right-click on the frame ruler.</summary>
    internal IEnumerable<MenuEntry> FrameMenu(int frame)
    {
        var list = new List<MenuEntry> { MenuEntry.Heading($"Frame {frame + 1} of {Session.Document.FrameCount}") };
        list.AddRange(FrameCommands(frame, includeNew: true));
        return list;
    }

    /// <summary>Right-click on a layer name in the timeline.</summary>
    internal IEnumerable<MenuEntry> LayerTrackMenu()
    {
        var s = Session;
        var d = s.Document;
        var l = d.ActiveLayer;
        int i = d.ActiveIndex;
        return
        [
            MenuEntry.Heading(l.Name),
            new("plus", "New layer", "Ctrl+Shift+N", () => s.AddLayer()),
            new("copy", "Duplicate layer (with all its drawings)", "Ctrl+J", s.DuplicateLayer),
            new("trash", "Delete layer", null, s.DeleteLayer, d.Layers.Count > 1),
            new("merge", "Merge down", "Ctrl+E", s.MergeDown, i > 0),
            MenuEntry.Separator,
            new("chevu", "Move layer up", "Ctrl+]", () => s.MoveLayer(+1), i < d.Layers.Count - 1),
            new("chevd", "Move layer down", "Ctrl+[", () => s.MoveLayer(-1), i > 0),
            new(l.Visible ? "eyeoff" : "eye", l.Visible ? "Hide layer" : "Show layer", null, () => s.SetLayerProperty(l, p => p with { Visible = !p.Visible })),
            new("lock", "Lock layer", null, () => s.SetLayerProperty(l, p => p with { Locked = !p.Locked }), Checked: l.Locked),
            new("figure", "Adjust reference cycle…", null, () => _ = AdjustReferenceCycleAsync(l), l.ReferenceCycle is not null),
            new("layers", "Opacity, blending and more: Layers panel…", null, ShowAnimationLayersPanel),
        ];
    }

    /// <summary>The timeline's ⋯ button: less frequent frame and drawing commands.</summary>
    internal IEnumerable<MenuEntry> TimelineMoreMenu()
    {
        var d = Session.Document;
        int f = d.CurrentFrame;
        var layer = d.ActiveLayer;
        var (hs, he) = d.HoldRange(layer, f);
        var list = new List<MenuEntry> { MenuEntry.Heading($"Frame {f + 1}") };
        list.AddRange(FrameCommands(f, includeNew: false));
        list.Add(MenuEntry.Separator);
        list.Add(MenuEntry.Heading($"Drawing on “{layer.Name}”"));
        list.AddRange(DrawingCommands(layer, f, d.CelAt(layer, f), hs, he));
        list.Add(MenuEntry.Separator);
        list.Add(new("export", "Export animation…", null, () => _ = ExportAnimationAsync()));
        list.Add(new("settings", "Customize workspace…", null, () => _ = CustomizeWorkspaceAsync()));
        return list;
    }

    private void ShowAnimationLayersPanel()
    {
        if (Workspace.Kind != WorkspaceKind.Animation) return;
        if (!AnimationState.IsShown(CustomOptions.LayersDock, WorkspaceKind.Animation))
        {
            AnimationState.SetShown(CustomOptions.LayersDock, WorkspaceKind.Animation, true);
            ApplyCustomization();
            ShowToast("Layers panel shown · hide it again in Window › Customize workspace");
        }
        else if (RightHost.Child is DockArea { Collapsed: true } dock) dock.SetCollapsed(false);
    }

    // ================================================================== commands

    private async Task FrameDurationAsync(int frame)
    {
        var d = Session.Document;
        var a = d.Animation;
        int current = a?.DurationOf(frame) ?? (int)Math.Round(1000 / Engine.Documents.Animation.DefaultFps);
        var r = await _dialogs.AskFrameDurationAsync(frame, current, a?.DurationOverride(frame) is not null, a?.Fps ?? Engine.Documents.Animation.DefaultFps);
        if (r is not { } choice) return;
        Session.SetFrameDuration(frame, choice.Ms);
    }

    /// <summary>File › New animation.</summary>
    public async Task NewAnimationAsync()
    {
        if (!await ConfirmDiscardAsync("starting a new animation")) return;
        bool pixel = Workspace.Kind == WorkspaceKind.PixelArt || PixelAnimation;
        var r = await _dialogs.AskNewAnimationAsync(pixel, pixel ? 64 : 1280, pixel ? 64 : 720);
        if (r is not { } c) return;
        CreateNewAnimation(c);
    }

    /// <summary>Creates the animation and opens Animation Studio in the chosen style (tests use this directly).</summary>
    public void CreateNewAnimation(NewAnimationChoice c)
    {
        Animator.Stop();
        var doc = Document.NewAnimation(c.Width, c.Height, c.Paper, c.Frames, c.Fps, c.PixelArt ? "Sprite" : "Drawing");
        Session.ReplaceDocument(doc, null);
        AnimationState.PixelAnimation = c.PixelArt;
        AnimationState.Tool = null;
        if (Workspace.Kind != WorkspaceKind.Animation) SetWorkspace(WorkspaceKind.Animation);
        else ApplyWorkspace();
        Canvas.FitToView();
        ShowToast($"New animation · {c.Width} × {c.Height} · {c.Frames} frame{(c.Frames == 1 ? "" : "s")} at {c.Fps:0.##} fps");
    }

    /// <summary>File › Export animation: sprite sheet, PNG sequence or animated GIF.</summary>
    public async Task<bool> ExportAnimationAsync()
    {
        Animator.Pause();
        Session.EndStroke();
        Session.CommitTransform();
        var doc = Session.Document;
        bool hasPaper = doc.Paper is not null && doc.PaperVisible;
        var settings = await _dialogs.AskExportAnimationAsync(_settings.AnimationExport, doc.FrameCount, doc.Width, doc.Height, hasPaper);
        if (settings is null) return false;
        _settings.AnimationExport = settings;

        string ext = settings.Format == AnimationExportFormat.Gif ? ".gif" : ".png";
        string baseName = Path.GetFileNameWithoutExtension(Session.DisplayName);
        string suggested = settings.Format switch
        {
            AnimationExportFormat.SpriteSheet => baseName + "_sheet.png",
            AnimationExportFormat.PngSequence => baseName + ".png",
            _ => baseName + ".gif",
        };
        IStorageFolder? folder = null;
        if (Session.FilePath is { } current && Path.GetDirectoryName(current) is { } dir)
            folder = await StorageProvider.TryGetFolderFromPathAsync(dir);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {AnimationDialogs.FormatName(settings.Format).ToLowerInvariant()}",
            SuggestedFileName = suggested,
            SuggestedStartLocation = folder,
            DefaultExtension = ext.TrimStart('.'),
            FileTypeChoices = [settings.Format == AnimationExportFormat.Gif ? new FilePickerFileType("Animated GIF") { Patterns = ["*.gif"], MimeTypes = ["image/gif"] } : PngType],
            // A PNG sequence writes name_0001.png …, not the chosen name itself: overwriting is checked below instead.
            ShowOverwritePrompt = settings.Format != AnimationExportFormat.PngSequence,
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            if (file is not null) await _dialogs.ShowMessageAsync("Couldn't export", "Please choose a folder on this computer.");
            return false;
        }
        if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;

        if (settings.Format == AnimationExportFormat.PngSequence)
        {
            var paths = AnimationExport.SequencePaths(path, doc.FrameCount);
            int existing = paths.Count(File.Exists);
            if (existing > 0)
            {
                var ok = await _dialogs.ShowAsync("Replace existing files?",
                    DialogHost.Text($"{existing} of the {paths.Count} files ({Path.GetFileName(paths[0])} … {Path.GetFileName(paths[^1])}) already exist in this folder. Replace them?"),
                    [("cancel", "Cancel", false), ("ok", "Replace", true)], "cancel", "cancel", 440);
                if (ok != "ok") return false;
            }
        }
        if (_exporting)
        {
            ShowToast("An export is still being written — one moment");
            return false;
        }
        _exporting = true;
        try
        {
            ShowToast($"Exporting {AnimationDialogs.FormatName(settings.Format).ToLowerInvariant()}… you can keep working");
            var written = await ExportAnimationToPathAsync(path, settings);
            ShowToast($"Exported {written}");
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't export", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
        finally
        {
            _exporting = false;
        }
    }

    private bool _exporting;

    /// <summary>Writes an animation export without asking (tests and smoke test). Returns a description of what was written.</summary>
    public async Task<string> ExportAnimationToPathAsync(string path, AnimationExportSettings settings)
    {
        var doc = Session.Document;
        bool paper = settings.Background && doc.Paper is not null && doc.PaperVisible;
        // Frames are composited here (the document belongs to this thread); encoding and writing run in the background.
        var frames = AnimationExport.RenderFrames(doc, paper, Math.Clamp(settings.Scale, 1, 16));
        double fps = doc.Animation?.Fps ?? Engine.Documents.Animation.DefaultFps;
        bool loop = doc.Animation?.Loop ?? true;
        switch (settings.Format)
        {
            case AnimationExportFormat.SpriteSheet:
            {
                var options = new SpriteSheetOptions(settings.Layout, settings.Columns, settings.Spacing, settings.Padding, settings.Margin);
                await Task.Run(() =>
                {
                    var sheet = AnimationExport.BuildSheet(frames, options);
                    AnimationExport.WriteAtomically(path, PngCodec.Encode(sheet.Image));
                    if (settings.Json)
                        AnimationExport.WriteAtomically(Path.ChangeExtension(path, ".json"),
                            System.Text.Encoding.UTF8.GetBytes(AnimationExport.SheetJson(sheet, Path.GetFileName(path), fps, loop)));
                });
                return Path.GetFileName(path) + (settings.Json ? " + .json" : "");
            }
            case AnimationExportFormat.PngSequence:
            {
                var paths = AnimationExport.SequencePaths(path, frames.Count);
                await Task.Run(() => AnimationExport.WriteSequence(frames, paths));
                return $"{paths.Count} PNG files ({Path.GetFileName(paths[0])} …)";
            }
            default:
                await Task.Run(() => GifEncoder.Save(frames, path, new GifOptions(settings.Loop, settings.Dither)));
                return Path.GetFileName(path);
        }
    }

    // ================================================================== samples

    /// <summary>Help › Sample animations: opens a copy of a sample (saving asks for a new name, so the sample stays as shipped).</summary>
    public async Task OpenSampleAsync(SampleAnimation sample)
    {
        if (!await ConfirmDiscardAsync("opening a sample animation")) return;
        try
        {
            var doc = Samples.Open(sample);
            Animator.Stop();
            Session.ReplaceDocument(doc, null);
            AnimationState.PixelAnimation = Samples.IsPixelArt(sample);
            AnimationState.Tool = null;
            if (Workspace.Kind != WorkspaceKind.Animation) SetWorkspace(WorkspaceKind.Animation);
            else ApplyWorkspace();
            Canvas.FitToView();
            ShowToast($"Sample “{Samples.Name(sample)}” · press Enter to play · edit it freely");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the sample", ex.Message);
        }
    }

    // ================================================================== customize workspace and preferences

    /// <summary>Window › Customize workspace: which optional controls this workspace shows.</summary>
    public async Task CustomizeWorkspaceAsync()
    {
        var kind = Workspace.Kind;
        var options = CustomOptions.For(kind).ToList();
        if (options.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Customize workspace", $"{Workspace.Name} is just the canvas: there is nothing to add or hide here.");
            return;
        }
        while (true)
        {
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(DialogHost.Text($"Choose what {Workspace.Name} shows. Everything else stays in the menus."));
            foreach (var o in options)
            {
                var id = o.Id;
                var (toggle, _) = UiKit.Check(o.Label, null, () => WsState.IsShown(id, kind), v =>
                {
                    WsState.SetShown(id, kind, v);
                    ApplyCustomization();
                });
                toggle.Name = "Customize_" + id;
                toggle.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                toggle.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                var row = new StackPanel { Spacing = 0 };
                row.Children.Add(toggle);
                row.Children.Add(new TextBlock { Text = o.Description, Classes = { "dim" }, FontSize = 11, Margin = new Avalonia.Thickness(36, -4, 0, 4), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                body.Children.Add(row);
            }
            var r = await _dialogs.ShowAsync($"Customize {Workspace.Name}", body, [("reset", "Reset to default", false), ("ok", "Done", true)], "ok", "ok", 440);
            if (r != "reset") return;
            ResetWorkspaceLayout();
        }
    }

    /// <summary>Back to the shipped layout of the current workspace.</summary>
    public void ResetWorkspaceLayout()
    {
        WsState.ResetLayout();
        ApplyCustomization();
        if (RightHost.Child is DockArea dock) dock.SetCollapsed(false);
        ShowToast($"{Workspace.Name} is back to its default layout");
    }

    public async Task PreferencesAsync()
    {
        var r = await _dialogs.AskPreferencesAsync(_settings.AutosaveSeconds);
        if (r is not { } seconds) return;
        _settings.AutosaveSeconds = seconds;
        ConfigureAutosave();
        if (seconds == 0) _recovery?.ClearOwn();
        ShowToast(seconds == 0 ? "Autosave is off" : $"Autosave every {AnimationDialogs.AutosaveChoices.First(c => c.Seconds == seconds).Label}");
    }

    // ================================================================== saving, autosave and recovery

    /// <summary>Saves a .dnk: the document is copied at this moment (quickly, on this thread) and compressed and written in
    /// the background, so drawing can continue. Edits made meanwhile stay marked unsaved.</summary>
    private async Task SaveProjectInBackgroundAsync(string path)
    {
        if (_saveTask is { IsCompleted: false })
        {
            try { await _saveTask; }
            catch (Exception) { /* reported by its own caller */ }
        }
        var doc = Session.Document;
        long start = PerformanceLog.Now();
        var snapshot = DnkFile.Snapshot(doc, Version);
        PerformanceLog.Operation("Save: copy (UI thread)", PerformanceLog.Since(start));
        var task = Task.Run(() =>
        {
            long write = PerformanceLog.Now();
            DnkFile.Save(snapshot, path);
            PerformanceLog.Operation("Save: compress and write (background)", PerformanceLog.Since(write));
        });
        _saveTask = task;
        await task;
        Session.MarkSaved(doc, path, snapshot.StateId);
        if (!doc.IsDirty) _recovery?.ClearOwn();
    }

    /// <summary>Starts crash recovery and autosave with a recovery folder (the real app uses %LOCALAPPDATA%).</summary>
    internal void StartRecovery(RecoveryStore store)
    {
        _recovery?.Dispose();
        _recovery = store;
        store.StartSession();
        ConfigureAutosave();
    }

    internal RecoveryStore? Recovery => _recovery;

    private void ConfigureAutosave()
    {
        _autosaveTimer.Stop();
        if (_recovery is null || _settings.AutosaveSeconds <= 0) return;
        _autosaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, _settings.AutosaveSeconds));
        _autosaveTimer.Start();
    }

    /// <summary>Writes a recovery copy if there are unsaved changes the last copy doesn't have. Returns true if written.</summary>
    internal async Task<bool> AutosaveNowAsync()
    {
        if (_recovery is not { } store || _autosaveTask is { IsCompleted: false }) return false;
        var doc = Session.Document;
        if (!doc.IsDirty)
        {
            if (_autosavedDocument is not null) store.ClearOwn();
            _autosavedDocument = null;
            return false;
        }
        // Mid-stroke or mid-transform the pixels aren't settled yet: try again next time.
        if (Session.IsStrokeActive || Session.ActiveTransform is not null) return false;
        long state = doc.History.CurrentStateId;
        if (ReferenceEquals(doc, _autosavedDocument) && state == _autosavedState) return false;
        try
        {
            long start = PerformanceLog.Now();
            var snapshot = DnkFile.Snapshot(doc, Version);
            PerformanceLog.Operation("Autosave: copy (UI thread)", PerformanceLog.Since(start));
            string? original = Session.FilePath;
            string name = Session.DisplayName;
            var task = Task.Run(() =>
            {
                long write = PerformanceLog.Now();
                store.Write(snapshot, original, name);
                PerformanceLog.Operation("Autosave: write (background)", PerformanceLog.Since(write));
            });
            _autosaveTask = task;
            await task;
            _autosavedDocument = doc;
            _autosavedState = state;
            return true;
        }
        catch (Exception)
        {
            return false; // recovery copies are a safety net; a full disk must not interrupt drawing
        }
    }

    private DateTime _lastErrorMessage = DateTime.MinValue;

    /// <summary>
    /// An unexpected error reached the UI thread: log it, keep a recovery copy of unsaved work and tell the user once
    /// (not for every repeat). DoodleNook keeps running; the work in progress is protected either way.
    /// </summary>
    internal async void ReportUnexpectedError(Exception ex)
    {
        ErrorLog.Write(ex);
        try
        {
            Canvas.InterruptGesture();
            await AutosaveNowAsync();
        }
        catch (Exception)
        {
            // Best effort only.
        }
        if ((DateTime.UtcNow - _lastErrorMessage).TotalSeconds < 10) return;
        _lastErrorMessage = DateTime.UtcNow;
        try
        {
            await _dialogs.ShowMessageAsync("Something went wrong",
                $"DoodleNook ran into an unexpected problem ({ex.GetType().Name}: {ex.Message}). It kept running, and unsaved work " +
                $"has a recovery copy. Saving now is a good idea.\n\nDetails were written to {ErrorLog.DefaultPath}.");
        }
        catch (Exception)
        {
            ShowToast("Something went wrong — details are in the DoodleNook error log");
        }
    }

    /// <summary>At start: offers work left behind by a DoodleNook that didn't close normally.</summary>
    internal async Task OfferRecoveryAsync()
    {
        if (_recovery is not { } store) return;
        var entries = store.FindRecoverable();
        if (entries.Count == 0) return;
        var entry = entries[0];
        var choice = await _dialogs.AskRecoverAsync(entry, entries.Count - 1);
        if (choice == "discard")
        {
            store.Discard(entry);
            ShowToast("Recovery copy discarded");
            return;
        }
        if (choice != "recover") return;
        if (!await ConfirmDiscardAsync("recovering the other picture")) return;
        try
        {
            var doc = store.Load(entry);
            Animator.Stop();
            Session.ReplaceWithRecovered(doc, entry.OriginalPath);
            // Keep the work protected: our own recovery copy first, then the old one can go.
            await AutosaveNowAsync();
            store.Discard(entry);
            if (doc.IsAnimated && Workspace.Kind != WorkspaceKind.Animation) SetWorkspace(WorkspaceKind.Animation);
            ShowToast($"Recovered “{entry.DisplayName}” · save it to keep it");
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync("Couldn't recover", $"The recovery copy could not be read ({ex.Message}). It was kept in {store.Directory}.");
        }
    }
}
