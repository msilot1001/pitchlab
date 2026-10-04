using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Tests.Batting
{
    /// <summary>
    /// TASK-004 replay of tracked batted balls (pre-registered: ExecPlan "E pre-registered analysis"). Tropicana Field
    /// dome games: 72 °F, 15 ft elevation, no wind. Batted-ball spin is not public, so spin comes from Nathan's
    /// average-Statcast spin model (<see cref="AverageStatcastSpin"/>). The pre-registered backspin-only rule
    /// (<see cref="BackspinOnlyRule"/>) omitted sidespin and carried balls ≈ 30 ft too far (Docs/VALIDATION_TASK004.md).
    /// </summary>
    public class BattedBallValidationTests
    {
        private const string FixturePath = "Assets/Game/Tests/Fixtures/Statcast/batted_validation.csv";

        private sealed class Ball
        {
            public string Role, Type;
            public double ExitMph, LaunchDeg, SprayDeg, StatcastFeet, ModelFeet, HangTime, ApexFeet;
            public bool RightHanded;
        }

        private static List<Ball> _balls;

        private static IReadOnlyList<Ball> Balls => _balls ??= Replay(AerodynamicModel.BattedBall, AverageStatcastSpin);

        /// <summary>(backspin, sidespin) rpm in <see cref="BattedBallLaunch"/> convention, from LA°, spray° and batter hand.</summary>
        private delegate (double Back, double Side) SpinModel(double launchDeg, double sprayDeg, bool rightHanded);

        /// <summary>Pre-registered TASK-004 rule: ω_back = 100 rpm/° × (LA − 7°) in [0, 4500] rpm, no sidespin. Superseded.</summary>
        private static (double, double) BackspinOnlyRule(double la, double spray, bool rhb) => (Math.Max(0.0, Math.Min(4500.0, 100.0 * (la - 7.0))), 0.0);

        /// <summary>
        /// Nathan, TrajectoryCalculator-new-3D.xlsx, sheet "BattedBallTrajectory-2" (spin "fixed at average Statcast
        /// values"): ω_b = −763 + 120·LA + 21·φ·s, ω_s = −849·s − 94·φ, s = +1 RHB / −1 LHB, φ = spray (+ toward RF).
        /// Nathan's ω_s > 0 breaks toward LF, hence the sign flip for <see cref="BattedBallLaunch.SidespinRpm"/>.
        /// </summary>
        internal static (double Back, double Side) AverageStatcastSpin(double la, double spray, bool rhb)
        {
            double s = rhb ? 1.0 : -1.0;
            return (-763.0 + 120.0 * la + 21.0 * spray * s, -(-849.0 * s - 94.0 * spray));
        }

        /// <summary>
        /// Models compared in the report. Add a candidate only with a cited, independently derived parameterisation
        /// (Docs/VALIDATION_TASK004.md); never one fitted to this fixture.
        /// </summary>
        private static readonly (string Name, AerodynamicModel Model, SpinModel Spin)[] Candidates =
        {
            ("pre-registered: Nathan 2017 fit, τ 30 s, backspin-only rule", AerodynamicModel.BattedBall, BackspinOnlyRule),
            ("adopted: Nathan 2017 fit, τ 30 s, average Statcast spin", AerodynamicModel.BattedBall, AverageStatcastSpin),
            ("cross-check: Nathan calculator (C_D,0 0.3008, no decay), average Statcast spin", new AerodynamicModel(0.3008, true, 0.0292), AverageStatcastSpin),
        };

        /// <summary>Spray angle (+ toward RF) from Statcast hit coordinates, home plate at hc (125.42, 198.27).</summary>
        private static double SprayDegrees(string hcX, string hcY) => Units.RadiansToDegrees(
            Math.Atan2(double.Parse(hcX, CultureInfo.InvariantCulture) - 125.42, 198.27 - double.Parse(hcY, CultureInfo.InvariantCulture)));

        private static List<Ball> Replay(AerodynamicModel model, SpinModel spinModel)
        {
            string[] lines = File.ReadAllLines(FixturePath);
            string[] header = lines[0].Split(',');
            int Col(string name) => Array.IndexOf(header, name);
            var rows = lines.Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')).ToList();
            double D(string[] f, string name) => double.Parse(f[Col(name)], CultureInfo.InvariantCulture);

            // Imputed EV/LA (Statcast fills untracked balls with repeated values): drop pairs that occur more than once.
            var pairCounts = rows.GroupBy(f => (f[Col("launch_speed")], f[Col("launch_angle")])).ToDictionary(g => g.Key, g => g.Count());
            EnvironmentState dome = EnvironmentState.FromWeather((72.0 - 32.0) * 5.0 / 9.0,
                EnvironmentState.StandardAtmospherePressure(Units.FeetToMeters(15.0)), 0.5, Vector3d.Zero);
            var contact = new Vector3d(0.0, 0.7, 0.9);
            var balls = new List<Ball>();
            foreach (string[] f in rows)
            {
                if (pairCounts[(f[Col("launch_speed")], f[Col("launch_angle")])] > 1) continue;
                double ev = D(f, "launch_speed"), la = D(f, "launch_angle"), spray = SprayDegrees(f[Col("hc_x")], f[Col("hc_y")]);
                bool rhb = f[Col("stand")] == "R";
                var (back, side) = spinModel(la, spray, rhb);
                BattedBallResult r = BattedBallSimulation.Run(new BattedBallLaunch(ev, la, spray, back, side).ToState(contact), dome, model);
                balls.Add(new Ball
                {
                    Role = f[Col("role")], Type = f[Col("bb_type")], ExitMph = ev, LaunchDeg = la, SprayDeg = spray, RightHanded = rhb, StatcastFeet = D(f, "hit_distance_sc"),
                    ModelFeet = Units.MetersToFeet(r.Metrics.Distance), HangTime = r.Metrics.HangTime, ApexFeet = Units.MetersToFeet(r.Metrics.ApexHeight),
                });
            }

            return balls;
        }

        private static Ball[] Primary(string role) => Primary(Balls, role);

        private static Ball[] Primary(IEnumerable<Ball> balls, string role) => balls.Where(b => b.Role == role && (b.Type == "fly_ball" || b.Type == "line_drive")
            && b.ExitMph >= 90.0 && b.LaunchDeg >= 20.0 && b.LaunchDeg <= 35.0).ToArray();

        [TestCase("development"), TestCase("holdout")]
        public void PrimaryFlyBallsMatchStatcastDistance(string role)
        {
            // Pre-registered #1: |mean(model − Statcast)| ≤ 10 ft and RMS ≤ 25 ft.
            Ball[] set = Primary(role);
            Assert.GreaterOrEqual(set.Length, 20);
            double[] errors = set.Select(b => b.ModelFeet - b.StatcastFeet).ToArray();
            Assert.LessOrEqual(Math.Abs(errors.Average()), 10.0, $"mean {errors.Average():0.0} ft (n {set.Length})");
            Assert.LessOrEqual(Math.Sqrt(errors.Average(e => e * e)), 25.0, $"RMS {Math.Sqrt(errors.Average(e => e * e)):0.0} ft");
        }

        [Test]
        public void BattersPullGroundBalls()
        {
            // Guards the spray sign (hc_x → + toward RF) and the stand → hand mapping that the spin model depends on.
            Ball[] grounders = Balls.Where(b => b.Type == "ground_ball").ToArray();
            Assert.Less(grounders.Where(b => b.RightHanded).Average(b => b.SprayDeg), -5.0, "right-handed batters pull to LF");
            Assert.Greater(grounders.Where(b => !b.RightHanded).Average(b => b.SprayDeg), 5.0, "left-handed batters pull to RF");
        }

        [Test, Explicit("Writes TestResults/batted_validation.md; run on demand for the TASK-004 report.")]
        public void WriteBattedValidationReport()
        {
            var md = new StringBuilder();
            md.AppendLine($"Balls replayed: {Balls.Count} (imputed EV/LA dropped)");
            md.AppendLine();
            md.AppendLine("| set | n | mean error ft | median error ft | RMS ft | mean Statcast ft | mean model ft | model hang time s (mean) |");
            md.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (string role in new[] { "development", "holdout" })
            {
                Ball[] p = Primary(role);
                md.AppendLine(Row($"{role} primary (EV≥90, LA 20–35)", p));
            }

            md.AppendLine();
            md.AppendLine("| launch angle bucket (all airborne, both roles) | n | mean error ft | RMS ft | mean Statcast ft | mean model ft |");
            md.AppendLine("|---|---|---|---|---|---|");
            for (int lo = 10; lo < 45; lo += 5)
            {
                Ball[] g = Balls.Where(b => b.Type != "ground_ball" && b.LaunchDeg >= lo && b.LaunchDeg < lo + 5).ToArray();
                if (g.Length == 0) continue;
                double[] e = g.Select(b => b.ModelFeet - b.StatcastFeet).ToArray();
                md.AppendLine($"| {lo}–{lo + 5}° | {g.Length} | {e.Average():0.0} | {Math.Sqrt(e.Average(x => x * x)):0.0} | {g.Average(b => b.StatcastFeet):0} | {g.Average(b => b.ModelFeet):0} |");
            }

            md.AppendLine();
            md.AppendLine("| exit speed bucket (primary, both roles) | n | mean error ft |");
            md.AppendLine("|---|---|---|");
            Ball[] all = Primary("development").Concat(Primary("holdout")).ToArray();
            for (int lo = 90; lo < 115; lo += 5)
            {
                Ball[] g = all.Where(b => b.ExitMph >= lo && b.ExitMph < lo + 5).ToArray();
                if (g.Length > 0) md.AppendLine($"| {lo}–{lo + 5} mph | {g.Length} | {g.Average(b => b.ModelFeet - b.StatcastFeet):0.0} |");
            }

            md.AppendLine();
            md.AppendLine("Candidate models on the primary set (EV ≥ 90 mph, LA 20–35°): error = model − Statcast, ft.");
            md.AppendLine();
            md.AppendLine("| model | set | n | mean | MAE | median | RMSE | mean by EV 90–100 / 100–110 | mean by LA 20–27.5 / 27.5–35 | mean by predicted distance low / mid / high tercile |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var (name, model, spin) in Candidates)
            {
                List<Ball> replay = Replay(model, spin);
                foreach (string role in new[] { "development", "holdout" })
                {
                    Ball[] p = Primary(replay, role);
                    double[] e = p.Select(b => b.ModelFeet - b.StatcastFeet).OrderBy(x => x).ToArray();
                    double Mean(IEnumerable<Ball> g) { var a = g.ToArray(); return a.Length == 0 ? double.NaN : a.Average(b => b.ModelFeet - b.StatcastFeet); }
                    Ball[] byPred = p.OrderBy(b => b.ModelFeet).ToArray();
                    int third = byPred.Length / 3;
                    md.AppendLine($"| {name} | {role} | {p.Length} | {e.Average():0.0} | {e.Average(Math.Abs):0.0} | {e[e.Length / 2]:0.0} | {Math.Sqrt(e.Average(x => x * x)):0.0} | " +
                                  $"{Mean(p.Where(b => b.ExitMph < 100)):0.0} / {Mean(p.Where(b => b.ExitMph >= 100)):0.0} | " +
                                  $"{Mean(p.Where(b => b.LaunchDeg < 27.5)):0.0} / {Mean(p.Where(b => b.LaunchDeg >= 27.5)):0.0} | " +
                                  $"{Mean(byPred.Take(third)):0.0} / {Mean(byPred.Skip(third).Take(byPred.Length - 2 * third)):0.0} / {Mean(byPred.Skip(byPred.Length - third)):0.0} |");
                }
            }

            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/batted_validation.md", md.ToString());
            TestContext.WriteLine(md.ToString());
        }

        private static string Row(string name, Ball[] g)
        {
            double[] e = g.Select(b => b.ModelFeet - b.StatcastFeet).OrderBy(x => x).ToArray();
            return $"| {name} | {g.Length} | {e.Average():0.0} | {e[e.Length / 2]:0.0} | {Math.Sqrt(e.Average(x => x * x)):0.0} | {g.Average(b => b.StatcastFeet):0} | {g.Average(b => b.ModelFeet):0} | {g.Average(b => b.HangTime):0.00} |";
        }
    }
}
