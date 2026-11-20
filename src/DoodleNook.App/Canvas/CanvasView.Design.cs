using Avalonia;
using Avalonia.Media;
using DoodleNook.App.Editor;
using DoodleNook.App.Input;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Canvas;

/// <summary>Mission 008 overlays: guides, snap lines, text and shape frames, the symmetry origin and perspective lines.
/// All are drawn over the picture on screen only; none of them is ever part of the picture or an export.</summary>
public sealed partial class CanvasView
{
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.Parse("#3FC1D6"));
    private static readonly IBrush SnapBrush = new SolidColorBrush(Color.Parse("#E05BB5"));
    private static readonly IBrush AssistBrush = new SolidColorBrush(Color.FromArgb(150, 233, 169, 77));
    private static readonly IBrush AssistFaint = new SolidColorBrush(Color.FromArgb(70, 233, 169, 77));

    private void OnDesignChanged()
    {
        UpdateCursor();
        InvalidateVisual();
    }

    /// <summary>A long line through a document point in a direction, in view coordinates.</summary>
    private (Point A, Point B) Through(double x, double y, double dx, double dy)
    {
        double reach = 4 * Math.Max(_session!.Document.Width, _session.Document.Height) + 10_000 / Math.Max(View.Scale, 1e-3);
        return (V(x - dx * reach, y - dy * reach), V(x + dx * reach, y + dy * reach));
    }

    private void DrawDesignOverlays(DrawingContext context, Pen dark)
    {
        if (_session is null || Input is null) return;
        var doc = _session.Document;
        var o = Input.Overlay;

        // Guides (never exported; hidden with View › Show guides).
        if (_session.ShowGuides)
        {
            var pen = new Pen(GuideBrush, 1, doc.GuidesLocked ? new DashStyle([6, 4], 0) : null);
            foreach (var g in doc.Guides)
            {
                var (a, b) = g.Vertical ? Through(g.Position, 0, 0, 1) : Through(0, g.Position, 1, 0);
                context.DrawLine(pen, a, b);
            }
        }
        if (o.GuidePreview is { } preview)
        {
            var (a, b) = preview.Vertical ? Through(preview.Position, 0, 0, 1) : Through(0, preview.Position, 1, 0);
            context.DrawLine(new Pen(GuideBrush, 2), a, b);
        }

        // Perspective: rays from the vanishing points and the horizon, with handles to move the points (Move tool).
        var persp = doc.Perspective;
        if (persp.Points > 0 && _session.Assist.ShowPerspective)
        {
            var faint = new Pen(AssistFaint, 1);
            foreach (var (x1, y1, x2, y2) in Perspective.Rays(persp, doc.Width, doc.Height, 24))
                context.DrawLine(faint, V(x1, y1), V(x2, y2));
            DrawAssistHandle(context, persp.V1X, persp.V1Y, "1");
            if (persp.Points >= 2) DrawAssistHandle(context, persp.V2X, persp.V2Y, "2");
        }

        // Symmetry: the mirror lines (or spokes) through the origin, for painting tools and the Move tool.
        var sym = Input.CurrentSymmetry;
        bool painting = _session.Tool is ToolKind.Brush or ToolKind.Eraser or ToolKind.PixelPencil or ToolKind.PixelEraser or ToolKind.Move;
        if (sym != Symmetry.None && painting)
        {
            var place = _session.Assist.Placement;
            double ox = place.OriginX(doc.Width), oy = place.OriginY(doc.Height);
            var pen = new Pen(AssistBrush, 1, new DashStyle([5, 5], 0));
            if (sym is Symmetry.Vertical or Symmetry.Both)
            {
                var (a, b) = Through(ox, oy, 0, 1);
                context.DrawLine(pen, a, b);
            }
            if (sym is Symmetry.Horizontal or Symmetry.Both)
            {
                var (a, b) = Through(ox, oy, 1, 0);
                context.DrawLine(pen, a, b);
            }
            if (sym == Symmetry.Radial)
            {
                int n = place.Count;
                double reach = Math.Max(doc.Width, doc.Height);
                for (int k = 0; k < n; k++)
                {
                    double t = -Math.PI / 2 + k * 2 * Math.PI / n;
                    context.DrawLine(pen, V(ox, oy), V(ox + Math.Cos(t) * reach, oy + Math.Sin(t) * reach));
                }
            }
            DrawAssistHandle(context, ox, oy, null);
        }

        // Snap lines while dragging.
        if (o.SnapX is { } sx)
        {
            var (a, b) = Through(sx, 0, 0, 1);
            context.DrawLine(new Pen(SnapBrush, 1), a, b);
        }
        if (o.SnapY is { } sy)
        {
            var (a, b) = Through(0, sy, 1, 0);
            context.DrawLine(new Pen(SnapBrush, 1), a, b);
        }

        // The frame and handles of the text or shape being edited.
        if (Input.FramedObject is { } layer && doc.BoxOf(layer) is { } box && Input.Gesture is not (Gesture.NewShape or Gesture.NewText))
        {
            var accent = new SolidColorBrush(Controls.DnColors.Accent);
            var accentPen = new Pen(accent, 1.5);
            if (layer.Content is ShapeContent { Kind: ShapeType.Line } line)
            {
                var a = box.ToDocument(line.X, line.Y);
                var b = box.ToDocument(line.X + line.Width, line.Y + line.Height);
                context.DrawLine(dark, V(a.X, a.Y), V(b.X, b.Y));
                context.DrawLine(accentPen, V(a.X, a.Y), V(b.X, b.Y));
                foreach (var p in new[] { a, b }) context.DrawEllipse(Brushes.White, accentPen, V(p.X, p.Y), 5, 5);
                return;
            }
            var handles = CanvasInputController.FrameHandles(box).Select(h => V(h.X, h.Y)).ToArray();
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(handles[0], false);
                for (int i = 1; i < 4; i++) ctx.LineTo(handles[i]);
                ctx.EndFigure(true);
            }
            context.DrawGeometry(null, dark, geo);
            context.DrawGeometry(null, layer.Locked ? new Pen(accent, 1.2, new DashStyle([4, 4], 0)) : accentPen, geo);
            if (layer.Locked) return;
            double w = Math.Sqrt(Math.Pow(handles[1].X - handles[0].X, 2) + Math.Pow(handles[1].Y - handles[0].Y, 2));
            double h = Math.Sqrt(Math.Pow(handles[3].X - handles[0].X, 2) + Math.Pow(handles[3].Y - handles[0].Y, 2));
            for (int i = 0; i < 8; i++)
            {
                if (i is 4 or 6 && w < 44) continue;
                if (i is 5 or 7 && h < 44) continue;
                if (i < 4 && (w < 20 || h < 20)) continue;
                if (layer.Kind == LayerKind.Text && i is 4 or 6) continue; // text: corners scale, sides set the wrap width
                context.DrawRectangle(Brushes.White, accentPen, new Rect(handles[i].X - 4.5, handles[i].Y - 4.5, 9, 9));
            }
        }
    }

    /// <summary>A round handle for the symmetry origin (no label) or a numbered vanishing point.</summary>
    private void DrawAssistHandle(DrawingContext context, double x, double y, string? label)
    {
        var p = V(x, y);
        var ring = new Pen(new SolidColorBrush(Controls.DnColors.Accent), 2);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(200, 30, 26, 23)), new Pen(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), 4), p, 8, 8);
        context.DrawEllipse(null, ring, p, 8, 8);
        if (label is null)
        {
            context.DrawLine(ring, new Point(p.X - 4, p.Y), new Point(p.X + 4, p.Y));
            context.DrawLine(ring, new Point(p.X, p.Y - 4), new Point(p.X, p.Y + 4));
            return;
        }
        var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("fonts:Inter#Inter, $Default"), FontStyle.Normal, FontWeight.Bold), 10, new SolidColorBrush(Controls.DnColors.Accent));
        context.DrawText(text, new Point(p.X - text.Width / 2, p.Y - text.Height / 2));
    }
}
