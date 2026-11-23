using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Diagnostics;

/// <summary>
/// Help › Diagnostics (Mission 011): what DoodleNook is doing under the hood — version, picture size, layers, frames,
/// memory, recent timings and caches. Only values DoodleNook actually knows are shown (a timing appears once it has
/// happened). The copied text has no artwork, no file names and no folders. Nothing is measured while the window is
/// closed beyond the clock readings around work that happens anyway (see <see cref="PerformanceLog"/>).
/// </summary>
public static class DiagnosticsReport
{
    public sealed record Section(string Title, List<(string Key, string Value)> Rows);

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString(bytes < 10 * 1048576 ? "0.0" : "0", CultureInfo.InvariantCulture) + " MB";

    private static string Ms(double ms) => ms.ToString(ms < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " ms";

    public static List<Section> Build(MainWindow w)
    {
        var sections = new List<Section>();
        var doc = w.Session.Document;

        sections.Add(new("DoodleNook", [
            ("Version", $"{MainWindow.Version} ({MainWindow.Milestone})"),
            ("Runtime", $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}"),
            ("System", $"{RuntimeInformation.OSDescription}, {Environment.ProcessorCount} logical processors"),
            ("Canvas drawing", w.Canvas.UsesGpu ? "GPU texture (Skia)" : "software (Skia raster)"),
            ("Display", $"{w.Canvas.RenderScaling * 100:0}% scaling · zoom {w.Canvas.ActualZoom * 100:0.#}%"),
        ]));

        var rows = new List<(string, string)> { ("Size", $"{doc.Width} × {doc.Height} px") };
        var kinds = doc.Layers.GroupBy(l => l.Kind).Select(g => $"{g.Count()} {Kind(g.Key, g.Count())}");
        rows.Add(("Layers", $"{doc.Layers.Count} ({string.Join(", ", kinds)})"));
        if (doc.IsAnimated) rows.Add(("Frames", $"{doc.FrameCount} ({doc.AllCels().Count()} drawings) at {doc.Animation!.Fps:0.#} fps"));
        rows.Add(("Palette", doc.PaletteLocked ? $"locked to {doc.Palette.Colors.Count} colours" : "not locked"));
        rows.Add(("Picture memory", $"{Mb(PictureBytes(doc))} (layers, drawings, masks and the composite)"));
        var h = doc.History;
        rows.Add(("Undo history", $"{h.UndoCount} step{(h.UndoCount == 1 ? "" : "s")} to undo{(h.RedoCount > 0 ? $", {h.RedoCount} to redo" : "")}, " +
                                  $"{Mb(h.TotalBytes)} (limit {h.MaxEntries} steps, {Mb(h.MaxBytes)})"));
        if (doc.StrokeCacheBytes > 0) rows.Add(("Painting cache", $"{Mb(doc.StrokeCacheBytes)} (the picture under the painted layer)"));
        if (doc.IsAnimated) rows.Add(("Frame cache", $"{Mb(doc.FrameCacheBytes)} of {Mb(doc.FrameCacheBudget)}"));
        sections.Add(new("Picture", rows));

        var gc = GC.GetGCMemoryInfo();
        using var process = Process.GetCurrentProcess();
        sections.Add(new("Memory", [
            ("Managed objects", Mb(GC.GetTotalMemory(false))),
            ("Managed heap", $"{Mb(gc.TotalCommittedBytes)} committed"),
            ("Working set", $"{Mb(Environment.WorkingSet)} now, {Mb(process.PeakWorkingSet64)} at most"),
            ("Garbage collections", $"{GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)} (gen 0/1/2), " +
                                    $"{GC.GetTotalPauseDuration().TotalMilliseconds:0} ms paused in total"),
        ]));

        var timings = new List<(string, string)>();
        if (PerformanceLog.RecentFrames() is { } f)
            timings.Add(("Canvas frames", $"{Ms(f.AverageMs)} average, {Ms(f.MaxMs)} slowest (last {f.Count})"));
        var input = w.Canvas.Diagnostics;
        if (input.EngineMsMax > 0) timings.Add(("Brush, last stroke", $"{Ms(input.EngineMsAverage)} average, {Ms(input.EngineMsMax)} slowest per pen event"));
        foreach (var (what, last, max, count) in PerformanceLog.RecentOperations())
            timings.Add((what, count > 1 ? $"{Ms(last)} last, {Ms(max)} slowest ({count}×)" : Ms(last)));
        if (timings.Count == 0) timings.Add(("—", "nothing measured yet: draw, undo or save first"));
        sections.Add(new("Recent timings", timings));

        var lib = w.Session.Library;
        var caches = new List<(string, string)>
        {
            ("Creative library", $"{lib.Brushes.Count} brushes, {lib.Stamps.Count} stamps, {lib.Patterns.Count} patterns, {lib.Palettes.Count} palettes"),
            ("Canvas uploads", $"{Mb(w.Canvas.UploadedPixels * 4)} sent to the screen so far"),
        };
        if (w.Catalog is { } catalog) caches.Add(("Game Edit thumbnails", $"{catalog.ThumbnailCount} of {w.Game?.Images.Count ?? 0} images"));
        sections.Add(new("Caches", caches));
        return sections;
    }

    private static string Kind(LayerKind kind, int n) => kind switch
    {
        LayerKind.Raster => n == 1 ? "paint" : "paint",
        LayerKind.Group => n == 1 ? "group" : "groups",
        LayerKind.Adjustment => n == 1 ? "adjustment" : "adjustments",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>The bytes of every distinct pixel buffer the picture holds (counted once even when shared, e.g. held drawings).</summary>
    private static long PictureBytes(Document doc)
    {
        var seen = new HashSet<RasterImage>(ReferenceEqualityComparer.Instance);
        long bytes = 0;
        void Add(RasterImage? image)
        {
            if (image is not null && seen.Add(image)) bytes += (long)image.Pixels.Length * 4;
        }
        Add(doc.Projection);
        foreach (var l in doc.Layers)
        {
            Add(l.Image);
            Add(l.Mask);
        }
        foreach (var cel in doc.AllCels()) Add(cel.Image);
        return bytes;
    }

    /// <summary>The report as plain text for the clipboard (numbers only: no artwork, names or paths).</summary>
    public static string ToText(IEnumerable<Section> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DoodleNook diagnostics — {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var s in sections)
        {
            sb.AppendLine();
            sb.AppendLine(s.Title);
            foreach (var (k, v) in s.Rows) sb.AppendLine($"  {k}: {v}");
        }
        return sb.ToString();
    }

    /// <summary>The window's content: the sections, refreshed every second while it is open, and a Copy button.</summary>
    public static Control View(MainWindow w, out Action stop)
    {
        var grid = new StackPanel { Spacing = 10 };
        var copied = new TextBlock { Classes = { "dim" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        void Fill()
        {
            grid.Children.Clear();
            foreach (var s in Build(w))
            {
                // Mission 017: long timing names ("Save: compress and write (background)") wrap in their column
                // instead of running into the value.
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), RowSpacing = 3 };
                foreach (var (k, v) in s.Rows)
                {
                    int row = g.RowDefinitions.Count;
                    g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    var label = new TextBlock { Text = k, Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
                    var value = new TextBlock { Text = v, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                    Grid.SetRow(label, row);
                    Grid.SetRow(value, row);
                    Grid.SetColumn(value, 1);
                    g.Children.Add(label);
                    g.Children.Add(value);
                }
                grid.Children.Add(new StackPanel { Spacing = 4, Children = { new TextBlock { Text = s.Title, FontWeight = FontWeight.SemiBold }, g } });
            }
        }
        Fill();
        var copy = UiKit.Button("copy", "Copy diagnostics", "Copy these numbers as text (no artwork, names or folders)", () => _ = CopyAsync());
        copy.Name = "CopyDiagnostics";
        copy.Height = 32;
        async Task CopyAsync()
        {
            string text = ToText(Build(w));
            LastCopiedText = text;
            if (TopLevel.GetTopLevel(w)?.Clipboard is { } clipboard)
            {
                try
                {
                    await clipboard.SetTextAsync(text);
                    copied.Text = "Copied";
                }
                catch (Exception)
                {
                    copied.Text = "Could not copy";
                }
            }
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Fill();
        timer.Start();
        stop = timer.Stop;
        var root = new StackPanel { Spacing = 12, Name = "DiagnosticsReport" };
        root.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 460, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        root.Children.Add(UiKit.Row(8, copy, copied));
        return root;
    }

    /// <summary>The last text copied (for tests, where there may be no clipboard).</summary>
    public static string? LastCopiedText { get; private set; }
}
