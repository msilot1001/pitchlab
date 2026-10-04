using System;
using NUnit.Framework;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Tracking;

namespace Pitchlab.Tests.Statcast
{
    public class StatcastAdapterTests
    {
        private static StatcastPitch Pitch(double releaseY, double vy0, double ay, double ax = 0.0, double az = 0.0,
            double vx0 = 0.0, double vz0 = 0.0, double releaseX = -2.0, double releaseZ = 6.0) =>
            new StatcastPitch("FF", "R", 95.0, releaseX, releaseY, releaseZ, 60.5 - releaseY, vx0, vy0, vz0, ax, ay, az, 2300, 200, 0, 0, 0, 0);

        [Test]
        public void ConstantVelocityFitIsSolvedExactly()
        {
            // No acceleration: release at y = 54 ft with vy = −140 ft/s is 4/140 s before y = 50 ft.
            var fit = new StatcastNinePointFit(Pitch(54.0, -140.0, 0.0, vx0: 7.0, vz0: -3.5));
            Assert.AreEqual(-4.0 / 140.0, fit.ReleaseTime, 1e-12);
            Assert.AreEqual(-2.0 + 7.0 * 4.0 / 140.0, fit.X0, 1e-12, "x at y = 50 ft");
            Assert.AreEqual(6.0 - 3.5 * 4.0 / 140.0, fit.Z0, 1e-12, "z at y = 50 ft");
            Assert.AreEqual(50.0 / 140.0, fit.TimeAtY(0.0), 1e-12);
        }

        [Test]
        public void AcceleratingFitReachesReleaseAndPlateConsistently()
        {
            var fit = new StatcastNinePointFit(Pitch(54.0, -140.0, 30.0, ax: -10.0, az: -20.0, vx0: 6.0, vz0: -4.0));
            Vector3d release = fit.PositionFeet(fit.ReleaseTime);
            Assert.AreEqual(-2.0, release.X, 1e-9);
            Assert.AreEqual(54.0, release.Y, 1e-9);
            Assert.AreEqual(6.0, release.Z, 1e-9);
            double t = fit.TimeAtY(17.0 / 12.0);
            Assert.AreEqual(17.0 / 12.0, fit.PositionFeet(t).Y, 1e-9);
            Assert.Greater(t, 0.3);
        }

        [Test]
        public void FitStateIsConvertedToSiWithoutRotatingAxes()
        {
            var fit = new StatcastNinePointFit(Pitch(54.0, -140.0, 0.0, vx0: 5.0, vz0: -2.0));
            BallState state = StatcastAdapter.FitState(fit, 0.0, Vector3d.Zero);
            Assert.AreEqual(50.0 * 0.3048, state.Position.Y, 1e-12);
            Assert.AreEqual(-140.0 * 0.3048, state.Velocity.Y, 1e-12);
            Assert.AreEqual(5.0 * 0.3048, state.Velocity.X, 1e-12);
            Assert.AreEqual(-2.0 * 0.3048, state.Velocity.Z, 1e-12);
            Assert.AreEqual(0.4318, StatcastAdapter.PlateFrontYFeet * 0.3048, 1e-12);
        }

        [Test]
        public void SpinAxisMapsToStatcastBackspinAndSidespin()
        {
            Vector3d backspin = StatcastAdapter.TransverseSpin(2000.0, 180.0);
            Assert.AreEqual(-Units.RpmToRadiansPerSecond(2000.0), backspin.X, 1e-9);
            Assert.AreEqual(0.0, backspin.Y, 1e-12);
            Assert.AreEqual(0.0, backspin.Z, 1e-9);
            Vector3d toFirstBase = StatcastAdapter.TransverseSpin(2000.0, 90.0);
            Assert.AreEqual(Units.RpmToRadiansPerSecond(2000.0), toFirstBase.Z, 1e-9, "axis 90° → +Z spin → Magnus +X for a pitch along −Y");
        }

        [Test]
        public void DecompositionRecoversKnownDragAndLift()
        {
            // Build a fit whose acceleration is gravity + a drag of C_D 0.35 along −v + a Magnus of C_L 0.2 along +Z,
            // for a pitch moving straight along −Y at constant velocity (vy0 only; accelerations set directly).
            var environment = new EnvironmentState(1.2, EnvironmentState.StandardGravity, Vector3d.Zero);
            BallProperties ball = BallProperties.Baseball;
            double v = 40.0;
            double q = 0.5 * 1.2 * ball.CrossSectionArea * v * v / ball.Mass;
            double drag = 0.35 * q;  // m/s², along +Y (opposing −Y motion)
            double lift = 0.20 * q;  // m/s², along +Z
            double toFeet = 1.0 / Units.MetersPerFoot;
            var pitch = new StatcastPitch("FF", "R", 90.0, 0.0, 54.0, 6.0, 6.5, 0.0, -v * toFeet, 0.0,
                0.0, drag * toFeet, (lift - EnvironmentState.StandardGravity) * toFeet, 2300, 180, 0, 0, 0, 0);
            var fit = new StatcastNinePointFit(pitch);

            AerodynamicDecomposition d = StatcastAdapter.Decompose(fit, 0.0, ball, environment);
            Assert.AreEqual(0.35, d.ImpliedDragCoefficient, 1e-9);
            Assert.AreEqual(0.20, d.ImpliedLiftCoefficient, 1e-9);
            Assert.AreEqual(lift, d.MagnusAcceleration.Z, 1e-9);
        }

        [Test]
        public void LiftInversionRoundTrips()
        {
            foreach (double s in new[] { 0.01, 0.1, 0.25, 0.6 })
                Assert.AreEqual(s, AerodynamicModel.SpinParameterForLift(AerodynamicModel.Baseball.LiftCoefficient(s)), 1e-12);
            Assert.AreEqual(double.PositiveInfinity, AerodynamicModel.SpinParameterForLift(0.49), "beyond saturation");
            Assert.AreEqual(0.0, AerodynamicModel.SpinParameterForLift(0.0));
        }

        [Test]
        public void StandardAtmospherePressureMatchesIsaTable()
        {
            // ISA: 1000 m → 89 875 Pa; sea level 101 325 Pa.
            Assert.AreEqual(101325.0, EnvironmentState.StandardAtmospherePressure(0.0), 1e-9);
            Assert.AreEqual(89875.0, EnvironmentState.StandardAtmospherePressure(1000.0), 10.0);
        }
    }
}
