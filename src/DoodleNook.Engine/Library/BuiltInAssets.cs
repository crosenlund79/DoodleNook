using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Library.ArtKit;

namespace DoodleNook.Engine.Library;

/// <summary>A brush preset: a name, where it is listed, and the brush settings.</summary>
public sealed record BrushPreset(string Id, string Name, string Description, BrushSettings Settings, string Category = BrushCategories.Mine, bool BuiltIn = false);

public static class BrushCategories
{
    public const string Sketch = "Sketch";
    public const string Ink = "Ink";
    public const string Paint = "Paint";
    public const string Texture = "Texture & stamps";
    public const string Mine = "My brushes";

    public static readonly string[] Order = [Sketch, Ink, Paint, Texture, Mine];
}

/// <summary>A reusable picture placed with the stamp tool (or repeated by a stamp brush).</summary>
public sealed record StampAsset(string Id, string Name, string Collection, bool BuiltIn = false);

/// <summary>A repeating tile for pattern fills and brush textures.</summary>
public sealed record PatternAsset(string Id, string Name, string Collection, bool BuiltIn = false);

/// <summary>A named colour collection. Palettes by other artists carry their <see cref="Credit"/>; palettes imported from
/// files remember where they came from and what the file said about them (<see cref="Origin"/>).</summary>
public sealed record PaletteAsset(string Id, string Name, string Collection, IReadOnlyList<ColorRgb> Colors, bool BuiltIn = false,
    PaletteCredit? Credit = null, PaletteOrigin? Origin = null);

/// <summary>Where an imported palette came from: the file's name (the file itself isn't needed afterwards: the colours
/// are copied into the library), and the description, creator, source and licence the file gave, when it gave them.</summary>
public sealed record PaletteOrigin(string FileName, string? Description = null, string? Creator = null, string? Source = null, string? Licence = null);

/// <summary>Who made a palette, where it is published, the terms it is shared under (and where the creator says so),
/// and its mood in a few words.</summary>
public sealed record PaletteCredit(string Creator, string Source, string Licence, string Evidence, string Mood);

/// <summary>
/// DoodleNook's own starter brushes, tips, stamps, patterns and palettes. All are drawn by code (no copied artwork),
/// so they are always available and can never be deleted; users make their own variations instead.
/// </summary>
public static class BuiltInAssets
{
    public const string Prefix = "builtin:";

    // ================================================================== brushes

    public static readonly IReadOnlyList<BrushPreset> Brushes =
    [
        // The nine Mission 005 brushes, unchanged (same ids and settings).
        new("pencil", "Sketch pencil", "Grainy graphite: light pressure lets the paper show",
            new BrushSettings { Size = 6, Hardness = 0.85, Opacity = 0.9, PressureSize = true, PressureOpacity = true, Grain = 0.55 }, BrushCategories.Sketch, true),
        new("graphite", "Graphite pencil", "Fine, hard graphite: grainy when light, solid when you press",
            new BrushSettings { Size = 3.5, Hardness = 0.92, Opacity = 0.85, PressureSize = true, PressureOpacity = true, MinimumSizeFraction = 0.4, Grain = 0.75, GrainScale = 0.7, PressureGamma = 1.2 },
            BrushCategories.Sketch, true),
        new("charcoal", "Charcoal", "A broken, smudgy charcoal stick with rough texture",
            new BrushSettings { Size = 26, Hardness = 0.7, Opacity = 0.85, PressureSize = true, PressureOpacity = true, MinimumSizeFraction = 0.35, Grain = 0.85, GrainScale = 1.3,
                TipId = Prefix + "charcoal", TextureId = "pattern:" + Prefix + "rough", Spacing = 0.12, Angle = 20, RotationJitter = 0.08, SizeJitter = 0.12 },
            BrushCategories.Sketch, true),
        new("chalk", "Chalk", "Rough, textured strokes on grainy paper",
            new BrushSettings { Size = 32, Hardness = 0.55, Opacity = 0.9, PressureSize = true, PressureOpacity = true, Grain = 0.9, GrainScale = 1.6 }, BrushCategories.Sketch, true),
        new("ink", "Ink pen", "Crisp ink with a strong pressure taper",
            new BrushSettings { Size = 9, Hardness = 1, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.02 }, BrushCategories.Ink, true),
        new("liner", "Fine liner", "Even, constant line",
            new BrushSettings { Size = 3, Hardness = 1, Opacity = 1, PressureSize = false }, BrushCategories.Ink, true),
        new("dryink", "Dry ink brush", "Bristle streaks that break up on fast or light strokes",
            new BrushSettings { Size = 22, Hardness = 1, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.3, TipId = Prefix + "bristles", FollowDirection = true, Angle = 90,
                Spacing = 0.04, Grain = 0.35, GrainScale = 1.2, OpacityJitter = 0.1 },
            BrushCategories.Ink, true),
        new("marker", "Marker", "Flat chisel tip: thick one way, thin the other",
            new BrushSettings { Size = 26, Hardness = 0.9, Opacity = 0.55, PressureSize = false, Roundness = 0.3, Angle = 35 }, BrushCategories.Ink, true),
        new("hard", "Hard round", "Solid colour blocking",
            new BrushSettings { Size = 30, Hardness = 0.98, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.25 }, BrushCategories.Paint, true),
        new("soft", "Soft round", "Soft shading and glows",
            new BrushSettings { Size = 70, Hardness = 0.05, Opacity = 0.6, PressureSize = true, PressureOpacity = true }, BrushCategories.Paint, true),
        new("paint", "Soft paint brush", "Soft-edged paint with a hint of canvas weave",
            new BrushSettings { Size = 40, Hardness = 0.45, Opacity = 0.85, PressureSize = true, PressureOpacity = true, MinimumSizeFraction = 0.3, Grain = 0.18,
                TextureId = "pattern:" + Prefix + "canvas" },
            BrushCategories.Paint, true),
        new("watercolor", "Watercolour wash", "Translucent washes that collect at the edges; layer them to deepen",
            new BrushSettings { Size = 56, Hardness = 0.35, Opacity = 0.55, PressureSize = true, PressureOpacity = true, MinimumSizeFraction = 0.5, WetEdges = 0.75, Grain = 0.3,
                GrainScale = 1.4, SizeJitter = 0.05 },
            BrushCategories.Paint, true),
        new("wash", "Flat wash", "Even layers of translucent colour",
            new BrushSettings { Size = 48, Hardness = 0.95, Opacity = 0.7, PressureSize = false, PressureOpacity = true }, BrushCategories.Paint, true),
        new("air", "Airbrush", "Builds up while you hold it, even standing still",
            new BrushSettings { Size = 120, Hardness = 0, Opacity = 1, PressureSize = false, PressureOpacity = true, BuildUp = true, Flow = 0.04, Spray = true }, BrushCategories.Paint, true),
        new("spatter", "Spatter", "Flicked paint: scattered dots of varied size",
            new BrushSettings { Size = 40, Hardness = 1, Opacity = 0.9, PressureSize = false, PressureOpacity = true, TipId = Prefix + "spatter", Spacing = 0.9, Scatter = 0.8,
                ScatterCount = 2, SizeJitter = 0.6, RotationJitter = 1, OpacityJitter = 0.4 },
            BrushCategories.Texture, true),
        new("grass", "Grass tufts", "Paints the Grass tuft stamp along the stroke, varied in size",
            new BrushSettings { Size = 48, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.5, TipId = "stamp:" + Prefix + "grass", TipColors = true, Spacing = 0.55,
                Scatter = 0.35, SizeJitter = 0.35, RotationJitter = 0.06, OpacityJitter = 0.1 },
            BrushCategories.Texture, true),
        new("leaves", "Leaves", "Scattered leaves in their own colours, turned every which way",
            new BrushSettings { Size = 30, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.4, TipId = "stamp:" + Prefix + "leaf", TipColors = true, Spacing = 0.6,
                Scatter = 1, ScatterCount = 2, SizeJitter = 0.4, RotationJitter = 1 },
            BrushCategories.Texture, true),
    ];

    /// <summary>The nine Mission 005/006 brushes (order as in those builds).</summary>
    public static readonly string[] ClassicIds = ["pencil", "ink", "liner", "hard", "soft", "marker", "air", "chalk", "wash"];

    // ================================================================== tips

    private static readonly Dictionary<string, Lazy<BrushTip>> TipCache = new()
    {
        [Prefix + "charcoal"] = new(CharcoalTip),
        [Prefix + "bristles"] = new(BristleTip),
        [Prefix + "spatter"] = new(SpatterTip),
    };

    public static IReadOnlyList<(string Id, string Name)> Tips { get; } =
        [(Prefix + "charcoal", "Charcoal stick"), (Prefix + "bristles", "Bristles"), (Prefix + "spatter", "Spatter dots")];

    public static BrushTip? Tip(string id) => TipCache.TryGetValue(id, out var t) ? t.Value : null;

    private static BrushTip CharcoalTip() => BrushTip.FromMask(64, 40, (x, y) =>
    {
        double u = (x + 0.5 - 32) / 30, v = (y + 0.5 - 20) / 17;
        double edge = 1 + 0.18 * (TileNoise(x * 0.35, y * 0.35, 64, 11) - 0.5) * 2;
        double d = Math.Sqrt(u * u + v * v) / edge;
        if (d >= 1) return 0;
        double speckle = TileNoise(x * 0.9, y * 0.9, 64, 12);
        return (float)(Math.Clamp((1 - d) * 4, 0, 1) * (speckle < 0.28 ? 0.25 : 0.75 + 0.25 * speckle));
    });

    private static BrushTip BristleTip()
    {
        var img = new RasterImage(72, 18);
        var rng = new Random(707);
        for (int i = 0; i < 14; i++)
        {
            double cx = 4 + i * 4.9 + rng.NextDouble() * 1.4, cy = 9 + (rng.NextDouble() - 0.5) * 4;
            double r = 1.3 + rng.NextDouble() * 1.8, a = 0.55 + rng.NextDouble() * 0.45;
            Fill(img, Ellipse(cx, cy, r, r * 1.2), Solid(ColorRgb.Black), a);
        }
        return BrushTip.FromImage(img);
    }

    private static BrushTip SpatterTip()
    {
        var img = new RasterImage(64, 64);
        var rng = new Random(321);
        for (int i = 0; i < 24; i++)
        {
            double ang = rng.NextDouble() * Math.PI * 2, dist = Math.Pow(rng.NextDouble(), 0.7) * 27;
            double r = 1 + Math.Pow(rng.NextDouble(), 2) * 5;
            Fill(img, Ellipse(32 + Math.Cos(ang) * dist, 32 + Math.Sin(ang) * dist, r, r * (0.8 + rng.NextDouble() * 0.4)), Solid(ColorRgb.Black));
        }
        return BrushTip.FromImage(img);
    }

    // ================================================================== stamps

    public static readonly IReadOnlyList<(StampAsset Asset, Func<RasterImage> Draw)> Stamps =
    [
        (new(Prefix + "grass", "Grass tuft", "Nature", true), GrassTuft),
        (new(Prefix + "leaf", "Leaf", "Nature", true), Leaf),
        (new(Prefix + "flower", "Daisy", "Nature", true), Daisy),
        (new(Prefix + "mushroom", "Mushroom", "Nature", true), Mushroom),
        (new(Prefix + "pebble", "Pebble", "Nature", true), Pebble),
        (new(Prefix + "cloud", "Cloud", "Sky", true), Cloud),
        (new(Prefix + "star", "Star", "Sky", true), Star),
    ];

    private static RasterImage GrassTuft()
    {
        var img = new RasterImage(128, 112);
        var rng = new Random(5);
        string[] greens = ["#35553F", "#4F7A45", "#5F8F5A", "#7AA66A", "#9CC07A"];
        for (int i = 0; i < 11; i++)
        {
            double bx = 64 + (i - 5) * 6.5 + rng.NextDouble() * 4, lean = (i - 5) * 6 + (rng.NextDouble() - 0.5) * 18;
            double height = 55 + rng.NextDouble() * 45 - Math.Abs(i - 5) * 4;
            var tip = (bx + lean * 1.4, 108 - height);
            var ctrl = (bx + lean * 0.2, 108 - height * 0.55);
            var colour = C(greens[Math.Min(greens.Length - 1, (i * 3 + 1) % greens.Length)]);
            Fill(img, Blade((bx, 108), ctrl, tip, 9 + rng.NextDouble() * 4), Shade(Mix(colour, C("#C9E39A"), 0.25), Mix(colour, C("#22383F"), 0.3), 108 - height, 108));
        }
        return img;
    }

    private static RasterImage Leaf()
    {
        var img = new RasterImage(80, 120);
        // A leaf: two arcs meeting at the tip and the stem, tilted slightly.
        var shape = Polygon(Enumerable.Range(0, 41).Select(i =>
        {
            double t = i / 40.0 * Math.PI * 2;
            double y = 8 + (1 - Math.Cos(t)) / 2 * 96;
            double x = 40 + Math.Sin(t) * 30 * Math.Pow(Math.Sin((y - 8) / 96 * Math.PI), 0.8);
            return (x, y);
        }).ToArray());
        Fill(img, shape, (x, y) => Mix(C("#9CC07A"), C("#4F7A45"), Math.Clamp((x - 20) / 40 + (y - 10) / 300, 0, 1)));
        Fill(img, Blade((40, 112), (41, 60), (40, 12), 3.2), Solid(C("#D6E8A8")), 0.85);
        for (int k = 0; k < 4; k++)
        {
            double y = 32 + k * 18;
            Fill(img, Blade((40, y + 10), (40 + 10, y + 2), (40 + 20 - k * 2, y - 6), 1.8), Solid(C("#D6E8A8")), 0.6);
            Fill(img, Blade((40, y + 10), (40 - 10, y + 2), (40 - 20 + k * 2, y - 6), 1.8), Solid(C("#D6E8A8")), 0.6);
        }
        Fill(img, Blade((40, 104), (39, 110), (37, 118), 3), Solid(C("#6B4231")));
        return img;
    }

    private static RasterImage Daisy()
    {
        var img = new RasterImage(120, 120);
        for (int i = 0; i < 12; i++)
        {
            double a = i * 30 + 8;
            double rad = a * Math.PI / 180;
            Fill(img, Ellipse(60 + Math.Cos(rad) * 30, 60 + Math.Sin(rad) * 30, 22, 9, a), Shade(C("#FFFFFF"), C("#E8DCEB"), 30, 100));
        }
        Fill(img, Ellipse(60, 60, 17, 17), (x, y) => Mix(C("#F7E58A"), C("#E89A4A"), Math.Clamp((x + y - 100) / 40, 0, 1)));
        var rng = new Random(9);
        for (int i = 0; i < 14; i++)
            Fill(img, Ellipse(60 + (rng.NextDouble() - 0.5) * 20, 60 + (rng.NextDouble() - 0.5) * 20, 1.6, 1.6), Solid(C("#C96F4A")), 0.7);
        return img;
    }

    private static RasterImage Mushroom()
    {
        var img = new RasterImage(100, 120);
        Fill(img, Polygon((40, 60), (60, 60), (63, 112), (37, 112)), Shade(C("#F6EDE0"), C("#C9BFC3"), 60, 112));
        var dome = Ellipse(50, 66, 46, 50);
        Fill(img, (x, y) => y <= 66 && dome(x, y), (x, y) => Mix(C("#E86A5A"), C("#9E3B2E"), Math.Clamp((y - 20) / 50 + (x - 50) / 140, 0, 1)));
        foreach (var (cx, cy, r) in new[] { (34.0, 40.0, 6.0), (56.0, 30.0, 5.0), (70.0, 48.0, 5.5), (46.0, 54.0, 4.0), (24.0, 56.0, 3.5) })
            Fill(img, Ellipse(cx, cy, r, r * 0.85), Solid(C("#FFF6EA")));
        return img;
    }

    private static RasterImage Pebble()
    {
        var img = new RasterImage(120, 84);
        Fill(img, Ellipse(60, 46, 54, 34, -6), (x, y) => Mix(C("#C9BFC3"), C("#6E5C76"), Math.Clamp(((x - 20) * 0.4 + (y - 14)) / 70, 0, 1)));
        Fill(img, Ellipse(44, 32, 16, 7, -18), Solid(C("#EDE6DF")), 0.55);
        return img;
    }

    private static RasterImage Cloud()
    {
        var img = new RasterImage(160, 100);
        var puffs = new[] { (42.0, 62.0, 28.0), (72.0, 44.0, 34.0), (108.0, 52.0, 30.0), (128.0, 68.0, 22.0), (86.0, 72.0, 26.0) };
        bool Inside(double x, double y)
        {
            if (y > 92) return false;
            foreach (var p in puffs)
                if ((x - p.Item1) * (x - p.Item1) + (y - p.Item2) * (y - p.Item2) <= p.Item3 * p.Item3) return true;
            return false;
        }
        Fill(img, Inside, Shade(C("#FFFFFF"), C("#C7D6E0"), 30, 92));
        return img;
    }

    private static RasterImage Star()
    {
        var img = new RasterImage(120, 120);
        var pts = Enumerable.Range(0, 10).Select(i =>
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? 56 : 24;
            return (60 + Math.Cos(a) * r, 62 + Math.Sin(a) * r);
        }).ToArray();
        Fill(img, Polygon(pts), (x, y) => Mix(C("#F7E58A"), C("#E89A4A"), Math.Clamp((y - 10) / 110 + (x - 60) / 300, 0, 1)));
        Fill(img, Ellipse(46, 44, 7, 4, -30), Solid(C("#FFFBEA")), 0.8);
        return img;
    }

    // ================================================================== patterns (all repeat seamlessly)

    public static readonly IReadOnlyList<(PatternAsset Asset, Func<RasterImage> Draw)> Patterns =
    [
        (new(Prefix + "canvas", "Canvas weave", "Textures", true), CanvasWeave),
        (new(Prefix + "rough", "Rough paper", "Textures", true), RoughPaper),
        (new(Prefix + "brick", "Brick wall", "Game tiles", true), Brick),
        (new(Prefix + "grassland", "Grass ground", "Game tiles", true), GrassGround),
        (new(Prefix + "planks", "Wood planks", "Game tiles", true), Planks),
        (new(Prefix + "dots", "Polka dots", "Decorative", true), Dots),
        (new(Prefix + "gingham", "Picnic check", "Decorative", true), Gingham),
        (new(Prefix + "waves", "Waves", "Decorative", true), Waves),
    ];

    private static RasterImage Tile(int w, int h, Func<int, int, ColorRgb> colour)
    {
        var img = new RasterImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = Pixel.Opaque(colour(x, y));
        return img;
    }

    private static RasterImage CanvasWeave() => Tile(64, 64, (x, y) =>
    {
        double warp = 0.5 + 0.5 * Math.Sin((x + 0.5) * Math.PI / 2), weft = 0.5 + 0.5 * Math.Sin((y + 0.5) * Math.PI / 2);
        bool over = ((x / 4) + (y / 4)) % 2 == 0;
        double v = 0.45 + 0.4 * (over ? weft : warp) + 0.12 * (Hash(x, y, 3) - 0.5);
        byte g = (byte)Math.Clamp(v * 255, 0, 255);
        return new ColorRgb(g, g, g);
    });

    private static RasterImage RoughPaper() => Tile(128, 128, (x, y) =>
    {
        double v = 0.55 * TileNoise(x / 8.0, y / 8.0, 16, 21) + 0.3 * TileNoise(x / 3.0, y / 3.0, 43, 22) + 0.15 * Hash(x, y, 23);
        byte g = (byte)Math.Clamp(v * 255, 0, 255);
        return new ColorRgb(g, g, g);
    });

    private static RasterImage Brick() => Tile(64, 64, (x, y) =>
    {
        int row = y / 16, offset = row % 2 == 0 ? 0 : 16;
        int bx = (x + offset) % 64 / 32, by = y % 16, lx = (x + offset) % 32;
        if (by >= 14 || lx >= 30) return C("#C9BFC3");
        double shade = Hash(bx + row * 7, row, 31);
        var baseC = Mix(C("#B7525F"), C("#C96F4A"), shade);
        return Mix(baseC, C("#7A2E3A"), by >= 11 ? 0.35 : by <= 1 ? -0.0 : 0.08 * Hash(x, y, 32));
    });

    private static RasterImage GrassGround() => Tile(32, 32, (x, y) =>
    {
        double h = Hash(x, y, 41);
        if (h > 0.93) return C("#9CC07A");
        if (h < 0.06) return C("#35553F");
        return TileNoise(x / 4.0, y / 4.0, 8, 42) > 0.55 ? C("#5F8F5A") : C("#4F7A45");
    });

    private static RasterImage Planks() => Tile(64, 64, (x, y) =>
    {
        int plank = x / 16, lx = x % 16;
        if (lx == 15) return C("#3B2620");
        double grain = Math.Sin((y + Hash(plank, 0, 51) * 64) * 0.35 + Math.Sin(y * 0.09 + plank) * 2) * 0.5 + 0.5;
        var baseC = Mix(C("#C98D5C"), C("#9A6440"), Hash(plank, 1, 52) * 0.6);
        return Mix(baseC, C("#6B4231"), grain * 0.35 + (lx == 0 ? 0.2 : 0));
    });

    private static RasterImage Dots()
    {
        var img = Tile(48, 48, (_, _) => C("#F6DDBF"));
        foreach (var (cx, cy) in new[] { (12.0, 12.0), (36.0, 36.0) })
            Fill(img, (x, y) =>
            {
                // Distance on the wrapping tile, so dots crossing an edge continue on the other side.
                double dx = Math.Abs(x - cx), dy = Math.Abs(y - cy);
                dx = Math.Min(dx, 48 - dx);
                dy = Math.Min(dy, 48 - dy);
                return dx * dx + dy * dy <= 6.5 * 6.5;
            }, Solid(C("#E88A9A")));
        return img;
    }

    private static RasterImage Gingham() => Tile(32, 32, (x, y) =>
    {
        bool a = x % 16 < 8, b = y % 16 < 8;
        return a && b ? C("#B7525F") : a || b ? C("#E8A2A8") : C("#FFF6EA");
    });

    private static RasterImage Waves() => Tile(64, 32, (x, y) =>
    {
        double wave = 16 + 5 * Math.Sin((x + 0.5) * Math.PI * 2 / 64) + (y >= 16 ? 0 : 0);
        double band = (y + 32 - wave) % 16;
        return band < 3 ? C("#C7E1E8") : band < 9 ? C("#5F8FA8") : C("#3F6A82");
    });

    // ================================================================== palettes

    public static readonly IReadOnlyList<PaletteAsset> Palettes =
    [
        new(Prefix + "nook32", "Nook 32", "DoodleNook", Documents.Palette.Default().Colors, true),
        new(Prefix + "meadow", "Meadow", "Nature", Hex("#22383F", "#35553F", "#5F8F5A", "#9CC07A", "#D6E8A8", "#F7E58A", "#E89A4A", "#6B4231"), true),
        new(Prefix + "dusk", "Dusk", "Nature", Hex("#1B1622", "#4A3B52", "#6C5BA8", "#B7525F", "#E88A9A", "#E89A4A", "#F2C14E", "#F6DDBF"), true),
        new(Prefix + "seaside", "Seaside", "Nature", Hex("#22383F", "#3F6A82", "#5F8FA8", "#8FB8CC", "#C7E1E8", "#F6DDBF", "#E8B98A", "#FFFFFF"), true),
        new(Prefix + "cosyui", "Cosy UI", "Interface", Hex("#211C1A", "#2B2522", "#3A322E", "#A99D91", "#F3ECE4", "#E9A94D", "#7AA66A", "#C96F4A"), true),
        .. CuratedPalettes.All,
    ];

    private static ColorRgb[] Hex(params string[] hex) => hex.Select(ColorRgb.Parse).ToArray();
}
