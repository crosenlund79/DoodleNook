using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Storybook;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 017 — the defects the second pass found, each checked in the real window.</summary>
public sealed class Mission017FixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "doodlenook-m017-" + Guid.NewGuid().ToString("N"));

    public Mission017FixTests() => Directory.CreateDirectory(_dir);

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

    private static Button Anchor(MainWindow w, int row, int col) => Find<Button>(w, $"CanvasAnchor{row}{col}");

    /// <summary>The chosen anchor cell was styled "on", but a background set in code hid it: you couldn't see which
    /// anchor was chosen.</summary>
    [AvaloniaFact]
    public async Task Canvas_size_shows_which_anchor_is_chosen()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var ask = w.Dialogs.AskCanvasSizeAsync(640, 400);
        Pump();
        Assert.Equal("●", Anchor(w, 1, 1).Content);
        Assert.Contains("on", Anchor(w, 1, 1).Classes);
        // The "on" style shows: the chosen cell's background isn't the plain cells' one.
        Assert.NotEqual((Anchor(w, 0, 0).Background as Avalonia.Media.ISolidColorBrush)?.Color, (Anchor(w, 1, 1).Background as Avalonia.Media.ISolidColorBrush)?.Color);
        Assert.Equal("·", Anchor(w, 0, 0).Content);
        Click(w, Anchor(w, 0, 0));
        Assert.Equal("●", Anchor(w, 0, 0).Content);
        Assert.Equal("·", Anchor(w, 1, 1).Content);
        w.Dialogs.Close("ok");
        var result = await ask;
        Assert.Equal((0.0, 0.0), (result!.Value.AnchorX, result.Value.AnchorY));
        w.Close();
    }

    /// <summary>Quick Sketch's top bar is its drawing bar (brush, undo, ☰ with Clear layer): it stayed over a storybook,
    /// where it changed the hidden drawing. Pure Sketch's corner menu did the same.</summary>
    [AvaloniaFact]
    public async Task Quick_and_Pure_Sketch_leave_the_drawing_alone_while_a_storybook_is_shown()
    {
        var w = Open(1440, 900, WorkspaceKind.QuickSketch);
        Assert.True(Has(w, "QuickBar"));
        Assert.True(await w.CreateStorybookAsync(Path.Combine(_dir, "Quick.donkbook"), 210, 297, 150, StoryLayouts.Comic));
        Pump();
        Assert.True(w.StorybookShown);
        Assert.False(Has(w, "QuickBar"));
        Key(w, Avalonia.Input.Key.Escape);
        Assert.False(w.StorybookShown);
        Assert.True(Has(w, "QuickBar"));

        // ☰'s commands (if reached) and Pure Sketch's corner menu go back to the drawing first.
        w.ShowStorybook();
        Pump();
        int layers = w.Session.Document.Layers.Count;
        w.QuickMenu().First(e => e.Label == "Clear layer").Run!();
        Pump();
        Assert.False(w.StorybookShown);
        Assert.Equal(layers, w.Session.Document.Layers.Count);

        w.SetWorkspace(WorkspaceKind.PureSketch);
        w.ShowStorybook();
        Pump();
        Assert.True(w.StorybookShown);
        var fresh = w.PureMenu().First(e => e.Label == "New picture…");
        fresh.Run!();
        Pump();
        Assert.False(w.StorybookShown);
        if (w.Dialogs.IsOpen) w.Dialogs.Close(null);
        w.Session.Document.MarkSaved();
        w.Close();
    }

    private static string TopBarDocument(MainWindow w)
    {
        Pump();
        return w.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text?.Contains(" × ") == true).Text!;
    }

    /// <summary>The top bar's "· edited" only changed with the tool or colour: after a stroke it still looked saved (found
    /// in the real app while testing a failed save).</summary>
    [AvaloniaFact]
    public async Task The_top_bar_says_edited_as_soon_as_a_stroke_ends_and_after_undo_and_save()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var path = Path.Combine(_dir, "marker.dnk");
        DoodleNook.Engine.Files.DnkFile.Save(new DoodleNook.Engine.Documents.Document(160, 100, DoodleNook.Engine.Imaging.ColorRgb.White, "Background"), path);
        Assert.True(await w.OpenPathAsync(path));
        w.Canvas.FitToView();
        Pump();
        Assert.False(w.Session.Document.IsDirty);
        Assert.DoesNotContain("· edited", TopBarDocument(w));
        w.Session.SelectTool(DoodleNook.App.Editor.ToolKind.Brush);
        Drag(w, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None, (20, 20), (120, 60));
        Assert.True(w.Session.Document.IsDirty);
        Assert.Contains("· edited", TopBarDocument(w));
        w.Session.Undo();
        Assert.DoesNotContain("· edited", TopBarDocument(w));
        w.Session.Redo();
        Assert.Contains("· edited", TopBarDocument(w));
        Assert.True(await w.SaveToPathAsync(path));
        Assert.DoesNotContain("· edited", TopBarDocument(w));
        w.Close();
    }

    /// <summary>A save that fails (a folder that can't be written; found with a write-protected folder in the real app)
    /// says the file wasn't touched and what to do, and the picture stays unsaved.</summary>
    [AvaloniaFact]
    public async Task A_failed_save_says_the_file_is_untouched_and_keeps_the_picture_unsaved()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        w.Canvas.FitToView();
        Pump();
        w.Session.SelectTool(DoodleNook.App.Editor.ToolKind.Brush);
        Drag(w, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None, (20, 20), (120, 60));
        var save = w.SaveToPathAsync(Path.Combine(_dir, "no such folder", "picture.dnk"));
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
        Pump();
        var text = string.Join(" ", w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        Assert.Contains("Couldn't save", text);
        Assert.Contains("The file on disk was not changed", text);
        Assert.Contains("Save As", text);
        w.Dialogs.Close("ok");
        Assert.False(await save);
        Assert.True(w.Session.Document.IsDirty);
        Assert.Contains("· edited", TopBarDocument(w));
        w.Session.Document.MarkSaved();
        w.Close();
    }

    /// <summary>After a save, Help › Diagnostics lists "Save: compress and write (background)": at 150 % that name ran into
    /// its value (found by the menu crawl when earlier tests had saved).</summary>
    [AvaloniaFact]
    public async Task Diagnostics_timing_names_never_run_into_their_values()
    {
        var w = Open(1280, 720, WorkspaceKind.Painting);
        w.SetRenderScaling(1.5);
        Assert.True(await w.SaveToPathAsync(Path.Combine(_dir, "timed.dnk"))); // puts the long names in Recent timings
        var show = w.ShowPerformanceDiagnosticsAsync();
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
        Pump();
        var name = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text == "Save: compress and write (background)");
        var grid = (Grid)name.Parent!;
        var value = grid.Children.OfType<TextBlock>().First(t => Grid.GetRow(t) == Grid.GetRow(name) && Grid.GetColumn(t) == 1);
        // The name's laid-out text fits its column (it wraps), and it ends before its value begins.
        Assert.True(name.TextLayout.Width <= name.Bounds.Width + 0.5, $"name text {name.TextLayout.Width:0.0} wide in {name.Bounds.Width:0.0}");
        Assert.True(name.Bounds.Right <= value.Bounds.X + 0.5, $"name ends at {name.Bounds.Right:0.0}, value starts at {value.Bounds.X:0.0}");
        w.Dialogs.Close(null);
        await show;
        w.Close();
    }

    /// <summary>Brush Laboratory: dragging a slider changed the brush, but the number beside it stayed where it was
    /// (found in the real program by the workflow audit).</summary>
    [AvaloniaFact]
    public void Brush_Laboratory_numbers_follow_their_sliders()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        _ = w.OpenBrushLabAsync("charcoal");
        Pump();
        var lab = w.LastBrushLab!;
        // The first slider row of the open section (a label, then the slider and its number).
        (TextBlock Label, DoodleNook.App.Controls.DnSlider Slider, TextBlock Number) Row()
        {
            var slider = w.GetVisualDescendants().OfType<DoodleNook.App.Controls.DnSlider>().First(x => x.IsEffectivelyVisible
                && x.Parent is StackPanel { Parent: StackPanel outer } && outer.Children.Count == 2 && outer.Children[0] is TextBlock);
            var inner = (StackPanel)slider.Parent!;
            var label = ((StackPanel)inner.Parent!).Children.OfType<TextBlock>().First();
            return (label, slider, inner.Children.OfType<TextBlock>().Last());
        }
        var (name, s, number) = Row();
        string before = number.Text!;
        // Drag the knob from near the right end to about a fifth of the track, as a hand would.
        var y = s.Bounds.Height / 2;
        var from = s.TranslatePoint(new Point(s.Bounds.Width * 0.9, y), w)!.Value;
        var to = s.TranslatePoint(new Point(s.Bounds.Width * 0.2, y), w)!.Value;
        w.MouseMove(from);
        w.MouseDown(from, Avalonia.Input.MouseButton.Left);
        w.MouseMove(to, Avalonia.Input.RawInputModifiers.LeftMouseButton);
        w.MouseUp(to, Avalonia.Input.MouseButton.Left);
        Pump();
        string shown = number.Text!;
        // What a freshly built section shows for the brush as it now is (the Lab rebuilds it when you change tabs).
        lab.BuildSection();
        Pump();
        var again = Row();
        Assert.Equal(name.Text, again.Label.Text);
        Assert.NotEqual(before, again.Number.Text); // the brush did change
        Assert.Equal(again.Number.Text, shown);
        w.Dialogs.Close(null);
        w.Close();
    }

    /// <summary>Text: Ctrl+Enter (as the editor's hint says) added a new line instead of finishing.</summary>
    [AvaloniaFact]
    public void Ctrl_Enter_finishes_typing_text()
    {
        var w = Open(1280, 800, WorkspaceKind.Editing);
        w.Canvas.FitToView();
        Pump();
        w.Session.SelectTool(DoodleNook.App.Editor.ToolKind.Text);
        Drag(w, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None, (200, 200)); // a click on the canvas
        Assert.True(w.TextEditor.IsOpen);
        var box = Find<TextBox>(w, "TextEditorBox");
        box.Focus();
        box.Text = "Hello nook";
        box.CaretIndex = box.Text.Length;
        Pump();
        Key(w, Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Control);
        Assert.False(w.TextEditor.IsOpen);
        var layer = w.Session.Document.ActiveLayer;
        Assert.Equal("Hello nook", ((DoodleNook.Engine.Documents.TextContent)layer.Content!).Text);
        w.Session.Document.MarkSaved();
        w.Close();
    }

    /// <summary>Renaming the brush in use (Brush Laboratory › Save) left its old name in the options bar until another
    /// tool was picked.</summary>
    [AvaloniaFact]
    public void The_options_bar_shows_a_renamed_brush_by_its_new_name()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var lib = w.Session.Library;
        var preset = lib.AddBrush("Renamed brush", new DoodleNook.Engine.Painting.BrushSettings { Size = 14 });
        w.Session.UseBrush(preset.Settings, preset.Id);
        Pump();
        string Chip() => string.Join(" ", Find<Button>(w, "OptionsPresetChip").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains("Renamed brush", Chip());
        Assert.True(lib.RenameBrush(preset.Id, "Third brush"));
        Pump();
        Assert.Contains("Third brush", Chip());
        lib.DeleteBrush(preset.Id);
        w.Close();
    }

    /// <summary>[ and ] with the stamp tool resize the stamp, but the message gave the brush's size.</summary>
    [AvaloniaFact]
    public void The_stamp_tools_size_keys_report_the_stamps_size()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        w.Session.SelectTool(DoodleNook.App.Editor.ToolKind.Stamp);
        double before = w.Session.StampOptions.Scale;
        Key(w, Avalonia.Input.Key.OemCloseBrackets);
        Assert.True(w.Session.StampOptions.Scale > before);
        Assert.Equal($"Stamp size {w.Session.StampOptions.Scale * 100:0}%", w.LastToast);
        Key(w, Avalonia.Input.Key.OemOpenBrackets);
        Assert.Equal($"Stamp size {w.Session.StampOptions.Scale * 100:0}%", w.LastToast);
        // The brush still reports its own size.
        w.Session.SelectTool(DoodleNook.App.Editor.ToolKind.Brush);
        Key(w, Avalonia.Input.Key.OemCloseBrackets);
        Assert.Equal($"Size {w.Session.CurrentSize:0} px", w.LastToast);
        w.Close();
    }

    /// <summary>Ctrl+Z inside Liquify takes back the last Liquify stroke, but the message named the picture's previous
    /// step ("Undid brush stroke") or nothing ("Undid ").</summary>
    [AvaloniaFact]
    public async Task Undo_inside_Liquify_says_what_it_took_back()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        w.Canvas.FitToView();
        Pump();
        w.Session.SelectTool(DoodleNook.App.Editor.ToolKind.Brush);
        w.Session.UseBrush(new DoodleNook.Engine.Painting.BrushSettings { Size = 40, Hardness = 1, Opacity = 1 }, "m017");
        Drag(w, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None, (300, 300), (900, 400));
        int steps = w.Session.Document.History.UndoCount;
        Assert.True(await w.StartLiquifyAsync());
        Drag(w, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None, (500, 330), (560, 400), (620, 470));
        w.Undo();
        Assert.Equal("Took back the last Liquify stroke", w.LastToast);
        Assert.Equal(steps, w.Session.Document.History.UndoCount); // the brush stroke is still there
        Assert.NotNull(w.Session.ActiveLiquify);
        w.Session.Document.MarkSaved();
        w.Close();
    }

    private static Button PaletteMenuEntry(MainWindow w, string text)
    {
        Click(w, Find<Button>(w, "PixelPaletteMenu"));
        return w.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("menu") && b.IsEffectivelyVisible
            && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));
    }

    /// <summary>Pixel Art's palette menu changed the picture's palette (it is saved with the picture) without an undo
    /// step: not undoable, the picture didn't count as changed, and closing didn't ask. Its tick stayed on a saved
    /// palette after "Palette from the picture".</summary>
    [AvaloniaFact]
    public void Choosing_a_palette_in_Pixel_Art_is_an_undoable_change_and_its_tick_follows_the_picture()
    {
        var w = Open(1280, 800, WorkspaceKind.PixelArt);
        var doc = new DoodleNook.Engine.Documents.Document(64, 48, DoodleNook.Engine.Imaging.ColorRgb.White, "Background");
        w.Session.ReplaceDocument(doc, null);
        doc.MarkSaved();
        var lib = w.Session.Library;
        var saved = lib.AddPalette("M017 test palette", "My palettes", [new(10, 20, 30), new(200, 100, 50), new(250, 240, 230)]);
        Pump();
        string original = doc.Palette.Name;

        Click(w, PaletteMenuEntry(w, "M017 test palette"));
        Assert.Equal("M017 test palette", doc.Palette.Name);
        Assert.True(doc.IsDirty);
        Assert.Contains("· edited", TopBarDocument(w));
        var entry = PaletteMenuEntry(w, "M017 test palette");
        Assert.Contains("on", entry.Classes); // ticked: the picture's palette
        Click(w, PaletteMenuEntry(w, "Palette from the picture"));
        Assert.NotEqual("M017 test palette", doc.Palette.Name);
        Assert.DoesNotContain("on", PaletteMenuEntry(w, "M017 test palette").Classes); // no longer ticked
        Key(w, Avalonia.Input.Key.Escape); // close the menu

        w.Session.Undo();
        Assert.Equal("M017 test palette", doc.Palette.Name);
        w.Session.Undo();
        Assert.Equal(original, doc.Palette.Name);
        Assert.False(doc.IsDirty);
        lib.DeletePalette(saved.Id);
        w.Close();
    }

    [AvaloniaFact]
    public async Task About_names_this_build_not_an_old_mission()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        w.MenuFor("Help").First(e => e.Label == "About DoodleNook").Run!();
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
        Pump();
        var text = string.Join(" ", w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
        Assert.Contains($"({MainWindow.Milestone})", text);
        Assert.DoesNotContain("Mission 014", text);
        Assert.Contains("Storybook Studio", text);
        w.Dialogs.Close(null);
        w.Close();
    }
}
