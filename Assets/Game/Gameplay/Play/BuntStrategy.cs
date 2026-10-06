using System;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>
    /// When the CPU batter lays down a sacrifice bunt (TASK-025): the textbook spot — nobody out, a runner on first (second may
    /// be occupied, third not), a close game late, a batter with little power, fewer than two strikes (a foul bunt with two
    /// strikes is strike three). MLB sacrifices are rare (452 in 2024, ≈ 0.09 per team-game; Baseball Savant). ASSUMED rule
    /// of thumb, not a run-expectancy model (strategy AI is TASK-033).
    /// </summary>
    public static class BuntStrategy
    {
        /// <summary>Power at or below this (0–100) bunts in the spot.</summary>
        public const int WeakPower = 40;
        /// <summary>From this inning on, with the score within <see cref="CloseMargin"/>.</summary>
        public const int LateInning = 7, CloseMargin = 1;

        public static bool Sacrifice(GameState game)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (game.IsOver || game.Outs != 0 || !game.Bases.First || game.Bases.Third || game.Count.Strikes >= 2) return false;
            if (game.Inning < LateInning || Math.Abs(game.AwayScore - game.HomeScore) > CloseMargin) return false;
            return game.Batter.Ratings.Power <= WeakPower;
        }

        /// <summary>
        /// Where he squares the bat (rad toward first base): with a runner on first only, toward the first-base line (the first
        /// baseman is holding the runner); with runners on first and second, toward third (the third baseman must field it and
        /// leave third uncovered). The bat is squared to the ball's path; against a nearly still bat the ball keeps much of its
        /// speed along the bat's face, so a few degrees steer it — 5° puts it ≈ 25° toward the line (measured in the contact
        /// model; TUNED, Docs/BUNTING.md).
        /// </summary>
        public static double Aim(GameState game) => Units.DegreesToRadians(game.Bases.Second ? -5.0 : 5.0);
    }
}
