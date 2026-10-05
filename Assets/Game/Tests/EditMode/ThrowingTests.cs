using System;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-006A defensive throwing: regulation base geometry, the throw solver on the real flight, receiver conventions,
    /// the explicit ball-authority timeline (free → possessed → thrown → possessed / free), transfer and release
    /// continuity, a missed throw that is never snapped into a glove, and determinism.
    /// </summary>
    public class ThrowingTests
    {
        private const double Ft = 0.3048;
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);

        private static FieldingPlay Field(double mph, double launch, double spray, double spin) =>
            FieldingSolver.Solve(BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard));

        // The five TASK-006A scenarios.
        private static readonly (string Name, double Mph, double Launch, double Spray, double Spin, Base Target, DefensivePosition Thrower, DefensivePosition Receiver)[] Scenarios =
        {
            ("SS grounder to first", 85.0, -8.0, -15.0, -1000.0, Base.First, DefensivePosition.Shortstop, DefensivePosition.FirstBase),
            ("3B grounder to first", 80.0, -7.0, -30.0, -900.0, Base.First, DefensivePosition.ThirdBase, DefensivePosition.FirstBase),
            ("2B grounder to first", 80.0, -7.0, 18.0, -900.0, Base.First, DefensivePosition.SecondBase, DefensivePosition.FirstBase),
            ("LF single to second", 98.0, 9.0, -24.0, 900.0, Base.Second, DefensivePosition.LeftField, DefensivePosition.Shortstop),
            ("CF single to home", 95.0, 6.0, 0.0, 700.0, Base.Home, DefensivePosition.CenterField, DefensivePosition.C),
        };

        [Test]
        public void BasesFollowTheRegulationDiamond()
        {
            Vector3d first = FieldLayout.BasePosition(Base.First), second = FieldLayout.BasePosition(Base.Second), third = FieldLayout.BasePosition(Base.Third);
            double half = FieldLayout.BaseBagSide / 2.0, s = Math.Sqrt(0.5);
            // First base: outer corner on the line 90 ft from the plate's rear point, bag wholly in fair territory.
            Assert.AreEqual(90.0 * Ft, (first.X + first.Y) * s + half, 1e-9, "bag reaches 90 ft along the line");
            Assert.Less(FieldLayout.OutsideFoulLine(first.X, first.Y), -half + 1e-9, "inside fair territory");
            Assert.AreEqual(first.Y, third.Y, 1e-12);
            Assert.AreEqual(-first.X, third.X, 1e-12);
            Assert.AreEqual((127.0 + 3.375 / 12.0) * Ft, second.Y, 1e-9);
            Assert.AreEqual(0.0, second.X, 1e-12);
            Assert.Greater(FieldLayout.BasePosition(Base.Home).Y, 0.0, "home is the plate centre, in front of its rear point");
        }

        [Test]
        public void ThrowSolverHitsTheTargetOnTheRealFlight() => AssertHits(new Vector3d(-9.0, 33.0, 1.8), new Vector3d(19.1, 19.4, 1.3), 32.0);

        [Test]
        public void ThrowSolverReachesNearItsMaximumRange()
        {
            // 88 m at the right fielder's routine speed: with drag and backspin the highest-reaching arc there is ~35°, not
            // the 40° bound, and the solver must find the reaching arc below it (physics review).
            AssertHits(new Vector3d(0.0, 0.0, 1.8), new Vector3d(0.0, 88.0, 1.3), ThrowProfile.For(DefensivePosition.RightField).Speed);
        }

        private static void AssertHits(Vector3d release, Vector3d target, double speed)
        {
            BallState launch = ThrowSolver.Launch(release, target, speed, 5.0, EnvironmentState.Standard, out bool reaches);
            Assert.IsTrue(reaches);
            Assert.AreEqual(speed, launch.Velocity.Length, 1e-9, "at the requested speed");
            BallInPlay flight = BallInPlaySimulation.Run(launch, EnvironmentState.Standard, FieldLayout.Standard, ThrowSolver.Aerodynamics);
            var dir = new Vector3d(target.X - release.X, target.Y - release.Y, 0.0).Normalized;
            double d = new Vector3d(target.X - release.X, target.Y - release.Y, 0.0).Length;
            double lo = flight.First.Time, hi = flight.First.Time + 6.0;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Vector3d.Dot(flight.StateAt(mid).Position - release, dir) < d) lo = mid;
                else hi = mid;
            }

            Vector3d at = flight.StateAt(hi).Position;
            Assert.AreEqual(target.Z, at.Z, 0.02, "arrives at the target height");
            Assert.AreEqual(0.0, (new Vector3d(at.X, at.Y, 0.0) - new Vector3d(target.X, target.Y, 0.0)).Length, 0.02, "and over the target");
            Assert.Greater(Vector3d.Cross(launch.Spin, launch.Velocity).Z, 0.0, "backspin: Magnus lifts the throw");
        }

        [Test]
        public void TargetBelowTheLowestThrowDoesNotReach()
        {
            // 1 m away and 0.5 m below the release: even −15° passes over it — reported as not reaching (Codex review).
            ThrowSolver.Launch(new Vector3d(0.0, 0.0, 1.8), new Vector3d(0.0, 1.0, 1.3), 30.0, 0.0, EnvironmentState.Standard, out bool reaches);
            Assert.IsFalse(reaches);
        }

        [Test]
        public void CatcherThrowsHomeToThePitcherCovering()
        {
            // A dribbler the catcher fields, thrown home: the pitcher covers the plate and takes it there.
            FieldingPlay fielding = Field(20.0, -40.0, 0.0, -300.0);
            Assert.AreEqual(DefensivePosition.C, fielding.Primary);
            DefensivePlay play = ThrowPlanner.Plan(fielding, Base.Home);
            Assert.AreEqual(DefensivePosition.P, play.Throw.Receiver);
            Assert.IsTrue(play.Throw.Caught);
            Assert.AreEqual(InterceptKind.FlyCatch, play.Throw.Catch.Kind);
            AssertTimeline(play);
        }

        [Test]
        public void ThrowOutOfRangeFallsShort()
        {
            var release = new Vector3d(0.0, 90.0, 1.8);
            BallState launch = ThrowSolver.Launch(release, new Vector3d(0.0, 0.3, 1.3), 15.0, 0.0, EnvironmentState.Standard, out bool reaches);
            Assert.IsFalse(reaches, "15 m/s cannot reach home from 300 ft");
            BallInPlay flight = BallInPlaySimulation.Run(launch, EnvironmentState.Standard, FieldLayout.Standard, ThrowSolver.Aerodynamics);
            Assert.Greater(flight.FirstGroundContact.Value.Before.Position.Y, 10.0, "it lands well short");
        }

        [TestCaseSource(nameof(ScenarioNames))]
        public void ThrowScenarioTransfersPossession(string name)
        {
            var sc = Array.Find(Scenarios, x => x.Name == name);
            FieldingPlay fielding = Field(sc.Mph, sc.Launch, sc.Spray, sc.Spin);
            Assert.AreEqual(sc.Thrower, fielding.Primary, "the expected defender fields it");
            DefensivePlay play = ThrowPlanner.Plan(fielding, sc.Target);
            ThrowPlay th = play.Throw;
            Assert.AreEqual(sc.Thrower, th.Thrower);
            Assert.AreEqual(sc.Receiver, th.Receiver);
            Assert.IsTrue(th.ReachesTarget, "the solved throw reaches the bag");
            Assert.IsTrue(th.Caught, "caught");
            Assert.AreEqual(InterceptKind.FlyCatch, th.Catch.Kind, "on the fly");
            Assert.AreEqual(ThrowProfile.For(sc.Thrower).TransferTime, th.ReleaseTime - fielding.PossessionTime, 1e-12, "transfer time");

            // A line throw, not a rainbow: the flatter solution (≤ 25° even from centre field to home, ≤ 10° in the infield)
            // and carrying ≥ 70 % of its release speed on average to the glove.
            Vector3d v = th.Flight.First.Velocity, gapToCatch = th.Catch.Ball.Position - th.ReleasePoint;
            double elevation = Math.Atan2(v.Z, Math.Sqrt(v.X * v.X + v.Y * v.Y)) * 180.0 / Math.PI;
            Assert.That(elevation, Is.InRange(-5.0, sc.Thrower <= DefensivePosition.Shortstop ? 10.0 : 25.0), "elevation");
            Assert.AreEqual(ThrowProfile.For(sc.Thrower).Speed, v.Length, 1e-9, "released at the profile speed");
            double carried = new Vector3d(gapToCatch.X, gapToCatch.Y, 0.0).Length / (th.Catch.Time - th.ReleaseTime) / v.Length;
            Assert.That(carried, Is.InRange(0.7, 1.0), "average horizontal speed / release speed");

            // Receiver on the bag when he catches (he got there first), glove within reach of the ball.
            Vector3d receiver = play.FielderPositionAt(th.Receiver, th.Catch.Time), bag = FieldLayout.BasePosition(sc.Target);
            Assert.Less(new Vector3d(receiver.X - bag.X, receiver.Y - bag.Y, 0.0).Length, 1e-6, "takes it on the bag");
            Vector3d gap = th.Catch.Ball.Position - receiver;
            Assert.LessOrEqual(new Vector3d(gap.X, gap.Y, 0.0).Length, FielderProfile.For(th.Receiver).ReachAt(th.Catch.Ball.Position.Z) + 1e-6);
            Assert.AreEqual(th.Catch.Ball.Position, th.Flight.StateAt(th.Catch.Time).Position, "caught on the throw's authoritative flight");

            AssertTimeline(play);
            Assert.AreEqual(th.Catch.Time, play.EndTime, 0.0, "the play ends with the receiver's catch");

            // After the catch the receiver has it: the throw's own trajectory (which flies on, bounces and rolls) no longer
            // moves the ball.
            double later = th.Catch.Time + 1.0;
            Assert.Less((play.BallPositionAt(later) - (play.FielderPositionAt(th.Receiver, later) + FieldingPlay.HoldOffset)).Length, 1e-9, "held by the receiver");
            Assert.Greater((play.BallPositionAt(later) - th.Flight.StateAt(later).Position).Length, 1.0, "not on the throw's trajectory");
        }

        private static string[] ScenarioNames() => Array.ConvertAll(Scenarios, s => s.Name);

        /// <summary>Exactly one authority at every instant, in order, with the ball continuous across every hand-over.</summary>
        private static void AssertTimeline(DefensivePlay play)
        {
            FieldingPlay f = play.Fielding;
            ThrowPlay th = play.Throw;
            double take = f.PossessionTime, release = th.ReleaseTime, catchAt = th.Caught ? th.Catch.Time : th.FirstContactTime;
            Assert.Less(take, release);
            Assert.Less(release, catchAt);
            for (double t = f.Ball.First.Time; t < play.EndTime + 1.0; t += 0.01)
            {
                BallAuthority a = play.AuthorityAt(t);
                DefensivePosition? holder = play.HolderAt(t);
                Assert.AreEqual(a == BallAuthority.Possessed, holder.HasValue, $"a holder exactly when possessed ({t:0.00})");
                if (t < take) Assert.AreEqual(BallAuthority.FreeBall, a);
                else if (t < release) { Assert.AreEqual(BallAuthority.Possessed, a); Assert.AreEqual(th.Thrower, holder); }
                else if (t < catchAt) Assert.AreEqual(BallAuthority.Thrown, a);
                else if (th.Caught) { Assert.AreEqual(BallAuthority.Possessed, a); Assert.AreEqual(th.Receiver, holder); }
                else Assert.AreEqual(BallAuthority.FreeBall, a);
            }

            // While possessed the batted ball's free trajectory no longer moves it.
            double mid = 0.5 * (take + release);
            Assert.Greater((play.BallPositionAt(mid) - f.Ball.StateAt(mid).Position).Length, 0.5, "not on the batted ball's trajectory");
            // Hand-overs are continuous: take, release, catch.
            foreach (double h in new[] { take, release, catchAt })
                Assert.Less((play.BallPositionAt(h - 1e-6) - play.BallPositionAt(h + 1e-6)).Length, 1e-3, $"continuous at {h - f.Ball.First.Time:0.00}");
            Assert.Less((play.BallPositionAt(release) - th.ReleasePoint).Length, 1e-9, "leaves from the release point");
            // The release point is in the thrower's hand: release height, within half a metre of him, a step toward the target.
            Vector3d thrower = f.Motion(th.Thrower).PositionAt(release), bag = FieldLayout.BasePosition(th.Target);
            Vector3d step = th.ReleasePoint - thrower, toBag = bag - thrower;
            Assert.AreEqual(ThrowProfile.For(th.Thrower).ReleaseHeight, th.ReleasePoint.Z, 1e-9, "release height");
            Assert.Less(new Vector3d(step.X, step.Y, 0.0).Length, 0.5, "in the thrower's hand");
            Assert.Greater(step.X * toBag.X + step.Y * toBag.Y, 0.0, "a step toward the target");
        }

        [Test]
        public void ThrowerHoldsTheBallUntilTheBaseIsCovered()
        {
            // The pitcher fields a chopper while the first baseman is still running to the bag: he holds the ball past his
            // transfer so the throw arrives when the bag is covered, and the first baseman catches it on the bag.
            FieldingPlay fielding = Field(60.0, -20.0, 5.0, -800.0);
            Assert.AreEqual(DefensivePosition.P, fielding.Primary);
            DefensivePlay play = ThrowPlanner.Plan(fielding, Base.First);
            ThrowPlay th = play.Throw;
            Assert.AreEqual(DefensivePosition.FirstBase, th.Receiver);
            Assert.Greater(th.ReleaseTime - fielding.PossessionTime, ThrowProfile.For(DefensivePosition.P).TransferTime + 0.1, "waits for the cover");
            Assert.IsTrue(th.Caught, "caught");
            Assert.AreEqual(InterceptKind.FlyCatch, th.Catch.Kind);
            Assert.LessOrEqual(th.Path.ToBase.ArrivalTime, th.Catch.Time, "on the bag before the catch");
            Assert.Less(th.Catch.Time - th.Path.ToBase.ArrivalTime, 0.15, "but no longer than needed");
            Vector3d receiver = play.FielderPositionAt(th.Receiver, th.Catch.Time), bag = FieldLayout.BasePosition(Base.First);
            Assert.Less(new Vector3d(receiver.X - bag.X, receiver.Y - bag.Y, 0.0).Length, 1e-6, "takes it on the bag");
            AssertTimeline(play);
        }

        [Test]
        public void MissedThrowIsNotSnappedIntoAGlove()
        {
            // The shortstop's throw at a feeble 12 m/s: it falls short, nobody catches it on the fly, it stays a free ball
            // on its own trajectory (bouncing, rolling) — no possession after the release.
            FieldingPlay fielding = Field(85.0, -8.0, -15.0, -1000.0);
            ThrowProfile Weak(DefensivePosition p) => new ThrowProfile(12.0, 0.7, 1.8);
            DefensivePlay play = ThrowPlanner.Plan(fielding, Base.First, FielderProfile.For, Weak, EnvironmentState.Standard, FieldLayout.Standard);
            ThrowPlay th = play.Throw;
            Assert.IsFalse(th.ReachesTarget, "12 m/s cannot reach first from short");
            Assert.IsFalse(th.Caught);
            AssertTimeline(play);
            double later = th.FirstContactTime + 0.5;
            Assert.AreEqual(BallAuthority.FreeBall, play.AuthorityAt(later));
            Assert.IsNull(play.HolderAt(later));
            Assert.AreEqual(th.Flight.StateAt(later).Position, play.BallPositionAt(later), "on the throw's own trajectory");
            Assert.AreEqual(th.Flight.EndTime, play.EndTime, 0.0);
        }

        [Test]
        public void ReceiverConventions()
        {
            Assert.AreEqual(DefensivePosition.FirstBase, ThrowAssignment.Receiver(Base.First, DefensivePosition.Shortstop));
            Assert.AreEqual(DefensivePosition.SecondBase, ThrowAssignment.Receiver(Base.First, DefensivePosition.FirstBase), "1B fielding: 2B covers");
            Assert.AreEqual(DefensivePosition.Shortstop, ThrowAssignment.Receiver(Base.Second, DefensivePosition.LeftField));
            Assert.AreEqual(DefensivePosition.SecondBase, ThrowAssignment.Receiver(Base.Second, DefensivePosition.Shortstop));
            Assert.AreEqual(DefensivePosition.ThirdBase, ThrowAssignment.Receiver(Base.Third, DefensivePosition.RightField));
            Assert.AreEqual(DefensivePosition.Shortstop, ThrowAssignment.Receiver(Base.Third, DefensivePosition.ThirdBase));
            Assert.AreEqual(DefensivePosition.C, ThrowAssignment.Receiver(Base.Home, DefensivePosition.CenterField));
            Assert.AreEqual(DefensivePosition.P, ThrowAssignment.Receiver(Base.Home, DefensivePosition.C));
        }

        [Test]
        public void NoThrowKeepsTheFieldingPlay()
        {
            FieldingPlay fielding = Field(85.0, -8.0, -15.0, -1000.0);
            DefensivePlay play = ThrowPlanner.Plan(fielding, null);
            Assert.IsNull(play.Throw);
            Assert.AreEqual(fielding.EndTime, play.EndTime);
            Assert.AreEqual(BallAuthority.Possessed, play.AuthorityAt(fielding.PossessionTime + 5.0));
            Assert.AreEqual(fielding.BallPositionAt(fielding.PossessionTime + 5.0), play.BallPositionAt(fielding.PossessionTime + 5.0));
        }

        [Test]
        public void ProfilesFollowTheSourcedValues()
        {
            // Transfer: infield 0.70 s, outfield 1.00 s, catcher 0.735 s; routine speed 0.85 × Statcast arm strength.
            Assert.AreEqual(0.70, ThrowProfile.For(DefensivePosition.Shortstop).TransferTime, 1e-12);
            Assert.AreEqual(0.70, ThrowProfile.For(DefensivePosition.P).TransferTime, 1e-12);
            Assert.AreEqual(1.00, ThrowProfile.For(DefensivePosition.CenterField).TransferTime, 1e-12);
            Assert.AreEqual(0.735, ThrowProfile.For(DefensivePosition.C).TransferTime, 1e-12);
            Assert.AreEqual(0.85 * 86.1 * 0.44704, ThrowProfile.For(DefensivePosition.Shortstop).Speed, 1e-9);
            Assert.AreEqual(0.85 * 90.7 * 0.44704, ThrowProfile.For(DefensivePosition.RightField).Speed, 1e-9);
        }

        [Test]
        public void DefaultTargets()
        {
            Assert.AreEqual(Base.First, ThrowPlanner.DefaultTarget(Field(85.0, -8.0, -15.0, -1000.0)), "SS grounder → first");
            Assert.AreEqual(Base.First, ThrowPlanner.DefaultTarget(Field(60.0, -20.0, 5.0, -800.0)), "pitcher → first");
            Assert.AreEqual(Base.Second, ThrowPlanner.DefaultTarget(Field(98.0, 9.0, -24.0, 900.0)), "LF single → second");
            FieldingPlay first = Field(80.0, -8.0, 38.0, -900.0);
            Assert.AreEqual(DefensivePosition.FirstBase, first.Primary);
            Assert.AreEqual(Base.Second, ThrowPlanner.DefaultTarget(first), "1B → second (no unassisted put-outs yet)");
            FieldingPlay fly = Field(92.0, 32.0, 0.0, 2400.0);
            Assert.AreEqual(InterceptKind.FlyCatch, fly.Intercept.Kind);
            Assert.IsNull(ThrowPlanner.DefaultTarget(fly), "no throw after a catch on the fly");
        }

        [Test]
        public void FirstBasemanThrowsToTheSecondBasemanCoveringFirst()
        {
            // 1B ranges to his right; the 2B covers first and takes the throw there.
            FieldingPlay fielding = Field(80.0, -8.0, 38.0, -900.0);
            DefensivePlay play = ThrowPlanner.Plan(fielding, Base.First);
            Assert.AreEqual(DefensivePosition.SecondBase, play.Throw.Receiver);
            Assert.IsTrue(play.Throw.Caught);
            AssertTimeline(play);
        }

        [Test]
        public void NoThrowWhenNobodyFieldedIt()
        {
            FieldingPlay homeRun = Field(110.0, 28.0, 0.0, 2200.0);
            Assert.AreEqual(FieldingOutcome.OutOfPlay, homeRun.Outcome);
            Assert.IsNull(ThrowPlanner.DefaultTarget(homeRun));
            DefensivePlay play = ThrowPlanner.Plan(homeRun, Base.Second);
            Assert.IsNull(play.Throw);
            Assert.AreEqual(homeRun.EndTime, play.EndTime, 0.0);
        }

        [Test]
        public void EveryThrowHasAnotherReceiver()
        {
            foreach (Base b in Enum.GetValues(typeof(Base)))
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
                Assert.AreNotEqual(p, ThrowAssignment.Receiver(b, p), $"{p} throwing to {b}");
        }

        [Test]
        public void IdenticalPlaysGiveIdenticalThrows()
        {
            foreach (var (launch, target) in new[] { ((95.0, 6.0, 0.0, 700.0), Base.Home), ((60.0, -20.0, 5.0, -800.0), Base.First) })
            {
                DefensivePlay a = ThrowPlanner.Plan(Field(launch.Item1, launch.Item2, launch.Item3, launch.Item4), target);
                DefensivePlay b = ThrowPlanner.Plan(Field(launch.Item1, launch.Item2, launch.Item3, launch.Item4), target);
                Assert.AreEqual(a.Throw.ReleaseTime, b.Throw.ReleaseTime, 0.0);
                Assert.AreEqual(a.Throw.Flight.First.Velocity, b.Throw.Flight.First.Velocity);
                Assert.AreEqual(a.Throw.Catch.Time, b.Throw.Catch.Time, 0.0);
                for (double t = a.Fielding.Ball.First.Time; t < a.EndTime + 1.0; t += 0.05)
                {
                    Assert.AreEqual(a.BallPositionAt(t), b.BallPositionAt(t), $"ball {t:0.00}");
                    Assert.AreEqual(a.FielderPositionAt(a.Throw.Receiver, t), b.FielderPositionAt(b.Throw.Receiver, t), $"receiver {t:0.00}");
                    Assert.AreEqual(a.HolderAt(t), b.HolderAt(t));
                }
            }
        }
    }
}
