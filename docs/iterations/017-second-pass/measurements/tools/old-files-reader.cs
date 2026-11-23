using System.IO.Compression;
using System.Text.Json;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Library;
using DoodleNook.Engine.Storybook;
int problems = 0;
foreach (var dir in Directory.GetDirectories(args[0]).OrderBy(d => d))
{
    string m = Path.GetFileName(dir);
    foreach (var dnk in Directory.GetFiles(dir, "*.dnk").OrderBy(f => f))
    {
        string name = Path.GetFileNameWithoutExtension(dnk);
        int version;
        using (var zip = ZipFile.OpenRead(dnk)) using (var s = zip.GetEntry("document.json")!.Open()) version = JsonDocument.Parse(s).RootElement.GetProperty("version").GetInt32();
        try
        {
            var doc = DnkFile.Load(dnk);
            var renders = new List<(RasterImage Now, string Png)>();
            if (doc.IsAnimated) for (int f = 0; f < doc.FrameCount; f++) renders.Add((doc.RenderFrame(f, true), Path.Combine(dir, $"{name}.frame{f}.png")));
            else renders.Add((doc.Flatten(), Path.Combine(dir, $"{name}.flat.png")));
            int differing = 0, worst = 0;
            foreach (var (now, png) in renders)
            {
                var then = PngCodec.Decode(File.ReadAllBytes(png));
                if (then.Width != now.Width || then.Height != now.Height) { differing = -1; break; }
                for (int i = 0; i < now.Pixels.Length; i++)
                    if (now.Pixels[i] != then.Pixels[i])
                    {
                        differing++;
                        var a = Pixel.ToStraight(now.Pixels[i]); var b = Pixel.ToStraight(then.Pixels[i]);
                        worst = Math.Max(worst, Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A))));
                    }
            }
            if (differing != 0) problems++;
            bool lockedThen = name == "palette-locked";
            if (lockedThen && !(doc.PaletteLocked && doc.Palette.Colors.Count == 32)) { problems++; Console.WriteLine($"{m} {name}.dnk: PALETTE LOCK LOST"); }
            Console.WriteLine($"{m} {name}.dnk (format {version}): opens · {doc.Layers.Count} layers{(doc.IsAnimated ? $", {doc.FrameCount} frames" : "")}{(doc.PaletteLocked ? $", locked to \"{doc.Palette.Name}\" ({doc.Palette.Colors.Count} colours)" : "")} · {renders.Count} renders: {(differing == 0 ? "identical pixels" : $"{differing} pixels differ (largest {worst})")}");
        }
        catch (Exception ex) { problems++; Console.WriteLine($"{m} {name}.dnk (format {version}): FAILED {ex.GetType().Name}: {ex.Message}"); }
    }
    var ws = Path.Combine(dir, "workshop");
    if (Directory.Exists(ws))
    {
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "dn-oldlib-" + Guid.NewGuid().ToString("N"));
            var lib = new CreativeLibrary(root);
            var c = AssetPack.Import(lib, Directory.GetFiles(ws, "*.dnkpack")[0]);
            var board = ReferenceBoard.Load(Directory.GetFiles(ws, "*.dnkboard")[0]);
            var pal = PaletteFiles.ReadFile(Directory.GetFiles(ws, "*.gpl")[0]);
            Console.WriteLine($"{m} workshop: pack imports ({c}) · board opens ({board.Items.Count} pictures) · palette \"{pal.Name}\" {pal.Colors.Count} colours");
            Directory.Delete(root, true);
        }
        catch (Exception ex) { problems++; Console.WriteLine($"{m} workshop: FAILED {ex.GetType().Name}: {ex.Message}"); }
    }
    foreach (var book in Directory.GetFiles(dir, "*.donkbook", SearchOption.AllDirectories))
    {
        try
        {
            var sb = StorybookProject.Load(book);
            var links = sb.Pages.SelectMany(p => p.Panels).Where(p => p.Artwork is not null).Select(p => sb.Resolve(p.Artwork)).ToList();
            int ok = links.Count(l => l is not null && File.Exists(l) && DnkFile.Load(l) is not null);
            Console.WriteLine($"{m} storybook: opens · {sb.Pages.Count} pages · {ok}/{links.Count} linked drawings open");
            if (ok != links.Count) problems++;
        }
        catch (Exception ex) { problems++; Console.WriteLine($"{m} storybook: FAILED {ex.GetType().Name}: {ex.Message}"); }
    }
}
Console.WriteLine(problems == 0 ? "ALL OLD FILES OPEN WITH IDENTICAL PIXELS" : $"{problems} PROBLEMS");
