using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Pitching
{
    /// <summary>
    /// Pitch measurements in SI (convert for display). Plate values are taken where the ball centre crosses the
    /// front of home plate (Y = <see cref="PitchingGeometry.PlateFrontY"/>), like Statcast plate_x/plate_z.
    /// Movement = (actual plate crossing) − (no-lift reference plate crossing), with both flights started from
    /// the same release state; i.e. the displacement caused by the Magnus force over the full flight from release,
    /// catcher's view (+X = first-base side, +Z = up). Drag and gravity are in both flights, so they cancel.
    /// </summary>
    public readonly struct PitchMetrics
    {
        public readonly double ReleaseSpeed;
        public readonly double SpinRate;
        public readonly Vector3d SpinVector;
        /// <summary>Fraction of spin perpendicular to the release velocity (0–1).</summary>
        public readonly double SpinEfficiency;
        /// <summary>False if the ball reached the ground first; plate values and flight time are then NaN.</summary>
        public readonly bool ReachedPlate;
        /// <summary>Release to the plate-front crossing, s.</summary>
        public readonly double FlightTime;
        public readonly double PlateX;
        public readonly double PlateZ;
        public readonly double PlateSpeed;
        public readonly double HorizontalMovement;
        public readonly double VerticalMovement;

        private PitchMetrics(BallState release, bool reachedPlate, BallState plate, bool referenceReachedPlate, BallState reference)
        {
            ReleaseSpeed = release.Velocity.Length;
            SpinRate = release.Spin.Length;
            SpinVector = release.Spin;
            double speed = release.Velocity.Length;
            Vector3d transverse = speed > 0.0 ? Vector3d.Cross(release.Spin, release.Velocity) / speed : Vector3d.Zero;
            SpinEfficiency = SpinRate > 0.0 ? transverse.Length / SpinRate : 0.0;
            ReachedPlate = reachedPlate;
            FlightTime = reachedPlate ? plate.Time - release.Time : double.NaN;
            PlateX = reachedPlate ? plate.Position.X : double.NaN;
            PlateZ = reachedPlate ? plate.Position.Z : double.NaN;
            PlateSpeed = reachedPlate ? plate.Velocity.Length : double.NaN;
            bool movementDefined = reachedPlate && referenceReachedPlate;
            HorizontalMovement = movementDefined ? plate.Position.X - reference.Position.X : double.NaN;
            VerticalMovement = movementDefined ? plate.Position.Z - reference.Position.Z : double.NaN;
        }

        public static PitchMetrics From(TrajectoryResult flight, TrajectoryResult noLiftReference) =>
            new PitchMetrics(flight.First, flight.End == FlightEnd.CrossedStopPlane, flight.Final,
                noLiftReference.End == FlightEnd.CrossedStopPlane, noLiftReference.Final);
    }
}
