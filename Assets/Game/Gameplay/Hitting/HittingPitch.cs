using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// A pitch prepared for hitting: the full flight until 1 m behind the plate (or the ground), plus the exact
    /// time and state at which it crosses the swing's contact plane. Uses the shared simulator with
    /// hitting-specific limits (PitchSimulation stops at the plate front, too early for contact behind it).
    /// </summary>
    public sealed class HittingPitch
    {
        public const double StopBehindPlateY = -1.0;
        public const double MaxFlightTime = 3.0;

        private HittingPitch(TrajectoryResult flight, double idealContactTime, BallState idealContactState)
        {
            Flight = flight;
            IdealContactTime = idealContactTime;
            IdealContactState = idealContactState;
        }

        public TrajectoryResult Flight { get; }
        /// <summary>When the ball crosses the contact plane; NaN if it reaches the ground first.</summary>
        public double IdealContactTime { get; }
        public BallState IdealContactState { get; }
        public bool ReachesContactPlane => !double.IsNaN(IdealContactTime);

        public static HittingPitch Create(BallState release, EnvironmentState environment, double contactPlaneY)
        {
            var simulator = new BallFlightSimulator(BallProperties.Baseball, environment, AerodynamicModel.Baseball);
            TrajectoryResult flight = simulator.Simulate(release, new FlightLimits(StopBehindPlateY, 0.0, MaxFlightTime));
            TrajectoryResult toContact = simulator.Simulate(release, new FlightLimits(contactPlaneY, 0.0, MaxFlightTime));
            return toContact.End == FlightEnd.CrossedStopPlane
                ? new HittingPitch(flight, toContact.Final.Time, toContact.Final)
                : new HittingPitch(flight, double.NaN, default);
        }

        public static HittingPitch Create(PitchInput input, EnvironmentState environment, double contactPlaneY) =>
            Create(input.ToInitialState(), environment, contactPlaneY);
    }
}
