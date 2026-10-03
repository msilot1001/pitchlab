using System;

namespace Pitchlab.Simulation.BallFlight
{
    /// <summary>
    /// Empirical aerodynamic coefficients. Isolated here so a fitted or measured model can replace them.
    /// Both coefficients are dimensionless; see Docs/PHYSICS.md for sources and validity ranges.
    /// </summary>
    public readonly struct AerodynamicModel
    {
        /// <summary>Constant drag coefficient C_D.</summary>
        public readonly double DragCoefficient;
        public readonly bool LiftEnabled;

        public AerodynamicModel(double dragCoefficient, bool liftEnabled)
        {
            if (!(dragCoefficient >= 0.0)) throw new ArgumentOutOfRangeException(nameof(dragCoefficient));
            DragCoefficient = dragCoefficient;
            LiftEnabled = liftEnabled;
        }

        /// <summary>
        /// Lowest Reynolds number (ρ·v·2r/μ) for which constant C_D = 0.35 is supported: pitch-speed free-flight data
        /// (Lyu et al. 2022) put the drag crisis at Re ≈ 0.75–1.75 × 10⁵. ≈ 69 mph at standard density.
        /// </summary>
        public const double MinSupportedReynolds = 1.5e5;
        /// <summary>Highest spin parameter for which the C_L fit agrees with measured data (S ≈ 0.1–0.3 well, sparse to ≈ 0.6).</summary>
        public const double MaxSupportedSpinParameter = 0.4;

        /// <summary>C_D = 0.35 (pitch-speed free-flight measurements, Lyu et al. 2022) with Nathan (2017) lift.</summary>
        public static AerodynamicModel Baseball => new AerodynamicModel(0.35, true);

        /// <summary>Same drag, no Magnus force. Reference trajectory for movement metrics.</summary>
        public AerodynamicModel WithoutLift => new AerodynamicModel(DragCoefficient, false);

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
