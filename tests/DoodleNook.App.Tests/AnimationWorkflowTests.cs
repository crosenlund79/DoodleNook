using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using SkiaSharp;
using static DoodleNook.App.Tests.WindowTests;
using K = Avalonia.Input.Key;

namespace DoodleNook.App.Tests;

/// <summary>Complete animation workflows through the real window, with real files.</summary>
public sealed class AnimationWorkflowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-animflow-" + Guid.NewGuid().ToString("N"));

    public AnimationWorkflowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
        return Path.Combine([dir!.FullName, .. parts]);
    }

    private static void Line(MainWindow w, double x0, double y0, double x1, double y1) =>
        Drag(w, MouseButton.Left, RawInputModifiers.None, (x0, y0), ((x0 + x1) / 2, (y0 + y1) / 2), (x1, y1));

    [AvaloniaFact]
    public async Task Pixel_art_walk_cycle_to_a_sprite_sheet()
    {
        var w = Open(1280, 800, WorkspaceKind.PixelArt);
        w.SetWorkspace(WorkspaceKind.Animation);
        w.CreateNewAnimation(new NewAnimationChoice(24, 24, 1, 8, PixelArt: true, Paper: null));
        Pump();
        Assert.Equal(ToolKind.PixelPencil, w.Session.Tool);
        Line(w, 4.5, 20.5, 19.5, 20.5);      // frame 1
        Key(w, K.N, RawInputModifiers.Shift); // frame 2: a copy to change
        Line(w, 10.5, 4.5, 10.5, 12.5);
        Key(w, K.N);                          // frame 3: blank, with the onion skin to follow
        Pump();
        Assert.True(w.Session.Document.OnionVisible);
        Line(w, 14.5, 4.5, 14.5, 12.5);
        var doc = w.Session.Document;
        Assert.Equal(3, doc.FrameCount);

        var path = Path.Combine(_dir, "walk.png");
        await w.ExportAnimationToPathAsync(path, new AnimationExportSettings(AnimationExportFormat.SpriteSheet, SheetLayout.Horizontal, Spacing: 2, Json: true));
        var sheet = PngCodec.Decode(File.ReadAllBytes(path));
        Assert.Equal((24 * 3 + 4, 24), (sheet.Width, sheet.Height));
        Assert.Equal(255, Pixel.A(sheet[10, 20]));          // frame 1: the ground line
        Assert.Equal(0, Pixel.A(sheet[10, 8]));
        Assert.Equal(255, Pixel.A(sheet[26 + 10, 20]));     // frame 2: copy + new stroke
        Assert.Equal(255, Pixel.A(sheet[26 + 10, 8]));
        Assert.Equal(0, Pixel.A(sheet[52 + 10, 20]));       // frame 3: only its own drawing (onion skins are not exported)
        Assert.Equal(255, Pixel.A(sheet[52 + 14, 8]));
        Assert.True(File.Exists(Path.Combine(_dir, "walk.json")));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Hand_drawn_animation_onion_play_save_reopen_and_gif()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.Animator.ManualClock = true;
        w.CreateNewAnimation(new NewAnimationChoice(320, 200, 1, 12, PixelArt: false, Paper: ColorRgb.White));
        Pump();
        Line(w, 40, 100, 120, 100);
        Key(w, K.N);
        Pump();
        Assert.True(w.Session.Document.OnionVisible);
        Line(w, 140, 100, 220, 100);
        Key(w, K.N);
        Line(w, 240, 100, 300, 100);
        w.Session.SetFrameDuration(1, 250);
        var doc = w.Session.Document;

        w.Session.GoToFrame(0);
        Key(w, K.Enter);
        w.Animator.Tick(100);
        Assert.Equal(1, doc.CurrentFrame);
        w.Animator.Tick(300); // frame 2 is held for 250 ms
        Assert.Equal(1, doc.CurrentFrame);
        w.Animator.Tick(340);
        Assert.Equal(2, doc.CurrentFrame);
        Key(w, K.Enter);
        Assert.False(w.Animator.IsPlaying);

        var path = Path.Combine(_dir, "line.dnk");
        Assert.True(await w.SaveToPathAsync(path));
        Assert.False(doc.IsDirty);
        var frames = Enumerable.Range(0, 3).Select(f => doc.RenderFrame(f).Pixels).ToArray();
        w.Close();

        var again = Open(1280, 800, WorkspaceKind.Painting);
        Assert.True(await again.OpenPathAsync(path));
        Assert.Contains("Alt+6", again.LastToast);
        var back = again.Session.Document;
        Assert.Equal(3, back.FrameCount);
        Assert.Equal(250, back.Animation!.DurationOf(1));
        for (int f = 0; f < 3; f++) Assert.Equal(frames[f], back.RenderFrame(f).Pixels);

        var gif = Path.Combine(_dir, "line.gif");
        await again.ExportAnimationToPathAsync(gif, new AnimationExportSettings(AnimationExportFormat.Gif, Background: true));
        using var codec = SKCodec.Create(gif);
        Assert.Equal(3, codec.FrameCount);
        Assert.InRange(codec.FrameInfo[1].Duration, 240, 260);
        using var bmp = new SKBitmap(codec.Info.Width, codec.Info.Height);
        codec.GetPixels(bmp.Info, bmp.GetPixels(), new SKCodecOptions(0));
        Assert.Equal(255, bmp.GetPixel(10, 10).Red); // the white paper was included
        again.Close();
    }

    [AvaloniaFact]
    public async Task A_mission_005_picture_becomes_an_animation_and_still_opens_afterwards()
    {
        var fixture = RepoFile("tests", "DoodleNook.Engine.Tests", "Fixtures", "mission005-two-layers.dnk");
        var copy = Path.Combine(_dir, "old.dnk");
        File.Copy(fixture, copy);
        var w = Open(1280, 800, WorkspaceKind.Painting);
        Assert.True(await w.OpenPathAsync(copy));
        var doc = w.Session.Document;
        var original = doc.Flatten().Pixels;
        Assert.False(doc.IsAnimated);

        Key(w, K.D6, RawInputModifiers.Alt);
        Assert.Contains("press N", w.LastToast);
        Key(w, K.N, RawInputModifiers.Shift);
        Assert.Equal(2, doc.FrameCount);
        Assert.Equal(original, doc.RenderFrame(0).Pixels); // the picture is frame 1, unchanged
        Assert.True(await w.SaveAsync());                  // saved in place as an animation (.dnk version 2)
        w.Close();

        var again = Open(1280, 800, WorkspaceKind.Animation);
        Assert.True(await again.OpenPathAsync(copy));
        var back = again.Session.Document;
        Assert.True(back.IsAnimated);
        Assert.Equal(2, back.FrameCount);
        Assert.Equal(original, back.RenderFrame(0).Pixels);
        Assert.Equal(doc.Layers.Select(l => l.Name), back.Layers.Select(l => l.Name));
        again.Close();
    }

    [AvaloniaFact]
    public void Switching_workspaces_keeps_the_animation_frame_and_history()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.CreateNewAnimation(new NewAnimationChoice(200, 120, 4, 12, false, null));
        Pump();
        w.Session.GoToFrame(2);
        Line(w, 20, 30, 180, 30);
        var doc = w.Session.Document;
        foreach (var kind in new[] { WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.PixelArt, WorkspaceKind.PureSketch, WorkspaceKind.QuickSketch, WorkspaceKind.Animation })
        {
            w.SetWorkspace(kind);
            Pump();
            Assert.Same(doc, w.Session.Document);
            Assert.Equal(2, doc.CurrentFrame);
        }
        // Painting on frame 3 in another workspace edits that frame only.
        w.SetWorkspace(WorkspaceKind.Painting);
        Line(w, 20, 90, 180, 90);
        Assert.True(Pixel.A(doc.CelAt(doc.ActiveLayer, 2)!.Image[100, 90]) > 0);
        Assert.Null(doc.CelAt(doc.ActiveLayer, 1));
        Assert.True(doc.History.CanUndo);
        w.Close();
    }
}
