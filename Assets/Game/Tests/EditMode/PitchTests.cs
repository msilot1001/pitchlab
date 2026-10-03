using System;
using NUnit.Framework;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    public class PitchTests
    {
        private static PitchMetrics Run(PitchInput input) => PitchSimulation.Run(input, EnvironmentState.Standard).Metrics;

        private static PitchInput PureTransverse(double spinAxisDegrees) => new PitchInput
        {
            ReleaseHeightFeet = 6.0,
            ExtensionFeet = 6.0,
            SpeedMph = 90.0,
            SpinRateRpm = 2200.0,
            SpinAxisDegrees = spinAxisDegrees,
        };

        [Test]
        public void ReleaseInputMapsToDocumentedFrame()
        {
            BallState release = new PitchInput
            {
                ReleaseSideFeet = -2.0, ReleaseHeightFeet = 6.0, ExtensionFeet = 6.5, SpeedMph = 90.0,
                HorizontalAngleDegrees = 2.0, VerticalAngleDegrees = 1.0,
            }.ToInitialState();

            Assert.AreEqual(Units.FeetToMeters(-2.0), release.Position.X, 1e-12, "negative side = third-base side");
            Assert.AreEqual(Units.FeetToMeters(54.0), release.Position.Y, 1e-12, "release Y = 60.5 ft − extension");
            Assert.AreEqual(Units.FeetToMeters(6.0), release.Position.Z, 1e-12);
            Assert.Less(release.Velocity.Y, 0.0, "pitch travels toward the plate along −Y");
            Assert.Greater(release.Velocity.X, 0.0, "positive horizontal angle aims toward +X (first base)");
            Assert.Greater(release.Velocity.Z, 0.0, "positive vertical angle aims up");
            Assert.AreEqual(Units.MphToMetersPerSecond(90.0), release.Velocity.Length, 1e-12);
        }

        [TestCase(180.0, 0.0, 1.0, TestName = "Axis 180 (backspin) breaks up")]
        [TestCase(0.0, 0.0, -1.0, TestName = "Axis 0 (topspin) breaks down")]
        [TestCase(90.0, 1.0, 0.0, TestName = "Axis 90 breaks toward first base (+X)")]
        [TestCase(270.0, -1.0, 0.0, TestName = "Axis 270 breaks toward third base (-X)")]
        public void SpinAxisBreaksInStatcastDirection(double axis, double expectedX, double expectedZ)
        {
            PitchMetrics metrics = Run(PureTransverse(axis));
            double x = metrics.HorizontalMovement;
            double z = metrics.VerticalMovement;
            double length = Math.Sqrt(x * x + z * z);

            Assert.Greater(Units.MetersToInches(length), 10.0, "pure transverse spin should move the pitch substantially");
            // Direction within ~3° of the Statcast convention (gravity bends the path, so not exact).
            Assert.Greater((x * expectedX + z * expectedZ) / length, Math.Cos(Units.DegreesToRadians(3.0)));
        }

        [Test]
        public void RightHandedFourSeamRidesAndRunsArmSide()
        {
            PitchMetrics metrics = Run(PitchPresets.FourSeam);
            Assert.Greater(metrics.VerticalMovement, 0.0);
            Assert.Less(metrics.HorizontalMovement, 0.0, "a right-hander's arm side is third base (−X) from the catcher's view");
        }

        [Test]
        public void SpinEfficiencyScalesBreak()
        {
            PitchInput full = PureTransverse(180.0);
            PitchInput gyro = full;
            gyro.GyroAngleDegrees = 60.0;
            PitchInput bullet = full;
            bullet.GyroAngleDegrees = 90.0;

            Assert.AreEqual(0.5, Run(gyro).SpinEfficiency, 1e-12);
            Assert.Less(Run(gyro).VerticalMovement, Run(full).VerticalMovement);
            Assert.Less(Math.Abs(Units.MetersToInches(Run(bullet).VerticalMovement)), 0.5, "pure gyro spin barely moves the pitch");
        }

        [Test]
        public void GroundContactJustBeforeThePlateEndsTheFlightAtAnyStepSize()
        {
            // Vacuum flight built so the ball's bottom touches the ground 5 cm before the plate-front plane;
            // a single integration step usually contains both events.
            double g = EnvironmentState.StandardGravity;
            double r = BallProperties.Baseball.Radius;
            double speed = 40.0;
            double startY = 16.0;
            double groundY = PitchingGeometry.PlateFrontY + 0.05;
            double groundTime = (startY - groundY) / speed;
            var release = new BallState(0.0, new Vector3d(0.0, startY, r + 0.5 * g * groundTime * groundTime),
                new Vector3d(0.0, -speed, 0.0), Vector3d.Zero);

            foreach (double dt in new[] { 0.0037, 0.005, 0.0113 })
            {
                PitchResult result = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Vacuum, AerodynamicModel.None, dt);
                Assert.AreEqual(FlightEnd.ReachedGround, result.Flight.End, $"dt {dt}");
                Assert.AreEqual(groundTime, result.Flight.Final.Time, 1e-9, $"dt {dt}");
                Assert.IsFalse(result.Metrics.ReachedPlate, $"dt {dt}");
                Assert.IsNaN(result.Metrics.FlightTime, $"dt {dt}");
            }
        }

        [Test]
        public void FlightEndsAtMaxDurationWhenNoOtherLimitIsReached()
        {
            var climbing = new BallState(0.0, new Vector3d(0.0, 10.0, 1.0), new Vector3d(0.0, 0.0, 30.0), Vector3d.Zero);
            var limits = new FlightLimits(0.0, 0.0, 0.5);
            TrajectoryResult flight = new BallFlightSimulator(BallProperties.Baseball, EnvironmentState.Vacuum, AerodynamicModel.None).Simulate(climbing, limits);
            Assert.AreEqual(FlightEnd.ReachedMaxDuration, flight.End);
            Assert.AreEqual(0.5, flight.Final.Time, 1e-12);
        }

        [Test]
        public void GyroSignDoesNotChangeReleaseLiftButMattersOnceThePathCurves()
        {
            // At release the gyro component is parallel to v and contributes nothing, whatever its sign. During flight
            // ω stays fixed while v turns downward, so part of the gyro component becomes transverse spin; its sign
            // then decides whether that adds to or subtracts from the break.
            PitchInput plus = PitchPresets.Slider;
            plus.GyroAngleDegrees = 60.0;
            PitchInput minus = plus;
            minus.GyroAngleDegrees = -60.0;
            BallState a = plus.ToInitialState();
            BallState b = minus.ToInitialState();
            var simulator = new BallFlightSimulator(BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.Baseball);
            Assert.AreEqual(0.0, (simulator.MagnusForce(a.Velocity, a.Spin) - simulator.MagnusForce(b.Velocity, b.Spin)).Length, 1e-12);

            double difference = Units.MetersToInches(Math.Abs(Run(plus).HorizontalMovement - Run(minus).HorizontalMovement));
            Assert.That(difference, Is.InRange(0.1, 2.0), "small but real effect of path curvature on gyro spin");
        }

        [Test]
        public void FourSeamAxisBreaksTowardOneOClockFromThePitchersView()
        {
            // θ = 210° → Magnus direction (sin θ, −cos θ) = (−0.5, +0.87): up and to −X (third base, right-hander's arm side).
            PitchMetrics metrics = Run(PureTransverse(210.0));
            double angle = Units.RadiansToDegrees(Math.Atan2(metrics.HorizontalMovement, metrics.VerticalMovement));
            Assert.AreEqual(-30.0, angle, 3.0);
        }

        [Test]
        public void SelectedTimeStepConvergesToFineReference()
        {
            foreach (PitchInput input in PitchPresets.All)
            {
                BallState release = input.ToInitialState();
                PitchMetrics chosen = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.Baseball).Metrics;
                PitchMetrics fine = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.Baseball, 1e-4).Metrics;

                // Budget: 1 µm at the plate, 1 µs of flight time, 1e-5 m/s of speed — far below tracking precision.
                Assert.AreEqual(fine.PlateX, chosen.PlateX, 1e-6, input.Label);
                Assert.AreEqual(fine.PlateZ, chosen.PlateZ, 1e-6, input.Label);
                Assert.AreEqual(fine.FlightTime, chosen.FlightTime, 1e-6, input.Label);
                Assert.AreEqual(fine.PlateSpeed, chosen.PlateSpeed, 1e-5, input.Label);
                Assert.AreEqual(fine.HorizontalMovement, chosen.HorizontalMovement, 1e-6, input.Label);
                Assert.AreEqual(fine.VerticalMovement, chosen.VerticalMovement, 1e-6, input.Label);
            }
        }

        [Test]
        public void PlateCrossingIsExactWhenNoStepLandsOnThePlate()
        {
            BallState release = PitchPresets.Slider.ToInitialState();
            PitchMetrics reference = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.Baseball, 1e-4).Metrics;

            // Awkward step sizes put the plate plane at arbitrary points inside a step.
            foreach (double dt in new[] { 0.0037, 0.0071, 0.0113 })
            {
                PitchResult result = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.Baseball, dt);
                Assert.AreEqual(FlightEnd.CrossedStopPlane, result.Flight.End);
                Assert.AreEqual(PitchingGeometry.PlateFrontY, result.Flight.Final.Position.Y, 1e-9, $"dt {dt}");
                Assert.AreEqual(reference.FlightTime, result.Metrics.FlightTime, 1e-6, $"dt {dt}");
                Assert.AreEqual(reference.PlateX, result.Metrics.PlateX, 2e-4, $"dt {dt}");
                Assert.AreEqual(reference.PlateZ, result.Metrics.PlateZ, 2e-4, $"dt {dt}");
            }
        }

        [Test]
        public void PitchThatReachesTheGroundHasNoPlateCrossing()
        {
            PitchInput spiked = PitchPresets.Curveball;
            spiked.VerticalAngleDegrees = -8.0;
            PitchResult result = PitchSimulation.Run(spiked, EnvironmentState.Standard);

            Assert.AreEqual(FlightEnd.ReachedGround, result.Flight.End);
            Assert.AreEqual(BallProperties.Baseball.Radius, result.Flight.Final.Position.Z, 1e-9, "stops when the ball touches the ground");
            Assert.IsFalse(result.Metrics.ReachedPlate);
            Assert.IsNaN(result.Metrics.PlateZ);
        }

        [Test]
        public void PlaybackAtAnyFrameRateMatchesTheAuthoritativeFlight()
        {
            // Presentation advances a clock by frame delta and samples StateAt. Whatever the frame times, displayed
            // states must match a direct integration to that exact time. The 1e-7 m bound needs cubic interpolation:
            // linear interpolation between 5 ms samples would be off by up to h²|a|/8 ≈ 6e-5 m.
            BallState release = PitchPresets.Curveball.ToInitialState();
            var simulator = new BallFlightSimulator(BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.Baseball);
            PitchResult result = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.Baseball);
            var random = new Random(7);

            foreach (double fps in new[] { 24.0, 60.0, 144.0, 0.0 })
            {
                double time = 0.0;
                while (time < result.Flight.Duration)
                {
                    time += fps > 0.0 ? 1.0 / fps : 0.004 + random.NextDouble() * 0.05; // 0 = jittery frame times
                    BallState shown = result.Flight.StateAt(time);
                    BallState exact = IntegrateTo(simulator, release, Math.Min(time, result.Flight.Duration));
                    Assert.AreEqual(0.0, (shown.Position - exact.Position).Length, 1e-7, $"fps {fps}, t {time}");
                    Assert.AreEqual(0.0, (shown.Velocity - exact.Velocity).Length, 1e-3, $"fps {fps}, t {time}");
                }

                Assert.AreEqual(result.Flight.Final.Position, result.Flight.StateAt(time).Position, "playback clamps at the plate crossing");
            }
        }

        private static BallState IntegrateTo(BallFlightSimulator simulator, BallState state, double time)
        {
            while (state.Time + simulator.TimeStep <= time) state = simulator.Step(state, simulator.TimeStep);
            return state.Time < time ? simulator.Step(state, time - state.Time) : state;
        }

        [Test]
        public void PresetsProducePlausibleMlbFlights()
        {
            PitchMetrics fourSeam = Run(PitchPresets.FourSeam);
            Assert.That(fourSeam.FlightTime, Is.InRange(0.38, 0.45), "94.5 mph flight time");
            Assert.That(Units.MetersToInches(fourSeam.VerticalMovement), Is.InRange(14.0, 19.0), "four-seam induced vertical break");
            Assert.That(Units.MetersPerSecondToMph(fourSeam.ReleaseSpeed - fourSeam.PlateSpeed), Is.InRange(6.0, 10.0), "speed lost to drag");

            foreach (PitchInput input in PitchPresets.All)
            {
                PitchMetrics metrics = Run(input);
                Assert.IsTrue(metrics.ReachedPlate, input.Label);
                Assert.Less(Math.Abs(metrics.PlateX), PitchingGeometry.PlateHalfWidth, input.Label);
                Assert.That(metrics.PlateZ, Is.InRange(PitchingGeometry.DefaultZoneBottom, PitchingGeometry.DefaultZoneTop), input.Label);
            }
        }

        [Test]
        public void LabelDoesNotAffectFlight()
        {
            PitchInput a = PitchPresets.Slider;
            PitchInput b = a;
            b.Label = "Four-Seam-like";
            Assert.AreEqual(PitchSimulation.Run(a, EnvironmentState.Standard).Flight.Final.Position,
                PitchSimulation.Run(b, EnvironmentState.Standard).Flight.Final.Position);
        }

        [Test]
        public void UnitConversionsAreCorrect()
        {
            Assert.AreEqual(44.704, Units.MphToMetersPerSecond(100.0), 1e-12);
            Assert.AreEqual(100.0, Units.MetersPerSecondToMph(44.704), 1e-12);
            Assert.AreEqual(2.0 * Math.PI, Units.RpmToRadiansPerSecond(60.0), 1e-12);
            Assert.AreEqual(2400.0, Units.RadiansPerSecondToRpm(Units.RpmToRadiansPerSecond(2400.0)), 1e-9);
            Assert.AreEqual(18.4404, Units.FeetToMeters(60.5), 1e-12);
            Assert.AreEqual(17.0, Units.MetersToInches(Units.InchesToMeters(17.0)), 1e-12);
        }
    }
}
