using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DoodleNook.App.Bones;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Files;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Bones;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Samples;

namespace DoodleNook.App;

/// <summary>
/// Mission 019 — Bare Bones: File › New skeletal animation… / Open skeletal animation…, the Bare Bones view over the
/// canvas, the trip from a sprite to the normal editor and back, and export through the animation exporters. A
/// skeletal animation links ordinary drawings; it never paints itself and never changes them.
/// </summary>
public partial class MainWindow
{
    private static readonly FilePickerFileType SkeletonType = new("DoodleNook skeletal animation (.dnkbones)") { Patterns = ["*" + SkeletonProject.Extension] };

    private BareBonesView? _bonesView;
    private Border? _bonesChip;
    private TextBlock? _bonesChipText;
    private string? _bonesEditing;
    private (string Path, long State, SpriteFrames Frames)? _liveSprite;

    /// <summary>The skeletal animation open now, or null.</summary>
    public SkeletonProject? Skeleton { get; private set; }

    internal BareBonesView? BonesView => _bonesView;

    public bool BonesShown => _bonesView is { IsVisible: true };

    /// <summary>A view with the window to itself (Storybook Studio or Bare Bones): the drawing chrome steps aside.</summary>
    public bool DedicatedViewShown => StorybookShown || BonesShown;

    // ================================================================== new, open, close

    public async Task NewSkeletonAsync()
    {
        var choice = await AskSkeletonSettingsAsync("New skeletal animation", null);
        if (choice is not { } c) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the new skeletal animation (its new drawings go in an “art” folder beside it)",
            SuggestedFileName = "My character" + SkeletonProject.Extension,
            DefaultExtension = SkeletonProject.Extension.TrimStart('.'),
            FileTypeChoices = [SkeletonType],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        await CreateSkeletonAsync(path, c.Width, c.Height, c.Fps, c.Length, c.PixelArt);
    }

    /// <summary>Creates and opens an empty skeletal animation at <paramref name="path"/> (tests call this directly).</summary>
    public async Task<bool> CreateSkeletonAsync(string path, int width, int height, double fps, int length, bool pixelArt)
    {
        var project = new SkeletonProject { Width = width, Height = height, Fps = fps, Length = length, PixelArt = pixelArt };
        try
        {
            project.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't create the skeletal animation", ex.Message);
            return false;
        }
        OpenSkeleton(project);
        ShowToast("Bare Bones · drag on the stage to draw the first bone (for a character, from the hips upwards)");
        return true;
    }

    public async Task OpenSkeletonAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a skeletal animation",
            AllowMultiple = false,
            FileTypeFilter = [SkeletonType, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await OpenSkeletonFromAsync(path);
    }

    public async Task<bool> OpenSkeletonFromAsync(string path)
    {
        SkeletonProject project;
        try
        {
            project = SkeletonProject.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SkeletonFormatException)
        {
            await _dialogs.ShowMessageAsync("Couldn't open the skeletal animation", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
        OpenSkeleton(project);
        int missing = project.Bones.SelectMany(b => b.Attachments).Select(a => project.Resolve(a.Source)).Distinct().Count(p => p is null || !File.Exists(p));
        if (missing > 0) ShowToast($"{missing} sprite file{(missing == 1 ? " is" : "s are")} missing: the sprites are kept; select one and choose “Replace…” to find it again.");
        else ShowToast($"Opened {Path.GetFileName(path)} · Enter plays it");
        return true;
    }

    /// <summary>Help › Samples › Wave hello: the sample in Documents/DoodleNook/Samples (made once, then reopened).</summary>
    public async Task OpenSampleSkeletonAsync(string? folder = null)
    {
        folder ??= Path.Combine(DocumentsFolder(), "DoodleNook", "Samples", SampleSkeleton.Name);
        string path = Path.Combine(folder, SampleSkeleton.Name + SkeletonProject.Extension);
        if (!File.Exists(path))
        {
            try
            {
                path = await Task.Run(() => SampleSkeleton.Create(folder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await _dialogs.ShowMessageAsync("Couldn't make the sample", ex.Message);
                return;
            }
        }
        await OpenSkeletonFromAsync(path);
    }

    private void OpenSkeleton(SkeletonProject project)
    {
        Skeleton = project;
        _bonesEditing = null;
        ShowBonesView();
        _bonesView!.Open(project);
    }

    /// <summary>After an undo the view holds a new project object.</summary>
    internal void SkeletonReplaced(SkeletonProject project) => Skeleton = project;

    public void ShowBonesView()
    {
        if (Skeleton is null) return;
        if (_bonesView is null)
        {
            _bonesView = new BareBonesView(this);
            CanvasHost.Children.Add(_bonesView);
            InitBonesChip();
        }
        if (CatalogShown) HideCatalog();
        if (StorybookShown) HideStorybook();
        Animator.Pause();
        Session.EndStroke();
        Session.CommitTransform();
        _bonesView.IsVisible = true;
        ApplyChromeVisibility();
        if (!ReferenceEquals(_bonesView.Project, Skeleton)) _bonesView.Open(Skeleton);
        else _bonesView.Rebuild();
        _bonesView.Stage.Focus();
        RefreshBonesChip();
    }

    public void HideBonesView()
    {
        if (_bonesView is null) return;
        _bonesView.StopPlaying();
        bool was = _bonesView.IsVisible;
        _bonesView.IsVisible = false;
        if (was) ApplyChromeVisibility();
        RefreshBonesChip();
        Canvas.Focus();
    }

    public void CloseSkeleton()
    {
        HideBonesView();
        Skeleton = null;
        _bonesEditing = null;
        _liveSprite = null;
        RefreshBonesChip();
    }

    /// <summary>While the view is shown, its keys come first; nothing else may change the drawing out of sight (as in
    /// the storybook). New and Open go back to drawing first; Help, full screen and diagnostics work as everywhere.</summary>
    internal bool HandleBonesKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers mods)
    {
        bool ctrl = mods.HasFlag(Avalonia.Input.KeyModifiers.Control) || mods.HasFlag(Avalonia.Input.KeyModifiers.Meta);
        if (_bonesView!.HandleKey(key, mods)) return true;
        switch (key)
        {
            case Avalonia.Input.Key.N or Avalonia.Input.Key.O when ctrl:
                HideBonesView();
                return false;
            case Avalonia.Input.Key.S when ctrl:
                ShowToast("Bare Bones saves as you go · Esc goes back to the drawing");
                return true;
            case Avalonia.Input.Key.F1 or Avalonia.Input.Key.F11 or Avalonia.Input.Key.F12:
                return false;
            default:
                return true;
        }
    }

    // ================================================================== sprites in the editor

    private void InitBonesChip()
    {
        _bonesChipText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis };
        var back = UiKit.Button("bone", "Back to Bare Bones", "See the character with this sprite", ShowBonesView);
        back.Height = 26;
        back.FontSize = 12;
        back.Name = "BonesBackToBones";
        _bonesChip = new Border
        {
            Classes = { "float" },
            Name = "BonesChip",
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(10, 3, 4, 3),
            CornerRadius = new CornerRadius(999),
            Child = UiKit.Row(6, _bonesChipText, back),
        };
        CanvasHost.Children.Add(_bonesChip);
        Session.DocumentReplaced += RefreshBonesChip;
        Session.StateChanged += RefreshBonesChip;
    }

    /// <summary>While a sprite is open in the editor: a chip back to the character.</summary>
    private void RefreshBonesChip()
    {
        if (_bonesChip is null || _bonesChipText is null) return;
        bool on = Skeleton is not null && !BonesShown && _bonesEditing is { } ed && Session.FilePath is { } open
                  && string.Equals(Path.GetFullPath(open), ed, StringComparison.OrdinalIgnoreCase);
        _bonesChip.IsVisible = on;
        if (on)
            _bonesChipText.Text = $"Sprite of “{Path.GetFileNameWithoutExtension(Skeleton!.FilePath)}”{(Session.Document.IsDirty ? " · not saved yet (shown anyway)" : "")}";
    }

    /// <summary>A sprite's drawing in the normal editor (still: Painting or Pixel Art; animated: Animation Studio).</summary>
    public async Task<bool> OpenBonesSpriteAsync(string path)
    {
        path = Path.GetFullPath(path);
        bool already = Session.FilePath is { } open && string.Equals(Path.GetFullPath(open), path, StringComparison.OrdinalIgnoreCase);
        if (!already)
        {
            if (!await ConfirmDiscardAsync("opening the sprite")) return false;
            if (!await OpenPathAsync(path)) return false;
        }
        _bonesEditing = path;
        HideBonesView();
        bool pixel = Skeleton?.PixelArt == true;
        if (Session.Document.IsAnimated)
        {
            if (Workspace.Kind != WorkspaceKind.Animation)
            {
                AnimationState.PixelAnimation = pixel;
                SetWorkspace(WorkspaceKind.Animation);
            }
        }
        else if (Workspace.Kind is WorkspaceKind.Animation or WorkspaceKind.PureSketch or WorkspaceKind.QuickSketch)
            SetWorkspace(pixel ? WorkspaceKind.PixelArt : WorkspaceKind.Painting);
        Canvas.FitToView();
        RefreshBonesChip();
        ShowToast("Draw as usual, save with Ctrl+S, then “Back to Bare Bones” (unsaved changes show there too).");
        return true;
    }

    /// <summary>The drawing open in the editor, when it is a sprite with changes not saved yet (so the stage shows them).</summary>
    internal SpriteFrames? LiveSprite(string path)
    {
        var doc = Session.Document;
        if (Session.FilePath is not { } open || !doc.IsDirty || !string.Equals(Path.GetFullPath(open), path, StringComparison.OrdinalIgnoreCase)) return null;
        long state = doc.History.CurrentStateId;
        if (_liveSprite is { } live && live.Path == path && live.State == state) return live.Frames;
        var frames = SkeletonAssets.FromDocument(doc);
        _liveSprite = (path, state, frames);
        return frames;
    }

    /// <summary>A new transparent drawing for a bone (still or animated), in the "art" folder, linked and opened in the editor.</summary>
    public async Task NewBoneDrawingAsync(Bone bone)
    {
        if (Skeleton is not { } p || _bonesView is null || p.Folder is null) return;
        // Sized to the bone as it lies on screen in the rest pose: a drawing is shown the way it is drawn.
        var world = p.RestWorld(bone);
        var (ax, ay) = world.Apply(0, 0);
        var (bx, by) = world.Apply(bone.Length, 0);
        double pad = Math.Max(24, bone.Length * 0.45);
        int Round8(double v) => Math.Clamp((int)Math.Ceiling(v / 8) * 8, 32, 2048);
        int w = Round8(Math.Abs(bx - ax) + pad), h = Round8(Math.Abs(by - ay) + pad);
        var name = new TextBox { Text = bone.Name, Width = 220, Name = "BonesDrawingName" };
        bool animated = false;
        var widthBox = new TextBox { Text = w.ToString(CultureInfo.CurrentCulture), Width = 64 };
        var heightBox = new TextBox { Text = h.ToString(CultureInfo.CurrentCulture), Width = 64 };
        Action refreshKind = () => { };
        var (kind, rk) = UiKit.Segmented([(false, null, "Still", "One picture"), (true, "film", "Animated", "Frames that play on their own (Idle, Blink, Wave…)")],
            () => animated, v =>
            {
                animated = v;
                refreshKind();
            }, compact: true);
        refreshKind = rk;
        var body = new StackPanel { Spacing = 10, Name = "BonesNewDrawingDialog" };
        body.Children.Add(UiKit.Row(8, Label("Name"), name));
        body.Children.Add(UiKit.Row(8, Label("Size"), widthBox, new TextBlock { Text = "×", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }, heightBox,
            new TextBlock { Text = "px", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }));
        body.Children.Add(UiKit.Row(8, Label("Kind"), kind));
        body.Children.Add(DialogHost.Text($"A transparent drawing saved in “art” beside the project and shown on {bone.Name} the way you draw it. " +
                                          "It opens in the editor; save it and come back with “Back to Bare Bones”."));
        var r = await _dialogs.ShowAsync("New drawing for the bone", body, [("cancel", "Cancel", false), ("create", "Create and draw", true)], "create", "cancel", 460);
        if (r != "create") return;
        if (!int.TryParse(widthBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out w) || !int.TryParse(heightBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out h))
            (w, h) = (128, 128);
        w = Math.Clamp(w, 1, 4096);
        h = Math.Clamp(h, 1, 4096);
        if (!await ConfirmDiscardAsync("drawing the sprite")) return;
        string path;
        try
        {
            path = NewArtPath(p, string.IsNullOrWhiteSpace(name.Text) ? bone.Name : name.Text.Trim());
            var doc = animated ? Document.NewAnimation(w, h, null, frames: 4, fps: p.Fps) : new Document(w, h, null);
            DnkFile.Save(doc, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Couldn't create the drawing", ex.Message);
            return;
        }
        _bonesView.AddSpriteFile(bone, path);
        await OpenBonesSpriteAsync(path);
    }

    /// <summary>A free file name in the project's "art" folder.</summary>
    private static string NewArtPath(SkeletonProject p, string name, string extension = ".dnk")
    {
        var folder = Path.Combine(p.Folder!, "art");
        Directory.CreateDirectory(folder);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        var path = Path.Combine(folder, name + extension);
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{name} {n}{extension}");
        return path;
    }

    /// <summary>An editable DoodleNook drawing made from a picture (the picture isn't changed).</summary>
    internal async Task<string?> EditableCopyAsync(string picture)
    {
        if (Skeleton is not { Folder: not null } p) return null;
        try
        {
            var target = NewArtPath(p, Path.GetFileNameWithoutExtension(picture));
            await Task.Run(() => DnkFile.Save(ImageFiles.Open(picture), target));
            ShowToast($"Made {Path.GetFileName(target)} from {Path.GetFileName(picture)} (the picture isn't changed)");
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            await _dialogs.ShowMessageAsync("Couldn't make an editable copy", $"{Path.GetFileName(picture)}: {ex.Message}");
            return null;
        }
    }

    internal async Task<string?> PickSpriteFileAsync(string title)
    {
        IStorageFolder? start = null;
        if (Skeleton?.Folder is { } dir) start = await StorageProvider.TryGetFolderFromPathAsync(dir);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title + " (a DoodleNook drawing, still or animated, or a picture)",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType("Drawings and pictures") { Patterns = ["*.dnk", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] }, FilePickerFileTypes.All],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    // ================================================================== size, speed, save as

    private async Task<(int Width, int Height, double Fps, int Length, bool PixelArt)?> AskSkeletonSettingsAsync(string title, SkeletonProject? current)
    {
        var widthBox = new TextBox { Text = (current?.Width ?? 512).ToString(CultureInfo.CurrentCulture), Width = 70, Name = "BonesSettingsWidth" };
        var heightBox = new TextBox { Text = (current?.Height ?? 512).ToString(CultureInfo.CurrentCulture), Width = 70, Name = "BonesSettingsHeight" };
        var fpsBox = new TextBox { Text = (current?.Fps ?? 12).ToString("0.##", CultureInfo.CurrentCulture), Width = 70, Name = "BonesSettingsFps" };
        var lengthBox = new TextBox { Text = (current?.Length ?? 24).ToString(CultureInfo.CurrentCulture), Width = 70, Name = "BonesSettingsLength" };
        bool pixel = current?.PixelArt ?? false;
        var (pixelCheck, _) = UiKit.Check("Pixel art (exact pixels, no smoothing when bones turn)", null, () => pixel, v => pixel = v);
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#FF8A7A")), IsVisible = false, TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel { Spacing = 10, Name = "BonesSettingsDialog" };
        body.Children.Add(UiKit.Row(8, Label("Picture"), widthBox, new TextBlock { Text = "×", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }, heightBox,
            new TextBlock { Text = "px", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }));
        body.Children.Add(UiKit.Row(8, Label("Speed"), fpsBox, new TextBlock { Text = "frames per second", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }));
        body.Children.Add(UiKit.Row(8, Label("Length"), lengthBox, new TextBlock { Text = "frames", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center }));
        body.Children.Add(pixelCheck);
        body.Children.Add(DialogHost.Text(current is null
            ? "The picture is the size the character is assembled and exported at. Sprites keep their own sizes and speeds."
            : "Sprites and keys aren't changed by this. A shorter length removes keys past the end (Undo brings them back)."));
        body.Children.Add(error);
        while (true)
        {
            var r = await _dialogs.ShowAsync(title, body, [("cancel", "Cancel", false), ("ok", current is null ? "Create…" : "Apply", true)], "ok", "cancel", 460);
            if (r != "ok") return null;
            if (int.TryParse(widthBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int w) && w is >= 8 and <= 8192 &&
                int.TryParse(heightBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int h) && h is >= 8 and <= 8192 &&
                double.TryParse(fpsBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double fps) && fps is >= 1 and <= 60 &&
                int.TryParse(lengthBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int length) && length is >= 1 and <= 10_000)
                return (w, h, fps, length, pixel);
            error.Text = "Please enter a picture of 8 to 8192 pixels, 1 to 60 frames per second and 1 to 10 000 frames.";
            error.IsVisible = true;
        }
    }

    public async Task SkeletonSettingsAsync()
    {
        if (Skeleton is not { } p || _bonesView is null) return;
        var choice = await AskSkeletonSettingsAsync("Picture size and speed", p);
        if (choice is not { } c) return;
        int lost = p.Tracks.Sum(t => t.Keys.Count(k => k.Frame >= c.Length) + t.Switches.Count(s => s.Frame >= c.Length));
        _bonesView.Edit(q =>
        {
            q.Width = c.Width;
            q.Height = c.Height;
            q.Fps = c.Fps;
            q.PixelArt = c.PixelArt;
            q.SetLength(c.Length);
        });
        _bonesView.Stage.FitToView();
        if (lost > 0) ShowToast($"{lost} key{(lost == 1 ? " was" : "s were")} past the new end and removed · Ctrl+Z brings {(lost == 1 ? "it" : "them")} back");
    }

    public async Task SaveSkeletonCopyAsync()
    {
        if (Skeleton is not { } p || _bonesView is null) return;
        IStorageFolder? start = p.Folder is { } dir ? await StorageProvider.TryGetFolderFromPathAsync(dir) : null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the skeletal animation as (it keeps linking the same drawings)",
            SuggestedFileName = Path.GetFileNameWithoutExtension(p.FilePath) + " copy" + SkeletonProject.Extension,
            SuggestedStartLocation = start,
            DefaultExtension = SkeletonProject.Extension.TrimStart('.'),
            FileTypeChoices = [SkeletonType],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            p.Save(path);
            _bonesView.Rebuild();
            ShowToast($"Saved as {Path.GetFileName(path)} · you're working on it now (the links to the drawings were kept)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't save", ex.Message);
        }
    }

    // ================================================================== export

    /// <summary>Export: the existing animation export (GIF, sprite sheet + JSON, PNG sequence) with the skeleton's frames.</summary>
    public async Task<bool> ExportSkeletonAsync()
    {
        if (Skeleton is not { } p || _bonesView is null) return false;
        _bonesView.StopPlaying();
        var settings = await _dialogs.AskExportAnimationAsync(_settings.AnimationExport, p.Length, p.Width, p.Height, hasPaper: false);
        if (settings is null) return false;
        _settings.AnimationExport = settings;
        string ext = settings.Format == AnimationExportFormat.Gif ? ".gif" : ".png";
        string baseName = Path.GetFileNameWithoutExtension(p.FilePath) ?? "animation";
        IStorageFolder? start = p.Folder is { } dir ? await StorageProvider.TryGetFolderFromPathAsync(dir) : null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {AnimationDialogs.FormatName(settings.Format).ToLowerInvariant()}",
            SuggestedFileName = settings.Format switch
            {
                AnimationExportFormat.SpriteSheet => baseName + "_sheet.png",
                AnimationExportFormat.PngSequence => baseName + ".png",
                _ => baseName + ".gif",
            },
            SuggestedStartLocation = start,
            DefaultExtension = ext.TrimStart('.'),
            FileTypeChoices = [settings.Format == AnimationExportFormat.Gif ? new FilePickerFileType("Animated GIF") { Patterns = ["*.gif"], MimeTypes = ["image/gif"] } : PngType],
            ShowOverwritePrompt = settings.Format != AnimationExportFormat.PngSequence,
        });
        if (file?.TryGetLocalPath() is not { } path) return false;
        if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) path += ext;
        if (settings.Format == AnimationExportFormat.PngSequence)
        {
            var paths = AnimationExport.SequencePaths(path, p.Length);
            int existing = paths.Count(File.Exists);
            if (existing > 0)
            {
                var ok = await _dialogs.ShowAsync("Replace existing files?",
                    DialogHost.Text($"{existing} of the {paths.Count} files ({Path.GetFileName(paths[0])} … {Path.GetFileName(paths[^1])}) already exist in this folder. Replace them?"),
                    [("cancel", "Cancel", false), ("ok", "Replace", true)], "cancel", "cancel", 440);
                if (ok != "ok") return false;
            }
        }
        try
        {
            ShowToast($"Exporting {AnimationDialogs.FormatName(settings.Format).ToLowerInvariant()}…");
            ShowToast($"Exported {await ExportSkeletonToPathAsync(path, settings)}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await _dialogs.ShowMessageAsync("Couldn't export", $"{Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Writes the export without asking (tests and the smoke test). Only the artwork: bones are never drawn.</summary>
    public async Task<string> ExportSkeletonToPathAsync(string path, AnimationExportSettings settings)
    {
        if (Skeleton is not { } p || _bonesView is null) return "";
        var frames = SkeletonRenderer.RenderAnimation(p, _bonesView.Assets);
        int scale = Math.Clamp(settings.Scale, 1, 16);
        if (scale > 1) frames = frames.Select(f => f with { Image = AnimationExport.ScaleNearest(f.Image, scale) }).ToList();
        return await WriteFramesAsync(path, settings, frames, p.Fps, p.Loop);
    }

    public async Task<bool> ExportSkeletonFrameAsync()
    {
        if (Skeleton is not { } p || _bonesView is null) return false;
        IStorageFolder? start = p.Folder is { } dir ? await StorageProvider.TryGetFolderFromPathAsync(dir) : null;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export this frame as PNG",
            SuggestedFileName = $"{Path.GetFileNameWithoutExtension(p.FilePath)} frame {_bonesView.Frame + 1:00}.png",
            SuggestedStartLocation = start,
            DefaultExtension = "png",
            FileTypeChoices = [PngType],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return false;
        try
        {
            var image = SkeletonRenderer.Render(p, _bonesView.EvaluateShown(), _bonesView.Assets);
            await Task.Run(() => AnimationExport.WriteAtomically(path, PngCodec.Encode(image)));
            ShowToast($"Exported {Path.GetFileName(path)} · {image.Width} × {image.Height}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't export", ex.Message);
            return false;
        }
    }
}
