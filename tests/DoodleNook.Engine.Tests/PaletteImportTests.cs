using System.Buffers.Binary;
using System.Text;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 015 — bulk palette import: the file formats, the folder scan, duplicates and name clashes, and the
/// personal library keeping its own copies.</summary>
public sealed class PaletteImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-palettes-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _lib;
    private readonly string _src;

    public PaletteImportTests()
    {
        _lib = Path.Combine(_dir, "library");
        _src = Path.Combine(_dir, "Downloads", "My Lospec");
        Directory.CreateDirectory(_src);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static readonly ColorRgb[] Sunset = [new(0x2B, 0x0F, 0x54), new(0xAB, 0x1F, 0x65), new(0xFF, 0x4F, 0x69), new(0xFF, 0xF7, 0xF8)];
    private static readonly ColorRgb[] Forest = [new(0x0E, 0x1C, 0x14), new(0x2E, 0x5A, 0x34), new(0x6E, 0xA0, 0x4C), new(0xC8, 0xE0, 0x8C), new(0xF4, 0xF0, 0xD8)];

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_src, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string WriteBytes(string relative, byte[] bytes)
    {
        var path = Path.Combine(_src, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Gpl(string name, IEnumerable<ColorRgb> colours) =>
        $"GIMP Palette\n#Palette Name: {name}\n#Description: A test palette by Example Artist.\n#Colors: {colours.Count()}\n" +
        string.Concat(colours.Select(c => $"{c.R}\t{c.G}\t{c.B}\t{c.ToHex()[1..].ToLowerInvariant()}\n"));

    // ================================================================== formats

    [Fact]
    public void Every_supported_format_reads_the_exact_colours()
    {
        var gpl = PaletteFiles.ReadFile(Write("sunset.gpl", Gpl("Sunset Four", Sunset)));
        Assert.Equal("Sunset Four", gpl.Name);
        Assert.Equal(Sunset, gpl.Colors);
        Assert.Equal("A test palette by Example Artist.", gpl.Description);

        Assert.Equal(Sunset, PaletteFiles.ReadFile(Write("sunset.hex", string.Concat(Sunset.Select(c => c.ToHex()[1..] + "\n")))).Colors);

        var jasc = PaletteFiles.ReadFile(Write("sunset.pal", "JASC-PAL\r\n0100\r\n4\r\n" + string.Concat(Sunset.Select(c => $"{c.R} {c.G} {c.B}\r\n"))));
        Assert.Equal(Sunset, jasc.Colors);
        Assert.Equal("sunset", jasc.Name);

        var paintNet = PaletteFiles.ReadFile(Write("sunset.txt",
            ";paint.net Palette File\n;Downloaded from a palette site\n;Palette Name: Sunset Four\n;Colors: 4\n" + string.Concat(Sunset.Select(c => "FF" + c.ToHex()[1..] + "\n"))));
        Assert.Equal(Sunset, paintNet.Colors);
        Assert.Equal("Sunset Four", paintNet.Name);

        // RIFF PAL: "RIFF" size "PAL " "data" size version count, then R G B flags.
        var riff = new List<byte>();
        riff.AddRange("RIFF"u8.ToArray());
        riff.AddRange(BitConverter.GetBytes(4 + 8 + 4 + 4 * Forest.Length));
        riff.AddRange("PAL data"u8.ToArray());
        riff.AddRange(BitConverter.GetBytes(4 + 4 * Forest.Length));
        riff.AddRange(BitConverter.GetBytes((ushort)0x300));
        riff.AddRange(BitConverter.GetBytes((ushort)Forest.Length));
        foreach (var c in Forest) riff.AddRange(new byte[] { c.R, c.G, c.B, 0 });
        Assert.Equal(Forest, PaletteFiles.ReadFile(WriteBytes("forest.pal", riff.ToArray())).Colors);

        Assert.Equal(Forest, PaletteFiles.ReadFile(WriteBytes("forest.ase", Ase("Forest", Forest))).Colors);
        Assert.Equal("Forest", PaletteFiles.ReadFile(WriteBytes("forest2.ase", Ase("Forest", Forest))).Name);

        // A palette picture: 8 × 8 blocks of colour, as palette sites offer "8x" PNGs.
        var picture = new RasterImage(8 * Forest.Length, 8);
        for (int i = 0; i < Forest.Length; i++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++) picture[i * 8 + x, y] = Pixel.Opaque(Forest[i]);
        Assert.Equal(Forest, PaletteFiles.ReadFile(WriteBytes("forest-8x.png", PngCodec.Encode(picture))).Colors);

        // The existing single-file import still works the same way.
        var (name, colours) = PaletteFiles.Read(Path.Combine(_src, "sunset.gpl"));
        Assert.Equal("Sunset Four", name);
        Assert.Equal(Sunset, colours);
    }

    /// <summary>An Adobe swatch exchange file with RGB swatches inside a named group.</summary>
    private static byte[] Ase(string group, IEnumerable<ColorRgb> colours)
    {
        var o = new List<byte>();
        void U16(int v) => o.AddRange(new[] { (byte)(v >> 8), (byte)v });
        void I32(int v)
        {
            var b = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, v);
            o.AddRange(b);
        }
        byte[] Name(string s) => [.. Encoding.BigEndianUnicode.GetBytes(s + "\0")];
        var list = colours.ToList();
        o.AddRange("ASEF"u8.ToArray());
        U16(1);
        U16(0);
        I32(list.Count + 2);
        var g = Name(group);
        U16(0xC001);
        I32(2 + g.Length);
        U16(g.Length / 2);
        o.AddRange(g);
        foreach (var c in list)
        {
            var n = Name(c.ToHex());
            U16(0x0001);
            I32(2 + n.Length + 4 + 12 + 2);
            U16(n.Length / 2);
            o.AddRange(n);
            o.AddRange("RGB "u8.ToArray());
            foreach (var v in new[] { c.R, c.G, c.B })
            {
                var f = new byte[4];
                BinaryPrimitives.WriteSingleBigEndian(f, v / 255f);
                o.AddRange(f);
            }
            U16(2);
        }
        U16(0xC002);
        I32(0);
        return [.. o];
    }

    [Fact]
    public void Damaged_palettes_and_files_that_are_not_palettes_are_told_apart()
    {
        Assert.Throws<InvalidDataException>(() => PaletteFiles.ReadFile(Write("broken.gpl", "GIMP Palette\n255 0\n")));
        Assert.Throws<InvalidDataException>(() => PaletteFiles.ReadFile(Write("empty.hex", "\n\n")));
        Assert.Throws<InvalidDataException>(() => PaletteFiles.ReadFile(Write("short.pal", "JASC-PAL\n0100\n3\n1 2 3\n")));
        Assert.Throws<NotAPaletteException>(() => PaletteFiles.ReadFile(Write("README.txt", "These palettes come from a palette site.\n")));
        var photo = new RasterImage(40, 40);
        for (int i = 0; i < photo.Pixels.Length; i++) photo.Pixels[i] = Pixel.Opaque(new ColorRgb((byte)i, (byte)(i / 7), 90));
        Assert.Throws<NotAPaletteException>(() => PaletteFiles.ReadFile(WriteBytes("photo.png", PngCodec.Encode(photo))));
    }

    // ================================================================== the folder

    [Fact]
    public void A_folder_imports_in_one_go_skips_duplicates_and_keeps_both_on_a_name_clash()
    {
        Write("sunset.gpl", Gpl("Sunset Four", Sunset));
        Write("sunset.hex", string.Concat(Sunset.Select(c => c.ToHex()[1..] + "\n")));              // the same colours again
        Write("sunset-reordered.hex", string.Concat(Sunset.Reverse().Select(c => c.ToHex()[1..] + "\n"))); // and in another order
        Write("nested/forest.gpl", Gpl("Forest Five", Forest));
        Write("nested/clash.gpl", Gpl("Sunset Four", Forest.Take(3)));                              // same name, other colours
        Write("pico.hex", "000000\n1D2B53\n7E2553\n008751\nAB5236\n5F574F\nC2C3C7\nFFF1E8\nFF004D\nFFA300\nFFEC27\n00E436\n29ADFF\n83769C\nFF77A8\nFFCCAA\n"); // built in
        Write("broken.gpl", "GIMP Palette\nnot a colour\n");
        Write("README.txt", "Downloaded palettes.\n");
        Write("notes.md", "# notes\n");

        var lib = new CreativeLibrary(_lib);
        int changes = 0;
        lib.Changed += () => changes++;
        var top = PaletteFolderImport.Import(lib, _src, includeSubfolders: false);
        Assert.Equal(1, top.ImportedCount);
        Assert.Equal(3, top.Duplicates);
        Assert.Equal(1, top.Invalid);
        Assert.Equal(2, top.Unsupported);
        Assert.Equal(5, top.Found);
        Assert.Equal(1, changes); // one save for the whole import
        Assert.Contains("already in your library as “PICO-8 palette”", top.Items.Single(i => i.Path.EndsWith("pico.hex")).Detail);
        Assert.Equal("5 palette files found · 1 imported · 3 already in your library (skipped) · 1 damaged or empty · 2 other files skipped.",
            PaletteFolderImport.Describe(top));

        var all = PaletteFolderImport.Import(lib, _src, includeSubfolders: true);
        Assert.Equal(2, all.ImportedCount); // the nested two; everything else is already there
        Assert.Equal(["Forest Five", "Sunset Four (2)"], all.Imported.Select(p => p.Name).Order().ToArray());
        Assert.Equal(0, PaletteFolderImport.Import(lib, _src, includeSubfolders: true).ImportedCount); // importing again adds nothing

        var sunset = lib.Palettes.Single(p => p.Name == "Sunset Four");
        Assert.Equal(Sunset, sunset.Colors);
        Assert.Equal("My Lospec", sunset.Collection);
        Assert.Equal("sunset.gpl", sunset.Origin!.FileName);
        Assert.Equal("A test palette by Example Artist.", sunset.Origin.Description);
        Assert.False(sunset.BuiltIn);
    }

    [Fact]
    public void Imported_palettes_are_independent_copies_that_survive_a_restart_and_the_folder_being_deleted()
    {
        Write("sunset.gpl", Gpl("Sunset Four", Sunset));
        Write("Lospec/forest.gpl", Gpl("Forest Five", Forest));
        var lib = new CreativeLibrary(_lib);
        Assert.Equal("Lospec (imported)", PaletteFolderImport.CollectionFor(Path.Combine(_src, "Lospec"), lib)); // never mixed with the built-in ones
        PaletteFolderImport.Import(lib, _src, includeSubfolders: true);
        Directory.Delete(Path.Combine(_dir, "Downloads"), recursive: true);

        var reopened = new CreativeLibrary(_lib);
        var sunset = reopened.Palettes.Single(p => p.Name == "Sunset Four");
        Assert.Equal(Sunset, sunset.Colors);
        Assert.Equal("sunset.gpl", sunset.Origin?.FileName);
        Assert.Equal("A test palette by Example Artist.", sunset.Origin?.Description);
        Assert.Equal(Forest, reopened.Palettes.Single(p => p.Name == "Forest Five").Colors);

        // Personal entries can be renamed and removed; built-in palettes can't be removed.
        Assert.True(reopened.RenamePalette(sunset.Id, "Evening"));
        Assert.Equal("sunset.gpl", reopened.FindPalette(sunset.Id)!.Origin?.FileName);
        Assert.True(reopened.DeletePalette(sunset.Id));
        Assert.False(reopened.DeletePalette("builtin:lospec-pico-8"));
        Assert.NotNull(new CreativeLibrary(_lib).FindPalette("builtin:lospec-pico-8"));
        Assert.Null(new CreativeLibrary(_lib).FindPalette(sunset.Id));
    }

    [Fact]
    public void An_imported_palette_locks_and_remaps_a_picture_like_any_other()
    {
        Write("sunset.gpl", Gpl("Sunset Four", Sunset));
        var lib = new CreativeLibrary(_lib);
        var imported = PaletteFolderImport.Import(lib, _src, false).Imported.Single();

        var doc = new Document(4, 1, null);
        for (int x = 0; x < 4; x++) doc.ActiveLayer.Image[x, 0] = Pixel.Opaque(new ColorRgb((byte)(Sunset[x].R - 3), Sunset[x].G, Sunset[x].B)); // slightly off
        PaletteRemap.Analyse(doc, imported.Colors).Apply(doc, imported.Name, lockAfter: true, libraryId: imported.Id);
        Assert.True(doc.PaletteLocked);
        Assert.Equal(Sunset, doc.Palette.Colors);
        Assert.Equal(imported.Id, doc.Palette.LibraryId);
        for (int x = 0; x < 4; x++) Assert.Equal(Pixel.Opaque(Sunset[x]), doc.ActiveLayer.Image[x, 0]);
    }

    /// <summary>Mission 016 (found with Inkscape's 5414-colour Munsell chart): a palette longer than 1024 colours keeps
    /// its first 1024, the summary says so, and importing the same folder again adds nothing.</summary>
    [Fact]
    public void A_very_long_palette_keeps_its_first_1024_colours_and_is_recognised_when_imported_again()
    {
        var text = new System.Text.StringBuilder("GIMP Palette\nName: Huge chart\n");
        for (int i = 0; i < 1500; i++) text.Append($"{i % 256} {i / 256 * 40} {(i * 7) % 256}\tc{i}\n");
        Write("charts/huge.gpl", text.ToString());
        var library = new CreativeLibrary(_lib);
        var first = PaletteFolderImport.Import(library, _src, includeSubfolders: true);
        var item = Assert.Single(first.Items);
        Assert.Equal(PaletteImportOutcome.Imported, item.Outcome);
        Assert.Equal("the first 1024 of 1500 colours (a palette holds at most 1024)", item.Detail);
        Assert.Equal(1024, first.Imported[0].Colors.Count);
        Assert.Equal(new ColorRgb(0, 0, 0), first.Imported[0].Colors[0]);

        var again = PaletteFolderImport.Import(library, _src, includeSubfolders: true);
        Assert.Equal(PaletteImportOutcome.Duplicate, Assert.Single(again.Items).Outcome);
        Assert.Empty(again.Imported);
        Assert.Single(new CreativeLibrary(_lib).Palettes, p => p.Name.StartsWith("Huge chart"));
    }
}
