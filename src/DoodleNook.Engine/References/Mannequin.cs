using System.Numerics;

namespace DoodleNook.Engine.References;

/// <summary>Something the mannequin holds or uses, drawn with it so the action reads clearly.</summary>
public enum PropKind
{
    None,
    Sword,
    Hammer,
    Shovel,
    Gun,
    Orb,
    Box,
    Crate,
    Stool,
    Ladder,
    /// <summary>A heavy box carried in both hands (bigger than <see cref="Box"/>).</summary>
    Load,
    /// <summary>A rope held in both hands, running forward from the front hand.</summary>
    Rope,
}

/// <summary>
/// One arm or leg, in degrees. <see cref="Swing"/> moves it forward (+) or back (−) from hanging straight down;
/// <see cref="Spread"/> lifts it out to the side; <see cref="Bend"/> bends the elbow (forearm folds forward) or the
/// knee (shin folds back); <see cref="Twist"/> turns a bent forearm in towards the body.
/// </summary>
public readonly record struct Limb(double Swing = 0, double Spread = 0, double Bend = 0, double Twist = 0)
{
    public static Limb Lerp(Limb a, Limb b, double t) =>
        new(a.Swing + (b.Swing - a.Swing) * t, a.Spread + (b.Spread - a.Spread) * t, a.Bend + (b.Bend - a.Bend) * t, a.Twist + (b.Twist - a.Twist) * t);
}

/// <summary>
/// A mannequin pose in degrees (and body heights for distances). Unset values give a relaxed standing pose. Unless
/// <see cref="Airborne"/>, the lowest part of the body rests on the ground, so walking and crouching keep the feet down
/// by themselves; airborne poses are lifted by <see cref="Rise"/> instead.
/// </summary>
public sealed record Pose
{
    public Limb ArmL { get; init; } = new(0, 7, 12);
    public Limb ArmR { get; init; } = new(0, 7, 12);
    public Limb LegL { get; init; } = new(0, 3, 4);
    public Limb LegR { get; init; } = new(0, 3, 4);

    /// <summary>Upper body leaning forward (+) or back (−).</summary>
    public double Lean { get; init; }
    /// <summary>Upper body turned to its right (+) or left (−).</summary>
    public double Twist { get; init; }
    /// <summary>Upper body tilted to its right (+) or left (−).</summary>
    public double Tilt { get; init; }
    public double HeadPitch { get; init; }
    public double HeadYaw { get; init; }

    /// <summary>The whole body turned about the vertical (+ to its right), around the anchor: for turning on the spot.</summary>
    public double Turn { get; init; }
    /// <summary>The whole body tipped forward (+) or back (−) around the feet (falling over).</summary>
    public double BodyPitch { get; init; }
    /// <summary>The whole body tipped to its right (+) or left (−).</summary>
    public double BodyRoll { get; init; }
    /// <summary>The body moved forward (+) or back (−) and to its right (+), in body heights.</summary>
    public double Forward { get; init; }
    public double Sideways { get; init; }

    /// <summary>Toes pointed down (plantar flexion), per foot.</summary>
    public double ToeL { get; init; }
    public double ToeR { get; init; }

    /// <summary>Off the ground: <see cref="Rise"/> lifts the standing height by that many body heights.</summary>
    public bool Airborne { get; init; }
    public double Rise { get; init; }

    public PropKind Prop { get; init; }
    /// <summary>The held prop's angle against the forearm (sword, hammer).</summary>
    public double PropAngle { get; init; }
    /// <summary>0–1: a box still on the ground (0) or held (1); an orb's size.</summary>
    public double PropAmount { get; init; } = 1;

    public static Pose Lerp(Pose a, Pose b, double t)
    {
        if (t <= 0) return a;
        if (t >= 1) return b;
        double L(double x, double y) => x + (y - x) * t;
        return new Pose
        {
            ArmL = Limb.Lerp(a.ArmL, b.ArmL, t),
            ArmR = Limb.Lerp(a.ArmR, b.ArmR, t),
            LegL = Limb.Lerp(a.LegL, b.LegL, t),
            LegR = Limb.Lerp(a.LegR, b.LegR, t),
            Lean = L(a.Lean, b.Lean),
            Twist = L(a.Twist, b.Twist),
            Tilt = L(a.Tilt, b.Tilt),
            HeadPitch = L(a.HeadPitch, b.HeadPitch),
            HeadYaw = L(a.HeadYaw, b.HeadYaw),
            Turn = L(a.Turn, b.Turn),
            BodyPitch = L(a.BodyPitch, b.BodyPitch),
            BodyRoll = L(a.BodyRoll, b.BodyRoll),
            Forward = L(a.Forward, b.Forward),
            Sideways = L(a.Sideways, b.Sideways),
            ToeL = L(a.ToeL, b.ToeL),
            ToeR = L(a.ToeR, b.ToeR),
            Airborne = t < 0.5 ? a.Airborne : b.Airborne,
            Rise = L(a.Rise, b.Rise),
            Prop = a.Prop == PropKind.None ? b.Prop : a.Prop,
            PropAngle = L(a.PropAngle, b.PropAngle),
            PropAmount = L(a.PropAmount, b.PropAmount),
        };
    }
}

/// <summary>What a part of the mannequin is painted with.</summary>
public enum PartPaint
{
    Body,
    Left,
    Right,
    Face,
    Wood,
    Metal,
    Glow,
    Shadow,
}

/// <summary>A drawable piece in body space (y up, z forward, x to the body's right), in body heights: a capsule from
/// <see cref="A"/> to <see cref="B"/>, a ball, a box or a ground shadow.</summary>
public readonly record struct Part(PartKind Kind, Vector3 A, Vector3 B, float RadiusA, float RadiusB, PartPaint Paint);

public enum PartKind
{
    Capsule,
    Ball,
    Box,
    Shadow,
}

/// <summary>
/// Mission 013: the reference mannequin. A simple jointed figure about six heads tall (a ball head, a torso, upper and
/// lower arms and legs, hands and feet) posed by <see cref="Pose"/> and turned into drawable parts. Right limbs are
/// painted warm and left limbs cool so the artist can always tell which arm or leg is which. One pose gives every view.
/// </summary>
public static class Mannequin
{
    // Proportions in body heights (standing height = 1).
    private const float PelvisHeight = 0.52f, Spine = 0.27f, Neck = 0.055f, HeadRadius = 0.085f;
    private const float HipHalf = 0.08f, ShoulderHalf = 0.13f;
    private const float Thigh = 0.245f, Shin = 0.235f, Foot = 0.085f, UpperArm = 0.17f, Forearm = 0.15f;

    private static Vector3 X => Vector3.UnitX;
    private static Vector3 Y => Vector3.UnitY;
    private static Vector3 Z => Vector3.UnitZ;
    private static Vector3 Down => -Vector3.UnitY;

    private static Vector3 Rot(Vector3 v, Vector3 axis, double degrees) =>
        Vector3.Transform(v, Quaternion.CreateFromAxisAngle(axis, (float)(degrees * Math.PI / 180)));

    /// <summary>A frame of reference: three axes (right, up, forward).</summary>
    private readonly record struct Frame(Vector3 R, Vector3 U, Vector3 F)
    {
        public static Frame Identity => new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        public Vector3 Apply(Vector3 v) => R * v.X + U * v.Y + F * v.Z;
        public Frame Rotate(Vector3 localAxis, double degrees)
        {
            var axis = Vector3.Normalize(Apply(localAxis));
            return new Frame(Rot(R, axis, degrees), Rot(U, axis, degrees), Rot(F, axis, degrees));
        }
    }

    private static float? _standingOffset;

    /// <summary>How far the relaxed standing pose must be lifted for its feet to touch the ground.</summary>
    private static float StandingOffset => _standingOffset ??= ComputeStandingOffset();

    private static float ComputeStandingOffset()
    {
        var parts = Raw(new Pose());
        return -parts.Where(p => IsBody(p.Paint)).Min(p => Math.Min(p.A.Y - p.RadiusA, p.B.Y - p.RadiusB));
    }

    private static bool IsBody(PartPaint p) => p is PartPaint.Body or PartPaint.Left or PartPaint.Right or PartPaint.Face;

    /// <summary>The pose's parts, resting on the ground (y = 0) under the anchor (x = z = 0) unless airborne.</summary>
    public static List<Part> Build(Pose pose)
    {
        var parts = Raw(pose);
        float lift = pose.Airborne
            ? StandingOffset + (float)pose.Rise
            : -parts.Where(p => IsBody(p.Paint)).Min(p => Math.Min(p.A.Y - p.RadiusA, p.B.Y - p.RadiusB));
        var up = new Vector3(0, lift, 0);
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (p.Kind == PartKind.Shadow || p.Paint == PartPaint.Wood && pose.Prop == PropKind.Ladder) continue;
            if (pose.Prop is PropKind.Box && p.Kind == PartKind.Box && pose.PropAmount < 0.5) continue; // still on the ground
            if (pose.Prop is PropKind.Stool or PropKind.Crate && p.Kind == PartKind.Box) continue;      // stands on the ground
            parts[i] = p with { A = p.A + up, B = p.B + up };
        }
        // The shadow sits on the ground under the pelvis, smaller the higher the body is.
        var pelvis = parts.First(p => p.Paint == PartPaint.Body);
        float height = Math.Max(0, pelvis.A.Y - PelvisHeight);
        float r = 0.17f / (1 + height * 3);
        parts.Insert(0, new Part(PartKind.Shadow, new Vector3(pelvis.A.X, 0, pelvis.A.Z), default, r, r, PartPaint.Shadow));
        return parts;
    }

    private static List<Part> Raw(Pose pose)
    {
        var parts = new List<Part>();
        // The whole body, tipped around the feet.
        var body = (pose.Turn == 0 ? Frame.Identity : Frame.Identity.Rotate(Y, pose.Turn)).Rotate(X, pose.BodyPitch).Rotate(Z, -pose.BodyRoll);
        var pelvis = body.Apply(new Vector3((float)pose.Sideways, PelvisHeight, (float)pose.Forward));
        var hips = body.Rotate(Y, -pose.Twist * 0.3);
        var chestFrame = body.Rotate(Y, pose.Twist).Rotate(X, pose.Lean).Rotate(Z, -pose.Tilt);
        var chest = pelvis + chestFrame.Apply(new Vector3(0, Spine, 0));
        var neckTop = chest + chestFrame.Apply(new Vector3(0, Neck, 0));
        var headFrame = chestFrame.Rotate(Y, pose.HeadYaw).Rotate(X, pose.HeadPitch);
        var head = neckTop + headFrame.Apply(new Vector3(0, HeadRadius * 0.85f, 0));

        // Torso and head.
        parts.Add(new Part(PartKind.Capsule, pelvis, pelvis + chestFrame.Apply(new Vector3(0, 0.05f, 0)), 0.068f, 0.07f, PartPaint.Body));
        parts.Add(new Part(PartKind.Capsule, pelvis + chestFrame.Apply(new Vector3(0, 0.07f, 0)), chest, 0.062f, 0.082f, PartPaint.Body));
        parts.Add(new Part(PartKind.Capsule, chest, neckTop, 0.026f, 0.026f, PartPaint.Body));
        // Shoulders and hips as bars, so the body has its width from the front and from above.
        parts.Add(new Part(PartKind.Capsule, chest + chestFrame.Apply(new Vector3(-ShoulderHalf * 0.85f, -0.03f, 0)),
            chest + chestFrame.Apply(new Vector3(ShoulderHalf * 0.85f, -0.03f, 0)), 0.048f, 0.048f, PartPaint.Body));
        parts.Add(new Part(PartKind.Capsule, pelvis + hips.Apply(new Vector3(-HipHalf * 0.8f, 0, 0)),
            pelvis + hips.Apply(new Vector3(HipHalf * 0.8f, 0, 0)), 0.058f, 0.058f, PartPaint.Body));
        parts.Add(new Part(PartKind.Ball, head, head, HeadRadius, HeadRadius, PartPaint.Body));
        // The face: which way the head looks.
        var nose = head + headFrame.Apply(new Vector3(0, -0.012f, HeadRadius * 0.86f));
        parts.Add(new Part(PartKind.Ball, nose, nose, 0.024f, 0.024f, PartPaint.Face));

        // Legs.
        Vector3 footR = default, footL = default;
        foreach (var (side, limb, toe) in new[] { (1, pose.LegR, pose.ToeR), (-1, pose.LegL, pose.ToeL) })
        {
            var hip = pelvis + hips.Apply(new Vector3(side * HipHalf, -0.02f, 0));
            var leg = hips.Rotate(X, -limb.Swing).Rotate(Z, side * limb.Spread).Rotate(Y, -side * limb.Twist);
            var knee = hip + leg.Apply(Down * Thigh);
            var shin = leg.Rotate(X, limb.Bend);
            var ankle = knee + shin.Apply(Down * Shin);
            var foot = shin.Rotate(X, toe);
            var toes = ankle + foot.Apply(new Vector3(0, -0.012f, Foot));
            var paint = side > 0 ? PartPaint.Right : PartPaint.Left;
            parts.Add(new Part(PartKind.Capsule, hip, knee, 0.046f, 0.038f, paint));
            parts.Add(new Part(PartKind.Capsule, knee, ankle, 0.036f, 0.028f, paint));
            parts.Add(new Part(PartKind.Capsule, ankle + foot.Apply(new Vector3(0, -0.01f, -0.02f)), toes, 0.026f, 0.022f, paint));
            if (side > 0) footR = ankle; else footL = ankle;
        }

        // Arms.
        Vector3 handR = default, handL = default, foreR = default, axisR = default;
        foreach (var (side, limb) in new[] { (1, pose.ArmR), (-1, pose.ArmL) })
        {
            var shoulder = chest + chestFrame.Apply(new Vector3(side * ShoulderHalf, -0.025f, 0));
            var arm = chestFrame.Rotate(X, -limb.Swing).Rotate(Z, side * limb.Spread);
            var elbow = shoulder + arm.Apply(Down * UpperArm);
            var fore = arm.Rotate(Y, -side * limb.Twist).Rotate(X, -limb.Bend);
            var dir = fore.Apply(Down);
            var wrist = elbow + dir * Forearm;
            var hand = wrist + dir * 0.025f;
            var paint = side > 0 ? PartPaint.Right : PartPaint.Left;
            parts.Add(new Part(PartKind.Capsule, shoulder, elbow, 0.036f, 0.03f, paint));
            parts.Add(new Part(PartKind.Capsule, elbow, wrist, 0.029f, 0.024f, paint));
            parts.Add(new Part(PartKind.Ball, hand, hand, 0.031f, 0.031f, paint));
            if (side > 0)
            {
                handR = hand;
                foreR = dir;
                axisR = fore.Apply(X);
            }
            else handL = hand;
        }

        AddProp(parts, pose, handR, handL, foreR, axisR, pelvis, hips);
        _ = footR;
        _ = footL;
        return parts;
    }

    private static void AddProp(List<Part> parts, Pose pose, Vector3 handR, Vector3 handL, Vector3 fore, Vector3 axis, Vector3 pelvis, Frame hips)
    {
        switch (pose.Prop)
        {
            case PropKind.Sword:
            {
                var blade = Vector3.Normalize(Rot(fore, axis, -pose.PropAngle));
                parts.Add(new Part(PartKind.Capsule, handR - blade * 0.05f, handR + blade * 0.03f, 0.016f, 0.016f, PartPaint.Wood));
                parts.Add(new Part(PartKind.Capsule, handR + blade * 0.03f - axis * 0.045f, handR + blade * 0.03f + axis * 0.045f, 0.013f, 0.013f, PartPaint.Metal));
                parts.Add(new Part(PartKind.Capsule, handR + blade * 0.04f, handR + blade * 0.42f, 0.017f, 0.008f, PartPaint.Metal));
                break;
            }
            case PropKind.Hammer:
            {
                var handle = Vector3.Normalize(Rot(fore, axis, -pose.PropAngle));
                var end = handR + handle * 0.26f;
                parts.Add(new Part(PartKind.Capsule, handR - handle * 0.04f, end, 0.014f, 0.014f, PartPaint.Wood));
                parts.Add(new Part(PartKind.Capsule, end - fore * 0.06f, end + fore * 0.06f, 0.034f, 0.034f, PartPaint.Metal));
                break;
            }
            case PropKind.Shovel:
            {
                var shaft = SafeNormal(handL - handR, fore);
                var end = handL + shaft * 0.42f;
                parts.Add(new Part(PartKind.Capsule, handR - shaft * 0.07f, end, 0.014f, 0.014f, PartPaint.Wood));
                parts.Add(new Part(PartKind.Capsule, end, end + shaft * 0.13f, 0.045f, 0.03f, PartPaint.Metal));
                break;
            }
            case PropKind.Gun:
            {
                var barrel = SafeNormal(handL - handR, fore);
                parts.Add(new Part(PartKind.Capsule, handR - barrel * 0.1f, handR + barrel * 0.02f, 0.034f, 0.03f, PartPaint.Wood));
                parts.Add(new Part(PartKind.Capsule, handR, handL + barrel * 0.3f, 0.026f, 0.02f, PartPaint.Metal));
                break;
            }
            case PropKind.Orb:
            {
                if (pose.PropAmount <= 0.05) break;
                var mid = (handR + handL) / 2 + SafeNormal(fore, Z) * 0.05f;
                float r = 0.06f * (float)pose.PropAmount;
                parts.Add(new Part(PartKind.Ball, mid, mid, r, r, PartPaint.Glow));
                break;
            }
            case PropKind.Box:
            {
                var at = pose.PropAmount >= 0.5 ? (handR + handL) / 2 + hips.Apply(new Vector3(0, 0, 0.02f)) : pelvis with { Y = 0.08f } + hips.Apply(new Vector3(0, 0, 0.3f));
                parts.Add(new Part(PartKind.Box, at, at, 0.08f, 0.08f, PartPaint.Wood));
                break;
            }
            case PropKind.Crate:
            {
                var at = pelvis with { Y = 0.17f } + hips.Apply(new Vector3(0, 0, 0.44f));
                parts.Add(new Part(PartKind.Box, at, at, 0.17f, 0.17f, PartPaint.Wood));
                break;
            }
            case PropKind.Stool:
            {
                var at = pelvis with { Y = 0.12f } + hips.Apply(new Vector3(0, 0, -0.06f));
                parts.Add(new Part(PartKind.Box, at, at, 0.12f, 0.12f, PartPaint.Wood));
                break;
            }
            case PropKind.Load:
            {
                // Held between the hands, against the front of the body.
                var at = (handR + handL) / 2 + hips.Apply(new Vector3(0, 0, 0.01f));
                parts.Add(new Part(PartKind.Box, at, at, 0.12f, 0.12f, PartPaint.Wood));
                break;
            }
            case PropKind.Rope:
            {
                // Through both hands, then straight on forward to whatever is being pulled, with a short tail behind.
                var forward = hips.Apply(Z);
                var (rear, front) = Vector3.Dot(handL - handR, forward) >= 0 ? (handR, handL) : (handL, handR);
                parts.Add(new Part(PartKind.Capsule, rear - forward * 0.12f, front, 0.012f, 0.012f, PartPaint.Wood));
                parts.Add(new Part(PartKind.Capsule, front, front + forward * 0.75f, 0.012f, 0.012f, PartPaint.Wood));
                break;
            }
            case PropKind.Ladder:
            {
                // Fixed in front of the climber, from the ground up.
                float z = 0.2f, x = 0.14f;
                parts.Add(new Part(PartKind.Capsule, new Vector3(-x, 0, z), new Vector3(-x, 1.55f, z), 0.014f, 0.014f, PartPaint.Wood));
                parts.Add(new Part(PartKind.Capsule, new Vector3(x, 0, z), new Vector3(x, 1.55f, z), 0.014f, 0.014f, PartPaint.Wood));
                for (float y = 0.12f; y < 1.55f; y += 0.18f)
                    parts.Add(new Part(PartKind.Capsule, new Vector3(-x, y, z), new Vector3(x, y, z), 0.011f, 0.011f, PartPaint.Wood));
                break;
            }
        }
    }

    private static Vector3 SafeNormal(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : Vector3.Normalize(fallback);
}
