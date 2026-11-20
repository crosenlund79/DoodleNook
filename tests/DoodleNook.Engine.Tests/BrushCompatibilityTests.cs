using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;
using DoodleNook.Engine.Tests.Reference;

namespace DoodleNook.Engine.Tests;

/// <summary>
/// Mission 007 extended the brush engine. Every Mission 005/006 brush must still paint exactly the same pixels: each
/// is drawn by the current engine and by a frozen copy of the Mission 006 engine, and the results must be identical.
/// </summary>
public class BrushCompatibilityTests
{
    internal static readonly (string Id, BrushSettings Brush)[] Mission006Brushes =
    [
        ("pencil", new BrushSettings { Size = 6, Hardness = 0.85, Opacity = 0.9, PressureSize = true, PressureOpacity = true, Grain = 0.55 }),
        ("ink", new BrushSettings { Size = 9, Hardness = 1, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.02 }),
        ("liner", new BrushSettings { Size = 3, Hardness = 1, Opacity = 1, PressureSize = false }),
        ("hard", new BrushSettings { Size = 30, Hardness = 0.98, Opacity = 1, PressureSize = true, MinimumSizeFraction = 0.25 }),
        ("soft", new BrushSettings { Size = 70, Hardness = 0.05, Opacity = 0.6, PressureSize = true, PressureOpacity = true }),
        ("marker", new BrushSettings { Size = 26, Hardness = 0.9, Opacity = 0.55, PressureSize = false, Roundness = 0.3, Angle = 35 }),
        ("air", new BrushSettings { Size = 120, Hardness = 0, Opacity = 1, PressureSize = false, PressureOpacity = true, BuildUp = true, Flow = 0.04, Spray = true }),
        ("chalk", new BrushSettings { Size = 32, Hardness = 0.55, Opacity = 0.9, PressureSize = true, PressureOpacity = true, Grain = 0.9, GrainScale = 1.6 }),
        ("wash", new BrushSettings { Size = 48, Hardness = 0.95, Opacity = 0.7, PressureSize = false, PressureOpacity = true }),
    ];

    private static StrokeSample At(int i) => new(20 + i * 2.8, 100 + 55 * Math.Sin(i / 14.0), (float)(0.15 + 0.85 * Math.Sin(Math.PI * i / 100.0)));

    private static uint[] Draw(Func<Document, (Func<StrokeStyle, StrokeSample, PixelRect> Begin, Func<StrokeSample[], PixelRect> Add, Func<PixelRect> End)> make,
        StrokeStyle style, Action<Document>? setup = null)
    {
        var doc = new Document(320, 200, null);
        setup?.Invoke(doc);
        var e = make(doc);
        e.Begin(style, At(0));
        for (int i = 1; i <= 100; i += 2) e.Add([At(i), At(i + 1)]);
        e.End();
        return doc.ActiveLayer.Image.Pixels;
    }

    private static uint[] Current(StrokeStyle style, Action<Document>? setup = null) => Draw(d =>
    {
        var e = new StrokeEngine(d);
        return ((s, f) => e.Begin(s, f), s => e.Add(s), e.End);
    }, style, setup);

    private static uint[] Mission006(StrokeStyle style, Action<Document>? setup = null) => Draw(d =>
    {
        var e = new Mission006StrokeEngine(d);
        return ((s, f) => e.Begin(s, f), s => e.Add(s), e.End);
    }, style, setup);

    public static TheoryData<string> Ids => new(Mission006Brushes.Select(b => b.Id));

    [Theory]
    [MemberData(nameof(Ids))]
    public void Existing_brushes_paint_exactly_as_in_mission_006(string id)
    {
        var brush = Mission006Brushes.First(b => b.Id == id).Brush;
        var ink = ColorRgb.Parse("#3B2F2A");
        foreach (var style in new[]
        {
            new StrokeStyle(brush, PressureCurve.Normal, ink, PaintMode.Paint),
            new StrokeStyle(brush with { Smoothing = 0.6 }, PressureCurve.Soft, ink, PaintMode.Paint, Symmetry.Both),
            new StrokeStyle(brush, PressureCurve.Firm, ink, PaintMode.Erase),
        })
        {
            void Setup(Document d)
            {
                d.ActiveLayer.Image.Fill(Pixel.Opaque(ColorRgb.Parse("#9CC07A")));
                d.SetSelection(Operations.Selections.Rectangle(d, 40, 30, 260, 170), "Select");
            }
            Assert.Equal(Mission006(style), Current(style));
            Assert.Equal(Mission006(style, Setup), Current(style, Setup));
        }
    }
}
