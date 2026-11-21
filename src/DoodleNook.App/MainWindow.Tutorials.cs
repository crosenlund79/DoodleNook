using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Shell;
using DoodleNook.App.Tutorials;

namespace DoodleNook.App;

/// <summary>
/// Mission 014 — Help › Guided Tutorials…: interactive lessons done in the real interface. The guide and its highlight
/// live on a click-through layer above the workspace and below dialogs; nothing about them exists until a tutorial
/// starts, so they cost nothing when unused.
/// </summary>
public partial class MainWindow
{
    private TutorialRunner? _tutorials;

    /// <summary>Saves that finished successfully this session (tutorials notice "saved").</summary>
    internal int SavesCompleted { get; private set; }

    /// <summary>Animation exports that finished successfully this session.</summary>
    internal int AnimationExportsCompleted { get; private set; }

    internal event Action? SaveCompleted;
    internal event Action? AnimationExported;

    /// <summary>The area the guide keeps to (the canvas).</summary>
    internal Control TutorialArea => CanvasHost;

    internal TutorialRunner Tutorials => _tutorials ??= CreateTutorialRunner();

    private TutorialRunner CreateTutorialRunner()
    {
        var layer = new Avalonia.Controls.Canvas { Name = "TutorialLayer" }; // no background: clicks pass through
        Grid.SetRowSpan(layer, 4);
        Root.Children.Insert(Root.Children.IndexOf(DialogLayer), layer);
        return new TutorialRunner(this, layer);
    }

    private void NoteSaved()
    {
        SavesCompleted++;
        SaveCompleted?.Invoke();
    }

    private void NoteAnimationExported()
    {
        AnimationExportsCompleted++;
        AnimationExported?.Invoke();
    }

    internal Task<bool> ConfirmDiscardForTutorialAsync() => ConfirmDiscardAsync("starting the practice animation");

    /// <summary>Brings Animation Studio's controls into view: the interface shown, the workspace open, the timeline open.</summary>
    internal void RevealAnimationControls()
    {
        if (UiHidden) SetUiHidden(false);
        if (Workspace.Kind != WorkspaceKind.Animation) SetWorkspace(WorkspaceKind.Animation);
        if (AnimationState.TimelineCollapsed) _timeline?.SetCollapsed(false);
    }

    /// <summary>Help › Guided Tutorials…: the lessons on offer, each with a Start button.</summary>
    public async Task ShowGuidedTutorialsAsync()
    {
        Animator.Pause();
        var body = new StackPanel { Spacing = 12, Width = 440, Name = "GuidedTutorials" };
        body.Children.Add(DialogHost.Text("Learn by doing, right here in DoodleNook. A small guide points at the real controls and notices " +
                                          "when each step is done; you do all the drawing, and you can stop at any time."));
        foreach (var t in FirstAnimationTutorial.All)
        {
            var id = t.Id;
            var start = UiKit.Button(null, "Start", $"Start “{t.Title}”", () => _dialogs.Close("start:" + id), "solid", "on");
            start.Name = "StartTutorial_" + t.Id;
            start.Height = 32;
            start.HorizontalAlignment = HorizontalAlignment.Right;
            var card = new Border
            {
                Background = new SolidColorBrush(Controls.DnColors.Ui3),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = t.Title, FontSize = 15, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = t.Description, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
                        new DockPanel
                        {
                            Children =
                            {
                                WithDock(start, Dock.Right),
                                new TextBlock { Text = t.Length, Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                            },
                        },
                    },
                },
            };
            body.Children.Add(card);
        }
        var r = await _dialogs.ShowAsync("Guided tutorials", body, [("close", "Close", false)], "close", "close", 500);
        if (r?.StartsWith("start:", StringComparison.Ordinal) == true && FirstAnimationTutorial.All.FirstOrDefault(t => "start:" + t.Id == r) is { } chosen)
            Tutorials.Start(chosen);
    }

    private static T WithDock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }
}
