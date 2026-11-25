using DoodleNook.Engine.Bones;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Samples;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 019 "Bare Bones": the skeleton model, keyframe evaluation, animated attachments, rendering, the
/// .dnkbones file and the sample project.</summary>
public sealed class SkeletonTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-bones-" + Guid.NewGuid().ToString("N"));

    public SkeletonTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly ColorRgb Red = new(220, 40, 40);
    private static readonly ColorRgb Blue = new(40, 60, 220);

    private static void Near((double X, double Y) actual, double x, double y, double tolerance = 1e-6)
    {
        Assert.InRange(actual.X, x - tolerance, x + tolerance);
        Assert.InRange(actual.Y, y - tolerance, y + tolerance);
    }

    private SkeletonProject NewProject(int length = 24, double fps = 12)
    {
        var p = new SkeletonProject { Width = 200, Height = 200, Length = length, Fps = fps };
        p.PlaceAt(Path.Combine(_dir, "test" + SkeletonProject.Extension));
        return p;
    }

    private string Png(string name, int w, int h, ColorRgb colour, bool transparentLeftHalf = false)
    {
        var img = new RasterImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = transparentLeftHalf && x < w / 2 ? Pixel.Transparent : Pixel.Opaque(colour);
        var path = Path.Combine(_dir, name + ".png");
        File.WriteAllBytes(path, PngCodec.Encode(img));
        return path;
    }

    /// <summary>An animated drawing of <paramref name="frames"/> solid 4×4 frames at <paramref name="fps"/>.</summary>
    private string Animated(string name, int frames, double fps)
    {
        var images = Enumerable.Range(0, frames).Select(i =>
        {
            var img = new RasterImage(4, 4);
            img.Fill(Pixel.Opaque(new ColorRgb((byte)(10 * i), 100, 100)));
            return img;
        }).ToList();
        var path = Path.Combine(_dir, name + DnkFile.Extension);
        DnkFile.Save(SpriteSheetImport.NormalDocument(images, fps), path);
        return path;
    }

    // ================================================================== skeleton

    [Fact]
    public void Children_follow_their_parent_through_position_rotation_and_scale()
    {
        var p = NewProject();
        var root = p.AddBone("Root", null, 100, 100, rotation: 0, length: 50);
        var child = p.AddBone("Child", root, 50, 0, rotation: 90, length: 20);
        var pose = SkeletonEvaluator.Evaluate(p, 0);
        Near(pose[child.Id]!.Start, 150, 100);
        Near(pose[child.Id]!.End, 150, 120); // 90° clockwise on screen: pointing down

        root.Rotation = 90; // the whole arm turns about the root's joint
        pose = SkeletonEvaluator.Evaluate(p, 0);
        Near(pose[root.Id]!.Start, 100, 100);
        Near(pose[child.Id]!.Start, 100, 150);
        Near(pose[child.Id]!.End, 80, 150);

        root.Rotation = 0;
        root.ScaleX = root.ScaleY = 2; // a parent's scale carries to its children
        pose = SkeletonEvaluator.Evaluate(p, 0);
        Near(pose[child.Id]!.Start, 200, 100);
        Near(pose[child.Id]!.End, 200, 140);
    }

    [Fact]
    public void Reparenting_keeps_the_bone_in_place_and_refuses_loops()
    {
        var p = NewProject();
        var a = p.AddBone("A", null, 40, 40, rotation: 30, length: 50);
        var b = p.AddBone("B", a, 50, 0, rotation: 45, length: 30);
        var c = p.AddBone("C", b, 30, 0, rotation: -20, length: 20);
        var other = p.AddBone("Other", null, 150, 20, rotation: 120, length: 40);
        var before = SkeletonEvaluator.Evaluate(p, 0)[c.Id]!;

        Assert.True(p.Reparent(c, other));
        Assert.Equal(other.Id, c.ParentId);
        var after = SkeletonEvaluator.Evaluate(p, 0)[c.Id]!;
        Near(after.Start, before.Start.X, before.Start.Y, 1e-6);
        Near(after.End, before.End.X, before.End.Y, 1e-6);

        Assert.True(p.Reparent(c, null)); // a root, still in place
        Near(SkeletonEvaluator.Evaluate(p, 0)[c.Id]!.End, before.End.X, before.End.Y, 1e-6);

        Assert.False(p.Reparent(a, b)); // its own child: a loop
        Assert.False(p.Reparent(a, a));
        Assert.Null(a.ParentId);
    }

    [Fact]
    public void Deleting_a_bone_keeps_its_children_where_they_were()
    {
        var p = NewProject();
        var a = p.AddBone("A", null, 40, 40, length: 50);
        var b = p.AddBone("B", a, 50, 0, rotation: 60, length: 30);
        var c = p.AddBone("C", b, 30, 0, rotation: 10, length: 20);
        p.AddAttachment(b, Png("b", 4, 4, Red));
        p.SetPoseKey(b, 5, 50, 0, 90, 1, 1);
        var end = SkeletonEvaluator.Evaluate(p, 0)[c.Id]!.End;

        p.DeleteBone(b);
        Assert.Null(p.Find(b.Id));
        Assert.Equal(a.Id, c.ParentId);
        Assert.DoesNotContain(b.Id, p.DrawOrder);
        Assert.Null(p.TrackOf(b.Id));
        Near(SkeletonEvaluator.Evaluate(p, 0)[c.Id]!.End, end.X, end.Y, 1e-6);
        Assert.True(File.Exists(Path.Combine(_dir, "b.png"))); // its drawing is untouched
    }

    [Fact]
    public void Names_stay_unique_and_parents_come_before_children()
    {
        var p = NewProject();
        var a = p.AddBone("Arm", null, 0, 0);
        var a2 = p.AddBone("Arm", null, 0, 0);
        Assert.Equal("Arm 2", a2.Name);
        var child = p.AddBone("Hand", a2, 0, 0);
        p.Reparent(a, child);
        var order = p.Ordered();
        Assert.True(order.IndexOf(a2) < order.IndexOf(child));
        Assert.True(order.IndexOf(child) < order.IndexOf(a));
        Assert.Equal(2, p.Depth(a));
    }

    // ================================================================== keys

    [Fact]
    public void Keys_change_position_and_scale_evenly_and_rotation_the_short_way()
    {
        var p = NewProject();
        var bone = p.AddBone("Bone", null, 0, 0, rotation: 350);
        p.SetPoseKey(bone, 0, 0, 0, 350, 1, 1);
        p.SetPoseKey(bone, 10, 100, 50, 10, 3, 1);
        var (x, y, r, sx, sy) = SkeletonEvaluator.PoseAt(bone, p.TrackOf(bone.Id), 5);
        Assert.Equal(50, x, 6);
        Assert.Equal(25, y, 6);
        Assert.Equal(360, r, 6); // 350 → 10 goes through 0, not back through 180
        Assert.Equal(2, sx, 6);
        Assert.Equal(1, sy, 6);
        // Before the first key and after the last, the nearest key holds.
        p.SetPoseKey(bone, 15, 100, 50, 10, 3, 1);
        Assert.Equal(100, SkeletonEvaluator.PoseAt(bone, p.TrackOf(bone.Id), 20).X, 6);
        Assert.Equal(-170, SkeletonEvaluator.ShortestTurn(10, 200), 6);
        Assert.Equal(170, SkeletonEvaluator.ShortestTurn(200, 10), 6);
    }

    [Fact]
    public void A_smooth_key_eases_in_and_out()
    {
        var p = NewProject();
        var bone = p.AddBone("Bone", null, 0, 0);
        p.SetPoseKey(bone, 0, 0, 0, 0, 1, 1, smooth: true);
        p.SetPoseKey(bone, 8, 80, 0, 0, 1, 1);
        double At(double f) => SkeletonEvaluator.PoseAt(bone, p.TrackOf(bone.Id), f).X;
        Assert.True(At(2) < 20); // slow out of the key…
        Assert.Equal(40, At(4), 6); // …same halfway…
        Assert.True(At(6) > 60); // …slow into the next
        p.SetPoseKey(bone, 0, 0, 0, 0, 1, 1, smooth: false);
        Assert.Equal(20, At(2), 6);
    }

    [Fact]
    public void The_first_key_at_a_later_frame_keeps_the_rest_pose_before_it()
    {
        var p = NewProject();
        var bone = p.AddBone("Bone", null, 10, 20, rotation: 15);
        p.SetPoseKey(bone, 12, 90, 20, 15, 1, 1);
        var keys = p.TrackOf(bone.Id)!.Keys;
        Assert.Equal([0, 12], keys.Select(k => k.Frame));
        Assert.Equal(10, SkeletonEvaluator.PoseAt(bone, p.TrackOf(bone.Id), 0).X, 6);
        Assert.Equal(50, SkeletonEvaluator.PoseAt(bone, p.TrackOf(bone.Id), 6).X, 6);
        Assert.True(p.RemovePoseKey(bone, 12));
        Assert.False(p.RemovePoseKey(bone, 12));
        Assert.Equal([0], p.KeyFrames(bone));
    }

    [Fact]
    public void Shortening_the_timeline_removes_keys_past_the_end()
    {
        var p = NewProject(length: 24);
        var bone = p.AddBone("Bone", null, 0, 0);
        p.SetPoseKey(bone, 20, 5, 0, 0, 1, 1);
        p.SetSwitch(bone, 22, null);
        p.SetLength(12);
        Assert.Equal([0], p.KeyFrames());
    }

    // ================================================================== attachments

    [Fact]
    public void Switch_keys_change_the_sprite_without_blending()
    {
        var p = NewProject();
        var bone = p.AddBone("Head", null, 100, 100);
        var idle = p.AddAttachment(bone, Png("idle", 4, 4, Red), "Idle");
        var blink = p.AddAttachment(bone, Png("blink", 4, 4, Blue), "Blink");
        Assert.Equal(idle.Id, bone.Attachment); // the first sprite is the rest sprite
        p.SetSwitch(bone, 6, blink);
        p.SetSwitch(bone, 9, idle);
        p.SetSwitch(bone, 15, null);
        string? At(int f) => SkeletonEvaluator.Evaluate(p, f)[bone.Id]!.Attachment?.Name;
        Assert.Equal(["Idle", "Idle", "Blink", "Blink", "Idle", "Idle", null],
            new[] { 0, 5, 6, 8, 9, 14, 15 }.Select(At));
        p.RemoveAttachment(bone, blink); // its switch keys go with it
        Assert.Equal([9, 15], p.TrackOf(bone.Id)!.Switches.Select(s => s.Frame));
    }

    [Fact]
    public void An_animated_sprite_runs_on_its_own_clock_from_its_switch()
    {
        // An 8 fps, 4-frame wave (125 ms per frame) under a 12 fps master timeline (83⅓ ms per frame).
        var p = NewProject(length: 48, fps: 12);
        var bone = p.AddBone("Hand", null, 100, 100);
        var still = p.AddAttachment(bone, Png("hand", 4, 4, Red), "Hand");
        var wave = p.AddAttachment(bone, Animated("wave", 4, 8), "Wave");
        p.SetSwitch(bone, 12, wave);
        var assets = new SkeletonAssets();
        int At(int f) => SkeletonEvaluator.Evaluate(p, f, assets)[bone.Id]!.AttachmentFrame;
        Assert.Equal(0, At(11)); // the still hand
        // Restart (the default): frame 0 at the switch, then by time: 0, 83, 167, 250, 333, 417, 500 ms → 0,0,1,2,2,3,0.
        Assert.Equal([0, 0, 1, 2, 2, 3, 0], Enumerable.Range(12, 7).Select(At));

        wave.Speed = 2; // twice as fast: 0, 167, 333 ms of the drawing's time
        Assert.Equal([0, 1, 2], Enumerable.Range(12, 3).Select(At));
        wave.Speed = 1;

        wave.Restart = false; // as if it had been playing from the start: frame 12 is 1000 ms in → 1000 % 500 = 0
        Assert.Equal(0, At(12));
        Assert.Equal(1, At(14)); // 1167 ms → 167 → frame 1
        Assert.NotNull(still);
    }

    [Fact]
    public void A_one_shot_sprite_holds_its_last_frame()
    {
        var p = NewProject(length: 48, fps: 12);
        var bone = p.AddBone("Head", null, 100, 100);
        p.AddAttachment(bone, Png("idle", 4, 4, Red), "Idle");
        var blink = p.AddAttachment(bone, Animated("blink", 3, 12), "Blink");
        blink.Loop = false;
        p.SetSwitch(bone, 10, blink);
        var assets = new SkeletonAssets();
        Assert.Equal([0, 1, 2, 2, 2, 2], Enumerable.Range(10, 6).Select(f => SkeletonEvaluator.Evaluate(p, f, assets)[bone.Id]!.AttachmentFrame));
        blink.Loop = true;
        Assert.Equal(0, SkeletonEvaluator.Evaluate(p, 13, assets)[bone.Id]!.AttachmentFrame);
    }

    [Fact]
    public void Scrubbing_in_any_order_and_looping_give_the_same_pictures()
    {
        var p = NewProject(length: 20, fps: 12);
        var bone = p.AddBone("Hand", null, 100, 100);
        p.AddAttachment(bone, Png("hand", 4, 4, Red));
        var wave = p.AddAttachment(bone, Animated("wave", 5, 7), "Wave");
        p.SetSwitch(bone, 3, wave);
        p.SetPoseKey(bone, 0, 100, 100, 0, 1, 1);
        p.SetPoseKey(bone, 19, 100, 100, 270, 1, 1);
        var assets = new SkeletonAssets();
        (int, double) At(int f)
        {
            var bp = SkeletonEvaluator.Evaluate(p, f, assets)[bone.Id]!;
            return (bp.AttachmentFrame, bp.Rotation);
        }
        var forward = Enumerable.Range(0, 20).Select(At).ToList();
        var backward = Enumerable.Range(0, 20).Reverse().Select(At).Reverse().ToList();
        var random = Enumerable.Range(0, 20).OrderBy(f => (f * 7919) % 20).Select(f => (f, At(f))).OrderBy(t => t.f).Select(t => t.Item2).ToList();
        Assert.Equal(forward, backward);
        Assert.Equal(forward, random);

        // Playback by time: the master timeline loops back to frame 0 every pass, with nothing carried over.
        Assert.Equal(0, SkeletonEvaluator.FrameAtTime(p, 0));
        Assert.Equal(19, SkeletonEvaluator.FrameAtTime(p, 19 / 12.0));
        Assert.Equal(0, SkeletonEvaluator.FrameAtTime(p, 20 / 12.0));
        Assert.Equal(5, SkeletonEvaluator.FrameAtTime(p, 100 * 20 / 12.0 + 5 / 12.0)); // the 101st pass: no drift
        Assert.Equal(At(5), At(SkeletonEvaluator.FrameAtTime(p, 100 * 20 / 12.0 + 5 / 12.0)));
        p.Loop = false;
        Assert.Equal(19, SkeletonEvaluator.FrameAtTime(p, 50));
    }

    // ================================================================== rendering

    [Fact]
    public void A_sprite_turns_about_its_bone_joint()
    {
        var p = NewProject();
        var bone = p.AddBone("Arm", null, 100, 100, rotation: 0, length: 40);
        var a = p.AddAttachment(bone, Png("arm", 40, 10, Red));
        Assert.Equal(20, a.X); // centred halfway along the bone
        var assets = new SkeletonAssets();
        var img = SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets);
        Assert.Equal(Pixel.Opaque(Red), img[130, 100]);
        Assert.Equal(0u, img[100, 130]);

        bone.Rotation = 90; // now it hangs down from the same joint
        img = SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets);
        Assert.Equal(Pixel.Opaque(Red), img[100, 130]);
        Assert.Equal(0u, img[130, 100]);

        a.X = 0; // the sprite's own offset: centred on the joint
        a.Rotation = 90;
        img = SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets);
        Assert.Equal(Pixel.Opaque(Red), img[115, 100]);
        Assert.Equal(Pixel.Opaque(Red), img[85, 100]);
    }

    [Fact]
    public void Draw_order_and_transparency_decide_what_shows()
    {
        var p = NewProject();
        var back = p.AddBone("Back", null, 80, 100, length: 40);
        var front = p.AddBone("Front", null, 80, 100, length: 40);
        p.AddAttachment(back, Png("back", 40, 20, Blue));
        p.AddAttachment(front, Png("front", 40, 20, Red, transparentLeftHalf: true));
        var assets = new SkeletonAssets();
        RasterImage Render() => SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets);
        var img = Render();
        Assert.Equal(Pixel.Opaque(Red), img[110, 100]); // the front sprite's opaque half
        Assert.Equal(Pixel.Opaque(Blue), img[90, 100]); // its clear half shows the one behind

        Assert.True(p.MoveInDrawOrder(front, -1));
        Assert.Equal([front.Id, back.Id], p.DrawOrder);
        img = Render();
        Assert.Equal(Pixel.Opaque(Blue), img[110, 100]);
        Assert.False(p.MoveInDrawOrder(front, -1)); // already at the back
    }

    [Fact]
    public void The_picture_is_only_the_artwork_never_the_bones()
    {
        var p = NewProject();
        var root = p.AddBone("Root", null, 50, 50, length: 80);
        p.AddBone("Child", root, 80, 0, rotation: 45, length: 60);
        var assets = new SkeletonAssets();
        var img = SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets);
        Assert.All(img.Pixels, px => Assert.Equal(0u, px));
        var withPaper = SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets, ColorRgb.White);
        Assert.All(withPaper.Pixels, px => Assert.Equal(Pixel.Opaque(ColorRgb.White), px));
    }

    [Fact]
    public void Pixel_art_projects_keep_exact_pixels()
    {
        var p = NewProject();
        p.PixelArt = true;
        var bone = p.AddBone("Bone", null, 60, 60, rotation: 90, length: 40);
        var a = p.AddAttachment(bone, Png("px", 8, 8, Red));
        a.Scale = 3;
        var assets = new SkeletonAssets();
        var img = SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets);
        var colours = img.Pixels.Where(px => px != 0).Distinct().ToList();
        Assert.Equal([Pixel.Opaque(Red)], colours);
        Assert.Equal(24 * 24, img.Pixels.Count(px => px != 0));
    }

    // ================================================================== files

    [Fact]
    public void Saving_and_reopening_keeps_everything_with_relative_links()
    {
        var art = Path.Combine(_dir, "art");
        Directory.CreateDirectory(art);
        var pngPath = Png("art/torso", 6, 6, Red);
        var animPath = Animated("art/wave", 3, 6);
        var p = new SkeletonProject { Width = 320, Height = 240, Fps = 10, Length = 30, Loop = false, PixelArt = true };
        var root = p.AddBone("Hips", null, 160, 200, rotation: -90, length: 20);
        var arm = p.AddBone("Arm", root, 20, 10, rotation: 30, length: 50);
        p.AddAttachment(root, pngPath, "Torso");
        var wave = p.AddAttachment(arm, animPath, "Wave");
        wave.Loop = false;
        wave.Speed = 1.5;
        wave.Restart = false;
        p.SetPoseKey(arm, 10, 20, 10, 80, 1.2, 0.8, smooth: true);
        p.SetSwitch(arm, 12, null);
        p.MoveInDrawOrder(arm, -1);
        Assert.True(Path.IsPathRooted(wave.Source)); // not saved yet: a full path

        var path = Path.Combine(_dir, "My character" + SkeletonProject.Extension);
        p.Save(path);
        Assert.Equal("art/wave.dnk", wave.Source); // relative once it has a home
        var json = File.ReadAllText(path);
        Assert.Contains("\"format\": \"DoodleNook skeletal animation\"", json);
        Assert.Contains("\"version\": 1", json);
        Assert.DoesNotContain(_dir, json); // nothing absolute

        var q = SkeletonProject.Load(path);
        Assert.Equal(p.ToJson(), q.ToJson());
        Assert.Equal(Path.GetFullPath(path), q.FilePath);

        // The folder can move as a whole: links still resolve.
        var moved = Path.Combine(Path.GetTempPath(), "doodlenook-bones-moved-" + Guid.NewGuid().ToString("N"));
        Directory.Move(_dir, moved);
        try
        {
            var r = SkeletonProject.Load(Path.Combine(moved, "My character" + SkeletonProject.Extension));
            var assets = new SkeletonAssets();
            Assert.NotNull(assets.Get(r, r.Bones[0].Attachments[0]));
            Assert.Equal(3, assets.Get(r, r.Bones[1].Attachments[0])!.Frames.Count);
        }
        finally
        {
            Directory.Move(moved, _dir);
        }
    }

    [Fact]
    public void Saving_elsewhere_relinks_and_never_changes_the_artwork()
    {
        var png = Png("sprite", 6, 6, Red);
        var bytes = File.ReadAllBytes(png);
        var stamp = File.GetLastWriteTimeUtc(png);
        var p = NewProject();
        var bone = p.AddBone("Bone", null, 50, 50);
        var a = p.AddAttachment(bone, png);
        p.Save();
        Assert.Equal("sprite.png", a.Source);
        var sub = Path.Combine(_dir, "projects", "copy" + SkeletonProject.Extension);
        Directory.CreateDirectory(Path.GetDirectoryName(sub)!);
        p.Save(sub);
        Assert.Equal("../sprite.png", a.Source);
        var assets = new SkeletonAssets();
        SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets);
        Assert.Equal(bytes, File.ReadAllBytes(png));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(png));
    }

    [Fact]
    public void A_missing_file_shows_nothing_and_says_why()
    {
        var p = NewProject();
        var bone = p.AddBone("Bone", null, 50, 50);
        var a = p.AddAttachment(bone, Path.Combine(_dir, "gone.png"));
        var assets = new SkeletonAssets();
        Assert.Null(assets.Get(p, a));
        Assert.Contains("gone.png is missing", assets.Problem(p, a));
        var img = SkeletonRenderer.Render(p, SkeletonEvaluator.Evaluate(p, 0, assets), assets);
        Assert.All(img.Pixels, px => Assert.Equal(0u, px));

        // Put back, it is picked up again.
        Png("gone", 4, 4, Red);
        Assert.NotNull(assets.Get(p, a));
        Assert.Null(assets.Problem(p, a));

        File.WriteAllText(Path.Combine(_dir, "junk.png"), "not a picture");
        var junk = p.AddAttachment(bone, Path.Combine(_dir, "junk.png"));
        Assert.Null(assets.Get(p, junk));
        Assert.NotNull(assets.Problem(p, junk));
    }

    [Fact]
    public void Damaged_newer_or_foreign_files_are_refused_or_repaired()
    {
        Assert.Throws<SkeletonFormatException>(() => SkeletonProject.Parse("{ not json", null));
        Assert.Throws<SkeletonFormatException>(() => SkeletonProject.Parse("{\"format\":\"something else\"}", null));
        var newer = Assert.Throws<SkeletonFormatException>(() =>
            SkeletonProject.Parse("{\"format\":\"DoodleNook skeletal animation\",\"version\":99}", null));
        Assert.Contains("newer DoodleNook", newer.Message);

        // A loop between two bones, a key past the end and a missing parent are repaired, not fatal.
        var json = """
        {
          "format": "DoodleNook skeletal animation", "version": 1, "length": 10, "fps": 0,
          "bones": [
            { "id": "a", "name": "A", "parentId": "b" },
            { "id": "b", "name": "B", "parentId": "a" },
            { "id": "c", "name": "C", "parentId": "nobody", "attachment": "zzz" }
          ],
          "tracks": [ { "boneId": "a", "keys": [ { "frame": 50 }, { "frame": 3 } ] }, { "boneId": "ghost" } ]
        }
        """;
        var p = SkeletonProject.Parse(json, null);
        Assert.Contains(p.Bones, b => b.ParentId is null && b.Id != "c");
        Assert.Null(p.Find("c")!.ParentId);
        Assert.Null(p.Find("c")!.Attachment);
        Assert.Equal(1, p.Fps);
        Assert.Single(p.Tracks);
        Assert.Equal([3], p.Tracks[0].Keys.Select(k => k.Frame));
        Assert.Equal(3, p.Ordered().Count);
        SkeletonEvaluator.Evaluate(p, 4); // and it can be shown
    }

    [Fact]
    public void A_clone_is_independent()
    {
        var p = NewProject();
        var bone = p.AddBone("Bone", null, 10, 10);
        var copy = p.Clone();
        copy.Bones[0].X = 99;
        copy.AddBone("Another", null, 0, 0);
        Assert.Equal(10, bone.X);
        Assert.Single(p.Bones);
        Assert.Equal(p.FilePath, copy.FilePath);
    }

    // ================================================================== export

    [Fact]
    public void Every_master_frame_exports_through_the_existing_exporters()
    {
        var p = NewProject(length: 6, fps: 10);
        var bone = p.AddBone("Arm", null, 100, 100, length: 40);
        p.AddAttachment(bone, Png("arm", 40, 10, Red));
        p.SetPoseKey(bone, 5, 100, 100, 90, 1, 1);
        var assets = new SkeletonAssets();
        var frames = SkeletonRenderer.RenderAnimation(p, assets);
        Assert.Equal(6, frames.Count);
        Assert.All(frames, f => Assert.Equal(100, f.DurationMs));
        Assert.Equal(Enumerable.Range(0, 6), frames.Select(f => f.Index));
        Assert.NotEqual(frames[0].Image.Pixels, frames[5].Image.Pixels);

        var gif = Path.Combine(_dir, "out.gif");
        GifEncoder.Save(frames, gif, new GifOptions());
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(gif), 0, 6));

        var sheet = AnimationExport.BuildSheet(frames, new SpriteSheetOptions());
        Assert.Equal((1200, 200), (sheet.Image.Width, sheet.Image.Height));
        var paths = AnimationExport.SequencePaths(Path.Combine(_dir, "seq.png"), frames.Count);
        AnimationExport.WriteSequence(frames, paths);
        Assert.All(paths, path => Assert.True(File.Exists(path)));
        var first = PngCodec.DecodeAny(File.ReadAllBytes(paths[0]));
        Assert.Equal(frames[0].Image.Pixels, first.Pixels);
    }

    // ================================================================== the sample

    [Fact]
    public void The_sample_waves_hello_with_animated_sprites()
    {
        var path = SampleSkeleton.Create(Path.Combine(_dir, "sample"));
        Assert.EndsWith(SkeletonProject.Extension, path);
        var p = SkeletonProject.Load(path);
        Assert.Equal((12.0, 48, true), (p.Fps, p.Length, p.Loop));
        Bone Bone(string name) => p.Bones.Single(b => b.Name == name);
        Assert.Equal(["Idle", "Blink"], Bone("Head").Attachments.Select(a => a.Name));
        Assert.Equal(["Hand", "Wave"], Bone("Right hand").Attachments.Select(a => a.Name));
        Assert.Single(Bone("Body").Attachments);
        Assert.Single(Bone("Left arm").Attachments);
        Assert.All(p.Bones.SelectMany(b => b.Attachments), a => Assert.StartsWith("art/", a.Source));

        var assets = new SkeletonAssets();
        Assert.All(p.Bones.SelectMany(b => b.Attachments), a => Assert.Null(assets.Problem(p, a)));
        Assert.False(assets.Get(p, Bone("Body").Attachments[0])!.IsAnimated); // torso: still
        Assert.True(assets.Get(p, Bone("Head").Attachments[0])!.IsAnimated); // idle
        Assert.True(assets.Get(p, Bone("Head").Attachments[1])!.IsAnimated); // blink
        Assert.False(Bone("Head").Attachments[1].Loop);
        Assert.True(assets.Get(p, Bone("Right hand").Attachments[1])!.IsAnimated); // wave

        // The story: idle, blink, back to idle; the arm goes up and waves, comes down; the body sways; and loops.
        string? Shows(string bone, int f) => SkeletonEvaluator.Evaluate(p, f, assets)[Bone(bone).Id]!.Attachment?.Name;
        Assert.Equal("Idle", Shows("Head", 0));
        Assert.Equal("Blink", Shows("Head", 12));
        Assert.Equal("Idle", Shows("Head", 20));
        Assert.Equal("Hand", Shows("Right hand", 10));
        Assert.Equal("Wave", Shows("Right hand", 28));
        Assert.Equal("Hand", Shows("Right hand", 44));
        var waving = Enumerable.Range(22, 12).Select(f => SkeletonEvaluator.Evaluate(p, f, assets)[Bone("Right hand").Id]!.AttachmentFrame).Distinct();
        Assert.True(waving.Count() >= 3);
        var hand = (int f) => SkeletonEvaluator.Evaluate(p, f, assets)[Bone("Right hand").Id]!.End;
        Assert.True(hand(28).Y < hand(4).Y - 100); // raised
        Assert.Equal(hand(4), hand(47));
        Assert.True(Math.Abs(SkeletonEvaluator.Evaluate(p, 43, assets)[Bone("Body").Id]!.Rotation) > 3); // the sway

        // Every frame has the character on it, inside the picture.
        var frames = SkeletonRenderer.RenderAnimation(p, assets);
        Assert.Equal(48, frames.Count);
        foreach (var f in new[] { 0, 12, 28, 43 })
        {
            var img = frames[f].Image;
            int covered = img.Pixels.Count(px => px >> 24 > 0);
            Assert.InRange(covered, 15_000, 60_000);
            for (int x = 0; x < img.Width; x++)
            {
                Assert.Equal(0u, img[x, 0]);
                Assert.Equal(0u, img[x, img.Height - 1]);
            }
        }
        Assert.NotEqual(frames[0].Image.Pixels, frames[12].Image.Pixels); // eyes closing
    }
}
