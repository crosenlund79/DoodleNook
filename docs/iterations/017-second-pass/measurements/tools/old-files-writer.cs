// Writes .dnk (and other) files with an OLD mission's own engine, plus that engine's own render of each picture/frame.
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
#if ANIM
using DoodleNook.Engine.Samples;
#endif
#if DESIGN
using DoodleNook.Engine.Operations;
#endif
#if REF
using DoodleNook.Engine.References;
#endif
string outDir = args[0];
Directory.CreateDirectory(outDir);
void Write(Document doc, string name)
{
    DnkFile.Save(doc, Path.Combine(outDir, name + ".dnk"));
#if ANIM
    if (doc.IsAnimated)
    {
        for (int f = 0; f < doc.FrameCount; f++) File.WriteAllBytes(Path.Combine(outDir, $"{name}.frame{f}.png"), PngCodec.Encode(doc.RenderFrame(f, true)));
        return;
    }
#endif
    File.WriteAllBytes(Path.Combine(outDir, $"{name}.flat.png"), PngCodec.Encode(doc.Flatten()));
}
// Format 1: two layers, soft alpha.
var still = new Document(320, 200, ColorRgb.White, "Background");
var art = new RasterImage(320, 200);
for (int y = 0; y < 200; y++) for (int x = 0; x < 320; x++)
    if ((x - 160) * (x - 160) + (y - 100) * (y - 100) < 70 * 70) art[x, y] = Pixel.FromStraight((byte)(x % 256), 120, (byte)(y + 40), (byte)(255 - x / 2));
still.AddLayer(art, "Ink");
Write(still, "still");
#if ANIM
Write(SampleAnimations.BouncingBall(), "bouncing-ball");
Write(SampleAnimations.PixelSlime(), "pixel-slime");
#endif
#if WORKSHOP
SampleWorkshop.WriteAll(Path.Combine(outDir, "workshop"));
// Format 3: a picture locked to a palette (Palette Lock, Mission 007's addendum).
var locked = new Document(96, 64, ColorRgb.White, "Background");
var dots = new RasterImage(96, 64);
for (int y = 0; y < 64; y++) for (int x = 0; x < 96; x++) dots[x, y] = Pixel.FromStraight((byte)(x * 2), (byte)(y * 3), (byte)(200 - x), 255);
locked.AddLayer(dots, "Pixels");
locked.LockToPalette(Palette.Default());
Write(locked, "palette-locked");
#endif
#if DESIGN
var design = new Document(320, 200, ColorRgb.White, "Background");
var a = design.AddShapeLayer(new ShapeContent(ShapeType.Rectangle, 20, 20, 140, 90, new ColorRgb(230, 120, 60), new ColorRgb(40, 40, 60)));
var b = design.AddShapeLayer(new ShapeContent(ShapeType.Ellipse, 120, 60, 150, 110, new ColorRgb(80, 160, 210)));
design.GroupLayers([a, b], "Shapes");
var painted = design.AddLayer(art.Clone(), "Painted");
design.AddMask(painted);
design.AddAdjustmentLayer(new BrightnessContrast(20, 10));
Write(design, "design");
#endif
#if REF
var withRef = SampleAnimations.BouncingBall();
ReferenceInsert.Insert(withRef, new ReferenceCycleSettings("walk", ReferenceView.Side, ReferenceFacing.Right, 150, 320, 360));
Write(withRef, "with-reference");
#endif
#if STORY
DoodleNook.Engine.Samples.SampleStorybook.Create(Path.Combine(outDir, "storybook"), 150);
#endif
Console.WriteLine("written " + string.Join(", ", Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".png")).Select(Path.GetFileName)));
