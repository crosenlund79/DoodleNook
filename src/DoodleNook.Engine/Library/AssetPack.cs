using System.IO.Compression;
using System.Text.Json;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Library;

/// <summary>What an asset pack contains (or what was imported from one).</summary>
public sealed record AssetPackContents(int Brushes, int Stamps, int Patterns, int Palettes)
{
    public int Total => Brushes + Stamps + Patterns + Palettes;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Brushes > 0) parts.Add(Plural(Brushes, "brush", "brushes"));
        if (Stamps > 0) parts.Add(Plural(Stamps, "stamp", "stamps"));
        if (Patterns > 0) parts.Add(Plural(Patterns, "pattern", "patterns"));
        if (Palettes > 0) parts.Add(Plural(Palettes, "palette", "palettes"));
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}

/// <summary>
/// A DoodleNook asset pack (<c>.dnkpack</c>): a ZIP holding <c>pack.json</c> and the pictures in <c>images/</c>.
/// Exporting a brush brings along the tip, stamp and pattern pictures it uses, so it works on another computer.
/// Importing never replaces anything: every imported asset gets a new id (references inside the pack are updated),
/// and built-in assets are referred to, not copied.
/// <code>
/// { "format": "DoodleNook asset pack", "version": 1,
///   "brushes":  [ { "id", "name", "description", "settings": { …brush settings… } } ],
///   "stamps":   [ { "id", "name", "collection" } ],  "patterns": [ … ],  "tips": [ "id", … ],
///   "palettes": [ { "id", "name", "collection", "colors": [ "#RRGGBB", … ] } ] }
/// </code>
/// </summary>
public static class AssetPack
{
    public const string Extension = ".dnkpack";
    public const string FormatName = "DoodleNook asset pack";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AssetPackContents Export(CreativeLibrary library, string path, IEnumerable<string>? brushIds = null, IEnumerable<string>? stampIds = null,
        IEnumerable<string>? patternIds = null, IEnumerable<string>? paletteIds = null)
    {
        var brushes = (brushIds ?? []).Select(library.FindBrush).OfType<BrushPreset>().ToList();
        var stamps = new HashSet<string>((stampIds ?? []).Where(id => library.FindStamp(id) is { BuiltIn: false }));
        var patterns = new HashSet<string>((patternIds ?? []).Where(id => library.FindPattern(id) is { BuiltIn: false }));
        var tips = new HashSet<string>();
        // Bring along what the brushes use (built-in parts exist everywhere and stay references).
        foreach (var b in brushes)
        {
            if (b.Settings.TipId is { } t)
            {
                if (t.StartsWith("stamp:", StringComparison.Ordinal) && library.FindStamp(t[6..]) is { BuiltIn: false }) stamps.Add(t[6..]);
                else if (t.StartsWith("tip:", StringComparison.Ordinal)) tips.Add(t[4..]);
            }
            if (b.Settings.TextureId is { } x && x.StartsWith("pattern:", StringComparison.Ordinal) && library.FindPattern(x[8..]) is { BuiltIn: false })
                patterns.Add(x[8..]);
        }
        var palettes = (paletteIds ?? []).Select(library.FindPalette).OfType<PaletteAsset>().ToList();

        var manifest = new PackFile
        {
            Format = FormatName,
            Version = 1,
            Brushes = brushes.Select(b => new CreativeLibrary.BrushDto { Id = b.Id, Name = b.Name, Description = b.Description, Settings = b.Settings }).ToList(),
            Stamps = stamps.Select(library.FindStamp).OfType<StampAsset>().Select(s => new CreativeLibrary.AssetDto { Id = s.Id, Name = s.Name, Collection = s.Collection }).ToList(),
            Patterns = patterns.Select(library.FindPattern).OfType<PatternAsset>().Select(p => new CreativeLibrary.AssetDto { Id = p.Id, Name = p.Name, Collection = p.Collection }).ToList(),
            Tips = tips.ToList(),
            Palettes = palettes.Select(p => new CreativeLibrary.PaletteDto { Id = p.Id, Name = p.Name, Collection = p.Collection, Colors = p.Colors.Select(c => c.ToHex()).ToList() }).ToList(),
        };
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var s = zip.CreateEntry("pack.json").Open()) JsonSerializer.Serialize(s, manifest, Json);
            foreach (var id in manifest.Stamps.Select(s => s.Id!).Concat(manifest.Patterns.Select(p => p.Id!)).Concat(tips))
            {
                var img = library.ImageForExport(id) ?? throw new InvalidOperationException("A picture of the pack is missing from the library.");
                using var e = zip.CreateEntry($"images/{id}.png", CompressionLevel.NoCompression).Open();
                e.Write(PngCodec.Encode(img));
            }
        }
        AnimationExport.WriteAtomically(path, ms.ToArray());
        return new AssetPackContents(manifest.Brushes.Count, manifest.Stamps.Count, manifest.Patterns.Count, manifest.Palettes.Count);
    }

    /// <summary>Adds everything in a pack to the library. Throws <see cref="InvalidDataException"/> for files that are not
    /// valid asset packs (nothing is imported then).</summary>
    public static AssetPackContents Import(CreativeLibrary library, string path)
    {
        if (library.ReadOnly) throw new InvalidOperationException("The library is read-only (it was saved by a newer DoodleNook).");
        PackFile manifest;
        var images = new Dictionary<string, RasterImage>();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("pack.json") ?? throw new InvalidDataException("This is not a DoodleNook asset pack (no pack.json).");
            using (var s = entry.Open()) manifest = JsonSerializer.Deserialize<PackFile>(s, Json) ?? throw new InvalidDataException("The pack description is empty.");
            if (manifest.Format != FormatName) throw new InvalidDataException("This is not a DoodleNook asset pack.");
            if (manifest.Version > 1) throw new InvalidDataException("This pack was made by a newer DoodleNook.");
            foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("images/", StringComparison.Ordinal) && e.FullName.EndsWith(".png", StringComparison.Ordinal)))
            {
                using var s = e.Open();
                using var buffer = new MemoryStream();
                s.CopyTo(buffer);
                images[Path.GetFileNameWithoutExtension(e.FullName)] = PngCodec.Decode(buffer.ToArray());
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException($"The asset pack could not be read ({ex.Message}).", ex);
        }
        RasterImage Image(string? id) => id is not null && images.TryGetValue(id, out var img)
            ? img : throw new InvalidDataException("A picture listed in the pack is missing.");

        // Check everything before changing the library, then add it all with new ids.
        foreach (var s in manifest.Stamps ?? []) Image(s.Id);
        foreach (var p in manifest.Patterns ?? []) Image(p.Id);
        foreach (var t in manifest.Tips ?? []) Image(t);
        var map = new Dictionary<string, string>();
        foreach (var s in manifest.Stamps ?? [])
        {
            var id = map[s.Id!] = CreativeLibrary.NewId();
            library.ImportStamp(new StampAsset(id, s.Name ?? "Stamp", s.Collection ?? "Imported"), Image(s.Id));
        }
        foreach (var p in manifest.Patterns ?? [])
        {
            var id = map[p.Id!] = CreativeLibrary.NewId();
            library.ImportPattern(new PatternAsset(id, p.Name ?? "Pattern", p.Collection ?? "Imported"), Image(p.Id));
        }
        foreach (var t in manifest.Tips ?? [])
        {
            var id = map[t] = CreativeLibrary.NewId();
            library.ImportTip(id, Image(t));
        }
        int brushes = 0;
        foreach (var b in manifest.Brushes ?? [])
        {
            if (b.Settings is null) continue;
            var settings = b.Settings.Normalized();
            settings = settings with { TipId = Remap(settings.TipId, map), TextureId = Remap(settings.TextureId, map) };
            library.ImportBrush(new BrushPreset(CreativeLibrary.NewId(), b.Name ?? "Brush", b.Description ?? "", settings, BrushCategories.Mine));
            brushes++;
        }
        int palettes = 0;
        foreach (var p in manifest.Palettes ?? [])
        {
            var colours = (p.Colors ?? []).Select(h => ColorRgb.TryParse(h, out var c) ? (ColorRgb?)c : null).OfType<ColorRgb>().ToList();
            library.ImportPalette(new PaletteAsset(CreativeLibrary.NewId(), p.Name ?? "Palette", p.Collection ?? "Imported", colours));
            palettes++;
        }
        library.SaveAfterImport();
        return new AssetPackContents(brushes, manifest.Stamps?.Count ?? 0, manifest.Patterns?.Count ?? 0, palettes);
    }

    private static string? Remap(string? reference, Dictionary<string, string> map)
    {
        if (reference is null) return null;
        int colon = reference.IndexOf(':');
        if (colon < 0) return reference;
        string kind = reference[..(colon + 1)], id = reference[(colon + 1)..];
        return map.TryGetValue(id, out var to) ? kind + to : reference;
    }

    private sealed class PackFile
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public List<CreativeLibrary.BrushDto>? Brushes { get; set; }
        public List<CreativeLibrary.AssetDto>? Stamps { get; set; }
        public List<CreativeLibrary.AssetDto>? Patterns { get; set; }
        public List<string>? Tips { get; set; }
        public List<CreativeLibrary.PaletteDto>? Palettes { get; set; }
    }
}

/// <summary>Palette files other programs use: GIMP palettes (<c>.gpl</c>) and plain lists of hex colours (<c>.hex</c>,
/// one <c>RRGGBB</c> per line, as palette sites offer them). Colours are kept exactly.</summary>
public static partial class PaletteFiles
{
    /// <summary>A palette file's name and colours (any format <see cref="ReadFile"/> understands).</summary>
    public static (string Name, List<ColorRgb> Colors) Read(string path)
    {
        var data = ReadFile(path);
        return (data.Name, data.Colors.ToList());
    }

    public static void Write(string path, string name, IReadOnlyList<ColorRgb> colors)
    {
        string text;
        if (path.EndsWith(".hex", StringComparison.OrdinalIgnoreCase))
            text = string.Join("\n", colors.Select(c => c.ToHex().TrimStart('#').ToLowerInvariant())) + "\n";
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("GIMP Palette\nName: ").Append(name.Replace('\n', ' ')).Append("\nColumns: 8\n#\n");
            foreach (var c in colors) sb.Append($"{c.R,3} {c.G,3} {c.B,3}\t{c.ToHex()}\n");
            text = sb.ToString();
        }
        AnimationExport.WriteAtomically(path, System.Text.Encoding.UTF8.GetBytes(text));
    }
}
