namespace DoodleNook.Engine.Imaging;

/// <summary>
/// Finds a small set of colours that represents a picture (palette from image). Pictures with few colours (pixel art)
/// keep their exact colours; others are reduced by median cut and refined with a few k-means passes, weighted by how
/// often each colour appears. Deterministic: the same picture always gives the same palette.
/// </summary>
public static class ColorQuantizer
{
    public static List<ColorRgb> Extract(RasterImage image, int count)
    {
        count = Math.Clamp(count, 1, 256);
        var counts = new Dictionary<int, int>();
        int step = Math.Max(1, (int)Math.Sqrt(image.Pixels.Length / 250_000.0));
        for (int y = 0; y < image.Height; y += step)
            for (int x = 0; x < image.Width; x += step)
            {
                uint p = image[x, y];
                if (Pixel.A(p) < 128) continue;
                var (r, g, b, _) = Pixel.ToStraight(p);
                int key = r << 16 | g << 8 | b;
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        if (counts.Count == 0) return [];
        List<(int Key, int Weight)> colours = counts.Select(kv => (kv.Key, kv.Value)).ToList();
        List<ColorRgb> result;
        if (colours.Count <= count)
            result = colours.OrderByDescending(c => c.Weight).Select(c => FromKey(c.Key)).ToList();
        else
        {
            var centres = MedianCut(colours, count);
            result = KMeans(colours, centres, 6);
        }
        return result.OrderBy(Lightness).ThenBy(c => c.R).ToList();
    }

    private static ColorRgb FromKey(int k) => new((byte)(k >> 16), (byte)(k >> 8), (byte)k);

    private static double Lightness(ColorRgb c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    private static List<double[]> MedianCut(List<(int Key, int Weight)> colours, int count)
    {
        var boxes = new List<List<(int Key, int Weight)>> { colours };
        while (boxes.Count < count)
        {
            // Split the box with the largest weighted spread.
            int best = -1, bestChannel = 0;
            double bestSpread = -1;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                for (int ch = 0; ch < 3; ch++)
                {
                    int shift = 16 - ch * 8;
                    int min = 255, max = 0;
                    long weight = 0;
                    foreach (var (k, w) in boxes[i])
                    {
                        int v = k >> shift & 255;
                        if (v < min) min = v;
                        if (v > max) max = v;
                        weight += w;
                    }
                    double spread = (max - min) * Math.Sqrt(weight);
                    if (spread > bestSpread)
                    {
                        bestSpread = spread;
                        best = i;
                        bestChannel = ch;
                    }
                }
            }
            if (best < 0) break;
            int sh = 16 - bestChannel * 8;
            var box = boxes[best].OrderBy(c => c.Key >> sh & 255).ToList();
            long total = box.Sum(c => (long)c.Weight), acc = 0;
            int cut = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                acc += box[i].Weight;
                if (acc * 2 >= total)
                {
                    cut = i + 1;
                    break;
                }
                cut = i + 1;
            }
            boxes[best] = box.GetRange(0, cut);
            boxes.Add(box.GetRange(cut, box.Count - cut));
        }
        return boxes.Where(b => b.Count > 0).Select(Mean).ToList();
    }

    private static double[] Mean(List<(int Key, int Weight)> box)
    {
        double r = 0, g = 0, b = 0, w = 0;
        foreach (var (k, wt) in box)
        {
            r += (k >> 16 & 255) * wt;
            g += (k >> 8 & 255) * wt;
            b += (k & 255) * wt;
            w += wt;
        }
        return [r / w, g / w, b / w];
    }

    private static List<ColorRgb> KMeans(List<(int Key, int Weight)> colours, List<double[]> centres, int passes)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            var sums = centres.Select(_ => new double[4]).ToList();
            foreach (var (k, w) in colours)
            {
                double r = k >> 16 & 255, g = k >> 8 & 255, b = k & 255;
                int best = 0;
                double bestD = double.MaxValue;
                for (int i = 0; i < centres.Count; i++)
                {
                    var c = centres[i];
                    double d = (r - c[0]) * (r - c[0]) * 0.3 + (g - c[1]) * (g - c[1]) * 0.59 + (b - c[2]) * (b - c[2]) * 0.11;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                var s = sums[best];
                s[0] += r * w;
                s[1] += g * w;
                s[2] += b * w;
                s[3] += w;
            }
            for (int i = 0; i < centres.Count; i++)
                if (sums[i][3] > 0) centres[i] = [sums[i][0] / sums[i][3], sums[i][1] / sums[i][3], sums[i][2] / sums[i][3]];
        }
        return centres.Select(c => new ColorRgb((byte)Math.Round(c[0]), (byte)Math.Round(c[1]), (byte)Math.Round(c[2]))).Distinct().ToList();
    }

    /// <summary>Colour-wheel companions of a colour (complementary, analogous, triadic): a gentle starting point.</summary>
    public static List<ColorRgb> Harmony(ColorRgb c, string kind)
    {
        var (h, s, v) = ToHsv(c);
        double[] offsets = kind switch
        {
            "complementary" => [0, 180],
            "analogous" => [-30, 0, 30],
            "triadic" => [0, 120, 240],
            _ => [0],
        };
        return offsets.Select(o => FromHsv((h + o + 360) % 360, s, v)).ToList();
    }

    public static (double H, double S, double V) ToHsv(ColorRgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static ColorRgb FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return new ColorRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
