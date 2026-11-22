using System.Text.Json;
using System.Text.Json.Serialization;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Library;

/// <summary>
/// The artist's own brushes, stamps, patterns, brush tips and palettes, next to DoodleNook's built-in ones.
/// <list type="bullet">
/// <item>Stored in one folder (normally %APPDATA%\DoodleNook\Library): <c>library.json</c> describes everything, and
/// every picture is DoodleNook's own copy in <c>images/</c>, so moving or deleting the original file breaks nothing.</item>
/// <item>Every change is written at once, safely (temporary file, then replace). Built-in assets live in code and can't
/// be deleted or changed; editing one saves a copy.</item>
/// <item>A damaged <c>library.json</c> is set aside (renamed, never overwritten) and reported; a missing picture only
/// affects that asset. A file from a newer DoodleNook is opened read-only so nothing it contains is lost.</item>
/// </list>
/// With no folder (tests, smoke test) the library lives in memory only.
/// </summary>
public sealed class CreativeLibrary
{
    public const int FormatVersion = 1;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly List<BrushPreset> _brushes = new();
    private readonly List<StampAsset> _stamps = new();
    private readonly List<PatternAsset> _patterns = new();
    private readonly List<string> _tips = new();
    private readonly List<PaletteAsset> _palettes = new();
    private readonly HashSet<string> _favBrushes = new(), _favStamps = new(), _favPatterns = new();
    private readonly Dictionary<string, RasterImage> _memoryImages = new();
    private readonly Dictionary<string, RasterImage?> _imageCache = new();
    private readonly Dictionary<string, BrushTip?> _tipCache = new();
    private readonly Dictionary<string, BrushTexture?> _textureCache = new();
    private readonly List<string> _problems = new();

    public CreativeLibrary(string? root = null)
    {
        Root = root;
        if (root is not null) Load();
    }

    /// <summary>The standard place for the library.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DoodleNook", "Library");

    public string? Root { get; }

    /// <summary>True when the library file came from a newer DoodleNook: nothing is written, so nothing is lost.</summary>
    public bool ReadOnly { get; private set; }

    /// <summary>What could not be loaded (shown to the user once).</summary>
    public IReadOnlyList<string> LoadProblems => _problems;

    /// <summary>Raised after any change.</summary>
    public event Action? Changed;

    private string? FilePath => Root is null ? null : Path.Combine(Root, "library.json");
    private string? ImagePath(string id) => Root is null ? null : Path.Combine(Root, "images", id + ".png");

    public static string NewId() => "u" + Guid.NewGuid().ToString("N")[..12];

    // ================================================================== brushes

    public IReadOnlyList<BrushPreset> Brushes => [.. BuiltInAssets.Brushes, .. _brushes];

    public BrushPreset? FindBrush(string? id) => id is null ? null : Brushes.FirstOrDefault(b => b.Id == id);

    public BrushPreset AddBrush(string name, BrushSettings settings, string? description = null)
    {
        var b = new BrushPreset(NewId(), CleanName(name, "My brush"), description ?? "", settings.Normalized(), BrushCategories.Mine);
        _brushes.Add(b);
        Save();
        return b;
    }

    /// <summary>A copy of any brush (built-in or own) in My brushes, named "… copy".</summary>
    public BrushPreset DuplicateBrush(string id, string? name = null)
    {
        var src = FindBrush(id) ?? throw new ArgumentException("No such brush.", nameof(id));
        return AddBrush(name ?? src.Name + " copy", src.Settings, src.Description);
    }

    /// <summary>Saves new settings into one of the user's brushes (built-in brushes can't be changed).</summary>
    public bool UpdateBrush(string id, BrushSettings settings, string? description = null) =>
        Replace(_brushes, id, b => b with { Settings = settings.Normalized(), Description = description ?? b.Description });

    public bool RenameBrush(string id, string name) => Replace(_brushes, id, b => b with { Name = CleanName(name, b.Name) });

    public bool DeleteBrush(string id)
    {
        if (_brushes.RemoveAll(b => b.Id == id) == 0) return false;
        _favBrushes.Remove(id);
        Save();
        return true;
    }

    public bool IsFavoriteBrush(string id) => _favBrushes.Contains(id);

    public void SetFavoriteBrush(string id, bool favorite) => SetFavorite(_favBrushes, id, favorite);

    // ================================================================== stamps

    public IReadOnlyList<StampAsset> Stamps => [.. BuiltInAssets.Stamps.Select(s => s.Asset), .. _stamps];

    public StampAsset? FindStamp(string? id) => id is null ? null : Stamps.FirstOrDefault(s => s.Id == id);

    /// <summary>Adds a picture as a stamp (empty borders trimmed). The picture is copied; the source stays as it is.</summary>
    public StampAsset AddStamp(string name, string? collection, RasterImage image)
    {
        var trimmed = Trim(image) ?? throw new ArgumentException("The picture is empty.", nameof(image));
        var s = new StampAsset(NewId(), CleanName(name, "Stamp"), CleanName(collection, "My stamps"));
        StoreImage(s.Id, trimmed);
        _stamps.Add(s);
        Save();
        return s;
    }

    public RasterImage? StampImage(string id) => AssetImage(id, BuiltInAssets.Stamps.FirstOrDefault(s => s.Asset.Id == id).Draw);

    public bool RenameStamp(string id, string name) => Replace(_stamps, id, s => s with { Name = CleanName(name, s.Name) });

    public bool MoveStamp(string id, string collection) => Replace(_stamps, id, s => s with { Collection = CleanName(collection, s.Collection) });

    public bool DeleteStamp(string id)
    {
        if (_stamps.RemoveAll(s => s.Id == id) == 0) return false;
        _favStamps.Remove(id);
        DeleteImage(id);
        Save();
        return true;
    }

    public bool IsFavoriteStamp(string id) => _favStamps.Contains(id);

    public void SetFavoriteStamp(string id, bool favorite) => SetFavorite(_favStamps, id, favorite);

    // ================================================================== patterns

    public IReadOnlyList<PatternAsset> Patterns => [.. BuiltInAssets.Patterns.Select(p => p.Asset), .. _patterns];

    public PatternAsset? FindPattern(string? id) => id is null ? null : Patterns.FirstOrDefault(p => p.Id == id);

    /// <summary>Adds a tile (kept exactly, including its edges: a pattern's size matters).</summary>
    public PatternAsset AddPattern(string name, string? collection, RasterImage tile)
    {
        if (tile.Width < 1 || tile.Height < 1 || tile.Width > 2048 || tile.Height > 2048)
            throw new ArgumentException("A pattern tile must be 1 to 2048 pixels per side.", nameof(tile));
        var p = new PatternAsset(NewId(), CleanName(name, "Pattern"), CleanName(collection, "My patterns"));
        StoreImage(p.Id, tile.Clone());
        _patterns.Add(p);
        Save();
        return p;
    }

    public RasterImage? PatternImage(string id) => AssetImage(id, BuiltInAssets.Patterns.FirstOrDefault(p => p.Asset.Id == id).Draw);

    public bool RenamePattern(string id, string name) => Replace(_patterns, id, p => p with { Name = CleanName(name, p.Name) });

    public bool MovePattern(string id, string collection) => Replace(_patterns, id, p => p with { Collection = CleanName(collection, p.Collection) });

    public bool DeletePattern(string id)
    {
        if (_patterns.RemoveAll(p => p.Id == id) == 0) return false;
        _favPatterns.Remove(id);
        DeleteImage(id);
        Save();
        return true;
    }

    public bool IsFavoritePattern(string id) => _favPatterns.Contains(id);

    public void SetFavoritePattern(string id, bool favorite) => SetFavorite(_favPatterns, id, favorite);

    // ================================================================== brush tips

    /// <summary>Stores a picture as a brush tip and returns its id for <see cref="BrushSettings.TipId"/>.</summary>
    public string AddTip(RasterImage image)
    {
        var trimmed = Trim(image, opaqueToo: true) ?? throw new ArgumentException("The picture is empty.", nameof(image));
        string id = NewId();
        StoreImage(id, trimmed);
        _tips.Add(id);
        Save();
        return "tip:" + id;
    }

    /// <summary>The picture behind a tip id (built-in tips, own tips and stamps), for previews.</summary>
    public BrushTip? Tip(string? tipId)
    {
        if (tipId is null) return null;
        if (_tipCache.TryGetValue(tipId, out var cached)) return cached;
        BrushTip? tip = null;
        if (tipId.StartsWith(BuiltInAssets.Prefix, StringComparison.Ordinal)) tip = BuiltInAssets.Tip(tipId);
        else if (tipId.StartsWith("tip:", StringComparison.Ordinal) && AssetImage(tipId[4..], null) is { } img) tip = BrushTip.FromImage(img);
        else if (tipId.StartsWith("stamp:", StringComparison.Ordinal) && StampImage(tipId[6..]) is { } st) tip = BrushTip.FromImage(st);
        _tipCache[tipId] = tip;
        return tip;
    }

    public BrushTexture? Texture(string? textureId)
    {
        if (textureId is null) return null;
        if (_textureCache.TryGetValue(textureId, out var cached)) return cached;
        BrushTexture? tex = null;
        if (textureId.StartsWith("pattern:", StringComparison.Ordinal) && PatternImage(textureId[8..]) is { } img) tex = BrushTexture.FromImage(img);
        _textureCache[textureId] = tex;
        return tex;
    }

    /// <summary>The loaded tip and texture a brush needs (null when it uses neither).</summary>
    public BrushResources? Resolve(BrushSettings brush)
    {
        if (brush.TipId is null && brush.TextureId is null) return null;
        return new BrushResources(Tip(brush.TipId), Texture(brush.TextureId));
    }

    /// <summary>True when a brush names a tip or texture that is no longer available (it then paints with a round tip
    /// or the paper grain, and the Brush Laboratory says so).</summary>
    public bool HasMissingParts(BrushSettings brush) =>
        brush.TipId is not null && Tip(brush.TipId) is null || brush.TextureId is not null && Texture(brush.TextureId) is null;

    // ================================================================== palettes

    public IReadOnlyList<PaletteAsset> Palettes => [.. BuiltInAssets.Palettes, .. _palettes];

    public PaletteAsset? FindPalette(string? id) => id is null ? null : Palettes.FirstOrDefault(p => p.Id == id);

    public PaletteAsset AddPalette(string name, string? collection, IEnumerable<ColorRgb> colors)
    {
        var p = new PaletteAsset(NewId(), CleanName(name, "Palette"), CleanName(collection, "My palettes"), colors.Take(1024).ToList());
        _palettes.Add(p);
        Save();
        return p;
    }

    /// <summary>Adds imported palettes in one go (one save, one change notification).</summary>
    public IReadOnlyList<PaletteAsset> AddPalettes(IEnumerable<(PaletteFileData Data, string Collection, string FileName)> palettes)
    {
        var added = new List<PaletteAsset>();
        foreach (var (data, collection, file) in palettes)
        {
            var p = new PaletteAsset(NewId(), CleanName(data.Name, "Palette"), CleanName(collection, "Imported"), data.Colors.Take(1024).ToList(),
                Origin: new PaletteOrigin(file, Short(data.Description), Short(data.Creator), Short(data.Source), Short(data.Licence)));
            _palettes.Add(p);
            added.Add(p);
        }
        if (added.Count > 0) Save();
        return added;
    }

    private static string? Short(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim().Length > 400 ? text.Trim()[..400] + "…" : text.Trim();

    public bool UpdatePalette(string id, IEnumerable<ColorRgb> colors) => Replace(_palettes, id, p => p with { Colors = colors.Take(1024).ToList() });

    public bool RenamePalette(string id, string name) => Replace(_palettes, id, p => p with { Name = CleanName(name, p.Name) });

    public bool MovePalette(string id, string collection) => Replace(_palettes, id, p => p with { Collection = CleanName(collection, p.Collection) });

    public PaletteAsset DuplicatePalette(string id)
    {
        var src = FindPalette(id) ?? throw new ArgumentException("No such palette.", nameof(id));
        return AddPalette(src.Name + " copy", src.BuiltIn ? "My palettes" : src.Collection, src.Colors);
    }

    public bool DeletePalette(string id)
    {
        if (_palettes.RemoveAll(p => p.Id == id) == 0) return false;
        Save();
        return true;
    }

    /// <summary>Brushes that use a stamp, tip or pattern (asked before deleting it).</summary>
    public IReadOnlyList<BrushPreset> BrushesUsing(string assetId) =>
        Brushes.Where(b => b.Settings.TipId is { } t && (t == "stamp:" + assetId || t == "tip:" + assetId) || b.Settings.TextureId == "pattern:" + assetId).ToList();

    // ================================================================== internals

    private static string CleanName(string? name, string fallback)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) n = fallback;
        return n.Length > 60 ? n[..60] : n;
    }

    private bool Replace<T>(List<T> list, string id, Func<T, T> change) where T : class
    {
        int i = list.FindIndex(x => IdOf(x) == id);
        if (i < 0) return false;
        list[i] = change(list[i]);
        Save();
        return true;
    }

    private static string IdOf(object x) => x switch
    {
        BrushPreset b => b.Id,
        StampAsset s => s.Id,
        PatternAsset p => p.Id,
        PaletteAsset p => p.Id,
        _ => "",
    };

    private void SetFavorite(HashSet<string> set, string id, bool favorite)
    {
        if (favorite ? set.Add(id) : set.Remove(id)) Save();
    }

    /// <summary>Crops away fully transparent borders (and for tips, white borders of opaque pictures).</summary>
    internal static RasterImage? Trim(RasterImage image, bool opaqueToo = false)
    {
        bool opaque = opaqueToo && image.Pixels.All(p => Pixel.A(p) == 255);
        bool Empty(uint p) => opaque ? Pixel.R(p) > 250 && Pixel.G(p) > 250 && Pixel.B(p) > 250 : Pixel.A(p) == 0;
        int x0 = image.Width, y0 = image.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (!Empty(image[x, y]))
                {
                    x0 = Math.Min(x0, x);
                    x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y);
                    y1 = Math.Max(y1, y);
                }
        if (x1 < 0) return null;
        var r = new RasterImage(x1 - x0 + 1, y1 - y0 + 1);
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
                r[x, y] = image[x + x0, y + y0];
        return r;
    }

    private RasterImage? AssetImage(string id, Func<RasterImage>? builtIn)
    {
        if (_imageCache.TryGetValue(id, out var cached)) return cached;
        RasterImage? img = null;
        if (builtIn is not null) img = builtIn();
        else if (_memoryImages.TryGetValue(id, out var m)) img = m;
        else if (ImagePath(id) is { } path && File.Exists(path))
        {
            try
            {
                img = PngCodec.Decode(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                _problems.Add($"The picture of an asset could not be read ({Path.GetFileName(path)}).");
            }
        }
        _imageCache[id] = img;
        return img;
    }

    private void StoreImage(string id, RasterImage image)
    {
        _imageCache[id] = image;
        if (Root is null || ReadOnly)
        {
            _memoryImages[id] = image;
            return;
        }
        var path = ImagePath(id)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AnimationExport.WriteAtomically(path, PngCodec.Encode(image));
    }

    private void DeleteImage(string id)
    {
        _imageCache.Remove(id);
        _memoryImages.Remove(id);
        foreach (var key in _tipCache.Keys.Where(k => k.EndsWith(":" + id, StringComparison.Ordinal)).ToList()) _tipCache.Remove(key);
        foreach (var key in _textureCache.Keys.Where(k => k.EndsWith(":" + id, StringComparison.Ordinal)).ToList()) _textureCache.Remove(key);
        if (Root is null || ReadOnly || ImagePath(id) is not { } path) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // An orphaned picture does no harm.
        }
    }

    private void Save()
    {
        Changed?.Invoke();
        if (Root is null || ReadOnly) return;
        var file = new LibraryFile
        {
            Version = FormatVersion,
            Brushes = _brushes.Select(b => new BrushDto { Id = b.Id, Name = b.Name, Description = b.Description, Settings = b.Settings }).ToList(),
            Stamps = _stamps.Select(s => new AssetDto { Id = s.Id, Name = s.Name, Collection = s.Collection }).ToList(),
            Patterns = _patterns.Select(p => new AssetDto { Id = p.Id, Name = p.Name, Collection = p.Collection }).ToList(),
            Tips = _tips.ToList(),
            Palettes = _palettes.Select(p => new PaletteDto
            {
                Id = p.Id, Name = p.Name, Collection = p.Collection, Colors = p.Colors.Select(c => c.ToHex()).ToList(),
                ImportedFrom = p.Origin?.FileName, Description = p.Origin?.Description, Creator = p.Origin?.Creator, Source = p.Origin?.Source, Licence = p.Origin?.Licence,
            }).ToList(),
            Favorites = new FavoritesDto { Brushes = _favBrushes.ToList(), Stamps = _favStamps.ToList(), Patterns = _favPatterns.ToList() },
        };
        Directory.CreateDirectory(Root);
        AnimationExport.WriteAtomically(FilePath!, JsonSerializer.SerializeToUtf8Bytes(file, Json));
    }

    private void Load()
    {
        var path = FilePath!;
        if (!File.Exists(path)) return;
        LibraryFile? file;
        try
        {
            file = JsonSerializer.Deserialize<LibraryFile>(File.ReadAllBytes(path), Json);
            if (file is null) throw new JsonException("Empty library file.");
        }
        catch (Exception ex)
        {
            // Keep the damaged file for inspection; start with the built-in assets.
            var aside = Path.Combine(Root!, $"library.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try
            {
                File.Move(path, aside);
            }
            catch (Exception)
            {
                ReadOnly = true; // couldn't move it: don't overwrite it either
            }
            _problems.Add($"Your library file could not be read ({ex.Message}). It was kept as {Path.GetFileName(aside)}; built-in brushes and assets are available.");
            return;
        }
        if (file.Version > FormatVersion)
        {
            ReadOnly = true;
            _problems.Add("Your library was saved by a newer DoodleNook. It is opened read-only, so nothing in it is lost.");
        }
        foreach (var b in file.Brushes ?? [])
            if (!string.IsNullOrEmpty(b.Id) && b.Settings is not null)
                _brushes.Add(new BrushPreset(b.Id, CleanName(b.Name, "My brush"), b.Description ?? "", b.Settings.Normalized(), BrushCategories.Mine));
        foreach (var s in file.Stamps ?? [])
            if (!string.IsNullOrEmpty(s.Id)) _stamps.Add(new StampAsset(s.Id, CleanName(s.Name, "Stamp"), CleanName(s.Collection, "My stamps")));
        foreach (var p in file.Patterns ?? [])
            if (!string.IsNullOrEmpty(p.Id)) _patterns.Add(new PatternAsset(p.Id, CleanName(p.Name, "Pattern"), CleanName(p.Collection, "My patterns")));
        _tips.AddRange((file.Tips ?? []).Where(t => !string.IsNullOrEmpty(t)));
        foreach (var p in file.Palettes ?? [])
            if (!string.IsNullOrEmpty(p.Id))
                _palettes.Add(new PaletteAsset(p.Id, CleanName(p.Name, "Palette"), CleanName(p.Collection, "My palettes"),
                    (p.Colors ?? []).Select(h => ColorRgb.TryParse(h, out var c) ? (ColorRgb?)c : null).OfType<ColorRgb>().ToList(),
                    Origin: p.ImportedFrom is { Length: > 0 } from ? new PaletteOrigin(from, p.Description, p.Creator, p.Source, p.Licence) : null));
        _favBrushes.UnionWith(file.Favorites?.Brushes ?? []);
        _favStamps.UnionWith(file.Favorites?.Stamps ?? []);
        _favPatterns.UnionWith(file.Favorites?.Patterns ?? []);
        int missing = _stamps.Count(s => !File.Exists(ImagePath(s.Id))) + _patterns.Count(p => !File.Exists(ImagePath(p.Id)));
        if (missing > 0) _problems.Add($"{missing} stamp or pattern picture{(missing == 1 ? " is" : "s are")} missing from the library folder.");
    }

    /// <summary>Copies of assets and their pictures, for asset packs (see <see cref="AssetPack"/>).</summary>
    internal RasterImage? ImageForExport(string id) => AssetImage(id, null) ?? StampImage(id) ?? PatternImage(id);

    internal void ImportTip(string id, RasterImage image)
    {
        StoreImage(id, image);
        _tips.Add(id);
    }

    internal void ImportStamp(StampAsset s, RasterImage image)
    {
        StoreImage(s.Id, image);
        _stamps.Add(s);
    }

    internal void ImportPattern(PatternAsset p, RasterImage image)
    {
        StoreImage(p.Id, image);
        _patterns.Add(p);
    }

    internal void ImportBrush(BrushPreset b) => _brushes.Add(b);

    internal void ImportPalette(PaletteAsset p) => _palettes.Add(p);

    internal void SaveAfterImport() => Save();

    private sealed class LibraryFile
    {
        public int Version { get; set; }
        public List<BrushDto>? Brushes { get; set; }
        public List<AssetDto>? Stamps { get; set; }
        public List<AssetDto>? Patterns { get; set; }
        public List<string>? Tips { get; set; }
        public List<PaletteDto>? Palettes { get; set; }
        public FavoritesDto? Favorites { get; set; }
    }

    internal sealed class BrushDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public BrushSettings? Settings { get; set; }
    }

    internal sealed class AssetDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Collection { get; set; }
    }

    internal sealed class PaletteDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Collection { get; set; }
        public List<string>? Colors { get; set; }

        // Mission 015: where an imported palette came from (left out for other palettes; older versions ignore them).
        public string? ImportedFrom { get; set; }
        public string? Description { get; set; }
        public string? Creator { get; set; }
        public string? Source { get; set; }
        public string? Licence { get; set; }
    }

    private sealed class FavoritesDto
    {
        public List<string>? Brushes { get; set; }
        public List<string>? Stamps { get; set; }
        public List<string>? Patterns { get; set; }
    }
}
