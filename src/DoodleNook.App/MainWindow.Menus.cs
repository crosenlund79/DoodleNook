using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Operations;

namespace DoodleNook.App;

/// <summary>The menus. Every entry is a real command; entries that can't run right now are disabled.</summary>
public partial class MainWindow
{
    internal IReadOnlyList<string> MenuNames => Workspace.Kind switch
    {
        WorkspaceKind.PixelArt => ["File", "Edit", "Sprite", "Layer", "Select", "View", "Window", "Help"],
        WorkspaceKind.Animation => ["File", "Edit", "Image", "Animation", "Layer", "Select", "View", "Window", "Help"],
        _ => ["File", "Edit", "Image", "Layer", "Select", "View", "Window", "Help"],
    };

    internal IEnumerable<MenuEntry> MenuFor(string name) => name switch
    {
        "File" => FileMenu(),
        "Edit" => EditMenu(),
        "Image" or "Sprite" => ImageMenu(),
        "Animation" => AnimationMenu(),
        "Layer" => LayerMenu(),
        "Select" => SelectMenu(),
        "View" => ViewMenu(),
        "Window" => WindowMenu(),
        "Help" => HelpMenu(),
        _ => [],
    };

    private IEnumerable<MenuEntry> FileMenu() =>
    [
        new("new", "New picture…", "Ctrl+N", () => _ = NewAsync()),
        new("film", "New animation…", null, () => _ = NewAnimationAsync()),
        new("open", "Open…", "Ctrl+O", () => _ = OpenAsync()),
        MenuEntry.Separator,
        new("save", "Save", "Ctrl+S", () => _ = SaveAsync()),
        new("saveas", "Save as…", "Ctrl+Shift+S", () => _ = SaveAsAsync()),
        MenuEntry.Separator,
        new("film", "Export animation…", null, () => _ = ExportAnimationAsync(), Session.Document.IsAnimated),
        new("export", "Export as PNG…", null, () => _ = ExportAsync(ExportFormat.Png)),
        new("export", "Export as JPEG…", null, () => _ = ExportAsync(ExportFormat.Jpeg)),
        new("export", "Export as WebP…", null, () => _ = ExportAsync(ExportFormat.WebP)),
        new("place", "Place image as layer…", null, () => _ = PlaceImageAsync()),
        MenuEntry.Separator,
        new("settings", "Preferences…", null, () => _ = PreferencesAsync()),
        new("exit", "Exit", "Alt+F4", Close),
    ];

    private IEnumerable<MenuEntry> EditMenu()
    {
        var s = Session;
        var h = s.Document.History;
        bool sel = s.Document.Selection is { IsEmpty: false };
        return
        [
            new("undo", h.NextUndoName is { } u ? $"Undo {u.ToLowerInvariant()}" : "Undo", "Ctrl+Z", Undo, h.CanUndo),
            new("redo", h.NextRedoName is { } r ? $"Redo {r.ToLowerInvariant()}" : "Redo", "Ctrl+Shift+Z", Redo, h.CanRedo),
            new("history", "History…", null, () => SetWorkspaceSection(WorkspaceKind.Editing, "history")),
            MenuEntry.Separator,
            new(null, "Cut", "Ctrl+X", () => Copy(cut: true)),
            new("copy", "Copy", "Ctrl+C", () => Copy(cut: false)),
            new("paste", "Paste as new layer", "Ctrl+V", Paste, s.Clipboard is not null),
            MenuEntry.Separator,
            new("fill", sel ? "Fill selection with foreground" : "Fill layer with foreground", "Alt+Backspace", () => s.FillSelection()),
            new("fill", sel ? "Fill selection with background" : "Fill layer with background", "Ctrl+Backspace", () => s.FillSelection(background: true)),
            new("trash", sel ? "Clear selection" : "Clear layer", "Delete", ClearSelectionOrLayer),
            new("pattern", sel ? "Fill selection with pattern" : "Fill layer with pattern", null, () =>
            {
                if (!s.FillSelectionWithPattern()) ShowToast("Choose a pattern first: paint bucket › Pattern");
            }, s.PatternOptions.PatternId is not null),
            MenuEntry.Separator,
            new("stamp", sel ? "Save selection as stamp…" : "Save layer as stamp…", null, () => _ = SaveSelectionAsAssetAsync(PickerKind.Stamps)),
            new("pattern", sel ? "Save selection as pattern…" : "Save layer as pattern…", null, () => _ = SaveSelectionAsAssetAsync(PickerKind.Patterns)),
            MenuEntry.Separator,
            new("transform", "Free transform", "Ctrl+T", FreeTransform),
            new("flip", "Flip horizontal", null, () => FlipSelection(true)),
            new(null, "Flip vertical", null, () => FlipSelection(false)),
            new("rotate", "Rotate 90° clockwise", null, () => RotateSelection(90)),
            new("rotateccw", "Rotate 90° anticlockwise", null, () => RotateSelection(-90)),
        ];
    }

    private IEnumerable<MenuEntry> ImageMenu()
    {
        bool sel = Session.Document.Selection is { IsEmpty: false };
        var list = new List<MenuEntry>
        {
            new("expand", "Image size…", "Ctrl+Alt+I", () => _ = ImageSizeAsync()),
            new("fit", "Canvas size…", "Ctrl+Alt+C", () => _ = CanvasSizeAsync()),
            new("crop", "Crop to selection", null, CropToSelection, sel),
            MenuEntry.Separator,
            new("rotate", "Rotate picture 90° clockwise", null, () => RotateImage(1)),
            new("rotateccw", "Rotate picture 90° anticlockwise", null, () => RotateImage(-1)),
            new(null, "Rotate picture 180°", null, () => RotateImage(2)),
            new("flip", "Flip picture horizontal", null, () => FlipImage(true)),
            new(null, "Flip picture vertical", null, () => FlipImage(false)),
            MenuEntry.Separator,
            MenuEntry.Heading("Adjustments (selection or layer)"),
            new(null, "Brightness / Contrast…", null, () => _ = AdjustAsync(AdjustKind.BrightnessContrast)),
            new(null, "Hue / Saturation…", "Ctrl+U", () => _ = AdjustAsync(AdjustKind.HueSaturation)),
            new(null, "Grayscale", "Ctrl+Shift+U", () => ApplyAdjustment(new Desaturate())),
            new("invert", "Invert colours", "Ctrl+I", () => ApplyAdjustment(new Invert())),
            new(null, "Levels…", "Ctrl+L", () => _ = AdjustAsync(AdjustKind.Levels)),
            new(null, "Colour balance…", "Ctrl+B", () => _ = AdjustAsync(AdjustKind.ColourBalance)),
            new(null, "Blur…", null, () => _ = AdjustAsync(AdjustKind.Blur)),
            new(null, "Sharpen…", null, () => _ = AdjustAsync(AdjustKind.Sharpen)),
            new("adjust", "As an adjustment layer: Layer menu", null, null, false),
        };
        var doc = Session.Document;
        list.Add(MenuEntry.Separator);
        list.Add(MenuEntry.Heading(doc.PaletteLocked ? $"Palette (locked to “{doc.Palette.Name}”)" : "Palette"));
        list.Add(new("palette", "Remap to palette…", null, () => _ = RemapToPaletteAsync()));
        if (doc.PaletteLocked) list.Add(new("lock", "Unlock palette", null, UnlockPalette));
        else if (Workspace.Kind == WorkspaceKind.PixelArt || Session.PixelMode)
            list.Add(new("lock", $"Lock to “{doc.Palette.Name}”…", null, () => _ = LockToPaletteAsync(doc.Palette.Clone())));
        else list.Add(new("lock", "Lock to a palette…", null, () => _ = RemapToPaletteAsync(forceLock: true)));
        if (Workspace.Kind == WorkspaceKind.PixelArt)
        {
            list.Add(new("image", "Palette from the picture", null, () =>
            {
                Session.Document.Palette = Palette.FromImage(Session.Document.Flatten(), 64);
                Session.SetForeground(Session.Foreground);
                ShowToast($"Palette: {Session.Document.Palette.Colors.Count} colours from the picture");
            }, !doc.PaletteLocked));
        }
        return list;
    }

    private IEnumerable<MenuEntry> LayerMenu()
    {
        var s = Session;
        var d = s.Document;
        var l = d.ActiveLayer;
        int i = d.ActiveIndex;
        return
        [
            new("plus", "New layer", "Ctrl+Shift+N", () => s.AddLayer()),
            new("copy", "Duplicate layer", "Ctrl+J", s.DuplicateLayer),
            new("trash", "Delete layer", null, s.DeleteLayer, d.Layers.Count > 1),
            new("merge", "Merge down", "Ctrl+E", s.MergeDown, d.MergeDownProblem(l) is null),
            MenuEntry.Separator,
            new("chevu", "Move layer up", "Ctrl+]", () => s.MoveLayer(+1), i < d.Layers.Count - 1),
            new("chevd", "Move layer down", "Ctrl+[", () => s.MoveLayer(-1), i > 0),
            MenuEntry.Separator,
            new(l.Visible ? "eyeoff" : "eye", l.Visible ? "Hide layer" : "Show layer", null, () => s.SetLayerProperty(l, p => p with { Visible = !p.Visible })),
            new("lock", "Lock layer", null, () => s.SetLayerProperty(l, p => p with { Locked = !p.Locked }), Checked: l.Locked),
            new("alphalock", "Lock transparency", null, () => s.SetLayerProperty(l, p => p with { AlphaLocked = !p.AlphaLocked }), Checked: l.AlphaLocked),
            new("clip", "Clip to layer below", null, () => s.SetLayerProperty(l, p => p with { ClipToBelow = !p.ClipToBelow }), i > 0, l.ClipToBelow),
            MenuEntry.Separator,
            new("marquee", "Select layer pixels", null, SelectLayerPixels),
            new("trash", "Clear layer", null, ClearLayer),
            new("paper", d.PaperVisible && d.Paper is not null ? "Hide the paper (see transparency)" : "Show the paper",
                null, () => d.SetPaper(d.Paper ?? Engine.Imaging.ColorRgb.White, !(d.PaperVisible && d.Paper is not null))),
            .. DesignLayerEntries(),
        ];
    }

    private IEnumerable<MenuEntry> SelectMenu()
    {
        var s = Session;
        bool sel = s.Document.Selection is { IsEmpty: false };
        var list = new List<MenuEntry>
        {
            new("marquee", "Select all", "Ctrl+A", s.SelectAll),
            new("close", "Deselect", "Ctrl+D", s.Deselect, sel),
            new("invert", "Invert selection", "Ctrl+Shift+I", s.InvertSelection),
            new(null, "Select layer pixels", null, SelectLayerPixels),
            MenuEntry.Separator,
        };
        foreach (var t in new[] { ToolKind.RectSelect, ToolKind.EllipseSelect, ToolKind.Lasso, ToolKind.MagicWand })
        {
            if (!Workspace.Tools.Contains(t)) continue;
            var info = Workspaces.Tools[t];
            var tool = t;
            list.Add(new(info.Icon, info.Name, info.Shortcut, () => s.SelectTool(tool), Checked: s.Tool == t));
        }
        return list;
    }

    private IEnumerable<MenuEntry> ViewMenu()
    {
        bool pixel = Workspace.Kind == WorkspaceKind.PixelArt;
        var list = new List<MenuEntry>
        {
            new("plus", "Zoom in", "Ctrl++", () => Canvas.ZoomStep(+1)),
            new("minus", "Zoom out", "Ctrl+−", () => Canvas.ZoomStep(-1)),
            new("fit", "Fit on screen", "Ctrl+0", () => Canvas.FitToView()),
            new("zoom", "Actual pixels (100%)", "Ctrl+1", Canvas.ZoomToActualPixels),
            MenuEntry.Separator,
            new("rotateccw", "Rotate view 15° left", null, () => Canvas.RotateBy(-15)),
            new("rotate", "Rotate view 15° right", "hold R + drag", () => Canvas.RotateBy(15)),
            new(null, "Straighten view", null, Canvas.ResetRotation, Canvas.View.Rotation != 0),
            new("flip", "Flip view", null, ToggleFlipView, Checked: Canvas.View.FlipHorizontal),
            MenuEntry.Separator,
        };
        list.AddRange(DesignViewEntries());
        if (pixel)
        {
            list.Add(new("grid", "Pixel grid", "Ctrl+'", () => SetShowGrid(!WsState.ShowGrid), Checked: WsState.ShowGrid));
            list.Add(new("tiled", "Tiled view", null, () => SetTiled(!WsState.Tiled), Checked: WsState.Tiled));
            list.Add(new("image", "1:1 preview", null, () => SetPreview(!WsState.Preview), Checked: WsState.Preview));
            list.Add(MenuEntry.Separator);
        }
        list.Add(new("expand", "Hide interface", "Tab", () => SetUiHidden(true)));
        list.Add(new("fullscreen", "Full screen", "F11", ToggleFullScreen, Checked: WindowState == WindowState.FullScreen));
        return list;
    }

    private IEnumerable<MenuEntry> WindowMenu()
    {
        var list = new List<MenuEntry> { MenuEntry.Heading("Workspaces") };
        list.AddRange(WorkspaceEntries());
        var sections = DockSectionsHere;
        if (sections.Count > 0)
        {
            list.Add(MenuEntry.Separator);
            list.Add(MenuEntry.Heading("Panels"));
            foreach (var (id, title) in sections)
            {
                var sid = id;
                list.Add(new(null, title, null, () => ToggleDockSection(sid), Checked: !WsState.ClosedSections.Contains(id) && !WsState.DockCollapsed));
            }
            list.Add(new(WsState.DockCollapsed ? "chevl" : "chevr", WsState.DockCollapsed ? "Expand panels" : "Collapse panels to icons", null, ToggleDockCollapsed));
        }
        list.Add(MenuEntry.Separator);
        list.Add(MenuEntry.Heading("Creative tools"));
        list.Add(new("lab", "Brush Laboratory…", null, () => _ = OpenBrushLabAsync()));
        list.Add(new("palette", "Palettes…", null, () => _ = OpenPalettesAsync()));
        list.Add(new("board", "Reference board", null, () => SetReferenceBoardVisible(!WsState.IsShown(CustomOptions.ReferenceBoard, Workspace.Kind)),
            Workspace.Kind != WorkspaceKind.PureSketch, WsState.IsShown(CustomOptions.ReferenceBoard, Workspace.Kind)));
        list.Add(new("stamp", "Creative library…", null, () => _ = OpenLibraryAsync()));
        list.Add(MenuEntry.Separator);
        list.Add(new("settings", "Customize workspace…", null, () => _ = CustomizeWorkspaceAsync(), CustomOptions.For(Workspace.Kind).Any()));
        list.Add(new(null, "Reset workspace layout", null, ResetWorkspaceLayout));
        list.Add(MenuEntry.Separator);
        list.Add(new("pulse", "Pen diagnostics", "F12", ToggleDiagnostics, Checked: _diagnosticsPanel.IsVisible));
        return list;
    }

    private IEnumerable<MenuEntry> WorkspaceEntries()
    {
        int n = 1;
        foreach (var ws in Workspaces.All)
        {
            var kind = ws.Kind;
            yield return new(ws.Icon, ws.Name, $"Alt+{n++}", () => SetWorkspace(kind), Checked: ws.Kind == Workspace.Kind);
        }
    }

    private IEnumerable<MenuEntry> HelpMenu() =>
    [
        new("keyboard", "Keyboard shortcuts", "F1", () => _ = ShowShortcutsAsync()),
        MenuEntry.Separator,
        MenuEntry.Heading("Sample animations"),
        new("film", "Bouncing ball (hand-drawn)", null, () => _ = OpenSampleAsync(SampleAnimation.BouncingBall)),
        new("pixpencil", "Pixel slime (pixel art)", null, () => _ = OpenSampleAsync(SampleAnimation.PixelSlime)),
        MenuEntry.Separator,
        new("pulse", "Pen diagnostics", "F12", ToggleDiagnostics),
        new("info", "About DoodleNook", null, () => _ = ShowAboutAsync()),
    ];

    /// <summary>Quick Sketch's single menu (the bar's ☰).</summary>
    internal IEnumerable<MenuEntry> QuickMenu()
    {
        var list = new List<MenuEntry>
        {
            new("new", "New picture…", "Ctrl+N", () => _ = NewAsync()),
            new("open", "Open…", "Ctrl+O", () => _ = OpenAsync()),
            new("save", "Save", "Ctrl+S", () => _ = SaveAsync()),
            new("saveas", "Save as…", "Ctrl+Shift+S", () => _ = SaveAsAsync()),
            new("export", "Export as PNG…", null, () => _ = ExportAsync(ExportFormat.Png)),
            new("export", "Export as JPEG…", null, () => _ = ExportAsync(ExportFormat.Jpeg)),
            new("place", "Place image as layer…", null, () => _ = PlaceImageAsync()),
            MenuEntry.Separator,
            new("trash", "Clear layer", null, ClearLayer),
            new("lab", "Brush Laboratory…", null, () => _ = OpenBrushLabAsync()),
            new("board", "Reference board", null, () => SetReferenceBoardVisible(!WsState.IsShown(CustomOptions.ReferenceBoard, Workspace.Kind)),
                Checked: WsState.IsShown(CustomOptions.ReferenceBoard, Workspace.Kind)),
            new("settings", "Customize workspace…", null, () => _ = CustomizeWorkspaceAsync()),
            MenuEntry.Separator,
            new("fit", "Fit on screen", "Ctrl+0", () => Canvas.FitToView()),
            new("zoom", "Actual pixels (100%)", "Ctrl+1", Canvas.ZoomToActualPixels),
            new("flip", "Flip view", null, ToggleFlipView, Checked: Canvas.View.FlipHorizontal),
            new("fullscreen", "Full screen", "F11", ToggleFullScreen),
            new("expand", "Hide interface", "Tab", () => SetUiHidden(true)),
            MenuEntry.Separator,
            MenuEntry.Heading("Workspaces"),
        };
        list.AddRange(WorkspaceEntries());
        list.Add(MenuEntry.Separator);
        list.Add(new("pulse", "Pen diagnostics", "F12", ToggleDiagnostics));
        list.Add(new("keyboard", "Keyboard shortcuts", "F1", () => _ = ShowShortcutsAsync()));
        list.Add(new("settings", "Preferences…", null, () => _ = PreferencesAsync()));
        list.Add(new("info", "About DoodleNook", null, () => _ = ShowAboutAsync()));
        list.Add(MenuEntry.Separator);
        list.Add(new("exit", "Exit", "Alt+F4", Close));
        return list;
    }

    /// <summary>Pure Sketch's corner menu: workspaces, files and view, without leaving the canvas.</summary>
    internal IEnumerable<MenuEntry> PureMenu()
    {
        var list = new List<MenuEntry> { MenuEntry.Heading("Workspaces") };
        list.AddRange(WorkspaceEntries());
        list.Add(new(null, $"Back to {Workspaces.Get(_previousWorkspace == WorkspaceKind.PureSketch ? WorkspaceKind.QuickSketch : _previousWorkspace).Name}", "Tab", ReturnFromPureSketch));
        list.Add(MenuEntry.Separator);
        list.Add(new("radial", "Quick palette: right-click or the pen button", null, () => _radial.Open(Canvas.Bounds.Width / 2, Canvas.Bounds.Height / 2)));
        list.Add(new("new", "New picture…", "Ctrl+N", () => _ = NewAsync()));
        list.Add(new("open", "Open…", "Ctrl+O", () => _ = OpenAsync()));
        list.Add(new("save", "Save", "Ctrl+S", () => _ = SaveAsync()));
        list.Add(new("export", "Export as PNG…", null, () => _ = ExportAsync(ExportFormat.Png)));
        list.Add(MenuEntry.Separator);
        list.Add(new("fit", "Fit on screen", "Ctrl+0", () => Canvas.FitToView()));
        list.Add(new("rotateccw", "Rotate view 15° left", null, () => Canvas.RotateBy(-15)));
        list.Add(new("rotate", "Rotate view 15° right", "hold R + drag", () => Canvas.RotateBy(15)));
        list.Add(new(null, "Straighten view", null, Canvas.ResetRotation, Canvas.View.Rotation != 0));
        list.Add(new("flip", "Flip view", null, ToggleFlipView, Checked: Canvas.View.FlipHorizontal));
        list.Add(new("fullscreen", "Full screen", "F11", ToggleFullScreen));
        list.Add(MenuEntry.Separator);
        list.Add(new("exit", "Exit", "Alt+F4", Close));
        return list;
    }

    /// <summary>Opens a workspace with one of its dock sections showing (e.g. Edit › History).</summary>
    private void SetWorkspaceSection(WorkspaceKind kind, string section)
    {
        SetWorkspace(kind);
        WsState.ClosedSections.Remove(section);
        if (RightHost.Child is DockArea dock)
        {
            if (dock.Collapsed) dock.SetCollapsed(false);
            else dock.Refresh();
        }
    }

    internal Task ShowShortcutsAsync()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 3, ColumnSpacing = 16 };
        (string Keys, string What)[] rows =
        [
            ("Alt+1 … Alt+6", "Pure Sketch · Quick Sketch · Painting · Editing · Pixel Art · Animation Studio"),
            ("B / E / I / H", "Brush (pixel pencil) / Eraser / Eyedropper / Hand"),
            ("V / M / L / W / C", "Move / Rectangle select (Shift: ellipse) / Lasso / Magic wand / Crop"),
            ("G / Shift+G / U", "Paint bucket (colour or pattern) / Gradient / Shapes (pixel art: rectangle, Shift: ellipse)"),
            ("S", "Stamp tool · [ and ] change the stamp's size"),
            ("T", "Text tool (Painting, Editing) · click to type, click text to edit it"),
            ("Shift+drag · Shift+click", "Straight brush line · line from the end of the last stroke"),
            ("Ctrl+G / Ctrl+Shift+G", "Group layer / Ungroup"),
            ("Ctrl+R · Ctrl+; · Ctrl+Shift+;", "Rulers · show guides · snapping"),
            ("Ctrl+click (Move tool)", "Pick the text or shape under the pointer"),
            ("Ctrl+L / Ctrl+B", "Levels / Colour balance"),
            ("[ and ] · right-drag", "Smaller / larger brush"),
            ("Right-click · pen button", "Quick palette at the pen"),
            ("X / D", "Swap / default colours · Alt: eyedropper with any tool"),
            ("Ctrl+Z / Ctrl+Shift+Z", "Undo / Redo (also Ctrl+Y)"),
            ("Ctrl+T · Enter / Esc", "Free transform · apply / cancel"),
            ("Ctrl+A / D / Shift+I", "Select all / Deselect / Invert"),
            ("Ctrl+C / X / V", "Copy / Cut / Paste as new layer"),
            ("Delete · Alt/Ctrl+Backspace", "Clear · fill with foreground / background"),
            ("Ctrl+Shift+N / J / E", "New layer / Duplicate / Merge down"),
            ("Ctrl+[ / Ctrl+]", "Move layer down / up"),
            ("Ctrl+U / Shift+U / I", "Hue/Saturation / Grayscale / Invert"),
            ("Space-drag · middle-drag", "Pan"),
            ("Hold R and drag", "Rotate the view"),
            ("Wheel · Ctrl+0 / Ctrl+1", "Zoom · fit / actual pixels"),
            ("Ctrl+'", "Pixel grid (Pixel Art)"),
            ("Arrows (Move tool)", "Nudge 1 px (Shift: 10 px)"),
            ("Ctrl+N / O / S / Shift+S", "New / Open / Save / Save as"),
            ("Tab · F11 · F12", "Hide interface (Pure Sketch: go back) · full screen · pen diagnostics"),
            ("Animation Studio", ""),
            ("Enter · Esc", "Play / pause · stop playing (back to the start frame)"),
            (", and . · ← and →", "Previous / next frame (arrows move the selection with the Move tool)"),
            ("Home / End", "First / last frame"),
            ("N / Shift+N", "New blank frame / duplicate frame"),
            ("O", "Onion skin on or off"),
        ];
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = new TextBlock { Text = rows[i].Keys, FontWeight = FontWeight.SemiBold, FontSize = 12 };
            if (rows[i].What.Length == 0) k.Margin = new Thickness(0, 8, 0, 0); // a heading row
            var w = new TextBlock { Text = rows[i].What, Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(k, i);
            Grid.SetRow(w, i);
            Grid.SetColumn(w, 1);
            grid.Children.Add(k);
            grid.Children.Add(w);
        }
        return _dialogs.ShowAsync("Keyboard shortcuts", new ScrollViewer { Content = grid, MaxHeight = 520 }, [("ok", "Close", true)], "ok", "ok", 560);
    }

    private Task ShowAboutAsync() => _dialogs.ShowAsync("About DoodleNook",
        DialogHost.Text($"DoodleNook {Version} — Mission 008 “The Designer's Toolkit”.\nSix workspaces around one picture: Pure Sketch, Quick Sketch, Painting, Editing, Pixel Art and Animation Studio, " +
                        "with the Brush Laboratory, stamps, patterns, palettes, a reference board, editable text and shapes, groups, masks, adjustment layers and drawing assistants.\n\n" +
                        "Built with .NET, Avalonia and SkiaSharp (MIT licences). Fonts: Inter and Varela Round (SIL Open Font License). " +
                        "See THIRD-PARTY-NOTICES.txt next to DoodleNook.exe."),
        [("ok", "Close", true)], "ok", "ok");
}
