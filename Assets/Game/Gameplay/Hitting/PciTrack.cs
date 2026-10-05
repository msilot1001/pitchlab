using System;
using System.Collections.Generic;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// The PCI as an exact function of time. Aim input is a piecewise-constant velocity (stick, keys) that changes only
    /// at timestamped input events, plus instantaneous timestamped displacements (mouse deltas), so the position at any
    /// moment — in particular at a swing event's timestamp — is determined by the input trace alone, never by when
    /// frames happened to sample it. Positions are clamped per axis to the PCI area (exact, because the velocity is
    /// constant between events). Units are the caller's (the hitting sandbox uses normalized <see cref="PciFrame"/> units).
    /// </summary>
    public sealed class PciTrack
    {
        private readonly struct Segment
        {
            public readonly double Time, X, Z, VelocityX, VelocityZ;

            public Segment(double time, double x, double z, double vx, double vz)
            {
                Time = time;
                X = x;
                Z = z;
                VelocityX = vx;
                VelocityZ = vz;
            }
        }

        /// <summary>
        /// Input history kept for queries slightly in the past: within one Input System update, events from different
        /// devices are processed in arrival order, so a swing can be resolved after a newer aim event was applied.
        /// </summary>
        public const double HistorySeconds = 0.5;

        private readonly double _minX, _maxX, _minZ, _maxZ;
        private readonly List<Segment> _segments = new List<Segment>();

        public PciTrack(double minX, double maxX, double minZ, double maxZ, double time, double x, double z)
        {
            if (!(minX < maxX) || !(minZ < maxZ)) throw new ArgumentException("Empty PCI area.");
            _minX = minX;
            _maxX = maxX;
            _minZ = minZ;
            _maxZ = maxZ;
            _segments.Add(new Segment(time, ClampX(x), ClampZ(z), 0.0, 0.0));
        }

        /// <summary>Moves the PCI to (x, z) at <paramref name="time"/>, keeping the current velocity.</summary>
        public void Place(double time, double x, double z)
        {
            Segment last = Last;
            Append(new Segment(Math.Max(time, last.Time), ClampX(x), ClampZ(z), last.VelocityX, last.VelocityZ));
        }

        /// <summary>Displaces the PCI by (dx, dz) at <paramref name="time"/> (a mouse movement event), keeping the velocity.
        /// Events are expected in time order; an older event is applied at the latest one's time.</summary>
        public void Move(double time, double dx, double dz)
        {
            Segment last = Last;
            double t = Math.Max(time, last.Time);
            (double x, double z) = Evaluate(last, t);
            Append(new Segment(t, ClampX(x + dx), ClampZ(z + dz), last.VelocityX, last.VelocityZ));
        }

        /// <summary>From <paramref name="time"/> on, the PCI moves at (vx, vz) units per second. Events are expected in time order;
        /// an event older than the latest one is applied at the latest one's time.</summary>
        public void SetVelocity(double time, double vx, double vz)
        {
            Segment last = Last;
            double t = Math.Max(time, last.Time);
            (double x, double z) = Evaluate(last, t);
            Append(new Segment(t, x, z, vx, vz));
        }

        /// <summary>PCI position at <paramref name="time"/> (before the first record: the first position).</summary>
        public (double X, double Z) PositionAt(double time)
        {
            for (int i = _segments.Count - 1; i >= 0; i--)
                if (_segments[i].Time <= time) return Evaluate(_segments[i], time);
            return (_segments[0].X, _segments[0].Z);
        }

        private Segment Last => _segments[_segments.Count - 1];

        private (double, double) Evaluate(Segment s, double time)
        {
            double dt = time - s.Time;
            return (ClampX(s.X + s.VelocityX * dt), ClampZ(s.Z + s.VelocityZ * dt));
        }

        private void Append(Segment segment)
        {
            _segments.Add(segment);
            // Keep the segment that covers (newest − HistorySeconds) and everything after it.
            int drop = 0;
            while (drop + 1 < _segments.Count - 1 && _segments[drop + 1].Time <= segment.Time - HistorySeconds) drop++;
            if (drop >= 64) _segments.RemoveRange(0, drop);   // batched: a high-rate mouse appends every millisecond or faster
        }

        private double ClampX(double x) => Math.Max(_minX, Math.Min(_maxX, x));
        private double ClampZ(double z) => Math.Max(_minZ, Math.Min(_maxZ, z));
    }
}
