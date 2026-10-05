using System;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-006B rules foundation: force relationships and their removal, the batter-runner, fly / force / tag outs and safe
    /// calls from authoritative timing, the simultaneous-arrival rule, unassisted put-outs, the defensive decision, the
    /// chronological event list and an out count that counts each out once.
    /// </summary>
    public class RulesTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);

        private static FieldingPlay Field(double mph, double launch, double spray, double spin) =>
            FieldingSolver.Solve(BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard));

        private static FieldingPlay SsGrounder() => Field(85.0, -8.0, -15.0, -1000.0);
        private static FieldingPlay FirstBaseGrounder() => Field(80.0, -8.0, 38.0, -900.0);
        private static FieldingPlay SecondBaseGrounder() => Field(80.0, -7.0, 18.0, -900.0);
        private static FieldingPlay CenterFieldFly() => Field(92.0, 32.0, 0.0, 2400.0);

        private static readonly Runner Batter = Runner.Batter, OnFirst = new Runner(Base.First), OnSecond = new Runner(Base.Second), OnThird = new Runner(Base.Third);

        /// <summary>A runner timing with chosen times (s from contact) to make the boundaries explicit.</summary>
        private sealed class FixedTiming : IRunnerTiming
        {
            private readonly double _batter, _runner;
            public FixedTiming(double batter, double runner = 3.75)
            {
                _batter = batter;
                _runner = runner;
            }

            public double TimeToNextBase(Runner runner) => runner.IsBatter ? _batter : _runner;
            public Vector3d PositionAt(Runner runner, double sinceContact) => ReferenceRunnerTiming.Instance.PositionAt(runner, sinceContact * ReferenceRunnerTiming.Instance.TimeToNextBase(runner) / TimeToNextBase(runner));
        }

        // ---------------------------------------------------------------- forces

        [Test]
        public void ForcesPropagateFromTheBatterThroughOccupiedBases()
        {
            CollectionAssert.AreEqual(new[] { Batter }, Forces.Forced(BaseOccupancy.Empty));
            CollectionAssert.AreEqual(new[] { Batter, OnFirst }, Forces.Forced(new BaseOccupancy(true, false, false)));
            CollectionAssert.AreEqual(new[] { Batter, OnFirst, OnSecond }, Forces.Forced(new BaseOccupancy(true, true, false)));
            CollectionAssert.AreEqual(new[] { Batter }, Forces.Forced(new BaseOccupancy(false, true, false)), "runner on second, first open: not forced");
            CollectionAssert.AreEqual(new[] { Batter, OnFirst }, Forces.Forced(new BaseOccupancy(true, false, true)), "runner on third, second open: not forced");
            CollectionAssert.AreEqual(new[] { Batter }, Forces.Forced(new BaseOccupancy(false, true, true)));
        }

        [Test]
        public void BasesLoadedForcesEveryBaseIncludingHome()
        {
            CollectionAssert.AreEqual(new[] { Batter, OnFirst, OnSecond, OnThird }, Forces.Forced(BaseOccupancy.Loaded));
            CollectionAssert.AreEqual(new[] { Base.First, Base.Second, Base.Third, Base.Home }, Forces.Forced(BaseOccupancy.Loaded).Select(r => r.Next));
            RulesPlay play = PlayResolver.Resolve(SsGrounder(), BaseOccupancy.Loaded, 0);
            // Every base has a force candidate the defense can see.
            foreach (Base b in new[] { Base.First, Base.Second, Base.Third, Base.Home })
                Assert.IsTrue(play.Candidates.Any(a => a.IsForce && a.Target == b), $"force at {b}");
        }

        [Test]
        public void ForceIsRemovedWhenAFollowingRunnerIsOutOrTheRunnerReachesTheBase()
        {
            var on1 = new BaseOccupancy(true, false, false);
            var batterOut = new PlayEvent(3.0, PlayEventKind.ForceOut, Batter, Base.First, DefensivePosition.FirstBase);
            Assert.IsTrue(Forces.IsForcedAt(OnFirst, on1, new[] { batterOut }, 2.999), "forced until the batter-runner is out");
            Assert.IsFalse(Forces.IsForcedAt(OnFirst, on1, new[] { batterOut }, 3.0), "batter-runner out: the runner must be tagged");
            var leadOut = new PlayEvent(3.0, PlayEventKind.ForceOut, OnFirst, Base.Second, DefensivePosition.Shortstop);
            Assert.IsTrue(Forces.IsForcedAt(Batter, on1, new[] { leadOut }, 3.5), "a lead runner's out does not remove the batter's force");
            var reached = new PlayEvent(3.75, PlayEventKind.Safe, OnFirst, Base.Second, null);
            Assert.IsFalse(Forces.IsForcedAt(OnFirst, on1, new[] { reached }, 3.75), "on the base he was forced to");
            Assert.IsFalse(Forces.IsForcedAt(OnSecond, on1, Array.Empty<PlayEvent>(), 1.0), "no runner on second");
        }

        // ---------------------------------------------------------------- outs and safe calls

        [Test]
        public void CaughtFlyIsOneFlyOut()
        {
            FieldingPlay fly = CenterFieldFly();
            RulesPlay play = PlayResolver.Resolve(fly, new BaseOccupancy(true, false, false), 1);
            Assert.AreEqual(1, play.Resolution.Events.Count, "the fly out only: runners hold (tagging up is TASK-007)");
            PlayEvent e = play.Resolution.Events[0];
            Assert.AreEqual(PlayEventKind.FlyOut, e.Kind);
            Assert.AreEqual(Batter, e.Runner);
            Assert.AreEqual(fly.PossessionTime, e.Time, 0.0, "at the catch");
            Assert.AreEqual(1, play.Resolution.Outs);
            Assert.AreEqual(2, play.Resolution.OutsAfter);
            Assert.AreEqual(1, play.Resolution.OutsAt(fly.PossessionTime - 1e-6));
            Assert.AreEqual(2, play.Resolution.OutsAt(fly.PossessionTime));
            Assert.AreEqual(2, play.Resolution.OutsAt(fly.PossessionTime + 100.0), "possession and later states never count it again");
            Assert.AreEqual(DefensiveActionKind.HoldBall, play.Chosen.Kind);
            Assert.IsFalse(Forces.IsForcedAt(OnFirst, play.Before, play.Resolution.Events, fly.PossessionTime), "batter out: no force");
        }

        [Test]
        public void RoutineShortstopGrounderIsAForceOutAtFirst()
        {
            RulesPlay play = PlayResolver.Resolve(SsGrounder(), BaseOccupancy.Empty, 0);
            Assert.AreEqual(DefensiveActionKind.ThrowToBase, play.Chosen.Kind);
            Assert.AreEqual(Base.First, play.Chosen.Target);
            ThrowPlay th = play.Defense.Throw;
            PlayEvent e = play.Resolution.Events.Single();
            Assert.AreEqual(PlayEventKind.ForceOut, e.Kind);
            Assert.AreEqual(Batter, e.Runner);
            Assert.AreEqual(Base.First, e.At);
            Assert.AreEqual(DefensivePosition.FirstBase, e.Fielder);
            Assert.AreEqual(th.Catch.Time, e.Time, 0.0, "the out is the catch on the bag");
            Assert.Less(e.Time, play.ArrivalTime(Batter), "the throw beats the runner");
            // The authoritative force test: possession and base touch, not before the catch.
            Assert.IsTrue(DefensiveDecision.HoldsBallOnBase(play.Defense, DefensivePosition.FirstBase, Base.First, th.Catch.Time));
            Assert.IsFalse(DefensiveDecision.HoldsBallOnBase(play.Defense, DefensivePosition.FirstBase, Base.First, th.Catch.Time - 1e-3), "no out without possession");
            Assert.IsFalse(DefensiveDecision.HoldsBallOnBase(play.Defense, DefensivePosition.FirstBase, Base.Second, th.Catch.Time), "not on the wrong base");
            Assert.AreEqual(1, play.Resolution.OutsAfter);
            Assert.AreEqual(PlayStatus.Live, play.Resolution.StatusAt(e.Time - 1e-3));
            Assert.AreEqual(PlayStatus.PlayComplete, play.Resolution.StatusAt(e.Time));
            Assert.AreEqual(RunnerStatus.Advancing, play.RunnerStateAt(Batter, e.Time - 1e-3).Status);
            Assert.IsTrue(play.RunnerStateAt(Batter, e.Time - 1e-3).Forced);
            Assert.AreEqual(RunnerStatus.Out, play.RunnerStateAt(Batter, e.Time).Status);
        }

        [Test]
        public void RunnerWhoBeatsTheThrowIsSafe()
        {
            // The same play against a batter-runner 3.3 s to first: the throw is late by less than the close-play window, so
            // it is still made, and the runner is safe at his arrival.
            FieldingPlay f = SsGrounder();
            RulesPlay play = PlayResolver.Resolve(f, BaseOccupancy.Empty, 0, new FixedTiming(3.3));
            Assert.AreEqual(DefensiveActionKind.ThrowToBase, play.Chosen.Kind, "a close play is still made");
            Assert.AreEqual("close play", play.Reason);
            Assert.Less(play.Chosen.Margin, 0.0);
            PlayEvent e = play.Resolution.Events.Single();
            Assert.AreEqual(PlayEventKind.Safe, e.Kind);
            Assert.AreEqual(Base.First, e.At);
            Assert.AreEqual(f.Ball.First.Time + 3.3, e.Time, 1e-12);
            Assert.AreEqual(0, play.Resolution.OutsAfter);
            Assert.AreEqual(RunnerStatus.Safe, play.RunnerStateAt(Batter, e.Time).Status);
            // Beaten by far more than the window: no throw (an infielder holds), the runner is still safe.
            RulesPlay beaten = PlayResolver.Resolve(f, BaseOccupancy.Empty, 0, new FixedTiming(2.5));
            Assert.AreEqual(DefensiveActionKind.HoldBall, beaten.Chosen.Kind, "no pointless throw");
            Assert.IsNull(beaten.Defense.Throw);
            Assert.AreEqual(PlayEventKind.Safe, beaten.Resolution.Events.Single().Kind);
        }

        [Test]
        public void SimultaneousArrivalIsSafeAndOneMillisecondDecides()
        {
            // OBR: out only if the base is tagged *before* the runner touches it. Within 1 ms (the timing resolution) is
            // simultaneous → safe.
            FieldingPlay f = SsGrounder();
            double done = PlayResolver.Resolve(f, BaseOccupancy.Empty, 0).Chosen.CompletionTime - f.Ball.First.Time;
            PlayEventKind Call(double runner) => PlayResolver.Resolve(f, BaseOccupancy.Empty, 0, new FixedTiming(runner)).Resolution.Events.Single().Kind;
            Assert.AreEqual(PlayEventKind.Safe, Call(done), "exact tie");
            Assert.AreEqual(PlayEventKind.Safe, Call(done + 0.9e-3), "within the resolution");
            Assert.AreEqual(PlayEventKind.ForceOut, Call(done + 1.1e-3), "the defense first by more than 1 ms");
            Assert.AreEqual(PlayEventKind.Safe, Call(done - 0.1), "the runner first");
            Assert.IsFalse(TimingCall.DefenseFirst(5.0, 5.0));
            Assert.IsTrue(TimingCall.DefenseFirst(5.0, 5.002));
        }

        [Test]
        public void FirstBasemanStepsOnFirstInsteadOfThrowing()
        {
            FieldingPlay f = FirstBaseGrounder();
            Assert.AreEqual(DefensivePosition.FirstBase, f.Primary);
            RulesPlay play = PlayResolver.Resolve(f, BaseOccupancy.Empty, 0);
            Assert.AreEqual(DefensiveActionKind.TouchBase, play.Chosen.Kind, "unassisted");
            Assert.IsNull(play.Defense.Throw, "no pointless throw");
            DefensiveAction thrown = play.Candidates.Single(a => a.Kind == DefensiveActionKind.ThrowToBase && a.Target == Base.First);
            Assert.Less(play.Chosen.CompletionTime, thrown.CompletionTime, "touching the bag is quicker than the flip to the covering 2B");
            PlayEvent e = play.Resolution.Events.Single();
            Assert.AreEqual(PlayEventKind.ForceOut, e.Kind);
            Assert.AreEqual(DefensivePosition.FirstBase, e.Fielder);
            Assert.AreEqual(Base.First, e.At);
            // He carried the ball onto the bag: holding it, within the touch envelope, moving continuously from his take.
            Assert.IsTrue(DefensiveDecision.HoldsBallOnBase(play.Defense, DefensivePosition.FirstBase, Base.First, e.Time));
            Assert.IsFalse(DefensiveDecision.HoldsBallOnBase(play.Defense, DefensivePosition.FirstBase, Base.First, e.Time - 0.05), "not on it earlier");
            Assert.AreEqual(BallAuthority.Possessed, play.Defense.AuthorityAt(e.Time + 1.0));
            AssertContinuous(t => play.Defense.FielderPositionAt(DefensivePosition.FirstBase, t), f.PossessionTime, "carry starts where he took the ball");
            AssertContinuous(t => VelocityOf(play.Defense, DefensivePosition.FirstBase, t), f.PossessionTime, "and as fast as he was going", 0.05);
        }

        [Test]
        public void FielderAlreadyOnTheBagMakesAnImmediateOut()
        {
            // A first baseman taking the ball standing on first (forced to his own bag): the out is the possession itself.
            var onBag = new DefensiveAlignment(Enumerable.Range(0, DefensiveAlignment.Count).Select(i => (DefensivePosition)i == DefensivePosition.FirstBase
                ? FieldLayout.BasePosition(Base.First) : DefensiveAlignment.Standard[(DefensivePosition)i]).ToArray());
            // A hard grounder aimed straight through the bag: he takes it on the bag (a softer one he charges and takes off it).
            Vector3d bag = FieldLayout.BasePosition(Base.First);
            double spray = Math.Atan2(bag.X - Contact.X, bag.Y - Contact.Y) * 180.0 / Math.PI;
            FieldingPlay f = FieldingSolver.Solve(BallInPlaySimulation.Run(new BattedBallLaunch(115.0, 0.0, spray, -800.0).ToState(Contact),
                EnvironmentState.Standard, FieldLayout.Standard), onBag, FielderProfile.For, FieldLayout.Standard);
            Assert.AreEqual(DefensivePosition.FirstBase, f.Primary);
            Assert.AreNotEqual(InterceptKind.FlyCatch, f.Intercept.Kind);
            Assert.IsTrue(BaseTouch.IsTouching(f.Motion(DefensivePosition.FirstBase).PositionAt(f.PossessionTime), Base.First), "taken on the bag");
            RulesPlay play = PlayResolver.Resolve(f, BaseOccupancy.Empty, 0);
            Assert.AreEqual("immediate out", play.Reason);
            Assert.AreEqual(f.PossessionTime, play.Resolution.Events.Single().Time, 0.0);
            Assert.IsNull(play.Defense.Carry);
        }

        [Test]
        public void RunnerOnFirstExposesBothForcesAndTakesTheEarlier()
        {
            var on1 = new BaseOccupancy(true, false, false);
            RulesPlay ss = PlayResolver.Resolve(SsGrounder(), on1, 0);
            DefensiveAction atSecond = ss.Candidates.Single(a => a.Kind == DefensiveActionKind.ThrowToBase && a.Target == Base.Second);
            DefensiveAction atFirst = ss.Candidates.Single(a => a.Kind == DefensiveActionKind.ThrowToBase && a.Target == Base.First);
            Assert.IsTrue(atSecond.Retires && atFirst.Retires, "two force outs available (a double play is not executed yet)");
            Assert.AreEqual(OnFirst, atSecond.Runner);
            Assert.AreSame(atSecond, ss.Chosen, "the earlier force: the lead runner at second");
            Assert.Less(atSecond.CompletionTime, atFirst.CompletionTime);
            Assert.AreEqual(new[] { PlayEventKind.ForceOut, PlayEventKind.Safe }, ss.Resolution.Events.Select(e => e.Kind));
            Assert.AreEqual(Batter, ss.Resolution.Events[1].Runner, "the batter-runner reaches first");

            RulesPlay second = PlayResolver.Resolve(SecondBaseGrounder(), on1, 0);
            Assert.AreEqual(DefensivePosition.SecondBase, second.Fielding.Primary);
            DefensiveAction quickest = second.Candidates.Where(a => a.IsForce && a.Retires).OrderBy(a => a.CompletionTime).First();
            Assert.AreSame(quickest, second.Chosen);
        }

        [Test]
        public void RunnerWhoIsNotForcedNeedsATag()
        {
            // Runner on second, first open: not forced. Scripted to run on contact, a throw to third must tag him.
            var on2 = new BaseOccupancy(false, true, false);
            FieldingPlay f = SsGrounder();
            RulesPlay play = PlayResolver.Resolve(f, on2, 0, runsOnContact: r => r == OnSecond,
                choose: cs => cs.Single(a => a.Kind == DefensiveActionKind.TagRunner && a.Target == Base.Third && a.Play.Throw != null));
            Assert.IsFalse(play.Candidates.Any(a => a.IsForce && a.Runner == OnSecond), "no force play on him");
            PlayEvent tag = play.Resolution.Events.Single(e => e.Runner == OnSecond);
            Assert.AreEqual(PlayEventKind.TagOut, tag.Kind);
            Assert.AreEqual(DefensivePosition.ThirdBase, tag.Fielder);
            Assert.Less(tag.Time, play.ArrivalTime(OnSecond), "tagged before he reaches third");
            Assert.GreaterOrEqual(tag.Time, play.Chosen.CompletionTime, "with the ball");
            Vector3d runner = ReferenceRunnerTiming.Instance.PositionAt(OnSecond, tag.Time - play.ContactTime);
            Assert.IsTrue(TagRules.CanTag(play.Defense.FielderPositionAt(DefensivePosition.ThirdBase, tag.Time), runner, true));
            Assert.IsFalse(TagRules.CanTag(play.Defense.FielderPositionAt(DefensivePosition.ThirdBase, tag.Time - 0.01),
                ReferenceRunnerTiming.Instance.PositionAt(OnSecond, tag.Time - 0.01 - play.ContactTime), true), "the first moment within reach");
            Assert.AreEqual(1, play.Resolution.Outs);
        }

        [Test]
        public void TagNeedsPossessionAndReach()
        {
            var defender = new Vector3d(10.0, 10.0, 0.0);
            Assert.IsTrue(TagRules.CanTag(defender, defender + new Vector3d(0.9, 0.0, 0.0), true));
            Assert.IsFalse(TagRules.CanTag(defender, defender + new Vector3d(0.9, 0.0, 0.0), false), "no ball, no tag");
            Assert.IsFalse(TagRules.CanTag(defender, defender + new Vector3d(1.2, 0.0, 0.0), true), "out of reach");
        }

        [Test]
        public void BaseTouchEnvelope()
        {
            Vector3d bag = FieldLayout.BasePosition(Base.First);
            Assert.IsTrue(BaseTouch.IsTouching(bag + new Vector3d(0.59, 0.0, 0.0), Base.First));
            Assert.IsFalse(BaseTouch.IsTouching(bag + new Vector3d(0.61, 0.0, 0.0), Base.First));
            Assert.IsFalse(BaseTouch.IsTouching(bag, Base.Second));
        }

        // ---------------------------------------------------------------- decision, events, counting

        [Test]
        public void OutfieldHitWithNoPlayReturnsTheBallAndPretendsNoOut()
        {
            FieldingPlay single = Field(98.0, 9.0, -24.0, 900.0);
            RulesPlay play = PlayResolver.Resolve(single, BaseOccupancy.Empty, 0);
            Assert.AreEqual(DefensiveActionKind.ThrowToBase, play.Chosen.Kind);
            Assert.AreEqual(Base.Second, play.Chosen.Target);
            Assert.IsNull(play.Chosen.Runner, "returning the ball, not a play on a runner");
            Assert.AreEqual(0, play.Resolution.Outs);
            Assert.AreEqual(PlayEventKind.Safe, play.Resolution.Events.Single().Kind);
        }

        [Test]
        public void DecisionsForTheEverydayPlays()
        {
            // (Replaces TASK-006A's fixed default targets.) Infielders and the pitcher throw to first; the first baseman steps
            // on it; an outfield hit goes back to second; a caught fly is held.
            Assert.AreEqual(Expect(DefensiveActionKind.ThrowToBase, Base.First), Decision(SsGrounder()), "SS grounder");
            Assert.AreEqual(Expect(DefensiveActionKind.ThrowToBase, Base.First), Decision(Field(60.0, -20.0, 5.0, -800.0)), "pitcher's chopper");
            Assert.AreEqual(Expect(DefensiveActionKind.TouchBase, Base.First), Decision(FirstBaseGrounder()), "1B grounder");
            Assert.AreEqual(Expect(DefensiveActionKind.ThrowToBase, Base.Second), Decision(Field(98.0, 9.0, -24.0, 900.0)), "LF single");
            Assert.AreEqual(Expect(DefensiveActionKind.HoldBall, (Base?)null), Decision(CenterFieldFly()), "fly out");
        }

        private static (DefensiveActionKind, Base?) Expect(DefensiveActionKind kind, Base? b) => (kind, b);

        private static (DefensiveActionKind, Base?) Decision(FieldingPlay f)
        {
            DefensiveAction a = PlayResolver.Resolve(f, BaseOccupancy.Empty, 0).Chosen;
            return (a.Kind, a.Target);
        }

        [Test]
        public void EventsAreChronologicalAndEachRunnerAppearsOnce()
        {
            RulesPlay play = PlayResolver.Resolve(SsGrounder(), BaseOccupancy.Loaded, 1);
            var times = play.Resolution.Events.Select(e => e.Time).ToArray();
            CollectionAssert.AreEqual(times.OrderBy(t => t).ToArray(), times);
            Assert.AreEqual(4, play.Resolution.Events.Select(e => e.Runner).Distinct().Count(), "every runner resolved once");
            Assert.AreEqual(1, play.Resolution.Outs);
            Assert.AreEqual(2, play.Resolution.OutsAfter);
            Assert.Throws<ArgumentException>(() => new PlayResolution(new[]
            {
                new PlayEvent(1.0, PlayEventKind.ForceOut, Batter, Base.First, DefensivePosition.FirstBase),
                new PlayEvent(2.0, PlayEventKind.ForceOut, Batter, Base.First, DefensivePosition.FirstBase),
            }, 0, 2.0, false), "the same out cannot be recorded twice");
            var two = new PlayResolution(new[]
            {
                new PlayEvent(3.0, PlayEventKind.ForceOut, Batter, Base.First, DefensivePosition.FirstBase),
                new PlayEvent(2.0, PlayEventKind.ForceOut, OnFirst, Base.Second, DefensivePosition.Shortstop),
            }, 0, 3.0, false);
            Assert.AreEqual(2, two.Outs, "several outs in one play can be represented (double plays later)");
            Assert.AreEqual(OnFirst, two.Events[0].Runner, "ordered by time");
        }

        [Test]
        public void DeadBallsHaveNoRunnersOrOuts()
        {
            FieldingPlay homer = Field(110.0, 28.0, 0.0, 2200.0);
            RulesPlay play = PlayResolver.Resolve(homer, BaseOccupancy.Loaded, 2);
            Assert.IsTrue(play.Resolution.BallDead);
            Assert.IsEmpty(play.Resolution.Events);
            Assert.IsFalse(play.HasBatterRunner);
            Assert.AreEqual(PlayStatus.BallDead, play.Resolution.StatusAt(10.0));
            Assert.AreEqual(2, play.Resolution.OutsAfter);
        }

        [Test]
        public void IdenticalPlaysResolveIdentically()
        {
            RulesPlay a = PlayResolver.Resolve(SsGrounder(), BaseOccupancy.Loaded, 0), b = PlayResolver.Resolve(SsGrounder(), BaseOccupancy.Loaded, 0);
            Assert.AreEqual(a.Chosen.ToString(), b.Chosen.ToString());
            Assert.AreEqual(a.Resolution.Events.Count, b.Resolution.Events.Count);
            for (int i = 0; i < a.Resolution.Events.Count; i++)
            {
                Assert.AreEqual(a.Resolution.Events[i].Time, b.Resolution.Events[i].Time, 0.0);
                Assert.AreEqual(a.Resolution.Events[i].Kind, b.Resolution.Events[i].Kind);
                Assert.AreEqual(a.Resolution.Events[i].Runner, b.Resolution.Events[i].Runner);
            }
        }

        // ---------------------------------------------------------------- motion continuity (TASK-006A limitations)

        [Test]
        public void ReceiverAdjustsWithoutStopping()
        {
            // A weak (15 m/s) throw from third falls short of first: the first baseman, still running to the bag, comes in to
            // catch it. His position and velocity are continuous where he switches from covering to adjusting.
            FieldingPlay f = Field(80.0, -7.0, -30.0, -900.0);
            ThrowProfile Weak(DefensivePosition p) => new ThrowProfile(15.0, 0.7, 1.8);
            DefensivePlay play = ThrowPlanner.Plan(f, Base.First, FielderProfile.For, Weak, EnvironmentState.Standard, FieldLayout.Standard);
            ThrowPlay th = play.Throw;
            ReceiverPath path = th.Path;
            Assert.IsTrue(th.Caught, "caught on the fly");
            Assert.Less(path.SwitchTime, path.ToBase.ArrivalTime, "he adjusts before reaching the bag");
            Assert.Greater(path.ToBase.SpeedAt(path.SwitchTime), 3.0, "running when he adjusts (the case that used to stop dead)");
            AssertContinuous(path.PositionAt, path.SwitchTime, "position at the switch");
            AssertContinuous(path.VelocityAt, path.SwitchTime, "velocity at the switch", 0.01);
            // No velocity jump anywhere from the cover through the catch: |Δv| per 1 ms within the law's acceleration.
            for (double t = path.ToBase.StartTime; t < th.Catch.Time + 1.0; t += 1e-3)
                Assert.Less((path.VelocityAt(t + 1e-3) - path.VelocityAt(t)).Length, 0.03, $"acceleration at +{t - f.Ball.First.Time:0.000}");
            Vector3d gap = th.Catch.Ball.Position - path.PositionAt(th.Catch.Time);
            Assert.LessOrEqual(new Vector3d(gap.X, gap.Y, 0.0).Length, FielderProfile.For(DefensivePosition.FirstBase).ReachAt(th.Catch.Ball.Position.Z) + 1e-6, "within reach at the catch");
            Assert.LessOrEqual(path.ToCatch.RouteSpeed, FielderProfile.For(DefensivePosition.FirstBase).MaxSpeed + 1e-9, "no faster than he can run");
        }

        [Test]
        public void ThrowerStillSlidingWaitsOnceAndReleasesOnce()
        {
            // The shortstop takes a grounder on the run (still braking after the take) and must wait for a slow cover at first
            // (a 3 m/s first baseman): the hold-for-cover is iterated while his slide changes the throw. One release, after the
            // transfer, timed so the cover is on the bag when the throw arrives; identical on recomputation.
            FieldingPlay f = SsGrounder();
            FielderProfile Slow(DefensivePosition p) => p == DefensivePosition.FirstBase
                ? new FielderProfile(0.25, 3.0, 0.78, 6.0, 1.0, 0.6, 2.6, 0.30) : FielderProfile.For(p);
            DefensivePlay Plan() => ThrowPlanner.Plan(f, Base.First, Slow, ThrowProfile.For, EnvironmentState.Standard, FieldLayout.Standard);
            DefensivePlay play = Plan();
            ThrowPlay th = play.Throw;
            double transferEnd = f.PossessionTime + ThrowProfile.For(DefensivePosition.Shortstop).TransferTime;
            Assert.Greater(f.Motion(DefensivePosition.Shortstop).SpeedAt(transferEnd), 0.5, "still sliding when he would have thrown");
            Assert.Greater(th.ReleaseTime, transferEnd + 0.2, "he waits for the cover");
            Assert.IsTrue(th.Caught, "caught");
            Assert.LessOrEqual(th.Path.ToBase.ArrivalTime, th.Catch.Time + 1e-3, "the cover is there when the throw arrives");
            Assert.Less(th.Catch.Time - th.Path.ToBase.ArrivalTime, 0.15, "and the wait was no longer than needed");
            // Exactly one possessed → thrown transition, at the release; possession valid throughout the wait.
            int releases = 0;
            BallAuthority previous = play.AuthorityAt(f.PossessionTime);
            for (double t = f.PossessionTime; t < th.Catch.Time + 0.5; t += 1e-3)
            {
                BallAuthority a = play.AuthorityAt(t);
                if (previous == BallAuthority.Possessed && a == BallAuthority.Thrown) releases++;
                if (t < th.ReleaseTime) Assert.AreEqual(DefensivePosition.Shortstop, play.HolderAt(t), $"holding while he waits ({t:0.000})");
                previous = a;
            }

            Assert.AreEqual(1, releases, "released once");
            DefensivePlay again = Plan();
            Assert.AreEqual(th.ReleaseTime, again.Throw.ReleaseTime, 0.0, "deterministic");
            Assert.AreEqual(th.Catch.Time, again.Throw.Catch.Time, 0.0);
        }

        private static Vector3d VelocityOf(DefensivePlay play, DefensivePosition p, double t) =>
            (play.FielderPositionAt(p, t + 1e-5) - play.FielderPositionAt(p, t - 1e-5)) / 2e-5;

        /// <summary>No jump at <paramref name="at"/>: the change across ±1e-7 s (≤ 1e-6 m at running speed) stays under the tolerance.</summary>
        private static void AssertContinuous(Func<double, Vector3d> f, double at, string what, double tolerance = 1e-5) =>
            Assert.Less((f(at + 1e-7) - f(at - 1e-7)).Length, tolerance, what);
    }
}
