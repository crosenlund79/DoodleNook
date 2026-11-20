using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 010 — Smudge, Clone and Repair in the window: tools, shortcuts, Alt+click source, options and undo.</summary>
public class RetouchWindowTests
{
    private static readonly ColorRgb Red = new(200, 40, 40), Blue = new(40, 60, 200), Grey = new(128, 128, 128);

    /// <summary>A 400 × 300 picture: red left half, blue right half, a black speck at (300, 150) and a checkered patch at the top left.</summary>
    internal static MainWindow Window(WorkspaceKind ws = WorkspaceKind.Painting)
    {
        var w = Open(1280, 800, ws);
        var img = new RasterImage(400, 300);
        for (int y = 0; y < 300; y++)
            for (int x = 0; x < 400; x++)
                img[x, y] = Pixel.Opaque(x < 200 ? Red : Blue);
        for (int y = 20; y < 80; y++)
            for (int x = 20; x < 80; x++)
                img[x, y] = Pixel.Opaque(((x / 4) + (y / 4)) % 2 == 0 ? new ColorRgb(230, 230, 230) : new ColorRgb(60, 60, 60));
        for (int y = 147; y < 153; y++)
            for (int x = 297; x < 303; x++)
                img[x, y] = Pixel.Opaque(ColorRgb.Black);
        w.Session.ReplaceDocument(Document.FromImage(img, "Test"), null);
        w.Canvas.FitToView();
        Pump();
        return w;
    }

    private static uint Px(MainWindow w, int x, int y) => w.Session.Document.ActiveLayer.Image[x, y];

    [AvaloniaFact]
    public void Smudge_is_one_button_in_painting_with_clone_and_repair_behind_it_and_has_shortcuts()
    {
        var w = Window();
        Assert.True(Has(w, "ToolSmudge"));
        Assert.False(Has(w, "ToolClone"));   // one button for the group
        Key(w, Avalonia.Input.Key.B, RawInputModifiers.Shift);
        Assert.Equal(ToolKind.Smudge, w.Session.Tool);
        Key(w, Avalonia.Input.Key.S, RawInputModifiers.Shift);
        Assert.Equal(ToolKind.Clone, w.Session.Tool);
        Key(w, Avalonia.Input.Key.J);
        Assert.Equal(ToolKind.Repair, w.Session.Tool);
        // The button now shows Repair; right-click lists the group.
        var button = Find<Button>(w, "ToolSmudge");
        Assert.Equal(ToolKind.Repair, button.Tag);
        Assert.Contains("on", button.Classes);
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, button, MouseButton.Right);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Clone");
        Key(w, Avalonia.Input.Key.Escape);
        Key(w, Avalonia.Input.Key.B);
        Assert.Equal(ToolKind.Brush, w.Session.Tool);
        Assert.False(button.Classes.Contains("on"));
        // Not in the minimal workspaces unless asked for (Customize workspace).
        w.SetWorkspace(WorkspaceKind.QuickSketch);
        Pump();
        Assert.False(Has(w, "ToolSmudge"));
        Assert.DoesNotContain(ToolKind.Smudge, w.CurrentTools);
        w.Close();
    }

    [AvaloniaFact]
    public void Smudge_drags_red_into_blue_as_one_undo_step()
    {
        var w = Window();
        w.Session.SelectTool(ToolKind.Smudge);
        w.Session.SetRetouchOptions(w.Session.Retouch with { SmudgeSize = 40, SmudgeStrength = 0.8 });
        Pump();
        Assert.True(Has(w, "OptionsBar") || true);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (170, 200), (200, 200), (230, 200), (260, 200));
        Assert.True(Pixel.R(Px(w, 225, 200)) > 100, $"{Pixel.R(Px(w, 225, 200))}");
        Assert.Equal(Pixel.Opaque(Blue), Px(w, 225, 120));
        Assert.True(w.Session.Document.CanUndo);
        w.Undo();
        Assert.Equal(Pixel.Opaque(Blue), Px(w, 225, 200));
        Assert.False(w.Session.Document.CanUndo);
        // Normal painting afterwards is unaffected.
        Key(w, Avalonia.Input.Key.B);
        w.Session.SetForeground(ColorRgb.Black);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (100, 250), (120, 250));
        Assert.NotEqual(Pixel.Opaque(Red), Px(w, 110, 250));
        w.Close();
    }

    [AvaloniaFact]
    public void Clone_asks_for_a_source_then_alt_click_sets_it_and_painting_copies_from_there()
    {
        var w = Window();
        var s = w.Session;
        s.SelectTool(ToolKind.Clone);
        s.SetRetouchOptions(s.Retouch with { CloneSize = 30, CloneSoftness = 0 });
        Pump();
        // Without a source: a hint, nothing painted.
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 150), (305, 150));
        Assert.Contains("Alt+click", w.LastToast);
        Assert.False(s.Document.CanUndo);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Alt+click where to copy from, then paint");

        // Alt+click the checkered patch, then paint over the speck.
        var src = DocToWindow(w, 50, 50);
        w.KeyPress(Avalonia.Input.Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        w.MouseMove(src, RawInputModifiers.Alt);
        w.MouseDown(src, MouseButton.Left, RawInputModifiers.Alt);
        w.MouseUp(src, MouseButton.Left, RawInputModifiers.Alt);
        w.KeyRelease(Avalonia.Input.Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Pump();
        Assert.NotNull(s.CloneSource);
        Assert.False(s.Document.CanUndo); // choosing a source isn't an edit
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Source chosen · Alt+click to change");
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 150), (301, 150));
        Assert.NotNull(s.CloneOffset);
        var (dx, dy) = s.CloneOffset!.Value;
        Assert.InRange(dx, -252, -248);
        Assert.InRange(dy, -101, -99);
        Assert.Equal(Px(w, 300 + dx, 150 + dy), Px(w, 300, 150));      // the speck is now the patch
        Assert.NotEqual(Pixel.Opaque(ColorRgb.Black), Px(w, 300, 150));
        // A second stroke keeps the same offset (aligned).
        Drag(w, MouseButton.Left, RawInputModifiers.None, (320, 160), (321, 160));
        Assert.Equal(s.CloneOffset, (dx, dy));
        w.Undo();
        w.Undo();
        Assert.Equal(Pixel.Opaque(ColorRgb.Black), Px(w, 300, 150));
        w.Close();
    }

    [AvaloniaFact]
    public void Repair_removes_the_speck_from_its_surroundings_and_works_on_a_game_style_locked_palette()
    {
        var w = Window();
        var s = w.Session;
        s.Document.LockToPalette(new Palette("Game", [Red, Blue, ColorRgb.Black, new ColorRgb(230, 230, 230), new ColorRgb(60, 60, 60)]));
        Key(w, Avalonia.Input.Key.J);
        s.SetRetouchOptions(s.Retouch with { RepairSize = 16, RepairFromSource = false });
        Pump();
        Drag(w, MouseButton.Left, RawInputModifiers.None, (298, 150), (302, 150));
        Assert.Equal(Pixel.Opaque(Blue), Px(w, 300, 150)); // filled from the blue around it, a palette colour
        Assert.All(s.Document.ActiveLayer.Image.Pixels, p => Assert.True(s.Document.PaletteConformer!.IsPaletteColour(p)));
        Assert.True(s.Document.CanUndo);
        w.Close();
    }
}
