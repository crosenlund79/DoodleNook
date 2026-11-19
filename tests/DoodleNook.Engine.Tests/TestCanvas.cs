using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>Small helpers so tests read like drawing.</summary>
internal static class TestCanvas
{
    public static readonly ColorRgb Ink = new(40, 30, 20);

    public static BrushSettings Hard(double size, double opacity = 1, bool pressureSize = false, bool pressureOpacity = false) => new()
    {
        Size = size,
        Opacity = opacity,
        Hardness = 1,
        PressureSize = pressureSize,
        PressureOpacity = pressureOpacity,
    };

    public static StrokeStyle Paint(BrushSettings brush, ColorRgb? color = null) =>
        new(brush, PressureCurve.Normal, color ?? Ink, PaintMode.Paint);

    public static StrokeStyle Erase(BrushSettings brush) =>
        new(brush, PressureCurve.Normal, ColorRgb.Black, PaintMode.Erase);

    /// <summary>Draws a stroke through the given samples (the first starts it) and ends it.</summary>
    public static void Stroke(Document doc, StrokeStyle style, params StrokeSample[] samples)
    {
        var engine = new StrokeEngine(doc);
        engine.Begin(style, samples[0]);
        foreach (var s in samples.Skip(1)) engine.Add(s);
        engine.End();
    }

    public static StrokeSample S(double x, double y, float p = 1f) => new(x, y, p);

    /// <summary>Samples along a straight line, every <paramref name="step"/> px, with pressure going from p0 to p1.</summary>
    public static StrokeSample[] Line(double x0, double y0, double x1, double y1, double step, float p0 = 1f, float p1 = 1f)
    {
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        int n = Math.Max(1, (int)Math.Ceiling(len / step));
        return Enumerable.Range(0, n + 1).Select(i =>
        {
            double t = (double)i / n;
            return new StrokeSample(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, p0 + (p1 - p0) * (float)t);
        }).ToArray();
    }

    public static byte Alpha(Document doc, int x, int y) => Pixel.A(doc.ActiveLayer.Image[x, y]);

    /// <summary>Number of pixels in column x with any paint.</summary>
    public static int ColumnCoverage(Document doc, int x) =>
        Enumerable.Range(0, doc.Height).Count(y => Pixel.A(doc.ActiveLayer.Image[x, y]) > 0);

    public static uint[] Snapshot(Document doc) => (uint[])doc.ActiveLayer.Image.Pixels.Clone();
}
