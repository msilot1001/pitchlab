using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Simulation.Tracking
{
    /// <summary>Accelerations implied by a constant-acceleration fit, split into gravity, drag, and Magnus (SI).</summary>
    public readonly struct AerodynamicDecomposition
    {
        /// <summary>Air-relative speed used for the coefficients, m/s.</summary>
        public readonly double AirSpeed;
        /// <summary>Component of (a − g) along the airflow; negative = drag.</summary>
        public readonly double DragAcceleration;
        /// <summary>Component of (a − g) perpendicular to the airflow, m/s².</summary>
        public readonly Vector3d MagnusAcceleration;
        public readonly double ImpliedDragCoefficient;
        public readonly double ImpliedLiftCoefficient;

        public AerodynamicDecomposition(double airSpeed, double dragAcceleration, Vector3d magnusAcceleration, double cd, double cl)
        {
            AirSpeed = airSpeed;
            DragAcceleration = dragAcceleration;
            MagnusAcceleration = magnusAcceleration;
            ImpliedDragCoefficient = cd;
            ImpliedLiftCoefficient = cl;
        }
    }

    /// <summary>
    /// Boundary between Statcast data and the simulation: feet → metres, ft/s → m/s, rpm → rad/s, Statcast spin axis →
    /// spin vector. The axes already coincide (Docs/PHYSICS.md), so no rotation is involved. No Statcast logic lives in
    /// the simulator itself.
    /// </summary>
    public static class StatcastAdapter
    {
        public static readonly double PlateFrontYFeet = Units.MetersToFeet(PitchingGeometry.PlateFrontY);

        /// <summary>Fit state at fit time <paramref name="t"/> (s, 0 at y = 50 ft) as an SI simulation state starting at time 0.</summary>
        public static BallState FitState(StatcastNinePointFit fit, double t, Vector3d spin) => new BallState(
            0.0, fit.PositionFeet(t) * Units.MetersPerFoot, fit.VelocityFeet(t) * Units.MetersPerFoot, spin);

        /// <summary>
        /// Spin vector (rad/s) with the given transverse rate and Statcast spin axis, gyro component omitted (its
        /// sign is not measured and it produces no lift at release).
        /// </summary>
        public static Vector3d TransverseSpin(double transverseRpm, double spinAxisDegrees) =>
            Units.RpmToRadiansPerSecond(transverseRpm) * PitchInput.SpinDirection(Units.DegreesToRadians(spinAxisDegrees), 0.0);

        /// <summary>
        /// Splits the fit's constant acceleration, minus gravity, into components along and perpendicular to the
        /// air-relative velocity at fit time <paramref name="t"/> (Nathan's PITCHf/x method), and converts them to
        /// implied C_D and C_L with the ball's area and mass and the given air density.
        /// </summary>
        public static AerodynamicDecomposition Decompose(StatcastNinePointFit fit, double t, BallProperties ball, EnvironmentState environment)
        {
            Vector3d acceleration = fit.AccelerationFeet * Units.MetersPerFoot + new Vector3d(0.0, 0.0, environment.Gravity);
            Vector3d air = fit.VelocityFeet(t) * Units.MetersPerFoot - environment.Wind;
            double speed = air.Length;
            Vector3d direction = air / speed;
            double along = Vector3d.Dot(acceleration, direction);
            Vector3d magnus = acceleration - along * direction;
            double q = 0.5 * environment.AirDensity * ball.CrossSectionArea * speed * speed / ball.Mass;
            return new AerodynamicDecomposition(speed, along, magnus, -along / q, magnus.Length / q);
        }
    }
}
