using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using static DoodleNook.App.Tests.WindowTests;
using K = Avalonia.Input.Key;

namespace DoodleNook.App.Tests;

/// <summary>Mission 007 through the real window: the Brush Laboratory, stamps, patterns, palettes, the reference board
/// and the six workflows of the brief (A–F), with a temporary library folder standing in for %APPDATA%.</summary>
public sealed class WorkshopWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-workshop-" + Guid.NewGuid().ToString("N")[..8]);

    public WorkshopWindowTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string LibDir => Path.Combine(_dir, "Library");

    /// <summary>A window with its own library folder (a restart = a new window on the same folder).</summary>
    private MainWindow Window(WorkspaceKind ws = WorkspaceKind.Painting, int width = 400, int height = 300, ColorRgb? paper = null)
    {
        var w = Open(1280, 800, ws);
        w.UseLibrary(new CreativeLibrary(LibDir));
        w.Session.ReplaceDocument(new Document(width, height, paper, "Sketch"), null);
        Pump();
        w.Canvas.FitToView();
        Pump();
        return w;
    }

    private static void Line(MainWindow w, double x0, double y0, double x1, double y1) =>
        Drag(w, MouseButton.Left, RawInputModifiers.None, (x0, y0), ((x0 + x1) / 2, (y0 + y1) / 2), (x1, y1));

    private static void ClickDoc(MainWindow w, double x, double y)
    {
        var p = DocToWindow(w, x, y);
        w.MouseMove(p);
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Pump();
    }

    private static byte A(Document d, int x, int y) => Pixel.A(d.ActiveLayer.Image[x, y]);

    // ================================================================== Brush Laboratory

    [AvaloniaFact]
    public void The_brush_lab_test_canvas_never_touches_the_artwork()
    {
        var w = Window();
        var doc = w.Session.Document;
        var before = doc.ActiveLayer.Image.Pixels.ToArray();
        var lab = w.OpenBrushLabAsync("charcoal");
        Pump();
        var bl = w.LastBrushLab!;
        bl.Playground.ExampleStroke();
        // Draw on the test canvas with the mouse too.
        var area = bl.Playground;
        var p0 = area.TranslatePoint(new Point(20, 40), w)!.Value;
        var p1 = area.TranslatePoint(new Point(300, 150), w)!.Value;
        w.MouseMove(p0);
        w.MouseDown(p0, MouseButton.Left);
        w.MouseMove(p1, RawInputModifiers.LeftMouseButton);
        w.MouseUp(p1, MouseButton.Left);
        Pump();
        Assert.True(bl.Playground.TestDocument.ActiveLayer.Image.Pixels.Count(px => Pixel.A(px) > 0) > 500);
        Assert.Equal(before, doc.ActiveLayer.Image.Pixels); // the picture is untouched
        Assert.False(doc.History.CanUndo);                 // and has no new undo steps
        w.Dialogs.Close("close");
        Pump();
        Assert.True(lab.IsCompleted);
        Assert.False(lab.Result);
        Assert.Equal("pencil", w.Session.BrushPresetId);  // Close keeps the brush you had
        w.Close();
    }

    [AvaloniaFact]
    public void Workflow_A_a_custom_pencil_is_saved_and_still_there_after_a_restart()
    {
        var w = Window();
        _ = w.OpenBrushLabAsync("pencil");
        Pump();
        var lab = w.LastBrushLab!;
        lab.Edit(b => b with { Grain = 0.95, GrainScale = 2, PressureGamma = 0.6, TipId = BuiltInAssets.Tips[0].Id, Size = 14 });
        lab.Playground.ExampleStroke();
        lab.SetName("My grainy pencil");
        Click(w, Find<Button>(w, "BrushLabSave")); // a built-in brush: saved as a new brush
        var saved = w.Session.Library.Brushes.Single(b => b.Name == "My grainy pencil");
        Assert.False(saved.BuiltIn);
        Assert.Equal(0.95, saved.Settings.Grain);
        Assert.Equal(EditorSession.Presets.First(p => p.Id == "pencil").Settings, w.Session.Library.FindBrush("pencil")!.Settings); // original unchanged
        w.Dialogs.Close("use");
        Pump();
        Assert.Equal(saved.Id, w.Session.BrushPresetId);
        w.Close();

        // "Restart": a new window on the same library folder.
        var again = Window();
        var b2 = again.Session.Library.Brushes.Single(b => b.Name == "My grainy pencil");
        again.Session.ApplyPreset(b2.Id);
        Assert.Equal(b2.Settings, again.Session.Brush);
        Line(again, 40, 150, 360, 150);
        Assert.True(again.Session.Document.ActiveLayer.Image.Pixels.Count(px => Pixel.A(px) > 0) > 300);
        Assert.True(again.Session.Document.History.CanUndo);
        again.Close();
    }

    [AvaloniaFact]
    public void Brush_picker_lists_favourites_first_and_finds_brushes_by_name()
    {
        var w = Window();
        w.Session.Library.SetFavoriteBrush("watercolor", true);
        Click(w, Find<Button>(w, "OptionsPresetChip"));
        Assert.True(Has(w, "BrushTile_watercolor") && Has(w, "OpenBrushLab"));
        var texts = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.True(texts.IndexOf("★ Favourites") < texts.IndexOf("Sketch"));
        int Visible(string name) => w.GetVisualDescendants().OfType<Control>().Count(c => c.Name == name && c.IsEffectivelyVisible);
        int inkBefore = Visible("BrushTile_ink"); // the picker, and the Painting dock's own brush list
        var search = w.GetVisualDescendants().OfType<TextBox>().Last(t => t.Name == "BrushSearch" && t.IsEffectivelyVisible);
        search.Text = "char";
        Pump();
        Assert.True(Has(w, "BrushTile_charcoal"));
        Assert.Equal(inkBefore - 1, Visible("BrushTile_ink")); // filtered out of the searched list
        Click(w, w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BrushTile_charcoal" && b.IsEffectivelyVisible && b.FindAncestorOfType<FlyoutPresenter>() is not null));
        Assert.Equal("charcoal", w.Session.BrushPresetId);
        UiKit.HideOpenFlyouts();
        Pump();

        // Favourite brushes pinned in the bar (Customize workspace).
        w.WsState.SetShown(CustomOptions.FavouriteBrushes, WorkspaceKind.Painting, true);
        w.ApplyCustomization();
        Pump();
        Click(w, Find<Button>(w, "Favourite_watercolor"));
        Assert.Equal("watercolor", w.Session.BrushPresetId);
        w.Close();
    }

    // ================================================================== stamps

    [AvaloniaFact]
    public void Stamp_tool_previews_places_resizes_and_undoes()
    {
        var w = Window();
        Key(w, K.S);
        Assert.Equal(ToolKind.Stamp, w.Session.Tool);
        Assert.True(Has(w, "StampChip"));
        w.Session.SetStampOptions(w.Session.StampOptions with { StampId = BuiltInAssets.Prefix + "pebble", Scale = 0.5 });
        w.MouseMove(DocToWindow(w, 200, 150));
        Pump();
        Assert.NotNull(w.Canvas.Input!.Overlay.Stamp); // the preview follows the pointer
        Key(w, K.OemCloseBrackets);
        Assert.True(w.Session.StampOptions.Scale > 0.5);
        ClickDoc(w, 200, 150);
        var doc = w.Session.Document;
        Assert.True(A(doc, 200, 150) > 200);
        Assert.Equal("Stamp", doc.History.NextUndoName);
        Key(w, K.Z, RawInputModifiers.Control);
        Assert.Equal(0, A(doc, 200, 150));
        w.Close();
    }

    [AvaloniaFact]
    public void Workflow_B_a_grass_stamp_becomes_a_brush_that_survives_a_restart()
    {
        var w = Window();
        var doc = w.Session.Document;
        // Draw a little tuft, select it, save it as a stamp (the dialog asks for a name).
        w.Session.SetForeground(ColorRgb.Parse("#5F8F5A"));
        w.Session.ApplyPreset("ink");
        Line(w, 100, 200, 90, 120);
        Line(w, 105, 200, 110, 110);
        Line(w, 110, 200, 130, 125);
        var before = doc.ActiveLayer.Image.Pixels.ToArray();
        w.Session.ApplySelection(Selections.Rectangle(doc, 70, 100, 150, 210), "Select");
        var save = w.SaveSelectionAsAssetAsync(PickerKind.Stamps);
        Pump();
        w.Dialogs.Close("ok");
        Pump();
        Assert.True(save.Result);
        Assert.Equal(before, doc.ActiveLayer.Image.Pixels); // the artwork is unchanged
        var stamp = w.Session.Library.Stamps.Single(s => !s.BuiltIn);

        var brush = w.MakeStampBrush(stamp.Id);
        w.Session.Library.UpdateBrush(brush.Id, brush.Settings with { Spacing = 0.8, Scatter = 0.6, Size = 40 });
        w.Session.ApplyPreset(brush.Id);
        w.Session.Deselect();
        Line(w, 180, 250, 380, 250);
        var greens = Enumerable.Range(180, 200).Count(x => Pixel.ToStraight(doc.ActiveLayer.Image[x, 250]) is var (r, g, b, a) && a > 128 && g > r && g > b);
        Assert.True(greens > 20, $"{greens} green pixels"); // the stamp's own colour
        w.Close();

        var again = Window();
        var b2 = again.Session.Library.Brushes.Single(b => b.Id == brush.Id);
        Assert.False(again.Session.Library.HasMissingParts(b2.Settings));
        Assert.Equal(0.8, b2.Settings.Spacing);
        again.Close();
    }

    // ================================================================== patterns

    [AvaloniaFact]
    public void Workflow_C_a_drawn_tile_becomes_a_pattern_and_fills_exactly_and_exports()
    {
        var w = Window(WorkspaceKind.PixelArt, 8, 8);
        var tileDoc = w.Session.Document;
        // An 8 × 8 tile: a red corner pixel and a blue diagonal.
        tileDoc.ActiveLayer.Image[0, 0] = Pixel.Opaque(ColorRgb.Parse("#FF0000"));
        for (int i = 1; i < 8; i++) tileDoc.ActiveLayer.Image[i, i] = Pixel.Opaque(ColorRgb.Parse("#0000FF"));
        tileDoc.SetSelection(Selections.Rectangle(tileDoc, 0, 0, 8, 8), "Select");
        var save = w.SaveSelectionAsAssetAsync(PickerKind.Patterns);
        Pump();
        Assert.True(w.GetVisualDescendants().OfType<Image>().Any(i => i.IsEffectivelyVisible && i.Source is not null)); // the tiled preview
        w.Dialogs.Close("ok");
        Pump();
        Assert.True(save.Result);
        var pattern = w.Session.Library.Patterns.Single(p => !p.BuiltIn);

        w.Session.ReplaceDocument(new Document(40, 24, null, "Sprite"), null);
        var doc = w.Session.Document;
        doc.SetSelection(Selections.Rectangle(doc, 0, 0, 32, 16), "Select");
        w.Session.SetPatternOptions(w.Session.PatternOptions with { PatternId = pattern.Id, Scale = 2, OffsetX = 4, OffsetY = 0 });
        Assert.True(w.Session.FillSelectionWithPattern());
        var png = Path.Combine(_dir, "tiles.png");
        w.ExportToPath(png, ExportFormat.Png);
        var out_ = PngCodec.Decode(File.ReadAllBytes(png));
        Assert.Equal(Pixel.Opaque(ColorRgb.Parse("#FF0000")), out_[4, 0]);  // the tile's corner, shifted by 4, doubled
        Assert.Equal(Pixel.Opaque(ColorRgb.Parse("#FF0000")), out_[5, 1]);
        Assert.Equal(Pixel.Opaque(ColorRgb.Parse("#0000FF")), out_[6, 2]);
        Assert.Equal(Pixel.Opaque(ColorRgb.Parse("#FF0000")), out_[20, 0]); // repeats every 16 px
        Assert.Equal(0u, out_[35, 20]);                                      // outside the selection
        Assert.All(out_.Pixels, p => Assert.True(Pixel.A(p) is 0 or 255));  // exact pixels, no blending
        w.Close();
    }

    [AvaloniaFact]
    public void The_paint_bucket_switches_to_pattern_mode_from_the_options_bar()
    {
        var w = Window();
        Key(w, K.G);
        Assert.Equal(ToolKind.Fill, w.Session.Tool);
        Assert.False(Has(w, "PatternChip"));
        var patternButton = w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Pattern"));
        Click(w, patternButton);
        Assert.True(w.Session.PatternOptions.UsePattern);
        Assert.True(Has(w, "PatternChip"));
        ClickDoc(w, 100, 100);
        var img = w.Session.Document.ActiveLayer.Image;
        Assert.Equal(255, Pixel.A(img[100, 100]));
        Assert.True(img.Pixels.Distinct().Count() > 2); // a pattern, not one colour
        w.Close();
    }

    // ================================================================== palettes

    [AvaloniaFact]
    public void Workflow_D_a_palette_from_a_picture_is_used_in_pixel_art_and_kept()
    {
        var w = Window(WorkspaceKind.Painting, 30, 30);
        var colours = new[] { "#22383F", "#5F8F5A", "#F2C14E" }.Select(ColorRgb.Parse).ToArray();
        var picture = new RasterImage(30, 30);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 30; x++)
                picture[x, y] = Pixel.Opaque(colours[(x / 10) % 3]);
        w.Session.ReplaceDocument(Document.FromImage(picture, "Picture"), null); // as if opened
        var doc = w.Session.Document;
        var manager = w.OpenPalettesAsync();
        Pump();
        var m = w.LastPaletteManager!;
        m.ExtractFrom(doc.Flatten(), 8);
        var palette = m.SaveProposal()!;
        Assert.Equal(colours.ToHashSet(), palette.Colors.ToHashSet()); // exact colours
        w.Dialogs.Close("use");
        Pump();
        Assert.Equal(palette.Id, w.Session.ActivePaletteId);

        w.SetWorkspace(WorkspaceKind.PixelArt);
        w.UsePalette(palette.Id);
        Assert.Equal(palette.Colors, w.Session.Document.Palette.Colors);
        w.Close();

        var again = Window(WorkspaceKind.PixelArt);
        Assert.Equal(palette.Colors, again.Session.Library.FindPalette(palette.Id)!.Colors);
        again.Close();
    }

    [AvaloniaFact]
    public void The_palette_manager_edits_reorders_and_protects_built_in_palettes()
    {
        var w = Window();
        _ = w.OpenPalettesAsync(BuiltInAssets.Prefix + "meadow");
        Pump();
        var m = w.LastPaletteManager!;
        var meadow = w.Session.Library.FindPalette(BuiltInAssets.Prefix + "meadow")!.Colors.ToList();
        m.EditColors(c => c.Clear()); // built in: refused
        Assert.Equal(meadow, w.Session.Library.FindPalette(BuiltInAssets.Prefix + "meadow")!.Colors);
        var mine = w.Session.Library.DuplicatePalette(BuiltInAssets.Prefix + "meadow");
        m.Select(mine.Id);
        w.Session.SetForeground(ColorRgb.Parse("#123456"));
        m.EditColors(c => c.Add(w.Session.Foreground));
        m.EditColors(c => (c[0], c[1]) = (c[1], c[0]));
        var now = w.Session.Library.FindPalette(mine.Id)!.Colors;
        Assert.Equal(meadow.Count + 1, now.Count);
        Assert.Equal(meadow[1], now[0]);
        Assert.Equal(ColorRgb.Parse("#123456"), now[^1]);
        w.Dialogs.Close("close");
        Pump();
        w.Close();
    }

    // ================================================================== reference board

    [AvaloniaFact]
    public void Workflow_E_references_stay_beside_the_artwork_and_out_of_exports()
    {
        var w = Window(WorkspaceKind.Painting, 400, 300, ColorRgb.White);
        w.SetReferenceBoardVisible(true);
        Pump();
        Assert.True(Has(w, "ReferenceBoard"));
        var board = w.ReferenceBoard;
        var red = new RasterImage(60, 40);
        red.Fill(Pixel.Opaque(ColorRgb.Parse("#E0604A")));
        var blue = new RasterImage(30, 30);
        blue.Fill(Pixel.Opaque(ColorRgb.Parse("#3F6A82")));
        board.Add(red, "Red");
        var item = board.Add(blue, "Blue");
        item.X += 100;
        Pump();
        Assert.Equal(2, board.Board.Items.Count);

        // Clicking and dragging on the board never paints on the canvas underneath.
        var view = Find<ReferenceBoardView>(w, "ReferenceBoardView");
        var onBoard = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), w)!.Value;
        w.MouseMove(onBoard);
        w.MouseDown(onBoard, MouseButton.Left);
        w.MouseMove(onBoard + new Vector(30, 10), RawInputModifiers.LeftMouseButton);
        w.MouseUp(onBoard + new Vector(30, 10), MouseButton.Left);
        Pump();
        Assert.False(w.Session.Document.History.CanUndo);

        // Alt-click picks a colour from a reference.
        var redCenter = board.Board.Items.First(i => i.Name == "Red");
        board.Board.ViewX = redCenter.X;
        board.Board.ViewY = redCenter.Y;
        board.Board.Zoom = 1;
        view.InvalidateVisual();
        var pick = view.TranslatePoint(new Point(20, 20), w)!.Value;
        w.MouseMove(pick, RawInputModifiers.Alt);
        w.MouseDown(pick, MouseButton.Left, RawInputModifiers.Alt);
        w.MouseUp(pick, MouseButton.Left, RawInputModifiers.Alt);
        Pump();
        Assert.Equal(ColorRgb.Parse("#E0604A"), w.Session.Foreground);

        // Draw on the canvas, save and reopen the board, export the picture.
        w.Session.SetForeground(ColorRgb.Parse("#3B2F2A"));
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.ApplyPreset("ink");
        Line(w, 20, 250, 380, 250);
        Assert.True(w.Session.Document.History.CanUndo);
        var boardFile = Path.Combine(_dir, "refs.dnkboard");
        board.Board.Save(boardFile);
        board.ReplaceBoard(ReferenceBoard.Load(boardFile));
        Assert.Equal(["Red", "Blue"], board.Board.Items.Select(i => i.Name));
        var png = Path.Combine(_dir, "art.png");
        w.ExportToPath(png, ExportFormat.Png);
        var exported = PngCodec.Decode(File.ReadAllBytes(png));
        Assert.Equal(w.Session.Document.Flatten().Pixels, exported.Pixels); // exactly the artwork…
        Assert.DoesNotContain(Pixel.Opaque(ColorRgb.Parse("#E0604A")), exported.Pixels); // …without the references

        // Hidden: the board takes no input at all.
        w.SetReferenceBoardVisible(false);
        Pump();
        Assert.False(Has(w, "ReferenceBoard"));
        w.Close();
    }

    // ================================================================== animation

    [AvaloniaFact]
    public async Task Workflow_F_brushes_and_stamps_on_an_animation_frame_change_only_that_frame()
    {
        var w = Open(1280, 800, WorkspaceKind.Animation);
        w.UseLibrary(new CreativeLibrary(LibDir));
        w.CreateNewAnimation(new NewAnimationChoice(200, 120, 3, 12, false, null));
        Pump();
        var doc = w.Session.Document;
        var layer = doc.ActiveLayer;
        w.Session.GoToFrame(1);
        w.Session.ApplyPreset("charcoal"); // a custom-tip brush
        Line(w, 20, 60, 180, 60);
        Assert.True(Pixel.A(doc.CelAt(layer, 1)!.Image[100, 60]) > 0);
        w.WsState.SetShown(CustomOptions.StampTool, WorkspaceKind.Animation, true);
        w.ApplyCustomization();
        Pump();
        Key(w, K.S);
        Assert.Equal(ToolKind.Stamp, w.Session.Tool);
        w.Session.SetStampOptions(w.Session.StampOptions with { StampId = BuiltInAssets.Prefix + "star", Scale = 0.4 });
        ClickDoc(w, 100, 30);
        Assert.Equal(1, doc.CurrentFrame);
        Assert.True(Pixel.A(doc.CelAt(layer, 1)!.Image[100, 30]) > 200);
        w.Animator.ManualClock = true;
        Key(w, K.Enter);
        w.Animator.Tick(100);
        Key(w, K.Enter);
        w.Session.GoToFrame(1);
        Key(w, K.Z, RawInputModifiers.Control); // undo the stamp
        Assert.Equal(0, Pixel.A(doc.CelAt(layer, 1)!.Image[100, 30]));
        Assert.True(Pixel.A(doc.CelAt(layer, 1)!.Image[100, 60]) > 0); // the brush stroke stays

        var path = Path.Combine(_dir, "anim.dnk");
        Assert.True(await w.SaveToPathAsync(path));
        var back = DnkFile.Load(path);
        Assert.Null(back.CelAt(back.Layers[0], 0));
        Assert.Null(back.CelAt(back.Layers[0], 2));
        Assert.True(Pixel.A(back.CelAt(back.Layers[0], 1)!.Image[100, 60]) > 0);
        w.Close();
    }

    // ================================================================== interface

    [AvaloniaFact]
    public void New_tools_stay_out_of_the_way_until_asked_for()
    {
        var w = Window(WorkspaceKind.Editing);
        Assert.DoesNotContain(ToolKind.Stamp, w.CurrentTools);
        Key(w, K.S);
        Assert.NotEqual(ToolKind.Stamp, w.Session.Tool); // S does nothing here by default
        w.WsState.SetShown(CustomOptions.StampTool, WorkspaceKind.Editing, true);
        w.ApplyCustomization();
        Pump();
        Assert.Contains(ToolKind.Stamp, w.CurrentTools);
        Assert.Equal(ToolKind.Hand, w.CurrentTools[^1]);
        Assert.True(Has(w, "ToolStamp"));
        w.WsState.ResetLayout();
        w.ApplyCustomization();
        Pump();
        Assert.False(Has(w, "ToolStamp"));

        w.SetWorkspace(WorkspaceKind.PureSketch);
        Pump();
        Assert.False(Has(w, "ReferenceBoard") || Has(w, "StampChip"));
        foreach (var kind in Workspaces.All.Select(x => x.Kind))
        {
            w.SetWorkspace(kind);
            Pump();
            Assert.False(Has(w, "ReferenceBoard"), kind.ToString()); // nothing new opens by itself
        }
        w.Close();
    }

    [AvaloniaFact]
    public void Existing_shortcuts_keep_their_meaning()
    {
        var w = Window();
        Key(w, K.B);
        Assert.Equal(ToolKind.Brush, w.Session.Tool);
        Key(w, K.G);
        Assert.Equal(ToolKind.Fill, w.Session.Tool);
        Key(w, K.U);
        Assert.Equal(ToolKind.Shape, w.Session.Tool);
        Key(w, K.S, RawInputModifiers.Control); // Ctrl+S saves (opens the save picker), it never picks the stamp tool
        Assert.Equal(ToolKind.Shape, w.Session.Tool);
        Key(w, K.OemCloseBrackets);
        Assert.Equal(ToolKind.Shape, w.Session.Tool);
        w.Close();
    }
}
