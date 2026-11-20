using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public class AnimationModelTests
{
    private static readonly ColorRgb Red = new(220, 30, 30);

    private static void Dot(Document doc, double x, double y, ColorRgb? c = null, double size = 10) =>
        Stroke(doc, Paint(Hard(size), c ?? Ink), S(x, y), S(x + 1, y));

    private static byte A(Document doc, int x, int y) => Pixel.A(doc.ActiveLayer.Image[x, y]);

    [Fact]
    public void A_new_animation_has_empty_frames_and_drawing_creates_a_cel()
    {
        var doc = Document.NewAnimation(64, 48, null, frames: 3);
        Assert.True(doc.IsAnimated);
        Assert.Equal(3, doc.FrameCount);
        Assert.False(doc.CanUndo);
        Assert.All(doc.ActiveLayer.Track, c => Assert.Null(c));

        Dot(doc, 20, 20);
        var cel = doc.CelAt(doc.ActiveLayer, 0);
        Assert.NotNull(cel);
        Assert.Same(cel!.Image, doc.ActiveLayer.Image);
        Assert.Null(doc.CelAt(doc.ActiveLayer, 1));

        Assert.True(doc.Undo()); // one step: the drawing and its new cel go together
        Assert.Null(doc.CelAt(doc.ActiveLayer, 0));
        Assert.Equal(0, A(doc, 20, 20));
        Assert.True(doc.Redo());
        Assert.Same(cel, doc.CelAt(doc.ActiveLayer, 0));
        Assert.True(A(doc, 20, 20) > 0);
    }

    [Fact]
    public void Turning_a_still_picture_into_an_animation_keeps_every_layer()
    {
        var doc = new Document(40, 30, ColorRgb.White);
        Dot(doc, 10, 10);
        var top = doc.AddLayer("Top");
        Dot(doc, 30, 20, Red);
        var flat = doc.Flatten().Pixels;

        Assert.True(doc.MakeAnimation(8));
        Assert.Equal(1, doc.FrameCount);
        Assert.Equal(8, doc.Animation!.Fps);
        Assert.Equal(flat, doc.Projection.Pixels);
        Assert.NotNull(doc.CelAt(doc.Layers[0], 0));
        Assert.NotNull(doc.CelAt(top, 0));

        Assert.True(doc.Undo());
        Assert.False(doc.IsAnimated);
        Assert.Equal(flat, doc.Flatten().Pixels);
    }

    [Fact]
    public void New_frame_gives_the_active_layer_an_empty_cell_and_holds_the_others()
    {
        var doc = new Document(40, 30, ColorRgb.White, "Background");
        Dot(doc, 5, 5, Red);
        var drawing = doc.AddLayer("Drawing");
        Dot(doc, 20, 15);
        var bg = doc.Layers[0];

        doc.NewFrame(); // also turns the picture into an animation (one undo step)
        Assert.Equal(2, doc.FrameCount);
        Assert.Equal(1, doc.CurrentFrame);
        Assert.Null(doc.CelAt(drawing, 1));
        Assert.Same(doc.CelAt(bg, 0), doc.CelAt(bg, 1)); // the background is held
        Assert.Equal(0, A(doc, 20, 15));
        Assert.Equal(255, Pixel.A(doc.Projection[5, 5]));

        Assert.True(doc.Undo());
        Assert.False(doc.IsAnimated);
    }

    [Fact]
    public void Duplicate_frame_makes_an_independent_copy()
    {
        var doc = Document.NewAnimation(40, 30, null);
        Dot(doc, 10, 10);
        doc.NewFrame(NewFrameContent.Duplicate);
        var first = doc.CelAt(doc.ActiveLayer, 0)!;
        var second = doc.CelAt(doc.ActiveLayer, 1)!;
        Assert.NotSame(first, second);
        Assert.Equal(first.Image.Pixels, second.Image.Pixels);

        Dot(doc, 30, 20); // edit only the copy
        Assert.Equal(0, Pixel.A(first.Image[30, 20]));
        Assert.True(Pixel.A(second.Image[30, 20]) > 0);
    }

    [Fact]
    public void A_held_drawing_is_one_drawing_until_it_is_made_independent()
    {
        var doc = Document.NewAnimation(40, 30, null);
        var layer = doc.ActiveLayer;
        Dot(doc, 10, 10);
        Assert.True(doc.ExtendHold(layer, 0));
        Assert.True(doc.ExtendHold(layer, 0));
        Assert.Equal(3, doc.FrameCount);
        Assert.Equal((0, 2), doc.HoldRange(layer, 1));
        var held = doc.CelAt(layer, 0)!;

        doc.SetCurrentFrame(1);
        Dot(doc, 30, 20); // editing a held drawing changes every frame of the hold
        Assert.True(Pixel.A(held.Image[30, 20]) > 0);
        Assert.Same(held, doc.CelAt(layer, 2));

        Assert.True(doc.MakeIndependent(layer, 1));
        var copy = doc.CelAt(layer, 1)!;
        Assert.NotSame(held, copy);
        Assert.Same(copy, doc.CelAt(layer, 2));
        Assert.Same(held, doc.CelAt(layer, 0));
        Dot(doc, 5, 25);
        Assert.Equal(0, Pixel.A(held.Image[5, 25]));
    }

    [Fact]
    public void Shortening_a_hold_removes_a_trailing_frame_that_shows_nothing_new()
    {
        var doc = Document.NewAnimation(40, 30, null);
        var layer = doc.ActiveLayer;
        Dot(doc, 10, 10);
        doc.ExtendHold(layer, 0);
        doc.ExtendHold(layer, 0);
        Assert.Equal(3, doc.FrameCount);
        Assert.True(doc.ShortenHold(layer, 0));
        Assert.Equal(2, doc.FrameCount);
        Assert.Equal((0, 1), doc.HoldRange(layer, 0));
        Assert.True(doc.Undo());
        Assert.Equal(3, doc.FrameCount);
    }

    [Fact]
    public void Insert_delete_and_move_frames_affect_every_layer_and_undo()
    {
        var doc = Document.NewAnimation(40, 30, null, frames: 1);
        var a = doc.ActiveLayer;
        var b = doc.AddLayer("B");
        doc.SetActiveLayer(a);
        Dot(doc, 5, 5);           // A0
        doc.NewFrame();
        Dot(doc, 10, 10);          // A1
        doc.NewFrame();
        Dot(doc, 15, 15);          // A2
        var a0 = doc.CelAt(a, 0); var a1 = doc.CelAt(a, 1); var a2 = doc.CelAt(a, 2);
        Assert.Equal(3, b.Track.Count);

        Assert.True(doc.MoveFrame(2, 0));
        Assert.Equal([a2, a0, a1], a.Track.ToArray());
        Assert.Equal(0, doc.CurrentFrame);
        Assert.True(doc.DeleteFrame(1));
        Assert.Equal([a2, a1], a.Track.ToArray());
        Assert.Equal(2, b.Track.Count);
        Assert.True(doc.Undo());
        Assert.True(doc.Undo());
        Assert.Equal([a0, a1, a2], a.Track.ToArray());

        Assert.False(Document.NewAnimation(10, 10, null).DeleteFrame(0)); // the last frame stays
    }

    [Fact]
    public void Undo_goes_back_to_the_frame_of_the_change_and_restores_the_right_drawing()
    {
        var doc = Document.NewAnimation(40, 30, null);
        Dot(doc, 10, 10);
        doc.NewFrame(NewFrameContent.Duplicate);
        doc.SetCurrentFrame(0);
        Dot(doc, 30, 20);         // edit frame 0
        var frame0 = doc.CelAt(doc.ActiveLayer, 0)!;
        doc.SetCurrentFrame(1);    // look at frame 1, then undo
        Assert.True(doc.Undo());
        Assert.Equal(0, doc.CurrentFrame);
        Assert.Equal(0, Pixel.A(frame0.Image[30, 20]));
        Assert.True(Pixel.A(doc.CelAt(doc.ActiveLayer, 1)!.Image[10, 10]) > 0);
    }

    [Fact]
    public void Rendering_any_frame_respects_layers_visibility_and_transparency()
    {
        var doc = Document.NewAnimation(30, 20, ColorRgb.White, frames: 1, firstLayerName: "Bg");
        Dot(doc, 3, 3, Red);
        var fg = doc.AddLayer("Fg");
        Dot(doc, 10, 10);
        doc.NewFrame();
        Dot(doc, 20, 10);

        var f0 = doc.RenderFrame(0);
        var f1 = doc.RenderFrame(1);
        Assert.Equal(1, doc.CurrentFrame); // rendering didn't change the current frame
        Assert.NotEqual(Pixel.Opaque(ColorRgb.White), f0[10, 10]);
        Assert.Equal(Pixel.Opaque(ColorRgb.White), f0[20, 10]);
        Assert.Equal(Pixel.Opaque(ColorRgb.White), f1[10, 10]);
        Assert.Equal(f0[3, 3], f1[3, 3]); // the held background is in both
        Assert.Equal(0, Pixel.A(doc.RenderFrame(0, includePaper: false)[28, 18]));

        doc.SetLayerProperties(fg, fg.Properties with { Visible = false });
        Assert.Equal(Pixel.Opaque(ColorRgb.White), doc.RenderFrame(0)[10, 10]);
        Assert.Equal(doc.RenderFrame(1).Pixels, doc.Flatten().Pixels);
    }

    [Fact]
    public void Frame_cache_returns_identical_frames_and_never_shows_stale_drawings()
    {
        var doc = Document.NewAnimation(64, 64, ColorRgb.White);
        Dot(doc, 10, 10);
        doc.NewFrame();
        Dot(doc, 40, 40);
        var f1 = (uint[])doc.Projection.Pixels.Clone();
        doc.SetCurrentFrame(0);
        var f0 = (uint[])doc.Projection.Pixels.Clone();
        doc.SetCurrentFrame(1);
        Assert.Equal(f1, doc.Projection.Pixels);
        Dot(doc, 50, 10); // change frame 1, then come back to it
        doc.SetCurrentFrame(0);
        Assert.Equal(f0, doc.Projection.Pixels);
        doc.SetCurrentFrame(1);
        Assert.NotEqual(Pixel.Opaque(ColorRgb.White), doc.Projection[50, 10]);
    }

    [Fact]
    public void Onion_skins_show_neighbouring_drawings_aligned_tinted_and_never_in_exports()
    {
        var doc = Document.NewAnimation(60, 40, ColorRgb.White);
        Dot(doc, 10, 20);
        doc.NewFrame();
        Dot(doc, 30, 20);
        doc.NewFrame();
        Dot(doc, 50, 20);
        doc.SetCurrentFrame(1);

        var settings = OnionSkinSettings.Default;
        var (before, after) = OnionSkin.Frames(doc, settings);
        Assert.Equal([0], before);
        Assert.Equal([2], after);
        var (overlay, bounds) = OnionSkin.Build(doc, settings);
        Assert.NotNull(overlay);
        Assert.True(Pixel.A(overlay![10, 20]) > 0);  // exactly where the previous drawing is
        Assert.True(Pixel.A(overlay[50, 20]) > 0);
        Assert.Equal(0, Pixel.A(overlay[30, 2]));
        Assert.True(Pixel.R(overlay[10, 20]) > Pixel.B(overlay[10, 20])); // previous: warm
        Assert.True(Pixel.B(overlay[50, 20]) > Pixel.R(overlay[50, 20])); // next: cool

        var exportBefore = doc.RenderFrame(1).Pixels;
        doc.SetOnionSkin(overlay, bounds);
        Assert.NotEqual(Pixel.Opaque(ColorRgb.White), doc.Projection[10, 20]); // visible on screen
        Assert.Equal(exportBefore, doc.Flatten().Pixels);                        // not in exports
        Assert.Equal(exportBefore, doc.RenderFrame(1).Pixels);
        Assert.Equal(0, Pixel.A(doc.ActiveLayer.Image[10, 20]));                // never painted pixels
        doc.SetOnionSkin(null, PixelRect.Empty);
        Assert.Equal(exportBefore, doc.Projection.Pixels);
    }

    [Fact]
    public void Onion_skins_skip_held_frames_and_honour_counts()
    {
        var doc = Document.NewAnimation(40, 20, null);
        var layer = doc.ActiveLayer;
        Dot(doc, 5, 10);
        doc.ExtendHold(layer, 0); // frames 0-1 hold the same drawing
        doc.SetCurrentFrame(1);
        doc.NewFrame();
        Dot(doc, 20, 10);
        doc.NewFrame();
        Dot(doc, 35, 10);
        var s = OnionSkinSettings.Default with { Before = 2, After = 0 };
        var (before, _) = OnionSkin.Frames(doc, s);
        Assert.Equal([2, 1], before); // nearest first; frame 0 is the same drawing as frame 1
        Assert.Empty(OnionSkin.Frames(doc, s with { Enabled = false }).Before);
    }

    [Fact]
    public void Playback_clock_follows_durations_holds_loops_and_ping_pong()
    {
        var clock = new PlaybackClock([100, 100, 200], loop: true, pingPong: false);
        Assert.Equal(0, clock.FrameAt(0));
        Assert.Equal(0, clock.FrameAt(99.9));
        Assert.Equal(1, clock.FrameAt(100));
        Assert.Equal(2, clock.FrameAt(399));
        Assert.Equal(0, clock.FrameAt(400));          // loops
        Assert.Equal(1, clock.FrameAt(400 * 1000 + 150)); // no drift after many passes

        var once = new PlaybackClock([100, 100], loop: false, pingPong: false);
        Assert.Equal(1, once.FrameAt(150));
        Assert.Equal(-1, once.FrameAt(200));

        var pp = new PlaybackClock([100, 100, 100, 100], loop: true, pingPong: true);
        int[] seen = Enumerable.Range(0, 7).Select(i => pp.FrameAt(i * 100 + 50)).ToArray();
        Assert.Equal([0, 1, 2, 3, 2, 1, 0], seen);

        var resume = new PlaybackClock([100, 100, 100], loop: true, pingPong: false, startFrame: 2);
        Assert.Equal(2, resume.FrameAt(0));            // resumes from the paused frame
        Assert.Equal(0, resume.FrameAt(100));

        var doc = Document.NewAnimation(10, 10, null, frames: 3, fps: 10);
        doc.SetFrameDuration(1, 300);
        var c = PlaybackClock.For(doc.Animation!, 0);
        Assert.Equal(500, c.PassDurationMs);
        Assert.Equal(1, c.FrameAt(350));
    }

    [Fact]
    public void Timing_changes_are_undoable_and_clamped()
    {
        var doc = Document.NewAnimation(10, 10, null);
        Assert.True(doc.SetTiming(24, loop: false, pingPong: true));
        Assert.Equal(24, doc.Animation!.Fps);
        Assert.False(doc.Animation.Loop);
        Assert.True(doc.SetTiming(500, true, false));
        Assert.Equal(Animation.MaxFps, doc.Animation.Fps);
        doc.Undo();
        doc.Undo();
        Assert.Equal(Animation.DefaultFps, doc.Animation.Fps);
        Assert.True(doc.Animation.Loop);
    }

    [Fact]
    public void Resizing_an_animation_resizes_every_drawing_and_undoes()
    {
        var doc = Document.NewAnimation(40, 20, null);
        Dot(doc, 10, 10);
        doc.NewFrame();
        Dot(doc, 30, 10);
        ImageOperations.ResizeImage(doc, 80, 40, Interpolation.Nearest);
        Assert.All(doc.AllCels(), c => Assert.Equal(80, c.Image.Width));
        Assert.True(Pixel.A(doc.RenderFrame(0)[20, 20]) > 0);
        Assert.True(Pixel.A(doc.RenderFrame(1)[60, 20]) > 0);
        Assert.Equal(80, doc.ActiveLayer.Image.Width);
        doc.Undo();
        Assert.Equal(40, doc.Width);
        Assert.True(Pixel.A(doc.RenderFrame(0)[10, 10]) > 0);
        ImageOperations.Flip(doc, horizontal: true);
        Assert.True(Pixel.A(doc.RenderFrame(0)[29, 10]) > 0);
    }

    [Fact]
    public void Merge_down_merges_frame_by_frame_and_keeps_holds()
    {
        var doc = Document.NewAnimation(30, 20, null, firstLayerName: "Low");
        var low = doc.ActiveLayer;
        Dot(doc, 5, 5);
        doc.ExtendHold(low, 0); // a held background over 2 frames
        var up = doc.AddLayer("Up");
        Dot(doc, 20, 5, Red);
        doc.SetCurrentFrame(1);
        Dot(doc, 25, 15, Red);
        var f0 = doc.RenderFrame(0).Pixels;
        var f1 = doc.RenderFrame(1).Pixels;

        Assert.True(doc.MergeDown(up));
        Assert.Single(doc.Layers);
        Assert.Equal(f0, doc.RenderFrame(0).Pixels);
        Assert.Equal(f1, doc.RenderFrame(1).Pixels);
        Assert.True(doc.Undo());
        Assert.Equal(2, doc.Layers.Count);
        Assert.Equal(f1, doc.RenderFrame(1).Pixels);
    }

    [Fact]
    public void Duplicated_layers_and_pasted_drawings_are_independent()
    {
        var doc = Document.NewAnimation(30, 20, null);
        Dot(doc, 5, 5);
        doc.ExtendHold(doc.ActiveLayer, 0);
        var original = doc.ActiveLayer;
        var copy = doc.DuplicateLayer(original);
        Assert.NotSame(doc.CelAt(original, 0), doc.CelAt(copy, 0));
        Assert.Same(doc.CelAt(copy, 0), doc.CelAt(copy, 1)); // the hold is kept in the copy

        var clip = Document.CopyOf(doc.CelAt(original, 0)!);
        Assert.True(doc.PasteCel(original, 1, clip));
        Assert.NotSame(doc.CelAt(original, 0), doc.CelAt(original, 1));
        Assert.True(doc.MoveCel(original, 1, 0));
    }

    [Fact]
    public void Pixel_animation_frames_keep_exact_pixels()
    {
        var doc = Document.NewAnimation(16, 16, null);
        var pencil = new PixelStroke(doc);
        var c = new ColorRgb(10, 200, 90);
        pencil.Begin(new PixelStrokeStyle(c, false, 1, true, Symmetry.None, false, "Pixel pencil"), 2, 2);
        pencil.Add(6, 2);
        pencil.End();
        doc.NewFrame(NewFrameContent.Duplicate);
        pencil.Begin(new PixelStrokeStyle(c, false, 1, true, Symmetry.None, false, "Pixel pencil"), 2, 8);
        pencil.End();
        var f0 = doc.RenderFrame(0, includePaper: false);
        var f1 = doc.RenderFrame(1, includePaper: false);
        Assert.Equal(Pixel.Opaque(c), f0[4, 2]);
        Assert.Equal(Pixel.Opaque(c), f1[4, 2]);
        Assert.Equal(Pixel.Opaque(c), f1[2, 8]);
        Assert.Equal(0u, f0[2, 8]);
        Assert.All(f1.Pixels, p => Assert.True(Pixel.A(p) is 0 or 255));
    }
}
