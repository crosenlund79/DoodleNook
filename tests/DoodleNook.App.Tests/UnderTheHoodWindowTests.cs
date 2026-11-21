using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DoodleNook.App.Diagnostics;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 011 — Under the Hood in the window: no listeners pile up, hidden panels still show the truth when they
/// come back, replaced pictures are released, and Help › Diagnostics shows real numbers without paths.</summary>
public class UnderTheHoodWindowTests
{
    private static int Listeners(object target, string eventName) =>
        (target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as Delegate)?.GetInvocationList().Length ?? 0;

    [AvaloniaFact]
    public void Changing_tools_and_workspaces_again_and_again_adds_no_listeners()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        // Visit every workspace once (each builds its panels the first time), then measure.
        foreach (var k in Enum.GetValues<WorkspaceKind>())
        {
            w.SetWorkspace(k);
            Pump();
        }
        w.SetWorkspace(WorkspaceKind.Painting);
        Pump();
        int state = Listeners(w.Session, "StateChanged"), library = Listeners(w.Session, "LibraryChanged"), replaced = Listeners(w.Session, "DocumentReplaced");
        int view = Listeners(w.Canvas, "ViewChanged");
        for (int round = 0; round < 3; round++)
        {
            foreach (var k in Enum.GetValues<WorkspaceKind>())
            {
                w.SetWorkspace(k);
                Pump();
                foreach (var tool in w.CurrentTools)
                {
                    w.Session.SelectTool(tool);
                    Pump();
                }
            }
            w.SetWorkspace(WorkspaceKind.Painting);
            w.Session.SelectTool(ToolKind.Brush);
            Pump();
        }
        Assert.Equal(state, Listeners(w.Session, "StateChanged"));
        Assert.Equal(library, Listeners(w.Session, "LibraryChanged"));
        Assert.Equal(replaced, Listeners(w.Session, "DocumentReplaced"));
        Assert.Equal(view, Listeners(w.Canvas, "ViewChanged"));
        w.Close();
    }

    [AvaloniaFact]
    public void A_pop_over_brush_panel_shows_the_current_brush_when_it_opens_even_if_it_changed_while_closed()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        w.Session.SelectTool(ToolKind.Brush);
        w.Session.ApplyPreset("ink");
        Pump();
        // The options bar's brush chip opens a brush panel in a pop-over; change the brush while it is closed.
        w.Session.ApplyPreset("pencil");
        w.Session.SetSize(33);
        Pump();
        var chip = w.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Flyout is Flyout { Content: BrushPanel });
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, chip);
        Pump();
        var panel = (BrushPanel)((Flyout)chip.Flyout!).Content!;
        Assert.True(panel.IsEffectivelyVisible);
        // The pencil's tile is the chosen one (accent border), not the ink's.
        static Avalonia.Media.Color Edge(BrushPanel panel, string id) =>
            ((Avalonia.Media.SolidColorBrush)((Border)panel.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BrushTile_" + id).Content!).BorderBrush!).Color;
        Assert.Equal(DoodleNook.App.Controls.DnColors.Accent, Edge(panel, "pencil"));
        Assert.NotEqual(DoodleNook.App.Controls.DnColors.Accent, Edge(panel, "ink"));
        Key(w, Avalonia.Input.Key.Escape);
        Pump();
        w.Close();
    }

    [AvaloniaFact]
    public void Pictures_that_were_replaced_are_released()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var old = new List<WeakReference>();
        for (int i = 0; i < 6; i++)
        {
            old.Add(ReplaceWithNew(w));
            Pump();
        }
        w.Session.ReplaceDocument(new Document(400, 300, ColorRgb.White), null);
        Pump();
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.Equal(0, old.Count(r => r.IsAlive));
        w.Close();
    }

    /// <summary>In its own method, so no local variable of the test keeps the picture alive.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ReplaceWithNew(MainWindow w)
    {
        var d = new Document(1200, 800, ColorRgb.White);
        d.AddLayer("Ink");
        w.Session.ReplaceDocument(d, null);
        return new WeakReference(d);
    }

    [AvaloniaFact]
    public void Help_diagnostics_shows_real_numbers_and_copies_them_without_paths()
    {
        var w = Open(1280, 800, WorkspaceKind.Painting);
        var doc = new Document(1920, 1080, ColorRgb.White, "Sky");
        doc.AddLayer("Ink");
        w.Session.ReplaceDocument(doc, Path.Combine(Path.GetTempPath(), "secret-folder", "my picture.dnk"));
        Pump();
        Menu(w, "Help", "Diagnostics…");
        Pump();
        Assert.True(Has(w, "DiagnosticsReport"));
        var texts = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("1920 × 1080 px", texts);
        Assert.Contains(texts, t => t?.StartsWith("2 (") == true); // layers
        Assert.Contains(texts, t => t?.Contains(MainWindow.Version) == true);
        w.CaptureRenderedFrame()?.Dispose();
        Click(w, Find<Button>(w, "CopyDiagnostics"));
        Pump();
        var text = DiagnosticsReport.LastCopiedText;
        Assert.NotNull(text);
        Assert.Contains("1920 × 1080 px", text);
        Assert.DoesNotContain("secret-folder", text);
        Assert.DoesNotContain("my picture", text);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), text);
        Assert.DoesNotContain(Environment.UserName, text);
        w.Dialogs.Close("close");
        Pump();
        Assert.False(Has(w, "DiagnosticsReport"));
        w.Session.Document.MarkSaved();
        w.Close();
    }
}
