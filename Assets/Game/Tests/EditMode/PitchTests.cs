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
            Assert.AreEqual(2.0, Units.RadiansToDegrees(Math.Atan2(release.Velocity.X, -release.Velocity.Y)), 1e-9, "azimuth toward +X (first base)");
            double speed = release.Velocity.Length;
            Assert.AreEqual(1.0, Units.RadiansToDegrees(Math.Asin(release.Velocity.Z / speed)), 1e-9, "elevation up");
            Assert.AreEqual(Units.MphToMetersPerSecond(90.0), speed, 1e-12);
        }

        [Test]
        public void SpinInputMapsToStatcastAxisInTheFixedXzPlane()
        {
            PitchInput input = PitchPresets.Slider; // non-zero release angles and a large gyro angle
            BallState release = input.ToInitialState();
            Vector3d spin = release.Spin;

            Assert.AreEqual(Units.RpmToRadiansPerSecond(input.SpinRateRpm), spin.Length, 1e-9, "spin magnitude = rpm·2π/60");
            double projectedAxis = Units.RadiansToDegrees(Math.Atan2(spin.Z, spin.X));
            Assert.AreEqual(input.SpinAxisDegrees, (projectedAxis + 360.0) % 360.0, 1e-9, "X–Z projection has exactly the Statcast angle");
            Assert.AreEqual(Math.Sin(Units.DegreesToRadians(-input.GyroAngleDegrees)), spin.Y / spin.Length, 1e-12, "positive gyro points toward the plate (−Y)");
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
        public void OnlyTransverseSpinBreaksThePitch()
        {
            // 2200 rpm at 60° gyro has the same transverse spin as 1100 rpm of pure backspin, so it should break about
            // the same (within the ~1 in path-curvature effect on gyro spin). Using total spin in S would add > 3 in.
            PitchInput gyro = PureTransverse(180.0);
            gyro.GyroAngleDegrees = 60.0;
            PitchInput halfRate = PureTransverse(180.0);
            halfRate.SpinRateRpm = 1100.0;
            PitchInput bullet = PureTransverse(180.0);
            bullet.GyroAngleDegrees = 90.0;

            Assert.AreEqual(0.5, Run(gyro).SpinEfficiency, 1e-12);
            Assert.AreEqual(Units.MetersToInches(Run(halfRate).VerticalMovement), Units.MetersToInches(Run(gyro).VerticalMovement), 1.0);
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

            double plateTime = (startY - PitchingGeometry.PlateFrontY) / speed;
            foreach (double dt in new[] { 0.005, 0.0113 })
            {
                Assert.AreEqual(Math.Floor(groundTime / dt), Math.Floor(plateTime / dt), $"both events inside one step for dt {dt}");
                PitchResult result = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Vacuum, AerodynamicModel.None, dt);
                Assert.AreEqual(FlightEnd.ReachedGround, result.Flight.End, $"dt {dt}");
                Assert.AreEqual(groundTime, result.Flight.Final.Time, 1e-9, $"dt {dt}");
                Assert.IsFalse(result.Metrics.ReachedPlate, $"dt {dt}");
                Assert.IsNaN(result.Metrics.FlightTime, $"dt {dt}");
                Assert.IsNaN(result.Metrics.VerticalMovement, $"dt {dt}");
            }
        }

        [Test]
        public void PlateCrossingJustBeforeGroundContactReachesThePlate()
        {
            // Mirror case: the plate plane is crossed 2 cm before the ball touches the ground, inside one step.
            double g = EnvironmentState.StandardGravity;
            double r = BallProperties.Baseball.Radius;
            double speed = 40.0;
            double startY = 16.0;
            double groundY = PitchingGeometry.PlateFrontY - 0.02;
            double groundTime = (startY - groundY) / speed;
            double plateTime = (startY - PitchingGeometry.PlateFrontY) / speed;
            var release = new BallState(0.0, new Vector3d(0.0, startY, r + 0.5 * g * groundTime * groundTime),
                new Vector3d(0.0, -speed, 0.0), Vector3d.Zero);

            foreach (double dt in new[] { 0.005, 0.0113 })
            {
                Assert.AreEqual(Math.Floor(groundTime / dt), Math.Floor(plateTime / dt), $"both events inside one step for dt {dt}");
                PitchResult result = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Vacuum, AerodynamicModel.None, dt);
                Assert.AreEqual(FlightEnd.CrossedStopPlane, result.Flight.End, $"dt {dt}");
                Assert.IsTrue(result.Metrics.ReachedPlate, $"dt {dt}");
                Assert.AreEqual(plateTime, result.Metrics.FlightTime, 1e-9, $"dt {dt}");
            }
        }

        [Test]
        public void EventExactlyOnAStepBoundaryIsFoundOnce()
        {
            // Vacuum, horizontal: the plate is reached at exactly 80 steps of 5 ms.
            double plateTime = 80 * BallFlightSimulator.DefaultTimeStep;
            double startY = PitchingGeometry.PlateFrontY + 40.0 * plateTime;
            var release = new BallState(0.0, new Vector3d(0.0, startY, 1.5), new Vector3d(0.0, -40.0, 0.0), Vector3d.Zero);
            PitchResult result = PitchSimulation.Run(release, BallProperties.Baseball, EnvironmentState.Vacuum, AerodynamicModel.None);
            Assert.AreEqual(FlightEnd.CrossedStopPlane, result.Flight.End);
            Assert.AreEqual(plateTime, result.Metrics.FlightTime, 1e-9);
        }

        [Test]
        public void FlightEndsAtMaxDurationWhenNoOtherLimitIsReached()
        {
            var climbing = new BallState(0.0, new Vector3d(0.0, 10.0, 1.0), new Vector3d(0.0, 0.0, 30.0), Vector3d.Zero);
            var limits = new FlightLimits(0.0, 0.0, 0.5);
            TrajectoryResult flight = new BallFlightSimulator(BallProperties.Baseball, EnvironmentState.Vacuum, AerodynamicModel.None).Simulate(climbing, limits);
            Assert.AreEqual(FlightEnd.ReachedMaxDuration, flight.End);
            Assert.AreEqual(0.5, flight.Final.Time, 1e-12);

            // A duration that is not a multiple of the step ends exactly on time, with no drift-induced extra step.
            TrajectoryResult odd = new BallFlightSimulator(BallProperties.Baseball, EnvironmentState.Vacuum, AerodynamicModel.None)
                .Simulate(climbing, new FlightLimits(0.0, 0.0, 0.5023));
            Assert.AreEqual(0.5023, odd.Final.Time, 1e-15);
            Assert.AreEqual(101, odd.Samples.Count - 1, "100 full steps + one 2.3 ms step");
        }

        [Test]
        public void GyroSignOnlyMattersOnceVelocityLeavesTheGyroAxis()
        {
            // Released exactly along −Y, ±gyro spin is parallel to v and gives identical lift. During flight ω stays
            // fixed (physically: precession ≈ 1° per pitch vs a 5–8° turn of v; Nathan, Kagan) while v turns down, so
            // part of the gyro component becomes sidespin whose sign follows the gyro sign — documented in Nathan's
            // gyroball note and by Kagan (~0.5 in per 1500 rpm of gyro). Band: current 1.4 in ± 50 %.
            PitchInput plus = PureTransverse(90.0);
            plus.GyroAngleDegrees = 60.0;
            PitchInput minus = plus;
            minus.GyroAngleDegrees = -60.0;
            BallState a = plus.ToInitialState();
            BallState b = minus.ToInitialState();
            var simulator = new BallFlightSimulator(BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.Baseball);
            Assert.AreEqual(0.0, (simulator.MagnusForce(a.Velocity, a.Spin) - simulator.MagnusForce(b.Velocity, b.Spin)).Length, 1e-12);

            PitchInput sliderPlus = PitchPresets.Slider;
            sliderPlus.GyroAngleDegrees = 60.0;
            PitchInput sliderMinus = sliderPlus;
            sliderMinus.GyroAngleDegrees = -60.0;
            double difference = Units.MetersToInches(Math.Abs(Run(sliderPlus).HorizontalMovement - Run(sliderMinus).HorizontalMovement));
            Assert.That(difference, Is.InRange(0.7, 2.1));
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
                Assert.AreEqual(reference.PlateX, result.Metrics.PlateX, 1e-6, $"dt {dt}");
                Assert.AreEqual(reference.PlateZ, result.Metrics.PlateZ, 1e-6, $"dt {dt}");
                Assert.AreEqual(reference.PlateSpeed, result.Metrics.PlateSpeed, 1e-5, $"dt {dt}");
                Assert.AreEqual(reference.HorizontalMovement, result.Metrics.HorizontalMovement, 1e-6, $"dt {dt}");
                Assert.AreEqual(reference.VerticalMovement, result.Metrics.VerticalMovement, 1e-6, $"dt {dt}");
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

                Assert.AreEqual(result.Flight.Final.Position, result.Flight.StateAt(time + 1.0).Position, "playback clamps at the plate crossing");
            }
        }

        private static BallState IntegrateTo(BallFlightSimulator simulator, BallState state, double time)
        {
            while (state.Time + simulator.TimeStep <= time) state = simulator.Step(state, simulator.TimeStep);
            return state.Time < time ? simulator.Step(state, time - state.Time) : state;
        }

        // [Tune] regression bands (±1.5 in) around the current model output for each preset, so a sign or axis
        // regression in one pitch family is caught. Not external validation: the presets were tuned with this model.
        // Approximate published MLB averages for comparison only: 4-seam +16/−7..8 arm, sinker +7..9/−15,
        // slider +1..2/+5..6, curveball ≈ −10/+8..10, changeup +6/−14 (IVB/HB in; Docs/PHYSICS.md).
        [TestCase("Four-Seam-like", -8.56, 16.99)]
        [TestCase("Sinker-like", -16.13, 8.72)]
        [TestCase("Slider-like", 5.5, 1.5)]
        [TestCase("Curveball-like", 9.0, -10.0)]
        [TestCase("Changeup-like", -15.04, 6.38)]
        public void PresetMovementRegression(string label, double horizontalInches, double verticalInches)
        {
            PitchInput input = Array.Find(PitchPresets.All, p => p.Label == label);
            PitchMetrics metrics = Run(input);
            Assert.AreEqual(horizontalInches, Units.MetersToInches(metrics.HorizontalMovement), 1.5, "HB");
            Assert.AreEqual(verticalInches, Units.MetersToInches(metrics.VerticalMovement), 1.5, "IVB");
            Assert.IsTrue(metrics.ReachedPlate);
            Assert.Less(Math.Abs(metrics.PlateX), PitchingGeometry.PlateHalfWidth, "presets are aimed at the zone");
            Assert.That(metrics.PlateZ, Is.InRange(PitchingGeometry.DefaultZoneBottom, PitchingGeometry.DefaultZoneTop));
            Assert.IsTrue(metrics.WithinSupportedAerodynamicRange, "presets stay inside the data-supported aero range");
        }

        [Test]
        public void SlowHighSpinPitchIsFlaggedOutsideSupportedAeroRange()
        {
            PitchInput eephus = PureTransverse(180.0);
            eephus.SpeedMph = 45.0;
            eephus.VerticalAngleDegrees = 6.0;
            eephus.SpinRateRpm = 3000.0;
            PitchMetrics metrics = Run(eephus);
            Assert.Less(metrics.MinReynolds, AerodynamicModel.MinSupportedReynolds);
            Assert.Greater(metrics.MaxSpinParameter, AerodynamicModel.MaxSupportedSpinParameter);
            Assert.IsFalse(metrics.WithinSupportedAerodynamicRange);
        }

        [Test]
        public void ExtremeButPlayableInputsGiveFiniteResults()
        {
            foreach (double mph in new[] { 40.0, 70.0, 105.0 })
            foreach (double rpm in new[] { 0.0, 1500.0, 3500.0 })
            foreach (double axis in new[] { 0.0, 135.0, 270.0 })
            foreach (double windX in new[] { -15.0, 0.0, 15.0 })
            foreach (double density in new[] { 0.9, 1.3 })
            {
                PitchInput input = PitchPresets.FourSeam;
                input.SpeedMph = mph;
                input.SpinRateRpm = rpm;
                input.SpinAxisDegrees = axis;
                var environment = new EnvironmentState(density, EnvironmentState.StandardGravity, new Vector3d(windX, 0.0, 0.0));
                PitchResult result = PitchSimulation.Run(input, environment);
                foreach (BallState s in result.Flight.Samples)
                    Assert.IsFalse(double.IsNaN(s.Position.Length) || double.IsInfinity(s.Position.Length), $"{mph} mph {rpm} rpm {axis}° wind {windX}");
                Assert.AreNotEqual(FlightEnd.ReachedMaxDuration, result.Flight.End, "every pitch ends at the plate or the ground");
            }
        }

        [Test]
        public void InvalidPitchInputsAreRejected()
        {
            PitchInput nanSpeed = PitchPresets.FourSeam;
            nanSpeed.SpeedMph = double.NaN;
            Assert.Throws<ArgumentOutOfRangeException>(() => nanSpeed.ToInitialState());
            PitchInput negativeSpin = PitchPresets.FourSeam;
            negativeSpin.SpinRateRpm = -1.0;
            Assert.Throws<ArgumentOutOfRangeException>(() => negativeSpin.ToInitialState());
            PitchInput nanAxis = PitchPresets.FourSeam;
            nanAxis.SpinAxisDegrees = double.NaN;
            Assert.Throws<ArgumentException>(() => nanAxis.ToInitialState());
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
            Assert.AreEqual(0.4318, Units.InchesToMeters(17.0), 1e-12);
            Assert.AreEqual(17.0, Units.MetersToInches(0.4318), 1e-12);
            Assert.AreEqual(Math.PI, Units.DegreesToRadians(180.0), 1e-15);
        }
    }
}
