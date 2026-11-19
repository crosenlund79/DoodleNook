namespace DoodleNook.Engine.Painting;

/// <summary>
/// A seamless 256 × 256 "paper tooth" texture (fractal value noise, generated once, deterministic). Grainy brushes
/// read it in document space, so the grain stays put like real paper instead of moving with the pen.
/// </summary>
public static class PaperGrain
{
    private const int N = 256;
    private static readonly Lazy<float[]> Texture = new(Generate);

    /// <summary>Grain value 0–1 at a document pixel.</summary>
    public static float Sample(int x, int y, double scale)
    {
        var t = Texture.Value;
        if (scale == 1)
            return t[(y & (N - 1)) * N + (x & (N - 1))];
        int sx = (int)Math.Floor(x / scale), sy = (int)Math.Floor(y / scale);
        return t[(sy & (N - 1)) * N + (sx & (N - 1))];
    }

    private static float[] Generate()
    {
        var rng = new Random(20261119);
        var result = new float[N * N];
        double total = 0;
        // Octaves: coarse fibres to fine tooth.
        (int Cell, double Weight)[] octaves = [(32, 0.25), (16, 0.3), (8, 0.35), (4, 0.45), (2, 0.5), (1, 0.35)];
        foreach (var (cell, weight) in octaves)
        {
            int grid = N / cell;
            var lattice = new float[grid * grid];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rng.NextDouble();
            for (int y = 0; y < N; y++)
            {
                double fy = (double)y / cell;
                int y0 = (int)fy, y1 = (y0 + 1) % grid;
                double ty = Smooth(fy - y0);
                for (int x = 0; x < N; x++)
                {
                    double fx = (double)x / cell;
                    int x0 = (int)fx, x1 = (x0 + 1) % grid;
                    double tx = Smooth(fx - x0);
                    double a = lattice[y0 * grid + x0], b = lattice[y0 * grid + x1];
                    double c = lattice[y1 * grid + x0], d = lattice[y1 * grid + x1];
                    double v = (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
                    result[y * N + x] += (float)(v * weight);
                }
            }
            total += weight;
        }
        // Normalise to 0–1 with a gentle contrast stretch.
        float min = result.Min(), max = result.Max();
        for (int i = 0; i < result.Length; i++) result[i] = (result[i] - min) / (max - min);
        return result;
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);
}
