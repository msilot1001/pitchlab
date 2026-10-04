using System;

namespace Pitchlab.Simulation.Field
{
    public enum SurfaceKind
    {
        NaturalGrass,
        InfieldDirt,
        WarningTrack,
        Wall,
    }

    /// <summary>
    /// Ball–surface coefficients (Docs/SURFACE_PHYSICS.md, which records each value's source and confidence). Impact:
    /// normal restitution e_n, Coulomb friction μ, tangential (grip) restitution e_t, plow κ (centre-of-mass tangential
    /// impulse per unit normal impulse, grass). Ground contact: μ for sliding; rolling deceleration a₀ + b·v rising with
    /// speed (the measured form for balls rolling on turf, Kolitzus/ISSS; values ASSUMED).
    /// </summary>
    public readonly struct BallSurfaceProperties
    {
        public readonly SurfaceKind Kind;
        public readonly double NormalRestitution, Friction, TangentialRestitution, Plow;
        /// <summary>Rolling deceleration a₀ (m/s²) and its speed coefficient b (1/s): a(v) = a₀ + b·v.</summary>
        public readonly double RollingDecelerationBase, RollingDecelerationPerSpeed;
        /// <summary>Change of the plow coefficient per m/s of impact speed away from <see cref="PlowReferenceSpeed"/> (soft
        /// surfaces plow more at higher speed; Pennbounce speed dependence). κ(v) = max(0, κ + slope·(v − 40.2)), with v
        /// clamped to the measured 31–40.2 m/s (no extrapolation).</summary>
        public readonly double PlowSpeedSlope;
        public const double PlowReferenceSpeed = 40.2;

        public BallSurfaceProperties(SurfaceKind kind, double normalRestitution, double friction, double tangentialRestitution, double plow,
            double rollingDecelerationBase, double rollingDecelerationPerSpeed, double plowSpeedSlope = 0.0)
        {
            if (!(normalRestitution >= 0.0 && normalRestitution <= 1.0)) throw new ArgumentOutOfRangeException(nameof(normalRestitution));
            if (!(friction >= 0.0)) throw new ArgumentOutOfRangeException(nameof(friction));
            if (!(tangentialRestitution >= -1.0 && tangentialRestitution <= 1.0)) throw new ArgumentOutOfRangeException(nameof(tangentialRestitution));
            if (!(plow >= 0.0)) throw new ArgumentOutOfRangeException(nameof(plow));
            if (!(rollingDecelerationBase >= 0.0)) throw new ArgumentOutOfRangeException(nameof(rollingDecelerationBase));
            if (!(rollingDecelerationPerSpeed >= 0.0)) throw new ArgumentOutOfRangeException(nameof(rollingDecelerationPerSpeed));
            Kind = kind;
            NormalRestitution = normalRestitution;
            Friction = friction;
            TangentialRestitution = tangentialRestitution;
            Plow = plow;
            RollingDecelerationBase = rollingDecelerationBase;
            RollingDecelerationPerSpeed = rollingDecelerationPerSpeed;
            PlowSpeedSlope = plowSpeedSlope;
        }

        /// <summary>Plow coefficient for an impact at <paramref name="speed"/> (m/s).</summary>
        public double PlowAt(double speed) => Math.Max(0.0, Plow + PlowSpeedSlope * (Math.Max(31.0, Math.Min(PlowReferenceSpeed, speed)) - PlowReferenceSpeed));

        /// <summary>Rolling deceleration (m/s²) at rolling speed <paramref name="speed"/>.</summary>
        public double RollingDeceleration(double speed) => RollingDecelerationBase + RollingDecelerationPerSpeed * speed;

        /// <summary>Distance and time to stop from rolling speed v under dv/dt = −(a₀ + b·v) (exact).</summary>
        public (double Distance, double Time) RollingStop(double v)
        {
            double a = RollingDecelerationBase, b = RollingDecelerationPerSpeed;
            if (b <= 0.0) return a > 0.0 ? (v * v / (2.0 * a), v / a) : (double.PositiveInfinity, double.PositiveInfinity);
            if (a <= 0.0) return (v / b, double.PositiveInfinity);
            double log = Math.Log(1.0 + b * v / a);
            return (v / b - a / (b * b) * log, log / b);
        }

        /// <summary>e_n ASSUMED (≈ Cross's 0.39 for a baseball on a hard surface); κ and its speed slope fit to the four
        /// Pennbounce skinned-infield means, RMS 0.017 (DERIVED); μ, e_t from Cross; rolling ASSUMED.</summary>
        public static BallSurfaceProperties InfieldDirt => new BallSurfaceProperties(SurfaceKind.InfieldDirt, 0.40, 0.50, 0.25, 0.070, 0.50, 0.10, -0.014);
        /// <summary>e_n ASSUMED (softer than dirt); κ and slope fit to the four Pennbounce turfgrass means, RMS 0.013
        /// (DERIVED); rolling ASSUMED.</summary>
        public static BallSurfaceProperties NaturalGrass => new BallSurfaceProperties(SurfaceKind.NaturalGrass, 0.30, 0.40, 0.0, 0.655, 1.00, 0.20, 0.014);
        /// <summary>No baseball data: skinned-infield bounce, slightly looser (ASSUMED).</summary>
        public static BallSurfaceProperties WarningTrack => new BallSurfaceProperties(SurfaceKind.WarningTrack, 0.40, 0.55, 0.25, 0.10, 0.80, 0.12, -0.014);
        /// <summary>Padded outfield wall, no data (ASSUMED below the 0.55 rigid-wall ball COR).</summary>
        public static BallSurfaceProperties Wall => new BallSurfaceProperties(SurfaceKind.Wall, 0.30, 0.50, 0.10, 0.0, 0.0, 0.0);

        public static BallSurfaceProperties For(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.InfieldDirt: return InfieldDirt;
                case SurfaceKind.WarningTrack: return WarningTrack;
                case SurfaceKind.Wall: return Wall;
                default: return NaturalGrass;
            }
        }
    }
}
