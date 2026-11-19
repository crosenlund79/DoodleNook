using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Painting;
using static DoodleNook.Engine.Tests.TestCanvas;

namespace DoodleNook.Engine.Tests;

public class PressureTests
{
    private static readonly BrushSettings SizeBrush = new() { Size = 40, Hardness = 1, PressureSize = true, MinimumSizeFraction = 0.05 };

    [Theory]
    [InlineData(0f, 2.0)]
    [InlineData(1f, 40.0)]
    [InlineData(0.5f, 21.0)]
    public void Size_follows_pressure_between_minimum_and_full(float pressure, double expected)
    {
        var (diameter, alpha) = SizeBrush.Evaluate(pressure, PressureCurve.Normal);
        Assert.Equal(expected, diameter, 6);
        Assert.Equal(1f, alpha);
    }

    [Fact]
    public void Opacity_follows_pressure_when_enabled()
    {
        var b = new BrushSettings { PressureSize = false, PressureOpacity = true };
        Assert.Equal(0.3f, b.Evaluate(0.3f, PressureCurve.Normal).Alpha, 5);
        Assert.Equal(b.Size, b.Evaluate(0.3f, PressureCurve.Normal).Diameter);
    }

    [Fact]
    public void Disabled_dynamics_ignore_pressure()
    {
        var b = new BrushSettings { Size = 30, PressureSize = false, PressureOpacity = false };
        foreach (var p in new[] { 0f, 0.2f, 0.7f, 1f })
            Assert.Equal((30.0, 1f), b.Evaluate(p, PressureCurve.Normal));
    }

    [Fact]
    public void Curves_shape_light_touches_but_keep_end_points()
    {
        Assert.True(PressureCurve.Soft.Map(0.25f) > 0.25f);
        Assert.True(PressureCurve.Firm.Map(0.25f) < 0.25f);
        Assert.Equal(0.25f, PressureCurve.Normal.Map(0.25f));
        foreach (var c in new[] { PressureCurve.Soft, PressureCurve.Normal, PressureCurve.Firm })
        {
            Assert.Equal(0f, c.Map(0f));
            Assert.Equal(1f, c.Map(1f));
        }
    }

    [Fact]
    public void Curves_are_monotonic()
    {
        foreach (var c in new[] { PressureCurve.Soft, PressureCurve.Normal, PressureCurve.Firm })
        {
            float prev = -1;
            for (int i = 0; i <= 100; i++)
            {
                float v = c.Map(i / 100f);
                Assert.True(v >= prev);
                prev = v;
            }
        }
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(-0.5f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(float.NegativeInfinity, 0f)]
    public void Invalid_pressure_is_sanitised(float raw, float expected)
    {
        Assert.Equal(expected, PressureCurve.Sanitize(raw));
    }

    [Fact]
    public void Invalid_curve_falls_back_to_linear()
    {
        Assert.Equal(0.4f, new PressureCurve(double.NaN).Map(0.4f));
        Assert.Equal(0.4f, new PressureCurve(-2).Map(0.4f));
    }

    [Fact]
    public void Increasing_pressure_makes_the_stroke_steadily_wider()
    {
        var doc = new Document(400, 80);
        Stroke(doc, Paint(SizeBrush), Line(20, 40, 380, 40, step: 4, p0: 0.05f, p1: 1f));
        int[] widths = new[] { 60, 120, 180, 240, 300, 360 }.Select(x => ColumnCoverage(doc, x)).ToArray();
        for (int i = 1; i < widths.Length; i++) Assert.True(widths[i] > widths[i - 1], string.Join(",", widths));
    }

    [Fact]
    public void Decreasing_pressure_makes_the_stroke_taper()
    {
        var doc = new Document(400, 80);
        Stroke(doc, Paint(SizeBrush), Line(20, 40, 380, 40, step: 4, p0: 1f, p1: 0.0f));
        int[] widths = new[] { 60, 120, 180, 240, 300, 360 }.Select(x => ColumnCoverage(doc, x)).ToArray();
        for (int i = 1; i < widths.Length; i++) Assert.True(widths[i] < widths[i - 1], string.Join(",", widths));
        Assert.True(Alpha(doc, 370, 40) > 0, "the taper should fade, not break off");
    }

    [Fact]
    public void Pressure_between_samples_is_interpolated_smoothly()
    {
        // Only two samples, with very different pressure: the width must still change gradually.
        var doc = new Document(400, 80);
        Stroke(doc, Paint(SizeBrush), S(20, 40, 0.1f), S(380, 40, 1f));
        int prev = ColumnCoverage(doc, 40);
        for (int x = 60; x <= 340; x += 20)
        {
            int w = ColumnCoverage(doc, x);
            Assert.True(w >= prev && w - prev <= 4, $"jump at x={x}: {prev} -> {w}");
            prev = w;
        }
    }

    [Fact]
    public void Constant_full_pressure_matches_disabled_dynamics()
    {
        // Mouse input uses a constant pressure of 1, which must give exactly the slider size.
        var a = new Document(200, 60);
        var b = new Document(200, 60);
        Stroke(a, Paint(SizeBrush), S(20, 30, 1f), S(180, 30, 1f));
        Stroke(b, Paint(SizeBrush with { PressureSize = false }), S(20, 30), S(180, 30));
        Assert.Equal(b.Layer.Pixels, a.Layer.Pixels);
    }

    [Fact]
    public void Brush_settings_are_normalised()
    {
        var b = new BrushSettings { Size = -5, Opacity = 3, Hardness = double.NaN, Smoothing = 9 }.Normalized();
        Assert.Equal(BrushSettings.MinSize, b.Size);
        Assert.Equal(1, b.Opacity);
        Assert.Equal(0.9, b.Hardness);
        Assert.Equal(1, b.Smoothing);
        Assert.Equal(BrushSettings.MaxSize, (new BrushSettings { Size = 1e9 }).Normalized().Size);
    }
}
