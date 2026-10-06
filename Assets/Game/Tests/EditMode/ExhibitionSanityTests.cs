using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-022: whole CPU-vs-CPU games on the production pipeline (CPU pitcher → execution → CPU batter → contact → live
    /// play with defensive execution) — a statistical sanity sample and archetype matchups. Bounds are regression guards on
    /// directions, not MLB calibration (Docs/AI_EXHIBITION.md reports the numbers and the discrepancies).
    /// </summary>
    public class ExhibitionSanityTests
    {
        /// <summary>What one side did at bat over a set of games (its misplays: those of the defense it faced).</summary>
        public sealed class Line
        {
            public int Games, PlateAppearances, Walks, Strikeouts, InPlay, HomeRuns, Runs, Misplays, Pitches;
            public double Per(int n) => n / (double)Math.Max(1, PlateAppearances);
            public double RunsPerGame => Runs / (double)Math.Max(1, Games);
            public double HomeRunsPerGame => HomeRuns / (double)Math.Max(1, Games);
            public double MisplaysPerGame => Misplays / (double)Math.Max(1, Games);

            public override string ToString() =>
                $"{Games} g · {PlateAppearances / (double)Math.Max(1, Games):F1} PA/g · R/g {RunsPerGame:F1} · BB {100 * Per(Walks):F1}% · K {100 * Per(Strikeouts):F1}% · " +
                $"BIP {InPlay / (double)Math.Max(1, Games):F1}/g · HR/g {HomeRunsPerGame:F2} · misplays/g {MisplaysPerGame:F2} · P/PA {Pitches / (double)Math.Max(1, PlateAppearances):F2}";
        }

        /// <summary>Plays the games and returns the line of the side at bat in <paramref name="half"/> (null: both sides).</summary>
        public static Line Play(Func<int, GameState> game, IEnumerable<int> seeds, Half? half = null)
        {
            var line = new Line();
            foreach (int seed in seeds)
            {
                GameState g = game(seed);
                var sim = new GameSimulator(g);
                int misplaysBefore = 0;
                while (!g.IsOver)
                {
                    Half now = g.Half;
                    sim.PlayPitch();
                    if (half == null || now == half) line.Misplays += sim.Misplays.Count - misplaysBefore;
                    misplaysBefore = sim.Misplays.Count;
                }

                line.Games++;
                line.Runs += half == Half.Top ? g.AwayScore : half == Half.Bottom ? g.HomeScore : g.AwayScore + g.HomeScore;
                foreach (PlateAppearance pa in g.Completed)
                {
                    if (half != null && pa.Half != half) continue;
                    line.PlateAppearances++;
                    line.Pitches += pa.Pitches.Count;
                    if (pa.End == PlateAppearanceEnd.Walk) line.Walks++;
                    else if (pa.End == PlateAppearanceEnd.Strikeout) line.Strikeouts++;
                    else if (pa.End == PlateAppearanceEnd.InPlay) line.InPlay++;
                    if (pa.PlayResult == PlayResultKind.HomeRun || pa.PlayResult == PlayResultKind.InsideTheParkHomeRun) line.HomeRuns++;
                }
            }

            return line;
        }

        /// <summary>A game: <paramref name="batting"/> (away) against <paramref name="fielding"/> (home, with its pitcher).</summary>
        private static Func<int, GameState> Matchup(Func<Team> batting, Func<Team> fielding) => seed => new GameState(batting(), fielding(), seed);

        private static PlayerProfile Starter(string id, GenericRosters.Pitching p, Repertoire r) => GenericRosters.Pitcher(id, Hand.Right, p, r);

        public static readonly GenericRosters.Pitching PoorCommand = new GenericRosters.Pitching(55, 15, 50, 60);
        public static readonly GenericRosters.Pitching AveragePitcher = new GenericRosters.Pitching(50, 50, 50, 60);

        private static Team Lineup(GenericRosters.Batting b, GenericRosters.Defense? d = null) => GenericRosters.Uniform("Bat", "B", b, d ?? GenericRosters.AverageDefender, Starter("BP", AveragePitcher, GenericRosters.PowerRepertoire()));
        private static Team Staff(GenericRosters.Pitching p, Repertoire r, GenericRosters.Defense? d = null) => GenericRosters.Uniform("Field", "F", GenericRosters.BalancedHitter, d ?? GenericRosters.AverageDefender, Starter("FP", p, r));

        /// <summary>Development report: the standard rosters and the archetype matchups.</summary>
        public static string Report(int games = 30, int matchupGames = 12)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"standard rosters: {Play(s => new GameState(GenericRosters.Away(), GenericRosters.Home(), s), Enumerable.Range(1001, games))}");
            IEnumerable<int> Seeds() => Enumerable.Range(2001, matchupGames);
            void Row(string name, Func<Team> bat, Func<Team> field) => sb.AppendLine($"{name}: {Play(Matchup(bat, field), Seeds(), Half.Top)}");
            Row("power pitcher vs contact hitters", () => Lineup(GenericRosters.ContactHitter), () => Staff(GenericRosters.PowerStarter, GenericRosters.PowerRepertoire()));
            Row("power pitcher vs free swingers", () => Lineup(GenericRosters.FreeSwinger), () => Staff(GenericRosters.PowerStarter, GenericRosters.PowerRepertoire()));
            Row("command pitcher vs patient hitters", () => Lineup(GenericRosters.PatientHitter), () => Staff(GenericRosters.CommandStarter, GenericRosters.CommandRepertoire()));
            Row("poor-command pitcher vs patient hitters", () => Lineup(GenericRosters.PatientHitter), () => Staff(PoorCommand, GenericRosters.PowerRepertoire()));
            Row("poor-command pitcher vs power hitters", () => Lineup(GenericRosters.PowerHitter), () => Staff(PoorCommand, GenericRosters.PowerRepertoire()));
            Row("poor-command pitcher vs contact hitters", () => Lineup(GenericRosters.ContactHitter), () => Staff(PoorCommand, GenericRosters.PowerRepertoire()));
            Row("balanced hitters vs elite defense", () => Lineup(GenericRosters.BalancedHitter), () => Staff(AveragePitcher, GenericRosters.PowerRepertoire(), GenericRosters.EliteDefender));
            Row("balanced hitters vs poor defense", () => Lineup(GenericRosters.BalancedHitter), () => Staff(AveragePitcher, GenericRosters.PowerRepertoire(), GenericRosters.PoorDefender));
            return sb.ToString();
        }

        [Test]
        public void ThePitcherAndTheHittersBothMatter()
        {
            // Regression guards on directions (prototype bounds, not validation).
            IEnumerable<int> seeds = Enumerable.Range(3001, 4);
            Line patientVsCommand = Play(Matchup(() => Lineup(GenericRosters.PatientHitter), () => Staff(GenericRosters.CommandStarter, GenericRosters.CommandRepertoire())), seeds, Half.Top);
            Line patientVsWild = Play(Matchup(() => Lineup(GenericRosters.PatientHitter), () => Staff(PoorCommand, GenericRosters.PowerRepertoire())), seeds, Half.Top);
            Assert.Greater(patientVsWild.Per(patientVsWild.Walks), 2.0 * patientVsCommand.Per(patientVsCommand.Walks), $"walks: {patientVsWild} vs {patientVsCommand}");
            // Strikeout rates need more plate appearances than walks to separate reliably: 10 games (≈ 450 PA) a side.
            IEnumerable<int> more = Enumerable.Range(3001, 10);
            Line contact = Play(Matchup(() => Lineup(GenericRosters.ContactHitter), () => Staff(GenericRosters.PowerStarter, GenericRosters.PowerRepertoire())), more, Half.Top);
            Line free = Play(Matchup(() => Lineup(GenericRosters.FreeSwinger), () => Staff(GenericRosters.PowerStarter, GenericRosters.PowerRepertoire())), more, Half.Top);
            // ≈ 1.5× on this sample (15 vs 22.5 %; 11 vs 24 % on the report's): a guard at 1.3×.
            Assert.Less(1.3 * contact.Per(contact.Strikeouts), free.Per(free.Strikeouts), $"strikeouts: {contact} vs {free}");
            // The defense behind the pitcher (TASK-021): a poor one misplays more.
            Line elite = Play(Matchup(() => Lineup(GenericRosters.BalancedHitter), () => Staff(AveragePitcher, GenericRosters.PowerRepertoire(), GenericRosters.EliteDefender)), Enumerable.Range(3101, 8), Half.Top);
            Line poor = Play(Matchup(() => Lineup(GenericRosters.BalancedHitter), () => Staff(AveragePitcher, GenericRosters.PowerRepertoire(), GenericRosters.PoorDefender)), Enumerable.Range(3101, 8), Half.Top);
            Assert.Less(elite.Misplays, poor.Misplays, $"misplays: elite {elite.Misplays} vs poor {poor.Misplays}");
        }

        [Test]
        public void TheStandardGameIsABaseballGame()
        {
            // Broad sanity (wide regression guards: the sample reports the real discrepancies): every game ends, plate
            // appearances, walks, strikeouts and balls in play all happen.
            Line l = Play(s => new GameState(GenericRosters.Away(), GenericRosters.Home(), s), Enumerable.Range(1001, 4));
            Assert.That(l.PlateAppearances / (double)l.Games, Is.InRange(60.0, 110.0), l.ToString());
            Assert.That(l.Per(l.Walks), Is.InRange(0.02, 0.20), l.ToString());
            Assert.That(l.Per(l.Strikeouts), Is.InRange(0.08, 0.35), l.ToString());
            Assert.That(l.Per(l.InPlay), Is.InRange(0.5, 0.85), l.ToString());
            Assert.That(l.Pitches / (double)l.PlateAppearances, Is.InRange(3.0, 4.5), l.ToString());
            Assert.Greater(l.Misplays, 0, "the defense misplays sometimes");
            // Guards on the known discrepancies (Docs/AI_EXHIBITION.md: ≈ 16 runs and 8.7 home runs per game against MLB ≈ 9 and
            // 2.3): they must not get worse unnoticed.
            Assert.Less(l.RunsPerGame, 25.0, l.ToString());
            Assert.Less(l.HomeRunsPerGame, 12.0, l.ToString());
        }
    }
}
