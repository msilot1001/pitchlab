using System;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// The called strike (TASK-012): a pitch is a strike when any part of the ball passes through the zone where it crosses
    /// the front plane of home plate (the Statcast plate_x / plate_z plane) — over the 17-inch plate, between the default
    /// zone's bottom and top, each widened by the ball's radius. REFERENCE CONVENTION: the rulebook zone (OBR Definitions,
    /// "Strike Zone") is the volume over the whole plate and its height comes from the batter's stance; this is the usual
    /// 2-D approximation with a fixed batter.
    /// </summary>
    public static class StrikeZone
    {
        public static double HalfWidth => PitchingGeometry.PlateHalfWidth;
        public static double Bottom => PitchingGeometry.DefaultZoneBottom;
        public static double Top => PitchingGeometry.DefaultZoneTop;

        /// <summary>Where the pitch crosses the front plane of the plate (x across, z up); NaN if it never does.</summary>
        public static (double X, double Z) Crossing(HittingPitch pitch)
        {
            TrajectoryResult flight = pitch.Flight;
            double plane = PitchingGeometry.PlateFrontY;
            if (flight.First.Position.Y < plane || flight.Final.Position.Y > plane) return (double.NaN, double.NaN);
            // The flight moves toward the plate throughout (Y decreasing): bisect on time.
            double a = flight.First.Time, b = flight.Final.Time;
            for (int i = 0; i < 60; i++)
            {
                double m = 0.5 * (a + b);
                if (flight.StateAt(m).Position.Y > plane) a = m;
                else b = m;
            }

            Vector3d at = flight.StateAt(0.5 * (a + b)).Position;
            return (at.X, at.Z);
        }

        /// <summary>Is the ball (centre <paramref name="x"/>, <paramref name="z"/> at the plate's front plane) in the zone?</summary>
        public static bool Contains(double x, double z) => Contains(x, z, Bottom, Top);

        /// <summary>The same for a batter whose zone runs from <paramref name="bottom"/> to <paramref name="top"/> (m).</summary>
        public static bool Contains(double x, double z, double bottom, double top)
        {
            double r = BallProperties.Baseball.Radius;
            return Math.Abs(x) <= HalfWidth + r && z >= bottom - r && z <= top + r;
        }

        public static bool IsStrike(HittingPitch pitch) => IsStrike(pitch, Bottom, Top);

        public static bool IsStrike(HittingPitch pitch, double bottom, double top)
        {
            (double x, double z) = Crossing(pitch);
            return !double.IsNaN(x) && Contains(x, z, bottom, top);
        }
    }

    public static class PitchOutcomes
    {
        /// <summary>
        /// The pitch's result from the authoritative pitch, swing, contact and play: no swing → the zone call; a swing without
        /// contact → swinging strike; contact whose play ends dead without an award → foul; otherwise in play.
        /// </summary>
        public static PitchOutcome Of(HittingPitch pitch, SwingInput? swing, ContactResult? result, LivePlay play) =>
            Of(pitch, swing, result, play, StrikeZone.Bottom, StrikeZone.Top);

        /// <summary>The same against a batter whose zone runs from <paramref name="zoneBottom"/> to <paramref name="zoneTop"/>.</summary>
        public static PitchOutcome Of(HittingPitch pitch, SwingInput? swing, ContactResult? result, LivePlay play, double zoneBottom, double zoneTop)
        {
            if (pitch == null) throw new ArgumentNullException(nameof(pitch));
            if (!swing.HasValue) return StrikeZone.IsStrike(pitch, zoneBottom, zoneTop) ? PitchOutcome.CalledStrike : PitchOutcome.Ball;
            if (!(result is ContactResult r) || !r.IsContact) return PitchOutcome.SwingingStrike;
            if (play == null) throw new ArgumentNullException(nameof(play), "a batted ball has a play");
            return play.IsFoul ? PitchOutcome.Foul : PitchOutcome.InPlay;
        }

        public static string Describe(PitchOutcome o) => o switch
        {
            PitchOutcome.Ball => "BALL",
            PitchOutcome.CalledStrike => "CALLED STRIKE",
            PitchOutcome.SwingingStrike => "SWINGING STRIKE",
            PitchOutcome.Foul => "FOUL",
            _ => "IN PLAY",
        };
    }
}
