using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 012: the bundled palettes by other artists are exactly as published, credited, free of duplicates,
/// and work with Palette Lock and Remap like any other palette.</summary>
public sealed class CuratedPaletteTests
{
    private static PaletteAsset Named(string name) => CuratedPalettes.All.Single(p => p.Name == name);

    [Fact]
    public void Exactly_the_palettes_with_clear_permission_are_bundled_with_their_published_colour_counts()
    {
        var expected = new (string Name, int Count, string Creator)[]
        {
            ("PICO-8 palette", 16, "Lexaloffle Games"), ("Resurrect 64", 64, "Kerrie Lake"), ("ARQ16", 16, "ENDESGA"),
            ("DawnBringer 16", 16, "DawnBringer"), ("CL8UDS", 8, "_Nicola"), ("Japanese Woodblock", 12, "Polyducks"), ("AYY4", 4, "Polyducks"),
            ("Cave4", 4, "Polyducks"), ("A Blood-Steeped Corridor", 5, "Polyducks"), ("1bit Monitor Glow", 2, "Polyducks"),
        };
        Assert.Equal(expected, CuratedPalettes.All.Select(p => (p.Name, p.Colors.Count, p.Credit!.Creator)));
        Assert.All(CuratedPalettes.All, p =>
        {
            Assert.True(p.BuiltIn);
            Assert.Equal(CuratedPalettes.Collection, p.Collection);
            Assert.StartsWith("https://lospec.com/palette-list/", p.Credit!.Source);
            Assert.StartsWith("https://", p.Credit.Evidence);
            Assert.False(string.IsNullOrWhiteSpace(p.Credit.Licence));
        });
    }

    [Fact]
    public void Colours_are_exact_in_the_published_order()
    {
        // Spot checks against the published lists (first, last and a middle colour of each).
        Assert.Equal(["#000000", "#1D2B53", "#7E2553", "#008751", "#AB5236", "#5F574F", "#C2C3C7", "#FFF1E8",
                      "#FF004D", "#FFA300", "#FFEC27", "#00E436", "#29ADFF", "#83769C", "#FF77A8", "#FFCCAA"],
            Named("PICO-8 palette").Colors.Select(c => c.ToHex().ToUpperInvariant()));
        Assert.Equal(["#140C1C", "#442434", "#30346D", "#4E4A4E", "#854C30", "#346524", "#D04648", "#757161",
                      "#597DCE", "#D27D2C", "#8595A1", "#6DAA2C", "#D2AA99", "#6DC2CA", "#DAD45E", "#DEEED6"],
            Named("DawnBringer 16").Colors.Select(c => c.ToHex().ToUpperInvariant()));
        var r64 = Named("Resurrect 64").Colors;
        Assert.Equal(ColorRgb.Parse("#2E222F"), r64[0]);
        Assert.Equal(ColorRgb.Parse("#FFFFFF"), r64[9]);
        Assert.Equal(ColorRgb.Parse("#FDCBB0"), r64[63]);
        Assert.Equal([ColorRgb.Parse("#00303B"), ColorRgb.Parse("#FF7777"), ColorRgb.Parse("#FFCE96"), ColorRgb.Parse("#F1F2DA")], Named("AYY4").Colors);
        Assert.All(CuratedPalettes.All, p => Assert.Equal(p.Colors.Count, p.Colors.Distinct().Count()));
    }

    [Fact]
    public void The_library_has_no_duplicate_palettes()
    {
        var all = BuiltInAssets.Palettes;
        Assert.Equal(all.Count, all.Select(p => p.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(p => p.Name.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(all.Count, all.Select(p => string.Join(",", p.Colors.Select(c => c.ToHex()))).Distinct().Count());
        foreach (var p in CuratedPalettes.All) Assert.Contains(p, all);
        Assert.Equal(CuratedPalettes.All.Count, new CreativeLibrary().Palettes.Count(p => p.Credit is not null));
    }

    [Fact]
    public void A_bundled_palette_locks_a_picture_and_remaps_one_like_any_palette()
    {
        var pico = Named("PICO-8 palette");
        var locked = new Document(160, 90, null);
        locked.LockToPalette(new Palette(pico.Name, pico.Colors) { LibraryId = pico.Id });
        Stroke(locked, Paint(new Painting.BrushSettings { Size = 30, Hardness = 0.2, Opacity = 0.7 }, new ColorRgb(90, 140, 220)), Line(10, 45, 150, 45, 2));
        var conformer = new PaletteConformer(pico.Colors);
        var painted = locked.ActiveLayer.Image.Pixels.Where(p => p != 0).ToList();
        Assert.NotEmpty(painted);
        Assert.All(painted, p => Assert.True(conformer.ExactIndex(p) >= 0));

        // A painted picture remapped to Resurrect 64: only its colours, alpha untouched.
        var r64 = Named("Resurrect 64");
        var doc = new Document(64, 64, null);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                doc.ActiveLayer.Image[x, y] = Pixel.FromStraight((byte)(x * 4), (byte)(y * 4), (byte)(255 - x * 2), (byte)(y < 8 ? 120 : 255));
        var alphas = doc.ActiveLayer.Image.Pixels.Select(Pixel.A).ToArray();
        PaletteRemap.Analyse(doc, r64.Colors).Apply(doc, r64.Name, lockAfter: true);
        Assert.Equal(alphas, doc.ActiveLayer.Image.Pixels.Select(Pixel.A).ToArray());
        var target = new PaletteConformer(r64.Colors);
        Assert.All(doc.ActiveLayer.Image.Pixels, p => Assert.True(target.ExactIndex(p) >= 0));
        Assert.Equal(r64.Name, doc.Palette.Name);

        // The lock and its palette survive saving.
        var path = Path.Combine(Path.GetTempPath(), $"dn-lospec-{Guid.NewGuid():N}.dnk");
        try
        {
            DnkFile.Save(doc, path);
            var back = DnkFile.Load(path);
            Assert.True(back.PaletteLocked);
            Assert.Equal(r64.Colors, back.Palette.Colors);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
