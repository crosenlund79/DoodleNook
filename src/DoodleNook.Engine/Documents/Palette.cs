using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>A named list of colours (used for pixel art and saved swatches).</summary>
public sealed class Palette
{
    public Palette(string name, IEnumerable<ColorRgb> colors)
    {
        Name = name;
        Colors = colors.ToList();
    }

    public string Name { get; set; }
    public List<ColorRgb> Colors { get; }

    public Palette Clone() => new(Name, Colors);

    /// <summary>"Nook 32": DoodleNook's own warm, game-friendly starter palette (darks, skin and wood tones, greens,
    /// blues and accents). Designed for DoodleNook; not copied from any existing palette.</summary>
    public static Palette Default() => new("Nook 32", new[]
    {
        "#1B1622", "#2E2433", "#4A3B52", "#6E5C76", "#9C8C9E", "#C9BFC3", "#EDE6DF", "#FFFFFF",
        "#3B2620", "#6B4231", "#9A6440", "#C98D5C", "#E8B98A", "#F6DDBF", "#E88A9A", "#B7525F",
        "#7A2E3A", "#C96F4A", "#E89A4A", "#F2C14E", "#F7E58A", "#9CC07A", "#5F8F5A", "#35553F",
        "#22383F", "#3F6A82", "#5F8FA8", "#8FB8CC", "#C7E1E8", "#6C5BA8", "#9A86D0", "#D5C9F0",
    }.Select(ColorRgb.Parse));

    /// <summary>Distinct opaque colours used in an image (up to <paramref name="max"/>, most frequent first).</summary>
    public static Palette FromImage(RasterImage image, int max = 64, string name = "From picture")
    {
        var counts = new Dictionary<uint, int>();
        foreach (var p in image.Pixels)
        {
            if (Pixel.A(p) < 128) continue;
            var (r, g, b, _) = Pixel.ToStraight(p);
            uint key = (uint)(r << 16 | g << 8 | b);
            counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
        }
        return new Palette(name, counts.OrderByDescending(kv => kv.Value).Take(max)
            .Select(kv => new ColorRgb((byte)(kv.Key >> 16), (byte)(kv.Key >> 8), (byte)kv.Key)));
    }
}
