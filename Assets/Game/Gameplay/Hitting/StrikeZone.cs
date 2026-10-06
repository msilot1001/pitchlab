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
    /// the front plane of home plate (the Statcast plate_x / plate_z plane) — over the 17-inch plate, between the batter's
    /// zone bottom and top (TASK-013: from his height; the default zone when there is no batter, as in the HittingLab), each
    /// widened by the ball's radius. REFERENCE CONVENTION: the rulebook zone (OBR Definitions, "Strike Zone") is the volume
    /// over the whole plate and its height comes from the batter's stance; this is the usual 2-D approximation.
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
            if (!Offered(swing, result)) return StrikeZone.IsStrike(pitch, zoneBottom, zoneTop) ? PitchOutcome.CalledStrike : PitchOutcome.Ball;
            if (!(result is ContactResult r) || !r.IsContact) return PitchOutcome.SwingingStrike;
            if (play == null) throw new ArgumentNullException(nameof(play), "a batted ball has a play");
            return play.IsFoul ? PitchOutcome.Foul : PitchOutcome.InPlay;
        }

        /// <summary>Did he swing? A swing checked before the offer point is none (TASK-024).</summary>
        public static bool Offered(SwingInput? swing, ContactResult? result) =>
            swing.HasValue && !(result is ContactResult r && r.Outcome == ContactOutcome.CheckedSwing);

        /// <summary>
        /// The pitch's result decided before any play (TASK-024), or null when a batted ball must be played out:
        /// <list type="bullet">
        /// <item>contact caught as a foul tip → <see cref="PitchOutcome.FoulTip"/> (Definitions, "Foul tip");</item>
        /// <item>other contact → null (the play decides);</item>
        /// <item>no contact and the pitch touches the batter: after a swing a strike, in the zone a strike, otherwise
        /// <see cref="PitchOutcome.HitByPitch"/> (OBR 5.05(b)(2), Definitions "Strike" (e), (f));</item>
        /// <item>otherwise a swinging strike, or the zone call for a take (a checked swing is a take).</item>
        /// </list>
        /// </summary>
        public static PitchOutcome? BeforePlay(HittingPitch pitch, SwingInput? swing, ContactResult? result, BatterSide side, double heightInches, double zoneBottom, double zoneTop) =>
            BeforePlay(pitch, swing, result, result is ContactResult r && r.IsContact ? null : BatterBody.FirstTouch(pitch, side, heightInches), zoneBottom, zoneTop);

        /// <summary>The same with the batter's touch already found (<paramref name="touch"/>; null: the pitch missed him).</summary>
        public static PitchOutcome? BeforePlay(HittingPitch pitch, SwingInput? swing, ContactResult? result, BodyHit? touch, double zoneBottom, double zoneTop)
        {
            if (pitch == null) throw new ArgumentNullException(nameof(pitch));
            if (result is ContactResult r && r.IsContact) return FoulTips.IsCaught(pitch, r) ? PitchOutcome.FoulTip : (PitchOutcome?)null;
            bool swung = Offered(swing, result), strike = StrikeZone.IsStrike(pitch, zoneBottom, zoneTop);
            if (swung) return PitchOutcome.SwingingStrike;
            if (touch.HasValue && !strike) return PitchOutcome.HitByPitch;
            return strike ? PitchOutcome.CalledStrike : PitchOutcome.Ball;
        }

        public static string Describe(PitchOutcome o) => o switch
        {
            PitchOutcome.Ball => "BALL",
            PitchOutcome.CalledStrike => "CALLED STRIKE",
            PitchOutcome.SwingingStrike => "SWINGING STRIKE",
            PitchOutcome.Foul => "FOUL",
            PitchOutcome.HitByPitch => "HIT BY PITCH",
            PitchOutcome.FoulTip => "FOUL TIP",
            _ => "IN PLAY",
        };
    }
}
