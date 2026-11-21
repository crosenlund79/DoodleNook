using DoodleNook.Engine.Documents;

namespace DoodleNook.App.Tutorials;

/// <summary>
/// Mission 014: a guided tutorial — a short introduction, a few steps done in the real application, and a closing
/// card. Each step is plain data: what to do (one to three sentences), which control to point at, and how DoodleNook
/// notices that it's done. That is the whole "framework": a new tutorial is a new list of steps.
/// </summary>
public sealed record Tutorial(
    string Id,
    string Title,
    string Description,
    string Length,
    string IntroText,
    IReadOnlyList<TutorialStep> Steps,
    string FinishTitle,
    string FinishText);

/// <summary>
/// One step. <see cref="Target"/> names the real control to highlight (its <c>Name</c> in the window). <see cref="Done"/>
/// reads real application state (a frame was added, playback started, the picture was saved); a step without it is
/// read and confirmed with Next. A step whose state can't be read reliably always offers "I've done this", so a
/// detection that fails never traps the artist. <see cref="Action"/> is a button that does the step for you (creating
/// the practice animation); <see cref="Reveal"/> brings a hidden target into view.
/// </summary>
public sealed record TutorialStep(string Title, string Text)
{
    public string? Target { get; init; }
    public Func<TutorialContext, bool>? Done { get; init; }
    public string? DoneText { get; init; }

    /// <summary>Already true when the step starts (e.g. onion skin was on): say so and wait for Next.</summary>
    public string? AlreadyText { get; init; }

    public Func<TutorialContext, string?>? Progress { get; init; }
    public string? ActionLabel { get; init; }
    public Func<TutorialContext, Task<bool>>? Action { get; init; }
    public Action<TutorialContext>? Reveal { get; init; }

    /// <summary>Move on by itself shortly after the step is done (the artist can always go Back).</summary>
    public bool AutoAdvance { get; init; } = true;
}

/// <summary>
/// What a step's checks can see: the window and its document, plus counts taken when the step started (frames,
/// undo steps, saves, exports, times Play was pressed), so "done" means "done during this step".
/// </summary>
public sealed class TutorialContext(MainWindow app)
{
    public MainWindow App { get; } = app;
    public Document Document => App.Session.Document;

    /// <summary>The practice animation the tutorial made (null until it does).</summary>
    public Document? Practice { get; internal set; }

    public int FramesAtStart { get; private set; }
    public int UndoAtStart { get; private set; }
    public int SavesAtStart { get; private set; }
    public int ExportsAtStart { get; private set; }
    public int PlaysAtStart { get; private set; }

    /// <summary>How many times playback has started since the tutorial began.</summary>
    public int Plays { get; internal set; }

    internal void Snapshot()
    {
        FramesAtStart = Document.FrameCount;
        UndoAtStart = Document.History.UndoCount;
        SavesAtStart = App.SavesCompleted;
        ExportsAtStart = App.AnimationExportsCompleted;
        PlaysAtStart = Plays;
    }

    /// <summary>Frames on which some drawing layer shows a drawing (library reference layers don't count).</summary>
    public int FramesWithDrawing()
    {
        var doc = Document;
        if (!doc.IsAnimated) return doc.Layers.Any(l => l.IsPaintable && !l.ContentBounds.IsEmpty) ? 1 : 0;
        var layers = doc.Layers.Where(l => l.IsTrack && l.ReferenceCycle is null).ToList();
        int n = 0;
        for (int f = 0; f < doc.FrameCount; f++)
            if (layers.Any(l => doc.CelAt(l, f) is { ContentBounds.IsEmpty: false })) n++;
        return n;
    }

    /// <summary>True when the frame on screen has a drawing at least <paramref name="size"/> pixels across on the
    /// active layer, made during this step.</summary>
    public bool DrewOnThisFrame(int size = 6)
    {
        var doc = Document;
        if (doc.History.UndoCount <= UndoAtStart) return false;
        var b = doc.IsAnimated ? doc.CelAt(doc.ActiveLayer, doc.CurrentFrame)?.ContentBounds ?? Engine.Imaging.PixelRect.Empty : doc.ActiveLayer.ContentBounds;
        return !b.IsEmpty && Math.Max(b.Width, b.Height) >= size;
    }
}
