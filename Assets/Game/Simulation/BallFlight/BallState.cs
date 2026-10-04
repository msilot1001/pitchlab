using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.BallFlight
{
    /// <summary>Instantaneous ball state in the simulation frame (SI units; see Docs/PHYSICS.md).</summary>
    public readonly struct BallState
    {
        /// <summary>Seconds since the start of the flight.</summary>
        public readonly double Time;
        /// <summary>Ball centre, metres.</summary>
        public readonly Vector3d Position;
        /// <summary>Ground-relative velocity, m/s.</summary>
        public readonly Vector3d Velocity;
        /// <summary>Angular velocity, rad/s, right-hand rule.</summary>
        public readonly Vector3d Spin;

        public BallState(double time, Vector3d position, Vector3d velocity, Vector3d spin)
        {
            Time = time;
            Position = position;
            Velocity = velocity;
            Spin = spin;
        }
    }
}
