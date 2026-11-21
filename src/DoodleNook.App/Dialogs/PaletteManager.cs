using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DoodleNook.App.Controls;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;

namespace DoodleNook.App.Dialogs;

/// <summary>
/// Palettes: pick one, change its colours (add the current colour, remove, move), name and collection, make one from a
/// picture (choose how many colours, see them before saving), add colour harmonies, import and export GIMP .gpl and
/// .hex files. Built-in palettes are read-only; duplicate one to change it. Colours are kept exactly.
/// </summary>
public sealed class PaletteManager : Grid
{
    private readonly MainWindow _app;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly WrapPanel _swatches = new() { ItemWidth = 32, ItemHeight = 32, MaxWidth = 330 };
    private readonly TextBox _name = new() { Width = 180, Name = "PaletteName" };
    private readonly TextBox _collection = new() { Width = 130 };
    private readonly TextBlock _info = new() { Classes = { "dim" }, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 330 };
    private readonly TextBlock _creditText = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 318, Name = "PaletteCredit" };
    private readonly Border _credit;
    private readonly StackPanel _extract = new() { Spacing = 6, IsVisible = false };
    private readonly WrapPanel _extracted = new() { ItemWidth = 26, ItemHeight = 26, MaxWidth = 330 };
    private readonly StackPanel _editButtons;
    private readonly Action _refreshCount;
    private RasterImage? _extractSource;
    private int _extractCount = 8;
    private List<ColorRgb> _proposal = new();
    private int _selected = -1;
    private bool _confirmDelete;

    public PaletteManager(MainWindow app, string? paletteId)
    {
        _app = app;
        Name = "PaletteManager";
        ColumnDefinitions = new ColumnDefinitions("180,16,*");
        PaletteId = Lib.FindPalette(paletteId)?.Id ?? Lib.Palettes[0].Id;

        var newButton = Small("plus", "New", "A new empty palette in My palettes", () =>
        {
            var p = Lib.AddPalette("New palette", "My palettes", [_app.Session.Foreground]);
            Select(p.Id);
        });
        var import = Small("open", "Import…", "GIMP .gpl, .hex or a DoodleNook asset pack", () => _ = ImportAsync());
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 280, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        left.Children.Add(UiKit.Row(4, newButton, import));
        Children.Add(left);

        var right = new StackPanel { Spacing = 8 };
        right.Children.Add(UiKit.Row(6, _name, _collection));
        right.Children.Add(_swatches);
        // Palettes by other artists: who made it, the terms it is shared under and where it is published.
        _credit = new Border
        {
            Child = _creditText,
            Background = new SolidColorBrush(DnColors.Ui2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6),
            MaxWidth = 330,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        right.Children.Add(_credit);
        _editButtons = UiKit.Row(4,
            Small("plus", "Colour", "Add the current foreground colour", () => EditColors(c => { if (!c.Contains(_app.Session.Foreground)) c.Add(_app.Session.Foreground); })),
            Small("trash", "Remove", "Remove the selected colour", () => EditColors(c => { if (_selected >= 0 && _selected < c.Count) c.RemoveAt(_selected); _selected = -1; })),
            Small("chevl", null, "Move the selected colour earlier", () => Move(-1)),
            Small("chevr", null, "Move the selected colour later", () => Move(+1)),
            Menus.MenuButton(UiKit.Content(null, "Harmony"), HarmonyMenu, "Add colours that go with the current one", 240));
        right.Children.Add(_editButtons);
        right.Children.Add(UiKit.Row(4,
            Small("image", "From picture…", "Make a palette from the open picture or an image file", StartExtract),
            Small("copy", "Duplicate", "A copy you can change", () => Select(Lib.DuplicatePalette(PaletteId).Id)),
            Small("export", "Export…", "Save as GIMP .gpl or .hex", () => _ = ExportAsync()),
            Small("trash", null, "Delete this palette (click twice)", Delete)));
        _extract.Children.Add(new TextBlock { Text = "Colours from the picture", FontWeight = FontWeight.SemiBold });
        var (count, refreshCount) = UiKit.Slider("How many", 2, 32, () => _extractCount, v => { _extractCount = (int)Math.Round(v); Propose(); }, v => $"{Math.Round(v):0}", trackWidth: 140);
        _refreshCount = refreshCount;
        _extract.Children.Add(count);
        _extract.Children.Add(_extracted);
        _extract.Children.Add(UiKit.Row(4,
            Small("image", "Image file…", "Use another picture", () => _ = ExtractFromFileAsync()),
            Small("check", "Save as palette", "Keep these colours as a new palette (nothing else changes)", () => SaveProposal()),
            Small("close", "Cancel", null, () => _extract.IsVisible = false)));
        right.Children.Add(_extract);
        right.Children.Add(_info);
        SetColumn(right, 2);
        Children.Add(right);

        _name.LostFocus += (_, _) => CommitName();
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitName(); };
        _collection.LostFocus += (_, _) => CommitName();
        app.Session.LibraryChanged += Refresh;
        Refresh();
    }

    private CreativeLibrary Lib => _app.Session.Library;

    public string PaletteId { get; private set; }

    private PaletteAsset Current => Lib.FindPalette(PaletteId) ?? Lib.Palettes[0];

    public static async Task<string?> ShowAsync(MainWindow app, string? paletteId = null)
    {
        var m = new PaletteManager(app, paletteId ?? app.Session.ActivePaletteId);
        app.LastPaletteManager = m;
        var body = new ScrollViewer { Content = m, MaxHeight = Math.Max(300, app.Bounds.Height - 150), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var r = await app.Dialogs.ShowAsync("Palettes", body, [("close", "Close", false), ("remap", "Remap the picture…", false), ("use", "Use this palette", true)], "use", "close", 600);
        app.Session.LibraryChanged -= m.Refresh;
        if (r == "remap" && app.Session.Library.FindPalette(m.PaletteId) is { } chosen)
        {
            _ = app.RemapToPaletteAsync(new Palette(chosen.Name, chosen.Colors) { LibraryId = chosen.Id });
            return chosen.Id;
        }
        if (r != "use") return null;
        app.UsePalette(m.PaletteId);
        return m.PaletteId;
    }

    private static Button Small(string? icon, string? label, string? tip, Action click)
    {
        var b = UiKit.Button(icon, label, tip, click);
        b.Height = 28;
        if (label is null)
        {
            b.Width = 28;
            b.MinWidth = 28;
        }
        return b;
    }

    public void Select(string id)
    {
        PaletteId = id;
        _selected = -1;
        _confirmDelete = false;
        Refresh();
    }

    private void CommitName()
    {
        var p = Current;
        if (p.BuiltIn) return;
        if (!string.IsNullOrWhiteSpace(_name.Text) && _name.Text != p.Name) Lib.RenamePalette(p.Id, _name.Text);
        if (!string.IsNullOrWhiteSpace(_collection.Text) && _collection.Text != p.Collection) Lib.MovePalette(p.Id, _collection.Text);
    }

    public void EditColors(Action<List<ColorRgb>> change)
    {
        var p = Current;
        if (p.BuiltIn)
        {
            _info.Text = "Built-in palettes can't be changed: Duplicate it first.";
            return;
        }
        var list = p.Colors.ToList();
        change(list);
        Lib.UpdatePalette(p.Id, list);
    }

    private void Move(int dir)
    {
        int to = _selected + dir;
        EditColors(c =>
        {
            if (_selected < 0 || _selected >= c.Count || to < 0 || to >= c.Count) return;
            (c[_selected], c[to]) = (c[to], c[_selected]);
            _selected = to;
        });
    }

    private IEnumerable<MenuEntry> HarmonyMenu()
    {
        var fg = _app.Session.Foreground;
        foreach (var (kind, label) in new[] { ("complementary", "Complementary (opposite)"), ("analogous", "Analogous (neighbours)"), ("triadic", "Triadic (three apart)") })
        {
            var k = kind;
            yield return new(null, label, null, () => EditColors(c => { foreach (var h in ColorQuantizer.Harmony(fg, k)) if (!c.Contains(h)) c.Add(h); }), !Current.BuiltIn);
        }
    }

    private void Delete()
    {
        if (Current.BuiltIn) return;
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            _info.Text = "Click Delete again to delete this palette.";
            return;
        }
        Lib.DeletePalette(PaletteId);
        if (_app.Session.ActivePaletteId == PaletteId) _app.Session.SetActivePalette(null);
        Select(Lib.Palettes[0].Id);
    }

    // ================================================================== from a picture

    private void StartExtract()
    {
        _extractSource = _app.Session.Document.RenderFrame(_app.Session.Document.CurrentFrame, includePaper: false);
        _extract.IsVisible = true;
        Propose();
    }

    /// <summary>Proposes colours from a picture (tests call this with their own picture).</summary>
    public void ExtractFrom(RasterImage image, int count)
    {
        _extractSource = image;
        _extractCount = Math.Clamp(count, 2, 32);
        _refreshCount();
        _extract.IsVisible = true;
        Propose();
    }

    private void Propose()
    {
        if (_extractSource is null) return;
        _proposal = ColorQuantizer.Extract(_extractSource, _extractCount);
        _extracted.Children.Clear();
        foreach (var c in _proposal) _extracted.Children.Add(UiKit.Swatch(c, 22, 5));
        if (_proposal.Count == 0) _extracted.Children.Add(new TextBlock { Text = "The picture has no visible colours", Classes = { "dim" } });
    }

    public PaletteAsset? SaveProposal()
    {
        if (_proposal.Count == 0) return null;
        var p = Lib.AddPalette("From picture", "My palettes", _proposal);
        _extract.IsVisible = false;
        Select(p.Id);
        return p;
    }

    private async Task ExtractFromFileAsync()
    {
        var files = await _app.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "A picture to take colours from",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0) return;
        try
        {
            await using var s = await files[0].OpenReadAsync();
            ExtractFrom(ImageFiles.Decode(s), _extractCount);
        }
        catch (Exception ex)
        {
            _info.Text = $"Couldn't read {files[0].Name}: {ex.Message}";
        }
    }

    // ================================================================== files

    private async Task ImportAsync()
    {
        var files = await _app.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import palettes",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Palettes (.gpl, .hex)") { Patterns = ["*.gpl", "*.hex"] }, MainWindow.PackType, FilePickerFileTypes.All],
        });
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            try
            {
                if (path.EndsWith(AssetPack.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    var c = AssetPack.Import(Lib, path);
                    _info.Text = $"Imported {c}.";
                }
                else
                {
                    var (name, colors) = PaletteFiles.Read(path);
                    Select(Lib.AddPalette(name, "Imported", colors).Id);
                    _info.Text = $"Imported “{name}” ({colors.Count} colours).";
                }
            }
            catch (Exception ex)
            {
                _info.Text = $"Couldn't import {f.Name}: {ex.Message}";
            }
        }
    }

    private async Task ExportAsync()
    {
        var p = Current;
        var file = await _app.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export palette",
            SuggestedFileName = p.Name + ".gpl",
            DefaultExtension = "gpl",
            FileTypeChoices = [new FilePickerFileType("GIMP palette") { Patterns = ["*.gpl"] }, new FilePickerFileType("Hex colours") { Patterns = ["*.hex"] }],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            PaletteFiles.Write(path, p.Name, p.Colors);
            _info.Text = $"Exported {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            _info.Text = $"Couldn't export: {ex.Message}";
        }
    }

    // ================================================================== display

    public void Refresh()
    {
        _list.Children.Clear();
        Button? chosen = null;
        foreach (var group in Lib.Palettes.GroupBy(p => p.Collection))
        {
            _list.Children.Add(new TextBlock { Text = group.Key, Classes = { "dim" }, FontSize = 11, Margin = new Thickness(4, 4, 0, 0) });
            foreach (var p in group)
            {
                var row = new StackPanel { Spacing = 2 };
                row.Children.Add(UiKit.Row(6, new TextBlock { Text = p.Name, FontSize = 12 },
                    new TextBlock { Text = p.Colors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 11, Classes = { "dim", "num" } }));
                var strip = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var c in p.Colors.Take(12)) strip.Children.Add(new Border { Width = 12, Height = 8, Background = UiKit.Brush(c) });
                row.Children.Add(strip);
                var id = p.Id;
                var b = UiKit.Button(null, null, p.Name, () => Select(id));
                b.Content = row;
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Height = 40;
                b.Classes.Set("on", id == PaletteId);
                b.Name = "Palette_" + p.Name.Replace(' ', '_');
                if (id == PaletteId) chosen = b;
                _list.Children.Add(b);
            }
        }
        // The chosen palette is always in view in the list (e.g. one in the Lospec collection further down).
        if (chosen is not null) Avalonia.Threading.Dispatcher.UIThread.Post(() => chosen.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
        var cur = Current;
        _name.Text = cur.Name;
        _collection.Text = cur.Collection;
        _name.IsReadOnly = _collection.IsReadOnly = cur.BuiltIn;
        _editButtons.IsEnabled = !cur.BuiltIn;
        _swatches.Children.Clear();
        for (int i = 0; i < cur.Colors.Count; i++)
        {
            var c = cur.Colors[i];
            int index = i;
            var sw = UiKit.Button(null, null, $"{c.ToHex()} · click: select and use");
            sw.Width = sw.Height = sw.MinWidth = 30;
            sw.Padding = new Thickness(0);
            var dot = UiKit.Swatch(c, 26, 6);
            if (i == _selected)
            {
                dot.BorderBrush = new SolidColorBrush(DnColors.Accent);
                dot.BorderThickness = new Thickness(2);
            }
            sw.Content = dot;
            sw.Click += (_, _) =>
            {
                _selected = index;
                _app.Session.SetForeground(c);
                Refresh();
            };
            _swatches.Children.Add(sw);
        }
        _credit.IsVisible = cur.Credit is not null;
        if (cur.Credit is { } credit)
        {
            _creditText.Text = $"By {credit.Creator} · {credit.Mood}\n{credit.Licence}\nSource: {credit.Source}";
            ToolTip.SetTip(_creditText, $"Where the creator says so: {credit.Evidence}");
        }
        if (!_confirmDelete)
            _info.Text = cur.BuiltIn
                ? $"“{cur.Name}” is built in ({cur.Colors.Count} colours). Duplicate it to make your own version."
                : $"{cur.Colors.Count} colours. Click one to select it (and paint with it).";
        // A locked picture keeps its own copy: library edits never recolour it by themselves.
        var doc = _app.Session.Document;
        if (!_confirmDelete && doc.PaletteLocked && doc.Palette.LibraryId == cur.Id)
            _info.Text += cur.Colors.SequenceEqual(doc.Palette.Colors)
                ? " The open picture is locked to this palette and keeps its own copy: changes here don't recolour it."
                : " The open picture is locked to its own copy of this palette. To use these changes there: palette menu › Update from this palette.";
    }
}
