using DoodleNook.App.Dialogs;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Tutorials;

/// <summary>
/// Mission 014 — "Your First Animation": a bouncing ball, drawn frame by frame in the real Animation Studio. The artist
/// draws everything; the guide only points at the next control and notices when each thing is done.
/// </summary>
public static class FirstAnimationTutorial
{
    public const string Id = "first-animation";
    public const int PracticeSize = 256;
    public const double PracticeFps = 8;
    public const int BounceFrames = 6;

    /// <summary>All guided tutorials. Later lessons are added here.</summary>
    public static readonly IReadOnlyList<Tutorial> All = [Create()];

    private static void RevealAnimation(TutorialContext c) => c.App.RevealAnimationControls();

    public static Tutorial Create() => new(
        Id,
        "Your First Animation",
        "Create a simple bouncing-ball animation, learn frames and onion skinning, and see your drawing come alive.",
        "8 short steps · about 10 minutes",
        "We're going to make a bouncing ball. You'll draw a few frames, play them back and export your animation. " +
        "You do all the drawing; this guide just points the way.",
        [
            new("Create the animation",
                "DoodleNook opens a small practice animation in Animation Studio: 256 × 256 pixels at 8 frames per second. " +
                "Anything you have open stays safe: you'll be asked first if it has unsaved changes.")
            {
                ActionLabel = "Create practice animation",
                Action = CreatePracticeAsync,
                Done = c => c.Practice is not null && ReferenceEquals(c.Document, c.Practice) && c.App.Workspace.Kind == WorkspaceKind.Animation,
                DoneText = "✓ Your practice animation is ready.",
            },
            new("Draw the first ball",
                "Pick the brush and draw a ball near the top of the canvas. A rough circle is perfect.")
            {
                Target = "ToolBrush",
                Reveal = RevealAnimation,
                Done = c => c.DrewOnThisFrame(),
                DoneText = "✓ That's your first drawing.",
            },
            new("Add another frame",
                "Each frame is a slightly different moment in your animation. Press New frame (or N) to start the next one.")
            {
                Target = "NewFrameButton",
                Reveal = RevealAnimation,
                Done = c => c.Document.FrameCount > c.FramesAtStart,
                DoneText = "✓ A fresh frame. The one before is still there.",
            },
            new("Turn on onion skin",
                "Onion skinning shows the neighbouring frames faintly, so you can see where the ball was. Turn it on with Onion skin (or O).")
            {
                Target = "OnionToggle",
                Reveal = RevealAnimation,
                Done = c => c.App.Session.Onion.Enabled,
                DoneText = "✓ Now the ball before shows faintly.",
                AlreadyText = "Onion skin is already on: the ball you drew shows faintly behind this frame. Keep it on while you draw.",
            },
            new("Make it bounce",
                "Draw the ball a little lower on each new frame. Near the ground, squash it slightly, then draw it rising again. " +
                "About six to eight frames is plenty.")
            {
                Target = "NewFrameButton",
                Reveal = RevealAnimation,
                Progress = c => $"Frames with a ball: {c.FramesWithDrawing()} · aim for about 6–8",
                Done = c => c.FramesWithDrawing() >= BounceFrames,
                DoneText = "✓ That's a whole bounce.",
            },
            new("Play it",
                "Press Play (or Enter) and watch your drawings come alive. The fps chip next to the timeline changes the speed.")
            {
                Target = "PlayButton",
                Reveal = RevealAnimation,
                Done = c => c.Plays > c.PlaysAtStart,
                DoneText = "✓ It moves! Press Play again to pause, then Next.",
                AutoAdvance = false,
            },
            new("Save your project",
                "Save with Ctrl+S (or File › Save) and choose where to keep it. A .dnk file is your editable project: " +
                "every frame and layer, ready to work on again.")
            {
                Target = "MenuFile",
                Reveal = RevealAnimation,
                Done = c => c.App.SavesCompleted > c.SavesAtStart,
                DoneText = "✓ Saved as an editable project.",
            },
            new("Export the animation",
                "File › Export animation… makes a copy to share or use in a game: an animated GIF, numbered PNGs or a sprite " +
                "sheet. Your .dnk stays the editable original.")
            {
                Target = "MenuFile",
                Reveal = RevealAnimation,
                Done = c => c.App.AnimationExportsCompleted > c.ExportsAtStart,
                DoneText = "✓ Exported.",
            },
        ],
        "You made your first animation!",
        "Your bouncing ball is yours to keep: draw more frames, try another speed with the fps chip, or start something new. " +
        "Help › Guided Tutorials is here whenever you want it again.");

    /// <summary>A fresh, unsaved practice animation, made the normal way (after the usual save / don't save / cancel
    /// question if the open picture has unsaved changes). False when the artist cancels.</summary>
    private static async Task<bool> CreatePracticeAsync(TutorialContext c)
    {
        if (!await c.App.ConfirmDiscardForTutorialAsync()) return false;
        c.App.CreateNewAnimation(new NewAnimationChoice(PracticeSize, PracticeSize, 1, PracticeFps, PixelArt: false, Paper: ColorRgb.White));
        c.Practice = c.App.Session.Document;
        return true;
    }
}
