using System;

namespace Pitchlab.Gameplay.Rules
{
    /// <summary>
    /// Deterministic variability (TASK-018): a stream of uniform and normal deviates from a hash of its keys (SplitMix64).
    /// No shared random state: the same keys give the same values whatever happened before, at any frame rate. Never
    /// UnityEngine.Random in gameplay.
    /// </summary>
    public struct SeedStream
    {
        private ulong _state;

        public SeedStream(params long[] keys)
        {
            ulong h = 0x5EED5EED5EED5EEDUL;
            foreach (long k in keys) h = Mix(h ^ (ulong)k);
            _state = h;
        }

        /// <summary>A uniform deviate in (0, 1).</summary>
        public double Unit()
        {
            _state = Mix(_state + 0x9E3779B97F4A7C15UL);
            return ((_state >> 11) + 0.5) * (1.0 / (1UL << 53));
        }

        /// <summary>A standard normal deviate (Box–Muller).</summary>
        public double Normal()
        {
            double u1 = Unit(), u2 = Unit();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>A stable key for a string (e.g. a player id): FNV-1a.</summary>
        public static long Key(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in text ?? string.Empty) h = (h ^ c) * 1099511628211UL;
            return (long)h;
        }

        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
