using System;
using System.Collections.Generic;
using System.Linq;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;
using Pitchlab.Simulation.Tracking;

namespace Pitchlab.Tests.Statcast
{
    /// <summary>
    /// TASK-002 replay of tracked pitches (pre-registered analysis: Plans/*/OVERNIGHT-physics-hitting.md).
    /// All lengths here are inches and times seconds unless named otherwise.
    /// </summary>
    public static class StatcastValidation
    {
        public sealed class PitchResult
        {
            public string Role, Family, PitchType, Throws, PitcherId;
            /// <summary>Savant Hawk-Eye active spin for this pitcher and pitch type (season), or NaN.</summary>
            public double ActiveSpin;
            public double SpinRateRpm, SpinAxis, ReleaseSpeedMph;
            // 1. Interpretation check (fit vs CSV)
            public double FitPlateErrorFeet, FitReleaseSpeedErrorMph;
            // 2. Drag
            public double FitPlateTime, ImpliedDragCoefficient;
            // 3. Lift
            public double ImpliedLiftCoefficient, ImpliedTransverseRpm, ImpliedEfficiency, MagnusAxisDeviationDegrees;
            // 4. Replays (inches; time s)
            public double ConsistencyErrorX, ConsistencyErrorZ, ConsistencyTimeError;
            public double PredictionErrorX = double.NaN, PredictionErrorZ = double.NaN;
            // 5. pfx candidates minus CSV pfx (inches): PITCHf/x 40 ft, drag-corrected release→plate,
            //    drag-corrected 40 ft, uncorrected release→plate, our simulated movement metric.
            public double[] PfxErrorX = new double[5], PfxErrorZ = new double[5];
            public bool WithinSupportedRange;
            // Diagnostics (B6): replay with the fit's observed Magnus direction instead of spin_axis; holdout with wind.
            public double ObservedDirectionErrorX, ObservedDirectionErrorZ;
            public double WindImpliedDragCoefficient = double.NaN, WindImpliedEfficiency = double.NaN, WindAxisDeviation = double.NaN;
            public double WindObservedDirectionError2D = double.NaN;
            public double ObservedDirectionError2D => Math.Sqrt(ObservedDirectionErrorX * ObservedDirectionErrorX + ObservedDirectionErrorZ * ObservedDirectionErrorZ);

            public double ConsistencyError2D => Math.Sqrt(ConsistencyErrorX * ConsistencyErrorX + ConsistencyErrorZ * ConsistencyErrorZ);
            public double PredictionError2D => Math.Sqrt(PredictionErrorX * PredictionErrorX + PredictionErrorZ * PredictionErrorZ);
        }

        public static readonly string[] PfxDefinitions =
        {
            "PITCHf/x: ½(ax, az+g)·t² over y=40 ft→plate",
            "drag-corrected ½a_M·t² over release→plate",
            "drag-corrected ½a_M·t² over y=40 ft→plate",
            "uncorrected ½(ax, az+g)·t² over release→plate",
            "Pitchlab metric: simulated flight − no-lift flight from release",
        };

        private static List<PitchResult> _results;
        public static IReadOnlyList<PitchResult> Results => _results ??= Run();

        private static List<PitchResult> Run()
        {
            BallProperties ball = BallProperties.Baseball;
            double gFeet = Units.MetersToFeet(EnvironmentState.StandardGravity);
            var results = new List<PitchResult>();

            foreach (StatcastFixture.Row row in StatcastFixture.Rows)
            {
                StatcastPitch p = row.Pitch;
                EnvironmentState environment = StatcastFixture.EnvironmentFor(row.Role);
                var fit = new StatcastNinePointFit(p);
                double tRelease = fit.ReleaseTime;
                double tPlate = fit.TimeAtY(StatcastAdapter.PlateFrontYFeet);
                double t40 = fit.TimeAtY(40.0);
                double tMid = 0.5 * (tRelease + tPlate);
                var r = new PitchResult
                {
                    Role = row.Role, Family = row.Family, PitchType = p.PitchType, Throws = p.Throws, PitcherId = row.PitcherId,
                    ActiveSpin = StatcastFixture.ActiveSpin(row.PitcherId, p.PitchType),
                    SpinRateRpm = p.SpinRateRpm, SpinAxis = p.SpinAxisDegrees, ReleaseSpeedMph = p.ReleaseSpeedMph,
                };

                // 1. Interpretation: the reconstructed fit must reproduce Statcast's own derived fields.
                Vector3d plate = fit.PositionFeet(tPlate);
                r.FitPlateErrorFeet = Math.Sqrt(Sq(plate.X - p.PlateX) + Sq(plate.Z - p.PlateZ));
                r.FitReleaseSpeedErrorMph = Units.MetersPerSecondToMph(fit.VelocityFeet(tRelease).Length * Units.MetersPerFoot) - p.ReleaseSpeedMph;
                r.FitPlateTime = tPlate;

                // 2–3. Implied coefficients from the fit's average acceleration, at mid-flight speed.
                AerodynamicDecomposition d = StatcastAdapter.Decompose(fit, tMid, ball, environment);
                r.ImpliedDragCoefficient = d.ImpliedDragCoefficient;
                r.ImpliedLiftCoefficient = d.ImpliedLiftCoefficient;
                double s = AerodynamicModel.SpinParameterForLift(d.ImpliedLiftCoefficient);
                r.ImpliedTransverseRpm = Units.RadiansPerSecondToRpm(s * d.AirSpeed / ball.Radius);
                r.ImpliedEfficiency = r.ImpliedTransverseRpm / p.SpinRateRpm;
                // Magnus direction in the catcher's X–Z view vs the direction implied by spin_axis: (sin θ, −cos θ).
                double magnusAngle = Math.Atan2(d.MagnusAcceleration.X, d.MagnusAcceleration.Z);
                double axisAngle = Math.Atan2(Math.Sin(Units.DegreesToRadians(p.SpinAxisDegrees)), -Math.Cos(Units.DegreesToRadians(p.SpinAxisDegrees)));
                r.MagnusAxisDeviationDegrees = WrapDegrees(Units.RadiansToDegrees(magnusAngle - axisAngle));

                // 4a. Consistency replay from the y = 50 ft state with the implied transverse spin about Statcast's axis.
                double rpm = double.IsInfinity(r.ImpliedTransverseRpm) ? 0.0 : r.ImpliedTransverseRpm;
                var replay = Replay(fit, rpm, p.SpinAxisDegrees, environment);
                r.ConsistencyErrorX = Units.MetersToInches(replay.Metrics.PlateX) - p.PlateX * 12.0;
                r.ConsistencyErrorZ = Units.MetersToInches(replay.Metrics.PlateZ) - p.PlateZ * 12.0;
                r.ConsistencyTimeError = replay.Metrics.FlightTime - tPlate;
                r.WithinSupportedRange = replay.Metrics.WithinSupportedAerodynamicRange;

                // Diagnostic: same magnitude, but Magnus direction taken from the fit (ω ∥ v × a_M), not from spin_axis.
                var observed = ReplayWithObservedDirection(fit, d, environment);
                r.ObservedDirectionErrorX = Units.MetersToInches(observed.PlateX) - p.PlateX * 12.0;
                r.ObservedDirectionErrorZ = Units.MetersToInches(observed.PlateZ) - p.PlateZ * 12.0;

                if (row.Role == StatcastFixture.Holdout)
                {
                    EnvironmentState windy = new EnvironmentState(environment.AirDensity, environment.Gravity, StatcastFixture.HoldoutReportedWind);
                    AerodynamicDecomposition dw = StatcastAdapter.Decompose(fit, tMid, ball, windy);
                    r.WindImpliedDragCoefficient = dw.ImpliedDragCoefficient;
                    double sw = AerodynamicModel.SpinParameterForLift(dw.ImpliedLiftCoefficient);
                    r.WindImpliedEfficiency = Units.RadiansPerSecondToRpm(sw * dw.AirSpeed / ball.Radius) / p.SpinRateRpm;
                    r.WindAxisDeviation = WrapDegrees(Units.RadiansToDegrees(Math.Atan2(dw.MagnusAcceleration.X, dw.MagnusAcceleration.Z) - axisAngle));
                    var windObserved = ReplayWithObservedDirection(fit, dw, windy);
                    r.WindObservedDirectionError2D = Math.Sqrt(Sq(Units.MetersToInches(windObserved.PlateX) - p.PlateX * 12.0) +
                                                               Sq(Units.MetersToInches(windObserved.PlateZ) - p.PlateZ * 12.0));
                }

                // 5. pfx candidates (feet → inches).
                Vector3d aM = d.MagnusAcceleration * (1.0 / Units.MetersPerFoot);
                double tFull = tPlate - tRelease;
                double tWindow = tPlate - t40;
                SetPfx(r, 0, 0.5 * p.Ax * tWindow * tWindow, 0.5 * (p.Az + gFeet) * tWindow * tWindow, p);
                SetPfx(r, 1, 0.5 * aM.X * tFull * tFull, 0.5 * aM.Z * tFull * tFull, p);
                SetPfx(r, 2, 0.5 * aM.X * tWindow * tWindow, 0.5 * aM.Z * tWindow * tWindow, p);
                SetPfx(r, 3, 0.5 * p.Ax * tFull * tFull, 0.5 * (p.Az + gFeet) * tFull * tFull, p);
                PitchSimulationResult fromRelease = ReplayFrom(fit, tRelease, rpm, p.SpinAxisDegrees, environment);
                r.PfxErrorX[4] = Units.MetersToInches(fromRelease.Metrics.HorizontalMovement) - p.PfxX * 12.0;
                r.PfxErrorZ[4] = Units.MetersToInches(fromRelease.Metrics.VerticalMovement) - p.PfxZ * 12.0;
                results.Add(r);
            }

            // 4b. Holdout prediction: efficiency = median implied efficiency of the same family in the development game.
            var devEfficiency = results.Where(x => x.Role == StatcastFixture.Development && IsFinite(x.ImpliedEfficiency))
                .GroupBy(x => x.Family).ToDictionary(g => g.Key, g => Median(g.Select(x => x.ImpliedEfficiency)));
            int i = 0;
            foreach (StatcastFixture.Row row in StatcastFixture.Rows)
            {
                PitchResult r = results[i++];
                if (row.Role != StatcastFixture.Holdout || !devEfficiency.TryGetValue(r.Family, out double efficiency)) continue;
                var fit = new StatcastNinePointFit(row.Pitch);
                var replay = Replay(fit, efficiency * row.Pitch.SpinRateRpm, row.Pitch.SpinAxisDegrees, StatcastFixture.EnvironmentFor(row.Role));
                r.PredictionErrorX = Units.MetersToInches(replay.Metrics.PlateX) - row.Pitch.PlateX * 12.0;
                r.PredictionErrorZ = Units.MetersToInches(replay.Metrics.PlateZ) - row.Pitch.PlateZ * 12.0;
            }

            return results;
        }

        private struct PitchSimulationResult
        {
            public PitchMetrics Metrics;
        }

        private static PitchSimulationResult Replay(StatcastNinePointFit fit, double transverseRpm, double spinAxis, EnvironmentState environment) =>
            ReplayFrom(fit, 0.0, transverseRpm, spinAxis, environment);

        private static PitchSimulationResult ReplayFrom(StatcastNinePointFit fit, double t, double transverseRpm, double spinAxis, EnvironmentState environment)
        {
            BallState start = StatcastAdapter.FitState(fit, t, StatcastAdapter.TransverseSpin(transverseRpm, spinAxis));
            return new PitchSimulationResult
            {
                Metrics = PitchSimulation.Run(start, BallProperties.Baseball, environment, AerodynamicModel.Baseball).Metrics,
            };
        }

        private static PitchMetrics ReplayWithObservedDirection(StatcastNinePointFit fit, AerodynamicDecomposition d, EnvironmentState environment)
        {
            double s = AerodynamicModel.SpinParameterForLift(d.ImpliedLiftCoefficient);
            double omega = IsFinite(s) ? s * d.AirSpeed / BallProperties.Baseball.Radius : 0.0;
            Vector3d airAtStart = fit.VelocityFeet(0.0) * Units.MetersPerFoot - environment.Wind;
            Vector3d spin = omega * Vector3d.Cross(airAtStart, d.MagnusAcceleration).Normalized;
            return PitchSimulation.Run(StatcastAdapter.FitState(fit, 0.0, spin), BallProperties.Baseball, environment, AerodynamicModel.Baseball).Metrics;
        }

        private static void SetPfx(PitchResult r, int index, double xFeet, double zFeet, StatcastPitch p)
        {
            r.PfxErrorX[index] = (xFeet - p.PfxX) * 12.0;
            r.PfxErrorZ[index] = (zFeet - p.PfxZ) * 12.0;
        }

        /// <summary>
        /// Per (pitcher, pitch type, game) group with ≥ 3 pitches and a Savant value: median implied efficiency divided by
        /// Savant's measured active spin. ≈ 1 means our C_L turns the measured transverse spin into the observed lift.
        /// </summary>
        public static IEnumerable<(string Family, string PitchType, string PitcherId, int Count, double Ratio)> ActiveSpinRatios() =>
            Results.Where(r => IsFinite(r.ActiveSpin) && IsFinite(r.ImpliedEfficiency))
                .GroupBy(r => (r.PitcherId, r.PitchType, r.Role))
                .Where(g => g.Count() >= 3)
                .Select(g => (g.First().Family, g.Key.PitchType, g.Key.PitcherId, g.Count(), Median(g.Select(r => r.ImpliedEfficiency)) / g.First().ActiveSpin));

        public static double Median(IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return double.NaN;
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
        }

        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        private static double Sq(double v) => v * v;
        private static double WrapDegrees(double degrees) => ((degrees + 540.0) % 360.0) - 180.0;
    }
}
