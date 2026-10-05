using System;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>
    /// Running law of a defender along a straight route (a baseball field needs no path finding): from rest, speed
    /// v(t) = v_max·(1 − e^(−t/τ)), distance x(t) = v_max·(t − τ·(1 − e^(−t/τ))) — the exponential sprint model that fits the
    /// Statcast 90-ft splits (Docs/FIELDING.md) — optionally followed by constant braking. Pure functions: the reach-time
    /// prediction and the motion use the same equations.
    /// </summary>
    public static class RunningLaw
    {
        public static double Distance(FielderProfile p, double t) =>
            t <= 0.0 ? 0.0 : p.MaxSpeed * (t - p.AccelerationTime * (1.0 - Math.Exp(-t / p.AccelerationTime)));

        public static double Speed(FielderProfile p, double t) => t <= 0.0 ? 0.0 : p.MaxSpeed * (1.0 - Math.Exp(-t / p.AccelerationTime));

        /// <summary>g(t) = τ·(1 − e^(−t/τ)): how far an initial velocity carries a defender (per m/s of it) by t while the law
        /// relaxes his velocity toward the new route — the law's momentum term (<see cref="ContinuationMotion"/>).</summary>
        public static double Glide(FielderProfile p, double t) => t <= 0.0 ? 0.0 : p.AccelerationTime * (1.0 - Math.Exp(-t / p.AccelerationTime));

        /// <summary>Running time (after the reaction) to cover <paramref name="distance"/>, arriving at speed.</summary>
        public static double RunThroughTime(FielderProfile p, double distance)
        {
            if (distance <= 0.0) return 0.0;
            double lo = 0.0, hi = distance / p.MaxSpeed + p.AccelerationTime + 1.0;
            for (int i = 0; i < 64; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Distance(p, mid) < distance) lo = mid;
                else hi = mid;
            }

            return hi;
        }

        /// <summary>
        /// Run then brake at <see cref="FielderProfile.BrakeDeceleration"/> to stop exactly at <paramref name="distance"/>:
        /// the moment braking starts and the total time (after the reaction).
        /// </summary>
        public static (double BrakeStart, double Total) StopTime(FielderProfile p, double distance)
        {
            if (distance <= 0.0) return (0.0, 0.0);
            double lo = 0.0, hi = RunThroughTime(p, distance);
            for (int i = 0; i < 64; i++)
            {
                double mid = 0.5 * (lo + hi);
                double v = Speed(p, mid);
                if (Distance(p, mid) + v * v / (2.0 * p.BrakeDeceleration) < distance) lo = mid;
                else hi = mid;
            }

            return (lo, lo + Speed(p, lo) / p.BrakeDeceleration);
        }
    }

    public enum MotionPhase
    {
        /// <summary>Standing at the start (not yet reacting, or not involved).</summary>
        Ready,
        Running,
        Braking,
        /// <summary>At the target, waiting for the ball.</summary>
        Arrived,
    }

    /// <summary>
    /// A defender's authoritative movement for one play: hold at the start, or a straight route from the start to a
    /// target beginning at <see cref="StartTime"/>, either stopping at the target (enough time) or arriving at speed and
    /// braking past it (tight play). Position, velocity and phase are exact functions of time — identical whatever the
    /// render frame rate, and the same law the intercept solver predicts with.
    /// </summary>
    public sealed class FielderMotion
    {
        public FielderMotion(FielderProfile profile, Vector3d start)
        {
            Profile = profile;
            Start = start;
            Target = start;
            StartTime = double.PositiveInfinity;
        }

        /// <summary>Route to <paramref name="target"/>, moving from <paramref name="startTime"/>.</summary>
        public FielderMotion(FielderProfile profile, Vector3d start, Vector3d target, double startTime, bool stopAtTarget)
        {
            Profile = profile;
            Start = start;
            Target = target;
            StartTime = startTime;
            Vector3d d = target - start;
            RouteDistance = new Vector3d(d.X, d.Y, 0.0).Length;
            Direction = RouteDistance > 0.0 ? new Vector3d(d.X, d.Y, 0.0) / RouteDistance : Vector3d.Zero;
            StopsAtTarget = stopAtTarget;
            if (stopAtTarget)
            {
                (double brake, double total) = RunningLaw.StopTime(profile, RouteDistance);
                _brakeStart = brake;
                _runEnd = total;
            }
            else
            {
                _brakeStart = RunningLaw.RunThroughTime(profile, RouteDistance);
                _runEnd = _brakeStart + RunningLaw.Speed(profile, _brakeStart) / profile.BrakeDeceleration;
            }
        }

        private readonly double _brakeStart, _runEnd;   // after StartTime

        public FielderProfile Profile { get; }
        public Vector3d Start { get; }
        public Vector3d Target { get; }
        /// <summary>When the defender starts moving (contact + reaction, possibly later); +∞ when holding.</summary>
        public double StartTime { get; }
        public double RouteDistance { get; }
        public Vector3d Direction { get; }
        public bool StopsAtTarget { get; }
        /// <summary>When the defender reaches the target (on a run-through route he keeps going and brakes past it).</summary>
        public double ArrivalTime => double.IsPositiveInfinity(StartTime) ? double.PositiveInfinity : StartTime + (StopsAtTarget ? _runEnd : _brakeStart);

        /// <summary>Distance along the route at <paramref name="time"/> (may exceed the route on a run-through).</summary>
        public double DistanceAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0)) return 0.0;
            double sBrake = RunningLaw.Distance(Profile, _brakeStart), vBrake = RunningLaw.Speed(Profile, _brakeStart);
            if (t <= _brakeStart) return RunningLaw.Distance(Profile, t);
            double tb = Math.Min(t, _runEnd) - _brakeStart;
            return sBrake + vBrake * tb - 0.5 * Profile.BrakeDeceleration * tb * tb;
        }

        public double SpeedAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0) || t >= _runEnd) return 0.0;
            if (t <= _brakeStart) return RunningLaw.Speed(Profile, t);
            return Math.Max(0.0, RunningLaw.Speed(Profile, _brakeStart) - Profile.BrakeDeceleration * (t - _brakeStart));
        }

        public Vector3d PositionAt(double time) => Start + DistanceAt(time) * Direction;

        public Vector3d VelocityAt(double time) => SpeedAt(time) * Direction;

        public MotionPhase PhaseAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0)) return MotionPhase.Ready;
            if (t >= _runEnd) return MotionPhase.Arrived;
            return t <= _brakeStart ? MotionPhase.Running : MotionPhase.Braking;
        }
    }
}

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>
    /// A defender's run that starts while he may already be moving (TASK-006B): the sprint law with an initial velocity v₀,
    /// v(t) = u·d̂ + (v₀ − u·d̂)·e^(−t/τ) — the TASK-005 law is the case v₀ = 0 — so position and velocity stay continuous when
    /// a route changes (a receiver adjusting to a throw, a fielder carrying the ball to a base). His momentum alone takes him
    /// to c(t) = start + v₀·g(t) (<see cref="RunningLaw.Glide"/>); running at speed u he can be anywhere within
    /// u·(t − g(t)) of it. To be at a target exactly at an arrival time T he runs at u = |target − c(T)| / (T − g(T)) (≤ v_max
    /// when the target is reachable; slower — a jog — when there is time to spare), in the fixed direction d̂ toward
    /// target − c(T); after T he brakes to rest along his velocity at the braking deceleration. Exact functions of time.
    /// </summary>
    public sealed class ContinuationMotion
    {
        /// <summary>Holds no target: momentum only, relaxing to rest (v₀ = 0: standing).</summary>
        public ContinuationMotion(FielderProfile profile, Vector3d start, Vector3d initialVelocity, double startTime)
        {
            Profile = profile;
            Start = start;
            InitialVelocity = Flat(initialVelocity);
            StartTime = startTime;
            ArrivalTime = double.PositiveInfinity;
            Target = start + InitialVelocity * profile.AccelerationTime;
        }

        /// <summary>From <paramref name="start"/> moving at <paramref name="initialVelocity"/> at <paramref name="startTime"/>,
        /// at <paramref name="target"/> exactly at <paramref name="arrivalTime"/> (which must be reachable: see
        /// <see cref="CanReach"/>).</summary>
        public ContinuationMotion(FielderProfile profile, Vector3d start, Vector3d initialVelocity, double startTime, Vector3d target, double arrivalTime)
        {
            Profile = profile;
            Start = start;
            InitialVelocity = Flat(initialVelocity);
            StartTime = startTime;
            Target = new Vector3d(target.X, target.Y, start.Z);
            ArrivalTime = arrivalTime;
            double T = arrivalTime - startTime;
            Vector3d toTarget = Target - Carried(profile, Start, InitialVelocity, T);
            double run = T - RunningLaw.Glide(profile, T), gap = toTarget.Length;
            if (gap > 1e-12 && run > 1e-12)
            {
                _direction = toTarget / gap;
                _speed = gap / run;
            }

            if (T > 0.0) _arrivalVelocity = VelocityBefore(T);
        }

        private readonly Vector3d _direction;
        private readonly double _speed;
        private readonly Vector3d _arrivalVelocity;

        public FielderProfile Profile { get; }
        public Vector3d Start { get; }
        public Vector3d InitialVelocity { get; }
        public double StartTime { get; }
        public Vector3d Target { get; }
        /// <summary>When he is at <see cref="Target"/> (+∞: no target, momentum only).</summary>
        public double ArrivalTime { get; }
        /// <summary>The steady speed of the route (m/s; ≤ the profile's top speed for a reachable target).</summary>
        public double RouteSpeed => _speed;

        /// <summary>Where momentum alone carries a defender in <paramref name="t"/> (s) of the law.</summary>
        public static Vector3d Carried(FielderProfile p, Vector3d start, Vector3d velocity, double t) => start + Flat(velocity) * RunningLaw.Glide(p, t);

        /// <summary>Can he (from <paramref name="start"/> at <paramref name="velocity"/>) be within <paramref name="radius"/> of
        /// <paramref name="point"/> after <paramref name="t"/> s of running?</summary>
        public static bool CanReach(FielderProfile p, Vector3d start, Vector3d velocity, Vector3d point, double radius, double t)
        {
            if (t < 0.0) return false;
            Vector3d gap = point - Carried(p, start, velocity, t);
            return new Vector3d(gap.X, gap.Y, 0.0).Length <= RunningLaw.Distance(p, t) + radius + 1e-9;
        }

        /// <summary>The earliest time (s after the start) he can be within <paramref name="radius"/> of <paramref name="point"/>:
        /// a 5 ms scan then bisection (the reachable disc is not monotone in the first ~τ·ln 2 when he is moving away).</summary>
        public static double EarliestWithin(FielderProfile p, Vector3d start, Vector3d velocity, Vector3d point, double radius, double horizon = 30.0)
        {
            if (CanReach(p, start, velocity, point, radius, 0.0)) return 0.0;
            double previous = 0.0;
            for (double t = 0.005; t <= horizon; t += 0.005)
            {
                if (!CanReach(p, start, velocity, point, radius, t))
                {
                    previous = t;
                    continue;
                }

                double lo = previous, hi = t;
                for (int i = 0; i < 40; i++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (CanReach(p, start, velocity, point, radius, mid)) hi = mid;
                    else lo = mid;
                }

                return hi;
            }

            return double.PositiveInfinity;
        }

        /// <summary>
        /// The earliest route that ends at rest exactly on <paramref name="target"/> (a tag at a base needs him there, not
        /// running through it): the law to a brake point B by T₁, then braking along his velocity, with B placed so the braking
        /// ends on the target (damped fixed-point iteration). T₁ is the smallest brake start for which such a B is reachable
        /// (bisection on T₁; ponytail: assumes feasibility is monotone in T₁, true away from tight reversals). Null if none
        /// within <paramref name="horizon"/> s.
        /// </summary>
        public static ContinuationMotion ToRest(FielderProfile p, Vector3d start, Vector3d velocity, double startTime, Vector3d target, double horizon = 20.0)
        {
            Vector3d flatTarget = new Vector3d(target.X, target.Y, start.Z);
            if ((Flat(velocity)).Length < 1e-9 && (flatTarget - start).Length < 1e-9) return new ContinuationMotion(p, start, velocity, startTime);

            ContinuationMotion Plan(double brakeStart)
            {
                Vector3d brakePoint = flatTarget;
                for (int i = 0; i < 200; i++)
                {
                    var m = new ContinuationMotion(p, start, velocity, startTime, brakePoint, startTime + brakeStart);
                    if (m.RouteSpeed > p.MaxSpeed + 1e-9) return null;
                    Vector3d v = m.VelocityAt(startTime + brakeStart);
                    Vector3d next = flatTarget - v * (v.Length / (2.0 * p.BrakeDeceleration));
                    if ((next - brakePoint).Length < 1e-7) return m;
                    brakePoint += 0.5 * (next - brakePoint);
                }

                return null;
            }

            double lo = 0.0, hi = 0.25;
            while (Plan(hi) == null)
            {
                lo = hi;
                hi *= 2.0;
                if (hi > horizon) return null;
            }

            for (int i = 0; i < 40; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Plan(mid) == null) lo = mid;
                else hi = mid;
            }

            return Plan(hi);
        }

        /// <summary>When he comes to rest (+∞ with no target and momentum).</summary>
        public double RestTime => double.IsPositiveInfinity(ArrivalTime) ? double.PositiveInfinity
            : ArrivalTime + _arrivalVelocity.Length / Profile.BrakeDeceleration;

        public Vector3d PositionAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0)) return Start;
            double T = ArrivalTime - StartTime;
            if (t <= T) return Carried(Profile, Start, InitialVelocity, t) + _speed * (t - RunningLaw.Glide(Profile, t)) * _direction;
            // Braking past the arrival along the arrival velocity.
            double v = _arrivalVelocity.Length, tb = Math.Min(t - T, v / Profile.BrakeDeceleration);
            return v > 0.0 ? Target + (v * tb - 0.5 * Profile.BrakeDeceleration * tb * tb) / v * _arrivalVelocity : Target;
        }

        public Vector3d VelocityAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0)) return InitialVelocity;
            double T = ArrivalTime - StartTime;
            if (t <= T) return VelocityBefore(t);
            double v = _arrivalVelocity.Length, left = v - Profile.BrakeDeceleration * (t - T);
            return left > 0.0 ? left / v * _arrivalVelocity : Vector3d.Zero;
        }

        public double SpeedAt(double time) => VelocityAt(time).Length;

        /// <summary>Heading: along the velocity (the route direction when standing).</summary>
        public Vector3d DirectionAt(double time)
        {
            Vector3d v = VelocityAt(time);
            return v.Length > 1e-6 ? v / v.Length : _direction;
        }

        /// <summary>Distance run since the start (phases the run cycle): ∫|v| dt, Simpson's rule on a fixed 64-panel grid
        /// (pure function of time).</summary>
        public double DistanceAt(double time)
        {
            double t = Math.Min(time - StartTime, ArrivalTime - StartTime);
            double run = 0.0;
            if (t > 0.0)
            {
                const int n = 64;
                double h = t / n;
                for (int i = 0; i <= n; i++)
                    run += (i == 0 || i == n ? 1.0 : i % 2 == 1 ? 4.0 : 2.0) * VelocityBefore(i * h).Length;
                run *= h / 3.0;
            }

            if (time - StartTime > ArrivalTime - StartTime)
                run += (PositionAt(time) - Target).Length;   // braking: straight along the arrival velocity
            return run;
        }

        private Vector3d VelocityBefore(double t)
        {
            double e = Math.Exp(-t / Profile.AccelerationTime);
            return InitialVelocity * e + _speed * (1.0 - e) * _direction;
        }

        private static Vector3d Flat(Vector3d v) => new Vector3d(v.X, v.Y, 0.0);
    }
}
