using DoodleNook.Engine.Bones;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Samples;

/// <summary>
/// Mission 019's sample skeletal animation, "Wave hello": a simple mannequin whose body parts are ordinary DoodleNook
/// drawings in an art folder. The torso, arms and legs are still sprites; the head has two animated sprites (a looping
/// Idle and a one-shot Blink) and the right hand a still Hand and a looping Wave. On the master timeline the body
/// sways, the head blinks and returns to Idle, the right arm lifts while the hand waves, then lowers again, and it all
/// loops. Everything is drawn here, so the sample needs no files of its own.
/// </summary>
public static class SampleSkeleton
{
    public const string Name = "Wave hello";

    private static readonly ColorRgb Ink = new(58, 46, 42);
    private static readonly ColorRgb Skin = new(238, 196, 160);
    private static readonly ColorRgb Shirt = new(84, 140, 176);
    private static readonly ColorRgb ShirtBack = new(64, 112, 146);
    private static readonly ColorRgb Trousers = new(96, 84, 128);

    /// <summary>Writes the sample (art folder and project) into <paramref name="folder"/> and returns the project's path.</summary>
    public static string Create(string folder)
    {
        Directory.CreateDirectory(folder);
        var art = Path.Combine(folder, "art");
        Directory.CreateDirectory(art);
        string Save(Document doc, string name)
        {
            var path = Path.Combine(art, name + DnkFile.Extension);
            DnkFile.Save(doc, path);
            return path;
        }

        var torso = Save(Document.FromImage(Torso(), "Torso"), "torso");
        var armFront = Save(Document.FromImage(Limb(26, 96, Shirt), "Arm"), "arm-front");
        var armBack = Save(Document.FromImage(Limb(26, 96, ShirtBack), "Arm"), "arm-back");
        var legFront = Save(Document.FromImage(Limb(32, 124, Trousers), "Leg"), "leg-front");
        var legBack = Save(Document.FromImage(Limb(32, 124, Darker(Trousers)), "Leg"), "leg-back");
        var hand = Save(Document.FromImage(Hand(0), "Hand"), "hand");
        var headIdle = Save(Files.SpriteSheetImport.NormalDocument([Head(look: -2, blink: 0), Head(look: 2, blink: 0)], 2), "head-idle");
        var headBlink = Save(Files.SpriteSheetImport.NormalDocument([Head(0, 0.5), Head(0, 1), Head(0, 0.5), Head(0, 0)], 12), "head-blink");
        var wave = Save(Files.SpriteSheetImport.NormalDocument([Hand(-18), Hand(0), Hand(18), Hand(0)], 8), "hand-wave");

        var p = new SkeletonProject { Width = 360, Height = 440, Fps = 12, Length = 48, Loop = true };
        p.PlaceAt(Path.Combine(folder, Name + SkeletonProject.Extension));

        // The skeleton: hips at the root, pointing up; legs hang from the hips, the body from them.
        var hips = p.AddBone("Hips", null, 180, 262, rotation: -90, length: 16);
        var body = p.AddBone("Body", hips, 16, 0, length: 104);
        var head = p.AddBone("Head", body, 104, 0, length: 76);
        var armL = p.AddBone("Left arm", body, 92, -40, rotation: -168, length: 88);
        var handL = p.AddBone("Left hand", armL, 88, 0, length: 26);
        var armR = p.AddBone("Right arm", body, 92, 40, rotation: 168, length: 88);
        var handR = p.AddBone("Right hand", armR, 88, 0, length: 26);
        var legL = p.AddBone("Left leg", hips, 0, -22, rotation: -176, length: 124);
        var legR = p.AddBone("Right leg", hips, 0, 22, rotation: 176, length: 124);

        // Sprites: drawn upright, so each is turned to lie along its bone (torso, head and hands point away from their joint
        // with their top, arms and legs with their bottom).
        Attach(p, body, torso, "Torso", 44, 0, 90);
        Attach(p, head, headIdle, "Idle", 38, 0, 90);
        Attach(p, head, headBlink, "Blink", 38, 0, 90).Loop = false;
        Attach(p, armL, armBack, "Arm", 44, 0, -90);
        Attach(p, armR, armFront, "Arm", 44, 0, -90);
        Attach(p, handL, hand, "Hand", 12, 0, 90);
        Attach(p, handR, hand, "Hand", 12, 0, 90);
        Attach(p, handR, wave, "Wave", 12, 0, 90);
        Attach(p, legL, legBack, "Leg", 56, 0, -90);
        Attach(p, legR, legFront, "Leg", 56, 0, -90);
        // Back to front: the far arm and leg behind the body, the near ones in front, the head on top.
        p.DrawOrder = [legL.Id, armL.Id, handL.Id, legR.Id, hips.Id, body.Id, head.Id, armR.Id, handR.Id];

        // The master timeline, 48 frames at 12 fps (frames 1–48 to the user are 0–47 here): standing (idle), a blink,
        // the right arm lifts and the hand waves, the arm lowers back to idle, then a slight body sway, and round again.
        p.SetSwitch(head, 10, head.Attachments[1]); // blink (plays once, then holds its last frame)…
        p.SetSwitch(head, 16, head.Attachments[0]); // …and back to idle
        Key(p, armR, 16, 168, smooth: true);
        Key(p, armR, 22, 40);
        Key(p, armR, 34, 40, smooth: true);
        Key(p, armR, 40, 168);
        p.SetSwitch(handR, 22, handR.Attachments[1]); // wave while the arm is up
        p.SetSwitch(handR, 34, handR.Attachments[0]);
        Key(p, body, 38, 0, smooth: true);
        Key(p, body, 43, 4, smooth: true);
        Key(p, body, 47, 0);
        p.Save();
        return p.FilePath!;
    }

    private static BoneAttachment Attach(SkeletonProject p, Bone bone, string file, string name, double x, double y, double rotation)
    {
        var a = p.AddAttachment(bone, file, name);
        a.X = x;
        a.Y = y;
        a.Rotation = rotation;
        return a;
    }

    private static void Key(SkeletonProject p, Bone bone, int frame, double rotation, bool smooth = false) =>
        p.SetPoseKey(bone, frame, bone.X, bone.Y, rotation, bone.ScaleX, bone.ScaleY, smooth);

    // ================================================================== drawing

    private static RasterImage Torso()
    {
        var img = new RasterImage(96, 124);
        Shape(img, (x, y) => RoundedRect(x, y, 48, 62, 40, 56, 22), Shirt);
        Shape(img, (x, y) => RoundedRect(x, y, 48, 104, 34, 12, 8), Darker(Shirt)); // belt line
        return img;
    }

    /// <summary>An upright limb (shoulder or hip at the top).</summary>
    private static RasterImage Limb(int width, int height, ColorRgb colour)
    {
        var img = new RasterImage(width + 8, height + 8);
        double r = width / 2.0;
        Shape(img, (x, y) => Capsule(x, y, (width + 8) / 2.0, 4 + r, (width + 8) / 2.0, height + 4 - r, r), colour);
        return img;
    }

    /// <summary>An open hand, fingers up, turned by <paramref name="tilt"/> degrees (the wave).</summary>
    private static RasterImage Hand(double tilt)
    {
        var img = new RasterImage(40, 40);
        double t = tilt * Math.PI / 180;
        (double, double) Turn(double x, double y)
        {
            double dx = x - 20, dy = y - 26;
            return (20 + dx * Math.Cos(t) - dy * Math.Sin(t), 26 + dx * Math.Sin(t) + dy * Math.Cos(t));
        }
        Shape(img, (x, y) =>
        {
            double d = Circle(x, y, 20, 27, 10);
            for (int i = 0; i < 4; i++)
            {
                var (ax, ay) = Turn(13 + i * 4.7, 22);
                var (bx, by) = Turn(10.5 + i * 6.3, 7.5 + Math.Abs(i - 1.5) * 2.2);
                d = Math.Min(d, Capsule(x, y, ax, ay, bx, by, 3.3));
            }
            return d;
        }, Skin);
        return img;
    }

    /// <summary>A round head looking left/right a little; <paramref name="blink"/> 0 = eyes open, 1 = closed.</summary>
    private static RasterImage Head(double look, double blink)
    {
        var img = new RasterImage(96, 100);
        Shape(img, (x, y) => Circle(x, y, 48, 50, 40), Skin);
        Shape(img, (x, y) => Math.Max(Circle(x, y, 48, 42, 42), y - 30), new ColorRgb(110, 72, 52)); // hair
        foreach (double ex in new[] { 34.0, 62.0 })
        {
            double open = Math.Max(0.12, 1 - blink);
            Shape(img, (x, y) => Ellipse(x, y, ex + look, 52, 4.2, 5.5 * open), Ink);
        }
        Shape(img, (x, y) => Math.Max(Math.Abs(Circle(x, y, 48, 60, 13)) - 1.6, 62 - y), Ink); // smile
        return img;
    }

    private static ColorRgb Darker(ColorRgb c) => new((byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8));

    /// <summary>Fills where <paramref name="distance"/> is inside (negative), anti-aliased, with a dark outline.</summary>
    private static void Shape(RasterImage img, Func<double, double, double> distance, ColorRgb fill)
    {
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                double d = distance(x + 0.5, y + 0.5);
                if (d > 1) continue;
                double coverage = Math.Clamp(0.5 - d, 0, 1);
                double inner = Math.Clamp(-1.5 - d, 0, 1); // inside the 2 px outline
                byte R(byte f, byte o) => (byte)Math.Round(o + (f - o) * inner);
                var px = Pixel.FromStraight(R(fill.R, Ink.R), R(fill.G, Ink.G), R(fill.B, Ink.B), (byte)Math.Round(255 * coverage));
                img.Pixels[y * img.Width + x] = Pixel.Over(px, img.Pixels[y * img.Width + x]);
            }
    }

    private static double Circle(double x, double y, double cx, double cy, double r) => Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

    private static double Ellipse(double x, double y, double cx, double cy, double rx, double ry) =>
        (Math.Sqrt((x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry)) - 1) * Math.Min(rx, ry);

    private static double Capsule(double x, double y, double ax, double ay, double bx, double by, double r)
    {
        double px = x - ax, py = y - ay, vx = bx - ax, vy = by - ay;
        double h = Math.Clamp((px * vx + py * vy) / Math.Max(1e-9, vx * vx + vy * vy), 0, 1);
        double dx = px - vx * h, dy = py - vy * h;
        return Math.Sqrt(dx * dx + dy * dy) - r;
    }

    private static double RoundedRect(double x, double y, double cx, double cy, double hw, double hh, double r)
    {
        double qx = Math.Abs(x - cx) - hw + r, qy = Math.Abs(y - cy) - hh + r;
        return Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - r;
    }
}
