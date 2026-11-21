using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DoodleNook.App.Files;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using static DoodleNook.App.Tests.WindowTests;

namespace DoodleNook.App.Tests;

/// <summary>Mission 012 — pasting pictures copied in other programs, and Paste as reference. The system clipboard is
/// replaced by a fake that hands over the same raw data Windows would (PNG bytes, a bitmap, copied file paths); the
/// Windows reader itself is exercised by the smoke test on Windows.</summary>
public sealed class ClipboardWindowTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "dn-clip-" + Guid.NewGuid().ToString("N")[..8]);

    public ClipboardWindowTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        try { Directory.Delete(_temp, true); } catch (IOException) { }
    }

    internal sealed class FakeClipboard : ISystemClipboard
    {
        public uint? ChangeCount { get; set; } = 1;
        public ClipboardContent Content { get; set; } = ClipboardContent.Empty;
        public Exception? Throws { get; set; }
        public int Reads { get; private set; }

        public Task<ClipboardContent> ReadAsync()
        {
            Reads++;
            return Throws is { } ex ? Task.FromException<ClipboardContent>(ex) : Task.FromResult(Content);
        }

        /// <summary>Another program copies something.</summary>
        public void Copied(ClipboardContent content)
        {
            Content = content;
            ChangeCount++;
        }
    }

    /// <summary>20 × 10: the left half opaque red, the right half half-transparent blue.</summary>
    private static RasterImage Picture(int w = 20, int h = 10)
    {
        var img = new RasterImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img[x, y] = x < w / 2 ? Pixel.Opaque(new ColorRgb(220, 30, 30)) : Pixel.FromStraight(30, 60, 220, 128);
        return img;
    }

    private static ClipboardContent Png(RasterImage img) => new(PngCodec.Encode(img), null, [], true);

    private static (MainWindow W, FakeClipboard Clip) Open(WorkspaceKind workspace = WorkspaceKind.Painting)
    {
        var w = WindowTests.Open(1280, 800, workspace);
        var clip = new FakeClipboard();
        w.SystemClipboard = clip;
        return (w, clip);
    }

    [AvaloniaFact]
    public async Task Ctrl_V_pastes_a_picture_from_another_program_with_its_transparency_on_a_new_movable_layer()
    {
        var (w, clip) = Open();
        var doc = w.Session.Document;
        var active = doc.ActiveLayer;
        w.Session.SetLayerProperty(active, p => p with { Locked = true }); // the layer you were on is locked: still safe
        var before = active.Image.Pixels.ToArray();
        int layers = doc.Layers.Count, undo = doc.History.UndoCount;
        var picture = Picture();
        clip.Copied(Png(picture));

        Key(w, Avalonia.Input.Key.V, RawInputModifiers.Control);
        await ArtistsCompanionWindowTests.Until(() => doc.Layers.Count == layers + 1);
        Assert.Equal("Pasted image", doc.ActiveLayer.Name);
        Assert.NotNull(w.Session.ActiveTransform); // handles to move and scale it
        Assert.Contains("drag the handles", w.LastToast);
        w.Session.CommitTransform();

        var pasted = doc.ActiveLayer;
        int x0 = (doc.Width - 20) / 2, y0 = (doc.Height - 10) / 2;
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 20; x++)
                Assert.Equal(picture[x, y], pasted.Image[x0 + x, y0 + y]);
        Assert.Equal(0u, Pixel.A(pasted.Image[x0 - 1, y0]));
        Assert.Equal(before, active.Image.Pixels);
        Assert.True(active.Locked);
        // One undo step for the paste (plus one if it was moved).
        while (doc.History.UndoCount > undo + 1) w.Undo();
        w.Undo();
        Assert.Equal(layers, doc.Layers.Count);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_newest_copy_wins_DoodleNooks_own_or_another_programs()
    {
        var (w, clip) = Open();
        var doc = w.Session.Document;
        clip.Copied(Png(Picture()));
        w.Session.FillSelection(); // something to copy
        w.Copy(cut: false);        // DoodleNook's own copy is now the newest

        Assert.True(await w.PasteAsync(asReference: false));
        Assert.Equal("Pasted", doc.ActiveLayer.Name);
        Assert.Equal(0, clip.Reads);

        clip.Copied(Png(Picture())); // copied in another program afterwards
        Assert.True(await w.PasteAsync(asReference: false));
        Assert.Equal("Pasted image", doc.ActiveLayer.Name);
        w.Session.CommitTransform();

        clip.Copied(new ClipboardContent(null, null, [], HasOther: true)); // then text: DoodleNook's own picture is still there
        Assert.True(await w.PasteAsync(asReference: false));
        Assert.Equal("Pasted", doc.ActiveLayer.Name);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Empty_text_only_busy_failing_damaged_or_huge_clipboards_give_a_short_message()
    {
        var (w, clip) = Open();
        var doc = w.Session.Document;
        int layers = doc.Layers.Count;
        async Task Expect(string message)
        {
            Assert.False(await w.PasteAsync(asReference: false));
            Assert.Equal(message, w.LastToast);
            Assert.Equal(layers, doc.Layers.Count);
        }

        await Expect("The clipboard is empty. Copy a picture first.");
        clip.Copied(new ClipboardContent(null, null, [], HasOther: true));
        await Expect("The clipboard has no picture. Copy an image and try again.");
        clip.Copied(ClipboardContent.Failed("The clipboard is busy in another program. Try again in a moment."));
        await Expect("The clipboard is busy in another program. Try again in a moment.");
        clip.Throws = new UnauthorizedAccessException();
        clip.ChangeCount++;
        await Expect("The clipboard couldn't be read (UnauthorizedAccessException).");
        clip.Throws = null;
        clip.Copied(new ClipboardContent([137, 80, 78, 71, 1, 2, 3], null, [], true));
        await Expect("The picture on the clipboard couldn't be read.");
        // A PNG whose header says 40000 × 10 (refused before decoding).
        var huge = PngCodec.Encode(new RasterImage(4, 4));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(huge.AsSpan(16), 40_000);
        clip.Copied(new ClipboardContent(huge, null, [], true));
        await Expect("The picture on the clipboard is 40000 × 4 pixels, which is larger than DoodleNook supports.");
        string notes = Path.Combine(_temp, "notes.txt");
        await File.WriteAllTextAsync(notes, "hello");
        clip.Copied(new ClipboardContent(null, null, [notes], true));
        await Expect("“notes.txt” isn't a picture DoodleNook can paste.");
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_picture_file_copied_in_Explorer_and_a_Windows_bitmap_both_paste()
    {
        var (w, clip) = Open();
        var doc = w.Session.Document;
        string file = Path.Combine(_temp, "sticker.png");
        await File.WriteAllBytesAsync(file, PngCodec.Encode(Picture()));
        clip.Copied(new ClipboardContent(null, null, [file], true));
        Assert.True(await w.PasteAsync(asReference: false));
        w.Session.CommitTransform();
        int x0 = (doc.Width - 20) / 2, y0 = (doc.Height - 10) / 2;
        Assert.Equal(Picture()[15, 5], doc.ActiveLayer.Image[x0 + 15, y0 + 5]);

        // Paint copies a 32-bit bitmap whose alpha bytes are all zero: it pastes opaque.
        var dib = new byte[40 + 2 * 2 * 4];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(2).CopyTo(dib, 4);
        BitConverter.GetBytes(2).CopyTo(dib, 8);
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);
        for (int i = 0; i < 4; i++) new byte[] { 40, 120, 200, 0 }.CopyTo(dib, 40 + i * 4);
        clip.Copied(new ClipboardContent(null, dib, [], true));
        Assert.True(await w.PasteAsync(asReference: false));
        w.Session.CommitTransform();
        int bx = (doc.Width - 2) / 2, by = (doc.Height - 2) / 2;
        Assert.Equal(Pixel.Opaque(new ColorRgb(200, 120, 40)), doc.ActiveLayer.Image[bx, by]);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_picture_larger_than_the_canvas_is_scaled_to_fit()
    {
        var (w, clip) = Open();
        var doc = w.Session.Document;
        clip.Copied(Png(Picture(doc.Width * 2, doc.Height)));
        Assert.True(await w.PasteAsync(asReference: false));
        Assert.Contains("scaled to fit", w.LastToast);
        w.Session.CommitTransform();
        var bounds = doc.ActiveLayer.Image.ContentBounds();
        Assert.True(bounds.Width <= doc.Width && bounds.Width >= doc.Width - 1, bounds.ToString());
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public async Task Paste_as_reference_in_an_animation_adds_a_35_percent_layer_on_this_frame_that_locks_once_placed()
    {
        var (w, clip) = Open();
        var import = w.ImportAnimationFromAsync([ArtistsCompanionWindowTests.Fixture("walk-dispose2.gif")]);
        await ArtistsCompanionWindowTests.Until(() => w.Dialogs.IsOpen);
        w.Dialogs.Close("import");
        Assert.True(await import);
        var doc = w.Session.Document;
        var reference = doc.Layers[0];
        var referenceCels = Enumerable.Range(0, doc.FrameCount).Select(f => doc.CelAt(reference, f)).ToList();
        w.Session.GoToFrame(2);
        clip.Copied(Png(Picture()));

        w.CaptureRenderedFrame()?.Dispose();
        Menu(w, "Edit", "Paste as reference");
        await ArtistsCompanionWindowTests.Until(() => doc.Layers.Count == 5);
        var pasted = doc.ActiveLayer;
        Assert.Equal("Pasted reference", pasted.Name);
        Assert.Equal(AnimationImport.ReferenceOpacity, pasted.Opacity, 3);
        Assert.True(pasted.Visible);
        Assert.False(pasted.Locked); // still being positioned
        Assert.NotNull(w.Session.ActiveTransform);
        Assert.Contains("Enter locks it", w.LastToast);
        Key(w, Avalonia.Input.Key.Enter);
        Assert.Null(w.Session.ActiveTransform);
        Assert.True(pasted.Locked);
        // It sits just under the layer you draw on, and that layer is active again.
        Assert.Equal(["Reference", "Pasted reference", "Sketch", "Outline", "Coloring"], doc.Layers.Select(l => l.Name));
        Assert.Equal("Sketch", doc.ActiveLayer.Name);
        // This frame only; the imported Reference animation is untouched.
        Assert.NotNull(doc.CelAt(pasted, 2));
        Assert.Null(doc.CelAt(pasted, 1));
        Assert.Null(doc.CelAt(pasted, 3));
        for (int f = 0; f < doc.FrameCount; f++) Assert.Same(referenceCels[f], doc.CelAt(reference, f));
        Assert.True(reference.Locked);
        // Undo unlocks it first, then removes it.
        w.Undo();
        Assert.False(pasted.Locked);
        doc.MarkSaved();
        w.Close();
    }

    [AvaloniaFact]
    public void Windows_bitmaps_decode_like_Skia_reads_the_same_bitmap_as_a_bmp_file()
    {
        // Independent truth: a .bmp file is the clipboard bitmap behind a 14-byte file header; Skia reads that file.
        foreach (var (bits, row, table) in new (int, Func<int, byte[]>, uint[]?)[]
                 {
                     (24, y => Enumerable.Range(0, 5).SelectMany(x => new[] { (byte)(x * 40), (byte)(y * 60), (byte)(255 - x * 30) }).ToArray(), null),
                     (8, y => Enumerable.Range(0, 5).Select(x => (byte)((x + y) % 3)).ToArray(), [0x00C02020, 0x0020C020, 0x002020C0]),
                     (4, y => [(byte)(0x01 + y * 0x10), 0x20, 0x10], [0x00000000, 0x00FF8000, 0x000080FF]),
                 })
        {
            int stride = (5 * bits + 31) / 32 * 4;
            using var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            bw.Write(40); bw.Write(5); bw.Write(4); bw.Write((short)1); bw.Write((short)bits); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            bw.Write(table?.Length ?? 0); bw.Write(0);
            foreach (var c in table ?? []) bw.Write(c);
            for (int y = 3; y >= 0; y--)
            {
                var line = new byte[stride];
                row(y).CopyTo(line, 0);
                bw.Write(line);
            }
            var dib = ms.ToArray();
            var file = new byte[14 + dib.Length];
            file[0] = (byte)'B';
            file[1] = (byte)'M';
            BitConverter.GetBytes(file.Length).CopyTo(file, 2);
            BitConverter.GetBytes(14 + 40 + (table?.Length ?? 0) * 4).CopyTo(file, 10);
            dib.CopyTo(file, 14);
            var ours = DibDecoder.Decode(dib);
            var skia = ImageFiles.Decode(new MemoryStream(file));
            Assert.Equal(skia.Pixels, ours.Pixels);
        }
    }

    [AvaloniaFact]
    public void Explorers_file_list_is_read()
    {
        var paths = new[] { @"C:\Pictures\cat.png", @"D:\Ünïcode\fox.jpg" };
        var text = System.Text.Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        var drop = new byte[20 + text.Length];
        BitConverter.GetBytes(20).CopyTo(drop, 0);
        BitConverter.GetBytes(1).CopyTo(drop, 16);
        text.CopyTo(drop, 20);
        Assert.Equal(paths, ClipboardPictures.ParseFileDrop(drop));
        Assert.Empty(ClipboardPictures.ParseFileDrop(new byte[8]));
    }
}
