using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 008 screenshots (out/screenshots/design-*.png): text, shapes, groups, masks, adjustment layers, assistants.</summary>
public class DesignScreenshots
{
    internal static readonly ColorRgb Plum = ColorRgb.Parse("#5B3A6B"), Cream = ColorRgb.Parse("#F6E7C8"), Gold = ColorRgb.Parse("#E9A94D"),
        Ink = ColorRgb.Parse("#2B2230"), Sky = ColorRgb.Parse("#9CC9D9");

    /// <summary>A game title screen: a painted background, and a "Menu" group with a button shape and its label.</summary>
    internal static MainWindow MenuScene(double width, double height, double scaling, WorkspaceKind workspace = WorkspaceKind.Painting)
    {
        var w = Open(width, height, workspace);
        w.SetRenderScaling(scaling);
        Pump();
        var s = w.Session;
        s.ReplaceDocument(new Document(1600, 1000, Cream, "Background"), null);
        var doc = s.Document;
        w.Canvas.FitToView(enlarge: false);
        // Background: a sky gradient and a few hills, painted.
        s.SetForeground(Sky);
        s.SetBackground(Cream);
        var g = new GradientOperation(doc, doc.ActiveLayer, Sky, Cream, s.Gradient with { Shape = GradientShape.Linear, ToTransparent = false, Opacity = 1 });
        g.Update(800, 0, 800, 1000);
        g.Commit();
        doc.AddShapeLayer(new ShapeContent(ShapeType.Ellipse, -200, 640, 1100, 700, ColorRgb.Parse("#7FA36B")), "Hill");
        doc.AddShapeLayer(new ShapeContent(ShapeType.Ellipse, 700, 700, 1200, 700, ColorRgb.Parse("#6A9159")), "Hill 2");
        doc.AddTextLayer(new TextContent("Doodle Quest", "Varela Round", 120, Plum, Align: TextAlign.Center, X: 0, Y: 120,
            Outline: new OutlineEffect(Cream, 6), Shadow: new ShadowEffect(Ink, 4, 6, 6, 0.35)));
        var title = doc.ActiveLayer;
        doc.AlignObjects([title], Engine.Geometry.AlignEdge.CenterX, toCanvas: true);
        var button = doc.AddShapeLayer(new ShapeContent(ShapeType.RoundedRectangle, 600, 460, 400, 120, Gold, Ink, 5, 0, 36,
            Shadow: new ShadowEffect(Ink, 0, 10, 8, 0.4)), "Play button");
        var label = doc.AddTextLayer(new TextContent("PLAY", "Inter", 64, Ink, Bold: true, Align: TextAlign.Center, X: 700, Y: 470), "PLAY");
        s.Align(Engine.Geometry.AlignEdge.CenterX, true);
        s.Align(Engine.Geometry.AlignEdge.CenterY, true);
        var group = doc.GroupLayers([button, label], "Menu");
        doc.SetGuides([new Guide(true, 800), new Guide(false, 520)], "Guides");
        doc.SetActiveLayer(label);
        doc.MarkSaved();
        w.Canvas.FitToView(enlarge: false);
        Pump();
        _ = group;
        return w;
    }

    [AvaloniaFact]
    public async Task Capture_design_screenshots()
    {
        foreach (var (name, width, height, scaling) in new[] { ("desktop27", 2560.0, 1440.0, 1.0), ("desktop1080", 1920.0, 1080.0, 1.0), ("kamvas13-150", 1280.0, 720.0, 1.5) })
        {
            // The game menu: Move tool on the label (handles), rulers and guides, the layers panel with the group.
            var w = MenuScene(width, height, scaling);
            w.SetShowRulers(true);
            w.Session.SelectTool(ToolKind.Move);
            Pump();
            w.MouseMove(DocToWindow(w, 1200, 800));
            WorkshopScreenshots.Save(w, $"design-{name}-menu");
            w.SetShowRulers(false);
            w.Close();
        }

        var k = MenuScene(1280, 720, 1.5);
        // Text tool, typing.
        k.Session.SelectTool(ToolKind.Text);
        Pump();
        k.TextEditor.Open(k.Session.Document.ActiveLayer, false);
        Pump();
        WorkshopScreenshots.Save(k, "design-text-editing");
        k.TextEditor.Finish();
        // Text effects.
        Click(k, Find<Button>(k, "EffectsButton"));
        WorkshopScreenshots.Save(k, "design-text-effects");
        UiKit.HideOpenFlyouts();
        Pump();
        // Editable shape options on the button.
        var button = k.Session.Document.Layers.First(l => l.Name == "Play button");
        k.Session.Document.SetActiveLayer(button);
        k.Session.SelectTool(ToolKind.Shape);
        Pump();
        WorkshopScreenshots.Save(k, "design-shape-options");
        // The drawing assistants with two-point perspective.
        k.Session.SelectTool(ToolKind.Brush);
        k.Session.SetPerspective(2);
        Pump();
        k.ShowAssistants(Find<Button>(k, "AssistantsButton"));
        Pump();
        WorkshopScreenshots.Save(k, "design-assistants");
        UiKit.HideOpenFlyouts();
        k.Session.SetPerspective(0);
        // A mask and an adjustment layer in the layers panel; the adjustment dialog.
        var hill = k.Session.Document.Layers.First(l => l.Name == "Hill");
        k.Session.Document.SetActiveLayer(hill);
        k.Session.AddMask();
        var adjust = k.NewAdjustmentLayerAsync(AdjustKind.HueSaturation);
        Pump();
        WorkshopScreenshots.Save(k, "design-adjustment-layer");
        k.Dialogs.Close("ok");
        await adjust;
        k.Close();

        // The Editing workspace: the layers panel with a group, a mask being painted and an adjustment layer.
        var e = MenuScene(1920, 1080, 1, WorkspaceKind.Editing);
        var doc = e.Session.Document;
        var hill2 = doc.Layers.First(l => l.Name == "Hill 2");
        doc.SetActiveLayer(hill2);
        e.Session.AddAdjustmentLayer(new HueSaturation(-20, 25, 0));
        var hill1 = doc.Layers.First(l => l.Name == "Hill");
        doc.SetActiveLayer(hill1);
        e.Session.AddMask();
        e.Session.SelectTool(ToolKind.Brush);
        e.Session.SetForeground(ColorRgb.Black);
        Drag(e, MouseButton.Left, RawInputModifiers.None, (100, 900), (300, 820), (500, 900));
        Pump();
        WorkshopScreenshots.Save(e, "design-desktop1080-layers");
        e.Close();
    }
}
