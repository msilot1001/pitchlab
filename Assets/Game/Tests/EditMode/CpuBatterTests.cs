using System;
using System.Collections.Generic;
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
    /// TASK-019: the CPU batter perceives the pitch only up to the present, decides, and swings through the same timestamped
    /// PCI + swing input and the same contact model as a human. Population checks against a fixed pitch set.
    /// </summary>
    public class CpuBatterTests
    {
        private static readonly EnvironmentState Env = EnvironmentState.Standard;

        public static PlayerProfile Batter(GenericRosters.Batting b, string id = null, BatterSide side = BatterSide.Right) =>
            new PlayerProfile(id ?? $"B{b.Contact}-{b.Power}-{b.Vision}-{b.Discipline}", "B", side, Hand.Right, 73.0, null,
                GenericRosters.Compose(b, new GenericRosters.Running(50, 50, 50), GenericRosters.AverageDefender, 50));

        /// <summary>A fixed pitch set: the away starter's repertoire by the automatic pitcher, every count, executed (seeded).</summary>
        public static List<(HittingPitch Pitch, Count Count)> PitchSet(int n = 600, int seed = 5)
        {
            PlayerProfile pitcher = GenericRosters.Away().Pitcher;
            var set = new List<(HittingPitch, Count)>();
            for (int i = 0; i < n; i++)
            {
                var c = new Count(i % 4, i / 4 % 3);
                PitchCommand cmd = AutoPitcher.Choose(seed, i, 1, c, pitcher.Repertoire);
                (double tx, double tz) = PitchTargets.Point(cmd.Target, StrikeZone.Bottom, StrikeZone.Top);
                PitchInput aimed = PitchTargets.Aim(PitchExecution.PitcherPitch(pitcher, (PitchType)cmd.Preset), tx, tz, Env);
                SeedStream stream = PitchExecution.StreamFor(seed, pitcher.Id, i, 1);
                set.Add((HittingPitch.Create(PitchExecution.Execute(aimed, pitcher, (PitchType)cmd.Preset, 0, ref stream, out _), Env), c));
            }

            return set;
        }

        public sealed class Stats
        {
            public int Zone, Out, ZoneSwings, Chases, ZoneContact, OutContact, Contacts, Hard, Squared;
            public double ExitSpeedSum, PredictionSq, TimingSq, SquaredExitSum, ArrivalSq;
            public int Predictions;
            public double ZoneSwing => (double)ZoneSwings / Zone;
            public double Chase => (double)Chases / Out;
            public double ZoneContactRate => (double)ZoneContact / Math.Max(1, ZoneSwings);
            public double OutContactRate => (double)OutContact / Math.Max(1, Chases);
            public double ContactRate => (double)Contacts / Math.Max(1, ZoneSwings + Chases);
            public double WhiffRate => 1.0 - ContactRate;
            public double MeanExitMph => ExitSpeedSum / Math.Max(1, Contacts);
            /// <summary>Mean exit speed when he squares it up (within 1 cm of the sweet spot's height, 1 in along the barrel).</summary>
            public double SquaredExitMph => SquaredExitSum / Math.Max(1, Squared);
            public double HardRate => (double)Hard / Math.Max(1, Contacts);
            public double PredictionRms => Math.Sqrt(PredictionSq / Math.Max(1, Predictions));
            /// <summary>His misjudgement of the arrival time at the decision (s, RMS).</summary>
            public double ArrivalRms => Math.Sqrt(ArrivalSq / Math.Max(1, Predictions));
            public double TimingRms => Math.Sqrt(TimingSq / Math.Max(1, ZoneSwings + Chases));

            public override string ToString() =>
                $"Z-swing {100 * ZoneSwing:F0}% chase {100 * Chase:F0}% | Z-contact {100 * ZoneContactRate:F0}% O-contact {100 * OutContactRate:F0}% | " +
                $"EV {MeanExitMph:F1} mph squared {SquaredExitMph:F1} (n {Squared}) hard(95+) {100 * HardRate:F0}% | prediction {100 * PredictionRms:F1} cm arrival {1000 * ArrivalRms:F1} ms timing {1000 * TimingRms:F1} ms";
        }

        /// <summary>The batter against the pitch set (the zone call from the actual crossing — for scoring only, never shown to him).</summary>
        public static Stats Population(PlayerProfile batter, List<(HittingPitch Pitch, Count Count)> set, int seed = 9)
        {
            var s = new Stats();
            SwingParameters swing = SwingParameters.For(batter);
            for (int i = 0; i < set.Count; i++)
            {
                HittingPitch pitch = set[i].Pitch;
                if (!pitch.ReachesContactPlane) continue;
                bool zone = StrikeZone.IsStrike(pitch, batter.ZoneBottom, batter.ZoneTop);
                SeedStream stream = CpuBatter.StreamFor(seed, batter.Id, i, 1);
                BatterPlan plan = CpuBatter.Plan(CpuBatter.Observe(pitch), batter, set[i].Count, swing, pitch.ContactPlaneY, ref stream);
                (double cx, double cz) = StrikeZone.Crossing(pitch);
                if (!double.IsNaN(plan.PredictedX) && !double.IsNaN(cx))
                {
                    s.PredictionSq += (plan.PredictedX - cx) * (plan.PredictedX - cx) + (plan.PredictedZ - cz) * (plan.PredictedZ - cz);
                    s.ArrivalSq += (plan.PredictedContactTime - pitch.IdealContactTime) * (plan.PredictedContactTime - pitch.IdealContactTime);
                    s.Predictions++;
                }

                if (zone) s.Zone++;
                else s.Out++;
                if (!plan.Swing) continue;
                if (zone) s.ZoneSwings++;
                else s.Chases++;
                ContactResult r = ContactResolver.Resolve(pitch, plan.Input.Value, swing);
                if (Finite(r.TimingError)) s.TimingSq += r.TimingError * r.TimingError;
                if (!r.IsContact) continue;
                if (zone) s.ZoneContact++;
                else s.OutContact++;
                s.Contacts++;
                double mph = r.ExitSpeed / Units.MphToMetersPerSecond(1.0);
                s.ExitSpeedSum += mph;
                if (mph >= 95.0) s.Hard++;
                if (Math.Abs(r.VerticalOffset) < 0.01 && Math.Abs(r.OffsetAlongBarrel) < 0.0254)
                {
                    s.Squared++;
                    s.SquaredExitSum += mph;
                }
            }

            return s;
        }

        private static PlayerProfile Rated(int contact = 50, int power = 50, int vision = 50, int discipline = 50, string id = null) =>
            new PlayerProfile(id ?? $"R{contact}-{power}-{vision}-{discipline}", "R", BatterSide.Right, Hand.Right, 73.0, null,
                new PlayerRatings(contact: contact, power: power, vision: vision, discipline: discipline));

        private static HittingPitch Pitch(PitchType type = PitchType.FourSeam, PitchTarget target = PitchTarget.Middle)
        {
            (double tx, double tz) = PitchTargets.Point(target, StrikeZone.Bottom, StrikeZone.Top);
            return HittingPitch.Create(PitchTargets.Aim(PitchPresets.All[(int)type], tx, tz, Env), Env);
        }

        private static BatterPlan Plan(Func<double, Vector3d> ballAt, HittingPitch pitch, PlayerProfile batter, Count count, int key)
        {
            SeedStream stream = CpuBatter.StreamFor(3, batter.Id, key, 1);
            return CpuBatter.Plan(ballAt, batter, count, SwingParameters.For(batter), pitch.ContactPlaneY, ref stream);
        }

        private static string Describe(BatterPlan p) =>
            $"{p.Swing} {p.SwingStart:R} {p.DecisionTime:R} {p.PredictedX:R} {p.PredictedZ:R} " + string.Join(";", System.Linq.Enumerable.Select(p.Aim, e => $"{e.Time:R},{e.X:R},{e.Z:R}"));

        private static List<(HittingPitch Pitch, Count Count)> _set;
        private static List<(HittingPitch Pitch, Count Count)> Set => _set ??= PitchSet();

        [Test]
        public void TheSameSeedGivesTheSameEventsAndAnotherSeedOthers()
        {
            HittingPitch pitch = Pitch();
            PlayerProfile b = Rated();
            var distinct = new HashSet<string>();
            for (int k = 0; k < 20; k++)
            {
                string a = Describe(Plan(CpuBatter.Observe(pitch), pitch, b, new Count(1, 1), k));
                Assert.AreEqual(a, Describe(Plan(CpuBatter.Observe(pitch), pitch, b, new Count(1, 1), k)));
                distinct.Add(a);
            }

            Assert.Greater(distinct.Count, 15, "each plate appearance / pitch key draws its own deviates");
        }

        [Test]
        public void HeSeesTheAuthoritativeBall()
        {
            HittingPitch pitch = Pitch(PitchType.Slider);
            Func<double, Vector3d> seen = CpuBatter.Observe(pitch);
            for (double t = 0.0; t < pitch.Flight.Final.Time; t += 0.037)
                Assert.AreEqual(pitch.Flight.StateAt(t).Position, seen(t), $"t {t}");
        }

        [Test]
        public void HeNeverUsesTheFlightBeyondWhatHeHasSeen()
        {
            // Every event depends only on the ball up to its latest look: the same pitch with a different future (from
            // just after his last look the "ball" jumps 1 m) gives exactly the same events — no final crossing, call or
            // contact result can leak in.
            PlayerProfile b = Rated();
            int swings = 0;
            for (int k = 0; k < 60; k++)
            {
                HittingPitch pitch = Pitch((PitchType)(k % 5), PitchTargets.All[k % PitchTargets.All.Length]);
                double latest = double.NegativeInfinity;
                Func<double, Vector3d> watched = t =>
                {
                    latest = Math.Max(latest, t);
                    return CpuBatter.Observe(pitch)(t);
                };
                BatterPlan plan = Plan(watched, pitch, b, new Count(1, 1), k);
                double lastEvent = plan.Swing ? plan.SwingStart : plan.Aim[plan.Aim.Count - 1].Time;
                Assert.LessOrEqual(plan.LastObservation, lastEvent, "a look precedes the event it informs");
                Assert.LessOrEqual(latest, plan.LastObservation + CpuBatter.Tick + 1e-12, "at most one look past his last event (the one that ends his watching)");
                Func<double, Vector3d> otherFuture = t => CpuBatter.Observe(pitch)(t) + (t > plan.LastObservation ? new Vector3d(1.0, -2.0, 1.0) : Vector3d.Zero);
                BatterPlan replay = Plan(otherFuture, pitch, b, new Count(1, 1), k);
                // The future differs from just after the last look that produced an event; later looks (after the swing
                // started, or past him) may be made but change nothing.
                Assert.AreEqual(Describe(plan), Describe(replay), $"pitch {k}");
                Assert.Less(plan.LastObservation, pitch.IdealContactTime - 0.15, "he commits well before the ball arrives");
                if (plan.Swing) swings++;
            }

            Assert.Greater(swings, 10, "the pitches are swung at");
        }

        [Test]
        public void HisSwingIsTheSameInputAHumanGives()
        {
            // The swing is a press time and the PCI where it was then; the contact model takes it like a human's.
            HittingPitch pitch = Pitch();
            PlayerProfile b = Rated();
            for (int k = 0; k < 40; k++)
            {
                BatterPlan plan = Plan(CpuBatter.Observe(pitch), pitch, b, new Count(0, 2), k);
                if (!plan.Swing) continue;
                SwingInput input = plan.Input.Value;
                Assert.AreEqual(plan.SwingStart, input.StartTime);
                Assert.AreEqual(plan.PciAt(plan.SwingStart), (input.PciX, input.PciZ));
                Assert.GreaterOrEqual(plan.SwingStart, plan.DecisionTime, "he presses after he decides");
                foreach (AimEvent e in plan.Aim)
                {
                    Assert.LessOrEqual(Math.Abs(e.X - PciFrame.Default.CenterX), PciFrame.Default.HalfWidth + 1e-12, "inside the PCI area");
                    Assert.LessOrEqual(Math.Abs(e.Z - PciFrame.Default.CenterZ), PciFrame.Default.HalfHeight + 1e-12);
                }
                return;
            }

            Assert.Fail("no swing at a middle-middle pitch with two strikes");
        }

        [Test]
        public void HisPressCarriesHisOwnTimingError()
        {
            // Mutation guard: the press is his predicted arrival − swing duration + his timing error, whose spread is his
            // Contact's (no timing noise → 0).
            HittingPitch pitch = Pitch();
            foreach (int contact in new[] { 90, 10 })
            {
                PlayerProfile b = Rated(contact: contact);
                double sum = 0.0, sq = 0.0;
                int n = 0;
                for (int k = 0; k < 600; k++)
                {
                    BatterPlan plan = Plan(CpuBatter.Observe(pitch), pitch, b, new Count(0, 2), k);
                    if (!plan.Swing) continue;
                    double e = plan.SwingStart - (plan.PredictedContactTime - SwingParameters.Default.SwingDuration);
                    sum += e;
                    sq += e * e;
                    n++;
                }

                double mean = sum / n, sd = Math.Sqrt(sq / n - mean * mean), expected = CpuBatter.TimingSigma(b.Ratings);
                Assert.Greater(n, 400);
                Assert.That(sd, Is.InRange(0.85 * expected, 1.15 * expected), $"contact {contact}: {1000 * sd:F1} ms vs {1000 * expected:F1}");
                Assert.AreEqual(CpuBatter.TimingBias, mean, 0.15 * expected, "his average timing is his bias (slightly early, TASK-023); an early press is never cut short");
            }
        }

        [Test]
        public void ContactAloneSharpensTimingAndAim()
        {
            // Vision fixed, so his perception is the same in distribution: only his own timing and aim errors differ. With no
            // aim or timing error (mutations) the two would match.
            (double timing, double aim) Spread(PlayerProfile b)
            {
                HittingPitch pitch = Pitch();
                (double cx, double cz) = (pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
                double t2 = 0.0, a2 = 0.0;
                int n = 0;
                for (int k = 0; k < 600; k++)
                {
                    BatterPlan plan = Plan(CpuBatter.Observe(pitch), pitch, b, new Count(0, 2), k);
                    if (!plan.Swing) continue;
                    SwingInput s = plan.Input.Value;
                    t2 += Sq(s.StartTime + SwingParameters.Default.SwingDuration - pitch.IdealContactTime);
                    a2 += Sq(s.PciX - cx) + Sq(s.PciZ - cz);
                    n++;
                }

                Assert.Greater(n, 400);
                return (Math.Sqrt(t2 / n), Math.Sqrt(a2 / (2 * n)));
            }

            (double goodT, double goodA) = Spread(Rated(contact: 90));
            (double poorT, double poorA) = Spread(Rated(contact: 10));
            Assert.Greater(poorT, goodT * 1.3, $"timing {1000 * poorT:F1} vs {1000 * goodT:F1} ms");
            // Aim spread (both axes) = perception (the same for both) ⊕ his aim error (σ scales 0.73× vs 1.27× of 11 cm along /
            // 2.5 cm across by Contact): clearly wider for the poor hitter; equal without it.
            Assert.Greater(poorA, goodA * 1.1, $"aim {100 * poorA:F1} vs {100 * goodA:F1} cm");
            Assert.Greater(goodA, CpuBatter.AimSigmaAlong(Rated(contact: 90).Ratings) * 0.8, "his aim error is in the PCI");
        }

        /// <summary>His PCI at the press minus the ball at the contact plane, over his swings at <paramref name="pitch"/>.</summary>
        private static List<(double Dx, double Dz)> AimOffsets(HittingPitch pitch, PlayerProfile b, Count count, int n)
        {
            Vector3d c = pitch.IdealContactState.Position;
            var list = new List<(double, double)>();
            for (int k = 0; k < n; k++)
            {
                BatterPlan plan = Plan(CpuBatter.Observe(pitch), pitch, b, count, k);
                if (plan.Input is SwingInput s) list.Add((s.PciX - c.X, s.PciZ - c.Z));
            }

            return list;
        }

        private static double Sd(IEnumerable<double> v)
        {
            var a = v.ToList();
            double m = a.Average();
            return Math.Sqrt(a.Sum(x => (x - m) * (x - m)) / a.Count);
        }

        [Test]
        public void HeSwingsToLiftAndMissesMoreAlongTheBarrelThanAcrossIt()
        {
            // TASK-023: he aims a little under the ball (lift intent), and his aim error is much wider along the barrel (how
            // squarely he meets it) than across it (whiffs, launch angle).
            HittingPitch middle = Pitch();
            List<(double Dx, double Dz)> o = AimOffsets(middle, Rated(), new Count(0, 2), 800);
            Assert.Greater(o.Count, 500);
            // Against his own prediction (his perception's error is separate: his prior expects no lift, so he reads a rising
            // fastball low): the PCI sits LiftIntent under where he expects the ball, plus his zero-mean aim error.
            var underPrediction = new List<double>();
            for (int k = 0; k < 800; k++)
            {
                BatterPlan plan = Plan(CpuBatter.Observe(middle), middle, Rated(), new Count(0, 2), k);
                if (!plan.Swing) continue;
                AimEvent e = plan.Aim.Last(a => a.Time <= plan.SwingStart);
                underPrediction.Add(e.Z - e.PredictedZ);
            }

            Assert.That(underPrediction.Average(), Is.InRange(-CpuBatter.LiftIntent - 0.003, -CpuBatter.LiftIntent + 0.003), "under his predicted ball by his lift intent");
            Assert.Greater(Sd(o.Select(x => x.Dx)), 2.5 * Sd(o.Select(x => x.Dz)), "wider along the barrel");
        }

        [Test]
        public void APitchFarOutsideIsHarderToSquareUp()
        {
            // TASK-023: the reach penalty — his aim error grows with how far outside the zone he sees the pitch.
            PlayerProfile b = Rated();
            List<(double Dx, double Dz)> middle = AimOffsets(Pitch(PitchType.FourSeam, PitchTarget.Middle), b, new Count(0, 2), 800);
            List<(double Dx, double Dz)> wide = AimOffsets(Pitch(PitchType.FourSeam, PitchTarget.BallRight), b, new Count(0, 2), 3000);
            Assert.Greater(wide.Count, 40, "he chases it sometimes");
            Assert.Greater(Sd(wide.Select(x => x.Dz)), 1.5 * Sd(middle.Select(x => x.Dz)), "a wider aim error well outside the zone");
        }

        [Test]
        public void DisciplineLowersTheChaseRateButNotToZero()
        {
            Stats patient = Population(Rated(discipline: 90), Set), free = Population(Rated(discipline: 10), Set);
            Assert.Less(patient.Chase, free.Chase * 0.75, $"patient {patient.Chase:P0} vs free {free.Chase:P0}");
            Assert.Greater(patient.Chase, 0.05, "a disciplined hitter still chases sometimes");
            Assert.Greater(patient.ZoneSwing, 0.5, "and still swings at strikes");
        }

        [Test]
        public void TheCountChangesHisApproach()
        {
            HittingPitch edge = Pitch(PitchType.FourSeam, PitchTarget.Middle);
            PlayerProfile b = Rated();
            int Swings(Count c)
            {
                int n = 0;
                for (int k = 0; k < 300; k++) n += Plan(CpuBatter.Observe(edge), edge, b, c, k).Swing ? 1 : 0;
                return n;
            }

            int threeOh = Swings(new Count(3, 0)), even = Swings(new Count(0, 0)), twoStrikes = Swings(new Count(0, 2));
            Assert.Less(threeOh, even / 2, "3-0: very selective");
            Assert.Greater(twoStrikes, even * 1.4, "two strikes: protects the zone");
        }

        [Test]
        public void VisionSharpensHisPrediction()
        {
            Stats good = Population(Rated(vision: 90), Set), poor = Population(Rated(vision: 10), Set);
            Assert.Greater(poor.PredictionRms, good.PredictionRms * 1.15, $"{100 * poor.PredictionRms:F1} vs {100 * good.PredictionRms:F1} cm");
            Assert.Greater(poor.ArrivalRms, good.ArrivalRms * 1.15, "and his read of the arrival time");
        }

        [Test]
        public void BetterHittersMakeMoreContact()
        {
            Stats good = Population(Rated(contact: 85, vision: 85), Set), poor = Population(Rated(contact: 15, vision: 15), Set);
            Assert.Greater(good.ContactRate, poor.ContactRate + 0.10, $"contact {good.ContactRate:P0} vs {poor.ContactRate:P0}");
        }

        [Test]
        public void PowerIsBatSpeedSoSquaredUpBallsLeaveFaster()
        {
            // Power is bat speed (a physical input), never a post-contact scale: the same squared-up swing on the same pitch.
            Assert.Greater(SwingParameters.BatSpeedMph(90), SwingParameters.BatSpeedMph(10) + 7.0);
            HittingPitch pitch = Pitch();
            double Exit(int power)
            {
                SwingParameters swing = SwingParameters.For(Rated(power: power));
                var input = new SwingInput(pitch.IdealContactTime - swing.SwingDuration, pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
                return ContactResolver.Resolve(pitch, input, swing).ExitSpeed / Units.MphToMetersPerSecond(1.0);
            }

            Assert.Greater(Exit(90), Exit(50) + 4.0);
            Assert.Greater(Exit(50), Exit(10) + 4.0);
            // And over his population, with his own errors.
            Stats strong = Population(Rated(power: 90, id: "strong"), Set), weak = Population(Rated(power: 10, id: "weak"), Set);
            // Over his contacts the gap is smaller than squared up: since TASK-023 much contact is off the barrel (weak), where bat
            // speed matters less. Regression guard (≈ 3.7 mph measured).
            Assert.Greater(strong.MeanExitMph, weak.MeanExitMph + 2.5, $"EV {strong.MeanExitMph:F1} vs {weak.MeanExitMph:F1}");
        }

        [Test]
        public void APitchInTheDirtIsTakenOrMissed()
        {
            PitchInput low = PitchPresets.All[(int)PitchType.Curveball];
            low.VerticalAngleDegrees -= 3.0;
            HittingPitch dirt = HittingPitch.Create(low, Env);
            Assert.IsTrue(dirt.Flight.Final.Position.Z < 0.05 || !dirt.ReachesContactPlane, "a pitch that bounces");
            PlayerProfile b = Rated();
            int swings = 0;
            for (int k = 0; k < 200; k++)
            {
                BatterPlan plan = Plan(CpuBatter.Observe(dirt), dirt, b, new Count(0, 0), k);
                if (!plan.Swing) continue;
                swings++;
                PitchOutcome o = PitchOutcomes.Of(dirt, plan.Input, ContactResolver.Resolve(dirt, plan.Input.Value, SwingParameters.For(b)), null);
                Assert.AreEqual(PitchOutcome.SwingingStrike, o);
            }

            Assert.Less(swings, 20, "rarely chased at 0-0");
        }

        [Test]
        public void LeftHandedHittersHitLikeRightHanded()
        {
            PlayerProfile left = new PlayerProfile("L", "L", BatterSide.Left, Hand.Left, 73.0, null, new PlayerRatings());
            Stats l = Population(left, Set), r = Population(Rated(id: "Rmirror"), Set);
            Assert.AreEqual(r.ContactRate, l.ContactRate, 0.08, $"left {l.ContactRate:P0} right {r.ContactRate:P0}");
            Assert.AreEqual(r.Chase, l.Chase, 0.08);
        }

        [Test]
        public void TheSimulatorBatsWithHim()
        {
            // Each simulated pitch is what his events give: rebuild the pitch and his plan before the simulator plays it.
            var game = new GameState(GenericRosters.Away(), GenericRosters.Home(), 6);
            var sim = new GameSimulator(game);
            int swings = 0;
            for (int i = 0; i < 40 && !game.IsOver; i++)
            {
                PlateAppearance pa = game.Current;
                int n = pa.Pitches.Count + 1;
                PitchDecision call = CpuPitcher.Choose(game);
                HittingPitch pitch = GamePitches.Create(game, call.Type, call.TargetX, call.TargetZ, true, Env, out _, out _);
                SeedStream stream = CpuBatter.StreamFor(game.Seed, pa.Batter.Id, pa.Number, n);
                SwingParameters swing = SwingParameters.For(pa.Batter);
                BatterPlan plan = CpuBatter.Plan(CpuBatter.Observe(pitch), pa.Batter, game.Count, swing, pitch.ContactPlaneY, ref stream);
                ContactResult? r = plan.Input is SwingInput s ? ContactResolver.Resolve(pitch, s, swing) : (ContactResult?)null;
                PitchOutcome played = sim.PlayPitch();
                if (plan.Swing) swings++;
                if (r is ContactResult c && c.IsContact) Assert.That(played, Is.EqualTo(PitchOutcome.Foul).Or.EqualTo(PitchOutcome.InPlay), $"pitch {i}");
                else Assert.AreEqual(PitchOutcomes.Of(pitch, plan.Input, r, null, pa.Batter.ZoneBottom, pa.Batter.ZoneTop), played, $"pitch {i}");
            }

            Assert.Greater(swings, 5);
        }

        [Test]
        public void ArchetypesBehaveDifferently()
        {
            // Regression guard on the archetype population (bounds set after the prototype — not validation).
            Stats contact = Population(Batter(GenericRosters.ContactHitter), Set), free = Population(Batter(GenericRosters.FreeSwinger), Set);
            Stats patient = Population(Batter(GenericRosters.PatientHitter), Set), pitcher = Population(Batter(GenericRosters.PitcherBatting), Set);
            Assert.Greater(contact.ContactRate, free.ContactRate);
            Assert.Greater(free.ContactRate, pitcher.ContactRate);
            Assert.Less(patient.Chase, free.Chase);
            Assert.Less(patient.Chase, pitcher.Chase);
            foreach (Stats s in new[] { contact, free, patient, pitcher })
            {
                Assert.That(s.ZoneSwing, Is.InRange(0.5, 0.85));
                Assert.That(s.Chase, Is.InRange(0.1, 0.45));
                Assert.That(s.ContactRate, Is.InRange(0.45, 0.95));
            }
        }

        private static double Sq(double v) => v * v;

        /// <summary>Development report: every archetype against the pitch set.</summary>
        public static string Report()
        {
            var set = PitchSet();
            var lines = new System.Text.StringBuilder();
            foreach ((string name, GenericRosters.Batting b) in Archetypes)
                lines.AppendLine($"{name,-9} {Population(Batter(b), set)}");
            return lines.ToString();
        }

        public static readonly (string, GenericRosters.Batting)[] Archetypes =
        {
            ("contact", GenericRosters.ContactHitter), ("power", GenericRosters.PowerHitter), ("balanced", GenericRosters.BalancedHitter),
            ("patient", GenericRosters.PatientHitter), ("free", GenericRosters.FreeSwinger), ("pitcher", GenericRosters.PitcherBatting),
        };

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
