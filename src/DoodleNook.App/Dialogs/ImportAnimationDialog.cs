using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Dialogs;

/// <summary>What the Import animation dialog decided: which frames (in order), the frame rate for frames without timing,
/// and the animation style.</summary>
public sealed record ImportAnimationChoice(IReadOnlyList<int> Keep, double Fps, bool PixelArt);

/// <summary>
/// Mission 012: the one compact Import animation dialog. It shows what was found (file or sequence, frame count, size,
/// timing), a filmstrip where a click leaves a frame out, an optional frame range, the frame rate for image sequences and
/// the style, and says how the four layers are prepared. No wizard.
/// </summary>
public static class ImportAnimationDialog
{
    private const int ThumbSize = 56;

    public static async Task<ImportAnimationChoice?> AskImportAnimationAsync(this DialogHost host, ImportedAnimation source, string summary,
        double defaultFps)
    {
        int n = source.Frames.Count;
        var excluded = new bool[n];
        bool pixel = Math.Max(source.Width, source.Height) <= 256;
        var from = new TextBox { Text = "1", Width = 60, Name = "ImportFrom" };
        var to = new TextBox { Text = n.ToString(CultureInfo.InvariantCulture), Width = 60, Name = "ImportTo" };
        var fps = new TextBox { Text = defaultFps.ToString("0.##", CultureInfo.CurrentCulture), Width = 60, Name = "ImportFps" };
        var count = new TextBlock { Classes = { "dim", "num" }, VerticalAlignment = VerticalAlignment.Center, Name = "ImportCount" };
        var memory = new TextBlock { Classes = { "dim" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#FF8A7A")), IsVisible = false, TextWrapping = TextWrapping.Wrap };

        // The filmstrip: every frame, small; a click leaves it out (or brings it back).
        var strip = new WrapPanel { Name = "ImportFilmstrip" };
        var tiles = new List<Button>();
        for (int i = 0; i < n; i++)
        {
            int index = i;
            var img = new Image { Width = ThumbSize, Height = ThumbSize, Stretch = Stretch.Uniform, Source = BrushPreview.ToBitmap(Thumb(source.Frames[i])) };
            RenderOptions.SetBitmapInterpolationMode(img, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
            var label = new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "dim" } };
            var tile = UiKit.Button(null, null, $"Frame {i + 1}: click to leave it out of the import (or bring it back)");
            tile.Content = new StackPanel { Spacing = 2, Children = { new Border { Child = img, Background = UiKit.CheckerBrush(), CornerRadius = new CornerRadius(4) }, label } };
            tile.Padding = new Thickness(3);
            tile.Margin = new Thickness(0, 0, 4, 4);
            tile.Name = $"ImportFrame{i + 1}";
            tile.Click += (_, _) =>
            {
                excluded[index] = !excluded[index];
                Refresh();
            };
            tiles.Add(tile);
            strip.Children.Add(tile);
        }

        List<int> Keep()
        {
            int a = Parse(from.Text, 1), b = Parse(to.Text, n);
            a = Math.Clamp(a, 1, n);
            b = Math.Clamp(b, a, n);
            return Enumerable.Range(a - 1, b - a + 1).Where(i => !excluded[i]).ToList();
        }

        void Refresh()
        {
            var keep = Keep();
            var set = keep.ToHashSet();
            for (int i = 0; i < n; i++)
            {
                tiles[i].Opacity = set.Contains(i) ? 1 : 0.3;
                tiles[i].Classes.Set("on", set.Contains(i));
            }
            count.Text = keep.Count == n ? $"all {n} frames" : $"{keep.Count} of {n} frames";
            long bytes = keep.Count == 0 ? 0 : AnimationImport.EstimateBytes(source, keep);
            memory.Text = $"Memory for the reference drawings: about {bytes / 1048576.0:0} MB" +
                          (bytes > 1_500L * 1048576 ? " — that is a lot: consider leaving frames out or a shorter range." : ".");
        }
        from.TextChanged += (_, _) => Refresh();
        to.TextChanged += (_, _) => Refresh();
        Refresh();

        var (style, _) = UiKit.Segmented(
            [(false, "brush", "Hand-drawn", "Brushes with pressure, soft edges"), (true, "pixpencil", "Pixel art", "Exact pixels, whole-number zoom")],
            () => pixel, v => pixel = v);

        var body = new StackPanel { Spacing = 10, Name = "ImportAnimationDialog" };
        body.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Name = "ImportSummary" });
        body.Children.Add(new ScrollViewer { Content = strip, MaxHeight = 214, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        body.Children.Add(UiKit.Row(8, Dim("Frames", 70), Dim("from"), from, Dim("to"), to, count));
        if (source.HasTiming)
        {
            var (tfps, durations) = AnimationImport.Timing(source.DelaysMs, defaultFps);
            int own = durations.Count(d => d is not null);
            body.Children.Add(UiKit.Row(8, Dim("Timing", 70), new TextBlock
            {
                Text = $"from the file: {tfps:0.##} frames per second" + (own > 0 ? $", {own} frame{(own == 1 ? "" : "s")} with {(own == 1 ? "its" : "their")} own duration" : ""),
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 380,
            }));
        }
        else body.Children.Add(UiKit.Row(8, Dim("Frame rate", 70), fps, Dim("frames per second")));
        body.Children.Add(UiKit.Row(8, Dim("Style", 70), style));
        foreach (var note in source.Notes) body.Children.Add(DialogHost.Text(note));
        body.Children.Add(DialogHost.Text("Four layers are prepared: Reference (these frames, 35 % and locked), Sketch, Outline and Coloring. " +
                                          "The files you chose are only read, never changed."));
        body.Children.Add(memory);
        body.Children.Add(error);

        while (true)
        {
            var r = await host.ShowAsync("Import animation", body, [("cancel", "Cancel", false), ("import", "Import", true)], "import", "cancel", 520);
            if (r != "import") return null;
            var keep = Keep();
            double f = defaultFps;
            if (keep.Count == 0) error.Text = "Choose at least one frame.";
            else if (!source.HasTiming && (!double.TryParse(fps.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out f)
                                           || f < Engine.Documents.Animation.MinFps || f > Engine.Documents.Animation.MaxFps))
                error.Text = $"Please enter a frame rate from {Engine.Documents.Animation.MinFps} to {Engine.Documents.Animation.MaxFps}.";
            else return new ImportAnimationChoice(keep, f, pixel);
            error.IsVisible = true;
        }
    }

    private static int Parse(string? text, int fallback) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static TextBlock Dim(string text, double? width = null)
    {
        var t = new TextBlock { Text = text, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center };
        if (width is { } w) t.Width = w;
        return t;
    }

    /// <summary>A small copy for the filmstrip (whole-pixel steps keep pixel art crisp).</summary>
    private static RasterImage Thumb(RasterImage frame)
    {
        int max = Math.Max(frame.Width, frame.Height);
        if (max <= ThumbSize) return frame;
        double s = (double)ThumbSize / max;
        int w = Math.Max(1, (int)Math.Round(frame.Width * s)), h = Math.Max(1, (int)Math.Round(frame.Height * s));
        return Engine.Operations.Resampler.Resize(frame, w, h, Engine.Operations.Interpolation.Bilinear);
    }
}
