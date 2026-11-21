namespace DoodleNook.Engine.References;

/// <summary>
/// One reference cycle: its name, category, how many drawings it has, the speed it was made for, whether it loops,
/// which views it makes sense in, and the pose of every drawing. Cycles that are another played backwards (Drop is
/// Pick up in reverse) say so with <see cref="ReverseOf"/>.
/// </summary>
public sealed record ReferenceCycle(string Id, string Name, string Category, int Frames, double Fps, bool Loop, string Description,
    Func<int, Pose> PoseAt, IReadOnlyList<ReferenceView>? OnlyViews = null, string? ReverseOf = null)
{
    public static readonly IReadOnlyList<ReferenceView> AllViews = [ReferenceView.Side, ReferenceView.TopDown, ReferenceView.ThreeQuarter];

    public IReadOnlyList<ReferenceView> Views => OnlyViews ?? AllViews;

    public bool Supports(ReferenceView view) => Views.Contains(view);

    /// <summary>The pose of drawing <paramref name="frame"/> (0-based, wrapped).</summary>
    public Pose Pose(int frame) => PoseAt(((frame % Frames) + Frames) % Frames);
}

/// <summary>
/// Mission 013 — the built-in Reference Animation Library: common game-animation cycles as a simple mannequin, in side,
/// top-down and three-quarter views. Reference material to animate over, not finished art.
/// <para>
/// Frame counts are practical for game sprites and for tracing: 4–8 drawings, made for 8–12 frames per second.
/// To add a cycle, add one entry below: keyframe poses (<see cref="Keys"/>) or a gait (<see cref="Gait"/>), a frame count,
/// a speed and a category. Every cycle is drawn by the same mannequin, so each one works in every view by itself.
/// </para>
/// </summary>
public static class ReferenceLibrary
{
    public const string Movement = "Movement", Combat = "Combat", Interaction = "Interaction";
    public static readonly IReadOnlyList<string> Categories = [Movement, Combat, Interaction];

    private static readonly Pose Stand = new();

    public static readonly IReadOnlyList<ReferenceCycle> Cycles = Build();

    public static ReferenceCycle? Find(string? id) => Cycles.FirstOrDefault(c => c.Id == id);

    // ================================================================== building blocks

    /// <summary>Poses at given drawings; the drawings between are eased in between. A looping cycle eases from its last
    /// key back to its first.</summary>
    private static Func<int, Pose> Keys(int frames, bool loop, params (double At, Pose Pose)[] keys) => frame =>
    {
        double f = frame;
        for (int i = 0; i < keys.Length; i++)
        {
            if (Math.Abs(keys[i].At - f) < 1e-9) return keys[i].Pose;
            double nextAt = i + 1 < keys.Length ? keys[i + 1].At : loop ? keys[0].At + frames : double.PositiveInfinity;
            if (f > keys[i].At && f < nextAt)
            {
                if (double.IsPositiveInfinity(nextAt)) return keys[i].Pose;
                var next = i + 1 < keys.Length ? keys[i + 1].Pose : keys[0].Pose;
                double t = (f - keys[i].At) / (nextAt - keys[i].At);
                return Pose.Lerp(keys[i].Pose, next, t * t * (3 - 2 * t));
            }
        }
        return f < keys[0].At ? keys[0].Pose : keys[^1].Pose;
    };

    /// <summary>A walking-style gait: legs swing in opposite phase, the swinging knee lifts, arms swing against the legs.</summary>
    private sealed record GaitSpec(
        double Stride, double KneeStance, double KneeLift, double Toe, double ArmSwing, double ArmBend, double ArmBendSwing,
        double Lean, double Twist, double HeadPitch = 0, double ArmForward = 0, double ArmSpread = 7, double ArmTwist = 0,
        double Stance = 0.5, bool Backwards = false, PropKind Prop = PropKind.None);

    private static Func<int, Pose> Gait(int frames, GaitSpec g) => frame =>
    {
        double t = (double)frame / frames;
        if (g.Backwards) t = 1 - t;
        double Leg(double q, out double bend, out double toe)
        {
            q -= Math.Floor(q);
            double swing = g.Stride * Math.Cos(2 * Math.PI * q);
            if (q < g.Stance)
            {
                double u = q / g.Stance;
                bend = g.KneeStance * (0.4 + 0.6 * Math.Sin(Math.PI * u));
                toe = u > 0.6 ? g.Toe * Math.Sin(Math.PI * (u - 0.6) / 0.8) : 0;
            }
            else
            {
                double u = (q - g.Stance) / (1 - g.Stance);
                bend = g.KneeStance * 0.4 + g.KneeLift * Math.Sin(Math.PI * Math.Pow(u, 0.8));
                toe = g.Toe * Math.Max(0, 1 - u * 2.5);
            }
            return swing;
        }
        double sr = Leg(t, out double br, out double tr), sl = Leg(t + 0.5, out double bl, out double tl);
        double cr = Math.Cos(2 * Math.PI * t);
        Limb Arm(double phase) => new(g.ArmForward + g.ArmSwing * phase, g.ArmSpread, g.ArmBend + g.ArmBendSwing * (phase + 1) / 2, g.ArmTwist);
        return new Pose
        {
            LegR = new(sr, 3, br),
            LegL = new(sl, 3, bl),
            ToeR = tr,
            ToeL = tl,
            ArmR = Arm(-cr),
            ArmL = Arm(cr),
            Lean = g.Lean,
            Twist = g.Twist * cr,
            HeadYaw = -g.Twist * cr * 0.7,
            HeadPitch = g.HeadPitch,
            Prop = g.Prop,
        };
    };

    // ================================================================== the cycles

    private static List<ReferenceCycle> Build()
    {
        var cycles = new List<ReferenceCycle>
        {
            // ---------------------------------------------------------- movement
            new("idle", "Idle", Movement, 6, 8, true, "Standing and breathing: a gentle loop for a character waiting.",
                f =>
                {
                    double b = Math.Sin(2 * Math.PI * f / 6);
                    return new Pose
                    {
                        Lean = 2 + b, HeadPitch = -b * 1.5,
                        ArmL = new(-3, 8 + b, 14 + b * 4), ArmR = new(-3, 8 + b, 14 + b * 4),
                        LegL = new(-5, 4, 6 + (1 - b) * 3), LegR = new(6, 4, 6 + (1 - b) * 3),
                    };
                }),
            new("walk", "Walk", Movement, 8, 10, true, "A relaxed walk in place: contact, down, passing and up for each leg.",
                Gait(8, new GaitSpec(Stride: 24, KneeStance: 14, KneeLift: 52, Toe: 28, ArmSwing: 20, ArmBend: 14, ArmBendSwing: 14, Lean: 4, Twist: 7))),
            new("run", "Run", Movement, 8, 12, true, "A running loop in place: long strides, high knees, bent arms pumping.",
                Gait(8, new GaitSpec(Stride: 40, KneeStance: 32, KneeLift: 95, Toe: 40, ArmSwing: 38, ArmBend: 78, ArmBendSwing: 12, Lean: 14, Twist: 12,
                    HeadPitch: -8, Stance: 0.42))),
            new("sneak", "Sneak", Movement, 8, 8, true, "Crouched, careful steps with the arms held ready.",
                Gait(8, new GaitSpec(Stride: 18, KneeStance: 55, KneeLift: 45, Toe: 18, ArmSwing: 8, ArmBend: 75, ArmBendSwing: 6, Lean: 24, Twist: 5,
                    HeadPitch: -20, ArmForward: 25, ArmSpread: 14, ArmTwist: 20))),
            new("jump", "Jump", Movement, 8, 12, false, "Crouch, push off, tuck at the top and reach down for the ground (follow with Land).",
                Keys(8, false,
                    (0, Stand),
                    (1, new Pose { Lean = 30, LegL = new(48, 3, 75), LegR = new(48, 3, 75), ArmL = new(-45, 10, 20), ArmR = new(-45, 10, 20), HeadPitch = -12 }),
                    (2, new Pose { Lean = 6, LegL = new(-4, 3, 4), LegR = new(-4, 3, 4), ToeL = 45, ToeR = 45, ArmL = new(150, 12, 15), ArmR = new(150, 12, 15) }),
                    (3, new Pose { Airborne = true, Rise = 0.18, Lean = 2, LegL = new(15, 4, 35), LegR = new(25, 4, 50), ArmL = new(160, 18, 15), ArmR = new(160, 18, 15) }),
                    (4, new Pose { Airborne = true, Rise = 0.3, Lean = 10, LegL = new(55, 5, 95), LegR = new(60, 5, 100), ArmL = new(115, 32, 35), ArmR = new(115, 32, 35) }),
                    (5, new Pose { Airborne = true, Rise = 0.28, Lean = 8, LegL = new(45, 5, 80), LegR = new(50, 5, 85), ArmL = new(100, 40, 30), ArmR = new(100, 40, 30) }),
                    (6, new Pose { Airborne = true, Rise = 0.14, Lean = 4, LegL = new(18, 4, 28), LegR = new(22, 4, 32), ArmL = new(85, 50, 20), ArmR = new(85, 50, 20) }),
                    (7, new Pose { Airborne = true, Rise = 0.03, Lean = 6, LegL = new(12, 4, 12), LegR = new(14, 4, 14), ArmL = new(70, 45, 20), ArmR = new(70, 45, 20) }))),
            new("fall", "Fall", Movement, 4, 8, true, "Falling through the air, arms up and legs loose: loop it as long as the fall lasts.",
                f =>
                {
                    double c = Math.Cos(2 * Math.PI * f / 4);
                    return new Pose
                    {
                        Airborne = true, Rise = 0.22 + 0.015 * c, Lean = -8, HeadPitch = -12,
                        ArmR = new(118 + 14 * c, 62, 28), ArmL = new(118 - 14 * c, 62, 28),
                        LegR = new(14 + 10 * c, 6, 34 - 8 * c), LegL = new(8 - 10 * c, 6, 26 + 8 * c),
                    };
                }),
            new("land", "Land", Movement, 4, 12, false, "Touch down, absorb the impact in a crouch and stand up.",
                Keys(4, false,
                    (0, new Pose { Airborne = true, Rise = 0.04, Lean = 6, LegL = new(12, 4, 12), LegR = new(14, 4, 14), ArmL = new(70, 45, 20), ArmR = new(70, 45, 20) }),
                    (1, new Pose { Lean = 32, LegL = new(55, 4, 85), LegR = new(55, 4, 85), ArmL = new(38, 22, 30), ArmR = new(38, 22, 30), HeadPitch = -15 }),
                    (2, new Pose { Lean = 14, LegL = new(22, 4, 35), LegR = new(22, 4, 35), ArmL = new(12, 12, 25), ArmR = new(12, 12, 25) }),
                    (3, Stand))),

            // ---------------------------------------------------------- combat
            new("attack-slash", "Attack (slash)", Combat, 7, 12, false, "An overhead sword slash: wind up, strike down, follow through, recover.",
                Keys(7, false,
                    (0, new Pose { Prop = PropKind.Sword, PropAngle = 70, ArmR = new(40, 10, 70), ArmL = new(20, 18, 45), LegR = new(12, 4, 8), LegL = new(-10, 4, 8) }),
                    (1, new Pose { Prop = PropKind.Sword, PropAngle = 40, Twist = 22, Lean = -6, ArmR = new(165, 25, 70), ArmL = new(35, 25, 40), LegR = new(12, 4, 8), LegL = new(-12, 4, 10), HeadPitch = -6 }),
                    (2, new Pose { Prop = PropKind.Sword, PropAngle = 20, Twist = 8, Lean = 4, ArmR = new(125, 14, 25), ArmL = new(20, 25, 40), LegR = new(18, 4, 14), LegL = new(-14, 4, 10) }),
                    (3, new Pose { Prop = PropKind.Sword, PropAngle = 8, Twist = -18, Lean = 16, ArmR = new(62, 6, 4), ArmL = new(-15, 30, 35), LegR = new(28, 4, 30), LegL = new(-22, 4, 8), HeadPitch = 8 }),
                    (4, new Pose { Prop = PropKind.Sword, PropAngle = 14, Twist = -26, Lean = 20, ArmR = new(22, 8, 6), ArmL = new(-25, 30, 30), LegR = new(28, 4, 32), LegL = new(-22, 4, 8), HeadPitch = 8 }),
                    (5, new Pose { Prop = PropKind.Sword, PropAngle = 45, Twist = -10, Lean = 9, ArmR = new(32, 10, 45), ArmL = new(5, 22, 40), LegR = new(18, 4, 15), LegL = new(-14, 4, 8) }),
                    (6, new Pose { Prop = PropKind.Sword, PropAngle = 70, ArmR = new(40, 10, 70), ArmL = new(20, 18, 45), LegR = new(12, 4, 8), LegL = new(-10, 4, 8) }))),
            new("attack-thrust", "Attack (thrust)", Combat, 6, 12, false, "A lunging thrust: draw back, lunge with the blade straight ahead, return to guard.",
                Keys(6, false,
                    (0, new Pose { Prop = PropKind.Sword, PropAngle = 82, ArmR = new(28, 6, 82), ArmL = new(25, 20, 60), LegR = new(15, 4, 10), LegL = new(-12, 4, 10) }),
                    (1, new Pose { Prop = PropKind.Sword, PropAngle = 88, Twist = 16, Lean = -4, ArmR = new(6, 12, 112), ArmL = new(35, 22, 55), LegR = new(12, 4, 14), LegL = new(-14, 4, 14) }),
                    (2, new Pose { Prop = PropKind.Sword, PropAngle = 2, Twist = -16, Lean = 16, Forward = 0.05, ArmR = new(86, 2, 2), ArmL = new(-30, 28, 20), LegR = new(38, 4, 38), LegL = new(-26, 4, 4), ToeL = 20 }),
                    (3, new Pose { Prop = PropKind.Sword, PropAngle = 2, Twist = -18, Lean = 17, Forward = 0.05, ArmR = new(88, 2, 0), ArmL = new(-32, 28, 18), LegR = new(40, 4, 40), LegL = new(-26, 4, 4), ToeL = 20 }),
                    (4, new Pose { Prop = PropKind.Sword, PropAngle = 70, Twist = 4, Lean = 5, ArmR = new(30, 8, 75), ArmL = new(15, 22, 55), LegR = new(20, 4, 15), LegL = new(-14, 4, 10) }),
                    (5, new Pose { Prop = PropKind.Sword, PropAngle = 82, ArmR = new(28, 6, 82), ArmL = new(25, 20, 60), LegR = new(15, 4, 10), LegL = new(-12, 4, 10) }))),
            new("hurt", "Hurt", Combat, 4, 12, false, "Hit from the front: recoil back, stagger, recover.",
                Keys(4, false,
                    (0, Stand),
                    (1, new Pose { Lean = -20, HeadPitch = -22, BodyPitch = -4, ArmL = new(25, 38, 35), ArmR = new(25, 38, 35), LegR = new(-14, 4, 14), LegL = new(8, 4, 8) }),
                    (2, new Pose { Lean = -10, HeadPitch = -6, ArmL = new(12, 25, 30), ArmR = new(12, 25, 30), LegR = new(-10, 4, 30), LegL = new(14, 4, 28) }),
                    (3, new Pose { Lean = 3, ArmL = new(0, 10, 18), ArmR = new(0, 10, 18), LegR = new(-6, 4, 10), LegL = new(6, 4, 8) }))),
            new("death", "Death", Combat, 8, 10, false, "Hit, knees give way, fall forward to the ground and lie still.",
                Keys(8, false,
                    (0, Stand),
                    (1, new Pose { Lean = -18, HeadPitch = -24, BodyPitch = -4, ArmL = new(25, 35, 35), ArmR = new(25, 35, 35), LegR = new(-12, 4, 14), LegL = new(8, 4, 8) }),
                    (2, new Pose { Lean = 18, HeadPitch = 20, ArmL = new(5, 18, 25), ArmR = new(5, 18, 25), LegR = new(28, 4, 60), LegL = new(25, 4, 58) }),
                    (3, new Pose { Lean = 26, HeadPitch = 26, ArmL = new(-5, 15, 20), ArmR = new(-5, 15, 20), LegR = new(70, 4, 110), LegL = new(68, 4, 112) }),
                    (4, new Pose { BodyPitch = 32, Lean = 22, HeadPitch = 10, ArmL = new(40, 22, 15), ArmR = new(40, 22, 15), LegR = new(60, 4, 100), LegL = new(58, 4, 100) }),
                    (5, new Pose { BodyPitch = 64, Lean = 14, ArmL = new(80, 28, 10), ArmR = new(80, 28, 10), LegR = new(30, 4, 60), LegL = new(28, 4, 60) }),
                    (6, new Pose { BodyPitch = 86, Lean = 4, HeadPitch = -10, ArmL = new(110, 32, 10), ArmR = new(100, 26, 15), LegR = new(10, 6, 25), LegL = new(6, 6, 18) }),
                    (7, new Pose { BodyPitch = 90, Lean = 0, HeadPitch = -12, HeadYaw = 30, ArmL = new(115, 35, 8), ArmR = new(95, 25, 20), LegR = new(8, 8, 20), LegL = new(4, 6, 12) }))),
            new("cast", "Cast", Combat, 6, 10, false, "Gather power in both hands, raise it and push it forward.",
                Keys(6, false,
                    (0, new Pose { Prop = PropKind.Orb, PropAmount = 0 }),
                    (1, new Pose { Prop = PropKind.Orb, PropAmount = 0.35, Lean = -3, ArmL = new(28, 6, 62, 42), ArmR = new(28, 6, 62, 42), LegR = new(6, 4, 8), LegL = new(-6, 4, 8) }),
                    (2, new Pose { Prop = PropKind.Orb, PropAmount = 0.6, Lean = -6, HeadPitch = -12, ArmL = new(105, 8, 40, 30), ArmR = new(105, 8, 40, 30), LegR = new(6, 4, 6), LegL = new(-6, 4, 6) }),
                    (3, new Pose { Prop = PropKind.Orb, PropAmount = 0.9, Lean = -9, HeadPitch = -22, ArmL = new(162, 14, 22, 20), ArmR = new(162, 14, 22, 20), LegR = new(8, 4, 6), LegL = new(-8, 4, 6) }),
                    (4, new Pose { Prop = PropKind.Orb, PropAmount = 1.2, Lean = 12, ArmL = new(86, 6, 6, 18), ArmR = new(86, 6, 6, 18), LegR = new(24, 4, 22), LegL = new(-16, 4, 6) }),
                    (5, new Pose { Prop = PropKind.Orb, PropAmount = 0.3, Lean = 6, ArmL = new(66, 10, 12, 18), ArmR = new(66, 10, 12, 18), LegR = new(20, 4, 16), LegL = new(-14, 4, 6) }))),
            new("shoot", "Shoot", Combat, 6, 12, false, "Raise a crossbow or gun, aim, fire with a small recoil, keep aiming.",
                Keys(6, false,
                    (0, new Pose { Prop = PropKind.Gun, ArmR = new(28, 6, 62, 10), ArmL = new(38, 12, 72, 45), LegR = new(8, 4, 6), LegL = new(-8, 4, 6) }),
                    (1, new Pose { Prop = PropKind.Gun, ArmR = new(70, 4, 32, 18), ArmL = new(76, 6, 26, 48), HeadPitch = 4, LegR = new(10, 4, 8), LegL = new(-10, 4, 8) }),
                    (2, new Pose { Prop = PropKind.Gun, Lean = 4, ArmR = new(86, 2, 14, 20), ArmL = new(90, 2, 18, 52), HeadPitch = 6, LegR = new(12, 4, 10), LegL = new(-12, 4, 10) }),
                    (3, new Pose { Prop = PropKind.Gun, Lean = -6, ArmR = new(100, 2, 18, 20), ArmL = new(104, 2, 22, 52), HeadPitch = -2, LegR = new(10, 4, 12), LegL = new(-14, 4, 12) }),
                    (4, new Pose { Prop = PropKind.Gun, Lean = 2, ArmR = new(90, 2, 15, 20), ArmL = new(94, 2, 19, 52), HeadPitch = 5, LegR = new(12, 4, 10), LegL = new(-12, 4, 10) }),
                    (5, new Pose { Prop = PropKind.Gun, Lean = 4, ArmR = new(86, 2, 14, 20), ArmL = new(90, 2, 18, 52), HeadPitch = 6, LegR = new(12, 4, 10), LegL = new(-12, 4, 10) }))),

            // ---------------------------------------------------------- interaction
            new("pick-up", "Pick up", Interaction, 7, 10, false, "Bend down, take a box from the ground and lift it to the chest.",
                Keys(7, false, PickUpKeys()), ReverseOf: null),
            new("drop", "Drop", Interaction, 7, 10, false, "Put a box down: Pick up played backwards.",
                Keys(7, false, PickUpKeys()), ReverseOf: "pick-up"),
            new("push", "Push", Interaction, 8, 8, true, "Lean into a heavy crate and push it, step by step.",
                Gait(8, new GaitSpec(Stride: 18, KneeStance: 26, KneeLift: 40, Toe: 30, ArmSwing: 3, ArmBend: 28, ArmBendSwing: 4, Lean: 28, Twist: 4,
                    HeadPitch: -16, ArmForward: 78, ArmSpread: 8, ArmTwist: 12, Stance: 0.6, Prop: PropKind.Crate))),
            new("pull", "Pull", Interaction, 8, 8, true, "Hold a crate and drag it, stepping backwards and leaning back.",
                Gait(8, new GaitSpec(Stride: 16, KneeStance: 24, KneeLift: 38, Toe: 18, ArmSwing: 3, ArmBend: 10, ArmBendSwing: 4, Lean: -12, Twist: 4,
                    HeadPitch: 6, ArmForward: 62, ArmSpread: 8, ArmTwist: 10, Stance: 0.6, Backwards: true, Prop: PropKind.Crate))),
            new("climb", "Climb", Interaction, 6, 8, true, "Climb a ladder: hand over hand, foot over foot.",
                f =>
                {
                    double c = Math.Cos(2 * Math.PI * f / 6);
                    return new Pose
                    {
                        Prop = PropKind.Ladder, Airborne = true, Rise = 0.3, Lean = 6, HeadPitch = -18,
                        ArmR = new(150 + 18 * c, 14, 50 - 22 * c), ArmL = new(150 - 18 * c, 14, 50 + 22 * c),
                        LegR = new(52 - 26 * c, 6, 82 - 16 * c), LegL = new(52 + 26 * c, 6, 82 + 16 * c),
                    };
                }, [ReferenceView.Side, ReferenceView.ThreeQuarter]),
            new("sit-down", "Sit down", Interaction, 6, 8, false, "Lower onto a stool and settle.",
                Keys(6, false, SitKeys())),
            new("stand-up", "Stand up", Interaction, 6, 8, false, "Get up from a stool: Sit down played backwards.",
                Keys(6, false, SitKeys()), ReverseOf: "sit-down"),
            new("hammer", "Hammer", Interaction, 6, 10, true, "Work with a hammer: lift, strike, bounce back.",
                Keys(6, true,
                    (0, new Pose { Prop = PropKind.Hammer, PropAngle = 90, Lean = 22, Twist = 12, ArmR = new(138, 16, 72), ArmL = new(38, 22, 60), LegR = new(16, 4, 16), LegL = new(-10, 4, 12), HeadPitch = 14 }),
                    (1, new Pose { Prop = PropKind.Hammer, PropAngle = 85, Lean = 18, Twist = 16, ArmR = new(158, 18, 95), ArmL = new(40, 22, 60), LegR = new(16, 4, 16), LegL = new(-10, 4, 12), HeadPitch = 14 }),
                    (2, new Pose { Prop = PropKind.Hammer, PropAngle = 85, Lean = 26, Twist = 4, ArmR = new(92, 10, 28), ArmL = new(36, 22, 60), LegR = new(16, 4, 18), LegL = new(-10, 4, 12), HeadPitch = 18 }),
                    (3, new Pose { Prop = PropKind.Hammer, PropAngle = 82, Lean = 32, Twist = -4, ArmR = new(48, 6, 2), ArmL = new(34, 22, 60), LegR = new(16, 4, 22), LegL = new(-10, 4, 14), HeadPitch = 20 }),
                    (4, new Pose { Prop = PropKind.Hammer, PropAngle = 88, Lean = 28, Twist = 2, ArmR = new(76, 8, 38), ArmL = new(36, 22, 60), LegR = new(16, 4, 18), LegL = new(-10, 4, 12), HeadPitch = 18 }),
                    (5, new Pose { Prop = PropKind.Hammer, PropAngle = 90, Lean = 24, Twist = 8, ArmR = new(112, 14, 60), ArmL = new(38, 22, 60), LegR = new(16, 4, 16), LegL = new(-10, 4, 12), HeadPitch = 16 }))),
            new("dig", "Dig", Interaction, 6, 8, true, "Dig with a shovel: push in, lever up, toss to the side, back again.",
                Keys(6, true,
                    (0, new Pose { Prop = PropKind.Shovel, Lean = 30, ArmR = new(42, 10, 52), ArmL = new(52, 4, 18, 18), LegR = new(22, 4, 30), LegL = new(-12, 4, 14), HeadPitch = 18 }),
                    (1, new Pose { Prop = PropKind.Shovel, Lean = 34, ArmR = new(34, 10, 30), ArmL = new(46, 4, 10, 18), LegR = new(26, 4, 42), LegL = new(-14, 4, 20), HeadPitch = 20 }),
                    (2, new Pose { Prop = PropKind.Shovel, Lean = 20, ArmR = new(30, 10, 72), ArmL = new(72, 6, 38, 20), LegR = new(18, 4, 26), LegL = new(-12, 4, 14), HeadPitch = 10 }),
                    (3, new Pose { Prop = PropKind.Shovel, Lean = 10, Twist = -28, ArmR = new(58, 14, 60), ArmL = new(96, 18, 14, 20), LegR = new(14, 4, 18), LegL = new(-10, 4, 12), HeadYaw = -12 }),
                    (4, new Pose { Prop = PropKind.Shovel, Lean = 16, Twist = -10, ArmR = new(48, 12, 58), ArmL = new(70, 8, 30, 20), LegR = new(16, 4, 20), LegL = new(-10, 4, 12), HeadPitch = 8 }),
                    (5, new Pose { Prop = PropKind.Shovel, Lean = 26, ArmR = new(44, 10, 50), ArmL = new(58, 4, 22, 18), LegR = new(20, 4, 26), LegL = new(-12, 4, 14), HeadPitch = 16 }))),
        };
        // Reversed cycles play their source backwards.
        for (int i = 0; i < cycles.Count; i++)
            if (cycles[i].ReverseOf is not null)
            {
                var c = cycles[i];
                var forward = c.PoseAt;
                cycles[i] = c with { PoseAt = f => forward(c.Frames - 1 - f) };
            }
        return cycles;
    }

    private static (double, Pose)[] PickUpKeys() =>
    [
        (0, new Pose { Prop = PropKind.Box, PropAmount = 0 }),
        (1, new Pose { Prop = PropKind.Box, PropAmount = 0, Lean = 32, HeadPitch = 14, LegL = new(32, 5, 32), LegR = new(34, 5, 34), ArmL = new(38, 12, 18), ArmR = new(38, 12, 18) }),
        (2, new Pose { Prop = PropKind.Box, PropAmount = 0, Lean = 52, HeadPitch = 14, LegL = new(66, 6, 82), LegR = new(68, 6, 84), ArmL = new(62, 14, 8, 20), ArmR = new(62, 14, 8, 20) }),
        (3, new Pose { Prop = PropKind.Box, PropAmount = 1, Lean = 52, HeadPitch = 14, LegL = new(66, 6, 82), LegR = new(68, 6, 84), ArmL = new(62, 14, 8, 20), ArmR = new(62, 14, 8, 20) }),
        (4, new Pose { Prop = PropKind.Box, PropAmount = 1, Lean = 30, HeadPitch = 6, LegL = new(36, 5, 44), LegR = new(38, 5, 46), ArmL = new(46, 10, 48, 32), ArmR = new(46, 10, 48, 32) }),
        (5, new Pose { Prop = PropKind.Box, PropAmount = 1, Lean = 4, LegL = new(4, 4, 8), LegR = new(6, 4, 8), ArmL = new(22, 8, 82, 44), ArmR = new(22, 8, 82, 44) }),
        (6, new Pose { Prop = PropKind.Box, PropAmount = 1, Lean = 2, LegL = new(2, 4, 6), LegR = new(4, 4, 6), ArmL = new(20, 8, 86, 46), ArmR = new(20, 8, 86, 46) }),
    ];

    private static (double, Pose)[] SitKeys() =>
    [
        (0, new Pose { Prop = PropKind.Stool }),
        (1, new Pose { Prop = PropKind.Stool, Lean = 18, LegL = new(26, 5, 30), LegR = new(26, 5, 30), ArmL = new(18, 10, 20), ArmR = new(18, 10, 20), HeadPitch = -6 }),
        (2, new Pose { Prop = PropKind.Stool, Lean = 30, LegL = new(56, 5, 62), LegR = new(56, 5, 62), ArmL = new(32, 12, 25), ArmR = new(32, 12, 25), HeadPitch = -12 }),
        (3, new Pose { Prop = PropKind.Stool, Lean = 22, LegL = new(80, 6, 84), LegR = new(80, 6, 84), ArmL = new(30, 12, 30), ArmR = new(30, 12, 30), HeadPitch = -8 }),
        (4, new Pose { Prop = PropKind.Stool, Lean = 8, LegL = new(88, 7, 88), LegR = new(88, 7, 88), ArmL = new(28, 10, 40), ArmR = new(28, 10, 40) }),
        (5, new Pose { Prop = PropKind.Stool, Lean = 4, LegL = new(90, 8, 90), LegR = new(90, 8, 90), ArmL = new(34, 8, 46), ArmR = new(34, 8, 46) }),
    ];
}
