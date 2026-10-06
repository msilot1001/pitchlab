using System;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>A defender's handling skills (TASK-021): Fielding (ground balls), Catching (balls in the air, throws) and
    /// ArmAccuracy (throw direction), 0–100 like every rating.</summary>
    public readonly struct FielderSkill
    {
        public FielderSkill(int fielding, int catching, int armAccuracy)
        {
            Fielding = fielding;
            Catching = catching;
            ArmAccuracy = armAccuracy;
        }

        public int Fielding { get; }
        public int Catching { get; }
        public int ArmAccuracy { get; }
        public static FielderSkill Average => new FielderSkill(50, 50, 50);
        public static FielderSkill Of(PlayerRatings r) => new FielderSkill(r.Fielding, r.Catching, r.ArmAccuracy);
    }

    /// <summary>How a take went: clean, the ball knocked loose (a bobble or a drop), or not touched at all.</summary>
    public enum TakeOutcome
    {
        Clean,
        Bobble,
        Miss,
    }

    /// <summary>
    /// Defensive execution (TASK-021; Docs/DEFENSIVE_VARIABILITY.md). The difficulty of a take comes from what the play already
    /// knows at the take — the kind of take, the time to spare, the defender's speed, the ball's speed — and the defender's
    /// skill; a seeded draw then decides whether the ball is held. A failed take is physical: the ball is missed (it carries
    /// on along its own path) or knocked loose (a new free ball from the glove); it stays live and is fielded again. A throw
    /// is released with a direction error by ArmAccuracy and flies on the throw physics; the receiver reacts to the real
    /// throw. Nothing here decides an out or a base: the rules read what then happens.
    /// </summary>
    public static class FieldingExecution
    {
        /// <summary>An average defender's chance of not holding a take of each kind in routine conditions (ASSUMED, TUNED so
        /// whole games give ≈ 0.25 fielding misplays per team-game — about half of MLB's ≈ 0.5 errors, the fielding share —
        /// with routine plays nearly certain and dives often failing).</summary>
        public static double BaseChance(FieldingAction a) => a switch
        {
            FieldingAction.StandingCatch => 0.002,
            FieldingAction.RunningCatch => 0.004,
            FieldingAction.OverShoulderCatch => 0.03,
            FieldingAction.SlidingCatch => 0.05,
            FieldingAction.JumpingCatch => 0.10,
            FieldingAction.DivingCatch => 0.40,
            FieldingAction.HopCatch => 0.015,
            FieldingAction.WallPlay => 0.04,
            FieldingAction.CenteredPickup => 0.004,
            FieldingAction.ForehandPickup => 0.012,
            FieldingAction.BackhandPickup => 0.02,
            FieldingAction.ChargingPickup => 0.025,
            FieldingAction.ShortHopPickup => 0.04,
            FieldingAction.ReceiveThrow => 0.003,
            _ => 0.0,
        };

        /// <summary>The base chances (except dives) are scaled by this (TUNED to the whole-game misplay rate).</summary>
        public const double RoutineScale = 0.5;

        /// <summary>Is the take a ground ball handled with Fielding (otherwise a catch, handled with Catching)?</summary>
        public static bool IsPickup(FieldingAction a) => a == FieldingAction.CenteredPickup || a == FieldingAction.ForehandPickup
            || a == FieldingAction.BackhandPickup || a == FieldingAction.ChargingPickup || a == FieldingAction.ShortHopPickup;

        /// <summary>Skill scales the chance by e^(−r̂): an elite defender (90) ≈ × 0.45, a poor one (15) ≈ × 2.0 (ASSUMED).</summary>
        public const double SkillScale = 1.0;
        /// <summary>A rushed take (less than this much time to spare, s) is harder: × 1.5 (ASSUMED).</summary>
        public const double RushedMargin = 0.15;

        /// <summary>
        /// The chance that <paramref name="action"/> is not held: its base chance, harder for a hard-hit ball on the ground (× 1 +
        /// (v − 20 m/s)/15 above 20 m/s), a catch on the run (× 1 + v/20 at his speed v m/s), a rushed take, or a receiver who had
        /// to move for the throw (× 1 + 2·distance m, at most × 5); then scaled by his skill. At most 0.9.
        /// </summary>
        public static double Chance(FieldingAction action, double margin, double fielderSpeed, double ballSpeed, double moved, FielderSkill skill)
        {
            double p = BaseChance(action) * (action == FieldingAction.DivingCatch ? 1.0 : RoutineScale);
            bool pickup = IsPickup(action);
            if (pickup) p *= 1.0 + Math.Max(0.0, ballSpeed - 20.0) / 15.0;
            else if (action != FieldingAction.ReceiveThrow) p *= 1.0 + fielderSpeed / 20.0;
            else p *= Math.Min(5.0, 1.0 + 2.0 * moved);
            if (margin < RushedMargin) p *= 1.5;
            int rating = pickup ? skill.Fielding : skill.Catching;
            p *= Math.Exp(-SkillScale * RatingScale.Unit(rating));
            return Math.Min(0.9, p);
        }

        /// <summary>The outcome of a take with failure chance <paramref name="chance"/>: a failed ground ball is bobbled two times
        /// in three (ASSUMED), a failed dive is missed outright, any other failed catch is a drop or a miss half the time.</summary>
        public static TakeOutcome Attempt(FieldingAction action, double chance, ref SeedStream stream)
        {
            double u = stream.Unit(), v = stream.Unit();
            if (u >= chance) return TakeOutcome.Clean;
            if (action == FieldingAction.DivingCatch) return TakeOutcome.Miss;
            return v < (IsPickup(action) ? 0.67 : 0.5) ? TakeOutcome.Bobble : TakeOutcome.Miss;
        }

        // ------------------------------------------------------------------ throws

        /// <summary>Per-axis direction error of an average routine throw (°): 0.6° · (1 − 0.35 r̂(ArmAccuracy)), × 1.2 at full
        /// effort; 3 % of throws get away with 4 × the spread (ASSUMED, TUNED so ≈ 0.25 throwing misplays per team-game).</summary>
        public const double ThrowSigmaDeg = 0.6, AccuracyScale = 0.35, FullEffortFactor = 1.2, WildThrowChance = 0.03, WildThrowFactor = 4.0;

        public static double ThrowSigma(FielderSkill skill, bool fullEffort) =>
            ThrowSigmaDeg * (1.0 - AccuracyScale * RatingScale.Unit(skill.ArmAccuracy)) * (fullEffort ? FullEffortFactor : 1.0);

        /// <summary>
        /// Where the throw actually goes, as an offset of the aim point (m): the direction error (horizontal across the line of
        /// the throw, and vertical) at the throw's distance from <paramref name="release"/> to <paramref name="aim"/>.
        /// </summary>
        public static Vector3d ThrowError(Vector3d release, Vector3d aim, FielderSkill skill, bool fullEffort, ref SeedStream stream)
        {
            double sigma = ThrowSigma(skill, fullEffort);
            if (stream.Unit() < WildThrowChance) sigma *= WildThrowFactor;
            double h = sigma * stream.Normal(), v = sigma * stream.Normal();
            var flat = new Vector3d(aim.X - release.X, aim.Y - release.Y, 0.0);
            double d = flat.Length;
            if (d < 1e-6) return Vector3d.Zero;
            var across = new Vector3d(-flat.Y / d, flat.X / d, 0.0);
            double rad = Math.PI / 180.0;
            return d * Math.Tan(h * rad) * across + new Vector3d(0.0, 0.0, d * Math.Tan(v * rad));
        }

        /// <summary>The deviates of one defensive event: the play's seed, the defender, the instant (µs).</summary>
        public static SeedStream StreamFor(long playSeed, DefensivePosition who, double time, int salt) =>
            new SeedStream(playSeed, (int)who, (long)Math.Round(time * 1e6), salt);
    }
}
