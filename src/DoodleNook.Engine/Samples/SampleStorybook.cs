using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.References;
using DoodleNook.Engine.Storybook;

namespace DoodleNook.Engine.Samples;

/// <summary>
/// Mission 015 — "A little walk", a two-page sample storybook with linked, editable drawings. Everything is drawn by code
/// (hills, a sky, the Reference Animation Library's mannequin): DoodleNook's own artwork, free of outside licences. Each
/// drawing is a normal two-layer .dnk (Background, Figure); one of them appears in two panels, to show that editing a
/// shared drawing updates every panel that shows it.
/// </summary>
public static class SampleStorybook
{
    public const string Name = "A little walk";

    /// <summary>Creates the sample in <paramref name="folder"/> (an existing one is opened as it is, never overwritten).
    /// Returns the storybook's path.</summary>
    public static string Create(string folder, int dpi = 300)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Name + StorybookProject.Extension);
        if (File.Exists(path)) return path;
        var book = StorybookProject.Create(210, 297, dpi, StoryLayouts.Comic);
        book.FilePath = path;
        book.AddPage(StoryLayouts.Cinematic);

        // Page 1: the hill (wide), off we go, what was that?, and the hill again (the same drawing).
        var p1 = book.Pages[0];
        var r1 = book.PanelRects(p1);
        Link(book, p1, 0, "the hill", Hill(book.SuggestedCanvas(r1[0]), dusk: false), "Morning over the hill.");
        Link(book, p1, 1, "off we go", Figure(book.SuggestedCanvas(r1[1]), "walk", 2, Sky), "Off we go.");
        Link(book, p1, 2, "what was that", Figure(book.SuggestedCanvas(r1[2]), "startle", 2, Sky, bush: true), "What was that?");
        p1.Panels[3].Artwork = p1.Panels[0].Artwork; // shared: edit "the hill" and both panels change
        p1.Panels[3].Fit = PanelFit.Fill;
        p1.Panels[3].Note = "The same hill: one drawing, two panels.";

        // Page 2: sunset (wide), turning back, tired but happy.
        var p2 = book.Pages[1];
        var r2 = book.PanelRects(p2);
        Link(book, p2, 0, "sunset", Hill(book.SuggestedCanvas(r2[0]), dusk: true), "Evening already.");
        Link(book, p2, 1, "turning back", Figure(book.SuggestedCanvas(r2[1]), "turn-180", 6, Dusk), "Time to go home.");
        Link(book, p2, 2, "tired", Figure(book.SuggestedCanvas(r2[2]), "exhausted", 1, Dusk), "Tired, but happy.");
        book.Save();
        return path;
    }

    private static void Link(StorybookProject book, StoryPage page, int slot, string name, Document art, string note)
    {
        var file = book.NewArtworkPath(name);
        DnkFile.Save(art, file);
        page.Panels[slot].Artwork = book.Relate(file);
        page.Panels[slot].Note = note;
    }

    private static readonly ColorRgb Sky = new(0xBF, 0xDD, 0xEC), Dusk = new(0xF2, 0xB4, 0x8C);

    /// <summary>A sky, a sun, two rolling hills and a path (dusk: warmer, lower sun).</summary>
    private static Document Hill((int W, int H) size, bool dusk)
    {
        var (w, h) = size;
        var bg = new RasterImage(w, h);
        var top = dusk ? new ColorRgb(0x6B, 0x5C, 0x9E) : new ColorRgb(0x8F, 0xC6, 0xE6);
        var low = dusk ? new ColorRgb(0xF6, 0xB8, 0x8A) : new ColorRgb(0xE4, 0xF1, 0xF2);
        for (int y = 0; y < h; y++)
        {
            double t = (double)y / h;
            bg.Row(y).Fill(Pixel.Opaque(Mix(top, low, t)));
        }
        Disc(bg, w * (dusk ? 0.72 : 0.78), h * (dusk ? 0.52 : 0.24), h * 0.11, dusk ? new ColorRgb(0xFF, 0xD9, 0x8A) : new ColorRgb(0xFF, 0xF2, 0xB8));
        Hillside(bg, 0.62, 0.18, 1.3, dusk ? new ColorRgb(0x5E, 0x6E, 0x5C) : new ColorRgb(0x8C, 0xB8, 0x6A));
        Hillside(bg, 0.78, 0.12, 2.1, dusk ? new ColorRgb(0x46, 0x56, 0x44) : new ColorRgb(0x6A, 0x9E, 0x55));
        // A path winding up the near hill.
        for (int y = (int)(h * 0.72); y < h; y++)
        {
            double t = (y - h * 0.72) / (h * 0.28);
            double cx = w * (0.42 + 0.08 * Math.Sin(t * 3.2)), half = w * (0.008 + 0.05 * t);
            for (int x = (int)(cx - half); x <= (int)(cx + half); x++)
                if ((uint)x < (uint)w) bg[x, y] = Pixel.Opaque(dusk ? new ColorRgb(0xB8, 0x92, 0x6E) : new ColorRgb(0xE2, 0xCC, 0x9A));
        }
        return new Document(w, h, ColorRgb.White, true, [(new LayerProperties("Background"), bg), (new LayerProperties("Figure"), new RasterImage(w, h))], 1);
    }

    /// <summary>The mannequin from the reference library, on a plain sky and ground (two layers).</summary>
    private static Document Figure((int W, int H) size, string cycleId, int drawing, ColorRgb sky, bool bush = false)
    {
        var (w, h) = size;
        var bg = new RasterImage(w, h);
        int ground = (int)(h * 0.82);
        for (int y = 0; y < h; y++)
            bg.Row(y).Fill(Pixel.Opaque(y < ground ? Mix(sky, ColorRgb.White, 0.35 * y / ground) : new ColorRgb(0x7E, 0xA8, 0x5E)));
        if (bush)
        {
            Disc(bg, w * 0.18, ground - h * 0.06, h * 0.13, new ColorRgb(0x4E, 0x80, 0x48));
            Disc(bg, w * 0.27, ground - h * 0.04, h * 0.1, new ColorRgb(0x5A, 0x8E, 0x50));
        }
        var cycle = ReferenceLibrary.Find(cycleId) ?? ReferenceLibrary.Cycles[0];
        var framing = new ReferenceFraming(ReferenceView.Side, ReferenceFacing.Right, h * 0.66, w * 0.55, ground + h * 0.02, HardEdges: false, Marker: false);
        var figure = ReferenceRenderer.Render(cycle.Pose(Math.Clamp(drawing, 0, cycle.Frames - 1)), w, h, framing);
        return new Document(w, h, ColorRgb.White, true, [(new LayerProperties("Background"), bg), (new LayerProperties("Figure"), figure)], 1);
    }

    private static ColorRgb Mix(ColorRgb a, ColorRgb b, double t) =>
        new((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static void Disc(RasterImage img, double cx, double cy, double r, ColorRgb c)
    {
        uint p = Pixel.Opaque(c);
        for (int y = Math.Max(0, (int)(cy - r)); y <= Math.Min(img.Height - 1, (int)(cy + r)); y++)
            for (int x = Math.Max(0, (int)(cx - r)); x <= Math.Min(img.Width - 1, (int)(cx + r)); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) img[x, y] = p;
    }

    /// <summary>A hill whose top is at <paramref name="level"/> of the height, rising and falling gently.</summary>
    private static void Hillside(RasterImage img, double level, double swell, double waves, ColorRgb c)
    {
        uint p = Pixel.Opaque(c);
        for (int x = 0; x < img.Width; x++)
        {
            double t = (double)x / img.Width;
            int top = (int)(img.Height * (level - swell * 0.5 * (1 + Math.Sin(t * Math.PI * waves + 0.6))));
            for (int y = Math.Max(0, top); y < img.Height; y++) img[x, y] = p;
        }
    }
}
