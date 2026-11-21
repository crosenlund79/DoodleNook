using DoodleNook.App.Dialogs;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.References;

namespace DoodleNook.App;

/// <summary>
/// Mission 013 — the Reference Animation Library in Animation Studio: Animation › Insert reference cycle… adds a cycle
/// (walk, run, attack…) as a locked, faint layer just below the layer you draw on, without touching anything that is
/// already in the animation; Animation › Adjust reference cycle… (also on the layer's right-click menu) moves, resizes,
/// turns or swaps it later.
/// </summary>
public partial class MainWindow
{
    private ReferenceCycleSettings? _lastReference;

    public async Task<bool> InsertReferenceCycleAsync()
    {
        Animator.Stop();
        Session.CommitTransform();
        var doc = Session.Document;
        var drawOn = doc.ActiveLayer;
        bool pixel = AnimationState.PixelAnimation || Session.PixelMode;
        var choice = await ReferenceCycleDialog.AskAsync(this, ReferenceCycleDialog.Defaults(doc, pixel, _lastReference), ReferenceInsert.DefaultOpacity, adjusting: false);
        if (choice is null || !ReferenceEquals(doc, Session.Document)) return false;
        _lastReference = choice.Settings;
        try
        {
            var layer = ReferenceInsert.Insert(doc, choice.Settings, choice.Opacity, below: doc.Layers.Contains(drawOn) ? drawOn : doc.ActiveLayer);
            _timeline?.ShowRows(Math.Min(doc.Layers.Count, 5));
            var cycle = ReferenceLibrary.Find(choice.Settings.CycleId)!;
            ShowToast($"{cycle.Name} reference added under “{doc.ActiveLayer.Name}”, locked at {choice.Opacity * 100:0} % · Animation › Adjust reference cycle… moves or changes it");
            _ = layer;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            await _dialogs.ShowMessageAsync("Couldn't insert the reference cycle", ex.Message);
            return false;
        }
    }

    /// <summary>Moves, resizes, turns or swaps a reference cycle (the active layer's, or <paramref name="layer"/>'s).</summary>
    public async Task<bool> AdjustReferenceCycleAsync(Layer? layer = null)
    {
        Animator.Stop();
        Session.CommitTransform();
        var doc = Session.Document;
        layer ??= doc.ActiveLayer;
        if (layer.ReferenceCycle is not { } current)
        {
            ShowToast("This layer isn't a reference cycle · Animation › Insert reference cycle… adds one");
            return false;
        }
        var choice = await ReferenceCycleDialog.AskAsync(this, current, layer.Opacity, adjusting: true);
        if (choice is null || !ReferenceEquals(doc, Session.Document) || !doc.Layers.Contains(layer)) return false;
        _lastReference = choice.Settings;
        ReferenceInsert.Update(doc, layer, choice.Settings);
        if (Math.Abs(choice.Opacity - layer.Opacity) > 1e-6) Session.SetLayerProperty(layer, p => p with { Opacity = choice.Opacity }, "Layer opacity");
        ShowToast($"“{layer.Name}” updated · Ctrl+Z takes it back");
        return true;
    }
}
