using System;
using System.Collections.Generic;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>A defender's movement law over one stretch of a play (exact functions of time).</summary>
    public interface IFieldMotion
    {
        Vector3d PositionAt(double time);
        Vector3d VelocityAt(double time);
        double SpeedAt(double time);
        /// <summary>Heading (unit, ground plane).</summary>
        Vector3d DirectionAt(double time);
        /// <summary>Distance run since this motion began (phases the run cycle).</summary>
        double DistanceAt(double time);
    }

    /// <summary>
    /// A defender's whole movement in a live play (TASK-008): a chronological list of motions, each starting where and as
    /// fast as the previous one was at its start time (the caller continues from <see cref="PositionAt"/>/<see cref="VelocityAt"/>),
    /// so the defender never teleports or stops dead when his job changes. Any instant can be queried.
    /// </summary>
    public sealed class FielderTrack
    {
        private readonly List<(double Start, IFieldMotion Motion, double RunBefore)> _segments = new List<(double, IFieldMotion, double)>();

        public FielderTrack(FielderProfile profile, IFieldMotion first, double start)
        {
            Profile = profile;
            _segments.Add((start, first, 0.0));
        }

        public FielderProfile Profile { get; }
        public IFieldMotion Current => _segments[_segments.Count - 1].Motion;
        public double CurrentStart => _segments[_segments.Count - 1].Start;
        public int Count => _segments.Count;
        /// <summary>Changes whenever the track is changed (for caching what was computed from it).</summary>
        public int Version { get; private set; }

        /// <summary>When he first moves (+∞ if he never does in this play).</summary>
        public double FirstMoveTime
        {
            get
            {
                if (_segments[0].Motion is FielderMotion route && !double.IsPositiveInfinity(route.StartTime)) return route.StartTime;
                return _segments.Count > 1 ? _segments[1].Start : double.PositiveInfinity;
            }
        }

        /// <summary>Continue with <paramref name="motion"/> from <paramref name="start"/> (later than the current segment's start).</summary>
        public void Add(double start, IFieldMotion motion)
        {
            var last = _segments[_segments.Count - 1];
            if (start < last.Start) throw new ArgumentException("Segments are chronological.", nameof(start));
            double run = last.RunBefore + last.Motion.DistanceAt(start);
            if (start == last.Start) _segments.RemoveAt(_segments.Count - 1);
            _segments.Add((start, motion, start == last.Start ? last.RunBefore : run));
            Version++;
        }

        private int At(double time)
        {
            for (int i = _segments.Count - 1; i > 0; i--)
                if (time >= _segments[i].Start) return i;
            return 0;
        }

        public Vector3d PositionAt(double time) => _segments[At(time)].Motion.PositionAt(time);
        public Vector3d VelocityAt(double time) => _segments[At(time)].Motion.VelocityAt(time);
        public double SpeedAt(double time) => _segments[At(time)].Motion.SpeedAt(time);
        public Vector3d DirectionAt(double time) => _segments[At(time)].Motion.DirectionAt(time);

        public double DistanceAt(double time)
        {
            var s = _segments[At(time)];
            return s.RunBefore + s.Motion.DistanceAt(time);
        }
    }
}
