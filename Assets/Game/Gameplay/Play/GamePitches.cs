using System;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>
    /// A pitch in a game (TASK-018): the pitcher on the mound throws his own pitch of the chosen type, aimed at the target in
    /// the batter's zone (intent), executed with his command from the game's seed (execution); the pitch physics decides the
    /// flight. One path for the GameLab and the simulator.
    /// </summary>
    public static class GamePitches
    {
        /// <param name="type">Must be in the pitcher's repertoire (any type if the team has no rated pitcher).</param>
        /// <param name="target">Null: the pitch's own aim (no target).</param>
        /// <param name="executionVariance">False: the intended pitch exactly (debug; physics comparisons).</param>
        public static HittingPitch Create(GameState game, PitchType type, PitchTarget? target, bool executionVariance, EnvironmentState environment,
            out PitchInfo info, out ExecutionError? error)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            (double tx, double tz) = target is PitchTarget t ? PitchTargets.Point(t, game.Batter.ZoneBottom, game.Batter.ZoneTop) : (double.NaN, double.NaN);
            return Create(game, type, tx, tz, executionVariance, environment, out info, out error);
        }

        /// <summary>The same aimed at a point (<paramref name="tx"/>, <paramref name="tz"/>) of the plate's front plane (m; NaN:
        /// no target) — the CPU pitcher's locations (TASK-020).</summary>
        public static HittingPitch Create(GameState game, PitchType type, double tx, double tz, bool executionVariance, EnvironmentState environment,
            out PitchInfo info, out ExecutionError? error)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            PlayerProfile pitcher = game.Pitcher;
            bool rated = pitcher?.Repertoire != null;
            if (rated && !pitcher.Repertoire.Has(type)) throw new ArgumentException($"{pitcher.Name} does not throw a {type}.", nameof(type));
            int pitchCount = game.PitchCount(game.Fielding);
            PitchInput intended = rated ? PitchExecution.PitcherPitch(pitcher, type, pitchCount) : PitchPresets.All[(int)type];
            if (!double.IsNaN(tx) && !double.IsNaN(tz)) intended = PitchTargets.Aim(intended, tx, tz, environment);
            else if (rated)
            {
                // No target: where the type's preset itself goes (his speed, spin and side would move it otherwise).
                PitchInput preset = PitchPresets.All[(int)type];
                if (pitcher.Throws == Hand.Left) preset = PitchExecution.Mirror(preset);
                (double px, double pz) = StrikeZone.Crossing(HittingPitch.Create(preset, environment));
                if (!double.IsNaN(px)) intended = PitchTargets.Aim(intended, px, pz, environment);
            }

            PitchInput executed = intended;
            error = null;
            if (rated && executionVariance)
            {
                SeedStream stream = PitchExecution.StreamFor(game.Seed, pitcher.Id, game.Current.Number, game.Current.Pitches.Count + 1);
                executed = PitchExecution.Execute(intended, pitcher, type, pitchCount, ref stream, out ExecutionError e);
                error = e;
            }

            HittingPitch pitch = HittingPitch.Create(executed, environment);
            info = PitchInfo.Of(PitchPresets.All[(int)type].Label, pitch, tx, tz);
            return pitch;
        }

        /// <summary>The pitch type to throw for a requested <paramref name="type"/>: itself if the pitcher throws it, else his
        /// first pitch.</summary>
        public static PitchType Available(GameState game, PitchType type)
        {
            Repertoire r = game?.Pitcher?.Repertoire;
            return r == null || r.Has(type) ? type : r.Pitches[0].Type;
        }
    }
}
