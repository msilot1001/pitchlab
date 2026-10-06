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

        /// <summary>A max-effort throw (the arm strength itself): the relay of a double play, when every hundredth counts.</summary>
        public static ThrowProfile Full(DefensivePosition p)
        {
            ThrowProfile routine = For(p);
            return new ThrowProfile(Units.MphToMetersPerSecond(ArmStrengthMph(p)), routine.TransferTime, routine.ReleaseHeight);
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

            // Bracket the flat (rising-branch) solution cheaply: Error rises from MinElevation to the best arc (≈ 30–40°), so the
            // first of 10°, 25°, 40° that reaches the target closes a bracket whose lower end is below it.
            double lo = MinElevation, hi = double.NaN, eHi = double.NaN, eLo = double.NaN;
            foreach (double e in new[] { 10.0, 25.0, MaxElevation })
            {
                double err = Error(e);
                if (err >= 0.0)
                {
                    hi = e;
                    eHi = err;
                    break;
                }

                lo = e;
                eLo = err;
            }

            if (double.IsNaN(hi))
            {
                // Rare (near or beyond the maximum range): scan for the highest-reaching arc, which brackets the flat
                // solution from above if the target is reachable at all.
                double best = double.NegativeInfinity, bestAt = MaxElevation;
                for (int k = 0; k <= 11; k++)
                {
                    double e = MinElevation + k * (MaxElevation - MinElevation) / 11.0, err = Error(e);
                    if (err > best)
                    {
                        best = err;
                        bestAt = e;
                    }
                }

                reaches = best >= 0.0;
                if (!reaches) return State(release, dir, speed, bestAt, releaseTime);
                lo = MinElevation;
                eLo = double.NaN;
                hi = bestAt;
                eHi = best;
            }

            if (double.IsNaN(eLo)) eLo = Error(lo);
            reaches = true;
            if (eLo > 0.0)
            {
                reaches = false;   // so close below the release that even the lowest throw passes over the target
                return State(release, dir, speed, lo, releaseTime);
            }

            // Illinois regula falsi on [lo, hi] (bisection while an end is −∞: the throw landed short); deterministic, a few
            // flight integrations instead of thirty.
            // The interpolation weights (wLo, wHi) are halved Illinois-style; the true errors (eLo, eHi) decide termination.
            int side = 0;
            double wLo = eLo, wHi = eHi;
            for (int i = 0; i < 60 && hi - lo > 1e-9 && eHi > 1e-9; i++)
            {
                double mid = double.IsInfinity(wLo) ? 0.5 * (lo + hi) : (lo * wHi - hi * wLo) / (wHi - wLo);
                if (!(mid > lo && mid < hi)) mid = 0.5 * (lo + hi);
                double eMid = Error(mid);
                if (eMid < 0.0)
                {
                    lo = mid;
                    eLo = wLo = eMid;
                    if (side == -1) wHi *= 0.5;
                    side = -1;
                }
                else
                {
                    hi = mid;
                    eHi = wHi = eMid;
                    if (side == 1 && !double.IsInfinity(wLo)) wLo *= 0.5;
                    side = 1;
                }
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

        /// <summary>
        /// Ball height when it has covered <paramref name="distance"/> horizontally along <paramref name="dir"/> (−∞ if it lands
        /// first). The simulator's own fixed steps (<see cref="BallFlightSimulator.Step"/>, times from the step index, the
        /// ground contact located inside its step by bisection, as <see cref="BallFlightSimulator.Simulate"/> does), stopping
        /// as soon as the distance is passed and recording nothing — the same numbers without the sample list.
        /// </summary>
        private static double HeightAt(BallFlightSimulator sim, BallState start, Vector3d dir, double distance)
        {
            double radius = sim.Ball.Radius, dt = sim.TimeStep, end = start.Time + 10.0;
            double Along(BallState s) => Vector3d.Dot(s.Position - start.Position, dir);
            if (start.Position.Z - radius <= 0.0) return double.NegativeInfinity;
            BallState current = start;
            for (long k = 1; ; k++)
            {
                double nextTime = Math.Min(start.Time + k * dt, end), step = nextTime - current.Time;
                BallState stepped = sim.Step(current, step);
                var next = new BallState(nextTime, stepped.Position, stepped.Velocity, stepped.Spin);
                if (next.Position.Z - radius <= 0.0)
                {
                    double lo = 0.0, hi = step;
                    for (int i = 0; i < 60 && hi - lo > 1e-12; i++)
                    {
                        double mid = 0.5 * (lo + hi);
                        if (sim.Step(current, mid).Position.Z - radius <= 0.0) hi = mid;
                        else lo = mid;
                    }

                    next = sim.Step(current, hi);
                    return Along(next) >= distance ? Interpolate(current, next) : double.NegativeInfinity;
                }

                if (Along(next) >= distance) return Interpolate(current, next);
                if (next.Time >= end) return double.NegativeInfinity;
                current = next;
            }

            double Interpolate(BallState a, BallState b)
            {
                double u = (distance - Along(a)) / (Along(b) - Along(a));
                return a.Position.Z + u * (b.Position.Z - a.Position.Z);
            }
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

    /// <summary>A receiver's movement: to the base, then — from where he is and as fast as he is moving when he adjusts to
    /// the throw — to the catch (<see cref="ContinuationMotion"/>: position and velocity continuous at the switch). No
    /// adjustment (<see cref="ToCatch"/> null): he keeps to the base.</summary>
    public sealed class ReceiverPath
    {
        public ReceiverPath(FielderMotion toBase, double switchTime, ContinuationMotion toCatch)
        {
            ToBase = toBase;
            SwitchTime = toCatch == null ? double.PositiveInfinity : switchTime;
            ToCatch = toCatch;
        }

        public FielderMotion ToBase { get; }
        public double SwitchTime { get; }
        public ContinuationMotion ToCatch { get; }

        public Vector3d PositionAt(double t) => t < SwitchTime ? ToBase.PositionAt(t) : ToCatch.PositionAt(t);
        public double SpeedAt(double t) => t < SwitchTime ? ToBase.SpeedAt(t) : ToCatch.SpeedAt(t);
        public Vector3d VelocityAt(double t) => t < SwitchTime ? ToBase.VelocityAt(t) : ToCatch.VelocityAt(t);
        /// <summary>Distance run along both legs (the run cycle's phase).</summary>
        public double DistanceAt(double t) => t < SwitchTime ? ToBase.DistanceAt(t) : ToBase.DistanceAt(SwitchTime) + ToCatch.DistanceAt(t);
        public Vector3d DirectionAt(double t) => t < SwitchTime ? ToBase.Direction : ToCatch.DirectionAt(t);
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
    /// into a glove). Instead of throwing, the fielder may carry the ball himself (<see cref="Carry"/>, TASK-006B: to a base
    /// for an unassisted put-out). Everything is a function of time.
    /// </summary>
    public sealed class DefensivePlay
    {
        public DefensivePlay(FieldingPlay fielding, ThrowPlay throwPlay) : this(fielding, throwPlay, null)
        {
        }

        /// <param name="carry">The fielder's own movement with the ball from the moment of possession (no throw).</param>
        public DefensivePlay(FieldingPlay fielding, ThrowPlay throwPlay, ContinuationMotion carry)
        {
            if (throwPlay != null && carry != null) throw new ArgumentException("A fielder either throws or carries the ball.", nameof(carry));
            if (carry != null && Math.Abs(carry.StartTime - fielding.PossessionTime) > 1e-12)
                throw new ArgumentException("The carry starts at the possession.", nameof(carry));
            Fielding = fielding;
            Throw = throwPlay;
            Carry = carry;
            if (throwPlay != null && throwPlay.Caught)
                _catchOffset = throwPlay.Catch.Ball.Position - throwPlay.Path.PositionAt(throwPlay.Catch.Time);
            if (fielding.Outcome == FieldingOutcome.Fielded)
                _takeOffset = fielding.Intercept.Ball.Position - fielding.Motion(fielding.Primary.Value).PositionAt(fielding.PossessionTime);
        }

        private readonly Vector3d _catchOffset, _takeOffset;

        public FieldingPlay Fielding { get; }
        public ThrowPlay Throw { get; }
        /// <summary>The fielder's movement with the ball after possession (null: he stays on his fielding route).</summary>
        public ContinuationMotion Carry { get; }

        /// <summary>When the play is over: the receiver's catch; a missed throw at rest; the end of a carry; otherwise the
        /// fielding play's end.</summary>
        public double EndTime => Throw != null ? Throw.Caught ? Throw.Catch.Time : Throw.Flight.EndTime
            : Carry != null ? Math.Max(Fielding.EndTime, Carry.ArrivalTime) : Fielding.EndTime;

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

        private bool Carrying(DefensivePosition p, double t) => Carry != null && p == Fielding.Primary && t >= Carry.StartTime;

        public Vector3d FielderPositionAt(DefensivePosition p, double t) =>
            Throw != null && p == Throw.Receiver ? Throw.Path.PositionAt(t)
            : Carrying(p, t) ? Carry.PositionAt(t) : Fielding.Motion(p).PositionAt(t);

        public double FielderSpeedAt(DefensivePosition p, double t) =>
            Throw != null && p == Throw.Receiver ? Throw.Path.SpeedAt(t)
            : Carrying(p, t) ? Carry.SpeedAt(t) : Fielding.Motion(p).SpeedAt(t);

        /// <summary>Distance run so far (phases the run cycle).</summary>
        public double FielderDistanceAt(DefensivePosition p, double t) =>
            Throw != null && p == Throw.Receiver ? Throw.Path.DistanceAt(t)
            : Carrying(p, t) ? Fielding.Motion(p).DistanceAt(Carry.StartTime) + Carry.DistanceAt(t) : Fielding.Motion(p).DistanceAt(t);

        public Vector3d FielderDirectionAt(DefensivePosition p, double t) =>
            Throw != null && p == Throw.Receiver ? Throw.Path.DirectionAt(t)
            : Carrying(p, t) ? Carry.DirectionAt(t) : Fielding.Motion(p).Direction;

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

            // The fielder: from where he took it into the glove at his chest over the secure time (wherever he moves).
            double s = Math.Min(1.0, (t - Fielding.PossessionTime) / FieldingPlay.SecureTime);
            s = s * s * (3.0 - 2.0 * s);
            Vector3d held = FielderPositionAt(Fielding.Primary.Value, t) + (1.0 - s) * _takeOffset + s * FieldingPlay.HoldOffset;
            if (Throw == null) return held;
            // Thrower: then brought to the release point over the arm action before the release.
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
    public static partial class ThrowPlanner
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

            // The receiver adjusts from where he is, and as he is moving, shortly after the release; catchable only on the fly.
            double switchAt = release + AdjustReaction;
            Vector3d adjustFrom = toBase.PositionAt(switchAt), adjustVelocity = toBase.VelocityAt(switchAt);
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
                adjustFrom = basePoint;   // on the bag, at rest (the cover route stops there)
                adjustVelocity = Vector3d.Zero;
                adjust = atBag;
                switchAt = onBag;
            }
            else take = OnTheFly(InterceptSolver.Solve(flight, adjustFrom, adjust, field, firstContact, initialVelocity: adjustVelocity));

            // To the catch from his position and velocity at the switch: there exactly at the catch (a jog when there is
            // time), braking after it. The throw does not come to him: he keeps to the bag.
            ContinuationMotion toCatch = take.Feasible ? new ContinuationMotion(adjust, adjustFrom, adjustVelocity, switchAt, take.FielderTarget, take.Time) : null;
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
        internal static double ArrivalAtBase(BallInPlay flight, Vector3d basePoint)
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
    }
}

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>
    /// A throw in a live play (TASK-008): from any holder to any receiver who is covering a base or standing at a cut-off
    /// point — the authoritative flight, the receiver's catch on the fly (or not), and how he moves to make it.
    /// </summary>
    public sealed class LiveThrow
    {
        internal LiveThrow(DefensivePosition thrower, DefensivePosition receiver, Base? target, Vector3d aimPoint, double releaseTime,
            Vector3d releasePoint, BallInPlay flight, bool reaches, Intercept catchIntercept, double switchTime, ContinuationMotion receiverMotion,
            ThrowProfile arm, double onPoint, Vector3d aimError)
        {
            Arm = arm;
            OnPoint = onPoint;
            AimError = aimError;
            Thrower = thrower;
            Receiver = receiver;
            Target = target;
            AimPoint = aimPoint;
            ReleaseTime = releaseTime;
            ReleasePoint = releasePoint;
            Flight = flight;
            ReachesTarget = reaches;
            Catch = catchIntercept;
            SwitchTime = switchTime;
            ReceiverMotion = receiverMotion;
            double first = flight.EndTime;
            foreach (BallEvent e in flight.Events)
                if (e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.WallImpact || e.Kind == BallEventKind.LeftPlay || e.Kind == BallEventKind.ClearedFence)
                {
                    first = e.Time;
                    break;
                }

            FirstContactTime = first;
        }

        public DefensivePosition Thrower { get; }
        public DefensivePosition Receiver { get; }
        /// <summary>The base it is thrown to (null: to a cut-off or relay man's spot).</summary>
        public Base? Target { get; }
        /// <summary>Where it is aimed on the ground (the bag, or the cut-off man's spot).</summary>
        public Vector3d AimPoint { get; }
        public double ReleaseTime { get; }
        public Vector3d ReleasePoint { get; }
        public BallInPlay Flight { get; }
        public bool ReachesTarget { get; }
        public Intercept Catch { get; }
        public bool Caught => Catch.Feasible;
        /// <summary>When the receiver leaves his cover/spot to take the throw (+∞: he stays).</summary>
        public double SwitchTime { get; }
        /// <summary>The receiver's move to the catch from his state at <see cref="SwitchTime"/> (null: he stays).</summary>
        public ContinuationMotion ReceiverMotion { get; }
        public double FirstContactTime { get; }
        /// <summary>The thrower's arm on this throw (routine or full effort).</summary>
        public ThrowProfile Arm { get; }
        /// <summary>When the receiver was planned to be on the point (−∞: no waiting for him).</summary>
        public double OnPoint { get; }
        /// <summary>How far the throw was off its aim at the receiver's chest (m; zero: thrown as aimed — TASK-021).</summary>
        public Vector3d AimError { get; }
        /// <summary>When the ball is no longer this throw's: the catch, or its first contact if missed.</summary>
        public double EndTime => Caught ? Catch.Time : FirstContactTime;
    }

    public static partial class ThrowPlanner
    {
        /// <summary>
        /// Plans a throw in a live play (TASK-008): <paramref name="thrower"/> (moving per <paramref name="throwerTrack"/>) releases
        /// no earlier than <paramref name="ready"/> — later if the receiver would not be there when the throw arrives (hold-for-
        /// cover, iterated); aimed at the receiver's chest over <paramref name="point"/>; the receiver, moving per
        /// <paramref name="receiverTrack"/> and at <paramref name="point"/> from <paramref name="onPoint"/>, takes a throw passing
        /// within reach there without leaving it, otherwise adjusts from his position and velocity (on the fly or on a hop).
        /// <paramref name="onPoint"/> = −∞: no waiting for him (a throw to a cut-off man, who adjusts to it).
        /// </summary>
        /// <param name="aimError">TASK-021: the executed throw's offset from its aim (m); a throw with an error is released at
        /// <paramref name="ready"/> (its hold for the cover was already planned with the throw as aimed).</param>
        public static LiveThrow PlanLive(DefensivePosition thrower, FielderTrack throwerTrack, double ready, ThrowProfile arm,
            DefensivePosition receiver, FielderTrack receiverTrack, double onPoint, Vector3d point, Base? target,
            EnvironmentState environment, FieldLayout field, Vector3d aimError = default)
        {
            // The throw is followed for twice its straight-line time plus 2 s (a catch, or a hop, is well inside); a throw that
            // is not caught is planned again on its whole flight, so its retrieval sees the real ball.
            Vector3d from = throwerTrack.PositionAt(ready);
            double horizon = 2.0 * new Vector3d(point.X - from.X, point.Y - from.Y, 0.0).Length / arm.Speed + 2.0;
            LiveThrow th = PlanLive(thrower, throwerTrack, ready, arm, receiver, receiverTrack, onPoint, point, target, environment, field, horizon, aimError);
            return th.Caught ? th : PlanLive(thrower, throwerTrack, ready, arm, receiver, receiverTrack, onPoint, point, target, environment, field, BallInPlaySimulation.MaxPlayTime, aimError);
        }

        private static LiveThrow PlanLive(DefensivePosition thrower, FielderTrack throwerTrack, double ready, ThrowProfile arm,
            DefensivePosition receiver, FielderTrack receiverTrack, double onPoint, Vector3d point, Base? target,
            EnvironmentState environment, FieldLayout field, double horizon, Vector3d aimError)
        {
            bool errant = !aimError.Equals(Vector3d.Zero);
            var aim = new Vector3d(point.X, point.Y, TargetHeight) + aimError;
            double release = ready;
            Vector3d releasePoint = ReleaseFrom(throwerTrack.PositionAt(release), point, arm);
            BallState launch = ThrowSolver.Launch(releasePoint, aim, arm.Speed, release, environment, out bool reaches);
            BallInPlay flight = BallInPlaySimulation.Run(launch, environment, field, ThrowSolver.Aerodynamics, horizon);
            for (int i = 0; i < (errant ? 0 : 4); i++)
            {
                double wait = onPoint - ArrivalAtBase(flight, point);
                if (wait <= 1e-3) break;
                release += wait;
                Vector3d moved = ReleaseFrom(throwerTrack.PositionAt(release), point, arm);
                // Waiting where he stands: the same throw, later (the flight does not depend on the time).
                if (moved.Equals(releasePoint)) launch = new BallState(release, launch.Position, launch.Velocity, launch.Spin);
                else launch = ThrowSolver.Launch(moved, aim, arm.Speed, release, environment, out reaches);
                releasePoint = moved;
                flight = BallInPlaySimulation.Run(launch, environment, field, ThrowSolver.Aerodynamics, horizon);
            }

            // A live throw may be taken on a hop (long throws home are one-hoppers); it is playable until it leaves the park.
            double playable = double.PositiveInfinity;
            foreach (BallEvent e in flight.Events)
                if (e.Kind == BallEventKind.LeftPlay || e.Kind == BallEventKind.ClearedFence)
                {
                    playable = e.Time;
                    break;
                }

            FielderProfile p = receiverTrack.Profile;
            double switchAt = release + AdjustReaction;
            double arrived = Math.Max(switchAt, onPoint);
            var atPoint = new FielderProfile(arrived - release, p.MaxSpeed, p.AccelerationTime, p.BrakeDeceleration, p.Reach, p.GroundReach, p.CatchHeightMax, p.PickupHeightMax);
            double pass = ArrivalAtBase(flight, point);
            Vector3d from = receiverTrack.PositionAt(arrived);
            bool onIt = new Vector3d(from.X - point.X, from.Y - point.Y, 0.0).Length < 0.05;
            Intercept take = Intercept.None;
            Vector3d adjustFrom = from, adjustVelocity = Vector3d.Zero;
            double adjustStart = arrived;
            FielderProfile adjust = atPoint;
            if (onIt)
            {
                // On a base he stretches for it, his foot on the bag: a longer reach for the take where it passes.
                // Only for a throw off its aim (TASK-021): a throw as aimed is planned exactly as before.
                FielderProfile stretched = target != null && errant ? new FielderProfile(atPoint.ReactionTime, p.MaxSpeed, p.AccelerationTime, p.BrakeDeceleration,
                    p.Reach + StretchReach, p.GroundReach + StretchReach, p.CatchHeightMax, p.PickupHeightMax) : atPoint;
                take = pass >= arrived && pass < playable && InterceptSolver.Feasible(flight, from, stretched, field, pass, out Intercept there) && there.RouteDistance == 0.0
                    ? there
                    : InterceptSolver.Solve(flight, from, atPoint, field, playable);
            }

            if (!take.Feasible)
            {
                // Adjusting from where he is, as he is moving, shortly after the release.
                adjustStart = switchAt;
                adjustFrom = receiverTrack.PositionAt(switchAt);
                adjustVelocity = receiverTrack.VelocityAt(switchAt);
                adjust = new FielderProfile(AdjustReaction, p.MaxSpeed, p.AccelerationTime, p.BrakeDeceleration, p.Reach, p.GroundReach, p.CatchHeightMax, p.PickupHeightMax);
                take = InterceptSolver.Solve(flight, adjustFrom, adjust, field, playable, initialVelocity: adjustVelocity);
            }

            ContinuationMotion motion = take.Feasible && take.RouteDistance > 0.0
                ? new ContinuationMotion(adjust, adjustFrom, adjustVelocity, adjustStart, take.FielderTarget, take.Time)
                : null;
            return new LiveThrow(thrower, receiver, target, point, release, releasePoint, flight, reaches, take,
                motion != null ? adjustStart : double.PositiveInfinity, motion, arm, onPoint, aimError);
        }

        /// <summary>A receiver on a base stretches this much further (m) for a throw, keeping his foot on the bag (a first
        /// baseman's stretch reaches ≈ 1.0–1.2 m beyond his standing reach of the bag — Docs/RULES.md; ASSUMED 0.6 m extra).</summary>
        public const double StretchReach = 0.6;

        /// <summary>The ball at hand height on the throwing side, a step toward the target.</summary>
        private static Vector3d ReleaseFrom(Vector3d holder, Vector3d target, ThrowProfile arm)
        {
            Vector3d toTarget = new Vector3d(target.X - holder.X, target.Y - holder.Y, 0.0);
            Vector3d facing = toTarget.Length > 1e-6 ? toTarget / toTarget.Length : new Vector3d(0.0, 1.0, 0.0);
            Vector3d right = new Vector3d(facing.Y, -facing.X, 0.0);
            return new Vector3d(holder.X, holder.Y, 0.0) + 0.3 * facing + 0.25 * right + new Vector3d(0.0, 0.0, arm.ReleaseHeight);
        }
    }
}
