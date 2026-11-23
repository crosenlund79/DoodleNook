// Writes every export kind with DoodleNook's own code, plus the exact pixels each should hold (straight RGBA, raw),
// so an independent decoder (Pillow) can check them.
using DoodleNook.App.Files;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;
string inDir = args[0], outDir = args[1];
Directory.CreateDirectory(outDir);
var manifest = new List<string>();
void Ref(string name, RasterImage img) { File.WriteAllBytes(Path.Combine(outDir, name + ".rgba"), img.ToStraightRgba()); manifest.Add($"ref {name} {img.Width} {img.Height}"); }

// Stills: a layered design (opaque paper) and a transparent pixel-art frame.
var design = DnkFile.Load(Path.Combine(inDir, "design.dnk"));
Ref("design", design.Flatten());
foreach (var f in new[] { ExportFormat.Png, ExportFormat.Jpeg, ExportFormat.WebP })
    ImageFiles.Export(design, Path.Combine(outDir, "design." + f.ToString().ToLowerInvariant()), f);
Ref("design-matte", ImageFiles.OnMatte(design.Flatten(), ColorRgb.White));

var slime = DnkFile.Load(Path.Combine(inDir, "pixel-slime.dnk"));
var frames = AnimationExport.RenderFrames(slime, includePaper: false);
var paper = AnimationExport.RenderFrames(slime, includePaper: true);
for (int i = 0; i < frames.Count; i++) { Ref($"slime{i}", frames[i].Image); Ref($"slime-paper{i}", paper[i].Image); }
manifest.Add($"durations {string.Join(",", frames.Select(f => f.DurationMs))}");

// PNG sequence (transparent), as File › Export › PNG sequence writes it.
var seq = AnimationExport.SequencePaths(Path.Combine(outDir, "slime.png"), frames.Count);
AnimationExport.WriteSequence(frames, seq);
manifest.Add($"sequence {string.Join(",", seq.Select(Path.GetFileName))}");

// Sprite sheet + JSON (grid with spacing/padding/margin to make the rectangles non-trivial).
var sheet = AnimationExport.BuildSheet(frames, new SpriteSheetOptions(SheetLayout.Grid, Columns: 3, Spacing: 2, Padding: 1, Margin: 3));
AnimationExport.WriteAtomically(Path.Combine(outDir, "slime-sheet.png"), PngCodec.Encode(sheet.Image));
File.WriteAllText(Path.Combine(outDir, "slime-sheet.json"), AnimationExport.SheetJson(sheet, "slime-sheet.png", slime.Animation!.Fps, true));

// Animated GIFs: pixel art (exact colours, on paper) and the bouncing ball (many colours, transparent).
GifEncoder.Save(paper, Path.Combine(outDir, "slime.gif"), new GifOptions(Loop: true));
var ball = DnkFile.Load(Path.Combine(inDir, "bouncing-ball.dnk"));
var ballFrames = AnimationExport.RenderFrames(ball, includePaper: true);
for (int i = 0; i < ballFrames.Count; i++) Ref($"ball{i}", ballFrames[i].Image);
manifest.Add($"balldurations {string.Join(",", ballFrames.Select(f => f.DurationMs))}");
GifEncoder.Save(ballFrames, Path.Combine(outDir, "ball.gif"), new GifOptions(Loop: false));
File.WriteAllLines(Path.Combine(outDir, "manifest.txt"), manifest);
Console.WriteLine($"wrote {Directory.GetFiles(outDir).Length} files; sheet {sheet.Image.Width}x{sheet.Image.Height} {sheet.Columns}x{sheet.Rows}");
