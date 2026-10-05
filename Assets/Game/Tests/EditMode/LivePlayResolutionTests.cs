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
    /// TASK-009 live play resolution: double plays earned (or missed) by timing from double-play depth, home runs and
    /// awarded bases, tag-up throws home, and each play's event log.
    /// </summary>
    public class LivePlayResolutionTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);
        private static readonly BaseOccupancy OnFirst = new BaseOccupancy(true, false, false), OnThird = new BaseOccupancy(false, false, true);

        /// <summary>The play from <paramref name="situation"/>, the defense in the situation's alignment.</summary>
        private static LivePlay Play(double mph, double launch, double spray, double spin, Situation situation)
        {
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard);
            var play = new LivePlay(FieldingSolver.Solve(ball, situation.Alignment, FielderProfile.For, FieldLayout.Standard), situation);
            play.RunToEnd();
            Assert.AreEqual(1, play.Log.Count(e => e.Kind == PlayLogKind.PlayOver), "the play ends once");
            Assert.IsFalse(play.Log.Any(e => e.Text.Contains("time limit")));
            Assert.AreEqual(play.RulesEvents.Count(e => e.IsOut), play.Log.Count(e => e.Kind == PlayLogKind.Out), "one log entry per out");
            return play;
        }

        private static readonly Situation R1NoneOut = new Situation(0, OnFirst);

        [TestCase(85.0, -8.0, -15.0, -1000.0, DefensivePosition.Shortstop, DefensivePosition.SecondBase, TestName = "6-4-3")]
        [TestCase(80.0, -7.0, -30.0, -900.0, DefensivePosition.ThirdBase, DefensivePosition.SecondBase, TestName = "5-4-3")]
        [TestCase(88.0, -8.0, 16.0, -900.0, DefensivePosition.SecondBase, DefensivePosition.Shortstop, TestName = "4-6-3")]
        [TestCase(80.0, -12.0, -2.0, -900.0, DefensivePosition.P, DefensivePosition.Shortstop, TestName = "1-6-3")]
        public void DoublePlayIsTurned(double mph, double launch, double spray, double spin, DefensivePosition fielder, DefensivePosition pivot)
        {
            LivePlay play = Play(mph, launch, spray, spin, R1NoneOut);
            Assert.AreEqual(fielder, play.Fielding.Primary);
            // The feed to the pivot man covering second (force on the lead runner), then his throw to first.
            Assert.AreEqual(2, play.Defense.Throws.Count);
            LiveThrow feed = play.Defense.Throws[0], relay = play.Defense.Throws[1];
            Assert.AreEqual((fielder, pivot, (Base?)Base.Second), (feed.Thrower, feed.Receiver, feed.Target));
            Assert.AreEqual((pivot, DefensivePosition.FirstBase, (Base?)Base.First), (relay.Thrower, relay.Receiver, relay.Target));
            Assert.AreEqual(ThrowProfile.Full(pivot).Speed, relay.Flight.First.Velocity.Length, 1e-6, "the turn is a full-effort throw");
            Assert.GreaterOrEqual(relay.ReleaseTime, feed.Catch.Time + ThrowProfile.For(pivot).TransferTime - 1e-9, "a real pivot transfer");
            // Two force outs, in that order, earned by timing: the ball beats the batter-runner to first.
            PlayEvent[] outs = play.RulesEvents.Where(e => e.IsOut).ToArray();
            Assert.AreEqual(2, outs.Length);
            Assert.AreEqual((PlayEventKind.ForceOut, new Runner(Base.First), (Base?)Base.Second), (outs[0].Kind, outs[0].Runner, outs[0].At));
            Assert.AreEqual((PlayEventKind.ForceOut, Runner.Batter, (Base?)Base.First), (outs[1].Kind, outs[1].Runner, outs[1].At));
            Assert.AreEqual(BaseOccupancy.Empty, play.ResultingBases());
        }

        [Test]
        public void ASlowRollerIsOnlyTheForceAtSecond()
        {
            // The same play on a 70 mph roller: the pivot's throw arrives after the batter-runner — one out.
            LivePlay play = Play(70.0, -10.0, -15.0, -900.0, R1NoneOut);
            Assert.AreEqual(2, play.Defense.Throws.Count, "they try for two");
            Assert.AreEqual(1, play.OutsMade);
            Assert.IsTrue(play.Log.Any(e => e.Kind == PlayLogKind.Safe && e.Runner == Runner.Batter && e.At == Base.First), "safe at first");
            Assert.IsTrue(play.ResultingBases().First && !play.ResultingBases().Second);
        }

        [Test]
        public void DoublePlayDepthWithARunnerOnFirstAndFewerThanTwoOut()
        {
            DefensiveAlignment standard = DefensiveAlignment.Standard, depth = DefensiveAlignment.DoublePlayDepth;
            Vector3d second = FieldLayout.BasePosition(Base.Second);
            foreach (DefensivePosition m in new[] { DefensivePosition.Shortstop, DefensivePosition.SecondBase })
            {
                Assert.Less((depth[m] - second).Length, (standard[m] - second).Length - 2.0, $"{m} closer to the bag");
                Assert.Less(depth[m].Length, standard[m].Length - 1.0, $"{m} in toward home");
            }

            foreach (DefensivePosition p in new[] { DefensivePosition.P, DefensivePosition.C, DefensivePosition.FirstBase, DefensivePosition.ThirdBase, DefensivePosition.CenterField })
                Assert.AreEqual(standard[p], depth[p]);
            Assert.AreEqual(depth[DefensivePosition.Shortstop], new Situation(1, OnFirst).Alignment[DefensivePosition.Shortstop]);
            Assert.AreEqual(standard[DefensivePosition.Shortstop], new Situation(2, OnFirst).Alignment[DefensivePosition.Shortstop], "two out: normal depth");
            Assert.AreEqual(standard[DefensivePosition.Shortstop], new Situation(0, OnThird).Alignment[DefensivePosition.Shortstop], "no force at second");
        }

        [Test]
        public void HomeRunScoresEveryRunnerAndTheBatter()
        {
            LivePlay play = Play(106.0, 28.0, -10.0, 2000.0, new Situation(1, BaseOccupancy.Loaded));
            Assert.AreEqual(4, play.AwardedBases);
            Assert.AreEqual(4, play.Runs);
            Assert.AreEqual(0, play.OutsMade);
            Assert.AreEqual(BaseOccupancy.Empty, play.ResultingBases());
            // Each runs the bases in order (touching every one), the lead runner first; nobody passes anybody.
            foreach (var (runner, bases) in new[] { (Runner.Batter, 4), (new Runner(Base.First), 3), (new Runner(Base.Third), 1) })
                Assert.AreEqual(bases, play.Log.Count(e => e.Kind == PlayLogKind.BaseTouch && e.Runner == runner), $"{runner}");
            double[] scored = play.Log.Where(e => e.Kind == PlayLogKind.Run).Select(e => e.Time).ToArray();
            CollectionAssert.AreEqual(scored.OrderBy(t => t).ToArray(), scored);
            Assert.AreEqual(Runner.Batter, play.Log.Last(e => e.Kind == PlayLogKind.Run).Runner, "the batter scores last");
            Assert.Greater(play.EndTime, play.Log.Last(e => e.Kind == PlayLogKind.Run).Time - 1e-9, "the play ends once he has scored");
        }

        [TestCase(Base.Home, 4, Base.Home)]
        [TestCase(Base.First, 4, Base.Home)]
        [TestCase(Base.Third, 4, Base.Home)]
        [TestCase(Base.Home, 2, Base.Second)]
        [TestCase(Base.First, 2, Base.Third)]
        [TestCase(Base.Second, 2, Base.Home)]
        [TestCase(Base.Third, 2, Base.Home)]
        public void AwardedBases(Base from, int n, Base to) => Assert.AreEqual(to, LivePlay.AwardedBase(from, n));

        [Test]
        public void TagUpThrowHomeIsAPlayAtThePlate()
        {
            var situation = new Situation(1, OnThird);
            // A medium fly to centre: the runner tags and scores ahead of a close throw home.
            LivePlay safe = Play(78.0, 38.0, -8.0, 2400.0, situation);
            LiveThrow th = safe.Defense.Throws[0];
            Assert.AreEqual((DefensivePosition.CenterField, DefensivePosition.C, (Base?)Base.Home), (th.Thrower, th.Receiver, th.Target));
            double scored = safe.Log.First(e => e.Kind == PlayLogKind.Run).Time;
            Assert.That(th.Catch.Time - scored, Is.InRange(0.0, DefensiveDecision.CloseWindow), "a close play");
            Assert.AreEqual(1, safe.Runs);
            // A shallower one: the throw beats him; the tag is the third out, no run.
            LivePlay @out = Play(76.0, 40.0, 8.0, 2400.0, situation);
            Assert.IsTrue(@out.RulesEvents.Any(e => e.Kind == PlayEventKind.TagOut && e.At == Base.Home && e.Runner == new Runner(Base.Third)));
            Assert.AreEqual(2, @out.OutsMade, "fly out and the tag at home");
            Assert.AreEqual(0, @out.Runs);
            Assert.IsTrue(@out.Log.Last().Text.Contains("third out"));
        }
    }
}
