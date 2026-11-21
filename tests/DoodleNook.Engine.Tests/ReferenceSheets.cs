using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.References;

namespace DoodleNook.Engine.Tests;

/// <summary>Mission 013: contact sheets of every reference cycle in every view (out/references/*.png), for looking at the
/// motion by eye.</summary>
public sealed class ReferenceSheets
{
    internal static string OutDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DoodleNook.slnx"))) dir = dir.Parent;
            var path = Path.Combine(dir!.FullName, "tests", "DoodleNook.Engine.Tests", "out", "references");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    [Fact]
    public void Write_close_ups()
    {
        foreach (var (id, view) in new[] { ("walk", ReferenceView.TopDown), ("walk", ReferenceView.Side), ("shoot", ReferenceView.Side), ("dig", ReferenceView.Side), ("hammer", ReferenceView.Side), ("walk", ReferenceView.ThreeQuarter) })
        {
            var c = ReferenceLibrary.Find(id)!;
            const int cell = 300;
            var sheet = new RasterImage(c.Frames * cell, cell);
            sheet.Fill(Pixel.Opaque(new ColorRgb(250, 250, 247)));
            for (int f = 0; f < c.Frames; f++)
                ReferenceRenderer.Draw(sheet, c.Pose(f), new ReferenceFraming(view, ReferenceRenderer.DefaultFacing(view), 220, f * cell + cell / 2.0,
                    view == ReferenceView.TopDown ? cell / 2.0 : cell * 0.85));
            File.WriteAllBytes(Path.Combine(OutDir, $"close-{id}-{view}.png"), PngCodec.Encode(sheet));
        }
    }

    [Fact]
    public void Write_contact_sheets()
    {
        const int cell = 150, h = 100;
        foreach (var view in ReferenceCycle.AllViews)
        {
            var cycles = ReferenceLibrary.Cycles.Where(c => c.Supports(view)).ToList();
            int cols = cycles.Max(c => c.Frames);
            var sheet = new RasterImage(cols * cell, cycles.Count * cell);
            sheet.Fill(Pixel.Opaque(new ColorRgb(250, 250, 247)));
            for (int r = 0; r < cycles.Count; r++)
                for (int f = 0; f < cycles[r].Frames; f++)
                {
                    var framing = new ReferenceFraming(view, ReferenceRenderer.DefaultFacing(view), h, f * cell + cell / 2.0,
                        r * cell + (view == ReferenceView.TopDown ? cell / 2.0 : cell * 0.82));
                    ReferenceRenderer.Draw(sheet, cycles[r].Pose(f), framing);
                }
            File.WriteAllBytes(Path.Combine(OutDir, $"sheet-{view}.png"), PngCodec.Encode(sheet));
        }
    }
}
