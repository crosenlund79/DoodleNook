using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Editor;
using DoodleNook.App.Controls;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 009 — Game Edit Mode: the catalog, the two-part save, backups and progress.</summary>
public sealed class GameEditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-game-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _assets;

    public GameEditTests()
    {
        _assets = Path.Combine(_dir, "MyGame", "assets");
        Png("characters/player.png", 32, 24, Pixel.Opaque(new ColorRgb(200, 40, 40)));
        Png("characters/enemy.png", 16, 16, Pixel.Opaque(new ColorRgb(40, 160, 60)));
        Png("environment/trees/tree.png", 20, 30, Pixel.FromStraight(40, 60, 200, 128));
        Png("logo.png", 8, 8, Pixel.Opaque(ColorRgb.White));
        File.WriteAllText(Path.Combine(_assets, "notes.txt"), "not an image");
        Png("_DoodleNook/Originals/characters/stale.png", 4, 4, Pixel.Opaque(ColorRgb.Black)); // never part of the catalog
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // A test that made a folder unwritable may leave it behind in the temp folder.
        }
    }

    /// <summary>A PNG whose left half is the colour and right half transparent.</summary>
    private void Png(string relative, int w, int h, uint colour)
    {
        var path = Path.Combine(_assets, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var img = new RasterImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w / 2; x++)
                img[x, y] = colour;
        File.WriteAllBytes(path, PngCodec.Encode(img));
    }

    private static Button ButtonWithText(MainWindow w, string text) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

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

    private async Task<MainWindow> GameWindow(bool backups = true)
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var opening = w.OpenGameFolderAsync(_assets);
        await Until(() => w.Dialogs.IsOpen); // first time: the backup question
        Pump();
        if (!backups) Click(w, Find<Button>(w, "GameBackupCheck"));
        w.Dialogs.Close("start");
        Assert.True(await opening);
        Pump();
        return w;
    }

    private static async Task Save(MainWindow w)
    {
        var task = w.SaveAsync();
        await Until(() => task.IsCompleted || w.Dialogs.IsOpen);
        if (!task.IsCompleted) return; // a dialog is waiting; the test answers it
        Assert.True(await task);
    }

    private static void Paint(MainWindow w, double x0, double y, double x1)
    {
        w.Canvas.FitToView();
        Pump();
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new BrushSettings { Size = 4, Hardness = 1, Opacity = 1 }, "pencil");
        w.Session.SetForeground(new ColorRgb(20, 20, 120));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (x0, y), (x1, y));
    }

    // ================================================================== discovery and progress

    [Fact]
    public void Finds_every_png_in_subfolders_with_relative_paths_and_skips_the_working_folder()
    {
        var p = GameProject.Open(_assets);
        Assert.Equal(["characters/enemy.png", "characters/player.png", "environment/trees/tree.png", "logo.png"], p.Images.Select(i => i.RelativePath));
        var player = p.Find("characters/player.png")!;
        Assert.Equal("characters", player.Folder);
        Assert.Equal("player.png", player.FileName);
        Assert.Equal(Path.Combine(_assets, "_DoodleNook", "characters", "player.dnk"), player.DnkPath);
        Assert.Equal(Path.Combine(_assets, "_DoodleNook", "Originals", "characters", "player.png"), player.BackupPath);
        Assert.All(p.Images, i => Assert.Equal(GameImageStatus.NotStarted, i.Status));
        Assert.False(File.Exists(p.CatalogPath)); // just looking writes nothing
    }

    [Fact]
    public void Progress_counts_only_images_marked_complete_and_statuses_survive_reopening_and_rescans()
    {
        var p = GameProject.Open(_assets);
        p.SetBackupOriginals(false);
        var player = p.Find("characters/player.png")!;
        var enemy = p.Find("characters/enemy.png")!;
        p.RecordSaved(player);                               // first complete save
        Assert.Equal(GameImageStatus.InProgress, player.Status);
        p.RecordSaved(player);                               // more saves stay In progress
        Assert.Equal(GameImageStatus.InProgress, player.Status);
        p.MarkComplete(enemy);                               // complete only when asked
        Assert.Equal(new GameProgress(4, 2, 1, 1), p.Progress);
        Assert.Equal(25, p.Progress.Percent, 6);
        p.RecordSaved(enemy);                                // saving a complete image keeps it complete
        Assert.Equal(GameImageStatus.Complete, enemy.Status);

        // Like closing and starting DoodleNook again.
        var again = GameProject.Open(_assets);
        Assert.False(again.BackupOriginals);
        Assert.Equal(GameImageStatus.InProgress, again.Find("characters/player.png")!.Status);
        Assert.Equal(GameImageStatus.Complete, again.Find("characters/enemy.png")!.Status);
        again.MarkInProgress(again.Find("characters/enemy.png")!);
        Assert.Equal(GameImageStatus.InProgress, GameProject.Open(_assets).Find("characters/enemy.png")!.Status);

        // A rescan: a new PNG is Not started, a removed one stops counting, a returning one keeps its status.
        Png("characters/boss.png", 8, 8, Pixel.Opaque(ColorRgb.Black));
        var playerBytes = File.ReadAllBytes(player.PngPath);
        File.Delete(player.PngPath);
        again.Rescan();
        Assert.Equal(GameImageStatus.NotStarted, again.Find("characters/boss.png")!.Status);
        Assert.Null(again.Find("characters/player.png"));
        Assert.Equal(4, again.Progress.Total);
        Assert.Equal(GameImageStatus.InProgress, again.Find("characters/enemy.png")!.Status);
        File.WriteAllBytes(player.PngPath, playerBytes);
        again.Rescan();
        Assert.Equal(GameImageStatus.InProgress, again.Find("characters/player.png")!.Status);
    }

    // ================================================================== editing and saving

    [AvaloniaFact]
    public async Task Ctrl_S_saves_the_dnk_then_replaces_the_png_in_place_and_backs_up_the_original_once()
    {
        var w = await GameWindow();
        Assert.True(Has(w, "GameCatalog"));
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "assets — 4 images");
        Assert.Equal("Not started: 4   ·   In progress: 0   ·   Complete: 0", Find<TextBlock>(w, "GameCounts").Text);
        var game = w.Game!;
        var player = game.Find("characters/player.png")!;
        var original = File.ReadAllBytes(player.PngPath);

        // Clicking a tile opens the PNG as an editable picture; looking changes nothing.
        Click(w, w.GetVisualDescendants().OfType<Border>().First(b => b.Name == "GameTile_characters/player.png"));
        await Until(() => w.EditingGameImage);
        Assert.Equal((32, 24), (w.Session.Document.Width, w.Session.Document.Height));
        Assert.Equal($"DoodleNook — {MainWindow.Milestone} — player.dnk", w.Title);
        Assert.Equal(GameImageStatus.NotStarted, player.Status);
        Assert.False(Has(w, "GameCatalog"));
        Assert.True(Has(w, "GameChip"));

        // Edit and save: the .dnk, the backup of the original bytes, then the PNG.
        Paint(w, 2, 20, 30);
        Key(w, Avalonia.Input.Key.S, RawInputModifiers.Control);
        await Until(() => player.Status == GameImageStatus.InProgress);
        Assert.True(File.Exists(player.DnkPath));
        Assert.Equal(original, File.ReadAllBytes(player.BackupPath));
        var png = PngCodec.Decode(File.ReadAllBytes(player.PngPath));
        Assert.Equal((32, 24), (png.Width, png.Height));
        Assert.Equal(w.Session.Document.Flatten().Pixels, png.Pixels);
        Assert.Equal(0u, png[30, 2]);                            // transparency kept
        Assert.Equal(Pixel.Opaque(new ColorRgb(200, 40, 40)), png[2, 2]);
        Assert.NotEqual(0u, png[20, 20]);                        // the new paint
        Assert.False(w.Session.Document.IsDirty);
        Assert.DoesNotContain(Directory.GetFiles(Path.GetDirectoryName(player.PngPath)!), f => f.EndsWith(".tmp", StringComparison.Ordinal));

        // A second save: still In progress, and the backup still holds the very first original.
        Paint(w, 2, 10, 30);
        await Save(w);
        Assert.Equal(GameImageStatus.InProgress, player.Status);
        Assert.Equal(original, File.ReadAllBytes(player.BackupPath));

        // Mark complete from the chip; a later save keeps it complete; Mark in progress reopens it.
        Click(w, Find<Button>(w, "GameMarkComplete"));
        Assert.Equal(GameImageStatus.Complete, player.Status);
        Paint(w, 2, 6, 30);
        await Save(w);
        Assert.Equal(GameImageStatus.Complete, player.Status);
        Click(w, Find<Button>(w, "GameMarkComplete"));
        Assert.Equal(GameImageStatus.InProgress, player.Status);
        Assert.Equal(GameImageStatus.InProgress, GameProject.Open(_assets).Find("characters/player.png")!.Status);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_editable_version_is_reopened_with_its_layers_not_the_flat_png()
    {
        var w = await GameWindow(backups: false);
        var game = w.Game!;
        var tree = game.Find("environment/trees/tree.png")!;
        Assert.True(await w.OpenGameImageAsync(tree));
        w.Session.AddLayer();
        Paint(w, 2, 15, 18);
        await Save(w);
        Assert.False(File.Exists(tree.BackupPath)); // backups were switched off for this folder
        Assert.Equal(2, DnkFile.Load(tree.DnkPath).Layers.Count);

        var logo = game.Find("logo.png")!;
        Assert.True(await w.OpenGameImageAsync(logo));
        Assert.Single(w.Session.Document.Layers);
        Assert.True(await w.OpenGameImageAsync(tree));
        Assert.Equal(2, w.Session.Document.Layers.Count); // the layers are back
        Assert.Equal(tree.DnkPath, w.Session.FilePath);

        // An unsaved change and another image: the usual save / don't save / cancel question.
        Paint(w, 2, 5, 18);
        var switching = w.OpenGameImageAsync(logo);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("cancel");
        Assert.False(await switching);
        Assert.Same(tree, w.GameImage);
        Assert.True(w.Session.Document.IsDirty);
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_failed_backup_or_png_write_leaves_the_png_and_the_status_alone_and_can_be_retried()
    {
        var w = await GameWindow();
        var game = w.Game!;
        var enemy = game.Find("characters/enemy.png")!;
        var original = File.ReadAllBytes(enemy.PngPath);
        Assert.True(await w.OpenGameImageAsync(enemy));
        Paint(w, 1, 8, 15);

        // The backup can't be made (a file stands where its folder should be): the PNG is not replaced.
        var originals = Path.Combine(game.WorkFolder, GameProject.OriginalsFolderName);
        if (Directory.Exists(originals)) Directory.Delete(originals, recursive: true);
        File.WriteAllText(originals, "in the way");
        await Save(w);
        await Until(() => w.Dialogs.IsOpen);
        Assert.True(File.Exists(enemy.DnkPath));                 // the editable version is safe
        Assert.Equal(original, File.ReadAllBytes(enemy.PngPath)); // the game's PNG untouched
        Assert.Equal(GameImageStatus.NotStarted, enemy.Status);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("PNG not updated") == true);

        // Fixed, then "Try again": the backup and the PNG are written and the status moves on.
        File.Delete(originals);
        w.Dialogs.Close("retry");
        await Until(() => enemy.Status == GameImageStatus.InProgress);
        Assert.Equal(original, File.ReadAllBytes(enemy.BackupPath));
        Assert.NotEqual(original, File.ReadAllBytes(enemy.PngPath));
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_png_changed_elsewhere_is_never_overwritten_silently()
    {
        var w = await GameWindow(backups: false);
        var game = w.Game!;
        var logo = game.Find("logo.png")!;
        Assert.True(await w.OpenGameImageAsync(logo));
        Paint(w, 1, 4, 7);
        await Save(w);
        // Another program changes the PNG.
        await Task.Delay(20);
        Png("logo.png", 8, 8, Pixel.Opaque(new ColorRgb(1, 2, 3)));
        File.SetLastWriteTimeUtc(logo.PngPath, DateTime.UtcNow.AddMinutes(1));
        var changed = File.ReadAllBytes(logo.PngPath);
        Assert.True(game.ChangedOutside(logo));
        Paint(w, 1, 2, 7);
        var saving = w.SaveAsync();
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("dnk"); // keep the other program's PNG
        Assert.True(await saving);
        Assert.Equal(changed, File.ReadAllBytes(logo.PngPath));
        w.Close();
    }

    [AvaloniaFact]
    public async Task An_unreadable_dnk_is_reported_and_the_png_can_be_opened_instead()
    {
        var w = await GameWindow(backups: false);
        var game = w.Game!;
        var enemy = game.Find("characters/enemy.png")!;
        Directory.CreateDirectory(Path.GetDirectoryName(enemy.DnkPath)!);
        File.WriteAllText(enemy.DnkPath, "not a zip");
        var opening = w.OpenGameImageAsync(enemy);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("png");
        Assert.True(await opening);
        Assert.Equal((16, 16), (w.Session.Document.Width, w.Session.Document.Height));
        Assert.Equal(GameImageStatus.NotStarted, enemy.Status);
        Assert.Equal("not a zip", File.ReadAllText(enemy.DnkPath)); // only replaced when saving
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_catalog_filters_searches_marks_complete_and_is_remembered()
    {
        var w = await GameWindow(backups: false);
        var game = w.Game!;
        var tiles = w.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is GameImage).ToList();
        Assert.Equal(4, tiles.Count);
        // Mark complete from a tile.
        var logoTile = tiles.First(t => ((GameImage)t.Tag!).RelativePath == "logo.png");
        Click(w, logoTile.GetVisualDescendants().OfType<Button>().First());
        Assert.Equal(GameImageStatus.Complete, game.Find("logo.png")!.Status);
        Assert.Equal("Completion 25%", Find<TextBlock>(w, "GameProgressText").Text);
        // Filters and search.
        Click(w, ButtonWithText(w, "Complete"));
        Assert.Equal(["logo.png"], tiles.Where(t => t.IsVisible).Select(t => ((GameImage)t.Tag!).RelativePath));
        Click(w, ButtonWithText(w, "All"));
        Find<TextBox>(w, "GameSearch").Text = "char";
        Pump();
        Assert.Equal(2, tiles.Count(t => t.IsVisible));

        // Leaving and coming back: the same folder, no folder picker.
        w.LeaveGameEditMode();
        Assert.False(Has(w, "GameCatalog"));
        await w.EnterGameEditModeAsync();
        Assert.True(Has(w, "GameCatalog"));
        Assert.Equal(GameImageStatus.Complete, w.Game!.Find("logo.png")!.Status);
        w.Close();
    }

    // ================================================================== template_ graphics (fallbacks and their replacements)

    [Fact]
    public void Only_template_names_have_a_replacement_and_a_replaced_template_is_not_counted_twice()
    {
        Png("characters/template_hero.png", 8, 8, Pixel.Opaque(ColorRgb.Black));
        Png("template_logo.png", 8, 8, Pixel.Opaque(ColorRgb.Black));    // logo.png exists: replaced
        Png("ui/my_template_button.png", 8, 8, Pixel.Opaque(ColorRgb.Black));
        Png("ui/Template_icon.png", 8, 8, Pixel.Opaque(ColorRgb.Black));
        Png("ui/template_.png", 8, 8, Pixel.Opaque(ColorRgb.Black));
        var p = GameProject.Open(_assets);
        var templateHero = p.Find("characters/template_hero.png")!;
        Assert.Equal("characters/hero.png", templateHero.ReplacementPath);
        Assert.Equal("logo.png", p.Find("template_logo.png")!.ReplacementPath);
        foreach (var rel in new[] { "characters/player.png", "ui/my_template_button.png", "ui/Template_icon.png", "ui/template_.png" })
            Assert.Null(p.ReplacementFor(p.Find(rel)!));
        Assert.True(p.IsReplaced(p.Find("template_logo.png")!));
        Assert.False(p.IsReplaced(templateHero));
        Assert.Equal(9, p.Images.Count);
        Assert.Equal(8, p.Progress.Total);

        // The replacement before and after it exists is one and the same image.
        var hero = p.ReplacementFor(templateHero)!;
        Assert.Null(p.Find("characters/hero.png"));
        Png("characters/hero.png", 8, 8, Pixel.Opaque(ColorRgb.White));
        p.Rescan();
        Assert.Same(hero, p.Find("characters/hero.png"));
        Assert.True(p.IsReplaced(templateHero));
        Assert.Equal(8, p.Progress.Total);
    }

    [AvaloniaFact]
    public async Task Saving_a_template_can_create_its_replacement_and_later_saves_go_there()
    {
        Png("characters/template_hero.png", 16, 12, Pixel.Opaque(new ColorRgb(90, 90, 200)));
        var w = await GameWindow();
        var game = w.Game!;
        var template = game.Find("characters/template_hero.png")!;
        var templateBytes = File.ReadAllBytes(template.PngPath);
        Assert.True(await w.OpenGameImageAsync(template));
        Paint(w, 1, 6, 14);

        // Cancel: nothing is saved.
        var saving = w.SaveAsync();
        await Until(() => w.Dialogs.IsOpen);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Create a replacement without the 'template_' prefix?");
        w.Dialogs.Close("cancel");
        Assert.False(await saving);
        Assert.False(File.Exists(template.DnkPath));
        Assert.False(File.Exists(Path.Combine(_assets, "characters", "hero.png")));
        Assert.True(w.Session.Document.IsDirty);

        // Yes: hero.dnk and hero.png; the template stays exactly as it was.
        saving = w.SaveAsync();
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("yes");
        Assert.True(await saving);
        var hero = game.ReplacementFor(template)!;
        Assert.Equal("characters/hero.png", hero.RelativePath);
        Assert.Equal(templateBytes, File.ReadAllBytes(template.PngPath));
        Assert.False(File.Exists(template.DnkPath));
        Assert.False(File.Exists(template.BackupPath));
        Assert.True(File.Exists(hero.DnkPath));
        Assert.Equal(w.Session.Document.Flatten().Pixels, PngCodec.Decode(File.ReadAllBytes(hero.PngPath)).Pixels);
        Assert.Same(hero, w.GameImage);
        Assert.True(w.EditingGameImage);
        Assert.Equal($"DoodleNook — {MainWindow.Milestone} — hero.dnk", w.Title);
        Assert.Equal(GameImageStatus.InProgress, hero.Status);
        Assert.Equal(GameImageStatus.NotStarted, template.Status);

        // Later saves go to hero.png without asking again.
        Paint(w, 1, 2, 14);
        await Save(w);
        Assert.False(w.Dialogs.IsOpen);
        Assert.Equal(w.Session.Document.Flatten().Pixels, PngCodec.Decode(File.ReadAllBytes(hero.PngPath)).Pixels);
        Assert.Equal(templateBytes, File.ReadAllBytes(template.PngPath));

        // The catalog: hero.png is an ordinary image; the template says it's replaced and isn't counted.
        w.ShowCatalog();
        Pump();
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "assets — 6 images");
        Assert.Equal("Not started: 4   ·   In progress: 1   ·   Complete: 0   ·   Replaced templates: 1", Find<TextBlock>(w, "GameCounts").Text);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Replaced by hero.png");
        var heroTile = w.GetVisualDescendants().OfType<Border>().First(b => b.Name == "GameTile_characters/hero.png");
        Click(w, heroTile.GetVisualDescendants().OfType<Button>().First());
        Assert.Equal(GameImageStatus.Complete, hero.Status);
        Assert.Equal("Completion 20%", Find<TextBlock>(w, "GameProgressText").Text);
        w.HideCatalog();                     // marked complete in the catalog, then saved: still complete
        Paint(w, 1, 10, 14);
        await Save(w);
        Assert.Equal(GameImageStatus.Complete, hero.Status);
        var again = GameProject.Open(_assets);
        Assert.Equal(GameImageStatus.Complete, again.Find("characters/hero.png")!.Status);
        Assert.Equal(GameImageStatus.NotStarted, again.Find("characters/template_hero.png")!.Status);
        w.Close();
    }

    [AvaloniaFact]
    public async Task An_existing_replacement_is_only_overwritten_after_asking_and_no_saves_the_template_as_usual()
    {
        Png("characters/template_enemy.png", 16, 16, Pixel.Opaque(new ColorRgb(1, 100, 1)));
        var w = await GameWindow();
        var game = w.Game!;
        var template = game.Find("characters/template_enemy.png")!;
        var enemy = game.Find("characters/enemy.png")!;
        var templateBytes = File.ReadAllBytes(template.PngPath);
        var enemyBytes = File.ReadAllBytes(enemy.PngPath);
        Assert.True(await w.OpenGameImageAsync(template));
        Paint(w, 1, 8, 15);

        // Yes, but enemy.png exists: asked first, and Cancel leaves everything as it was.
        var saving = w.SaveAsync();
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("yes");
        await Until(() => w.Dialogs.IsOpen || saving.IsCompleted);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "“enemy.png” already exists");
        w.Dialogs.Close("cancel");
        Assert.False(await saving);
        Assert.Equal(enemyBytes, File.ReadAllBytes(enemy.PngPath));
        Assert.False(File.Exists(enemy.DnkPath));
        Assert.Same(template, w.GameImage);

        // Replace: the old enemy.png is backed up by the usual rule, then replaced; the template is untouched.
        saving = w.SaveAsync();
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("yes");
        await Until(() => w.Dialogs.IsOpen || saving.IsCompleted);
        w.Dialogs.Close("replace");
        Assert.True(await saving);
        Assert.Equal(enemyBytes, File.ReadAllBytes(enemy.BackupPath));
        Assert.NotEqual(enemyBytes, File.ReadAllBytes(enemy.PngPath));
        Assert.Equal(templateBytes, File.ReadAllBytes(template.PngPath));
        Assert.Same(enemy, w.GameImage);
        Assert.Equal(GameImageStatus.InProgress, enemy.Status);

        // No — Save normally: the template itself is saved as usual (backed up first), and asked only once.
        Png("ui/template_button.png", 12, 8, Pixel.Opaque(new ColorRgb(200, 150, 60)));
        w.ShowCatalog();
        var button = game.Find("ui/template_button.png")!;
        var buttonBytes = File.ReadAllBytes(button.PngPath);
        Assert.True(await w.OpenGameImageAsync(button));
        Paint(w, 1, 4, 11);
        saving = w.SaveAsync();
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("no");
        Assert.True(await saving);
        Assert.Equal(buttonBytes, File.ReadAllBytes(button.BackupPath));
        Assert.NotEqual(buttonBytes, File.ReadAllBytes(button.PngPath));
        Assert.True(File.Exists(button.DnkPath));
        Assert.False(File.Exists(Path.Combine(_assets, "ui", "button.png")));
        Assert.Equal(GameImageStatus.InProgress, button.Status);
        Paint(w, 1, 2, 11);
        await Save(w);
        Assert.False(w.Dialogs.IsOpen);
        w.Close();
    }

    // ================================================================== the project palette

    private static readonly ColorRgb Red = new(200, 40, 40), Green = new(40, 160, 60);

    /// <summary>Player's red, enemy's green, white and black: everything but the tree's blue.</summary>
    private static Palette GamePalette() => new("Game", [Red, Green, ColorRgb.White, ColorRgb.Black]);

    private static Palette Forest() => new("Forest", [Green, ColorRgb.Black, ColorRgb.White]);

    [Fact]
    public void The_project_palette_and_each_images_choice_are_saved_and_progress_stays_independent()
    {
        var p = GameProject.Open(_assets);
        p.SetBackupOriginals(false);
        p.SetProjectPalette(GamePalette(), lockAll: true);
        p.SetPaletteChoice(p.Find("environment/trees/tree.png")!, GamePaletteChoice.Custom, Forest());
        p.SetPaletteChoice(p.Find("logo.png")!, GamePaletteChoice.None);
        p.RecordSaved(p.Find("characters/player.png")!);

        var again = GameProject.Open(_assets); // like starting DoodleNook again
        Assert.Equal("Game", again.ProjectPalette!.Name);
        Assert.Equal(GamePalette().Colors, again.ProjectPalette.Colors);
        Assert.True(again.LockAllToPalette);
        var player = again.Find("characters/player.png")!;
        var tree = again.Find("environment/trees/tree.png")!;
        var logo = again.Find("logo.png")!;
        Assert.Equal(GamePaletteChoice.Project, player.PaletteChoice);
        Assert.Equal(GamePaletteChoice.Custom, tree.PaletteChoice);
        Assert.Equal(GamePaletteChoice.None, logo.PaletteChoice);
        Assert.Equal(GamePalette().Colors, again.LockPaletteFor(player)!.Colors);
        Assert.Equal(Forest().Colors, again.LockPaletteFor(tree)!.Colors);
        Assert.Null(again.LockPaletteFor(logo));
        Assert.Equal(GameImageStatus.InProgress, player.Status);   // palette choices don't touch progress
        Assert.Equal(GameImageStatus.NotStarted, tree.Status);
        Assert.Equal(GameImageStatus.NotStarted, logo.Status);

        // Not locking all graphics any more: project images have no rule; the custom palette stays.
        again.SetProjectPalette(again.ProjectPalette, lockAll: false);
        Assert.Null(GameProject.Open(_assets).LockPaletteFor(GameProject.Open(_assets).Find("characters/player.png")!));
        Assert.Equal(Forest().Colors, GameProject.Open(_assets).Find("environment/trees/tree.png")!.CustomPalette!.Colors);
    }

    [Fact]
    public void A_bundled_Lospec_palette_works_as_the_project_palette_and_as_an_images_own_palette()
    {
        var pico = Engine.Library.CuratedPalettes.All.Single(x => x.Name == "PICO-8 palette");
        var cave = Engine.Library.CuratedPalettes.All.Single(x => x.Name == "Cave4");
        var p = GameProject.Open(_assets);
        p.SetBackupOriginals(false);
        p.SetProjectPalette(new Palette(pico.Name, pico.Colors) { LibraryId = pico.Id }, lockAll: true);
        p.SetPaletteChoice(p.Find("environment/trees/tree.png")!, GamePaletteChoice.Custom, new Palette(cave.Name, cave.Colors) { LibraryId = cave.Id });

        var again = GameProject.Open(_assets);
        Assert.Equal(pico.Colors, again.LockPaletteFor(again.Find("characters/player.png")!)!.Colors);
        Assert.Equal(cave.Colors, again.LockPaletteFor(again.Find("environment/trees/tree.png")!)!.Colors);
        Assert.Equal("PICO-8 palette", again.ProjectPalette!.Name);
    }

    [AvaloniaFact]
    public async Task Opening_locks_to_the_project_palette_and_other_colours_get_the_remap_preview_before_anything_changes()
    {
        var w = await GameWindow(backups: false);
        var game = w.Game!;
        game.SetProjectPalette(GamePalette(), lockAll: true);

        // The player's colours fit: locked at once, with nothing to save or undo.
        Assert.True(await w.OpenGameImageAsync(game.Find("characters/player.png")!));
        Assert.True(w.Session.Document.PaletteLocked);
        Assert.Equal(GamePalette().Colors, w.Session.Document.Palette.Colors);
        Assert.False(w.Session.Document.IsDirty);
        Assert.Equal(0, w.Session.Document.History.UndoCount);

        // The tree has another colour: the remap preview comes first, and Cancel changes nothing.
        var tree = game.Find("environment/trees/tree.png")!;
        var treeBytes = File.ReadAllBytes(tree.PngPath);
        var opening = w.OpenGameImageAsync(tree);
        await Until(() => Has(w, "PaletteRemapDialog"));
        w.Dialogs.Close("cancel");
        Assert.True(await opening);
        var doc = w.Session.Document;
        Assert.False(doc.PaletteLocked);
        Assert.False(doc.IsDirty);
        var before = (uint[])doc.ActiveLayer.Image.Pixels.Clone();

        // Ctrl+S never saves it as if it kept to the palette: Cancel saves nothing.
        var saving = w.SaveAsync();
        await Until(() => w.Dialogs.IsOpen);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "“tree.png” has colours outside “Game”");
        w.Dialogs.Close("cancel");
        Assert.False(await saving);
        Assert.False(File.Exists(tree.DnkPath));
        Assert.Equal(treeBytes, File.ReadAllBytes(tree.PngPath));

        // Remap…: the preview, Apply, then the usual save of the .dnk (layers kept) and the PNG; alpha is untouched.
        w.Session.AddLayer();
        saving = w.SaveAsync();
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("remap");
        await Until(() => Has(w, "PaletteRemapDialog"));
        w.Dialogs.Close("apply");
        Assert.True(await saving);
        Assert.True(doc.PaletteLocked);
        var png = PngCodec.Decode(File.ReadAllBytes(tree.PngPath));
        var conformer = new PaletteConformer(GamePalette().Colors);
        Assert.All(png.Pixels, px => Assert.True(conformer.IsPaletteColour(px)));
        Assert.Equal(before.Select(Pixel.A), png.Pixels.Select(Pixel.A));
        var dnk = DnkFile.Load(tree.DnkPath);
        Assert.Equal(2, dnk.Layers.Count);
        Assert.True(dnk.PaletteLocked);
        Assert.Equal(GameImageStatus.InProgress, tree.Status);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Exceptions_keep_their_own_palette_or_no_lock_are_marked_in_the_catalog_and_a_new_project_palette_recolours_nothing()
    {
        var w = await GameWindow(backups: false);
        var game = w.Game!;
        game.SetProjectPalette(GamePalette(), lockAll: true);
        var player = game.Find("characters/player.png")!;
        var enemy = game.Find("characters/enemy.png")!;
        var tree = game.Find("environment/trees/tree.png")!;
        var logo = game.Find("logo.png")!;

        // The logo is saved while following the project palette, so its .dnk is locked…
        Assert.True(await w.OpenGameImageAsync(logo));
        Paint(w, 1, 4, 7);
        await Save(w);
        Assert.True(DnkFile.Load(logo.DnkPath).PaletteLocked);
        // …then made an exception: the open picture is unlocked at once, and stays unlocked when opened again.
        await w.SetGamePaletteChoiceAsync(logo, GamePaletteChoice.None);
        Assert.False(w.Session.Document.PaletteLocked);
        Assert.True(w.Session.Document.IsDirty); // unlocking is an ordinary undo step
        await w.SetGamePaletteChoiceAsync(enemy, GamePaletteChoice.Custom, Forest());
        var opening = w.OpenGameImageAsync(enemy);
        await Until(() => w.Dialogs.IsOpen);      // the usual unsaved-changes question
        w.Dialogs.Close("discard");                // keep the locked .dnk on disk
        Assert.True(await opening);
        Assert.True(w.Session.Document.PaletteLocked);
        Assert.Equal(Forest().Colors, w.Session.Document.Palette.Colors); // the usual document lock, to its own palette
        Assert.True(await w.OpenGameImageAsync(logo));
        Assert.False(w.Session.Document.PaletteLocked);
        Assert.False(w.Session.Document.IsDirty);

        // The catalog: exceptions are marked, and so is the tree, whose PNG has a colour outside the palette.
        w.ShowCatalog();
        await Until(() => Has(w, "GameOffPalette_environment/trees/tree.png"));
        Assert.False(Has(w, "GameOffPalette_characters/player.png"));
        DnIcon Icon(GameImage i) => (DnIcon)Find<Button>(w, "GamePalette_" + i.RelativePath).Content!;
        Assert.Equal("lock", Icon(player).Kind);
        Assert.Equal("palette", Icon(enemy).Kind);
        Assert.Equal("unlock", Icon(logo).Kind);
        Assert.Contains("“Forest” (3 colours)", (string)ToolTip.GetTip(Find<Button>(w, "GamePalette_characters/enemy.png"))!);
        Assert.Contains("no palette lock", (string)ToolTip.GetTip(Find<Button>(w, "GamePalette_logo.png"))!);
        var tiles = w.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is GameImage).ToList();
        w.CaptureRenderedFrame()?.Dispose(); // the filter button just appeared: draw it before clicking it
        Click(w, Find<Button>(w, "GamePaletteExceptions"));
        Assert.Equal(["characters/enemy.png", "environment/trees/tree.png", "logo.png"],
            tiles.Where(t => t.IsVisible).Select(t => ((GameImage)t.Tag!).RelativePath).Order());
        Click(w, Find<Button>(w, "GamePaletteExceptions"));
        Assert.Equal(4, tiles.Count(t => t.IsVisible));

        // A new project palette (through Project palette…) changes no file and keeps every exception.
        var files = Directory.GetFiles(_assets, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(GameProject.CatalogFileName, StringComparison.Ordinal))
            .ToDictionary(f => f, File.ReadAllBytes);
        var dialog = w.ShowProjectPaletteAsync();
        await Until(() => Has(w, "ProjectPaletteLock"));
        Pump();
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Game (4 colours)");
        w.Dialogs.Close("save");
        Assert.True(await dialog);
        game.SetProjectPalette(new Palette("Night", [ColorRgb.Black, new ColorRgb(30, 30, 90)]), lockAll: true);
        Assert.All(files, f => Assert.Equal(f.Value, File.ReadAllBytes(f.Key)));
        var again = GameProject.Open(_assets);
        Assert.Equal(GamePaletteChoice.Custom, again.Find("characters/enemy.png")!.PaletteChoice);
        Assert.Equal(Forest().Colors, again.Find("characters/enemy.png")!.CustomPalette!.Colors);
        Assert.Equal(GamePaletteChoice.None, again.Find("logo.png")!.PaletteChoice);
        Assert.Equal(GameImageStatus.InProgress, again.Find("logo.png")!.Status);
        w.Close();
    }

    [AvaloniaFact]
    public void The_window_title_shows_the_milestone()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        Assert.Equal("M012", MainWindow.Milestone);
        Assert.Equal("DoodleNook — M012", w.Title);
        w.Session.AddLayer();
        Pump();
        Assert.StartsWith("DoodleNook — M012 — Untitled", w.Title);
        w.Close();
    }
}

/// <summary>Screenshots of Game Edit Mode (out/screenshots/game-*.png).</summary>
public class GameEditScreenshots
{
    [AvaloniaFact]
    public async Task Capture_game_edit_screenshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "dn-game-shots-" + Guid.NewGuid().ToString("N")[..8], "assets");
        void Save(string relative, RasterImage image)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, PngCodec.Encode(image));
        }
        // A small game's graphics: the sample animations' frames as sprites, plus a few tiles and a title.
        var slime = Engine.Samples.SampleAnimations.PixelSlime();
        for (int f = 0; f < 6; f++) Save($"characters/slime/slime_{f}.png", slime.RenderFrame(f, includePaper: false));
        var ball = Engine.Samples.SampleAnimations.BouncingBall();
        for (int f = 0; f < 4; f++) Save($"effects/ball_{f}.png", Engine.Operations.Resampler.Resize(ball.RenderFrame(f * 3), 320, 180, Engine.Operations.Interpolation.Bilinear));
        string[] tiles = ["#7FA36B", "#6A9159", "#C9A26B", "#8E8E8E", "#5F8FA8"];
        for (int i = 0; i < tiles.Length; i++)
        {
            var t = new Document(32, 32, null, "Tile");
            t.AddShapeLayer(new ShapeContent(i % 2 == 0 ? ShapeType.RoundedRectangle : ShapeType.Ellipse, 2, 2, 28, 28, ColorRgb.Parse(tiles[i]), ColorRgb.Parse("#2B2230"), 2, 0, 6));
            Save($"environment/tiles/tile_{i}.png", t.Flatten());
        }
        var title = new Document(480, 160, null, "Title");
        title.AddTextLayer(new TextContent("Slime Quest", "Varela Round", 72, ColorRgb.Parse("#5B3A6B"), X: 20, Y: 30, Outline: new OutlineEffect(ColorRgb.Parse("#F6E7C8"), 5)));
        Save("ui/title.png", title.Flatten());
        var button = new Document(200, 64, null, "Button");
        button.AddShapeLayer(new ShapeContent(ShapeType.RoundedRectangle, 4, 4, 192, 56, ColorRgb.Parse("#E9A94D"), ColorRgb.Parse("#2B2230"), 4, 0, 20));
        Save("ui/button_play.png", button.Flatten());

        var project = GameProject.Open(root);
        project.SetBackupOriginals(true);
        foreach (var rel in new[] { "characters/slime/slime_0.png", "characters/slime/slime_1.png", "ui/title.png", "environment/tiles/tile_0.png" })
            project.MarkComplete(project.Find(rel)!);
        foreach (var rel in new[] { "characters/slime/slime_2.png", "ui/button_play.png", "effects/ball_0.png" })
            project.RecordSaved(project.Find(rel)!);

        foreach (var (name, width, height, scaling) in new[] { ("kamvas13-150", 1280.0, 720.0, 1.5), ("desktop1080", 1920.0, 1080.0, 1.0) })
        {
            var w = WindowTests.Open(width, height, WorkspaceKind.Painting);
            w.SetRenderScaling(scaling);
            WindowTests.Pump();
            Assert.True(await w.OpenGameFolderAsync(root));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (w.GetVisualDescendants().OfType<Image>().Count(i => i.Source is not null && i.FindAncestorOfType<GameCatalogView>() is not null) < 10
                   && DateTime.UtcNow < deadline)
            {
                WindowTests.Pump();
                await Task.Delay(20);
            }
            WindowTests.Pump();
            WorkshopScreenshots.Save(w, $"game-catalog-{name}");
            Assert.True(await w.OpenGameImageAsync(w.Game!.Find("ui/button_play.png")!));
            w.Canvas.FitToView();
            WindowTests.Pump();
            WorkshopScreenshots.Save(w, $"game-editing-{name}");
            w.Close();
        }
        Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
    }
}
