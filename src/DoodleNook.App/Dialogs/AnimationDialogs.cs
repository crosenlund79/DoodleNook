using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Files;
using DoodleNook.App.Settings;
using DoodleNook.App.Shell;
using DoodleNook.App.Controls;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Dialogs;

/// <summary>What the New animation dialog asks for.</summary>
public sealed record NewAnimationChoice(int Width, int Height, int Frames, double Fps, bool PixelArt, ColorRgb? Paper);

/// <summary>The animation dialogs: new animation, export animation, frame duration, recovery and preferences.</summary>
public static class AnimationDialogs
{
    private static TextBox Number(int value, double width = 70) => new() { Text = value.ToString(), Width = width };

    private static TextBlock Dim(string text, double? width = null)
    {
        var t = new TextBlock { Text = text, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center };
        if (width is { } w) t.Width = w;
        return t;
    }

    private static TextBlock Error() => new() { Foreground = new SolidColorBrush(Color.Parse("#FF8A7A")), IsVisible = false, TextWrapping = TextWrapping.Wrap };

    private static Button Preset(string label, Action click)
    {
        var b = UiKit.Button(null, label);
        b.Height = 28;
        b.Margin = new Thickness(0, 0, 6, 6);
        b.Background = new SolidColorBrush(DnColors.Ui3);
        b.Click += (_, _) => click();
        return b;
    }

    // ================================================================== new animation

    /// <summary>New animation: drawing style, size, number of frames and frame rate. Null when cancelled.</summary>
    public static async Task<NewAnimationChoice?> AskNewAnimationAsync(this DialogHost host, bool pixelArt, int width, int height)
    {
        bool pixel = pixelArt;
        bool white = !pixelArt;
        var w = Number(width, 80);
        var h = Number(height, 80);
        var frames = Number(8, 60);
        var fps = Number(pixelArt ? 8 : 12, 60);
        var presets = new WrapPanel();
        var (whiteToggle, refreshWhite) = UiKit.Check("White paper", "Off: transparent frames (sprites, overlays)", () => white, v => white = v);

        void FillPresets()
        {
            presets.Children.Clear();
            var sizes = pixel
                ? new[] { (16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256) }
                : new[] { (640, 480), (1280, 720), (1920, 1080), (1080, 1080) };
            foreach (var (pw, ph) in sizes)
                presets.Children.Add(Preset($"{pw} × {ph}", () =>
                {
                    w.Text = pw.ToString();
                    h.Text = ph.ToString();
                }));
        }

        Action refreshStyle = () => { };
        var (style, refreshSeg) = UiKit.Segmented(
            [(false, "brush", "Hand-drawn", "Brushes with pressure, soft edges, layers"),
             (true, "pixpencil", "Pixel art", "Exact pixels, whole-number zoom, crisp onion skins")],
            () => pixel,
            v =>
            {
                if (pixel == v) return;
                pixel = v;
                w.Text = v ? "64" : "1280";
                h.Text = v ? "64" : "720";
                fps.Text = v ? "8" : "12";
                white = !v;
                refreshWhite();
                FillPresets();
                refreshStyle();
            });
        refreshStyle = refreshSeg;
        FillPresets();

        var error = Error();
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(UiKit.Row(8, Dim("Style", 64), style));
        body.Children.Add(presets);
        body.Children.Add(UiKit.Row(8, Dim("Size", 64), w, Dim("×"), h, Dim("px")));
        body.Children.Add(UiKit.Row(8, Dim("Frames", 64), frames, Dim("at"), fps, Dim("frames per second")));
        body.Children.Add(UiKit.Row(8, Dim("Paper", 64), whiteToggle));
        body.Children.Add(DialogHost.Text("You can add, remove and retime frames at any time."));
        body.Children.Add(error);
        while (true)
        {
            var r = await host.ShowAsync("New animation", body, [("cancel", "Cancel", false), ("create", "Create", true)], "create", "cancel", 440);
            if (r != "create") return null;
            if (!int.TryParse(w.Text, out int iw) || !int.TryParse(h.Text, out int ih) || !RasterImage.IsSupportedSize(iw, ih))
                error.Text = $"Please enter a size from 1 to {RasterImage.MaxDimension} pixels per side.";
            else if (!int.TryParse(frames.Text, out int n) || n < 1 || n > 999)
                error.Text = "Please enter 1 to 999 frames.";
            else if (!double.TryParse(fps.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double f)
                     || f < Engine.Documents.Animation.MinFps || f > Engine.Documents.Animation.MaxFps)
                error.Text = $"Please enter a frame rate from {Engine.Documents.Animation.MinFps} to {Engine.Documents.Animation.MaxFps}.";
            else
                return new NewAnimationChoice(iw, ih, n, f, pixel, white ? ColorRgb.White : null);
            error.IsVisible = true;
        }
    }

    // ================================================================== export animation

    public static string FormatName(AnimationExportFormat f) => f switch
    {
        AnimationExportFormat.SpriteSheet => "Sprite sheet",
        AnimationExportFormat.PngSequence => "PNG sequence",
        _ => "Animated GIF",
    };

    /// <summary>
    /// Export animation: choose the format; only that format's options are shown. Returns the (remembered) choices, or
    /// null when cancelled.
    /// </summary>
    public static async Task<AnimationExportSettings?> AskExportAnimationAsync(this DialogHost host, AnimationExportSettings start, int frames, int width, int height, bool hasPaper)
    {
        var s = start with
        {
            Columns = Math.Clamp(start.Columns, 1, 999),
            Spacing = Math.Clamp(start.Spacing, 0, 256),
            Padding = Math.Clamp(start.Padding, 0, 256),
            Margin = Math.Clamp(start.Margin, 0, 256),
            Scale = Math.Clamp(start.Scale, 1, 16),
        };
        var options = new StackPanel { Spacing = 10 };
        var summary = new TextBlock { Classes = { "dim", "num" }, TextWrapping = TextWrapping.Wrap };
        var error = Error();

        // The format's option controls are created afresh whenever the format changes. (Mission 006 reused the same
        // number boxes, still attached to the previous rows: switching back to Sprite sheet threw and crashed the app.)
        // Every box writes its value into the settings as it changes, so nothing has to be read back later.
        TextBox NumberBox(int value, int min, int max, Func<AnimationExportSettings, int, AnimationExportSettings> set)
        {
            var box = Number(value, 60);
            box.TextChanged += (_, _) =>
            {
                if (int.TryParse(box.Text, out int v)) s = set(s, Math.Clamp(v, min, max));
                Summarise();
            };
            return box;
        }

        void Summarise()
        {
            int fw = width * s.Scale, fh = height * s.Scale;
            string what = s.Format switch
            {
                AnimationExportFormat.SpriteSheet => SheetSummary(fw, fh),
                AnimationExportFormat.PngSequence => $"{frames} PNG files of {fw} × {fh}, named like name_0001.png",
                _ => $"One GIF of {fw} × {fh} with {frames} frames",
            };
            summary.Text = what;
        }

        string SheetSummary(int fw, int fh)
        {
            var o = new SpriteSheetOptions(s.Layout, s.Columns, s.Spacing, s.Padding, s.Margin).Normalized();
            int cols = o.Layout switch { SheetLayout.Horizontal => frames, SheetLayout.Vertical => 1, _ => Math.Min(o.Columns, frames) };
            int rows = (frames + cols - 1) / cols;
            int cw = fw + 2 * o.Padding, ch = fh + 2 * o.Padding;
            int sw = 2 * o.Margin + cols * cw + (cols - 1) * o.Spacing, sh = 2 * o.Margin + rows * ch + (rows - 1) * o.Spacing;
            return $"{frames} frames of {fw} × {fh} in {cols} × {rows} → {sw} × {sh} px{(s.Json ? " + .json" : "")}";
        }

        Action rebuild = () => { };
        var (format, refreshFormat) = UiKit.Segmented(
            [(AnimationExportFormat.SpriteSheet, null, "Sprite sheet", "All frames in one PNG (game engines), optionally with JSON"),
             (AnimationExportFormat.PngSequence, null, "PNG sequence", "One numbered PNG per frame"),
             (AnimationExportFormat.Gif, null, "Animated GIF", "Plays in browsers and chats")],
            () => s.Format, v => { s = s with { Format = v }; rebuild(); });

        Control ToggleRow(string label, string tip, Func<bool> get, Action<bool> set)
        {
            var (b, _) = UiKit.Check(label, tip, get, v =>
            {
                set(v);
                Summarise();
            });
            return b;
        }

        Control ScaleRow()
        {
            Action refreshScale = () => { };
            var (seg, refresh) = UiKit.Segmented(new[] { 1, 2, 3, 4, 8 }.Select(n => (n, (string?)null, (string?)$"{n}×", (string?)$"Scale every frame {n}× with exact pixels")),
                () => s.Scale, v => { s = s with { Scale = v }; refreshScale(); Summarise(); });
            refreshScale = refresh;
            return UiKit.Row(8, Dim("Scale", 76), seg);
        }

        rebuild = () =>
        {
            refreshFormat();
            options.Children.Clear();
            switch (s.Format)
            {
                case AnimationExportFormat.SpriteSheet:
                {
                    Action refreshLayout = () => { };
                    var (layout, rl) = UiKit.Segmented(
                        [(SheetLayout.Horizontal, null, "Row", "All frames side by side"),
                         (SheetLayout.Vertical, null, "Column", "All frames stacked"),
                         (SheetLayout.Grid, null, "Grid", "Rows of a fixed number of columns")],
                        () => s.Layout, v => { s = s with { Layout = v }; refreshLayout(); rebuild(); });
                    refreshLayout = rl;
                    options.Children.Add(UiKit.Row(8, Dim("Layout", 76), layout));
                    if (s.Layout == SheetLayout.Grid)
                        options.Children.Add(UiKit.Row(8, Dim("Columns", 76), NumberBox(s.Columns, 1, 999, (x, v) => x with { Columns = v })));
                    options.Children.Add(UiKit.Row(8, Dim("Spacing", 76),
                        NumberBox(s.Spacing, 0, 256, (x, v) => x with { Spacing = v }), Dim("Padding"),
                        NumberBox(s.Padding, 0, 256, (x, v) => x with { Padding = v }), Dim("Margin"),
                        NumberBox(s.Margin, 0, 256, (x, v) => x with { Margin = v }), Dim("px")));
                    options.Children.Add(ScaleRow());
                    options.Children.Add(UiKit.Row(8, Dim("", 76),
                        ToggleRow("JSON metadata", "A .json file next to the PNG with every frame's position and duration", () => s.Json, v => s = s with { Json = v })));
                    break;
                }
                case AnimationExportFormat.PngSequence:
                    options.Children.Add(ScaleRow());
                    break;
                default:
                    options.Children.Add(ScaleRow());
                    options.Children.Add(UiKit.Row(8, Dim("Playback", 76),
                        ToggleRow("Loop forever", "Off: the GIF plays once and stops on the last frame", () => s.Loop, v => s = s with { Loop = v })));
                    options.Children.Add(UiKit.Row(8, Dim("Colours", 76),
                        ToggleRow("Smooth gradients (dither)", "GIF has at most 256 colours. Pictures with more are reduced; dithering hides banding. Pixel art with few colours is kept exact.",
                            () => s.Dither, v => s = s with { Dither = v })));
                    break;
            }
            if (hasPaper)
                options.Children.Add(UiKit.Row(8, Dim("Paper", 76),
                    ToggleRow("Include the paper", "Off: transparent background (the paper colour is left out)", () => s.Background, v => s = s with { Background = v })));
            else
                options.Children.Add(DialogHost.Text("Transparent areas stay transparent."));
            Summarise();
        };
        rebuild();

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(format);
        body.Children.Add(options);
        body.Children.Add(summary);
        body.Children.Add(DialogHost.Text("Onion skins and the timeline are never exported."));
        body.Children.Add(error);
        var r = await host.ShowAsync("Export animation", body, [("cancel", "Cancel", false), ("ok", "Choose file…", true)], "ok", "cancel", 520);
        return r == "ok" ? s : null;
    }

    // ================================================================== frame duration

    /// <summary>How long one frame is shown. Returns (true, ms) or (true, null) for "follow the frame rate"; null when cancelled.</summary>
    public static async Task<(bool Ok, int? Ms)?> AskFrameDurationAsync(this DialogHost host, int frame, int currentMs, bool isOverride, double fps)
    {
        var box = Number(currentMs, 80);
        var error = Error();
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(DialogHost.Text($"Frame {frame + 1} is shown for {currentMs} ms{(isOverride ? "" : $" (from {fps:0.##} fps)")}. A longer frame is a pause; a shorter one speeds up a moment."));
        body.Children.Add(UiKit.Row(8, Dim("Duration", 70), box, Dim("ms (10 – 10000)")));
        body.Children.Add(error);
        while (true)
        {
            var r = await host.ShowAsync("Frame duration", body, [("auto", "Use frame rate", false), ("cancel", "Cancel", false), ("ok", "Set", true)], "ok", "cancel", 400);
            if (r == "auto") return (true, null);
            if (r != "ok") return null;
            if (int.TryParse(box.Text, out int ms) && ms is >= 10 and <= 10000) return (true, ms);
            error.Text = "Please enter 10 to 10000 milliseconds.";
            error.IsVisible = true;
        }
    }

    // ================================================================== recovery and preferences

    /// <summary>Offers a recovery copy. Returns "recover", "discard" or "later".</summary>
    public static async Task<string> AskRecoverAsync(this DialogHost host, RecoveryEntry entry, int others)
    {
        string when = entry.SavedAt.ToLocalTime().ToString("g");
        string what = entry.IsAnimation ? "animation" : "picture";
        var text = $"DoodleNook didn't close normally last time. Unsaved work was kept: “{entry.DisplayName}” ({what}, saved automatically {when}).";
        if (entry.OriginalPath is not null) text += $"\n\nIts file: {entry.OriginalPath}. The file itself was not changed.";
        if (others > 0) text += $"\n\n{others} more recovery cop{(others == 1 ? "y is" : "ies are")} waiting and will be offered next time.";
        var r = await host.ShowAsync("Recover unsaved work?", DialogHost.Text(text),
            [("discard", "Discard", false), ("later", "Not now", false), ("recover", "Recover", true)], "recover", "later", 460);
        return r ?? "later";
    }

    public static readonly (int Seconds, string Label)[] AutosaveChoices = [(0, "Off"), (30, "30 s"), (60, "1 min"), (120, "2 min"), (300, "5 min")];

    /// <summary>Preferences: currently the autosave interval. Returns the chosen seconds, or null when cancelled.</summary>
    public static async Task<int?> AskPreferencesAsync(this DialogHost host, int autosaveSeconds)
    {
        int chosen = AutosaveChoices.Any(c => c.Seconds == autosaveSeconds) ? autosaveSeconds : 60;
        Action refresh = () => { };
        var (seg, r) = UiKit.Segmented(AutosaveChoices.Select(c => (c.Seconds, (string?)null, (string?)c.Label, (string?)null)), () => chosen, v => { chosen = v; refresh(); });
        refresh = r;
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(UiKit.Row(8, Dim("Autosave", 80), seg));
        body.Children.Add(DialogHost.Text("While you work, DoodleNook keeps a recovery copy of unsaved changes on this computer " +
                                          "(never over your own file). If DoodleNook or Windows closes unexpectedly, it offers the copy at the next start."));
        var result = await host.ShowAsync("Preferences", body, [("cancel", "Cancel", false), ("ok", "OK", true)], "ok", "cancel", 460);
        return result == "ok" ? chosen : null;
    }
}
