using System;
using System.Collections.Generic;

namespace Pitchlab.Gameplay.Players
{
    /// <summary>The pitch types the game knows: one per <see cref="Simulation.Pitching.PitchPresets.All"/> entry, in that order.</summary>
    public enum PitchType
    {
        FourSeam,
        Sinker,
        Slider,
        Curveball,
        Changeup,
    }

    /// <summary>One pitch a pitcher throws (TASK-018).</summary>
    public readonly struct RepertoirePitch
    {
        /// <param name="usage">Relative usage weight.</param>
        /// <param name="velocityOffsetMph">His speed on this pitch relative to his rated level for the type (mph).</param>
        /// <param name="familiarity">−50…+50: how much better (or worse) his command of this pitch is than his Command rating.</param>
        public RepertoirePitch(PitchType type, double usage, double velocityOffsetMph = 0.0, int familiarity = 0)
        {
            if (!(usage > 0.0)) throw new ArgumentOutOfRangeException(nameof(usage));
            if (familiarity < -50 || familiarity > 50) throw new ArgumentOutOfRangeException(nameof(familiarity));
            Type = type;
            Usage = usage;
            VelocityOffsetMph = velocityOffsetMph;
            Familiarity = familiarity;
        }

        public PitchType Type { get; }
        public double Usage { get; }
        public double VelocityOffsetMph { get; }
        public int Familiarity { get; }
    }

    /// <summary>The pitches a pitcher throws, with usage weights (TASK-018). A pitcher throws nothing else.</summary>
    public sealed class Repertoire
    {
        private readonly RepertoirePitch[] _pitches;

        public Repertoire(params RepertoirePitch[] pitches)
        {
            if (pitches == null || pitches.Length == 0) throw new ArgumentException("A repertoire has at least one pitch.", nameof(pitches));
            var seen = new HashSet<PitchType>();
            foreach (RepertoirePitch p in pitches)
                if (!seen.Add(p.Type)) throw new ArgumentException($"{p.Type} listed twice.", nameof(pitches));
            _pitches = (RepertoirePitch[])pitches.Clone();
        }

        public IReadOnlyList<RepertoirePitch> Pitches => _pitches;
        public int Count => _pitches.Length;

        public bool Has(PitchType type) => Array.FindIndex(_pitches, p => p.Type == type) >= 0;

        public RepertoirePitch Get(PitchType type)
        {
            int i = Array.FindIndex(_pitches, p => p.Type == type);
            return i >= 0 ? _pitches[i] : throw new ArgumentException($"Not in the repertoire: {type}.", nameof(type));
        }

        /// <summary>The next (or previous, <paramref name="step"/> −1) pitch in the repertoire after <paramref name="type"/>
        /// (manual pitching cycles through his pitches only).</summary>
        public PitchType Step(PitchType type, int step)
        {
            int i = Array.FindIndex(_pitches, p => p.Type == type);
            int n = _pitches.Length;
            return _pitches[(((i < 0 ? 0 : i + step) % n) + n) % n].Type;
        }
    }
}
