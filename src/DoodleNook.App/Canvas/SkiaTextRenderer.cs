using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;
using SkiaSharp;

namespace DoodleNook.App.Canvas;

/// <summary>
/// Draws text layers with the computer's own fonts (and the two fonts DoodleNook ships: Inter and Varela Round).
/// When a font isn't installed, the nearest installed one (or Inter) is used and named, so the layer can say which font
/// it shows instead; the layer keeps asking for the original. Text is drawn straight into the layer's pixels: Skia's
/// premultiplied BGRA is the engine's 0xAARRGGBB on little-endian machines.
/// </summary>
public sealed class SkiaTextRenderer : ITextRenderer
{
    public const string DefaultFont = "Inter";

    private static readonly Lazy<Dictionary<string, SKTypeface>> Bundled = new(LoadBundled);
    private readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), (SKTypeface Face, string? Substitute, bool FakeBold, bool FakeItalic)> _faces = new();

    /// <summary>Font families offered in the font list: DoodleNook's own first, then the installed ones (sorted).</summary>
    public static IReadOnlyList<string> Families()
    {
        var installed = SKFontManager.Default.GetFontFamilies().Where(f => !string.IsNullOrWhiteSpace(f) && !f.StartsWith('@'));
        return Bundled.Value.Keys.Select(k => k.Split('|')[0]).Distinct()
            .Concat(installed.Order(StringComparer.CurrentCultureIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when the family can be used without substitution.</summary>
    public bool IsAvailable(string family) => Face(family, false, false).Substitute is null;

    private static Dictionary<string, SKTypeface> LoadBundled()
    {
        var faces = new Dictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);
        void Load(string key, string uri)
        {
            try
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(uri));
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                if (SKTypeface.FromStream(ms) is { } face) faces[key] = face;
            }
            catch (Exception)
            {
                // A missing resource only means the system's own font is used.
            }
        }
        Load("Inter|regular", "avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf");
        Load("Inter|bold", "avares://Avalonia.Fonts.Inter/Assets/Inter-Bold.ttf");
        Load("Varela Round|regular", "avares://DoodleNook/Assets/Fonts/VarelaRound-Regular.ttf");
        return faces;
    }

    private (SKTypeface Face, string? Substitute, bool FakeBold, bool FakeItalic) Face(string family, bool bold, bool italic) =>
        _faces.GetOrAdd((family, bold, italic), key =>
        {
            var style = new SKFontStyle(key.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
                key.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            var bundled = Bundled.Value;
            if (bundled.TryGetValue($"{key.Family}|{(key.Bold ? "bold" : "regular")}", out var own)) return (own, null, false, key.Italic);
            if (bundled.TryGetValue($"{key.Family}|regular", out var ownRegular)) return (ownRegular, null, key.Bold, key.Italic);
            var face = SKFontManager.Default.MatchFamily(key.Family, style);
            if (face is not null && string.Equals(face.FamilyName, key.Family, StringComparison.OrdinalIgnoreCase))
                return (face, null, key.Bold && face.FontWeight < 600, key.Italic && face.FontSlant == SKFontStyleSlant.Upright);
            // Not installed: DoodleNook's Inter (always there), named as the substitute.
            var fallback = bundled.GetValueOrDefault(key.Bold ? "Inter|bold" : "Inter|regular") ?? SKTypeface.Default;
            return (fallback, fallback.FamilyName, false, key.Italic);
        });

    public TextRenderResult Render(TextContent t, RasterImage target)
    {
        var (face, substitute, fakeBold, fakeItalic) = Face(t.FontFamily, t.Bold, t.Italic);
        using var font = new SKFont(face, (float)Math.Clamp(t.Size, 1, 2000))
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias,
            Embolden = fakeBold,
            SkewX = fakeItalic ? -0.2f : 0,
        };
        var metrics = font.Metrics;
        double ascent = -metrics.Ascent, descent = metrics.Descent;
        double lineHeight = t.Size * Math.Clamp(t.LineSpacing, 0.5, 5);
        var lines = Layout(t, font);
        double width = t.BoxWidth > 0 ? t.BoxWidth : Math.Max(1, lines.Max(l => l.Width));
        double height = Math.Max(1, (lines.Count - 1) * lineHeight + ascent + descent);

        // Everything drawn lies in the turned box grown by the outline and the shadow.
        var box = new ObjectBox(t.X, t.Y, width, height, t.Rotation);
        var (l, top, r, b) = box.Extent();
        double grow = 2 + (t.Outline?.Width ?? 0) + t.Size * 0.25; // italic overhang, accents
        double sl = 0, st = 0, sr = 0, sb = 0;
        if (t.Shadow is { } sh)
        {
            double blur = Math.Clamp(sh.Blur, 0, 64) * 3;
            sl = Math.Min(0, sh.OffsetX) - blur;
            sr = Math.Max(0, sh.OffsetX) + blur;
            st = Math.Min(0, sh.OffsetY) - blur;
            sb = Math.Max(0, sh.OffsetY) + blur;
        }
        var bounds = PixelRect.FromEdges((int)Math.Floor(l - grow + sl), (int)Math.Floor(top - grow + st), (int)Math.Ceiling(r + grow + sr) + 1,
            (int)Math.Ceiling(b + grow + sb) + 1).Intersect(target.Bounds);
        if (bounds.IsEmpty || t.Text.Length == 0) return new TextRenderResult(width, height, PixelRect.Empty, substitute);

        var handle = GCHandle.Alloc(target.Pixels, GCHandleType.Pinned);
        try
        {
            var info = new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, handle.AddrOfPinnedObject(), target.Width * 4);
            var canvas = surface.Canvas;
            canvas.ClipRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
            var (cx, cy) = box.Center;
            canvas.Translate((float)cx, (float)cy);
            canvas.RotateDegrees((float)t.Rotation);
            canvas.Translate((float)-cx, (float)-cy);

            using var blobs = new BlobSet();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Glyphs.Length == 0) continue;
                double x = t.X + t.Align switch
                {
                    TextAlign.Center => (width - line.Width) / 2,
                    TextAlign.Right => width - line.Width,
                    _ => 0,
                };
                double y = t.Y + ascent + i * lineHeight;
                var positions = new SKPoint[line.Glyphs.Length];
                for (int g = 0; g < positions.Length; g++) positions[g] = new SKPoint((float)(x + line.Offsets[g]), (float)y);
                using var builder = new SKTextBlobBuilder();
                var run = builder.AllocatePositionedRun(font, line.Glyphs.Length);
                line.Glyphs.CopyTo(run.Glyphs);
                positions.CopyTo(run.Positions);
                if (builder.Build() is { } blob) blobs.Add(blob);
            }

            if (t.Shadow is { } shadow && shadow.Opacity > 0)
            {
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Color = new SKColor(shadow.Color.R, shadow.Color.G, shadow.Color.B, (byte)Math.Clamp(shadow.Opacity * 255, 0, 255)),
                    MaskFilter = shadow.Blur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Clamp(shadow.Blur, 0, 64)) : null,
                };
                canvas.Save();
                // The offset is in picture space (a shadow falls the same way however the text is turned).
                canvas.SetMatrix(SKMatrix.CreateTranslation((float)shadow.OffsetX, (float)shadow.OffsetY).PreConcat(canvas.TotalMatrix));
                foreach (var blob in blobs) canvas.DrawText(blob, 0, 0, paint);
                if (t.Outline is { Width: > 0 } so)
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = (float)(so.Width * 2);
                    paint.StrokeJoin = SKStrokeJoin.Round;
                    foreach (var blob in blobs) canvas.DrawText(blob, 0, 0, paint);
                }
                canvas.Restore();
            }
            if (t.Outline is { Width: > 0 } outline)
            {
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)(outline.Width * 2), // centred on the edge: the fill covers the inner half
                    StrokeJoin = SKStrokeJoin.Round,
                    Color = new SKColor(outline.Color.R, outline.Color.G, outline.Color.B),
                };
                foreach (var blob in blobs) canvas.DrawText(blob, 0, 0, paint);
            }
            using (var fill = new SKPaint { IsAntialias = true, Color = new SKColor(t.Color.R, t.Color.G, t.Color.B) })
                foreach (var blob in blobs) canvas.DrawText(blob, 0, 0, fill);
            canvas.Flush();
        }
        finally
        {
            handle.Free();
        }
        return new TextRenderResult(width, height, bounds, substitute);
    }

    private sealed class BlobSet : List<SKTextBlob>, IDisposable
    {
        public void Dispose()
        {
            foreach (var b in this) b.Dispose();
        }
    }

    private readonly record struct Line(ushort[] Glyphs, double[] Offsets, double Width);

    /// <summary>Splits the text into lines (its own line breaks, and wrapping at the box width when one is set).</summary>
    private static List<Line> Layout(TextContent t, SKFont font)
    {
        var result = new List<Line>();
        double spacing = t.LetterSpacing;
        Line Shape(string s)
        {
            var glyphs = font.GetGlyphs(s);
            var widths = font.GetGlyphWidths(glyphs);
            var offsets = new double[glyphs.Length];
            double x = 0;
            for (int i = 0; i < glyphs.Length; i++)
            {
                offsets[i] = x;
                x += widths[i] + (i < glyphs.Length - 1 ? spacing : 0);
            }
            return new Line(glyphs, offsets, Math.Max(0, x));
        }
        foreach (var paragraph in t.Text.Replace("\r\n", "\n").Split('\n'))
        {
            if (t.BoxWidth <= 0 || paragraph.Length == 0)
            {
                result.Add(Shape(paragraph));
                continue;
            }
            // Word wrap: add words while they fit; a single word wider than the box gets its own line.
            var words = paragraph.Split(' ');
            string current = "";
            foreach (var word in words)
            {
                string attempt = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && Shape(attempt).Width > t.BoxWidth)
                {
                    result.Add(Shape(current));
                    current = word;
                }
                else current = attempt;
            }
            result.Add(Shape(current));
        }
        if (result.Count == 0) result.Add(Shape(""));
        return result;
    }
}
