namespace Pitchlab.Simulation.Pitching
{
    /// <summary>
    /// Convenience starting points: right-handed pitches with approximate MLB-average speed, spin rate, spin axis,
    /// and spin efficiency (see Docs/PHYSICS.md). Release angles were tuned so each crosses near the middle of
    /// the default zone. Presets are only initial conditions; the label never reaches the physics.
    /// </summary>
    public static class PitchPresets
    {
        public static PitchInput FourSeam => Create("Four-Seam-like", -1.8, 5.9, 6.4, 94.5, -2.55, 2.69, 2300, 205, -25);
        public static PitchInput Sinker => Create("Sinker-like", -1.9, 5.7, 6.3, 93.5, -1.54, 3.49, 2150, 240, -28);
        public static PitchInput Slider => Create("Slider-like", -1.9, 5.8, 6.2, 85.5, -0.35, 1.21, 2450, 97, -72);
        public static PitchInput Curveball => Create("Curveball-like", -1.8, 6.0, 6.0, 79.5, 1.36, 1.01, 2550, 40, -52);
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
