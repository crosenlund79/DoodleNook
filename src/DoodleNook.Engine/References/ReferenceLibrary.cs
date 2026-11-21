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
/// To add a cycle, add one entry below: its drawings (feet placed with <see cref="PoseSolver"/> so planted feet stay put)
/// or a step cycle (<see cref="Steps"/>), a frame count,
/// a speed and a category. Every cycle is drawn by the same mannequin, so each one works in every view by itself.
/// </para>
/// </summary>
public static partial class ReferenceLibrary
{
    public const string Movement = "Movement", Combat = "Combat", Interaction = "Interaction", Emotion = "Emotion", Transitions = "Transitions";
    public static readonly IReadOnlyList<string> Categories = [Movement, Combat, Interaction, Emotion, Transitions];

    public static readonly IReadOnlyList<ReferenceCycle> Cycles = Build();

    public static ReferenceCycle? Find(string? id) => Cycles.FirstOrDefault(c => c.Id == id);

    // ================================================================== building blocks

    /// <summary>The drawings of a cycle, worked out once (placing feet and hands takes a few small calculations).</summary>
    private static Func<int, Pose> Cached(int frames, Func<int, Pose> make)
    {
        var cache = new Pose?[frames];
        return f => cache[f] ??= make(f);
    }

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    /// A step cycle in place with planted feet. Each foot is on the ground for <see cref="Duty"/> of the cycle and its
    /// contact point travels back at one steady speed (so the cycle can be moved along at that speed without the feet
    /// sliding), rolling from heel to toe; then it lifts, swings forward on an eased arc and lands again. The pelvis dips
    /// as weight is taken and rises over the standing leg, shifting sideways over it; shoulders and arms counter the hips.
    /// </summary>
    private sealed record StepSpec
    {
        public double Length { get; init; } = 0.36;     // how far a planted foot travels back while it is down
        public double Duty { get; init; } = 0.6;        // share of the cycle each foot is on the ground
        public double Height { get; init; } = 0.52;     // the pelvis at its lowest (standing ≈ 0.54)
        public double Bob { get; init; } = 0.02;        // how much higher it is at its highest
        public double LowAt { get; init; } = 0.12;      // when it is lowest, as a share of a step after contact
        public double Center { get; init; } = 0.02;     // the middle of a stance, forward of the hips
        public double Width { get; init; } = 0.09;      // the ankles either side of the centre line
        public double Lift { get; init; } = 0.07;       // the swinging foot's clearance at its highest
        public double LiftAt { get; init; } = 0.42;     // when in its swing it is highest (0–1)
        public double TravelDelay { get; init; }        // share of the swing spent lifting before moving forward
        public double Strike { get; init; } = -16;      // foot pitch at contact (negative: heel first)
        public double Flat { get; init; } = 0.18;       // share of the stance until the foot is flat
        public double HeelOff { get; init; } = 0.6;     // share of the stance when the heel starts to rise
        public double ToeOff { get; init; } = 32;       // foot pitch as it leaves the ground
        public double Reach { get; init; } = 0;         // foot pitch in mid-swing
        public double Lean { get; init; } = 3;
        public double LeanBob { get; init; }            // extra lean at the pelvis' low point
        public double Twist { get; init; } = 6;         // shoulders turning against the hips
        public double Sway { get; init; } = 0.012;      // pelvis over the standing foot
        public double Tilt { get; init; } = 1.5;        // shoulders over the standing foot
        public double ArmSwing { get; init; } = 18;
        public double ArmForward { get; init; }
        public double ArmBend { get; init; } = 14;
        public double ArmBendSwing { get; init; } = 14; // elbows bend more as the arm swings forward
        public double ArmSpread { get; init; } = 7;
        public double ArmTwist { get; init; }
        public double ArmLag { get; init; } = 0.04;     // arms follow the legs a little late
        public double HeadPitch { get; init; }
        public double HeadNod { get; init; }            // the head nods against the pelvis bob
        public bool Backwards { get; init; }
        public PropKind Prop { get; init; }
        /// <summary>Hands placed on something (push, pull): the pose and pelvis height in, the pose out.</summary>
        public Func<Pose, double, Pose>? Hands { get; init; }

        // An uneven gait (a limp): the left foot can land at another moment, stay down for another share of the cycle
        // (its stance length follows, at the same ground speed) and lift and roll differently.
        public double? LeftDuty { get; init; }
        public double LeftAt { get; init; } = 0.5;
        public double? LeftLift { get; init; }
        public double? LeftStrike { get; init; }
        public double? LeftToeOff { get; init; }
        public double? LeftHeelOff { get; init; }
        /// <summary>The right foot swings out to the side by this much mid-swing (a stiff leg swung around).</summary>
        public double RightSwingOut { get; init; }
        /// <summary>Changes the upper body for a moment of the cycle (t in 0–1).</summary>
        public Func<double, Pose, Pose>? Shape { get; init; }
        /// <summary>The pelvis height for a moment of the cycle, instead of the even bob.</summary>
        public Func<double, double>? HeightAt { get; init; }
    }

    /// <summary>One moment (t in 0–1, any value) of a step cycle: the upper body, the pelvis height and both feet, before
    /// they are placed. Transitions use it to arrive exactly at a cycle's drawing.</summary>
    private static (Pose Upper, double Height, FootAt Right, FootAt Left) StepFrame(StepSpec g, double t)
    {
        FootAt Foot(double q, int side)
        {
            q -= Math.Floor(q);
            bool left = side < 0;
            double duty = left ? g.LeftDuty ?? g.Duty : g.Duty;
            double length = left && g.LeftDuty is { } ld ? g.Length * ld / g.Duty : g.Length;
            double strike = left ? g.LeftStrike ?? g.Strike : g.Strike;
            double toeOff = left ? g.LeftToeOff ?? g.ToeOff : g.ToeOff;
            double heelOff = left ? g.LeftHeelOff ?? g.HeelOff : g.HeelOff;
            double liftMax = left ? g.LeftLift ?? g.Lift : g.Lift;
            double x = side * g.Width;
            if (q < duty)
            {
                double s = q / duty;
                double pitch = s < g.Flat ? strike * (1 - Smooth(s / g.Flat))
                    : s > heelOff ? toeOff * Math.Pow((s - heelOff) / (1 - heelOff), 1.6)
                    : 0;
                return new FootAt(g.Center + length / 2 - length * s, 0, pitch, x);
            }
            double u = (q - duty) / (1 - duty);
            double v = Math.Clamp((u - g.TravelDelay) / (1 - g.TravelDelay), 0, 1);
            double travel = v * v * v * (v * (v * 6 - 15) + 10); // eased out of the back and into the front
            double peak = Math.Log(0.5) / Math.Log(g.LiftAt);
            double lift = liftMax * Math.Sin(Math.PI * Math.Pow(u, peak));
            double swingPitch = u < 0.5 ? Lerp(toeOff, g.Reach, Smooth(u / 0.5)) : Lerp(g.Reach, strike, Smooth((u - 0.5) / 0.5));
            if (side > 0 && g.RightSwingOut != 0) x += g.RightSwingOut * Math.Sin(Math.PI * u);
            return new FootAt(g.Center - length / 2 + length * travel, lift, swingPitch, x);
        }
        var right = Foot(t, 1);
        var left = Foot(t - g.LeftAt, -1);
        double step = (t - g.LowAt * 0.5) / 0.5;
        double rise = 0.5 * (1 - Math.Cos(2 * Math.PI * step)); // 0 at the low point, 1 half a step later
        double height = g.HeightAt?.Invoke(t) ?? g.Height + g.Bob * rise;
        double side = Math.Cos(2 * Math.PI * (t - g.Duty / 2));   // +1 over the right foot, −1 over the left
        double arm = Math.Cos(2 * Math.PI * (t - g.ArmLag));      // +1: right leg forward, left arm forward
        Limb Arm(double phase) => new(g.ArmForward + g.ArmSwing * phase, g.ArmSpread, g.ArmBend + g.ArmBendSwing * (phase + 1) / 2, g.ArmTwist);
        var upper = new Pose
        {
            Sideways = g.Sway * side,
            Tilt = g.Tilt * side,
            Lean = g.Lean + g.LeanBob * (1 - rise),
            Twist = g.Twist * arm,
            HeadYaw = -g.Twist * arm * 0.8,
            HeadPitch = g.HeadPitch + g.HeadNod * (1 - rise),
            ArmR = Arm(-arm),
            ArmL = Arm(arm),
            LegR = new Limb(0, 3, 0),
            LegL = new Limb(0, 3, 0),
            Prop = g.Prop,
        };
        if (g.Shape is { } shape) upper = shape(t, upper);
        return (upper, height, right, left);
    }

    /// <summary>Plants a step-cycle moment: feet placed, hands on what they hold, airborne when both feet are up.</summary>
    private static Pose StepPose(StepSpec g, (Pose Upper, double Height, FootAt Right, FootAt Left) m)
    {
        var pose = PoseSolver.Plant(m.Upper, m.Height, m.Right, m.Left);
        if (g.Hands is { } hands) pose = hands(pose, m.Height);
        return m.Right.Lift > 1e-4 && m.Left.Lift > 1e-4 ? PoseSolver.Fly(pose, m.Height) : pose;
    }

    private static Func<int, Pose> Steps(int frames, StepSpec g) => Cached(frames, frame =>
    {
        double t = (double)frame / frames;
        if (g.Backwards) t = (1 - t) % 1;
        return StepPose(g, StepFrame(g, t));
    });

    /// <summary>Both hands on the near face of the crate the body pushes or pulls, <paramref name="y"/> above the ground.
    /// The crate stands 0.44 in front of the pelvis and is 0.34 tall (see <see cref="Mannequin"/>).</summary>
    private static Func<Pose, double, Pose> OnCrate(double y) => (pose, height) =>
    {
        float z = (float)(pose.Forward + 0.44 - 0.17 - 0.032), x = (float)pose.Sideways;
        return PoseSolver.Reach(pose, height, new System.Numerics.Vector3(x + 0.09f, (float)y, z), new System.Numerics.Vector3(x - 0.09f, (float)y, z));
    };

    /// <summary>A cycle given as its drawings, worked out once when first needed.</summary>
    private static Func<int, Pose> Drawings(Func<Pose[]> make)
    {
        var all = new Lazy<Pose[]>(make);
        return f => all.Value[f];
    }

    /// <summary>A foot <paramref name="z"/> forward and <paramref name="x"/> to the side, flat on the ground unless lifted or pitched.</summary>
    private static FootAt Foot(double z, double x, double lift = 0, double pitch = 0) => new(z, lift, pitch, x);

    private static Pose Plant(Pose upper, double height, FootAt right, FootAt left) => PoseSolver.Plant(upper, height, right, left);

    private static System.Numerics.Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);

    /// <summary>The relaxed standing pose with both feet flat on the ground, knees soft.</summary>
    private static Pose Standing(Pose? upper = null) =>
        PoseSolver.Plant(upper ?? new Pose(), 0.532, new FootAt(-0.015, X: 0.1), new FootAt(-0.015, X: -0.1));

    /// <summary>The pose moved along the ground so the chosen joint ends up at forward position <paramref name="z"/>
    /// (the body still rests on its lowest point).</summary>
    private static Pose PinZ(Pose pose, Func<PoseSolver.Joints, System.Numerics.Vector3> joint, double z)
    {
        var p = pose;
        for (int i = 0; i < 6; i++)
        {
            double err = z - joint(PoseSolver.Measure(Mannequin.Build(p))).Z;
            if (Math.Abs(err) < 1e-5) break;
            p = p with { Forward = p.Forward + err / Math.Max(0.25, Math.Cos(p.BodyPitch * Math.PI / 180)) };
        }
        return p;
    }

    /// <summary>
    /// Death: struck, the knees buckle and the body drops faster and faster until the knees hit the ground; the torso
    /// slumps, topples forward from the knees, accelerating, hits the ground chest first and settles face down, head
    /// turned, one arm by the head and one at the side. The feet stay where they stood until the knees land.
    /// </summary>
    private static Pose[] DeathPoses()
    {
        static double KneeZ(PoseSolver.Joints j) => (j.KneeR.Z + j.KneeL.Z) / 2;
        var knees = new Func<PoseSolver.Joints, System.Numerics.Vector3>(j => new System.Numerics.Vector3(0, 0, (float)KneeZ(j)));
        const double feet = -0.015;
        return
        [
            Standing(),
            // Hit: the chest and head snap back, the pelvis is pushed back over the heels, arms fly up loosely.
            PoseSolver.Plant(new Pose { Forward = -0.035, Lean = -16, HeadPitch = -24, Tilt = 4, ArmR = new(32, 28, 52), ArmL = new(46, 34, 38) },
                0.515, new FootAt(feet, X: 0.1), new FootAt(feet, X: -0.1)),
            // The knees give way: the body sags, the torso starts to fold, the head drops, the arms go limp; heels lift.
            PoseSolver.Plant(new Pose { Forward = 0.0, Lean = 10, HeadPitch = 16, Tilt = -3, ArmR = new(14, 16, 24), ArmL = new(8, 18, 30) },
                0.455, new FootAt(feet, Pitch: 22, X: 0.1), new FootAt(feet, Pitch: 22, X: -0.1)),
            // The knees hit the ground: kneeling, shins flat behind, torso slumped forward, arms hanging.
            PinZ(new Pose { LegR = new(18, 4, 108), LegL = new(22, 5, 112), ToeR = 82, ToeL = 82, Lean = 30, HeadPitch = 30, Tilt = -2,
                ArmR = new(-2, 12, 16), ArmL = new(-6, 14, 22) }, knees, 0.2),
            // Toppling from the knees, slowly at first: the body tips as one (the knees open as it goes), the head hangs.
            PinZ(new Pose { BodyPitch = 26, LegR = new(0, 4, 64), LegL = new(2, 5, 68), ToeR = 90, ToeL = 90, Lean = 16, HeadPitch = 24,
                ArmR = new(10, 14, 14), ArmL = new(4, 16, 20) }, knees, 0.21),
            // Falling fast, the arms trailing.
            PinZ(new Pose { BodyPitch = 68, LegR = new(0, 4, 22), LegL = new(2, 5, 26), ToeR = 90, ToeL = 90, Lean = 8, HeadPitch = 6,
                HeadYaw = 20, ArmR = new(-12, 24, 18), ArmL = new(-18, 20, 14) }, knees, 0.23),
            // Impact: chest and head hit the ground; the lower legs bounce up and an arm is still flopping down.
            PinZ(new Pose { BodyPitch = 90, LegR = new(7, 5, 22), LegL = new(8, 7, 30), ToeR = 90, ToeL = 90, Lean = 0, HeadPitch = 0,
                HeadYaw = 50, ArmR = new(188, 44, 20), ArmL = new(-6, 18, 4) }, knees, 0.25),
            // At rest: the legs drop back, the head settles to the side, one arm lies by the head, the other at the side.
            PinZ(new Pose { BodyPitch = 90, LegR = new(7, 6, 2), LegL = new(8, 9, 4), ToeR = 90, ToeL = 90, Lean = 0, HeadPitch = 0,
                HeadYaw = 80, ArmR = new(172, 34, 70, 90), ArmL = new(9, 12, 0) }, knees, 0.25),
        ];
    }

    /// <summary>The walk and the run, by name, so transitions can arrive exactly at their drawings.</summary>
    private static StepSpec WalkSpec => new()
    {
        Length = 0.36, Duty = 0.6, Height = 0.505, Bob = 0.03, LowAt = 0.12, Center = 0, Width = 0.06,
        Lift = 0.06, Strike = -18, ToeOff = 35, Lean = 3, Twist = 7, ArmSwing = 20, ArmBend = 14, ArmBendSwing = 14,
    };

    private static StepSpec RunSpec => new()
    {
        Length = 0.32, Duty = 0.32, Height = 0.485, Bob = 0.045, LowAt = 0.22, Center = 0, Width = 0.04,
        Lift = 0.26, LiftAt = 0.45, TravelDelay = 0.3, Strike = -8, Flat = 0.15, HeelOff = 0.35, ToeOff = 45, Reach = 20,
        Lean = 10, LeanBob = 2, Twist = 12, Sway = 0.006, Tilt = 1, ArmSwing = 35, ArmForward = 10, ArmBend = 80,
        ArmBendSwing = 10, HeadPitch = -6,
    };

    // ================================================================== keyed drawings (feet planted)

    private static Pose[] IdlePoses() =>
    [
        .. Enumerable.Range(0, 6).Select(f =>
        {
            double b = Math.Sin(2 * Math.PI * f / 6);        // breathing: +1 in, −1 out
            double l = Math.Sin(2 * Math.PI * f / 6 - 0.7);  // shoulders and elbows follow a little late
            // Weight on the right leg, the left foot relaxed a little behind.
            return Plant(new Pose
            {
                Sideways = 0.014, Tilt = -1.5, Lean = 1.5 - 0.8 * b, HeadPitch = -1 + 0.8 * b,
                ArmL = new(-2, 8 + 1.2 * l, 14 + 3 * l), ArmR = new(-3, 7 + 1.2 * l, 16 + 3 * l),
            }, 0.526 + 0.003 * b, Foot(0.03, 0.1), Foot(-0.06, -0.11));
        }),
    ];

    private static Pose[] JumpPoses()
    {
        Pose Air(double height, Pose p) => PoseSolver.Fly(p, height);
        return
        [
            Standing(),
            // Anticipation: hips back and down, arms swung back.
            Plant(new Pose { Forward = -0.035, Lean = 34, HeadPitch = -16, ArmL = new(-48, 10, 22), ArmR = new(-48, 10, 22) },
                0.395, Foot(-0.015, 0.1), Foot(-0.015, -0.1)),
            // Push off: legs straight, up on the toes, arms thrown up.
            Plant(new Pose { Forward = 0.02, Lean = 6, HeadPitch = -6, ArmL = new(150, 12, 15), ArmR = new(150, 12, 15) },
                0.58, Foot(-0.015, 0.1, pitch: 42), Foot(-0.015, -0.1, pitch: 42)),
            // In the air on a gravity arc (fast up, hang at the top, faster down): tuck, then reach for the ground.
            Air(0.74, new Pose { Lean = 2, LegL = new(15, 4, 35), LegR = new(25, 4, 50), ToeL = 30, ToeR = 30, ArmL = new(160, 18, 15), ArmR = new(160, 18, 15) }),
            Air(0.815, new Pose { Lean = 10, LegL = new(55, 5, 95), LegR = new(60, 5, 100), ToeL = 20, ToeR = 20, ArmL = new(115, 32, 35), ArmR = new(115, 32, 35) }),
            Air(0.805, new Pose { Lean = 8, LegL = new(45, 5, 80), LegR = new(50, 5, 85), ToeL = 15, ToeR = 15, ArmL = new(100, 40, 30), ArmR = new(100, 40, 30) }),
            Air(0.71, new Pose { Lean = 4, LegL = new(18, 4, 28), LegR = new(22, 4, 32), ToeL = 10, ToeR = 10, ArmL = new(85, 50, 20), ArmR = new(85, 50, 20) }),
            Air(0.6, new Pose { Lean = 6, LegL = new(12, 4, 12), LegR = new(14, 4, 14), ToeL = 8, ToeR = 8, ArmL = new(70, 45, 20), ArmR = new(70, 45, 20) }),
        ];
    }

    private static Pose[] LandPoses()
    {
        FootAt r = Foot(0.0, 0.11), l = Foot(0.0, -0.11);
        return
        [
            PoseSolver.Fly(new Pose { Lean = 6, LegL = new(10, 4, 10), LegR = new(12, 4, 12), ToeL = 8, ToeR = 8, ArmL = new(70, 45, 20), ArmR = new(70, 45, 20) }, 0.565),
            // Impact: knees and hips absorb it in one quick drop, arms out for balance.
            Plant(new Pose { Forward = -0.03, Lean = 34, HeadPitch = -16, ArmL = new(40, 26, 30), ArmR = new(40, 26, 30) }, 0.37, r, l),
            Plant(new Pose { Forward = -0.01, Lean = 14, ArmL = new(12, 14, 26), ArmR = new(12, 14, 26) }, 0.47, r, l),
            Plant(new Pose(), 0.532, r, l),
        ];
    }

    /// <summary>A fighting stance: right foot forward, left back, both planted; the pelvis carries the weight.</summary>
    private static Pose Stance(Pose upper, double height, double forward, double backHeel = 0) =>
        Plant(upper with { Forward = forward }, height, Foot(0.11, 0.1), Foot(-0.15, -0.11, pitch: backHeel));

    private static Pose[] SlashPoses() =>
    [
        Stance(new Pose { Prop = PropKind.Sword, PropAngle = 70, ArmR = new(40, 10, 70), ArmL = new(20, 18, 45) }, 0.5, 0),
        Stance(new Pose { Prop = PropKind.Sword, PropAngle = 40, Twist = 22, Lean = -6, ArmR = new(165, 25, 70), ArmL = new(35, 25, 40), HeadPitch = -6 }, 0.505, -0.035),
        Stance(new Pose { Prop = PropKind.Sword, PropAngle = 20, Twist = 8, Lean = 4, ArmR = new(125, 14, 25), ArmL = new(20, 25, 40) }, 0.5, -0.01),
        Stance(new Pose { Prop = PropKind.Sword, PropAngle = 8, Twist = -18, Lean = 16, ArmR = new(62, 6, 4), ArmL = new(-15, 30, 35), HeadPitch = 8 }, 0.465, 0.05, 14),
        Stance(new Pose { Prop = PropKind.Sword, PropAngle = 14, Twist = -26, Lean = 20, ArmR = new(22, 8, 6), ArmL = new(-25, 30, 30), HeadPitch = 8 }, 0.46, 0.06, 18),
        Stance(new Pose { Prop = PropKind.Sword, PropAngle = 45, Twist = -10, Lean = 9, ArmR = new(32, 10, 45), ArmL = new(5, 22, 40) }, 0.485, 0.02, 4),
        Stance(new Pose { Prop = PropKind.Sword, PropAngle = 70, ArmR = new(40, 10, 70), ArmL = new(20, 18, 45) }, 0.5, 0),
    ];

    private static Pose[] ThrustPoses()
    {
        Pose At(Pose upper, double height, double forward, FootAt front, double backHeel = 0) =>
            Plant(upper with { Forward = forward }, height, front, Foot(-0.15, -0.11, pitch: backHeel));
        var guard = new Pose { Prop = PropKind.Sword, PropAngle = 82, ArmR = new(28, 6, 82), ArmL = new(25, 20, 60) };
        return
        [
            At(guard, 0.5, 0, Foot(0.11, 0.1)),
            // Draw back: weight onto the back leg, the front foot lifts to step.
            At(new Pose { Prop = PropKind.Sword, PropAngle = 88, Twist = 16, Lean = -4, ArmR = new(6, 12, 112), ArmL = new(35, 22, 55) }, 0.495, -0.045, Foot(0.13, 0.1, lift: 0.03, pitch: 6)),
            // Lunge: a long step forward, the back leg straight and its heel up, the blade straight ahead.
            At(new Pose { Prop = PropKind.Sword, PropAngle = 2, Twist = -16, Lean = 16, ArmR = new(86, 2, 2), ArmL = new(-30, 28, 20) }, 0.435, 0.1, Foot(0.3, 0.1), 22),
            At(new Pose { Prop = PropKind.Sword, PropAngle = 2, Twist = -18, Lean = 17, ArmR = new(88, 2, 0), ArmL = new(-32, 28, 18) }, 0.43, 0.11, Foot(0.3, 0.1), 24),
            // Recover: push back off the front foot and step it back to guard.
            At(new Pose { Prop = PropKind.Sword, PropAngle = 70, Twist = 4, Lean = 5, ArmR = new(30, 8, 75), ArmL = new(15, 22, 55) }, 0.485, 0.02, Foot(0.19, 0.1, lift: 0.03, pitch: 4), 6),
            At(guard, 0.5, 0, Foot(0.11, 0.1)),
        ];
    }

    private static Pose[] HurtPoses()
    {
        FootAt r = Foot(-0.015, 0.1), l = Foot(-0.015, -0.1);
        return
        [
            Standing(),
            // The hit: head and chest snap back, the hips are pushed back and the body rocks onto its heels.
            Plant(new Pose { Forward = -0.055, Lean = -20, HeadPitch = -22, ArmL = new(25, 38, 35), ArmR = new(25, 38, 35) }, 0.515,
                Foot(-0.015, 0.1, pitch: -10), Foot(-0.015, -0.1, pitch: -12)),
            // Absorb: the knees give a little as the weight comes back over the feet.
            Plant(new Pose { Forward = -0.025, Lean = -6, HeadPitch = -4, ArmL = new(12, 24, 30), ArmR = new(12, 24, 30) }, 0.48, r, l),
            Plant(new Pose { Lean = 3, ArmL = new(2, 10, 18), ArmR = new(2, 10, 18) }, 0.525, r, l),
        ];
    }

    private static Pose[] CastPoses()
    {
        var back = Foot(-0.06, -0.11);
        Pose At(Pose upper, double height, double forward, FootAt front) => Plant(upper with { Prop = PropKind.Orb, Forward = forward }, height, front, back);
        return
        [
            At(new Pose { PropAmount = 0 }, 0.53, 0, Foot(0.03, 0.1)),
            At(new Pose { PropAmount = 0.35, Lean = -3, ArmL = new(28, 6, 62, 42), ArmR = new(28, 6, 62, 42) }, 0.52, 0, Foot(0.03, 0.1)),
            At(new Pose { PropAmount = 0.6, Lean = -6, HeadPitch = -12, ArmL = new(105, 8, 40, 30), ArmR = new(105, 8, 40, 30) }, 0.525, -0.01, Foot(0.03, 0.1)),
            // Rising onto the back leg, the front foot lifting to step into the push.
            At(new Pose { PropAmount = 0.9, Lean = -9, HeadPitch = -22, ArmL = new(162, 14, 22, 20), ArmR = new(162, 14, 22, 20) }, 0.53, -0.03, Foot(0.08, 0.1, lift: 0.035, pitch: 10)),
            At(new Pose { PropAmount = 1.2, Lean = 12, ArmL = new(86, 6, 6, 18), ArmR = new(86, 6, 6, 18) }, 0.48, 0.07, Foot(0.22, 0.1)),
            At(new Pose { PropAmount = 0.3, Lean = 6, ArmL = new(66, 10, 12, 18), ArmR = new(66, 10, 12, 18) }, 0.49, 0.05, Foot(0.22, 0.1)),
        ];
    }

    private static Pose[] ShootPoses()
    {
        Pose At(Pose upper, double forward) => Plant(upper with { Prop = PropKind.Gun, Forward = forward }, 0.515, Foot(0.09, 0.1), Foot(-0.12, -0.11));
        return
        [
            At(new Pose { ArmR = new(28, 6, 62, 10), ArmL = new(38, 12, 72, 45) }, 0),
            At(new Pose { ArmR = new(70, 4, 32, 18), ArmL = new(76, 6, 26, 48), HeadPitch = 4 }, 0.01),
            At(new Pose { Lean = 4, ArmR = new(86, 2, 14, 20), ArmL = new(90, 2, 18, 52), HeadPitch = 6 }, 0.02),
            // Recoil: the shoulders and hips are knocked back a little; the feet stay.
            At(new Pose { Lean = -6, ArmR = new(100, 2, 18, 20), ArmL = new(104, 2, 22, 52), HeadPitch = -2 }, -0.012),
            At(new Pose { Lean = 2, ArmR = new(90, 2, 15, 20), ArmL = new(94, 2, 19, 52), HeadPitch = 5 }, 0.012),
            At(new Pose { Lean = 4, ArmR = new(86, 2, 14, 20), ArmL = new(90, 2, 18, 52), HeadPitch = 6 }, 0.02),
        ];
    }

    /// <summary>Pick up: feet planted, the hips stay over them while the knees bend (so the box on the ground stays put),
    /// hands go to the sides of the box, and the legs lift it to the chest.</summary>
    private static Pose[] PickUpPoses()
    {
        Pose At(Pose upper, double height, double heel, (double Y, double Z)? hands)
        {
            var p = Plant(upper with { Prop = PropKind.Box }, height, Foot(-0.015, 0.12, pitch: heel), Foot(-0.015, -0.12, pitch: heel));
            return hands is { } h ? PoseSolver.Reach(p, height, V(0.11, h.Y, h.Z), V(-0.11, h.Y, h.Z)) : p;
        }
        return
        [
            At(new Pose { PropAmount = 0 }, 0.532, 0, null),
            At(new Pose { PropAmount = 0, Lean = 26, HeadPitch = 12, ArmL = new(40, 12, 18), ArmR = new(40, 12, 18) }, 0.44, 0, null),
            At(new Pose { PropAmount = 0, Lean = 50, HeadPitch = 10, ArmL = new(62, 14, 8, 20), ArmR = new(62, 14, 8, 20) }, 0.255, 12, (0.08, 0.28)),
            At(new Pose { PropAmount = 1, Lean = 50, HeadPitch = 10, ArmL = new(62, 14, 8, 20), ArmR = new(62, 14, 8, 20) }, 0.25, 14, (0.08, 0.28)),
            At(new Pose { PropAmount = 1, Lean = 28, HeadPitch = 4, ArmL = new(46, 10, 48, 32), ArmR = new(46, 10, 48, 32) }, 0.41, 4, (0.3, 0.22)),
            At(new Pose { PropAmount = 1, Lean = 4, ArmL = new(22, 8, 82, 44), ArmR = new(22, 8, 82, 44) }, 0.522, 0, (0.6, 0.15)),
            At(new Pose { PropAmount = 1, Lean = 1, ArmL = new(20, 8, 86, 46), ArmR = new(20, 8, 86, 46) }, 0.528, 0, (0.62, 0.14)),
        ];
    }

    /// <summary>Sit down: the stool stays where it is, so the hips go straight down onto it from a stand just in front
    /// of it, feet planted.</summary>
    private static Pose[] SitPoses()
    {
        FootAt r = Foot(0.13, 0.11), l = Foot(0.13, -0.11);
        Pose At(Pose upper, double height) => Plant(upper with { Prop = PropKind.Stool }, height, r, l);
        return
        [
            At(new Pose { Lean = 10, ArmL = new(6, 9, 16), ArmR = new(6, 9, 16) }, 0.515),
            At(new Pose { Lean = 24, HeadPitch = -6, ArmL = new(20, 10, 20), ArmR = new(20, 10, 20) }, 0.465),
            At(new Pose { Lean = 32, HeadPitch = -12, ArmL = new(30, 12, 25), ArmR = new(30, 12, 25) }, 0.395),
            At(new Pose { Lean = 24, HeadPitch = -8, ArmL = new(30, 12, 30), ArmR = new(30, 12, 30) }, 0.33),
            At(new Pose { Lean = 9, ArmL = new(28, 10, 40), ArmR = new(28, 10, 40) }, 0.308),
            At(new Pose { Lean = 4, ArmL = new(34, 8, 46), ArmR = new(34, 8, 46) }, 0.308),
        ];
    }

    private static Pose[] HammerPoses()
    {
        Pose At(Pose upper, double height, double forward) =>
            Plant(upper with { Prop = PropKind.Hammer, Forward = forward }, height, Foot(0.08, 0.1), Foot(-0.14, -0.11));
        return
        [
            At(new Pose { PropAngle = 90, Lean = 22, Twist = 12, ArmR = new(138, 16, 72), ArmL = new(38, 22, 60), HeadPitch = 14 }, 0.5, 0),
            At(new Pose { PropAngle = 85, Lean = 18, Twist = 16, ArmR = new(158, 18, 95), ArmL = new(40, 22, 60), HeadPitch = 14 }, 0.505, -0.01),
            At(new Pose { PropAngle = 85, Lean = 26, Twist = 4, ArmR = new(92, 10, 28), ArmL = new(36, 22, 60), HeadPitch = 18 }, 0.495, 0.005),
            // The strike: the body drops into it.
            At(new Pose { PropAngle = 82, Lean = 32, Twist = -4, ArmR = new(48, 6, 2), ArmL = new(34, 22, 60), HeadPitch = 20 }, 0.478, 0.018),
            At(new Pose { PropAngle = 88, Lean = 28, Twist = 2, ArmR = new(76, 8, 38), ArmL = new(36, 22, 60), HeadPitch = 18 }, 0.488, 0.012),
            At(new Pose { PropAngle = 90, Lean = 24, Twist = 8, ArmR = new(112, 14, 60), ArmL = new(38, 22, 60), HeadPitch = 16 }, 0.497, 0.004),
        ];
    }

    /// <summary>Dig: hands one above the other on the shaft (right on top), so the shovel points down to the ground in
    /// front: set the blade, push it in, lift the load, toss it to the side and come back.</summary>
    private static Pose[] DigPoses()
    {
        Pose At(Pose upper, double height, double forward, (double X, double Y, double Z) right, (double X, double Y, double Z) left)
        {
            var p = Plant(upper with { Prop = PropKind.Shovel, Forward = forward }, height, Foot(0.1, 0.11), Foot(-0.12, -0.11));
            return PoseSolver.Reach(p, height, V(right.X, right.Y, right.Z), V(left.X, left.Y, left.Z));
        }
        return
        [
            At(new Pose { Lean = 26, HeadPitch = 18, ArmR = new(12, -32, 85), ArmL = new(45, -12, 30) }, 0.47, 0.01, (0.0, 0.56, 0.1), (-0.02, 0.4, 0.24)),
            // Push in: the weight goes down through the arms onto the blade.
            At(new Pose { Lean = 32, HeadPitch = 20, ArmR = new(12, -32, 85), ArmL = new(45, -12, 30) }, 0.445, 0.03, (0.0, 0.5, 0.14), (-0.02, 0.34, 0.28)),
            // Lift the load: knees and back straighten, the shovel comes up level.
            At(new Pose { Lean = 18, HeadPitch = 10, ArmR = new(12, -32, 85), ArmL = new(45, -12, 30) }, 0.48, 0, (0.0, 0.48, 0.08), (-0.02, 0.46, 0.28)),
            // Toss it to the side: the shoulders turn and the shovel swings left and up.
            At(new Pose { Lean = 10, Twist = -26, HeadYaw = -14, ArmR = new(12, -32, 85), ArmL = new(45, -12, 30) }, 0.5, -0.02, (-0.06, 0.55, 0.12), (-0.2, 0.55, 0.22)),
            At(new Pose { Lean = 18, Twist = -8, HeadPitch = 8, ArmR = new(12, -32, 85), ArmL = new(45, -12, 30) }, 0.49, -0.005, (0.0, 0.52, 0.1), (-0.06, 0.46, 0.25)),
            At(new Pose { Lean = 24, HeadPitch = 15, ArmR = new(12, -32, 85), ArmL = new(45, -12, 30) }, 0.475, 0.005, (0.01, 0.55, 0.1), (-0.03, 0.42, 0.24)),
        ];
    }

    /// <summary>
    /// Climb in place: hands grip the rungs and feet stand on them. Each hand and the opposite foot hold a rung and are
    /// pulled down one rung while the other pair reaches up to the next, so the body climbs at a steady speed when moved
    /// up; the hips stay close to the ladder.
    /// </summary>
    private static Pose[] ClimbPoses()
    {
        const double height = 0.84, rung = 0.18, hands = 1.38, feet = 0.48;
        return
        [
            .. Enumerable.Range(0, 6).Select(f =>
            {
                double t = f / 6.0;
                // Phase 0–0.5: holding and pulled down one rung; 0.5–1: let go, reach up, grip the rung above.
                (double Down, double Off) Limb(double q)
                {
                    q -= Math.Floor(q);
                    if (q < 0.5) return (rung * q / 0.5, 0);
                    double u = (q - 0.5) / 0.5;
                    return (rung * (1 - Smooth(u)), Math.Sin(Math.PI * u));
                }
                var (hr, hro) = Limb(t);
                var (hl, hlo) = Limb(t + 0.5);
                var (fl, flo) = Limb(t);         // left foot with the right hand
                var (fr, fro) = Limb(t + 0.5);
                var upper = new Pose { Prop = PropKind.Ladder, Forward = -0.05, Lean = 9 + 2 * Math.Cos(2 * Math.PI * t * 2), HeadPitch = -18, Twist = 3 * Math.Cos(2 * Math.PI * t) };
                var p = Plant(upper, height,
                    Foot(0.115 - 0.05 * fro, 0.09, lift: feet - fr + 0.012 + 0.04 * fro, pitch: 6 * fro),
                    Foot(0.115 - 0.05 * flo, -0.09, lift: feet - fl + 0.012 + 0.04 * flo, pitch: 6 * flo));
                p = PoseSolver.Reach(p, height, V(0.09, hands - hr, 0.19 - 0.06 * hro), V(-0.09, hands - hl, 0.19 - 0.06 * hlo));
                return PoseSolver.Fly(p, height);
            }),
        ];
    }

    // ================================================================== the cycles

    private static List<ReferenceCycle> Build()
    {
        var cycles = new List<ReferenceCycle>
        {
            // ---------------------------------------------------------- movement
            new("idle", "Idle", Movement, 6, 8, true, "Standing and breathing, weight on one leg: a gentle loop for a character waiting.",
                Drawings(IdlePoses)),
            new("walk", "Walk", Movement, 8, 10, true, "A relaxed walk in place: contact, down, passing and up for each leg.",
                Steps(8, WalkSpec)),
            new("run", "Run", Movement, 8, 12, true, "A running loop in place: long strides, high knees, bent arms pumping.",
                Steps(8, RunSpec)),
            new("sneak", "Sneak", Movement, 8, 8, true, "Crouched, careful steps: weight low, each foot lifted and set down softly, toes first.",
                Steps(8, new StepSpec
                {
                    Length = 0.30, Duty = 0.62, Height = 0.46, Bob = 0.01, LowAt = 0.15, Center = 0.04, Width = 0.11,
                    Lift = 0.085, LiftAt = 0.45, TravelDelay = 0.08, Strike = 10, Flat = 0.3, HeelOff = 0.68, ToeOff = 22, Reach = 16,
                    Lean = 21, LeanBob = 1.5, Twist = 4, Sway = 0.02, Tilt = 2.5, ArmSwing = 6, ArmForward = 12, ArmBend = 62,
                    ArmBendSwing = 6, ArmSpread = 20, ArmTwist = 12, ArmLag = 0.08, HeadPitch = -16, HeadNod = -1.5,
                })),
            new("jump", "Jump", Movement, 8, 12, false, "Crouch, push off, tuck at the top and reach down for the ground (follow with Land).",
                Drawings(JumpPoses)),
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
                Drawings(LandPoses)),
            // ---------------------------------------------------------- combat
            new("attack-slash", "Attack (slash)", Combat, 7, 12, false, "An overhead sword slash: wind up, strike down, follow through, recover.",
                Drawings(SlashPoses)),
            new("attack-thrust", "Attack (thrust)", Combat, 6, 12, false, "A lunging thrust: draw back, step into a lunge with the blade straight ahead, return to guard.",
                Drawings(ThrustPoses)),
            new("hurt", "Hurt", Combat, 4, 12, false, "Hit from the front: recoil back onto the heels, absorb it, recover.",
                Drawings(HurtPoses)),
            new("death", "Death", Combat, 8, 10, false, "Hit, the knees give way, a fall forward to the ground, and still.",
                Drawings(DeathPoses)),
            new("cast", "Cast", Combat, 6, 10, false, "Gather power in both hands, raise it and step into pushing it forward.",
                Drawings(CastPoses)),
            new("shoot", "Shoot", Combat, 6, 12, false, "Raise a crossbow or gun, aim, fire with a small recoil, keep aiming.",
                Drawings(ShootPoses)),
            // ---------------------------------------------------------- interaction
            new("pick-up", "Pick up", Interaction, 7, 10, false, "Squat down, take a box from the ground and lift it to the chest with the legs.",
                Drawings(PickUpPoses)),
            new("drop", "Drop", Interaction, 7, 10, false, "Put a box down: Pick up played backwards.",
                Drawings(PickUpPoses), ReverseOf: "pick-up"),
            new("push", "Push", Interaction, 8, 8, true, "Lean into a heavy crate and push it, step by step.",
                Steps(8, new StepSpec
                {
                    Length = 0.28, Duty = 0.68, Height = 0.45, Bob = 0.012, LowAt = 0.12, Center = -0.12, Width = 0.08,
                    Lift = 0.04, Strike = -4, Flat = 0.15, HeelOff = 0.5, ToeOff = 30, Lean = 44, Twist = 3, Sway = 0.012, Tilt = 1,
                    ArmSwing = 0, ArmForward = 70, ArmBend = 20, ArmBendSwing = 0, HeadPitch = -28, Prop = PropKind.Crate,
                    Hands = OnCrate(0.3),
                })),
            new("pull", "Pull", Interaction, 8, 8, true, "Haul a rope hand over hand, leaning back against it with the front heel dug in: the pull comes from the legs and the body's weight.",
                Drawings(PullPoses)),
            new("climb", "Climb", Interaction, 6, 8, true, "Climb a ladder: hands grip the rungs and feet stand on them, hand and opposite foot together.",
                Drawings(ClimbPoses), [ReferenceView.Side, ReferenceView.ThreeQuarter]),
            new("sit-down", "Sit down", Interaction, 6, 8, false, "Lower onto a stool and settle.",
                Drawings(SitPoses)),
            new("stand-up", "Stand up", Interaction, 6, 8, false, "Get up from a stool: Sit down played backwards.",
                Drawings(SitPoses), ReverseOf: "sit-down"),
            new("hammer", "Hammer", Interaction, 6, 10, true, "Work with a hammer: lift, strike, bounce back.",
                Drawings(HammerPoses)),
            new("dig", "Dig", Interaction, 6, 8, true, "Dig with a shovel: push in, lever up, toss to the side, back again.",
                Drawings(DigPoses)),
        };
        AddExpansionPack1(cycles);
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
}
