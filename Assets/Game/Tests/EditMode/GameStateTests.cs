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

namespace Pitchlab.Tests
{
    /// <summary>TASK-010: the half-inning state machine — plate appearances, outs, runs, the half-inning change, the editor.</summary>
    public class GameStateTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);

        /// <summary>The ball played from the game's current situation (its alignment too), to the end.</summary>
        private static LivePlay Play(GameState game, double mph, double launch, double spray, double spin)
        {
            Situation s = game.Situation;
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard);
            var play = new LivePlay(FieldingSolver.Solve(ball, s.Alignment, FielderProfile.For, FieldLayout.Standard), s);
            play.RunToEnd();
            return play;
        }

        private static LivePlay Single(GameState g) => Play(g, 95.0, 6.0, 0.0, 700.0);          // to centre
        private static LivePlay Grounder(GameState g) => Play(g, 85.0, -8.0, -15.0, -1000.0);  // to short
        private static LivePlay Foul(GameState g) => Play(g, 85.0, 35.0, 50.0, 2000.0);
        private static LivePlay HomeRun(GameState g) => Play(g, 106.0, 28.0, -10.0, 2000.0);

        [Test]
        public void AFoulIsAStrikeAndTheSameBatterStaysUp()
        {
            var game = new GameState();
            game.Set(1, Half.Top, 1, new BaseOccupancy(true, false, false), 0, 0);
            for (int i = 1; i <= 3; i++)
            {
                LivePlay foul = Foul(game);
                Assert.AreEqual(LivePlay.BallKind.Dead, foul.Kind);
                Assert.AreEqual(PlateAppearanceEnd.None, game.Apply(foul));
                Assert.AreEqual(new Count(0, Math.Min(i, 2)), game.Count, "a strike until two strikes");
                Assert.AreEqual((1, new BaseOccupancy(true, false, false), 0), (game.Outs, game.Bases, game.CompletedPlateAppearances), "same batter, runner back");
            }
        }

        [Test]
        public void ABattedBallIsAFoulOnlyWhenItIsDeadWithoutAnAward()
        {
            var game = new GameState();
            HittingPitch pitch = HittingPitch.Create(Simulation.Pitching.PitchPresets.FourSeam, EnvironmentState.Standard);
            var swing = new SwingInput(pitch.IdealContactTime - SwingParameters.Default.SwingDuration, pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
            ContactResult contact = ContactResolver.Resolve(pitch, swing, SwingParameters.Default);
            Assert.IsTrue(contact.IsContact);
            Assert.AreEqual(PitchOutcome.Foul, PitchOutcomes.Of(pitch, swing, contact, Foul(game)));
            Assert.AreEqual(PitchOutcome.InPlay, PitchOutcomes.Of(pitch, swing, contact, Single(game)));
            LivePlay hr = HomeRun(game);
            Assert.AreEqual(LivePlay.BallKind.Dead, hr.Kind, "a home run is dead too — with an award");
            Assert.AreEqual(PitchOutcome.InPlay, PitchOutcomes.Of(pitch, swing, contact, hr));
            Assert.Throws<ArgumentNullException>(() => PitchOutcomes.Of(pitch, swing, contact, null));
        }

        [Test]
        public void ResetAfterAFinishedPlateAppearanceReturnsToTheNextOnesStart()
        {
            // The first pitch of each plate appearance marks where RESET PA returns (not the editor's last change).
            var game = new GameState();
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);   // walk: PA 1 over
            game.Pitch(PitchOutcome.Ball);
            game.ResetPlateAppearance();
            Assert.AreEqual((new BaseOccupancy(true, false, false), new Count(), 1, 1), (game.Bases, game.Count, game.CompletedPlateAppearances, game.Log.Count), "back to PA 2's start, the walk stands");
            game.Apply(Single(game));
            game.Apply(Foul(game));
            Assert.AreEqual(new Count(0, 1), game.Count);
            game.ResetPlateAppearance();
            Assert.AreEqual((new BaseOccupancy(true, true, false), new Count(), 2), (game.Bases, game.Count, game.CompletedPlateAppearances), "back to after the single");
        }

        [Test]
        public void EditingMidCountThenResettingStartsThePlateAppearanceOver()
        {
            var game = new GameState();
            game.Pitch(PitchOutcome.Ball);
            game.Pitch(PitchOutcome.CalledStrike);
            game.Set(GameState.Presets.First(p => p.Name == "R3, 1 out"));
            Assert.AreEqual(new Count(1, 1), game.Count, "the editor keeps the count");
            game.ResetPlateAppearance();
            Assert.AreEqual((1, new BaseOccupancy(false, false, true), new Count()), (game.Outs, game.Bases, game.Count));
        }

        [Test]
        public void BallFourWalksTheBatterAndForcesTheRunners()
        {
            var game = new GameState();
            game.Set(3, Half.Bottom, 2, BaseOccupancy.Loaded, 1, 1);
            game.Pitch(PitchOutcome.Ball);
            game.Pitch(PitchOutcome.CalledStrike);
            game.Pitch(PitchOutcome.Ball);
            game.Pitch(PitchOutcome.Ball);
            Assert.AreEqual(new Count(3, 1), game.Count);
            Assert.AreEqual(PlateAppearanceEnd.Walk, game.Pitch(PitchOutcome.Ball));
            Assert.IsFalse(game.IsOver, "a walk-off only in the 9th or later");
            Assert.AreEqual((2, BaseOccupancy.Loaded, 1, 2, 1), (game.Outs, game.Bases, game.AwayScore, game.HomeScore, game.CompletedPlateAppearances), "forced in: the home team scores");
            Assert.AreEqual(new Count(), game.Count, "the next batter starts 0–0");
            StringAssert.Contains("walk, 1 run", game.Log.Last());
        }

        [Test]
        public void StrikeThreeIsAnOutAndTheThirdEndsTheHalf()
        {
            var game = new GameState();
            game.Set(1, Half.Top, 1, new BaseOccupancy(false, true, false), 0, 0);
            game.Pitch(PitchOutcome.SwingingStrike);
            game.Apply(Foul(game));
            Assert.AreEqual(new Count(0, 2), game.Count);
            Assert.AreEqual(PlateAppearanceEnd.Strikeout, game.Pitch(PitchOutcome.CalledStrike));
            Assert.AreEqual("strikeout looking", game.Completed.Last().Result);
            Assert.AreEqual((2, new BaseOccupancy(false, true, false), new Count()), (game.Outs, game.Bases, game.Count), "the runner stays");
            StringAssert.Contains("strikeout looking", game.Log.Last());
            for (int i = 0; i < 3; i++) game.Pitch(PitchOutcome.SwingingStrike);
            Assert.AreEqual((Half.Bottom, 0, BaseOccupancy.Empty), (game.Half, game.Outs, game.Bases), "three out");
            StringAssert.Contains("strikeout swinging, 0 runs → 3 out", game.Log[game.Log.Count - 2]);
            Assert.AreEqual("— Bottom 1 —", game.Log.Last());
            game.Set(1, Half.Bottom, 2, BaseOccupancy.Loaded, 0, 0);
            for (int i = 0; i < 3; i++) game.Pitch(PitchOutcome.CalledStrike);
            Assert.AreEqual((2, Half.Top, 0, BaseOccupancy.Empty, 0, 0), (game.Inning, game.Half, game.Outs, game.Bases, game.AwayScore, game.HomeScore), "the bottom half ends: next inning, nobody scored");
            Assert.Throws<ArgumentException>(() => game.Pitch(PitchOutcome.Foul), "a batted ball comes with its play");
        }

        [Test]
        public void ResetMidPlateAppearanceGoesBackToOhAndOh()
        {
            var game = new GameState();
            game.Set(1, Half.Top, 0, new BaseOccupancy(true, false, false), 0, 0);
            string start = game.ToString();
            game.Pitch(PitchOutcome.Ball);
            game.Pitch(PitchOutcome.SwingingStrike);
            game.ResetPlateAppearance();
            Assert.AreEqual(start, game.ToString(), "mid plate appearance: back to 0–0");
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            Assert.AreEqual(new BaseOccupancy(true, true, false), game.Bases);
            game.ResetPlateAppearance();
            Assert.AreEqual(start, game.ToString(), "after the walk: the walk is replayed from 0–0");
            Assert.AreEqual((0, 0), (game.CompletedPlateAppearances, game.Log.Count));
        }

        [Test]
        public void ABallInPlayEndsThePlateAppearance()
        {
            var game = new GameState();
            game.Apply(Single(game));
            Assert.AreEqual(1, game.CompletedPlateAppearances);
            Assert.AreEqual(new BaseOccupancy(true, false, false), game.Bases, "the batter is on first");
            Assert.AreEqual(0, game.Outs);
            // The next plate appearance starts from there: a grounder to short is a double play from double-play depth.
            LivePlay dp = Grounder(game);
            Assert.AreEqual(DefensiveAlignment.DoublePlayDepth[DefensivePosition.Shortstop], dp.Fielding.Motion(DefensivePosition.Shortstop).Start);
            game.Apply(dp);
            Assert.AreEqual((2, BaseOccupancy.Empty, 2), (game.Outs, game.Bases, game.CompletedPlateAppearances));
        }

        [Test]
        public void RunsGoToTheBattingTeam()
        {
            var game = new GameState();
            game.Set(3, Half.Top, 0, BaseOccupancy.Loaded, 0, 0);
            game.Apply(HomeRun(game));
            Assert.AreEqual((4, 0), (game.AwayScore, game.HomeScore), "a grand slam in the top half: the visitors");
            Assert.AreEqual(BaseOccupancy.Empty, game.Bases);
            game.Set(3, Half.Bottom, 2, new BaseOccupancy(false, false, true), 4, 0);
            game.Apply(HomeRun(game));
            Assert.AreEqual((4, 2), (game.AwayScore, game.HomeScore));
        }

        [Test]
        public void TheThirdOutChangesTheHalfInning()
        {
            var game = new GameState();
            game.Set(1, Half.Top, 1, new BaseOccupancy(true, false, false), 0, 0);
            game.Apply(Grounder(game));   // 6-4-3
            Assert.AreEqual((1, Half.Bottom, 0, BaseOccupancy.Empty), (game.Inning, game.Half, game.Outs, game.Bases));
            game.Set(1, Half.Bottom, 2, new BaseOccupancy(false, true, false), 0, 0);
            game.Apply(Grounder(game));   // out at first
            Assert.AreEqual((2, Half.Top, 0, BaseOccupancy.Empty), (game.Inning, game.Half, game.Outs, game.Bases), "after the bottom half, the next inning");
            Assert.IsTrue(game.Log.Last().Contains("Top 2"));
        }

        [Test]
        public void NoRunScoresOnAForceForTheThirdOut()
        {
            // Two out, runner on third, grounder to short: whatever the runner does, the batter is out at first for the third
            // out before reaching it — no run (OBR 5.08(a)), the score is unchanged.
            var game = new GameState();
            game.Set(5, Half.Top, 2, new BaseOccupancy(false, false, true), 1, 1);
            game.Apply(Grounder(game));
            Assert.AreEqual((1, 1), (game.AwayScore, game.HomeScore));
            Assert.AreEqual(Half.Bottom, game.Half);
        }

        [Test]
        public void ResetGoesBackToTheStartOfThePlateAppearance()
        {
            var game = new GameState();
            game.Set(GameState.Presets.First(p => p.Name == "R1 R3, 1 out"));
            string start = game.ToString();
            game.Apply(Single(game));
            Assert.AreNotEqual(start, game.ToString());
            game.ResetPlateAppearance();
            Assert.AreEqual(start, game.ToString());
            Assert.AreEqual(0, game.CompletedPlateAppearances, "the plate appearance is replayed");
            Assert.AreEqual(0, game.Log.Count, "and its log line removed");
        }

        [Test]
        public void APlayIsAppliedOnce()
        {
            // A solo home run leaves outs and bases as they were: applying it again must still be refused.
            var game = new GameState();
            LivePlay hr = HomeRun(game);
            game.Apply(hr);
            Assert.Throws<InvalidOperationException>(() => game.Apply(hr));
            Assert.AreEqual((1, 1), (game.AwayScore, game.CompletedPlateAppearances));
        }

        [Test]
        public void TheEditorAcceptsOnlyLegalSituations()
        {
            var game = new GameState();
            Assert.Throws<ArgumentOutOfRangeException>(() => game.Set(1, Half.Top, 3, BaseOccupancy.Empty, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => game.Set(0, Half.Top, 0, BaseOccupancy.Empty, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => game.Set(1, Half.Top, 0, BaseOccupancy.Empty, -1, 0));
            // A play from another situation cannot be applied to this one.
            LivePlay other = Single(game);
            game.Set(1, Half.Top, 1, BaseOccupancy.Empty, 0, 0);
            Assert.Throws<InvalidOperationException>(() => game.Apply(other));
        }

        [Test]
        public void PresetsCoverTheStandardSituations()
        {
            CollectionAssert.AreEqual(
                new[] { "Empty, 0 out", "R1, 0 out", "R1, 1 out", "R1 R2, 0 out", "R1 R3, 1 out", "R3, 1 out", "Loaded, 1 out", "Loaded, 2 out" },
                GameState.Presets.Select(p => p.Name));
            foreach (SituationPreset p in GameState.Presets)
            {
                var game = new GameState();
                game.Set(p);
                Assert.AreEqual((p.Outs, p.Bases), (game.Outs, game.Bases), p.Name);
            }
        }
    }
}
