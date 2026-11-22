using System.Text.Json;
using System.Text.Json.Serialization;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Storybook;

/// <summary>How a drawing sits in its panel (never changing the drawing itself).</summary>
public enum PanelFit
{
    /// <summary>The whole drawing, as large as fits; the page shows around it if its shape differs.</summary>
    Fit,

    /// <summary>The panel filled; what sticks out (top and bottom, or the sides) is cropped.</summary>
    Fill,

    /// <summary>At the project's resolution, pixel for pixel, centred (cropped if larger than the panel).</summary>
    Actual,
}

/// <summary>One panel slot of a page: the editable drawing it shows (a .dnk file), how it sits, and an optional note.</summary>
public sealed class StoryPanel
{
    /// <summary>The drawing, relative to the project file's folder with '/' separators (absolute only on another drive).</summary>
    public string? Artwork { get; set; }

    public PanelFit Fit { get; set; } = PanelFit.Fit;

    /// <summary>A short note (storyboards: what happens here).</summary>
    public string? Note { get; set; }

    public StoryPanel Clone() => new() { Artwork = Artwork, Fit = Fit, Note = Note };
}

/// <summary>One page: its layout and one <see cref="StoryPanel"/> per slot of the layout, in order.</summary>
public sealed class StoryPage
{
    public string Id { get; set; } = NewId();

    public string Layout { get; set; } = StoryLayouts.Single;

    public List<StoryPanel> Panels { get; set; } = new();

    /// <summary>Drawings that had no slot after the layout changed to fewer panels: kept here, never dropped.</summary>
    public List<StoryPanel> Unplaced { get; set; } = new();

    internal static string NewId() => "p" + Guid.NewGuid().ToString("N")[..10];

    public StoryPage Clone() => new()
    {
        Layout = Layout, Panels = Panels.Select(p => p.Clone()).ToList(), Unplaced = Unplaced.Select(p => p.Clone()).ToList(),
    };
}

/// <summary>The storybook file could not be read (damaged, not a storybook, or from a newer DoodleNook).</summary>
public sealed class StorybookFormatException(string message) : Exception(message);

/// <summary>
/// Mission 015 — a Storybook project (<c>.donkbook</c>): pages of panels that show ordinary, editable DoodleNook drawings
/// (<c>.dnk</c> files, usually in the "… art" folder beside it). The file is small, readable JSON (documented in
/// docs/iterations/015-game-artists-workshop/README.md): page size in millimetres, the project's resolution (DPI), the
/// margins and gutters, and for each page its layout and which drawing each panel shows and how. Drawings are linked,
/// never copied in or flattened, so one drawing can appear in several panels and editing it updates all of them.
/// <para>The one rule for resolution: every panel uses the same number of pixels per centimetre
/// (<see cref="Pixels(double)"/>), so a larger panel has proportionally more pixels.</para>
/// </summary>
public sealed class StorybookProject
{
    public const string Extension = ".donkbook";
    public const int CurrentVersion = 1;
    public const string FormatName = "DoodleNook Storybook";

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;

    public double PageWidthMm { get; set; } = 210;
    public double PageHeightMm { get; set; } = 297;
    public int Dpi { get; set; } = 300;
    public double MarginMm { get; set; } = 10;
    public double GutterMm { get; set; } = 5;

    /// <summary>The panel border's width in millimetres (0: none).</summary>
    public double BorderMm { get; set; } = 0.6;

    /// <summary>The page colour (#RRGGBB).</summary>
    public string Paper { get; set; } = "#FFFFFF";

    public List<StoryPage> Pages { get; set; } = new();

    /// <summary>Where the project was opened from or saved to (not stored in the file).</summary>
    [JsonIgnore] public string? FilePath { get; set; }

    [JsonIgnore] public string? Folder => FilePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(FilePath));

    [JsonIgnore] public string Name => FilePath is null ? "Storybook" : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>The folder new drawings for panels go in: "&lt;name&gt; art" beside the project file.</summary>
    [JsonIgnore] public string? ArtFolder => Folder is null ? null : Path.Combine(Folder, Name + " art");

    [JsonIgnore] public ColorRgb PaperColour => ColorRgb.TryParse(Paper, out var c) ? c : ColorRgb.White;

    // ================================================================== resolution

    /// <summary>Millimetres to pixels at the project's resolution (the one rule every panel follows).</summary>
    public int Pixels(double mm) => (int)Math.Round(mm / 25.4 * Dpi, MidpointRounding.AwayFromZero);

    /// <summary>Pixels to millimetres at the project's resolution.</summary>
    public double Millimetres(int px) => px * 25.4 / Dpi;

    public (int Width, int Height) PagePixels => (Pixels(PageWidthMm), Pixels(PageHeightMm));

    /// <summary>A panel's place on the page in millimetres.</summary>
    public IReadOnlyList<RectMm> PanelRects(StoryPage page) => StoryLayouts.Get(page.Layout).Rects(PageWidthMm, PageHeightMm, MarginMm, GutterMm);

    /// <summary>A panel's place on the page in project pixels (edges rounded the same way everywhere, so panels never gap or overlap).</summary>
    public PixelRect PanelPixels(RectMm r)
    {
        int x0 = Pixels(r.X), y0 = Pixels(r.Y), x1 = Pixels(r.X + r.Width), y1 = Pixels(r.Y + r.Height);
        return new PixelRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    /// <summary>The canvas size a new drawing for this panel should have: its physical size at the project's resolution.</summary>
    public (int Width, int Height) SuggestedCanvas(RectMm r) => (Math.Max(1, Pixels(r.Width)), Math.Max(1, Pixels(r.Height)));

    /// <summary>
    /// How a drawing of <paramref name="artWidth"/> × <paramref name="artHeight"/> pixels comes out in a panel: its effective
    /// resolution there (pixels per inch of print), and whether that is below the project's.
    /// </summary>
    public ResolutionCheck Check(RectMm panel, int artWidth, int artHeight, PanelFit fit)
    {
        double wIn = panel.Width / 25.4, hIn = panel.Height / 25.4;
        double dpiX = artWidth / wIn, dpiY = artHeight / hIn;
        double effective = fit switch
        {
            PanelFit.Fit => Math.Max(dpiX, dpiY),    // the side that fills the panel sets the scale
            PanelFit.Fill => Math.Min(dpiX, dpiY),   // the side that just covers it does
            _ => Dpi,
        };
        return new ResolutionCheck(effective, Dpi, effective < Dpi * 0.98);
    }

    // ================================================================== pages

    public static StorybookProject Create(double pageWidthMm = 210, double pageHeightMm = 297, int dpi = 300, string firstLayout = StoryLayouts.Comic)
    {
        var p = new StorybookProject
        {
            PageWidthMm = Math.Clamp(pageWidthMm, 20, 2000), PageHeightMm = Math.Clamp(pageHeightMm, 20, 2000), Dpi = Math.Clamp(dpi, 36, 1200),
        };
        p.AddPage(firstLayout);
        return p;
    }

    public StoryPage AddPage(string layout, int? at = null)
    {
        var page = new StoryPage { Layout = StoryLayouts.Get(layout).Id };
        Fill(page);
        Pages.Insert(Math.Clamp(at ?? Pages.Count, 0, Pages.Count), page);
        return page;
    }

    /// <summary>A copy right after the page: the same layout and the same drawings (shared: edits show in both).</summary>
    public StoryPage DuplicatePage(int index)
    {
        var copy = Pages[index].Clone();
        Pages.Insert(index + 1, copy);
        return copy;
    }

    public void DeletePage(int index)
    {
        Pages.RemoveAt(index);
        if (Pages.Count == 0) AddPage(StoryLayouts.Single);
    }

    public void MovePage(int from, int to)
    {
        to = Math.Clamp(to, 0, Pages.Count - 1);
        if (from == to) return;
        var page = Pages[from];
        Pages.RemoveAt(from);
        Pages.Insert(to, page);
    }

    /// <summary>
    /// Changes a page's layout. Panels keep their slots in order; when the new layout has fewer panels, the drawings
    /// left over wait "off the page" (never deleted); empty slots then take waiting drawings, in order.
    /// </summary>
    public void SetLayout(StoryPage page, string layout)
    {
        var l = StoryLayouts.Get(layout);
        page.Layout = l.Id;
        var old = page.Panels;
        page.Panels = old.Take(l.Count).ToList();
        page.Unplaced.AddRange(old.Skip(l.Count).Where(HasContent));
        Fill(page);
        for (int i = 0; i < page.Panels.Count; i++)
        {
            if (page.Panels[i].Artwork is not null) continue;
            int waiting = page.Unplaced.FindIndex(u => u.Artwork is not null);
            if (waiting < 0) break;
            var note = page.Panels[i].Note;
            page.Panels[i] = page.Unplaced[waiting];
            page.Unplaced.RemoveAt(waiting);
            if (!string.IsNullOrWhiteSpace(note) && string.IsNullOrWhiteSpace(page.Panels[i].Note)) page.Panels[i].Note = note;
        }
    }

    private static bool HasContent(StoryPanel p) => p.Artwork is not null || !string.IsNullOrWhiteSpace(p.Note);

    /// <summary>Puts a drawing that is off the page into a slot (whatever was in the slot goes off the page in its place).</summary>
    public void Place(StoryPage page, int unplacedIndex, int slot)
    {
        var incoming = page.Unplaced[unplacedIndex];
        page.Unplaced.RemoveAt(unplacedIndex);
        var outgoing = page.Panels[slot];
        page.Panels[slot] = incoming;
        if (HasContent(outgoing)) page.Unplaced.Insert(unplacedIndex, outgoing);
    }

    /// <summary>Swaps the drawings of two panels on a page.</summary>
    public void Swap(StoryPage page, int a, int b) => (page.Panels[a], page.Panels[b]) = (page.Panels[b], page.Panels[a]);

    private static void Fill(StoryPage page)
    {
        int n = StoryLayouts.Get(page.Layout).Count;
        while (page.Panels.Count < n) page.Panels.Add(new StoryPanel());
    }

    // ================================================================== artwork paths

    /// <summary>The full path of a panel's drawing (null for an empty panel).</summary>
    public string? Resolve(string? artwork)
    {
        if (string.IsNullOrWhiteSpace(artwork)) return null;
        var native = artwork.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(native)) return Path.GetFullPath(native);
        return Folder is null ? Path.GetFullPath(native) : Path.GetFullPath(Path.Combine(Folder, native));
    }

    /// <summary>How a drawing is stored: relative to the project's folder (so a project folder can be moved as a whole),
    /// absolute only when it is on another drive.</summary>
    public string Relate(string path)
    {
        var full = Path.GetFullPath(path);
        if (Folder is null) return full;
        var relative = Path.GetRelativePath(Folder, full);
        return Path.IsPathRooted(relative) ? full : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Every drawing the project uses, once each, with the panels that show it (page index, slot).</summary>
    public Dictionary<string, List<(int Page, int Slot)>> Usage()
    {
        var map = new Dictionary<string, List<(int, int)>>(StringComparer.OrdinalIgnoreCase);
        for (int p = 0; p < Pages.Count; p++)
            for (int s = 0; s < Pages[p].Panels.Count; s++)
                if (Resolve(Pages[p].Panels[s].Artwork) is { } path)
                {
                    if (!map.TryGetValue(path, out var list)) map[path] = list = new List<(int, int)>();
                    list.Add((p, s));
                }
        return map;
    }

    /// <summary>A file name in the art folder that isn't taken yet ("page-02-panel-1.dnk", then "… (2).dnk").</summary>
    public string NewArtworkPath(string baseName)
    {
        var folder = ArtFolder ?? throw new InvalidOperationException("Save the storybook first.");
        Directory.CreateDirectory(folder);
        var safe = string.Concat(baseName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        if (safe.Length == 0) safe = "panel";
        var path = Path.Combine(folder, safe + DnkFile.Extension);
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{safe} ({n}){DnkFile.Extension}");
        return path;
    }

    // ================================================================== the file

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Saves safely: a temporary file, then replaced (a failed save never damages the previous file).</summary>
    public void Save(string? path = null)
    {
        path ??= FilePath ?? throw new InvalidOperationException("Where should the storybook be saved?");
        FilePath = Path.GetFullPath(path);
        Version = CurrentVersion;
        AnimationExport.WriteAtomically(FilePath, System.Text.Encoding.UTF8.GetBytes(ToJson()));
    }

    public static StorybookProject Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>A project from its JSON (a file's contents, or an undo snapshot), belonging at <paramref name="path"/>.</summary>
    public static StorybookProject Parse(string json, string path)
    {
        StorybookProject? p;
        try
        {
            p = JsonSerializer.Deserialize<StorybookProject>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new StorybookFormatException("This storybook file is damaged: " + ex.Message);
        }
        if (p is null || p.Format != FormatName) throw new StorybookFormatException("This file isn't a DoodleNook storybook.");
        if (p.Version > CurrentVersion) throw new StorybookFormatException($"This storybook was saved by a newer DoodleNook (version {p.Version}). Please update DoodleNook to open it.");
        p.FilePath = Path.GetFullPath(path);
        p.PageWidthMm = Math.Clamp(p.PageWidthMm, 20, 2000);
        p.PageHeightMm = Math.Clamp(p.PageHeightMm, 20, 2000);
        p.Dpi = Math.Clamp(p.Dpi, 36, 1200);
        p.MarginMm = Math.Clamp(p.MarginMm, 0, Math.Min(p.PageWidthMm, p.PageHeightMm) / 3);
        p.GutterMm = Math.Clamp(p.GutterMm, 0, 50);
        p.BorderMm = Math.Clamp(p.BorderMm, 0, 10);
        p.Pages ??= new();
        foreach (var page in p.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Id)) page.Id = StoryPage.NewId();
            page.Layout = StoryLayouts.Get(page.Layout).Id;
            page.Panels ??= new();
            page.Unplaced ??= new();
            // A file with more panels than its layout has keeps the extra ones off the page, never drops them.
            int n = StoryLayouts.Get(page.Layout).Count;
            if (page.Panels.Count > n)
            {
                page.Unplaced.AddRange(page.Panels.Skip(n).Where(HasContent));
                page.Panels = page.Panels.Take(n).ToList();
            }
            Fill(page);
        }
        if (p.Pages.Count == 0) p.AddPage(StoryLayouts.Single);
        return p;
    }
}

/// <summary>A rectangle in millimetres from the page's top-left corner.</summary>
public readonly record struct RectMm(double X, double Y, double Width, double Height);

/// <summary>A drawing's effective resolution in a panel, against the project's.</summary>
public readonly record struct ResolutionCheck(double EffectiveDpi, int TargetDpi, bool BelowTarget);

/// <summary>A page layout: panels on a grid of equal columns and rows, each spanning one or more cells.</summary>
public sealed record StoryLayout(string Id, string Name, int Columns, int Rows, IReadOnlyList<(int Col, int Row, int ColSpan, int RowSpan)> Cells)
{
    public int Count => Cells.Count;

    public IReadOnlyList<RectMm> Rects(double pageW, double pageH, double margin, double gutter)
    {
        double contentW = pageW - 2 * margin, contentH = pageH - 2 * margin;
        double cellW = (contentW - (Columns - 1) * gutter) / Columns, cellH = (contentH - (Rows - 1) * gutter) / Rows;
        return Cells.Select(c => new RectMm(margin + c.Col * (cellW + gutter), margin + c.Row * (cellH + gutter),
            c.ColSpan * cellW + (c.ColSpan - 1) * gutter, c.RowSpan * cellH + (c.RowSpan - 1) * gutter)).ToList();
    }
}

/// <summary>The six ready-made page layouts (deliberately a small set: no free-form page design).</summary>
public static class StoryLayouts
{
    public const string Single = "single", TwoStacked = "two-stacked", Four = "four", Six = "six", Cinematic = "cinematic", Comic = "comic";

    public static readonly IReadOnlyList<StoryLayout> All =
    [
        new(Single, "One full-page panel", 1, 1, [(0, 0, 1, 1)]),
        new(TwoStacked, "Two stacked panels", 1, 2, [(0, 0, 1, 1), (0, 1, 1, 1)]),
        new(Four, "Four equal panels", 2, 2, [(0, 0, 1, 1), (1, 0, 1, 1), (0, 1, 1, 1), (1, 1, 1, 1)]),
        new(Six, "Six equal panels", 2, 3, [(0, 0, 1, 1), (1, 0, 1, 1), (0, 1, 1, 1), (1, 1, 1, 1), (0, 2, 1, 1), (1, 2, 1, 1)]),
        new(Cinematic, "Cinematic: one wide over two", 2, 2, [(0, 0, 2, 1), (0, 1, 1, 1), (1, 1, 1, 1)]),
        new(Comic, "Comic: wide, two, wide", 2, 3, [(0, 0, 2, 1), (0, 1, 1, 1), (1, 1, 1, 1), (0, 2, 2, 1)]),
    ];

    /// <summary>A layout by id; an unknown id (a newer file) falls back to the single panel.</summary>
    public static StoryLayout Get(string? id) => All.FirstOrDefault(l => l.Id == id) ?? All[0];
}
