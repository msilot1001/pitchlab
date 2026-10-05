using System;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    /// <summary>TASK-013: the plate appearance lifecycle and the two batting orders.</summary>
    public class PlateAppearanceTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);

        private static LivePlay Play(GameState game, double mph, double launch, double spray, double spin)
        {
            Situation s = game.Situation;
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard);
            var play = new LivePlay(FieldingSolver.Solve(ball, s.Alignment, FielderProfile.For, FieldLayout.Standard), s);
            play.RunToEnd();
            return play;
        }

        private static LivePlay Single(GameState g) => Play(g, 95.0, 6.0, 0.0, 700.0);
        private static LivePlay Foul(GameState g) => Play(g, 85.0, 35.0, 50.0, 2000.0);
        private static LivePlay HomeRun(GameState g) => Play(g, 106.0, 28.0, -10.0, 2000.0);

        private static void StrikeOut(GameState g)
        {
            for (int i = 0; i < 3; i++) g.Pitch(PitchOutcome.SwingingStrike);
        }

        [Test]
        public void NonTerminalPitchesStayWithTheSameBatter()
        {
            var game = new GameState();
            PlateAppearance pa = game.Current;
            Assert.AreEqual((TeamSide.Away, 1, "A1", 1), (pa.Team, pa.Slot, pa.Batter.Id, pa.Number));
            var info = new PitchInfo("Four-Seam-like", 94.5, 0.1, 0.8);
            game.Pitch(PitchOutcome.Ball, info);
            game.Pitch(PitchOutcome.Ball);
            game.Pitch(PitchOutcome.CalledStrike);
            game.Pitch(PitchOutcome.SwingingStrike);
            game.Apply(Foul(game));
            game.Apply(Foul(game));
            game.Pitch(PitchOutcome.Ball);
            Assert.AreSame(pa, game.Current, "one plate appearance");
            Assert.AreEqual((new Count(3, 2), 7, 0), (game.Count, pa.Pitches.Count, game.CompletedPlateAppearances));
            CollectionAssert.AreEqual(Enumerable.Range(1, 7), pa.Pitches.Select(p => p.Number));
            CollectionAssert.AreEqual(
                new[] { PitchOutcome.Ball, PitchOutcome.Ball, PitchOutcome.CalledStrike, PitchOutcome.SwingingStrike, PitchOutcome.Foul, PitchOutcome.Foul, PitchOutcome.Ball },
                pa.Pitches.Select(p => p.Outcome));
            Assert.AreEqual(new Count(2, 2), pa.Pitches[5].Before, "a two-strike foul");
            Assert.AreEqual(new Count(2, 2), pa.Pitches[5].After, "leaves the count");
            Assert.AreEqual("Four-Seam-like", pa.Pitches[0].Info.Label);
            Assert.AreEqual(1, game.UpNext(TeamSide.Away), "the order has not moved");
            Assert.IsFalse(pa.IsComplete);
        }

        [Test]
        public void TerminalResultsCompleteThePlateAppearanceAndBringUpTheNextBatter()
        {
            var game = new GameState();
            StrikeOut(game);
            PlateAppearance k = game.Completed.Single();
            Assert.AreEqual((PlateAppearanceEnd.Strikeout, "strikeout swinging", 1, "A1"), (k.End, k.Result, k.OutsMade, k.Batter.Id));
            Assert.AreEqual(new Count(0, 2), k.Pitches.Last().Before);
            Assert.AreEqual((1, 2, "A2", new Count()), (game.Outs, game.Current.Slot, game.Batter.Id, game.Count), "one out, the next batter at 0–0");

            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            Assert.AreEqual((PlateAppearanceEnd.Walk, "walk"), (game.Completed[1].End, game.Completed[1].Result));
            Assert.AreEqual((3, new BaseOccupancy(true, false, false)), (game.Current.Slot, game.Bases));

            // A ball in play completes the plate appearance only through its finished play.
            LivePlay single = Single(game);
            Assert.AreEqual((3, 2), (game.Current.Slot, game.CompletedPlateAppearances), "the play alone changes nothing");
            game.Apply(single);
            Assert.AreEqual(PlateAppearanceEnd.InPlay, game.Completed[2].End);
            Assert.AreEqual((4, single.ResultingBases()), (game.Current.Slot, game.Bases));
            Assert.Throws<InvalidOperationException>(() => game.Apply(single), "applied exactly once");
            Assert.AreEqual(3, game.CompletedPlateAppearances);
        }

        [Test]
        public void AHomeRunCompletesThePlateAppearance()
        {
            var game = new GameState();
            game.Set(1, Half.Top, 0, new BaseOccupancy(true, false, false), 0, 0);
            game.Pitch(PitchOutcome.Ball);
            LivePlay hr = HomeRun(game);
            Assert.AreEqual(PlateAppearanceEnd.InPlay, game.Apply(hr));
            PlateAppearance pa = game.Completed.Single();
            Assert.AreEqual((2, 2, 0), (pa.Runs, pa.Pitches.Count, pa.OutsMade));
            Assert.AreEqual((2, BaseOccupancy.Empty, 2), (game.AwayScore, game.Bases, game.Current.Slot));
        }

        [Test]
        public void TheOrderWrapsFromNineToOneAndCarriesOverBetweenInnings()
        {
            var game = new GameState();
            // Top 1: 1, 2, 3 strike out. Bottom 1: the home team starts at its own 1.
            for (int i = 0; i < 3; i++) StrikeOut(game);
            Assert.AreEqual((Half.Bottom, TeamSide.Home, 1, "H1"), (game.Half, game.Batting, game.Current.Slot, game.Batter.Id));
            Assert.AreEqual(4, game.UpNext(TeamSide.Away), "the visitors' order waits at 4");
            // Bottom 1: walks to 1–4 then three strikeouts (5, 6, 7): 8 is due next time.
            for (int w = 0; w < 4; w++)
                for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            Assert.AreEqual(1, game.HomeScore, "a run forced in");
            for (int i = 0; i < 3; i++) StrikeOut(game);
            Assert.AreEqual((2, Half.Top, 4, "A4"), (game.Inning, game.Half, game.Current.Slot, game.Batter.Id), "top 2 resumes with the visitors' 4");
            Assert.AreEqual(8, game.UpNext(TeamSide.Home));
            // Visitors 4–9 then 1: six walks score three, then the order wraps to 1.
            for (int w = 0; w < 6; w++)
                for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            Assert.AreEqual((1, "A1", 3), (game.Current.Slot, game.Batter.Id, game.AwayScore), "9 → 1");
            StrikeOut(game);
            Assert.AreEqual(2, game.Current.Slot);
            // Every completed plate appearance of a team, in order, follows its order without skipping or repeating.
            foreach (TeamSide team in new[] { TeamSide.Away, TeamSide.Home })
            {
                int[] slots = game.Completed.Where(p => p.Team == team).Select(p => p.Slot).ToArray();
                for (int i = 1; i < slots.Length; i++) Assert.AreEqual(slots[i - 1] % 9 + 1, slots[i], $"{team} PA {i}");
            }
        }

        [Test]
        public void ConsecutiveBattersChangeSidesInTheGenericLineups()
        {
            Lineup away = Lineup.GenericAway(), home = Lineup.GenericHome();
            Assert.AreEqual(BatterSide.Left, away[1].Bats);
            Assert.AreEqual(BatterSide.Right, away[2].Bats);
            Assert.AreEqual(BatterSide.Right, home[1].Bats);
            Assert.AreEqual(BatterSide.Left, home[2].Bats);
            Assert.AreEqual(18, Enumerable.Range(1, 9).SelectMany(s => new[] { away[s].Id, home[s].Id }).Distinct().Count(), "stable, distinct identities");
            Assert.Throws<ArgumentException>(() => new Lineup("x", new PlayerProfile[8]));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = away[0]);
        }

        [Test]
        public void EachBatterIsCalledAgainstHisOwnZone()
        {
            // The zone scales with height (the default zone is the 73-in batter's); every lineup height still calls each
            // aimed location as aimed.
            Lineup away = Lineup.GenericAway();
            PlayerProfile small = away[1], average = away[2], tall = away[3];
            Assert.AreEqual((70.0, 73.0, 76.0), (small.HeightInches, average.HeightInches, tall.HeightInches));
            Assert.AreEqual(PitchingGeometry.DefaultZoneBottom, average.ZoneBottom, 1e-12);
            Assert.AreEqual(PitchingGeometry.DefaultZoneTop, average.ZoneTop, 1e-12);
            Assert.Less(small.ZoneTop, average.ZoneTop);
            Assert.Greater(tall.ZoneBottom, average.ZoneBottom);
            foreach (PlayerProfile batter in new[] { small, average, tall })
                foreach (PitchInput preset in PitchPresets.All)
                    foreach (PitchLocation location in PitchLocation.All)
                    {
                        HittingPitch pitch = HittingPitch.Create(location.Aim(preset), EnvironmentState.Standard);
                        bool strike = !location.Name.StartsWith("Ball");
                        Assert.AreEqual(strike ? PitchOutcome.CalledStrike : PitchOutcome.Ball,
                            PitchOutcomes.Of(pitch, null, null, null, batter.ZoneBottom, batter.ZoneTop), $"{batter.HeightInches} in, {preset.Label} {location}");
                    }

            // A pitch at the knees of the tall batter's zone but below the small one's top is judged by the batter.
            Assert.IsTrue(StrikeZone.Contains(0.0, tall.ZoneTop, tall.ZoneBottom, tall.ZoneTop));
            Assert.IsFalse(StrikeZone.Contains(0.0, tall.ZoneTop, small.ZoneBottom, small.ZoneTop));
        }

        [Test]
        public void ResetRestoresTheBatterAndTheOrder()
        {
            var game = new GameState();
            StrikeOut(game);
            game.Pitch(PitchOutcome.Ball);
            game.ResetPlateAppearance();
            Assert.AreEqual((2, new Count(), 0, 1), (game.Current.Slot, game.Count, game.Current.Pitches.Count, game.CompletedPlateAppearances), "mid plate appearance: batter 2 again at 0–0");
            StrikeOut(game);
            Assert.AreEqual(3, game.Current.Slot);
            game.ResetPlateAppearance();
            Assert.AreEqual((2, 1, 1, 2), (game.Current.Slot, game.Outs, game.CompletedPlateAppearances, game.UpNext(TeamSide.Away)), "the strikeout is undone: batter 2 again");

            // The inning-ending strikeout undone: back in the top half with the visitors' batter.
            StrikeOut(game);
            StrikeOut(game);
            Assert.AreEqual(Half.Bottom, game.Half);
            game.ResetPlateAppearance();
            Assert.AreEqual((Half.Top, 2, 3, "A3"), (game.Half, game.Outs, game.Current.Slot, game.Batter.Id));
            Assert.AreEqual(1, game.UpNext(TeamSide.Home));
        }

        [Test]
        public void TheEditorKeepsTheBatterUnlessTheHalfChanges()
        {
            var game = new GameState();
            game.Pitch(PitchOutcome.Ball);
            game.Set(1, Half.Top, 2, BaseOccupancy.Loaded, 0, 0);
            Assert.AreEqual(("A1", new Count(1, 0)), (game.Batter.Id, game.Count));
            game.Set(4, Half.Bottom, 1, BaseOccupancy.Empty, 0, 0);
            Assert.AreEqual((TeamSide.Home, "H1", new Count()), (game.Batting, game.Batter.Id, game.Count), "the home team's next batter");
            Assert.AreEqual(1, game.UpNext(TeamSide.Away));
        }
    }
}
