using System;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.BallFlight
{
    /// <summary>Physical ball properties (SI).</summary>
    public readonly struct BallProperties
    {
        public readonly double Mass;
        public readonly double Radius;

        public BallProperties(double mass, double radius)
        {
            if (!(mass > 0.0)) throw new ArgumentOutOfRangeException(nameof(mass));
            if (!(radius > 0.0)) throw new ArgumentOutOfRangeException(nameof(radius));
            Mass = mass;
            Radius = radius;
        }

        public double CrossSectionArea => Math.PI * Radius * Radius;

        /// <summary>
        /// Nominal MLB ball: 5.125 oz and 9.125 in circumference, the midpoints of the Official Baseball Rules
        /// ranges (5–5.25 oz, 9–9.25 in) and the defaults of Nathan's trajectory calculator.
        /// </summary>
        public static BallProperties Baseball => new BallProperties(
            5.125 * 0.028349523125,
            Units.InchesToMeters(9.125) / (2.0 * Math.PI));
    }
}
