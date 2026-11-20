using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DoodleNook.App.Controls;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Dialogs;

/// <summary>
/// The Brush Laboratory: edit a brush in five plain sections (Tip, Stroke, Pressure, Texture, Scatter), see every
/// change at once in the preview stroke, and try it on a test canvas that never touches the artwork. Built-in brushes
/// are never changed: saving one makes a copy in My brushes.
/// </summary>
public sealed class BrushLab : Grid
{
    public enum Section
    {
        Tip,
        Stroke,
        Pressure,
        Texture,
        Scatter,
    }

    private readonly MainWindow _app;
    private readonly StackPanel _settings = new() { Spacing = 8 };
    private readonly Image _preview = new() { Width = 300, Height = 56, Stretch = Stretch.Fill };
    private readonly TextBox _name = new() { Width = 200, Name = "BrushLabName" };
    private readonly TextBlock _status = new() { Classes = { "dim" }, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 330 };
    private readonly Button _save, _delete;
    private readonly Action _refreshSection;
    private Section _section = Section.Tip;
    private bool _confirmDelete;
    private bool _previewQueued;
    private PressureCurveView? _curve;

    public BrushLab(MainWindow app, string? presetId)
    {
        _app = app;
        Name = "BrushLab";
        var preset = Lib.FindBrush(presetId ?? app.Session.BrushPresetId) ?? Lib.Brushes[0];
        PresetId = preset.Id;
        Settings = presetId is null && app.Session.BrushPresetId == preset.Id ? app.Session.Brush : preset.Settings;
        _name.Text = preset.BuiltIn ? preset.Name + " (my version)" : preset.Name;

        ColumnDefinitions = new ColumnDefinitions("340,16,*");
        Playground = new BrushPlayground(330, 190, () => new StrokeStyle(Settings, app.Session.Curve, app.Session.Foreground, PaintMode.Paint, Symmetry.None, Lib.Resolve(Settings)));

        // ---- left: which brush, the test canvas, saving
        var brushChip = Menus.MenuButton(UiKit.Row(6, new DnIcon { Kind = "brush" }, new TextBlock { Text = "Brushes", VerticalAlignment = VerticalAlignment.Center }, new DnIcon { Kind = "chevd" }),
            BrushListMenu, "Choose another brush to edit", 260);
        brushChip.Name = "BrushLabBrushes";
        var clear = UiKit.Button("trash", "Clear", "Clear the test area", Playground.Clear);
        var example = UiKit.Button("brush", "Example", "Draw a sample stroke (light → firm → light pressure)", Playground.ExampleStroke);
        var (backdrop, refreshBackdrop) = UiKit.Segmented(
            [(BrushPlayground.Backdrop.Paper, null, "Paper", "Light paper"), (BrushPlayground.Backdrop.Dark, null, "Dark", "Dark background"),
             (BrushPlayground.Backdrop.Checker, null, "▦", "Checkerboard: shows transparency")],
            () => Playground.Background, v => { Playground.Background = v; Playground.InvalidateVisual(); }, compact: true);
        Action rb = () => { };
        rb = refreshBackdrop;
        backdrop.PointerReleased += (_, _) => rb();
        foreach (var b in new[] { clear, example }) b.Height = 28;
        var canvasBox = new Border { Child = Playground, CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderBrush = new SolidColorBrush(DnColors.Line), BorderThickness = new Thickness(1) };
        ToolTip.SetTip(canvasBox, "Try the brush here (mouse or pen). Nothing here touches your picture.");

        _save = UiKit.Button("save", "Save", "Save these settings", () => Save(asNew: false));
        _save.Classes.Add("solid");
        _save.Classes.Add("on");
        _save.Name = "BrushLabSave";
        var saveNew = UiKit.Button("plus", "Save as new", "Keep these settings as another brush in My brushes", () => Save(asNew: true));
        saveNew.Name = "BrushLabSaveNew";
        var revert = UiKit.Button("undo", "Revert", "Back to the saved settings", Revert);
        _delete = UiKit.Button("trash", "Delete", "Delete this brush (only your own brushes)", Delete);
        _delete.Name = "BrushLabDelete";
        var more = Menus.MenuButton(new DnIcon { Kind = "dots" }, MoreMenu, "Favourite, import and export", 240);
        foreach (var b in new[] { _save, saveNew, revert, _delete }) b.Height = 30;

        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(UiKit.Row(6, brushChip, _name));
        left.Children.Add(canvasBox);
        left.Children.Add(UiKit.Row(4, clear, example, backdrop));
        left.Children.Add(UiKit.Row(4, _save, saveNew, revert));
        left.Children.Add(UiKit.Row(4, _delete, more));
        left.Children.Add(_status);
        Children.Add(left);

        // ---- right: preview stroke and the sections
        var (sections, refreshSections) = UiKit.Segmented(
            [(Section.Tip, null, "Tip", "Shape of the brush"), (Section.Stroke, null, "Stroke", "Size, opacity, spacing, flow"),
             (Section.Pressure, null, "Pressure", "What pen pressure does"), (Section.Texture, null, "Texture", "Grain and texture"),
             (Section.Scatter, null, "Scatter", "Random placement and variation")],
            () => _section, v => { _section = v; BuildSection(); }, compact: true);
        _refreshSection = refreshSections;
        var right = new StackPanel { Spacing = 8 };
        right.Children.Add(new Border { Child = _preview, Background = new SolidColorBrush(Color.Parse("#F8F4EC")), CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left });
        right.Children.Add(sections);
        right.Children.Add(new ScrollViewer { Content = _settings, MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        SetColumn(right, 2);
        Children.Add(right);
        BuildSection();
        UpdateState();
    }

    private CreativeLibrary Lib => _app.Session.Library;

    /// <summary>The brush being edited (unsaved changes included).</summary>
    public BrushSettings Settings { get; private set; }

    /// <summary>The preset the settings belong to.</summary>
    public string PresetId { get; private set; }

    public BrushPlayground Playground { get; }

    private BrushPreset Preset => Lib.FindBrush(PresetId) ?? Lib.Brushes[0];

    private bool Modified => Settings != Preset.Settings.Normalized() && Settings != Preset.Settings;

    /// <summary>Shows the Laboratory. Returns true when the brush was put to use.</summary>
    public static async Task<bool> ShowAsync(MainWindow app, string? presetId = null, Section section = Section.Tip)
    {
        var lab = new BrushLab(app, presetId) { _section = section };
        lab.BuildSection();
        var body = new ScrollViewer { Content = lab, MaxHeight = Math.Max(300, app.Bounds.Height - 150), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        app.LastBrushLab = lab;
        var r = await app.Dialogs.ShowAsync("Brush Laboratory", body, [("close", "Close", false), ("use", "Use this brush", true)], "use", "close", 700);
        if (r != "use") return false;
        app.Session.UseBrush(lab.Settings, lab.PresetId);
        app.ShowToast($"Brush: {lab.Preset.Name}{(lab.Modified ? " (changed, not saved)" : "")}");
        return true;
    }

    /// <summary>Changes the brush being edited (what the section controls do; tests use it directly).</summary>
    internal void Edit(Func<BrushSettings, BrushSettings> change) => Change(change, rebuild: true);

    internal void SetName(string name) => _name.Text = name;

    // ================================================================== changes

    private void Change(Func<BrushSettings, BrushSettings> f, bool rebuild = false)
    {
        Settings = f(Settings).Normalized();
        _curve?.InvalidateVisual();
        UpdateState();
        if (rebuild) BuildSection();
    }

    private void UpdateState()
    {
        if (!_previewQueued)
        {
            _previewQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _previewQueued = false;
                _preview.Source = BrushPreview.Render(Settings, _app.Session.Foreground, 150, 28, 2, Lib.Resolve(Settings));
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
        var p = Preset;
        _delete.IsEnabled = !p.BuiltIn;
        _save.IsEnabled = !Lib.ReadOnly;
        string missing = Lib.HasMissingParts(Settings) ? " Its tip or texture picture is missing, so it paints with a round tip or the paper grain." : "";
        _status.Text = (p.BuiltIn
            ? $"“{p.Name}” is built in: Save keeps your changes as a new brush in My brushes, the original stays as it is."
            : Modified ? "Unsaved changes." : "Saved.") + missing;
        if (!_confirmDelete) _delete.Content = UiKit.Content("trash", "Delete");
    }

    private void Load(string id)
    {
        PresetId = id;
        var p = Preset;
        Settings = p.Settings;
        _name.Text = p.BuiltIn ? p.Name + " (my version)" : p.Name;
        _confirmDelete = false;
        BuildSection();
        UpdateState();
    }

    /// <summary>Saves into the user's brush (or, for a built-in brush or "as new", into a new one).</summary>
    public BrushPreset Save(bool asNew)
    {
        var p = Preset;
        string name = string.IsNullOrWhiteSpace(_name.Text) ? p.Name : _name.Text.Trim();
        BrushPreset saved;
        if (asNew || p.BuiltIn)
        {
            if (asNew && name == p.Name) name += " copy";
            saved = Lib.AddBrush(name, Settings, p.Description);
        }
        else
        {
            Lib.UpdateBrush(p.Id, Settings);
            if (name != p.Name) Lib.RenameBrush(p.Id, name);
            saved = Lib.FindBrush(p.Id)!;
        }
        _app.ShowToast($"Saved “{saved.Name}” in My brushes");
        Load(saved.Id);
        return saved;
    }

    private void Revert()
    {
        Settings = Preset.Settings;
        BuildSection();
        UpdateState();
    }

    private void Delete()
    {
        if (Preset.BuiltIn) return;
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            _delete.Content = UiKit.Content("trash", "Click again to delete");
            return;
        }
        var name = Preset.Name;
        Lib.DeleteBrush(PresetId);
        _app.ShowToast($"Deleted “{name}”");
        Load(Lib.Brushes[0].Id);
    }

    private IEnumerable<MenuEntry> BrushListMenu()
    {
        foreach (var cat in BrushCategories.Order)
        {
            var list = Lib.Brushes.Where(b => b.Category == cat).ToList();
            if (list.Count == 0) continue;
            yield return MenuEntry.Heading(cat);
            foreach (var b in list)
            {
                var id = b.Id;
                yield return new(Lib.IsFavoriteBrush(id) ? "star" : null, b.Name, null, () => Load(id), Checked: id == PresetId);
            }
        }
    }

    private IEnumerable<MenuEntry> MoreMenu()
    {
        var id = PresetId;
        bool fav = Lib.IsFavoriteBrush(id);
        yield return new("star", fav ? "Remove from favourites" : "Add to favourites", null, () => Lib.SetFavoriteBrush(id, !fav));
        yield return new("export", "Export this brush…", null, () => _ = _app.ExportPackAsync(Preset.Name, brushIds: [id]), !Preset.BuiltIn);
        yield return new("open", "Import brushes from a pack…", null, () => _ = _app.ImportPackAsync());
        if (Preset.BuiltIn) yield return new("undo", "Restore the built-in settings", null, Revert);
    }

    // ================================================================== sections

    private Control SliderRow(string label, double min, double max, Func<double> get, Action<double> set, Func<double, string> fmt, string? tip = null, bool log = false)
    {
        var (row, _) = UiKit.Slider(null, min, max, get, set, fmt, trackWidth: 150, log: log, tip: tip);
        var l = new TextBlock { Text = label, Classes = { "dim" }, Width = 100, VerticalAlignment = VerticalAlignment.Center };
        return UiKit.Row(4, l, row);
    }

    private Control CheckRow(string label, string? tip, Func<bool> get, Action<bool> set)
    {
        var (b, _) = UiKit.Check(label, tip, get, set);
        return b;
    }

    private static TextBlock Note(string text) => new() { Text = text, Classes = { "dim" }, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };

    internal void BuildSection()
    {
        _refreshSection();
        _settings.Children.Clear();
        var s = Settings;
        switch (_section)
        {
            case Section.Tip:
            {
                var (shape, _) = UiKit.Segmented([(false, null, "Round", "A round or flattened tip"), (true, null, "Picture", "A tip made from a picture")],
                    () => Settings.TipId is not null, v => Change(b => b with { TipId = v ? b.TipId ?? BuiltInAssets.Tips[0].Id : null }, rebuild: true), compact: true);
                _settings.Children.Add(UiKit.Row(4, new TextBlock { Text = "Tip", Classes = { "dim" }, Width = 100, VerticalAlignment = VerticalAlignment.Center }, shape));
                if (s.TipId is null)
                    _settings.Children.Add(SliderRow("Hardness", 0, 1, () => Settings.Hardness, v => Change(b => b with { Hardness = v }), v => $"{v * 100:0}%", "Soft, feathered edge ↔ crisp edge"));
                else
                {
                    _settings.Children.Add(TipChooser());
                    if (Lib.Tip(s.TipId) is { HasColor: true })
                        _settings.Children.Add(CheckRow("Paint in the picture's own colours", "On: a stamp brush. Off: the picture's shape in the current colour",
                            () => Settings.TipColors, v => Change(b => b with { TipColors = v })));
                }
                _settings.Children.Add(SliderRow(s.TipId is null ? "Roundness" : "Squash", 0.1, 1, () => Settings.Roundness, v => Change(b => b with { Roundness = v }), v => $"{v * 100:0}%",
                    "100 % = as is; less = flatter (a chisel or calligraphy tip)"));
                _settings.Children.Add(SliderRow("Angle", -180, 180, () => Settings.Angle, v => Change(b => b with { Angle = Math.Round(v) }), v => $"{v:0}°", "Turns the tip"));
                _settings.Children.Add(CheckRow("Turn with the stroke", "The tip follows the direction you draw in (dry brushes, ribbons)",
                    () => Settings.FollowDirection, v => Change(b => b with { FollowDirection = v })));
                break;
            }
            case Section.Stroke:
                _settings.Children.Add(SliderRow("Size", 1, 500, () => Settings.Size, v => Change(b => b with { Size = Math.Round(v, v < 10 ? 1 : 0) }), v => $"{v:0.#} px", log: true));
                _settings.Children.Add(SliderRow("Opacity", 0.05, 1, () => Settings.Opacity, v => Change(b => b with { Opacity = v }), v => $"{v * 100:0}%",
                    "A stroke never gets darker than this, even where it crosses itself"));
                _settings.Children.Add(CheckRow("Automatic spacing", "Dabs close enough for a smooth edge", () => Settings.Spacing == 0,
                    v => Change(b => b with { Spacing = v ? 0 : 0.25 }, rebuild: true)));
                if (s.Spacing > 0)
                    _settings.Children.Add(SliderRow("Spacing", 0.02, 3, () => Settings.Spacing, v => Change(b => b with { Spacing = v }), v => $"{v * 100:0}%",
                        "Distance between dabs, as a share of the brush size", log: true));
                _settings.Children.Add(SliderRow("Smoothing", 0, 1, () => Settings.Smoothing, v => Change(b => b with { Smoothing = v }), v => $"{v * 100:0}%", "Steadies wobbly lines"));
                _settings.Children.Add(CheckRow("Build up", "Paint keeps adding up within a stroke (airbrush, glazes)", () => Settings.BuildUp, v => Change(b => b with { BuildUp = v }, rebuild: true)));
                if (s.BuildUp)
                {
                    _settings.Children.Add(SliderRow("Flow", 0.005, 1, () => Settings.Flow, v => Change(b => b with { Flow = v }), v => $"{v * 100:0.#}%", "Paint added by each dab", log: true));
                    _settings.Children.Add(CheckRow("Keep spraying while the pen rests", null, () => Settings.Spray, v => Change(b => b with { Spray = v })));
                }
                _settings.Children.Add(SliderRow("Wet edges", 0, 1, () => Settings.WetEdges, v => Change(b => b with { WetEdges = v }), v => $"{v * 100:0}%",
                    "Watercolour look: paint collects at the edge of the stroke"));
                break;
            case Section.Pressure:
                _settings.Children.Add(Note("What the pen's pressure changes. (A mouse always draws at full pressure.)"));
                _settings.Children.Add(CheckRow("Pressure → size", null, () => Settings.PressureSize, v => Change(b => b with { PressureSize = v }, rebuild: true)));
                if (s.PressureSize)
                    _settings.Children.Add(SliderRow("Smallest size", 0, 1, () => Settings.MinimumSizeFraction, v => Change(b => b with { MinimumSizeFraction = v }), v => $"{v * 100:0}%",
                        "The size at the lightest touch"));
                _settings.Children.Add(CheckRow("Pressure → opacity", null, () => Settings.PressureOpacity, v => Change(b => b with { PressureOpacity = v })));
                if (s.BuildUp) _settings.Children.Add(CheckRow("Pressure → flow", "Press harder to build up faster", () => Settings.PressureFlow, v => Change(b => b with { PressureFlow = v })));
                _settings.Children.Add(SliderRow("Response", 0.3, 3, () => Settings.PressureGamma, v => Change(b => b with { PressureGamma = Math.Round(v, 2) }, rebuild: false),
                    v => v < 0.95 ? "light touch" : v > 1.05 ? "firm hand" : "even", "Left: a light touch already gives a lot. Right: you need to press firmly.", log: true));
                _settings.Children.Add(_curve = new PressureCurveView(() => Settings.PressureGamma, () => _app.Session.Curve));
                _settings.Children.Add(Note("Works on top of the pressure feel (Soft / Normal / Firm) in the brush settings."));
                break;
            case Section.Texture:
                _settings.Children.Add(SliderRow("Strength", 0, 1, () => Settings.Grain, v => Change(b => b with { Grain = v }), v => $"{v * 100:0}%",
                    "How much the texture shows (more on light strokes)"));
                _settings.Children.Add(TextureChooser());
                _settings.Children.Add(SliderRow("Scale", 0.25, 8, () => Settings.GrainScale, v => Change(b => b with { GrainScale = v }), v => $"{v * 100:0}%", log: true));
                _settings.Children.Add(SliderRow("Rotation", -180, 180, () => Settings.TextureRotation, v => Change(b => b with { TextureRotation = Math.Round(v) }), v => $"{v:0}°"));
                _settings.Children.Add(CheckRow("Invert (paint into the dark parts)", null, () => Settings.InvertTexture, v => Change(b => b with { InvertTexture = v })));
                break;
            case Section.Scatter:
                _settings.Children.Add(SliderRow("Scatter", 0, 3, () => Settings.Scatter, v => Change(b => b with { Scatter = v }), v => $"{v * 100:0}%",
                    "How far dabs land from the line, as a share of the size"));
                _settings.Children.Add(SliderRow("Count", 1, 8, () => Settings.ScatterCount, v => Change(b => b with { ScatterCount = (int)Math.Round(v) }), v => $"{Math.Round(v):0}",
                    "Dabs at each step"));
                _settings.Children.Add(SliderRow("Size jitter", 0, 1, () => Settings.SizeJitter, v => Change(b => b with { SizeJitter = v }), v => $"{v * 100:0}%"));
                _settings.Children.Add(SliderRow("Angle jitter", 0, 1, () => Settings.RotationJitter, v => Change(b => b with { RotationJitter = v }), v => $"{v * 180:0}°"));
                _settings.Children.Add(SliderRow("Opacity jitter", 0, 1, () => Settings.OpacityJitter, v => Change(b => b with { OpacityJitter = v }), v => $"{v * 100:0}%"));
                _settings.Children.Add(Note("Random, but the same stroke always gives the same marks."));
                break;
        }
    }

    private Control TipChooser()
    {
        var wrap = new WrapPanel { MaxWidth = 300 };
        var lib = Lib;
        var tips = BuiltInAssets.Tips.Select(t => (t.Id, t.Name)).Concat(lib.Stamps.Select(st => ("stamp:" + st.Id, st.Name + " (stamp)")));
        if (Settings.TipId is { } cur && !tips.Any(t => t.Item1 == cur)) tips = tips.Append((cur, "Own tip"));
        foreach (var (id, name) in tips)
        {
            var tip = lib.Tip(id);
            var img = tip?.ToImage(_app.Session.Foreground);
            var b = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                Width = 46, Height = 46, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = UiKit.Chosen(new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#F8F4EC")), CornerRadius = new CornerRadius(6), Padding = new Thickness(3),
                    Child = img is null ? new TextBlock { Text = "?" } : new Image { Source = BrushPreview.ToBitmap(AssetPicker.Fit(img, 76)), Stretch = Stretch.Uniform },
                }, Settings.TipId == id),
            };
            ToolTip.SetTip(b, name);
            var value = id;
            b.Click += (_, _) => Change(x => x with { TipId = value, TipColors = value.StartsWith("stamp:", StringComparison.Ordinal) && x.TipColors }, rebuild: true);
            wrap.Children.Add(b);
        }
        var fromSel = UiKit.Button("plus", "From selection", "Use the selected artwork as the tip (the artwork itself is not changed)", () =>
        {
            if (_app.Session.SelectionPicture() is not { } pic)
            {
                _app.ShowToast("Select some artwork first (or draw on the layer)");
                return;
            }
            var id = Lib.AddTip(pic);
            Change(x => x with { TipId = id }, rebuild: true);
        });
        var import = UiKit.Button("open", "PNG…", "Use a picture file as the tip (DoodleNook keeps its own copy)", () => _ = ImportTipAsync());
        fromSel.Height = import.Height = 28;
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(wrap);
        panel.Children.Add(UiKit.Row(4, fromSel, import));
        panel.Children.Add(Note("Transparent pictures paint their shape; opaque ones paint where they are dark."));
        return panel;
    }

    private async Task ImportTipAsync()
    {
        var files = await _app.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "A picture for the brush tip",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var id = Lib.AddTip(ImageFiles.Decode(stream));
            Change(x => x with { TipId = id }, rebuild: true);
        }
        catch (Exception ex)
        {
            _status.Text = $"Couldn't use {files[0].Name}: {ex.Message}";
        }
    }

    private Control TextureChooser()
    {
        var wrap = new WrapPanel { MaxWidth = 300 };
        var items = new List<(string? Id, string Name)> { (null, "Paper grain") };
        items.AddRange(Lib.Patterns.Select(p => ((string?)("pattern:" + p.Id), p.Name)));
        foreach (var (id, name) in items)
        {
            Control content;
            if (id is null) content = new TextBlock { Text = "Paper", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else
            {
                var img = Lib.PatternImage(id[8..]);
                content = new Image { Source = img is null ? null : AssetPicker.TiledPreview(img, 36, 36), Stretch = Stretch.Fill };
            }
            var b = new Button
            {
                Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource("DnButton")!,
                Width = 46, Height = 46, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = UiKit.Chosen(content, Settings.TextureId == id),
            };
            ToolTip.SetTip(b, name);
            var value = id;
            b.Click += (_, _) => Change(x => x with { TextureId = value, Grain = x.Grain == 0 ? 0.5 : x.Grain }, rebuild: true);
            wrap.Children.Add(b);
        }
        return wrap;
    }
}

/// <summary>A small graph of the pressure response (pressure → amount), with the pressure feel applied.</summary>
public sealed class PressureCurveView : Control
{
    private readonly Func<double> _gamma;
    private readonly Func<PressureCurve> _feel;

    public PressureCurveView(Func<double> gamma, Func<PressureCurve> feel)
    {
        _gamma = gamma;
        _feel = feel;
        Width = 150;
        Height = 70;
        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(104, 0, 0, 0);
    }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(DnColors.Ui2), new Pen(new SolidColorBrush(DnColors.Line), 1), new RoundedRect(r, 6));
        context.DrawLine(new Pen(new SolidColorBrush(DnColors.Line), 1, new DashStyle([3, 3], 0)), new Point(4, r.Height - 4), new Point(r.Width - 4, 4));
        var b = new BrushSettings { PressureGamma = _gamma() };
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i <= 40; i++)
            {
                float p = i / 40f;
                float m = b.MapPressure(p, _feel());
                var pt = new Point(4 + p * (r.Width - 8), r.Height - 4 - m * (r.Height - 8));
                if (i == 0) g.BeginFigure(pt, false);
                else g.LineTo(pt);
            }
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(DnColors.Accent), 2), geo);
    }
}
