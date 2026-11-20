using Avalonia.Controls;
using DoodleNook.App.Canvas;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.App;

/// <summary>
/// Mission 008 — The Designer's Toolkit in the window: the text editor, rulers, adjustment layers, the drawing
/// assistants pop-over and the Layer and View menu entries for groups, masks, text, shapes and guides.
/// </summary>
public partial class MainWindow
{
    private TextEditorOverlay _textEditor = null!;
    private RulerBar _rulerTop = null!;
    private RulerBar _rulerLeft = null!;
    private RulerCorner _rulerCorner = null!;

    static MainWindow()
    {
        // Text layers are drawn with the computer's fonts.
        Document.TextRenderer ??= new SkiaTextRenderer();
    }

    internal TextEditorOverlay TextEditor => _textEditor;

    private void InitDesign()
    {
        _rulerTop = new RulerBar(this, horizontal: true) { IsVisible = false };
        _rulerLeft = new RulerBar(this, horizontal: false) { IsVisible = false };
        _rulerCorner = new RulerCorner(this) { IsVisible = false };
        CanvasHost.Children.Add(_rulerTop);
        CanvasHost.Children.Add(_rulerLeft);
        CanvasHost.Children.Add(_rulerCorner);
        _textEditor = new TextEditorOverlay(this);
        CanvasHost.Children.Add(_textEditor);
        InitMaskChip();

        Canvas.Input!.TextEditRequested += (layer, isNew) => _textEditor.Open(layer, isNew);
        Session.DocumentReplaced += () =>
        {
            _textEditor.Finish();
            _ = AnnounceMissingFontsAsync();
        };
        Session.StateChanged += () =>
        {
            if (_textEditor.IsOpen && Session.Tool is not (ToolKind.Text or ToolKind.Move)) _textEditor.Finish();
        };
    }

    // ================================================================== rulers and guides

    public bool RulersShown => WsState.ShowRulers && Workspace.Kind != WorkspaceKind.PureSketch && !UiHidden;

    internal void ApplyRulers()
    {
        if (_rulerTop is null) return;
        bool show = RulersShown;
        _rulerTop.IsVisible = _rulerLeft.IsVisible = _rulerCorner.IsVisible = show;
        UpdateOverlayPositions();
    }

    // ================================================================== the mask chip

    private Border _maskChip = null!;
    private TextBlock _maskChipText = null!;

    /// <summary>While painting goes to a mask, a small chip at the top of the canvas says so (and switches back).</summary>
    private void InitMaskChip()
    {
        _maskChipText = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12.5 };
        var back = UiKit.Button(null, "Paint on the layer", "Paint on the layer's pixels again", () => Session.SetEditingMask(false));
        back.Height = 26;
        back.FontSize = 12;
        _maskChip = new Border
        {
            Classes = { "float" },
            Name = "MaskChip",
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
            Padding = new Avalonia.Thickness(10, 3, 4, 3),
            CornerRadius = new Avalonia.CornerRadius(999),
            Child = UiKit.Row(8, new Controls.DnIcon { Kind = "mask", Foreground = new Avalonia.Media.SolidColorBrush(Controls.DnColors.Accent) }, _maskChipText, back),
        };
        CanvasHost.Children.Add(_maskChip);
        void Update()
        {
            var doc = Session.Document;
            bool on = doc.EditingMask;
            _maskChip.IsVisible = on;
            if (on) _maskChipText.Text = $"Painting on the mask of “{doc.ActiveLayer.Name}” · black hides, white shows";
            _maskChip.Margin = new Avalonia.Thickness(0, RulersShown ? 12 + RulerBar.Size : 12, 0, 0);
        }
        Document? watched = null;
        void Watch()
        {
            if (watched is not null) watched.ActiveLayerChanged -= Update;
            watched = Session.Document;
            watched.ActiveLayerChanged += Update;
            Update();
        }
        Session.DocumentReplaced += Watch;
        Session.StateChanged += Update;
        Watch();
    }

    public void SetShowRulers(bool on)
    {
        WsState.ShowRulers = on;
        ApplyRulers();
        if (on) ShowToast("Rulers · drag from a ruler to add a guide");
    }

    // ================================================================== fonts

    private async Task AnnounceMissingFontsAsync()
    {
        var missing = Session.Document.MissingFonts;
        if (missing.Count == 0) return;
        var lines = missing.Select(m => $"• “{m.Layer}” asks for {m.Requested} — shown in {m.Used}").Distinct().Take(12);
        await _dialogs.ShowMessageAsync("Some fonts aren't installed",
            "This picture uses fonts this computer doesn't have. The text stays editable and keeps asking for its font; until the font is installed, it is shown in another one:\n\n" +
            string.Join("\n", lines) + "\n\nInstall the font and open the picture again, or choose another font in the options bar.");
    }

    // ================================================================== adjustment layers

    /// <summary>Adds an adjustment layer and opens its settings (Cancel removes it again).</summary>
    public async Task NewAdjustmentLayerAsync(AdjustKind kind)
    {
        Session.EndStroke();
        Session.CommitTransform();
        var neutral = EditDialogs.Neutral(kind);
        var layer = Session.AddAdjustmentLayer(neutral);
        var doc = Session.Document;
        var result = await _dialogs.AskAdjustmentAsync(kind, a => doc.PreviewContent(layer, new AdjustmentContent(a ?? neutral)), null, layer: true);
        if (result is null)
        {
            doc.PreviewContent(layer, new AdjustmentContent(neutral));
            if (doc.History.NextUndoName == "Add adjustment layer" && ReferenceEquals(doc.ActiveLayer, layer)) doc.Undo();
            return;
        }
        // The "Add adjustment layer" step holds this same layer, so it adds it with these settings.
        doc.PreviewContent(layer, new AdjustmentContent(result));
        ShowToast($"{result.Name} layer · double-click it in the layers panel to change it");
    }

    /// <summary>Changes an adjustment layer's settings (one undo step).</summary>
    public async Task EditAdjustmentLayerAsync(Layer layer)
    {
        if (layer.Content is not AdjustmentContent from) return;
        var doc = Session.Document;
        var kind = EditDialogs.KindOf(from.Adjustment);
        var result = await _dialogs.AskAdjustmentAsync(kind, a => doc.PreviewContent(layer, a is null ? from : new AdjustmentContent(a)), from.Adjustment, layer: true);
        if (result is null)
        {
            doc.PreviewContent(layer, from);
            return;
        }
        doc.PreviewContent(layer, new AdjustmentContent(result));
        doc.CommitContent(layer, from, "Edit adjustment");
    }

    // ================================================================== objects

    /// <summary>Opens the text editor for the active text layer.</summary>
    public void EditActiveText()
    {
        if (Session.Document.ActiveLayer is { Kind: LayerKind.Text } layer) _textEditor.Open(layer, false);
    }

    /// <summary>Chooses a colour for part of the active text or shape (fill, outline, shadow…) with the colour dialog.</summary>
    public async Task ChooseObjectColourAsync(string what, ColorRgb initial, Func<LayerContent, ColorRgb, LayerContent> set)
    {
        if (Session.ActiveObject is null) return;
        var colour = await _dialogs.AskColourAsync(what, initial, "Use colour");
        if (colour is { } c) Session.EditObject(content => set(content, Session.Allowed(c)), what);
    }

    // ================================================================== assistants

    /// <summary>The drawing assistants pop-over (symmetry, perspective, snapping), at a control or at the pointer.</summary>
    public void ShowAssistants(Control? anchor = null)
    {
        var panel = new AssistantsPanel(this);
        var flyout = UiKit.PopFlyout(panel, anchor is null ? Avalonia.Controls.PlacementMode.Pointer : Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft);
        flyout.ShowAt(anchor ?? Canvas, showAtPointer: anchor is null);
    }

    // ================================================================== menu entries

    internal IEnumerable<MenuEntry> DesignLayerEntries()
    {
        var s = Session;
        var d = s.Document;
        var l = d.ActiveLayer;
        var group = new List<MenuEntry>
        {
            new("folder", "New group", null, s.NewGroup),
            new("folder", l.IsGroup ? "Group this group" : "Group layer", "Ctrl+G", s.GroupActiveLayer),
            new(null, "Ungroup", "Ctrl+Shift+G", s.Ungroup, l.IsGroup || l.ParentId != 0),
            new(null, "Move out of group", null, s.MoveOutOfGroup, l.ParentId != 0),
        };
        var mask = l.HasMask
            ? new List<MenuEntry>
            {
                new("mask", d.EditingMask ? "Paint on the layer" : "Paint on the mask", null, () => s.SetEditingMask(!d.EditingMask)),
                new(null, "Use mask", null, s.ToggleMaskEnabled, Checked: l.MaskEnabled),
                new("invert", "Invert mask", null, s.InvertMask),
                new("check", "Apply mask", null, s.ApplyMask, l.IsPaintable),
                new("trash", "Delete mask", null, s.DeleteMask),
            }
            : new List<MenuEntry>
            {
                new("mask", "Add mask", null, () => s.AddMask()),
                new("mask", "Mask from selection", null, () => s.AddMask(fromSelection: true), d.Selection is not null),
            };
        var adjust = new List<MenuEntry>
        {
            new("adjust", "Brightness / Contrast…", null, () => _ = NewAdjustmentLayerAsync(AdjustKind.BrightnessContrast)),
            new("adjust", "Hue / Saturation…", null, () => _ = NewAdjustmentLayerAsync(AdjustKind.HueSaturation)),
            new("adjust", "Levels…", null, () => _ = NewAdjustmentLayerAsync(AdjustKind.Levels)),
            new("adjust", "Colour balance…", null, () => _ = NewAdjustmentLayerAsync(AdjustKind.ColourBalance)),
        };
        var list = new List<MenuEntry>
        {
            MenuEntry.Separator,
            MenuEntry.Sub("folder", "Group", group),
            MenuEntry.Sub("mask", l.HasMask ? "Mask" : "Mask (add…)", mask),
            MenuEntry.Sub("adjust", "New adjustment layer", adjust),
        };
        if (l.Kind == LayerKind.Adjustment) list.Add(new("props", "Edit adjustment…", null, () => _ = EditAdjustmentLayerAsync(l)));
        if (l.IsObject)
        {
            var obj = new List<MenuEntry>();
            if (l.Kind == LayerKind.Text) obj.Add(new("text", "Edit text", null, EditActiveText));
            obj.Add(new("alignc", "Centre on the canvas", null, () =>
            {
                s.Align(AlignEdge.CenterX, false);
                s.Align(AlignEdge.CenterY, false);
            }));
            obj.Add(new("alignc", "Centre on the layer below", null, () =>
            {
                s.Align(AlignEdge.CenterX, true);
                s.Align(AlignEdge.CenterY, true);
            }, d.LayerBelow(l) is not null));
            obj.Add(new("image", "Convert to pixels", null, s.ConvertToPixels));
            list.Add(MenuEntry.Sub(l.Kind == LayerKind.Text ? "text" : "shape", l.Kind == LayerKind.Text ? "Text" : "Shape", obj));
        }
        return list;
    }

    internal IEnumerable<MenuEntry> DesignViewEntries()
    {
        var s = Session;
        var d = s.Document;
        return
        [
            new("ruler", "Rulers", "Ctrl+R", () => SetShowRulers(!WsState.ShowRulers), Checked: WsState.ShowRulers),
            MenuEntry.Sub("guides", "Guides", [
                new("guides", "Show guides", "Ctrl+;", () => s.SetShowGuides(!s.ShowGuides), Checked: s.ShowGuides),
                new("lock", "Lock guides", null, s.ToggleGuidesLocked, d.Guides.Count > 0 || d.GuidesLocked, d.GuidesLocked),
                new("trash", "Clear guides", null, s.ClearGuides, d.Guides.Count > 0 && !d.GuidesLocked),
            ]),
            new("magnet", "Snap", "Ctrl+Shift+;", () => s.SetAssistOptions(s.Assist with { Snap = !s.Assist.Snap }), Checked: s.Assist.Snap),
            new("assist", "Drawing assistants…", null, () => ShowAssistants()),
            MenuEntry.Separator,
        ];
    }
}
