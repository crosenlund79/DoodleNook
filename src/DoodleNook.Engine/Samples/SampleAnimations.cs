using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Samples;

/// <summary>
/// The two sample animations, drawn with DoodleNook's own brushes and pixel pencil so they are ordinary, fully editable
/// documents: layers, drawings, holds and timing like anything a user makes.
/// </summary>
public static class SampleAnimations
{
    /// <summary>A hand-drawn bouncing ball: squash and stretch, a soft shadow, a held ground line and a held pause at the top.</summary>
    public static Document BouncingBall()
    {
        const int w = 640, h = 400, frames = 12;
        const double ground = 330, radius = 34, apex = 210, cx = 320;
        var doc = Document.NewAnimation(w, h, ColorRgb.White, 1, 12, "Ground");
        var groundLayer = doc.ActiveLayer;

        // The ground: one drawing held over all twelve frames.
        var pencil = new BrushSettings { Size = 5, Hardness = 0.85, PressureSize = true, MinimumSizeFraction = 0.4 };
        var ink = ColorRgb.Parse("#5A4A42");
        // A horizon behind the ball and a few floor lines in front of it.
        Stroke(doc, groundLayer, pencil, ink, Line(90, ground + radius - 16, 550, ground + radius - 16, 40, 0.6f, 1f));
        Stroke(doc, groundLayer, pencil with { Size = 3 }, ColorRgb.Parse("#9C8B80"), Line(150, ground + radius + 18, 290, ground + radius + 18, 20, 0.4f, 0.8f));
        Stroke(doc, groundLayer, pencil with { Size = 3 }, ColorRgb.Parse("#9C8B80"), Line(370, ground + radius + 24, 500, ground + radius + 24, 20, 0.4f, 0.8f));
        while (doc.FrameCount < frames) doc.ExtendHold(groundLayer, 0);
        var shadow = doc.AddLayer("Shadow");
        var ball = doc.AddLayer("Ball");

        var red = ColorRgb.Parse("#E0604A");
        var dark = ColorRgb.Parse("#9E3B2E");
        var shine = ColorRgb.Parse("#FFF4E8");
        var shadowColor = ColorRgb.Parse("#C9BCB2");
        for (int f = 0; f < frames; f++)
        {
            doc.SetCurrentFrame(f);
            // Height follows a parabola: contact at frame 0, apex at frame 6.
            double t = f / (double)frames;
            double height = 4 * t * (1 - t) * (ground - apex);
            double y = ground - height;
            double squash = f == 0 ? 0.72 : 1;
            double stretch = f is 1 or 11 ? 1.18 : 1;
            double rx = radius / Math.Sqrt(squash * stretch) * (f == 0 ? 1.12 : 1);
            double ry = radius * squash * stretch;
            if (f == 0) y = ground + radius - ry;

            // The ball: a filled disc (one big round dab), a darker rim and a highlight.
            double roundness = Math.Clamp(ry / rx, 0.2, 1);
            var fill = new BrushSettings { Size = 2 * Math.Max(rx, ry), Hardness = 0.96, PressureSize = false, Roundness = roundness, Angle = rx >= ry ? 0 : 90 };
            Stroke(doc, ball, fill, red, [new StrokeSample(cx, y, 1)]);
            Stroke(doc, ball, pencil with { Size = 4 }, dark, Ellipse(cx, y, rx, ry, 48));
            Stroke(doc, ball, new BrushSettings { Size = 9, Hardness = 0.7, PressureSize = false }, shine,
                [new StrokeSample(cx - rx * 0.38, y - ry * 0.4, 1), new StrokeSample(cx - rx * 0.2, y - ry * 0.55, 1)]);

            // The shadow: smaller and fainter the higher the ball is.
            double k = 1 - height / (ground - apex) * 0.55;
            var soft = new BrushSettings { Size = 2 * radius * 1.3 * k, Hardness = 0.35, Opacity = 0.35 + 0.4 * k, PressureSize = false, Roundness = 0.22, Angle = 0 };
            Stroke(doc, shadow, soft, shadowColor, [new StrokeSample(cx, ground + radius + 2, 1)]);
        }
        // A short pause at the top of the bounce (a per-frame duration).
        doc.SetFrameDuration(6, 160);
        doc.SetActiveLayer(ball);
        doc.SetCurrentFrame(0);
        return Clean(doc);
    }

    /// <summary>A pixel-art slime hopping on the spot (32 × 32, transparent, 8 fps).</summary>
    public static Document PixelSlime()
    {
        const int size = 32, frames = 8;
        var doc = Document.NewAnimation(size, size, null, frames, 8, "Shadow");
        var shadow = doc.ActiveLayer;
        var body = doc.AddLayer("Slime");
        var outline = ColorRgb.Parse("#1E3A2E");
        var green = ColorRgb.Parse("#4FB06A");
        var light = ColorRgb.Parse("#9BE07D");
        var eye = ColorRgb.Parse("#14231C");
        var shade = ColorRgb.Parse("#2E7A4F");
        var shadowColor = ColorRgb.Parse("#33302E");

        // Per frame: (lift above the ground, half width, height).
        (int Lift, int HalfW, int H)[] poses = [(0, 11, 10), (0, 12, 8), (2, 9, 13), (5, 8, 14), (7, 8, 13), (5, 9, 12), (2, 10, 11), (0, 13, 7)];
        const int floor = 27, cx = 16;
        for (int f = 0; f < frames; f++)
        {
            doc.SetCurrentFrame(f);
            var (lift, hw, hh) = poses[f];
            int bottom = floor - lift;
            int top = bottom - hh + 1;
            // The body is a dome: a half ellipse on top of a short flat-bottomed base.
            bool Inside(int x, int y)
            {
                if (y < top || y > bottom) return false;
                double ry = Math.Max(1, hh * 0.75);
                double centreY = top + ry;
                double dx = (x + 0.5 - cx) / hw;
                if (y + 0.5 >= centreY) return Math.Abs(dx) <= 1;
                double dy = (y + 0.5 - centreY) / ry;
                return dx * dx + dy * dy <= 1;
            }
            var colours = new Dictionary<(int, int), ColorRgb>();
            for (int y = top; y <= bottom; y++)
                for (int x = cx - hw - 1; x <= cx + hw; x++)
                {
                    if (!Inside(x, y)) continue;
                    bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                    colours[(x, y)] = edge ? outline : y >= bottom - 1 ? shade : green;
                }
            // Highlight and eyes.
            foreach (var (x, y) in new[] { (cx - hw / 2, top + 2), (cx - hw / 2 + 1, top + 2), (cx - hw / 2 - 1, top + 3) })
                if (colours.TryGetValue((x, y), out var c) && c == green) colours[(x, y)] = light;
            int eyeY = top + Math.Max(3, hh / 2);
            foreach (var x in new[] { cx - 3, cx + 2 })
                for (int y = eyeY; y <= eyeY + 1; y++)
                    if (colours.ContainsKey((x, y))) colours[(x, y)] = eye;
            // Paint row by row in runs of one colour.
            for (int y = top; y <= bottom; y++)
            {
                int x = cx - hw - 1;
                while (x <= cx + hw)
                {
                    if (!colours.TryGetValue((x, y), out var c)) { x++; continue; }
                    int end = x;
                    while (colours.TryGetValue((end + 1, y), out var n) && n == c) end++;
                    PixelLine(doc, body, c, x, y, end, y);
                    x = end + 1;
                }
            }
            // Shadow on the ground: narrower while the slime is in the air.
            int sh = Math.Max(3, 10 - lift);
            PixelLine(doc, shadow, shadowColor, cx - sh, floor + 1, cx + sh - 1, floor + 1);
            if (lift < 3) PixelLine(doc, shadow, shadowColor, cx - sh + 2, floor + 2, cx + sh - 3, floor + 2);
        }
        // Squash frames linger a little.
        doc.SetFrameDuration(1, 180);
        doc.SetFrameDuration(7, 180);
        doc.SetActiveLayer(body);
        doc.SetCurrentFrame(0);
        return Clean(doc);
    }

    private static void Stroke(Document doc, Layer layer, BrushSettings brush, ColorRgb color, IReadOnlyList<StrokeSample> samples)
    {
        doc.SetActiveLayer(layer);
        var engine = new StrokeEngine(doc);
        engine.Begin(new StrokeStyle(brush, PressureCurve.Normal, color, PaintMode.Paint), samples[0], layer);
        for (int i = 1; i < samples.Count; i++) engine.Add(samples[i]);
        engine.End();
    }

    private static void PixelLine(Document doc, Layer layer, ColorRgb color, int x0, int y0, int x1, int y1)
    {
        doc.SetActiveLayer(layer);
        var stroke = new PixelStroke(doc);
        if (!stroke.Begin(new PixelStrokeStyle(color), x0, y0, layer)) return;
        if (x1 != x0 || y1 != y0) stroke.Add(x1, y1);
        stroke.End();
    }

    private static StrokeSample[] Line(double x0, double y0, double x1, double y1, int steps, float p0, float p1) =>
        Enumerable.Range(0, steps + 1).Select(i =>
        {
            double t = i / (double)steps;
            // Pressure rises and falls like a real pen line.
            float p = (float)(p0 + (p1 - p0) * Math.Sin(Math.PI * t));
            return new StrokeSample(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, p);
        }).ToArray();

    private static StrokeSample[] Ellipse(double cx, double cy, double rx, double ry, int steps) =>
        Enumerable.Range(0, steps + 1).Select(i =>
        {
            double a = i * 2 * Math.PI / steps;
            return new StrokeSample(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a), 0.85f + 0.15f * (float)Math.Sin(a));
        }).ToArray();

    /// <summary>A fresh copy without the drawing history (exactly what opening the saved file gives).</summary>
    private static Document Clean(Document doc)
    {
        using var ms = new MemoryStream();
        DnkFile.Write(doc, ms);
        ms.Position = 0;
        return DnkFile.Read(ms);
    }
}
