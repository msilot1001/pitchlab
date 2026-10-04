using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Pitching
{
    /// <summary>
    /// Field reference geometry in the simulation frame (metres). Origin: rear point of home plate, on the ground.
    /// +Y toward the pitcher, +Z up, +X toward the catcher's right (first-base side). Same axes as Statcast.
    /// </summary>
    public static class PitchingGeometry
    {
        /// <summary>Front of the pitching rubber to the rear point of home plate: 60 ft 6 in (OBR 2.01).</summary>
        public static readonly double RubberFrontY = Units.FeetToMeters(60.5);
        /// <summary>Front edge of home plate, 17 in from the rear point (Statcast plate_x/plate_z plane).</summary>
        public static readonly double PlateFrontY = Units.InchesToMeters(17.0);
        /// <summary>Half of the 17 in plate width.</summary>
        public static readonly double PlateHalfWidth = Units.InchesToMeters(8.5);
        /// <summary>Default strike-zone bounds; real zones depend on the batter. Game parameter.</summary>
        public static readonly double DefaultZoneBottom = Units.FeetToMeters(1.5);
        public static readonly double DefaultZoneTop = Units.FeetToMeters(3.5);
    }
}
