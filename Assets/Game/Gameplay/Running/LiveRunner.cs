using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Running
{
    public enum RunnerPhase
    {
        /// <summary>Standing on a base (or at his lead before the ball is put in play).</summary>
        Standing,
        /// <summary>Running toward a base.</summary>
        Running,
        /// <summary>Going back to a base he left.</summary>
        Returning,
        /// <summary>Past first base after running through it (protected while he returns).</summary>
        Overrunning,
        /// <summary>Off the base, waiting to see whether a fly ball is caught (halfway / tag-up).</summary>
        Reading,
        Scored,
        Out,
    }

    /// <summary>
    /// A runner as an authoritative gameplay entity (TASK-007): where he is on the base path (a leg and the distance along
    /// it), the history of his motion segments (exact functions of time, so any instant can be queried), the base he last
    /// touched, where he is going, and his phase. Presentation reads it; it never writes it.
    /// </summary>
    public sealed class LiveRunner
    {
        private readonly List<Segment> _segments = new List<Segment>();

        internal LiveRunner(Runner id, RunnerProfile profile, Base onBase, BaseLeg leg, double distance, double time)
        {
            Id = id;
            Profile = profile;
            LastTouched = onBase;
            Target = onBase;
            Phase = RunnerPhase.Standing;
            _segments.Add(new Segment(leg, new PathMotion(profile, time, distance, 0.0, distance, 0.0)));
        }

        public Runner Id { get; }
        public RunnerProfile Profile { get; }
        /// <summary>The last base he legally touched (the one he is entitled to; home once he scores).</summary>
        public Base LastTouched { get; internal set; }
        /// <summary>The base he is going to (= <see cref="LastTouched"/> when holding there).</summary>
        public Base Target { get; internal set; }
        public RunnerPhase Phase { get; internal set; }
        /// <summary>A caught fly found him off his base: he must retouch it before advancing (OBR 5.09(b)(5)).</summary>
        public bool MustRetouch { get; internal set; }
        /// <summary>Runs through first base (no stop) on the current leg.</summary>
        internal bool ThroughFirst { get; set; }
        /// <summary>Base to round toward when the current leg ends (null: stop at its end).</summary>
        internal Base? Continue { get; set; }
        public double OutTime { get; internal set; } = double.PositiveInfinity;
        public double ScoreTime { get; internal set; } = double.PositiveInfinity;
        public bool IsOut => !double.IsPositiveInfinity(OutTime);
        public bool HasScored => !double.IsPositiveInfinity(ScoreTime);
        /// <summary>Retired or scored: no longer on the bases.</summary>
        public bool IsDone => IsOut || HasScored;

        internal Segment Current => _segments[_segments.Count - 1];
        internal IReadOnlyList<Segment> Segments => _segments;

        internal void Add(BaseLeg leg, PathMotion motion) => _segments.Add(new Segment(leg, motion));

        private Segment At(double time)
        {
            for (int i = _segments.Count - 1; i > 0; i--)
                if (time >= _segments[i].Motion.StartTime) return _segments[i];
            return _segments[0];
        }

        public BaseLeg LegAt(double time) => At(time).Leg;
        public double DistanceAlongAt(double time) => At(time).Motion.DistanceAt(time);

        /// <summary>Ground position (simulation frame).</summary>
        public Vector3d PositionAt(double time)
        {
            Segment s = At(time);
            return s.Leg.PositionAt(s.Motion.DistanceAt(time));
        }

        public Vector3d VelocityAt(double time)
        {
            Segment s = At(time);
            double d = s.Motion.DistanceAt(time);
            return s.Motion.VelocityAt(time) * s.Leg.DirectionAt(Math.Max(0.0, Math.Min(s.Leg.Length, d)));
        }

        public double SpeedAt(double time) => Math.Abs(At(time).Motion.VelocityAt(time));

        /// <summary>Heading (unit; the leg direction, reversed while returning).</summary>
        public Vector3d HeadingAt(double time)
        {
            Segment s = At(time);
            double d = s.Motion.DistanceAt(time);
            Vector3d dir = s.Leg.DirectionAt(Math.Max(0.0, Math.Min(s.Leg.Length, d)));
            return s.Motion.VelocityAt(time) < -1e-6 ? -1.0 * dir : dir;
        }

        /// <summary>Total distance run since the play began (phases a run cycle).</summary>
        public double DistanceRunAt(double time)
        {
            double sum = 0.0;
            for (int i = 0; i < _segments.Count; i++)
            {
                Segment s = _segments[i];
                if (time <= s.Motion.StartTime && i > 0) break;
                double end = i + 1 < _segments.Count ? Math.Min(time, _segments[i + 1].Motion.StartTime) : time;
                sum += Math.Abs(s.Motion.DistanceAt(end) - s.Motion.D0);
            }

            return sum;
        }

        /// <summary>Touching a base (a foot on the bag): within <see cref="TouchDistance"/> of it along the path.</summary>
        public bool TouchingBaseAt(double time, out Base touched)
        {
            Segment s = At(time);
            double d = s.Motion.DistanceAt(time);
            touched = s.Leg.From;
            if (Math.Abs(d) <= TouchDistance) return true;
            touched = s.Leg.To;
            return Math.Abs(d - s.Leg.Length) <= TouchDistance;
        }

        /// <summary>A runner's foot reaches this far along the path from his body (m; half an 18 in bag plus a stride
        /// — ASSUMED).</summary>
        public const double TouchDistance = 0.3;

        internal readonly struct Segment
        {
            public Segment(BaseLeg leg, PathMotion motion)
            {
                Leg = leg;
                Motion = motion;
            }

            public BaseLeg Leg { get; }
            public PathMotion Motion { get; }
        }
    }
}
