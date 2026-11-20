using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Library.ArtKit;

namespace DoodleNook.Engine.Samples;

/// <summary>
/// The Mission 007 sample files shipped in the package's Samples folder, all drawn by code (original, no third-party
/// material): a small starter asset pack to import, a GIMP palette and a reference board to open.
/// </summary>
public static class SampleWorkshop
{
    public const string PackFile = "Woodland starter pack" + AssetPack.Extension;
    public const string PaletteFile = "Autumn walk.gpl";
    public const string BoardFile = "Sample references" + ReferenceBoard.Extension;

    /// <summary>"Autumn walk": warm leaves, bark and a grey sky (the .gpl sample).</summary>
    public static readonly IReadOnlyList<ColorRgb> AutumnWalk =
    [
        C("#3B2A26"), C("#6B4231"), C("#A0522D"), C("#C96F4A"), C("#E9A94D"), C("#F2D27A"),
        C("#7C8B5A"), C("#4F6B4A"), C("#8E9AA6"), C("#D9D4CC"),
    ];

    /// <summary>"Pixel forest": twelve exact colours for small game sprites (in the pack).</summary>
    public static readonly IReadOnlyList<ColorRgb> PixelForest =
    [
        C("#1A1C2C"), C("#2E3B32"), C("#38603F"), C("#5B8C51"), C("#9CC07A"), C("#D8E8A0"),
        C("#5D3A2B"), C("#8A5A3C"), C("#C49A6C"), C("#C8473E"), C("#F2E3C6"), C("#FFFFFF"),
    ];

    /// <summary>A library holding the pack's assets: two stamps, a pattern, three brushes that use them, and a palette.</summary>
    public static CreativeLibrary StarterLibrary(out List<string> brushIds, out List<string> stampIds, out List<string> patternIds, out List<string> paletteIds)
    {
        var lib = new CreativeLibrary();
        var sprig = lib.AddStamp("Pine sprig", "Woodland", PineSprig());
        var heart = lib.AddStamp("Pixel heart", "Pixel art", PixelHeart());
        var stones = lib.AddPattern("Pixel stones", "Game tiles", PixelStones());
        var needles = lib.AddBrush("Pine needles", new BrushSettings
        {
            Size = 72, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.6, TipId = "stamp:" + sprig.Id, TipColors = true,
            Spacing = 0.45, FollowDirection = true, Angle = 90, RotationJitter = 0.06, SizeJitter = 0.2, Scatter = 0.2,
        }, "Paints pine sprigs along the stroke, turned with it");
        var stipple = lib.AddBrush("Stipple shading", new BrushSettings
        {
            Size = 7, Hardness = 0.9, Opacity = 0.9, PressureSize = false, PressureOpacity = true, Spacing = 1.4, Scatter = 3, ScatterCount = 4,
            SizeJitter = 0.5, OpacityJitter = 0.3,
        }, "Clusters of small dots for shading; press harder for darker");
        var cobble = lib.AddBrush("Cobbled paint", new BrushSettings
        {
            Size = 60, Hardness = 0.6, Opacity = 0.85, PressureOpacity = true, Grain = 0.9, GrainScale = 3, TextureId = "pattern:" + stones.Id,
        }, "Soft paint with the Pixel stones pattern: press lightly to see the stones, firmly to fill them in");
        var palette = lib.AddPalette("Pixel forest", "Game", PixelForest);
        brushIds = [needles.Id, stipple.Id, cobble.Id];
        stampIds = [sprig.Id, heart.Id];
        patternIds = [stones.Id];
        paletteIds = [palette.Id];
        return lib;
    }

    /// <summary>Writes the three sample files into a folder.</summary>
    public static void WriteAll(string folder)
    {
        Directory.CreateDirectory(folder);
        var lib = StarterLibrary(out var brushes, out var stamps, out var patterns, out var palettes);
        AssetPack.Export(lib, Path.Combine(folder, PackFile), brushes, stamps, patterns, palettes);
        PaletteFiles.Write(Path.Combine(folder, PaletteFile), "Autumn walk", AutumnWalk);
        References().Save(Path.Combine(folder, BoardFile));
    }

    /// <summary>A board with a few pictures to draw from: a frame of each sample animation, two stamps and a colour study.</summary>
    public static ReferenceBoard References()
    {
        var board = new ReferenceBoard { Background = C("#3A3330") };
        board.Add(SampleAnimations.BouncingBall().RenderFrame(9), "Bouncing ball, frame 10");
        board.Add(Resampler.Resize(SampleAnimations.PixelSlime().RenderFrame(2), 192, 192, Interpolation.Nearest), "Pixel slime (4×)");
        var lib = new CreativeLibrary();
        board.Add(Resampler.Resize(lib.StampImage(BuiltInAssets.Prefix + "mushroom")!, 200, 240, Interpolation.Bilinear), "Mushroom");
        board.Add(PineSprig(), "Pine sprig");
        board.Add(ColourStudy(), "Autumn colour study");
        // Two rows instead of one long strip.
        var items = board.Items;
        double x = 0;
        for (int i = 3; i < items.Count; i++)
        {
            items[i].X = x;
            items[i].Y = items.Take(3).Max(t => t.Y + t.Height) + 24;
            x += items[i].Width + 24;
        }
        return board;
    }

    // ================================================================== pictures

    /// <summary>A pine sprig: a curved twig with needles in two greens.</summary>
    public static RasterImage PineSprig()
    {
        var img = new RasterImage(96, 128);
        string[] greens = ["#2E4A3A", "#3F6A4A", "#5B8C5A"];
        for (int i = 0; i < 16; i++)
        {
            double t = i / 15.0, y = 116 - t * 100, x = 48 + Math.Sin(t * 2.2) * 6;
            double len = 30 - t * 18, side = i % 2 == 0 ? 1 : -1;
            var colour = C(greens[i % greens.Length]);
            Fill(img, Blade((x, y), (x + side * len * 0.5, y - len * 0.25), (x + side * len, y - len * 0.65), 3.2), Solid(colour));
            Fill(img, Blade((x, y), (x - side * len * 0.4, y - len * 0.3), (x - side * len * 0.8, y - len * 0.75), 2.8), Solid(Mix(colour, C("#9CC07A"), 0.3)));
        }
        Fill(img, Blade((50, 124), (44, 70), (48, 12), 4.5), Solid(C("#6B4231")));
        return img;
    }

    /// <summary>A 9 × 8 pixel heart (hard edges, for pixel art).</summary>
    public static RasterImage PixelHeart()
    {
        string[] rows =
        [
            ".RR...RR.",
            "RWRR.RRRR",
            "RWRRRRRRR",
            "RRRRRRRRD",
            ".RRRRRRD.",
            "..RRRRD..",
            "...RRD...",
            "....D....",
        ];
        var colours = new Dictionary<char, ColorRgb> { ['R'] = C("#C8473E"), ['W'] = C("#F2E3C6"), ['D'] = C("#8E2F2A") };
        var img = new RasterImage(rows[0].Length, rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (colours.TryGetValue(rows[y][x], out var c)) img[x, y] = Pixel.Opaque(c);
        return img;
    }

    /// <summary>A seamless 16 × 16 pixel tile of rounded stones in earth (hard edges, for game maps).</summary>
    public static RasterImage PixelStones()
    {
        var img = new RasterImage(16, 16);
        var earth = C("#5D3A2B");
        (double X, double Y, double R, string Colour)[] stones = [(4, 4, 3.4, "#C49A6C"), (12, 5, 3.0, "#8E9AA6"), (8, 12, 3.6, "#A88B6A"), (0, 12, 2.6, "#8E9AA6"), (16, 12, 2.6, "#8E9AA6")];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                var c = earth;
                foreach (var s in stones)
                {
                    double dx = Math.Min(Math.Abs(x + 0.5 - s.X), 16 - Math.Abs(x + 0.5 - s.X)), dy = Math.Min(Math.Abs(y + 0.5 - s.Y), 16 - Math.Abs(y + 0.5 - s.Y));
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= s.R) c = d <= s.R - 1.4 && dx + dy < s.R * 0.6 && y + 0.5 < s.Y ? Mix(C(s.Colour), C("#FFFFFF"), 0.35) : C(s.Colour);
                }
                img[x, y] = Pixel.Opaque(c);
            }
        return img;
    }

    /// <summary>Soft bands of the Autumn walk colours (something to pick colours from with the eyedropper).</summary>
    private static RasterImage ColourStudy()
    {
        var img = new RasterImage(320, 200);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                double t = (x + 18 * Math.Sin(y / 23.0)) / img.Width * (AutumnWalk.Count - 1);
                int i = Math.Clamp((int)t, 0, AutumnWalk.Count - 2);
                img[x, y] = Pixel.Opaque(Mix(AutumnWalk[i], AutumnWalk[i + 1], Math.Clamp(t - i, 0, 1)));
            }
        return img;
    }
}
