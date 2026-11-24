namespace DoodleNook.App.Shell;

/// <summary>
/// Mission 018: where a value read-out goes while the pen is changing it. The owner's note: "show a pop-up with the
/// pixel size, because the pen obstructs the view". Directly above the pen tip is the one place neither the pen's
/// barrel nor the hand holding it covers (whichever hand it is); near the top edge it moves beside the pen instead.
/// </summary>
public static class PenReadout
{
    /// <summary>The gap between the pen tip and the read-out.</summary>
    public const double Gap = 40;

    /// <summary>The top-left corner for a read-out of <paramref name="width"/> × <paramref name="height"/> near the pen at
    /// (<paramref name="penX"/>, <paramref name="penY"/>), inside an area of <paramref name="areaWidth"/> ×
    /// <paramref name="areaHeight"/> (all in the same coordinates).</summary>
    public static (double X, double Y) Place(double penX, double penY, double width, double height, double areaWidth, double areaHeight)
    {
        const double margin = 6;
        double x = penX - width / 2, y = penY - Gap - height;
        if (y < margin)
        {
            // No room above: beside the pen, on the left (the right-handed hand rests to the right), else on the right.
            y = penY - height / 2;
            x = penX - Gap - width;
            if (x < margin) x = penX + Gap;
        }
        x = Math.Clamp(x, margin, Math.Max(margin, areaWidth - width - margin));
        y = Math.Clamp(y, margin, Math.Max(margin, areaHeight - height - margin));
        return (x, y);
    }
}
