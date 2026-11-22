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
    // Mission 015: Import folder… shows what it found, then what it did, right here (no dialog per palette).
    private readonly StackPanel _folder = new() { Spacing = 6, IsVisible = false, Name = "PaletteFolderImport", MaxWidth = 330, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _folderText = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 330, Name = "PaletteFolderText" };
    private readonly TextBlock _folderDetails = new() { Classes = { "dim" }, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 330, Name = "PaletteFolderDetails" };
    private readonly Button _folderGo;
    private readonly Control _folderSubfolders;
    private string? _folderPath;
    private bool _includeSubfolders = true;
    private bool _importing;
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
        var import = Small("open", "Import…", "Palette files (.gpl, .hex, .pal, .txt, .ase, .png) or a DoodleNook asset pack", () => _ = ImportAsync());
        var importFolder = Small("folder", "Import folder…", "Every palette file in a folder (and its subfolders) into your library at once", () => _ = ImportFolderAsync());
        importFolder.Name = "PaletteImportFolder";
        importFolder.HorizontalAlignment = HorizontalAlignment.Left;
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 280, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        left.Children.Add(UiKit.Row(4, newButton, import));
        left.Children.Add(importFolder);
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
        _folder.Children.Add(new TextBlock { Text = "Import a folder of palettes", FontWeight = FontWeight.SemiBold });
        _folder.Children.Add(_folderText);
        var (subfolders, refreshSubfolders) = UiKit.Check("Include subfolders", "Also the palettes in the folders inside it", () => _includeSubfolders, v =>
        {
            _includeSubfolders = v;
            ShowFolderPlan();
        });
        _refreshSubfolders = refreshSubfolders;
        _folderSubfolders = subfolders;
        _folder.Children.Add(subfolders);
        _folderGo = Small("check", "Import", "Copy the palettes into your library (the folder is only read)", () => _ = RunFolderImportAsync());
        _folderGo.Name = "PaletteFolderGo";
        _folder.Children.Add(UiKit.Row(4, _folderGo, Small("close", "Close", null, () => _folder.IsVisible = false)));
        _folder.Children.Add(_folderDetails);
        right.Children.Add(_folder);
        right.Children.Add(_info);
        SetColumn(right, 2);
        Children.Add(right);

        _name.LostFocus += (_, _) => CommitName();
        _name.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitName(); };
        _collection.LostFocus += (_, _) => CommitName();
        app.Session.LibraryChanged += Refresh;
        Refresh();
    }

    private Action _refreshSubfolders = () => { };

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
            FileTypeFilter = [new FilePickerFileType("Palettes (.gpl, .hex, .pal, .txt, .ase, .png)") { Patterns = ["*.gpl", "*.hex", "*.pal", "*.txt", "*.ase", "*.png"] },
                MainWindow.PackType, FilePickerFileTypes.All],
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
                    var data = PaletteFiles.ReadFile(path);
                    Select(Lib.AddPalettes([(data, "Imported", Path.GetFileName(path))])[0].Id);
                    _info.Text = $"Imported “{data.Name}” ({data.Colors.Count} colours).";
                }
            }
            catch (Exception ex)
            {
                _info.Text = $"Couldn't import {f.Name}: {ex.Message}";
            }
        }
    }

    // ================================================================== a whole folder

    private async Task ImportFolderAsync()
    {
        var folders = await _app.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder of palettes (.gpl, .hex, .pal, .txt, .ase, .png)",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        PrepareFolderImport(path);
    }

    /// <summary>Shows what a folder holds, ready to import (tests call this with their own folder).</summary>
    public void PrepareFolderImport(string folder)
    {
        _folderPath = folder;
        _folderDetails.Text = "";
        _folderGo.IsVisible = true;
        _extract.IsVisible = false;
        _folder.IsVisible = true;
        ShowFolderPlan();
    }

    private void ShowFolderPlan()
    {
        if (_folderPath is not { } folder || _importing) return;
        try
        {
            var (palettes, other, subfolders) = PaletteFolderImport.Count(folder, _includeSubfolders);
            string name = new DirectoryInfo(folder).Name;
            _folderText.Text = $"“{name}” holds {palettes} palette file{(palettes == 1 ? "" : "s")}{(_includeSubfolders && subfolders > 0 ? " (with its subfolders)" : "")}" +
                               (other > 0 ? $" and {other} other file{(other == 1 ? "" : "s")}, which are left alone." : ".") +
                               $" They go into “{PaletteFolderImport.CollectionFor(folder, Lib)}”; palettes you already have are skipped.";
            _folderSubfolders.IsVisible = subfolders > 0;
            _folderGo.IsEnabled = palettes > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _folderText.Text = $"Couldn't look in the folder: {ex.Message}";
            _folderGo.IsEnabled = false;
        }
    }

    /// <summary>Imports the prepared folder: files are read in the background, then added to the library in one go.</summary>
    public async Task<PaletteImportSummary?> RunFolderImportAsync()
    {
        if (_folderPath is not { } folder || _importing) return null;
        _importing = true;
        _folderGo.IsEnabled = false;
        _folderDetails.Text = "Reading the palettes…";
        try
        {
            var existing = Lib.Palettes;
            string collection = PaletteFolderImport.CollectionFor(folder, Lib);
            bool sub = _includeSubfolders;
            var plan = await Task.Run(() => PaletteFolderImport.Plan(existing, collection, folder, sub));
            var summary = PaletteFolderImport.Commit(Lib, plan);
            _folderText.Text = PaletteFolderImport.Describe(summary);
            var notes = summary.Items.Where(i => i.Outcome is PaletteImportOutcome.Invalid or PaletteImportOutcome.Error)
                .Select(i => $"{Path.GetFileName(i.Path)}: {i.Detail}").Take(6).ToList();
            if (summary.Items.Count(i => i.Outcome is PaletteImportOutcome.Invalid or PaletteImportOutcome.Error) > notes.Count) notes.Add("…");
            _folderDetails.Text = string.Join("\n", notes);
            _folderGo.IsVisible = false;
            _folderSubfolders.IsVisible = false;
            if (summary.Imported.Count > 0) Select(summary.Imported[0].Id);
            return summary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _folderDetails.Text = $"Couldn't import the folder: {ex.Message}";
            _folderGo.IsEnabled = true;
            return null;
        }
        finally
        {
            _importing = false;
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
        _credit.IsVisible = cur.Credit is not null || cur.Origin is not null;
        if (cur.Credit is { } credit)
        {
            _creditText.Text = $"By {credit.Creator} · {credit.Mood}\n{credit.Licence}\nSource: {credit.Source}";
            ToolTip.SetTip(_creditText, $"Where the creator says so: {credit.Evidence}");
        }
        else if (cur.Origin is { } origin)
        {
            // An imported palette: what its file said about it, kept with your copy.
            var lines = new List<string> { $"Imported from {origin.FileName}" };
            if (origin.Creator is { } by) lines.Add($"By {by}");
            if (origin.Description is { } about) lines.Add(about);
            if (origin.Licence is { } licence) lines.Add(licence);
            if (origin.Source is { } source) lines.Add($"Source: {source}");
            _creditText.Text = string.Join("\n", lines);
            ToolTip.SetTip(_creditText, "Your own copy in your library: the original file isn't needed any more");
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
