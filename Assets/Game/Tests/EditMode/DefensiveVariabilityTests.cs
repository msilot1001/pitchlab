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
    /// TASK-021: defenders can fail — physically. A failed take leaves a live free ball (missed: on its own path; knocked loose:
    /// a new ball from the glove) that is fielded again; a throw flies off its aim on the throw physics and the receiver reacts
    /// to it. Seeded and deterministic; without a seed the validated perfect defense is unchanged.
    /// </summary>
    public class DefensiveVariabilityTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);
        private static readonly FieldLayout Field = FieldLayout.Standard;

        private static BallInPlay Hit(double mph, double launch, double spray, double backspin) =>
            BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, backspin).ToState(Contact), EnvironmentState.Standard, Field);

        private static PlayPersonnel Crew(int skill) => new PlayPersonnel(FielderProfile.For, ThrowProfile.For, ThrowProfile.Full,
            _ => RunnerProfile.Standard, _ => new FielderSkill(skill, skill, skill));

        // The fielding solve depends on the movement profiles only (skill does not change them): solved once per ball and alignment.
        private static readonly Dictionary<(BallInPlay, bool), FieldingPlay> Solved = new Dictionary<(BallInPlay, bool), FieldingPlay>();

        private static LivePlay Play(BallInPlay ball, PlayPersonnel crew, long? seed, BaseOccupancy? bases = null)
        {
            var situation = new Situation(0, bases ?? BaseOccupancy.Empty);
            bool dp = situation.Bases.First;   // with no outs: double-play depth
            if (!Solved.TryGetValue((ball, dp), out FieldingPlay f)) Solved[(ball, dp)] = f = FieldingSolver.Solve(ball, situation.Alignment, crew.Fielder, Field);
            var play = new LivePlay(f, situation, personnel: crew, executionSeed: seed);
            play.RunToEnd();
            return play;
        }

        /// <summary>A grid of fair balls in play: grounders, liners and flies across the field.</summary>
        private static List<BallInPlay> Grid()
        {
            var balls = new List<BallInPlay>();
            foreach (double spray in new[] { -38.0, -25.0, -12.0, 0.0, 12.0, 25.0, 38.0 })
            foreach ((double mph, double la, double spin) in new[] { (85.0, -8.0, -500.0), (100.0, -4.0, -500.0), (70.0, 5.0, 500.0), (95.0, 18.0, 1800.0), (88.0, 30.0, 2200.0), (78.0, 45.0, 2500.0) })
                balls.Add(Hit(mph, la, spray, spin));
            // Liners an outfielder can reach only diving (found by the fielding solve: DivingCatch).
            foreach ((double mph, double la, double spray) in new[] { (95.0, 12.0, -18.0), (95.0, 12.0, 18.0), (85.0, 16.0, -4.0), (85.0, 16.0, 4.0) })
                balls.Add(Hit(mph, la, spray, 1500.0));
            return balls;
        }

        private static List<BallInPlay> _grid;
        private static List<BallInPlay> Balls => _grid ??= Grid();

        // ------------------------------------------------------------------ the chance of a take

        [Test]
        public void RoutinePlaysAreNearlyCertainAndHardOnesFailMore()
        {
            FielderSkill avg = FielderSkill.Average;
            double Chance(FieldingAction a, double margin = 1.0, double speed = 0.0, double ball = 10.0) =>
                FieldingExecution.Chance(a, margin, speed, ball, 0.0, avg);
            Assert.Less(Chance(FieldingAction.StandingCatch), 0.003, "a routine fly");
            Assert.Less(Chance(FieldingAction.CenteredPickup), 0.005, "a routine grounder");
            Assert.Less(Chance(FieldingAction.StandingCatch), Chance(FieldingAction.RunningCatch, speed: 6.0));
            Assert.Less(Chance(FieldingAction.RunningCatch, speed: 6.0), Chance(FieldingAction.JumpingCatch, speed: 3.0));
            Assert.Less(Chance(FieldingAction.JumpingCatch, speed: 3.0), Chance(FieldingAction.DivingCatch, speed: 6.0));
            Assert.Less(Chance(FieldingAction.CenteredPickup), Chance(FieldingAction.BackhandPickup));
            Assert.Less(Chance(FieldingAction.BackhandPickup, ball: 15.0), Chance(FieldingAction.BackhandPickup, ball: 40.0), "a hard-hit ball is harder");
            Assert.Less(Chance(FieldingAction.ForehandPickup), Chance(FieldingAction.ForehandPickup, margin: 0.05), "rushed");
            Assert.Less(FieldingExecution.Chance(FieldingAction.BackhandPickup, 1.0, 0.0, 30.0, 0.0, new FielderSkill(90, 90, 90)),
                FieldingExecution.Chance(FieldingAction.BackhandPickup, 1.0, 0.0, 30.0, 0.0, new FielderSkill(15, 15, 15)) * 0.3, "skill");
            // Ground balls by Fielding, catches by Catching — not the other way round.
            var fielder = new FielderSkill(90, 10, 50);
            var catcher = new FielderSkill(10, 90, 50);
            Assert.Less(FieldingExecution.Chance(FieldingAction.BackhandPickup, 1.0, 0.0, 30.0, 0.0, fielder), FieldingExecution.Chance(FieldingAction.BackhandPickup, 1.0, 0.0, 30.0, 0.0, catcher));
            Assert.Less(FieldingExecution.Chance(FieldingAction.RunningCatch, 1.0, 6.0, 30.0, 0.0, catcher), FieldingExecution.Chance(FieldingAction.RunningCatch, 1.0, 6.0, 30.0, 0.0, fielder));
        }

        // ------------------------------------------------------------------ whole plays

        [Test]
        public void WithoutASeedTheDefenseIsExactlyAsBefore()
        {
            foreach (BallInPlay ball in Balls.Take(12))
            {
                LivePlay exact = Play(ball, PlayPersonnel.Standard, null);
                Assert.IsEmpty(exact.Misplays);
                LivePlay again = Play(ball, Crew(0), null);   // even the worst crew never misplays without a seed
                Assert.IsEmpty(again.Misplays);
                Assert.AreEqual(Describe(exact), Describe(again));
            }
        }

        [Test]
        public void TheSameSeedGivesTheSamePlay()
        {
            foreach (BallInPlay ball in Balls.Take(15))
                for (long seed = 1; seed <= 4; seed++)
                    Assert.AreEqual(Describe(Play(ball, Crew(20), seed)), Describe(Play(ball, Crew(20), seed)));
        }

        [Test]
        public void SteppingThePlayInAnySizeGivesTheSamePlay()
        {
            // Misplays and their consequences do not depend on how the play is advanced (frames): stepped in uneven sizes or
            // run at once, the same seeded play.
            int misplays = 0;
            foreach (BallInPlay ball in Balls.Take(20))
                for (long seed = 1; seed <= 3; seed++)
                {
                    LivePlay once = Play(ball, Crew(10), seed, new BaseOccupancy(true, false, false));
                    misplays += once.Misplays.Count;
                    var stepped = new LivePlay(once.Fielding, once.Situation, personnel: once.Personnel, executionSeed: seed);
                    double[] steps = { 0.013, 0.21, 0.004, 0.07 };
                    int k = 0;
                    for (double t = stepped.ContactTime; !stepped.IsOver && t < stepped.ContactTime + 120.0; t += steps[k++ % steps.Length]) stepped.AdvanceTo(t);
                    stepped.RunToEnd();
                    Assert.AreEqual(Describe(once), Describe(stepped), $"seed {seed}");
                }

            Assert.Greater(misplays, 0, "the sample includes misplays");
        }

        private static string Describe(LivePlay p) =>
            string.Join("|", p.Log.Select(e => $"{e.Time:R}:{e.Text}")) + string.Join(",", p.Defense.Takes.Select(k => $"{k.Fielder}@{k.Time:R}"));

        /// <summary>Plays of the grid with a poor crew over seeds, until <paramref name="match"/> finds a misplay.</summary>
        private static (LivePlay Play, Misplay Misplay, BallInPlay Ball) Find(Func<Misplay, bool> match, int crew = 5, BaseOccupancy? bases = null)
        {
            for (long seed = 1; seed < 200; seed++)
                foreach (BallInPlay ball in Balls)
                {
                    LivePlay p = Play(ball, Crew(crew), seed, bases);
                    foreach (Misplay m in p.Misplays)
                        if (match(m)) return (p, m, ball);
                }

            Assert.Fail("no such misplay found");
            return default;
        }

        [Test]
        public void ADiveCanFailAndTheBallStaysLive()
        {
            (LivePlay p, Misplay m, BallInPlay ball) = Find(x => x.What.StartsWith("dive missed"));
            // Missed: the ball carries on along its own path, free, and someone fields it later.
            double t = m.Time;
            Assert.AreEqual(BallAuthority.FreeBall, p.Defense.AuthorityAt(t + 0.05));
            Assert.AreEqual(ball.StateAt(t + 0.05).Position, p.Defense.BallPositionAt(t + 0.05), "the same ball, no teleport");
            BallTake retake = p.Defense.Takes.First(k => k.Held && k.Time > t);
            if (retake.Fielder == m.Fielder) Assert.GreaterOrEqual(retake.Time, t + FieldingActions.DiveRecovery, "he gets up first");
            // Not held in the air by anyone: it touched the ground, so it is no catch and no fly out.
            Assert.AreNotEqual(LivePlay.BallKind.Caught, p.Kind, "a missed fly that lands is no longer a catch");
            Assert.IsFalse(p.RulesEvents.Any(e => e.Kind == PlayEventKind.FlyOut), "no fly out");
            Assert.IsTrue(p.Defense.Takes.Any(k => !k.Held && k.Time == t && k.Fielder == m.Fielder), "his dive is shown (an attempt not held)");
        }

        [Test]
        public void ABobbleCostsTimeAndTheBallIsNeverTeleported()
        {
            (LivePlay p, Misplay m, BallInPlay ball) = Find(x => x.What.StartsWith("bobble"));
            double t = m.Time;
            // Continuous through the bobble: the ball leaves the glove from where it was.
            Assert.Less((p.Defense.BallPositionAt(t - 1e-6) - p.Defense.BallPositionAt(t + 1e-6)).Length, 0.01);
            Assert.AreEqual(BallAuthority.FreeBall, p.Defense.AuthorityAt(t + 0.05));
            BallTake retake = p.Defense.Takes.First(k => k.Held && k.Time > t);
            Assert.Greater(retake.Time, t + 0.2, "picking it up again takes time");
            // The knocked-loose ball moves continuously until it is picked up again, where it is (no jump to the glove).
            Vector3d prev = p.Defense.BallPositionAt(t);
            for (double u = t + 0.01; u < retake.Time; u += 0.01)
            {
                Vector3d now = p.Defense.BallPositionAt(u);
                Assert.Less((now - prev).Length, 0.5, $"continuous at {u}");
                prev = now;
            }

            Assert.Less((p.Defense.BallPositionAt(retake.Time - 1e-6) - retake.BallPoint).Length, 0.05, "retaken where the ball is");
            // The same ball without the misplay is held at the bobble time (the clean play is faster).
            LivePlay exact = Play(ball, Crew(5), null);
            Assert.AreEqual(t, exact.Defense.Takes[0].Time, 1e-9);
        }

        [Test]
        public void AMissedGroundBallCarriesOnAlongItsPath()
        {
            (LivePlay p, Misplay m, BallInPlay ball) = Find(x => x.What.StartsWith("missed the ball"), crew: 0);
            Assert.AreEqual(BallAuthority.FreeBall, p.Defense.AuthorityAt(m.Time + 0.05), "free after the miss");
            for (double dt = 0.05; dt < 0.4; dt += 0.05)
                if (p.Defense.AuthorityAt(m.Time + dt) == BallAuthority.FreeBall)
                    Assert.AreEqual(ball.StateAt(m.Time + dt).Position, p.Defense.BallPositionAt(m.Time + dt));
        }

        [Test]
        public void ABadThrowFliesOffItsAimAndTheReceiverReactsToIt()
        {
            (LivePlay p, Misplay m, _) = Find(x => x.What.StartsWith("throw pulled"), crew: 0, bases: new BaseOccupancy(true, false, false));
            // The misplayed throw: the one of this thrower in the air at the misplay (seen as it passes the bag).
            LiveThrow th = p.Defense.Throws.First(x => x.Thrower == m.Fielder && x.ReleaseTime <= m.Time && m.Time <= x.EndTime + 1e-9);
            Assert.Greater(th.AimError.Length, 0.3, "off target");
            // It flies on the throw physics from the hand toward its aim plus its error, not toward the aim.
            Assert.AreEqual(th.ReleasePoint, th.Flight.First.Position);
            var aim = new Vector3d(th.AimPoint.X, th.AimPoint.Y, ThrowPlanner.TargetHeight);
            double Closest(Vector3d target)
            {
                double best = double.PositiveInfinity;
                for (double t = th.ReleaseTime; t <= th.EndTime; t += 0.005) best = Math.Min(best, (th.Flight.StateAt(t).Position - target).Length);
                return best;
            }

            Assert.Less(Closest(aim + th.AimError), Closest(aim), "toward where it was really thrown");
            Assert.AreEqual(BallAuthority.Thrown, p.Defense.AuthorityAt(th.ReleaseTime + 0.05));
            // The receiver left the bag for it — only after the release (he could not know where it would go).
            Assert.IsNotNull(th.ReceiverMotion);
            Assert.GreaterOrEqual(th.SwitchTime, th.ReleaseTime + ThrowPlanner.AdjustReaction - 1e-9);
            // Off the bag: his catch is no force out at that base.
            Assert.IsFalse(p.RulesEvents.Any(e => e.Kind == PlayEventKind.ForceOut && Math.Abs(e.Time - th.Catch.Time) < 1e-6), "no force off the bag");
        }

        [Test]
        public void AnOverthrowIsSeenAtTheBagAndTheBatterTakesSecond()
        {
            // The FieldingLab's routine SS grounder with a poor crew, seed 10: the throw is 2.2 m off and gets past 1B (found in
            // the runtime walkthrough). The runners see it when it passes the bag — not when 1B finally gathers it.
            LivePlay p = Play(Hit(85.0, -8.0, -15.0, -1000.0), Crew(10), 10);   // the FieldingLab preset "SS routine grounder"
            Misplay m = p.Misplays.Single(x => x.Kind == MisplayKind.ThrowingMisplay);
            StringAssert.StartsWith("throw got past", m.What);
            LiveThrow th = p.Defense.Throws.Single();
            Assert.Less(m.Time, th.Catch.Time - 1.0, "seen at the bag, long before he gathers it");
            Assert.IsTrue(p.Log.Any(e => e.Text.Contains("batter-runner touches 2B")), "the batter-runner takes second on it");
        }

        [Test]
        public void AnInfieldFlyWithTheRuleInEffectIsNeverDropped()
        {
            // Runners on first and second, nobody out: an infielder's fly is an out by rule (a drop is not modelled).
            var bases = new BaseOccupancy(true, true, false);
            int infieldFlies = 0;
            foreach (BallInPlay ball in Balls)
                for (long seed = 1; seed <= 20; seed++)
                {
                    LivePlay p = Play(ball, Crew(0), seed, bases);
                    if (p.Fielding.Primary == null || p.Fielding.Intercept.Kind != InterceptKind.FlyCatch || DefensiveDecision.IsOutfielder(p.Fielding.Primary.Value)) break;
                    infieldFlies++;
                    Assert.IsFalse(p.Misplays.Any(x => x.Fielder == p.Fielding.Primary.Value && x.Time <= p.Fielding.PossessionTime + 1e-9), "held");
                    Assert.IsTrue(p.RulesEvents.Any(e => e.Kind == PlayEventKind.FlyOut));
                }

            Assert.Greater(infieldFlies, 0, "the grid has infield flies");
        }

        [Test]
        public void TheDefenseDoesNotKnowItsErrorsBeforeTheyHappen()
        {
            // Everything before the first misplay — the decisions, the throws chosen, the runners — is the same as in the play
            // with no execution errors at all: nobody planned around the coming error.
            int checkedPlays = 0;
            for (long seed = 1; seed <= 40 && checkedPlays < 8; seed++)
                foreach (BallInPlay ball in Balls)
                {
                    LivePlay p = Play(ball, Crew(5), seed, new BaseOccupancy(true, false, false));
                    if (p.Misplays.Count == 0) continue;
                    double first = p.Misplays[0].Time;
                    // Throws are released with their errors (not misplays unless held badly): compare up to the first release too.
                    foreach (LiveThrow x in p.Defense.Throws) first = Math.Min(first, x.ReleaseTime);
                    LivePlay exact = Play(ball, Crew(5), null, new BaseOccupancy(true, false, false));
                    string Before(LivePlay q) => string.Join("|", q.Log.Where(e => e.Time < first - 1e-9).Select(e => $"{e.Time:R}:{e.Text}"));
                    Assert.AreEqual(Before(exact), Before(p), $"seed {seed}");
                    checkedPlays++;
                }

            Assert.GreaterOrEqual(checkedPlays, 8);
        }

        [Test]
        public void AMissedFlyIsStillACatchUntilItLands()
        {
            // OBR: a fly is caught by any fielder before it touches the ground. After a miss the ball stays a catchable fly until
            // its first contact (a backup holding it then makes the out — rare: in 18,400 seeded plays no backup got there), and
            // only then becomes a hit ball. Stepped in time, as the play unfolds.
            (LivePlay done, Misplay m, BallInPlay ball) = Find(x => x.What.StartsWith("dive missed") || x.What.StartsWith("missed the catch (") && !x.What.Contains("receive"));
            double landing = ball.Events.First(e => e.Time > m.Time && (e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.WallImpact)).Time;
            Assume.That(landing > m.Time + 0.01, "the ball is still in the air after the miss");
            var situation = new Situation(0, BaseOccupancy.Empty);
            var stepped = new LivePlay(done.Fielding, situation, personnel: done.Personnel, executionSeed: done.ExecutionSeed);
            stepped.AdvanceTo(0.5 * (m.Time + landing));
            Assert.AreEqual(LivePlay.BallKind.Caught, stepped.Kind, "between the miss and the landing: still a catchable fly");
            stepped.AdvanceTo(landing + 0.05);
            Assert.AreNotEqual(LivePlay.BallKind.Caught, stepped.Kind, "on the ground: a hit ball");
        }

        [Test]
        public void ABallOutOfPlayAfterAMisplayAwardsTwoBases()
        {
            // Rare in this park (no misplayed ball left the field in thousands of seeded plays), so the dead-ball award is driven
            // directly: a grounder through the infield with a runner on first, the ball going out mid-play.
            LivePlay p = Play(Hit(100.0, -4.0, 12.0, -500.0), PlayPersonnel.Standard, null, new BaseOccupancy(true, false, false));
            var live = new LivePlay(p.Fielding, p.Situation, personnel: p.Personnel);
            double t = live.ContactTime + 1.0;   // both runners on their way: the batter-runner short of first
            live.AdvanceTo(t);
            typeof(LivePlay).GetMethod("OnBallOutOfPlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(live, new object[] { t, 2 });
            live.RunToEnd();
            Assert.IsTrue(live.IsOver);
            Assert.IsFalse(live.RulesEvents.Any(e => e.IsOut), "a dead ball: no outs");
            // Two bases from the last base touched: the batter (home) to second, the runner (first) to third.
            Assert.AreEqual(new BaseOccupancy(false, true, true), live.ResultingBases());
            Assert.AreEqual(0, live.Runs);
            // Not a ground-rule double (a fair ball bouncing over the fence): the batter's result is where he ended — second.
            Assert.AreEqual(PlayResultKind.Double, PlayResults.Classify(live));

            // Over the fence off a glove: a home run — classified so, everyone scores.
            var homer = new LivePlay(p.Fielding, p.Situation, personnel: p.Personnel);
            homer.AdvanceTo(t);
            typeof(LivePlay).GetMethod("OnBallOutOfPlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(homer, new object[] { t, 4 });
            homer.RunToEnd();
            Assert.AreEqual(PlayResultKind.HomeRun, PlayResults.Classify(homer));
            Assert.AreEqual(2, homer.Runs);
            Assert.AreEqual(BaseOccupancy.Empty, homer.ResultingBases());
        }

        [Test]
        public void ARunnerOverrunningFirstStillGetsTheAward()
        {
            // The ball goes out while the batter-runner overruns first: he walks back, then takes his two bases — third.
            BallInPlay ball = Hit(95.0, -10.0, 24.0, -900.0);   // the FieldingLab's "1B ranges right: safe at 1B"
            LivePlay probe = Play(ball, PlayPersonnel.Standard, null);
            double touch = probe.Log.First(e => e.Text.Contains("batter-runner touches 1B")).Time;
            var live = new LivePlay(probe.Fielding, probe.Situation, personnel: probe.Personnel);
            double t = touch + 0.15;
            live.AdvanceTo(t);
            Assert.AreEqual(RunnerPhase.Overrunning, live.RunnerOf(Runner.Batter).Phase, "overrunning when the ball goes out");
            typeof(LivePlay).GetMethod("OnBallOutOfPlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(live, new object[] { t, 2 });
            live.RunToEnd();
            Assert.AreEqual(new BaseOccupancy(false, false, true), live.ResultingBases());
        }

        [Test]
        public void SimulatedGamesLabelTheirMisplays()
        {
            var g = new GameState(Gameplay.Players.GenericRosters.Away(), Gameplay.Players.GenericRosters.Home(), 7);
            var sim = new GameSimulator(g);
            sim.PlayToEnd();
            int labelled = g.Completed.Count(pa => pa.Result.Contains("misplay"));
            Assert.LessOrEqual(labelled, sim.Misplays.Count);
            if (sim.Misplays.Count > 0) Assert.Greater(labelled + sim.Misplays.Count(m => m.Kind == MisplayKind.ThrowingMisplay), 0);
            // A rate guard on whole games (regression guard; MLB ≈ 0.5 errors per team-game): misplays are rare, not chaos.
            int total = 0;
            for (int seed = 31; seed <= 36; seed++)
            {
                var game = new GameState(Gameplay.Players.GenericRosters.Away(), Gameplay.Players.GenericRosters.Home(), seed);
                var s = new GameSimulator(game);
                s.PlayToEnd();
                total += s.Misplays.Count;
            }

            Assert.That(total / 12.0, Is.InRange(0.1, 1.5), "misplays per team-game");
        }

        [Test]
        public void ThereIsNeverMoreThanOneBallHolderAndTakesAreOrdered()
        {
            int misplays = 0;
            for (long seed = 1; seed <= 3; seed++)
                foreach (BallInPlay ball in Balls)
                {
                    LivePlay p = Play(ball, Crew(5), seed, new BaseOccupancy(true, false, false));
                    misplays += p.Misplays.Count;
                    double last = double.NegativeInfinity;
                    foreach (BallTake k in p.Defense.Takes.Where(x => x.Held))
                    {
                        Assert.Greater(k.Time, last, "one take at a time");
                        last = k.Time;
                        Assert.AreEqual(k.Fielder, p.Defense.HolderAt(k.Time + 1e-6), "the taker holds it");
                    }

                    for (double t = p.ContactTime; t < p.EndTime; t += 0.05)
                    {
                        BallAuthority a = p.Defense.AuthorityAt(t);
                        Assert.AreEqual(a == BallAuthority.Possessed, p.Defense.HolderAt(t) != null, $"authority and holder agree at {t}");
                    }
                }

            Assert.Greater(misplays, 5, "the grid exercised misplays");
        }

        // ------------------------------------------------------------------ populations

        [Test]
        public void BetterDefendersMisplayLess()
        {
            // The same grid and seeds for elite, average and poor crews (regression guard on the ordering, not validation).
            int Misplays(int skill)
            {
                int n = 0;
                for (long seed = 1; seed <= 8; seed++)
                    foreach (BallInPlay ball in Balls)
                        n += Play(ball, Crew(skill), seed, new BaseOccupancy(true, false, false)).Misplays.Count;
                return n;
            }

            int elite = Misplays(90), average = Misplays(50), poor = Misplays(10);
            Assert.Less(elite, average, $"elite {elite} < average {average}");
            Assert.Less(average, poor, $"average {average} < poor {poor}");
            Assert.Greater(poor, elite * 2, "significantly");
        }

        [Test]
        public void RoutinePlaysAreAlmostAlwaysMadeAndDivesOftenFail()
        {
            // Whole plays with an average crew: routine takes (standing/running catches, centered pickups) almost never fail;
            // dives fail often.
            int routine = 0, routineFailed = 0, dives = 0, divesFailed = 0;
            for (long seed = 1; seed <= 15; seed++)
                foreach (BallInPlay ball in Balls)
                {
                    LivePlay p = Play(ball, Crew(50), seed);
                    if (p.Defense.Takes.Count == 0) continue;
                    BallTake first = p.Defense.Takes[0];
                    bool isRoutine = first.Action == FieldingAction.StandingCatch || first.Action == FieldingAction.CenteredPickup || first.Action == FieldingAction.RunningCatch;
                    if (isRoutine) { routine++; if (!first.Held) routineFailed++; }
                    if (first.Action == FieldingAction.DivingCatch) { dives++; if (!first.Held) divesFailed++; }
                }

            Assert.Greater(routine, 150);
            Assert.Less(routineFailed / (double)routine, 0.01, $"routine {routineFailed}/{routine}");
            Assert.Greater(dives, 25);
            Assert.Greater(divesFailed / (double)dives, 0.25, $"dives {divesFailed}/{dives}");
        }

        [Test]
        public void ArmAccuracyMakesFewerThrowingMisplays()
        {
            // Whole plays: only the throwers' accuracy differs (the throw error uses the thrower's own skill).
            int Throwing(int accuracy)
            {
                int n = 0;
                for (long seed = 1; seed <= 8; seed++)
                    foreach (BallInPlay ball in Balls)
                        n += Play(ball, new PlayPersonnel(FielderProfile.For, ThrowProfile.For, ThrowProfile.Full, _ => RunnerProfile.Standard,
                            _ => new FielderSkill(90, 90, accuracy)), seed, new BaseOccupancy(true, false, false)).Misplays.Count(m => m.Kind == MisplayKind.ThrowingMisplay);
                return n;
            }

            int accurate = Throwing(95), wild = Throwing(5);
            Assert.Less(accurate, wild, $"{accurate} vs {wild}");
        }

        [Test]
        public void ArmAccuracyNarrowsTheThrows()
        {
            double Rms(int accuracy)
            {
                double sq = 0.0;
                var from = new Vector3d(-10.0, 30.0, 1.8);
                var aim = new Vector3d(19.4, 19.4, 1.3);
                for (int i = 0; i < 3000; i++)
                {
                    SeedStream s = FieldingExecution.StreamFor(7, DefensivePosition.Shortstop, i, 1);
                    sq += FieldingExecution.ThrowError(from, aim, new FielderSkill(50, 50, accuracy), false, ref s).LengthSquared;
                }

                return Math.Sqrt(sq / 3000);
            }

            double good = Rms(90), avg = Rms(50), poor = Rms(10);
            Assert.Less(good, avg * 0.85);
            Assert.Less(avg, poor * 0.85);
            Assert.That(avg, Is.InRange(0.3, 1.0), "an average 30-m throw misses its aim by tens of centimetres (RMS)");
        }
    }
}
