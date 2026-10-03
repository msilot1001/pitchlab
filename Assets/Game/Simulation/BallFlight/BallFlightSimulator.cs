using System;
using System.Collections.Generic;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.BallFlight
{
    /// <summary>When a flight stops. All limits are in the simulation frame (metres, seconds).</summary>
    public readonly struct FlightLimits
    {
        /// <summary>Stop when the ball centre moves through the plane Y = StopPlaneY in the -Y direction.</summary>
        public readonly double StopPlaneY;
        /// <summary>Stop when the bottom of the ball reaches Z = GroundZ.</summary>
        public readonly double GroundZ;
        public readonly double MaxDuration;

        public FlightLimits(double stopPlaneY, double groundZ, double maxDuration)
        {
            StopPlaneY = stopPlaneY;
            GroundZ = groundZ;
            MaxDuration = maxDuration;
        }
    }

    /// <summary>
    /// Authoritative ball-flight integrator: gravity + quadratic drag + Magnus lift, classic RK4 at a fixed
    /// time step. Stateless between calls and free of Unity and wall-clock time, so identical inputs give
    /// identical results. See Docs/PHYSICS.md for the model, frame, and the time-step study.
    /// </summary>
    public sealed class BallFlightSimulator
    {
        /// <summary>5 ms. Chosen from a convergence study (Docs/PHYSICS.md); validated by tests.</summary>
        public const double DefaultTimeStep = 0.005;

        public BallFlightSimulator(BallProperties ball, EnvironmentState environment, AerodynamicModel aerodynamics, double timeStep = DefaultTimeStep)
        {
            if (!(timeStep > 0.0)) throw new ArgumentOutOfRangeException(nameof(timeStep), "Time step must be positive.");
            Ball = ball;
            Environment = environment;
            Aerodynamics = aerodynamics;
            TimeStep = timeStep;
        }

        public BallProperties Ball { get; }
        public EnvironmentState Environment { get; }
        public AerodynamicModel Aerodynamics { get; }
        public double TimeStep { get; }

        /// <summary>Drag force (N) for a ball moving at <paramref name="velocity"/> relative to the ground.</summary>
        public Vector3d DragForce(Vector3d velocity)
        {
            Vector3d air = velocity - Environment.Wind;
            double speed = air.Length;
            double q = 0.5 * Environment.AirDensity * Ball.CrossSectionArea;
            return -q * Aerodynamics.DragCoefficient * speed * air;
        }

        /// <summary>
        /// Magnus (lift) force (N): magnitude ½ρA·C_L(S)·v², direction ω×v. Only spin perpendicular to the airflow
        /// contributes, because |ω×v| = ω⊥·v; S = r·ω⊥/v.
        /// </summary>
        public Vector3d MagnusForce(Vector3d velocity, Vector3d spin)
        {
            Vector3d air = velocity - Environment.Wind;
            double speed = air.Length;
            Vector3d spinCrossAir = Vector3d.Cross(spin, air);
            double crossLength = spinCrossAir.Length;
            if (speed <= 0.0 || crossLength <= 0.0) return Vector3d.Zero;

            double transverseSpin = crossLength / speed;
            double spinParameter = Ball.Radius * transverseSpin / speed;
            double q = 0.5 * Environment.AirDensity * Ball.CrossSectionArea;
            return q * Aerodynamics.LiftCoefficient(spinParameter) * speed * speed / crossLength * spinCrossAir;
        }

        /// <summary>Total acceleration (m/s²) of the ball centre.</summary>
        public Vector3d Acceleration(Vector3d velocity, Vector3d spin)
        {
            Vector3d gravity = new Vector3d(0.0, 0.0, -Environment.Gravity);
            return gravity + (DragForce(velocity) + MagnusForce(velocity, spin)) / Ball.Mass;
        }

        /// <summary>
        /// Advances one classic RK4 step of length <paramref name="dt"/>. Spin is held constant (no decay).
        /// The position stages are exact only because acceleration does not depend on position (uniform air and
        /// gravity). Position-dependent wind or density would need p + ½dt·k1p etc. passed into Acceleration.
        /// </summary>
        public BallState Step(BallState state, double dt)
        {
            Vector3d spin = state.Spin;
            Vector3d p = state.Position;
            Vector3d v = state.Velocity;

            Vector3d k1v = Acceleration(v, spin);
            Vector3d k1p = v;
            Vector3d k2v = Acceleration(v + 0.5 * dt * k1v, spin);
            Vector3d k2p = v + 0.5 * dt * k1v;
            Vector3d k3v = Acceleration(v + 0.5 * dt * k2v, spin);
            Vector3d k3p = v + 0.5 * dt * k2v;
            Vector3d k4v = Acceleration(v + dt * k3v, spin);
            Vector3d k4p = v + dt * k3v;

            return new BallState(
                state.Time + dt,
                p + dt / 6.0 * (k1p + 2.0 * k2p + 2.0 * k3p + k4p),
                v + dt / 6.0 * (k1v + 2.0 * k2v + 2.0 * k3v + k4v),
                spin);
        }

        /// <summary>
        /// Integrates from <paramref name="initial"/> at the fixed time step, recording every step, until a limit
        /// is reached. The terminating event is located inside the last step by bisection on a partial RK4 step,
        /// so its time and position do not depend on where a fixed step happens to land.
        /// </summary>
        public TrajectoryResult Simulate(BallState initial, FlightLimits limits)
        {
            if (!(limits.MaxDuration > 0.0)) throw new ArgumentOutOfRangeException(nameof(limits), "MaxDuration must be positive.");
            var samples = new List<BallState>((int)Math.Min(limits.MaxDuration / TimeStep + 2, 100000)) { initial };
            double endTime = initial.Time + limits.MaxDuration;
            BallState current = initial;

            while (true)
            {
                double dt = Math.Min(TimeStep, endTime - current.Time);
                BallState next = Step(current, dt);

                bool crossed = CrossedPlane(current, next, limits);
                bool grounded = BelowGround(next, limits);
                if (crossed || grounded)
                {
                    // Both can happen inside one step; the earlier event ends the flight.
                    BallState plane = crossed ? LocateEvent(current, dt, FlightEnd.CrossedStopPlane, limits) : default;
                    BallState ground = grounded ? LocateEvent(current, dt, FlightEnd.ReachedGround, limits) : default;
                    bool groundFirst = grounded && (!crossed || ground.Time < plane.Time);
                    samples.Add(groundFirst ? ground : plane);
                    return new TrajectoryResult(samples.ToArray(), groundFirst ? FlightEnd.ReachedGround : FlightEnd.CrossedStopPlane);
                }

                samples.Add(next);
                if (next.Time >= endTime) return new TrajectoryResult(samples.ToArray(), FlightEnd.ReachedMaxDuration);
                current = next;
            }
        }

        private static bool CrossedPlane(BallState before, BallState after, FlightLimits limits) =>
            before.Position.Y > limits.StopPlaneY && after.Position.Y <= limits.StopPlaneY;

        private bool BelowGround(BallState state, FlightLimits limits) =>
            state.Position.Z - Ball.Radius <= limits.GroundZ;

        private BallState LocateEvent(BallState before, double dt, FlightEnd end, FlightLimits limits)
        {
            // Invariant: event not reached at lo, reached at hi. 60 halvings resolve far below double time precision.
            double lo = 0.0;
            double hi = dt;
            for (int i = 0; i < 60 && hi - lo > 1e-12; i++)
            {
                double mid = 0.5 * (lo + hi);
                BallState probe = Step(before, mid);
                bool reached = end == FlightEnd.CrossedStopPlane
                    ? probe.Position.Y <= limits.StopPlaneY
                    : BelowGround(probe, limits);
                if (reached) hi = mid;
                else lo = mid;
            }

            return Step(before, hi);
        }
    }
}
