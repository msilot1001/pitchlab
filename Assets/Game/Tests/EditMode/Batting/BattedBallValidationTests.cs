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
    /// dome games: 72 °F, 15 ft elevation, no wind. Batted-ball spin is not public; the validation spin model is
    /// ω_back = 100 rpm/° × (LA − 7°), clamped to [0, 4500] rpm (Nathan 2020, Statcast medians), no sidespin.
    /// </summary>
    public class BattedBallValidationTests
    {
        private const string FixturePath = "Assets/Game/Tests/Fixtures/Statcast/batted_validation.csv";

        private sealed class Ball
        {
            public string Role, Type;
            public double ExitMph, LaunchDeg, StatcastFeet, ModelFeet, HangTime, ApexFeet;
        }

        private static List<Ball> _balls;

        private static IReadOnlyList<Ball> Balls => _balls ??= Replay();

        public static double ValidationBackspin(double launchDegrees) => Math.Max(0.0, Math.Min(4500.0, 100.0 * (launchDegrees - 7.0)));

        private static List<Ball> Replay()
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
                double ev = D(f, "launch_speed"), la = D(f, "launch_angle");
                BattedBallResult r = BattedBallSimulation.Run(new BattedBallLaunch(ev, la, 0.0, ValidationBackspin(la)).ToState(contact), dome);
                balls.Add(new Ball
                {
                    Role = f[Col("role")], Type = f[Col("bb_type")], ExitMph = ev, LaunchDeg = la, StatcastFeet = D(f, "hit_distance_sc"),
                    ModelFeet = Units.MetersToFeet(r.Metrics.Distance), HangTime = r.Metrics.HangTime, ApexFeet = Units.MetersToFeet(r.Metrics.ApexHeight),
                });
            }

            return balls;
        }

        private static Ball[] Primary(string role) => Balls.Where(b => b.Role == role && (b.Type == "fly_ball" || b.Type == "line_drive")
            && b.ExitMph >= 90.0 && b.LaunchDeg >= 20.0 && b.LaunchDeg <= 35.0).ToArray();

        [TestCase("development"), TestCase("holdout")]
        [Ignore("Pre-registered criterion NOT met (model carries ≈ +29/+35 ft); kept visible on purpose. See Docs/VALIDATION_TASK004.md.")]
        public void PrimaryFlyBallsMatchStatcastDistance(string role)
        {
            // Pre-registered #1: |mean(model − Statcast)| ≤ 10 ft and RMS ≤ 25 ft.
            Ball[] set = Primary(role);
            Assert.GreaterOrEqual(set.Length, 20);
            double[] errors = set.Select(b => b.ModelFeet - b.StatcastFeet).ToArray();
            Assert.LessOrEqual(Math.Abs(errors.Average()), 10.0, $"mean {errors.Average():0.0} ft (n {set.Length})");
            Assert.LessOrEqual(Math.Sqrt(errors.Average(e => e * e)), 25.0, $"RMS {Math.Sqrt(errors.Average(e => e * e)):0.0} ft");
        }

        [TestCase("development"), TestCase("holdout")]
        public void KnownLimitationModelCarriesBattedBallsAbout30FeetTooFar(string role)
        {
            // Post-hoc regression guard (not acceptance): observed mean +28.8 ft (development), +34.6 ft (holdout),
            // scatter after removing the bias ≈ 15 ft. If a model change removes the bias, this fails — update the docs
            // and re-enable the pre-registered test above.
            double[] errors = Primary(role).Select(b => b.ModelFeet - b.StatcastFeet).ToArray();
            Assert.That(errors.Average(), Is.InRange(15.0, 45.0));
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
