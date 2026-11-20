using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
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
}
