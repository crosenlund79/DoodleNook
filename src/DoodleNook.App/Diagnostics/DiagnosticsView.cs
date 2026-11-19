using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DoodleNook.App.Canvas;
using DoodleNook.App.Controls;
using DoodleNook.App.Editor;
using DoodleNook.App.Input;

namespace DoodleNook.App.Diagnostics;

/// <summary>
/// The small pen diagnostics panel (F12). Hidden during normal drawing. Shows what Windows delivers and what the brush
/// does with it, to tell driver problems (no pen / no pressure) apart from brush or rendering problems.
/// </summary>
public sealed class DiagnosticsView : StackPanel
{
    private readonly CanvasView _canvas;
    private readonly EditorSession _session;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Grid _grid = new() { ColumnDefinitions = new ColumnDefinitions("88,*"), RowSpacing = 3 };
    private readonly Dictionary<string, TextBlock> _values = new();
    private readonly PressureGraph _graph = new() { Height = 54 };
    private readonly TextBlock _verdict = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(DnColors.Text2) };

    public DiagnosticsView(CanvasView canvas, EditorSession session, Action close)
    {
        _canvas = canvas;
        _session = session;
        Spacing = 8;
        Children.Add(Ui.Header("Pen diagnostics", close));
        foreach (var name in new[] { "Input", "Pressure", "Brush size", "Tilt", "Buttons", "Events", "Last gap", "Engine", "Renderer", "Display", "Windows pen" })
        {
            int row = _grid.RowDefinitions.Count;
            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = name, Classes = { "dim" }, FontSize = 12 };
            var value = new TextBlock { Classes = { "num" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            _grid.Children.Add(label);
            _grid.Children.Add(value);
            _values[name] = value;
        }
        Children.Add(_grid);
        var graphTitle = new TextBlock { Text = "Pressure during the last stroke", Classes = { "dim" }, FontSize = 12 };
        Children.Add(graphTitle);
        Children.Add(_graph);
        Children.Add(_verdict);
        _timer.Tick += (_, _) => Update();
    }

    public void SetActive(bool active)
    {
        if (active)
        {
            Update();
            _timer.Start();
        }
        else _timer.Stop();
    }

    private void Set(string key, string text) => _values[key].Text = text;

    private void Update()
    {
        var d = _canvas.Diagnostics;
        string kind = d.LastKind switch
        {
            PointerKind.Pen => "Pen (Windows Ink)",
            PointerKind.Touch => "Touch",
            PointerKind.Mouse => "Mouse",
            _ => "—",
        };
        Set("Input", d.LastKind is null ? "move the pen over the canvas" : $"{kind} · {(d.InContact ? "touching" : "hovering")}");
        Set("Pressure", d.LastKind == PointerKind.Pen
            ? $"{d.RawPressure:0.000} raw → {d.MappedPressure:0.000} ({_session.PressureFeel.ToString().ToLowerInvariant()} curve)"
            : $"{d.RawPressure:0.000} raw · mouse uses {CanvasInputController.MousePressure:0.0}");
        Set("Brush size", $"{d.BrushDiameter:0.0} px now · slider {_session.CurrentPaintSettings.Size:0} px");
        Set("Tilt", d.LastKind == PointerKind.Pen ? $"x {d.TiltX:0}° · y {d.TiltY:0}°" : "—");
        var buttons = new List<string>();
        if (d.Barrel) buttons.Add("barrel");
        if (d.RightButton) buttons.Add("right");
        if (d.MiddleButton) buttons.Add("middle");
        if (d.EraserEnd) buttons.Add("eraser end");
        Set("Buttons", buttons.Count == 0 ? "none pressed" : string.Join(" · ", buttons));
        Set("Events", $"{d.EventsPerSecond:0}/s · {d.SamplesPerEvent:0.0} samples each · {d.SamplesPerSecond:0} samples/s");
        Set("Last gap", d.LastGapMs > 0 ? $"{d.LastGapMs:0.0} ms between events" : "—");
        Set("Engine", d.EngineMsMax > 0 ? $"{d.EngineMsAverage:0.00} ms avg · {d.EngineMsMax:0.00} ms max per event" : "—");
        Set("Renderer", _canvas.UsesGpu ? "GPU texture (Skia), dirty-area uploads" : "software (Skia raster)");
        Set("Display", $"{_canvas.RenderScaling * 100:0}% Windows scaling · zoom {_canvas.ActualZoom * 100:0}%");
        Set("Windows pen", TopLevel.GetTopLevel(this) is MainWindow w ? w.PenSetupStatus : "—");
        _graph.Values = d.StrokePressure;
        _graph.InvalidateVisual();
        _verdict.Text = d.Verdict;
    }
}

/// <summary>A tiny line graph of pressure over the last stroke.</summary>
public sealed class PressureGraph : Control
{
    public IReadOnlyList<float> Values { get; set; } = Array.Empty<float>();

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(DnColors.Ui), new Pen(new SolidColorBrush(DnColors.Line), 1), new RoundedRect(r, 6));
        var v = Values;
        if (v.Count < 2) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i < v.Count; i++)
            {
                var p = new Point(4 + (r.Width - 8) * i / (v.Count - 1), r.Height - 4 - (r.Height - 8) * Math.Clamp(v[i], 0, 1));
                if (i == 0) g.BeginFigure(p, false);
                else g.LineTo(p);
            }
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(DnColors.AccentBrush, 1.5), geo);
    }
}
