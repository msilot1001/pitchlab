using Pitchlab.Simulation.BallFlight;

namespace Pitchlab.Simulation.Pitching
{
    /// <summary>A simulated pitch: the actual flight, its no-lift reference, and derived metrics.</summary>
    public sealed class PitchResult
    {
        public PitchResult(TrajectoryResult flight, TrajectoryResult noLiftReference, PitchMetrics metrics)
        {
            Flight = flight;
            NoLiftReference = noLiftReference;
            Metrics = metrics;
        }

        public TrajectoryResult Flight { get; }
        /// <summary>Same release state, same drag and gravity, Magnus force disabled. Defines movement.</summary>
        public TrajectoryResult NoLiftReference { get; }
        public PitchMetrics Metrics { get; }
    }

    /// <summary>Runs a pitch from release to the front of home plate (or the ground).</summary>
    public static class PitchSimulation
    {
        /// <summary>Upper bound on flight time; far above any pitch (~0.4–0.6 s).</summary>
        public const double MaxFlightTime = 3.0;

        public static PitchResult Run(BallState release, BallProperties ball, EnvironmentState environment, AerodynamicModel aerodynamics,
            double timeStep = BallFlightSimulator.DefaultTimeStep)
        {
            var flightLimits = new FlightLimits(PitchingGeometry.PlateFrontY, 0.0, MaxFlightTime);
            // The reference ignores the ground so movement stays defined for pitches that would bounce.
            var referenceLimits = new FlightLimits(PitchingGeometry.PlateFrontY, double.NegativeInfinity, MaxFlightTime);

            TrajectoryResult flight = new BallFlightSimulator(ball, environment, aerodynamics, timeStep).Simulate(release, flightLimits);
            TrajectoryResult reference = new BallFlightSimulator(ball, environment, aerodynamics.WithoutLift, timeStep).Simulate(release, referenceLimits);
            return new PitchResult(flight, reference, PitchMetrics.From(flight, reference, ball, environment));
        }

        public static PitchResult Run(PitchInput input, EnvironmentState environment) =>
            Run(input.ToInitialState(), BallProperties.Baseball, environment, AerodynamicModel.Baseball);
    }
}
