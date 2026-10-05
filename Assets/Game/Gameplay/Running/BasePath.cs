using System;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Running
{
    /// <summary>
    /// One leg of the base path, from a base to the next (home → first → second → third → home), on the ground. Straight
    /// between the bags, or — when the runner may round the next base — the "banana" route coaches teach: drifting out
    /// (away from the diamond) over the last part of the leg and cutting back across the bag toward the following base. The
    /// drift is the cubic o(s) = (27/4)·w·s²·(1 − s) over the last part of the leg (s from 0 to 1): it leaves the straight line
    /// tangentially (no kink), is widest (w) two thirds of the way, and crosses the bag already turned ≈ 33° toward the next
    /// base, so the remaining turn at the bag is ≈ 57°, not 90°. The path always passes through the bag centres. Arc length
    /// is tabulated so motion is along distance travelled.
    /// </summary>
    public sealed class BaseLeg
    {
        private const int Samples = 96;
        /// <summary>Outward drift of the banana route at its widest (m; ≈ 4 ft, coaching references — gameplay baseline).</summary>
        public const double BananaWidth = 1.2;
        /// <summary>Where along the leg (fraction) the drift starts.</summary>
        public const double BananaStart = 0.55;

        private readonly double[] _arc = new double[Samples + 1];

        private BaseLeg(Base from, bool banana)
        {
            From = from;
            To = Bases(from);
            Banana = banana;
            Start = FieldLayout.BasePosition(From);
            End = FieldLayout.BasePosition(To);
            Vector3d d = End - Start;
            _along = d / d.Length;
            // Outward: perpendicular to the leg, away from the middle of the diamond.
            Vector3d centre = 0.5 * (FieldLayout.BasePosition(Base.Home) + FieldLayout.BasePosition(Base.Second));
            var normal = new Vector3d(-_along.Y, _along.X, 0.0);
            Vector3d mid = 0.5 * (Start + End);
            _outward = Vector3d.Dot(mid - centre, normal) >= 0.0 ? normal : -1.0 * normal;
            _chord = d.Length;
            Vector3d previous = Start;
            for (int i = 1; i <= Samples; i++)
            {
                Vector3d p = AtFraction((double)i / Samples);
                _arc[i] = _arc[i - 1] + (p - previous).Length;
                previous = p;
            }

            Length = _arc[Samples];
        }

        private readonly Vector3d _along, _outward;
        private readonly double _chord;

        public Base From { get; }
        public Base To { get; }
        public bool Banana { get; }
        public Vector3d Start { get; }
        public Vector3d End { get; }
        /// <summary>Path length (m).</summary>
        public double Length { get; }

        private static readonly BaseLeg[] Straight = { new BaseLeg(Base.Home, false), new BaseLeg(Base.First, false), new BaseLeg(Base.Second, false), new BaseLeg(Base.Third, false) };
        private static readonly BaseLeg[] Rounding = { new BaseLeg(Base.Home, true), new BaseLeg(Base.First, true), new BaseLeg(Base.Second, true), new BaseLeg(Base.Third, true) };

        /// <summary>The leg starting at <paramref name="from"/>.</summary>
        public static BaseLeg Of(Base from, bool banana) => (banana ? Rounding : Straight)[(int)from];

        public static Base Bases(Base from) => from switch { Base.Home => Base.First, Base.First => Base.Second, Base.Second => Base.Third, _ => Base.Home };

        /// <summary>Ground position at distance <paramref name="distance"/> along the leg (clamped to it; beyond the end it
        /// continues straight along the leg's final direction, for overruns).</summary>
        public Vector3d PositionAt(double distance)
        {
            if (distance <= 0.0) return Start + distance * _along;
            if (distance >= Length) return End + (distance - Length) * EndDirection;
            int lo = 0, hi = Samples;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_arc[mid] <= distance) lo = mid;
                else hi = mid;
            }

            double u = (distance - _arc[lo]) / (_arc[hi] - _arc[lo]);
            return AtFraction((lo + u) / Samples);
        }

        /// <summary>Running direction at <paramref name="distance"/> (unit, ground plane).</summary>
        public Vector3d DirectionAt(double distance)
        {
            double h = Math.Min(0.05, Length * 1e-3);
            Vector3d a = PositionAt(Math.Max(0.0, Math.Min(Length, distance) - h)), b = PositionAt(Math.Min(Length, Math.Max(0.0, distance) + h));
            Vector3d d = b - a;
            return d.Length > 1e-12 ? d / d.Length : _along;
        }

        /// <summary>Heading leaving the leg's end bag (the banana's cut-back direction; the leg direction when straight).</summary>
        public Vector3d EndDirection
        {
            get
            {
                Vector3d d = AtFraction(1.0) - AtFraction(1.0 - 1e-4);
                return d / d.Length;
            }
        }

        private Vector3d AtFraction(double u)
        {
            double offset = 0.0;
            if (Banana && u > BananaStart)
            {
                double s = (u - BananaStart) / (1.0 - BananaStart);
                offset = 6.75 * BananaWidth * s * s * (1.0 - s);
            }
            return Start + u * _chord * _along + offset * _outward;
        }
    }
}
