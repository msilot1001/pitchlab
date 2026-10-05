using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>
    /// Generic throwing abilities (Docs/FIELDING.md, "Throwing"). Routine throws at 0.85 × the Statcast league-average arm
    /// strength by position (MEASURED 2026 averages; the 0.85 routine factor is ASSUMED: the leaderboard averages only a
    /// player's hardest throws). Transfer (possession → release): infield 0.70 s, outfield 1.00 s, catcher 0.735 s
    /// (REPORTED/DERIVED). Release 1.8 m up (≈ 6 ft, REPORTED THT bounce-throw models).
    /// </summary>
    public readonly struct ThrowProfile
    {
        public readonly double Speed, TransferTime, ReleaseHeight;

        public ThrowProfile(double speed, double transferTime, double releaseHeight)
        {
            if (!(speed > 0.0) || !(transferTime >= 0.0) || !(releaseHeight > 0.0)) throw new ArgumentOutOfRangeException(nameof(speed));
            Speed = speed;
            TransferTime = transferTime;
            ReleaseHeight = releaseHeight;
        }

        public const double RoutineFactor = 0.85;

        /// <summary>Statcast league-average arm strength (mph, 2026; catcher REPORTED ~81; pitcher ASSUMED 85).</summary>
        public static double ArmStrengthMph(DefensivePosition p)
        {
            switch (p)
            {
                case DefensivePosition.FirstBase: return 79.3;
                case DefensivePosition.SecondBase: return 79.1;
                case DefensivePosition.ThirdBase: return 84.9;
                case DefensivePosition.Shortstop: return 86.1;
                case DefensivePosition.LeftField: return 87.4;
                case DefensivePosition.CenterField: return 89.6;
                case DefensivePosition.RightField: return 90.7;
                case DefensivePosition.C: return 81.0;
                default: return 85.0;
            }
        }

        public static ThrowProfile For(DefensivePosition p)
        {
            bool outfield = p == DefensivePosition.LeftField || p == DefensivePosition.CenterField || p == DefensivePosition.RightField;
            double transfer = outfield ? 1.0 : p == DefensivePosition.C ? 0.735 : 0.70;
            return new ThrowProfile(RoutineFactor * Units.MphToMetersPerSecond(ArmStrengthMph(p)), transfer, 1.8);
        }
    }

    /// <summary>
    /// Initial conditions of a throw: from a release point toward a target point at a given speed, the launch elevation that
    /// brings the ball to the target's height when it has covered the target's horizontal distance — found by bisection on
    /// the real flight (the shared simulator with the pitch aerodynamics: drag + Magnus, backspin ≈ 20 rpm per mph, REPORTED
    /// THT model). The flatter of the two arcs is used; a target out of range at that speed gets the highest-reaching arc
    /// (a coarse scan: with drag ~30–35°, not the steepest) and falls short.
    /// </summary>
    public static class ThrowSolver
    {
        /// <summary>Backspin per unit throw speed: 20 rpm per mph, in rad/s per m/s.</summary>
        public static readonly double SpinPerSpeed = Units.RpmToRadiansPerSecond(20.0) / Units.MphToMetersPerSecond(1.0);
        public const double MinElevation = -15.0, MaxElevation = 40.0;
        public static AerodynamicModel Aerodynamics => AerodynamicModel.Baseball;

        public static BallState Launch(Vector3d release, Vector3d target, double speed, double releaseTime, EnvironmentState environment, out bool reaches)
        {
            var flat = new Vector3d(target.X - release.X, target.Y - release.Y, 0.0);
            double distance = flat.Length;
            Vector3d dir = distance > 0.0 ? flat / distance : new Vector3d(0.0, 1.0, 0.0);
            var sim = new BallFlightSimulator(BallProperties.Baseball, environment, Aerodynamics);
            double Error(double elevation) => HeightAt(sim, State(release, dir, speed, elevation, releaseTime), dir, distance) - target.Z;

            // The best arc (highest at the target distance) brackets the flat solution: Error rises from lo up to it.
            double hi = MaxElevation, best = double.NegativeInfinity;
            for (int k = 0; k <= 11; k++)
            {
                double e = MinElevation + k * (MaxElevation - MinElevation) / 11.0, err = Error(e);
                if (err > best)
                {
                    best = err;
                    hi = e;
                }
            }

            double lo = MinElevation;
            reaches = best >= 0.0;
            if (!reaches) return State(release, dir, speed, hi, releaseTime);
            if (Error(lo) > 0.0)
            {
                reaches = false;   // so close below the release that even the lowest throw passes over the target
                return State(release, dir, speed, lo, releaseTime);
            }

            for (int i = 0; i < 30; i++)   // 55° / 2^30 ≈ 5e-8°
            {
                double mid = 0.5 * (lo + hi);
                if (Error(mid) < 0.0) lo = mid;
                else hi = mid;
            }

            return State(release, dir, speed, hi, releaseTime);
        }

        private static BallState State(Vector3d release, Vector3d dir, double speed, double elevationDegrees, double time)
        {
            double e = elevationDegrees * Math.PI / 180.0;
            Vector3d v = speed * (Math.Cos(e) * dir + Math.Sin(e) * new Vector3d(0.0, 0.0, 1.0));
            Vector3d spin = SpinPerSpeed * speed * Vector3d.Cross(dir, new Vector3d(0.0, 0.0, 1.0));   // backspin (lift)
            return new BallState(time, release, v, spin);
        }

        /// <summary>Ball height when it has covered <paramref name="distance"/> horizontally along <paramref name="dir"/> (−∞ if it lands first).</summary>
        private static double HeightAt(BallFlightSimulator sim, BallState start, Vector3d dir, double distance)
        {
            TrajectoryResult flight = sim.Simulate(start, new FlightLimits(double.NegativeInfinity, 0.0, 10.0));
            double Along(BallState s) => Vector3d.Dot(s.Position - start.Position, dir);
            for (int i = 1; i < flight.Samples.Count; i++)
            {
                BallState a = flight.Samples[i - 1], b = flight.Samples[i];
                if (Along(b) < distance) continue;
                double u = (distance - Along(a)) / (Along(b) - Along(a));
                return a.Position.Z + u * (b.Position.Z - a.Position.Z);
            }

            return double.NegativeInfinity;
        }
    }

    /// <summary>
    /// Who takes a throw at a base (simple, replaceable conventions; no cut-offs or rotations yet): 1B at first, SS at second
    /// (2B when the shortstop throws), 3B at third (SS when the third baseman throws), C at home (P when the catcher throws).
    /// </summary>
    public static class ThrowAssignment
    {
        public static DefensivePosition Receiver(Base target, DefensivePosition thrower)
        {
            switch (target)
            {
                case Base.First: return thrower == DefensivePosition.FirstBase ? DefensivePosition.SecondBase : DefensivePosition.FirstBase;
                case Base.Second: return thrower == DefensivePosition.Shortstop ? DefensivePosition.SecondBase : DefensivePosition.Shortstop;
                case Base.Third: return thrower == DefensivePosition.ThirdBase ? DefensivePosition.Shortstop : DefensivePosition.ThirdBase;
                default: return thrower == DefensivePosition.C ? DefensivePosition.P : DefensivePosition.C;
            }
        }
    }

    /// <summary>A receiver's movement: to the base, then from where he is when the throw is in the air, to the catch.</summary>
    public sealed class ReceiverPath
    {
        public ReceiverPath(FielderMotion toBase, double switchTime, FielderMotion toCatch)
        {
            ToBase = toBase;
            SwitchTime = switchTime;
            ToCatch = toCatch;
        }

        public FielderMotion ToBase { get; }
        public double SwitchTime { get; }
        public FielderMotion ToCatch { get; }

        private FielderMotion At(double t) => t < SwitchTime ? ToBase : ToCatch;
        public Vector3d PositionAt(double t) => At(t).PositionAt(t);
        public double SpeedAt(double t) => At(t).SpeedAt(t);
        public Vector3d VelocityAt(double t) => At(t).VelocityAt(t);
        /// <summary>Distance run along both legs (the run cycle's phase).</summary>
        public double DistanceAt(double t) => t < SwitchTime ? ToBase.DistanceAt(t) : ToBase.DistanceAt(SwitchTime) + ToCatch.DistanceAt(t);
        public Vector3d DirectionAt(double t) => At(t).Direction;
    }

    /// <summary>One throw: thrower, release, the authoritative flight, receiver, and the catch (or not).</summary>
    public sealed class ThrowPlay
    {
        internal ThrowPlay(DefensivePosition thrower, DefensivePosition receiver, Base target, double releaseTime, Vector3d releasePoint,
            BallInPlay flight, bool reaches, ReceiverPath path, Intercept catchIntercept)
        {
            Thrower = thrower;
            Receiver = receiver;
            Target = target;
            ReleaseTime = releaseTime;
            ReleasePoint = releasePoint;
            Flight = flight;
            ReachesTarget = reaches;
            Path = path;
            Catch = catchIntercept;
        }

        public DefensivePosition Thrower { get; }
        public DefensivePosition Receiver { get; }
        public Base Target { get; }
        public double ReleaseTime { get; }
        public Vector3d ReleasePoint { get; }
        /// <summary>The throw's authoritative flight from the release: in the air, then (if nobody catches it) bounces and rolls.</summary>
        public BallInPlay Flight { get; }
        /// <summary>At the throw's speed the solver could bring it to the target point (else it was thrown long and falls short).</summary>
        public bool ReachesTarget { get; }
        public ReceiverPath Path { get; }
        /// <summary>The receiver's catch (Feasible = false: the throw is not caught).</summary>
        public Intercept Catch { get; }
        public bool Caught => Catch.Feasible;
        /// <summary>The throw's first ground contact (it is no longer catchable on the fly) or its end.</summary>
        public double FirstContactTime
        {
            get
            {
                foreach (BallEvent e in Flight.Events)
                    if (e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.WallImpact || e.Kind == BallEventKind.LeftPlay) return e.Time;
                return Flight.EndTime;
            }
        }
    }

    /// <summary>
    /// The whole defensive play with an explicit ball authority at every instant: <see cref="BallAuthority.FreeBall"/> on the
    /// batted ball until the first defender takes it → <see cref="BallAuthority.Possessed"/> by him → (if he throws)
    /// <see cref="BallAuthority.Thrown"/> on the throw's flight from the authoritative release → Possessed by the receiver at
    /// his catch, or FreeBall on the throw's own trajectory from its first ground contact if nobody catches it (no snapping
    /// into a glove). Everything is a function of time.
    /// </summary>
    public sealed class DefensivePlay
    {
        public DefensivePlay(FieldingPlay fielding, ThrowPlay throwPlay)
        {
            Fielding = fielding;
            Throw = throwPlay;
            if (throwPlay != null && throwPlay.Caught)
                _catchOffset = throwPlay.Catch.Ball.Position - throwPlay.Path.PositionAt(throwPlay.Catch.Time);
        }

        private readonly Vector3d _catchOffset;

        public FieldingPlay Fielding { get; }
        public ThrowPlay Throw { get; }

        /// <summary>When the play is over: the receiver's catch; a missed throw at rest; otherwise the fielding play's end.</summary>
        public double EndTime => Throw == null ? Fielding.EndTime : Throw.Caught ? Throw.Catch.Time : Throw.Flight.EndTime;

        public BallAuthority AuthorityAt(double t)
        {
            if (t < Fielding.PossessionTime) return BallAuthority.FreeBall;
            if (Throw == null || t < Throw.ReleaseTime) return BallAuthority.Possessed;
            if (Throw.Caught) return t < Throw.Catch.Time ? BallAuthority.Thrown : BallAuthority.Possessed;
            return t < Throw.FirstContactTime ? BallAuthority.Thrown : BallAuthority.FreeBall;
        }

        /// <summary>Who holds the ball at <paramref name="t"/> (null while it is free or thrown).</summary>
        public DefensivePosition? HolderAt(double t)
        {
            if (AuthorityAt(t) != BallAuthority.Possessed) return null;
            return Throw != null && t >= Throw.ReleaseTime ? Throw.Receiver : Fielding.Primary;
        }

        public Vector3d FielderPositionAt(DefensivePosition p, double t) =>
            Throw != null && p == Throw.Receiver ? Throw.Path.PositionAt(t) : Fielding.Motion(p).PositionAt(t);

        public double FielderSpeedAt(DefensivePosition p, double t) =>
            Throw != null && p == Throw.Receiver ? Throw.Path.SpeedAt(t) : Fielding.Motion(p).SpeedAt(t);

        /// <summary>Distance run so far (phases the run cycle).</summary>
        public double FielderDistanceAt(DefensivePosition p, double t) =>
            Throw != null && p == Throw.Receiver ? Throw.Path.DistanceAt(t) : Fielding.Motion(p).DistanceAt(t);

        public Vector3d FielderDirectionAt(DefensivePosition p, double t) =>
            Throw != null && p == Throw.Receiver ? Throw.Path.DirectionAt(t) : Fielding.Motion(p).Direction;

        /// <summary>The ball's authoritative position for whichever authority owns it.</summary>
        public Vector3d BallPositionAt(double t)
        {
            switch (AuthorityAt(t))
            {
                case BallAuthority.FreeBall:
                    return Throw != null && t >= Throw.ReleaseTime ? Throw.Flight.StateAt(t).Position : Fielding.BallPositionAt(t);
                case BallAuthority.Thrown:
                    return Throw.Flight.StateAt(t).Position;
            }

            if (Throw != null && t >= Throw.ReleaseTime)
            {
                // Receiver: from where he caught it to his chest over the secure time.
                double u = Math.Min(1.0, (t - Throw.Catch.Time) / FieldingPlay.SecureTime);
                u = u * u * (3.0 - 2.0 * u);
                return Throw.Path.PositionAt(t) + (1.0 - u) * _catchOffset + u * FieldingPlay.HoldOffset;
            }

            // Thrower: secured at the chest, then brought to the release point over the arm action before the release.
            Vector3d held = Fielding.BallPositionAt(t);
            if (Throw == null) return held;
            double action = Math.Min(ArmAction, Throw.ReleaseTime - Fielding.PossessionTime);   // never before the take
            double a = Math.Max(0.0, Math.Min(1.0, 1.0 - (Throw.ReleaseTime - t) / action));
            a = a * a * (3.0 - 2.0 * a);
            Vector3d holder = Fielding.Motion(Throw.Thrower).PositionAt(t);
            Vector3d release = holder + (Throw.ReleasePoint - Fielding.Motion(Throw.Thrower).PositionAt(Throw.ReleaseTime));
            return (1.0 - a) * held + a * release;
        }

        /// <summary>Arm action before the release, during which the ball goes from the chest to the release point (s).</summary>
        public const double ArmAction = 0.35;
    }

    /// <summary>
    /// Builds the throw after a defender's possession (TASK-006A): transfer, release point (throwing-hand side, facing the
    /// target), the solved launch at the receiver's chest over the bag, the authoritative flight (<see cref="BallInPlaySimulation"/>),
    /// the receiver breaking for the base at contact (after his reaction) and adjusting to the throw with the fielding
    /// intercept solver. A throw is caught only on the fly; otherwise it
    /// stays a free ball on its own trajectory.
    /// </summary>
    public static class ThrowPlanner
    {
        /// <summary>Receivers adjust to a throw this long after its release (s, ASSUMED: they track the ball's flight).</summary>
        public const double AdjustReaction = 0.1;
        /// <summary>Target point: the receiver's chest over the base (m).</summary>
        public const double TargetHeight = 1.3;

        public static DefensivePlay Plan(FieldingPlay fielding, Base? target, Func<DefensivePosition, FielderProfile> profiles,
            Func<DefensivePosition, ThrowProfile> throwers, EnvironmentState environment, FieldLayout field)
        {
            if (target == null || fielding.Outcome != FieldingOutcome.Fielded) return new DefensivePlay(fielding, null);
            DefensivePosition thrower = fielding.Primary.Value;
            DefensivePosition receiver = ThrowAssignment.Receiver(target.Value, thrower);
            ThrowProfile arm = throwers(thrower);
            Vector3d basePoint = FieldLayout.BasePosition(target.Value);

            // Receiver breaks for the base when the ball is hit (after his reaction), as covering infielders do.
            FielderProfile catcher = profiles(receiver);
            Vector3d from = fielding.Motion(receiver).PositionAt(fielding.Ball.First.Time);
            var toBase = new FielderMotion(catcher, from, basePoint, fielding.Ball.First.Time + catcher.ReactionTime, true);

            // Release: transfer after the take — later if the base would not be covered when the throw gets there (the
            // thrower holds the ball for the receiver, as on a pitcher's flip to a first baseman still running to the bag).
            double release = fielding.PossessionTime + arm.TransferTime;
            var aim = new Vector3d(basePoint.X, basePoint.Y, TargetHeight);   // the receiver's chest over the bag
            Vector3d releasePoint = ReleasePoint(fielding, thrower, arm, basePoint, release);
            BallState launch = ThrowSolver.Launch(releasePoint, aim, arm.Speed, release, environment, out bool reaches);
            BallInPlay flight = BallInPlaySimulation.Run(launch, environment, field, ThrowSolver.Aerodynamics);
            // Iterated: a thrower still sliding to a stop changes the throw's length while he waits (≤ 4 passes, to 1 ms).
            for (int i = 0; i < 4; i++)
            {
                double wait = toBase.ArrivalTime - ArrivalAtBase(flight, basePoint);
                if (wait <= 1e-3) break;
                release += wait;
                releasePoint = ReleasePoint(fielding, thrower, arm, basePoint, release);
                launch = ThrowSolver.Launch(releasePoint, aim, arm.Speed, release, environment, out reaches);
                flight = BallInPlaySimulation.Run(launch, environment, field, ThrowSolver.Aerodynamics);
            }

            // The receiver adjusts from where he is shortly after the release; catchable only on the fly.
            double switchAt = release + AdjustReaction;
            Vector3d adjustFrom = toBase.PositionAt(switchAt);   // adjusting: from where he is shortly after the release
            FielderProfile adjust = new FielderProfile(AdjustReaction, catcher.MaxSpeed, catcher.AccelerationTime, catcher.BrakeDeceleration,
                catcher.Reach, catcher.GroundReach, catcher.CatchHeightMax, catcher.PickupHeightMax);
            double firstContact = double.PositiveInfinity;
            foreach (BallEvent e in flight.Events)
                if (e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.WallImpact || e.Kind == BallEventKind.LeftPlay)
                {
                    firstContact = e.Time;
                    break;
                }

            // Covering receivers take the throw at the bag when they can (from the moment they are on it, stepping or
            // stretching only as far as needed); otherwise they adjust from where they are.
            double onBag = Math.Max(switchAt, toBase.ArrivalTime);
            FielderProfile atBag = new FielderProfile(onBag - release, catcher.MaxSpeed, catcher.AccelerationTime, catcher.BrakeDeceleration,
                catcher.Reach, catcher.GroundReach, catcher.CatchHeightMax, catcher.PickupHeightMax);
            // On the bag, a throw that passes within reach is taken there, without leaving it (the force-play habit);
            // only a throw off the bag makes him step toward it.
            double pass = ArrivalAtBase(flight, basePoint);
            Intercept take = pass >= onBag && pass < firstContact && InterceptSolver.Feasible(flight, basePoint, atBag, field, pass, out Intercept onTheBag)
                             && onTheBag.RouteDistance == 0.0
                ? onTheBag
                : OnTheFly(InterceptSolver.Solve(flight, basePoint, atBag, field, firstContact));
            if (take.Feasible)
            {
                adjustFrom = basePoint;
                adjust = atBag;
                switchAt = onBag;
            }
            else take = OnTheFly(InterceptSolver.Solve(flight, adjustFrom, adjust, field, firstContact));
            if (!take.Feasible)   // the throw does not come to him: he keeps to the bag
                return new DefensivePlay(fielding, new ThrowPlay(thrower, receiver, target.Value, release, releasePoint, flight, reaches,
                    new ReceiverPath(toBase, double.PositiveInfinity, toBase), take));

            // ponytail: the adjust leg starts from rest, so a receiver still running at the switch stops instantly (only off
            // the bag after a late cover); carry his velocity into the leg if that ever shows.
            (_, double stop) = RunningLaw.StopTime(adjust, take.RouteDistance);
            FielderMotion toCatch = switchAt + stop <= take.Time
                ? new FielderMotion(adjust, adjustFrom, take.FielderTarget, switchAt, true)
                : new FielderMotion(adjust, adjustFrom, take.FielderTarget, take.Time - RunningLaw.RunThroughTime(adjust, take.RouteDistance), false);

            var path = new ReceiverPath(toBase, switchAt, toCatch);
            return new DefensivePlay(fielding, new ThrowPlay(thrower, receiver, target.Value, release, releasePoint, flight, reaches, path, take));
        }

        /// <summary>A throw is caught only on the fly (the solver's search end is inclusive: a take at the first contact itself
        /// would be the state after the bounce).</summary>
        private static Intercept OnTheFly(Intercept take) => take.Feasible && take.Kind == InterceptKind.FlyCatch ? take : Intercept.None;

        /// <summary>The ball at hand height on the thrower's throwing side, a step toward the target, at <paramref name="release"/>.</summary>
        private static Vector3d ReleasePoint(FieldingPlay fielding, DefensivePosition thrower, ThrowProfile arm, Vector3d basePoint, double release)
        {
            Vector3d holder = fielding.Motion(thrower).PositionAt(release);
            Vector3d toTarget = new Vector3d(basePoint.X - holder.X, basePoint.Y - holder.Y, 0.0);
            Vector3d facing = toTarget.Length > 1e-6 ? toTarget / toTarget.Length : new Vector3d(0.0, 1.0, 0.0);
            Vector3d right = new Vector3d(facing.Y, -facing.X, 0.0);   // the thrower's right (throwing arm)
            return holder + 0.3 * facing + 0.25 * right + new Vector3d(0.0, 0.0, arm.ReleaseHeight);
        }

        /// <summary>When the throw passes closest (horizontally) to the base, on a 5 ms grid.</summary>
        private static double ArrivalAtBase(BallInPlay flight, Vector3d basePoint)
        {
            double best = flight.First.Time, bestDistance = double.PositiveInfinity;
            for (double t = flight.First.Time; t <= flight.EndTime; t += 0.005)
            {
                Vector3d d = flight.StateAt(t).Position - basePoint;
                double h = d.X * d.X + d.Y * d.Y;
                if (h > bestDistance) break;   // moving away
                best = t;
                bestDistance = h;
            }

            return best;
        }

        public static DefensivePlay Plan(FieldingPlay fielding, Base? target) =>
            Plan(fielding, target, FielderProfile.For, ThrowProfile.For, EnvironmentState.Standard, FieldLayout.Standard);

        /// <summary>Default target for the sandbox loop (no runners yet): none after a catch on the fly; infielders, pitcher
        /// and catcher to first (the first baseman to second); outfielders to second.</summary>
        public static Base? DefaultTarget(FieldingPlay fielding)
        {
            if (fielding.Outcome != FieldingOutcome.Fielded || fielding.Intercept.Kind == InterceptKind.FlyCatch) return null;
            DefensivePosition p = fielding.Primary.Value;
            bool outfield = p == DefensivePosition.LeftField || p == DefensivePosition.CenterField || p == DefensivePosition.RightField;
            if (p == DefensivePosition.FirstBase) return Base.Second;   // simple: no unassisted-putout logic yet
            return outfield ? Base.Second : Base.First;
        }
    }
}
