using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Bones;
using DoodleNook.Engine.Geometry;

namespace DoodleNook.App.Bones;

/// <summary>Setup: build the skeleton and dress it (the rest pose). Animate: pose it frame by frame (every change is a key).</summary>
public enum BonesMode
{
    Setup,
    Animate,
}

/// <summary>What a drag on the stage does: pose bones, or draw new ones.</summary>
public enum BonesTool
{
    Pose,
    AddBone,
}

/// <summary>
/// Mission 019 "Bare Bones": a small 2D skeletal animation editor over the canvas (like Storybook Studio). Bones on the
/// left (the hierarchy, then the draw order), the stage in the middle, the selected bone and its sprites on the right,
/// and the master timeline below. Sprites are ordinary DoodleNook drawings (still or animated) or pictures, linked by
/// path and never changed here: "Open in editor" takes one to the normal editor and "Back to Bare Bones" returns. Every
/// change is saved straight away (safely) and can be undone, one step per drag.
/// </summary>
public sealed class BareBonesView : Border
{
    private const double LeftWidth = 200, InspectorWidth = 270, TimelineHeight = 150;

    private readonly MainWindow _app;
    private readonly SkeletonAssets _assets;
    private readonly BonesStage _stage;
    private readonly BonesTimeline _timeline;
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Name = "BonesTitle", MaxWidth = 320 };
    private readonly Button _undo, _redo, _play, _fps, _length;
    private readonly Action _refreshMode, _refreshTool, _refreshBonesToggle, _refreshLoop;
    private readonly StackPanel _bonesList = new() { Spacing = 1, Name = "BonesList" };
    private readonly StackPanel _orderList = new() { Spacing = 1, Name = "BonesDrawOrder" };
    private readonly StackPanel _inspector = new() { Spacing = 8, Name = "BonesInspector" };
    private readonly TextBlock _hint = new() { Classes = { "dim" }, FontSize = 12, Name = "BonesHint", TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _keyButton, _deleteKeyButton, _easeButton, _copyButton, _pasteButton;
    private readonly List<string> _undoStack = new(), _redoStack = new();
    private readonly DispatcherTimer _playTimer;
    private readonly Stopwatch _playClock = new();
    private SkeletonProject? _project;
    private string? _changeStart;
    private int _frame;
    private string? _selectedId, _attachmentId;
    private double _playFrom;
    private List<(string BoneId, (double X, double Y, double R, double SX, double SY) Pose, bool Smooth, SwitchKey? Switch)>? _clipboard;
    private TextBox? _nameBox;
    private DockPanel? _bottom;

    public BareBonesView(MainWindow app)
    {
        _app = app;
        Name = "BareBonesView";
        Background = new SolidColorBrush(DnColors.Surround);
        Padding = new Thickness(14, 10, 12, 10);
        ClipToBounds = true;
        IsVisible = false;
        _assets = new SkeletonAssets { DecodeImage = DecodePicture, Live = app.LiveSprite };
        _stage = new BonesStage(this);
        _timeline = new BonesTimeline(this);
        _playTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => PlayTick());

        // ---------------------------------------------------------- header
        _undo = UiKit.Button("undo", tip: "Undo (Ctrl+Z)", click: Undo);
        _redo = UiKit.Button("redo", tip: "Redo (Ctrl+Shift+Z)", click: Redo);
        _undo.Name = "BonesUndo";
        _redo.Name = "BonesRedo";
        var (mode, refreshMode) = UiKit.Segmented(
            [(BonesMode.Setup, "figure", "Setup", "Build the skeleton and dress it: the rest pose (Tab switches)"),
             (BonesMode.Animate, "film", "Animate", "Pose the bones frame by frame: every change sets a key (Tab switches)")],
            () => Mode, SetMode);
        mode.Name = "BonesMode";
        _refreshMode = refreshMode;
        var export = Menus.MenuButton(UiKit.Content("export", "Export"), ExportMenu, "The animation as an animated GIF, a sprite sheet or PNG files (bones are never drawn)", 260);
        export.Name = "BonesExport";
        var more = Menus.MenuButton(UiKit.Content("dots", null), MoreMenu, "More", 260);
        more.Name = "BonesMore";
        var back = UiKit.Button("brush", "Back to drawing", "Return to the drawing you had open (Esc)", app.HideBonesView);
        back.Name = "BonesBack";
        foreach (var b in new[] { _undo, _redo, export, more, back }) b.Height = 32;
        var right = UiKit.Row(4, _undo, _redo, UiKit.Sep(), export, more, back);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        header.Children.Add(UiKit.Row(12, new DnIcon { Kind = "bone", Width = 22, Height = 22 }, _title, mode));

        // ---------------------------------------------------------- left: bones and draw order
        var addBone = UiKit.Button("plus", "Bone", "Draw a new bone on the stage: from the selected bone, or a new root (A)", () => SetTool(Tool == BonesTool.AddBone ? BonesTool.Pose : BonesTool.AddBone));
        addBone.Name = "BonesAddBone";
        var deleteBone = UiKit.Button("trash", tip: "Delete the selected bone (its children stay, joined to its parent)", click: () => { if (Selected is { } b) DeleteBone(b); });
        deleteBone.Name = "BonesDeleteBone";
        var forward = UiKit.Button("chevu", tip: "Bring the selected bone's sprite forward", click: () => MoveInOrder(+1));
        var backward = UiKit.Button("chevd", tip: "Send the selected bone's sprite backward", click: () => MoveInOrder(-1));
        forward.Name = "BonesForward";
        backward.Name = "BonesBackward";
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(SectionHeader("Bones", addBone, deleteBone));
        left.Children.Add(_bonesList);
        left.Children.Add(SectionHeader("Draw order", forward, backward));
        left.Children.Add(Dim("Front at the top."));
        left.Children.Add(_orderList);
        var leftScroll = new ScrollViewer
        {
            Content = left, Width = LeftWidth, Margin = new Thickness(0, 0, 12, 0),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        // ---------------------------------------------------------- middle: the stage, with its tools
        var (tool, refreshTool) = UiKit.Segmented(
            [(BonesTool.Pose, "move", null, "Pose: drag joints to move, bones or sprites to turn (V)"),
             (BonesTool.AddBone, "bone", null, "Add bones: drag on the stage to draw a bone from the selected one (A)")],
            () => Tool, SetTool, compact: true);
        tool.Name = "BonesTool";
        _refreshTool = refreshTool;
        var (bonesToggle, refreshBonesToggle) = UiKit.Toggle("eye", "Bones", "Show the bones over the character (H). They are never part of an export.", () => ShowBones, v =>
        {
            ShowBones = v;
            _refreshBonesToggle?.Invoke();
            _stage.InvalidateVisual();
        });
        bonesToggle.Name = "BonesShowBones";
        bonesToggle.Height = 30;
        _refreshBonesToggle = refreshBonesToggle;
        var fit = UiKit.Button("fit", tip: "Fit the picture on the stage (double-click an empty spot)", click: _stage.FitToView);
        fit.Height = 30;
        var stageTools = UiKit.Row(6, tool, bonesToggle, fit);
        stageTools.HorizontalAlignment = HorizontalAlignment.Left;
        stageTools.VerticalAlignment = VerticalAlignment.Top;
        stageTools.Margin = new Thickness(8);
        _hint.HorizontalAlignment = HorizontalAlignment.Left;
        _hint.VerticalAlignment = VerticalAlignment.Bottom;
        _hint.Margin = new Thickness(10, 0, 10, 6);
        var stageHost = new Panel { Name = "BonesStageHost" };
        stageHost.Children.Add(_stage);
        stageHost.Children.Add(stageTools);
        stageHost.Children.Add(_hint);
        var stageFrame = new Border
        {
            Child = stageHost, CornerRadius = new CornerRadius(10), ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#211D1A")), BorderBrush = new SolidColorBrush(DnColors.Line), BorderThickness = new Thickness(1),
        };

        // ---------------------------------------------------------- bottom: the master timeline
        _play = UiKit.Button("play", tip: "Play / pause (Enter)", click: TogglePlay);
        _play.Name = "BonesPlay";
        var first = UiKit.Button("first", tip: "First frame (Home)", click: () => SetFrame(0));
        var prev = UiKit.Button("chevl", tip: "Previous frame (←)", click: () => SetFrame(_frame - 1));
        var next = UiKit.Button("chevr", tip: "Next frame (→)", click: () => SetFrame(_frame + 1));
        _fps = Menus.MenuButton(UiKit.Content(null, "12 fps"), FpsMenu, "Frames per second of the master timeline", 180);
        _fps.Name = "BonesFps";
        _length = Menus.MenuButton(UiKit.Content(null, "24 frames"), LengthMenu, "How long the animation is", 200);
        _length.Name = "BonesLength";
        var (loop, refreshLoop) = UiKit.Toggle("loop", null, "Loop: play round and round", () => _project?.Loop ?? true, v => Edit(p => p.Loop = v));
        loop.Name = "BonesLoop";
        _refreshLoop = refreshLoop;
        _keyButton = UiKit.Button("plus", "Key", "Key the selected bone's pose at this frame (K)", () => SetKeyHere());
        _keyButton.Name = "BonesSetKey";
        _deleteKeyButton = UiKit.Button("trash", tip: "Delete the selected bone's keys at this frame (Delete)", click: DeleteKeysHere);
        _deleteKeyButton.Name = "BonesDeleteKey";
        _easeButton = UiKit.Button(null, "Ease", "Ease in and out from this key to the next (otherwise an even change)", ToggleEase);
        _easeButton.Name = "BonesEase";
        _copyButton = UiKit.Button("copy", tip: "Copy the pose at this frame: the selected bone, or every bone when none is selected (Ctrl+C)", click: CopyPose);
        _pasteButton = UiKit.Button("paste", tip: "Paste the copied pose as keys at this frame (Ctrl+V)", click: PastePose);
        _copyButton.Name = "BonesCopyPose";
        _pasteButton.Name = "BonesPastePose";
        foreach (var b in new[] { _play, first, prev, next, _fps, _length, loop, _keyButton, _deleteKeyButton, _easeButton, _copyButton, _pasteButton }) b.Height = 28;
        var transport = UiKit.Row(4, first, prev, _play, next, UiKit.Sep(), _fps, _length, loop, UiKit.Sep(), _keyButton, _deleteKeyButton, _easeButton, _copyButton, _pasteButton);
        transport.Margin = new Thickness(0, 0, 0, 6);
        var timelineFrame = new Border
        {
            Child = _timeline, CornerRadius = new CornerRadius(8), ClipToBounds = true,
            BorderBrush = new SolidColorBrush(DnColors.Line), BorderThickness = new Thickness(1),
        };
        var bottom = new DockPanel { Height = TimelineHeight, Margin = new Thickness(0, 10, 0, 0), Name = "BonesTimelineArea" };
        _bottom = bottom;
        DockPanel.SetDock(transport, Dock.Top);
        bottom.Children.Add(transport);
        bottom.Children.Add(timelineFrame);

        // ---------------------------------------------------------- right: the selected bone
        _inspector.Margin = new Thickness(0, 0, 12, 12); // clear of the scroll bar
        var inspectorScroll = new ScrollViewer
        {
            Content = _inspector, Width = InspectorWidth + 12, Margin = new Thickness(12, 0, 0, 0),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var body = new DockPanel();
        DockPanel.SetDock(leftScroll, Dock.Left);
        DockPanel.SetDock(inspectorScroll, Dock.Right);
        body.Children.Add(leftScroll);
        body.Children.Add(inspectorScroll);
        body.Children.Add(stageFrame);
        var middle = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        middle.Children.Add(bottom);
        middle.Children.Add(body);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(middle);
        Child = root;
        Focusable = true;
        SizeChanged += (_, _) => SizeTimeline();
    }

    // ================================================================== state

    public SkeletonProject? Project => _project;
    public SkeletonAssets Assets => _assets;
    internal BonesStage Stage => _stage;
    internal BonesTimeline Timeline => _timeline;
    public BonesMode Mode { get; private set; } = BonesMode.Setup;
    public BonesTool Tool { get; private set; } = BonesTool.Pose;
    public bool ShowBones { get; set; } = true;
    public int Frame => _frame;
    public bool IsPlaying => _playTimer.IsEnabled;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public Bone? Selected => _project?.Find(_selectedId);

    /// <summary>The selected sprite (of the selected bone): the one chosen in the list, else the one showing now.</summary>
    public BoneAttachment? SelectedAttachment
    {
        get
        {
            if (Selected is not { } bone || _project is not { } p) return null;
            return p.FindAttachment(bone, _attachmentId) ?? _stage.Pose?[bone.Id]?.Attachment ?? bone.Attachments.FirstOrDefault();
        }
    }

    /// <summary>The character as the stage shows it: the rest pose in Setup, the frame in Animate.</summary>
    public SkeletonPose EvaluateShown() =>
        SkeletonEvaluator.Evaluate(_project!, Mode == BonesMode.Setup ? 0 : _frame, _assets, setupPose: Mode == BonesMode.Setup);

    /// <summary>Shows a project (from its first frame, in Setup when it has no keys yet).</summary>
    public void Open(SkeletonProject project)
    {
        StopPlaying();
        if (!ReferenceEquals(project, _project))
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _assets.Clear();
            _selectedId = null;
            _attachmentId = null;
            _frame = 0;
            Mode = project.Bones.Count > 0 && project.KeyFrames().Count > 0 ? BonesMode.Animate : BonesMode.Setup;
            Tool = project.Bones.Count == 0 ? BonesTool.AddBone : BonesTool.Pose;
        }
        _project = project;
        _stage.FitToView();
        Rebuild();
    }

    /// <summary>Everything again (after a change, or coming back from the editor: drawings may have changed).</summary>
    public void Rebuild()
    {
        if (_project is not { } p) return;
        if (_selectedId is not null && p.Find(_selectedId) is null) _selectedId = null;
        _frame = Math.Clamp(_frame, 0, p.Length - 1);
        _title.Text = Path.GetFileNameWithoutExtension(p.FilePath) ?? "Skeletal animation";
        _undo.IsEnabled = CanUndo;
        _redo.IsEnabled = CanRedo;
        _refreshMode();
        _refreshTool();
        _refreshBonesToggle();
        _refreshLoop();
        _fps.Content = UiKit.Content(null, $"{p.Fps:0.##} fps");
        _length.Content = UiKit.Content(null, $"{p.Length} frames");
        FillBones();
        FillOrder();
        SizeTimeline();
        _stage.Refresh();
        _timeline.Refresh();
        FillInspector();
        RefreshKeyButtons();
        Hint(null);
    }

    /// <summary>The timeline grows with the skeleton (every bone's row in sight), up to a third of the view.</summary>
    private void SizeTimeline()
    {
        if (_bottom is null || _project is not { } p) return;
        _bottom.Height = Math.Clamp(34 + BonesTimeline.RulerHeight + p.Bones.Count * BonesTimeline.RowHeight + 14, TimelineHeight,
            Math.Max(TimelineHeight, Bounds.Height / 3));
    }

    /// <summary>Only what moves while dragging or playing: the stage, the timeline and the key buttons.</summary>
    private void RefreshLive()
    {
        _stage.Refresh();
        _timeline.Refresh();
        RefreshKeyButtons();
    }

    private void RefreshKeyButtons()
    {
        bool animate = Mode == BonesMode.Animate;
        var bone = Selected;
        var key = bone is null ? null : _project?.TrackOf(bone.Id)?.Keys.FirstOrDefault(k => k.Frame == _frame);
        bool anyKey = bone is not null && _project!.KeyFrames(bone).Contains(_frame);
        _keyButton.IsEnabled = bone is not null;
        _deleteKeyButton.IsEnabled = animate && anyKey;
        _easeButton.IsEnabled = animate && key is not null;
        _easeButton.Classes.Set("on", key?.Smooth == true);
        _pasteButton.IsEnabled = _clipboard is { Count: > 0 };
        _copyButton.IsEnabled = _project is { Bones.Count: > 0 };
        _play.Content = UiKit.Content(IsPlaying ? "pause" : "play", null);
    }

    public void Hint(string? text) => _hint.Text = text ?? DefaultHint();

    private string DefaultHint()
    {
        if (_project is not { } p) return "";
        if (p.Bones.Count == 0) return "Drag on the stage to draw the first bone (a root). Then keep dragging from its end to add children.";
        if (Tool == BonesTool.AddBone) return Selected is { } s ? $"Drag to draw a child of {s.Name} (start at its end to chain). Esc or V: back to posing." : "Drag to draw a new root bone.";
        return Mode == BonesMode.Setup
            ? "Setup · drag a joint to move a bone, the bone to turn it, the tip to change its length, the selected sprite to place it."
            : $"Animate · frame {_frame + 1}: drag a joint to move, a bone or its sprite to turn it. Each change keys that bone here.";
    }

    // ================================================================== modes, tools, selection, frames

    public void SetMode(BonesMode mode)
    {
        if (mode == Mode) return;
        StopPlaying();
        Mode = mode;
        if (mode == BonesMode.Animate && Tool == BonesTool.AddBone) Tool = BonesTool.Pose;
        _app.ShowToast(mode == BonesMode.Setup
            ? "Setup · the rest pose: build the skeleton, place the sprites. Keys aren't changed here."
            : $"Animate · frame {_frame + 1}: posing a bone sets a key for it at this frame");
        Rebuild();
        Hint(null);
    }

    public void SetTool(BonesTool tool)
    {
        if (tool == BonesTool.AddBone && Mode != BonesMode.Setup)
        {
            Mode = BonesMode.Setup;
            _app.ShowToast("Bones are added in Setup (the rest pose)");
            Rebuild();
        }
        Tool = tool;
        _refreshTool();
        Hint(null);
    }

    public void Select(Bone? bone)
    {
        if (bone?.Id == _selectedId) return;
        _selectedId = bone?.Id;
        _attachmentId = null;
        FillBones();
        FillOrder();
        _stage.InvalidateVisual();
        _timeline.Refresh();
        FillInspector();
        RefreshKeyButtons();
        Hint(null);
    }

    private void SelectAttachment(BoneAttachment a)
    {
        _attachmentId = a.Id;
        _stage.InvalidateVisual();
        FillInspector();
    }

    /// <summary>Goes to a master frame (in Setup this switches to Animate: frames are what Animate is for).</summary>
    public void SetFrame(int frame)
    {
        if (_project is not { } p) return;
        frame = Math.Clamp(frame, 0, p.Length - 1);
        bool modeChanged = Mode == BonesMode.Setup;
        if (modeChanged)
        {
            Mode = BonesMode.Animate;
            Tool = BonesTool.Pose;
        }
        if (frame == _frame && !modeChanged) return;
        _frame = frame;
        _timeline.Follow(frame);
        if (modeChanged) Rebuild();
        else
        {
            RefreshLive();
            FillInspector();
        }
        Hint(null);
    }

    // ================================================================== playback

    public void TogglePlay()
    {
        if (IsPlaying) StopPlaying();
        else Play();
    }

    public void Play()
    {
        if (_project is not { } p || p.Bones.Count == 0) return;
        if (Mode == BonesMode.Setup)
        {
            Mode = BonesMode.Animate;
            Tool = BonesTool.Pose;
            Rebuild();
        }
        if (!p.Loop && _frame >= p.Length - 1) _frame = 0;
        _playFrom = _frame / p.Fps;
        _playClock.Restart();
        _playTimer.Start();
        RefreshKeyButtons();
    }

    public void StopPlaying()
    {
        if (!_playTimer.IsEnabled) return;
        _playTimer.Stop();
        _playClock.Stop();
        RefreshKeyButtons();
        FillInspector();
        Hint(null);
    }

    private void PlayTick()
    {
        if (_project is not { } p || !IsVisible)
        {
            StopPlaying();
            return;
        }
        double t = _playFrom + _playClock.Elapsed.TotalSeconds;
        int f = SkeletonEvaluator.FrameAtTime(p, t);
        if (f != _frame)
        {
            _frame = f;
            _timeline.Follow(f);
            RefreshLive();
        }
        if (!p.Loop && t * p.Fps >= p.Length - 1) StopPlaying();
    }

    // ================================================================== changes (saved at once, undoable)

    /// <summary>Changes the project: remembered for undo (one step), then saved safely.</summary>
    public bool Edit(Action<SkeletonProject> change)
    {
        if (_project is not { } p) return false;
        StopPlaying();
        var before = p.ToJson();
        change(p);
        if (p.ToJson() == before)
        {
            Rebuild();
            return true;
        }
        Remember(before);
        Save();
        Rebuild();
        return true;
    }

    /// <summary>A drag starts: the state before it is kept, so the whole drag is one undo step.</summary>
    public void BeginChange()
    {
        StopPlaying();
        _changeStart = _project?.ToJson();
    }

    public void EndChange()
    {
        if (_project is not { } p || _changeStart is null) return;
        var before = _changeStart;
        _changeStart = null;
        if (p.ToJson() != before)
        {
            Remember(before);
            Save();
        }
        Rebuild();
    }

    private void Remember(string before)
    {
        _undoStack.Add(before);
        if (_undoStack.Count > 200) _undoStack.RemoveAt(0);
        _redoStack.Clear();
    }

    private bool Save()
    {
        if (_project is not { FilePath: not null } p) return false;
        try
        {
            p.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.ShowToast($"Couldn't save the skeletal animation: {ex.Message}");
            return false;
        }
    }

    public void Undo() => Restore(_undoStack, _redoStack);

    public void Redo() => Restore(_redoStack, _undoStack);

    private void Restore(List<string> from, List<string> to)
    {
        if (_project is not { } p || from.Count == 0) return;
        StopPlaying();
        var json = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(p.ToJson());
        _project = SkeletonProject.Parse(json, p.FilePath);
        _app.SkeletonReplaced(_project);
        Save();
        Rebuild();
    }

    // ================================================================== posing (from the stage and the fields)

    /// <summary>A bone's pose: its rest pose in Setup, a key at this frame in Animate.</summary>
    public void SetPose(Bone bone, double x, double y, double rotation, double scaleX, double scaleY)
    {
        if (_project is not { } p) return;
        if (Mode == BonesMode.Setup) (bone.X, bone.Y, bone.Rotation, bone.ScaleX, bone.ScaleY) = (x, y, rotation, scaleX, scaleY);
        else p.SetPoseKey(bone, _frame, x, y, rotation, scaleX, scaleY);
        RefreshLive();
    }

    public void SetLength(Bone bone, double length)
    {
        bone.Length = Math.Clamp(length, 1, 100_000);
        _stage.InvalidateVisual();
    }

    public void SetAttachmentOffset(Bone bone, BoneAttachment a, double x, double y)
    {
        (a.X, a.Y) = (x, y);
        RefreshLive();
    }

    /// <summary>A bone drawn on the stage from <paramref name="start"/> to <paramref name="end"/> (picture pixels): a child of
    /// the selected bone, or a root.</summary>
    public void AddBoneDrawn((double X, double Y) start, (double X, double Y) end)
    {
        if (_project is not { } p) return;
        var parent = Selected;
        var parentWorld = p.RestWorld(parent);
        if (!parentWorld.IsInvertible) parentWorld = Affine.Identity;
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double worldLength = Math.Sqrt(dx * dx + dy * dy);
        if (worldLength < 4)
        {
            // A click: a short bone pointing the way its parent points (or right).
            var (px, py) = parentWorld.Apply(1, 0);
            var (ox, oy) = parentWorld.Apply(0, 0);
            double pl = Math.Max(1e-6, Math.Sqrt((px - ox) * (px - ox) + (py - oy) * (py - oy)));
            (dx, dy, worldLength) = ((px - ox) / pl * 40, (py - oy) / pl * 40, 40);
        }
        var inv = parentWorld.Invert();
        var (lx, ly) = inv.Apply(start.X, start.Y);
        var (lex, ley) = inv.Apply(start.X + dx, start.Y + dy);
        double rotation = Math.Atan2(ley - ly, lex - lx) * 180 / Math.PI;
        double length = Math.Sqrt((lex - lx) * (lex - lx) + (ley - ly) * (ley - ly));
        string? id = null;
        Edit(q => id = q.AddBone(parent is null ? (q.Bones.Count == 0 ? "Root" : "Bone") : "Bone", q.Find(parent?.Id), lx, ly, rotation, length).Id);
        if (_project?.Find(id) is { } made)
        {
            Select(made);
            _app.ShowToast($"Added {made.Name}{(parent is null ? " (a root)" : $" to {parent.Name}")} · drag again from its end to add the next");
        }
    }

    public void DeleteBone(Bone bone)
    {
        int children = _project?.ChildrenOf(bone.Id).Count() ?? 0;
        Edit(p => p.DeleteBone(p.Find(bone.Id)!));
        _app.ShowToast($"Deleted {bone.Name}{(children > 0 ? $" · its {children} child bone{(children == 1 ? "" : "s")} joined its parent" : "")} · Ctrl+Z brings it back");
    }

    private void MoveInOrder(int direction)
    {
        if (Selected is not { } bone) return;
        Edit(p => p.MoveInDrawOrder(p.Find(bone.Id)!, direction));
    }

    // ================================================================== keys

    /// <summary>Keys the selected bone's pose (as it is now) at this frame.</summary>
    public void SetKeyHere(Bone? bone = null)
    {
        bone ??= Selected;
        if (bone is null || _project is null) return;
        if (Mode == BonesMode.Setup) SetMode(BonesMode.Animate);
        var pose = SkeletonEvaluator.Evaluate(_project, _frame)[bone.Id]!;
        var id = bone.Id;
        int f = _frame;
        Edit(p => p.SetPoseKey(p.Find(id)!, f, pose.X, pose.Y, pose.Rotation, pose.ScaleX, pose.ScaleY));
    }

    public void DeleteKeysHere()
    {
        if (Selected is not { } bone || Mode != BonesMode.Animate) return;
        int f = _frame;
        Edit(p =>
        {
            var b = p.Find(bone.Id)!;
            p.RemovePoseKey(b, f);
            p.RemoveSwitch(b, f);
        });
    }

    private void ToggleEase()
    {
        if (Selected is not { } bone || _project?.TrackOf(bone.Id)?.Keys.FirstOrDefault(k => k.Frame == _frame) is null) return;
        int f = _frame;
        Edit(p =>
        {
            var k = p.TrackOf(bone.Id)!.Keys.First(k => k.Frame == f);
            k.Smooth = !k.Smooth;
        });
    }

    /// <summary>Moves a bone's keys (pose and switch) from one frame to another, replacing what was there.</summary>
    public void MoveKeys(Bone bone, int from, int to)
    {
        Edit(p =>
        {
            var t = p.TrackOf(bone.Id);
            if (t is null) return;
            if (t.Keys.FirstOrDefault(k => k.Frame == from) is { } key)
            {
                t.Keys.RemoveAll(k => k.Frame == to);
                key.Frame = to;
                t.Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
            if (t.Switches.FirstOrDefault(s => s.Frame == from) is { } sw)
            {
                t.Switches.RemoveAll(s => s.Frame == to);
                sw.Frame = to;
                t.Switches.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
        });
    }

    /// <summary>From this frame on the bone shows <paramref name="attachment"/> (null: nothing).</summary>
    public void SwitchHere(Bone bone, BoneAttachment? attachment)
    {
        if (Mode == BonesMode.Setup) SetMode(BonesMode.Animate);
        int f = _frame;
        Edit(p =>
        {
            var b = p.Find(bone.Id)!;
            p.SetSwitch(b, f, attachment is null ? null : p.FindAttachment(b, attachment.Id));
        });
    }

    public void CopyPose()
    {
        if (_project is not { } p) return;
        var pose = EvaluateShown();
        var bones = Selected is { } s ? [s] : p.Bones;
        _clipboard = bones.Select(b =>
        {
            var bp = pose[b.Id]!;
            var key = p.TrackOf(b.Id)?.Keys.FirstOrDefault(k => k.Frame == _frame);
            var sw = Mode == BonesMode.Animate ? p.TrackOf(b.Id)?.Switches.FirstOrDefault(x => x.Frame == _frame) : null;
            return (b.Id, (bp.X, bp.Y, bp.Rotation, bp.ScaleX, bp.ScaleY), key?.Smooth ?? false, sw is null ? null : new SwitchKey { Frame = sw.Frame, Attachment = sw.Attachment });
        }).ToList();
        RefreshKeyButtons();
        _app.ShowToast(Selected is { } one ? $"Copied {one.Name}'s pose" : $"Copied the pose of all {bones.Count} bones" + " · Ctrl+V pastes it as keys at another frame");
    }

    public void PastePose()
    {
        if (_project is null || _clipboard is not { Count: > 0 } clip) return;
        if (Mode == BonesMode.Setup) SetMode(BonesMode.Animate);
        int f = _frame;
        Edit(p =>
        {
            foreach (var (id, (x, y, r, sx, sy), smooth, sw) in clip)
            {
                if (p.Find(id) is not { } b) continue;
                p.SetPoseKey(b, f, x, y, r, sx, sy, smooth);
                if (sw is not null) p.SetSwitch(b, f, p.FindAttachment(b, sw.Attachment));
            }
        });
        _app.ShowToast($"Pasted as keys at frame {f + 1}");
    }

    // ================================================================== menus

    internal IEnumerable<MenuEntry> BoneMenu(Bone bone)
    {
        yield return new("bone", "Add a child bone", "A", () =>
        {
            Select(bone);
            SetTool(BonesTool.AddBone);
        });
        yield return new("text", "Rename", null, () =>
        {
            Select(bone);
            Dispatcher.UIThread.Post(() => _nameBox?.Focus());
        });
        yield return MenuEntry.Sub("figure", "Parent", ParentEntries(bone).ToList());
        yield return MenuEntry.Separator;
        yield return new("chevu", "Bring sprite forward", null, () => { Select(bone); MoveInOrder(+1); });
        yield return new("chevd", "Send sprite backward", null, () => { Select(bone); MoveInOrder(-1); });
        yield return new("image", "Add a sprite…", null, () => _ = AddSpriteAsync(bone));
        yield return MenuEntry.Separator;
        yield return new("trash", "Delete bone", Mode == BonesMode.Setup ? "Delete" : null, () => DeleteBone(bone));
    }

    private IEnumerable<MenuEntry> ParentEntries(Bone bone)
    {
        if (_project is not { } p) yield break;
        yield return new(null, "None (a root)", null, () => Reparent(bone, null), Checked: bone.ParentId is null);
        foreach (var other in p.Ordered())
        {
            if (p.IsSelfOrDescendant(bone, other)) continue;
            var target = other;
            yield return new(null, new string(' ', p.Depth(other) * 3) + other.Name, null, () => Reparent(bone, target), Checked: bone.ParentId == other.Id);
        }
    }

    private void Reparent(Bone bone, Bone? parent)
    {
        bool ok = true;
        Edit(p => ok = p.Reparent(p.Find(bone.Id)!, p.Find(parent?.Id)));
        _app.ShowToast(ok ? $"{bone.Name} now hangs from {parent?.Name ?? "nothing (a root)"}; it stays where it was" : "A bone can't hang from one of its own children");
    }

    internal IEnumerable<MenuEntry> KeyMenu(Bone bone, int frame)
    {
        var p = _project!;
        var track = p.TrackOf(bone.Id);
        var key = track?.Keys.FirstOrDefault(k => k.Frame == frame);
        var sw = track?.Switches.FirstOrDefault(s => s.Frame == frame);
        yield return MenuEntry.Heading($"{bone.Name} · frame {frame + 1}");
        yield return new("plus", key is null ? "Key the pose here" : "Key the pose here again", "K", () => SetKeyHere(bone));
        yield return new("trash", "Delete the pose key", null, () => Edit(q => q.RemovePoseKey(q.Find(bone.Id)!, frame)), key is not null);
        yield return new(null, "Ease in and out", null, ToggleEase, key is not null, Checked: key?.Smooth == true);
        yield return MenuEntry.Separator;
        var switches = new List<MenuEntry>();
        foreach (var a in bone.Attachments)
        {
            var att = a;
            switches.Add(new(null, a.Name, null, () => SwitchHere(bone, att), Checked: sw is not null && sw.Attachment == a.Id));
        }
        switches.Add(new(null, "Nothing (hide the sprite)", null, () => SwitchHere(bone, null), Checked: sw is not null && sw.Attachment is null));
        yield return MenuEntry.Sub("image", "Show sprite from here", switches, bone.Attachments.Count > 0);
        yield return new("close", "Remove the sprite switch", null, () => Edit(q => q.RemoveSwitch(q.Find(bone.Id)!, frame)), sw is not null);
        yield return MenuEntry.Separator;
        yield return new("copy", "Copy pose", "Ctrl+C", CopyPose);
        yield return new("paste", "Paste pose", "Ctrl+V", PastePose, _clipboard is { Count: > 0 });
    }

    private IEnumerable<MenuEntry> FpsMenu()
    {
        foreach (var fps in new[] { 6.0, 8, 10, 12, 15, 24, 30 })
        {
            double v = fps;
            yield return new(null, $"{fps:0} fps", null, () => Edit(p => p.Fps = v), Checked: Math.Abs((_project?.Fps ?? 0) - fps) < 0.01);
        }
        yield return MenuEntry.Separator;
        yield return new("settings", "Picture size and speed…", null, () => _ = _app.SkeletonSettingsAsync());
    }

    private IEnumerable<MenuEntry> LengthMenu()
    {
        var p = _project;
        if (p is null) yield break;
        foreach (var seconds in new[] { 1.0, 2, 3, 4, 6, 8 })
        {
            int frames = (int)Math.Round(seconds * p.Fps);
            yield return new(null, $"{frames} frames ({seconds:0} s)", null, () => SetLength(frames), Checked: p.Length == frames);
        }
        yield return MenuEntry.Separator;
        yield return new(null, "Up to the last key", null, () => SetLength(p.KeyFrames().DefaultIfEmpty(0).Max() + 1), p.KeyFrames().Count > 0);
        yield return new("settings", "Exact length…", null, () => _ = _app.SkeletonSettingsAsync());
    }

    public void SetLength(int frames)
    {
        if (_project is not { } p) return;
        int lost = p.Tracks.Sum(t => t.Keys.Count(k => k.Frame >= frames) + t.Switches.Count(s => s.Frame >= frames));
        Edit(q => q.SetLength(frames));
        if (lost > 0) _app.ShowToast($"{lost} key{(lost == 1 ? " was" : "s were")} past the new end and removed · Ctrl+Z brings {(lost == 1 ? "it" : "them")} back");
    }

    private IEnumerable<MenuEntry> ExportMenu()
    {
        bool any = _project is { Bones.Count: > 0 };
        yield return new("film", "Export animation…", null, () => _ = _app.ExportSkeletonAsync(), any);
        yield return new("export", "This frame as PNG…", null, () => _ = _app.ExportSkeletonFrameAsync(), any);
    }

    private IEnumerable<MenuEntry> MoreMenu()
    {
        yield return new("settings", "Picture size and speed…", null, () => _ = _app.SkeletonSettingsAsync());
        yield return new("saveas", "Save a copy as…", null, () => _ = _app.SaveSkeletonCopyAsync());
        yield return new("folder", "Show where it is saved", null, () => _app.ShowToast(_project?.FilePath ?? ""), _project?.FilePath is not null);
        yield return MenuEntry.Separator;
        yield return new("close", "Close the skeletal animation", null, _app.CloseSkeleton);
    }

    // ================================================================== keys (from MainWindow while the view is shown)

    /// <summary>Bare Bones' own shortcuts. Returns false for keys it leaves to the window.</summary>
    internal bool HandleKey(Key key, KeyModifiers mods)
    {
        bool ctrl = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        switch (key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Redo();
                return true;
            case Key.Z when ctrl:
                Undo();
                return true;
            case Key.C when ctrl:
                CopyPose();
                return true;
            case Key.V when ctrl:
                PastePose();
                return true;
            case Key.Enter:
                TogglePlay();
                return true;
            case Key.Space when !ctrl:
                _stage.SpaceHeld = true; // drag to move the view
                return true;
            case Key.Left or Key.OemComma when !ctrl:
                SetFrame(_frame - 1);
                return true;
            case Key.Right or Key.OemPeriod when !ctrl:
                SetFrame(_frame + 1);
                return true;
            case Key.Home:
                SetFrame(0);
                return true;
            case Key.End:
                SetFrame((_project?.Length ?? 1) - 1);
                return true;
            case Key.K when !ctrl:
                SetKeyHere();
                return true;
            case Key.A when !ctrl:
                SetTool(BonesTool.AddBone);
                return true;
            case Key.V when !ctrl:
                SetTool(BonesTool.Pose);
                return true;
            case Key.H when !ctrl:
                ShowBones = !ShowBones;
                _refreshBonesToggle();
                _stage.InvalidateVisual();
                return true;
            case Key.Tab when !ctrl:
                SetMode(Mode == BonesMode.Setup ? BonesMode.Animate : BonesMode.Setup);
                return true;
            case Key.Delete or Key.Back:
                if (Mode == BonesMode.Animate) DeleteKeysHere();
                else if (Selected is { } b) DeleteBone(b);
                return true;
            case Key.Escape:
                if (IsPlaying) StopPlaying();
                else if (Tool == BonesTool.AddBone) SetTool(BonesTool.Pose);
                else _app.HideBonesView();
                return true;
            default:
                return false;
        }
    }

    // ================================================================== the bone list and draw order

    private void FillBones()
    {
        _bonesList.Children.Clear();
        if (_project is not { } p) return;
        if (p.Bones.Count == 0)
        {
            _bonesList.Children.Add(Dim("No bones yet. Choose “＋ Bone” and drag on the stage."));
            return;
        }
        foreach (var bone in p.Ordered())
            _bonesList.Children.Add(BoneRow(bone, p.Depth(bone), $"BonesListRow_{bone.Name}"));
    }

    private void FillOrder()
    {
        _orderList.Children.Clear();
        if (_project is not { } p) return;
        foreach (var bone in Enumerable.Reverse(p.BackToFront()))
            _orderList.Children.Add(BoneRow(bone, 0, $"BonesOrderRow_{bone.Name}", order: true));
    }

    private Control BoneRow(Bone bone, int depth, string name, bool order = false)
    {
        bool selected = bone.Id == _selectedId;
        var label = new TextBlock
        {
            Text = bone.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal, FontSize = 12.5,
        };
        var row = new DockPanel { Margin = new Thickness(6 + Math.Min(depth, 6) * 12, 0, 6, 0) };
        int sprites = bone.Attachments.Count;
        if (!order && sprites > 0)
        {
            var count = new TextBlock { Text = sprites == 1 ? "1 sprite" : $"{sprites} sprites", Classes = { "dim" }, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
        }
        if (order && sprites == 0) label.Classes.Add("dim");
        row.Children.Add(label);
        var border = new Border
        {
            Child = row, Height = 26, CornerRadius = new CornerRadius(6), Name = name,
            Background = selected ? new SolidColorBrush(DnColors.Ui3) : Brushes.Transparent,
            BorderBrush = selected ? new SolidColorBrush(DnColors.Accent) : Brushes.Transparent, BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (!order && depth > 0) ToolTip.SetTip(border, $"{bone.Name} · hangs from {_project?.Find(bone.ParentId)?.Name}");
        border.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(border).Properties;
            Select(bone);
            if (props.IsRightButtonPressed) Menus.ShowAt(border, BoneMenu(bone).ToList(), 250);
            else if (e.ClickCount >= 2) Dispatcher.UIThread.Post(() => _nameBox?.Focus());
            e.Handled = true;
        };
        return border;
    }

    // ================================================================== the inspector

    private void FillInspector()
    {
        _inspector.Children.Clear();
        _nameBox = null;
        if (_project is not { } p) return;
        if (Selected is not { } bone)
        {
            _inspector.Children.Add(Heading(Path.GetFileNameWithoutExtension(p.FilePath) ?? "Skeletal animation"));
            _inspector.Children.Add(Dim($"{p.Width} × {p.Height} px · {p.Fps:0.##} fps · {p.Length} frames ({p.Length / p.Fps:0.#} s){(p.PixelArt ? " · pixel art" : "")}", "BonesProjectSummary"));
            var settings = UiKit.Button("settings", "Picture size and speed…", "The picture size it's assembled and exported at, frames per second, length", () => _ = _app.SkeletonSettingsAsync());
            _inspector.Children.Add(Stack(settings));
            _inspector.Children.Add(Dim(p.Bones.Count == 0
                ? "Start with a root bone: choose “＋ Bone” and drag on the stage (for a character, from the hips upwards). Keep dragging from a bone's end to add its children."
                : "Select a bone (on the stage or in the list) to name it, place it and give it sprites."));
            _inspector.Children.Add(Dim("Setup is the rest pose: the skeleton and where each sprite sits. Animate poses it frame by frame; every change becomes a key."));
            return;
        }

        bool animate = Mode == BonesMode.Animate;
        var pose = _stage.Pose?[bone.Id];
        _inspector.Children.Add(Heading("Bone"));
        _nameBox = new TextBox { Text = bone.Name, Name = "BonesBoneName" };
        _nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _stage.Focus();
                e.Handled = true;
            }
        };
        _nameBox.LostFocus += (_, _) =>
        {
            var text = _nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(text) || text == bone.Name) return;
            Edit(q =>
            {
                var b = q.Find(bone.Id)!;
                b.Name = q.Bones.Any(o => o != b && o.Name == text) ? q.UniqueName(text) : text;
            });
        };
        _inspector.Children.Add(_nameBox);
        var parent = Menus.MenuButton(UiKit.Content("figure", p.Find(bone.ParentId)?.Name ?? "None (a root)"), () => ParentEntries(bone), "What this bone hangs from: it moves and turns with it", 240);
        parent.Name = "BonesParent";
        parent.HorizontalAlignment = HorizontalAlignment.Stretch;
        _inspector.Children.Add(Field("Parent", parent));

        _inspector.Children.Add(Subheading(animate ? $"Pose at frame {_frame + 1}" : "Rest pose"));
        double x = pose?.X ?? bone.X, y = pose?.Y ?? bone.Y, r = pose?.Rotation ?? bone.Rotation, sx = pose?.ScaleX ?? bone.ScaleX, sy = pose?.ScaleY ?? bone.ScaleY;
        void PoseTo(double nx, double ny, double nr, double nsx, double nsy) => Edit(q =>
        {
            var b = q.Find(bone.Id)!;
            if (animate) q.SetPoseKey(b, _frame, nx, ny, nr, nsx, nsy);
            else (b.X, b.Y, b.Rotation, b.ScaleX, b.ScaleY) = (nx, ny, nr, nsx, nsy);
        });
        _inspector.Children.Add(UiKit.Row(6, Number("X", x, v => PoseTo(v, y, r, sx, sy), "BonesX"), Number("Y", y, v => PoseTo(x, v, r, sx, sy), "BonesY")));
        _inspector.Children.Add(UiKit.Row(6, Number("Turn", BonesStage.Normal(r), v => PoseTo(x, y, v, sx, sy), "BonesRotation", "°"),
            animate ? new Control() : Number("Length", bone.Length, v => Edit(q => q.Find(bone.Id)!.Length = Math.Max(1, v)), "BonesLength")));
        _inspector.Children.Add(UiKit.Row(6, Number("Scale", sx * 100, v => PoseTo(x, y, r, v / 100, sy), "BonesScaleX", "%"), Number("× ", sy * 100, v => PoseTo(x, y, r, sx, v / 100), "BonesScaleY", "%")));
        if (animate)
        {
            var key = p.TrackOf(bone.Id)?.Keys.FirstOrDefault(k => k.Frame == _frame);
            _inspector.Children.Add(Dim(key is not null
                ? $"◆ A key at frame {_frame + 1}{(key.Smooth ? ", easing to the next" : "")}."
                : p.TrackOf(bone.Id)?.Keys.Count > 0 ? "Between keys: changing the pose keys it here." : "No keys yet: changing the pose keys it here (and keeps the rest pose at frame 1).", "BonesKeyState"));
        }

        // Sprites.
        _inspector.Children.Add(Subheading("Sprites"));
        var shown = pose?.Attachment;
        if (bone.Attachments.Count == 0)
            _inspector.Children.Add(Dim("No sprite yet: add a picture or a DoodleNook drawing (still or animated), or draw a new one."));
        foreach (var a in bone.Attachments) _inspector.Children.Add(SpriteRow(p, bone, a, shown));
        var add = UiKit.Button("open", "Add…", "Link a drawing (.dnk, still or animated) or a picture (PNG, JPEG, WebP…) to this bone. The file isn't changed.", () => _ = AddSpriteAsync(bone));
        add.Name = "BonesAddSprite";
        var draw = UiKit.Button("brush", "New drawing…", "A new transparent drawing for this bone, opened in the editor", () => _ = _app.NewBoneDrawingAsync(bone));
        draw.Name = "BonesNewDrawing";
        _inspector.Children.Add(StackRow(add, draw));
        if (bone.Attachments.Count > 0 && animate)
            _inspector.Children.Add(Dim($"Click ● on a sprite to show it from frame {_frame + 1} on (a switch key)."));
        else if (bone.Attachments.Count > 1)
            _inspector.Children.Add(Dim("● is the sprite shown in the rest pose. Switch sprites over time in Animate."));

        if (SelectedAttachment is { } sel) AddSpriteDetails(p, bone, sel);
    }

    private Control SpriteRow(SkeletonProject p, Bone bone, BoneAttachment a, BoneAttachment? shown)
    {
        bool animate = Mode == BonesMode.Animate;
        bool isShown = shown?.Id == a.Id;
        bool chosen = SelectedAttachment?.Id == a.Id;
        var sprite = _assets.Get(p, a);
        var problem = _assets.Problem(p, a);
        string info = problem is not null ? "⚠ " + (File.Exists(p.Resolve(a.Source)) ? "can't be read" : "missing")
            : sprite is { IsAnimated: true } s ? $"{s.Frames.Count} frames · {s.TotalMs / 1000:0.##} s{(a.Loop ? " loop" : " once")}" : "still";
        var dot = UiKit.Button(null, isShown ? "●" : "○", animate ? $"Show {a.Name} from frame {_frame + 1} on" : $"Show {a.Name} in the rest pose",
            () =>
            {
                if (animate) SwitchHere(bone, a);
                else Edit(q => q.Find(bone.Id)!.Attachment = a.Id);
            });
        dot.Padding = new Thickness(6, 0);
        dot.Height = 26;
        dot.Name = $"BonesShow_{a.Name}";
        var nameText = new StackPanel
        {
            Spacing = 0, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = a.Name, FontWeight = chosen ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 },
                InfoText(info, problem is not null),
            },
        };
        var row = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(nameText);
        var border = new Border
        {
            Child = row, CornerRadius = new CornerRadius(8), Padding = new Thickness(2, 2, 6, 2), Name = $"BonesSprite_{a.Name}",
            Background = chosen ? new SolidColorBrush(DnColors.Ui3) : new SolidColorBrush(DnColors.Ui2),
            BorderBrush = chosen ? new SolidColorBrush(DnColors.Accent) : Brushes.Transparent, BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(border, problem ?? a.Source);
        border.PointerPressed += (_, e) =>
        {
            SelectAttachment(a);
            if (e.ClickCount >= 2) _ = OpenSpriteAsync(bone, a);
            e.Handled = true;
        };
        return border;
    }

    private void AddSpriteDetails(SkeletonProject p, Bone bone, BoneAttachment a)
    {
        var sprite = _assets.Get(p, a);
        _inspector.Children.Add(Subheading(a.Name));
        if (_assets.Problem(p, a) is { } problem)
        {
            _inspector.Children.Add(Warn(problem + " The sprite is kept; find the file again with Replace, or remove it."));
        }
        void Change(Action<BoneAttachment> change) => Edit(q =>
        {
            if (q.FindAttachment(q.Find(bone.Id)!, a.Id) is { } att) change(att);
        });
        _inspector.Children.Add(UiKit.Row(6, Number("X", a.X, v => Change(t => t.X = v), "BonesSpriteX"), Number("Y", a.Y, v => Change(t => t.Y = v), "BonesSpriteY")));
        _inspector.Children.Add(UiKit.Row(6, Number("Turn", a.Rotation, v => Change(t => t.Rotation = v), "BonesSpriteRotation", "°"),
            Number("Scale", a.Scale * 100, v => Change(t => t.Scale = Math.Clamp(v / 100, 0.01, 100)), "BonesSpriteScale", "%")));
        var (turn, _) = UiKit.Segmented([(-90.0, null, "−90°", "Turn the sprite a quarter left"), (90.0, null, "+90°", "Turn the sprite a quarter right"), (180.0, null, "180°", "Turn it round")],
            () => double.NaN, v => Change(t => t.Rotation = BonesStage.Normal(t.Rotation + v)), compact: true);
        _inspector.Children.Add(turn);
        if (sprite is { IsAnimated: true } anim)
        {
            _inspector.Children.Add(Dim($"Animated: {anim.Frames.Count} frames, {anim.TotalMs / 1000:0.##} s per pass at its own speed (whatever the timeline's fps)."));
            var (loop, _) = UiKit.Segmented([(true, "loop", "Loop", "Round and round while it shows"), (false, null, "Play once", "Once, then it keeps its last frame")],
                () => a.Loop, v => Change(t => t.Loop = v), compact: true);
            loop.Name = "BonesSpriteLoop";
            _inspector.Children.Add(loop);
            var (restart, _) = UiKit.Segmented([(true, null, "Restart", "From its first frame each time a key switches to it"), (false, null, "Keep running", "As if it had been playing since frame 1")],
                () => a.Restart, v => Change(t => t.Restart = v), compact: true);
            restart.Name = "BonesSpriteRestart";
            _inspector.Children.Add(restart);
            Action refreshSpeed = () => { };
            var (slider, rs) = UiKit.Slider("Speed ", 0.25, 4, () => a.Speed, v =>
            {
                if (_changeStart is null) BeginChange();
                a.Speed = Math.Round(v * 4) / 4;
                refreshSpeed();
                RefreshLive();
            }, v => $"{v:0.##}×", 110, log: true, finished: EndChange,
                tip: "How fast it plays: 1× is the drawing's own timing");
            refreshSpeed = rs;
            slider.Name = "BonesSpriteSpeed";
            _inspector.Children.Add(slider);
        }
        bool dnk = a.Source.EndsWith(Engine.Files.DnkFile.Extension, StringComparison.OrdinalIgnoreCase);
        var open = UiKit.Button("brush", dnk ? "Open in editor" : "Make editable…",
            dnk ? "Draw on this sprite in the normal editor; “Back to Bare Bones” returns here" : "Make an editable DoodleNook drawing from this picture (the picture isn't changed) and open it",
            () => _ = OpenSpriteAsync(bone, a));
        open.Name = "BonesOpenSprite";
        open.Classes.Add("solid");
        var replace = UiKit.Button("open", "Replace…", "Link another file instead (keeps the placement and keys)", () => _ = ReplaceSpriteAsync(bone, a));
        replace.Name = "BonesReplaceSprite";
        var remove = UiKit.Button("close", "Remove", "Remove the sprite from the bone, with its switch keys (the file isn't touched)", () =>
        {
            Edit(q =>
            {
                var b = q.Find(bone.Id)!;
                if (q.FindAttachment(b, a.Id) is { } att) q.RemoveAttachment(b, att);
            });
            _attachmentId = null;
            FillInspector();
        });
        remove.Name = "BonesRemoveSprite";
        _inspector.Children.Add(Stack(open));
        _inspector.Children.Add(StackRow(replace, remove));
    }

    // ================================================================== sprites (files)

    public async Task AddSpriteAsync(Bone bone)
    {
        var path = await _app.PickSpriteFileAsync("A sprite for " + bone.Name);
        if (path is null) return;
        AddSpriteFile(bone, path);
    }

    /// <summary>Links a file to a bone (tests call this directly).</summary>
    public BoneAttachment? AddSpriteFile(Bone bone, string path)
    {
        string? id = null;
        Edit(p =>
        {
            var b = p.Find(bone.Id)!;
            var att = p.AddAttachment(b, path);
            att.Rotation = UprightOn(p, b); // shown the way it was drawn
            id = att.Id;
        });
        if (Selected?.Id != bone.Id) Select(_project?.Find(bone.Id));
        var made = _project?.Find(bone.Id) is { } b ? _project.FindAttachment(b, id) : null;
        if (made is not null)
        {
            _attachmentId = made.Id;
            FillInspector();
            _stage.InvalidateVisual();
            if (_assets.Problem(_project!, made) is { } problem) _app.ShowToast(problem);
            else _app.ShowToast($"{made.Name} is on {bone.Name} · drag it on the stage (Setup) to place it, or use the fields");
        }
        return made;
    }

    /// <summary>The sprite turn that shows a drawing upright on screen on this bone in the rest pose.</summary>
    public static double UprightOn(SkeletonProject p, Bone bone)
    {
        var m = p.RestWorld(bone);
        return BonesStage.Normal(-Math.Atan2(m.M12, m.M11) * 180 / Math.PI);
    }

    private async Task ReplaceSpriteAsync(Bone bone, BoneAttachment a)
    {
        var path = await _app.PickSpriteFileAsync("Replace " + a.Name);
        if (path is null) return;
        Edit(p =>
        {
            if (p.FindAttachment(p.Find(bone.Id)!, a.Id) is { } att) att.Source = p.Relate(path);
        });
    }

    private async Task OpenSpriteAsync(Bone bone, BoneAttachment a)
    {
        if (_project is not { } p || p.Resolve(a.Source) is not { } path) return;
        if (!File.Exists(path))
        {
            _app.ShowToast($"{Path.GetFileName(path)} is missing: “Replace…” links it again");
            return;
        }
        if (!path.EndsWith(Engine.Files.DnkFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var copy = await _app.EditableCopyAsync(path);
            if (copy is null) return;
            Edit(q =>
            {
                if (q.FindAttachment(q.Find(bone.Id)!, a.Id) is { } att) att.Source = q.Relate(copy);
            });
            path = copy;
        }
        await _app.OpenBonesSpriteAsync(path);
    }

    private static Engine.Imaging.RasterImage? DecodePicture(string path)
    {
        try
        {
            return Files.ImageFiles.Decode(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Forgets loaded pictures (coming back from the editor: files may have been saved).</summary>
    internal void ReloadPictures()
    {
        _assets.Clear();
        Rebuild();
    }

    // ================================================================== little builders

    private static Control SectionHeader(string title, params Button[] buttons)
    {
        foreach (var b in buttons) b.Height = 26;
        var row = UiKit.Row(4, buttons);
        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(row, Dock.Right);
        dock.Children.Add(row);
        dock.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return dock;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };

    private static TextBlock InfoText(string text, bool warning)
    {
        var t = new TextBlock { Text = text, Classes = { "dim" }, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        if (warning) t.Foreground = new SolidColorBrush(Color.Parse("#F2C14E"));
        return t;
    }

    private static TextBlock Subheading(string text) => new() { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    private static TextBlock Dim(string text, string? name = null) => new() { Text = text, Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Name = name };

    private static Border Warn(string text) => new()
    {
        Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#F2C14E")) },
        Background = new SolidColorBrush(Color.FromArgb(40, 242, 193, 78)),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(8, 6),
        Name = "BonesWarning",
    };

    private static Control Field(string label, Control control)
    {
        var dock = new DockPanel();
        var l = new TextBlock { Text = label, Classes = { "dim" }, Width = 52, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        dock.Children.Add(l);
        dock.Children.Add(control);
        return dock;
    }

    /// <summary>A small number box: Enter or leaving it applies the value (one undo step).</summary>
    private static Control Number(string label, double value, Action<double> set, string name, string unit = "")
    {
        var box = new TextBox { Text = BonesStage.Format(value), Width = 62, Name = name, HorizontalContentAlignment = HorizontalAlignment.Right };
        void Apply()
        {
            if (double.TryParse(box.Text?.Replace(unit, "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double v) && double.IsFinite(v) &&
                Math.Abs(v - value) > 1e-9)
                set(v);
            else box.Text = BonesStage.Format(value);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Apply();
            e.Handled = true;
        };
        box.LostFocus += (_, _) => Apply();
        var row = UiKit.Row(3, new TextBlock { Text = label, Classes = { "dim" }, Width = 46, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, FontSize = 12.5 },
            box, new TextBlock { Text = unit, Classes = { "dim" }, Width = 12, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static StackPanel Stack(params Control[] items)
    {
        var s = new StackPanel { Spacing = 4 };
        foreach (var c in items)
        {
            if (c is Button b) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            s.Children.Add(c);
        }
        return s;
    }

    private static Grid StackRow(Button a, Button b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,*") };
        a.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(b, 2);
        g.Children.Add(a);
        g.Children.Add(b);
        return g;
    }
}
