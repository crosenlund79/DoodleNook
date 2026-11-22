using System.Numerics;

namespace DoodleNook.Engine.References;

/// <summary>
/// Expansion pack 1: purposeful movement (carrying a load, limping, stumbling, and the rope Pull), emotion and body
/// language, and transitions between the cycles. Same mannequin, same rules as the rest of the library: feet are placed
/// where they stand, hands on what they hold, and everything plays in place like the walk and the run.
/// </summary>
public static partial class ReferenceLibrary
{
    private static void AddExpansionPack1(List<ReferenceCycle> cycles)
    {
        cycles.AddRange(
        [
            // ---------------------------------------------------------- purposeful movement
            new("carry-heavy", "Carry heavy", Movement, 8, 8, true,
                "Walking with a heavy load held against the belly: the body leans back to balance it, the steps are short and wide, and the weight rolls from foot to foot.",
                Steps(8, CarrySpec)),
            new("limp", "Limp walk", Movement, 8, 8, true,
                "Favouring a sore right leg: it is on the ground only briefly, held stiff, and the good leg hurries in to take the weight, with the head dropping as it lands.",
                Steps(8, LimpSpec)),
            new("stumble", "Stumble", Movement, 10, 12, false,
                "Walking, a toe catches: the body pitches forward, two quick steps catch it, and it straightens up and looks back.",
                Drawings(StumblePoses)),
            // ---------------------------------------------------------- emotion and body language
            new("startle", "Fear / startle", Emotion, 8, 12, false,
                "A sudden fright: in one drawing the body flinches away, an arm up to guard the face, a foot stepping back; it holds, frozen, then settles slowly, still wary.",
                Drawings(StartlePoses)),
            new("hesitate", "Hesitation", Emotion, 10, 10, false,
                "Weight goes forward and a foot starts a step, pauses in the air, comes back; the hand goes to the chin. Small enough for an ordinary scene.",
                Drawings(HesitatePoses)),
            new("sad", "Sadness / crying", Emotion, 8, 8, true,
                "Slumped, head down, a hand over the eyes: the shoulders and chest catch twice in a sob, then sag out slowly.",
                Drawings(SadPoses)),
            new("laugh", "Laughing", Emotion, 8, 10, true,
                "Bursts of laughter of different sizes: folding forward onto a hand on the belly, rocking back, slapping the thigh, a quick breath in.",
                Drawings(LaughPoses)),
            new("exhausted", "Exhausted idle", Emotion, 8, 8, true,
                "Bent over, hands on the knees, the weight sunk into the legs: a quick heavy breath in lifts the chest and head, then a long sag out.",
                Drawings(ExhaustedPoses)),

            // ---------------------------------------------------------- transitions
            new("idle-to-walk", "Idle → walk", Transitions, 9, 10, false,
                "Starts on Idle's first drawing: the weight shifts onto one foot before the first step, then the body falls forward into it and speeds up. Ends just before Walk's first drawing: put Walk right after it at the same spot.",
                Drawings(IdleToWalkPoses)),
            new("walk-to-run", "Walk → run", Transitions, 8, 12, false,
                "Starts on Walk's first drawing; over two steps the stride lengthens, the steps quicken, the body leans in and the arms bend. Ends on Run's last drawing: Run follows on.",
                Drawings(WalkToRunPoses)),
            new("run-to-stop", "Run → stop", Transitions, 9, 12, false,
                "Starts on Run's first drawing. Notice how the leading foot brakes far ahead with the body leaning back, a second shorter step, then the trunk pitches forward as it settles.",
                Drawings(RunToStopPoses)),
            new("stand-to-crouch", "Stand → crouch", Transitions, 6, 10, false,
                "Hips back, knees forward and heels up together, faster as it drops, with a small dip past the crouch before it settles, arms forward for balance.",
                Drawings(CrouchPoses)),
            new("turn-180", "Turn 180°", Transitions, 10, 10, false,
                "The head turns first, then the shoulders, then the hips; one foot pivots on its ball while the other steps around, and the first adjusts to finish facing the other way.",
                Drawings(TurnPoses)),
        ]);
    }

    // ================================================================== helpers for planned sequences

    /// <summary>A foot on the ground plan: <see cref="Z"/> along the direction of travel and <see cref="X"/> to the side
    /// (the ankle of a flat foot), lifted, pitched and turned like a <see cref="FootAt"/>.</summary>
    private readonly record struct Fp(double Z, double X, double Lift = 0, double Pitch = 0, double Yaw = 0);

    /// <summary>
    /// One drawing of a sequence planned on the ground and played in place: the body has travelled
    /// <paramref name="s"/> along the ground, so a planted foot (fixed <see cref="Fp.Z"/>) moves back under it at exactly
    /// the speed the character should be moved, like a step cycle's feet.
    /// </summary>
    private static Pose OnGround(Pose upper, double s, double height, Fp r, Fp l, Func<Pose, double, Pose>? hands = null)
    {
        var p = Plant(upper, height, new FootAt(r.Z - s, r.Lift, r.Pitch, r.X, r.Yaw), new FootAt(l.Z - s, l.Lift, l.Pitch, l.X, l.Yaw));
        if (hands is not null) p = hands(p, height);
        return r.Lift > 1e-4 && l.Lift > 1e-4 ? PoseSolver.Fly(p, height) : p;
    }

    /// <summary>A moment of a step cycle placed on the ground plan at travel <paramref name="s"/>.</summary>
    private static (Pose Upper, double Height, Fp R, Fp L) Moment(StepSpec g, double t, double s)
    {
        var m = StepFrame(g, t);
        Fp G(FootAt f) => new(f.Z + s, f.X ?? 0, f.Lift, f.Pitch, f.Yaw);
        return (m.Upper, m.Height, G(m.Right), G(m.Left));
    }

    private static Pose OnGround((Pose Upper, double Height, Fp R, Fp L) m, double s) => OnGround(m.Upper, s, m.Height, m.R, m.L);

    /// <summary>A values-over-time curve through keys (t, value), eased between each pair; <paramref name="loop"/>
    /// wraps from the last key back to the first.</summary>
    private static Func<double, double> Curve(bool loop, params (double T, double V)[] keys) => t =>
    {
        if (loop) t -= Math.Floor(t);
        for (int i = 0; i < keys.Length; i++)
        {
            var a = keys[i];
            var b = i + 1 < keys.Length ? keys[i + 1] : loop ? (T: keys[0].T + 1, V: keys[0].V) : a;
            if (t >= a.T && t <= b.T) return b.T == a.T ? a.V : Lerp(a.V, b.V, Smooth((t - a.T) / (b.T - a.T)));
        }
        if (loop && t < keys[0].T) // between the last key (one cycle back) and the first
        {
            var a = keys[^1];
            double at = a.T - 1;
            return Lerp(a.V, keys[0].V, Smooth((t - at) / (keys[0].T - at)));
        }
        return t < keys[0].T ? keys[0].V : keys[^1].V;
    };

    // ================================================================== carry heavy

    /// <summary>A heavy box held against the belly: short, wide, slow steps, the body leaning back over the hips to balance
    /// the load, a rolling sideways weight shift and a dip as each foot takes the weight.</summary>
    private static StepSpec CarrySpec => new()
    {
        Length = 0.24, Duty = 0.66, Height = 0.475, Bob = 0.016, LowAt = 0.16, Center = 0.03, Width = 0.1,
        Lift = 0.04, Strike = -10, Flat = 0.2, HeelOff = 0.62, ToeOff = 22, Lean = -7, LeanBob = -1.5, Twist = 2,
        Sway = 0.026, Tilt = 3, ArmSwing = 0, ArmSpread = 10, ArmBend = 60, ArmBendSwing = 0, HeadPitch = -4, HeadNod = 2,
        Prop = PropKind.Load,
        Hands = (pose, h) => PoseSolver.Reach(pose, h, V(0.15 + pose.Sideways, h + 0.06, 0.17), V(-0.15 + pose.Sideways, h + 0.06, 0.17)),
    };

    // ================================================================== limp

    /// <summary>
    /// An antalgic limp on the right leg: the right foot is down for a short time (0.40 of the cycle), landing gingerly
    /// toes first with a stiff knee and no push-off; the good left leg is down for longer (0.74) and comes through quickly. The
    /// pelvis is held high over the sore leg and drops onto the good one, the head nodding with it; the trunk leans over
    /// the sore side while it bears weight, the stiff leg swings out around, and the hand on that side presses on the thigh.
    /// </summary>
    private static StepSpec LimpSpec => new()
    {
        Length = 0.16, Duty = 0.4, Center = 0.02, Width = 0.07, Lift = 0.026, LiftAt = 0.5, Strike = 8, Flat = 0.3, RightSwingOut = 0.05,
        HeelOff = 0.85, ToeOff = 6, Reach = 10, Lean = 6, Twist = 3, Sway = 0, Tilt = 0, ArmSwing = 0, ArmBend = 16, ArmBendSwing = 0,
        LeftDuty = 0.74, LeftAt = 0.34, LeftLift = 0.07, LeftStrike = -18, LeftToeOff = 32, LeftHeelOff = 0.6,
        HeightAt = Curve(true, (0.0, 0.515), (0.18, 0.532), (0.34, 0.522), (0.43, 0.488), (0.58, 0.502), (0.82, 0.518)),
        Shape = (t, p) =>
        {
            double rightDown = Curve(true, (0.0, 0.6), (0.18, 1), (0.36, 0.7), (0.46, -0.7), (0.75, -0.8), (0.95, 0.2))(t);
            double nod = Curve(true, (0.0, 0), (0.34, 0), (0.44, 1), (0.6, 0.35), (0.8, 0))(t);
            double swing = Curve(true, (0.0, -0.4), (0.3, 0.6), (0.55, 1), (0.8, -0.2))(t); // left arm: forward with the sore leg's swing
            return p with
            {
                Sideways = 0.026 * rightDown,
                Tilt = rightDown > 0 ? 5 * rightDown : 1.5 * rightDown,
                Lean = 6 + 7 * nod,
                HeadPitch = 4 + 13 * nod,
                ArmL = new Limb(18 * swing, 9, 16 + 12 * Math.Max(0, swing)),
                // The right hand presses on the sore thigh while that leg carries the weight.
                ArmR = rightDown > 0 ? new Limb(Lerp(-4, 14, rightDown), Lerp(11, -2, rightDown), Lerp(24, 40, rightDown), 25 * rightDown) : new Limb(-4, 11, 24),
            };
        },
    };

    // ================================================================== rope pull

    /// <summary>
    /// Pull: hauling a rope hand over hand. The feet are braced, the front heel dug in with its toes up; the hips sit back
    /// and the body leans back against the rope, so its weight does the pulling. Each hand grips and hauls back to the hip
    /// while the other slides forward along the rope; the lean deepens and the hips settle at the end of each haul.
    /// </summary>
    private static Pose[] PullPoses()
    {
        const double baseHeight = 0.47;
        var front = Foot(0.17, 0.1, pitch: -14);
        var back = Foot(-0.1, -0.11);
        return
        [
            .. Enumerable.Range(0, 8).Select(f =>
            {
                double t = f / 8.0;
                // Along the rope from the hip (0) to the front grip (1): gripping and hauling back for half the cycle, then
                // sliding forward to grip again.
                double Grip(double q)
                {
                    q -= Math.Floor(q);
                    return q < 0.5 ? 1 - q / 0.5 : Smooth((q - 0.5) / 0.5);
                }
                double haul = 0.5 * (1 - Math.Cos(4 * Math.PI * t)); // 0 as a hand grips, 1 as its haul ends
                double height = baseHeight - 0.014 * haul;
                var upper = new Pose
                {
                    Prop = PropKind.Rope, Forward = -0.03 - 0.012 * haul, Lean = -19 - 7 * haul, HeadPitch = 10 + 3 * haul,
                    Twist = 9 * Math.Cos(2 * Math.PI * t), HeadYaw = -6 * Math.Cos(2 * Math.PI * t),
                };
                var p = Plant(upper, height, front, back);
                Vector3 OnRope(double a, double x) => V(x, height + 0.0 + 0.012 * a, 0.03 + 0.17 * a);
                return PoseSolver.Reach(p, height, OnRope(Grip(t), 0.02), OnRope(Grip(t + 0.5), -0.03));
            }),
        ];
    }

    // ================================================================== stumble

    /// <summary>
    /// Stumble, planned on the ground and played in place (12 fps): a walking step, the left toe catches the ground and
    /// stops while the body keeps going over the right foot and pitches forward, arms flung out; the left foot is
    /// snatched forward to catch the fall, then the right, the body low; it straightens up, rocks back a little and
    /// looks back at what tripped it.
    /// </summary>
    private static Pose[] StumblePoses()
    {
        // The walk at 12 drawings a second: one drawing moves 10/12 of a walk drawing along the cycle.
        const double perDrawing = 10.0 / 12 / 8;
        var walk = WalkSpec;
        var m0 = Moment(walk, 0, 0);
        var m1 = Moment(walk, perDrawing, 0.0625);
        Fp R(double z, double lift = 0, double pitch = 0) => new(z, 0.06, lift, pitch);
        Fp L(double z, double lift = 0, double pitch = 0) => new(z, -0.06, lift, pitch);
        return
        [
            OnGround(m0, 0),
            OnGround(m1, 0.0625),
            // The left toe catches behind the standing foot; the body carries on and starts to tip.
            OnGround(new Pose { Lean = 14, HeadPitch = -6, Twist = -4, ArmR = new(-10, 14, 20), ArmL = new(28, 18, 26) },
                0.13, 0.505, R(0.18, pitch: 8), L(0.03, 0, 38)),
            // Falling forward over the ball of the right foot, arms flung forward and out.
            OnGround(new Pose { Lean = 30, HeadPitch = -22, Twist = -8, ArmR = new(70, 34, 24), ArmL = new(96, 26, 30) },
                0.215, 0.475, R(0.18, pitch: 26), L(0.05, 0.012, 34)),
            // The left foot is snatched forward to catch the fall.
            OnGround(new Pose { Lean = 34, HeadPitch = -24, Twist = 6, ArmR = new(64, 52, 20), ArmL = new(110, 40, 28) },
                0.31, 0.455, R(0.18, pitch: 42), L(0.33, 0.07, -4)),
            // It lands far ahead, heel first, the knee giving; the right foot leaves the ground.
            OnGround(new Pose { Lean = 27, HeadPitch = -16, Twist = 10, ArmR = new(96, 60, 30), ArmL = new(40, 56, 26) },
                0.395, 0.43, R(0.24, 0.05, 40), L(0.52, pitch: -14)),
            // A second, quicker catch step with the right foot.
            OnGround(new Pose { Lean = 19, HeadPitch = -10, Twist = 4, ArmR = new(60, 48, 30), ArmL = new(14, 52, 24) },
                0.465, 0.45, R(0.5, 0.08, 8), L(0.52, pitch: 10)),
            OnGround(new Pose { Lean = 11, HeadPitch = -4, Twist = -2, ArmR = new(26, 36, 30), ArmL = new(4, 40, 26) },
                0.52, 0.47, R(0.66, pitch: -10), L(0.52, pitch: 18)),
            // Balance regained: up, rocking back a touch, arms coming down.
            OnGround(new Pose { Lean = -3, HeadPitch = -2, ArmR = new(8, 18, 22), ArmL = new(-2, 20, 20) },
                0.555, 0.515, R(0.66), L(0.52, pitch: 6)),
            // Settled, a glance back at what tripped it.
            OnGround(new Pose { Lean = 1, HeadPitch = 10, HeadYaw = -55, Twist = -12, ArmR = new(2, 9, 16), ArmL = new(-4, 10, 18) },
                0.57, 0.525, R(0.66), L(0.52)),
        ];
    }

    // ================================================================== emotion: startle

    /// <summary>Fear / startle (12 fps): neutral; the flinch in one drawing (head ducked, chest turned away, the right
    /// forearm up across the face and the left hand out, the left foot stepping back); frozen for a moment; then a slow,
    /// easing settle into a wary stance with a hand on the chest.</summary>
    private static Pose[] StartlePoses()
    {
        var r = Foot(-0.015, 0.1);
        Pose At(double height, double forward, double lean, double twist, double head, double look, double tilt, Limb right, Limb left, FootAt rf, FootAt lf) =>
            Plant(new Pose { Forward = forward, Lean = lean, Twist = twist, HeadPitch = head, HeadYaw = look, Tilt = tilt, ArmR = right, ArmL = left }, height, rf, lf);
        return
        [
            Standing(),
            At(0.49, -0.04, -10, -22, 12, 18, -6, new(95, 15, 110, 30), new(55, 35, 70), Foot(-0.015, 0.1, pitch: 6), Foot(-0.08, -0.11, 0.03, 12)),
            At(0.474, -0.06, -14, -28, 14, 22, -7, new(106, 16, 122, 32), new(62, 40, 76), Foot(-0.015, 0.1, pitch: 8), Foot(-0.17, -0.12, pitch: 10)),
            At(0.476, -0.058, -13, -27, 14, 22, -7, new(104, 16, 120, 32), new(60, 39, 76), Foot(-0.015, 0.1, pitch: 7), Foot(-0.17, -0.12, pitch: 9)),
            At(0.488, -0.046, -9, -20, 9, 17, -5, new(82, 12, 108, 26), new(46, 31, 70), r, Foot(-0.17, -0.12, pitch: 4)),
            At(0.498, -0.036, -6, -14, 6, 13, -3, new(62, 10, 102, 22), new(30, 25, 60), r, Foot(-0.17, -0.12)),
            At(0.505, -0.031, -4, -10, 4, 11, -2, new(48, 10, 96, 20), new(18, 20, 42), r, Foot(-0.17, -0.12)),
            At(0.508, -0.029, -3, -8, 3, 10, -2, new(42, 10, 95, 20), new(12, 18, 36), r, Foot(-0.17, -0.12)),
        ];
    }

    // ================================================================== emotion: hesitation

    /// <summary>Hesitation: the weight shifts forward and the right foot starts a step, hangs in the air, is drawn back
    /// and set down where it was; the weight settles back and the right hand comes up to the chin, the head lowered
    /// and turning slightly, then the hand drops.</summary>
    private static Pose[] HesitatePoses()
    {
        var l = Foot(-0.015, -0.1);
        Pose At(double height, double forward, double side, double lean, double head, double look, Limb right, Limb left, FootAt rf, double tilt = 0) =>
            Plant(new Pose { Forward = forward, Sideways = side, Lean = lean, HeadPitch = head, HeadYaw = look, Tilt = tilt, ArmR = right, ArmL = left }, height, rf, l);
        Limb rest = new(0, 7, 12);
        return
        [
            Standing(),
            At(0.528, 0.01, -0.02, 4, 2, 0, new(-3, 7, 12), new(3, 7, 12), Foot(-0.015, 0.1, pitch: 14)),
            At(0.522, 0.03, -0.026, 6, 3, 0, new(-8, 7, 14), new(8, 7, 16), Foot(0.02, 0.1, 0.035, 10)),
            At(0.524, 0.045, -0.028, 6, 8, -8, new(-10, 7, 14), new(10, 7, 18), Foot(0.12, 0.1, 0.035, 0)),
            At(0.525, 0.046, -0.028, 4, 12, -10, new(-9, 7, 14), new(9, 7, 18), Foot(0.135, 0.1, 0.03, -2)),
            At(0.526, 0.03, -0.022, 1, 11, -6, new(-4, 7, 13), new(4, 7, 15), Foot(0.09, 0.1, 0.028, 2)),
            At(0.528, 0.01, -0.01, -1, 9, -2, rest, rest, Foot(0.0, 0.1, 0.012, 4)),
            At(0.53, -0.005, 0.0, -1, 13, -4, new(62, 16, 128, 34), rest, Foot(-0.015, 0.1), tilt: 3),
            At(0.53, -0.006, 0.0, -1, 14, 6, new(66, 16, 132, 34), rest, Foot(-0.015, 0.1), tilt: 3),
            At(0.531, -0.004, 0.0, 0, 10, 0, new(8, 9, 30, 8), rest, Foot(-0.015, 0.1), tilt: 1),
        ];
    }

    // ================================================================== emotion: sadness

    /// <summary>Sadness / crying: slumped, head down, the left hand over the eyes and the right arm hanging. Two quick
    /// catches of breath lift the chest and head (a sob), then a long, slow sag back down; the knees soften with it.</summary>
    private static Pose[] SadPoses()
    {
        double[] lean = [17, 12, 8, 9.5, 12.5, 15.5, 17.5, 17.5];
        double[] head = [27, 22, 16, 18, 22, 26, 28.5, 28];
        double[] height = [0.51, 0.516, 0.521, 0.52, 0.516, 0.512, 0.508, 0.509];
        double[] shrug = [6, 11, 14, 13, 10, 7, 5, 5.5];
        return
        [
            .. Enumerable.Range(0, 8).Select(f =>
            {
                var p = Plant(new Pose
                {
                    Lean = lean[f], HeadPitch = head[f], HeadYaw = -8, Tilt = -2, Twist = -4,
                    ArmR = new(4, shrug[f], 12), ArmL = new(40, 10, 120),
                }, height[f], Foot(-0.01, 0.08), Foot(-0.03, -0.09));
                var face = PoseSolver.At(p, height[f]).Head;
                return PoseSolver.Reach(p, height[f], null, face + V(-0.005, -0.025, 0.07));
            }),
        ];
    }

    // ================================================================== emotion: laughing

    /// <summary>Laughing: irregular bursts, a big one and two smaller, then a quick breath in. Each burst folds the chest
    /// forward onto the right hand on the belly and rocks back with the head up; the left hand slaps the thigh on the
    /// bigger ones and rests on it in between, lifting a little just before the next slap (Mission 016: it used to hang
    /// out in front in mid-air between slaps); the knees give a little each time.</summary>
    private static Pose[] LaughPoses()
    {
        double[] lean = [18, -8, 12, -3, 8, -6, -2, 9];
        double[] head = [10, -24, 4, -13, 2, -18, -7, 3];
        double[] height = [0.505, 0.522, 0.51, 0.52, 0.514, 0.522, 0.53, 0.512];
        double[] twist = [-6, 4, -4, 2, -3, 5, 1, -5];
        double[] tilt = [2, -3, 1, -1, 2, -2, 0, 1];
        bool[] slap = [true, false, true, false, false, false, false, true];
        double[] lift = [0, 1, 0, 0.2, 0.15, 0.25, 1, 0]; // between slaps: resting, or lifting for the next one
        return
        [
            .. Enumerable.Range(0, 8).Select(f =>
            {
                var p = Plant(new Pose
                {
                    Lean = lean[f], HeadPitch = head[f], Twist = twist[f], Tilt = tilt[f],
                    ArmR = new(30, 0, 100), ArmL = new(20, 12, 40),
                }, height[f], Foot(0.0, 0.1), Foot(-0.04, -0.11));
                var j = PoseSolver.At(p, height[f]);
                var belly = j.Pelvis + V(0.02, 0.13, 0.09);
                var thigh = (j.HipL + j.KneeL) / 2 + V(-0.02, 0.03, 0.06);
                var hand = slap[f] ? thigh : thigh + V(-0.02, 0.015 + 0.075 * lift[f], 0.02 * lift[f]);
                return PoseSolver.Reach(p, height[f], belly, hand);
            }),
        ];
    }

    // ================================================================== emotion: exhausted

    /// <summary>Exhausted idle: bent over with both hands on the knees, the hips back and the weight sunk into the legs,
    /// head up. A quick, heavy breath in straightens the back a little and lifts the head; the breath out is long.</summary>
    private static Pose[] ExhaustedPoses()
    {
        var breath = Curve(true, (0.0, 0), (0.25, 1), (0.4, 0.85), (1.0, 0)); // quick in, long out
        return
        [
            .. Enumerable.Range(0, 8).Select(f =>
            {
                double b = breath(f / 8.0), height = 0.44 + 0.008 * b;
                var p = Plant(new Pose
                {
                    Forward = -0.06, Lean = 46 - 7 * b, HeadPitch = -16 - 9 * b, Tilt = 1.5,
                    ArmR = new(40, 12, 20), ArmL = new(40, 12, 20),
                }, height, Foot(0.02, 0.13), Foot(-0.04, -0.13));
                var j = PoseSolver.At(p, height);
                return PoseSolver.Reach(p, height, j.KneeR + V(-0.005, 0.03, 0.04), j.KneeL + V(0.005, 0.03, 0.04));
            }),
        ];
    }

    // ================================================================== transitions: idle → walk

    /// <summary>Idle → walk (10 fps, like the walk): from Idle's first drawing (weight already on the right leg), the
    /// weight settles fully over the right foot, the left heel peels, the left foot steps through while the body falls
    /// forward and speeds up, landing on Walk's halfway drawing; the last three drawings are Walk's own, the last one
    /// just before its first.</summary>
    private static Pose[] IdleToWalkPoses()
    {
        var walk = WalkSpec;
        var r = new Fp(0.03, 0.1);          // where Idle has the right foot; it stays planted until Walk lifts it
        var l = new Fp(-0.06, -0.11);
        var contact = Moment(walk, 0.5, 0.15);
        contact = contact with { R = contact.R with { X = 0.1 } };
        return
        [
            IdlePoses()[0],
            OnGround(new Pose { Sideways = 0.03, Tilt = 2, Lean = 2, ArmR = new(1, 7, 14), ArmL = new(-2, 8, 14) },
                0, 0.526, r, l with { Pitch = 14 }),
            OnGround(new Pose { Sideways = 0.032, Tilt = 2, Lean = 4, Twist = -1, ArmR = new(5, 7, 16), ArmL = new(-5, 7, 14) },
                0.005, 0.523, r with { Pitch = 2 }, new Fp(-0.04, -0.1, 0.03, 24)),
            OnGround(new Pose { Sideways = 0.02, Tilt = 1.5, Lean = 5, Twist = -3, HeadYaw = 2, ArmR = new(10, 7, 20), ArmL = new(-10, 7, 16) },
                0.025, 0.528, r with { Pitch = 4 }, new Fp(0.1, -0.08, 0.055, 4)),
            OnGround(new Pose { Sideways = 0.006, Tilt = 0.5, Lean = 5, Twist = -5, HeadYaw = 4, ArmR = new(15, 7, 24), ArmL = new(-15, 7, 18) },
                0.075, 0.52, r with { Pitch = 9 }, new Fp(0.25, -0.065, 0.03, -12)),
            OnGround(contact, 0.15),
            OnGround(Moment(walk, 0.625, 0.225), 0.225),
            OnGround(Moment(walk, 0.75, 0.3), 0.3),
            OnGround(Moment(walk, 0.875, 0.375), 0.375),
        ];
    }

    // ================================================================== transitions: walk → run

    /// <summary>Walk → run (12 fps, like the run): from Walk's first drawing (right heel down) the left leg swings
    /// through higher and lands as a jog, the right leg leaves with a heel kick and drives its knee forward; the lean
    /// grows, the arms bend and pump harder, the body speeds up; the last drawing is Run's last.</summary>
    private static Pose[] WalkToRunPoses()
    {
        var start = Moment(WalkSpec, 0, 0);
        var end = Moment(RunSpec, 0.875, 0.6);
        Pose U(double lean, double twist, double head, double armR, double bendR, double armL, double bendL) =>
            new() { Lean = lean, Twist = twist, HeadYaw = -0.8 * twist, HeadPitch = head, ArmR = new(armR, 7, bendR), ArmL = new(armL, 7, bendL) };
        Fp R(double z, double lift = 0, double pitch = 0) => new(z, lift > 0 ? 0.045 : 0.06, lift, pitch); // the walk's width until it lifts
        Fp L(double z, double lift = 0, double pitch = 0) => new(z, lift > 0 && z < 0 ? -0.06 : -0.05, lift, pitch);
        return
        [
            OnGround(start, 0),
            OnGround(U(5, 2, -1, -5, 24, 8, 30), 0.065, 0.5, R(0.18), L(-0.09, 0.03, 38)),
            OnGround(U(7, -4, -2, 12, 40, -8, 40), 0.135, 0.505, R(0.18), L(0.12, 0.1, 12)),
            OnGround(U(8, -8, -3, 24, 55, -18, 52), 0.215, 0.512, R(0.18, pitch: 20), L(0.33, 0.06, -4)),
            OnGround(U(9, -11, -4, 32, 66, -24, 62), 0.3, 0.49, R(0.18, pitch: 40), L(0.43, pitch: -10)),
            OnGround(U(10, -6, -5, 22, 74, -14, 74), 0.39, 0.478, R(0.24, 0.16, 40), L(0.43)),
            OnGround(U(10, 2, -5, 5, 80, 12, 82), 0.49, 0.5, R(0.52, 0.2, 20), L(0.43, pitch: 25)),
            OnGround(end, 0.6),
        ];
    }

    // ================================================================== transitions: run → stop

    /// <summary>Run → stop (12 fps): from Run's first drawing the left leg comes through low instead of high, reaches far
    /// ahead and brakes heel first with the body leaning back; the right foot makes a second, shorter braking step; the
    /// body, still moving, catches up over the feet and the trunk pitches forward past upright before it settles.</summary>
    private static Pose[] RunToStopPoses()
    {
        var start = Moment(RunSpec, 0, 0);
        Pose U(double lean, double head, double armR, double bendR, double spreadR, double armL, double bendL, double spreadL, double twist = 0) =>
            new() { Lean = lean, HeadPitch = head, Twist = twist, HeadYaw = -0.6 * twist, ArmR = new(armR, spreadR, bendR), ArmL = new(armL, spreadL, bendL) };
        Fp R(double z, double lift = 0, double pitch = 0, double x = 0.04) => new(z, x, lift, pitch); // the run's width
        Fp L(double z, double lift = 0, double pitch = 0, double x = -0.06) => new(z, x, lift, pitch);
        return
        [
            OnGround(start, 0),
            OnGround(U(4, -4, 10, 76, 8, 6, 80, 8, 4), 0.12, 0.47, R(0.16), L(-0.02, 0.12, 30)),
            OnGround(U(0, -2, 26, 60, 12, -12, 64, 10, 2), 0.23, 0.49, R(0.16, pitch: 20), L(0.3, 0.06, -10)),
            OnGround(U(-8, 2, 34, 40, 18, -6, 46, 16, -4), 0.33, 0.465, R(0.16, pitch: 38), L(0.53, pitch: -22)),
            OnGround(U(-10, 4, 30, 34, 22, 14, 36, 20, -4), 0.41, 0.44, R(0.35, 0.06, 6, 0.08), L(0.53)),
            OnGround(U(-6, 4, 22, 30, 20, 24, 32, 20), 0.47, 0.445, R(0.57, pitch: -8, x: 0.09), L(0.53)),
            OnGround(U(2, 6, 26, 26, 16, 26, 28, 16), 0.51, 0.47, R(0.57, x: 0.09), L(0.53)),
            OnGround(U(6, 7, 16, 20, 12, 16, 22, 12), 0.535, 0.5, R(0.57, x: 0.09), L(0.53)),
            OnGround(U(1, 2, 2, 14, 8, 1, 14, 8), 0.545, 0.525, R(0.57, x: 0.09), L(0.53)),
        ];
    }

    // ================================================================== transitions: stand → crouch

    /// <summary>Stand → crouch: hips back, knees forward and heels coming up together, slowly at first and faster as it
    /// drops, a small dip past the final crouch, then settled low on the balls of the feet, arms forward for balance.</summary>
    private static Pose[] CrouchPoses()
    {
        Pose At(double height, double forward, double lean, double head, double heels, double arm, double bend) =>
            Plant(new Pose { Forward = forward, Lean = lean, HeadPitch = head, ArmR = new(arm, 10, bend), ArmL = new(arm, 10, bend) },
                height, Foot(-0.015, 0.1, pitch: heels), Foot(-0.015, -0.1, pitch: heels));
        return
        [
            Standing(),
            At(0.5, -0.02, 9, -3, 0, 8, 18),
            At(0.43, -0.05, 22, -10, 6, 22, 26),
            At(0.35, -0.066, 31, -16, 14, 34, 36),
            At(0.367, -0.06, 28, -15, 12, 32, 40),
            At(0.362, -0.062, 27, -16, 12, 30, 42),
        ];
    }

    // ================================================================== transitions: turn 180°

    /// <summary>A foot on the ground by the ball it pivots on (x, z), with its heading (degrees to the right of the
    /// starting direction), heel lift and height off the ground.</summary>
    private static Fp Ball(double x, double z, double yaw, double pitch = 0, double lift = 0)
    {
        double a = yaw * Math.PI / 180, ball = PoseSolver.BallForward;
        return new Fp(z - ball * Math.Cos(a), x - ball * Math.Sin(a), lift, pitch, yaw);
    }

    /// <summary>A turning drawing: the body faces <paramref name="turn"/> degrees to the right of where it started, its
    /// pelvis over (cx, cz); feet are given on the ground as seen from above, then placed in the body's own frame.</summary>
    private static Pose Turning(Pose upper, double turn, double cx, double cz, double height, Fp r, Fp l)
    {
        double a = turn * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        (double X, double Z) Body(double x, double z) => (x * c - z * s, x * s + z * c);
        var (px, pz) = Body(cx, cz);
        FootAt F(Fp f)
        {
            var (x, z) = Body(f.X, f.Z);
            return new FootAt(z, f.Lift, f.Pitch, x, f.Yaw - turn);
        }
        var p = Plant(upper with { Turn = turn, Sideways = px, Forward = pz }, height, F(r), F(l));
        return r.Lift > 1e-4 && l.Lift > 1e-4 ? PoseSolver.Fly(p, height) : p;
    }

    /// <summary>
    /// Turn 180° to the right: the head turns first, the shoulders follow, then the hips. The right foot lifts and is set
    /// down turned out behind; the left pivots on its ball, then steps around in front to its new place; the right
    /// pivots and adjusts. The pelvis moves over whichever foot carries the weight.
    /// </summary>
    private static Pose[] TurnPoses()
    {
        Pose U(double twist, double look, double lean = 1, double armR = 0, double armL = 0) =>
            new() { Twist = twist, HeadYaw = look, Lean = lean, ArmR = new(armR, 9, 14), ArmL = new(armL, 9, 14) };
        var r0 = Ball(0.1, 0.07, 0);
        var l0 = Ball(-0.1, 0.07, 0);
        return
        [
            Standing(),
            Turning(U(14, 32, 1, -4, 6), 0, -0.03, 0, 0.524, Ball(0.1, 0.07, 0, 12), l0),
            Turning(U(22, 45, 2, -8, 10), 30, -0.04, 0, 0.515, Ball(0.05, -0.01, 60, 10, 0.04), Ball(-0.1, 0.07, 30, 14)),
            Turning(U(22, 32, 2, -6, 12), 80, -0.04, -0.04, 0.51, Ball(0.065, -0.115, 95), Ball(-0.1, 0.07, 60, 18)),
            Turning(U(25, 28, 2, 4, 8), 105, -0.03, -0.07, 0.512, Ball(0.065, -0.115, 105, 6), Ball(-0.02, 0.06, 110, 10, 0.03)),
            Turning(U(20, 18, 2, 8, 2), 140, -0.01, -0.07, 0.518, Ball(0.065, -0.115, 120, 10), Ball(0.07, 0.02, 150, 4, 0.05)),
            Turning(U(10, 6, 1, 4, -2), 168, 0.0, -0.05, 0.515, Ball(0.065, -0.115, 140, 14), Ball(0.1, -0.11, 180)),
            Turning(U(3, 1, 1, 0, 0), 178, 0.0, -0.04, 0.52, Ball(-0.03, -0.09, 165, 8, 0.03), Ball(0.1, -0.11, 180)),
            Turning(U(0, -2, 1, 0, 0), 180, 0.0, -0.04, 0.524, Ball(-0.1, -0.11, 180), Ball(0.1, -0.11, 180)),
            Turning(U(0, 0, 0, 0, 0), 180, 0.0, -0.04, 0.53, Ball(-0.1, -0.11, 180), Ball(0.1, -0.11, 180)),
        ];
    }
}
