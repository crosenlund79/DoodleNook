using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.App.Dialogs;

public enum AdjustKind
{
    BrightnessContrast,
    HueSaturation,
    Blur,
    Sharpen,
    Levels,
    ColourBalance,
}

/// <summary>The image dialogs: image size, canvas size, colour adjustments (with live preview) and export options.</summary>
public static class EditDialogs
{
    private static TextBox Number(int value) => new() { Text = value.ToString(), Width = 90 };

    private static TextBlock Dim(string text, double? width = null)
    {
        var t = new TextBlock { Text = text, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center };
        if (width is { } w) t.Width = w;
        return t;
    }

    private static TextBlock Error() => new() { Foreground = new SolidColorBrush(Color.Parse("#FF8A7A")), IsVisible = false, TextWrapping = TextWrapping.Wrap };

    private static string SizeError => $"Please enter a size from 1 to {RasterImage.MaxDimension} pixels per side (up to {RasterImage.MaxPixels / 1_000_000} megapixels).";

    /// <summary>Image size: scales every layer. Returns the new size and resampling, or null.</summary>
    public static async Task<(int Width, int Height, Interpolation Mode)?> AskImageSizeAsync(this DialogHost host, int width, int height, bool pixelArt)
    {
        var w = Number(width);
        var h = Number(height);
        bool keep = true;
        bool updating = false;
        var percent = new TextBlock { Classes = { "dim", "num" }, VerticalAlignment = VerticalAlignment.Center };
        void UpdatePercent()
        {
            percent.Text = int.TryParse(w.Text, out int iw) ? $"{iw * 100.0 / width:0.#}%" : "";
        }
        w.TextChanged += (_, _) =>
        {
            if (updating || !keep || !int.TryParse(w.Text, out int iw)) { UpdatePercent(); return; }
            updating = true;
            h.Text = Math.Max(1, (int)Math.Round(iw * (double)height / width)).ToString();
            updating = false;
            UpdatePercent();
        };
        h.TextChanged += (_, _) =>
        {
            if (updating || !keep || !int.TryParse(h.Text, out int ih)) return;
            updating = true;
            w.Text = Math.Max(1, (int)Math.Round(ih * (double)width / height)).ToString();
            updating = false;
            UpdatePercent();
        };
        UpdatePercent();
        var (keepToggle, _) = UiKit.Toggle("lock", "Keep proportions", "Change width and height together", () => keep, v => keep = v);
        keepToggle.Click += (_, _) => keepToggle.Classes.Set("on", keep);

        var mode = pixelArt ? Interpolation.Nearest : Interpolation.Bicubic;
        Action refreshSeg = () => { };
        var (modeSeg, refreshModes) = UiKit.Segmented(
            [(Interpolation.Nearest, null, "Pixels", "Nearest neighbour: keeps hard pixels (pixel art)"),
             (Interpolation.Bilinear, null, "Smooth", "Bilinear"),
             (Interpolation.Bicubic, null, "Sharp", "Bicubic: smooth, keeps detail")],
            () => mode, v => { mode = v; refreshSeg(); });
        refreshSeg = refreshModes;

        var quick = new WrapPanel();
        foreach (var f in new[] { 0.25, 0.5, 2.0, 3.0, 4.0 })
        {
            var b = UiKit.Button(null, f < 1 ? $"{f * 100:0}%" : $"{f:0}×");
            b.Height = 28;
            b.Margin = new Thickness(0, 0, 4, 4);
            b.Background = new SolidColorBrush(DnColors.Ui3);
            b.Click += (_, _) =>
            {
                updating = true;
                w.Text = Math.Max(1, (int)Math.Round(width * f)).ToString();
                h.Text = Math.Max(1, (int)Math.Round(height * f)).ToString();
                updating = false;
                UpdatePercent();
            };
            quick.Children.Add(b);
        }
        var error = Error();
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(DialogHost.Text($"Now {width} × {height} px. Scales every layer."));
        body.Children.Add(UiKit.Row(8, Dim("Width"), w, Dim("Height"), h, Dim("px"), percent));
        body.Children.Add(quick);
        body.Children.Add(UiKit.Row(8, keepToggle));
        body.Children.Add(UiKit.Row(8, Dim("Resample", 70), modeSeg));
        body.Children.Add(error);
        keepToggle.Classes.Set("on", keep);
        while (true)
        {
            var r = await host.ShowAsync("Image size", body, [("cancel", "Cancel", false), ("ok", "Resize", true)], "ok", "cancel", 430);
            if (r != "ok") return null;
            if (int.TryParse(w.Text, out int iw) && int.TryParse(h.Text, out int ih) && RasterImage.IsSupportedSize(iw, ih)) return (iw, ih, mode);
            error.Text = SizeError;
            error.IsVisible = true;
        }
    }

    /// <summary>Canvas size: more or less room, without scaling. Returns size and anchor (0–1 each), or null.</summary>
    public static async Task<(int Width, int Height, double AnchorX, double AnchorY)?> AskCanvasSizeAsync(this DialogHost host, int width, int height)
    {
        var w = Number(width);
        var h = Number(height);
        double ax = 0.5, ay = 0.5;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("32,32,32"), RowDefinitions = new RowDefinitions("32,32,32"), HorizontalAlignment = HorizontalAlignment.Left };
        var cells = new List<(Button B, double X, double Y)>();
        void Mark()
        {
            foreach (var (b, x, y) in cells)
            {
                bool on = x == ax && y == ay;
                b.Classes.Set("solid", on);
                b.Classes.Set("on", on);
            }
        }
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                double x = col / 2.0, y = row / 2.0;
                var b = UiKit.Button(null, "·", "Keep the picture here");
                b.Width = 30;
                b.Height = 30;
                b.MinWidth = 30;
                b.Background = new SolidColorBrush(DnColors.Ui3);
                b.Click += (_, _) => { ax = x; ay = y; Mark(); };
                Grid.SetRow(b, row);
                Grid.SetColumn(b, col);
                grid.Children.Add(b);
                cells.Add((b, x, y));
            }
        Mark();
        var error = Error();
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(DialogHost.Text($"Now {width} × {height} px. Adds transparent room (or trims the edges) without scaling."));
        body.Children.Add(UiKit.Row(8, Dim("Width"), w, Dim("Height"), h, Dim("px")));
        body.Children.Add(UiKit.Row(12, Dim("Anchor", 70), grid));
        body.Children.Add(error);
        while (true)
        {
            var r = await host.ShowAsync("Canvas size", body, [("cancel", "Cancel", false), ("ok", "Apply", true)], "ok", "cancel", 400);
            if (r != "ok") return null;
            if (int.TryParse(w.Text, out int iw) && int.TryParse(h.Text, out int ih) && RasterImage.IsSupportedSize(iw, ih)) return (iw, ih, ax, ay);
            error.Text = SizeError;
            error.IsVisible = true;
        }
    }

    /// <summary>Which dialog edits an adjustment.</summary>
    public static AdjustKind KindOf(Adjustment a) => a switch
    {
        HueSaturation => AdjustKind.HueSaturation,
        Levels => AdjustKind.Levels,
        ColourBalance => AdjustKind.ColourBalance,
        Blur => AdjustKind.Blur,
        Sharpen => AdjustKind.Sharpen,
        _ => AdjustKind.BrightnessContrast,
    };

    /// <summary>A neutral adjustment of a kind (changes nothing until its sliders move).</summary>
    public static Adjustment Neutral(AdjustKind kind) => kind switch
    {
        AdjustKind.HueSaturation => new HueSaturation(0, 0, 0),
        AdjustKind.Levels => new Levels(),
        AdjustKind.ColourBalance => new ColourBalance(),
        AdjustKind.Blur => new Blur(3),
        AdjustKind.Sharpen => new Sharpen(0.8, 1.5),
        _ => new BrightnessContrast(0, 0),
    };

    /// <summary>An adjustment with live preview. <paramref name="preview"/> is called with the current settings
    /// (null = show the original); returns the chosen adjustment or null when cancelled. <paramref name="initial"/> starts
    /// from existing settings (an adjustment layer being edited).</summary>
    public static async Task<Adjustment?> AskAdjustmentAsync(this DialogHost host, AdjustKind kind, Action<Adjustment?> preview,
        Adjustment? initial = null, bool layer = false)
    {
        // The engine's units: −100…100 for brightness, contrast, saturation, lightness and colour balance; degrees for hue.
        var values = (initial ?? Neutral(kind)) switch
        {
            BrightnessContrast b => new[] { b.Brightness, b.Contrast },
            HueSaturation h => new[] { h.Hue, h.Saturation, h.Lightness },
            Levels l => new[] { l.InBlack, l.InWhite, l.Gamma, l.OutBlack, l.OutWhite },
            ColourBalance c => new[] { c.CyanRed, c.MagentaGreen, c.YellowBlue },
            Blur bl => new[] { bl.Radius },
            Sharpen sh => new[] { sh.Amount, sh.Radius },
            _ => new double[] { 0, 0 },
        };
        static string Signed(double v) => $"{v:+0;-0;0}";
        (string Label, double Min, double Max, Func<double, string> Fmt)[] specs = kind switch
        {
            AdjustKind.BrightnessContrast => [("Brightness", -100, 100, Signed), ("Contrast", -100, 100, Signed)],
            AdjustKind.HueSaturation => [("Hue", -180, 180, v => $"{v:+0;-0;0}°"), ("Saturation", -100, 100, Signed), ("Lightness", -100, 100, Signed)],
            AdjustKind.Levels => [("Input black", 0, 254, v => $"{v:0}"), ("Input white", 1, 255, v => $"{v:0}"), ("Midtones", 0.1, 10, v => $"{v:0.00}"),
                                  ("Output black", 0, 255, v => $"{v:0}"), ("Output white", 0, 255, v => $"{v:0}")],
            AdjustKind.ColourBalance => [("Cyan ↔ Red", -100, 100, Signed), ("Magenta ↔ Green", -100, 100, Signed), ("Yellow ↔ Blue", -100, 100, Signed)],
            AdjustKind.Blur => [("Radius", 0.5, 40, v => $"{v:0.#} px")],
            _ => [("Amount", 0.1, 3, v => $"{v * 100:0}%"), ("Radius", 0.5, 6, v => $"{v:0.#} px")],
        };
        bool show = true;
        Adjustment Current() => kind switch
        {
            AdjustKind.BrightnessContrast => new BrightnessContrast(Math.Round(values[0]), Math.Round(values[1])),
            AdjustKind.HueSaturation => new HueSaturation(Math.Round(values[0]), Math.Round(values[1]), Math.Round(values[2])),
            AdjustKind.Levels => new Levels(Math.Round(values[0]), Math.Max(Math.Round(values[1]), Math.Round(values[0]) + 1), Math.Round(values[2], 2),
                Math.Round(values[3]), Math.Round(values[4])),
            AdjustKind.ColourBalance => new ColourBalance(Math.Round(values[0]), Math.Round(values[1]), Math.Round(values[2])),
            AdjustKind.Blur => new Blur(values[0]),
            _ => new Sharpen(values[0], values[1]),
        };
        void Update() => preview(show ? Current() : null);

        var body = new StackPanel { Spacing = 8 };
        var refreshers = new List<Action>();
        for (int i = 0; i < specs.Length; i++)
        {
            int index = i;
            var (label, min, max, fmt) = specs[i];
            var (root, refresh) = UiKit.Slider(null, min, max, () => values[index], v => { values[index] = v; Update(); }, fmt, trackWidth: 220,
                log: kind == AdjustKind.Blur || (kind == AdjustKind.Levels && index == 2));
            refreshers.Add(refresh);
            body.Children.Add(UiKit.Row(8, Dim(label, 110), root));
        }
        var (previewToggle, refreshToggle) = UiKit.Toggle("eye", "Preview", "Compare with the original", () => show, v => { show = v; Update(); });
        previewToggle.Click += (_, _) => refreshToggle();
        var reset = UiKit.Button(null, "Reset", "Back to no change", () =>
        {
            var n = Neutral(kind);
            var nv = n switch
            {
                BrightnessContrast b => new[] { b.Brightness, b.Contrast },
                HueSaturation h => new[] { h.Hue, h.Saturation, h.Lightness },
                Levels l => new[] { l.InBlack, l.InWhite, l.Gamma, l.OutBlack, l.OutWhite },
                ColourBalance c => new[] { c.CyanRed, c.MagentaGreen, c.YellowBlue },
                Blur bl => new[] { bl.Radius },
                Sharpen sh => new[] { sh.Amount, sh.Radius },
                _ => values,
            };
            Array.Copy(nv, values, Math.Min(nv.Length, values.Length));
            foreach (var r in refreshers) r();
            Update();
        });
        reset.Height = 30;
        body.Children.Add(UiKit.Row(8, previewToggle, reset));
        body.Children.Add(DialogHost.Text(layer
            ? "An adjustment layer changes everything below it (in its group) without changing any pixels. Edit it again any time: double-click it in the layers panel."
            : "Applies to the selection, or the whole layer when nothing is selected."));
        Update();
        var title = kind switch
        {
            AdjustKind.BrightnessContrast => "Brightness / Contrast",
            AdjustKind.HueSaturation => "Hue / Saturation",
            AdjustKind.Levels => "Levels",
            AdjustKind.ColourBalance => "Colour balance",
            AdjustKind.Blur => "Blur",
            _ => "Sharpen",
        };
        var r = await host.ShowAsync(layer ? $"{title} (adjustment layer)" : title, body, [("cancel", "Cancel", false), ("ok", layer ? "OK" : "Apply", true)], "ok", "cancel", 460);
        return r == "ok" ? Current() : null;
    }

    /// <summary>Export options: quality for JPEG/WebP and what flattening means. Returns the quality, or null.</summary>
    public static async Task<int?> AskExportAsync(this DialogHost host, ExportFormat format, int layers, bool hasTransparency)
    {
        int quality = format == ExportFormat.Jpeg ? 92 : 90;
        var body = new StackPanel { Spacing = 10 };
        var lines = new List<string>();
        if (layers > 1) lines.Add($"Your {layers} layers are merged into one flat picture for this file. The layered picture stays open (save it as .dnk to keep the layers).");
        else lines.Add("Saves a flat copy of the picture.");
        if (format == ExportFormat.Jpeg && hasTransparency) lines.Add("JPEG has no transparency: transparent areas become white.");
        if (format == ExportFormat.Jpeg) lines.Add("JPEG is lossy: good for photos and paintings, not for pixel art.");
        if (format == ExportFormat.WebP) lines.Add("WebP keeps transparency and makes small files.");
        if (format == ExportFormat.Png) lines.Add("PNG is lossless and keeps transparency (best for pixel art and line art).");
        body.Children.Add(DialogHost.Text(string.Join("\n\n", lines)));
        if (format != ExportFormat.Png)
        {
            var (slider, _) = UiKit.Slider(null, 40, 100, () => quality, v => quality = (int)Math.Round(v), v => $"{v:0}", trackWidth: 200);
            body.Children.Add(UiKit.Row(8, Dim("Quality", 70), slider));
        }
        var r = await host.ShowAsync($"Export as {ImageFiles.DisplayName(format)}", body, [("cancel", "Cancel", false), ("ok", "Choose file…", true)], "ok", "cancel", 420);
        return r == "ok" ? quality : null;
    }
}
