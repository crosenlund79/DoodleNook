using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using DoodleNook.Engine.Storybook;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 016 — document safety: nothing unsaved is lost without asking, at every moment where it could be: closing,
/// opening another picture, switching workspaces, a missing file, the storybook and its drawings, autosave.
/// </summary>
public sealed class DocumentSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-safety-" + Guid.NewGuid().ToString("N"));

    public DocumentSafetyTests() => Directory.CreateDirectory(_dir);

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

    private static Task Until(Func<bool> done, int ms = 15_000) => ArtistsCompanionWindowTests.Until(done, ms);

    private static void Scribble(MainWindow w)
    {
        w.Canvas.FitToView();
        Pump();
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.UseBrush(new BrushSettings { Size = 12, Hardness = 1, Opacity = 1 }, "safety");
        w.Session.SetForeground(new ColorRgb(200, 60, 40));
        Drag(w, MouseButton.Left, RawInputModifiers.None, (20, 20), (120, 60));
    }

    private static string Texts(Window w)
    {
        Pump(); // laid out first: a dialog or chip that just appeared
        return string.Join(" | ", w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
    }

    private string Picture(string name)
    {
        var path = Path.Combine(_dir, name + ".dnk");
        DnkFile.Save(new Document(160, 100, ColorRgb.White, "Background"), path);
        return path;
    }

    [AvaloniaFact]
    public async Task Closing_with_unsaved_changes_asks_and_Cancel_keeps_everything()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        Assert.True(await w.OpenPathAsync(Picture("close")));
        Scribble(w);
        Assert.True(w.Session.Document.IsDirty);
        var pixels = w.Session.Document.Flatten().Pixels.ToArray();
        bool closed = false;
        w.Closed += (_, _) => closed = true;

        w.Close();
        await Until(() => w.Dialogs.IsOpen);
        Assert.Contains("You have unsaved changes", Texts(w));
        w.Dialogs.Close("cancel");
        Pump();
        Assert.False(closed);
        Assert.Equal(pixels, w.Session.Document.Flatten().Pixels);
        Assert.True(w.Session.Document.IsDirty);

        // Save from the question: written, then closed.
        w.Close();
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("save");
        await Until(() => closed);
        Assert.Equal(pixels, DnkFile.Load(Path.Combine(_dir, "close.dnk")).Flatten().Pixels);
    }

    [AvaloniaFact]
    public async Task Opening_another_picture_asks_first_and_a_missing_file_changes_nothing()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var first = Picture("first");
        Assert.True(await w.OpenPathAsync(first));
        Scribble(w);
        var doc = w.Session.Document;

        // A recent file that has gone: a short message, the open picture and its changes untouched.
        var missing = w.OpenPathAsync(Path.Combine(_dir, "gone.dnk"));
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("ok");
        Assert.False(await missing);
        Assert.Same(doc, w.Session.Document);
        Assert.True(doc.IsDirty);

        // A new picture (File › New; Open, the storybook and Game Edit ask the same way): the question first, Cancel
        // keeps everything.
        var fresh = w.NewAsync();
        await Until(() => w.Dialogs.IsOpen);
        Assert.Contains("You have unsaved changes", Texts(w));
        w.Dialogs.Close("cancel");
        await fresh;
        Assert.False(w.Dialogs.IsOpen);
        Assert.Same(doc, w.Session.Document);
        Assert.True(doc.IsDirty);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Switching_workspaces_keeps_the_picture_its_changes_and_its_undo_history()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        Assert.True(await w.OpenPathAsync(Picture("ws")));
        Scribble(w);
        var doc = w.Session.Document;
        var pixels = doc.Flatten().Pixels.ToArray();
        int undo = doc.History.UndoCount;
        foreach (var kind in Enum.GetValues<WorkspaceKind>())
        {
            w.SetWorkspace(kind);
            Pump();
            Assert.False(w.Dialogs.IsOpen);
            Assert.Same(doc, w.Session.Document);
        }
        Assert.Equal(pixels, doc.Flatten().Pixels);
        Assert.Equal(undo, doc.History.UndoCount);
        Assert.True(doc.IsDirty);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_storybook_never_loses_a_panel_drawings_unsaved_changes()
    {
        var w = Open(1440, 900, WorkspaceKind.Painting);
        var recovery = new RecoveryStore(Path.Combine(_dir, "recovery"));
        w.StartRecovery(recovery); // as at the program's start
        var book = Path.Combine(_dir, "Safe.donkbook");
        Assert.True(await w.CreateStorybookAsync(book, 210, 297, 150, StoryLayouts.Comic));
        var a = Path.Combine(_dir, "a.dnk");
        var b = Path.Combine(_dir, "b.dnk");
        DnkFile.Save(new Document(400, 300, ColorRgb.White, "Background"), a);
        DnkFile.Save(new Document(400, 300, ColorRgb.White, "Background"), b);
        Assert.True(await w.LinkStorybookFileAsync(0, 0, a, resizeIfSmall: false));
        Assert.True(await w.LinkStorybookFileAsync(0, 1, b, resizeIfSmall: false));

        // Draw in panel 1's drawing, go back to the storybook without saving: the chip said so, nothing is lost.
        await w.OpenStorybookPanelAsync(0, 0);
        Pump();
        Assert.False(w.StorybookShown);
        Scribble(w);
        var drawn = w.Session.Document.Flatten().Pixels.ToArray();
        // The chip under the drawing says it isn't saved yet (Mission 016: it only did after other changes of state).
        Assert.Contains("not saved yet", Texts(w));
        w.ShowStorybook();
        Pump();
        Assert.True(w.Session.Document.IsDirty);

        // Autosave keeps a recovery copy while the storybook is shown.
        Assert.True(await w.AutosaveNowAsync());
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_dir, "recovery"), "*", SearchOption.AllDirectories));

        // Opening panel 2's drawing asks about panel 1's changes; Cancel stays in the storybook with them kept.
        var other = w.OpenStorybookPanelAsync(0, 1);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("cancel");
        await other;
        Assert.Equal(a, w.Session.FilePath);
        Assert.Equal(drawn, w.Session.Document.Flatten().Pixels);

        // Save from the question: panel 1's drawing is written, then panel 2's opens.
        other = w.OpenStorybookPanelAsync(0, 1);
        await Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("save");
        await other;
        Assert.Equal(b, w.Session.FilePath);
        Assert.Equal(drawn, DnkFile.Load(a).Flatten().Pixels);
        w.Close();
    }
}
