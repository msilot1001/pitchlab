using System;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// A basic automatic pitcher (TASK-014) — a convenience so the player can bat through plate appearances, not a model of
    /// MLB pitch calling. The choice is a pure function of a seed, the plate appearance, the pitch number and the count (a
    /// hash, no shared random state): the same game produces the same pitches at any frame rate. Count-aware: the further
    /// behind in the count, the more pitches in the zone and the more fastballs; ahead, more pitches off the plate and more
    /// breaking balls.
    /// </summary>
    public static class AutoPitcher
    {
        /// <summary>Probability of aiming in the zone, by balls and strikes (rows: balls 0–3, columns: strikes 0–2).</summary>
        public static readonly double[,] ZoneShare =
        {
            { 0.55, 0.50, 0.25 },
            { 0.60, 0.55, 0.35 },
            { 0.70, 0.60, 0.45 },
            { 0.85, 0.75, 0.65 },
        };

        /// <summary>The same from a pitcher's repertoire (TASK-018): his pitches by their usage weights, fastballs (four-seam,
        /// sinker) weighted ×1.5 when behind in the count and ×0.7 when ahead.</summary>
        public static PitchCommand Choose(int seed, int plateAppearance, int pitchNumber, Count count, Players.Repertoire repertoire)
        {
            if (repertoire == null) return Choose(seed, plateAppearance, pitchNumber, count);
            PitchCommand where = Choose(seed, plateAppearance, pitchNumber, count);
            var stream = new SeedStream(seed, plateAppearance, pitchNumber, 0x7E9E);
            double total = 0.0;
            foreach (Players.RepertoirePitch p in repertoire.Pitches) total += Weight(p, count);
            double x = stream.Unit() * total;
            foreach (Players.RepertoirePitch p in repertoire.Pitches)
            {
                x -= Weight(p, count);
                if (x < 0.0) return new PitchCommand((int)p.Type, where.Target);
            }

            return new PitchCommand((int)repertoire.Pitches[repertoire.Count - 1].Type, where.Target);
        }

        private static double Weight(Players.RepertoirePitch p, Count count)
        {
            bool fastball = p.Type == Players.PitchType.FourSeam || p.Type == Players.PitchType.Sinker;
            double bias = count.Balls > count.Strikes ? 1.5 : count.Strikes > count.Balls ? 0.7 : 1.0;
            return p.Usage * (fastball ? bias : 1.0);
        }

        public static PitchCommand Choose(int seed, int plateAppearance, int pitchNumber, Count count)
        {
            // Each input mixed on its own (no overlapping bit ranges: any seed, any plate appearance and pitch number).
            ulong h = Mix(Mix(Mix((ulong)(uint)seed) ^ (ulong)(uint)plateAppearance) ^ (ulong)(uint)pitchNumber);
            double zone = Unit(ref h), which = Unit(ref h), type = Unit(ref h);
            PitchTarget target = zone < ZoneShare[count.Balls, count.Strikes]
                ? (PitchTarget)(int)(which * 9.0)
                : PitchTarget.BallUp + (int)(which * 4.0);
            return new PitchCommand(PresetFor(type, count), target);
        }

        /// <summary>Fastballs (four-seam, sinker) weigh 3 when behind in the count, 1 when ahead; the others the reverse
        /// (2 when ahead); all equal when even.</summary>
        private static int PresetFor(double u, Count count)
        {
            PitchInput[] all = PitchPresets.All;
            Span<double> weights = stackalloc double[all.Length];
            double total = 0.0;
            for (int i = 0; i < all.Length; i++)
            {
                bool fastball = i <= 1;   // PitchPresets.All: four-seam, sinker, slider, curveball, changeup
                weights[i] = count.Balls > count.Strikes ? (fastball ? 3.0 : 1.0) : count.Strikes > count.Balls ? (fastball ? 1.0 : 2.0) : 1.0;
                total += weights[i];
            }

            double x = u * total;
            for (int i = 0; i < all.Length; i++)
            {
                if (x < weights[i]) return i;
                x -= weights[i];
            }

            return all.Length - 1;
        }

        private static double Unit(ref ulong h)
        {
            h = Mix(h + 0x9E3779B97F4A7C15UL);
            return (h >> 11) * (1.0 / (1UL << 53));
        }

        /// <summary>SplitMix64 finaliser.</summary>
        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
