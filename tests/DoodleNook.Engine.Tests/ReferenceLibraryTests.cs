using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.References;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 013 — the Reference Animation Library: the cycles, the mannequin and its views, and inserting a
/// cycle into empty, still and already-started animations without touching what is there.</summary>
public sealed class ReferenceLibraryTests
{
    private static ReferenceCycleSettings Walk(double x = 160, double y = 210, int start = 0, int hold = 1, bool repeat = true, bool extend = true,
        ReferenceView view = ReferenceView.Side, double height = 120) =>
        new("walk", view, ReferenceRenderer.DefaultFacing(view), height, x, y, start, hold, repeat, extend);

    // ================================================================== the library

    [Fact]
    public void The_library_has_the_starter_cycles_with_practical_frame_counts()
    {
        var c = ReferenceLibrary.Cycles;
        Assert.Equal(22, c.Count);
        Assert.Equal(c.Count, c.Select(x => x.Id).Distinct().Count());
        Assert.Equal(c.Count, c.Select(x => x.Name).Distinct().Count());
        Assert.Equal(["Idle", "Walk", "Run", "Sneak", "Jump", "Fall", "Land"], c.Where(x => x.Category == ReferenceLibrary.Movement).Select(x => x.Name));
        Assert.Equal(["Attack (slash)", "Attack (thrust)", "Hurt", "Death", "Cast", "Shoot"], c.Where(x => x.Category == ReferenceLibrary.Combat).Select(x => x.Name));
        Assert.Equal(["Pick up", "Drop", "Push", "Pull", "Climb", "Sit down", "Stand up", "Hammer", "Dig"], c.Where(x => x.Category == ReferenceLibrary.Interaction).Select(x => x.Name));
        Assert.All(c, x =>
        {
            Assert.InRange(x.Frames, 4, 8);
            Assert.InRange(x.Fps, 8, 12);
            Assert.False(string.IsNullOrWhiteSpace(x.Description));
            Assert.Contains(ReferenceView.Side, x.Views);
        });
        Assert.True(ReferenceLibrary.Find("walk")!.Loop && ReferenceLibrary.Find("idle")!.Loop && !ReferenceLibrary.Find("death")!.Loop);
        Assert.Equal([ReferenceView.Side, ReferenceView.ThreeQuarter], ReferenceLibrary.Find("climb")!.Views);
    }

    [Fact]
    public void Every_cycle_draws_every_drawing_in_every_view_it_offers()
    {
        foreach (var cycle in ReferenceLibrary.Cycles)
            foreach (var view in cycle.Views)
                foreach (var facing in ReferenceRenderer.FacingsFor(view))
                {
                    var hashes = new HashSet<string>();
                    for (int f = 0; f < cycle.Frames; f++)
                    {
                        var img = ReferenceRenderer.Render(cycle.Pose(f), 200, 200, new ReferenceFraming(view, facing, 120, 100, view == ReferenceView.TopDown ? 100 : 170));
                        var b = img.ContentBounds();
                        Assert.False(b.IsEmpty, $"{cycle.Id} {view} {facing} frame {f} is empty");
                        Assert.True(img.Pixels.Count(p => Pixel.A(p) == 255) > 300, $"{cycle.Id} {view} {facing} frame {f} has too little figure");
                        hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(img.Pixels.AsSpan()))));
                    }
                    Assert.True(hashes.Count >= Math.Min(3, cycle.Frames), $"{cycle.Id} {view} {facing}: the drawings should differ");
                }
    }

    [Fact]
    public void Reversed_cycles_are_their_source_played_backwards()
    {
        foreach (var (rev, src) in new[] { ("drop", "pick-up"), ("stand-up", "sit-down") })
        {
            var a = ReferenceLibrary.Find(rev)!;
            var b = ReferenceLibrary.Find(src)!;
            Assert.Equal(src, a.ReverseOf);
            for (int f = 0; f < a.Frames; f++) Assert.Equal(b.Pose(b.Frames - 1 - f), a.Pose(f));
        }
    }

    [Fact]
    public void The_mannequin_keeps_its_feet_on_the_ground_unless_it_is_in_the_air()
    {
        static float Lowest(Pose p) => Mannequin.Build(p).Where(x => x.Paint is PartPaint.Body or PartPaint.Left or PartPaint.Right)
            .Min(x => Math.Min(x.A.Y - x.RadiusA, x.B.Y - x.RadiusB));
        foreach (var id in new[] { "idle", "walk", "sneak", "push", "pull", "sit-down", "death", "pick-up", "hurt", "attack-slash", "dig" })
        {
            var c = ReferenceLibrary.Find(id)!;
            for (int f = 0; f < c.Frames; f++) Assert.InRange(Lowest(c.Pose(f)), -0.001f, 0.001f);
        }
        // A run leaves the ground for one low drawing per step, and is on it for the rest.
        var run = ReferenceLibrary.Find("run")!;
        var heights = Enumerable.Range(0, run.Frames).Select(f => Lowest(run.Pose(f))).ToList();
        Assert.Equal(2, heights.Count(h => h > 0.01f));
        Assert.All(heights, h => Assert.InRange(h, -0.001f, 0.12f));
        var jump = ReferenceLibrary.Find("jump")!;
        Assert.True(Lowest(jump.Pose(4)) > 0.2f, "the jump's top is well off the ground");
        // The relaxed standing figure is one body height tall: head top near 1.
        float top = Mannequin.Build(new Pose()).Max(x => Math.Max(x.A.Y + x.RadiusA, x.B.Y + x.RadiusB));
        Assert.InRange(top, 0.95f, 1.05f);
    }

    [Fact]
    public void Drawing_is_exact_and_repeatable_and_pixel_art_edges_are_hard()
    {
        var pose = ReferenceLibrary.Find("run")!.Pose(2);
        var framing = new ReferenceFraming(ReferenceView.ThreeQuarter, ReferenceFacing.DownRight, 64, 40, 70);
        var a = ReferenceRenderer.Render(pose, 80, 80, framing);
        var b = ReferenceRenderer.Render(pose, 80, 80, framing);
        Assert.Equal(a.Pixels, b.Pixels);
        var hard = ReferenceRenderer.Render(pose, 80, 80, framing with { HardEdges = true });
        var alphas = hard.Pixels.Select(Pixel.A).Distinct().Order().ToList();
        Assert.All(alphas, al => Assert.True(al is 0 or 255 || Math.Abs(al - 60) <= 1, $"alpha {al}: no soft edges in pixel art (the ground shadow is see-through)"));
        Assert.Contains(a.Pixels, p => Pixel.A(p) is > 0 and < 255 and not (>= 59 and <= 61)); // the smooth version is antialiased
        // The figure is as tall as asked: about 64 px from the feet to the top of the head.
        var side = ReferenceRenderer.Render(new Pose(), 100, 120, new ReferenceFraming(ReferenceView.Side, ReferenceFacing.Right, 64, 50, 100, Marker: false));
        var body = side.Pixels.Select((p, i) => (p, i)).Where(t => Pixel.A(t.p) == 255).Select(t => t.i / 100).ToList();
        Assert.InRange(body.Max() - body.Min() + 1, 62, 68);
        Assert.InRange(body.Max(), 98, 101);
    }

    [Fact]
    public void Facings_mirror_the_side_view_and_turn_the_top_down_view()
    {
        var pose = ReferenceLibrary.Find("walk")!.Pose(1);
        var right = ReferenceRenderer.Render(pose, 200, 200, new ReferenceFraming(ReferenceView.Side, ReferenceFacing.Right, 120, 100, 170, Marker: false));
        var left = ReferenceRenderer.Render(pose, 200, 200, new ReferenceFraming(ReferenceView.Side, ReferenceFacing.Left, 120, 100, 170, Marker: false));
        // Facing left is the mirror image, except that the far and near sides swap colours.
        var r = right.ContentBounds();
        var l = left.ContentBounds();
        Assert.Equal(r.Width, l.Width);
        Assert.Equal(200 - r.Right, l.X);
        var up = ReferenceRenderer.Render(pose, 200, 200, new ReferenceFraming(ReferenceView.TopDown, ReferenceFacing.Up, 120, 100, 100, Marker: false)).ContentBounds();
        var rt = ReferenceRenderer.Render(pose, 200, 200, new ReferenceFraming(ReferenceView.TopDown, ReferenceFacing.Right, 120, 100, 100, Marker: false)).ContentBounds();
        Assert.InRange(up.Width, rt.Height - 2, rt.Height + 2);
        Assert.InRange(up.Height, rt.Width - 2, rt.Width + 2);
    }

    // ================================================================== timeline rules

    [Fact]
    public void Plans_start_hold_repeat_and_grow_only_when_asked()
    {
        var walk = ReferenceLibrary.Find("walk")!; // 8 drawings
        Assert.Equal(new ReferencePlan(0, 8, 8, 7, 8, 1), ReferenceInsert.Plan(walk, Walk(), 1));                       // empty animation: grows to one cycle
        Assert.Equal(new ReferencePlan(0, 24, 24, 0, 8, 1), ReferenceInsert.Plan(walk, Walk(), 24));                    // repeats to the end
        Assert.Equal(new ReferencePlan(3, 19, 24, 0, 8, 2), ReferenceInsert.Plan(walk, Walk(start: 3, hold: 2, repeat: false), 24)); // once, on twos
        Assert.Equal(new ReferencePlan(4, 12, 12, 6, 8, 1), ReferenceInsert.Plan(walk, Walk(start: 4, repeat: false), 6));  // grows to fit
        Assert.Equal(new ReferencePlan(4, 6, 6, 0, 8, 1), ReferenceInsert.Plan(walk, Walk(start: 4, repeat: false, extend: false), 6)); // stops at the end
        Assert.Equal(new ReferencePlan(5, 6, 6, 0, 8, 1), ReferenceInsert.Plan(walk, Walk(start: 99, extend: false), 6)); // a start past the end is the last frame
    }

    // ================================================================== inserting

    /// <summary>A 12-frame scene: a background held on every frame and a character drawn on frames 1–6.</summary>
    private static (Document Doc, Layer Background, Layer Character) Scene(int frames = 12)
    {
        var doc = Document.NewAnimation(320, 240, ColorRgb.White, frames, 12, "Background");
        var bg = doc.ActiveLayer;
        var sky = new RasterImage(320, 240);
        sky.Fill(Pixel.Opaque(new ColorRgb(140, 190, 220)));
        doc.PasteCel(bg, 0, new Cel(sky, sky.Bounds));
        for (int f = 1; f < frames; f++) doc.ExtendHold(bg, 0);
        var character = doc.AddLayer("Character");
        for (int f = 0; f < 6; f++)
        {
            var img = new RasterImage(320, 240);
            for (int y = 100; y < 140; y++)
                for (int x = 20 + f * 10; x < 40 + f * 10; x++)
                    img[x, y] = Pixel.Opaque(new ColorRgb(200, 60, 60));
            doc.PasteCel(character, f, new Cel(img, img.ContentBounds()));
        }
        doc.History.Clear();
        return (doc, bg, character);
    }

    private static List<Cel?> Cells(Document doc, Layer l) => Enumerable.Range(0, doc.FrameCount).Select(f => doc.CelAt(l, f)).ToList();

    [Fact]
    public void Inserting_into_a_started_scene_adds_one_locked_layer_and_leaves_everything_else_exactly_as_it_was()
    {
        var (doc, bg, character) = Scene();
        var bgBefore = Cells(doc, bg);
        var charBefore = Cells(doc, character);
        var charPixels = charBefore.Select(c => c?.Image.Pixels.ToArray()).ToList();
        int undo = doc.History.UndoCount;

        var layer = ReferenceInsert.Insert(doc, Walk(start: 2), below: character);

        Assert.Equal(12, doc.FrameCount);
        Assert.Equal(["Background", "Walk reference (side)", "Character"], doc.Layers.Select(l => l.Name));
        Assert.Same(character, doc.ActiveLayer);                 // you keep drawing where you were
        Assert.True(layer.Locked && layer.Visible);
        Assert.Equal(ReferenceInsert.DefaultOpacity, layer.Opacity, 6);
        Assert.Equal("walk", layer.ReferenceCycle!.CycleId);
        Assert.Equal(bgBefore, Cells(doc, bg));                   // the same drawings, not copies
        Assert.Equal(charBefore, Cells(doc, character));
        for (int f = 0; f < 6; f++) Assert.Equal(charPixels[f], doc.CelAt(character, f)!.Image.Pixels);
        // Empty before the start, then the cycle repeating to the end (8 drawings, shared by the repeat).
        var track = Cells(doc, layer);
        Assert.Null(track[0]);
        Assert.Null(track[1]);
        Assert.All(track.Skip(2), c => Assert.NotNull(c));
        Assert.Equal(8, track.Skip(2).Distinct().Count());
        Assert.Same(track[2], track[10]);
        Assert.Equal(undo + 1, doc.History.UndoCount);           // one undo step

        doc.Undo();
        Assert.Equal(["Background", "Character"], doc.Layers.Select(l => l.Name));
        Assert.Equal(charBefore, Cells(doc, character));
        doc.Redo();
        Assert.Equal(3, doc.Layers.Count);
        Assert.Same(character, doc.ActiveLayer);
        Assert.Equal(track, Cells(doc, doc.Layers[1]));
    }

    [Fact]
    public void A_cycle_longer_than_the_scene_adds_frames_where_the_other_layers_hold_and_undo_takes_them_away()
    {
        var (doc, bg, character) = Scene(frames: 6);
        var charBefore = Cells(doc, character);
        ReferenceInsert.Insert(doc, Walk(start: 4, repeat: false), below: character);
        Assert.Equal(12, doc.FrameCount);
        Assert.Equal(charBefore, Cells(doc, character).Take(6));
        for (int f = 6; f < 12; f++)
        {
            Assert.Same(doc.CelAt(bg, 5), doc.CelAt(bg, f));               // the background holds
            Assert.Same(doc.CelAt(character, 5), doc.CelAt(character, f)); // so does the last drawing
        }
        doc.Undo();
        Assert.Equal(6, doc.FrameCount);
        Assert.Equal(charBefore, Cells(doc, character));

        // Not allowed to grow: the cycle stops at the last frame.
        ReferenceInsert.Insert(doc, Walk(start: 4, repeat: false, extend: false), below: character);
        Assert.Equal(6, doc.FrameCount);
        Assert.Equal(2, Cells(doc, doc.Layers[1]).Count(c => c is not null));
    }

    [Fact]
    public void An_empty_animation_and_a_still_picture_get_the_whole_cycle()
    {
        var empty = Document.NewAnimation(200, 200, ColorRgb.White, 1, 12, "Drawing");
        ReferenceInsert.Insert(empty, Walk(100, 180, height: 140) with { CycleId = "idle" }, below: empty.ActiveLayer);
        Assert.Equal(6, empty.FrameCount);
        Assert.Equal("Idle reference (side)", empty.Layers[0].Name);
        Assert.Equal("Drawing", empty.ActiveLayer.Name);

        var picture = new RasterImage(200, 200);
        picture[5, 5] = Pixel.Opaque(new ColorRgb(1, 2, 3));
        var still = Document.FromImage(picture, "Picture");
        ReferenceInsert.Insert(still, Walk(100, 180), below: still.ActiveLayer);
        Assert.True(still.IsAnimated);
        Assert.Equal(8, still.FrameCount);
        Assert.Equal(Pixel.Opaque(new ColorRgb(1, 2, 3)), still.CelAt(still.Layers[1], 7)!.Image[5, 5]); // the picture is held on every frame
        still.Undo();
        Assert.False(still.IsAnimated);
        Assert.Single(still.Layers);
    }

    [Fact]
    public void A_reconstruction_document_keeps_its_imported_reference_and_gets_a_second_one()
    {
        var frames = Enumerable.Range(0, 5).Select(i =>
        {
            var img = new RasterImage(96, 64);
            img[i * 10 + 5, 30] = Pixel.Opaque(new ColorRgb(9, 9, 9));
            return img;
        }).ToList();
        var doc = AnimationImport.BuildDocument(AnimationImport.FromImages(frames));
        var imported = Cells(doc, doc.Layers[0]);
        var sketch = doc.Layers[1];
        doc.SetActiveLayer(sketch);
        ReferenceInsert.Insert(doc, Walk(48, 60, height: 50), below: sketch);
        Assert.Equal(["Reference", "Walk reference (side)", "Sketch", "Outline", "Coloring"], doc.Layers.Select(l => l.Name));
        Assert.Equal(8, doc.FrameCount); // the imported 5 frames, grown to one whole walk
        Assert.Equal(imported, Cells(doc, doc.Layers[0]).Take(5));
        Assert.All(Cells(doc, doc.Layers[0]).Skip(5), c => Assert.Same(imported[4], c)); // its last frame holds
    }

    [Fact]
    public void Save_and_reopen_keeps_the_cycle_its_settings_and_its_layer()
    {
        var (doc, _, character) = Scene();
        var s = Walk(start: 1, hold: 2) with { View = ReferenceView.ThreeQuarter, Facing = ReferenceFacing.UpLeft, Marker = false, HardEdges = true };
        var layer = ReferenceInsert.Insert(doc, s, below: character);
        doc.SetLayerProperties(layer, layer.Properties with { Locked = false, Opacity = 0.5 });
        var path = Path.Combine(Path.GetTempPath(), $"dn-ref-{Guid.NewGuid():N}.dnk");
        try
        {
            DnkFile.Save(doc, path);
            Assert.Equal(DnkFile.ReferenceVersion, DnkFile.Snapshot(doc).Manifest.Version);
            var back = DnkFile.Load(path);
            var l = back.Layers[1];
            Assert.Equal(s.Normalized(), l.ReferenceCycle);
            Assert.Equal((false, 0.5, true), (l.Locked, l.Opacity, l.Visible));
            for (int f = 0; f < doc.FrameCount; f++)
                Assert.Equal(doc.CelAt(layer, f)?.Image.Pixels, back.CelAt(l, f)?.Image.Pixels);
            // A picture without a library cycle is written as before.
            var (plain, _, _) = Scene();
            Assert.Equal(DnkFile.AnimationVersion, DnkFile.Snapshot(plain).Manifest.Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Exports_show_a_visible_reference_at_its_opacity_and_leave_out_a_hidden_one()
    {
        var doc = Document.NewAnimation(200, 200, null, 1, 12, "Drawing");
        var layer = ReferenceInsert.Insert(doc, Walk(100, 180, height: 140) with { Marker = false }, below: doc.ActiveLayer);
        var frames = AnimationExport.RenderFrames(doc, includePaper: false);
        Assert.Equal(8, frames.Count);
        var alphas = frames[0].Image.Pixels.Select(Pixel.A).Where(a => a > 0).ToList();
        Assert.NotEmpty(alphas);
        Assert.InRange(alphas.Max(), 85, 95); // 35 % of opaque
        doc.SetLayerProperties(layer, layer.Properties with { Visible = false });
        Assert.All(AnimationExport.RenderFrames(doc, includePaper: false), f => Assert.True(f.Image.ContentBounds().IsEmpty));
    }

    [Fact]
    public void Adjusting_draws_the_cycle_again_as_one_undo_step_and_keeps_a_name_you_chose()
    {
        var (doc, _, character) = Scene();
        var layer = ReferenceInsert.Insert(doc, Walk(), below: character);
        var before = Cells(doc, layer);
        ReferenceInsert.Update(doc, layer, Walk(x: 120, height: 90) with { CycleId = "run", Facing = ReferenceFacing.Left });
        Assert.Equal("Run reference (side)", layer.Name);
        Assert.Equal("run", layer.ReferenceCycle!.CycleId);
        Assert.NotEqual(before[0]!.Image.Pixels, doc.CelAt(layer, 0)!.Image.Pixels);
        Assert.True(layer.Locked);
        doc.Undo();
        Assert.Equal("Walk reference (side)", layer.Name);
        Assert.Equal("walk", layer.ReferenceCycle!.CycleId);
        Assert.Equal(before, Cells(doc, layer));

        doc.SetLayerProperties(layer, layer.Properties with { Name = "Hero guide" });
        ReferenceInsert.Update(doc, layer, Walk() with { View = ReferenceView.TopDown, Facing = ReferenceFacing.Up });
        Assert.Equal("Hero guide", layer.Name);
        // Duplicating the layer keeps its cycle, so the copy can be adjusted too.
        var copy = doc.DuplicateLayer(layer);
        Assert.Equal(layer.ReferenceCycle, copy.ReferenceCycle);
    }

    [Fact]
    public void Under_palette_lock_the_reference_uses_only_palette_colours()
    {
        var doc = Document.NewAnimation(160, 160, ColorRgb.White, 1, 12, "Drawing");
        var palette = new Palette("Four", [new ColorRgb(20, 20, 30), new ColorRgb(200, 200, 210), new ColorRgb(230, 140, 90), new ColorRgb(100, 150, 210)]);
        doc.LockToPalette(palette);
        var layer = ReferenceInsert.Insert(doc, Walk(80, 150, height: 120), below: doc.ActiveLayer);
        var conformer = new PaletteConformer(palette.Colors);
        foreach (var cel in Cells(doc, layer).OfType<Cel>().Distinct())
            Assert.All(cel.Image.Pixels.Where(p => p != 0), p => Assert.True(conformer.ExactIndex(p) >= 0));
    }

    // ================================================================== natural motion (the refinement after Mission 014)

    private static List<Part> Parts(ReferenceCycle c, int f) => Mannequin.Build(c.Pose(f));

    /// <summary>A foot's heel and toe (centres) and whether each rests on the ground.</summary>
    private static (System.Numerics.Vector3 Heel, bool HeelDown, System.Numerics.Vector3 Toe, bool ToeDown) Foot(ReferenceCycle c, int f, PartPaint side)
    {
        var foot = Parts(c, f).Where(p => p.Paint == side && p.Kind == PartKind.Capsule).ElementAt(2);
        return (foot.A, foot.A.Y - foot.RadiusA < 0.004f, foot.B, foot.B.Y - foot.RadiusB < 0.004f);
    }

    /// <summary>How far the part of a foot that is on the ground in both drawings moved between them (null: it wasn't).</summary>
    private static System.Numerics.Vector3? Slide(ReferenceCycle c, int a, int b, PartPaint side)
    {
        var x = Foot(c, a, side);
        var y = Foot(c, b, side);
        return x.HeelDown && y.HeelDown ? y.Heel - x.Heel : x.ToeDown && y.ToeDown ? y.Toe - x.Toe : null;
    }

    [Fact]
    public void Planted_feet_stay_exactly_where_they_are_in_every_cycle_that_does_not_step()
    {
        foreach (var id in new[] { "idle", "attack-slash", "attack-thrust", "hurt", "cast", "shoot", "pick-up", "drop", "sit-down", "stand-up", "hammer", "dig", "land", "jump", "death" })
        {
            var c = ReferenceLibrary.Find(id)!;
            int checkedSteps = 0;
            for (int f = 0; f + 1 < c.Frames || c.Loop && f < c.Frames; f++)
                foreach (var side in new[] { PartPaint.Right, PartPaint.Left })
                    if (Slide(c, f, (f + 1) % c.Frames, side) is { } d)
                    {
                        checkedSteps++;
                        // Under a pixel for a figure 170 pixels tall, even as a heel rises or settles.
                        Assert.True(Math.Abs(d.Z) < 0.004f && Math.Abs(d.Y) < 0.004f, $"{id}: the {side} foot slides {d} between drawings {f} and {f + 1}");
                    }
            Assert.True(checkedSteps >= 2, $"{id} has planted feet to check");
        }
    }

    [Fact]
    public void Stepping_cycles_move_a_planted_foot_at_one_steady_speed_so_they_can_be_moved_along_without_skating()
    {
        foreach (var (id, backwards) in new[] { ("walk", false), ("run", false), ("sneak", false), ("push", false), ("pull", true) })
        {
            var c = ReferenceLibrary.Find(id)!;
            var steps = new List<float>();
            for (int f = 0; f < c.Frames; f++)
                foreach (var side in new[] { PartPaint.Right, PartPaint.Left })
                    if (Slide(c, f, (f + 1) % c.Frames, side) is { } d)
                        steps.Add(d.Z);
            Assert.True(steps.Count >= 4, $"{id}: feet on the ground between drawings");
            Assert.All(steps, z => Assert.True(backwards ? z > 0 : z < 0, $"{id}: the planted foot moves {(backwards ? "forward" : "back")}"));
            Assert.True(steps.Max() - steps.Min() < 0.004f, $"{id}: planted-foot speed varies {steps.Min():0.000}…{steps.Max():0.000}");
        }
    }

    [Fact]
    public void Death_collapses_onto_the_knees_and_ends_lying_on_the_ground_not_propped_up()
    {
        var death = ReferenceLibrary.Find("death")!;
        Assert.False(death.Loop);
        static float Bottom(Part p) => Math.Min(p.A.Y - p.RadiusA, p.B.Y - p.RadiusB);
        float Pelvis(int f) => Parts(death, f).First(p => p.Paint == PartPaint.Body).A.Y;
        // The pelvis falls faster and faster until the knees land (gravity), and stays down from there on.
        double d1 = Pelvis(1) - Pelvis(2), d2 = Pelvis(2) - Pelvis(3);
        Assert.True(d2 > d1 * 2, $"the drop speeds up ({d1:0.000} then {d2:0.000})");
        // Kneeling: both shins on the ground (the leg capsules come first: right thigh, shin, foot, then the left's).
        var kneeling = Parts(death, 3).Where(p => p.Kind == PartKind.Capsule && p.Paint is PartPaint.Right or PartPaint.Left).ToList();
        Assert.True(Bottom(kneeling[1]) < 0.012f && Bottom(kneeling[4]) < 0.012f, "kneeling: both shins on the ground");
        // At rest: head, chest, hips, legs and arms all on (or within a hair of) the ground.
        var rest = Parts(death, death.Frames - 1).Skip(1).Where(p => p.Paint is PartPaint.Body or PartPaint.Left or PartPaint.Right).ToList();
        Assert.All(rest, p => Assert.True(Bottom(p) < 0.07f, $"a part floats {Bottom(p):0.000} above the ground at rest"));
        Assert.True(rest.Count(p => Bottom(p) < 0.004f) >= 3, "the body rests on the ground");
        Assert.True(Pelvis(death.Frames - 1) < 0.1f, "lying down");
    }

    [Fact]
    public void Hands_hold_what_they_hold_the_crate_face_and_the_ladder_rungs()
    {
        foreach (var id in new[] { "push", "pull" })
        {
            var c = ReferenceLibrary.Find(id)!;
            for (int f = 0; f < c.Frames; f++)
            {
                var parts = Parts(c, f);
                var crate = parts.First(p => p.Kind == PartKind.Box);
                foreach (var side in new[] { PartPaint.Right, PartPaint.Left })
                {
                    var hand = parts.First(p => p.Kind == PartKind.Ball && p.Paint == side);
                    float nearFace = crate.A.Z - crate.RadiusA;
                    Assert.InRange(nearFace - (hand.A.Z + hand.RadiusA), -0.01f, 0.01f); // touching the crate's near face
                    Assert.InRange(hand.A.Y, crate.A.Y, crate.A.Y + crate.RadiusA);       // on its upper half
                }
            }
        }
        var climb = ReferenceLibrary.Find("climb")!;
        for (int f = 0; f < climb.Frames; f++)
        {
            var parts = Parts(climb, f);
            var hands = parts.Where(p => p.Kind == PartKind.Ball && p.Paint is PartPaint.Right or PartPaint.Left).ToList();
            Assert.Contains(hands, h => Math.Abs(h.A.Z - 0.19f) < 0.01f); // at least one hand on the ladder at all times
            var feet = parts.Where(p => p.Kind == PartKind.Capsule && p.Paint is PartPaint.Right or PartPaint.Left).Where((_, i) => i % 5 == 2).ToList();
            Assert.Contains(feet, foot => foot.B.Z > 0.15f); // and a foot reaching the rungs
        }
    }

    [Fact]
    public void The_pose_solver_puts_feet_and_hands_exactly_where_asked()
    {
        var pose = PoseSolver.Plant(new Pose { Lean = 10, Twist = 8 }, 0.48, new FootAt(0.12, 0, -10, 0.1), new FootAt(-0.1, 0.03, 20, -0.12));
        pose = PoseSolver.Reach(pose, 0.48, new System.Numerics.Vector3(0.05f, 0.5f, 0.25f), new System.Numerics.Vector3(-0.15f, 0.7f, 0.1f));
        var j = PoseSolver.At(pose, 0.48);
        var (rz, ry) = PoseSolver.AnkleFor(new FootAt(0.12, 0, -10));
        var (lz, ly) = PoseSolver.AnkleFor(new FootAt(-0.1, 0.03, 20));
        Assert.Equal(rz, j.AnkleR.Z, 3);
        Assert.Equal(ry, j.AnkleR.Y, 3);
        Assert.Equal(0.1, j.AnkleR.X, 3);
        Assert.Equal(lz, j.AnkleL.Z, 3);
        Assert.Equal(ly, j.AnkleL.Y, 3);
        Assert.Equal(-0.12, j.AnkleL.X, 3);
        Assert.True(System.Numerics.Vector3.Distance(j.HandR, new(0.05f, 0.5f, 0.25f)) < 0.002f);
        Assert.True(System.Numerics.Vector3.Distance(j.HandL, new(-0.15f, 0.7f, 0.1f)) < 0.002f);
        // Grounded, the planted (flat) right foot is what touches the ground.
        var parts = Mannequin.Build(pose);
        var heel = parts.Where(p => p.Paint == PartPaint.Right && p.Kind == PartKind.Capsule).ElementAt(2);
        Assert.InRange(heel.A.Y - heel.RadiusA, -0.001f, 0.001f);
    }
}
