using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Bones;

namespace DoodleNook.App.Bones;

/// <summary>
/// Bare Bones' master timeline: a ruler of frames with the playhead and one row per bone (in the hierarchy's order).
/// A pose key is a diamond (round when it eases in and out), a sprite switch is a small tag followed by the sprite's
/// name. Click a cell to go to that frame and bone, drag the ruler to scrub, drag a key along its row to move it, and
/// right-click for key commands. The wheel scrolls the bones; Shift+wheel scrolls the frames.
/// </summary>
public sealed class BonesTimeline : Control
{
    public const double NameWidth = 150, RulerHeight = 22, RowHeight = 19;

    private static readonly Typeface Font = new(new FontFamily("fonts:Inter#Inter, $Default"));
    private static readonly IBrush Bg = new SolidColorBrush(DnColors.Ui);
    private static readonly IBrush HeaderBg = new SolidColorBrush(DnColors.Ui2);
    private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.FromArgb(40, DnColors.Accent.R, DnColors.Accent.G, DnColors.Accent.B));
    private static readonly IBrush ActiveRowBrush = new SolidColorBrush(Color.Parse("#2BE9A94D"));
    private static readonly IBrush KeyBrush = new SolidColorBrush(DnColors.Text);
    private static readonly IBrush SwitchBrush = new SolidColorBrush(Color.Parse("#7CC4D8"));
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.FromArgb(55, 239, 231, 221));
    private static readonly IBrush Text2 = new SolidColorBrush(DnColors.Text2);
    private static readonly IBrush TextBrush = new SolidColorBrush(DnColors.Text);
    private static readonly IPen LinePen = new Pen(new SolidColorBrush(DnColors.Line), 1);
    private static readonly IPen KeyPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 26, 23)), 1);
    private static readonly IPen DropPen = new Pen(new SolidColorBrush(DnColors.Accent), 2);

    private readonly BareBonesView _view;
    private double _scrollX, _scrollY;
    private Mode _mode;
    private Bone? _keyBone;
    private int _keyFrom, _keyTo;

    private enum Mode { None, Scrub, MaybeMoveKey, MoveKey }

    public BonesTimeline(BareBonesView view)
    {
        _view = view;
        Name = "BonesTimeline";
        ClipToBounds = true;
        Cursor = Cursor.Default;
    }

    private SkeletonProject? Project => _view.Project;

    private List<Bone> Rows => Project?.Ordered() ?? [];

    private double CellWidth
    {
        get
        {
            int n = Math.Max(1, Project?.Length ?? 1);
            return Math.Clamp((Bounds.Width - NameWidth - 6) / n, 9, 28);
        }
    }

    private double FrameX(int f) => NameWidth + f * CellWidth - _scrollX;

    private int FrameAt(double x) => Math.Clamp((int)Math.Floor((x - NameWidth + _scrollX) / CellWidth), 0, Math.Max(0, (Project?.Length ?? 1) - 1));

    private int RowAt(double y) => (int)Math.Floor((y - RulerHeight + _scrollY) / RowHeight);

    private static FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Font.FontFamily, FontStyle.Normal, weight), size, brush);

    public void Refresh()
    {
        ClampScroll();
        InvalidateVisual();
    }

    /// <summary>Keeps the playhead in sight (while playing or stepping).</summary>
    public void Follow(int frame)
    {
        double x = NameWidth + frame * CellWidth;
        double visible = Bounds.Width - NameWidth;
        if (x - _scrollX < NameWidth) _scrollX = Math.Max(0, x - NameWidth);
        else if (x + CellWidth - _scrollX > NameWidth + visible) _scrollX = x + CellWidth - NameWidth - visible;
        ClampScroll();
    }

    private void ClampScroll()
    {
        double content = (Project?.Length ?? 0) * CellWidth;
        _scrollX = Math.Clamp(_scrollX, 0, Math.Max(0, content - (Bounds.Width - NameWidth - 4)));
        _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, Rows.Count * RowHeight - (Bounds.Height - RulerHeight)));
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.FillRectangle(Bg, new Rect(size));
        if (Project is not { } p) return;
        var rows = Rows;
        double cw = CellWidth;
        int current = _view.Frame;
        int first = Math.Max(0, (int)Math.Floor(_scrollX / cw)), last = Math.Min(p.Length - 1, (int)Math.Ceiling((_scrollX + size.Width - NameWidth) / cw));
        var selected = _view.Selected;
        bool setup = _view.Mode == BonesMode.Setup;

        // Rows.
        using (ctx.PushClip(new Rect(0, RulerHeight, size.Width, Math.Max(0, size.Height - RulerHeight))))
        {
            for (int r = 0; r < rows.Count; r++)
            {
                double y = RulerHeight + r * RowHeight - _scrollY;
                if (y + RowHeight < RulerHeight || y > size.Height) continue;
                var bone = rows[r];
                bool active = bone.Id == selected?.Id;
                if (active) ctx.FillRectangle(ActiveRowBrush, new Rect(0, y, size.Width, RowHeight));
                ctx.DrawLine(LinePen, new Point(0, y + RowHeight - 0.5), new Point(size.Width, y + RowHeight - 0.5));
                // The name, indented by depth.
                double indent = 8 + Math.Min(6, p.Depth(bone)) * 10;
                var name = Text(bone.Name, 12, active ? TextBrush : Text2, active ? FontWeight.SemiBold : FontWeight.Normal);
                name.MaxTextWidth = NameWidth - indent - 6;
                name.MaxLineCount = 1;
                name.Trimming = TextTrimming.CharacterEllipsis;
                using (ctx.PushClip(new Rect(0, y, NameWidth, RowHeight)))
                    ctx.DrawText(name, new Point(indent, y + (RowHeight - name.Height) / 2));

                using (ctx.PushClip(new Rect(NameWidth, y, Math.Max(0, size.Width - NameWidth), RowHeight)))
                {
                    double cy = y + RowHeight / 2;
                    for (int f = first; f <= last; f++)
                        if (cw >= 12) ctx.FillRectangle(DotBrush, new Rect(FrameX(f) + cw / 2 - 1, cy - 1, 2, 2));
                    var track = p.TrackOf(bone.Id);
                    if (track is null) continue;
                    // Switch keys: a tag, then the sprite's name until the next switch.
                    for (int i = 0; i < track.Switches.Count; i++)
                    {
                        var sw = track.Switches[i];
                        double x = FrameX(sw.Frame);
                        double next = i + 1 < track.Switches.Count ? FrameX(track.Switches[i + 1].Frame) : FrameX(p.Length);
                        ctx.DrawRectangle(SwitchBrush, KeyPen, new RoundedRect(new Rect(x + cw / 2 - 4, cy - 7, 8, 5), 1.5));
                        var label = p.FindAttachment(bone, sw.Attachment)?.Name ?? "nothing";
                        var t = Text(label, 10, SwitchBrush);
                        if (next - x - cw / 2 - 6 > 18)
                            using (ctx.PushClip(new Rect(x + cw / 2 + 5, y, Math.Max(0, next - x - cw / 2 - 8), RowHeight)))
                                ctx.DrawText(t, new Point(x + cw / 2 + 6, y + 1));
                    }
                    // Pose keys.
                    foreach (var k in track.Keys)
                    {
                        if (_mode == Mode.MoveKey && bone == _keyBone && k.Frame == _keyFrom) continue;
                        DrawKey(ctx, FrameX(k.Frame) + cw / 2, cy + 3, k.Smooth, 1);
                    }
                    if (_mode == Mode.MoveKey && bone == _keyBone)
                    {
                        double x = FrameX(_keyTo);
                        ctx.DrawRectangle(null, DropPen, new Rect(x + 1, y + 2, cw - 2, RowHeight - 4));
                        var moving = track.Keys.FirstOrDefault(k => k.Frame == _keyFrom);
                        DrawKey(ctx, x + cw / 2, cy + 3, moving?.Smooth ?? false, 1);
                    }
                }
            }
        }

        // The playhead column.
        double px = FrameX(current);
        if (!setup && px + cw > NameWidth) ctx.FillRectangle(PlayheadBrush, new Rect(Math.Max(NameWidth, px), RulerHeight, cw, Math.Max(0, size.Height - RulerHeight)));

        // The ruler.
        ctx.FillRectangle(HeaderBg, new Rect(0, 0, size.Width, RulerHeight));
        ctx.DrawLine(LinePen, new Point(0, RulerHeight - 0.5), new Point(size.Width, RulerHeight - 0.5));
        var head = Text(setup ? "Setup pose" : $"Frame {current + 1} / {p.Length}", 11.5, setup ? Text2 : TextBrush, FontWeight.SemiBold);
        ctx.DrawText(head, new Point(8, (RulerHeight - head.Height) / 2));
        int step = cw >= 22 ? 1 : cw >= 14 ? 2 : 5;
        using (ctx.PushClip(new Rect(NameWidth, 0, Math.Max(0, size.Width - NameWidth), RulerHeight)))
        {
            for (int f = first; f <= last; f++)
            {
                double x = FrameX(f);
                bool isCurrent = f == current && !setup;
                if (isCurrent)
                    ctx.DrawRectangle(new SolidColorBrush(DnColors.Accent), null, new RoundedRect(new Rect(x + 1, 2, cw - 2, RulerHeight - 4), 4));
                if ((f + 1) % step == 0 || f == 0 || isCurrent)
                {
                    var t = Text((f + 1).ToString(CultureInfo.InvariantCulture), 10.5,
                        isCurrent ? new SolidColorBrush(DnColors.AccentInk) : Text2, isCurrent ? FontWeight.SemiBold : FontWeight.Normal);
                    ctx.DrawText(t, new Point(x + (cw - t.Width) / 2, (RulerHeight - t.Height) / 2));
                }
            }
        }
        ctx.DrawLine(LinePen, new Point(NameWidth - 0.5, 0), new Point(NameWidth - 0.5, size.Height));
        if (rows.Count == 0)
        {
            var t = Text("Bones appear here once you add them. Their keys show on their rows.", 12, Text2);
            ctx.DrawText(t, new Point(NameWidth + 12, RulerHeight + 10));
        }
    }

    private static void DrawKey(DrawingContext ctx, double x, double y, bool smooth, double scale)
    {
        double r = 4.5 * scale;
        if (smooth)
        {
            ctx.DrawEllipse(KeyBrush, KeyPen, new Point(x, y), r, r);
            return;
        }
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y - r - 0.5), true);
            c.LineTo(new Point(x + r + 0.5, y));
            c.LineTo(new Point(x, y + r + 0.5));
            c.LineTo(new Point(x - r - 0.5, y));
            c.EndFigure(true);
        }
        ctx.DrawGeometry(KeyBrush, KeyPen, g);
    }

    // ================================================================== input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Project is not { } p) return;
        var pt = e.GetCurrentPoint(this);
        var pos = pt.Position;
        e.Handled = true;
        _view.StopPlaying();
        bool right = pt.Properties.IsRightButtonPressed;
        var rows = Rows;
        int row = RowAt(pos.Y);
        var bone = pos.Y >= RulerHeight && row >= 0 && row < rows.Count ? rows[row] : null;

        if (pos.X < NameWidth)
        {
            if (bone is not null)
            {
                _view.Select(bone);
                if (right) Menus.ShowAt(this, _view.BoneMenu(bone).ToList(), 250);
            }
            return;
        }
        int frame = FrameAt(pos.X);
        if (pos.Y < RulerHeight || bone is null)
        {
            _view.SetFrame(frame);
            if (!right)
            {
                _mode = Mode.Scrub;
                e.Pointer.Capture(this);
            }
            return;
        }
        _view.Select(bone);
        _view.SetFrame(frame);
        if (right)
        {
            Menus.ShowAt(this, _view.KeyMenu(bone, frame).ToList(), 260);
            return;
        }
        if (p.KeyFrames(bone).Contains(frame))
        {
            _mode = Mode.MaybeMoveKey;
            _keyBone = bone;
            _keyFrom = _keyTo = frame;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Project is null) return;
        var pos = e.GetPosition(this);
        int frame = FrameAt(pos.X);
        switch (_mode)
        {
            case Mode.Scrub:
                _view.SetFrame(frame);
                break;
            case Mode.MaybeMoveKey when frame != _keyFrom:
            case Mode.MoveKey:
                _mode = Mode.MoveKey;
                _keyTo = frame;
                InvalidateVisual();
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == Mode.MoveKey && _keyBone is { } bone && _keyTo != _keyFrom)
        {
            _view.MoveKeys(bone, _keyFrom, _keyTo);
            _view.SetFrame(_keyTo);
        }
        _mode = Mode.None;
        _keyBone = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _mode = Mode.None;
        _keyBone = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
            _scrollX -= (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y) * CellWidth * 3;
        else _scrollY -= e.Delta.Y * RowHeight * 2;
        Refresh();
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Refresh();
    }
}
