using System.IO.Compression;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Operations;
using DoodleNook.Engine.Painting;

namespace DoodleNook.Engine.Tests;

/// <summary>Brushes, stamps, patterns, palettes and boards: storage, packs, operations.</summary>
public sealed class CreativeLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dn-lib-" + Guid.NewGuid().ToString("N")[..8]);

    public CreativeLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static RasterImage Blob(ColorRgb c, int w = 12, int h = 10)
    {
        var img = new RasterImage(w + 6, h + 6); // transparent border to trim
        for (int y = 3; y < 3 + h; y++)
            for (int x = 3; x < 3 + w; x++)
                img[x, y] = Pixel.Opaque(c);
        return img;
    }

    // ================================================================== brushes

    [Fact]
    public void Brush_presets_are_created_changed_and_kept_across_restarts()
    {
        var lib = new CreativeLibrary(_dir);
        var copy = lib.DuplicateBrush("pencil");
        Assert.Equal("Sketch pencil copy", copy.Name);
        Assert.Equal(BrushCategories.Mine, copy.Category);
        Assert.True(lib.UpdateBrush(copy.Id, copy.Settings with { Grain = 0.95, PressureGamma = 0.7 }));
        Assert.True(lib.RenameBrush(copy.Id, "My grainy pencil"));
        lib.SetFavoriteBrush(copy.Id, true);
        lib.SetFavoriteBrush("ink", true);
        var second = lib.AddBrush("Throwaway", new BrushSettings());
        Assert.True(lib.DeleteBrush(second.Id));

        var again = new CreativeLibrary(_dir); // a restart
        var b = again.FindBrush(copy.Id)!;
        Assert.Equal("My grainy pencil", b.Name);
        Assert.Equal(0.95, b.Settings.Grain);
        Assert.Equal(0.7, b.Settings.PressureGamma);
        Assert.True(again.IsFavoriteBrush(copy.Id) && again.IsFavoriteBrush("ink"));
        Assert.Null(again.FindBrush(second.Id));
        Assert.Empty(again.LoadProblems);
    }

    [Fact]
    public void Built_in_brushes_cannot_be_changed_or_deleted()
    {
        var lib = new CreativeLibrary(_dir);
        var before = lib.FindBrush("ink")!.Settings;
        Assert.False(lib.UpdateBrush("ink", before with { Size = 99 }));
        Assert.False(lib.DeleteBrush("ink"));
        Assert.False(lib.RenameBrush("ink", "Mine now"));
        Assert.Equal(before, lib.FindBrush("ink")!.Settings);
        Assert.False(lib.DeleteStamp(BuiltInAssets.Prefix + "grass"));
        Assert.False(lib.DeletePalette(BuiltInAssets.Prefix + "nook32"));
    }

    [Fact]
    public void A_damaged_library_file_is_set_aside_not_overwritten()
    {
        File.WriteAllText(Path.Combine(_dir, "library.json"), "{ this is not json");
        var lib = new CreativeLibrary(_dir);
        Assert.Single(lib.LoadProblems);
        Assert.NotEmpty(lib.Brushes); // the built-in ones
        Assert.Single(Directory.GetFiles(_dir, "library.broken-*.json"));
        lib.AddBrush("New", new BrushSettings());
        Assert.True(File.Exists(Path.Combine(_dir, "library.json")));
    }

    [Fact]
    public void A_library_from_a_newer_version_is_opened_read_only()
    {
        File.WriteAllText(Path.Combine(_dir, "library.json"), "{ \"Version\": 99, \"Brushes\": [], \"FutureThing\": { \"x\": 1 } }");
        var lib = new CreativeLibrary(_dir);
        Assert.True(lib.ReadOnly);
        lib.AddBrush("Not written", new BrushSettings());
        Assert.Contains("FutureThing", File.ReadAllText(Path.Combine(_dir, "library.json")));
    }

    [Fact]
    public void A_brush_whose_tip_picture_is_gone_still_paints_and_says_so()
    {
        var lib = new CreativeLibrary(_dir);
        var stamp = lib.AddStamp("Blob", null, Blob(ColorRgb.Parse("#5F8F5A")));
        var brush = lib.AddBrush("Blob brush", new BrushSettings { TipId = "stamp:" + stamp.Id, TipColors = true });
        Assert.False(lib.HasMissingParts(brush.Settings));
        Assert.Single(lib.BrushesUsing(stamp.Id));
        File.Delete(Path.Combine(_dir, "images", stamp.Id + ".png")); // deleted outside DoodleNook

        var again = new CreativeLibrary(_dir);
        var b = again.FindBrush(brush.Id)!;
        Assert.True(again.HasMissingParts(b.Settings));
        Assert.Contains(again.LoadProblems, p => p.Contains("missing"));
        var doc = new Document(80, 60, null);
        var e = new StrokeEngine(doc);
        e.Begin(new StrokeStyle(b.Settings, PressureCurve.Normal, ColorRgb.Black, PaintMode.Paint, Symmetry.None, again.Resolve(b.Settings)), new StrokeSample(40, 30));
        e.End();
        Assert.True(Pixel.A(doc.ActiveLayer.Image[40, 30]) > 0); // falls back to the round tip
    }

    // ================================================================== stamps, patterns, packs

    [Fact]
    public void Stamps_are_copied_trimmed_renamed_moved_and_deleted()
    {
        var lib = new CreativeLibrary(_dir);
        var source = Blob(ColorRgb.Parse("#C96F4A"));
        var s = lib.AddStamp("Brick", "Bits", source);
        source[5, 5] = 0; // changing the source later changes nothing
        var img = lib.StampImage(s.Id)!;
        Assert.Equal((12, 10), (img.Width, img.Height));
        Assert.Equal(255, Pixel.A(img[2, 2]));
        Assert.True(lib.RenameStamp(s.Id, "Red brick"));
        Assert.True(lib.MoveStamp(s.Id, "Walls"));
        var again = new CreativeLibrary(_dir);
        Assert.Equal(("Red brick", "Walls"), (again.FindStamp(s.Id)!.Name, again.FindStamp(s.Id)!.Collection));
        Assert.Equal(img.Pixels, again.StampImage(s.Id)!.Pixels);
        Assert.True(again.DeleteStamp(s.Id));
        Assert.False(File.Exists(Path.Combine(_dir, "images", s.Id + ".png")));
        Assert.Throws<ArgumentException>(() => again.AddStamp("Nothing", null, new RasterImage(4, 4)));
    }

    [Fact]
    public void Asset_packs_carry_brushes_with_their_pictures_and_never_replace_anything()
    {
        var lib = new CreativeLibrary(_dir);
        var stamp = lib.AddStamp("Tuft", null, Blob(ColorRgb.Parse("#5F8F5A")));
        var pattern = lib.AddPattern("Tile", null, Blob(ColorRgb.Parse("#3F6A82"), 8, 8));
        var brush = lib.AddBrush("Tuft brush", new BrushSettings { TipId = "stamp:" + stamp.Id, TipColors = true, TextureId = "pattern:" + pattern.Id, Grain = 0.3 });
        var palette = lib.AddPalette("Game", null, [ColorRgb.Parse("#123456"), ColorRgb.Parse("#ABCDEF")]);
        var pack = Path.Combine(_dir, "mine.dnkpack");
        var exported = AssetPack.Export(lib, pack, brushIds: [brush.Id], paletteIds: [palette.Id]);
        Assert.Equal(new AssetPackContents(1, 1, 1, 1), exported); // the stamp and pattern came along

        var other = new CreativeLibrary(Path.Combine(_dir, "other"));
        var imported = AssetPack.Import(other, pack);
        Assert.Equal(exported, imported);
        var b = other.Brushes.Single(x => x.Name == "Tuft brush");
        Assert.NotEqual(brush.Id, b.Id);
        Assert.False(other.HasMissingParts(b.Settings));
        Assert.Equal(lib.StampImage(stamp.Id)!.Pixels, other.Tip(b.Settings.TipId) is not null ? other.StampImage(b.Settings.TipId![6..])!.Pixels : null);
        Assert.Equal(palette.Colors, other.Palettes.Single(p => p.Name == "Game").Colors);
        AssetPack.Import(other, pack); // importing twice adds copies; nothing is overwritten
        Assert.Equal(2, other.Brushes.Count(x => x.Name == "Tuft brush"));
    }

    [Fact]
    public void Invalid_asset_packs_fail_cleanly_without_changing_the_library()
    {
        var lib = new CreativeLibrary(_dir);
        int before = lib.Brushes.Count;
        var notZip = Path.Combine(_dir, "bad.dnkpack");
        File.WriteAllText(notZip, "hello");
        Assert.Throws<InvalidDataException>(() => AssetPack.Import(lib, notZip));
        var missingPicture = Path.Combine(_dir, "broken.dnkpack");
        using (var zip = ZipFile.Open(missingPicture, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("pack.json").Open());
            w.Write("{\"Format\":\"DoodleNook asset pack\",\"Version\":1,\"Stamps\":[{\"Id\":\"x\",\"Name\":\"Ghost\"}]}");
        }
        Assert.Throws<InvalidDataException>(() => AssetPack.Import(lib, missingPicture));
        Assert.Equal(before, lib.Brushes.Count);
        Assert.DoesNotContain(lib.Stamps, s => s.Name == "Ghost");
    }

    // ================================================================== palettes

    [Fact]
    public void Palettes_keep_exact_colours_and_order_and_round_trip_through_gpl_and_hex()
    {
        var lib = new CreativeLibrary(_dir);
        var colours = new[] { "#000000", "#FFFFFF", "#123456", "#FE01A7", "#808080" }.Select(ColorRgb.Parse).ToList();
        var p = lib.AddPalette("Exact", "Game", colours);
        var reordered = new List<ColorRgb> { colours[2], colours[0], colours[1], colours[3] }; // one removed, order changed
        Assert.True(lib.UpdatePalette(p.Id, reordered));
        var dup = lib.DuplicatePalette(p.Id);
        Assert.Equal(reordered, new CreativeLibrary(_dir).FindPalette(p.Id)!.Colors);
        Assert.Equal(reordered, dup.Colors);
        foreach (var ext in new[] { ".gpl", ".hex" })
        {
            var path = Path.Combine(_dir, "exact" + ext);
            PaletteFiles.Write(path, "Exact", reordered);
            var (name, read) = PaletteFiles.Read(path);
            Assert.Equal(reordered, read);
            if (ext == ".gpl") Assert.Equal("Exact", name);
        }
        File.WriteAllText(Path.Combine(_dir, "bad.gpl"), "GIMP Palette\nName: x\nnot a colour\n");
        Assert.Throws<InvalidDataException>(() => PaletteFiles.Read(Path.Combine(_dir, "bad.gpl")));
    }

    [Fact]
    public void Colour_extraction_keeps_pixel_art_colours_and_reduces_photos()
    {
        var sprite = new RasterImage(16, 16);
        var used = new[] { "#1B1622", "#5F8F5A", "#F2C14E" }.Select(ColorRgb.Parse).ToArray();
        for (int i = 0; i < 256; i++) sprite.Pixels[i] = i % 7 == 0 ? 0 : Pixel.Opaque(used[i % 3]);
        Assert.Equal(used.OrderBy(c => c.R + c.G + c.B).ToHashSet(), ColorQuantizer.Extract(sprite, 8).ToHashSet());

        var gradient = new RasterImage(64, 64);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                gradient[x, y] = Pixel.Opaque(new ColorRgb((byte)(x * 4), (byte)(y * 4), 128));
        var six = ColorQuantizer.Extract(gradient, 6);
        Assert.Equal(6, six.Count);
        Assert.Equal(six, ColorQuantizer.Extract(gradient, 6)); // deterministic
        Assert.Equal(3, ColorQuantizer.Harmony(ColorRgb.Parse("#C96F4A"), "triadic").Count);
    }

    // ================================================================== stamps and patterns on the canvas

    [Fact]
    public void Placing_a_stamp_scales_rotates_fades_respects_the_selection_and_undoes()
    {
        var doc = new Document(100, 80, null);
        var stamp = CreativeLibrary.Trim(Blob(ColorRgb.Parse("#C96F4A"), 10, 10))!;
        Assert.True(StampsAndPatterns.PlaceStamp(doc, doc.ActiveLayer, stamp, new StampPlacement(30, 40, Scale: 2)));
        var img = doc.ActiveLayer.Image;
        Assert.Equal(255, Pixel.A(img[30, 40]));
        Assert.True(Pixel.A(img[21, 40]) > 0 && Pixel.A(img[17, 40]) == 0); // 20 px wide around x = 30
        Assert.Equal(1, doc.History.UndoCount);

        StampsAndPatterns.PlaceStamp(doc, doc.ActiveLayer, stamp, new StampPlacement(70, 40, Scale: 1, Rotation: 45, Opacity: 0.5));
        Assert.InRange(Pixel.A(img[70, 40]), 120, 135);
        Assert.True(Pixel.A(img[70, 33]) > 0); // a diamond: the corner reaches up

        doc.SetSelection(Selections.Rectangle(doc, 0, 0, 50, 40), "Select");
        StampsAndPatterns.PlaceStamp(doc, doc.ActiveLayer, stamp, new StampPlacement(50, 60, Scale: 3));
        Assert.Equal(0, Pixel.A(img[50, 60])); // outside the selection

        Assert.True(doc.Undo() && doc.Undo() && doc.Undo());
        Assert.All(img.Pixels, p => Assert.Equal(0u, p));
    }

    [Fact]
    public void Pixel_art_stamps_copy_whole_pixels()
    {
        var doc = new Document(32, 32, null);
        var stamp = new RasterImage(3, 2);
        stamp.Pixels[0] = Pixel.Opaque(ColorRgb.Parse("#FF0000"));
        stamp.Pixels[4] = Pixel.Opaque(ColorRgb.Parse("#00FF00"));
        StampsAndPatterns.PlaceStamp(doc, doc.ActiveLayer, stamp, new StampPlacement(10.3, 10.8, Scale: 2.2, Rotation: 10), pixelExact: true);
        var img = doc.ActiveLayer.Image;
        var colours = img.Pixels.Where(p => p != 0).Distinct().ToList();
        Assert.Equal(2, colours.Count); // only the stamp's exact colours, no blends
        Assert.Equal(8, img.Pixels.Count(p => p != 0)); // two pixels, each 2 × 2
    }

    [Fact]
    public void A_stamp_on_an_animation_frame_changes_only_that_frame()
    {
        var doc = Document.NewAnimation(60, 40, null, frames: 3);
        doc.SetCurrentFrame(1);
        var stamp = CreativeLibrary.Trim(Blob(ColorRgb.Parse("#5F8FA8"), 6, 6))!;
        Assert.True(StampsAndPatterns.PlaceStamp(doc, doc.ActiveLayer, stamp, new StampPlacement(30, 20)));
        Assert.Null(doc.CelAt(doc.ActiveLayer, 0));
        Assert.Null(doc.CelAt(doc.ActiveLayer, 2));
        Assert.Equal(255, Pixel.A(doc.CelAt(doc.ActiveLayer, 1)!.Image[30, 20]));
        Assert.True(doc.Undo());
        Assert.Null(doc.CelAt(doc.ActiveLayer, 1));
    }

    [Fact]
    public void Pattern_fills_tile_exactly_with_offset_scale_selection_and_transparency()
    {
        var tile = new RasterImage(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                tile[x, y] = x == 0 && y == 0 ? 0u : Pixel.Opaque(new ColorRgb((byte)(x * 60), (byte)(y * 60), 7));
        var doc = new Document(40, 30, null);
        Assert.True(StampsAndPatterns.FillSelection(doc, doc.ActiveLayer, tile, new PatternFillOptions(OffsetX: 1, OffsetY: 2)));
        var img = doc.ActiveLayer.Image;
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 40; x++)
                Assert.Equal(tile[((x - 1) % 4 + 4) % 4, ((y - 2) % 4 + 4) % 4], img[x, y]); // exact, seamless, shifted
        Assert.True(doc.Undo());

        StampsAndPatterns.FillSelection(doc, doc.ActiveLayer, tile, new PatternFillOptions(Scale: 2), pixelExact: true);
        Assert.Equal(img[2, 2], img[3, 3]);
        Assert.Equal(tile[1, 1], img[2, 2]);
        Assert.True(doc.Undo());

        doc.SetSelection(Selections.Rectangle(doc, 10, 10, 20, 20), "Select");
        StampsAndPatterns.FillSelection(doc, doc.ActiveLayer, tile, new PatternFillOptions());
        Assert.Equal(0u, img[5, 5]);
        Assert.NotEqual(0u, img[15, 15]);
    }

    [Fact]
    public void Large_pattern_fills_painted_row_by_row_in_parallel_match_a_pixel_by_pixel_reference()
    {
        // 700 × 500 is above the size where rows are painted in parallel; every pixel must be what painting them one at a
        // time gives: the pattern (rotated, scaled, faded) composited over the layer, through a soft selection, with the
        // transparency lock on.
        var lib = new CreativeLibrary();
        var tile = lib.PatternImage(BuiltInAssets.Prefix + "brick")!;
        var doc = new Document(700, 500, null);
        var layer = doc.ActiveLayer;
        for (int y = 0; y < 500; y++)
            for (int x = 0; x < 700; x++)
                if ((x / 50 + y / 50) % 2 == 0) layer.Image[x, y] = Pixel.FromStraight(90, 140, 200, (byte)(120 + (x + y) % 120));
        doc.SetLayerProperties(layer, layer.Properties with { AlphaLocked = true });
        doc.SetSelection(Selections.Ellipse(doc, 20, 10, 690, 490, antialias: true), "Select");
        var before = layer.Image.Clone();
        var o = new PatternFillOptions(Scale: 1.7, OffsetX: 13, OffsetY: -7, Rotation: 23, Opacity: 0.8);
        Assert.True(StampsAndPatterns.FillSelection(doc, layer, tile, o));
        Assert.True(Enumerable.Range(0, before.Pixels.Length).Count(i => before.Pixels[i] != layer.Image.Pixels[i]) > 100_000);
        for (int y = 0; y < 500; y++)
            for (int x = 0; x < 700; x++)
            {
                uint expected = before[x, y];
                int sel = doc.SelectionAt(x, y);
                byte ba = Pixel.A(expected);
                if (sel > 0 && ba > 0)
                {
                    // Same rounding as the operation: the pattern's opacity first, then the selection's softness.
                    uint p = Fade(Fade(StampsAndPatterns.PatternAt(tile, o, x, y, pixelExact: false), 0.8f), sel < 255 ? sel / 255f : 1);
                    if (Pixel.A(p) > 0)
                    {
                        var (r, g, b, _) = Pixel.ToStraight(Pixel.Over(p, expected));
                        expected = Pixel.FromStraight(r, g, b, ba);
                    }
                }
                Assert.Equal(expected, layer.Image[x, y]);
            }
        Assert.True(doc.Undo());
        Assert.Equal(before.Pixels, layer.Image.Pixels);
    }

    private static uint Fade(uint p, float k) => k >= 1 ? p : k <= 0 ? 0 :
        Pixel.Pack((byte)(Pixel.A(p) * k + 0.5f), (byte)(Pixel.R(p) * k + 0.5f), (byte)(Pixel.G(p) * k + 0.5f), (byte)(Pixel.B(p) * k + 0.5f));

    [Fact]
    public void Pattern_bucket_fills_only_the_clicked_region()
    {
        var doc = new Document(30, 20, null);
        for (int y = 0; y < 20; y++) doc.ActiveLayer.Image[15, y] = Pixel.Opaque(ColorRgb.Black); // a wall
        var tile = new RasterImage(2, 2);
        tile.Fill(Pixel.Opaque(ColorRgb.Parse("#E88A9A")));
        Assert.True(StampsAndPatterns.FillAt(doc, doc.ActiveLayer, 5, 5, tile, new PatternFillOptions(), new FillOptions()));
        Assert.Equal(tile[0, 0], doc.ActiveLayer.Image[3, 3]);
        Assert.Equal(0u, doc.ActiveLayer.Image[25, 5]);
    }

    // ================================================================== reference boards

    [Fact]
    public void Reference_boards_save_their_own_copies_and_sample_colours()
    {
        var board = new ReferenceBoard { Background = ColorRgb.Parse("#F3ECE4"), Zoom = 0.5 };
        var a = board.Add(Blob(ColorRgb.Parse("#5F8FA8"), 20, 10), "Sky");
        var b = board.Add(Blob(ColorRgb.Parse("#C96F4A"), 10, 10), "Roof");
        Assert.True(b.X > a.X + a.Width - 1);
        b.Width = 64;
        Assert.Equal(ColorRgb.Parse("#C96F4A"), board.ColorAt(b.X + 32, b.Y + 32));
        Assert.Null(board.ColorAt(b.X + 1, b.Y + 1)); // the transparent border
        var path = Path.Combine(_dir, "refs.dnkboard");
        board.Save(path);
        var again = ReferenceBoard.Load(path);
        Assert.Equal(2, again.Items.Count);
        Assert.Equal(("Roof", 64.0), (again.Items[1].Name, again.Items[1].Width));
        Assert.Equal(board.Items[0].Image.Pixels, again.Items[0].Image.Pixels);
        Assert.Equal((ColorRgb.Parse("#F3ECE4"), 0.5), (again.Background, again.Zoom));
        File.WriteAllText(Path.Combine(_dir, "bad.dnkboard"), "nope");
        Assert.Throws<InvalidDataException>(() => ReferenceBoard.Load(Path.Combine(_dir, "bad.dnkboard")));
    }

    [Fact]
    public void Built_in_stamps_and_patterns_are_drawn_and_patterns_tile_seamlessly()
    {
        foreach (var (asset, draw) in BuiltInAssets.Stamps)
        {
            var img = draw();
            Assert.True(img.Pixels.Count(p => Pixel.A(p) > 0) > img.Pixels.Length / 8, asset.Name);
        }
        foreach (var (asset, draw) in BuiltInAssets.Patterns)
        {
            var img = draw();
            Assert.All(img.Pixels, p => Assert.Equal(255, Pixel.A(p))); // opaque tiles
            Assert.True(img.Pixels.Distinct().Count() > 1, asset.Name);
        }
    }
}
