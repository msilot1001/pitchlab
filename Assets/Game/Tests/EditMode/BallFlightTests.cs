using System;
using NUnit.Framework;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    public class BallFlightTests
    {
        private static readonly BallProperties Ball = BallProperties.Baseball;
        private static readonly FlightLimits ToPlate = new FlightLimits(PitchingGeometry.PlateFrontY, double.NegativeInfinity, 5.0);

        private static BallFlightSimulator Simulator(EnvironmentState environment, AerodynamicModel aerodynamics, double dt = BallFlightSimulator.DefaultTimeStep) =>
            new BallFlightSimulator(Ball, environment, aerodynamics, dt);

        private static BallState Pitch(double mph, Vector3d spinRpm) =>
            new BallState(0.0, new Vector3d(0.0, 16.5, 1.8),
                new Vector3d(0.0, -Units.MphToMetersPerSecond(mph), 0.0),
                spinRpm * Units.RadiansPerSecondPerRpm);

        [Test]
        public void IdenticalInputsProduceBitIdenticalTrajectories()
        {
            BallState release = PitchPresets.Curveball.ToInitialState();
            TrajectoryResult a = Simulator(EnvironmentState.Standard, AerodynamicModel.Baseball).Simulate(release, ToPlate);
            TrajectoryResult b = Simulator(EnvironmentState.Standard, AerodynamicModel.Baseball).Simulate(release, ToPlate);

            Assert.AreEqual(a.Samples.Count, b.Samples.Count);
            for (int i = 0; i < a.Samples.Count; i++)
            {
                Assert.AreEqual(a.Samples[i].Time, b.Samples[i].Time);
                Assert.AreEqual(a.Samples[i].Position, b.Samples[i].Position);
                Assert.AreEqual(a.Samples[i].Velocity, b.Samples[i].Velocity);
            }
        }

        [Test]
        public void WithoutAerodynamicsFlightIsBallistic()
        {
            var release = new BallState(0.0, new Vector3d(-0.5, 16.5, 1.8), new Vector3d(1.0, -40.0, 2.0), new Vector3d(-200.0, 0.0, 50.0));
            TrajectoryResult flight = Simulator(EnvironmentState.Vacuum, AerodynamicModel.None).Simulate(release, ToPlate);
            double g = EnvironmentState.StandardGravity;

            foreach (BallState s in flight.Samples)
            {
                double t = s.Time;
                var expected = release.Position + release.Velocity * t + new Vector3d(0.0, 0.0, -0.5 * g * t * t);
                Assert.AreEqual(0.0, (s.Position - expected).Length, 1e-9, $"t = {t}");
            }

            double plateTime = (release.Position.Y - PitchingGeometry.PlateFrontY) / 40.0;
            Assert.AreEqual(plateTime, flight.Final.Time, 1e-9);
        }

        [Test]
        public void DragOnlyMatchesAnalyticSolution()
        {
            // 1D quadratic drag: x(t) = ln(1 + k·v0·t)/k  ⇒  t(d) = (e^{k·d} − 1)/(k·v0), with k = ρ·A·C_D/(2m).
            var still = new EnvironmentState(1.225, 0.0, Vector3d.Zero);
            var drag = new AerodynamicModel(0.35, liftEnabled: false);
            double v0 = Units.MphToMetersPerSecond(95.0);
            BallState release = Pitch(95.0, Vector3d.Zero);
            TrajectoryResult flight = Simulator(still, drag).Simulate(release, ToPlate);

            double k = still.AirDensity * Ball.CrossSectionArea * drag.DragCoefficient / (2.0 * Ball.Mass);
            double d = release.Position.Y - PitchingGeometry.PlateFrontY;
            double expectedTime = (Math.Exp(k * d) - 1.0) / (k * v0);
            double expectedSpeed = v0 / (1.0 + k * v0 * expectedTime);

            Assert.AreEqual(expectedTime, flight.Final.Time, 1e-9);
            Assert.AreEqual(expectedSpeed, flight.Final.Velocity.Length, 1e-7);
        }

        [Test]
        public void DragOpposesVelocityRelativeToAir()
        {
            var random = new Random(1234);
            for (int i = 0; i < 200; i++)
            {
                var wind = new Vector3d(random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10, random.NextDouble() * 4 - 2);
                var velocity = new Vector3d(random.NextDouble() * 80 - 40, random.NextDouble() * 80 - 40, random.NextDouble() * 40 - 20);
                var simulator = Simulator(new EnvironmentState(1.2, EnvironmentState.StandardGravity, wind), AerodynamicModel.Baseball);
                Vector3d air = velocity - wind;
                Vector3d drag = simulator.DragForce(velocity);

                Assert.Less(Vector3d.Dot(drag, air), 0.0);
                Assert.AreEqual(0.0, Vector3d.Cross(drag, air).Length / (drag.Length * air.Length), 1e-12, "drag must be antiparallel to airflow");
            }

            // A ball at rest in a tailwind is pushed downwind.
            var breeze = Simulator(new EnvironmentState(1.2, 0.0, new Vector3d(5.0, 0.0, 0.0)), AerodynamicModel.Baseball);
            Assert.Greater(breeze.DragForce(Vector3d.Zero).X, 0.0);
        }

        [Test]
        public void ZeroSpinProducesNoMagnusForceOrMovement()
        {
            var simulator = Simulator(EnvironmentState.Standard, AerodynamicModel.Baseball);
            Assert.AreEqual(Vector3d.Zero, simulator.MagnusForce(new Vector3d(1.0, -40.0, -2.0), Vector3d.Zero));

            PitchInput input = PitchPresets.FourSeam;
            input.SpinRateRpm = 0.0;
            PitchMetrics metrics = PitchSimulation.Run(input, EnvironmentState.Standard).Metrics;
            Assert.AreEqual(0.0, metrics.HorizontalMovement);
            Assert.AreEqual(0.0, metrics.VerticalMovement);
        }

        [Test]
        public void SpinAlongVelocityProducesNoMagnusForce()
        {
            var simulator = Simulator(EnvironmentState.Standard, AerodynamicModel.Baseball);
            var velocity = new Vector3d(2.0, -40.0, -1.0);
            Assert.AreEqual(0.0, simulator.MagnusForce(velocity, velocity.Normalized * 250.0).Length, 1e-12);
        }

        [Test]
        public void ReversingSpinReversesMagnusForce()
        {
            var simulator = Simulator(EnvironmentState.Standard, AerodynamicModel.Baseball);
            var velocity = new Vector3d(1.0, -40.0, -1.5);
            var spin = new Vector3d(-220.0, 15.0, 60.0);
            Vector3d forward = simulator.MagnusForce(velocity, spin);
            Vector3d reversed = simulator.MagnusForce(velocity, -spin);

            Assert.Greater(forward.Length, 0.0);
            Assert.AreEqual(0.0, (forward + reversed).Length, 1e-12);
        }

        [Test]
        public void BackspinLiftsAndTopspinDropsAPitch()
        {
            // Pitch moving along −Y: backspin is −X by the right-hand rule (top of the ball moves toward the pitcher).
            var simulator = Simulator(EnvironmentState.Standard, AerodynamicModel.Baseball);
            var velocity = new Vector3d(0.0, -40.0, 0.0);
            Assert.Greater(simulator.MagnusForce(velocity, new Vector3d(-240.0, 0.0, 0.0)).Z, 0.0);
            Assert.Less(simulator.MagnusForce(velocity, new Vector3d(240.0, 0.0, 0.0)).Z, 0.0);
        }

        [Test]
        public void MagnusDeflectionMatchesPublishedValues()
        {
            // Nathan, Am. J. Phys. 76, 119 (2008), Table I: deflection after 55 ft, horizontal launch, pure backspin,
            // computed with Adair's C_D and the Sawicki et al. C_L (air density not stated; we use 1.225).
            // Our C_L fit is up to ~9 % below Sawicki at S ≈ 0.1, so allow 15 %.
            (double mph, double rpm, double inches)[] cases = { (90, 1800, 19), (90, 1000, 14), (75, 1800, 21), (75, 1000, 16) };
            foreach (var (mph, rpm, inches) in cases)
            {
                var release = new BallState(0.0, new Vector3d(0.0, PitchingGeometry.PlateFrontY + Units.FeetToMeters(55.0), 1.8),
                    new Vector3d(0.0, -Units.MphToMetersPerSecond(mph), 0.0), new Vector3d(-Units.RpmToRadiansPerSecond(rpm), 0.0, 0.0));
                PitchMetrics metrics = PitchSimulation.Run(release, Ball, new EnvironmentState(1.225, EnvironmentState.StandardGravity, Vector3d.Zero),
                    AerodynamicModel.Baseball).Metrics;
                double deflection = Units.MetersToInches(metrics.VerticalMovement);
                Assert.AreEqual(inches, deflection, 0.15 * inches, $"{mph} mph, {rpm} rpm");
            }
        }

        [Test]
        public void MagnusForceHasModelMagnitudeAndIsPerpendicularToAirflow()
        {
            var wind = new Vector3d(3.0, 4.0, 0.5);
            var simulator = Simulator(new EnvironmentState(1.2, EnvironmentState.StandardGravity, wind), AerodynamicModel.Baseball);
            var velocity = new Vector3d(1.0, -38.0, -2.0);
            var spin = new Vector3d(-200.0, 30.0, 90.0);
            Vector3d air = velocity - wind;
            Vector3d force = simulator.MagnusForce(velocity, spin);

            double transverseSpin = Vector3d.Cross(spin, air).Length / air.Length;
            double s = Ball.Radius * transverseSpin / air.Length;
            double expected = 0.5 * 1.2 * Ball.CrossSectionArea * AerodynamicModel.Baseball.LiftCoefficient(s) * air.LengthSquared;
            Assert.AreEqual(expected, force.Length, 1e-12 * expected);
            Assert.AreEqual(0.0, Vector3d.Dot(force, air) / (force.Length * air.Length), 1e-12, "lift is perpendicular to the airflow, not the ground velocity");
            Assert.Greater(Vector3d.Dot(force, Vector3d.Cross(spin, air)), 0.0, "lift points along ω × v_air");
        }

        [Test]
        public void StandardDryAirMatchesIsaDensity()
        {
            Assert.AreEqual(1.2250, EnvironmentState.MoistAirDensity(15.0, 101325.0, 0.0), 0.0005);
            Assert.Less(EnvironmentState.MoistAirDensity(15.0, 101325.0, 1.0), EnvironmentState.MoistAirDensity(15.0, 101325.0, 0.0),
                "humid air is less dense");
        }
    }
}
