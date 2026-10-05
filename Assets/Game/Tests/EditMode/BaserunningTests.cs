using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-007 continuous baserunning: base-path geometry, the running law against MLB references, prediction = execution,
    /// forced and unforced decisions, rounding, overrunning first, tag-ups, runner order, the force-out boundary, frame-rate
    /// independence and carrying the result into the next plate appearance.
    /// </summary>
    public class BaserunningTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);
        private static readonly RunnerProfile P = RunnerProfile.Standard;

        private static FieldingPlay Field(double mph, double launch, double spray, double spin) =>
            FieldingSolver.Solve(BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard));

        private static LivePlay Play(FieldingPlay f, int outs, BaseOccupancy bases, RunnerProfile? profile = null)
        {
            var play = new LivePlay(f, new Situation(outs, bases), profile);
            play.RunToEnd();
            return play;
        }

        private static FieldingPlay SsGrounder() => Field(85.0, -8.0, -15.0, -1000.0);
        private static FieldingPlay LeftSideGrounder() => Field(80.0, -7.0, -30.0, -900.0);   // 3B
        private static FieldingPlay DeepCenterFly() => Field(95.0, 30.0, -10.0, 2200.0);
        private static FieldingPlay CenterFieldSingle() => Field(95.0, 6.0, 0.0, 700.0);

        private static double TouchTime(LivePlay play, Runner r, Base b) =>
            play.Log.First(e => e.Kind == PlayLogKind.BaseTouch && e.Runner == r && e.At == b).Time;

        // ---------------------------------------------------------------- geometry and law

        [Test]
        public void BasePathPassesThroughTheBagsAndTheBananaDriftsOutward()
        {
            foreach (Base from in new[] { Base.Home, Base.First, Base.Second, Base.Third })
            foreach (bool banana in new[] { false, true })
            {
                BaseLeg leg = BaseLeg.Of(from, banana);
                Assert.Less((leg.PositionAt(0.0) - FieldLayout.BasePosition(from)).Length, 1e-9);
                Assert.Less((leg.PositionAt(leg.Length) - FieldLayout.BasePosition(BaseLeg.Bases(from))).Length, 1e-9, "ends on the bag");
                double chord = (leg.End - leg.Start).Length;
                if (!banana) Assert.AreEqual(chord, leg.Length, 1e-9, "straight leg");
                else
                {
                    Assert.Greater(leg.Length, chord, "the banana is longer");
                    Assert.Less(leg.Length, chord * 1.02, "but only slightly");
                    // Its widest point is outside the diamond, by the banana width.
                    Vector3d centre = 0.5 * (FieldLayout.BasePosition(Base.Home) + FieldLayout.BasePosition(Base.Second));
                    Vector3d mid = leg.PositionAt(0.775 * leg.Length), onLine = leg.Start + 0.775 * (leg.End - leg.Start);
                    Assert.Greater((mid - centre).Length, (onLine - centre).Length + 0.9 * BaseLeg.BananaWidth - 1e-6, $"{from} drifts out");
                }
            }
        }

        [Test]
        public void HomeToFirstMatchesTheMlbAverage()
        {
            // Batter-runner running out a grounder (the force is made at second, so he reaches first): contact → first base
            // 4.28 s on average (Statcast; RHB 4.30, LHB 4.26).
            LivePlay play = Play(SsGrounder(), 0, new BaseOccupancy(true, false, false));
            double t = TouchTime(play, Runner.Batter, Base.First) - play.ContactTime;
            Assert.AreEqual(4.28, t, 0.06);
            // 90 ft from the first step (Statcast split average 4.02 s) at top speed 27 ft/s.
            Assert.AreEqual(4.02, t - P.BatterStartDelay, 0.06);
        }

        [Test]
        public void PredictionEqualsExecution()
        {
            // The planner's arrival at the start of a run is the moment the executed run touches the base (same law).
            LivePlay play = Play(LeftSideGrounder(), 1, BaseOccupancy.Loaded);
            double contact = play.ContactTime;
            foreach (Runner r in new[] { Runner.Batter, new Runner(Base.First), new Runner(Base.Third) })
            {
                double start = contact + (r.IsBatter ? P.BatterStartDelay : P.ReadDelay);
                double predicted = RunnerPlanner.ArrivalTime(P, BaseLeg.Of(r.From, false), LivePlay.Lead(r.From), 0.0, start, r.Next, r.IsBatter);
                Assert.AreEqual(predicted, TouchTime(play, r, r.Next), 1e-6, $"{r}");
            }

            // Multi-base: predicted arrival at third from the batter's box equals the executed run when he is sent there.
            double go = contact + P.BatterStartDelay;
            double toThird = RunnerPlanner.ArrivalTime(P, BaseLeg.Of(Base.Home, true), 0.0, 0.0, go, Base.Third, false);
            List<RunnerPlanner.PlannedLeg> plan = RunnerPlanner.Plan(P, BaseLeg.Of(Base.Home, true), 0.0, 0.0, go, Base.Third, false);
            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(toThird, plan[2].Motion.ArrivalTime, 0.0);
            Assert.AreEqual(plan[0].Motion.ArrivalTime, plan[1].Motion.StartTime, 0.0, "legs chain without a gap");
            Assert.AreEqual(plan[0].Motion.EndSpeed, plan[1].Motion.V0, 1e-12, "and without a speed jump");
            Assert.That(toThird - contact, Is.InRange(11.0, 12.5), "home to third ≈ 11.5 s for an average runner (DERIVED from Statcast records)");
        }

        [Test]
        public void RoundingKeepsSpeedUnderTheTurnLimitAndStopsSlideOnTheBag()
        {
            double go = 0.0;
            List<RunnerPlanner.PlannedLeg> plan = RunnerPlanner.Plan(P, BaseLeg.Of(Base.First, true), LivePlay.Lead(Base.First), 0.0, go, Base.Third, false);
            Assert.LessOrEqual(plan[0].Motion.EndSpeed, P.RoundingSpeed + 1e-9, "through second at no more than the rounding speed");
            Assert.Greater(plan[0].Motion.EndSpeed, 0.6 * P.MaxSpeed, "but rounding, not stopping");
            Assert.IsTrue(plan[0].Leg.Banana, "the leg to the base he rounds is the banana route");
            Assert.IsFalse(plan[1].Leg.Banana, "the leg to his destination is straight");
            Assert.AreEqual(0.0, plan[1].Motion.EndSpeed, 1e-9, "stops on third");
            Assert.AreEqual(plan[1].Leg.Length, plan[1].Motion.DistanceAt(plan[1].Motion.RestTime + 1.0), 1e-9, "on the bag, not past it");
            // Speed is continuous and never above the top speed.
            foreach (var leg in plan)
                for (double t = leg.Motion.StartTime; t < leg.Motion.ArrivalTime; t += 0.01)
                    Assert.LessOrEqual(Math.Abs(leg.Motion.VelocityAt(t)), P.MaxSpeed + 1e-9);
        }

        [Test]
        public void BatterRunsThroughFirstAndReturnsProtected()
        {
            LivePlay play = Play(CenterFieldSingle(), 0, BaseOccupancy.Empty);   // a single: he does not go on to second
            LiveRunner batter = play.RunnerOf(Runner.Batter);
            double touch = TouchTime(play, Runner.Batter, Base.First);
            double past = 0.0;
            for (double t = touch; t < touch + 3.0; t += 0.01)
                past = Math.Max(past, batter.DistanceAlongAt(t) - BaseLeg.Of(Base.Home, true).Length);
            Assert.That(past / 0.3048, Is.InRange(8.0, 25.0), "overruns first by a few strides (15–25 ft reported; less after slowing to round)");
            Assert.AreEqual(Base.First, batter.LastTouched);
            Assert.AreEqual(BaseOccupancy.Empty.Equals(play.ResultingBases()), false);
            Assert.IsTrue(play.ResultingBases().First, "safe at first");
            Assert.IsFalse(batter.IsOut, "never tagged while returning");
            // He ends up back on the bag.
            Vector3d end = batter.PositionAt(play.EndTime + 10.0);
            Assert.Less((end - FieldLayout.BasePosition(Base.First)).Length, 0.05);
        }

        // ---------------------------------------------------------------- decisions

        [Test]
        public void ForcedRunnersAdvanceAndUnforcedRunnersHold()
        {
            // Bases loaded: everybody is forced and runs.
            LivePlay loaded = Play(LeftSideGrounder(), 1, BaseOccupancy.Loaded);
            foreach (Runner r in new[] { new Runner(Base.First), new Runner(Base.Third) })
                Assert.IsTrue(loaded.Log.Any(e => e.Kind == PlayLogKind.BaseTouch && e.Runner == r && e.At == r.Next), $"{r} advanced");
            // Runner on second alone, grounder to the left side (3B): not forced, the ball is in front of him — he holds.
            LivePlay held = Play(LeftSideGrounder(), 0, new BaseOccupancy(false, true, false));
            LiveRunner second = held.RunnerOf(new Runner(Base.Second));
            Assert.IsFalse(held.Log.Any(e => e.Kind == PlayLogKind.BaseTouch && e.Runner == second.Id && e.At == Base.Third), "did not run into the play");
            Assert.AreEqual(Base.Second, second.LastTouched);
            Assert.IsTrue(held.ResultingBases().Second);
            Assert.Less((second.PositionAt(held.EndTime) - FieldLayout.BasePosition(Base.Second)).Length, 0.05, "back on his base");
        }

        [Test]
        public void RunnerOnThirdTagsUpOnADeepFlyAndScores()
        {
            FieldingPlay fly = DeepCenterFly();
            Assert.AreEqual(InterceptKind.FlyCatch, fly.Intercept.Kind);
            LivePlay play = Play(fly, 1, new BaseOccupancy(false, false, true));
            var r3 = new Runner(Base.Third);
            LiveRunner runner = play.RunnerOf(r3);
            // On his base when the ball is caught (he went back from his lead), and not before the catch does he leave it.
            Assert.Less((runner.PositionAt(fly.PossessionTime) - FieldLayout.BasePosition(Base.Third)).Length, 0.05, "on the bag at the catch");
            Assert.Less(runner.SpeedAt(fly.PossessionTime - 0.01), 1e-6, "waiting");
            Assert.Greater(runner.SpeedAt(fly.PossessionTime + 0.2), 0.5, "leaves at the catch (first touch)");
            Assert.IsTrue(runner.HasScored);
            Assert.AreEqual(1, play.Runs);
            Assert.AreEqual(2, play.Outs, "the fly out");
            double run = runner.ScoreTime - fly.PossessionTime;
            Assert.That(run, Is.InRange(3.6, 4.3), "third to home from a standing start ≈ 3.7–4.1 s (DERIVED)");
        }

        [Test]
        public void RunnersOffTheBaseOnACatchGoBackAndRetouch()
        {
            // Runner on first goes halfway on a fly (fewer than two out), then has to get back when it is caught.
            FieldingPlay fly = DeepCenterFly();
            LivePlay play = Play(fly, 0, new BaseOccupancy(true, false, false));
            LiveRunner r1 = play.RunnerOf(new Runner(Base.First));
            Assert.Greater(r1.DistanceAlongAt(fly.PossessionTime - 0.1), LivePlay.Lead(Base.First) + 1.0, "off the bag (halfway) during the fly");
            Assert.IsTrue(play.Log.Any(e => e.Kind == PlayLogKind.BaseTouch && e.Runner == r1.Id && e.At == Base.First && e.Time > fly.PossessionTime), "retouched first after the catch");
            Assert.IsFalse(r1.MustRetouch);
            Assert.IsTrue(play.ResultingBases().First);
            // With two out he runs on contact and the catch ends the inning.
            LivePlay twoOut = Play(fly, 2, new BaseOccupancy(true, false, false));
            Assert.AreEqual(3, twoOut.Outs);
            Assert.Greater(twoOut.RunnerOf(new Runner(Base.First)).DistanceAlongAt(fly.PossessionTime), 15.0, "running at the crack of the bat");
            Assert.AreEqual(BaseOccupancy.Empty, twoOut.ResultingBases(), "three out: bases cleared");
        }

        [Test]
        public void ExtraBaseDecisionDependsOnTheMargin()
        {
            // Runner on first, a ball off the left-field wall: the average runner keeps going and scores; the same play with a
            // slow runner stops earlier — the decision is the margin, not a scripted outcome. Neither is put out.
            FieldingPlay wall = Field(105.0, 14.0, -20.0, 1800.0);
            LivePlay normal = Play(wall, 0, new BaseOccupancy(true, false, false));
            var slow = new RunnerProfile(0.75 * P.MaxSpeed, P.AccelerationTime, P.BrakeDeceleration, 0.8, P.BatterStartDelay, P.ReadDelay);
            LivePlay slowPlay = Play(wall, 0, new BaseOccupancy(true, false, false), slow);
            LiveRunner n = normal.RunnerOf(new Runner(Base.First)), sl = slowPlay.RunnerOf(new Runner(Base.First));
            Assert.IsTrue(n.HasScored, "scores from first on a ball off the wall");
            Assert.IsFalse(sl.HasScored, "a slow runner holds up");
            Assert.IsFalse(sl.IsOut, "and is not thrown out");
            Assert.That(sl.LastTouched, Is.EqualTo(Base.Third).Or.EqualTo(Base.Second));
            Assert.IsTrue(normal.Log.Any(e => e.Kind == PlayLogKind.Decision && e.Runner == n.Id && e.Text.Contains("rounds 3B")), "he rounded third on his own decision");
        }

        [Test]
        public void RunnersStayInOrder()
        {
            foreach (var (f, bases, outs) in new[] { (CenterFieldSingle(), BaseOccupancy.Loaded, 0), (SsGrounder(), BaseOccupancy.Loaded, 1), (DeepCenterFly(), BaseOccupancy.Loaded, 2) })
            {
                LivePlay play = Play(f, outs, bases);
                for (double t = play.ContactTime; t <= play.EndTime; t += 0.02)
                {
                    var on = play.Runners.Where(r => !r.IsDone || r.OutTime > t && r.ScoreTime > t).OrderBy(r => r.Id.From).ToList();
                    for (int i = 1; i < on.Count; i++)
                        Assert.Less(Progress(on[i - 1], t), Progress(on[i], t) + 1e-9, $"{on[i - 1].Id} passed {on[i].Id} at +{t - play.ContactTime:0.00}");
                }
            }

            double Progress(LiveRunner r, double t) => (int)r.LegAt(t).From * 100.0 + r.DistanceAlongAt(t);
        }

        [Test]
        public void ForceOutNeedsTheDefenseFirstByMoreThanAMillisecond()
        {
            // Runner on first, routine grounder to short: the force at second. Find the runner speed at which he just beats
            // it; at the boundary his touch and the force differ by the 1 ms simultaneous window, and a tie is safe.
            FieldingPlay f = SsGrounder();
            var on1 = new BaseOccupancy(true, false, false);
            // The defense's choice is pinned to the throw to second (a faster runner would otherwise change it).
            DefensiveAction AtSecond(IReadOnlyList<DefensiveAction> cs) => cs.First(a => a.Kind == DefensiveActionKind.ThrowToBase && a.Target == Base.Second);
            LivePlay Run(double speed)
            {
                var p = new RunnerProfile(speed, P.AccelerationTime, P.BrakeDeceleration, 0.8, P.BatterStartDelay, P.ReadDelay);
                var play = new LivePlay(f, new Situation(0, on1), p, AtSecond);
                play.RunToEnd();
                return play;
            }

            bool Out(double speed) => Run(speed).RulesEvents.Any(e => e.Runner == new Runner(Base.First) && e.Kind == PlayEventKind.ForceOut);

            Assert.IsTrue(Out(P.MaxSpeed), "the average runner is out by a lot");
            Assert.IsFalse(Out(3.0 * P.MaxSpeed), "an impossibly fast one is safe");
            double lo = P.MaxSpeed, hi = 3.0 * P.MaxSpeed;   // out at lo, safe at hi
            for (int i = 0; i < 40; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Out(mid)) lo = mid;
                else hi = mid;
            }

            LivePlay boundary = Run(hi);
            Assert.AreEqual(Base.Second, boundary.Defense.Throw.Target);
            double catchAt = boundary.Defense.Throw.Catch.Time;
            LiveRunner r1 = boundary.RunnerOf(new Runner(Base.First));
            // His foot reaches the bag (touch distance) within 1 ms after the receiver has the ball on it: simultaneous, safe.
            double footOn = Enumerable.Range(0, 200000).Select(i => catchAt - 0.05 + i * 1e-6).First(t => r1.TouchingBaseAt(t, out Base b) && b == Base.Second);
            Assert.That(footOn - catchAt, Is.InRange(0.0, TimingCall.Simultaneous + 2e-6));
        }

        // ---------------------------------------------------------------- determinism and state

        [Test]
        public void SameResultAtAnyFrameSchedule()
        {
            foreach (var (f, bases, outs) in new[] { (CenterFieldSingle(), new BaseOccupancy(true, false, true), 1), (SsGrounder(), BaseOccupancy.Loaded, 0), (DeepCenterFly(), new BaseOccupancy(false, false, true), 1) })
            {
                LivePlay reference = Play(f, outs, bases);
                foreach (double step in new[] { 1.0 / 30.0, 1.0 / 60.0, 1.0 / 144.0, -1.0 })
                {
                    var play = new LivePlay(f, new Situation(outs, bases));
                    double t = play.ContactTime;
                    int k = 0;
                    while (!play.IsOver && t < play.ContactTime + 60.0)
                    {
                        // A jittered schedule for step −1: 4–40 ms frames from a fixed sequence.
                        t += step > 0.0 ? step : 0.004 + 0.036 * ((k++ * 7919) % 97) / 97.0;
                        play.AdvanceTo(t);
                    }

                    Assert.AreEqual(reference.Log.Count, play.Log.Count, $"log length at {step}");
                    for (int i = 0; i < reference.Log.Count; i++)
                    {
                        Assert.AreEqual(reference.Log[i].Text, play.Log[i].Text);
                        Assert.AreEqual(reference.Log[i].Time, play.Log[i].Time, 1e-9, reference.Log[i].Text);
                    }

                    Assert.AreEqual(reference.EndTime, play.EndTime, 1e-9);
                    foreach (LiveRunner r in reference.Runners)
                        for (double s = reference.ContactTime; s < reference.EndTime; s += 0.1)
                            Assert.Less((r.PositionAt(s) - play.RunnerOf(r.Id).PositionAt(s)).Length, 1e-9);
                }
            }
        }

        [Test]
        public void ResultingSituationStartsTheNextPlateAppearance()
        {
            LivePlay first = Play(CenterFieldSingle(), 0, new BaseOccupancy(true, false, false));
            BaseOccupancy after = first.ResultingBases();
            Assert.IsTrue(after.First, "the batter-runner is on first");
            var next = new LivePlay(SsGrounder(), new Situation(first.Outs, after));
            // Every runner of the new play starts on (his lead off) the base he ended the last play on; nobody else.
            foreach (Runner r in after.Runners)
                Assert.Less((next.RunnerOf(r).PositionAt(next.ContactTime) - BaseLeg.Of(r.From, false).PositionAt(LivePlay.Lead(r.From))).Length, 1e-9);
            Assert.AreEqual(after.Runners.Count() + 1, next.Runners.Count, "plus the new batter-runner");
            next.RunToEnd();
            Assert.AreEqual(first.Outs + next.OutsMade, next.Outs);
        }
    }
}
