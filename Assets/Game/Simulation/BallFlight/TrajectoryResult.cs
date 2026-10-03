using System;
using System.Collections.Generic;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.BallFlight
{
    public enum FlightEnd
    {
        CrossedStopPlane,
        ReachedGround,
        ReachedMaxDuration,
    }

    /// <summary>
    /// Recorded flight. Samples are the integrator states at every fixed step, plus a final state located
    /// exactly at the termination event, so the last interval is usually shorter than the time step.
    /// </summary>
    public sealed class TrajectoryResult
    {
        private readonly BallState[] _samples;

        public TrajectoryResult(BallState[] samples, FlightEnd end)
        {
            if (samples == null || samples.Length == 0) throw new ArgumentException("A trajectory needs at least one sample.", nameof(samples));
            _samples = samples;
            End = end;
        }

        public IReadOnlyList<BallState> Samples => _samples;
        public FlightEnd End { get; }
        public BallState First => _samples[0];
        public BallState Final => _samples[_samples.Length - 1];
        public double Duration => Final.Time - First.Time;

        /// <summary>
        /// State at an arbitrary time, by cubic Hermite interpolation of position (using the recorded velocities)
        /// and linear interpolation of velocity. Times outside the flight clamp to the ends. Intended for playback
        /// and display; authoritative events (plane crossing, ground) are already located exactly by the simulator.
        /// </summary>
        public BallState StateAt(double time)
        {
            if (time <= First.Time) return First;
            if (time >= Final.Time) return Final;

            // Binary search for the first sample with Time >= time.
            int hi = 1;
            int upper = _samples.Length - 1;
            while (hi < upper)
            {
                int mid = (hi + upper) / 2;
                if (_samples[mid].Time < time) hi = mid + 1;
                else upper = mid;
            }

            BallState a = _samples[hi - 1];
            BallState b = _samples[hi];
            double h = b.Time - a.Time;
            double s = (time - a.Time) / h;
            double s2 = s * s;
            double s3 = s2 * s;
            Vector3d position =
                (2 * s3 - 3 * s2 + 1) * a.Position +
                (s3 - 2 * s2 + s) * h * a.Velocity +
                (-2 * s3 + 3 * s2) * b.Position +
                (s3 - s2) * h * b.Velocity;
            Vector3d velocity = a.Velocity + (b.Velocity - a.Velocity) * s;
            return new BallState(time, position, velocity, a.Spin);
        }
    }
}
