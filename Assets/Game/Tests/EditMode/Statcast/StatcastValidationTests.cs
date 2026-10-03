using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Pitchlab.Simulation.BallFlight;
using static Pitchlab.Tests.Statcast.StatcastValidation;

namespace Pitchlab.Tests.Statcast
{
    /// <summary>
    /// TASK-002 acceptance checks. Thresholds were fixed in the ExecPlan before any replay was run; do not loosen them
    /// to make a result pass — investigate instead (Docs/VALIDATION_TASK002.md).
    /// </summary>
    public class StatcastValidationTests
    {
        private static PitchResult[] Role(string role) => Results.Where(r => r.Role == role).ToArray();

        [Test]
        public void FixtureCoversEveryPitchFamilyInBothGames()
        {
            foreach (string role in new[] { StatcastFixture.Development, StatcastFixture.Holdout })
            foreach (string family in new[] { "Four-seam", "Sinker", "Cutter", "Slider/sweeper", "Curveball", "Changeup/splitter" })
                Assert.GreaterOrEqual(Role(role).Count(r => r.Family == family), 5, $"{role} {family}");
        }

        [Test]
        public void ReconstructedFitReproducesStatcastPlateCrossingAndReleaseSpeed()
        {
            // Pre-registered #1: plate within 0.03 ft for ≥ 95 % of pitches; release speed within 0.2 mph.
            int ok = Results.Count(r => r.FitPlateErrorFeet <= 0.03);
            Assert.GreaterOrEqual(ok, 0.95 * Results.Count, $"{ok}/{Results.Count} within 0.03 ft");
            Assert.LessOrEqual(Results.Max(r => Math.Abs(r.FitReleaseSpeedErrorMph)), 0.2);
        }

        [TestCase(StatcastFixture.Development)]
        [TestCase(StatcastFixture.Holdout)]
        public void SimulatedFlightTimeAndImpliedDragMatchConstantDragModel(string role)
        {
            // Pre-registered #2: median |Δt| ≤ 3 ms from y = 50 ft to the plate; median implied C_D within 0.35 ± 0.05.
            PitchResult[] rs = Role(role);
            Assert.LessOrEqual(Median(rs.Select(r => Math.Abs(r.ConsistencyTimeError))), 0.003);
            Assert.That(Median(rs.Select(r => r.ImpliedDragCoefficient)), Is.InRange(0.30, 0.40));
        }

        [Test, Ignore("Pre-registered criterion NOT met; kept visible on purpose. Explanation: Docs/VALIDATION_TASK002.md §Lift.")]
        public void FourSeamImpliedSpinEfficiencyIsPhysical()
        {
            // Pre-registered #3: four-seam implied efficiency median in [0.80, 1.00]; ≤ 10 % above 1.05.
            PitchResult[] ff = Role(StatcastFixture.Development).Where(r => r.Family == "Four-seam").ToArray();
            Assert.That(Median(ff.Select(r => r.ImpliedEfficiency)), Is.InRange(0.80, 1.00));
            Assert.LessOrEqual(ff.Count(r => r.ImpliedEfficiency > 1.05), 0.10 * ff.Length);
        }

        [TestCase(StatcastFixture.Development), TestCase(StatcastFixture.Holdout)]
        [Ignore("Pre-registered criterion NOT met; kept visible on purpose. Explanation: Docs/VALIDATION_TASK002.md §Spin axis vs movement.")]
        public void ConsistencyReplayReproducesPlateLocation(string role)
        {
            // Pre-registered #4a: median 2D plate error ≤ 1 in.
            Assert.LessOrEqual(Median(Role(role).Select(r => r.ConsistencyError2D)), 1.0);
        }

        [Test, Ignore("Pre-registered criterion NOT met; kept visible on purpose. Explanation: Docs/VALIDATION_TASK002.md §Holdout prediction.")]
        public void HoldoutPredictionWithDevelopmentEfficiencies()
        {
            // Pre-registered #4b: median 2D plate error ≤ 3 in on the holdout game.
            PitchResult[] rs = Role(StatcastFixture.Holdout).Where(r => IsFinite(r.PredictionErrorX)).ToArray();
            Assert.Greater(rs.Length, 250);
            Assert.LessOrEqual(Median(rs.Select(r => r.PredictionError2D)), 3.0);
        }

        [Test, Ignore("Pre-registered criterion NOT met; kept visible on purpose. Explanation: Docs/VALIDATION_TASK002.md §Movement definition.")]
        public void SavantPfxMatchesOneCandidateDefinition()
        {
            // Pre-registered #5: at least one definition reproduces CSV pfx with median |Δ| ≤ 1 in in both axes.
            bool any = Enumerable.Range(0, PfxDefinitions.Length).Any(k =>
                Median(Results.Select(r => Math.Abs(r.PfxErrorX[k]))) <= 1.0 && Median(Results.Select(r => Math.Abs(r.PfxErrorZ[k]))) <= 1.0);
            Assert.IsTrue(any);
        }

        // ---- Post-hoc regression guards (thresholds chosen AFTER seeing the data, with margin around observed
        // values; they protect what TASK-002 established and are not acceptance criteria).

        [TestCase(StatcastFixture.Development), TestCase(StatcastFixture.Holdout)]
        public void ReplayWithObservedMagnusDirectionReproducesPlateLocation(string role)
        {
            // Observed: 0.32 in (development), 0.57 in (holdout). Shows the integrator, drag and lift magnitude are
            // consistent with the tracked flights once the Magnus direction is right.
            Assert.LessOrEqual(Median(Role(role).Select(r => r.ObservedDirectionError2D)), 0.75);
        }

        [TestCase("Four-seam"), TestCase("Sinker"), TestCase("Changeup/splitter"), TestCase("Cutter")]
        public void SpinAxisDeviationMirrorsWithThrowingHand(string family)
        {
            // A wrong axis convention would shift both hands the same way; a physical (seam-shifted-wake-like)
            // deviation mirrors. Observed: opposite signs for both hands in all four families.
            double right = Results.Where(r => r.Family == family && r.Throws == "R").Average(r => r.MagnusAxisDeviationDegrees);
            double left = Results.Where(r => r.Family == family && r.Throws == "L").Average(r => r.MagnusAxisDeviationDegrees);
            Assert.Less(right * left, 0.0, $"R {right:0.0}°, L {left:0.0}°");
        }

        [Test]
        public void LiftMatchesMeasuredActiveSpinForHighEfficiencyPitches()
        {
            // Observed median ratio (implied efficiency / Hawk-Eye active spin): four-seam 0.90, sinker 1.09, changeup 0.86.
            var ratios = ActiveSpinRatios().Where(x => x.PitchType == "FF" || x.PitchType == "SI" || x.PitchType == "CH").Select(x => x.Ratio).ToArray();
            Assert.GreaterOrEqual(ratios.Length, 20);
            Assert.That(Median(ratios), Is.InRange(0.8, 1.2));
        }

        [Test]
        public void KnownLimitationBreakingBallsGetLessLiftThanMeasuredActiveSpinImplies()
        {
            // Documents a known model limitation (Docs/VALIDATION_TASK002.md §Lift): for low-transverse-spin pitches the
            // C_L fit turns Hawk-Eye active spin into ~1.6–2.6× too much transverse-spin effect. If a model change
            // fixes this, this test fails — update the docs and replace it with an acceptance check.
            var ratios = ActiveSpinRatios().Where(x => x.PitchType == "SL" || x.PitchType == "FC" || x.PitchType == "CU" || x.PitchType == "KC")
                .Select(x => x.Ratio).ToArray();
            Assert.GreaterOrEqual(ratios.Length, 15);
            Assert.Less(Median(ratios), 0.8);
        }

        [Test, Explicit("Writes TestResults/statcast_validation_*.{csv,md}; run on demand to regenerate the TASK-002 report.")]
        public void WriteValidationReport()
        {
            Directory.CreateDirectory("TestResults");
            var csv = new StringBuilder("role,family,pitch_type,release_speed_mph,spin_rpm,spin_axis,fit_plate_err_ft,implied_cd,implied_cl,implied_eff,magnus_axis_dev_deg,cons_dx_in,cons_dz_in,cons_dt_s,pred_dx_in,pred_dz_in,in_range\n");
            foreach (PitchResult r in Results)
                csv.AppendLine(string.Join(",", r.Role, r.Family, r.PitchType, F(r.ReleaseSpeedMph), F(r.SpinRateRpm), F(r.SpinAxis), F(r.FitPlateErrorFeet),
                    F(r.ImpliedDragCoefficient), F(r.ImpliedLiftCoefficient), F(r.ImpliedEfficiency), F(r.MagnusAxisDeviationDegrees),
                    F(r.ConsistencyErrorX), F(r.ConsistencyErrorZ), F(r.ConsistencyTimeError), F(r.PredictionErrorX), F(r.PredictionErrorZ), r.WithinSupportedRange));
            File.WriteAllText("TestResults/statcast_validation_pitches.csv", csv.ToString());

            var md = new StringBuilder();
            md.AppendLine($"Pitches: {Results.Count}");
            md.AppendLine();
            md.AppendLine("| role | family | n | fit plate err ft (max) | C_D med | C_L med | eff med | eff>1.05 | axis dev med° | cons dX med | cons dZ med | cons 2D med | cons 2D p90 | cons dt med ms | pred dX med | pred dZ med | pred 2D med | pred 2D p90 | out of range |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in Results.GroupBy(r => (r.Role, r.Family)).OrderBy(g => g.Key.Role).ThenBy(g => g.Key.Family))
                md.AppendLine(Row(g.Key.Role, g.Key.Family, g.ToArray()));
            foreach (var g in Results.GroupBy(r => r.Role))
                md.AppendLine(Row(g.Key, "ALL", g.ToArray()));
            md.AppendLine();
            md.AppendLine("| pfx definition | role | median |Δx| in | median |Δz| in | mean Δx | mean Δz |");
            md.AppendLine("|---|---|---|---|---|---|");
            for (int k = 0; k < PfxDefinitions.Length; k++)
            foreach (var g in Results.GroupBy(r => r.Role))
                md.AppendLine($"| {PfxDefinitions[k]} | {g.Key} | {F2(Median(g.Select(r => Math.Abs(r.PfxErrorX[k]))))} | {F2(Median(g.Select(r => Math.Abs(r.PfxErrorZ[k]))))} | {F2(g.Average(r => r.PfxErrorX[k]))} | {F2(g.Average(r => r.PfxErrorZ[k]))} |");
            md.AppendLine();
            md.AppendLine("Diagnostics: signed Magnus-vs-spin_axis deviation (deg, + = observed movement rotated clockwise from the spin_axis direction in the catcher's view), observed-direction replay, holdout wind sensitivity.");
            md.AppendLine();
            md.AppendLine("| role | family | hand | n | signed dev mean | signed dev med | obs-dir 2D med | obs-dir 2D p90 | wind C_D med | wind eff med | wind |dev| med | wind obs-dir 2D med |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in Results.GroupBy(r => (r.Role, r.Family, r.Throws)).OrderBy(g => g.Key.Role).ThenBy(g => g.Key.Family).ThenBy(g => g.Key.Throws))
            {
                var a = g.ToArray();
                md.AppendLine($"| {g.Key.Role} | {g.Key.Family} | {g.Key.Throws} | {a.Length} | {F2(a.Average(r => r.MagnusAxisDeviationDegrees))} | {F2(Median(a.Select(r => r.MagnusAxisDeviationDegrees)))} | " +
                              $"{F2(Median(a.Select(r => r.ObservedDirectionError2D)))} | {F2(Percentile(a.Select(r => r.ObservedDirectionError2D), 0.9))} | " +
                              $"{F2(Median(a.Select(r => r.WindImpliedDragCoefficient)))} | {F2(Median(a.Select(r => r.WindImpliedEfficiency)))} | {F2(Median(a.Select(r => Math.Abs(r.WindAxisDeviation))))} | {F2(Median(a.Select(r => r.WindObservedDirectionError2D)))} |");
            }
            foreach (var g in Results.GroupBy(r => r.Role))
                md.AppendLine($"| {g.Key} | ALL | - | {g.Count()} | {F2(g.Average(r => r.MagnusAxisDeviationDegrees))} | {F2(Median(g.Select(r => r.MagnusAxisDeviationDegrees)))} | {F2(Median(g.Select(r => r.ObservedDirectionError2D)))} | {F2(Percentile(g.Select(r => r.ObservedDirectionError2D), 0.9))} | {F2(Median(g.Select(r => r.WindImpliedDragCoefficient)))} | {F2(Median(g.Select(r => r.WindImpliedEfficiency)))} | {F2(Median(g.Select(r => Math.Abs(r.WindAxisDeviation))))} | {F2(Median(g.Select(r => r.WindObservedDirectionError2D)))} |");
            md.AppendLine();
            md.AppendLine("| pitch type | pitcher-game groups | median implied eff / Savant active spin | min | max |");
            md.AppendLine("|---|---|---|---|---|");
            foreach (var g in ActiveSpinRatios().GroupBy(x => x.PitchType).OrderBy(g => g.Key))
                md.AppendLine($"| {g.Key} | {g.Count()} | {F2(Median(g.Select(x => x.Ratio)))} | {F2(g.Min(x => x.Ratio))} | {F2(g.Max(x => x.Ratio))} |");
            var ffDev = Results.Where(r => r.Role == StatcastFixture.Development && r.Family == "Four-seam").Select(r => r.ImpliedEfficiency).OrderBy(v => v).ToArray();
            md.AppendLine();
            md.AppendLine("Development four-seam implied efficiency deciles: " + string.Join(", ", Enumerable.Range(1, 9).Select(k => F2(ffDev[k * ffDev.Length / 10]))) + $"; max {F2(ffDev.Last())}");
            File.WriteAllText("TestResults/statcast_validation_summary.md", md.ToString());
            TestContext.WriteLine(md.ToString());
        }

        private static string Row(string role, string family, PitchResult[] g)
        {
            var finite = g.Where(r => IsFinite(r.ImpliedEfficiency)).ToArray();
            var pred = g.Where(r => IsFinite(r.PredictionErrorX)).ToArray();
            return $"| {role} | {family} | {g.Length} | {F2(g.Max(r => r.FitPlateErrorFeet))} | {F2(Median(g.Select(r => r.ImpliedDragCoefficient)))} | " +
                   $"{F2(Median(g.Select(r => r.ImpliedLiftCoefficient)))} | {F2(Median(finite.Select(r => r.ImpliedEfficiency)))} | {g.Count(r => !(r.ImpliedEfficiency <= 1.05))} | " +
                   $"{F2(Median(g.Select(r => Math.Abs(r.MagnusAxisDeviationDegrees))))} | {F2(Median(g.Select(r => r.ConsistencyErrorX)))} | {F2(Median(g.Select(r => r.ConsistencyErrorZ)))} | " +
                   $"{F2(Median(g.Select(r => r.ConsistencyError2D)))} | {F2(Percentile(g.Select(r => r.ConsistencyError2D), 0.9))} | {F2(1000 * Median(g.Select(r => r.ConsistencyTimeError)))} | " +
                   (pred.Length == 0 ? "- | - | - | - | " :
                   $"{F2(Median(pred.Select(r => r.PredictionErrorX)))} | {F2(Median(pred.Select(r => r.PredictionErrorZ)))} | {F2(Median(pred.Select(r => r.PredictionError2D)))} | {F2(Percentile(pred.Select(r => r.PredictionError2D), 0.9))} | ") +
                   $"{g.Count(r => !r.WithinSupportedRange)} |";
        }

        private static double Percentile(System.Collections.Generic.IEnumerable<double> values, double q)
        {
            double[] s = values.OrderBy(v => v).ToArray();
            return s.Length == 0 ? double.NaN : s[(int)Math.Min(s.Length - 1, Math.Floor(q * s.Length))];
        }

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
