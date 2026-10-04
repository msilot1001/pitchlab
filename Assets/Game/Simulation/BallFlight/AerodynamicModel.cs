using System;

namespace Pitchlab.Simulation.BallFlight
{
    /// <summary>
    /// Empirical aerodynamic coefficients. Isolated here so a fitted or measured model can replace them.
    /// Both coefficients are dimensionless; see Docs/PHYSICS.md for sources and validity ranges.
    /// </summary>
    public readonly struct AerodynamicModel
    {
        /// <summary>Drag coefficient at zero spin, C_D,0.</summary>
        public readonly double DragCoefficient;
        /// <summary>Linear spin dependence of drag: C_D = C_D,0 + this × (ω / 1000 rpm). Zero for the pitch model.</summary>
        public readonly double DragPerThousandRpm;
        public readonly bool LiftEnabled;
        /// <summary>Exponential spin-decay time constant τ (s): ω(t) = ω₀·e^(−t/τ). +∞ = no decay.</summary>
        public readonly double SpinDecayTime;

        public AerodynamicModel(double dragCoefficient, bool liftEnabled, double dragPerThousandRpm = 0.0, double spinDecayTime = double.PositiveInfinity)
        {
            if (!(dragCoefficient >= 0.0)) throw new ArgumentOutOfRangeException(nameof(dragCoefficient));
            if (!(dragPerThousandRpm >= 0.0) || double.IsInfinity(dragPerThousandRpm)) throw new ArgumentOutOfRangeException(nameof(dragPerThousandRpm));
            if (!(spinDecayTime > 0.0)) throw new ArgumentOutOfRangeException(nameof(spinDecayTime));
            DragCoefficient = dragCoefficient;
            DragPerThousandRpm = dragPerThousandRpm;
            LiftEnabled = liftEnabled;
            SpinDecayTime = spinDecayTime;
        }

        /// <summary>Drag coefficient for a ball spinning at <paramref name="spinRate"/> rad/s.</summary>
        public double DragCoefficientAt(double spinRate) =>
            DragCoefficient + DragPerThousandRpm * (spinRate / (1000.0 * 2.0 * Math.PI / 60.0));

        /// <summary>
        /// Lowest Reynolds number (ρ·v·2r/μ) for which constant C_D = 0.35 is supported: pitch-speed free-flight data
        /// (Lyu et al. 2022) put the drag crisis at Re ≈ 0.75–1.75 × 10⁵. ≈ 69 mph at standard density.
        /// </summary>
        public const double MinSupportedReynolds = 1.5e5;
        /// <summary>Highest spin parameter for which the C_L fit agrees with measured data (S ≈ 0.1–0.3 well, sparse to ≈ 0.6).</summary>
        public const double MaxSupportedSpinParameter = 0.4;

        /// <summary>C_D = 0.35 (pitch-speed free-flight measurements, Lyu et al. 2022) with Nathan (2017) lift.</summary>
        public static AerodynamicModel Baseball => new AerodynamicModel(0.35, true);

        /// <summary>
        /// Batted-ball model: Nathan (2017) Eqs. 10–11, the jointly fitted pair for 2016 Statcast fly balls at Tropicana
        /// Field — C_D = 0.297 + 0.0292·(ω / 1000 rpm) with the same C_L fit — plus exponential spin decay with τ = 30 s
        /// (unpublished TrackMan measurements quoted by Nathan). Pitches keep <see cref="Baseball"/> (ADR 0003).
        /// </summary>
        public static AerodynamicModel BattedBall => new AerodynamicModel(0.297, true, 0.0292, 30.0);

        /// <summary>Same drag (and spin decay), no Magnus force. Reference trajectory for movement metrics.</summary>
        public AerodynamicModel WithoutLift => new AerodynamicModel(DragCoefficient, false, DragPerThousandRpm, SpinDecayTime);

        /// <summary>No aerodynamic forces at all.</summary>
        public static AerodynamicModel None => new AerodynamicModel(0.0, false);

        /// <summary>
        /// Lift coefficient from spin parameter S = r·ω⊥/v (ω⊥ = spin perpendicular to the airflow):
        /// C_L = 1.120·S / (0.583 + 2.333·S), Nathan (2017) fit to Statcast data. Agrees with the Sawicki et al.
        /// (2003) parametrization within about 4 % for 0.12 ≤ S ≤ 0.3; about 9 % lower at S = 0.1.
        /// </summary>
        public double LiftCoefficient(double spinParameter)
        {
            if (!LiftEnabled || spinParameter <= 0.0) return 0.0;
            return 1.120 * spinParameter / (0.583 + 2.333 * spinParameter);
        }

        /// <summary>
        /// Inverse of <see cref="LiftCoefficient"/>: S = 0.583·C_L / (1.120 − 2.333·C_L). Returns +∞ at or above the
        /// fit's saturation value 1.120/2.333 ≈ 0.480 (no spin can produce that much lift in this model).
        /// </summary>
        public static double SpinParameterForLift(double liftCoefficient)
        {
            if (liftCoefficient <= 0.0) return 0.0;
            double denominator = 1.120 - 2.333 * liftCoefficient;
            return denominator > 0.0 ? 0.583 * liftCoefficient / denominator : double.PositiveInfinity;
        }
    }
}
