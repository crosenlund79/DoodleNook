using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 010 workflows F (a game graphic) and G (palette-locked artwork), through the window.</summary>
public sealed class FinishingTouchesWorkflowTests : IDisposable
{
    private static readonly ColorRgb Skin = new(232, 185, 138), Hair = new(91, 58, 107), Shirt = new(63, 106, 130), Eye = new(30, 26, 34);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-m010-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // Nothing to clean up.
        }
    }

    /// <summary>A 160 × 120 character: a head with hair and an eye, a shirt, soft (half-transparent) edges, the rest transparent.</summary>
    private static RasterImage Character()
    {
        var img = new RasterImage(160, 120);
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 160; x++)
            {
                double head = Math.Sqrt((x - 80) * (x - 80) + (y - 45) * (y - 45));
                if (head < 30) img[x, y] = Pixel.Opaque(y < 30 ? Hair : Skin);
                else if (head < 31.5) img[x, y] = Pixel.FromStraight(Skin.R, Skin.G, Skin.B, 128);
                if (y >= 78 && y < 118 && Math.Abs(x - 80) < 35) img[x, y] = Pixel.Opaque(Shirt);
                if (Math.Abs(x - 70) < 3 && Math.Abs(y - 45) < 3) img[x, y] = Pixel.Opaque(Eye);
            }
        return img;
    }

    private static async Task Until(Func<bool> done, int ms = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            Pump();
            await Task.Delay(15);
            Pump();
        }
    }

    [AvaloniaFact]
    public async Task Workflow_F_refine_a_game_graphic_with_liquify_and_smudge_then_save_both_files()
    {
        var assets = Path.Combine(_dir, "Game", "assets");
        Directory.CreateDirectory(Path.Combine(assets, "characters"));
        var png = Path.Combine(assets, "characters", "player.png");
        File.WriteAllBytes(png, PngCodec.Encode(Character()));
        var original = File.ReadAllBytes(png);

        var w = Open(1280, 800, WorkspaceKind.Painting);
        var opening = w.OpenGameFolderAsync(assets);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("start");
        Assert.True(await opening);
        var image = w.Game!.Find("characters/player.png")!;
        Assert.True(await w.OpenGameImageAsync(image));
        w.Canvas.FitToView();
        Pump();

        // Refine with Liquify: widen the face a little.
        Assert.True(await w.StartLiquifyAsync());
        w.Session.SetLiquifyOptions(w.Session.Liquify with { Mode = LiquifyMode.Push, Size = 30, Strength = 0.8 });
        Drag(w, MouseButton.Left, RawInputModifiers.None, (108, 50), (113, 50), (118, 50));
        Key(w, Avalonia.Input.Key.Enter);
        Assert.Null(w.Session.ActiveLiquify);
        // Blend part of it.
        w.Session.SelectTool(ToolKind.Smudge);
        w.Session.SetRetouchOptions(w.Session.Retouch with { SmudgeSize = 12, SmudgeStrength = 0.7 });
        Drag(w, MouseButton.Left, RawInputModifiers.None, (80, 25), (80, 32), (80, 38));
        Assert.Equal(2, w.Session.Document.History.UndoCount);

        // Ctrl+S: the editable .dnk and the game's PNG, the original backed up; still In progress.
        Key(w, Avalonia.Input.Key.S, RawInputModifiers.Control);
        await Until(() => image.Status == GameImageStatus.InProgress);
        Assert.True(File.Exists(image.DnkPath));
        Assert.Equal(original, File.ReadAllBytes(image.BackupPath));
        var saved = PngCodec.Decode(File.ReadAllBytes(png));
        Assert.Equal(w.Session.Document.Flatten().Pixels, saved.Pixels);
        Assert.NotEqual(Character().Pixels, saved.Pixels);
        Assert.Equal(GameImageStatus.InProgress, GameProject.Open(assets).Find("characters/player.png")!.Status); // not Complete by itself
        w.Session.Document.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public void Workflow_G_palette_locked_artwork_stays_on_its_palette_with_transparency_after_every_new_tool()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var s = w.Session;
        var doc = Document.FromImage(Character(), "Hero");
        var top = doc.AddLayer("Sketch");
        // An unrestricted reference on another layer (off-palette colours), for cloning "what is seen".
        var edit = doc.BeginEdit(top, "Reference");
        edit.Capture(new PixelRect(0, 0, 40, 20));
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 40; x++)
                top.Image[x, y] = Pixel.Opaque(new ColorRgb((byte)(100 + x * 3), 200, (byte)(30 + y * 5)));
        edit.Changed(new PixelRect(0, 0, 40, 20));
        edit.Commit();
        doc.SetActiveLayer(doc.Layers[0]);
        s.ReplaceDocument(doc, null);
        var palette = new Palette("Hero", [Skin, Hair, Shirt, Eye]);
        doc.LockToPalette(palette);
        w.Canvas.FitToView();
        Pump();
        var conformer = new PaletteConformer(palette.Colors);
        var hero = doc.Layers[0];
        var transparentBefore = Enumerable.Range(0, hero.Image.Pixels.Length).Where(i => hero.Image.Pixels[i] == 0 && i % 160 > 130).ToArray();

        // Smudge existing colours.
        s.SelectTool(ToolKind.Smudge);
        s.SetRetouchOptions(s.Retouch with { SmudgeSize = 14, SmudgeStrength = 0.8 });
        Drag(w, MouseButton.Left, RawInputModifiers.None, (80, 20), (80, 40), (80, 60));
        // Clone a region from what is seen (including the off-palette reference).
        s.SelectTool(ToolKind.Clone);
        s.SetRetouchOptions(s.Retouch with { CloneSize = 16, SampleAll = true });
        s.SetCloneSource(20, 10);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (60, 95), (70, 95));
        // Deform part of it.
        s.BeginLiquify();
        Assert.Equal(LiquifySampling.PaletteSafe, s.ActiveLiquify!.Sampling);
        s.SetLiquifyOptions(s.Liquify with { Mode = LiquifyMode.Push, Size = 40, Strength = 0.9 });
        Drag(w, MouseButton.Left, RawInputModifiers.None, (110, 45), (125, 50));
        s.ApplyLiquify();
        // And bend it with Warp.
        doc.SetSelection(SelectionMask.Rectangle(160, 120, new PixelRect(40, 70, 80, 50)), "Select");
        w.TransformIn(TransformMode.Warp);
        var g = s.ActiveTransform!.Warp!;
        s.ActiveTransform.SetWarpPoint(13, g.Points[13].X, g.Points[13].Y - 8);
        Key(w, Avalonia.Input.Key.Enter);

        // Export: every pixel is a palette colour (alpha free), soft edges stay soft, empty stays empty.
        var path = Path.Combine(_dir, "hero.png");
        Directory.CreateDirectory(_dir);
        doc.SetLayerProperties(top, top.Properties with { Visible = false });
        ImageFiles.Export(doc, path, ExportFormat.Png);
        var exported = PngCodec.Decode(File.ReadAllBytes(path));
        Assert.All(exported.Pixels, p => Assert.True(conformer.IsPaletteColour(p)));
        Assert.Contains(exported.Pixels, p => Pixel.A(p) is > 0 and < 255);
        Assert.All(transparentBefore, i => Assert.Equal(0u, exported.Pixels[i]));
        Assert.True(s.Document.History.UndoCount >= 5);
        s.Document.MarkSaved();
        w.Close();
    }
}
