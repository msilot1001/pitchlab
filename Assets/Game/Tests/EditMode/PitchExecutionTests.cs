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
            Assert.That(average / Math.Sqrt(2.0) * 39.37, Is.InRange(6.0, 9.0));
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

        /// <summary>Execution errors only (no flight): n draws for <paramref name="pitcher"/> throwing <paramref name="type"/>.</summary>
        private static ExecutionError[] Errors(PlayerProfile pitcher, PitchType type, int n, int pitchCount = 0)
        {
            PitchInput aimed = PitchExecution.PitcherPitch(pitcher, type);
            var e = new ExecutionError[n];
            for (int i = 0; i < n; i++)
            {
                SeedStream s = PitchExecution.StreamFor(9, pitcher.Id, i, 1);
                PitchExecution.Execute(aimed, pitcher, type, pitchCount, ref s, out e[i]);
            }

            return e;
        }

        private static double Sd(double[] v)
        {
            double m = v.Average();
            return Math.Sqrt(v.Average(x => (x - m) * (x - m)));
        }

        [Test]
        public void MissesFollowTheArmSlotAndSpinVariesByType()
        {
            const int n = 3000;
            foreach (Hand hand in new[] { Hand.Right, Hand.Left })
            {
                ExecutionError[] e = Errors(Pitcher(50, hand), PitchType.FourSeam, n);
                double[] h = e.Select(x => x.HorizontalDeg).ToArray(), v = e.Select(x => x.VerticalDeg).ToArray();
                double corr = h.Zip(v, (a, b) => (a - h.Average()) * (b - v.Average())).Average() / (Sd(h) * Sd(v));
                // Up-and-arm-side / down-and-glove-side: a right-hander's arm side is −X (ρ < 0), a left-hander's +X.
                if (hand == Hand.Right) Assert.Less(corr, -0.2, "right-hander");
                else Assert.Greater(corr, 0.2, "left-hander");
                Assert.That(Sd(e.Select(x => x.SpinRpm).ToArray()), Is.InRange(60.0, 80.0), "spin rate σ ≈ 70 rpm");
                Assert.That(Sd(e.Select(x => x.SpeedMph).ToArray()), Is.InRange(0.8, 1.0), "speed σ ≈ 0.9 mph");
                Assert.That(Math.Abs(e.Average(x => x.SpeedMph)), Is.LessThan(0.06), "zero-mean speed error (fatigue is in the aim, not the miss)");
                Assert.That(e.Count(x => x.Wild) / (double)n, Is.InRange(0.015, 0.045), "≈ 3 % heavy tail");
            }

            PlayerProfile p = Pitcher(50);
            Assert.That(Sd(Errors(p, PitchType.FourSeam, n).Select(x => x.AxisDeg).ToArray()), Is.InRange(3.6, 4.4), "fastball axis σ 4°");
            Assert.That(Sd(Errors(p, PitchType.Slider, n).Select(x => x.AxisDeg).ToArray()), Is.InRange(7.2, 8.8), "breaking ball axis σ 8°");
        }

        [Test]
        public void FamiliaritySpeedOffsetAndMovementAreHisOwn()
        {
            var repertoire = new Repertoire(new RepertoirePitch(PitchType.FourSeam, 1, 2.0, 50), new RepertoirePitch(PitchType.Changeup, 1, 0.0, -50));
            var p = new PlayerProfile("F", "F", BatterSide.Right, Hand.Right, 75.0, Gameplay.Fielding.DefensivePosition.P, new PlayerRatings(movement: 100), repertoire);
            double best = PitchExecution.AngleSigmaDeg(p, PitchType.FourSeam, 0), worst = PitchExecution.AngleSigmaDeg(p, PitchType.Changeup, 0);
            Assert.AreEqual(0.85 / 1.15, best / worst, 1e-12, "familiarity ±50 → ∓15 %");
            Assert.AreEqual(PitchExecution.LeagueSpeedMph(PitchType.FourSeam) + 2.0, PitchExecution.PitcherPitch(p, PitchType.FourSeam).SpeedMph, 1e-12, "his offset on the pitch");
            Assert.AreEqual(PitchPresets.FourSeam.SpinRateRpm * (1.0 + PitchExecution.MovementSpinScale), PitchExecution.PitcherPitch(p, PitchType.FourSeam).SpinRateRpm, 1e-9, "Movement 100: +20 % spin");
            // Fatigue lowers the speed he aims with, mildly and capped.
            int tired = PitchExecution.FatigueThreshold(p) + 100;
            Assert.AreEqual(1.5, PitchExecution.PitcherPitch(p, PitchType.FourSeam).SpeedMph - PitchExecution.PitcherPitch(p, PitchType.FourSeam, tired).SpeedMph, 1e-9);
        }

        [Test]
        public void AGamePitchIsExactlyHisPitchAimedAtTheBattersZoneAndExecuted()
        {
            // A tall batter (his zone, not the default), some pitches already thrown (the pitch number, the pitch count).
            var tall = new PlayerProfile("T", "T", BatterSide.Right, 86.0, "");
            var lineup = new Lineup("Away", new[] { tall }.Concat(Enumerable.Range(2, 8).Select(i => new PlayerProfile($"A{i}", $"A{i}", BatterSide.Right, 73.0, ""))).ToArray());
            Team home = GenericRosters.Home();
            var game = new GameState(new Team("Away", lineup), home, 11);
            game.Pitch(PitchOutcome.Ball);
            game.Pitch(PitchOutcome.CalledStrike);
            PlayerProfile p = game.Pitcher;
            HittingPitch actual = GamePitches.Create(game, PitchType.Changeup, PitchTarget.UpLeft, true, Env, out PitchInfo info, out ExecutionError? error);
            (double tx, double tz) = PitchTargets.Point(PitchTarget.UpLeft, tall.ZoneBottom, tall.ZoneTop);
            PitchInput aimed = PitchTargets.Aim(PitchExecution.PitcherPitch(p, PitchType.Changeup, 2), tx, tz, Env);
            SeedStream stream = PitchExecution.StreamFor(11, p.Id, game.Current.Number, 3);
            HittingPitch expected = HittingPitch.Create(PitchExecution.Execute(aimed, p, PitchType.Changeup, 2, ref stream, out _), Env);
            Assert.AreEqual(2, game.PitchCount(TeamSide.Home));
            Assert.AreEqual(expected.Flight.First.Velocity, actual.Flight.First.Velocity, "intent → execution, exactly once");
            Assert.AreEqual(StrikeZone.Crossing(expected), StrikeZone.Crossing(actual));
            Assert.AreEqual((tx, tz), (info.TargetX, info.TargetZ), "his zone's target");
            // The next pitch of the plate appearance, and the next plate appearance, draw differently.
            game.Pitch(PitchOutcome.Ball);
            GamePitches.Create(game, PitchType.Changeup, PitchTarget.UpLeft, true, Env, out _, out ExecutionError? next);
            Assert.AreNotEqual(error.Value.HorizontalDeg, next.Value.HorizontalDeg);
        }

        [Test]
        public void PitchCountsBelongToThePitchingTeam()
        {
            var game = new GameState();
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);   // top 1: the home pitcher throws
            game.Pitch(PitchOutcome.CalledStrike);
            Assert.AreEqual((5, 0), (game.PitchCount(TeamSide.Home), game.PitchCount(TeamSide.Away)));
            for (int k = 0; k < 8; k++) game.Pitch(PitchOutcome.SwingingStrike);   // 2 more for #2 (0–1), 3 each for #3 and #4: three outs
            Assert.AreEqual((Half.Bottom, 13, 0), (game.Half, game.PitchCount(TeamSide.Home), game.PitchCount(TeamSide.Away)));
            game.Pitch(PitchOutcome.Ball);
            Assert.AreEqual((13, 1), (game.PitchCount(TeamSide.Home), game.PitchCount(TeamSide.Away)), "each count carries over");
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
            Assert.IsFalse(default(PitchInfo).HasTarget, "a pitch recorded without its info has no target");
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
