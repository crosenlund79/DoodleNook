namespace DoodleNook.Engine.Geometry;

/// <summary>
/// A projective (perspective) transform of the plane, x' = (a·x + b·y + c) / (g·x + h·y + i), y' = (d·x + e·y + f) / (…).
/// Built from where the four corners of a rectangle should go (Perspective transform). The full 3 × 3 matrix is kept:
/// an inverse can have i = 0 (when the vanishing line passes through the origin).
/// </summary>
public readonly record struct Homography(double A, double B, double C, double D, double E, double F, double G, double H, double I = 1)
{
    public (double X, double Y) Apply(double x, double y)
    {
        double w = G * x + H * y + I;
        if (Math.Abs(w) < 1e-12) w = 1e-12;
        return ((A * x + B * y + C) / w, (D * x + E * y + F) / w);
    }

    /// <summary>The transform taking (0,0), (w,0), (w,h), (0,h) to the four given points (clockwise from the top left),
    /// or null when the points can't form a picture (three in a line, or crossed).</summary>
    public static Homography? FromRect(double w, double h, (double X, double Y)[] corners)
    {
        if (corners.Length != 4 || w <= 0 || h <= 0) return null;
        // Solve the 8 unknowns from 4 point pairs (standard direct linear transform).
        var src = new (double X, double Y)[] { (0, 0), (w, 0), (w, h), (0, h) };
        var m = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            var (x, y) = src[i];
            var (u, v) = corners[i];
            int r = 2 * i;
            m[r, 0] = x; m[r, 1] = y; m[r, 2] = 1; m[r, 3] = 0; m[r, 4] = 0; m[r, 5] = 0; m[r, 6] = -u * x; m[r, 7] = -u * y; m[r, 8] = u;
            m[r + 1, 0] = 0; m[r + 1, 1] = 0; m[r + 1, 2] = 0; m[r + 1, 3] = x; m[r + 1, 4] = y; m[r + 1, 5] = 1; m[r + 1, 6] = -v * x; m[r + 1, 7] = -v * y; m[r + 1, 8] = v;
        }
        // Gaussian elimination with partial pivoting.
        for (int col = 0; col < 8; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 8; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-12) return null;
            if (pivot != col)
                for (int c = 0; c < 9; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            for (int r = 0; r < 8; r++)
            {
                if (r == col) continue;
                double f = m[r, col] / m[col, col];
                if (f == 0) continue;
                for (int c = col; c < 9; c++) m[r, c] -= f * m[col, c];
            }
        }
        var k = new double[8];
        for (int i = 0; i < 8; i++) k[i] = m[i, 8] / m[i, i];
        var hm = new Homography(k[0], k[1], k[2], k[3], k[4], k[5], k[6], k[7]);
        if (!IsUsable(corners)) return null;
        return hm;
    }

    /// <summary>The inverse transform (null when it can't be inverted).</summary>
    public Homography? Invert()
    {
        // The adjugate of [[A,B,C],[D,E,F],[G,H,I]] (a homography doesn't change when its matrix is scaled).
        double a = A, b = B, c = C, d = D, e = E, f = F, g = G, h = H, i = I;
        double co00 = e * i - f * h, co01 = -(d * i - f * g), co02 = d * h - e * g;
        double det = a * co00 + b * co01 + c * co02;
        if (Math.Abs(det) < 1e-12) return null;
        double n00 = co00, n01 = -(b * i - c * h), n02 = b * f - c * e;
        double n10 = co01, n11 = a * i - c * g, n12 = -(a * f - c * d);
        double n20 = co02, n21 = -(a * h - b * g), n22 = a * e - b * d;
        // Scale to a comfortable size (any non-zero factor gives the same transform).
        double k = 1 / det;
        return new Homography(n00 * k, n01 * k, n02 * k, n10 * k, n11 * k, n12 * k, n20 * k, n21 * k, n22 * k);
    }

    /// <summary>True for a convex quadrilateral (corners in order, not crossed, not flat): a perspective view of a rectangle.</summary>
    public static bool IsUsable((double X, double Y)[] q)
    {
        double sign = 0;
        for (int i = 0; i < 4; i++)
        {
            var (ax, ay) = q[i];
            var (bx, by) = q[(i + 1) % 4];
            var (cx, cy) = q[(i + 2) % 4];
            double cross = (bx - ax) * (cy - by) - (by - ay) * (cx - bx);
            if (Math.Abs(cross) < 1e-6) return false;
            if (sign == 0) sign = Math.Sign(cross);
            else if (Math.Sign(cross) != sign) return false;
        }
        return true;
    }
}
