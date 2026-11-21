using System.Diagnostics;

namespace DoodleNook.App.Diagnostics;

/// <summary>
/// The few timings DoodleNook keeps for Help › Diagnostics (Mission 011): how long the canvas took to draw recently, and
/// the last save, autosave, open, undo and redo. Each is two clock readings around work that happens anyway, so keeping
/// them costs nothing noticeable; nothing is measured on a timer, nothing is written anywhere and nothing leaves the
/// computer.
/// </summary>
public static class PerformanceLog
{
    private const int FrameHistory = 120;
    private static readonly object Gate = new();
    private static readonly double[] Frames = new double[FrameHistory];
    private static int _frameCount, _frameNext;
    private static readonly Dictionary<string, (double LastMs, double MaxMs, int Count)> Operations = new();

    public static long Now() => Stopwatch.GetTimestamp();

    public static double Since(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    /// <summary>One canvas frame (render thread): the time to draw the picture and its overlays.</summary>
    public static void Frame(double ms)
    {
        lock (Gate)
        {
            Frames[_frameNext] = ms;
            _frameNext = (_frameNext + 1) % FrameHistory;
            if (_frameCount < FrameHistory) _frameCount++;
        }
    }

    /// <summary>A finished operation (save, open, undo…).</summary>
    public static void Operation(string what, double ms)
    {
        lock (Gate)
        {
            Operations.TryGetValue(what, out var old);
            Operations[what] = (ms, Math.Max(old.MaxMs, ms), old.Count + 1);
        }
    }

    /// <summary>The recent canvas frames: how many, average, slowest (null before the first frame).</summary>
    public static (int Count, double AverageMs, double MaxMs)? RecentFrames()
    {
        lock (Gate)
        {
            if (_frameCount == 0) return null;
            double sum = 0, max = 0;
            for (int i = 0; i < _frameCount; i++)
            {
                sum += Frames[i];
                max = Math.Max(max, Frames[i]);
            }
            return (_frameCount, sum / _frameCount, max);
        }
    }

    public static IReadOnlyList<(string What, double LastMs, double MaxMs, int Count)> RecentOperations()
    {
        lock (Gate) return Operations.Select(kv => (kv.Key, kv.Value.LastMs, kv.Value.MaxMs, kv.Value.Count)).OrderBy(o => o.Key).ToList();
    }
}
