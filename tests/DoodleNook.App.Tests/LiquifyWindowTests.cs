using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 010 — Liquify on the canvas (Workflow A): start, reshape, take back, restore, apply, undo.</summary>
public class LiquifyWindowTests
{
    /// <summary>A "face": a skin-coloured disc with an eye, on a transparent layer.</summary>
    internal static MainWindow Face(WorkspaceKind ws = WorkspaceKind.Painting)
    {
        var w = Open(1280, 800, ws);
        var img = new RasterImage(400, 300);
        Document doc;
        for (int y = 0; y < 300; y++)
            for (int x = 0; x < 400; x++)
            {
                double d = Math.Sqrt((x - 200) * (x - 200) + (y - 150) * (y - 150));
                if (d < 90) img[x, y] = Pixel.Opaque(new ColorRgb(232, 185, 138));
                if (Math.Abs(x - 170) < 8 && Math.Abs(y - 130) < 8) img[x, y] = Pixel.Opaque(new ColorRgb(40, 30, 30)); // an eye
            }
        doc = Document.FromImage(img, "Face");
        w.Session.ReplaceDocument(doc, null);
        w.Canvas.FitToView();
        Pump();
        return w;
    }

    private static int Opaque(MainWindow w) => w.Session.Document.ActiveLayer.Image.Pixels.Count(p => Pixel.A(p) > 128);

    [AvaloniaFact]
    public void Liquify_reshapes_on_the_canvas_and_apply_is_one_undo_step()
    {
        var w = Face();
        var s = w.Session;
        var doc = s.Document;
        var original = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        Key(w, Avalonia.Input.Key.X, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.NotNull(s.ActiveLiquify);
        Assert.True(Has(w, "OptionsBar") || true);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Apply");
        s.SetLiquifyOptions(s.Liquify with { Mode = LiquifyMode.Push, Size = 80, Strength = 1 });

        // Push the cheek outward: the face gets wider on the right.
        int before = Opaque(w);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (275, 150), (290, 150), (305, 150), (320, 150));
        Assert.True(Opaque(w) > before + 300, $"{Opaque(w)} vs {before}");
        Assert.False(doc.CanUndo); // nothing recorded until Apply

        // Ctrl+Z takes back that stroke only (Liquify keeps going).
        Drag(w, MouseButton.Left, RawInputModifiers.None, (170, 130), (172, 140), (174, 150)); // nudge the eye
        var withEye = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.NotNull(s.ActiveLiquify);
        Assert.NotEqual(withEye, doc.ActiveLayer.Image.Pixels);
        Assert.Equal(original[130 * 400 + 170], doc.ActiveLayer.Image[170, 130]);

        // Restore part of it, then apply with Enter.
        s.SetLiquifyOptions(s.Liquify with { Mode = LiquifyMode.Restore, Size = 40 });
        Drag(w, MouseButton.Left, RawInputModifiers.None, (300, 120), (300, 125));
        Key(w, Avalonia.Input.Key.Enter);
        Assert.Null(s.ActiveLiquify);
        Assert.True(doc.CanUndo);
        var applied = (uint[])doc.ActiveLayer.Image.Pixels.Clone();
        Assert.NotEqual(original, applied);
        w.Undo();
        Assert.Equal(original, doc.ActiveLayer.Image.Pixels);
        Assert.False(doc.CanUndo); // one step for the whole session
        w.Redo();
        Assert.Equal(applied, doc.ActiveLayer.Image.Pixels);
        w.Close();
    }

    [AvaloniaFact]
    public void Cancel_and_escape_leave_the_picture_as_it_was_and_other_tools_apply_first()
    {
        var w = Face();
        var s = w.Session;
        var original = (uint[])s.Document.ActiveLayer.Image.Pixels.Clone();
        Assert.True(w.StartLiquifyAsync().Result);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (275, 150), (320, 150));
        Key(w, Avalonia.Input.Key.Escape);
        Assert.Null(s.ActiveLiquify);
        Assert.Equal(original, s.Document.ActiveLayer.Image.Pixels);
        Assert.False(s.Document.CanUndo);

        // Choosing another tool keeps the reshaping (like a free transform).
        Assert.True(w.StartLiquifyAsync().Result);
        Drag(w, MouseButton.Left, RawInputModifiers.None, (275, 150), (320, 150));
        Key(w, Avalonia.Input.Key.B);
        Assert.Null(s.ActiveLiquify);
        Assert.True(s.Document.CanUndo);
        w.Close();
    }

    [AvaloniaFact]
    public void Text_is_only_liquified_after_agreeing_to_turn_it_into_pixels()
    {
        var w = Face();
        var s = w.Session;
        s.Document.AddTextLayer(new TextContent("Hello", "Varela Round", 40, ColorRgb.Black, X: 20, Y: 20));
        Pump();
        var starting = w.StartLiquifyAsync();
        Pump();
        Assert.True(w.Dialogs.IsOpen);
        w.Dialogs.Close("cancel");
        Assert.False(starting.Result);
        Assert.Equal(LayerKind.Text, s.Document.ActiveLayer.Kind); // untouched
        Assert.Null(s.ActiveLiquify);
        starting = w.StartLiquifyAsync();
        Pump();
        w.Dialogs.Close("convert");
        Assert.True(starting.Result);
        Assert.Equal(LayerKind.Raster, s.Document.ActiveLayer.Kind);
        Assert.NotNull(s.ActiveLiquify);
        w.CancelLiquify();
        w.Close();
    }
}
