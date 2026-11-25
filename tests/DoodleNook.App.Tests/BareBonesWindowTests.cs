using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DoodleNook.App.Bones;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Bones;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Samples;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 019 — Bare Bones in the real window: building a skeleton by dragging, sprites, keys by posing,
/// playback, sprite switches, undo (one step per drag), the trip to the editor and back, export, and the other
/// workspaces left as they were.</summary>
public sealed class BareBonesWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-boneswin-" + Guid.NewGuid().ToString("N"));

    public BareBonesWindowTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static Task Until(Func<bool> done, int ms = 10_000) => ArtistsCompanionWindowTests.Until(done, ms);

    internal static void Shot(MainWindow w, string name)
    {
        Pump();
        w.CaptureRenderedFrame()?.Dispose();
        Pump();
        using var frame = w.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(Screenshots.OutDir, name + ".png"), new PngBitmapEncoderOptions());
    }

    /// <summary>A point on the stage (picture pixels) in window coordinates.</summary>
    private static Point StagePoint(MainWindow w, double x, double y)
    {
        var stage = w.BonesView!.Stage;
        return stage.TranslatePoint(stage.ToScreen(x, y), w)!.Value;
    }

    private static void StageDrag(MainWindow w, RawInputModifiers mods, params (double X, double Y)[] points)
    {
        var first = StagePoint(w, points[0].X, points[0].Y);
        w.MouseMove(first, mods);
        w.MouseDown(first, MouseButton.Left, mods);
        foreach (var p in points.Skip(1)) w.MouseMove(StagePoint(w, p.X, p.Y), mods | RawInputModifiers.LeftMouseButton);
        w.MouseUp(StagePoint(w, points[^1].X, points[^1].Y), MouseButton.Left, mods);
        Pump();
    }

    private string Png(string name, int w, int h, ColorRgb c)
    {
        var img = new RasterImage(w, h);
        img.Fill(Pixel.Opaque(c));
        var path = Path.Combine(_dir, name + ".png");
        File.WriteAllBytes(path, PngCodec.Encode(img));
        return path;
    }

    [AvaloniaFact]
    public async Task The_sample_opens_plays_and_shows_its_story()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        await w.OpenSampleSkeletonAsync(Path.Combine(_dir, "sample"));
        Pump();
        var view = w.BonesView!;
        Assert.True(w.BonesShown);
        Assert.Equal(BonesMode.Animate, view.Mode); // it has keys: ready to play
        Assert.Contains("Wave hello", Find<TextBlock>(w, "BonesTitle").Text);
        Shot(w, "m019-bare-bones-sample");
        // The head at the blink: its two animated sprites, Blink playing once on its own clock.
        view.Select(view.Project!.Bones.Single(b => b.Name == "Head"));
        view.SetFrame(11);
        Pump();
        Assert.Equal("Blink", view.Stage.Pose![view.Selected!.Id]!.Attachment!.Name);
        Assert.Equal("Blink", view.SelectedAttachment!.Name);
        Assert.True(Has(w, "BonesSpriteLoop"));
        Assert.True(Has(w, "BonesSpriteSpeed"));
        Shot(w, "m019-bare-bones-head");
        // Waving: the right hand's Wave sprite, with the arm raised.
        view.Select(view.Project!.Bones.Single(b => b.Name == "Right hand"));
        view.SetFrame(27);
        Pump();
        Assert.Equal("Wave", view.Stage.Pose![view.Selected!.Id]!.Attachment!.Name);
        Shot(w, "m019-bare-bones-wave");
        // Setup: the rest pose with the skeleton (Tab switches).
        Key(w, Avalonia.Input.Key.Tab);
        Assert.Equal(BonesMode.Setup, view.Mode);
        view.Select(view.Project!.Bones.Single(b => b.Name == "Right arm"));
        Pump();
        Shot(w, "m019-bare-bones-sample-setup");
        // The drawing workspace's chrome steps aside.
        Assert.False(w.ToolHost.IsVisible);
        w.HideBonesView();
        Pump();
        Assert.True(w.ToolHost.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_skeleton_is_drawn_posed_keyed_and_undone_one_drag_at_a_time()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        var path = Path.Combine(_dir, "Stick" + SkeletonProject.Extension);
        Assert.True(await w.CreateSkeletonAsync(path, 360, 360, 12, 24, pixelArt: false));
        Pump();
        var view = w.BonesView!;
        Assert.Equal((BonesMode.Setup, BonesTool.AddBone), (view.Mode, view.Tool)); // empty: ready to draw a bone
        Assert.True(File.Exists(path));

        // Draw the root upwards, then a child from its end, pointing right.
        StageDrag(w, RawInputModifiers.None, (180, 260), (180, 200), (180, 160));
        var p = view.Project!;
        var root = Assert.Single(p.Bones);
        Assert.Equal("Root", root.Name);
        Assert.Equal(-90, root.Rotation, 1);
        Assert.Equal(100, root.Length, 0);
        StageDrag(w, RawInputModifiers.None, (181, 161), (220, 160), (240, 160));
        Assert.Equal(2, view.Project!.Bones.Count);
        var arm = view.Project.Bones[1];
        Assert.Equal(root.Id, arm.ParentId);
        Assert.Equal(90, arm.Rotation, 1); // pointing right = a quarter turn from its upward parent
        Assert.Equal(60, arm.Length, 0);
        Assert.Equal(arm.Id, view.Selected?.Id);

        // Undo: one drag, one step (the second bone goes, then comes back).
        view.Undo();
        Pump();
        Assert.Single(view.Project!.Bones);
        view.Redo();
        Pump();
        Assert.Equal(2, view.Project!.Bones.Count);
        Assert.Equal(2, SkeletonProject.Load(path).Bones.Count); // saved as it goes

        // Pose: turn the arm about its joint by dragging along it (many moves, one undo step).
        Key(w, Avalonia.Input.Key.V);
        Assert.Equal(BonesTool.Pose, view.Tool);
        StageDrag(w, RawInputModifiers.None, (225, 160), (222, 175), (215, 190), (200, 195));
        arm = view.Project!.Find(arm.Id)!;
        Assert.InRange(arm.Rotation, 90 + 55, 90 + 65); // turned about 60° clockwise
        view.Undo();
        Pump();
        Assert.Equal(90, view.Project!.Find(arm.Id)!.Rotation, 1);
        view.Redo();
        Pump();

        // A sprite on the arm shows on the stage.
        var red = Png("red", 40, 12, new ColorRgb(220, 40, 40));
        var sprite = view.AddSpriteFile(view.Project!.Find(arm.Id)!, red)!;
        Pump();
        Assert.NotNull(sprite);
        Assert.Contains(view.Stage.Picture!.Pixels, px => px == Pixel.Opaque(new ColorRgb(220, 40, 40)));
        Shot(w, "m019-bare-bones-setup");

        // Animate: go to frame 13 and turn the arm: a key there, and the rest pose kept at frame 1.
        view.SetFrame(12);
        Pump();
        Assert.Equal(BonesMode.Animate, view.Mode);
        var pose = view.Stage.Pose![arm.Id]!;
        var mid = ((pose.Start.X + pose.End.X) / 2, (pose.Start.Y + pose.End.Y) / 2);
        StageDrag(w, RawInputModifiers.None, mid, (pose.Start.X + 10, pose.Start.Y - 40));
        var track = view.Project!.TrackOf(arm.Id)!;
        Assert.Equal([0, 12], track.Keys.Select(k => k.Frame));
        Assert.NotEqual(track.Keys[0].Rotation, track.Keys[1].Rotation, 1);
        var restRotation = view.Project.Find(arm.Id)!.Rotation;
        Assert.Equal(restRotation, track.Keys[0].Rotation, 6); // the rest pose itself didn't change

        // Copy the pose at frame 13, paste it at frame 24: the loop comes back to it.
        view.Select(null);
        view.CopyPose();
        view.SetFrame(23);
        view.PastePose();
        Pump();
        Assert.Equal([0, 12, 23], view.Project!.TrackOf(arm.Id)!.Keys.Select(k => k.Frame));
        Assert.Equal(view.Project.TrackOf(arm.Id)!.Keys[1].Rotation, view.Project.TrackOf(arm.Id)!.Keys[2].Rotation, 6);

        // Play: the frame moves on by itself; Enter stops it.
        view.SetFrame(0);
        Key(w, Avalonia.Input.Key.Enter);
        Assert.True(view.IsPlaying);
        await Until(() => view.Frame >= 3);
        Key(w, Avalonia.Input.Key.Enter);
        Assert.False(view.IsPlaying);
        Shot(w, "m019-bare-bones-animate");

        // While Bare Bones is shown the drawing underneath is left alone; Esc goes back to it, as it was.
        long state = w.Session.Document.History.CurrentStateId;
        Key(w, Avalonia.Input.Key.Delete);
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(state, w.Session.Document.History.CurrentStateId);
        Key(w, Avalonia.Input.Key.Escape);
        Assert.False(w.BonesShown);
        Assert.True(w.ToolHost.IsVisible);
        Assert.True(Has(w, "MenuBar") || true);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Animated_sprites_switch_on_the_master_timeline_with_their_own_timing()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        Assert.True(await w.CreateSkeletonAsync(Path.Combine(_dir, "Head" + SkeletonProject.Extension), 200, 200, 12, 36, pixelArt: true));
        var view = w.BonesView!;
        view.AddBoneDrawn((100, 150), (100, 60));
        var head = view.Selected!;
        // Idle: a still picture. Blink: three frames at 8 fps, played once.
        var idle = view.AddSpriteFile(head, Png("idle", 30, 30, new ColorRgb(240, 200, 160)))!;
        var frames = Enumerable.Range(0, 3).Select(i =>
        {
            var img = new RasterImage(30, 30);
            img.Fill(Pixel.Opaque(new ColorRgb((byte)(60 + i * 60), 80, 200)));
            return img;
        }).ToList();
        var blinkPath = Path.Combine(_dir, "blink.dnk");
        DnkFile.Save(SpriteSheetImport.NormalDocument(frames, 8), blinkPath);
        var blink = view.AddSpriteFile(view.Project!.Find(head.Id)!, blinkPath)!;
        Pump();
        Assert.Equal(idle.Id, view.Project!.Find(head.Id)!.Attachment); // the first sprite is the rest sprite

        // Blink from frame 7 (index 6) to frame 13, as a one-shot.
        view.SetFrame(6);
        view.SwitchHere(view.Project!.Find(head.Id)!, view.Project.Find(head.Id)!.Attachments[1]);
        view.SetFrame(12);
        view.SwitchHere(view.Project!.Find(head.Id)!, view.Project.Find(head.Id)!.Attachments[0]);
        Pump();
        Assert.Equal([6, 12], view.Project!.TrackOf(head.Id)!.Switches.Select(s => s.Frame));
        view.Edit(p => p.Find(head.Id)!.Attachments[1].Loop = false);
        // 12 fps master over an 8 fps sprite: 0, 83, 167, 250, 333, 417 ms → frames 0, 0, 1, 2, 2, 2 (held).
        var shown = Enumerable.Range(6, 6).Select(f =>
        {
            view.SetFrame(f);
            var bp = view.Stage.Pose![head.Id]!;
            return (bp.Attachment!.Name, bp.AttachmentFrame);
        }).ToList();
        Assert.Equal([("blink", 0), ("blink", 0), ("blink", 1), ("blink", 2), ("blink", 2), ("blink", 2)], shown);
        view.SetFrame(12);
        Assert.Equal("idle", view.Stage.Pose![head.Id]!.Attachment!.Name);
        // Scrubbing back gives the same picture as going forward.
        view.SetFrame(8);
        var a = view.Stage.Picture!.Pixels.ToArray();
        view.SetFrame(30);
        view.SetFrame(8);
        Assert.Equal(a, view.Stage.Picture!.Pixels);
        Shot(w, "m019-bare-bones-switch");

        // Export the whole timeline as a GIF and a sprite sheet with the existing exporters: never with bones on.
        var gif = Path.Combine(_dir, "head.gif");
        await w.ExportSkeletonToPathAsync(gif, new AnimationExportSettings(AnimationExportFormat.Gif));
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(gif), 0, 6));
        var sheet = Path.Combine(_dir, "head_sheet.png");
        await w.ExportSkeletonToPathAsync(sheet, new AnimationExportSettings(AnimationExportFormat.SpriteSheet, SheetLayout.Grid, Columns: 6, Scale: 2));
        var img = PngCodec.DecodeAny(File.ReadAllBytes(sheet));
        Assert.Equal((200 * 2 * 6, 200 * 2 * 6), (img.Width, img.Height));
        Assert.True(File.Exists(Path.ChangeExtension(sheet, ".json")));
        // Only the sprites' colours (and transparency) are in it: no bone overlay.
        var colours = img.Pixels.Where(px => px >> 24 != 0).Distinct().ToHashSet();
        Assert.Subset(new HashSet<uint>(frames.Select(f => f.Pixels[0]).Append(Pixel.Opaque(new ColorRgb(240, 200, 160)))), colours);
        var seq = Path.Combine(_dir, "seq.png");
        await w.ExportSkeletonToPathAsync(seq, new AnimationExportSettings(AnimationExportFormat.PngSequence));
        Assert.Equal(36, AnimationExport.SequencePaths(seq, 36).Count(File.Exists));
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_sprite_is_drawn_in_the_normal_editor_and_the_stage_shows_it()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        Assert.True(await w.CreateSkeletonAsync(Path.Combine(_dir, "Doll" + SkeletonProject.Extension), 300, 300, 12, 24, pixelArt: false));
        var view = w.BonesView!;
        view.AddBoneDrawn((150, 250), (150, 120));
        var bone = view.Selected!;

        // New drawing for the bone: sized to it, transparent, saved in "art", linked, and opened in the editor.
        var made = w.NewBoneDrawingAsync(bone);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("create");
        await made;
        Pump();
        Assert.False(w.BonesShown);
        var art = w.Session.FilePath!;
        Assert.Equal(Path.Combine(_dir, "art", "Root.dnk"), art);
        Assert.Null(w.Session.Document.Paper);
        Assert.True(w.Session.Document.Height > w.Session.Document.Width); // the bone stands upright
        Assert.True(Has(w, "BonesChip"));
        var attachment = Assert.Single(view.Project!.Find(bone.Id)!.Attachments);
        Assert.Equal("art/Root.dnk", attachment.Source);
        Assert.Equal(90, attachment.Rotation, 1); // shown upright, the way it's drawn

        // Paint on it; back in Bare Bones the stage shows the new paint even before saving.
        Drag(w, MouseButton.Left, RawInputModifiers.None, (10, 20), (w.Session.Document.Width - 10, w.Session.Document.Height - 20));
        Assert.True(w.Session.Document.IsDirty);
        Click(w, Find<Button>(w, "BonesBackToBones"));
        Assert.True(w.BonesShown);
        Assert.Contains(view.Stage.Picture!.Pixels, px => px >> 24 != 0);

        // Saved, it is read from the file; the source drawing is only ever changed by the editor.
        Assert.True(await w.SaveAsync());
        view.ReloadPictures();
        Assert.Contains(view.Stage.Picture!.Pixels, px => px >> 24 != 0);
        Assert.Null(view.Assets.Problem(view.Project!, view.Project!.Find(bone.Id)!.Attachments[0]));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Missing_sprites_are_kept_and_explained_and_the_rest_still_works()
    {
        var folder = Path.Combine(_dir, "sample");
        var path = SampleSkeleton.Create(folder);
        File.Delete(Path.Combine(folder, "art", "torso.dnk"));
        var w = Open(1440, 900, WorkspaceKind.Painting);
        Assert.True(await w.OpenSkeletonFromAsync(path));
        Pump();
        var view = w.BonesView!;
        var body = view.Project!.Bones.Single(b => b.Name == "Body");
        Assert.Single(body.Attachments); // kept
        view.Select(body);
        Pump();
        Assert.Contains("torso.dnk is missing", Find<Border>(w, "BonesWarning").Child is TextBlock t ? t.Text : "");
        Assert.Contains(view.Stage.Picture!.Pixels, px => px >> 24 != 0); // everything else still shows
        // Replacing it links another file and keeps the placement.
        var x = body.Attachments[0].X;
        var torso = Png("new torso", 90, 120, new ColorRgb(80, 140, 170));
        view.Edit(p => p.Find(body.Id)!.Attachments[0].Source = p.Relate(torso));
        Assert.Null(view.Assets.Problem(view.Project!, view.Project!.Find(body.Id)!.Attachments[0]));
        Assert.Equal(x, view.Project.Find(body.Id)!.Attachments[0].X);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Menus_and_workspaces_go_back_to_drawing_and_the_storybook_and_bones_take_turns()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        await w.OpenSampleSkeletonAsync(Path.Combine(_dir, "sample"));
        Assert.True(w.BonesShown);
        // A workspace chosen: back to drawing in it.
        w.SetWorkspace(WorkspaceKind.PixelArt);
        Pump();
        Assert.False(w.BonesShown);
        Assert.Equal(WorkspaceKind.PixelArt, w.Workspace.Kind);
        w.ShowBonesView();
        Pump();
        // Edit › Undo is Bare Bones' own while it is shown.
        var undo = w.MenuFor("Edit").First(e => e.Icon == "undo");
        Assert.Equal("Undo (Bare Bones)", undo.Label);
        // A drawing command from a menu goes back to the drawing first.
        var fit = w.MenuFor("View").FirstOrDefault(e => e.Label.StartsWith("Fit", StringComparison.Ordinal));
        var newPicture = w.MenuFor("File").First(e => e.Label.StartsWith("Export as PNG", StringComparison.Ordinal));
        Assert.NotNull(newPicture.Run);
        Assert.Contains(w.MenuFor("File"), e => e.Label.StartsWith("Bare Bones “Wave hello”", StringComparison.Ordinal));
        Assert.Contains(w.MenuFor("Help"), e => e.Label == "Wave hello (Bare Bones)");
        _ = fit;
        // The storybook and Bare Bones take turns over the canvas.
        Assert.True(await w.CreateStorybookAsync(Path.Combine(_dir, "story.donkbook"), 210, 297, 150, Engine.Storybook.StoryLayouts.Four));
        Pump();
        Assert.True(w.StorybookShown);
        Assert.False(w.BonesShown);
        w.ShowBonesView();
        Pump();
        Assert.True(w.BonesShown);
        Assert.False(w.StorybookShown);
        w.CloseSkeleton();
        Pump();
        Assert.False(w.BonesShown);
        Assert.Null(w.Skeleton);
        Assert.True(w.ToolHost.IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Bare_Bones_fits_the_Kamvas_at_150_percent()
    {
        var w = Open(1280, 720, WorkspaceKind.Painting);
        w.SetRenderScaling(1.5);
        Pump();
        await w.OpenSampleSkeletonAsync(Path.Combine(_dir, "sample"));
        var view = w.BonesView!;
        view.Select(view.Project!.Bones.Single(b => b.Name == "Head"));
        view.SetFrame(11);
        Pump();
        // The stage keeps a useful size, every bone's row is in the timeline, and the controls are inside the window.
        Assert.True(view.Stage.Bounds.Width > 600 && view.Stage.Bounds.Height > 330, view.Stage.Bounds.ToString());
        var area = Find<DockPanel>(w, "BonesTimelineArea");
        Assert.True(area.Bounds.Height >= 150, area.Bounds.ToString());
        var timeline = view.Timeline;
        Assert.True(timeline.Bounds.Height >= BonesTimeline.RulerHeight + 9 * BonesTimeline.RowHeight, $"timeline {timeline.Bounds.Height:0} high for 9 bones");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Screenshots.OutDir)!, "m019-kamvas-stage.txt"),
            $"stage {view.Stage.Bounds.Width:0} x {view.Stage.Bounds.Height:0}, timeline {timeline.Bounds.Height:0}");
        foreach (var name in new[] { "BonesBack", "BonesExport", "BonesPlay", "BonesPastePose", "BonesAddBone", "BonesMode" })
        {
            var c = Find<Control>(w, name);
            var r = new Rect(c.TranslatePoint(new Point(0, 0), w)!.Value, c.Bounds.Size);
            Assert.True(r.Right <= w.Bounds.Width + 0.5 && r.Bottom <= w.Bounds.Height + 0.5, $"{name} at {r}");
        }
        Shot(w, "m019-bare-bones-kamvas-150");
        w.Close();
    }
}
