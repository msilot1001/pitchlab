using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-023: offense calibration — what simulated games produce at the plate and off the bat, against MLB league values
    /// (Docs/OFFENSE_CALIBRATION.md). The CPU batter's inputs are calibrated; the contact physics is not touched.
    /// </summary>
    public class OffenseCalibrationTests
    {
        public sealed class Sample
        {
            public int Games, PlateAppearances, Strikeouts, Walks, HomeRuns, Runs, Pitches;
            public int ZonePitches, OutPitches, ZoneSwings, OutSwings, ZoneContact, OutContact;
            public readonly List<(double Ev, double La, bool HomeRun)> Fair = new List<(double, double, bool)>();
            public double AlongSum, VerticalSum;
            public int Contacts;

            private static double Pct(double n, double d) => 100.0 * n / Math.Max(1.0, d);

            public double Mean(Func<(double Ev, double La, bool HomeRun), double> f) => Fair.Count == 0 ? double.NaN : Fair.Average(f);
            public double Share(Func<(double Ev, double La, bool HomeRun), bool> f) => Pct(Fair.Count(f), Fair.Count);
            public double KRate => Pct(Strikeouts, PlateAppearances);
            public double BBRate => Pct(Walks, PlateAppearances);
            public double RunsPerTeamGame => Runs / (2.0 * Math.Max(1, Games));
            public double HomeRunsPerTeamGame => HomeRuns / (2.0 * Math.Max(1, Games));
            public double ExitMph => Mean(b => b.Ev);
            public double LaunchDeg => Mean(b => b.La);
            public double LaunchSd => Math.Sqrt(Mean(b => (b.La - LaunchDeg) * (b.La - LaunchDeg)));
            public double HardHit => Share(b => b.Ev >= 95.0);
            /// <summary>Statcast barrel (approximation of its definition): EV ≥ 98 mph with LA in 26–30°, the window widening by
            /// ≈ 1° each side per mph above 98 (2–3° on the high side), to 8–50° at 116 mph.</summary>
            public double Barrel => Share(b => b.Ev >= 98.0 && b.La >= Math.Max(8.0, 26.0 - (b.Ev - 98.0)) && b.La <= Math.Min(50.0, 30.0 + 2.0 * (b.Ev - 98.0)));
            public double SweetSpot => Share(b => b.La >= 8.0 && b.La <= 32.0);
            public double GroundBall => Share(b => b.La < 10.0);
            public double LineDrive => Share(b => b.La >= 10.0 && b.La < 25.0);
            public double FlyBall => Share(b => b.La >= 25.0 && b.La <= 50.0);
            public double PopUp => Share(b => b.La > 50.0);
            public double HomeRunPerFly => Pct(Fair.Count(b => b.HomeRun && b.La >= 25.0 && b.La <= 50.0), Fair.Count(b => b.La >= 25.0 && b.La <= 50.0));
            public double ZoneContactRate => Pct(ZoneContact, ZoneSwings);
            public double OutContactRate => Pct(OutContact, OutSwings);
            public double ContactRate => Pct(ZoneContact + OutContact, ZoneSwings + OutSwings);
            public double ZoneSwingRate => Pct(ZoneSwings, ZonePitches);
            public double ChaseRate => Pct(OutSwings, OutPitches);

            public override string ToString() =>
                $"{Games} g · R/team-g {RunsPerTeamGame:F2} · HR/team-g {HomeRunsPerTeamGame:F2} · K {KRate:F1}% · BB {BBRate:F1}% · P/PA {Pitches / (double)Math.Max(1, PlateAppearances):F2}\n" +
                $"swing: Z {ZoneSwingRate:F0}% O {ChaseRate:F0}% · contact {ContactRate:F1}% (Z {ZoneContactRate:F1}% O {OutContactRate:F1}%)\n" +
                $"BBE {Fair.Count}: EV {ExitMph:F1} mph · LA {LaunchDeg:F1}° (SD {LaunchSd:F1}) · hard-hit {HardHit:F1}% · barrel {Barrel:F1}% · sweet-spot {SweetSpot:F1}%\n" +
                $"GB {GroundBall:F0}% LD {LineDrive:F0}% FB {FlyBall:F0}% PU {PopUp:F0}% · HR/FB {HomeRunPerFly:F1}% · |along| {100 * AlongSum / Math.Max(1, Contacts):F1} cm |vertical| {100 * VerticalSum / Math.Max(1, Contacts):F1} cm";
        }

        public static Sample Run(IEnumerable<int> seeds)
        {
            var s = new Sample();
            foreach (int seed in seeds)
            {
                var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);
                var sim = new GameSimulator(g);
                LivePlay seen = null;
                while (!g.IsOver)
                {
                    PlayerProfile batter = g.Batter;
                    PitchOutcome o = sim.PlayPitch();
                    if (sim.LastContact is ContactResult r && r.IsContact)
                    {
                        s.Contacts++;
                        s.AlongSum += Math.Abs(r.OffsetAlongBarrel);
                        s.VerticalSum += Math.Abs(r.VerticalOffset);
                    }

                    if (o == PitchOutcome.InPlay && !ReferenceEquals(sim.LastPlay, seen))
                    {
                        seen = sim.LastPlay;
                        Vector3d v = seen.Fielding.Ball.First.Velocity;
                        double ev = v.Length / Units.MphToMetersPerSecond(1.0), la = Units.RadiansToDegrees(Math.Asin(v.Z / v.Length));
                        s.Fair.Add((ev, la, seen.AwardedBases == 4));
                    }
                }

                s.Games++;
                s.Runs += g.AwayScore + g.HomeScore;
                foreach (PlateAppearance pa in g.Completed)
                {
                    s.PlateAppearances++;
                    s.Pitches += pa.Pitches.Count;
                    if (pa.End == PlateAppearanceEnd.Strikeout) s.Strikeouts++;
                    if (pa.End == PlateAppearanceEnd.Walk) s.Walks++;
                    if (pa.PlayResult == PlayResultKind.HomeRun || pa.PlayResult == PlayResultKind.InsideTheParkHomeRun) s.HomeRuns++;
                    foreach (PitchEvent e in pa.Pitches)
                    {
                        if (!e.Info.HasCrossing) continue;
                        bool zone = StrikeZone.Contains(e.Info.PlateX, e.Info.PlateZ, pa.Batter.ZoneBottom, pa.Batter.ZoneTop);
                        bool swung = e.Outcome == PitchOutcome.SwingingStrike || e.Outcome == PitchOutcome.Foul || e.Outcome == PitchOutcome.InPlay;
                        bool contact = e.Outcome == PitchOutcome.Foul || e.Outcome == PitchOutcome.InPlay;
                        if (zone)
                        {
                            s.ZonePitches++;
                            if (swung) s.ZoneSwings++;
                            if (contact) s.ZoneContact++;
                        }
                        else
                        {
                            s.OutPitches++;
                            if (swung) s.OutSwings++;
                            if (contact) s.OutContact++;
                        }
                    }
                }
            }

            return s;
        }

        [Test]
        public void SimulatedOffenseStaysNearMlb()
        {
            // Regression guards around MLB 2024 league values (FanGraphs/Savant; Docs/OFFENSE_CALIBRATION.md) — bands for an
            // 8-game sample, set after calibration: not validation, a tripwire against drifting back.
            Sample s = Run(Enumerable.Range(5101, 8));
            string all = s.ToString();
            Assert.That(s.RunsPerTeamGame, Is.InRange(3.0, 6.5), all);          // MLB 4.39
            Assert.That(s.KRate, Is.InRange(16.0, 28.0), all);                  // 22.6 %
            Assert.That(s.BBRate, Is.InRange(5.0, 13.5), all);                  // 8.2 %
            Assert.That(s.ExitMph, Is.InRange(84.5, 92.0), all);                // 88.8 mph
            Assert.That(s.LaunchDeg, Is.InRange(6.0, 17.0), all);               // 13.3°
            Assert.That(s.HardHit, Is.InRange(30.0, 48.0), all);                // 38.9 %
            Assert.That(s.Barrel, Is.InRange(3.5, 12.0), all);                  // 7.8 %
            Assert.That(s.ContactRate, Is.InRange(62.0, 82.0), all);            // 76.8 %
            // The known discrepancy (≈ 2× MLB's 1.13 per team-game; Docs/OFFENSE_CALIBRATION.md): must not get worse.
            Assert.Less(s.HomeRunsPerTeamGame, 3.2, all);
        }

        /// <summary>Development report (Docs/OFFENSE_CALIBRATION.md).</summary>
        public static string Report(int games = 30) => Run(Enumerable.Range(5001, games)).ToString();
    }
}
