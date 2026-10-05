using System;
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
    /// <summary>TASK-015: a fair ball's play described from its authoritative record (awards, outs, the batter's base).</summary>
    public class PlayResultsTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);

        private static LivePlay Play(int outs, BaseOccupancy bases, double mph, double launch, double spray, double spin)
        {
            var s = new Situation(outs, bases);
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard);
            var play = new LivePlay(FieldingSolver.Solve(ball, s.Alignment, FielderProfile.For, FieldLayout.Standard), s);
            play.RunToEnd();
            return play;
        }

        private static readonly BaseOccupancy Empty = BaseOccupancy.Empty, R1 = new BaseOccupancy(true, false, false), R3 = new BaseOccupancy(false, false, true);

        [TestCase(0, "", 60.0, -20.0, -40.0, -1000.0, PlayResultKind.Groundout)]
        [TestCase(0, "", 60.0, -20.0, 15.0, -1000.0, PlayResultKind.Single)]
        [TestCase(0, "", 60.0, 4.0, 0.0, 1800.0, PlayResultKind.LineOut)]
        [TestCase(0, "", 60.0, 30.0, -15.0, 1800.0, PlayResultKind.FlyOut)]
        [TestCase(0, "", 60.0, 60.0, -40.0, 3000.0, PlayResultKind.PopOut)]
        [TestCase(0, "", 90.0, 30.0, -40.0, 1800.0, PlayResultKind.HomeRun)]
        [TestCase(0, "", 100.0, 12.0, -15.0, 1800.0, PlayResultKind.Double)]
        [TestCase(0, "1", 60.0, -20.0, -40.0, -1000.0, PlayResultKind.FieldersChoice)]
        [TestCase(0, "1", 60.0, -8.0, 15.0, -1000.0, PlayResultKind.DoublePlay)]
        [TestCase(1, "3", 90.0, 18.0, -15.0, 1800.0, PlayResultKind.SacrificeFly)]
        [TestCase(1, "3", 85.0, 20.0, -30.0, 1800.0, PlayResultKind.SacrificeFly)]
        [TestCase(2, "3", 55.0, 28.0, -30.0, 1800.0, PlayResultKind.FlyOut)]           // two out: no sacrifice fly
        [TestCase(2, "", 95.0, 12.0, -42.0, 1800.0, PlayResultKind.Single)]           // thrown out stretching: still a single
        [TestCase(1, "2", 70.0, 12.0, -30.0, 1800.0, PlayResultKind.Single)]          // runner thrown out, not forced: a hit
        [TestCase(1, "123", 85.0, 20.0, -30.0, 1800.0, PlayResultKind.DoublePlay)]    // caught, a run scores, a runner doubled off
        public void ThePlayIsDescribedFromItsRecord(int outs, string on, double mph, double launch, double spray, double spin, PlayResultKind expected)
        {
            var bases = new BaseOccupancy(on.Contains("1"), on.Contains("2"), on.Contains("3"));
            LivePlay play = Play(outs, bases, mph, launch, spray, spin);
            PlayResultKind kind = PlayResults.Classify(play);
            Assert.AreEqual(expected, kind);
            // Each description agrees with the record it comes from.
            switch (kind)
            {
                case PlayResultKind.HomeRun: Assert.AreEqual(4, play.AwardedBases); break;
                case PlayResultKind.DoublePlay: Assert.AreEqual(2, play.OutsMade); break;
                case PlayResultKind.Single:
                    Assert.AreEqual(Base.First, play.RunnerOf(Runner.Batter).LastTouched);
                    if (play.OutsMade == 1 && !play.RunnerOf(Runner.Batter).IsOut)
                        Assert.IsTrue(play.RulesEvents.Any(e => e.IsOut && e.Kind != PlayEventKind.ForceOut), "the other runner's out is not a force");
                    break;
                case PlayResultKind.Double: Assert.AreEqual(Base.Second, play.RunnerOf(Runner.Batter).LastTouched); break;
                case PlayResultKind.SacrificeFly: Assert.AreEqual((1, 1), (play.Runs, play.OutsMade)); break;
                case PlayResultKind.FieldersChoice:
                    Assert.AreEqual(1, play.OutsMade);
                    Assert.IsFalse(play.RunnerOf(Runner.Batter).IsOut);
                    break;
                case PlayResultKind.LineOut: Assert.Less(PlayResults.LaunchAngle(play), PlayResults.LineDriveAngle); break;
                case PlayResultKind.PopOut: Assert.Greater(PlayResults.LaunchAngle(play), PlayResults.PopUpAngle); break;
            }
        }

        [Test]
        public void ADoublePlayOffACaughtFlyEvenWhenARunScores()
        {
            LivePlay play = Play(1, BaseOccupancy.Loaded, 85.0, 20.0, -30.0, 1800.0);
            Assert.AreEqual(LivePlay.BallKind.Caught, play.Kind);
            Assert.IsTrue(play.RunnerOf(Runner.Batter).IsOut, "the batter is out on the catch");
            Assert.AreEqual((2, PlayResultKind.DoublePlay), (play.OutsMade, PlayResults.Classify(play)));
        }

        [Test]
        public void PopUpsKeepRunnersAtTheBagButOutfieldFliesStillSendThemHalfway()
        {
            // A runner on third with one out does not leave on an infield pop-up …
            LivePlay pop = Play(1, R3, 55.0, 60.0, -42.0, 3000.0);
            Assert.IsFalse(DefensiveDecision.IsOutfielder(pop.Fielding.Primary.Value));
            Assert.AreEqual((0, R3, 1), (pop.Runs, pop.ResultingBases(), pop.OutsMade));
            Assert.IsFalse(pop.Log.Any(e => e.Text.Contains("halfway")));
            // … while on a fly to an outfielder a runner on first still goes halfway (TASK-007 convention).
            LivePlay fly = Play(0, R1, 70.0, 30.0, -20.0, 1800.0);
            Assert.IsTrue(DefensiveDecision.IsOutfielder(fly.Fielding.Primary.Value));
            Assert.IsTrue(fly.Log.Any(e => e.Text.Contains("runner on 1B: halfway")));
        }

        [Test]
        public void RunnersStayAtTheBagOnAnInfieldPopUp()
        {
            // A high pop-up to second base with runners on first and second, none out: they do not go halfway (a short throw
            // back would double them off) — the batter is out, nobody else.
            LivePlay play = Play(0, new BaseOccupancy(true, true, false), 60.0, 60.0, 0.0, 3000.0);
            Assert.AreEqual(LivePlay.BallKind.Caught, play.Kind);
            Assert.IsFalse(DefensiveDecision.IsOutfielder(play.Fielding.Primary.Value));
            Assert.AreEqual((1, PlayResultKind.PopOut), (play.OutsMade, PlayResults.Classify(play)));
            Assert.AreEqual(new BaseOccupancy(true, true, false), play.ResultingBases());
        }

        [Test]
        public void TheRecordedPlateAppearanceCarriesTheResult()
        {
            var game = new GameState();
            game.Set(1, Half.Top, 0, R1, 0, 0);
            LivePlay dp = Play(0, R1, 60.0, -8.0, 15.0, -1000.0);
            game.Apply(dp);
            PlateAppearance pa = game.Completed[0];
            Assert.AreEqual((PlayResultKind.DoublePlay, "double play"), (pa.PlayResult.Value, pa.Result));
            StringAssert.Contains("double play", game.Log[0]);
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            Assert.IsNull(game.Completed[1].PlayResult, "a walk is not a play");
            Assert.Throws<ArgumentException>(() => PlayResults.Classify(Play(0, Empty, 85.0, 35.0, 50.0, 2000.0)), "a foul has no result");
        }
    }
}
