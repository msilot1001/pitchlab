using System;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// Where a pitch is aimed relative to the preset's own (mid-zone) target (TASK-012): a small change of the release angles
    /// only — speed, spin and release point stay the preset's, so the flight physics decide where it really crosses. In and
    /// away are for a right-handed batter (on the third-base side). Not pitcher AI or command (TASK-014).
    /// </summary>
    public readonly struct PitchLocation
    {
        public PitchLocation(string name, double dx, double dz)
        {
            Name = name;
            Dx = dx;
            Dz = dz;
        }

        public string Name { get; }
        /// <summary>Aim offset at the plate (m): across (+ = first-base side) and up.</summary>
        public double Dx { get; }
        public double Dz { get; }

        /// <summary>Strikes first (inside the zone with margin), then balls (clearly outside it).</summary>
        public static readonly PitchLocation[] All =
        {
            new PitchLocation("Middle", 0.0, 0.0),
            new PitchLocation("Up", 0.0, 0.22),
            new PitchLocation("Down", 0.0, -0.22),
            new PitchLocation("In", -0.15, 0.0),
            new PitchLocation("Away", 0.15, 0.0),
            new PitchLocation("Ball high", 0.0, 0.55),
            new PitchLocation("Ball low", 0.0, -0.5),
            new PitchLocation("Ball in", -0.45, 0.0),
            new PitchLocation("Ball away", 0.45, 0.0),
        };

        /// <summary>The preset aimed here: its release angles turned by the offset seen from the release point.</summary>
        public PitchInput Aim(PitchInput pitch)
        {
            double distance = PitchingGeometry.RubberFrontY - Units.FeetToMeters(pitch.ExtensionFeet) - PitchingGeometry.PlateFrontY;
            pitch.VerticalAngleDegrees += Units.RadiansToDegrees(Math.Atan2(Dz, distance));
            pitch.HorizontalAngleDegrees += Units.RadiansToDegrees(Math.Atan2(Dx, distance));
            return pitch;
        }

        public override string ToString() => Name;
    }
}
