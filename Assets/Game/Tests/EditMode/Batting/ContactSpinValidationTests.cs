using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests.Batting
{
    /// <summary>
    /// TASK-004.5: spin produced by <see cref="ContactResolver"/> against the TASK-004 reference — Nathan's
    /// average-Statcast spin (<see cref="BattedBallValidationTests.AverageStatcastSpin"/>), with which the flight model
    /// matches 2024 Statcast distances. No per-ball spin truth is public, so this compares conditional means over a
    /// deterministic grid of contacts (five pitch types × both hands × timing × vertical and horizontal PCI offset). The
    /// grid is uniform, not a hitter population, and the reference is a regression (≈ ±700 rpm scatter, back/side split
    /// inferred by TrackMan). Thresholds were set after the Python prototype results: regression guards, not pre-registered.
    /// </summary>
    public class ContactSpinValidationTests
    {
        private sealed class Hit
        {
            public bool Right;
            public BattedBallLaunch Launch;
            public BallState State;
            /// <summary>Spin ⟂ the velocity (backspin and sidespin), the part the reference model and Magnus use; gyrospin excluded.</summary>
            public double TransverseRpm;
        }

        private static List<Hit> _grid;

        private static List<Hit> Grid => _grid ??= MakeGrid(SwingParameters.Default);

        private static List<Hit> MakeGrid(SwingParameters swing)
        {
            var hits = new List<Hit>();
            foreach (PitchInput input in new[] { PitchPresets.FourSeam, PitchPresets.Sinker, PitchPresets.Changeup, PitchPresets.Slider, PitchPresets.Curveball })
            {
                HittingPitch pitch = HittingPitch.Create(input, EnvironmentState.Standard);
                Vector3d ball = pitch.IdealContactState.Position;
                foreach (BatterSide side in new[] { BatterSide.Right, BatterSide.Left })
                {
                    SwingParameters p = swing;
                    p.Side = side;
                    for (int ms = -12; ms <= 12; ms += 2)
                    for (int mm = -30; mm <= 5; mm += 1)
                    foreach (double dx in new[] { -0.03, 0.0, 0.03 })
                    {
                        ContactResult r = ContactResolver.Resolve(pitch, new SwingInput(pitch.IdealContactTime - p.SwingDuration + ms * 0.001, ball.X + dx, ball.Z + mm * 0.001), p);
                        if (!r.IsContact) continue;
                        hits.Add(new Hit { Right = side == BatterSide.Right, Launch = BattedBallLaunch.FromState(r.BattedBall), State = r.BattedBall,
                            TransverseRpm = Math.Sqrt(Sq(BattedBallLaunch.FromState(r.BattedBall).BackspinRpm) + Sq(BattedBallLaunch.FromState(r.BattedBall).SidespinRpm)) });
                    }
                }
            }

            return hits;
        }

        private static double Sq(double v) => v * v;

        private static IEnumerable<Hit> FlyBalls(IEnumerable<Hit> hits, double laMin, double laMax) => hits.Where(h =>
            h.Launch.ExitSpeedMph >= 85.0 && Math.Abs(h.Launch.SprayAngleDegrees) <= 40.0 && h.Launch.LaunchAngleDegrees >= laMin && h.Launch.LaunchAngleDegrees < laMax);

        private static (double Back, double Side) Reference(Hit h) =>
            BattedBallValidationTests.AverageStatcastSpin(h.Launch.LaunchAngleDegrees, h.Launch.SprayAngleDegrees, h.Right);

        private static double CarryFeet(BallState s) => Units.MetersToFeet(BattedBallSimulation.Run(s, EnvironmentState.Standard).Metrics.Distance);

        /// <summary>Carry with the contact spin minus carry with the reference spin at the same exit speed and angles, ft.</summary>
        private static double CarryDifference(Hit h)
        {
            var (back, side) = Reference(h);
            BallState reference = new BattedBallLaunch(h.Launch.ExitSpeedMph, h.Launch.LaunchAngleDegrees, h.Launch.SprayAngleDegrees, back, side).ToState(h.State.Position);
            return CarryFeet(h.State) - CarryFeet(reference);
        }

        [TestCase(10.0), TestCase(15.0), TestCase(20.0), TestCase(25.0), TestCase(30.0)]
        public void SpinMatchesNathanAverageStatcastSpin(double laMin)
        {
            // Statcast measures spin rate directly but infers the back/side split, so compare back+side totals per 5° bucket.
            Hit[] g = FlyBalls(Grid, laMin, laMin + 5.0).ToArray();
            Assert.Greater(g.Length, 100);
            double ratio = g.Average(h => h.TransverseRpm) / g.Average(h => { var (b, s) = Reference(h); return Math.Sqrt(b * b + s * s); });
            Assert.That(ratio, Is.InRange(0.8, 1.25), $"mean total spin / Statcast average, n {g.Length}");
        }

        [TestCase(true), TestCase(false)]
        public void SidespinTracksSprayLikeNathanAverageStatcastSpin(bool rightHanded)
        {
            // Statcast fly balls: sidespin ≈ 90–94 rpm per degree of spray, zero about 10° to the pull side for both hands
            // (Nathan, carry-v2 2020; average model −849·s − 94·φ). Not independent of the bat tilt: a ≈ 30° tilt was seen to
            // reproduce this in the prototype before the 32° MLB average was looked up.
            Hit[] g = FlyBalls(Grid, 20.0, 35.0).Where(h => h.Right == rightHanded).ToArray();
            double mx = g.Average(h => h.Launch.SprayAngleDegrees), my = g.Average(h => h.Launch.SidespinRpm);
            double slope = g.Sum(h => (h.Launch.SprayAngleDegrees - mx) * (h.Launch.SidespinRpm - my)) / g.Sum(h => Math.Pow(h.Launch.SprayAngleDegrees - mx, 2));
            double zero = mx - my / slope;
            Assert.That(slope, Is.InRange(65.0, 125.0), "rpm per degree of spray");
            if (rightHanded) Assert.That(zero, Is.InRange(-20.0, 0.0), "zero sidespin on the pull side (left field)");
            else Assert.That(zero, Is.InRange(0.0, 20.0), "zero sidespin on the pull side (right field)");
        }

        [Test]
        public void FlyBallCarryMatchesTheValidatedReferenceSpin()
        {
            // The flight model matches Statcast with the reference spin (TASK-004). Contact spin must not move fly-ball
            // carry systematically: mean within ±10 ft, 10th and 90th percentiles within ±15 ft. Thresholds set after the
            // Python prototype results (observed: TestResults/contact_spin.md), so they are regression guards, not pre-registered.
            double[] d = FlyBalls(Grid, 20.0, 35.0).Where(h => h.Launch.ExitSpeedMph >= 90.0).Where((h, i) => i % 5 == 0).Select(CarryDifference).OrderBy(x => x).ToArray();
            Assert.Greater(d.Length, 100);
            Assert.That(d.Average(), Is.InRange(-10.0, 10.0), "mean ft");
            Assert.That(d[d.Length / 10], Is.GreaterThan(-15.0), "10th percentile ft");
            Assert.That(d[d.Length * 9 / 10], Is.LessThan(15.0), "90th percentile ft");
        }

        [Test, Explicit("Writes TestResults/contact_spin.md; run on demand for the TASK-004.5 report.")]
        public void WriteContactSpinReport()
        {
            SwingParameters current = SwingParameters.Default, previous = current;
            previous.TangentialRestitution = 0.40;
            previous.TangentialRecoil = 0.18;
            previous.VerticalBatAngle = 0.0;
            var md = new StringBuilder();
            md.AppendLine("Representative contacts, right-handed hitter, Four-Seam-like preset, standard air. Carry/hang with the contact spin; ");
            md.AppendLine("'ref carry' = same EV/LA/spray with Nathan's average-Statcast spin. PCI offsets: + dz = bat above ball centre, dx toward 1B.");
            md.AppendLine();
            md.AppendLine("| model | contact | timing ms | dx in | dz in | EV mph | LA ° | spray ° | backspin | sidespin | total rpm | carry ft | hang s | ref carry ft |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            var cases = new (string Name, double Ms, double Dx, double Dz)[]
            {
                ("centred", 0, 0, 0), ("slight undercut", 0, 0, -0.25), ("undercut", 0, 0, -0.5), ("strong undercut", 0, 0, -0.75),
                ("topped", 0, 0, 0.75), ("inside (handle side)", 0, 2, -0.5), ("outside (tip side)", 0, -2, -0.5),
                ("pulled fly", -8, 0, double.NaN), ("opposite-field fly", 8, 0, double.NaN),
            };
            HittingPitch pitch = HittingPitch.Create(PitchPresets.FourSeam, EnvironmentState.Standard);
            Vector3d ball = pitch.IdealContactState.Position;
            foreach (var (label, swing) in new[] { ("before (e_x 0.40, r_x 0.18, level bat)", previous), ("TASK-004.5", current) })
            foreach (var c0 in cases)
            {
                var c = c0;
                ContactResult Hit(double dzInches) => ContactResolver.Resolve(pitch, new SwingInput(pitch.IdealContactTime - swing.SwingDuration + c.Ms * 0.001,
                    ball.X + Units.InchesToMeters(c.Dx), ball.Z + Units.InchesToMeters(dzInches)), swing);
                // Pulled / opposite-field flies: mistiming also moves the bat height, so pick the undercut (0.02 in steps)
                // that gives a 27° launch, the middle of the fly-ball range.
                if (double.IsNaN(c.Dz))
                    c.Dz = Enumerable.Range(-100, 151).Select(i => i * 0.02).Where(dz => Hit(dz).IsContact)
                        .OrderBy(dz => Math.Abs(Hit(dz).LaunchAngleDegrees - 27.0)).First();
                ContactResult r = Hit(c.Dz);
                if (!r.IsContact) { md.AppendLine($"| {label} | {c.Name} | {c.Ms} | {c.Dx} | {c.Dz} | {r.Outcome} |||||||||"); continue; }
                BattedBallLaunch l = BattedBallLaunch.FromState(r.BattedBall);
                BattedBallMetrics m = BattedBallSimulation.Run(r.BattedBall, EnvironmentState.Standard).Metrics;
                var (back, side) = BattedBallValidationTests.AverageStatcastSpin(l.LaunchAngleDegrees, l.SprayAngleDegrees, true);
                double refCarry = l.LaunchAngleDegrees > 5.0
                    ? CarryFeet(new BattedBallLaunch(l.ExitSpeedMph, l.LaunchAngleDegrees, l.SprayAngleDegrees, back, side).ToState(r.BattedBall.Position)) : double.NaN;
                md.AppendLine($"| {label} | {c.Name} | {c.Ms} | {c.Dx} | {c.Dz:0.00} | {l.ExitSpeedMph:0.0} | {l.LaunchAngleDegrees:0.0} | {l.SprayAngleDegrees:+0.0;-0.0} | " +
                              $"{l.BackspinRpm:0} | {l.SidespinRpm:+0;-0} | {Units.RadiansPerSecondToRpm(r.BattedBall.Spin.Length):0} | {Units.MetersToFeet(m.Distance):0} | {m.HangTime:0.00} | {refCarry:0} |");
            }

            md.AppendLine();
            md.AppendLine("Grid (5 pitch types × both hands × timing ±12 ms × dz −30…+5 mm × dx ±30 mm), fly balls EV ≥ 85 mph, |spray| ≤ 40°:");
            md.AppendLine();
            md.AppendLine("| model | LA bucket | n | mean back+side rpm | Nathan avg back+side | ratio | mean backspin | Nathan avg backspin | carry − ref carry mean / 10–90 % ft |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var (label, swing) in new[] { ("before", previous), ("TASK-004.5", current) })
            {
                List<Hit> grid = MakeGrid(swing);
                for (double lo = 10.0; lo < 40.0; lo += 5.0)
                {
                    Hit[] g = FlyBalls(grid, lo, lo + 5.0).ToArray();
                    if (g.Length == 0) continue;
                    double refTotal = g.Average(h => { var (b, s) = Reference(h); return Math.Sqrt(b * b + s * s); });
                    double[] d = g.Where(h => h.Launch.ExitSpeedMph >= 90.0).Where((h, i) => i % 5 == 0).Select(CarryDifference).OrderBy(x => x).ToArray();
                    string carry = d.Length > 10 ? $"{d.Average():+0.0;-0.0} / {d[d.Length / 10]:+0;-0}…{d[d.Length * 9 / 10]:+0;-0}" : "-";
                    md.AppendLine($"| {label} | {lo:0}–{lo + 5:0}° | {g.Length} | {g.Average(h => h.TransverseRpm):0} | {refTotal:0} | {g.Average(h => h.TransverseRpm) / refTotal:0.00} | " +
                                  $"{g.Average(h => h.Launch.BackspinRpm):0} | {g.Average(h => Reference(h).Back):0} | {carry} |");
                }
            }

            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/contact_spin.md", md.ToString());
            TestContext.WriteLine(md.ToString());
        }
    }
}
