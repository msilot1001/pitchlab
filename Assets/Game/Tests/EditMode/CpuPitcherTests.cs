using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-020: the CPU pitcher's calls — seeded, explainable, count-, matchup- and batter-aware, repertoire-bound, not
    /// repetitive — and their effect once executed (zone rate by count) in simulated games.
    /// </summary>
    public class CpuPitcherTests
    {
        private static readonly IReadOnlyList<PitchEvent> None = Array.Empty<PitchEvent>();

        private static PlayerProfile Pitcher(Repertoire repertoire, Hand hand = Hand.Right, string id = "P") =>
            new PlayerProfile(id, id, BatterSide.Right, hand, 75.0, Gameplay.Fielding.DefensivePosition.P, new PlayerRatings(), repertoire);

        private static PlayerProfile Batter(BatterSide side = BatterSide.Right, int power = 50, int discipline = 50) =>
            new PlayerProfile($"B{side}{power}{discipline}", "B", side, Hand.Right, 73.0, null, new PlayerRatings(power: power, discipline: discipline));

        private static List<PitchDecision> Calls(PlayerProfile pitcher, PlayerProfile batter, Count count, int n = 2000, int seed = 1) =>
            Enumerable.Range(0, n).Select(i => CpuPitcher.Choose(pitcher, batter, count, 0, BaseOccupancy.Empty, None, seed, i)).ToList();

        private static double Share(IEnumerable<PitchDecision> calls, Func<PitchDecision, bool> f)
        {
            var list = calls.ToList();
            return list.Count(f) / (double)list.Count;
        }

        private static bool Strike(PitchDecision d) => d.Intent == LocationIntent.Heart || d.Intent == LocationIntent.Edge;

        [Test]
        public void TheSameSituationGivesTheSameCall()
        {
            PlayerProfile p = Pitcher(GenericRosters.PowerRepertoire()), b = Batter();
            for (int i = 0; i < 30; i++)
                Assert.AreEqual(CpuPitcher.Choose(p, b, new Count(1, 1), 1, BaseOccupancy.Empty, None, 4, i).ToString(),
                    CpuPitcher.Choose(p, b, new Count(1, 1), 1, BaseOccupancy.Empty, None, 4, i).ToString());
            Assert.Greater(Calls(p, b, new Count(1, 1), 50).Select(d => d.ToString()).Distinct().Count(), 15, "seeded, not a fixed cycle");
        }

        [Test]
        public void HeThrowsOnlyHisRepertoireByUsageAndMatchup()
        {
            // At 1-1 with no previous pitch, a type's share is usage × matchup (slider ×1.25, changeup ×0.6 against a
            // same-side hitter), normalised: the model's own expectation, within sampling error (n 4000).
            foreach (Repertoire rep in new[] { GenericRosters.PowerRepertoire(), GenericRosters.CommandRepertoire(), GenericRosters.BreakingBallRepertoire() })
            {
                List<PitchDecision> calls = Calls(Pitcher(rep), Batter(), new Count(1, 1), 4000);
                Assert.IsTrue(calls.All(d => rep.Has(d.Type)), "nothing outside his repertoire");
                double W(RepertoirePitch p) => p.Usage * (p.Type == PitchType.Slider ? 1.25 : p.Type == PitchType.Changeup ? 0.6 : 1.0);
                double total = rep.Pitches.Sum(W);
                foreach (RepertoirePitch p in rep.Pitches)
                    Assert.AreEqual(W(p) / total, Share(calls, d => d.Type == p.Type), 0.025, p.Type.ToString());
            }
        }

        [Test]
        public void ArchetypesCallDifferentGames()
        {
            List<PitchDecision> power = Calls(Pitcher(GenericRosters.PowerRepertoire()), Batter(), new Count(1, 1));
            List<PitchDecision> breaking = Calls(Pitcher(GenericRosters.BreakingBallRepertoire()), Batter(), new Count(1, 1));
            bool Fastball(PitchDecision d) => d.Type == PitchType.FourSeam || d.Type == PitchType.Sinker;
            Assert.Greater(Share(power, Fastball), Share(breaking, Fastball) + 0.15, "a power pitcher leans on his fastball");
        }

        [Test]
        public void HeRepeatsAPitchLessTheMoreHeHasThrownIt()
        {
            PlayerProfile p = Pitcher(GenericRosters.PowerRepertoire()), b = Batter();
            var game = new GameState(GenericRosters.Away(), GenericRosters.Home(), 1);
            PitchInfo four = PitchInfo.Of(PitchPresets.All[0].Label, HittingPitch.Create(PitchPresets.All[0], Simulation.BallFlight.EnvironmentState.Standard), 0.0, 0.75);
            double ShareAfter(int run)
            {
                var previous = Enumerable.Range(0, run).Select(i => new PitchEvent(i + 1, four, PitchOutcome.Foul, new Count(0, 2), new Count(0, 2), PlateAppearanceEnd.None)).ToList();
                return Share(Enumerable.Range(0, 3000).Select(i => CpuPitcher.Choose(p, b, new Count(0, 2), 0, BaseOccupancy.Empty, previous, 1, i)), d => d.Type == PitchType.FourSeam);
            }

            double none = ShareAfter(0), one = ShareAfter(1), two = ShareAfter(2), three = ShareAfter(3);
            Assert.Greater(none, one + 0.05);
            Assert.Greater(one, two + 0.04);
            Assert.Greater(two, three + 0.02);
            // After a four-seamer to a spot, another four-seamer rarely goes to the very same spot.
            var after = new List<PitchEvent> { new PitchEvent(1, four, PitchOutcome.Ball, new Count(0, 0), new Count(1, 0), PlateAppearanceEnd.None) };
            var again = Enumerable.Range(0, 3000).Select(i => CpuPitcher.Choose(p, b, new Count(1, 0), 0, BaseOccupancy.Empty, after, 1, i)).Where(d => d.Type == PitchType.FourSeam).ToList();
            Assert.Less(Share(again, d => Math.Abs(d.TargetX - four.TargetX) < 0.1 && Math.Abs(d.TargetZ - four.TargetZ) < 0.1), 0.1);
        }

        [Test]
        public void TargetsMatchTheirIntent()
        {
            // Heart and edge targets are strikes, chase and waste targets balls, waste further out than chase.
            PlayerProfile p = Pitcher(GenericRosters.PowerRepertoire()), b = Batter();
            foreach (PitchDecision d in Calls(p, b, new Count(1, 1), 1500).Concat(Calls(p, b, new Count(0, 2), 1500)))
            {
                double edge = CpuBatter.SignedZoneDistance(d.TargetX, d.TargetZ, b.ZoneBottom, b.ZoneTop);
                switch (d.Intent)
                {
                    case LocationIntent.Heart: Assert.Greater(edge, 0.08, d.ToString()); break;
                    case LocationIntent.Edge: Assert.That(edge, Is.InRange(0.0, 0.1), d.ToString()); break;
                    case LocationIntent.Chase: Assert.That(edge, Is.InRange(-0.12, -0.03), d.ToString()); break;
                    default: Assert.Less(edge, -0.2, d.ToString()); break;
                }
            }
        }

        [Test]
        public void ALeftHandersSpotsAreTheMirrorImage()
        {
            // Glove side: +X for a right-hander, −X for a left-hander. Sliders go glove side, changeups and sinkers arm side.
            var rep = new Repertoire(new RepertoirePitch(PitchType.Slider, 0.5), new RepertoirePitch(PitchType.Changeup, 0.5));
            foreach (Hand hand in new[] { Hand.Right, Hand.Left })
            {
                double glove = hand == Hand.Right ? 1.0 : -1.0;
                List<PitchDecision> calls = Calls(Pitcher(rep, hand, $"P{hand}"), Batter(), new Count(1, 1));
                Assert.Greater(Share(calls.Where(d => d.Type == PitchType.Slider), d => d.TargetX * glove > 0.0), 0.55, $"{hand}: slider glove side");
                Assert.Greater(Share(calls.Where(d => d.Type == PitchType.Changeup), d => d.TargetX * glove < 0.0), 0.45, $"{hand}: changeup arm side");
            }

            // "In" is toward the batter: −X for a right-handed hitter, +X for a left-handed one.
            foreach (BatterSide side in new[] { BatterSide.Right, BatterSide.Left })
            {
                double inSign = side == BatterSide.Right ? -1.0 : 1.0;
                foreach (PitchDecision d in Calls(Pitcher(GenericRosters.PowerRepertoire()), Batter(side), new Count(1, 1), 800).Where(d => d.Location.EndsWith("in")))
                    Assert.Greater(d.TargetX * inSign, 0.0, d.ToString());
            }
        }

        [Test]
        public void PitchTypesFollowThePresetOrder()
        {
            for (int i = 0; i < PitchPresets.All.Length; i++)
                Assert.AreEqual((PitchType)i, CpuPitcher.TypeOf(PitchInfo.Of(PitchPresets.All[i].Label, HittingPitch.Create(PitchPresets.All[i], Simulation.BallFlight.EnvironmentState.Standard))));
        }

        [Test]
        public void TheCountChangesHisApproach()
        {
            PlayerProfile p = Pitcher(GenericRosters.PowerRepertoire()), b = Batter();
            double threeOh = Share(Calls(p, b, new Count(3, 0)), Strike), even = Share(Calls(p, b, new Count(0, 0)), Strike);
            double ohTwo = Share(Calls(p, b, new Count(0, 2)), Strike);
            Assert.Greater(threeOh, even + 0.15, "3-0: strikes");
            Assert.Less(ohTwo, even - 0.15, "0-2: expand");
            bool Fastball(PitchDecision d) => d.Type == PitchType.FourSeam || d.Type == PitchType.Sinker;
            Assert.Greater(Share(Calls(p, b, new Count(3, 0)), Fastball), Share(Calls(p, b, new Count(0, 2)), Fastball) + 0.2, "fastballs when behind");
        }

        [Test]
        public void HeExpandsAgainstFreeSwingersAndAvoidsTheHeartAgainstPower()
        {
            PlayerProfile p = Pitcher(GenericRosters.PowerRepertoire());
            double chaseFree = Share(Calls(p, Batter(discipline: 10), new Count(1, 1)), d => d.Intent == LocationIntent.Chase);
            double chasePatient = Share(Calls(p, Batter(discipline: 90), new Count(1, 1)), d => d.Intent == LocationIntent.Chase);
            Assert.Greater(chaseFree, chasePatient * 1.3);
            double heartPower = Share(Calls(p, Batter(power: 90), new Count(1, 1)), d => d.Intent == LocationIntent.Heart);
            double heartWeak = Share(Calls(p, Batter(power: 10), new Count(1, 1)), d => d.Intent == LocationIntent.Heart);
            Assert.Less(heartPower, heartWeak * 0.75);
        }

        [Test]
        public void SlidersToSameSideChangeupsToOppositeSide()
        {
            PlayerProfile p = Pitcher(GenericRosters.PowerRepertoire());
            Assert.True(p.Repertoire.Has(PitchType.Slider) && p.Repertoire.Has(PitchType.Changeup));
            List<PitchDecision> same = Calls(p, Batter(BatterSide.Right), new Count(1, 1)), opposite = Calls(p, Batter(BatterSide.Left), new Count(1, 1));
            Assert.Greater(Share(same, d => d.Type == PitchType.Slider), Share(opposite, d => d.Type == PitchType.Slider));
            Assert.Greater(Share(opposite, d => d.Type == PitchType.Changeup), Share(same, d => d.Type == PitchType.Changeup));
            // A right-hander's slider goes to his glove side (+X): away from a right-handed hitter.
            Assert.Greater(Share(same.Where(d => d.Type == PitchType.Slider), d => d.TargetX > 0.0), 0.5);
        }

        [Test]
        public void ARunnerOnThirdMeansFewerWastePitches()
        {
            PlayerProfile p = Pitcher(GenericRosters.PowerRepertoire()), b = Batter();
            var third = new BaseOccupancy(false, false, true);
            double empty = Share(Calls(p, b, new Count(0, 2)), d => d.Intent == LocationIntent.Waste);
            double withRunner = Share(Enumerable.Range(0, 2000).Select(i => CpuPitcher.Choose(p, b, new Count(0, 2), 1, third, None, 1, i)), d => d.Intent == LocationIntent.Waste);
            Assert.Less(withRunner, empty * 0.6);
            double twoOuts = Share(Enumerable.Range(0, 2000).Select(i => CpuPitcher.Choose(p, b, new Count(0, 2), 2, third, None, 1, i)), d => d.Intent == LocationIntent.Waste);
            Assert.AreEqual(empty, twoOuts, 0.03, "with two outs the rule does not apply");
        }

        [Test]
        public void HeDoesNotRepeatHimselfInSimulatedGames()
        {
            // No pathological loops: over whole simulated games, no long runs of one pitch type, and a location rarely
            // repeated exactly.
            int longest = 0, pitches = 0, sameSpot = 0;
            foreach (int seed in new[] { 1, 2 })
            {
                var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);
                new GameSimulator(g).PlayToEnd();
                foreach (PlateAppearance pa in g.Completed)
                {
                    int run = 1;
                    for (int i = 1; i < pa.Pitches.Count; i++)
                    {
                        PitchInfo a = pa.Pitches[i - 1].Info, c = pa.Pitches[i].Info;
                        run = a.Label == c.Label ? run + 1 : 1;
                        longest = Math.Max(longest, run);
                        if (a.Label == c.Label && Math.Abs(a.TargetX - c.TargetX) < 1e-9 && Math.Abs(a.TargetZ - c.TargetZ) < 1e-9) sameSpot++;
                        pitches++;
                    }
                }
            }

            Assert.LessOrEqual(longest, 5, "no long runs of one pitch within a plate appearance");
            Assert.Less(sameSpot, pitches * 0.08, "the same pitch to the same spot back to back is rare");
        }

        public sealed class ZoneStats
        {
            public readonly int[,] Pitches = new int[4, 3], InZone = new int[4, 3];
            public double Rate(int balls, int strikes) => InZone[balls, strikes] / (double)Math.Max(1, Pitches[balls, strikes]);

            public double Overall
            {
                get
                {
                    int p = 0, z = 0;
                    for (int b = 0; b < 4; b++)
                    for (int s = 0; s < 3; s++)
                    {
                        p += Pitches[b, s];
                        z += InZone[b, s];
                    }

                    return z / (double)p;
                }
            }
        }

        /// <summary>The executed pitches' zone rate by count over simulated games (the crossing against the batter's zone).</summary>
        public static ZoneStats ZoneRates(IEnumerable<int> seeds)
        {
            var z = new ZoneStats();
            foreach (int seed in seeds)
            {
                var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);
                new GameSimulator(g).PlayToEnd();
                foreach (PlateAppearance pa in g.Completed.Append(g.Current))
                foreach (PitchEvent e in pa.Pitches)
                {
                    if (!e.Info.HasCrossing) continue;
                    z.Pitches[e.Before.Balls, e.Before.Strikes]++;
                    if (StrikeZone.Contains(e.Info.PlateX, e.Info.PlateZ, pa.Batter.ZoneBottom, pa.Batter.ZoneTop)) z.InZone[e.Before.Balls, e.Before.Strikes]++;
                }
            }

            return z;
        }

        [Test]
        public void ExecutedZoneRateFollowsTheCount()
        {
            // Regression guard (prototype bounds, not validation): MLB zone% ≈ 64 % at 3-0, 54 % at 0-0, 32 % at 0-2, ≈ 49 % overall.
            ZoneStats z = ZoneRates(new[] { 1, 2, 3, 4, 5 });
            double Group(Func<int, int, bool> f)
            {
                int p = 0, n = 0;
                for (int b = 0; b < 4; b++)
                for (int s = 0; s < 3; s++)
                    if (f(b, s))
                    {
                        p += z.Pitches[b, s];
                        n += z.InZone[b, s];
                    }

                return n / (double)p;
            }

            double behind = Group((b, s) => b > s), even = Group((b, s) => b == s), ahead = Group((b, s) => s > b);
            Assert.Greater(behind, even + 0.03, "behind in the count: more strikes");
            Assert.Greater(even, ahead + 0.05, "ahead: more pitches off the zone");
            Assert.That(z.Overall, Is.InRange(0.40, 0.60));
        }

        [Test]
        public void TheCommandPitcherHitsHisSpotsBetter()
        {
            // The same calls executed by each starter in simulated games: the command starter misses his targets by less.
            var miss = new Dictionary<string, (double sq, int n)>();
            foreach (int seed in new[] { 1, 2 })
            {
                var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);
                new GameSimulator(g).PlayToEnd();
                foreach (PlateAppearance pa in g.Completed)
                foreach (PitchEvent e in pa.Pitches)
                {
                    if (!e.Info.HasTarget || !e.Info.HasCrossing) continue;
                    string team = pa.Team.ToString();
                    miss.TryGetValue(team, out var m);
                    miss[team] = (m.sq + Sq(e.Info.PlateX - e.Info.TargetX) + Sq(e.Info.PlateZ - e.Info.TargetZ), m.n + 1);
                }
            }

            // Away bats against the home (command) starter; home bats against the away (power) starter.
            double Rms((double sq, int n) m) => Math.Sqrt(m.sq / m.n);
            Assert.Less(Rms(miss["Away"]), Rms(miss["Home"]), "the command starter (facing the away lineup) is more accurate");
        }

        private static double Sq(double v) => v * v;

        /// <summary>Development report: zone rates by count and pitch usage over simulated games.</summary>
        public static string Report(int games = 10)
        {
            ZoneStats z = ZoneRates(Enumerable.Range(201, games));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"zone overall {z.Overall:P0}");
            for (int b = 0; b < 4; b++)
            for (int s = 0; s < 3; s++)
                sb.Append($"{b}-{s} {z.Rate(b, s):P0} (n {z.Pitches[b, s]})  ");
            return sb.ToString();
        }
    }
}
