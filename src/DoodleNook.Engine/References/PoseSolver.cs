using System.Numerics;

namespace DoodleNook.Engine.References;

/// <summary>
/// Where a foot is, for <see cref="PoseSolver.Plant"/>: <see cref="Z"/> is the ankle's forward position (body heights)
/// when the foot is flat, <see cref="Lift"/> the sole's height above the ground, and <see cref="Pitch"/> the foot's angle
/// in degrees: positive raises the heel (the foot pivots on its ball, which stays where it was), negative raises the
/// toes (the foot pivots on its heel). So a planted foot that rolls from heel to toe keeps its contact point still.
/// <see cref="X"/>, when given, is the ankle's sideways position (to the body's right) and <see cref="Yaw"/> the way the
/// foot points (degrees, + toes turned to the body's right); otherwise the leg keeps its spread. All in the body's own
/// frame (see <see cref="Pose.Turn"/>).
/// </summary>
internal readonly record struct FootAt(double Z, double Lift = 0, double Pitch = 0, double? X = null, double Yaw = 0);

/// <summary>
/// Places limbs by where their ends should be rather than by joint angles, so a planted foot stays exactly where it was
/// from one drawing to the next, a sole rests on the ground instead of hovering, and hands can hold what they hold. It
/// works on the mannequin as it is: every measurement comes from <see cref="Mannequin.Build"/>, and the angles are found
/// by a few Newton steps. Used only to make the library's poses; nothing here is drawn.
/// </summary>
internal static class PoseSolver
{
    /// <summary>The pelvis height the mannequin's raw body uses (its standing pose is then lifted onto the ground).</summary>
    private const double RawPelvis = 0.52;

    private static readonly double StandingLift = Measure(Mannequin.Build(new Pose { Airborne = true })).Pelvis.Y - RawPelvis;

    /// <summary>The foot's shape around its ankle, measured from a straight leg: heel and toe centres and radii.</summary>
    private static readonly (Vector2 Heel, float HeelR, Vector2 Toe, float ToeR) FootShape = MeasureFoot();

    private static readonly double RestPitch = PitchOf(FootShape.Heel, FootShape.Toe);

    /// <summary>How far in front of the ankle the ball of a flat foot is (a foot pivots there).</summary>
    internal static double BallForward => FootShape.Toe.X;

    /// <summary>The pelvis standing in the relaxed pose (about 0.54 body heights).</summary>
    private static double StandingHeight => RawPelvis + StandingLift;

    // ------------------------------------------------------------------ measuring

    internal readonly record struct Joints(
        Vector3 Pelvis, Vector3 Head,
        Vector3 HipR, Vector3 KneeR, Vector3 AnkleR, Vector3 HeelR, Vector3 ToeR,
        Vector3 HipL, Vector3 KneeL, Vector3 AnkleL, Vector3 HeelL, Vector3 ToeL,
        Vector3 HandR, Vector3 HandL, float HeelRadius, float ToeRadius);

    internal static Joints Measure(List<Part> parts)
    {
        var body = parts.Where(p => p.Paint == PartPaint.Body).ToList();
        var r = parts.Where(p => p.Paint == PartPaint.Right && p.Kind == PartKind.Capsule).ToList();
        var l = parts.Where(p => p.Paint == PartPaint.Left && p.Kind == PartKind.Capsule).ToList();
        var hr = parts.First(p => p.Paint == PartPaint.Right && p.Kind == PartKind.Ball);
        var hl = parts.First(p => p.Paint == PartPaint.Left && p.Kind == PartKind.Ball);
        var head = parts.First(p => p.Paint == PartPaint.Body && p.Kind == PartKind.Ball);
        // Each leg is thigh (hip → knee), shin (knee → ankle) and foot (heel → toes), drawn before the arms.
        return new Joints(body[0].A, head.A,
            r[0].A, r[0].B, r[1].B, r[2].A, r[2].B,
            l[0].A, l[0].B, l[1].B, l[2].A, l[2].B,
            hr.A, hl.A, r[2].RadiusA, r[2].RadiusB);
    }

    /// <summary>The pose's joints with its pelvis at <paramref name="height"/> (for an upright body), in the body's own
    /// frame: a body turned with <see cref="Pose.Turn"/> is measured as if it faced forward.</summary>
    internal static Joints At(Pose pose, double height)
    {
        var j = Measure(Mannequin.Build(pose with { Airborne = true, Rise = height - RawPelvis - StandingLift }));
        if (pose.Turn == 0) return j;
        var back = Quaternion.Conjugate(TurnOf(pose));
        Vector3 U(Vector3 v) => Vector3.Transform(v, back);
        return j with
        {
            Pelvis = U(j.Pelvis), Head = U(j.Head),
            HipR = U(j.HipR), KneeR = U(j.KneeR), AnkleR = U(j.AnkleR), HeelR = U(j.HeelR), ToeR = U(j.ToeR),
            HipL = U(j.HipL), KneeL = U(j.KneeL), AnkleL = U(j.AnkleL), HeelL = U(j.HeelL), ToeL = U(j.ToeL),
            HandR = U(j.HandR), HandL = U(j.HandL),
        };
    }

    /// <summary>The whole-body turn as a rotation (as the mannequin applies it).</summary>
    private static Quaternion TurnOf(Pose pose) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(pose.Turn * Math.PI / 180));

    /// <summary>The pose lifted so its pelvis is <paramref name="height"/> above the ground (feet off the ground).</summary>
    public static Pose Fly(Pose pose, double height) => pose with { Airborne = true, Rise = height - RawPelvis - StandingLift };

    private static (Vector2, float, Vector2, float) MeasureFoot()
    {
        var j = At(new Pose { LegR = new Limb(0, 0, 0), ToeR = 0 }, StandingHeight);
        Vector2 Rel(Vector3 p) => new(p.Z - j.AnkleR.Z, p.Y - j.AnkleR.Y);
        return (Rel(j.HeelR), j.HeelRadius, Rel(j.ToeR), j.ToeRadius);
    }

    /// <summary>How far the toes point down past the heel, in degrees (z forward, y up).</summary>
    private static double PitchOf(Vector2 heel, Vector2 toe) => Math.Atan2(-(toe.Y - heel.Y), toe.X - heel.X) * 180 / Math.PI;

    /// <summary>The same for a foot pointing any way along the ground.</summary>
    private static double PitchOf(Vector3 heel, Vector3 toe) =>
        Math.Atan2(-(toe.Y - heel.Y), Math.Sqrt((toe.X - heel.X) * (toe.X - heel.X) + (toe.Z - heel.Z) * (toe.Z - heel.Z))) * 180 / Math.PI;

    // ------------------------------------------------------------------ feet

    /// <summary>The ankle position (forward, up) that puts a foot where <paramref name="f"/> says.</summary>
    internal static (double Z, double Y) AnkleFor(FootAt f)
    {
        var (z, y) = AnkleStraight(f);
        return f.Yaw == 0 ? (z, y) : (f.Z + (z - f.Z) * Math.Cos(f.Yaw * Math.PI / 180), y);
    }

    /// <summary>The ankle of a foot pointing straight ahead: pitched about its ball (or heel), which stays put.</summary>
    private static (double Z, double Y) AnkleStraight(FootAt f)
    {
        double b = f.Pitch * Math.PI / 180, c = Math.Cos(b), s = Math.Sin(b);
        // Turning the foot by its pitch about the ankle (toes down for a positive pitch).
        Vector2 Turn(Vector2 v) => new((float)(v.Y * s + v.X * c), (float)(v.Y * c - v.X * s));
        var heel = Turn(FootShape.Heel);
        var toe = Turn(FootShape.Toe);
        double bottom = Math.Min(heel.Y - FootShape.HeelR, toe.Y - FootShape.ToeR);
        double z = f.Pitch >= 0 ? f.Z + FootShape.Toe.X - toe.X : f.Z + FootShape.Heel.X - heel.X;
        return (z, f.Lift - bottom);
    }

    /// <summary>How far a pitched foot's ankle moves along the foot (its ball or heel stays put).</summary>
    private static double Along(FootAt f) => AnkleStraight(f).Z - f.Z;

    /// <summary>
    /// The pose with both legs placed: the pelvis <paramref name="height"/> above the ground (and at the pose's
    /// <see cref="Pose.Forward"/>), each foot where asked. Keeps each leg's spread. The result rests on the ground by
    /// itself (the lowest foot touches it); use <see cref="Fly"/> for a pose with both feet in the air.
    /// </summary>
    public static Pose Plant(Pose pose, double height, FootAt right, FootAt left)
    {
        var p = pose;
        p = SolveLeg(p, height, 1, right);
        p = SolveLeg(p, height, -1, left);
        return p with { Airborne = false, Rise = 0 };
    }

    private static Pose SolveLeg(Pose pose, double height, int side, FootAt foot)
    {
        var p = SolveLegInPlane(pose, height, side, foot);
        if (foot.X is not { } x0) return p;
        double x = foot.Yaw == 0 ? x0 : x0 + Along(foot) * Math.Sin(foot.Yaw * Math.PI / 180);
        // Sideways: the leg's spread and twist together keep the ankle at x and the foot pointing straight ahead, so a
        // planted foot neither slides nor turns as the knee bends (in a deep squat the knees go out over the toes).
        (double X, double Yaw) Error(Pose q)
        {
            var j = At(q, height);
            var (ankle, heel, toe) = side > 0 ? (j.AnkleR, j.HeelR, j.ToeR) : (j.AnkleL, j.HeelL, j.ToeL);
            return (x - ankle.X, Math.Atan2(toe.X - heel.X, toe.Z - heel.Z) * 180 / Math.PI - foot.Yaw);
        }
        Pose Adjust(Pose q, double spread, double twist) => SolveLegInPlane(side > 0
            ? q with { LegR = q.LegR with { Spread = q.LegR.Spread + spread, Twist = q.LegR.Twist + twist } }
            : q with { LegL = q.LegL with { Spread = q.LegL.Spread + spread, Twist = q.LegL.Twist + twist } }, height, side, foot);
        for (int i = 0; i < 10; i++)
        {
            var e = Error(p);
            if (Math.Abs(e.X) < 1e-5 && Math.Abs(e.Yaw) < 0.05) break;
            const double h = 0.5;
            var es = Error(Adjust(p, h, 0));
            var et = Error(Adjust(p, 0, h));
            double a = (es.X - e.X) / h, b = (et.X - e.X) / h, c = (es.Yaw - e.Yaw) / h, d = (et.Yaw - e.Yaw) / h;
            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12) break;
            double ds = (-e.X * d + b * e.Yaw) / det, dt = (-a * e.Yaw + c * e.X) / det;
            double big = Math.Max(Math.Abs(ds), Math.Abs(dt));
            if (big > 12) (ds, dt) = (ds * 12 / big, dt * 12 / big);
            p = Adjust(p, ds, dt);
        }
        return p;
    }

    private static Pose SolveLegInPlane(Pose pose, double height, int side, FootAt foot)
    {
        var (tz, ty) = AnkleFor(foot);
        double targetPitch = foot.Pitch + RestPitch;
        Pose With(double swing, double bend, double toe) => side > 0
            ? pose with { LegR = pose.LegR with { Swing = swing, Bend = bend }, ToeR = toe }
            : pose with { LegL = pose.LegL with { Swing = swing, Bend = bend }, ToeL = toe };
        Vector3 Residual(double swing, double bend, double toe)
        {
            var j = At(With(swing, bend, toe), height);
            var ankle = side > 0 ? j.AnkleR : j.AnkleL;
            var heel = side > 0 ? j.HeelR : j.HeelL;
            var toes = side > 0 ? j.ToeR : j.ToeL;
            // Straight-ahead feet measure their pitch in the side plane (as the library always has); turned feet along themselves.
            double pitch = foot.Yaw == 0 ? PitchOf(new Vector2(heel.Z, heel.Y), new Vector2(toes.Z, toes.Y)) : PitchOf(heel, toes);
            return new Vector3((float)(ankle.Z - tz), (float)(ankle.Y - ty), (float)((pitch - targetPitch) / 100));
        }

        // A first guess from the two-bone triangle in the side plane, then Newton steps on the real mannequin.
        var j0 = At(With(0, 0, 0), height);
        var hip = side > 0 ? j0.HipR : j0.HipL;
        double thigh = Vector3.Distance(j0.HipR, j0.KneeR), shin = Vector3.Distance(j0.KneeR, j0.AnkleR);
        double dz = tz - hip.Z, dy = ty - hip.Y, reach = Math.Clamp(Math.Sqrt(dz * dz + dy * dy), Math.Abs(thigh - shin) + 1e-4, thigh + shin - 1e-4);
        double knee = Math.Acos(Math.Clamp((thigh * thigh + shin * shin - reach * reach) / (2 * thigh * shin), -1, 1));
        double lean = Math.Acos(Math.Clamp((thigh * thigh + reach * reach - shin * shin) / (2 * thigh * reach), -1, 1));
        double toward = Math.Atan2(dz, -dy);
        double sw = (toward + lean) * 180 / Math.PI, bd = 180 - knee * 180 / Math.PI;
        double to = foot.Pitch + sw - bd; // the foot's pitch is −swing + bend + toe

        var u = Fit(v => Residual(v.X, v.Y, v.Z), new Vector3((float)sw, (float)bd, (float)to),
            new Vector3(-180, 0, -180), new Vector3(180, 170, 180));
        (sw, bd, to) = (u.X, u.Y, u.Z);
        return With(sw, bd, to);
    }

    // ------------------------------------------------------------------ hands

    /// <summary>The pose with its hands moved to where asked (x to the body's right, y up, z forward; body heights), the
    /// pelvis <paramref name="height"/> above the ground. A null hand keeps its arm as it is. The elbows point down and a
    /// little out and back unless <paramref name="elbows"/> (in the chest's own axes: right, up, forward) says otherwise.</summary>
    public static Pose Reach(Pose pose, double height, Vector3? right, Vector3? left, Vector3? elbows = null)
    {
        var p = pose;
        if (right is { } r) p = SolveArm(p, height, 1, r, elbows);
        if (left is { } l) p = SolveArm(p, height, -1, l, elbows is { } e ? e with { X = -e.X } : null);
        return p;
    }

    /// <summary>
    /// Two-bone reach: the elbow goes where the upper arm and forearm meet, on the side the elbow hint points to; the
    /// arm's swing, spread, bend and twist are read back from that elbow and hand in the chest's frame, exactly as the
    /// mannequin builds an arm. A target out of reach gets the arm pointing straight at it.
    /// </summary>
    private static Pose SolveArm(Pose pose, double height, int side, Vector3 targetInBody, Vector3? elbowHint)
    {
        var turn = TurnOf(pose);
        var target = pose.Turn == 0 ? targetInBody : Vector3.Transform(targetInBody, turn);
        var parts = Mannequin.Build(pose with { Airborne = true, Rise = height - RawPelvis - StandingLift });
        var body = parts.Where(q => q.Paint == PartPaint.Body).ToList();
        var paint = side > 0 ? PartPaint.Right : PartPaint.Left;
        var arm = parts.Where(q => q.Paint == paint && q.Kind == PartKind.Capsule).Skip(3).Take(2).ToList();
        // The chest's axes: across the shoulders, up the neck, and forward.
        var R = Vector3.Normalize(body[3].B - body[3].A);
        var U = Vector3.Normalize(body[2].B - body[2].A);
        var F = Vector3.Cross(R, U);
        var shoulder = arm[0].A;
        float upper = Vector3.Distance(arm[0].A, arm[0].B);
        float lower = Vector3.Distance(arm[1].A, arm[1].B) + 0.025f; // to the hand's centre
        var toTarget = target - shoulder;
        float reach = Math.Clamp(toTarget.Length(), Math.Abs(upper - lower) + 1e-4f, upper + lower - 1e-4f);
        var u = toTarget.LengthSquared() > 1e-10f ? Vector3.Normalize(toTarget) : -U;
        var hint = elbowHint ?? new Vector3(0.5f, -1, -0.4f);
        var pole = R * (hint.X * side) + U * hint.Y + F * hint.Z;
        var v = pole - u * Vector3.Dot(pole, u);
        v = v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : Vector3.Normalize(Vector3.Cross(u, R));
        float along = (upper * upper - lower * lower + reach * reach) / (2 * reach);
        float off = MathF.Sqrt(Math.Max(0, upper * upper - along * along));
        var elbow = shoulder + u * along + v * off;
        var hand = shoulder + u * reach;

        // Upper arm: Down turned by the spread (about forward), then by the swing (about right), in the chest's frame.
        var d1 = (elbow - shoulder) / upper;
        double spreadSigned = Math.Asin(Math.Clamp(Vector3.Dot(d1, R), -1, 1));
        double swing = Math.Atan2(Vector3.Dot(d1, F), -Vector3.Dot(d1, U));
        double cosElbow = Math.Clamp((upper * upper + lower * lower - reach * reach) / (2 * upper * lower), -1, 1);
        double bend = 180 - Math.Acos(cosElbow) * 180 / Math.PI;
        // The arm's own frame, built the way the mannequin builds it, then the forearm's twist within it.
        var (aR, aU, aF) = Turn((R, U, F), R, -swing);
        (aR, aU, aF) = Turn((aR, aU, aF), aF, spreadSigned);
        var d2 = Vector3.Normalize(hand - elbow);
        double phi = Math.Atan2(Vector3.Dot(d2, aR), Vector3.Dot(d2, aF));
        var limb = (side > 0 ? pose.ArmR : pose.ArmL) with
        {
            Swing = swing * 180 / Math.PI,
            Spread = spreadSigned * 180 / Math.PI * side,
            Bend = bend,
            Twist = bend > 2 ? -phi * 180 / Math.PI * side : (side > 0 ? pose.ArmR : pose.ArmL).Twist,
        };
        var solved = side > 0 ? pose with { ArmR = limb } : pose with { ArmL = limb };
        var handInBody = pose.Turn == 0 ? hand : Vector3.Transform(hand, Quaternion.Conjugate(turn));
        var miss = (side > 0 ? At(solved, height).HandR : At(solved, height).HandL) - handInBody;
        return miss.Length() < 1e-3f ? solved : RefineArm(solved, height, side, handInBody);
    }

    /// <summary>Rotates a frame's three axes by <paramref name="radians"/> about <paramref name="axis"/> (right-handed).</summary>
    private static (Vector3, Vector3, Vector3) Turn((Vector3 R, Vector3 U, Vector3 F) frame, Vector3 axis, double radians)
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)radians);
        return (Vector3.Transform(frame.R, q), Vector3.Transform(frame.U, q), Vector3.Transform(frame.F, q));
    }

    /// <summary>A safety net: polishes an arm numerically (swing, spread, bend) from where the two-bone answer left it.</summary>
    private static Pose RefineArm(Pose pose, double height, int side, Vector3 target)
    {
        Pose With(double swing, double spread, double bend) => side > 0
            ? pose with { ArmR = pose.ArmR with { Swing = swing, Spread = spread, Bend = bend } }
            : pose with { ArmL = pose.ArmL with { Swing = swing, Spread = spread, Bend = bend } };
        Vector3 Residual(double swing, double spread, double bend)
        {
            var j = At(With(swing, spread, bend), height);
            return (side > 0 ? j.HandR : j.HandL) - target;
        }
        var limb = side > 0 ? pose.ArmR : pose.ArmL;
        var u = Fit(v => Residual(v.X, v.Y, v.Z), new Vector3((float)limb.Swing, (float)limb.Spread, (float)limb.Bend),
            new Vector3(-180, -90, 0), new Vector3(200, 120, 150));
        return With(u.X, u.Y, u.Z);
    }

    // ------------------------------------------------------------------ the little numerical method

    /// <summary>
    /// Finds three angles (degrees) that bring <paramref name="residual"/> to zero, within limits: damped least squares
    /// (Levenberg–Marquardt) with a numerical Jacobian. Keeps the best angles found, so a target just out of reach
    /// gets the limb pointing straight at it instead of a wild guess.
    /// </summary>
    private static Vector3 Fit(Func<Vector3, Vector3> residual, Vector3 start, Vector3 min, Vector3 max)
    {
        var u = Vector3.Clamp(start, min, max);
        var r = residual(u);
        double damping = 1e-6;
        for (int i = 0; i < 60 && r.Length() > 1e-7f; i++)
        {
            const float h = 0.02f;
            var a = (residual(u + new Vector3(h, 0, 0)) - r) / h;
            var b = (residual(u + new Vector3(0, h, 0)) - r) / h;
            var c = (residual(u + new Vector3(0, 0, h)) - r) / h;
            // (J Jᵀ + λ I) y = −r, step = Jᵀ y.
            Vector3 Row(int k) => new(a[k] * a.X + b[k] * b.X + c[k] * c.X, a[k] * a.Y + b[k] * b.Y + c[k] * c.Y, a[k] * a.Z + b[k] * b.Z + c[k] * c.Z);
            var m0 = Row(0) + new Vector3((float)damping, 0, 0);
            var m1 = Row(1) + new Vector3(0, (float)damping, 0);
            var m2 = Row(2) + new Vector3(0, 0, (float)damping);
            // The matrix is symmetric, so its rows are its columns.
            if (!Solve3(m0, m1, m2, -r, out var y))
            {
                damping *= 8;
                continue;
            }
            var step = new Vector3(Vector3.Dot(a, y), Vector3.Dot(b, y), Vector3.Dot(c, y));
            float big = Math.Max(Math.Abs(step.X), Math.Max(Math.Abs(step.Y), Math.Abs(step.Z)));
            if (big > 20) step *= 20 / big;
            var next = Vector3.Clamp(u + step, min, max);
            var rn = residual(next);
            if (rn.Length() < r.Length())
            {
                u = next;
                r = rn;
                damping = Math.Max(1e-12, damping / 4);
            }
            else if ((damping *= 8) > 1) break;
        }
        return u;
    }

    /// <summary>Solves [a b c]·x = r (a, b, c are columns) by Cramer's rule.</summary>
    private static bool Solve3(Vector3 a, Vector3 b, Vector3 c, Vector3 r, out Vector3 x)
    {
        double det = Vector3.Dot(a, Vector3.Cross(b, c));
        if (Math.Abs(det) < 1e-18)
        {
            x = default;
            return false;
        }
        x = new Vector3(
            (float)(Vector3.Dot(r, Vector3.Cross(b, c)) / det),
            (float)(Vector3.Dot(a, Vector3.Cross(r, c)) / det),
            (float)(Vector3.Dot(a, Vector3.Cross(b, r)) / det));
        return true;
    }
}
