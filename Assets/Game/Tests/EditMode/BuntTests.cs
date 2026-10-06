using System;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-025: bunting — the bunt's contact (the same collision with a squared, nearly still bat), its rules (a foul bunt is
    /// a strike even on two strikes; the sacrifice), the CPU's sacrifice and its bunt.
    /// </summary>
    public class BuntTests
    {
        private static readonly EnvironmentState Env = EnvironmentState.Standard;
        private static readonly PlayerProfile Batter = GenericRosters.Home().Lineup[9];
        private static readonly SwingParameters BuntBat = SwingParameters.Bunt(Batter);

        private static HittingPitch Aimed(double x, double z, PitchInput preset) => HittingPitch.Create(PitchTargets.Aim(preset, x, z, Env), Env);

        private static SwingInput BuntAt(HittingPitch pitch, double dz = 0.004, double aim = 0.0, double square = 0.05, double pullBack = double.NaN)
        {
            Vector3d at = pitch.IdealContactState.Position;
            return SwingInput.Bunt(square, at.X, at.Z + dz, aim, pullBack);
        }

        // ── Contact ───────────────────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void TheBuntMeetsTheBallWhenItArrives()
        {
            HittingPitch pitch = Aimed(0.0, 0.75, PitchPresets.FourSeam);
            ContactResult early = ContactResolver.Resolve(pitch, BuntAt(pitch, square: 0.05), BuntBat);
            ContactResult later = ContactResolver.Resolve(pitch, BuntAt(pitch, square: pitch.IdealContactTime - BuntBat.SwingDuration), BuntBat);
            Assert.IsTrue(early.IsContact);
            Assert.AreEqual(pitch.IdealContactTime, early.BattedBall.Time, 1e-12, "no swing timing: the bat waits for the ball");
            Assert.AreEqual(early.BattedBall.Velocity, later.BattedBall.Velocity, "squared any time before he must be set");
            Assert.AreEqual(ContactOutcome.MissTiming, ContactResolver.Resolve(pitch, BuntAt(pitch, square: pitch.IdealContactTime - 0.1), BuntBat).Outcome, "not set in time");
        }

        [Test]
        public void ABuntIsSoftAndOnTheGround()
        {
            HittingPitch pitch = Aimed(0.0, 0.75, PitchPresets.FourSeam);
            ContactResult bunt = ContactResolver.Resolve(pitch, BuntAt(pitch), BuntBat);
            SwingParameters swing = SwingParameters.For(Batter);
            ContactResult swung = ContactResolver.Resolve(pitch, new SwingInput(pitch.IdealContactTime - swing.SwingDuration, pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z), swing);
            double mph = Units.MetersPerSecondToMph(bunt.ExitSpeed);
            TestContext.WriteLine($"bunt {mph:0.0} mph at {bunt.LaunchAngleDegrees:0}°; swing {Units.MetersPerSecondToMph(swung.ExitSpeed):0.0} mph");
            Assert.That(mph, Is.InRange(20.0, 45.0), "MLB sacrifice bunts ≈ 34 mph");
            Assert.Less(bunt.LaunchAngleDegrees, 0.0, "the bat over the ball's centre keeps it down");
            Assert.Less(bunt.ExitSpeed, 0.5 * swung.ExitSpeed);
        }

        [Test]
        public void TheBatAngleSteersTheBunt()
        {
            double Spray(double aimDeg)
            {
                double sum = 0.0;
                foreach (PitchInput preset in new[] { PitchPresets.FourSeam, PitchPresets.Sinker, PitchPresets.Slider, PitchPresets.Changeup })
                {
                    HittingPitch pitch = Aimed(0.0, 0.75, preset);
                    sum += ContactResolver.Resolve(pitch, BuntAt(pitch, aim: Units.DegreesToRadians(aimDeg)), BuntBat).SprayAngleDegrees;
                }

                return sum / 4.0;
            }

            double toFirst = Spray(5.0), straight = Spray(0.0), toThird = Spray(-5.0);
            TestContext.WriteLine($"spray: {toThird:0}° / {straight:0}° / {toFirst:0}°");
            Assert.Greater(toFirst, straight + 10.0, "angled toward first base: to the right side");
            Assert.Less(toThird, straight - 10.0, "toward third: to the left side");
            Assert.That(toFirst, Is.InRange(10.0, 45.0), "fair, toward the line");
            Assert.That(toThird, Is.InRange(-45.0, -10.0));
        }

        [Test]
        public void ABuntPulledBackInTimeIsNoAttempt()
        {
            HittingPitch pitch = Aimed(0.3, 0.75, PitchPresets.FourSeam);
            double deadline = SwingInput.PullBackDeadline(pitch.IdealContactTime);
            ContactResult pulled = ContactResolver.Resolve(pitch, BuntAt(pitch, pullBack: deadline), BuntBat);
            Assert.AreEqual(ContactOutcome.CheckedSwing, pulled.Outcome);
            Assert.IsFalse(PitchOutcomes.Offered(BuntAt(pitch, pullBack: deadline), pulled));
            Assert.AreEqual(PitchOutcome.Ball, PitchOutcomes.BeforePlay(pitch, BuntAt(pitch, pullBack: deadline), pulled, BuntBat.SwingDuration, null, StrikeZone.Bottom, StrikeZone.Top));
            Assert.AreNotEqual(ContactOutcome.CheckedSwing, ContactResolver.Resolve(pitch, BuntAt(pitch, pullBack: deadline + 0.001), BuntBat).Outcome, "too late: the bunt goes on");
        }

        [Test]
        public void ABuntAngleMustBeFiniteAndBelow45Degrees()
        {
            HittingPitch pitch = Aimed(0.0, 0.75, PitchPresets.FourSeam);
            Assert.AreEqual(ContactOutcome.InvalidInput, ContactResolver.Resolve(pitch, BuntAt(pitch, aim: double.NaN), BuntBat).Outcome);
            Assert.AreEqual(ContactOutcome.InvalidInput, ContactResolver.Resolve(pitch, BuntAt(pitch, aim: Units.DegreesToRadians(50.0)), BuntBat).Outcome);
            Assert.IsTrue(ContactResolver.Resolve(pitch, BuntAt(pitch, aim: Units.DegreesToRadians(10.0)), BuntBat).IsContact);
        }

        [Test]
        public void ABuntCanBePulledBackOnAPitchThatFallsShort()
        {
            HittingPitch dirt = Aimed(0.0, -0.4, PitchPresets.Curveball);
            Assume.That(dirt.ReachesContactPlane, Is.False, "in the dirt before the plate");
            double deadline = SwingInput.PullBackDeadline(ContactResolver.BuntArrival(dirt));
            Vector3d end = dirt.Flight.Final.Position;
            var pulled = SwingInput.Bunt(0.05, end.X, 0.5, 0.0, deadline - 0.01);
            ContactResult r = ContactResolver.Resolve(dirt, pulled, BuntBat);
            Assert.AreEqual(ContactOutcome.CheckedSwing, r.Outcome, "pulled back: no attempt");
            Assert.AreEqual(PitchOutcome.Ball, PitchOutcomes.BeforePlay(dirt, pulled, r, BuntBat.SwingDuration, null, StrikeZone.Bottom, StrikeZone.Top));
            var offered = SwingInput.Bunt(0.05, end.X, 0.5, 0.0);
            Assert.AreEqual(PitchOutcome.SwingingStrike, PitchOutcomes.BeforePlay(dirt, offered, ContactResolver.Resolve(dirt, offered, BuntBat), BuntBat.SwingDuration, null, StrikeZone.Bottom, StrikeZone.Top),
                "left out over a ball in the dirt: an attempt");
        }

        [Test]
        public void HisBuntStaysInTheAimableArea()
        {
            // As a human's PCI (and the labs' track) does: the simulator and the labs take the same bat position.
            HittingPitch wide = Aimed(1.2, 0.2, PitchPresets.Slider);
            SeedStream stream = CpuBatter.StreamFor(9, Batter.Id, 1, 1);
            BatterPlan plan = CpuBatter.PlanBunt(CpuBatter.Observe(wide), Batter, BuntBat, wide.ContactPlaneY, 0.0, ref stream);
            foreach (AimEvent e in plan.Aim)
            {
                (double u, double v) = PciFrame.Default.ToNormalized(e.X, e.Z);
                Assert.AreEqual((e.X, e.Z), PciFrame.Default.ToMeters(u, v), "inside the area");
            }
        }

        [Test]
        public void AMissedBuntIsAStrike()
        {
            HittingPitch pitch = Aimed(0.0, 0.75, PitchPresets.FourSeam);
            SwingInput over = BuntAt(pitch, dz: 0.2);
            ContactResult r = ContactResolver.Resolve(pitch, over, BuntBat);
            Assert.IsFalse(r.IsContact);
            Assert.AreEqual(PitchOutcome.SwingingStrike, PitchOutcomes.BeforePlay(pitch, over, r, BuntBat.SwingDuration, null, StrikeZone.Bottom, StrikeZone.Top));
        }

        // ── Rules ─────────────────────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void AFoulBuntIsAStrikeEvenWithTwoStrikes()
        {
            Assert.AreEqual((new Count(0, 1), PlateAppearanceEnd.None), new Count(0, 0).After(PitchOutcome.FoulBunt));
            Assert.AreEqual((default(Count), PlateAppearanceEnd.Strikeout), new Count(2, 2).After(PitchOutcome.FoulBunt), "OBR 5.09(a)(4)");
            Assert.AreEqual((new Count(2, 2), PlateAppearanceEnd.None), new Count(2, 2).After(PitchOutcome.Foul), "a foul swing is not");
        }

        private static LivePlay Play(GameState g, double mph, double launch, double spray, double spin)
        {
            Situation s = g.Situation;
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(new Vector3d(0.0, 0.7, 0.8)), Env, FieldLayout.Standard);
            var play = new LivePlay(FieldingSolver.Solve(ball, s.Alignment, g.Personnel.Fielder, FieldLayout.Standard), s, personnel: g.Personnel);
            play.RunToEnd();
            return play;
        }

        [Test]
        public void TheGameRecordsAFoulBuntStrikeoutAndASacrifice()
        {
            var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), 3);
            g.Pitch(PitchOutcome.CalledStrike);
            g.Pitch(PitchOutcome.CalledStrike);
            LivePlay foul = Play(g, 30.0, -30.0, 75.0, -800.0);
            Assume.That(foul.IsFoul, "a bunt rolling foul");
            Assert.AreEqual(PlateAppearanceEnd.Strikeout, g.Apply(foul, default, bunt: true));
            StringAssert.Contains("foul bunt", g.Completed.Last().Result);

            // A sacrifice: a bunt fielded by the first baseman, the batter out at first, the runner to second.
            g.Set(8, Half.Top, 0, new BaseOccupancy(true, false, false), 0, 0);
            LivePlay sac = null;
            foreach (double spray in new[] { 30.0, 25.0, 35.0, 20.0 })
                foreach (double mph in new[] { 30.0, 25.0, 35.0 })
                {
                    LivePlay p = Play(g, mph, -30.0, spray, -800.0);
                    if (!p.IsFoul && PlayResults.Bunted(PlayResults.Classify(p), p) == PlayResultKind.SacrificeBunt) { sac = p; break; }
                }

            Assert.IsNotNull(sac, "a sacrifice among the right-side bunts");
            Assert.AreEqual(PlateAppearanceEnd.InPlay, g.Apply(sac, default, bunt: true));
            Assert.AreEqual(PlayResultKind.SacrificeBunt, g.Completed.Last().PlayResult);
            Assert.AreEqual(1, g.Outs);
            Assert.IsTrue(g.Bases.Second);
        }

        [Test]
        public void OnlyABuntCanBeASacrifice()
        {
            var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), 3);
            g.Set(8, Half.Top, 0, new BaseOccupancy(true, false, false), 0, 0);
            foreach (double spray in new[] { 30.0, 25.0, 35.0, 20.0 })
            {
                LivePlay p = Play(g, 30.0, -30.0, spray, -800.0);
                if (p.IsFoul) continue;
                PlayResultKind kind = PlayResults.Classify(p);
                Assert.AreNotEqual(PlayResultKind.SacrificeBunt, kind, "a swung grounder is not a sacrifice");
                bool runnerOut = p.Runners.Any(r => !r.Id.IsBatter && r.IsOut);
                if (runnerOut) Assert.AreEqual(kind, PlayResults.Bunted(kind, p), "a runner out: no sacrifice");
            }
        }

        // ── The CPU ───────────────────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void TheSacrificeSpot()
        {
            var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), 3);
            PlayerProfile weak = null;
            for (int i = 0; i < 9 && weak == null; i++)
            {
                if (g.Batter.Ratings.Power <= BuntStrategy.WeakPower) weak = g.Batter;
                else g.Pitch(PitchOutcome.HitByPitch);
            }

            Assume.That(weak, Is.Not.Null, "a weak hitter in the order");
            g.Set(8, g.Half, 0, new BaseOccupancy(true, false, false), 2, 2);
            Assert.IsTrue(BuntStrategy.Spot(g), "nobody out, a runner on first, late, close, a weak hitter");
            Assert.Greater(BuntStrategy.Aim(g), 0.0, "toward first: the first baseman holds the runner");
            g.Set(8, g.Half, 0, new BaseOccupancy(true, true, false), 2, 2);
            Assert.IsTrue(BuntStrategy.Spot(g));
            Assert.Less(BuntStrategy.Aim(g), 0.0, "first and second: toward third");
            g.Set(8, g.Half, 1, new BaseOccupancy(true, false, false), 2, 2);
            Assert.IsFalse(BuntStrategy.Spot(g), "one out");
            g.Set(3, g.Half, 0, new BaseOccupancy(true, false, false), 2, 2);
            Assert.IsFalse(BuntStrategy.Spot(g), "early");
            g.Set(8, g.Half, 0, new BaseOccupancy(true, false, false), 6, 2);
            Assert.IsFalse(BuntStrategy.Spot(g), "not close");
            g.Set(8, g.Half, 0, new BaseOccupancy(true, false, true), 2, 2);
            Assert.IsFalse(BuntStrategy.Spot(g), "a runner on third (no squeeze)");
            g.Set(8, g.Half, 0, new BaseOccupancy(true, false, false), 2, 2);
            g.Pitch(PitchOutcome.CalledStrike);
            g.Pitch(PitchOutcome.CalledStrike);
            Assert.IsFalse(BuntStrategy.Spot(g), "two strikes: a foul bunt would be strike three");
        }

        [Test]
        public void HeBuntsStrikesAndPullsBackBalls()
        {
            int bunted = 0, pulled = 0;
            for (int k = 0; k < 60; k++)
            {
                bool strike = k % 2 == 0;
                HittingPitch pitch = strike ? Aimed(0.05 * ((k % 5) - 2), 0.75, PitchPresets.FourSeam) : Aimed(0.55 * (k % 4 < 2 ? 1 : -1), 0.6, PitchPresets.Slider);
                SeedStream stream = CpuBatter.StreamFor(9, Batter.Id, k, 1);
                BatterPlan plan = CpuBatter.PlanBunt(CpuBatter.Observe(pitch), Batter, BuntBat, pitch.ContactPlaneY, 0.05, ref stream);
                Assert.IsTrue(plan.IsBunt && plan.Swing);
                Assert.AreEqual(CpuBatter.Latency(Batter.Ratings), plan.SwingStart, 1e-12, "he squares as he sees the release");
                SwingInput input = plan.InputFor(pitch).Value;
                Assert.AreEqual(plan.PciAt(pitch.IdealContactTime), (input.PciX, input.PciZ), "the bat where his aim was when the ball arrived");
                ContactResult r = ContactResolver.Resolve(pitch, input, BuntBat);
                if (strike)
                {
                    if (double.IsNaN(plan.CheckTime)) bunted++;   // he may misjudge one (his perception)
                }
                else if (!double.IsNaN(plan.CheckTime))
                {
                    Assert.AreEqual(ContactOutcome.CheckedSwing, r.Outcome, $"pitch {k}: pulled back in time");
                    pulled++;
                }
            }

            Assert.Greater(pulled, 20, "the balls are pulled back");
            Assert.GreaterOrEqual(bunted, 26, "the strikes are bunted");
        }

        [Test]
        public void TheCpuDoesNotSacrificeUntilRunnersTakeLeads()
        {
            // TASK-025 measured 0 successful sacrifices in 134 fair bunts (no secondary leads yet): the CPU keeps swinging in
            // the spot. Games with sacrifice spots (seeds 7019–7028) play no bunts; the spot itself is recognised.
            int spots = 0;
            for (int seed = 7019; seed <= 7028; seed++)
            {
                var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);
                var sim = new GameSimulator(g);
                while (!g.IsOver)
                {
                    if (BuntStrategy.Spot(g)) spots++;
                    Assert.IsFalse(BuntStrategy.Sacrifice(g));
                    sim.PlayPitch();
                    Assert.IsFalse(sim.LastBunt);
                }
            }

            Assert.Greater(spots, 0, "the spots occur");
        }
    }
}
