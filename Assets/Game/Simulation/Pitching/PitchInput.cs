using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Pitching
{
    /// <summary>
    /// Pitch release described in baseball units (ft, mph, rpm, degrees). This is the unit-conversion boundary:
    /// <see cref="ToInitialState"/> produces the SI simulation state. <see cref="Label"/> is display metadata only and
    /// never affects the flight. Conventions are documented in Docs/PHYSICS.md.
    /// </summary>
    [Serializable]
    public struct PitchInput
    {
        public string Label;

        /// <summary>Release X, ft. Catcher's view: negative = third-base side (typical right-hander).</summary>
        public double ReleaseSideFeet;
        /// <summary>Release height above the ground at the plate, ft.</summary>
        public double ReleaseHeightFeet;
        /// <summary>Distance from the front of the rubber toward the plate at release, ft. Release Y = 60.5 − extension.</summary>
        public double ExtensionFeet;

        public double SpeedMph;
        /// <summary>Velocity elevation at release, degrees. Positive = upward.</summary>
        public double VerticalAngleDegrees;
        /// <summary>Velocity azimuth at release, degrees. Positive = toward +X (first-base side).</summary>
        public double HorizontalAngleDegrees;

        public double SpinRateRpm;
        /// <summary>
        /// Statcast spin axis, degrees, for the transverse spin: 180 = pure backspin, 0 = pure topspin,
        /// 90 = Magnus push toward +X (first base), 270 = toward −X (third base).
        /// </summary>
        public double SpinAxisDegrees;
        /// <summary>
        /// Gyro angle, degrees, −90..90: tilt of the spin vector out of the transverse plane toward the
        /// direction of motion (positive) or against it (negative). Spin efficiency = cos(gyro angle).
        /// </summary>
        public double GyroAngleDegrees;

        public double SpinEfficiency => Math.Cos(Units.DegreesToRadians(GyroAngleDegrees));

        public BallState ToInitialState()
        {
            var position = new Vector3d(
                Units.FeetToMeters(ReleaseSideFeet),
                PitchingGeometry.RubberFrontY - Units.FeetToMeters(ExtensionFeet),
                Units.FeetToMeters(ReleaseHeightFeet));

            double speed = Units.MphToMetersPerSecond(SpeedMph);
            double elevation = Units.DegreesToRadians(VerticalAngleDegrees);
            double azimuth = Units.DegreesToRadians(HorizontalAngleDegrees);
            var velocity = speed * new Vector3d(
                Math.Cos(elevation) * Math.Sin(azimuth),
                -Math.Cos(elevation) * Math.Cos(azimuth),
                Math.Sin(elevation));

            Vector3d spin = Units.RpmToRadiansPerSecond(SpinRateRpm) *
                SpinDirection(velocity, Units.DegreesToRadians(SpinAxisDegrees), Units.DegreesToRadians(GyroAngleDegrees));
            return new BallState(0.0, position, velocity, spin);
        }

        /// <summary>
        /// Unit spin vector for a ball moving along <paramref name="velocity"/>. Transverse basis (right-hand rule):
        /// e_h = normalize(Z × v̂), e_v = v̂ × e_h; for a pitch moving along −Y, e_h = +X and e_v = +Z.
        /// Transverse direction = cos θ·e_h + sin θ·e_v, so θ = 180° gives −X (backspin: Magnus lift +Z).
        /// </summary>
        public static Vector3d SpinDirection(Vector3d velocity, double spinAxisRadians, double gyroRadians)
        {
            if (velocity.LengthSquared == 0.0) throw new ArgumentException("Spin axis angle is undefined for zero velocity.", nameof(velocity));
            Vector3d forward = velocity.Normalized;
            Vector3d horizontal = Vector3d.Cross(new Vector3d(0.0, 0.0, 1.0), forward).Normalized;
            if (horizontal.LengthSquared == 0.0) throw new ArgumentException("Spin axis angle is undefined for vertical velocity.", nameof(velocity));
            Vector3d vertical = Vector3d.Cross(forward, horizontal);
            Vector3d transverse = Math.Cos(spinAxisRadians) * horizontal + Math.Sin(spinAxisRadians) * vertical;
            return Math.Cos(gyroRadians) * transverse + Math.Sin(gyroRadians) * forward;
        }
    }
}
