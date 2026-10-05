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
