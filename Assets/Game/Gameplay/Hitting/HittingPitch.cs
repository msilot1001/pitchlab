using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// A pitch prepared for hitting: the full flight until 1 m behind the plate (or the ground), plus the exact
    /// time and state at which it crosses the contact plane. Uses the shared simulator with hitting-specific limits
    /// (PitchSimulation stops at the plate front, too early for contact behind it). <see cref="ContactPlaneY"/> is the
    /// single source of the contact plane: <see cref="ContactResolver"/> reads it from here.
    /// </summary>
    public sealed class HittingPitch
    {
        public const double StopBehindPlateY = -1.0;
        public const double MaxFlightTime = 3.0;
        /// <summary>Default plane where on-time contact happens: 0.25 m in front of the plate (Docs/HITTING.md) [Tune].</summary>
        public static readonly double DefaultContactPlaneY = PitchingGeometry.PlateFrontY + 0.25;

        private HittingPitch(TrajectoryResult flight, double contactPlaneY, double idealContactTime, BallState idealContactState)
        {
            Flight = flight;
            ContactPlaneY = contactPlaneY;
            IdealContactTime = idealContactTime;
            IdealContactState = idealContactState;
        }

        public TrajectoryResult Flight { get; }
        /// <summary>Plane (simulation Y, m) where on-time contact happens.</summary>
        public double ContactPlaneY { get; }
        /// <summary>When the ball crosses the contact plane; NaN if it reaches the ground first.</summary>
        public double IdealContactTime { get; }
        public BallState IdealContactState { get; }
        public bool ReachesContactPlane => !double.IsNaN(IdealContactTime);

        public static HittingPitch Create(BallState release, EnvironmentState environment) =>
            Create(release, environment, DefaultContactPlaneY);

        public static HittingPitch Create(PitchInput input, EnvironmentState environment) =>
            Create(input.ToInitialState(), environment, DefaultContactPlaneY);

        public static HittingPitch Create(BallState release, EnvironmentState environment, double contactPlaneY)
        {
            // The plane must lie between where the flight stops and where the ball is released.
            if (!(contactPlaneY > StopBehindPlateY && contactPlaneY < release.Position.Y)) throw new ArgumentOutOfRangeException(nameof(contactPlaneY));
            var simulator = new BallFlightSimulator(BallProperties.Baseball, environment, AerodynamicModel.Baseball);
            TrajectoryResult flight = simulator.Simulate(release, new FlightLimits(StopBehindPlateY, 0.0, MaxFlightTime));
            TrajectoryResult toContact = simulator.Simulate(release, new FlightLimits(contactPlaneY, 0.0, MaxFlightTime));
            return toContact.End == FlightEnd.CrossedStopPlane
                ? new HittingPitch(flight, contactPlaneY, toContact.Final.Time, toContact.Final)
                : new HittingPitch(flight, contactPlaneY, double.NaN, default);
        }
    }
}
