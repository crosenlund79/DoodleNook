using DoodleNook.App.Canvas;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Editor;

/// <summary>What the Text tool gives new text.</summary>
public sealed record TextToolOptions(string Font = SkiaTextRenderer.DefaultFont, double Size = 48, bool Bold = false, bool Italic = false,
    TextAlign Align = TextAlign.Left);

/// <summary>
/// The Shapes tool's editable mode: shapes stay shapes (move, resize, turn, recolour them later) instead of being painted
/// into the layer. Fill uses the foreground colour, the outline the background colour.
/// </summary>
public sealed record DesignShapeOptions(bool Editable = false, ShapeType Kind = ShapeType.Rectangle, bool Fill = true, bool Outline = false,
    double OutlineWidth = 4, double CornerRadius = 16, int Sides = 6);

/// <summary>Drawing assistants: where symmetry is centred, snapping, and the perspective assistant's behaviour.</summary>
public sealed record AssistantOptions(
    double? OriginX = null,
    double? OriginY = null,
    int Spokes = 6,
    bool Snap = true,
    bool SnapGrid = false,
    double GridSize = 32,
    bool PerspectiveAssist = true,
    bool ShowPerspective = true)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public SymmetryPlacement Placement => new(OriginX, OriginY, Spokes);
}

/// <summary>Mission 008 — The Designer's Toolkit: text, editable shapes, groups, masks, adjustment layers and assistants.</summary>
public sealed partial class EditorSession
{
    public TextToolOptions Text { get; private set; } = new();
    public DesignShapeOptions DesignShape { get; private set; } = new();
    public AssistantOptions Assist { get; private set; } = new();

    /// <summary>Guides and rulers are shown (per picture window; saved with the app settings).</summary>
    public bool ShowGuides { get; private set; } = true;

    public void SetTextOptions(TextToolOptions o) { Text = o with { Size = Math.Clamp(o.Size, 1, 2000) }; Changed(); }
    public void SetDesignShapeOptions(DesignShapeOptions o) { DesignShape = o with { OutlineWidth = Math.Clamp(o.OutlineWidth, 0.5, 400), Sides = Math.Clamp(o.Sides, 3, 64) }; Changed(); }
    public void SetAssistOptions(AssistantOptions o) { Assist = o with { Spokes = Math.Clamp(o.Spokes, SymmetryPlacement.MinSpokes, SymmetryPlacement.MaxSpokes), GridSize = Math.Clamp(o.GridSize, 2, 1024) }; Changed(); }

    public void SetShowGuides(bool on)
    {
        ShowGuides = on;
        Changed();
    }

    /// <summary>The snapping targets for the current picture, or null when snapping is off.</summary>
    public SnapTargets? SnapTargetsFor(Layer? moving) =>
        Assist.Snap ? SnapTargets.For(Document, new SnapOptions(Guides: ShowGuides, Grid: Assist.SnapGrid ? Assist.GridSize : 0), moving) : null;

    // ================================================================== text and shapes

    /// <summary>The Shapes tool makes editable shapes: chosen in its options, or because an editable shape is the active
    /// layer (pixels can't be painted onto it). Pixel Art always paints pixels.</summary>
    public bool EditableShapesNow => !PixelMode && (DesignShape.Editable || ActiveObject is { Kind: LayerKind.Shape });

    /// <summary>The active layer when it is text or a shape.</summary>
    public Layer? ActiveObject => Document.ActiveLayer is { IsObject: true } l ? l : null;

    /// <summary>New text with the Text tool's settings and the foreground colour.</summary>
    public TextContent NewText(double x, double y, string text = "") =>
        new(text, Text.Font, Text.Size, Allowed(Foreground), Text.Bold, Text.Italic, Text.Align, x, y);

    public Layer AddText(TextContent content)
    {
        EndStroke();
        CommitTransform();
        RememberColor(content.Color);
        return Document.AddTextLayer(content);
    }

    /// <summary>An editable shape from a drag (Shift: square or circle; lines keep 15° steps with Shift).</summary>
    public ShapeContent ShapeFromDrag(double x0, double y0, double x1, double y1, bool constrain)
    {
        var o = DesignShape;
        if (o.Kind == ShapeType.Line)
        {
            var (ex, ey) = StraightLine.Constrain(x0, y0, x1, y1, constrain);
            return new ShapeContent(ShapeType.Line, x0, y0, ex - x0, ey - y0, null, Allowed(Foreground), Math.Max(1, o.OutlineWidth));
        }
        double w = x1 - x0, h = y1 - y0;
        if (constrain)
        {
            double s = Math.Max(Math.Abs(w), Math.Abs(h));
            w = Math.Sign(w == 0 ? 1 : w) * s;
            h = Math.Sign(h == 0 ? 1 : h) * s;
        }
        double l = Math.Min(x0, x0 + w), t = Math.Min(y0, y0 + h);
        bool fill = o.Fill || !o.Outline;
        return new ShapeContent(o.Kind, l, t, Math.Max(1, Math.Abs(w)), Math.Max(1, Math.Abs(h)),
            fill ? Allowed(Foreground) : null, o.Outline ? Allowed(Background) : null, o.OutlineWidth, 0, o.CornerRadius, o.Sides);
    }

    public Layer AddShape(ShapeContent content)
    {
        EndStroke();
        CommitTransform();
        if (content.Fill is { } f) RememberColor(f);
        return Document.AddShapeLayer(content);
    }

    /// <summary>Changes the active text or shape layer (one undo step).</summary>
    public bool EditObject(Func<LayerContent, LayerContent> change, string historyName)
    {
        if (ActiveObject is not { } layer) return false;
        if (layer.Locked)
        {
            Tell($"“{layer.Name}” is locked");
            return false;
        }
        return Document.SetContent(layer, change(layer.Content!), historyName);
    }

    public void ConvertToPixels()
    {
        CommitTransform();
        var layer = Document.ActiveLayer;
        if (!layer.IsObject)
        {
            Tell("Only text and shape layers can be turned into pixels");
            return;
        }
        Document.ConvertToPixels(layer);
        Tell($"“{layer.Name}” is now a pixel layer");
    }

    /// <summary>Lines the active text or shape up with the canvas or with the layer below it.</summary>
    public void Align(AlignEdge edge, bool toLayerBelow)
    {
        if (ActiveObject is not { } layer)
        {
            Tell("Choose a text or shape layer to align");
            return;
        }
        if (toLayerBelow)
        {
            if (Document.LayerBelow(layer) is not { } below || Document.ExtentOf(below) is not { } extent)
            {
                Tell("The layer below is empty");
                return;
            }
            Document.AlignObjectsTo([layer], edge, extent);
        }
        else Document.AlignObjects([layer], edge, toCanvas: true);
    }

    // ================================================================== groups

    public void GroupActiveLayer()
    {
        CommitTransform();
        Document.GroupLayers([Document.ActiveLayer]);
    }

    public void NewGroup()
    {
        CommitTransform();
        Document.AddGroup();
    }

    public void Ungroup()
    {
        CommitTransform();
        var layer = Document.ActiveLayer;
        if (layer.IsGroup) Document.Ungroup(layer);
        else if (Document.FindLayer(layer.ParentId) is { } group) Document.Ungroup(group);
        else Tell("This layer isn't in a group");
    }

    public void MoveOutOfGroup()
    {
        CommitTransform();
        if (!Document.MoveOutOfGroup(Document.ActiveLayer)) Tell("This layer isn't in a group");
    }

    // ================================================================== masks

    public void AddMask(bool fromSelection = false)
    {
        CommitTransform();
        var layer = Document.ActiveLayer;
        if (layer.HasMask)
        {
            Document.SetEditingMask(true);
            Tell("Painting on the mask: black hides, white shows");
            return;
        }
        if (fromSelection && Document.Selection is null)
        {
            Tell("Make a selection first");
            return;
        }
        Document.AddMask(layer, fromSelection);
        if (fromSelection) Document.SetSelection(null, "Deselect");
        Tell("Mask added: paint black to hide, white to show");
        Changed();
    }

    /// <summary>Chooses whether painting goes to the active layer's mask or its pixels.</summary>
    public void SetEditingMask(bool mask)
    {
        CommitTransform();
        Document.SetEditingMask(mask && Document.ActiveLayer.HasMask);
        Changed();
    }

    public void ToggleMaskEnabled()
    {
        var layer = Document.ActiveLayer;
        if (!layer.HasMask) return;
        SetLayerProperty(layer, p => p with { MaskEnabled = !p.MaskEnabled }, layer.MaskEnabled ? "Disable mask" : "Enable mask");
    }

    public void InvertMask() => Document.InvertMask(Document.ActiveLayer);

    public void ApplyMask()
    {
        CommitTransform();
        var layer = Document.ActiveLayer;
        if (!layer.IsPaintable)
        {
            Tell("Only a pixel layer's mask can be applied (delete it, or turn the layer into pixels first)");
            return;
        }
        Document.ApplyMask(layer);
        Changed();
    }

    public void DeleteMask()
    {
        Document.DeleteMask(Document.ActiveLayer);
        Changed();
    }

    // ================================================================== adjustment layers

    public Layer AddAdjustmentLayer(Adjustment adjustment)
    {
        EndStroke();
        CommitTransform();
        return Document.AddAdjustmentLayer(adjustment);
    }

    // ================================================================== guides

    public void AddGuide(bool vertical, double position)
    {
        if (Document.GuidesLocked) return;
        Document.SetGuides([.. Document.Guides, new Guide(vertical, Math.Round(position, 1))], "Add guide");
    }

    public void MoveGuide(int index, double position)
    {
        if (Document.GuidesLocked || index < 0 || index >= Document.Guides.Count) return;
        var list = Document.Guides.ToList();
        list[index] = list[index] with { Position = Math.Round(position, 1) };
        Document.SetGuides(list, "Move guide");
    }

    public void RemoveGuide(int index)
    {
        if (Document.GuidesLocked || index < 0 || index >= Document.Guides.Count) return;
        var list = Document.Guides.ToList();
        list.RemoveAt(index);
        Document.SetGuides(list, "Remove guide");
    }

    public void ClearGuides()
    {
        if (Document.GuidesLocked)
        {
            Tell("Guides are locked");
            return;
        }
        Document.SetGuides([], "Clear guides");
    }

    public void ToggleGuidesLocked()
    {
        Document.GuidesLocked = !Document.GuidesLocked;
        Changed();
    }

    /// <summary>Turns the perspective assistant off, or on with 1 or 2 vanishing points (keeping points already placed).</summary>
    public void SetPerspective(int points)
    {
        var current = Document.Perspective;
        var next = points == 0 ? current with { Points = 0 }
            : current.Points > 0 || current.V1X != 0 || current.V1Y != 0
                ? current with { Points = points, V2X = points == 2 && current.V2X == 0 && current.V2Y == 0 ? Document.Width * 0.9 : current.V2X, V2Y = points == 2 && current.V2Y == 0 ? current.V1Y : current.V2Y }
                : Perspective.Default(points, Document.Width, Document.Height);
        Document.SetPerspective(next);
        Changed();
    }

    /// <summary>A message about fonts that aren't installed (after opening a picture), or null.</summary>
    public string? MissingFontsMessage()
    {
        var missing = Document.MissingFonts;
        if (missing.Count == 0) return null;
        var fonts = missing.Select(m => $"“{m.Requested}”").Distinct().ToList();
        return fonts.Count == 1
            ? $"Font {fonts[0]} isn't installed: its text shows in {missing[0].Used} until it is"
            : $"Fonts {string.Join(", ", fonts)} aren't installed: their text shows in {missing[0].Used}";
    }
}
