namespace Pitchlab.Simulation.Tracking
{
    /// <summary>
    /// One Statcast pitch in Statcast's own units and frame (feet, ft/s, ft/s², mph, rpm, degrees; catcher's view,
    /// origin at the rear point of home plate, +y toward the pitcher, +z up — the same axes as the simulation).
    /// Field meanings follow the Baseball Savant CSV documentation; see Docs/VALIDATION_TASK002.md.
    /// </summary>
    public readonly struct StatcastPitch
    {
        public readonly string PitchType;
        /// <summary>Pitcher's throwing hand, "R" or "L" (metadata; never used by the physics).</summary>
        public readonly string Throws;
        public readonly double ReleaseSpeedMph;
        public readonly double ReleaseX, ReleaseY, ReleaseZ;
        public readonly double ReleaseExtension;
        /// <summary>Velocity of the 9-parameter fit at y = 50 ft (ft/s).</summary>
        public readonly double Vx0, Vy0, Vz0;
        /// <summary>Constant acceleration of the 9-parameter fit, gravity included (ft/s²).</summary>
        public readonly double Ax, Ay, Az;
        public readonly double SpinRateRpm;
        public readonly double SpinAxisDegrees;
        /// <summary>Savant movement, feet, catcher's view.</summary>
        public readonly double PfxX, PfxZ;
        /// <summary>Plate crossing (front edge of the plate), feet, catcher's view.</summary>
        public readonly double PlateX, PlateZ;

        public StatcastPitch(string pitchType, string throws, double releaseSpeedMph, double releaseX, double releaseY, double releaseZ, double releaseExtension,
            double vx0, double vy0, double vz0, double ax, double ay, double az, double spinRateRpm, double spinAxisDegrees,
            double pfxX, double pfxZ, double plateX, double plateZ)
        {
            PitchType = pitchType;
            Throws = throws;
            ReleaseSpeedMph = releaseSpeedMph;
            ReleaseX = releaseX;
            ReleaseY = releaseY;
            ReleaseZ = releaseZ;
            ReleaseExtension = releaseExtension;
            Vx0 = vx0;
            Vy0 = vy0;
            Vz0 = vz0;
            Ax = ax;
            Ay = ay;
            Az = az;
            SpinRateRpm = spinRateRpm;
            SpinAxisDegrees = spinAxisDegrees;
            PfxX = pfxX;
            PfxZ = pfxZ;
            PlateX = plateX;
            PlateZ = plateZ;
        }
    }
}
