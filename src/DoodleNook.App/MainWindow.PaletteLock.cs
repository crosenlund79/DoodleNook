using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using DoodleNook.App.Controls;
using DoodleNook.App.Dialogs;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App;

/// <summary>
/// Palette Lock and Remap to Palette in the window: one set of actions, offered from the colour panel's palette menu,
/// the Pixel Art palette column, the lock badge on the colour chips, the Image/Sprite menu and the palette manager.
/// Every change of a locked picture's colours goes through the remap preview; nothing is recoloured without Apply.
/// </summary>
public partial class MainWindow
{
    /// <summary>The remap dialog shown last (tests drive it).</summary>
    internal PaletteRemapDialog? LastRemapDialog { get; set; }

    /// <summary>The palette-lock entries for a palette menu (<paramref name="shown"/>: the palette the menu is about).</summary>
    internal IEnumerable<MenuEntry> PaletteLockEntries(Palette? shown = null)
    {
        var doc = Session.Document;
        if (doc.PaletteLocked)
        {
            yield return MenuEntry.Heading($"Locked to “{doc.Palette.Name}” ({doc.Palette.Colors.Count} colours)");
            yield return new("plus", "Add a colour to the palette…", null, () => _ = AddPaletteColourAsync());
            if (LibraryVersion() is { } lib)
                yield return new("palette", $"Update from “{lib.Name}” in your palettes…", null, () => _ = UpdateFromLibraryAsync());
            yield return new("palette", "Remap to another palette…", null, () => _ = RemapToPaletteAsync());
            yield return new("lock", "Unlock (any colour again)", null, UnlockPalette);
        }
        else
        {
            yield return MenuEntry.Heading("Palette lock");
            if (shown is { Colors.Count: > 0 })
                yield return new("lock", $"Lock the picture to “{shown.Name}”…", null, () => _ = LockToPaletteAsync(shown));
            else yield return new("lock", "Lock the picture to a palette…", null, () => _ = RemapToPaletteAsync(forceLock: true));
            yield return new("palette", "Remap the picture to a palette…", null, () => _ = RemapToPaletteAsync());
        }
    }

    /// <summary>The library palette a locked picture came from, when its colours have changed since (else null).</summary>
    private Engine.Library.PaletteAsset? LibraryVersion()
    {
        var doc = Session.Document;
        return Session.Library.FindPalette(doc.Palette.LibraryId) is { } lib && !lib.Colors.SequenceEqual(doc.Palette.Colors) ? lib : null;
    }

    /// <summary>Locks the picture to a palette: at once when it only uses those colours, otherwise through the remap
    /// preview (Cancel leaves the picture unlocked and unchanged).</summary>
    public async Task<bool> LockToPaletteAsync(Palette palette)
    {
        HideFlyouts();
        var doc = Session.Document;
        var use = await Task.Run(() => doc.CheckPalette(palette.Colors));
        if (use.OnlyPaletteColours)
        {
            doc.LockToPalette(palette.Clone());
            ShowToast($"Locked to “{palette.Name}”: only its colours can be used (transparency stays free)");
            return true;
        }
        return await PaletteRemapDialog.ShowAsync(this, new RemapRequest($"Lock to “{palette.Name}”", palette.Clone(), ForceLock: true,
            Intro: $"The picture uses colours that “{palette.Name}” doesn't have. Choose what they become; Cancel leaves the picture unlocked and unchanged."));
    }

    /// <summary>Image › Remap to palette…</summary>
    public Task<bool> RemapToPaletteAsync(Palette? target = null, bool forceLock = false)
    {
        HideFlyouts();
        return PaletteRemapDialog.ShowAsync(this, new RemapRequest(forceLock ? "Lock to a palette" : "Remap to palette", target, ForceLock: forceLock));
    }

    public void UnlockPalette()
    {
        if (!Session.Document.PaletteLocked) return;
        Session.Document.UnlockPalette();
        ShowToast("Palette unlocked: any colour can be used again");
    }

    /// <summary>Adds a colour to the locked palette (explicitly; existing artwork is not changed).</summary>
    public async Task AddPaletteColourAsync()
    {
        HideFlyouts();
        var doc = Session.Document;
        var c = await _dialogs.AskColourAsync($"Add a colour to “{doc.Palette.Name}”", Session.Foreground, "Add");
        if (c is not { } colour) return;
        if (doc.AddPaletteColour(colour)) ShowToast($"{colour.ToHex()} added to “{doc.Palette.Name}”");
        Session.SetForeground(colour);
    }

    /// <summary>Replaces a palette colour; its pixels move to the new colour (previewed, applied only on Apply).</summary>
    public async Task<bool> ReplacePaletteColourAsync(ColorRgb old)
    {
        HideFlyouts();
        var doc = Session.Document;
        var c = await _dialogs.AskColourAsync($"Replace {old.ToHex()}", old, "Next");
        if (c is not { } colour || colour == old) return false;
        var next = doc.Palette.Clone();
        int i = next.Colors.IndexOf(old);
        if (i < 0) return false;
        if (next.Colors.Contains(colour)) next.Colors.RemoveAt(i); // replacing with a colour already there merges them
        else next.Colors[i] = colour;
        return await PaletteRemapDialog.ShowAsync(this, new RemapRequest("Replace a palette colour", next, ForceLock: true, ChoosePalette: false,
            MatchFrom: doc.Palette.Colors, Intro: $"Pixels in {old.ToHex()} become {colour.ToHex()}. Check the preview, change anything you like, then Apply."));
    }

    /// <summary>Removes a palette colour; its pixels get the closest remaining colour (previewed first).</summary>
    public async Task<bool> RemovePaletteColourAsync(ColorRgb old)
    {
        HideFlyouts();
        var doc = Session.Document;
        if (doc.Palette.Colors.Count <= 1) return false;
        var next = doc.Palette.Clone();
        next.Colors.Remove(old);
        return await PaletteRemapDialog.ShowAsync(this, new RemapRequest("Remove a palette colour", next, ForceLock: true, ChoosePalette: false,
            Intro: $"Pixels in {old.ToHex()} need another colour from the palette. Check the suggestion, then Apply."));
    }

    /// <summary>The palette was changed in the library: apply the new version to this picture (previewed first).</summary>
    public Task<bool> UpdateFromLibraryAsync()
    {
        HideFlyouts();
        var doc = Session.Document;
        if (LibraryVersion() is not { } lib) return Task.FromResult(false);
        return PaletteRemapDialog.ShowAsync(this, new RemapRequest($"Update from “{lib.Name}”", new Palette(lib.Name, lib.Colors) { LibraryId = lib.Id },
            ForceLock: true, ChoosePalette: false, MatchFrom: doc.Palette.Colors,
            Intro: "The palette changed in your library. Colours that were replaced move to the colour now in their place; check the preview, then Apply."));
    }

    /// <summary>Right-click menu of a swatch while the picture is locked.</summary>
    internal IEnumerable<MenuEntry> LockedSwatchEntries(ColorRgb colour)
    {
        yield return MenuEntry.Heading(colour.ToHex());
        yield return new(null, "Replace this colour…", null, () => _ = ReplacePaletteColourAsync(colour));
        yield return new("trash", "Remove from the palette…", null, () => _ = RemovePaletteColourAsync(colour), Session.Document.Palette.Colors.Count > 1);
    }
}

/// <summary>A colour chosen with a saturation/value square, a hue strip and a hex box.</summary>
public static class ColourDialogs
{
    public static async Task<ColorRgb?> AskColourAsync(this DialogHost host, string title, ColorRgb initial, string ok)
    {
        var sv = new SaturationValueArea { Height = 150, Width = 260 };
        var hue = new HueStrip { Height = 22, Width = 260 };
        var swatch = UiKit.Swatch(initial, 30, 8, outline: true);
        var hex = new TextBox { Text = initial.ToHex(), Width = 100, Name = "ColourHex" };
        var current = initial;
        var (h0, s0, v0) = initial.ToHsv();
        double h = h0, s = s0, v = v0;
        sv.Set(h, s, v);
        hue.Set(h);
        void Show(ColorRgb c, bool updateHex = true)
        {
            current = c;
            swatch.Background = UiKit.Brush(c);
            if (updateHex) hex.Text = c.ToHex();
        }
        sv.UserChanged += (ns, nv) =>
        {
            s = ns;
            v = nv;
            Show(ColorRgb.FromHsv(h, s, v));
        };
        hue.UserChanged += nh =>
        {
            h = nh;
            sv.Set(h, s, v);
            Show(ColorRgb.FromHsv(h, s, v));
        };
        hex.TextChanged += (_, _) =>
        {
            if (ColorRgb.TryParse(hex.Text, out var c)) Show(c, updateHex: false);
        };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(sv);
        body.Children.Add(hue);
        body.Children.Add(UiKit.Row(8, swatch, hex));
        var r = await host.ShowAsync(title, body, [("cancel", "Cancel", false), ("ok", ok, true)], "ok", "cancel", 300);
        if (r != "ok") return null;
        // What's typed in the hex box counts even if the box never lost focus.
        return ColorRgb.TryParse(hex.Text, out var typed) ? typed : current;
    }
}
