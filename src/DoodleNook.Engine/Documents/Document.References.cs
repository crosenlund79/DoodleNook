using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.References;

namespace DoodleNook.Engine.Documents;

/// <summary>Mission 013: reference cycles from the library, added to an animation without touching what is there.</summary>
public partial class Document
{
    /// <summary>
    /// Adds a layer holding a reference cycle's drawings, one cell per frame of <paramref name="track"/>, as one undo
    /// step. A still picture becomes an animation first. If the track is longer than the animation, frames are added at
    /// the end and every other layer holds its last drawing there; nothing existing is removed or changed. The new layer
    /// goes just below <paramref name="below"/> (the layer being drawn on; at the bottom when null), in its group, and
    /// the active layer stays the one you were drawing on.
    /// </summary>
    public Layer AddReferenceCycle(LayerProperties properties, ReferenceCycleSettings settings, IReadOnlyList<Cel?> track, Layer? below = null)
    {
        if (track.Count == 0) throw new ArgumentException("A reference cycle needs at least one frame.", nameof(track));
        var start = BeginStructure();
        var actions = new List<IHistoryAction>();
        if (start.Conversion is { } conversion) actions.Add(conversion);
        if (GrowTo(track.Count)) actions.Add(new AnimationStructureAction("Add frames", start.Before, CaptureStructure()));

        foreach (var cel in track.OfType<Cel>().Distinct()) ConformToPalette(cel.Image, cel.ContentBounds);
        var anchor = below is not null && _layers.Contains(below) && !below.IsGroup ? below : null;
        int at = anchor is null ? _layers.Count : IndexOf(anchor);
        var layer = NewLayer(properties with { ParentId = anchor?.ParentId ?? 0 }, new RasterImage(Width, Height));
        layer.Cells = Padded(track);
        layer.ReferenceCycle = settings;
        InsertLayerInternal(layer, at, makeActive: false);
        actions.Add(new AddLayerAction(properties.Name, layer, at, makeActive: false));
        Commit(new CompositeAction("Insert reference cycle", actions));
        ShowFrameInternal(_animation!.CurrentFrame);
        AnimationChanged?.Invoke();
        LayersChanged?.Invoke();
        return layer;
    }

    /// <summary>Replaces a reference layer's drawings and settings (as one undo step), adding frames if the new track is
    /// longer. The name changes to <paramref name="name"/> unless the artist renamed the layer.</summary>
    public void UpdateReferenceCycle(Layer layer, ReferenceCycleSettings settings, IReadOnlyList<Cel?> track, string name)
    {
        if (!_layers.Contains(layer) || !layer.IsTrack) throw new ArgumentException("Not an animation layer of this picture.", nameof(layer));
        var start = BeginStructure();
        var oldSettings = layer.ReferenceCycle;
        GrowTo(track.Count);
        foreach (var cel in track.OfType<Cel>().Distinct()) ConformToPalette(cel.Image, cel.ContentBounds);
        layer.Cells = Padded(track);
        layer.ReferenceCycle = settings;
        var actions = new List<IHistoryAction>();
        if (start.Conversion is { } conversion) actions.Add(conversion);
        actions.Add(new AnimationStructureAction("Adjust reference cycle", start.Before, CaptureStructure()));
        actions.Add(new ReferenceSettingsAction(layer, oldSettings, settings));
        string? autoName = oldSettings is not null && ReferenceLibrary.Find(oldSettings.CycleId) is { } old ? ReferenceInsert.LayerName(old, oldSettings.View) : null;
        if (name != layer.Name && (autoName is null || layer.Name == autoName))
        {
            var before = layer.Properties;
            SetLayerPropertiesInternal(layer.Id, before with { Name = name });
            actions.Add(new LayerPropertiesAction("Rename layer", layer.Id, before, layer.Properties));
        }
        Commit(new CompositeAction("Adjust reference cycle", actions));
        ShowFrameInternal(_animation!.CurrentFrame);
        AnimationChanged?.Invoke();
        LayersChanged?.Invoke();
    }

    /// <summary>Makes the animation at least <paramref name="frames"/> long; added frames hold each layer's last drawing.</summary>
    private bool GrowTo(int frames)
    {
        var a = _animation!;
        int missing = frames - a.FrameCount;
        if (missing <= 0) return false;
        foreach (var l in _layers.Where(l => l.IsTrack))
        {
            var cells = l.Cells!;
            var last = cells.Count > 0 ? cells[^1] : null;
            for (int i = 0; i < missing; i++) cells.Add(last);
        }
        a.Durations.AddRange(Enumerable.Repeat<int?>(null, missing));
        return true;
    }

    private List<Cel?> Padded(IReadOnlyList<Cel?> track)
    {
        var cells = track.Take(_animation!.FrameCount).ToList();
        while (cells.Count < _animation.FrameCount) cells.Add(null);
        return cells;
    }
}

/// <summary>A reference layer's cycle settings changed (Adjust reference cycle).</summary>
internal sealed class ReferenceSettingsAction(Layer layer, ReferenceCycleSettings? before, ReferenceCycleSettings? after) : IHistoryAction
{
    public string Name => "Adjust reference cycle";
    public long ByteSize => 128;
    public void Undo(Document d) => layer.ReferenceCycle = before;
    public void Redo(Document d) => layer.ReferenceCycle = after;
}
