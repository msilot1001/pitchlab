using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
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
        public void TheEighthInningNeverEndsAGame()
        {
            var a = new GameState();
            a.Set(8, Half.Top, 2, BaseOccupancy.Empty, 1, 3);
            StrikeOut(a);
            Assert.IsFalse(a.IsOver, "home ahead after the top of the 8th: the 8th goes on");
            Assert.AreEqual((8, Half.Bottom), (a.Inning, a.Half));
            var b = new GameState();
            b.Set(8, Half.Bottom, 2, BaseOccupancy.Empty, 3, 1);
            StrikeOut(b);
            Assert.IsFalse(b.IsOver, "visitors ahead after 8: the 9th is played");
            Assert.AreEqual((9, Half.Top), (b.Inning, b.Half));
            var c = new GameState();
            c.Set(8, Half.Bottom, 0, BaseOccupancy.Loaded, 4, 4);
            LivePlay gap = Gapper(c);
            c.Apply(gap);
            Assert.IsFalse(c.IsOver, "no walk-off before the 9th");
            Assert.AreEqual(4 + gap.Runs, c.HomeScore, "every run counts");
        }

        [Test]
        public void VisitorsAheadAfterNineWin()
        {
            var g = new GameState();
            g.Set(9, Half.Bottom, 2, BaseOccupancy.Empty, 3, 2);
            StrikeOut(g);
            Assert.IsTrue(g.IsOver);
            Assert.AreEqual((TeamSide.Away, 9, Half.Bottom, "nine innings"), (g.Result.Winner, g.Result.Inning, g.Result.Half, g.Result.Reason));
        }

        [Test]
        public void AWalkOffFromOneDownCountsTheTwoRunsItNeeds()
        {
            var g = new GameState();
            g.Set(9, Half.Bottom, 0, BaseOccupancy.Loaded, 5, 4);
            LivePlay gap = Gapper(g);
            Assume.That(gap.Runs, Is.GreaterThanOrEqualTo(2));
            g.Apply(gap);
            Assert.IsTrue(g.IsOver);
            Assert.AreEqual((5, 6, 2), (g.AwayScore, g.HomeScore, g.Completed.Last().Runs));
        }

        [Test]
        public void ARunAnnulledByAForceThirdOutIsNoWalkOff()
        {
            // Two out, a runner on third: he crosses the plate, but the third out is a force (5.08(a)) — no run, extra innings.
            var g = new GameState();
            g.Set(9, Half.Bottom, 2, new BaseOccupancy(false, false, true), 3, 3);
            LivePlay play = Play(g, 60.0, -10.0, -44.0, -1000.0);
            Assert.IsTrue(play.Runners.Any(r => r.HasScored && !r.RunCounts), "he crossed the plate");
            Assert.AreEqual((0, 3), (play.Runs, play.Outs));
            g.Apply(play);
            Assert.IsFalse(g.IsOver);
            Assert.AreEqual((10, Half.Top, 3, 3), (g.Inning, g.Half, g.AwayScore, g.HomeScore));
        }

        [Test]
        public void AWinningRunBeforeATagThirdOutEndsTheGame()
        {
            // Two out, bases loaded: the winning run scores before the third out (a tag, not a force) — it counts, game over.
            var g = new GameState();
            g.Set(9, Half.Bottom, 2, BaseOccupancy.Loaded, 3, 3);
            LivePlay play = Play(g, 75.0, 22.0, -44.0, 1800.0);
            Assert.AreEqual((3, 1), (play.Outs, play.Runs), "the third out came after the run");
            g.Apply(play);
            Assert.IsTrue(g.IsOver);
            Assert.AreEqual((3, 4, "walk-off"), (g.AwayScore, g.HomeScore, g.Result.Reason));
            Assert.AreEqual(0, g.Completed.Last().OutsMade, "the out after the winning run does not count");
            Assert.AreEqual(2, g.Outs);
        }

        [Test]
        public void AWalkOffIsThePlayAsItStoodAtTheWinningRun()
        {
            // Bases loaded, none out, tied: a fly is caught, the runner from third tags and scores — the game is over — and a
            // runner is doubled off afterwards. As a play: a double play; as the game's last plate appearance: a sacrifice fly.
            var g = new GameState();
            g.Set(9, Half.Bottom, 0, BaseOccupancy.Loaded, 3, 3);
            LivePlay play = Play(g, 95.0, 25.0, 20.0, 1800.0);
            Assume.That(PlayResults.Classify(play), Is.EqualTo(PlayResultKind.DoublePlay));
            g.Apply(play);
            Assert.IsTrue(g.IsOver);
            PlateAppearance pa = g.Completed.Last();
            Assert.AreEqual((1, 1, PlayResultKind.SacrificeFly, "sacrifice fly"), (pa.Runs, pa.OutsMade, pa.PlayResult.Value, pa.Result));
            Assert.AreEqual((1, 3, 4), (g.Outs, g.AwayScore, g.HomeScore));
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
            PlateAppearance pa = g.Completed.Last();
            Assert.AreEqual((1, 0), (pa.Runs, pa.OutsMade), "one run; no out before it");
            Assert.AreEqual((9, Half.Bottom), (g.Result.Inning, g.Result.Half));
            Assert.Throws<InvalidOperationException>(() => g.Apply(Gapper(g)), "no play after the end");
        }

        [Test]
        public void AWalkOffExtraBaseHitIsCreditedWithTheWinningRunnersBases()
        {
            // A runner on third, tied: a ball in the gap — a double as a play — ends the game when he scores, and the batter is
            // credited with the one base the winning run advanced (OBR 9.06(f)).
            var g = new GameState();
            g.Set(9, Half.Bottom, 0, new BaseOccupancy(false, false, true), 3, 3);
            LivePlay gap = Gapper(g);
            Assume.That(PlayResults.Classify(gap), Is.EqualTo(PlayResultKind.Double));
            g.Apply(gap);
            Assert.IsTrue(g.IsOver);
            PlateAppearance pa = g.Completed.Last();
            Assert.AreEqual((1, PlayResultKind.Single, "single"), (pa.Runs, pa.PlayResult.Value, pa.Result));
        }

        [Test]
        public void AWalkOffHomeRunOutOfTheParkScoresEveryone()
        {
            var g = new GameState();
            g.Set(10, Half.Bottom, 2, new BaseOccupancy(true, true, false), 5, 5);
            LivePlay hr = HomeRun(g);
            Assert.AreEqual(4, hr.AwardedBases);
            Assert.AreEqual(PlayResultKind.HomeRun, PlayResults.Classify(hr), "out of the park");
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

        // Seeds pinned to exercise each ending with the current rosters and simulator (regression pins — re-chosen when either
        // changes; the seed drives the whole game): 5 visitors after nine, 2 home ahead after the top of the 9th, 10 a walk-off
        // in the 9th, 20 a walk-off in the 11th (extra innings, asserted below).
        [TestCase(5, "nine innings")]
        [TestCase(2, "the home team leads after the top of the 9th")]
        [TestCase(10, "walk-off")]
        [TestCase(20, "walk-off")]
        public void ASimulatedGameIsAValidGame(int seed, string ending)
        {
            var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);   // the seed drives execution too
            var sim = new GameSimulator(g);
            var timer = Stopwatch.StartNew();
            while (!g.IsOver)
            {
                sim.PlayPitch();
                // Who is on base always matches the bases: one distinct player per occupied base (TASK-017).
                foreach (Base b in new[] { Base.First, Base.Second, Base.Third })
                    Assert.AreEqual(g.Bases.IsOccupied(b), g.RunnerOn(b) != null, $"{b} after pitch {sim.Pitches}");
                var on = new[] { g.RunnerOn(Base.First), g.RunnerOn(Base.Second), g.RunnerOn(Base.Third) }.Where(x => x != null).ToArray();
                Assert.AreEqual(on.Length, on.Distinct().Count(), "distinct runners");
            }

            timer.Stop();
            TestContext.WriteLine($"seed {seed}: {g.Result}, {g.Result.Inning} innings, {g.CompletedPlateAppearances} PA, {sim.Pitches} pitches, {timer.ElapsedMilliseconds} ms");
            Assert.IsTrue(g.IsOver, "it ends");
            GameResult r = g.Result;
            Assert.AreEqual(ending, r.Reason, "the seed's ending");
            if (seed == 20) Assert.Greater(r.Inning, 9, "extra innings");
            Assert.AreEqual((g.AwayScore, g.HomeScore), (r.Away, r.Home));
            Assert.AreNotEqual(r.Away, r.Home, "never a tie");
            Assert.GreaterOrEqual(r.Inning, 9);
            Assert.AreEqual(sim.Pitches, g.Completed.Sum(p => p.Pitches.Count) + g.Current.Pitches.Count, "every simulated pitch recorded by the game");
            // Scores are the plate appearances' runs.
            Assert.AreEqual(r.Away, g.Completed.Where(p => p.Team == TeamSide.Away).Sum(p => p.Runs));
            Assert.AreEqual(r.Home, g.Completed.Where(p => p.Team == TeamSide.Home).Sum(p => p.Runs));
            // Every finished half-inning had exactly three outs; the last one three, or fewer on a walk-off.
            // The halves in order — top 1, bottom 1, … — each ended by exactly three outs, but a walk-off's.
            var order = g.Completed.Select(p => (p.Inning, p.Half)).Distinct().ToList();
            var expected = Enumerable.Range(1, r.Inning).SelectMany(i => new[] { (i, Half.Top), (i, Half.Bottom) }).Take(order.Count).ToList();
            CollectionAssert.AreEqual(expected, order, "every half up to the end, in order");
            Assert.AreEqual((r.Inning, r.Half), order.Last());
            for (int i = 0; i < order.Count; i++)
            {
                int outs = g.Completed.Where(p => (p.Inning, p.Half) == order[i]).Sum(p => p.OutsMade);
                if (i < order.Count - 1 || r.Reason != "walk-off") Assert.AreEqual(3, outs, $"half {order[i]}");
                else Assert.Less(outs, 3, "the walk-off came before the third out");
            }

            // The game ended at its first decisive moment and for the stated reason (7.01(e)).
            int away = 0, home = 0;
            for (int i = 0; i < g.CompletedPlateAppearances; i++)
            {
                PlateAppearance pa = g.Completed[i];
                int homeBefore = home;
                if (pa.Team == TeamSide.Away) away += pa.Runs;
                else home += pa.Runs;
                bool last = i == g.CompletedPlateAppearances - 1;
                bool halfOver = last || g.Completed[i + 1].Half != pa.Half;
                if (!last && pa.Inning >= 9 && pa.Half == Half.Bottom && homeBefore <= away && home > away) Assert.Fail($"a walk-off at PA {pa.Number} was played on");
                if (!last && halfOver && pa.Inning >= 9 && (pa.Half == Half.Top ? home > away : home != away)) Assert.Fail($"the game should have ended after PA {pa.Number}");
            }

            PlateAppearance final = g.Completed.Last();
            if (r.Reason == "walk-off")
            {
                Assert.AreEqual(Half.Bottom, r.Half);
                if (final.PlayResult != PlayResultKind.HomeRun) Assert.AreEqual(r.Away + 1, r.Home, "only the winning run");
            }
            else if (r.Half == Half.Top) Assert.Greater(r.Home, r.Away);
            else Assert.Greater(r.Away, r.Home);

            // Each team's order: 1, 2, …, 9, 1, … through the whole game, never skipping, across innings.
            foreach (TeamSide team in new[] { TeamSide.Away, TeamSide.Home })
            {
                int[] slots = g.Completed.Where(p => p.Team == team).Select(p => p.Slot).ToArray();
                Assert.AreEqual(1, slots[0]);
                for (int i = 1; i < slots.Length; i++) Assert.AreEqual(slots[i - 1] % 9 + 1, slots[i], $"{team} PA {i}");
                Assert.Greater(slots.Length, 9, "the order wraps");
            }

            CollectionAssert.AreEqual(Enumerable.Range(1, g.CompletedPlateAppearances), g.Completed.Select(p => p.Number), "each plate appearance recorded once");
            Assert.Less(timer.Elapsed.TotalSeconds, 60.0, "accelerated: no rendering (a loose bound; the time is logged)");
            Assert.Throws<InvalidOperationException>(() => sim.PlayPitch(), "no pitch after the end");
            sim.PlayToEnd();
            Assert.AreEqual(sim.Pitches, g.Completed.Sum(p => p.Pitches.Count) + g.Current.Pitches.Count, "a finished game plays no more");
        }

        [Test]
        public void SimulatedGamesAreDeterministic()
        {
            string Run(int seed)
            {
                var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);
                new GameSimulator(g).PlayToEnd();
                return string.Join("\n", g.Log) + string.Join("|", g.Completed.SelectMany(p => p.Pitches)
                    .Select(p => $"{p.Info.Label}@{p.Info.PlateX:R},{p.Info.PlateZ:R}:{p.Outcome}"));
            }

            string a = Run(3);
            Assert.AreEqual(a, Run(3), "same seed, same game");
            Assert.AreNotEqual(a, Run(4));
        }
    }
}
