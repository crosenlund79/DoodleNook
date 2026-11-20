using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Samples;

namespace DoodleNook.Engine.Tests;

public class SampleAnimationTests
{
    [Fact]
    public void Bouncing_ball_is_an_ordinary_editable_animation()
    {
        var doc = SampleAnimations.BouncingBall();
        Assert.True(doc.IsAnimated);
        Assert.Equal(12, doc.FrameCount);
        Assert.Equal(["Ground", "Shadow", "Ball"], doc.Layers.Select(l => l.Name));
        Assert.False(doc.IsDirty);
        Assert.False(doc.History.CanUndo);
        var ground = doc.Layers[0];
        Assert.Equal((0, 11), doc.HoldRange(ground, 0)); // one ground drawing held over every frame
        var ball = doc.Layers[2];
        Assert.Equal(12, Enumerable.Range(0, 12).Select(f => doc.CelAt(ball, f)).Distinct().Count()); // a drawing per frame
        Assert.Equal(160, doc.Animation!.DurationOf(6));
        // The ball really moves: its drawings differ between the contact and the top of the bounce.
        Assert.NotEqual(doc.CelAt(ball, 0)!.ContentBounds, doc.CelAt(ball, 6)!.ContentBounds);
    }

    [Fact]
    public void Pixel_slime_uses_exact_pixels_and_survives_saving()
    {
        var doc = SampleAnimations.PixelSlime();
        Assert.Equal((32, 32), (doc.Width, doc.Height));
        Assert.Null(doc.Paper);
        Assert.Equal(8, doc.FrameCount);
        foreach (var f in Enumerable.Range(0, 8))
        {
            var img = doc.RenderFrame(f, includePaper: false);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    byte a = Pixel.A(img[x, y]);
                    Assert.True(a == 0 || a == 255, $"frame {f} pixel {x},{y} alpha {a}"); // no soft edges
                }
        }
        using var ms = new MemoryStream();
        DnkFile.Write(doc, ms);
        ms.Position = 0;
        var again = DnkFile.Read(ms);
        for (int f = 0; f < 8; f++) Assert.Equal(doc.RenderFrame(f).Pixels, again.RenderFrame(f).Pixels);
    }

    /// <summary>Writes the samples as .dnk files (CI ships them in the package's Samples folder).</summary>
    [Fact]
    public void Write_sample_files_for_the_package()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var outDir = Path.Combine(dir.FullName, "tests", "DoodleNook.Engine.Tests", "out", "samples");
        Directory.CreateDirectory(outDir);
        foreach (var (name, doc) in new[] { ("Bouncing ball.dnk", SampleAnimations.BouncingBall()), ("Pixel slime.dnk", SampleAnimations.PixelSlime()) })
        {
            var path = Path.Combine(outDir, name);
            DnkFile.Save(doc, path, "sample");
            var back = DnkFile.Load(path);
            Assert.Equal(doc.FrameCount, back.FrameCount);
        }

        // Mission 007: a starter asset pack, a GIMP palette and a reference board, each read back as a user would.
        SampleWorkshop.WriteAll(outDir);
        var library = new CreativeLibrary();
        var contents = AssetPack.Import(library, Path.Combine(outDir, SampleWorkshop.PackFile));
        Assert.Equal(new AssetPackContents(3, 2, 1, 1), contents);
        var needles = library.Brushes.Single(b => b.Name == "Pine needles");
        Assert.NotNull(library.Resolve(needles.Settings)?.Tip);
        Assert.NotNull(library.Resolve(library.Brushes.Single(b => b.Name == "Cobbled paint").Settings)?.Texture);
        Assert.Equal(SampleWorkshop.PixelForest, library.Palettes.Single(p => p.Name == "Pixel forest").Colors);
        var (paletteName, colours) = PaletteFiles.Read(Path.Combine(outDir, SampleWorkshop.PaletteFile));
        Assert.Equal("Autumn walk", paletteName);
        Assert.Equal(SampleWorkshop.AutumnWalk, colours);
        var board = ReferenceBoard.Load(Path.Combine(outDir, SampleWorkshop.BoardFile));
        Assert.Equal(5, board.Items.Count);
        Assert.False(board.Items.Any(a => board.Items.Any(b => a != b && a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height)),
            "sample references overlap");
    }

    [Fact]
    public void Sample_pixel_assets_are_hard_edged_and_the_stone_tile_repeats()
    {
        foreach (var img in new[] { SampleWorkshop.PixelHeart(), SampleWorkshop.PixelStones() })
            Assert.All(img.Pixels, p => Assert.True(Pixel.A(p) is 0 or 255));
        var stones = SampleWorkshop.PixelStones();
        // Seamless: the tile's edges continue across the wrap (no hard seam of a different colour family everywhere).
        int differentAcrossSeam = Enumerable.Range(0, 16).Count(y => stones[0, y] != stones[15, y]);
        Assert.True(differentAcrossSeam < 16, "the stone tile has a seam along its whole left/right edge");
    }
}
