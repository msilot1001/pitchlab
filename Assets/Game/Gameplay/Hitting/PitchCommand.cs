using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// Where the pitcher aims (TASK-014), relative to the batter's zone, seen from the catcher: left is the third-base side
    /// (inside to a right-handed batter). Nine spots inside the zone (with a margin from its edges) and four clearly outside.
    /// </summary>
    public enum PitchTarget
    {
        UpLeft,
        UpMiddle,
        UpRight,
        MiddleLeft,
        Middle,
        MiddleRight,
        DownLeft,
        DownMiddle,
        DownRight,
        BallUp,
        BallDown,
        BallLeft,
        BallRight,
    }

    /// <summary>A pitch as called for: which preset (pitch type) and where (TASK-014). Only initial conditions are chosen —
    /// the flight decides where it really crosses, and the call reads the crossing.</summary>
    public readonly struct PitchCommand : IEquatable<PitchCommand>
    {
        public PitchCommand(int preset, PitchTarget target)
        {
            Preset = preset;
            Target = target;
        }

        /// <summary>Index into <see cref="PitchPresets.All"/>.</summary>
        public int Preset { get; }
        public PitchTarget Target { get; }

        public bool Equals(PitchCommand other) => Preset == other.Preset && Target == other.Target;
        public override bool Equals(object obj) => obj is PitchCommand c && Equals(c);
        public override int GetHashCode() => Preset * 31 + (int)Target;
        public override string ToString() => $"{PitchPresets.All[Preset].Label} {PitchTargets.Name(Target)}";
    }

    public static class PitchTargets
    {
        public static readonly PitchTarget[] All = (PitchTarget[])Enum.GetValues(typeof(PitchTarget));
        /// <summary>In-zone spots sit at this fraction of the zone's half-width / half-height from its centre.</summary>
        public const double Inner = 0.6;
        /// <summary>Out-of-zone spots: this far (m) beyond the zone's top / bottom edge, or beyond the plate's side.</summary>
        public const double Beyond = 0.22, Wide = 0.25;

        public static bool InZone(PitchTarget t) => t <= PitchTarget.DownRight;

        /// <summary>The aim point at the front plane of the plate (x across, z up; m) for a zone from <paramref name="bottom"/>
        /// to <paramref name="top"/>.</summary>
        public static (double X, double Z) Point(PitchTarget t, double bottom, double top)
        {
            double w = StrikeZone.HalfWidth, cz = 0.5 * (bottom + top), h = 0.5 * (top - bottom);
            switch (t)
            {
                case PitchTarget.BallUp: return (0.0, top + Beyond);
                case PitchTarget.BallDown: return (0.0, bottom - Beyond);
                case PitchTarget.BallLeft: return (-(w + Wide), cz);
                case PitchTarget.BallRight: return (w + Wide, cz);
            }

            int i = (int)t, row = i / 3, column = i % 3;
            return ((column - 1) * Inner * w, cz + (1 - row) * Inner * h);
        }

        public static string Name(PitchTarget t) => t switch
        {
            PitchTarget.UpLeft => "up-left", PitchTarget.UpMiddle => "up", PitchTarget.UpRight => "up-right",
            PitchTarget.MiddleLeft => "left", PitchTarget.Middle => "middle", PitchTarget.MiddleRight => "right",
            PitchTarget.DownLeft => "down-left", PitchTarget.DownMiddle => "down", PitchTarget.DownRight => "down-right",
            PitchTarget.BallUp => "ball up", PitchTarget.BallDown => "ball down", PitchTarget.BallLeft => "ball left",
            _ => "ball right",
        };

        /// <summary>
        /// The preset aimed at (<paramref name="x"/>, <paramref name="z"/>): its release angles corrected from where the
        /// simulated flight actually crosses the plate's front plane (a few deterministic iterations; speed, spin and release
        /// point stay the preset's). The pitch can still miss slightly — the call never uses the aim.
        /// </summary>
        public static PitchInput Aim(PitchInput preset, double x, double z, EnvironmentState environment)
        {
            var simulator = new BallFlightSimulator(BallProperties.Baseball, environment, AerodynamicModel.Baseball);
            double distance = PitchingGeometry.RubberFrontY - Units.FeetToMeters(preset.ExtensionFeet) - PitchingGeometry.PlateFrontY;
            PitchInput aimed = preset;
            for (int i = 0; i < 3; i++)
            {
                BallState crossing = simulator.Simulate(aimed.ToInitialState(), new FlightLimits(PitchingGeometry.PlateFrontY, 0.0, HittingPitch.MaxFlightTime)).Final;
                aimed.HorizontalAngleDegrees += Units.RadiansToDegrees(Math.Atan2(x - crossing.Position.X, distance));
                aimed.VerticalAngleDegrees += Units.RadiansToDegrees(Math.Atan2(z - crossing.Position.Z, distance));
            }

            return aimed;
        }

        /// <summary>The command's pitch for a batter whose zone runs from <paramref name="bottom"/> to <paramref name="top"/>.</summary>
        public static HittingPitch Create(PitchCommand command, double bottom, double top, EnvironmentState environment)
        {
            (double x, double z) = Point(command.Target, bottom, top);
            return HittingPitch.Create(Aim(PitchPresets.All[command.Preset], x, z, environment), environment);
        }
    }
}
