using System;

namespace Pitchlab.Simulation.Core
{
    /// <summary>Unit conversions. The simulation is SI internally; convert only at input/output boundaries.</summary>
    public static class Units
    {
        /// <summary>Exact by definition of the international mile (1609.344 m) and hour.</summary>
        public const double MetersPerSecondPerMph = 0.44704;
        /// <summary>Exact by definition of the international foot.</summary>
        public const double MetersPerFoot = 0.3048;
        public const double MetersPerInch = 0.0254;
        public const double RadiansPerSecondPerRpm = 2.0 * Math.PI / 60.0;

        public static double MphToMetersPerSecond(double mph) => mph * MetersPerSecondPerMph;
        public static double MetersPerSecondToMph(double mps) => mps / MetersPerSecondPerMph;
        public static double RpmToRadiansPerSecond(double rpm) => rpm * RadiansPerSecondPerRpm;
        public static double RadiansPerSecondToRpm(double radPerSec) => radPerSec / RadiansPerSecondPerRpm;
        public static double FeetToMeters(double feet) => feet * MetersPerFoot;
        public static double MetersToFeet(double meters) => meters / MetersPerFoot;
        public static double InchesToMeters(double inches) => inches * MetersPerInch;
        public static double MetersToInches(double meters) => meters / MetersPerInch;
        public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
        public static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
    }
}
