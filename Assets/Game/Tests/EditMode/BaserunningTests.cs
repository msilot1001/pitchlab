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

        /// <summary>The same batted ball fielded from the situation's alignment (double-play depth with a runner on first).</summary>
        private static FieldingPlay In(FieldingPlay f, Situation s) =>
            s.Bases.First && s.Outs < 2 ? FieldingSolver.Solve(f.Ball, s.Alignment, FielderProfile.For, FieldLayout.Standard) : f;

        private static LivePlay Play(FieldingPlay f, int outs, BaseOccupancy bases, RunnerProfile? profile = null)
        {
            var situation = new Situation(outs, bases);
            var play = new LivePlay(In(f, situation), situation, profile);
            play.RunToEnd();
            AssertEndedForAReason(play);
            return play;
        }

        /// <summary>A play ends on its own (third out, ball held with everyone on a base, dead ball) — never on the safety limit.</summary>
        private static void AssertEndedForAReason(LivePlay play)
        {
            Assert.IsFalse(play.Log.Any(e => e.Kind == PlayLogKind.PlayOver && e.Text.Contains("time limit")), "the play ended by itself");
            Assert.Less(play.EndTime, play.ContactTime + 30.0);
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
                    // It drifts outward (away from the diamond) by the banana width at its widest.
                    Vector3d centre = 0.5 * (FieldLayout.BasePosition(Base.Home) + FieldLayout.BasePosition(Base.Second));
                    double widest = 0.0;
                    for (double u = 0.0; u <= 1.0; u += 0.005)
                    {
                        Vector3d p = leg.PositionAt(u * leg.Length), chordPoint = leg.Start + Vector3d.Dot(p - leg.Start, (leg.End - leg.Start) / chord) * (leg.End - leg.Start) / chord;
                        double off = (p - chordPoint).Length * Math.Sign(Vector3d.Dot(p - chordPoint, chordPoint - centre));
                        widest = Math.Max(widest, off);
                        Assert.GreaterOrEqual(off, -1e-9, $"{from}: never inside the line");
                    }

                    Assert.AreEqual(BaseLeg.BananaWidth, widest, 0.02, $"{from} drifts out by the banana width");
                    // No kink where the drift begins: the heading changes smoothly (consecutive 5 cm steps turn < 1°).
                    Vector3d previous = leg.DirectionAt(0.0);
                    for (double dd = 0.05; dd < leg.Length - 0.05; dd += 0.05)
                    {
                        Vector3d dir = leg.DirectionAt(dd);
                        Assert.Greater(Vector3d.Dot(dir, previous), Math.Cos(Math.PI / 180.0), $"{from}: heading jump at {dd:0.00} m");
                        previous = dir;
                    }

                    // It crosses the bag turned toward the next base (the cut-back), so the turn at the bag is well under 90°.
                    double inward = Math.Acos(Math.Min(1.0, Vector3d.Dot(leg.EndDirection, (leg.End - leg.Start) / chord))) * 180.0 / Math.PI;
                    Assert.That(inward, Is.InRange(20.0, 45.0), "cut-back angle at the bag");
                }
            }
        }

        [Test]
        public void HomeToFirstMatchesTheMlbAverage()
        {
            // Batter-runner running out a slow grounder (the force is made at second, the double play fails, so he reaches
            // first): contact → first base 4.28 s on average (Statcast; RHB 4.30, LHB 4.26).
            LivePlay play = Play(Field(70.0, -10.0, -15.0, -900.0), 0, new BaseOccupancy(true, false, false));
            double t = TouchTime(play, Runner.Batter, Base.First) - play.ContactTime;
            Assert.AreEqual(4.28, t, 0.02, "contact → foot on first");
        }

        [Test]
        public void PredictionEqualsExecution()
        {
            // The planner's arrival at the start of a run is the moment the executed run touches the base (same law).
            LivePlay play = Play(LeftSideGrounder(), 1, BaseOccupancy.Loaded);
            double contact = play.ContactTime;
            foreach (Runner r in new[] { Runner.Batter, new Runner(Base.First), new Runner(Base.Second) })
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
            Assert.Less(toThird, plan[2].Motion.ArrivalTime, "the foot reaches the bag before the body stops on its centre");
            Assert.AreEqual(plan[2].Motion.TimeAt(plan[2].Leg.Length - LiveRunner.TouchDistance), toThird, 0.0);
            Assert.AreEqual(plan[0].Motion.ArrivalTime, plan[1].Motion.StartTime, 0.0, "legs chain without a gap");
            Assert.AreEqual(plan[0].Motion.EndSpeed, plan[1].Motion.V0, 1e-12, "and without a speed jump");
            Assert.That(toThird - contact, Is.InRange(11.0, 12.5), "home to third ≈ 11.5–11.8 s for an average runner (DERIVED from Statcast records)");

            // Re-planned mid-run: in the wall-ball play the runner from first decides at third to go home; the arrival
            // predicted from his state at that instant is the executed touch of the plate.
            LivePlay wall = Play(Field(105.0, 14.0, -20.0, 1800.0), 0, new BaseOccupancy(true, false, false));
            var r1 = new Runner(Base.First);
            LiveRunner runner = wall.RunnerOf(r1);
            double decided = wall.Log.First(e => e.Kind == PlayLogKind.Decision && e.Runner == r1 && e.Text.Contains("rounds 2B")).Time;
            double predictedThird = RunnerPlanner.ArrivalTime(P, runner.LegAt(decided), runner.DistanceAlongAt(decided), runner.PathVelocityAt(decided), decided, Base.Third, false, bananaAll: true);
            Assert.AreEqual(predictedThird, TouchTime(wall, r1, Base.Third), 1e-6, "prediction at the decision = execution");
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
            // A quick batter-runner beats the shortstop's throw to first: he overruns the bag and walks back past the first
            // baseman, who stands on it holding the ball — protected (OBR 5.09(b)(4) Exception).
            var quick = new RunnerProfile(1.4 * P.MaxSpeed, P.AccelerationTime, P.BrakeDeceleration, 0.8, P.BatterStartDelay, P.ReadDelay);
            var play = new LivePlay(SsGrounder(), new Situation(0, BaseOccupancy.Empty), quick,
                cs => cs.First(a => a.Kind == LiveActionKind.Throw && a.Target == Base.First));
            play.RunToEnd();
            AssertEndedForAReason(play);
            LiveRunner batter = play.RunnerOf(Runner.Batter);
            Assert.Less(TouchTime(play, Runner.Batter, Base.First), play.Defense.Throws[0].Catch.Time, "he beat the throw");
            double touch = TouchTime(play, Runner.Batter, Base.First);
            double past = 0.0, closest = double.PositiveInfinity;
            for (double t = touch; t < touch + 6.0; t += 0.01)
            {
                past = Math.Max(past, batter.DistanceAlongAt(t) - BaseLeg.Of(Base.Home, false).Length);
                if (play.Defense.HolderAt(t) == DefensivePosition.FirstBase && t > touch + 0.5)
                    closest = Math.Min(closest, (play.Defense.FielderPositionAt(DefensivePosition.FirstBase, t) - batter.PositionAt(t)).Length);
            }

            Assert.That(past / 0.3048, Is.InRange(15.0, 40.0), "overruns first (15–25 ft reported for an average runner; v²/2a ≈ 36 ft for this faster one)");
            Assert.Less(closest, TagRules.Reach, "the first baseman with the ball is within tag reach as he walks back");
            // The ball is live until he is back on the bag: the protection, not the end of the play, keeps him safe.
            bool liveInReach = false;
            for (double t = touch + 0.5; t < play.EndTime; t += 0.005)
                if (play.Defense.HolderAt(t) == DefensivePosition.FirstBase && !batter.TouchingBaseAt(t, out _)
                    && (play.Defense.FielderPositionAt(DefensivePosition.FirstBase, t) - batter.PositionAt(t)).Length < TagRules.Reach)
                    liveInReach = true;
            Assert.IsTrue(liveInReach, "in reach, off the bag, while the play is live");
            Assert.IsFalse(batter.IsOut, "never tagged while returning from the overrun");
            Assert.IsTrue(play.ResultingBases().First, "safe at first");
            Assert.Less((batter.PositionAt(play.EndTime) - FieldLayout.BasePosition(Base.First)).Length, 0.05, "the play ends with him back on the bag");
        }

        // ---------------------------------------------------------------- decisions

        [Test]
        public void ForcedRunnersAdvanceAndUnforcedRunnersHold()
        {
            // Bases loaded: everybody is forced and runs (the runner from third into the force at home).
            LivePlay loaded = Play(LeftSideGrounder(), 1, BaseOccupancy.Loaded);
            foreach (Runner r in new[] { new Runner(Base.First), new Runner(Base.Second) })
                Assert.IsTrue(loaded.Log.Any(e => e.Kind == PlayLogKind.BaseTouch && e.Runner == r && e.At == r.Next), $"{r} advanced");
            var r3 = new Runner(Base.Third);
            Assert.IsTrue(loaded.RulesEvents.Any(e => e.Kind == PlayEventKind.ForceOut && e.Runner == r3 && e.At == Base.Home), "forced out at home");
            Assert.Greater(loaded.RunnerOf(r3).DistanceAlongAt(loaded.RulesEvents.First(e => e.Runner == r3).Time), 10.0, "running home");
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
            // Runner on first, a ball off the left-field wall: the average runner goes first to third; the same play with a slow
            // runner stops at second — the decision is the margin, not a scripted outcome. Neither is put out.
            FieldingPlay wall = Field(105.0, 14.0, -20.0, 1800.0);
            LivePlay normal = Play(wall, 0, new BaseOccupancy(true, false, false));
            var slow = new RunnerProfile(0.75 * P.MaxSpeed, P.AccelerationTime, P.BrakeDeceleration, 0.8, P.BatterStartDelay, P.ReadDelay);
            LivePlay slowPlay = Play(wall, 0, new BaseOccupancy(true, false, false), slow);
            LiveRunner n = normal.RunnerOf(new Runner(Base.First)), sl = slowPlay.RunnerOf(new Runner(Base.First));
            Assert.AreEqual(Base.Third, n.LastTouched, "first to third on a ball off the wall");
            Assert.AreEqual(Base.Second, sl.LastTouched, "a slow runner holds up at second");
            Assert.IsFalse(n.IsOut || sl.IsOut, "nobody is thrown out");
            Assert.IsTrue(normal.Log.Any(e => e.Kind == PlayLogKind.Decision && e.Runner == n.Id && e.Text.Contains("rounds 2B")), "he rounded second on his own decision");
            // On a hit every leg is the banana route from its start (he may round any base; the geometry of a leg cannot
            // change once he is on it): the leg he rounded second and third on curves out.
            foreach (Base b in new[] { Base.Second, Base.Third })
                Assert.IsTrue(n.LegAt(TouchTime(normal, n.Id, b) - 0.01).Banana, $"banana into {b}");
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
            LiveAction AtSecond(IReadOnlyList<LiveAction> cs) => cs.First(a => a.Kind == LiveActionKind.Throw && a.Target == Base.Second);
            LivePlay Run(double speed)
            {
                var p = new RunnerProfile(speed, P.AccelerationTime, P.BrakeDeceleration, 0.8, P.BatterStartDelay, P.ReadDelay);
                var play = new LivePlay(In(f, new Situation(0, on1)), new Situation(0, on1), p, AtSecond);
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
            Assert.AreEqual(Base.Second, boundary.Defense.Throws[0].Target);
            double catchAt = boundary.Defense.Throws[0].Catch.Time;
            LiveRunner r1 = boundary.RunnerOf(new Runner(Base.First));
            // His foot reaches the bag (touch distance) within 1 ms after the receiver has the ball on it: simultaneous, safe.
            Assert.That(TouchTime(boundary, r1.Id, Base.Second) - catchAt, Is.InRange(TimingCall.Simultaneous - 2e-5, TimingCall.Simultaneous + 2e-5),
                "the boundary is the 1 ms window, not a tie");

            // Just slower runners — the defense first by 1.1 to 3 ms — are out every time (not missed between samples). His
            // foot-on-the-bag time is the planner's (equal to execution: PredictionEqualsExecution).
            int outs = 0;
            for (double speed = lo; speed > 0.5 * P.MaxSpeed; speed -= 0.0005)
            {
                LivePlay p = Run(speed);
                var profile = new RunnerProfile(speed, P.AccelerationTime, P.BrakeDeceleration, 0.8, P.BatterStartDelay, P.ReadDelay);
                double footOn = RunnerPlanner.ArrivalTime(profile, BaseLeg.Of(Base.First, false), LivePlay.Lead(Base.First), 0.0, p.ContactTime + profile.ReadDelay, Base.Second, false);
                double gap = footOn - p.Defense.Throws[0].Catch.Time;
                if (gap > 3e-3) break;
                if (gap < 1.1e-3) continue;
                Assert.IsTrue(p.RulesEvents.Any(e => e.Runner == new Runner(Base.First) && e.Kind == PlayEventKind.ForceOut), $"out by {gap * 1e3:0.00} ms");
                outs++;
            }

            Assert.Greater(outs, 3, "the sweep covered the 1.1–3 ms range");
        }

        [Test]
        public void OffTheBaseOnACatchIsDoubledOffWhenTheBaseIsTouchedFirst()
        {
            // The shortstop stands on second base and catches a line drive up the middle (the pitcher moved aside); the runner
            // from second is off at his lead: the defender holding the ball on the bag before the runner retouches it —
            // doubled off (OBR 5.09(b)(5)).
            Vector3d bag = FieldLayout.BasePosition(Base.Second);
            var alignment = new DefensiveAlignment(Enumerable.Range(0, DefensiveAlignment.Count).Select(i =>
                (DefensivePosition)i == DefensivePosition.Shortstop ? bag
                : (DefensivePosition)i == DefensivePosition.P ? new Vector3d(-8.0, 16.0, 0.0)
                : DefensiveAlignment.Standard[(DefensivePosition)i]).ToArray());
            FieldingPlay liner = FieldingSolver.Solve(BallInPlaySimulation.Run(new BattedBallLaunch(85.0, 10.0, 0.0, 0.0).ToState(Contact),
                EnvironmentState.Standard, FieldLayout.Standard), alignment, FielderProfile.For, FieldLayout.Standard);
            Assert.AreEqual(DefensivePosition.Shortstop, liner.Primary);
            Assert.AreEqual(InterceptKind.FlyCatch, liner.Intercept.Kind);
            Assert.IsTrue(BaseTouch.IsTouching(liner.Motion(DefensivePosition.Shortstop).PositionAt(liner.PossessionTime), Base.Second), "caught on the bag");
            LivePlay play = Play(liner, 0, new BaseOccupancy(false, true, false));
            Assert.Less(play.CatchHang, LivePlay.LineDriveHang, "a line drive: the runner froze at his lead");
            PlayEvent doubled = play.RulesEvents.Single(e => e.Runner == new Runner(Base.Second));
            Assert.AreEqual(PlayEventKind.RetouchOut, doubled.Kind);
            Assert.AreEqual(Base.Second, doubled.At);
            Assert.AreEqual(2, play.Outs, "the catch and the double-off");
            Assert.AreEqual(BaseOccupancy.Empty, play.ResultingBases());
        }

        [Test]
        public void FoulBallSendsRunnersBack()
        {
            FieldingPlay foul = Field(85.0, 35.0, 50.0, 2000.0);
            Assert.AreEqual(FieldingOutcome.DeadFoul, foul.Outcome);
            var bases = new BaseOccupancy(true, true, false);
            LivePlay play = Play(foul, 1, bases);
            Assert.IsNull(play.RunnerOf(Runner.Batter), "no batter-runner on a foul");
            Assert.AreEqual(bases, play.ResultingBases());
            Assert.AreEqual(1, play.Outs);
            Assert.AreEqual(0, play.Runs);
            foreach (LiveRunner r in play.Runners)
                Assert.Less((r.PositionAt(play.EndTime) - FieldLayout.BasePosition(r.Id.From)).Length, 0.05, $"{r.Id} back on his base");
        }

        [Test]
        public void SendDecisionGetsBolderWithTwoOut()
        {
            // The wall ball with a runner on first: find the runner speed at which he no longer goes first to third with none
            // out; just below it he is held at second with none out but goes with two out (margin 0.30 s → −0.10 s).
            FieldingPlay wall = Field(105.0, 14.0, -20.0, 1800.0);
            bool Sent(double speed, int outs)
            {
                var p = new RunnerProfile(speed, P.AccelerationTime, P.BrakeDeceleration, 0.8, P.BatterStartDelay, P.ReadDelay);
                LivePlay play = Play(wall, outs, new BaseOccupancy(true, false, false), p);
                return play.Log.Any(e => e.Kind == PlayLogKind.Decision && e.Runner == new Runner(Base.First) && e.Text.Contains("for 3B"));
            }

            double lo = 0.6 * P.MaxSpeed, hi = P.MaxSpeed;   // held at lo, sent at hi (none out)
            Assert.IsTrue(Sent(hi, 0));
            Assert.IsFalse(Sent(lo, 0));
            for (int i = 0; i < 25; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Sent(mid, 0)) hi = mid;
                else lo = mid;
            }

            Assert.IsFalse(Sent(lo, 0), "held with none out");
            Assert.IsTrue(Sent(lo, 2), "sent with two out at the same speed");
        }

        [Test]
        public void ThirdOutRules()
        {
            // OBR 5.08(a): two out, runner on third running on contact; he crosses the plate before a deep fly is caught —
            // the catch is the third out and the run does not count.
            LivePlay fly = Play(DeepCenterFly(), 2, new BaseOccupancy(false, false, true));
            LiveRunner r3 = fly.RunnerOf(new Runner(Base.Third));
            Assert.Less(r3.ScoreTime, fly.Fielding.PossessionTime, "crossed before the catch");
            Assert.AreEqual(3, fly.Outs);
            Assert.AreEqual(0, fly.Runs, "no run on a fly-out third out");
            Assert.IsTrue(fly.Log.Any(e => e.Text.Contains("does not count")));
            // One out: the same runner tags and scores — the run counts.
            Assert.AreEqual(1, Play(DeepCenterFly(), 1, new BaseOccupancy(false, false, true)).Runs);
            // Two out, bases loaded, force at second for the third out before anyone scores: nothing happens after it.
            LivePlay force = Play(SsGrounder(), 2, BaseOccupancy.Loaded);
            Assert.AreEqual(3, force.Outs);
            Assert.AreEqual(0, force.Runs);
            Assert.IsFalse(force.Log.Any(e => e.Time > force.EndTime), "the third out ends the play");
            Assert.AreEqual(BaseOccupancy.Empty, force.ResultingBases());
        }

        [Test]
        public void StopsLandExactlyOnTheBagAndReversingBrakesNormally()
        {
            for (int i = 0; i < 2700; i++)
            {
                double d0 = 0.01 * i, target = 27.43;
                foreach (double decel in new[] { P.BrakeDeceleration, P.SlideDeceleration })
                {
                    var m = new PathMotion(P, 0.0, d0, 0.0, target, 0.0, double.NaN, decel);
                    Assert.AreEqual(target, m.DistanceAt(m.RestTime), 0.0, $"from {d0}");
                    Assert.AreEqual(target, m.DistanceAt(m.RestTime + 5.0), 0.0);
                }
            }

            // Asked to stop where he can no longer brake in time: he brakes from now, passes the target at the speed he still
            // has and rests beyond it — continuously, never snapped back onto it.
            var late = new PathMotion(P, 0.0, 9.0, 8.0, 10.0, 0.0);
            Assert.Greater(late.EndSpeed, 0.0);
            Assert.AreEqual(9.0 + 8.0 * 8.0 / (2.0 * P.BrakeDeceleration), late.DistanceAt(late.RestTime + 1.0), 1e-9);
            for (double t = 0.0; t < late.RestTime + 0.5; t += 0.001)
            {
                Assert.GreaterOrEqual(late.DistanceAt(t + 0.001), late.DistanceAt(t) - 1e-12, "never moves back");
                Assert.Less(late.DistanceAt(t + 0.001) - late.DistanceAt(t), 0.009, "no jump");
            }

            Assert.AreEqual(10.0, late.DistanceAt(late.ArrivalTime), 1e-9, "arrival is when he passes the target");

            // Told to go back while running full speed the other way: he first brakes at the braking deceleration.
            var back = new PathMotion(P, 0.0, 10.0, P.MaxSpeed, 0.0, 0.0);
            double worst = 0.0;
            for (double t = 0.0; t < back.RestTime; t += 0.001)
                worst = Math.Max(worst, Math.Abs(back.VelocityAt(t + 0.001) - back.VelocityAt(t)) / 0.001);
            Assert.LessOrEqual(worst, Math.Max(P.BrakeDeceleration, P.MaxSpeed / P.AccelerationTime) + 0.1, "no sharper than braking or the start");
            Assert.AreEqual(P.BrakeDeceleration, (back.VelocityAt(0.0) - back.VelocityAt(0.01)) / 0.01, 1e-6, "brakes first");
            Assert.AreEqual(0.0, back.DistanceAt(back.RestTime), 0.0, "and stops on the bag behind him");
        }

        // ---------------------------------------------------------------- determinism and state

        [Test]
        public void SameResultAtAnyFrameSchedule()
        {
            foreach (var (f, bases, outs) in new[] { (CenterFieldSingle(), new BaseOccupancy(true, false, true), 1), (SsGrounder(), BaseOccupancy.Loaded, 0), (DeepCenterFly(), new BaseOccupancy(false, false, true), 1),
                         (CenterFieldSingle(), new BaseOccupancy(false, true, false), 2), (Field(100.0, 20.0, -15.0, 1800.0), new BaseOccupancy(true, false, false), 2) })
            {
                LivePlay reference = Play(f, outs, bases);
                foreach (double step in new[] { 1.0 / 30.0, 1.0 / 60.0, 1.0 / 144.0, -1.0 })
                {
                    var play = new LivePlay(In(f, new Situation(outs, bases)), new Situation(outs, bases));
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
                    // The whole defense too: the same throws at the same times, every fielder in the same place.
                    Assert.AreEqual(reference.Defense.Throws.Count, play.Defense.Throws.Count, $"throws at {step}");
                    for (int i = 0; i < reference.Defense.Throws.Count; i++)
                    {
                        Assert.AreEqual(reference.Defense.Throws[i].ReleaseTime, play.Defense.Throws[i].ReleaseTime, 1e-9);
                        Assert.AreEqual(reference.Defense.Throws[i].Catch.Time, play.Defense.Throws[i].Catch.Time, 1e-9);
                    }

                    foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
                        for (double s = reference.ContactTime; s < reference.EndTime; s += 0.1)
                            Assert.Less((reference.Defense.FielderPositionAt(p, s) - play.Defense.FielderPositionAt(p, s)).Length, 1e-9, $"{p}");
                }
            }
        }

        [Test]
        public void ResultingSituationStartsTheNextPlateAppearance()
        {
            LivePlay first = Play(CenterFieldSingle(), 0, new BaseOccupancy(true, false, false));
            BaseOccupancy after = first.ResultingBases();
            Assert.IsTrue(after.First, "the batter-runner is on first");
            var next = new LivePlay(In(SsGrounder(), new Situation(first.Outs, after)), new Situation(first.Outs, after));
            // Every runner of the new play starts on (his lead off) the base he ended the last play on; nobody else.
            foreach (Runner r in after.Runners)
                Assert.Less((next.RunnerOf(r).PositionAt(next.ContactTime) - BaseLeg.Of(r.From, false).PositionAt(LivePlay.Lead(r.From))).Length, 1e-9);
            Assert.AreEqual(after.Runners.Count() + 1, next.Runners.Count, "plus the new batter-runner");
            // Everyone left on base ended the play standing on his bag, one runner per base.
            foreach (LiveRunner r in first.Runners.Where(r => !r.IsDone))
                Assert.Less((r.PositionAt(first.EndTime + 10.0) - FieldLayout.BasePosition(r.LastTouched)).Length, 0.05, $"{r.Id} on {r.LastTouched}");
            Assert.AreEqual(first.Runners.Count(r => !r.IsDone), first.Runners.Where(r => !r.IsDone).Select(r => r.LastTouched).Distinct().Count());
        }
    }
}
