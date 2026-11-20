using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Animation;

/// <summary>
/// The timeline's grid: a ruler of frame numbers with the playhead, one row per layer (top layer first) with its name and
/// eye, and one cell per frame. A drawing held over several frames is drawn as one block; empty cells are faint dots.
/// Click a cell to go to that frame and layer; drag the ruler to scrub; drag a block's right edge to hold it longer or
/// shorter; drag a drawing onto another frame to move it; right-click for cel and frame commands. Wheel scrolls,
/// Ctrl+wheel zooms, Shift+wheel scrolls layers.
/// </summary>
public sealed class TimelineGrid : Control
{
    public const double HeaderWidth = 128;
    public const double RulerHeight = 22;
    public const double MinRow = 30;
    private const double HandleGrab = 9;

    private static readonly Typeface Font = new(new FontFamily("fonts:Inter#Inter, $Default"));
    private static readonly IBrush Bg = new SolidColorBrush(DnColors.Ui);
    private static readonly IBrush HeaderBg = new SolidColorBrush(DnColors.Ui2);
    private static readonly IBrush CelBrush = new SolidColorBrush(Color.Parse("#4A423B"));
    private static readonly IBrush CelActiveBrush = new SolidColorBrush(Color.Parse("#5A5047"));
    private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.FromArgb(34, DnColors.Accent.R, DnColors.Accent.G, DnColors.Accent.B));
    private static readonly IBrush ActiveRowBrush = new SolidColorBrush(Color.Parse("#2BE9A94D"));
    private static readonly IBrush DotBrush = new SolidColorBrush(Color.FromArgb(70, 239, 231, 221));
    private static readonly IBrush KeyBrush = new SolidColorBrush(DnColors.Text2);
    private static readonly Pen LinePen = new(new SolidColorBrush(DnColors.Line), 1);
    private static readonly Pen AccentPen = new(new SolidColorBrush(DnColors.Accent), 2);
    private static readonly Pen HoldPen = new(new SolidColorBrush(Color.FromArgb(150, 239, 231, 221)), 1.5);

    private readonly MainWindow _app;
    private readonly Dictionary<Cel, (int Revision, WriteableBitmap Bitmap)> _thumbs = new();
    private Document? _watched;
    private Mode _mode;
    private int _hoverFrame = -1;
    private Layer? _dragLayer;
    private int _dragFrom;
    private Point _pressAt;
    private int _dropTarget = -1;

    private enum Mode { None, Scrub, Hold, MaybeMove, Move }

    public TimelineGrid(MainWindow app)
    {
        _app = app;
        ClipToBounds = true;
        Focusable = false;
        Cursor = Cursor.Default;
        app.Session.DocumentReplaced += Watch;
        Watch();
    }

    public double ScrollX { get; private set; }
    public double ScrollY { get; private set; }

    /// <summary>Raised when the scroll position or content size changes (the panel's scroll bar follows it).</summary>
    public event Action? ScrollChanged;

    private Document Doc => _app.Session.Document;
    private double CellWidth => Math.Clamp(_app.AnimationState.TimelineCellWidth, 22, 96);
    private int FrameCount => Doc.FrameCount;
    private IReadOnlyList<Layer> Rows => Doc.Layers.Reverse().ToList();

    private double RowHeight
    {
        get
        {
            int rows = Math.Max(1, Doc.Layers.Count);
            double fill = (Bounds.Height - RulerHeight) / rows;
            return Math.Clamp(fill, MinRow, 64);
        }
    }

    private bool ShowThumbnails => _app.AnimationState.IsShown(CustomOptions.TimelineThumbnails, WorkspaceKind.Animation) && RowHeight >= 44 && CellWidth >= 30;

    public double ContentWidth => FrameCount * CellWidth + 24;
    public double ViewportWidth => Math.Max(0, Bounds.Width - HeaderWidth);

    private void Watch()
    {
        if (_watched is not null)
        {
            _watched.FrameChanged -= OnFrameChanged;
            _watched.AnimationChanged -= Refresh;
            _watched.LayersChanged -= Refresh;
            _watched.ActiveLayerChanged -= Refresh;
            _watched.HistoryChanged -= Refresh;
        }
        _watched = Doc;
        _watched.FrameChanged += OnFrameChanged;
        _watched.AnimationChanged += Refresh;
        _watched.LayersChanged += Refresh;
        _watched.ActiveLayerChanged += Refresh;
        _watched.HistoryChanged += Refresh;
        _thumbs.Clear();
        ScrollX = 0;
        ScrollY = 0;
        Refresh();
    }

    private void Refresh()
    {
        ClampScroll();
        InvalidateVisual();
        ScrollChanged?.Invoke();
    }

    private void OnFrameChanged()
    {
        EnsureVisible(Doc.CurrentFrame);
        Refresh();
    }

    /// <summary>Scrolls so a frame is visible (keeps the playhead in view while playing or stepping).</summary>
    public void EnsureVisible(int frame)
    {
        double x = frame * CellWidth;
        double view = ViewportWidth;
        if (view <= 0) return;
        if (x < ScrollX) ScrollX = x;
        else if (x + CellWidth > ScrollX + view) ScrollX = x + CellWidth - view;
        ClampScroll();
    }

    public void SetScrollX(double x)
    {
        ScrollX = x;
        ClampScroll();
        InvalidateVisual();
    }

    private void ClampScroll()
    {
        ScrollX = Math.Clamp(ScrollX, 0, Math.Max(0, ContentWidth - ViewportWidth));
        double content = Doc.Layers.Count * RowHeight;
        ScrollY = Math.Clamp(ScrollY, 0, Math.Max(0, content - (Bounds.Height - RulerHeight)));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Refresh();
    }

    // ------------------------------------------------------------------ geometry

    private double FrameX(int frame) => HeaderWidth + frame * CellWidth - ScrollX;
    private double RowY(int row) => RulerHeight + row * RowHeight - ScrollY;

    private int FrameAt(double x) => (int)Math.Floor((x - HeaderWidth + ScrollX) / CellWidth);

    private int RowAt(double y) => (int)Math.Floor((y - RulerHeight + ScrollY) / RowHeight);

    /// <summary>The centre of a layer's cell at a frame (tests and screenshots drive the timeline with it).</summary>
    internal Point CellCenter(Layer layer, int frame)
    {
        int row = Rows.ToList().IndexOf(layer);
        return new Point(FrameX(frame) + CellWidth / 2, RowY(row) + RowHeight / 2);
    }

    /// <summary>A point on the frame ruler above a frame.</summary>
    internal Point RulerPoint(int frame) => new(FrameX(frame) + CellWidth / 2, RulerHeight / 2);

    /// <summary>Where the hold handle of the drawing at a frame sits (the right end of its block).</summary>
    internal Point HoldHandlePoint(Layer layer, int frame)
    {
        var (_, end) = Doc.HoldRange(layer, frame);
        int row = Rows.ToList().IndexOf(layer);
        return new Point(FrameX(end) + CellWidth - 4, RowY(row) + RowHeight / 2);
    }

    internal double CellWidthNow => CellWidth;

    private Layer? LayerAtRow(int row) => row >= 0 && row < Doc.Layers.Count ? Doc.Layers[Doc.Layers.Count - 1 - row] : null;

    // ------------------------------------------------------------------ drawing

    private static FormattedText Text(string s, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Font.FontFamily, FontStyle.Normal, weight), size, brush);

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        ctx.DrawRectangle(Bg, null, new Rect(size));
        var doc = Doc;
        int frames = FrameCount;
        int current = doc.CurrentFrame;
        double cw = CellWidth, rh = RowHeight;
        int first = Math.Max(0, FrameAt(HeaderWidth)), last = Math.Min(frames - 1, FrameAt(size.Width));
        var rows = Rows;

        using (ctx.PushClip(new Rect(HeaderWidth, 0, Math.Max(0, size.Width - HeaderWidth), size.Height)))
        {
            // Playhead column.
            ctx.DrawRectangle(PlayheadBrush, null, new Rect(FrameX(current), 0, cw, size.Height));

            // Ruler.
            int step = cw >= 26 ? 1 : 5;
            for (int f = first; f <= last; f++)
            {
                double x = FrameX(f);
                bool isCurrent = f == current;
                if (isCurrent)
                    ctx.DrawRectangle(new SolidColorBrush(DnColors.Accent), null, new RoundedRect(new Rect(x + 2, 2, cw - 4, RulerHeight - 4), 5));
                if ((f + 1) % step == 0 || f == 0 || isCurrent)
                {
                    var t = Text((f + 1).ToString(CultureInfo.InvariantCulture), 11,
                        isCurrent ? new SolidColorBrush(DnColors.AccentInk) : new SolidColorBrush(DnColors.Text2), isCurrent ? FontWeight.SemiBold : FontWeight.Normal);
                    ctx.DrawText(t, new Point(x + (cw - t.Width) / 2, (RulerHeight - t.Height) / 2));
                }
            }
            ctx.DrawLine(LinePen, new Point(HeaderWidth, RulerHeight - 0.5), new Point(size.Width, RulerHeight - 0.5));

            // Cells.
            using (ctx.PushClip(new Rect(HeaderWidth, RulerHeight, Math.Max(0, size.Width - HeaderWidth), Math.Max(0, size.Height - RulerHeight))))
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    double y = RowY(r);
                    if (y + rh < RulerHeight || y > size.Height) continue;
                    var layer = rows[r];
                    bool activeRow = ReferenceEquals(layer, doc.ActiveLayer);
                    if (activeRow) ctx.DrawRectangle(ActiveRowBrush, null, new Rect(HeaderWidth, y, size.Width - HeaderWidth, rh));
                    DrawTrack(ctx, doc, layer, y, rh, cw, first, last, activeRow);
                    ctx.DrawLine(LinePen, new Point(HeaderWidth, y + rh - 0.5), new Point(size.Width, y + rh - 0.5));
                }
                // The current cell.
                int activeRowIndex = rows.ToList().IndexOf(doc.ActiveLayer);
                if (activeRowIndex >= 0)
                    ctx.DrawRectangle(null, AccentPen, new RoundedRect(new Rect(FrameX(current) + 1, RowY(activeRowIndex) + 1, cw - 2, rh - 2), 6));
                if (_mode == Mode.Move && _dropTarget >= 0 && _dragLayer is not null)
                {
                    int row = rows.ToList().IndexOf(_dragLayer);
                    ctx.DrawRectangle(null, new Pen(new SolidColorBrush(DnColors.Accent), 2, new DashStyle([3, 3], 0)),
                        new RoundedRect(new Rect(FrameX(_dropTarget) + 2, RowY(row) + 2, cw - 4, rh - 4), 6));
                }
            }
            // End-of-animation marker.
            double end = FrameX(frames);
            ctx.DrawLine(new Pen(new SolidColorBrush(DnColors.Line), 2), new Point(end, 0), new Point(end, size.Height));
        }

        // Layer headers.
        ctx.DrawRectangle(HeaderBg, null, new Rect(0, 0, HeaderWidth, size.Height));
        var plus = Text("+  Layer", 12, new SolidColorBrush(DnColors.Text2));
        ctx.DrawText(plus, new Point(10, (RulerHeight - plus.Height) / 2));
        using (ctx.PushClip(new Rect(0, RulerHeight, HeaderWidth, Math.Max(0, size.Height - RulerHeight))))
        {
            for (int r = 0; r < rows.Count; r++)
            {
                double y = RowY(r);
                var layer = rows[r];
                bool active = ReferenceEquals(layer, doc.ActiveLayer);
                if (active) ctx.DrawRectangle(ActiveRowBrush, null, new Rect(0, y, HeaderWidth, rh));
                var eye = Icons.Get(layer.Visible ? "eye" : "eyeoff");
                if (eye is not null)
                {
                    using (ctx.PushTransform(Matrix.CreateScale(16 / 24.0, 16 / 24.0) * Matrix.CreateTranslation(8, y + (rh - 16) / 2)))
                        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(layer.Visible ? DnColors.Text2 : Color.FromArgb(110, 169, 157, 145)), 1.8), eye);
                }
                var name = Text(layer.Name, 12.5, new SolidColorBrush(active ? DnColors.Text : DnColors.Text2), active ? FontWeight.SemiBold : FontWeight.Normal);
                name.MaxTextWidth = HeaderWidth - 40;
                name.MaxLineCount = 1;
                name.Trimming = TextTrimming.CharacterEllipsis;
                ctx.DrawText(name, new Point(32, y + (rh - name.Height) / 2));
                ctx.DrawLine(LinePen, new Point(0, y + rh - 0.5), new Point(HeaderWidth, y + rh - 0.5));
            }
        }
        ctx.DrawLine(LinePen, new Point(HeaderWidth - 0.5, 0), new Point(HeaderWidth - 0.5, size.Height));
    }

    private void DrawTrack(DrawingContext ctx, Document doc, Layer layer, double y, double rh, double cw, int first, int last, bool active)
    {
        var track = layer.Track;
        if (track.Count == 0)
        {
            // A still picture: its layer is the first (and only) drawing.
            if (first <= 0 && last >= 0) DrawRun(ctx, null, 0, 0, y, rh, cw, active, stillHasPixels: !layer.ContentBounds.IsEmpty);
            return;
        }
        int f = first;
        while (f > 0 && f <= last && ReferenceEquals(track[f - 1], track[f])) f--; // start of the run that is visible first
        while (f <= last)
        {
            var cel = track[f];
            int e = f;
            while (e + 1 < track.Count && ReferenceEquals(track[e + 1], cel)) e++;
            if (cel is null)
            {
                for (int k = Math.Max(f, first); k <= Math.Min(e, last); k++)
                    ctx.DrawEllipse(DotBrush, null, new Point(FrameX(k) + cw / 2, y + rh / 2), 2, 2);
            }
            else DrawRun(ctx, cel, f, e, y, rh, cw, active, false);
            f = e + 1;
        }
    }

    private void DrawRun(DrawingContext ctx, Cel? cel, int start, int end, double y, double rh, double cw, bool active, bool stillHasPixels)
    {
        if (cel is null && !stillHasPixels) return;
        var rect = new Rect(FrameX(start) + 2, y + 3, (end - start + 1) * cw - 4, rh - 6);
        ctx.DrawRectangle(active ? CelActiveBrush : CelBrush, null, new RoundedRect(rect, 6));
        var bmp = cel is not null && ShowThumbnails ? Thumbnail(cel, cw - 8, rh - 12) : null;
        if (bmp is not null)
        {
            double tw = bmp.PixelSize.Width, th = bmp.PixelSize.Height;
            var dest = new Rect(FrameX(start) + (cw - tw) / 2, y + (rh - th) / 2, tw, th);
            ctx.DrawRectangle(UiKit.CheckerBrush(), null, dest);
            ctx.DrawImage(bmp, dest);
        }
        else
        {
            // A key dot marks where a drawing starts.
            ctx.DrawEllipse(cel is { IsEmpty: true } ? null : KeyBrush, cel is { IsEmpty: true } ? new Pen(KeyBrush, 1.4) : null,
                new Point(FrameX(start) + cw / 2, y + rh / 2), 3.6, 3.6);
        }
        if (end > start)
        {
            // A held drawing: a line through the following frames.
            double x0 = FrameX(start) + cw - 2, x1 = FrameX(end) + cw - 10;
            ctx.DrawLine(HoldPen, new Point(x0, y + rh / 2), new Point(x1, y + rh / 2));
            ctx.DrawLine(HoldPen, new Point(x1, y + rh / 2 - 4), new Point(x1, y + rh / 2 + 4));
        }
        int current = Doc.CurrentFrame;
        bool handleShown = current >= start && current <= end || _hoverFrame >= start && _hoverFrame <= end;
        if (active && cel is not null && handleShown)
        {
            // The hold handle: drag to hold this drawing longer or shorter.
            double hx = FrameX(end) + cw - 5;
            ctx.DrawRectangle(new SolidColorBrush(DnColors.Text2), null, new RoundedRect(new Rect(hx - 1.5, y + rh / 2 - 6, 3, 12), 1.5));
        }
    }

    private WriteableBitmap? Thumbnail(Cel cel, double maxW, double maxH)
    {
        if (maxW < 8 || maxH < 8) return null;
        if (_thumbs.TryGetValue(cel, out var t) && t.Revision == cel.Revision) return t.Bitmap;
        var doc = Doc;
        double s = Math.Min(maxW / doc.Width, maxH / doc.Height);
        int w = Math.Max(1, (int)(doc.Width * s)), h = Math.Max(1, (int)(doc.Height * s));
        var img = new RasterImage(w, h);
        var src = cel.Image;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = src[Math.Min(doc.Width - 1, (int)((x + 0.5) / s)), Math.Min(doc.Height - 1, (int)((y + 0.5) / s))];
        var bmp = BrushPreview.ToBitmap(img);
        _thumbs[cel] = (cel.Revision, bmp);
        if (_thumbs.Count > 600) _thumbs.Clear();
        return bmp;
    }

    // ------------------------------------------------------------------ input

    private (Layer Layer, int Start, int End)? HoldHandleAt(Point p)
    {
        var layer = LayerAtRow(RowAt(p.Y));
        if (layer is null || !ReferenceEquals(layer, Doc.ActiveLayer) || layer.Track.Count == 0) return null;
        int frame = FrameAt(p.X + HandleGrab); // the handle sits at a run's right edge
        for (int f = Math.Max(0, frame - 1); f <= Math.Min(FrameCount - 1, frame); f++)
        {
            if (layer.Track[f] is null) continue;
            var (s, e) = Doc.HoldRange(layer, f);
            double edge = FrameX(e) + CellWidth;
            if (Math.Abs(p.X - edge) <= HandleGrab) return (layer, s, e);
        }
        return null;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverFrame < 0) return;
        _hoverFrame = -1;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        switch (_mode)
        {
            case Mode.None:
            {
                var handle = p.X > HeaderWidth && p.Y > RulerHeight ? HoldHandleAt(p) : null;
                Cursor = handle is not null ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
                // The hold handle shows on the selected drawing and on the drawing under the pointer.
                int hover = handle is { } h ? h.End
                    : p.X > HeaderWidth && p.Y > RulerHeight && ReferenceEquals(LayerAtRow(RowAt(p.Y)), Doc.ActiveLayer) ? FrameAt(p.X) : -1;
                if (hover != _hoverFrame)
                {
                    _hoverFrame = hover;
                    InvalidateVisual();
                }
                break;
            }
            case Mode.Scrub:
                _app.Session.GoToFrame(Math.Clamp(FrameAt(p.X), 0, FrameCount - 1));
                break;
            case Mode.Hold:
                DragHold(p);
                break;
            case Mode.MaybeMove:
                if (Math.Abs(p.X - _pressAt.X) > 8) _mode = Mode.Move;
                break;
            case Mode.Move:
                _dropTarget = Math.Clamp(FrameAt(p.X), 0, FrameCount - 1);
                InvalidateVisual();
                break;
        }
        if (_mode != Mode.None) e.Handled = true;
    }

    private void DragHold(Point p)
    {
        if (_dragLayer is null) return;
        int target = FrameAt(p.X - CellWidth / 2 + HandleGrab);
        var (s, e) = Doc.HoldRange(_dragLayer, _dragFrom);
        target = Math.Max(s, target);
        // One frame at a time, so every change is a normal undoable step.
        if (target > e) _app.Session.ExtendHold(_dragLayer, _dragFrom);
        else if (target < e && e > s) _app.Session.ShortenHold(_dragLayer, _dragFrom);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        var p = pt.Position;
        bool right = pt.Properties.IsRightButtonPressed || pt.Properties.IsBarrelButtonPressed;
        _app.Animator.Pause();
        e.Handled = true;

        if (p.X < HeaderWidth)
        {
            if (p.Y < RulerHeight)
            {
                if (!right) _app.Session.AddLayer();
                return;
            }
            var layer = LayerAtRow(RowAt(p.Y));
            if (layer is null) return;
            if (p.X < 28 && !right) _app.Session.SetLayerProperty(layer, l => l with { Visible = !l.Visible });
            else Doc.SetActiveLayer(layer);
            if (right) ShowMenu(_app.LayerTrackMenu(), p);
            return;
        }

        int frame = FrameAt(p.X);
        if (p.Y < RulerHeight)
        {
            if (frame >= FrameCount && !right) return;
            frame = Math.Clamp(frame, 0, FrameCount - 1);
            _app.Session.GoToFrame(frame);
            if (right)
            {
                ShowMenu(_app.FrameMenu(frame), p);
                return;
            }
            _mode = Mode.Scrub;
            e.Pointer.Capture(this);
            return;
        }

        var row = LayerAtRow(RowAt(p.Y));
        if (row is null) return;
        if (!right && HoldHandleAt(p) is { } handle)
        {
            _mode = Mode.Hold;
            _dragLayer = handle.Layer;
            _dragFrom = handle.End;
            e.Pointer.Capture(this);
            return;
        }
        if (frame >= FrameCount)
        {
            if (right) ShowMenu(_app.FrameMenu(FrameCount - 1), p);
            return;
        }
        Doc.SetActiveLayer(row);
        _app.Session.GoToFrame(frame);
        if (right)
        {
            ShowMenu(_app.CelMenu(row, frame), p);
            return;
        }
        if (row.Track.Count > 0 && row.Track[frame] is not null)
        {
            _mode = Mode.MaybeMove;
            _dragLayer = row;
            _dragFrom = frame;
            _pressAt = p;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == Mode.Move && _dragLayer is not null && _dropTarget >= 0 && _dropTarget != _dragFrom)
            _app.Session.MoveDrawing(_dragLayer, _dragFrom, _dropTarget);
        _mode = Mode.None;
        _dragLayer = null;
        _dropTarget = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _mode = Mode.None;
        _dropTarget = -1;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _app.AnimationState.TimelineCellWidth = Math.Clamp(CellWidth * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 22, 96);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ScrollY -= e.Delta.Y * RowHeight;
        else ScrollX -= (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y) * CellWidth * 2;
        Refresh();
        e.Handled = true;
    }

    private void ShowMenu(IEnumerable<MenuEntry> entries, Point at)
    {
        var flyout = UiKit.PopFlyout(new Panel(), PlacementMode.Pointer);
        flyout.Content = Menus.Build(entries, flyout.Hide, 260);
        Dispatcher.UIThread.Post(() => flyout.ShowAt(this, showAtPointer: true));
    }
}
