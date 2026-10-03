namespace Pitchlab.Simulation.Pitching
{
    /// <summary>
    /// Convenience starting points: right-handed pitches with approximate MLB-average speed and spin rate. Spin axis and
    /// efficiency are *effective* (movement-equivalent) values (ADR 0003): fastballs/changeup use typical efficiencies;
    /// slider and curveball axis/gyro were tuned to published average movement (≈ +5.5 / +1.5 in and +9 / −10 in HB/IVB),
    /// since Hawk-Eye active spin overstates their movement in this model. Release angles aim each at mid-zone.
    /// Presets are only initial conditions; the label never reaches the physics.
    /// </summary>
    public static class PitchPresets
    {
        public static PitchInput FourSeam => Create("Four-Seam-like", -1.8, 5.9, 6.4, 94.5, -2.55, 2.69, 2300, 205, -25);
        public static PitchInput Sinker => Create("Sinker-like", -1.9, 5.7, 6.3, 93.5, -1.54, 3.49, 2150, 240, -28);
        public static PitchInput Slider => Create("Slider-like", -1.9, 5.8, 6.2, 85.5, -0.39, 1.56, 2450, 111.2, -78.5);
        public static PitchInput Curveball => Create("Curveball-like", -1.8, 6.0, 6.0, 79.5, 0.97, 1.13, 2550, 48.7, -66.8);
        public static PitchInput Changeup => Create("Changeup-like", -1.9, 5.8, 6.4, 85.5, -0.91, 3.40, 1750, 245, -35);

        public static PitchInput[] All => new[] { FourSeam, Sinker, Slider, Curveball, Changeup };

        private static PitchInput Create(string label, double sideFeet, double heightFeet, double extensionFeet, double mph,
            double verticalDegrees, double horizontalDegrees, double rpm, double spinAxisDegrees, double gyroDegrees) => new PitchInput
        {
            Label = label,
            ReleaseSideFeet = sideFeet,
            ReleaseHeightFeet = heightFeet,
            ExtensionFeet = extensionFeet,
            SpeedMph = mph,
            VerticalAngleDegrees = verticalDegrees,
            HorizontalAngleDegrees = horizontalDegrees,
            SpinRateRpm = rpm,
            SpinAxisDegrees = spinAxisDegrees,
            GyroAngleDegrees = gyroDegrees,
        };
    }
}
