using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    /// <summary>TASK-014: the automatic pitcher is deterministic, legal and count-aware; its pitches are still called from
    /// the simulated flight.</summary>
    public class AutoPitcherTests
    {
        private static PitchCommand[] Sequence(int seed) =>
            Enumerable.Range(1, 40).Select(i => AutoPitcher.Choose(seed, 1 + i / 6, 1 + i % 6, new Count(i % 4, i % 3))).ToArray();

        [Test]
        public void TheSameStateAndSeedGiveTheSamePitch()
        {
            CollectionAssert.AreEqual(Sequence(7), Sequence(7));
            CollectionAssert.AreNotEqual(Sequence(7), Sequence(8), "another seed, another sequence");
            // Every input matters: the plate appearance and the pitch number change the choice somewhere.
            int differ = 0;
            for (int i = 1; i <= 50; i++)
                if (!AutoPitcher.Choose(3, i, 1, new Count()).Equals(AutoPitcher.Choose(3, i, 2, new Count()))) differ++;
            Assert.Greater(differ, 25);
        }

        [Test]
        public void ChoicesAreLegalAndCoverEveryPresetAndTarget()
        {
            var presets = new System.Collections.Generic.HashSet<int>();
            var targets = new System.Collections.Generic.HashSet<PitchTarget>();
            for (int pa = 1; pa <= 200; pa++)
                for (int b = 0; b < 4; b++)
                    for (int s = 0; s < 3; s++)
                    {
                        PitchCommand c = AutoPitcher.Choose(1, pa, 1 + b + s, new Count(b, s));
                        Assert.That(c.Preset, Is.InRange(0, PitchPresets.All.Length - 1));
                        Assert.IsTrue(System.Enum.IsDefined(typeof(PitchTarget), c.Target));
                        presets.Add(c.Preset);
                        targets.Add(c.Target);
                    }

            Assert.AreEqual(PitchPresets.All.Length, presets.Count);
            Assert.AreEqual(PitchTargets.All.Length, targets.Count);
        }

        [Test]
        public void BehindInTheCountHeThrowsStrikesAndFastballsAheadHeExpands()
        {
            (double zone, double fastballs) Shares(Count count)
            {
                int n = 4000, inZone = 0, fast = 0;
                for (int i = 0; i < n; i++)
                {
                    PitchCommand c = AutoPitcher.Choose(11, i / 7, i % 7 + 1, count);
                    if (PitchTargets.InZone(c.Target)) inZone++;
                    if (c.Preset <= 1) fast++;
                }

                return ((double)inZone / n, (double)fast / n);
            }

            var threeOh = Shares(new Count(3, 0));
            var even = Shares(new Count(1, 1));
            var ohTwo = Shares(new Count(0, 2));
            Assert.AreEqual(AutoPitcher.ZoneShare[3, 0], threeOh.zone, 0.03);
            Assert.AreEqual(AutoPitcher.ZoneShare[0, 2], ohTwo.zone, 0.03);
            Assert.Greater(threeOh.zone, even.zone);
            Assert.Greater(even.zone, ohTwo.zone);
            Assert.AreEqual(6.0 / 9.0, threeOh.fastballs, 0.03, "fastballs weigh 3 when behind");
            Assert.AreEqual(2.0 / 5.0, even.fastballs, 0.03);
            Assert.AreEqual(2.0 / 8.0, ohTwo.fastballs, 0.03, "breaking balls weigh 2 when ahead");
        }

        [Test]
        public void TheCallIsTheFlightsNotTheIntent()
        {
            // A command aimed at the edge of the plate is called from where the pitch crossed: the aim point is never read.
            HittingPitch pitch = PitchTargets.Create(new PitchCommand(2, PitchTarget.MiddleRight), StrikeZone.Bottom, StrikeZone.Top, EnvironmentState.Standard);
            (double x, double z) = StrikeZone.Crossing(pitch);
            Assert.AreEqual(StrikeZone.Contains(x, z) ? PitchOutcome.CalledStrike : PitchOutcome.Ball, PitchOutcomes.Of(pitch, null, null, null));
            (double tx, _) = PitchTargets.Point(PitchTarget.MiddleRight, StrikeZone.Bottom, StrikeZone.Top);
            Assert.AreNotEqual(tx, x, "it does not land exactly on the aim");
        }
    }
}
