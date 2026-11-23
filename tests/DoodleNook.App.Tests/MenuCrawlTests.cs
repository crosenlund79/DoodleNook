using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>
/// Mission 017 — every menu command in every workspace, run in a fresh window at the Kamvas 13's 150 % (1280 × 720 at
/// 1.5). Whatever dialog a command opens must fit the window with its buttons in view and close with Esc, and no
/// command may throw. Each dialog is captured (out/screenshots/m017-dialogs) and listed in out/m017-menu-crawl.json.
/// </summary>
public sealed class MenuCrawlTests
{
    private sealed record Finding(string Workspace, string Menu, string Entry, string Outcome, string? Detail);

    private static string Shots
    {
        get
        {
            var d = Path.Combine(Screenshots.OutDir, "m017-dialogs");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static IEnumerable<(string Path, MenuEntry Entry)> Flatten(string prefix, IEnumerable<MenuEntry> entries)
    {
        foreach (var e in entries)
        {
            if (e.IsSeparator || e.IsHeading) continue;
            if (e.Children is { } children)
            {
                foreach (var c in Flatten($"{prefix} › {e.Label}", children)) yield return c;
                continue;
            }
            yield return ($"{prefix} › {e.Label}", e);
        }
    }

    /// <summary>The menus a workspace really shows: Pure Sketch's corner menu, Quick Sketch's ☰ menu, the menu bar elsewhere.</summary>
    internal static List<(string Name, IEnumerable<MenuEntry> Entries)> VisibleMenus(MainWindow w) => w.Workspace.Kind switch
    {
        WorkspaceKind.PureSketch => [("Corner menu", w.PureMenu())],
        WorkspaceKind.QuickSketch => [("☰", w.QuickMenu())],
        _ => w.MenuNames.Select(n => (n, w.MenuFor(n))).ToList(),
    };

    private static async Task<MainWindow> Fresh(WorkspaceKind kind)
    {
        var w = Open(1280, 720, kind);
        w.SetRenderScaling(1.5);
        Pump();
        if (kind == WorkspaceKind.Animation)
        {
            await w.OpenSampleAsync(SampleAnimation.BouncingBall);
        }
        else
        {
            w.Session.ReplaceDocument(new Document(640, 400, ColorRgb.White, "Background"), null);
            w.Session.AddLayer();
            w.Session.SetForeground(new ColorRgb(200, 90, 60));
            w.Session.ApplySelection(SelectionMask.Rectangle(640, 400, new DoodleNook.Engine.Imaging.PixelRect(150, 100, 250, 100)), "Select");
            w.Session.FillSelection(); // something drawn, and a selection, so selection commands are enabled too
        }
        Pump();
        return w;
    }

    private static string Name(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');

    [AvaloniaFact]
    public async Task Every_menu_command_opens_a_dialog_that_fits_at_150_percent_and_closes_with_Esc()
    {
        var findings = new List<Finding>();
        var seen = new HashSet<string>();
        foreach (var kind in Enum.GetValues<WorkspaceKind>())
        {
            var probe = await Fresh(kind);
            var items = VisibleMenus(probe).SelectMany(m => Flatten(m.Name, m.Entries).Select(x => (Menu: m.Name, x.Path, x.Entry))).ToList();
            probe.Session.Document.MarkSaved();
            probe.Close();

            foreach (var (menu, path, entry) in items)
            {
                if (entry.Icon == "exit" || !entry.Enabled || entry.Run is null) continue;
                if (!seen.Add(path)) continue; // the same command in another workspace
                var w = await Fresh(kind);
                // The entry from this window's own menus (commands capture the window they belong to).
                var mine = VisibleMenus(w).SelectMany(m => Flatten(m.Name, m.Entries)).FirstOrDefault(x => x.Path == path).Entry;
                if (mine?.Run is null)
                {
                    w.Session.Document.MarkSaved();
                    w.Close();
                    continue;
                }
                string outcome = "no dialog", detail = "";
                try
                {
                    mine.Run();
                    for (int i = 0; i < 20 && !w.Dialogs.IsOpen; i++)
                    {
                        Pump();
                        await Task.Delay(10);
                    }
                    if (w.Dialogs.IsOpen)
                    {
                        Pump();
                        outcome = "dialog fits";
                        var card = w.GetVisualDescendants().OfType<Control>().Last(c => c.Name == "DialogCard" && c.IsEffectivelyVisible);
                        var buttons = w.GetVisualDescendants().OfType<Control>().LastOrDefault(c => c.Name == "DialogButtons" && c.IsEffectivelyVisible);
                        var at = card.TranslatePoint(new Point(0, 0), w)!.Value;
                        if (at.X < 0 || at.Y < 0 || at.X + card.Bounds.Width > w.ClientSize.Width + 0.5 || at.Y + card.Bounds.Height > w.ClientSize.Height + 0.5)
                            (outcome, detail) = ("DOESN'T FIT", $"card {at.X:0},{at.Y:0} {card.Bounds.Width:0}×{card.Bounds.Height:0} in {w.ClientSize.Width:0}×{w.ClientSize.Height:0}");
                        else if (buttons is null)
                            (outcome, detail) = ("NO BUTTONS", "");
                        else
                        {
                            double bottom = buttons.TranslatePoint(new Point(0, buttons.Bounds.Height), card)!.Value.Y;
                            if (bottom > card.Bounds.Height + 0.5) (outcome, detail) = ("BUTTONS CUT OFF", $"{bottom:0} of {card.Bounds.Height:0}");
                        }
                        // Single-line labels whose text is wider than the room they were given (Mission 017: "Magenta ↔ Gree").
                        if (outcome == "dialog fits")
                        {
                            var cut = card.GetVisualDescendants().OfType<TextBlock>()
                                .Where(t => t.IsEffectivelyVisible && t.Text is { Length: > 0 } && t.TextWrapping == Avalonia.Media.TextWrapping.NoWrap
                                            && t.TextTrimming == Avalonia.Media.TextTrimming.None && t.Bounds.Width > 0)
                                .Where(t =>
                                {
                                    var probeText = new TextBlock { Text = t.Text, FontSize = t.FontSize, FontFamily = t.FontFamily, FontWeight = t.FontWeight, FontStyle = t.FontStyle };
                                    probeText.Measure(Size.Infinity);
                                    return probeText.DesiredSize.Width > t.Bounds.Width + 1;
                                })
                                .Select(t => t.Text).ToList();
                            if (cut.Count > 0) (outcome, detail) = ("TEXT CUT", string.Join(" · ", cut));
                        }
                        using (var shot = w.CaptureRenderedFrame()!)
                            shot.Save(Path.Combine(Shots, $"{kind}-{Name(path)}.png"), new PngBitmapEncoderOptions());
                        Key(w, Avalonia.Input.Key.Escape);
                        for (int i = 0; i < 20 && w.Dialogs.IsOpen; i++)
                        {
                            Pump();
                            await Task.Delay(10);
                        }
                        if (w.Dialogs.IsOpen)
                        {
                            (outcome, detail) = ("ESC DOESN'T CLOSE", detail);
                            w.Dialogs.Close(null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    (outcome, detail) = ("THREW", $"{ex.GetType().Name}: {ex.Message}");
                }
                findings.Add(new Finding(kind.ToString(), menu, path, outcome, detail.Length > 0 ? detail : null));
                if (w.Dialogs.IsOpen) w.Dialogs.Close(null);
                if (w.StorybookShown) w.CloseStorybook();
                w.Session.Document.MarkSaved();
                w.Close();
            }
        }
        var outPath = Path.Combine(Path.GetDirectoryName(Screenshots.OutDir)!, "m017-menu-crawl.json");
        File.WriteAllText(outPath, JsonSerializer.Serialize(findings, new JsonSerializerOptions { WriteIndented = true }));
        var problems = findings.Where(f => f.Outcome is not ("dialog fits" or "no dialog")).ToList();
        Assert.True(findings.Count(f => f.Outcome == "dialog fits") >= 20, $"only {findings.Count(f => f.Outcome == "dialog fits")} dialogs reached");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Select(p => $"{p.Workspace} · {p.Entry}: {p.Outcome} {p.Detail}")));
    }

    /// <summary>What a command visibly did: the dialog it opened, the layers, the last undo step, the selection, the tool,
    /// the zoom, the workspace and the interface.</summary>
    private static async Task<string> Effect(MainWindow w)
    {
        for (int i = 0; i < 15 && !w.Dialogs.IsOpen; i++)
        {
            Pump();
            await Task.Delay(5);
        }
        Pump();
        var doc = w.Session.Document;
        string dialog = w.Dialogs.IsOpen
            ? w.GetVisualDescendants().OfType<Control>().Last(c => c.Name == "DialogCard").GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text is { Length: > 0 })?.Text ?? "?"
            : "-";
        return $"dialog {dialog} · layers {doc.Layers.Count} · active {doc.Layers.ToList().IndexOf(doc.ActiveLayer)} · undo {doc.History.NextUndoName ?? "-"} · " +
               $"selection {doc.Selection?.Bounds.ToString() ?? "-"} · tool {w.Session.Tool} · zoom {w.Canvas.View.Scale:0.###} · rotation {w.Canvas.View.Rotation:0.#} · " +
               $"workspace {w.Workspace.Kind} · ui hidden {w.UiHidden} · state {w.WindowState} · storybook {w.StorybookShown}";
    }

    /// <summary>Parses a menu's shortcut label ("Ctrl+Shift+S", "Ctrl+]", "F11", "Tab"); null for gestures ("hold R + drag").</summary>
    internal static (Avalonia.Input.Key Key, Avalonia.Input.KeyModifiers Mods, string? Symbol)? ParseShortcut(string label)
    {
        var parts = label.Replace("Ctrl++", "Ctrl+Plus").Replace("Ctrl+−", "Ctrl+Minus").Split('+');
        var mods = Avalonia.Input.KeyModifiers.None;
        foreach (var m in parts[..^1])
            mods |= m switch
            {
                "Ctrl" => Avalonia.Input.KeyModifiers.Control,
                "Shift" => Avalonia.Input.KeyModifiers.Shift,
                "Alt" => Avalonia.Input.KeyModifiers.Alt,
                _ => (Avalonia.Input.KeyModifiers)(-1),
            };
        if ((int)mods < 0) return null;
        string k = parts[^1];
        (Avalonia.Input.Key, string?)? key = k switch
        {
            { Length: 1 } when char.IsLetter(k[0]) => (Enum.Parse<Avalonia.Input.Key>(k.ToUpperInvariant()), k.ToLowerInvariant()),
            { Length: 1 } when char.IsDigit(k[0]) => (Enum.Parse<Avalonia.Input.Key>("D" + k), k),
            "[" => (Avalonia.Input.Key.OemOpenBrackets, "["),
            "]" => (Avalonia.Input.Key.OemCloseBrackets, "]"),
            "'" => (Avalonia.Input.Key.OemQuotes, "'"),
            ";" => (Avalonia.Input.Key.OemSemicolon, ";"),
            "Plus" => (Avalonia.Input.Key.OemPlus, "+"),
            "Minus" => (Avalonia.Input.Key.OemMinus, "-"),
            "Tab" => (Avalonia.Input.Key.Tab, null),
            "Delete" => (Avalonia.Input.Key.Delete, null),
            "Enter" => (Avalonia.Input.Key.Enter, null),
            "Esc" => (Avalonia.Input.Key.Escape, null),
            _ when k.Length >= 2 && k[0] == 'F' && int.TryParse(k[1..], out _) => (Enum.Parse<Avalonia.Input.Key>(k), null),
            _ => null,
        };
        return key is { } kk ? (kk.Item1, mods, kk.Item2) : null;
    }

    /// <summary>Mission 017: every shortcut a menu shows is one the window really answers to (in a fresh window of the
    /// workspace whose menu shows it, with the same picture and selection the dialog crawl uses).</summary>
    [AvaloniaFact]
    public async Task Every_shortcut_a_menu_shows_is_answered_by_the_window()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>();
        int checkedCount = 0;
        foreach (var kind in Enum.GetValues<WorkspaceKind>())
        {
            var probe = await Fresh(kind);
            var items = VisibleMenus(probe).SelectMany(m => Flatten(m.Name, m.Entries)).Where(x => x.Entry.Shortcut is { Length: > 0 } && x.Entry.Enabled).ToList();
            probe.Session.Document.MarkSaved();
            probe.Close();
            foreach (var (path, entry) in items)
            {
                if (entry.Shortcut == "Alt+F4" || ParseShortcut(entry.Shortcut!) is not { } sc) continue; // Alt+F4 is Windows' own
                if (!seen.Add($"{kind}|{entry.Shortcut}|{entry.Label}")) continue;
                // The same command by menu in one fresh window and by key in another: the same thing must happen.
                var byMenu = await Fresh(kind);
                var menuEntry = VisibleMenus(byMenu).SelectMany(m => Flatten(m.Name, m.Entries)).First(x => x.Path == path).Entry;
                menuEntry.Run!();
                var expected = await Effect(byMenu);
                var w = await Fresh(kind);
                bool handled;
                try
                {
                    handled = w.HandleKey(sc.Key, sc.Mods, sc.Symbol);
                }
                catch (Exception ex)
                {
                    handled = false;
                    problems.Add($"{kind} · {path} ({entry.Shortcut}): threw {ex.GetType().Name}: {ex.Message}");
                }
                var actual = await Effect(w);
                checkedCount++;
                if (!handled) problems.Add($"{kind} · {path}: the menu says {entry.Shortcut}, but the window doesn't answer to it");
                else if (actual != expected) problems.Add($"{kind} · {path} ({entry.Shortcut}): the menu does [{expected}], the key does [{actual}]");
                foreach (var x in new[] { byMenu, w })
                {
                    if (x.Dialogs.IsOpen) x.Dialogs.Close(null);
                    if (x.StorybookShown) x.CloseStorybook();
                    x.Session.Document.MarkSaved();
                    x.Close();
                }
            }
        }
        Assert.True(checkedCount > 40, $"only {checkedCount} shortcuts checked");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Mission 017: every button that shows only an icon explains itself with a tooltip, in every workspace.</summary>
    [AvaloniaFact]
    public async Task Every_icon_only_button_has_a_tooltip()
    {
        var missing = new List<string>();
        foreach (var kind in Enum.GetValues<WorkspaceKind>())
        {
            var w = await Fresh(kind);
            foreach (var b in w.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Cast<ContentControl>()
                         .Concat(w.GetVisualDescendants().OfType<Button>()).Distinct())
            {
                if (!b.IsEffectivelyVisible || b.Bounds.Width < 2) continue;
                if (b.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.ScrollBar>().Any()) continue; // a scroll bar's own arrows
                bool hasText = b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && t.Text!.Trim().Length > 1);
                if (hasText || ToolTip.GetTip(b) is not null) continue;
                var where = string.Join(" › ", b.GetVisualAncestors().OfType<Control>().Where(c => !string.IsNullOrEmpty(c.Name)).Select(c => c.Name).Take(3).Reverse());
                missing.Add($"{kind}: {b.GetType().Name} {b.Name ?? "(no name)"} in {where} at {b.TranslatePoint(new Point(0, 0), w)}");
            }
            w.Session.Document.MarkSaved();
            w.Close();
        }
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    /// <summary>Help › Keyboard shortcuts (F1) is written by hand: each key it names, pressed in the window, does what
    /// the list says (tools, workspaces, colours, frames, onion skin).</summary>
    [AvaloniaFact]
    public async Task Every_key_the_F1_list_names_does_what_it_says()
    {
        var problems = new List<string>();
        int checkedKeys = 0;
        var worked = new HashSet<string>();
        var rows = new HashSet<string>();
        // A tool key picks its tool where the workspace shows that tool (the F1 list says so) and leaves the tool alone
        // elsewhere; every key must work in at least one workspace.
        void Tool(MainWindow w, Avalonia.Input.Key key, bool shift, DoodleNook.App.Editor.ToolKind want, string row)
        {
            var before = want == DoodleNook.App.Editor.ToolKind.Hand ? DoodleNook.App.Editor.ToolKind.Eyedropper : DoodleNook.App.Editor.ToolKind.Hand;
            w.Session.SelectTool(before);
            Key(w, key, shift ? Avalonia.Input.RawInputModifiers.Shift : Avalonia.Input.RawInputModifiers.None);
            checkedKeys++;
            rows.Add(row);
            bool shown = w.CurrentTools.Contains(Workspaces.GroupOf(want));
            if (shown && w.Session.Tool != want) problems.Add($"{w.Workspace.Kind}: {row} gave {w.Session.Tool}, the F1 list says {want}");
            if (!shown && w.Session.Tool != before) problems.Add($"{w.Workspace.Kind}: {row} picked {w.Session.Tool}, which this workspace doesn't show");
            if (shown && w.Session.Tool == want) worked.Add(row);
        }
        var known = MainWindow.ShortcutRows.Select(r => r.Keys + " " + r.What).ToList();
        void Listed(string keys) { if (!known.Any(k => k.Contains(keys))) problems.Add($"not in the F1 list any more: {keys}"); }

        // Tools (Painting and Editing; the pixel tools in Pixel Art).
        foreach (var kind in new[] { WorkspaceKind.PureSketch, WorkspaceKind.QuickSketch, WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.Animation })
        {
            var w = await Fresh(kind);
            Listed("B / E / I / H");
            Tool(w, Avalonia.Input.Key.B, false, DoodleNook.App.Editor.ToolKind.Brush, "B");
            Tool(w, Avalonia.Input.Key.E, false, DoodleNook.App.Editor.ToolKind.Eraser, "E");
            Tool(w, Avalonia.Input.Key.I, false, DoodleNook.App.Editor.ToolKind.Eyedropper, "I");
            Tool(w, Avalonia.Input.Key.H, false, DoodleNook.App.Editor.ToolKind.Hand, "H");
            Listed("V / M / L / W / C");
            Tool(w, Avalonia.Input.Key.V, false, DoodleNook.App.Editor.ToolKind.Move, "V");
            Tool(w, Avalonia.Input.Key.M, false, DoodleNook.App.Editor.ToolKind.RectSelect, "M");
            Tool(w, Avalonia.Input.Key.M, true, DoodleNook.App.Editor.ToolKind.EllipseSelect, "Shift+M");
            Tool(w, Avalonia.Input.Key.L, false, DoodleNook.App.Editor.ToolKind.Lasso, "L");
            Tool(w, Avalonia.Input.Key.W, false, DoodleNook.App.Editor.ToolKind.MagicWand, "W");
            Tool(w, Avalonia.Input.Key.C, false, DoodleNook.App.Editor.ToolKind.Crop, "C");
            Listed("G / Shift+G / U");
            Tool(w, Avalonia.Input.Key.G, false, DoodleNook.App.Editor.ToolKind.Fill, "G");
            Tool(w, Avalonia.Input.Key.G, true, DoodleNook.App.Editor.ToolKind.Gradient, "Shift+G");
            Tool(w, Avalonia.Input.Key.U, false, DoodleNook.App.Editor.ToolKind.Shape, "U");
            Tool(w, Avalonia.Input.Key.S, false, DoodleNook.App.Editor.ToolKind.Stamp, "S");
            Tool(w, Avalonia.Input.Key.T, false, DoodleNook.App.Editor.ToolKind.Text, "T");
            Listed("Shift+B / Shift+S / J");
            Tool(w, Avalonia.Input.Key.B, true, DoodleNook.App.Editor.ToolKind.Smudge, "Shift+B");
            Tool(w, Avalonia.Input.Key.S, true, DoodleNook.App.Editor.ToolKind.Clone, "Shift+S");
            Tool(w, Avalonia.Input.Key.J, false, DoodleNook.App.Editor.ToolKind.Repair, "J");

            // X / D: swap / default colours.
            var (fg, bg) = (w.Session.Foreground, w.Session.Background);
            Key(w, Avalonia.Input.Key.X);
            if ((w.Session.Foreground, w.Session.Background) != (bg, fg)) problems.Add($"{kind}: X didn't swap the colours");
            Key(w, Avalonia.Input.Key.D);
            if ((w.Session.Foreground, w.Session.Background) != (DoodleNook.App.Editor.EditorSession.DefaultForeground, DoodleNook.App.Editor.EditorSession.DefaultBackground))
                problems.Add($"{kind}: D didn't restore the default colours");
            checkedKeys += 2;
            w.Session.Document.MarkSaved();
            w.Close();
        }
        {
            var w = await Fresh(WorkspaceKind.PixelArt);
            Tool(w, Avalonia.Input.Key.B, false, DoodleNook.App.Editor.ToolKind.PixelPencil, "B (pixel pencil)");
            Tool(w, Avalonia.Input.Key.E, false, DoodleNook.App.Editor.ToolKind.PixelEraser, "E");
            Tool(w, Avalonia.Input.Key.U, false, DoodleNook.App.Editor.ToolKind.PixelRect, "U (pixel art: rectangle)");
            Tool(w, Avalonia.Input.Key.U, true, DoodleNook.App.Editor.ToolKind.PixelEllipse, "Shift+U (pixel art: ellipse)");
            Tool(w, Avalonia.Input.Key.G, false, DoodleNook.App.Editor.ToolKind.Fill, "G");

            // Alt+1 … Alt+6: the six workspaces, in the list's order.
            Listed("Alt+1 … Alt+6");
            var order = new[] { WorkspaceKind.PureSketch, WorkspaceKind.QuickSketch, WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.PixelArt, WorkspaceKind.Animation };
            for (int i = 0; i < 6; i++)
            {
                Key(w, Avalonia.Input.Key.D1 + i, Avalonia.Input.RawInputModifiers.Alt);
                checkedKeys++;
                if (w.Workspace.Kind != order[i]) problems.Add($"Alt+{i + 1} gave {w.Workspace.Kind}, the F1 list says {order[i]}");
                if (w.Workspace.Kind == WorkspaceKind.PureSketch) Key(w, Avalonia.Input.Key.Tab); // Pure Sketch: Tab goes back
            }
            w.Session.Document.MarkSaved();
            w.Close();
        }

        // Animation Studio: frames and onion skin.
        {
            var w = await Fresh(WorkspaceKind.Animation);
            var doc = w.Session.Document;
            int frames = doc.FrameCount;
            void Frame(Avalonia.Input.Key key, int want, string row, Avalonia.Input.RawInputModifiers mods = Avalonia.Input.RawInputModifiers.None, string? symbol = null)
            {
                Key(w, key, mods, symbol);
                checkedKeys++;
                if (w.Session.Document.CurrentFrame != want) problems.Add($"Animation: {row} gave frame {w.Session.Document.CurrentFrame + 1}, expected {want + 1}");
            }
            Frame(Avalonia.Input.Key.End, frames - 1, "End");
            Frame(Avalonia.Input.Key.Home, 0, "Home");
            Frame(Avalonia.Input.Key.OemPeriod, 1, ". (next frame)", symbol: ".");
            Frame(Avalonia.Input.Key.OemComma, 0, ", (previous frame)", symbol: ",");
            Frame(Avalonia.Input.Key.Right, 1, "→");
            Frame(Avalonia.Input.Key.Left, 0, "←");
            Key(w, Avalonia.Input.Key.N);
            if (doc.FrameCount != frames + 1) problems.Add("Animation: N didn't add a frame");
            Key(w, Avalonia.Input.Key.N, Avalonia.Input.RawInputModifiers.Shift);
            if (doc.FrameCount != frames + 2) problems.Add("Animation: Shift+N didn't duplicate the frame");
            bool onion = w.Session.Onion.Enabled;
            Key(w, Avalonia.Input.Key.O);
            if (w.Session.Onion.Enabled == onion) problems.Add("Animation: O didn't turn the onion skin " + (onion ? "off" : "on"));
            checkedKeys += 3;
            doc.MarkSaved();
            w.Close();
        }
        foreach (var row in rows.Where(r => !worked.Contains(r))) problems.Add($"{row} didn't pick its tool in any workspace");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedKeys >= 50, $"only {checkedKeys} keys checked");
    }

    /// <summary>Every menu entry fits its menu: no label runs past the entry, into its shortcut or under the ▸ of a
    /// sub-menu (Mission 017: menus were a fixed 270 px, and long entries such as "Fill layer with foreground" or
    /// "Behind transparent areas: Checkerboard" ran over).</summary>
    [AvaloniaFact]
    public async Task Every_menu_entry_fits_its_menu()
    {
        var problems = new List<string>();
        int entries = 0;
        void Check(string where, IEnumerable<MenuEntry> list)
        {
            var items = list.ToList();
            var content = Menus.Build(items, () => { });
            var host = new Window { Content = new Border { Padding = new Thickness(10), Child = content }, SizeToContent = SizeToContent.WidthAndHeight };
            host.Show();
            Pump();
            foreach (var b in content.GetVisualDescendants().OfType<Button>())
            {
                entries++;
                var texts = b.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text))
                    .Select(t => (t.Text, X: t.TranslatePoint(new Point(0, 0), b)!.Value.X, W: t.DesiredSize.Width - t.Margin.Left - t.Margin.Right)).OrderBy(t => t.X).ToList();
                double inner = b.Bounds.Width - b.Padding.Right + 0.5;
                foreach (var t in texts.Where(t => t.X + t.W > inner)) problems.Add($"{where}: “{t.Text}” runs past its entry");
                for (int i = 1; i < texts.Count; i++)
                    if (texts[i - 1].X + texts[i - 1].W > texts[i].X + 0.5) problems.Add($"{where}: “{texts[i - 1].Text}” runs into “{texts[i].Text}”");
            }
            host.Close();
            foreach (var e in items.Where(e => e.Children is not null)) Check(where + " › " + e.Label, e.Children!);
        }
        foreach (var kind in new[] { WorkspaceKind.Painting, WorkspaceKind.Editing, WorkspaceKind.PixelArt, WorkspaceKind.Animation })
        {
            var w = await Fresh(kind);
            foreach (var name in w.MenuNames) Check($"{kind} › {name}", w.MenuFor(name));
            if (kind == WorkspaceKind.Animation) Check("Timeline ⋯", w.TimelineMoreMenu());
            if (kind == WorkspaceKind.PixelArt) Check("Palette lock", w.PaletteLockEntries());
            w.Session.Document.MarkSaved();
            w.Close();
        }
        {
            var w = await Fresh(WorkspaceKind.QuickSketch);
            Check("Quick Sketch ☰", w.QuickMenu());
            w.Session.Document.MarkSaved();
            w.Close();
        }
        {
            var w = await Fresh(WorkspaceKind.PureSketch);
            Check("Pure Sketch corner menu", w.PureMenu());
            w.Session.Document.MarkSaved();
            w.Close();
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
        Assert.True(entries > 150, $"only {entries} entries checked");
    }
}
