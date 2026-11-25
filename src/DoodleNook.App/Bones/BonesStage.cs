using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Bones;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Bones;

/// <summary>
/// Bare Bones' stage: the character as it will be exported (its sprites placed by the bones), with the bones drawn on
/// top only here, never in a picture or an export. Drag a joint to move a bone, the bone (or, while animating, its
/// sprite) to turn it about its joint, the tip of the selected bone to change its length, and in Setup the selected
/// bone's sprite to place it on the bone. With "Add bones", drag to draw a new bone from the selected one. Shift turns
/// in 15° steps. The wheel zooms, the middle button (or Space) drags the view, double-click an empty spot to fit.
/// </summary>
public sealed class BonesStage : Control
{
    private const double JointRadius = 6, Grab = 8;

    private static readonly Typeface Font = new(new FontFamily("fonts:Inter#Inter, $Default"));
    private static readonly IBrush BoneFill = new SolidColorBrush(Color.FromArgb(120, 250, 246, 238));
    private static readonly IBrush BoneFillSelected = new SolidColorBrush(Color.FromArgb(220, DnColors.Accent.R, DnColors.Accent.G, DnColors.Accent.B));
    private static readonly IPen BonePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 34, 28, 24)), 1.4);
    private static readonly IPen ParentPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 250, 246, 238)), 1, new DashStyle([3, 3], 0));
    private static readonly IPen SpritePen = new Pen(new SolidColorBrush(DnColors.Accent), 1.2, new DashStyle([4, 3], 0));
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1);
    private static readonly IPen NewBonePen = new Pen(new SolidColorBrush(DnColors.Accent), 2.5, lineCap: PenLineCap.Round);
    private static readonly IBrush Checker = UiKit.CheckerBrush();

    private readonly BareBonesView _view;
    private WriteableBitmap? _bitmap;
    private bool _fit = true;
    private double _scale = 1;
    private Point _origin;

    // A drag in progress.
    private DragKind _drag;
    private Bone? _dragBone;
    private Point _pressDoc, _nowDoc;
    private Affine _parentWorld = Affine.Identity, _boneWorld = Affine.Identity;
    private (double X, double Y, double Rotation, double ScaleX, double ScaleY, double Length) _start;
    private (double X, double Y) _startAttachment;
    private BoneAttachment? _dragAttachment;
    private Point _panFrom;
    private bool _spaceHeld;

    internal enum DragKind { None, Move, Turn, Tip, Sprite, NewBone, Pan }

    public BonesStage(BareBonesView view)
    {
        _view = view;
        Name = "BonesStage";
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>The character at the frame on show (also used for hit tests).</summary>
    public SkeletonPose? Pose { get; private set; }

    /// <summary>The picture shown (what an export of this frame gives).</summary>
    public RasterImage? Picture { get; private set; }

    /// <summary>Works out and draws the frame again (after any change).</summary>
    public void Refresh()
    {
        if (_view.Project is not { } p)
        {
            Pose = null;
            Picture = null;
            InvalidateVisual();
            return;
        }
        Pose = _view.EvaluateShown();
        Picture = SkeletonRenderer.Render(p, Pose, _view.Assets);
        if (_bitmap is null || _bitmap.PixelSize.Width != Picture.Width || _bitmap.PixelSize.Height != Picture.Height)
            _bitmap = new WriteableBitmap(new PixelSize(Picture.Width, Picture.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = _bitmap.Lock())
        {
            unsafe
            {
                fixed (uint* src = Picture.Pixels)
                {
                    for (int y = 0; y < Picture.Height; y++)
                        Buffer.MemoryCopy(src + (long)y * Picture.Width, (byte*)fb.Address + (long)y * fb.RowBytes, fb.RowBytes, Picture.Width * 4L);
                }
            }
        }
        InvalidateVisual();
    }

    public void FitToView()
    {
        _fit = true;
        InvalidateVisual();
    }

    // ================================================================== view transform

    private (double Scale, Point Origin) View()
    {
        if (_view.Project is not { } p) return (1, default);
        if (_fit)
        {
            // Room above for the stage's tools and below for the hint line.
            const double top = 50, bottom = 30;
            double s = Math.Min((Bounds.Width - 32) / p.Width, (Bounds.Height - top - bottom) / p.Height);
            s = Math.Max(0.05, s);
            if (p.PixelArt && s > 1) s = Math.Floor(s);
            _scale = s;
            _origin = new Point(Math.Round((Bounds.Width - p.Width * s) / 2), Math.Round(top + (Bounds.Height - top - bottom - p.Height * s) / 2));
        }
        return (_scale, _origin);
    }

    public Point ToScreen(double x, double y)
    {
        var (s, o) = View();
        return new Point(o.X + x * s, o.Y + y * s);
    }

    public Point ToDoc(Point screen)
    {
        var (s, o) = View();
        return new Point((screen.X - o.X) / s, (screen.Y - o.Y) / s);
    }

    private Point Screen((double X, double Y) doc) => ToScreen(doc.X, doc.Y);

    // ================================================================== drawing

    public override void Render(DrawingContext ctx)
    {
        if (_view.Project is not { } p) return;
        var (s, o) = View();
        var rect = new Rect(o.X, o.Y, p.Width * s, p.Height * s);
        ctx.DrawRectangle(Checker, FramePen, rect);
        if (_bitmap is not null)
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = p.PixelArt || s >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
                ctx.DrawImage(_bitmap, new Rect(0, 0, p.Width, p.Height), rect);
        if (Pose is null) return;

        var selected = _view.Selected;
        // The selected bone's sprite outline while setting up (where the sprite sits on its bone).
        if (_view.Mode == BonesMode.Setup && selected is not null && Pose[selected.Id] is { Attachment: { } a } sp && _view.Assets.Get(p, a) is { } sprite)
        {
            var m = sp.SpriteMatrix(sprite.Width, sprite.Height);
            var pts = new[] { m.Apply(0, 0), m.Apply(sprite.Width, 0), m.Apply(sprite.Width, sprite.Height), m.Apply(0, sprite.Height) };
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(Screen(pts[0]), false);
                for (int i = 1; i < 4; i++) c.LineTo(Screen(pts[i]));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(null, SpritePen, g);
        }

        if (_view.ShowBones)
        {
            // Dashed links from a parent's end to a child that doesn't start there.
            foreach (var bp in Pose.Bones)
                if (bp.Bone.ParentId is { } pid && Pose[pid] is { } parent)
                {
                    var a0 = Screen(parent.End);
                    var b0 = Screen(bp.Start);
                    if (Dist(a0, b0) > 4) ctx.DrawLine(ParentPen, a0, b0);
                }
            foreach (var bp in Pose.Bones.OrderBy(b => b.Bone.Id == selected?.Id))
                DrawBone(ctx, bp, bp.Bone.Id == selected?.Id);
        }

        if (_drag == DragKind.NewBone)
        {
            var a0 = ToScreen(_pressDoc.X, _pressDoc.Y);
            var b0 = ToScreen(_nowDoc.X, _nowDoc.Y);
            ctx.DrawLine(NewBonePen, a0, b0);
            ctx.DrawEllipse(BoneFillSelected, BonePen, a0, JointRadius, JointRadius);
        }
    }

    private void DrawBone(DrawingContext ctx, BonePose bp, bool selected)
    {
        var a = Screen(bp.Start);
        var b = Screen(bp.End);
        double len = Dist(a, b);
        var fill = selected ? BoneFillSelected : BoneFill;
        if (len > 2)
        {
            double ux = (b.X - a.X) / len, uy = (b.Y - a.Y) / len;
            double w = Math.Clamp(len * 0.1, 3, 9);
            var mid = new Point(a.X + ux * Math.Min(len * 0.22, 26), a.Y + uy * Math.Min(len * 0.22, 26));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(a, true);
                c.LineTo(new Point(mid.X - uy * w, mid.Y + ux * w));
                c.LineTo(b);
                c.LineTo(new Point(mid.X + uy * w, mid.Y - ux * w));
                c.EndFigure(true);
            }
            ctx.DrawGeometry(fill, BonePen, g);
        }
        ctx.DrawEllipse(fill, BonePen, a, JointRadius, JointRadius);
        if (selected && _view.Mode == BonesMode.Setup)
            ctx.DrawRectangle(BoneFillSelected, BonePen, new Rect(b.X - 4.5, b.Y - 4.5, 9, 9)); // the tip: length
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ================================================================== hit testing

    /// <summary>What is under a point on the stage: a joint, the selected bone's tip, a bone, or a sprite (front first).</summary>
    internal (DragKind Kind, Bone? Bone) HitTest(Point screen)
    {
        if (Pose is null || _view.Project is not { } p) return (DragKind.None, null);
        var selected = _view.Selected;
        if (_view.ShowBones)
        {
            if (selected is not null && Pose[selected.Id] is { } sel)
            {
                if (_view.Mode == BonesMode.Setup && Dist(screen, Screen(sel.End)) <= Grab) return (DragKind.Tip, selected);
                if (Dist(screen, Screen(sel.Start)) <= Grab) return (DragKind.Move, selected);
            }
            BonePose? best = null;
            double bestD = double.MaxValue;
            foreach (var bp in Pose.Bones)
            {
                double d = Dist(screen, Screen(bp.Start));
                if (d <= Grab && d < bestD) (best, bestD) = (bp, d);
            }
            if (best is not null) return (DragKind.Move, best.Bone);
            foreach (var bp in Pose.Bones)
            {
                double d = SegmentDistance(screen, Screen(bp.Start), Screen(bp.End));
                if (d <= Grab - 2 && d < bestD) (best, bestD) = (bp, d);
            }
            if (best is not null) return (DragKind.Turn, best.Bone);
        }
        // Sprites, front first: an opaque pixel under the pointer.
        var doc = ToDoc(screen);
        foreach (var bone in Enumerable.Reverse(p.BackToFront()))
        {
            if (Pose[bone.Id] is not { Attachment: { } a } bp || _view.Assets.Get(p, a) is not { } sprite) continue;
            var m = bp.SpriteMatrix(sprite.Width, sprite.Height);
            if (!m.IsInvertible) continue;
            var (u, v) = m.Invert().Apply(doc.X, doc.Y);
            int ix = (int)Math.Floor(u), iy = (int)Math.Floor(v);
            var frame = sprite.Frames[Math.Clamp(bp.AttachmentFrame, 0, sprite.Frames.Count - 1)];
            if (ix < 0 || iy < 0 || ix >= frame.Width || iy >= frame.Height) continue;
            if (frame.Pixels[iy * frame.Width + ix] >> 24 < 24) continue;
            bool place = _view.Mode == BonesMode.Setup && bone.Id == selected?.Id;
            return (place ? DragKind.Sprite : DragKind.Turn, bone);
        }
        return (DragKind.None, null);
    }

    private static double SegmentDistance(Point p, Point a, Point b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double t = Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / Math.Max(1e-9, vx * vx + vy * vy), 0, 1);
        return Dist(p, new Point(a.X + vx * t, a.Y + vy * t));
    }

    // ================================================================== input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_view.Project is null) return;
        var pt = e.GetCurrentPoint(this);
        var pos = pt.Position;
        e.Handled = true;
        _view.StopPlaying();
        if (pt.Properties.IsMiddleButtonPressed || (_spaceHeld && pt.Properties.IsLeftButtonPressed))
        {
            BeginPan(pos);
            e.Pointer.Capture(this);
            return;
        }
        if (pt.Properties.IsRightButtonPressed)
        {
            var (_, hit) = HitTest(pos);
            if (hit is not null) _view.Select(hit);
            if (_view.Selected is not null) Menus.ShowAt(this, _view.BoneMenu(_view.Selected).ToList(), 250);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;
        var doc = ToDoc(pos);
        _pressDoc = _nowDoc = doc;

        if (_view.Tool == BonesTool.AddBone)
        {
            // Chaining: pressing on the selected bone's end starts the new bone exactly there.
            if (_view.Selected is { } sel && Pose?[sel.Id] is { } sp && Dist(pos, Screen(sp.End)) <= Grab * 1.5)
                _pressDoc = _nowDoc = new Point(sp.End.X, sp.End.Y);
            _drag = DragKind.NewBone;
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        var (kind, bone) = HitTest(pos);
        if (bone is null)
        {
            if (e.ClickCount >= 2) FitToView();
            _view.Select(null);
            return;
        }
        if (bone.Id != _view.Selected?.Id) _view.Select(bone);
        if (Pose?[bone.Id] is not { } bpose) return;
        _drag = kind;
        _dragBone = bone;
        var parent = bone.ParentId is { } pid ? Pose[pid] : null;
        _parentWorld = parent?.World ?? Affine.Identity;
        _boneWorld = bpose.World;
        _start = (bpose.X, bpose.Y, bpose.Rotation, bpose.ScaleX, bpose.ScaleY, bone.Length);
        _dragAttachment = bpose.Attachment;
        _startAttachment = bpose.Attachment is { } att ? (att.X, att.Y) : (0, 0);
        _view.BeginChange();
        e.Pointer.Capture(this);
        _view.Hint(DragHint(kind, bone));
    }

    private void BeginPan(Point pos)
    {
        var (s, o) = View();
        _fit = false;
        _scale = s;
        _origin = o;
        _panFrom = pos;
        _drag = DragKind.Pan;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_drag == DragKind.None)
        {
            if (_view.Project is null) return;
            var (hoverKind, hoverBone) = _view.Tool == BonesTool.AddBone ? (DragKind.NewBone, null) : HitTest(pos);
            Cursor = new Cursor(hoverKind switch
            {
                DragKind.Move or DragKind.Sprite => StandardCursorType.SizeAll,
                DragKind.Turn or DragKind.Tip => StandardCursorType.Hand,
                DragKind.NewBone => StandardCursorType.Cross,
                _ => StandardCursorType.Arrow,
            });
            _view.Hint(hoverBone is null ? null : DragHint(hoverKind, hoverBone));
            return;
        }
        if (_drag == DragKind.Pan)
        {
            _origin += pos - _panFrom;
            _panFrom = pos;
            InvalidateVisual();
            return;
        }
        var doc = ToDoc(pos);
        _nowDoc = doc;
        if (_drag == DragKind.NewBone)
        {
            InvalidateVisual();
            _view.Hint($"New bone · {Dist(_pressDoc, doc):0} px");
            return;
        }
        if (_dragBone is not { } bone) return;
        bool snap = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var (x, y, r, sx, sy, length) = _start;
        switch (_drag)
        {
            case DragKind.Move:
            {
                // The joint follows the pointer (in the parent's coordinates).
                var inv = _parentWorld.IsInvertible ? _parentWorld.Invert() : Affine.Identity;
                var (ax, ay) = inv.Apply(_pressDoc.X, _pressDoc.Y);
                var (bx, by) = inv.Apply(doc.X, doc.Y);
                x += bx - ax;
                y += by - ay;
                _view.SetPose(bone, x, y, r, sx, sy);
                _view.Hint($"{bone.Name} · moved to {x:0}, {y:0}");
                break;
            }
            case DragKind.Turn:
            case DragKind.Tip:
            {
                var s0 = _boneWorld.Apply(0, 0);
                double a0 = Angle(s0, _drag == DragKind.Tip ? _boneWorld.Apply(bone.Length, 0) : (_pressDoc.X, _pressDoc.Y));
                double a1 = Angle(s0, (doc.X, doc.Y));
                double sign = _parentWorld.Determinant < 0 ? -1 : 1;
                double rot = r + sign * SkeletonEvaluator.ShortestTurn(a0, a1);
                if (snap) rot = Math.Round(rot / 15) * 15;
                if (_drag == DragKind.Tip)
                {
                    var (ex, ey) = _boneWorld.Apply(1, 0);
                    double unit = Math.Sqrt((ex - s0.X) * (ex - s0.X) + (ey - s0.Y) * (ey - s0.Y));
                    double newLength = Math.Max(2, Dist(new Point(s0.X, s0.Y), doc) / Math.Max(1e-6, unit));
                    _view.SetLength(bone, newLength);
                    _view.SetPose(bone, x, y, rot, sx, sy);
                    _view.Hint($"{bone.Name} · {newLength:0} px long, {Normal(rot):0}°");
                }
                else
                {
                    _view.SetPose(bone, x, y, rot, sx, sy);
                    _view.Hint($"{bone.Name} · {Normal(rot):0}°{(snap ? " (15° steps)" : " · Shift: 15° steps")}");
                }
                break;
            }
            case DragKind.Sprite:
            {
                if (_dragAttachment is not { } a || !_boneWorld.IsInvertible) break;
                var inv = _boneWorld.Invert();
                var (ax, ay) = inv.Apply(_pressDoc.X, _pressDoc.Y);
                var (bx, by) = inv.Apply(doc.X, doc.Y);
                _view.SetAttachmentOffset(bone, a, _startAttachment.X + bx - ax, _startAttachment.Y + by - ay);
                _view.Hint($"{a.Name} on {bone.Name} · at {a.X:0}, {a.Y:0}");
                break;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var kind = _drag;
        _drag = DragKind.None;
        e.Pointer.Capture(null);
        if (kind == DragKind.NewBone)
        {
            var end = _nowDoc;
            InvalidateVisual();
            _view.AddBoneDrawn((_pressDoc.X, _pressDoc.Y), (end.X, end.Y));
        }
        else if (kind is DragKind.Move or DragKind.Turn or DragKind.Tip or DragKind.Sprite) _view.EndChange();
        _dragBone = null;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag is DragKind.Move or DragKind.Turn or DragKind.Tip or DragKind.Sprite) _view.EndChange();
        _drag = DragKind.None;
        _dragBone = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_view.Project is null) return;
        var pos = e.GetPosition(this);
        var (s, o) = View();
        double ns = Math.Clamp(s * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 0.05, 32);
        // Zoom about the pointer: the point under it stays put.
        _origin = new Point(pos.X - (pos.X - o.X) * ns / s, pos.Y - (pos.Y - o.Y) * ns / s);
        _scale = ns;
        _fit = false;
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>Space held: dragging moves the view (set by the window, which sees the keys first).</summary>
    internal bool SpaceHeld
    {
        get => _spaceHeld;
        set
        {
            if (_spaceHeld == value) return;
            _spaceHeld = value;
            Cursor = new Cursor(value ? StandardCursorType.SizeAll : StandardCursorType.Arrow);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateVisual();
    }

    private string DragHint(DragKind kind, Bone bone) => kind switch
    {
        DragKind.Move => $"{bone.Name}: drag the joint to move the bone",
        DragKind.Tip => $"{bone.Name}: drag the tip to change its length and direction",
        DragKind.Sprite => $"{bone.Name}: drag the sprite to place it on its bone",
        DragKind.Turn => $"{bone.Name}: drag to turn it about its joint" + (_view.Mode == BonesMode.Animate ? $" (a key at frame {_view.Frame + 1})" : ""),
        _ => "",
    };

    private static double Angle((double X, double Y) from, (double X, double Y) to) => Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;

    private static double Dist(Point a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>An angle shown as −180…180.</summary>
    public static double Normal(double degrees)
    {
        double d = degrees % 360;
        if (d > 180) d -= 360;
        else if (d <= -180) d += 360;
        return d;
    }

    internal static string Format(double v) => (Math.Abs(v) < 0.05 ? 0 : v).ToString("0.#", CultureInfo.CurrentCulture);
}
