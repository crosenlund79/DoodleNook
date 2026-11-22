using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Library;

/// <summary>What happened to one file of an imported folder.</summary>
public enum PaletteImportOutcome
{
    Imported,

    /// <summary>The same colours are already in the library (or earlier in this import).</summary>
    Duplicate,

    /// <summary>Not a palette file (another type, or a text or picture that isn't a palette).</summary>
    Unsupported,

    /// <summary>A palette file that is damaged or has no colours.</summary>
    Invalid,

    /// <summary>The file couldn't be read (access denied, in use…).</summary>
    Error,
}

public sealed record PaletteImportItem(string Path, PaletteImportOutcome Outcome, string? PaletteName = null, string? Detail = null);

/// <summary>What an import will do: every file's outcome, and the palettes to add.</summary>
public sealed record PaletteImportPlan(IReadOnlyList<PaletteImportItem> Items, IReadOnlyList<(PaletteFileData Data, string Collection, string FileName)> ToAdd);

/// <summary>The one summary an import shows: how many palette files were found, imported, skipped as duplicates,
/// not palettes or damaged, and could not be read.</summary>
public sealed record PaletteImportSummary(IReadOnlyList<PaletteImportItem> Items, IReadOnlyList<PaletteAsset> Imported)
{
    public int Found => Items.Count(i => i.Outcome != PaletteImportOutcome.Unsupported);
    public int ImportedCount => Count(PaletteImportOutcome.Imported);
    public int Duplicates => Count(PaletteImportOutcome.Duplicate);
    public int Unsupported => Count(PaletteImportOutcome.Unsupported);
    public int Invalid => Count(PaletteImportOutcome.Invalid);
    public int Errors => Count(PaletteImportOutcome.Error);

    private int Count(PaletteImportOutcome o) => Items.Count(i => i.Outcome == o);
}

/// <summary>
/// Mission 015 — Palettes › Import folder…: every palette file in a folder (and, optionally, its subfolders) goes into
/// the personal library in one go, as independent copies: the folder can be moved or deleted afterwards. Palettes whose
/// colours are already in the library are skipped (so importing the same folder twice adds nothing); a palette with the
/// same name as another but different colours is kept under a numbered name. Nothing existing is ever overwritten.
/// </summary>
public static class PaletteFolderImport
{
    /// <summary>
    /// The files in the folder, in a stable order: the folder's own files before its subfolders', and formats that carry
    /// the palette's name (.gpl, .txt, .ase) before those that don't, so when a palette comes in several formats, the copy
    /// that is kept is the one with its proper name.
    /// </summary>
    public static List<string> Files(string folder, bool includeSubfolders)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        return Directory.EnumerateFiles(folder, "*", options)
            .OrderBy(f => Path.GetRelativePath(folder, f).Count(c => c is '/' or '\\'))
            .ThenBy(f => Path.GetExtension(f).ToLowerInvariant() switch { ".gpl" => 0, ".txt" => 1, ".ase" => 2, ".pal" => 3, ".hex" => 4, ".png" => 5, _ => 6 })
            .ThenBy(f => f, Comparer<string>.Create(DoodleNook.Engine.Files.AnimationImport.NaturalCompare))
            .ToList();
    }

    /// <summary>How many palette files the folder holds (for the summary before importing).</summary>
    public static (int Palettes, int Other, int Subfolders) Count(string folder, bool includeSubfolders)
    {
        var files = Files(folder, includeSubfolders);
        int palettes = files.Count(PaletteFiles.IsPaletteExtension);
        int subfolders = Directory.EnumerateDirectories(folder, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System }).Count();
        return (palettes, files.Count - palettes, subfolders);
    }

    /// <summary>Colours compared as a set: the same colours in another order (or repeated) are the same palette.</summary>
    public static string Key(IEnumerable<ColorRgb> colours) =>
        string.Join(",", colours.Select(c => c.ToHex()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));

    /// <summary>The collection imported palettes go in: the folder's name (not a built-in collection's).</summary>
    public static string CollectionFor(string folder, CreativeLibrary library)
    {
        var name = new DirectoryInfo(folder).Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Imported";
        bool builtIn = library.Palettes.Any(p => p.BuiltIn && string.Equals(p.Collection, name, StringComparison.OrdinalIgnoreCase));
        return builtIn ? name + " (imported)" : name;
    }

    /// <summary>Reads the folder and decides what to import, without changing the library (safe on a background thread:
    /// pass a snapshot of the library's palettes).</summary>
    public static PaletteImportPlan Plan(IReadOnlyList<PaletteAsset> existing, string collection, string folder, bool includeSubfolders,
        IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        var known = existing.GroupBy(p => Key(p.Colors)).ToDictionary(g => g.Key, g => g.First().Name);
        var names = new HashSet<string>(existing.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var items = new List<PaletteImportItem>();
        var add = new List<(PaletteFileData, string, string)>();
        var files = Files(folder, includeSubfolders);
        for (int i = 0; i < files.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var file = files[i];
            progress?.Report(i + 1);
            if (!PaletteFiles.IsPaletteExtension(file))
            {
                items.Add(new PaletteImportItem(file, PaletteImportOutcome.Unsupported, Detail: "not a palette file type"));
                continue;
            }
            PaletteFileData data;
            try
            {
                data = PaletteFiles.ReadFile(file);
            }
            catch (NotAPaletteException ex)
            {
                items.Add(new PaletteImportItem(file, PaletteImportOutcome.Unsupported, Detail: ex.Message));
                continue;
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException or OverflowException)
            {
                items.Add(new PaletteImportItem(file, PaletteImportOutcome.Invalid, Detail: ex.Message));
                continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                items.Add(new PaletteImportItem(file, PaletteImportOutcome.Error, Detail: ex.Message));
                continue;
            }
            var key = Key(data.Colors);
            if (known.TryGetValue(key, out var already))
            {
                items.Add(new PaletteImportItem(file, PaletteImportOutcome.Duplicate, data.Name, $"already in your library as “{already}”"));
                continue;
            }
            // Same name, other colours: both are kept, the new one under a numbered name.
            string name = data.Name.Trim().Length == 0 ? Path.GetFileNameWithoutExtension(file) : data.Name.Trim();
            if (names.Contains(name))
            {
                int n = 2;
                while (names.Contains($"{name} ({n})")) n++;
                name = $"{name} ({n})";
            }
            names.Add(name);
            known[key] = name;
            add.Add((data with { Name = name }, collection, Path.GetFileName(file)));
            items.Add(new PaletteImportItem(file, PaletteImportOutcome.Imported, name, $"{data.Colors.Count} colours"));
        }
        return new PaletteImportPlan(items, add);
    }

    /// <summary>Adds the planned palettes to the library (one save). On the thread that owns the library.</summary>
    public static PaletteImportSummary Commit(CreativeLibrary library, PaletteImportPlan plan) =>
        new(plan.Items, library.AddPalettes(plan.ToAdd));

    /// <summary>Plans and commits in one step.</summary>
    public static PaletteImportSummary Import(CreativeLibrary library, string folder, bool includeSubfolders, IProgress<int>? progress = null,
        CancellationToken cancel = default) =>
        Commit(library, Plan(library.Palettes, CollectionFor(folder, library), folder, includeSubfolders, progress, cancel));

    /// <summary>The summary in one short paragraph.</summary>
    public static string Describe(PaletteImportSummary s)
    {
        var parts = new List<string> { $"{s.Found} palette file{(s.Found == 1 ? "" : "s")} found", $"{s.ImportedCount} imported" };
        if (s.Duplicates > 0) parts.Add($"{s.Duplicates} already in your library (skipped)");
        if (s.Invalid > 0) parts.Add($"{s.Invalid} damaged or empty");
        if (s.Unsupported > 0) parts.Add($"{s.Unsupported} other file{(s.Unsupported == 1 ? "" : "s")} skipped");
        if (s.Errors > 0) parts.Add($"{s.Errors} couldn't be read");
        return string.Join(" · ", parts) + ".";
    }
}
