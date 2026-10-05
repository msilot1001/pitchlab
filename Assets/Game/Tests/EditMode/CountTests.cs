using System;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    /// <summary>TASK-012: the ball/strike count, the walk, and the called-strike zone.</summary>
    public class CountTests
    {
        [Test]
        public void FourBallsWalkAndThreeStrikesRetire()
        {
            var c = new Count();
            for (int i = 1; i < Count.BallsForWalk; i++)
            {
                var next = c.After(PitchOutcome.Ball);
                c = next.Next;
                Assert.AreEqual((i, PlateAppearanceEnd.None), (c.Balls, next.End));
            }

            Assert.AreEqual((new Count(), PlateAppearanceEnd.Walk), c.After(PitchOutcome.Ball), "ball four");
            Assert.AreEqual((new Count(0, 2), PlateAppearanceEnd.None), new Count(0, 1).After(PitchOutcome.SwingingStrike));
            Assert.AreEqual((new Count(), PlateAppearanceEnd.Strikeout), new Count(3, 2).After(PitchOutcome.CalledStrike), "strike three, full count");
            Assert.AreEqual((new Count(), PlateAppearanceEnd.Strikeout), new Count(1, 2).After(PitchOutcome.SwingingStrike));
            Assert.AreEqual((new Count(), PlateAppearanceEnd.InPlay), new Count(2, 1).After(PitchOutcome.InPlay));
            // Each pitch moves only its own half of the count.
            Assert.AreEqual((new Count(2, 2), PlateAppearanceEnd.None), new Count(2, 1).After(PitchOutcome.CalledStrike));
            Assert.AreEqual((new Count(2, 2), PlateAppearanceEnd.None), new Count(1, 2).After(PitchOutcome.Ball));
            Assert.AreEqual((new Count(0, 1), PlateAppearanceEnd.None), new Count().After(PitchOutcome.Foul));
        }

        [Test]
        public void AFoulIsAStrikeOnlyBelowTwoStrikes()
        {
            Assert.AreEqual((new Count(1, 1), PlateAppearanceEnd.None), new Count(1, 0).After(PitchOutcome.Foul));
            Assert.AreEqual((new Count(1, 2), PlateAppearanceEnd.None), new Count(1, 1).After(PitchOutcome.Foul));
            Assert.AreEqual((new Count(1, 2), PlateAppearanceEnd.None), new Count(1, 2).After(PitchOutcome.Foul), "no strikeout on a foul");
            Assert.Throws<ArgumentOutOfRangeException>(() => new Count(4, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Count(0, 3));
        }

        [TestCase(false, false, false, true, false, false, 0)]
        [TestCase(true, false, false, true, true, false, 0)]
        [TestCase(false, true, false, true, true, false, 0)]   // not forced: stays on second
        [TestCase(false, false, true, true, false, true, 0)]   // not forced: stays on third
        [TestCase(true, false, true, true, true, true, 0)]
        [TestCase(false, true, true, true, true, true, 0)]
        [TestCase(true, true, false, true, true, true, 0)]
        [TestCase(true, true, true, true, true, true, 1)]      // forced in
        public void AWalkAdvancesOnlyForcedRunners(bool r1, bool r2, bool r3, bool a1, bool a2, bool a3, int runs)
        {
            BaseOccupancy after = Count.Walk(new BaseOccupancy(r1, r2, r3), out int scored);
            Assert.AreEqual(new BaseOccupancy(a1, a2, a3), after);
            Assert.AreEqual(runs, scored);
        }

        [Test]
        public void TheZoneIsThePlateAndTheDefaultHeightsWidenedByTheBall()
        {
            double r = BallProperties.Baseball.Radius, mid = 0.5 * (StrikeZone.Bottom + StrikeZone.Top);
            Assert.IsTrue(StrikeZone.Contains(StrikeZone.HalfWidth + r - 1e-4, mid), "the ball's edge clips the plate");
            Assert.IsFalse(StrikeZone.Contains(StrikeZone.HalfWidth + r + 1e-4, mid));
            Assert.IsTrue(StrikeZone.Contains(-(StrikeZone.HalfWidth + r - 1e-4), mid));
            Assert.IsTrue(StrikeZone.Contains(0.0, StrikeZone.Top + r - 1e-4));
            Assert.IsFalse(StrikeZone.Contains(0.0, StrikeZone.Top + r + 1e-4));
            Assert.IsTrue(StrikeZone.Contains(0.0, StrikeZone.Bottom - r + 1e-4));
            Assert.IsFalse(StrikeZone.Contains(0.0, StrikeZone.Bottom - r - 1e-4));
        }

        [Test]
        public void EveryPresetAtEveryTargetIsCalledAsAimed()
        {
            // The physics decides where each aimed pitch crosses; the zone targets are strikes and the ball targets balls for
            // every preset (the call reads the crossing, never the target).
            for (int preset = 0; preset < PitchPresets.All.Length; preset++)
                foreach (PitchTarget target in PitchTargets.All)
                {
                    HittingPitch pitch = PitchTargets.Create(new PitchCommand(preset, target), StrikeZone.Bottom, StrikeZone.Top, EnvironmentState.Standard);
                    (double x, double z) = StrikeZone.Crossing(pitch);
                    bool aimedAtZone = PitchTargets.InZone(target);
                    Assert.AreEqual(aimedAtZone, StrikeZone.IsStrike(pitch), $"{PitchPresets.All[preset].Label} {target}: crosses at x {x:0.000} z {z:0.000}");
                    Assert.AreEqual(aimedAtZone ? PitchOutcome.CalledStrike : PitchOutcome.Ball, PitchOutcomes.Of(pitch, null, null, null));
                    // The aim converges on the target (a few centimetres at most), from a pure release-angle change.
                    (double tx, double tz) = PitchTargets.Point(target, StrikeZone.Bottom, StrikeZone.Top);
                    Assert.Less(System.Math.Sqrt((x - tx) * (x - tx) + (z - tz) * (z - tz)), 0.03, $"{PitchPresets.All[preset].Label} {target}");
                    Assert.AreEqual(PitchPresets.All[preset].SpeedMph, Units.MetersPerSecondToMph(pitch.Flight.First.Velocity.Length), 1e-9, "speed is the preset's");
                }
        }

        [Test]
        public void TargetsSitInsideTheZoneWithAMarginOrClearlyOutside()
        {
            double r = BallProperties.Baseball.Radius;
            foreach (PitchTarget t in PitchTargets.All)
            {
                (double x, double z) = PitchTargets.Point(t, StrikeZone.Bottom, StrikeZone.Top);
                // Inside: ≥ 10 cm from the (ball-widened) edges; outside: ≥ 10 cm beyond them.
                double margin = System.Math.Min(StrikeZone.HalfWidth + r - System.Math.Abs(x), System.Math.Min(z - (StrikeZone.Bottom - r), StrikeZone.Top + r - z));
                if (PitchTargets.InZone(t)) Assert.Greater(margin, 0.1, t.ToString());
                else Assert.Less(margin, -0.1, t.ToString());
            }

            // Relative to the batter's zone: exact positions for a zone from 0.5 to 1.2 m.
            Assert.AreEqual((0.0, 0.85 + 0.6 * 0.35), PitchTargets.Point(PitchTarget.UpMiddle, 0.5, 1.2));
            Assert.AreEqual((-0.6 * StrikeZone.HalfWidth, 0.85 - 0.6 * 0.35), PitchTargets.Point(PitchTarget.DownLeft, 0.5, 1.2));
            Assert.AreEqual((0.0, 1.2 + PitchTargets.Beyond), PitchTargets.Point(PitchTarget.BallUp, 0.5, 1.2));
            Assert.AreEqual((StrikeZone.HalfWidth + PitchTargets.Wide, 0.85), PitchTargets.Point(PitchTarget.BallRight, 0.5, 1.2));
            // Catcher's view: left is the third-base side.
            Assert.Less(PitchTargets.Point(PitchTarget.MiddleLeft, StrikeZone.Bottom, StrikeZone.Top).X, 0.0);
            Assert.Greater(PitchTargets.Point(PitchTarget.UpMiddle, StrikeZone.Bottom, StrikeZone.Top).Z, PitchTargets.Point(PitchTarget.DownMiddle, StrikeZone.Bottom, StrikeZone.Top).Z);
        }

        [Test]
        public void TheCrossingIsWhereTheFlightPassesTheFrontOfThePlate()
        {
            HittingPitch pitch = HittingPitch.Create(PitchPresets.FourSeam, EnvironmentState.Standard);
            (double x, double z) = StrikeZone.Crossing(pitch);
            PitchMetrics metrics = PitchSimulation.Run(PitchPresets.FourSeam, EnvironmentState.Standard).Metrics;
            // PitchMetrics stops its flight at the same plane (Statcast plate_x / plate_z).
            Assert.AreEqual(metrics.PlateX, x, 1e-3);
            Assert.AreEqual(metrics.PlateZ, z, 1e-3);
        }

        [Test]
        public void ASwingWithoutContactIsASwingingStrikeWhereverThePitchIs()
        {
            HittingPitch ball = PitchTargets.Create(new PitchCommand(0, PitchTarget.BallUp), StrikeZone.Bottom, StrikeZone.Top, EnvironmentState.Standard);
            Assert.IsFalse(StrikeZone.IsStrike(ball));
            var swing = new SwingInput(ball.IdealContactTime - 0.15, 0.0, 0.0);
            ContactResult miss = ContactResolver.Resolve(ball, swing, SwingParameters.Default);
            Assert.IsFalse(miss.IsContact);
            Assert.AreEqual(PitchOutcome.SwingingStrike, PitchOutcomes.Of(ball, swing, miss, null));
        }
    }
}
