using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Pitching
{
    /// <summary>
    /// Pitch measurements in SI (convert for display). Plate values are taken where the ball centre crosses the
    /// front of home plate (Y = <see cref="PitchingGeometry.PlateFrontY"/>), like Statcast plate_x/plate_z.
    /// Movement = (actual plate crossing) − (no-lift reference plate crossing), with both flights started from
    /// the same release state; i.e. the displacement caused by the Magnus force over the full flight from release,
    /// catcher's view (+X = first-base side, +Z = up). Gravity and drag act in both flights, so they approximately
    /// cancel (Magnus changes the path and speed slightly, which changes drag).
    /// </summary>
    public readonly struct PitchMetrics
    {
        public readonly double ReleaseSpeed;
        public readonly double SpinRate;
        public readonly Vector3d SpinVector;
        /// <summary>Fraction of spin perpendicular to the release airflow (0–1).</summary>
        public readonly double SpinEfficiency;
        /// <summary>False if the ball reached the ground first; plate values and flight time are then NaN.</summary>
        public readonly bool ReachedPlate;
        /// <summary>Release to the plate-front crossing, s.</summary>
        public readonly double FlightTime;
        public readonly double PlateX;
        public readonly double PlateZ;
        public readonly double PlateSpeed;
        /// <summary>NaN unless both the flight and the no-lift reference reach the plate.</summary>
        public readonly double HorizontalMovement;
        public readonly double VerticalMovement;
        /// <summary>Lowest Reynolds number over the recorded flight (air-relative speed).</summary>
        public readonly double MinReynolds;
        /// <summary>Highest spin parameter S = r·ω⊥/v over the recorded flight.</summary>
        public readonly double MaxSpinParameter;

        private PitchMetrics(TrajectoryResult flight, TrajectoryResult reference, BallProperties ball, EnvironmentState environment)
        {
            BallState release = flight.First;
            BallState plate = flight.Final;
            bool reachedPlate = flight.End == FlightEnd.CrossedStopPlane;
            bool movementDefined = reachedPlate && reference.End == FlightEnd.CrossedStopPlane;

            ReleaseSpeed = release.Velocity.Length;
            SpinRate = release.Spin.Length;
            SpinVector = release.Spin;
            SpinEfficiency = SpinRate > 0.0 ? TransverseSpin(release.Velocity - environment.Wind, release.Spin) / SpinRate : 0.0;
            ReachedPlate = reachedPlate;
            FlightTime = reachedPlate ? plate.Time - release.Time : double.NaN;
            PlateX = reachedPlate ? plate.Position.X : double.NaN;
            PlateZ = reachedPlate ? plate.Position.Z : double.NaN;
            PlateSpeed = reachedPlate ? plate.Velocity.Length : double.NaN;
            HorizontalMovement = movementDefined ? plate.Position.X - reference.Final.Position.X : double.NaN;
            VerticalMovement = movementDefined ? plate.Position.Z - reference.Final.Position.Z : double.NaN;

            double minReynolds = double.PositiveInfinity;
            double maxSpinParameter = 0.0;
            for (int i = 0; i < flight.Samples.Count; i++)
            {
                BallState s = flight.Samples[i];
                Vector3d air = s.Velocity - environment.Wind;
                double speed = air.Length;
                minReynolds = Math.Min(minReynolds, environment.AirDensity * speed * 2.0 * ball.Radius / EnvironmentState.AirDynamicViscosity);
                if (speed > 0.0) maxSpinParameter = Math.Max(maxSpinParameter, ball.Radius * TransverseSpin(air, s.Spin) / speed);
            }

            MinReynolds = minReynolds;
            MaxSpinParameter = maxSpinParameter;
        }

        /// <summary>
        /// True when the whole flight stays where the drag and lift coefficients are supported by data
        /// (<see cref="AerodynamicModel.MinSupportedReynolds"/>, <see cref="AerodynamicModel.MaxSupportedSpinParameter"/>).
        /// Outside it the simulation still runs, but its results are extrapolations.
        /// </summary>
        public bool WithinSupportedAerodynamicRange =>
            MinReynolds >= AerodynamicModel.MinSupportedReynolds && MaxSpinParameter <= AerodynamicModel.MaxSupportedSpinParameter;

        public static PitchMetrics From(TrajectoryResult flight, TrajectoryResult noLiftReference, BallProperties ball, EnvironmentState environment) =>
            new PitchMetrics(flight, noLiftReference, ball, environment);

        private static double TransverseSpin(Vector3d air, Vector3d spin)
        {
            double speed = air.Length;
            return speed > 0.0 ? Vector3d.Cross(spin, air).Length / speed : 0.0;
        }
    }
}
