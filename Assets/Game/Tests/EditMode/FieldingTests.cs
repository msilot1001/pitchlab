using System;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>The defender running law, reach time and motion (TASK-005).</summary>
    public class FielderMotionTests
    {
        private static readonly FielderProfile Generic = FielderProfile.For(DefensivePosition.LeftField);

        [Test]
        public void RunningLawMatchesTheStatcastSprintReference()
        {
            // 90 ft from rest: about 90/v_max + τ (Docs/FIELDING.md: the 90-ft splits average 4.02 s for 27.8 ft/s runners).
            FielderProfile runner = new FielderProfile(0.0, 27.8 * 0.3048, 0.78, 6.0, 1.0, 0.6, 2.6, 0.3);
            Assert.AreEqual(90.0 / 27.8 + 0.78, RunningLaw.RunThroughTime(runner, 90.0 * 0.3048), 0.01);
            Assert.Less(RunningLaw.Speed(Generic, 0.2), 0.25 * Generic.MaxSpeed, "nobody starts at top speed");
            Assert.AreEqual(Generic.MaxSpeed, RunningLaw.Speed(Generic, 10.0), 1e-3);
        }

        [Test]
        public void SpeedIsTheDerivativeOfDistance()
        {
            for (double t = 0.1; t < 5.0; t += 0.37)
            {
                double numeric = (RunningLaw.Distance(Generic, t + 1e-6) - RunningLaw.Distance(Generic, t - 1e-6)) / 2e-6;
                Assert.AreEqual(RunningLaw.Speed(Generic, t), numeric, 1e-5);
            }
        }

        [TestCase(0.5)]
        [TestCase(4.0)]
        [TestCase(25.0)]
        [TestCase(60.0)]
        public void PredictedReachTimeIsTheActualMotion(double distance)
        {
            // The reach-time model and the motion are the same law: arrival predicted = arrival performed, both modes.
            var start = new Vector3d(10.0, 50.0, 0.0);
            var target = start + new Vector3d(0.6, 0.8, 0.0) * distance;
            var run = new FielderMotion(Generic, start, target, 2.0, false);
            Assert.AreEqual(2.0 + RunningLaw.RunThroughTime(Generic, distance), run.ArrivalTime, 1e-12);
            Assert.Less((run.PositionAt(run.ArrivalTime) - target).Length, 1e-6, "run-through reaches the target at the predicted time");
            Assert.Greater(run.SpeedAt(run.ArrivalTime), 0.0, "and is still running");

            var stop = new FielderMotion(Generic, start, target, 2.0, true);
            Assert.Less((stop.PositionAt(stop.ArrivalTime) - target).Length, 1e-6, "stopping route ends on the target");
            Assert.AreEqual(0.0, stop.SpeedAt(stop.ArrivalTime + 1e-9), 1e-9);
            Assert.Less((stop.PositionAt(stop.ArrivalTime + 5.0) - target).Length, 1e-6, "and waits there");
            Assert.GreaterOrEqual(stop.ArrivalTime, run.ArrivalTime);
        }

        [Test]
        public void MotionIsContinuousAndNeverExceedsTheProfile()
        {
            var m = new FielderMotion(Generic, Vector3d.Zero, new Vector3d(30.0, 0.0, 0.0), 0.4, true);
            Vector3d previous = m.PositionAt(0.0);
            for (double t = 0.0; t < 8.0; t += 0.001)
            {
                Vector3d p = m.PositionAt(t);
                Assert.LessOrEqual((p - previous).Length, Generic.MaxSpeed * 0.001 + 1e-9, $"no jump at {t:0.000}");
                Assert.LessOrEqual(m.SpeedAt(t), Generic.MaxSpeed + 1e-9);
                previous = p;
            }

            Assert.AreEqual(MotionPhase.Ready, m.PhaseAt(0.3), "reacting");
            Assert.AreEqual(MotionPhase.Running, m.PhaseAt(1.0));
            Assert.AreEqual(MotionPhase.Arrived, m.PhaseAt(m.ArrivalTime + 0.1));
        }

        [Test]
        public void HoldingDefenderNeverMoves()
        {
            var hold = new FielderMotion(Generic, new Vector3d(3.0, 4.0, 0.0));
            Assert.AreEqual(new Vector3d(3.0, 4.0, 0.0), hold.PositionAt(100.0));
            Assert.AreEqual(MotionPhase.Ready, hold.PhaseAt(100.0));
        }
    }

    /// <summary>
    /// Intercepts and the fielding play on the authoritative ball in play (TASK-005): fly catches, grounders before rest,
    /// hops, rolling pickups, wall caroms, primary selection, no swarm, possession, dead and out-of-play balls.
    /// </summary>
    public class FieldingTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);
        private static readonly double R = BallProperties.Baseball.Radius;

        private static BallInPlay Hit(double mph, double launch, double spray, double backspin) =>
            BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, backspin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard);

        private static int ImpactsBefore(BallInPlay ball, double t)
        {
            int n = 0;
            foreach (BallEvent e in ball.Events)
                if ((e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.WallImpact) && e.Time <= t) n++;
            return n;
        }

        /// <summary>The primary meets the ball: within his reach horizontally, inside his glove height, at the intercept time.</summary>
        private static void AssertMeets(FieldingPlay f)
        {
            Intercept i = f.Intercept;
            Vector3d holder = f.Motion(f.Primary.Value).PositionAt(i.Time);
            Vector3d gap = i.Ball.Position - holder;
            Assert.LessOrEqual(new Vector3d(gap.X, gap.Y, 0.0).Length, FielderProfile.For(f.Primary.Value).ReachAt(i.Ball.Position.Z) + 1e-6, "glove reaches the ball");
            Assert.LessOrEqual(i.Ball.Position.Z, FielderProfile.For(f.Primary.Value).CatchHeightMax + 1e-9);
            Assert.AreEqual(i.Ball.Position, f.Ball.StateAt(i.Time).Position, "the intercept is on the authoritative trajectory");
            FielderMotion m = f.Motion(f.Primary.Value);
            Assert.GreaterOrEqual(m.StartTime, f.Ball.First.Time + m.Profile.ReactionTime - 1e-12, "no movement before the reaction time");
        }

        [Test]
        public void RoutineCenterFieldFlyIsCaughtAtAComfortableHeight()
        {
            FieldingPlay f = FieldingSolver.Solve(Hit(92.0, 32.0, 0.0, 2400.0));
            Assert.AreEqual(FieldingOutcome.Fielded, f.Outcome);
            Assert.AreEqual(DefensivePosition.CenterField, f.Primary);
            Assert.AreEqual(InterceptKind.FlyCatch, f.Intercept.Kind);
            Assert.Greater(f.Intercept.Margin, 1.0, "routine: time to spare");
            Assert.LessOrEqual(f.Intercept.Ball.Position.Z, InterceptSolver.ComfortCatchHeight + 1e-9);
            Assert.IsTrue(f.Motion(DefensivePosition.CenterField).StopsAtTarget, "settles under it");
            AssertMeets(f);
        }

        [Test]
        public void ShallowFlyIsCaughtBeforeItLands()
        {
            FieldingPlay f = FieldingSolver.Solve(Hit(75.0, 40.0, 10.0, 2800.0));
            Assert.AreEqual(InterceptKind.FlyCatch, f.Intercept.Kind);
            Assert.Less(f.Intercept.Time, f.Ball.FirstGroundContact.Value.Time);
            AssertMeets(f);
        }

        [Test]
        public void GapLinerOutOfReachDropsAndIsTakenOnTheHop()
        {
            BallInPlay ball = Hit(100.0, 20.0, -15.0, 1800.0);
            FieldingPlay f = FieldingSolver.Solve(ball);
            Assert.AreEqual(FieldingOutcome.Fielded, f.Outcome);
            Assert.AreNotEqual(InterceptKind.FlyCatch, f.Intercept.Kind, "nobody gets there in the air");
            Assert.Greater(ImpactsBefore(ball, f.Intercept.Time), 0);
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
            {
                Intercept c = f.Candidate(p);
                if (c.Feasible) Assert.AreNotEqual(InterceptKind.FlyCatch, c.Kind, $"{p} could not catch it either");
            }

            AssertMeets(f);
        }

        [Test]
        public void RoutineGrounderToShortIsFieldedBeforeItStops()
        {
            BallInPlay ball = Hit(85.0, -8.0, -15.0, -1000.0);
            FieldingPlay f = FieldingSolver.Solve(ball);
            Assert.AreEqual(DefensivePosition.Shortstop, f.Primary);
            Assert.AreEqual(InterceptKind.GroundPickup, f.Intercept.Kind);
            Assert.Less(f.Intercept.Time, ball.EndTime, "before rest");
            Assert.AreEqual(BallPhase.Rolling, ball.PhaseAt(f.Intercept.Time));
            AssertMeets(f);
        }

        [Test]
        public void ChopperIsTakenAfterItsSecondBounce()
        {
            BallInPlay ball = Hit(60.0, -20.0, 5.0, -800.0);
            FieldingPlay f = FieldingSolver.Solve(ball);
            Assert.GreaterOrEqual(ImpactsBefore(ball, f.Intercept.Time), 2, "second hop or later");
            Assert.Less(f.Intercept.Time, ball.EndTime);
            AssertMeets(f);
        }

        [Test]
        public void HardGrounderThroughTheInfieldIsTakenByAnOutfielderRolling()
        {
            BallInPlay ball = Hit(105.0, -6.0, -5.0, -900.0);
            FieldingPlay f = FieldingSolver.Solve(ball);
            Assert.AreEqual(DefensivePosition.CenterField, f.Primary, "it beats the infielders");
            Assert.AreEqual(InterceptKind.GroundPickup, f.Intercept.Kind);
            Assert.Less(f.Intercept.Time, ball.EndTime);
            AssertMeets(f);
        }

        [Test]
        public void WallCaromIsTakenOffTheWall()
        {
            BallInPlay ball = Hit(105.0, 14.0, -20.0, 1800.0);
            FieldingPlay f = FieldingSolver.Solve(ball);
            BallEvent wall = Array.Find(Events(ball), e => e.Kind == BallEventKind.WallImpact);
            Assert.AreEqual(BallEventKind.WallImpact, wall.Kind, "the ball reaches the wall");
            Assert.Greater(f.Intercept.Time, wall.Time, "taken after the carom");
            Assert.AreEqual(DefensivePosition.LeftField, f.Primary);
            AssertMeets(f);
        }

        [Test]
        public void TheInterceptIsTheEarliestFeasibleMomentToAMillisecond()
        {
            // Refinement: just before the intercept the defender cannot be there; at it he can (grounder: no comfort shift).
            BallInPlay ball = Hit(85.0, -8.0, -15.0, -1000.0);
            FieldingPlay f = FieldingSolver.Solve(ball);
            FielderProfile ss = FielderProfile.For(DefensivePosition.Shortstop);
            Vector3d start = DefensiveAlignment.Standard[DefensivePosition.Shortstop];
            Assert.IsTrue(InterceptSolver.Feasible(ball, start, ss, FieldLayout.Standard, f.Intercept.Time, out _));
            Assert.IsFalse(InterceptSolver.Feasible(ball, start, ss, FieldLayout.Standard, f.Intercept.Time - 1e-3, out _));
            Assert.AreEqual(0.0, f.Intercept.Margin, 1e-6, "arrives exactly as the ball does");
        }

        [Test]
        public void PrimaryIsTheEarliestInterceptNotTheNearestDefender()
        {
            // Hard grounder up the middle-left: the shortstop is nearer the ball's path early, but the centre fielder takes it.
            BallInPlay ball = Hit(105.0, -6.0, -5.0, -900.0);
            FieldingPlay f = FieldingSolver.Solve(ball);
            double primary = f.Intercept.Time;
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
                if (f.Candidate(p).Feasible) Assert.GreaterOrEqual(f.Candidate(p).Time, primary - FieldingSolver.TieWindow, p.ToString());
            Vector3d landing = ball.FirstGroundContact.Value.Before.Position;
            double ss = (DefensiveAlignment.Standard[DefensivePosition.Shortstop] - landing).Length;
            double cf = (DefensiveAlignment.Standard[DefensivePosition.CenterField] - landing).Length;
            Assert.Less(ss, cf, "the shortstop is nearer the landing point");
            Assert.AreEqual(DefensivePosition.CenterField, f.Primary);
        }

        [Test]
        public void MirroredTieGoesToTheShortstopByPriority()
        {
            // 2B and SS placed symmetrically about a ball hit straight up the middle: equal intercepts → positional priority.
            Vector3d ss = DefensiveAlignment.Standard[DefensivePosition.Shortstop];
            var positions = new Vector3d[DefensiveAlignment.Count];
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition))) positions[(int)p] = DefensiveAlignment.Standard[p] + new Vector3d(0.0, 200.0, 0.0);
            positions[(int)DefensivePosition.Shortstop] = new Vector3d(-3.0, ss.Y, 0.0);
            positions[(int)DefensivePosition.SecondBase] = new Vector3d(3.0, ss.Y, 0.0);
            positions[(int)DefensivePosition.CenterField] = new Vector3d(0.0, 98.0, 0.0);
            BallInPlay ball = Hit(80.0, -6.0, 0.0, 0.0);
            // Same abilities for both (the generic profiles differ by position: SS 28 ft/s, 2B 27 ft/s).
            FielderProfile Profiles(DefensivePosition p) => p == DefensivePosition.SecondBase ? FielderProfile.For(DefensivePosition.Shortstop) : FielderProfile.For(p);
            FieldingPlay f = FieldingSolver.Solve(ball, new DefensiveAlignment(positions), Profiles, FieldLayout.Standard);
            Assert.AreEqual(f.Candidate(DefensivePosition.Shortstop).Time, f.Candidate(DefensivePosition.SecondBase).Time, 1e-9, "a true tie");
            Assert.AreEqual(DefensivePosition.Shortstop, f.Primary);
        }

        [Test]
        public void OnlyThePrimaryChasesTheBall()
        {
            FieldingPlay f = FieldingSolver.Solve(Hit(85.0, -8.0, -15.0, -1000.0));
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
            {
                if (p == f.Primary) continue;
                Assert.AreEqual(DefensiveAlignment.Standard[p], f.Motion(p).PositionAt(f.EndTime), $"{p} holds");
            }

            Assert.AreNotEqual(DefensiveAlignment.Standard[f.Primary.Value], f.Motion(f.Primary.Value).PositionAt(f.EndTime));
        }

        [Test]
        public void PossessionTakesTheBallFromItsTrajectoryWithoutAJump()
        {
            FieldingPlay f = FieldingSolver.Solve(Hit(92.0, 32.0, 0.0, 2400.0));
            double t = f.PossessionTime;
            Assert.AreEqual(BallAuthority.FreeBall, f.AuthorityAt(t - 1e-6));
            Assert.AreEqual(BallAuthority.Possessed, f.AuthorityAt(t));
            Assert.Less((f.BallPositionAt(t - 1e-6) - f.BallPositionAt(t)).Length, 1e-4, "no jump at the catch");
            // Afterwards the ball goes with the holder (secured at his chest), not with the free trajectory (still falling).
            Vector3d holder = f.Motion(f.Primary.Value).PositionAt(t + 1.0);
            Assert.Less((f.BallPositionAt(t + 1.0) - holder - FieldingPlay.HoldOffset).Length, 1e-9);
            for (double u = t; u < t + FieldingPlay.SecureTime; u += 0.01)
                Assert.Less((f.BallPositionAt(u + 0.01) - f.BallPositionAt(u)).Length, 0.15, "secured smoothly, no jump");
            Assert.Greater((f.BallPositionAt(t + 1.0) - f.Ball.StateAt(t + 1.0).Position).Length, 1.0);
            Assert.AreEqual(t, f.EndTime, "the play ends at possession");
        }

        [Test]
        public void HomeRunIsOutOfPlayAndNobodyChasesIt()
        {
            FieldingPlay f = FieldingSolver.Solve(Hit(108.0, 28.0, 0.0, 2200.0));
            Assert.AreEqual(FieldingOutcome.OutOfPlay, f.Outcome);
            Assert.IsNull(f.Primary);
            Assert.AreEqual(BallInPlayCall.HomeRun, f.Call);
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
                Assert.AreEqual(DefensiveAlignment.Standard[p], f.Motion(p).PositionAt(30.0), p.ToString());
        }

        [Test]
        public void FoulBallIsDeadAndNotFielded()
        {
            FieldingPlay f = FieldingSolver.Solve(Hit(90.0, 25.0, -60.0, 2000.0));
            Assert.AreEqual(FieldingOutcome.DeadFoul, f.Outcome);
            Assert.IsNull(f.Primary);
            Assert.AreEqual(BallAuthority.FreeBall, f.AuthorityAt(100.0));
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
                Assert.AreEqual(DefensiveAlignment.Standard[p], f.Motion(p).PositionAt(30.0), $"{p} holds on a dead ball");
        }

        [Test]
        public void IdenticalPlaysGiveIdenticalFielding()
        {
            FieldingPlay a = FieldingSolver.Solve(Hit(98.0, 14.0, -8.0, 1400.0)), b = FieldingSolver.Solve(Hit(98.0, 14.0, -8.0, 1400.0));
            Assert.AreEqual(a.Primary, b.Primary);
            Assert.AreEqual(a.Intercept.Time, b.Intercept.Time, 0.0);
            Assert.AreEqual(a.Intercept.FielderTarget, b.Intercept.FielderTarget);
        }

        // ---- Envelope boundaries on synthetic balls (straight lines, one second per segment sample).

        private static BallInPlay Synthetic(Vector3d[] points, BallEvent[] events)
        {
            var states = new BallState[points.Length];
            for (int i = 0; i < points.Length; i++)
                states[i] = new BallState(i, points[i], Vector3d.Zero, Vector3d.Zero);   // zero velocities: the samples' exact heights hold between equal samples
            return new BallInPlay(new[] { new BallSegment(BallPhase.Airborne, new TrajectoryResult(states, FlightEnd.ReachedGround)) }, events, BallPhase.Rest);
        }

        [TestCase(2.599, true)]
        [TestCase(2.6, true)]
        [TestCase(2.601, false)]
        public void CatchHeightBoundary(double height, bool catchable)
        {
            // A ball hanging at constant height right above where a defender stands, two seconds into the play.
            FielderProfile p = FielderProfile.For(DefensivePosition.CenterField);
            var spot = new Vector3d(0.0, 90.0, 0.0);
            BallInPlay ball = Synthetic(new[] { new Vector3d(0.0, 90.0, height), new Vector3d(0.0, 90.0, height), new Vector3d(0.0, 90.0, height), new Vector3d(0.0, 90.0, R) }, new BallEvent[0]);
            Assert.AreEqual(catchable, InterceptSolver.Feasible(ball, spot, p, FieldLayout.Standard, 2.0, out Intercept i));
            if (catchable) Assert.AreEqual(InterceptKind.FlyCatch, i.Kind);
        }

        [TestCase(0.29, InterceptKind.GroundPickup)]
        [TestCase(0.30, InterceptKind.GroundPickup)]
        [TestCase(0.31, InterceptKind.HopCatch)]
        public void PickupHeightBoundaryAfterABounce(double height, InterceptKind expected)
        {
            FielderProfile p = FielderProfile.For(DefensivePosition.Shortstop);
            var spot = new Vector3d(0.0, 40.0, 0.0);
            var bounce = new BallState(0.5, new Vector3d(0.0, 40.0, R), Vector3d.Zero, Vector3d.Zero);
            BallInPlay ball = Synthetic(new[] { new Vector3d(0.0, 40.0, 1.0), new Vector3d(0.0, 40.0, height), new Vector3d(0.0, 40.0, height), new Vector3d(0.0, 40.0, R) },
                new[] { new BallEvent(BallEventKind.GroundImpact, bounce, bounce, SurfaceKind.InfieldDirt) });
            Assert.IsTrue(InterceptSolver.Feasible(ball, spot, p, FieldLayout.Standard, 1.5, out Intercept i));
            Assert.AreEqual(expected, i.Kind);
        }

        [Test]
        public void ReachIsHorizontalDistanceLessTheGlove()
        {
            // A defender 10.5 m from a ball sitting still on the ground: he runs 10.5 − 0.6 m (ground reach) and needs reaction
            // + run time to get there.
            FielderProfile p = FielderProfile.For(DefensivePosition.LeftField);
            var spot = new Vector3d(-30.0, 80.0, 0.0);
            BallInPlay ball = Synthetic(new[] { new Vector3d(-30.0, 90.5, R), new Vector3d(-30.0, 90.5, R) }, new BallEvent[0]);
            double route = 10.5 - p.GroundReach;
            double needed = p.ReactionTime + RunningLaw.RunThroughTime(p, route);
            Assert.IsFalse(InterceptSolver.Feasible(ball, spot, p, FieldLayout.Standard, needed - 1e-6, out _));
            Assert.IsTrue(InterceptSolver.Feasible(ball, spot, p, FieldLayout.Standard, needed + 1e-6, out Intercept i));
            Assert.AreEqual(route, i.RouteDistance, 1e-9);
        }

        [Test]
        public void GloveReachShrinksForLowBalls()
        {
            FielderProfile p = FielderProfile.For(DefensivePosition.Shortstop);
            Assert.AreEqual(p.GroundReach, p.ReachAt(0.04), 1e-12);
            Assert.AreEqual(p.Reach, p.ReachAt(1.5), 1e-12);
            Assert.Less(p.ReachAt(0.6), p.ReachAt(0.8));
        }

        [Test]
        public void BallThatStopsBeforeAnyoneArrivesIsPickedUpWhereItLies()
        {
            // A dribbler dead 2 m in front of the plate after 0.3 s: nobody is there by then, but someone walks over and
            // picks it up (physics review: a ball at rest was never fielded).
            var rest = new Vector3d(0.3, 2.0, R);
            BallInPlay ball = Synthetic(new[] { new Vector3d(0.0, 0.7, 0.8), rest }, new BallEvent[0]);
            FieldingPlay f = FieldingSolver.Solve(ball, DefensiveAlignment.Standard, FielderProfile.For, FieldLayout.Standard);
            Assert.AreEqual(FieldingOutcome.Fielded, f.Outcome);
            Assert.AreEqual(InterceptKind.GroundPickup, f.Intercept.Kind);
            Assert.Greater(f.Intercept.Time, ball.EndTime, "after it stopped");
            Assert.AreEqual(f.Intercept.EarliestArrival, f.Intercept.Time, 1e-6, "as soon as he can get there");
            Assert.AreEqual(DefensivePosition.C, f.Primary, "the catcher is nearest and quickest to it");
            AssertMeets(f);
        }

        [Test]
        public void FairRollerTakenBeforeItRollsFoulIsFair()
        {
            // Fair down the third-base line, it would cross the line and settle foul short of the bag. A third baseman
            // standing by takes it over fair ground first: fair and fielded. With nobody near, it is a dead foul.
            double s = Math.Sqrt(0.5);
            Vector3d Line(double along, double outside) => new Vector3d(-(s * along + s * outside), s * along - s * outside, R);
            BallInPlay ball = Synthetic(new[] { new Vector3d(0.0, 0.7, 0.8), Line(14.0, -0.4), Line(18.0, 0.2), Line(19.0, 1.5) }, new BallEvent[0]);
            Assert.AreEqual(BallInPlayCall.Foul, FairFoul.Call(ball).Call, "left alone it settles foul");

            var near = new Vector3d[DefensiveAlignment.Count];
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition))) near[(int)p] = DefensiveAlignment.Standard[p];
            near[(int)DefensivePosition.ThirdBase] = Line(15.0, -1.0) - new Vector3d(0.0, 0.0, R);
            FieldingPlay fielded = FieldingSolver.Solve(ball, new DefensiveAlignment(near), FielderProfile.For, FieldLayout.Standard);
            Assert.AreEqual(FieldingOutcome.Fielded, fielded.Outcome);
            Assert.AreEqual(DefensivePosition.ThirdBase, fielded.Primary);
            Assert.AreEqual(BallInPlayCall.Fair, fielded.Call);

            var far = (Vector3d[])near.Clone();
            for (int i = 0; i < far.Length; i++) far[i] = far[i] + new Vector3d(0.0, 60.0, 0.0);
            FieldingPlay dead = FieldingSolver.Solve(ball, new DefensiveAlignment(far), FielderProfile.For, FieldLayout.Standard);
            Assert.AreEqual(FieldingOutcome.DeadFoul, dead.Outcome);
        }

        private static Intercept At(double time, double margin) =>
            new Intercept(time, default, Vector3d.Zero, 0.0, time - margin, InterceptKind.GroundPickup);

        [Test]
        public void SelectionIsTheEarliestThenMarginThenPriorityWithoutDrift()
        {
            var candidates = new Intercept[DefensiveAlignment.Count];
            // Earliest wins outside the tie window, whatever the margins.
            candidates[(int)DefensivePosition.Shortstop] = At(1.00, 0.0);
            candidates[(int)DefensivePosition.CenterField] = At(1.20, 2.0);
            Assert.AreEqual((int)DefensivePosition.Shortstop, FieldingSolver.SelectPrimary(candidates));
            // Inside the window the larger margin wins.
            candidates[(int)DefensivePosition.LeftField] = At(1.05, 0.5);
            Assert.AreEqual((int)DefensivePosition.LeftField, FieldingSolver.SelectPrimary(candidates));
            // A chain of pairwise ties (SS 1.00, LF 1.08, CF 1.16 with growing margins) never drifts past the window from
            // the earliest (physics review: the old pairwise comparator handed it to CF, 0.16 s late).
            candidates[(int)DefensivePosition.LeftField] = At(1.08, 0.3);
            candidates[(int)DefensivePosition.CenterField] = At(1.16, 0.6);
            Assert.AreEqual((int)DefensivePosition.LeftField, FieldingSolver.SelectPrimary(candidates));
            // Equal time and margin: positional priority (SS over 2B).
            var tie = new Intercept[DefensiveAlignment.Count];
            tie[(int)DefensivePosition.SecondBase] = At(1.0, 0.2);
            tie[(int)DefensivePosition.Shortstop] = At(1.0, 0.2);
            Assert.AreEqual((int)DefensivePosition.Shortstop, FieldingSolver.SelectPrimary(tie));
            Assert.AreEqual(-1, FieldingSolver.SelectPrimary(new Intercept[DefensiveAlignment.Count]), "nobody");
        }

        [Test]
        public void ANearerDefenderWithASlowerReactionLoses()
        {
            // The catcher stands nearer a ball at rest than the pitcher, but reacts 0.5 s late: the pitcher gets there first.
            var rest = new Vector3d(0.0, 8.0, R);
            BallInPlay ball = Synthetic(new[] { new Vector3d(0.0, 0.7, 0.8), rest }, new BallEvent[0]);
            var positions = new Vector3d[DefensiveAlignment.Count];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector3d(0.0, 200.0, 0.0);
            positions[(int)DefensivePosition.C] = new Vector3d(0.0, 4.0, 0.0);
            positions[(int)DefensivePosition.P] = new Vector3d(0.0, 12.5, 0.0);
            FielderProfile Profiles(DefensivePosition p) => p == DefensivePosition.C
                ? new FielderProfile(1.5, 7.62, 0.78, 6.0, 1.0, 0.6, 2.6, 0.3)
                : FielderProfile.For(p);
            FieldingPlay f = FieldingSolver.Solve(ball, new DefensiveAlignment(positions), Profiles, FieldLayout.Standard);
            Assert.Less((positions[(int)DefensivePosition.C] - rest).Length, (positions[(int)DefensivePosition.P] - rest).Length - 0.4);
            Assert.AreEqual(DefensivePosition.P, f.Primary);
        }

        private static BallEvent[] Events(BallInPlay ball)
        {
            var e = new BallEvent[ball.Events.Count];
            for (int i = 0; i < e.Length; i++) e[i] = ball.Events[i];
            return e;
        }
    }
}
