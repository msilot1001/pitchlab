using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-016: a complete game — nine innings, the home team's half not played when it is ahead, walk-offs that end the
    /// game the moment the winning run scores, extra innings, the end — and whole games simulated through the production
    /// systems (accelerated, deterministic).
    /// </summary>
    public class GameFlowTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);

        private static LivePlay Play(GameState g, double mph, double launch, double spray, double spin)
        {
            Situation s = g.Situation;
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard);
            var play = new LivePlay(FieldingSolver.Solve(ball, s.Alignment, FielderProfile.For, FieldLayout.Standard), s);
            play.RunToEnd();
            return play;
        }

        private static LivePlay Gapper(GameState g) => Play(g, 100.0, 12.0, -15.0, 1800.0);     // a double to the gap
        private static LivePlay HomeRun(GameState g) => Play(g, 106.0, 28.0, -10.0, 2000.0);

        private static void StrikeOut(GameState g)
        {
            for (int i = 0; i < 3; i++) g.Pitch(PitchOutcome.SwingingStrike);
        }

        private static void Walk(GameState g)
        {
            for (int i = 0; i < 4; i++) g.Pitch(PitchOutcome.Ball);
        }

        [Test]
        public void TheGameStartsInPregameAndPlaysOnce()
        {
            var g = new GameState();
            Assert.AreEqual(GameStatus.Pregame, g.Status);
            g.Pitch(PitchOutcome.Ball);
            Assert.AreEqual(GameStatus.Playing, g.Status);
            Assert.IsFalse(g.IsOver);
        }

        [Test]
        public void TheHomeTeamDoesNotBatInTheNinthWhenItIsAhead()
        {
            var g = new GameState();
            g.Set(9, Half.Top, 2, BaseOccupancy.Empty, 1, 3);
            StrikeOut(g);
            Assert.IsTrue(g.IsOver, "7.01(e)(1): the bottom of the 9th is not played");
            Assert.AreEqual((1, 3, TeamSide.Home, 9, Half.Top), (g.Result.Away, g.Result.Home, g.Result.Winner, g.Result.Inning, g.Result.Half));
            Assert.AreEqual(GameStatus.GameOver, g.Status);
            Assert.Throws<InvalidOperationException>(() => g.Pitch(PitchOutcome.Ball), "no pitch after the end");
            StringAssert.StartsWith("FINAL", g.Log.Last());
        }

        [Test]
        public void TiedGoingIntoTheBottomOfTheNinthItIsPlayed()
        {
            var g = new GameState();
            g.Set(9, Half.Top, 2, BaseOccupancy.Empty, 2, 2);
            StrikeOut(g);
            Assert.IsFalse(g.IsOver);
            Assert.AreEqual((9, Half.Bottom), (g.Inning, g.Half));
            // Visitors ahead going into the bottom: also played.
            var v = new GameState();
            v.Set(9, Half.Top, 2, BaseOccupancy.Empty, 3, 2);
            StrikeOut(v);
            Assert.IsFalse(v.IsOver);
            Assert.AreEqual(Half.Bottom, v.Half);
        }

        [Test]
        public void AWalkOffWalkEndsTheGameWithTheOneRun()
        {
            var g = new GameState();
            g.Set(9, Half.Bottom, 1, BaseOccupancy.Loaded, 4, 4);
            Walk(g);
            Assert.IsTrue(g.IsOver);
            Assert.AreEqual((4, 5, TeamSide.Home, "walk-off"), (g.AwayScore, g.HomeScore, g.Result.Winner, g.Result.Reason));
            Assert.AreEqual(1, g.Completed.Last().Runs);
        }

        [Test]
        public void AWalkOffHitCountsOnlyTheWinningRun()
        {
            var g = new GameState();
            g.Set(9, Half.Bottom, 0, BaseOccupancy.Loaded, 3, 3);
            LivePlay gap = Gapper(g);
            Assert.Greater(gap.Runs, 1, "the play itself scores more than one");
            g.Apply(gap);
            Assert.IsTrue(g.IsOver, "7.01(e)(3): over the moment the winning run scores");
            Assert.AreEqual((3, 4), (g.AwayScore, g.HomeScore), "only the winning run counts");
            Assert.AreEqual(1, g.Completed.Last().Runs);
            Assert.AreEqual((9, Half.Bottom), (g.Result.Inning, g.Result.Half));
        }

        [Test]
        public void AWalkOffHomeRunOutOfTheParkScoresEveryone()
        {
            var g = new GameState();
            g.Set(10, Half.Bottom, 2, new BaseOccupancy(true, true, false), 5, 5);
            LivePlay hr = HomeRun(g);
            Assert.AreEqual(4, hr.AwardedBases);
            g.Apply(hr);
            Assert.IsTrue(g.IsOver);
            Assert.AreEqual((5, 8), (g.AwayScore, g.HomeScore), "7.01(e) EXCEPTION: the batter and all runners score");
            Assert.AreEqual((10, "walk-off"), (g.Result.Inning, g.Result.Reason));
        }

        [Test]
        public void ATieAfterNineGoesToExtraInningsWithTheBasesEmpty()
        {
            var g = new GameState();
            g.Set(9, Half.Bottom, 2, new BaseOccupancy(false, true, false), 2, 2);
            StrikeOut(g);
            Assert.IsFalse(g.IsOver);
            Assert.AreEqual((10, Half.Top, 0, BaseOccupancy.Empty), (g.Inning, g.Half, g.Outs, g.Bases), "traditional extra innings: nobody on");
            // The visitors score in the top of the 10th: the home team still bats.
            g.Set(10, Half.Top, 2, BaseOccupancy.Loaded, 2, 2);
            Walk(g);
            Assert.AreEqual(3, g.AwayScore);
            StrikeOut(g);
            Assert.IsFalse(g.IsOver, "the home team bats in the bottom of the 10th");
            Assert.AreEqual((10, Half.Bottom), (g.Inning, g.Half));
            // … and does not tie: the visitors win after ten.
            for (int i = 0; i < 3; i++) StrikeOut(g);
            Assert.IsTrue(g.IsOver);
            Assert.AreEqual((TeamSide.Away, 10, Half.Bottom, "10 innings"), (g.Result.Winner, g.Result.Inning, g.Result.Half, g.Result.Reason));
        }

        [Test]
        public void TheHomeTeamOvertakingInAnExtraInningEndsItAtOnce()
        {
            var g = new GameState();
            g.Set(11, Half.Bottom, 0, new BaseOccupancy(true, true, true), 6, 5);
            Walk(g);
            Assert.IsFalse(g.IsOver, "tied: not yet");
            Assert.AreEqual(6, g.HomeScore);
            Walk(g);
            Assert.IsTrue(g.IsOver);
            Assert.AreEqual((6, 7, 11, 0), (g.AwayScore, g.HomeScore, g.Result.Inning, g.Outs));
        }

        [Test]
        public void ResetUndoesTheEndAndTheEditorRevivesAGame()
        {
            var g = new GameState();
            g.Set(9, Half.Top, 2, BaseOccupancy.Empty, 1, 3);
            StrikeOut(g);
            Assert.IsTrue(g.IsOver);
            g.ResetPlateAppearance();
            Assert.IsFalse(g.IsOver, "the last plate appearance replayed");
            Assert.AreEqual((9, Half.Top, 2), (g.Inning, g.Half, g.Outs));
            StrikeOut(g);
            Assert.IsTrue(g.IsOver);
            g.Set(9, Half.Bottom, 0, BaseOccupancy.Empty, 3, 3);
            Assert.IsFalse(g.IsOver, "a development tool: the editor puts the game back in play");
        }

        // ------------------------------------------------------------------ whole games

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(12)]
        [TestCase(33)]
        public void ASimulatedGameIsAValidGame(int seed)
        {
            var g = new GameState();
            var sim = new GameSimulator(g, seed);
            var timer = Stopwatch.StartNew();
            sim.PlayToEnd();
            timer.Stop();
            TestContext.WriteLine($"seed {seed}: {g.Result}, {g.Result.Inning} innings, {g.CompletedPlateAppearances} PA, {sim.Pitches} pitches, {timer.ElapsedMilliseconds} ms");
            Assert.IsTrue(g.IsOver, "it ends");
            GameResult r = g.Result;
            Assert.AreEqual((g.AwayScore, g.HomeScore), (r.Away, r.Home));
            Assert.AreNotEqual(r.Away, r.Home, "never a tie");
            Assert.GreaterOrEqual(r.Inning, GameState.RegulationInnings);
            // Scores are the plate appearances' runs.
            Assert.AreEqual(r.Away, g.Completed.Where(p => p.Team == TeamSide.Away).Sum(p => p.Runs));
            Assert.AreEqual(r.Home, g.Completed.Where(p => p.Team == TeamSide.Home).Sum(p => p.Runs));
            // Every finished half-inning had exactly three outs; the last one three, or fewer on a walk-off.
            var halves = g.Completed.GroupBy(p => (p.Inning, p.Half)).ToList();
            Assert.AreEqual(2 * r.Inning - (r.Half == Half.Top ? 1 : 0), halves.Count, "every half up to the end was played, in order");
            for (int i = 0; i < halves.Count; i++)
            {
                int outs = halves[i].Sum(p => p.OutsMade);
                if (i < halves.Count - 1 || r.Reason != "walk-off") Assert.AreEqual(3, Math.Min(outs, 3), $"half {halves[i].Key}");
                else Assert.Less(halves[i].First().StartOuts + outs - halves[i].Last().OutsMade, 3, "the walk-off came before the third out");
            }

            // Each team's order: 1, 2, …, 9, 1, … through the whole game, never skipping, across innings.
            foreach (TeamSide team in new[] { TeamSide.Away, TeamSide.Home })
            {
                int[] slots = g.Completed.Where(p => p.Team == team).Select(p => p.Slot).ToArray();
                Assert.AreEqual(1, slots[0]);
                for (int i = 1; i < slots.Length; i++) Assert.AreEqual(slots[i - 1] % 9 + 1, slots[i], $"{team} PA {i}");
                Assert.Greater(slots.Length, 9, "the order wraps");
            }

            CollectionAssert.AreEqual(Enumerable.Range(1, g.CompletedPlateAppearances), g.Completed.Select(p => p.Number), "each plate appearance recorded once");
            Assert.Less(timer.Elapsed.TotalSeconds, 20.0, "accelerated: no rendering");
        }

        [Test]
        public void SimulatedGamesAreDeterministic()
        {
            string Run(int seed)
            {
                var g = new GameState();
                new GameSimulator(g, seed).PlayToEnd();
                return string.Join("\n", g.Log);
            }

            string a = Run(3);
            Assert.AreEqual(a, Run(3), "same seed, same game");
            Assert.AreNotEqual(a, Run(4));
        }
    }
}
