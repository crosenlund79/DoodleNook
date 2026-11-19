using DoodleNook.App.Input;

namespace DoodleNook.App.Diagnostics;

/// <summary>
/// Collects what the canvas receives from Windows, for the pen diagnostics panel. Everything stays on this computer:
/// nothing is logged to disk or sent anywhere.
/// </summary>
public sealed class InputDiagnostics
{
    private readonly Queue<(double TimeMs, int Samples)> _window = new();
    private readonly Queue<(double TimeMs, double EngineMs)> _engineWindow = new();
    private double _lastEventMs = double.NaN;

    public PointerKind? LastKind { get; private set; }
    public bool InContact { get; private set; }
    public float RawPressure { get; private set; }
    public float MappedPressure { get; private set; }
    public double BrushDiameter { get; private set; }
    public float TiltX { get; private set; }
    public float TiltY { get; private set; }
    public bool Barrel { get; private set; }
    public bool EraserEnd { get; private set; }
    public bool RightButton { get; private set; }
    public bool MiddleButton { get; private set; }

    public bool SeenPen { get; private set; }
    public bool SeenPressureVariation { get; private set; }
    private float _firstPenPressure = float.NaN;

    public double EventsPerSecond { get; private set; }
    public double SamplesPerSecond { get; private set; }
    public double SamplesPerEvent { get; private set; }
    public double LastGapMs { get; private set; }
    public double EngineMsAverage { get; private set; }
    public double EngineMsMax { get; private set; }

    /// <summary>Pressure of the current or last stroke (raw, 0–1), for the little graph.</summary>
    public List<float> StrokePressure { get; } = new();

    public event Action? Updated;

    public void StrokeStarted() => StrokePressure.Clear();

    /// <summary>Records one input event with its coalesced samples (timestamp in milliseconds).</summary>
    public void Record(ulong timestampMs, PointerKind kind, int sampleCount, bool contact, float pressure,
        float tiltX, float tiltY, bool barrel, bool eraser, bool right, bool middle, bool recordPressure)
    {
        double t = timestampMs;
        LastKind = kind;
        InContact = contact;
        RawPressure = pressure;
        TiltX = tiltX;
        TiltY = tiltY;
        Barrel = barrel;
        EraserEnd = eraser;
        RightButton = right;
        MiddleButton = middle;

        if (kind == PointerKind.Pen)
        {
            SeenPen = true;
            if (contact)
            {
                if (float.IsNaN(_firstPenPressure)) _firstPenPressure = pressure;
                else if (Math.Abs(pressure - _firstPenPressure) > 0.02f) SeenPressureVariation = true;
            }
        }

        if (contact && !double.IsNaN(_lastEventMs)) LastGapMs = t - _lastEventMs;
        _lastEventMs = t;

        _window.Enqueue((t, sampleCount));
        while (_window.Count > 0 && t - _window.Peek().TimeMs > 1000) _window.Dequeue();
        int events = _window.Count, samples = _window.Sum(w => w.Samples);
        double span = Math.Max(1, t - _window.Peek().TimeMs);
        EventsPerSecond = events > 1 ? (events - 1) * 1000.0 / span : 0;
        SamplesPerSecond = events > 1 ? (samples - _window.Peek().Samples) * 1000.0 / span : 0;
        SamplesPerEvent = events > 0 ? (double)samples / events : 0;

        if (recordPressure && contact && StrokePressure.Count < 4000) StrokePressure.Add(pressure);
        Updated?.Invoke();
    }

    /// <summary>Records the brush response to the latest sample.</summary>
    public void RecordBrush(float mappedPressure, double diameter)
    {
        MappedPressure = mappedPressure;
        BrushDiameter = diameter;
    }

    /// <summary>Records how long the engine took to process one input event (stamping and compositing).</summary>
    public void RecordEngineTime(double nowMs, double engineMs)
    {
        _engineWindow.Enqueue((nowMs, engineMs));
        while (_engineWindow.Count > 0 && nowMs - _engineWindow.Peek().TimeMs > 1000) _engineWindow.Dequeue();
        EngineMsAverage = _engineWindow.Average(e => e.EngineMs);
        EngineMsMax = _engineWindow.Max(e => e.EngineMs);
    }

    public void PointerLifted()
    {
        InContact = false;
        _lastEventMs = double.NaN;
        Updated?.Invoke();
    }

    /// <summary>A short plain-language verdict about the pen connection.</summary>
    public string Verdict =>
        !SeenPen ? "No pen input yet. If the pen moves the cursor but shows as Mouse, turn on “Windows Ink” in the Huion driver."
        : !SeenPressureVariation ? "Pen detected. Press lightly, then firmly, to check pressure."
        : "Pen and pressure are arriving.";
}
