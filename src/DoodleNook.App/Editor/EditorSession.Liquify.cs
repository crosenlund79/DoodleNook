using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Editor;

/// <summary>The Liquify brush's settings (kept between sessions).</summary>
public sealed record LiquifyOptions(LiquifyMode Mode = LiquifyMode.Push, double Size = 150, double Strength = 0.6, double Softness = 0.7);

/// <summary>
/// Liquify on the canvas (Mission 010): Edit › Liquify… starts a session on the active layer (or its mask, or the
/// current drawing). While it runs, dragging on the canvas reshapes and the options bar shows Liquify's controls;
/// Apply keeps the result as one undo step, Cancel puts the layer back. Anything that finishes pending work (another
/// tool, saving, a transform…) applies it, as with a free transform.
/// </summary>
public sealed partial class EditorSession
{
    private LiquifySession? _liquify;

    public LiquifyOptions Liquify { get; private set; } = new();

    /// <summary>The running Liquify session, or null.</summary>
    public LiquifySession? ActiveLiquify => _liquify is { IsFinished: false } ? _liquify : null;

    /// <summary>Starts Liquify on the active layer. Returns false (and says why) when it can't: text and shapes must be
    /// turned into pixels first (the window asks), locked or hidden layers can't change.</summary>
    public bool BeginLiquify()
    {
        if (ActiveLiquify is not null) return true;
        EndStroke();
        CommitTransform();
        if (!CanEditActiveLayer()) return false;
        var sampling = PixelMode ? LiquifySampling.Pixels : Document.PaletteLocked ? LiquifySampling.PaletteSafe : LiquifySampling.Smooth;
        _liquify = LiquifySession.Begin(Document, Document.ActiveLayer, sampling);
        if (_liquify is null) return false;
        ApplyLiquifyOptions();
        Changed();
        return true;
    }

    public void SetLiquifyOptions(LiquifyOptions o)
    {
        Liquify = o with
        {
            Size = Math.Clamp(o.Size, 4, BrushSettings.MaxSize),
            Strength = Math.Clamp(o.Strength, 0.05, 1),
            Softness = Math.Clamp(o.Softness, 0, 1),
        };
        ApplyLiquifyOptions();
        Changed();
    }

    private void ApplyLiquifyOptions()
    {
        if (_liquify is null) return;
        _liquify.Mode = Liquify.Mode;
        _liquify.Size = Liquify.Size;
        _liquify.Strength = Liquify.Strength;
        _liquify.Softness = Liquify.Softness;
    }

    /// <summary>Keeps the reshaped picture (one undo step). Returns false when nothing was running.</summary>
    public bool ApplyLiquify()
    {
        if (ActiveLiquify is not { } l) return false;
        l.Apply();
        _liquify = null;
        Changed();
        return true;
    }

    public bool CancelLiquify()
    {
        if (ActiveLiquify is not { } l) return false;
        l.Cancel();
        _liquify = null;
        Changed();
        return true;
    }

    public void ResetLiquify()
    {
        ActiveLiquify?.Reset();
        Changed();
    }

    /// <summary>Ctrl+Z while liquifying: takes back the last Liquify stroke.</summary>
    public bool UndoLiquifyStroke()
    {
        if (ActiveLiquify is not { } l || !l.UndoStroke()) return false;
        Changed();
        return true;
    }
}
