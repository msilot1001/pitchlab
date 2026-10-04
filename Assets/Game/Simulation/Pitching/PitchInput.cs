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
        /// Statcast spin axis, degrees: direction of the spin vector's projection onto the fixed X–Z plane
        /// (catcher's view), 180 = pure backspin, 0 = pure topspin, 90 = Magnus push toward +X (first base),
        /// 270 = toward −X (third base).
        /// </summary>
        public double SpinAxisDegrees;
        /// <summary>
        /// Gyro angle, degrees, −90..90: tilt of the spin vector out of the X–Z plane toward the plate (positive,
        /// along −Y, the direction of motion) or toward the pitcher (negative). For a pitch released along −Y the spin
        /// efficiency is cos(gyro angle); <see cref="PitchMetrics.SpinEfficiency"/> reports the exact value.
        /// </summary>
        public double GyroAngleDegrees;

        public BallState ToInitialState()
        {
            if (!IsFinite(ReleaseSideFeet) || !IsFinite(ReleaseHeightFeet) || !IsFinite(ExtensionFeet) || !IsFinite(VerticalAngleDegrees) ||
                !IsFinite(HorizontalAngleDegrees) || !IsFinite(SpinAxisDegrees) || !IsFinite(GyroAngleDegrees))
                throw new ArgumentException("Pitch input must be finite.");
            if (!(SpeedMph > 0.0) || double.IsInfinity(SpeedMph)) throw new ArgumentOutOfRangeException(nameof(SpeedMph));
            if (!(SpinRateRpm >= 0.0) || double.IsInfinity(SpinRateRpm)) throw new ArgumentOutOfRangeException(nameof(SpinRateRpm));

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
                SpinDirection(Units.DegreesToRadians(SpinAxisDegrees), Units.DegreesToRadians(GyroAngleDegrees));
            return new BallState(0.0, position, velocity, spin);
        }

        /// <summary>
        /// Unit spin vector from the Statcast spin axis θ and gyro angle γ, in the fixed simulation frame:
        /// ω̂ = cos γ·(cos θ·X + sin θ·Z) + sin γ·(−Y). Its X–Z projection has exactly Statcast's spin-axis angle.
        /// Right-hand rule: θ = 180° gives −X, i.e. backspin (Magnus +Z) for a pitch moving along −Y.
        /// </summary>
        public static Vector3d SpinDirection(double spinAxisRadians, double gyroRadians) => new Vector3d(
            Math.Cos(gyroRadians) * Math.Cos(spinAxisRadians),
            -Math.Sin(gyroRadians),
            Math.Cos(gyroRadians) * Math.Sin(spinAxisRadians));

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
