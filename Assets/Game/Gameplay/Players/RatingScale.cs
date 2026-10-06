using System;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Running;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Players
{
    /// <summary>
    /// Display ratings → the physical and input parameters gameplay runs on (TASK-017; Docs/PLAYER_RATINGS.md records the
    /// source of every range). Each mapping is linear in r̂ = (rating − 50) / 50 ∈ [−1, 1], monotonic, and gives the existing
    /// generic value at 50 — so an all-50 player plays exactly as the generic profiles did. Nothing here decides an outcome:
    /// these are the inputs (speeds, delays, arm speeds) the simulation then plays out.
    /// </summary>
    public static class RatingScale
    {
        /// <summary>The normalised rating r̂ ∈ [−1, 1].</summary>
        public static double Unit(int rating) => (rating - PlayerRatings.Average) / (double)PlayerRatings.Average;

        // ------------------------------------------------------------------ running

        /// <summary>Sprint speed (ft/s, Statcast definition): 27 ± 3.5 → 23.5–30.5 (MEASURED range: MLB average 27, elite ≥ 30,
        /// slowest regulars ≈ 23–24).</summary>
        public static double SprintSpeedFtPerS(int speed) => 27.0 + 3.5 * Unit(speed);

        /// <summary>Running acceleration time constant τ (s): 0.69 · (1 ∓ 0.15) (DERIVED at 50 from the Statcast 90-ft split;
        /// the ± spread is a GAMEPLAY ASSUMPTION — no public per-player acceleration measure).</summary>
        public static double RunnerAccelerationTime(int acceleration) => 0.69 * (1.0 - 0.15 * Unit(acceleration));

        /// <summary>A runner on base reading the ball off the bat (s): 0.25 ∓ 0.08 (GAMEPLAY ASSUMPTION).</summary>
        public static double ReadDelay(int baserunning) => 0.25 - 0.08 * Unit(baserunning);

        public static RunnerProfile Runner(PlayerRatings r) =>
            new RunnerProfile(SprintSpeedFtPerS(r.Speed) * FtToM, RunnerAccelerationTime(r.Acceleration), 6.0, 0.8, 0.34, ReadDelay(r.Baserunning));

        // ------------------------------------------------------------------ fielding

        /// <summary>
        /// A defender at <paramref name="position"/>: the position's generic profile (Docs/FIELDING.md) with his reaction scaled
        /// 1 ∓ 25 % (outfield 0.30–0.50 s, infield 0.19–0.31 s; Statcast jump spread, DERIVED/ASSUMED), his own sprint speed and
        /// an acceleration τ scaled 1 ∓ 15 % (ASSUMED). Reach, heights and braking stay generic.
        /// </summary>
        public static FielderProfile Fielder(PlayerRatings r, DefensivePosition position)
        {
            FielderProfile b = FielderProfile.For(position);
            return new FielderProfile(b.ReactionTime * (1.0 - 0.25 * Unit(r.Reaction)), SprintSpeedFtPerS(r.Speed) * FtToM,
                b.AccelerationTime * (1.0 - 0.15 * Unit(r.Acceleration)), b.BrakeDeceleration, b.Reach, b.GroundReach, b.CatchHeightMax, b.PickupHeightMax);
        }

        // ------------------------------------------------------------------ throwing

        /// <summary>Arm strength (mph, Statcast's measure: a fielder's hardest throws): the position's MLB average ± 8 mph
        /// (MEASURED spread of the Statcast arm-strength leaderboard: ≈ ±8 mph around each position's average).</summary>
        public static double ArmStrengthMph(int armStrength, DefensivePosition position) => ThrowProfile.ArmStrengthMph(position) + 8.0 * Unit(armStrength);

        /// <summary>Glove-to-hand transfer: the position's generic time · (1 ∓ 20 %) (GAMEPLAY ASSUMPTION; infield 0.56–0.84 s).</summary>
        public static double TransferTime(int transfer, DefensivePosition position) => ThrowProfile.For(position).TransferTime * (1.0 - 0.2 * Unit(transfer));

        /// <summary>A routine throw (85 % of his arm strength, as the generic profile).</summary>
        public static ThrowProfile Throw(PlayerRatings r, DefensivePosition position) =>
            new ThrowProfile(ThrowProfile.RoutineFactor * Units.MphToMetersPerSecond(ArmStrengthMph(r.ArmStrength, position)), TransferTime(r.Transfer, position), ThrowProfile.For(position).ReleaseHeight);

        /// <summary>A max-effort throw (his arm strength).</summary>
        public static ThrowProfile FullThrow(PlayerRatings r, DefensivePosition position) =>
            new ThrowProfile(Units.MphToMetersPerSecond(ArmStrengthMph(r.ArmStrength, position)), TransferTime(r.Transfer, position), ThrowProfile.For(position).ReleaseHeight);

        private const double FtToM = 0.3048;
    }
}
