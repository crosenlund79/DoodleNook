using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoodleNook.App.Files;

/// <summary>How far an image of a game folder is. Saving never makes an image complete: only the artist does.</summary>
public enum GameImageStatus
{
    NotStarted,
    InProgress,
    Complete,
}

/// <summary>One PNG of a game graphics folder.</summary>
public sealed class GameImage
{
    private readonly GameProject _project;

    internal GameImage(GameProject project, string relativePath, GameImageStatus status)
    {
        _project = project;
        RelativePath = relativePath;
        Status = status;
    }

    /// <summary>Path from the game folder, with forward slashes (e.g. "characters/player.png").</summary>
    public string RelativePath { get; }

    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>The folder part of <see cref="RelativePath"/> ("" for images directly in the game folder).</summary>
    public string Folder => RelativePath.Contains('/') ? RelativePath[..RelativePath.LastIndexOf('/')] : "";

    public GameImageStatus Status { get; internal set; }

    /// <summary>The game's own PNG.</summary>
    public string PngPath => Path.Combine(_project.Root, Native(RelativePath));

    /// <summary>The editable DoodleNook document: _DoodleNook/&lt;same folders&gt;/name.dnk.</summary>
    public string DnkPath => Path.Combine(_project.WorkFolder, Native(Path.ChangeExtension(RelativePath, ".dnk")));

    /// <summary>Where the untouched original is kept: _DoodleNook/Originals/&lt;same folders&gt;/name.png.</summary>
    public string BackupPath => Path.Combine(_project.WorkFolder, GameProject.OriginalsFolderName, Native(RelativePath));

    public bool HasDnk => File.Exists(DnkPath);

    /// <summary>For a game's fallback graphic named <c>template_…</c> ("characters/template_player.png"), the path of its
    /// replacement in the same folder ("characters/player.png"); null for every other image.</summary>
    public string? ReplacementPath
    {
        get
        {
            var name = FileName;
            if (!name.StartsWith(GameProject.TemplatePrefix, StringComparison.Ordinal)
                || Path.GetFileNameWithoutExtension(name).Length == GameProject.TemplatePrefix.Length) return null;
            name = name[GameProject.TemplatePrefix.Length..];
            return Folder.Length == 0 ? name : Folder + "/" + name;
        }
    }

    private static string Native(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);

    public override string ToString() => $"{RelativePath} ({Status})";
}

/// <summary>Counts for the progress overview. <see cref="Percent"/> counts only images marked complete.</summary>
public readonly record struct GameProgress(int Total, int NotStarted, int InProgress, int Complete)
{
    public double Percent => Total == 0 ? 0 : Complete * 100.0 / Total;
}

/// <summary>
/// Game Edit Mode's folder: every PNG under a game graphics folder (subfolders included), with each image's progress.
/// DoodleNook's own files live in one working folder inside it, <c>_DoodleNook</c>, which is never scanned:
/// <c>catalog.json</c> (statuses and the backup choice), the editable <c>.dnk</c> documents in the same folder layout as
/// the PNGs, and <c>Originals/</c> (untouched copies of the PNGs, made just before each one is first replaced).
/// </summary>
public sealed class GameProject
{
    public const string WorkFolderName = "_DoodleNook";
    public const string CatalogFileName = "catalog.json";
    public const string OriginalsFolderName = "Originals";

    /// <summary>A game's fallback graphics start with this: template_player.png is used until player.png exists.</summary>
    public const string TemplatePrefix = "template_";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // Statuses by relative path. Entries for PNGs that are gone stay in the file (they just don't count), so a file that
    // comes back keeps its status and nothing unrelated is ever lost.
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private List<GameImage> _images = new();
    private Dictionary<string, GameImage> _byPath = new(StringComparer.OrdinalIgnoreCase);

    // One object per image for the project's lifetime, so the picture being edited, the catalog's tile and the progress
    // always share the same status (a rescan doesn't make new ones).
    private readonly Dictionary<string, GameImage> _objects = new(StringComparer.OrdinalIgnoreCase);

    private GameProject(string root)
    {
        Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        WorkFolder = Path.Combine(Root, WorkFolderName);
    }

    public string Root { get; }

    /// <summary>The folder's name, for the catalog's title.</summary>
    public string Name => Path.GetFileName(Root) is { Length: > 0 } n ? n : Root;

    public string WorkFolder { get; }

    public string CatalogPath => Path.Combine(WorkFolder, CatalogFileName);

    /// <summary>Back up each original PNG before it is first replaced (chosen when the folder is first opened).</summary>
    public bool BackupOriginals { get; private set; } = true;

    /// <summary>The PNGs found by the last scan, in path order.</summary>
    public IReadOnlyList<GameImage> Images => _images;

    /// <summary>Set when catalog.json couldn't be read (it was kept aside and a fresh catalog started).</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>True when the folder has been opened in Game Edit Mode before.</summary>
    public static bool IsKnown(string root) => File.Exists(Path.Combine(root, WorkFolderName, CatalogFileName));

    /// <summary>Opens a game folder: reads its catalog (if any) and scans for PNGs. Writes nothing until something changes.</summary>
    public static GameProject Open(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"The folder “{root}” doesn't exist.");
        var project = new GameProject(root);
        project.Load();
        project.Rescan();
        return project;
    }

    /// <summary>Starts Game Edit Mode in a folder for the first time (or changes the backup choice) and writes the catalog.</summary>
    public void SetBackupOriginals(bool backup)
    {
        BackupOriginals = backup;
        Save();
    }

    // ================================================================== scanning

    /// <summary>Finds every PNG under the folder again. New ones start as Not started; known ones keep their status.</summary>
    public void Rescan()
    {
        var found = new List<string>();
        Scan(Root, "", found);
        found.Sort(StringComparer.OrdinalIgnoreCase);
        _images = found.Select(ImageAt).ToList();
        _byPath = _images.ToDictionary(i => i.RelativePath, StringComparer.OrdinalIgnoreCase);
    }

    private GameImage ImageAt(string relativePath)
    {
        if (!_objects.TryGetValue(relativePath, out var image))
            _objects[relativePath] = image = new GameImage(this, relativePath, _entries.TryGetValue(relativePath, out var e) ? e.Status : GameImageStatus.NotStarted);
        return image;
    }

    private static void Scan(string dir, string relative, List<string> found)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, MatchCasing = MatchCasing.CaseInsensitive };
        foreach (var file in Directory.EnumerateFiles(dir, "*.png", options))
            found.Add(relative.Length == 0 ? Path.GetFileName(file) : relative + "/" + Path.GetFileName(file));
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", options))
        {
            var name = Path.GetFileName(sub);
            if (string.Equals(name, WorkFolderName, StringComparison.OrdinalIgnoreCase)) continue; // DoodleNook's own files
            Scan(sub, relative.Length == 0 ? name : relative + "/" + name, found);
        }
    }

    /// <summary>The counts. A template_ image whose replacement exists isn't counted: the game shows the replacement,
    /// which carries the progress.</summary>
    public GameProgress Progress
    {
        get
        {
            var counted = _images.Where(i => !IsReplaced(i)).ToList();
            return new(counted.Count,
                counted.Count(i => i.Status == GameImageStatus.NotStarted),
                counted.Count(i => i.Status == GameImageStatus.InProgress),
                counted.Count(i => i.Status == GameImageStatus.Complete));
        }
    }

    public GameImage? Find(string relativePath) => _byPath.GetValueOrDefault(relativePath);

    // ================================================================== template_ graphics

    /// <summary>The image replacing a template_ image (template_player.png → player.png; it may not exist yet), or null
    /// for other images.</summary>
    public GameImage? ReplacementFor(GameImage image) => image.ReplacementPath is { } rel ? ImageAt(rel) : null;

    /// <summary>True for a template_ image whose replacement was found by the last scan.</summary>
    public bool IsReplaced(GameImage image) => image.ReplacementPath is { } rel && _byPath.ContainsKey(rel);

    // ================================================================== status

    /// <summary>The artist says the image is finished.</summary>
    public void MarkComplete(GameImage image) => SetStatus(image, GameImageStatus.Complete);

    /// <summary>Reopens finished work (only a complete image changes).</summary>
    public void MarkInProgress(GameImage image)
    {
        if (image.Status == GameImageStatus.Complete) SetStatus(image, GameImageStatus.InProgress);
    }

    private void SetStatus(GameImage image, GameImageStatus status)
    {
        if (image.Status == status) return;
        var entry = EntryFor(image);
        var before = (image.Status, entry.Status);
        image.Status = status;
        entry.Status = status;
        try
        {
            Save();
        }
        catch (Exception)
        {
            (image.Status, entry.Status) = before; // not recorded: not changed
            throw;
        }
    }

    /// <summary>After a complete save (the .dnk and the PNG): Not started becomes In progress; other statuses stay.
    /// Remembers the PNG as written, to notice later changes made by other programs.</summary>
    public void RecordSaved(GameImage image)
    {
        var entry = EntryFor(image);
        var before = (image.Status, entry.Status, entry.Length, entry.Written);
        if (image.Status == GameImageStatus.NotStarted) image.Status = GameImageStatus.InProgress;
        entry.Status = image.Status;
        var info = new FileInfo(image.PngPath);
        entry.Length = info.Length;
        entry.Written = info.LastWriteTimeUtc;
        try
        {
            Save();
        }
        catch (Exception)
        {
            (image.Status, entry.Status, entry.Length, entry.Written) = before; // not recorded: not changed
            throw;
        }
    }

    /// <summary>True when the PNG was changed by something else since DoodleNook last wrote it.</summary>
    public bool ChangedOutside(GameImage image)
    {
        if (!_entries.TryGetValue(image.RelativePath, out var e) || e.Written is not { } written) return false;
        var info = new FileInfo(image.PngPath);
        return !info.Exists || info.Length != e.Length || info.LastWriteTimeUtc != written;
    }

    // ================================================================== backups

    /// <summary>Keeps the original PNG (its exact bytes) before it is first replaced. An existing backup is never
    /// overwritten. Throws when the copy can't be made, so the PNG isn't replaced.</summary>
    public void BackUpOriginal(GameImage image)
    {
        if (!BackupOriginals || File.Exists(image.BackupPath) || !File.Exists(image.PngPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(image.BackupPath)!);
        var temp = image.BackupPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.Copy(image.PngPath, temp);
            File.Move(temp, image.BackupPath); // never overwrites: a backup that appeared meanwhile wins
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    // ================================================================== catalog.json

    private Entry EntryFor(GameImage image)
    {
        if (!_entries.TryGetValue(image.RelativePath, out var e)) _entries[image.RelativePath] = e = new Entry();
        return e;
    }

    private void Load()
    {
        if (!File.Exists(CatalogPath)) return;
        try
        {
            var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(CatalogPath), Json) ?? new CatalogFile();
            BackupOriginals = file.BackupOriginals;
            foreach (var (path, entry) in file.Images) _entries[path.Replace('\\', '/')] = entry;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file for reference and start a fresh catalog (statuses start over, nothing is deleted).
            var aside = CatalogPath + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(CatalogPath, aside, overwrite: true);
            }
            catch (Exception)
            {
                // If even the copy fails, the original stays where it is until the next save.
            }
            LoadProblem = $"The progress file couldn't be read ({ex.Message}). A copy was kept as {Path.GetFileName(aside)}; statuses start over.";
        }
    }

    /// <summary>Writes catalog.json safely (a temporary file, then replace).</summary>
    public void Save()
    {
        Directory.CreateDirectory(WorkFolder);
        var file = new CatalogFile
        {
            BackupOriginals = BackupOriginals,
            Images = _entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(e => e.Key, e => e.Value),
        };
        ImageFiles.WriteAtomically(CatalogPath, s => JsonSerializer.Serialize(s, file, Json));
    }

    private sealed class CatalogFile
    {
        public int Version { get; set; } = 1;
        public bool BackupOriginals { get; set; } = true;
        public Dictionary<string, Entry> Images { get; set; } = new();
    }

    private sealed class Entry
    {
        public GameImageStatus Status { get; set; }

        /// <summary>Size of the PNG as DoodleNook last wrote it.</summary>
        public long? Length { get; set; }

        /// <summary>Time (UTC) of the PNG as DoodleNook last wrote it.</summary>
        public DateTime? Written { get; set; }
    }
}
