using System;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-018: a pitcher aims (intent) and executes an imperfect physical pitch (release angles, speed, spin): command
    /// spreads it, the physics decides where it goes. Seeded, deterministic, repertoire-bound, mirrored for left-handers.
    /// </summary>
    public class PitchExecutionTests
    {
        private static readonly EnvironmentState Env = EnvironmentState.Standard;

        private static PlayerProfile Pitcher(int command, Hand hand = Hand.Right, int velocity = 50, int stamina = 50) =>
            new PlayerProfile($"P{command}{hand}", "P", BatterSide.Right, hand, 75.0, Gameplay.Fielding.DefensivePosition.P,
                new PlayerRatings(command: command, velocity: velocity, stamina: stamina), GenericRosters.PowerRepertoire());

        /// <summary>Execution of the four-seamer aimed at the middle of the default zone: (crossing − target) per pitch.</summary>
        private static (double dx, double dz, double mph, bool strike)[] Sample(PlayerProfile pitcher, int n, int pitchCount = 0, PitchType type = PitchType.FourSeam, int seed = 1)
        {
            (double tx, double tz) = PitchTargets.Point(PitchTarget.Middle, StrikeZone.Bottom, StrikeZone.Top);
            PitchInput aimed = PitchTargets.Aim(PitchExecution.PitcherPitch(pitcher, type), tx, tz, Env);
            var result = new (double, double, double, bool)[n];
            for (int i = 0; i < n; i++)
            {
                SeedStream s = PitchExecution.StreamFor(seed, pitcher.Id, 1 + i / 6, 1 + i % 6);
                PitchInput executed = PitchExecution.Execute(aimed, pitcher, type, pitchCount, ref s, out _);
                HittingPitch pitch = HittingPitch.Create(executed, Env);
                (double x, double z) = StrikeZone.Crossing(pitch);
                result[i] = (x - tx, z - tz, Units.MetersPerSecondToMph(pitch.Flight.First.Velocity.Length), StrikeZone.IsStrike(pitch));
            }

            return result;
        }

        private static double Rms(double[] v) => Math.Sqrt(v.Average(x => x * x));

        [Test]
        public void TheSameSeedThrowsTheSamePitchAndAnotherVaries()
        {
            PlayerProfile p = Pitcher(50);
            PitchInput aimed = PitchExecution.PitcherPitch(p, PitchType.Slider);
            PitchInput Run(int seed)
            {
                SeedStream s = PitchExecution.StreamFor(seed, p.Id, 3, 2);
                return PitchExecution.Execute(aimed, p, PitchType.Slider, 10, ref s, out _);
            }

            PitchInput a = Run(5), b = Run(5), c = Run(6);
            Assert.AreEqual((a.HorizontalAngleDegrees, a.VerticalAngleDegrees, a.SpeedMph, a.SpinRateRpm, a.SpinAxisDegrees),
                (b.HorizontalAngleDegrees, b.VerticalAngleDegrees, b.SpeedMph, b.SpinRateRpm, b.SpinAxisDegrees));
            Assert.AreNotEqual(a.HorizontalAngleDegrees, c.HorizontalAngleDegrees);
            // Only the release inputs change: the release point stays his.
            Assert.AreEqual((aimed.ReleaseSideFeet, aimed.ReleaseHeightFeet, aimed.ExtensionFeet, aimed.GyroAngleDegrees), (a.ReleaseSideFeet, a.ReleaseHeightFeet, a.ExtensionFeet, a.GyroAngleDegrees));
            // The plate location is the simulated flight's: the same executed input, the same crossing.
            Assert.AreEqual(StrikeZone.Crossing(HittingPitch.Create(a, Env)), StrikeZone.Crossing(HittingPitch.Create(b, Env)));
        }

        [Test]
        public void BetterCommandMissesLessButNobodyIsPerfect()
        {
            const int n = 240;
            var report = new System.Text.StringBuilder();
            double Report(string name, int command)
            {
                var all = Sample(Pitcher(command), n);
                // A pitch in the dirt never reaches the plate's plane: a miss of its own (counted, not measured).
                var s = all.Where(x => !double.IsNaN(x.dx)).ToArray();
                int bounced = n - s.Length;
                double rh = Rms(s.Select(x => x.dx).ToArray()), rv = Rms(s.Select(x => x.dz).ToArray());
                double strike = all.Count(x => x.strike) / (double)n;
                report.AppendLine($"{name} (command {command}): RMS miss horizontal {rh * 39.37:0.0} in, vertical {rv * 39.37:0.0} in, " +
                                  $"radial {Math.Sqrt(rh * rh + rv * rv) * 39.37:0.0} in; {bounced} in the dirt; strikes when aimed at the middle {strike:P0}");
                Assert.Greater(rh, 0.02, $"{name}: not perfectly accurate");
                Assert.Less(bounced, n / 20, $"{name}: aimed at the middle, few bounce");
                return Math.Sqrt(rh * rh + rv * rv) + bounced * 1e-3;
            }

            double elite = Report("elite", 90), average = Report("average", 50), poor = Report("poor", 15);
            TestContext.WriteLine(report.ToString());
            Assert.Less(elite, average);
            Assert.Less(average, poor);
            // Sanity (regression guard, not validation): an average pitcher's per-axis miss ≈ 6–8 in (Docs/PITCH_EXECUTION.md).
            Assert.That(average / Math.Sqrt(2.0) * 39.37, Is.InRange(5.0, 9.0));
        }

        [Test]
        public void SpeedAndSpinVaryPlausibly()
        {
            PlayerProfile p = Pitcher(50, velocity: 80);
            var s = Sample(p, 200);
            double mean = s.Average(x => x.mph), sd = Math.Sqrt(s.Average(x => (x.mph - mean) * (x.mph - mean)));
            TestContext.WriteLine($"four-seam: mean {mean:0.0} mph, SD {sd:0.00} mph");
            Assert.AreEqual(PitchExecution.SpeedMph(p, p.Repertoire.Get(PitchType.FourSeam)), mean, 0.3, "around his rated speed");
            Assert.That(sd, Is.InRange(0.6, 1.2), "≈ 1 mph pitch to pitch");
            Assert.Less(s.Max(x => Math.Abs(x.mph - mean)), 5.0, "no absurd outliers");
            // His speed follows the Velocity rating; a slider is slower than his fastball.
            Assert.Greater(PitchExecution.SpeedMph(Pitcher(50, velocity: 90), p.Repertoire.Get(PitchType.FourSeam)), PitchExecution.SpeedMph(Pitcher(50, velocity: 10), p.Repertoire.Get(PitchType.FourSeam)));
            Assert.Greater(PitchExecution.SpeedMph(p, p.Repertoire.Get(PitchType.FourSeam)), PitchExecution.SpeedMph(p, p.Repertoire.Get(PitchType.Slider)) + 5.0);
        }

        [Test]
        public void HisBestPitchIsCommandedBestAndFatigueIsMild()
        {
            PlayerProfile p = Pitcher(50);   // PowerRepertoire: four-seam +10 familiarity, changeup −15
            Assert.Less(PitchExecution.AngleSigmaDeg(p, PitchType.FourSeam, 0), PitchExecution.AngleSigmaDeg(p, PitchType.Changeup, 0));
            Assert.Less(PitchExecution.AngleSigmaDeg(Pitcher(80), PitchType.FourSeam, 0), PitchExecution.AngleSigmaDeg(Pitcher(20), PitchType.FourSeam, 0));
            int threshold = PitchExecution.FatigueThreshold(p);
            Assert.AreEqual(PitchExecution.AngleSigmaDeg(p, PitchType.FourSeam, 0), PitchExecution.AngleSigmaDeg(p, PitchType.FourSeam, threshold), "fresh until the threshold");
            Assert.Greater(PitchExecution.AngleSigmaDeg(p, PitchType.FourSeam, threshold + 30), PitchExecution.AngleSigmaDeg(p, PitchType.FourSeam, threshold));
            Assert.LessOrEqual(PitchExecution.AngleSigmaDeg(p, PitchType.FourSeam, 400), 1.25 * PitchExecution.AngleSigmaDeg(p, PitchType.FourSeam, 0) + 1e-12, "at most +25 %");
            Assert.LessOrEqual(PitchExecution.FatigueSpeedLossMph(p, 400), 1.5);
            Assert.Greater(PitchExecution.FatigueThreshold(Pitcher(50, stamina: 90)), PitchExecution.FatigueThreshold(Pitcher(50, stamina: 10)));
        }

        [Test]
        public void ALeftHandersPitchIsTheMirrorImage()
        {
            foreach (PitchType t in new[] { PitchType.FourSeam, PitchType.Slider, PitchType.Changeup })
            {
                PitchInput right = PitchExecution.PitcherPitch(Pitcher(50), t), left = PitchExecution.PitcherPitch(Pitcher(50, Hand.Left), t);
                (double rx, double rz) = StrikeZone.Crossing(HittingPitch.Create(right, Env));
                (double lx, double lz) = StrikeZone.Crossing(HittingPitch.Create(left, Env));
                Assert.AreEqual(-rx, lx, 1e-6, $"{t}: mirrored across the plate");
                Assert.AreEqual(rz, lz, 1e-6, $"{t}: same height");
                Assert.Greater(left.ReleaseSideFeet, 0.0, "released on the first-base side");
                // Movement mirrors too (the physics, not a mirrored picture): the horizontal break changes sign.
                PitchMetrics mr = PitchSimulation.Run(right, Env).Metrics, ml = PitchSimulation.Run(left, Env).Metrics;
                Assert.AreEqual(-mr.HorizontalMovement, ml.HorizontalMovement, 1e-6, t.ToString());
                Assert.AreEqual(mr.VerticalMovement, ml.VerticalMovement, 1e-6);
            }
        }

        [Test]
        public void APitcherThrowsOnlyHisPitches()
        {
            var game = new GameState();   // top 1: the home starter (sinker, four-seam, changeup, curveball) pitches
            Assert.IsFalse(game.Pitcher.Repertoire.Has(PitchType.Slider));
            Assert.Throws<ArgumentException>(() => GamePitches.Create(game, PitchType.Slider, PitchTarget.Middle, true, Env, out _, out _));
            Assert.AreEqual(PitchType.Sinker, GamePitches.Available(game, PitchType.Slider), "his first pitch instead");
            for (int pa = 1; pa < 40; pa++)
            {
                PitchCommand c = AutoPitcher.Choose(3, pa, 1 + pa % 5, new Count(pa % 4, pa % 3), game.Pitcher.Repertoire);
                Assert.IsTrue(game.Pitcher.Repertoire.Has((PitchType)c.Preset), $"{(PitchType)c.Preset}");
            }

            // A game pitch records its target and is executed (variance on) or exact (off).
            HittingPitch exact = GamePitches.Create(game, PitchType.Sinker, PitchTarget.Middle, false, Env, out PitchInfo exactInfo, out ExecutionError? none);
            HittingPitch executed = GamePitches.Create(game, PitchType.Sinker, PitchTarget.Middle, true, Env, out PitchInfo info, out ExecutionError? error);
            Assert.IsNull(none);
            Assert.IsTrue(error.HasValue);
            Assert.IsTrue(info.HasTarget);
            Assert.AreEqual((exactInfo.TargetX, exactInfo.TargetZ), (info.TargetX, info.TargetZ));
            Assert.Less(Math.Abs(exactInfo.PlateX - exactInfo.TargetX), 0.03, "intended: on target");
            Assert.AreNotEqual(exactInfo.PlateX, info.PlateX, "executed: off it");
            Assert.AreEqual(executed.Flight.First.Position, exact.Flight.First.Position, "the same release point");
        }

        [Test]
        public void ExecutionIsPartOfTheGamesSeed()
        {
            HittingPitch Pitch(int seed)
            {
                var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);
                return GamePitches.Create(g, PitchType.Sinker, PitchTarget.UpLeft, true, Env, out _, out _);
            }

            Assert.AreEqual(Pitch(4).Flight.Final.Position, Pitch(4).Flight.Final.Position);
            Assert.AreNotEqual(Pitch(4).Flight.Final.Position, Pitch(5).Flight.Final.Position);
        }
    }
}
