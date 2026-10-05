using System;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>
    /// Fair/foul calls (Official Baseball Rules, fair/foul ball definitions) on synthetic plays on and near the lines: the
    /// line is fair, the ball counts if any part of it is over the line, landings beyond the bases decide at once, before
    /// the bases the ball is judged where it passes the base or where it settles, home runs where they leave the field.
    /// </summary>
    public class FairFoulTests
    {
        private static readonly double R = BallProperties.Baseball.Radius;
        private static readonly double Base = FieldLayout.BaseDistance;

        /// <summary>A point at distance <paramref name="along"/> along the first-base line, <paramref name="outside"/> metres
        /// outside it (negative: inside fair territory).</summary>
        private static Vector3d FirstBaseSide(double along, double outside, double z = 0.0)
        {
            double s = Math.Sqrt(0.5);
            return new Vector3d(s * along + s * outside, s * along - s * outside, z == 0.0 ? R : z);
        }

        /// <summary>Straight-line play through the points (one second apart); a ground impact at <paramref name="landingIndex"/>.</summary>
        private static BallInPlay Play(Vector3d[] points, int landingIndex, BallPhase end, BallEventKind? extra = null, int extraIndex = -1)
        {
            var states = new BallState[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3d v = i + 1 < points.Length ? points[i + 1] - points[i] : Vector3d.Zero;
                states[i] = new BallState(i, points[i], v, Vector3d.Zero);
            }

            var segments = new[] { new BallSegment(BallPhase.Airborne, new TrajectoryResult(states, FlightEnd.ReachedGround)) };
            var events = new System.Collections.Generic.List<BallEvent>();
            if (landingIndex >= 0) events.Add(new BallEvent(BallEventKind.GroundImpact, states[landingIndex], states[landingIndex], SurfaceKind.NaturalGrass));
            if (extra.HasValue) events.Add(new BallEvent(extra.Value, states[extraIndex], states[extraIndex], SurfaceKind.Wall));
            events.Sort((a, b) => a.Time.CompareTo(b.Time));
            return new BallInPlay(segments, events.ToArray(), end);
        }

        [TestCase(-0.02, BallInPlayCall.Fair)]   // 2 cm inside the line
        [TestCase(0.0, BallInPlayCall.Fair)]     // on the line
        [TestCase(0.03, BallInPlayCall.Fair)]    // centre 3 cm outside: part of the ball (r 3.7 cm) still over the line
        [TestCase(0.04, BallInPlayCall.Foul)]    // whole ball outside
        public void FirstLandingBeyondFirstBaseDecides(double outside, BallInPlayCall expected)
        {
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), FirstBaseSide(Base + 10.0, outside), FirstBaseSide(Base + 30.0, outside + 20.0) }, 1, BallPhase.Rest);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(expected, call.Call);
            Assert.AreEqual(CallBasis.FirstLandingBeyondBase, call.Basis);
        }

        [Test]
        public void LandsFoulBeforeTheBaseAndSettlesFair()
        {
            // Chopper lands foul 40 ft up the line, spins back into fair territory and stops: fair (judged where it settles).
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), FirstBaseSide(12.0, 0.5), FirstBaseSide(14.0, -0.3) }, 1, BallPhase.Rest);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(BallInPlayCall.Fair, call.Call);
            Assert.AreEqual(CallBasis.Settled, call.Basis);
        }

        [Test]
        public void LandsFairBeforeTheBaseAndRollsPastItFoul()
        {
            // Lands fair, then crosses the line before first base and bounds past the base outside it: foul.
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), FirstBaseSide(15.0, -0.5), FirstBaseSide(Base + 5.0, 1.5) }, 1, BallPhase.Rest);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(BallInPlayCall.Foul, call.Call);
            Assert.AreEqual(CallBasis.PassingBase, call.Basis);
            Assert.AreEqual(Base * Math.Sqrt(0.5), call.At.Position.Y, 1e-9, "judged as it passes the base (the line through first and third)");
        }

        [Test]
        public void LandsFoulBeforeTheBaseAndRollsPastItOverTheLine()
        {
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), FirstBaseSide(15.0, 0.3), FirstBaseSide(Base + 5.0, -0.3) }, 1, BallPhase.Rest);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(BallInPlayCall.Fair, call.Call, "over fair territory (the line) as it passes first base");
            Assert.AreEqual(CallBasis.PassingBase, call.Basis);
        }

        [TestCase(-0.5, BallInPlayCall.HomeRun)]
        [TestCase(0.03, BallInPlayCall.HomeRun)]   // past the foul pole by less than a ball radius: off the pole, fair
        [TestCase(0.5, BallInPlayCall.Foul)]
        public void OverTheFenceIsJudgedWhereItLeaves(double outside, BallInPlayCall expected)
        {
            Vector3d over = FirstBaseSide(101.0, outside, 6.0);
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), over, FirstBaseSide(120.0, outside, R) }, 2, BallPhase.OutOfPlay, BallEventKind.ClearedFence, 1);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(expected, call.Call);
            Assert.AreEqual(CallBasis.OverTheFence, call.Basis);
        }

        [Test]
        public void BallBehindThePlateIsFoul()
        {
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), new Vector3d(1.0, -8.0, R) }, 1, BallPhase.Rest);
            Assert.AreEqual(BallInPlayCall.Foul, FairFoul.Call(play).Call);
        }

        [Test]
        public void SimulatedPlaysAreCalledConsistently()
        {
            // End-to-end on the real simulation: a ball up the middle is fair, one pulled far down the line is foul.
            BallState Launch(double spray) => new Simulation.Batting.BattedBallLaunch(95.0, 12.0, spray, 1200.0).ToState(new Vector3d(0.0, 0.7, 0.8));
            Assert.IsTrue(FairFoul.Call(BallInPlaySimulation.Run(Launch(0.0), EnvironmentState.Standard, FieldLayout.Standard)).IsFair);
            Assert.AreEqual(BallInPlayCall.Foul, FairFoul.Call(BallInPlaySimulation.Run(Launch(-55.0), EnvironmentState.Standard, FieldLayout.Standard)).Call);
        }

        private static Vector3d ThirdBaseSide(double along, double outside)
        {
            Vector3d p = FirstBaseSide(along, outside);
            return new Vector3d(-p.X, p.Y, p.Z);
        }

        [TestCase(0.03, BallInPlayCall.Fair)]
        [TestCase(0.04, BallInPlayCall.Foul)]
        public void ThirdBaseSideIsTheMirror(double outside, BallInPlayCall expected)
        {
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), ThirdBaseSide(Base + 10.0, outside), ThirdBaseSide(Base + 30.0, outside + 20.0) }, 1, BallPhase.Rest);
            Assert.AreEqual(expected, FairFoul.Call(play).Call);
        }

        [Test]
        public void LandingAtTheBaseDistanceIsBeyondTheBase()
        {
            // Just past the base, landing 0.5 m foul: decided at once. Just short of it and staying there: judged where it
            // settles. "Past the base" = past the line through first and third base (y = 90 ft·√½), which meets the foul
            // line at the bag.
            double yb = Base * Math.Sqrt(0.5), foul = 0.5 * Math.Sqrt(2.0);
            BallInPlay past = Play(new[] { new Vector3d(0, 0.7, 0.9), new Vector3d(yb + 1e-6 + foul, yb + 1e-6, R) }, 1, BallPhase.Rest);
            Assert.AreEqual(CallBasis.FirstLandingBeyondBase, FairFoul.Call(past).Basis);
            BallInPlay shortOf = Play(new[] { new Vector3d(0, 0.7, 0.9), new Vector3d(yb - 1e-3 + foul, yb - 1e-3, R) }, 1, BallPhase.Rest);
            Assert.AreEqual(CallBasis.Settled, FairFoul.Call(shortOf).Basis);
            Assert.AreEqual(BallInPlayCall.Foul, FairFoul.Call(shortOf).Call);
        }

        [Test]
        public void FairLandingInShallowCentreIsPastTheBasesEvenIfItRollsFoul()
        {
            // Codex review: lands 100 ft straight toward centre (past the first–third line), then rolls into foul ground.
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), new Vector3d(0.0, 30.48, R), new Vector3d(30.0, 25.0, R) }, 1, BallPhase.Rest);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(BallInPlayCall.Fair, call.Call);
            Assert.AreEqual(CallBasis.FirstLandingBeyondBase, call.Basis);
        }

        [TestCase(0.03, BallInPlayCall.Fair)]
        [TestCase(0.045, BallInPlayCall.Foul)]
        public void PassingTheBaseOverTheLineCountsAnyPartOfTheBall(double outsideAtBase, BallInPlayCall expected)
        {
            // Lands fair before the base and rolls straight along the line at a constant offset past the base.
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), FirstBaseSide(15.0, outsideAtBase), FirstBaseSide(Base + 5.0, outsideAtBase) }, 1, BallPhase.Rest);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(CallBasis.PassingBase, call.Basis);
            Assert.AreEqual(expected, call.Call);
        }

        [Test]
        public void SettlingJustBehindThePlatesRearPointIsFoul()
        {
            // 5 cm straight behind the apex: farther than a ball radius from any fair ground.
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), new Vector3d(0.0, -0.05, R) }, 1, BallPhase.Rest);
            Assert.AreEqual(BallInPlayCall.Foul, FairFoul.Call(play).Call);
            Assert.AreEqual(0.05, FieldLayout.OutsideFoulLine(0.0, -0.05), 1e-12);
        }

        [Test]
        public void FairBounceOverTheFenceIsAGroundRuleDoubleNotAHomeRun()
        {
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), FirstBaseSide(95.0, -10.0), FirstBaseSide(101.0, -10.0, 4.0), FirstBaseSide(110.0, -10.0) },
                1, BallPhase.OutOfPlay, BallEventKind.ClearedFence, 2);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(BallInPlayCall.Fair, call.Call);
            Assert.AreEqual(CallBasis.FirstLandingBeyondBase, call.Basis);
        }

        [Test]
        public void OffTheWallOnTheFlyIsFairWhereverItLands()
        {
            // A corner carom near the pole that comes down foul beyond first base.
            BallInPlay play = Play(new[] { new Vector3d(0, 0.7, 0.9), FirstBaseSide(100.0, 0.01, 2.0), FirstBaseSide(95.0, 3.0) },
                2, BallPhase.Rest, BallEventKind.WallImpact, 1);
            FairFoulResult call = FairFoul.Call(play);
            Assert.AreEqual(BallInPlayCall.Fair, call.Call);
            Assert.AreEqual(CallBasis.OffTheWall, call.Basis);
        }

        [TestCase(0.03, 5.0, BallInPlayCall.HomeRun)]   // over the pole by part of the ball: home run
        [TestCase(0.06, 5.0, BallInPlayCall.Foul)]      // wholly outside: foul, no fence there
        [TestCase(0.03, 1.5, BallInPlayCall.Fair)]      // into the pole/fence below its top: off the wall, fair
        public void SimulatedBallGrazingTheRightFieldPole(double outside, double height, BallInPlayCall expected)
        {
            // Straight down the line in vacuum (no aero), from 300 ft, centre a few cm outside the line.
            Vector3d start = FirstBaseSide(300.0 * 0.3048, outside, height);
            var dir = new Vector3d(Math.Sqrt(0.5), Math.Sqrt(0.5), 0.0);
            var s = new BallState(0.0, start, 40.0 * dir, Vector3d.Zero);
            BallInPlay play = BallInPlaySimulation.Run(s, EnvironmentState.Vacuum, FieldLayout.Standard, AerodynamicModel.None);
            Assert.AreEqual(expected, FairFoul.Call(play).Call);
        }
    }
}
