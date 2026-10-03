using System;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    public class ContactResolverTests
    {
        private static readonly SwingParameters Params = SwingParameters.Default;

        private static HittingPitch Pitch(PitchInput input) => HittingPitch.Create(input, EnvironmentState.Standard, Params.ContactPlaneY);

        /// <summary>Swing timed <paramref name="timingError"/> s late, PCI at the ball's on-time position shifted by (dx, dz).</summary>
        private static ContactResult Swing(HittingPitch pitch, double timingError = 0.0, double pciDx = 0.0, double pciDz = 0.0, SwingParameters? p = null)
        {
            SwingParameters sp = p ?? Params;
            Vector3d ball = pitch.IdealContactState.Position;
            var input = new SwingInput(pitch.IdealContactTime - sp.SwingDuration + timingError, ball.X + pciDx, ball.Z + pciDz);
            return ContactResolver.Resolve(pitch, input, sp);
        }

        private static double Mph(double mps) => Units.MetersPerSecondToMph(mps);

        [Test]
        public void HeadOnContactFollowsCollisionEfficiencyFormula()
        {
            // Vacuum, horizontal 40 m/s pitch, level swing, perfect contact: exit speed = q·v_ball + (1+q)·v_bat
            // (Nathan 2003), straight back up the middle, no spin.
            SwingParameters level = Params;
            level.AttackAngle = 0.0;
            var release = new BallState(0.0, new Vector3d(0.0, 18.0, 0.8), new Vector3d(0.0, -40.0, 0.0), Vector3d.Zero);
            HittingPitch pitch = HittingPitch.Create(release, new EnvironmentState(0.0, 0.0, Vector3d.Zero), level.ContactPlaneY);
            ContactResult r = Swing(pitch, p: level);

            Assert.IsTrue(r.IsContact);
            double q = level.SweetSpotEfficiency;
            Assert.AreEqual(q * 40.0 + (1.0 + q) * level.BatSpeed, r.ExitSpeed, 1e-6);
            Assert.AreEqual(0.0, r.LaunchAngleDegrees, 1e-6);
            Assert.AreEqual(0.0, r.SprayAngleDegrees, 1e-6);
            Assert.AreEqual(0.0, r.BattedBall.Spin.Length, 1e-9);
            Assert.AreEqual(Params.ContactPlaneY, r.BattedBall.Position.Y, 1e-6, "contact at the contact plane when on time");
        }

        [Test]
        public void CentredOnTimeContactHitsHardestAndUpTheMiddle()
        {
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            ContactResult perfect = Swing(pitch);
            Assert.IsTrue(perfect.IsContact);
            Assert.AreEqual(TimingLabel.Good, perfect.Timing);
            Assert.That(Mph(perfect.ExitSpeed), Is.InRange(100.0, 109.0), "below Statcast's squared-up ceiling 1.23·72 + 0.21·94.5 ≈ 108.6 mph");
            Assert.That(perfect.SprayAngleDegrees, Is.InRange(-3.0, 3.0));
            Assert.That(perfect.LaunchAngleDegrees, Is.InRange(5.0, 20.0), "line drive: attack angle + pitch descent");

            foreach (ContactResult worse in new[] { Swing(pitch, -0.008), Swing(pitch, 0.008), Swing(pitch, pciDz: -0.015), Swing(pitch, pciDz: 0.015), Swing(pitch, pciDx: 0.05), Swing(pitch, pciDx: -0.05) })
                Assert.Less(worse.ExitSpeed, perfect.ExitSpeed);
        }

        [Test]
        public void EarlyPullsAndLateGoesOppositeFieldForBothHands()
        {
            HittingPitch pitch = Pitch(PitchPresets.Curveball);
            Assert.Less(Swing(pitch, -0.010).SprayAngleDegrees, -10.0, "right-handed hitter, early → pull to left field (−X)");
            Assert.Greater(Swing(pitch, 0.010).SprayAngleDegrees, 10.0, "late → opposite field");

            SwingParameters lefty = Params;
            lefty.Side = BatterSide.Left;
            Assert.Greater(Swing(pitch, -0.010, p: lefty).SprayAngleDegrees, 10.0, "left-handed hitter pulls to right field");
            Assert.Less(Swing(pitch, 0.010, p: lefty).SprayAngleDegrees, -10.0);
        }

        [Test]
        public void UnderTheBallLiftsWithBackspinAndOverTheBallDrivesItDownWithTopspin()
        {
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            ContactResult centred = Swing(pitch);
            ContactResult under = Swing(pitch, pciDz: -0.0127);   // bat 0.5 in below the ball centre
            ContactResult over = Swing(pitch, pciDz: 0.0254);     // bat 1 in above

            Assert.Greater(under.LaunchAngleDegrees, centred.LaunchAngleDegrees + 8.0);
            Assert.Less(over.LaunchAngleDegrees, 0.0);
            // Magnus direction ω × v: backspin lifts (+Z), topspin pushes down.
            Assert.Greater(Vector3d.Cross(under.BattedBall.Spin, under.BattedBall.Velocity).Z, 0.0);
            Assert.Less(Vector3d.Cross(over.BattedBall.Spin, over.BattedBall.Velocity).Z, 0.0);
            // Spin grows with undercut, in the measured range for fly balls (≈ 2000–4000 rpm at 20–30°; Nathan et al. 2012).
            Assert.That(Units.RadiansPerSecondToRpm(under.BattedBall.Spin.Length), Is.InRange(2000.0, 4500.0));
            Assert.Greater(Swing(pitch, pciDz: -0.025).BattedBall.Spin.Length, under.BattedBall.Spin.Length);
        }

        [Test]
        public void OffSweetSpotLosesExitSpeedFasterTowardTheTip()
        {
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            // Right-handed hitter: barrel tip toward first base (+X). Ball at +X of the sweet spot = toward the tip.
            ContactResult towardTip = Swing(pitch, pciDx: -0.0762);
            ContactResult towardHandle = Swing(pitch, pciDx: 0.0762);
            Assert.Less(towardTip.CollisionEfficiency, towardHandle.CollisionEfficiency);
            Assert.Less(towardHandle.CollisionEfficiency, Params.SweetSpotEfficiency);
            Assert.Less(towardTip.ExitSpeed, towardHandle.ExitSpeed);
        }

        [Test]
        public void SevereMissesDoNotMakeContact()
        {
            HittingPitch pitch = Pitch(PitchPresets.Slider);
            Assert.AreEqual(ContactOutcome.MissTiming, Swing(pitch, -0.050).Outcome);
            Assert.AreEqual(ContactOutcome.MissTiming, Swing(pitch, 0.050).Outcome);
            Assert.AreEqual(ContactOutcome.MissUnder, Swing(pitch, pciDz: -0.10).Outcome, "PCI far below the ball: bat passes under");
            Assert.AreEqual(ContactOutcome.MissOver, Swing(pitch, pciDz: 0.10).Outcome);
            Assert.AreEqual(ContactOutcome.MissOffBarrel, Swing(pitch, pciDx: 0.20).Outcome);
        }

        [TestCase(double.NaN, 0.0, 0.7), TestCase(0.25, double.NaN, 0.7), TestCase(0.25, 0.0, double.NaN),
         TestCase(double.PositiveInfinity, 0.0, 0.7), TestCase(0.25, double.NegativeInfinity, 0.7), TestCase(0.25, 0.0, double.PositiveInfinity)]
        public void NonFiniteSwingInputIsNeverContact(double start, double pciX, double pciZ)
        {
            ContactResult r = ContactResolver.Resolve(Pitch(PitchPresets.FourSeam), new SwingInput(start, pciX, pciZ), Params);
            Assert.AreEqual(ContactOutcome.InvalidInput, r.Outcome);
        }

        [Test]
        public void SwingAfterTheBallHasLandedIsAMiss()
        {
            // Crosses the contact plane but lands before 1 m behind the plate: a late swing must not hit the resting ball.
            var release = new BallState(0.0, new Vector3d(0.0, 3.0, 0.25), new Vector3d(0.0, -38.0, -3.0), Vector3d.Zero);
            HittingPitch pitch = HittingPitch.Create(release, EnvironmentState.Standard, Params.ContactPlaneY);
            Assert.IsTrue(pitch.ReachesContactPlane);
            Assert.AreEqual(FlightEnd.ReachedGround, pitch.Flight.End);
            double afterLanding = pitch.Flight.Final.Time + 0.002 - pitch.IdealContactTime;
            Assert.Less(afterLanding, Params.MaxTimingError, "inside the timing window");
            Assert.AreEqual(ContactOutcome.MissTiming, Swing(pitch, afterLanding).Outcome);
        }

        [Test]
        public void InvalidSwingParametersAreRejected()
        {
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            void Expect(Func<SwingParameters, SwingParameters> mutate) =>
                Assert.Throws<ArgumentException>(() => Swing(pitch, p: mutate(Params)));
            Expect(p => { p.BatSpeed = -10.0; return p; });
            Expect(p => { p.BatSpeed = double.NaN; return p; });
            Expect(p => { p.BarrelRadius = 0.0; return p; });
            Expect(p => { p.SwingDuration = double.NaN; return p; });
            Expect(p => { p.Friction = -0.1; return p; });
            Expect(p => { p.SprayRate = 100.0; return p; });   // would yaw the bat past 90° inside the window
            Expect(p => { p.SweetSpotEfficiency = 1.5; return p; });
            Expect(p => { p.AttackAngle = Math.PI; return p; });

            SwingParameters instant = Params;
            instant.SwingDuration = 0.0;
            Assert.IsTrue(Swing(pitch, p: instant).IsContact, "zero swing duration is valid (swing starts at contact)");
        }

        [Test]
        public void TimingWindowBoundary()
        {
            HittingPitch pitch = Pitch(PitchPresets.Curveball);
            double edge = Params.MaxTimingError;
            Assert.AreNotEqual(ContactOutcome.MissTiming, Swing(pitch, -(edge - 1e-9)).Outcome);
            Assert.AreNotEqual(ContactOutcome.MissTiming, Swing(pitch, edge - 1e-9).Outcome);
            Assert.AreEqual(ContactOutcome.MissTiming, Swing(pitch, -(edge + 1e-9)).Outcome);
            Assert.AreEqual(ContactOutcome.MissTiming, Swing(pitch, edge + 1e-9).Outcome);
        }

        [Test]
        public void VerticalMissBoundaryAndGrazingContact()
        {
            // On time and unyawed, the bat-frame vertical offset is the world height difference × cos(attack angle).
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            double reach = (BallProperties.Baseball.Radius + Params.BarrelRadius) / Math.Cos(Params.AttackAngle);
            Assert.AreEqual(ContactOutcome.MissUnder, Swing(pitch, pciDz: -reach * (1.0 + 1e-6)).Outcome);
            // Just inside, the bat only grazes the bottom of a ball that is moving away from it along the normal.
            ContactResult grazeUnder = Swing(pitch, pciDz: -reach * (1.0 - 1e-6));
            Assert.That(grazeUnder.Outcome, Is.EqualTo(ContactOutcome.MissGlancing).Or.EqualTo(ContactOutcome.Contact));
            Assert.AreEqual(ContactOutcome.MissOver, Swing(pitch, pciDz: reach * (1.0 + 1e-6)).Outcome);
            ContactResult grazeOver = Swing(pitch, pciDz: reach * (1.0 - 1e-6));
            // A grazing hit over the top barely changes the ball's path (it keeps going toward the catcher), so no
            // spin-direction expectation applies; it must still never be NaN.
            if (grazeOver.IsContact) Assert.IsFalse(double.IsNaN(grazeOver.BattedBall.Spin.Length));
        }

        [Test]
        public void BarrelEdgeStillMakesContactAndTipEfficiencyClampsAtZero()
        {
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            double h = Params.BarrelHalfLength * (1.0 - 1e-9);
            ContactResult tipEdge = Swing(pitch, pciDx: -h);     // ball 5 in toward the tip (+X) for a right-handed hitter
            Assert.IsTrue(tipEdge.IsContact);
            Assert.AreEqual(0.0, tipEdge.CollisionEfficiency, "0.21 − 0.012/in²·(5 in)² < 0 → clamped");
            Assert.Greater(tipEdge.ExitSpeed, 0.0);
            ContactResult handleEdge = Swing(pitch, pciDx: h);
            Assert.AreEqual(Params.SweetSpotEfficiency - 0.003 * 25.0, handleEdge.CollisionEfficiency, 1e-6);
        }

        [Test]
        public void LeftHandedBarrelTipPointsTowardThirdBase()
        {
            SwingParameters lefty = Params;
            lefty.Side = BatterSide.Left;
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            ContactResult towardTip = Swing(pitch, pciDx: 0.0762, p: lefty);      // ball 3 in toward −X
            ContactResult towardHandle = Swing(pitch, pciDx: -0.0762, p: lefty);
            Assert.Less(towardTip.CollisionEfficiency, towardHandle.CollisionEfficiency);
        }

        [TestCase(1.0, TestName = "Oblique 30° collision, tangential impulse below friction limit")]
        [TestCase(0.05, TestName = "Oblique 30° collision, friction-limited")]
        public void ObliqueCollisionMatchesHandDerivation(double friction)
        {
            // Vacuum, level swing, ball at 40 m/s along −Y, bat under the ball so the line of centres is 30° above
            // horizontal. Expected values derived by hand in scalar form (Docs/HITTING.md): n = (0, cos30, sin30),
            // t̂ = (0, −sin30, cos30), slip = (40 + V)·sin30, normal impulse = (1 + q)(40 + V)·cos30.
            SwingParameters p = Params;
            p.AttackAngle = 0.0;
            p.Friction = friction;
            var release = new BallState(0.0, new Vector3d(0.0, 18.0, 0.8), new Vector3d(0.0, -40.0, 0.0), Vector3d.Zero);
            HittingPitch pitch = HittingPitch.Create(release, new EnvironmentState(0.0, 0.0, Vector3d.Zero), p.ContactPlaneY);
            double centres = BallProperties.Baseball.Radius + p.BarrelRadius;
            ContactResult r = Swing(pitch, pciDz: -0.5 * centres, p: p);

            double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6), v = 40.0, V = p.BatSpeed, q = p.SweetSpotEfficiency;
            double outNormal = q * v * c + (1 + q) * V * c;
            double slip = (v + V) * s;
            double j = Math.Min(2.0 / 7.0 * (1 + p.TangentialRestitution) / (1 + p.TangentialRecoil) * slip, friction * (1 + q) * (v + V) * c);
            // Ball velocity = normal part + bat tangential part (V·s along (0, s, −c)) + relative tangential (slip along t̂) − J·t̂.
            double vy = outNormal * c + V * s * s + (slip - j) * -s;
            double vz = outNormal * s - V * s * c + (slip - j) * c;
            Assert.AreEqual(ContactOutcome.Contact, r.Outcome);
            Assert.AreEqual(0.0, r.BattedBall.Velocity.X, 1e-9);
            Assert.AreEqual(vy, r.BattedBall.Velocity.Y, 1e-9);
            Assert.AreEqual(vz, r.BattedBall.Velocity.Z, 1e-9);
            Assert.AreEqual(2.5 * j / BallProperties.Baseball.Radius, r.BattedBall.Spin.X, 1e-6, "backspin about +X");
            Assert.AreEqual(0.0, r.BattedBall.Spin.Y, 1e-9);
            Assert.AreEqual(0.0, r.BattedBall.Spin.Z, 1e-9);
        }

        [Test]
        public void SprayGrowsMonotonicallyWithTimingAndIsSymmetricInVacuum()
        {
            SwingParameters level = Params;
            level.AttackAngle = 0.0;
            var release = new BallState(0.0, new Vector3d(0.0, 18.0, 0.8), new Vector3d(0.0, -40.0, 0.0), Vector3d.Zero);
            HittingPitch pitch = HittingPitch.Create(release, new EnvironmentState(0.0, 0.0, Vector3d.Zero), level.ContactPlaneY);
            double previous = double.NegativeInfinity;
            for (int ms = -30; ms <= 30; ms += 2)
            {
                double spray = Swing(pitch, ms * 0.001, p: level).SprayAngleDegrees;
                Assert.Greater(spray, previous, $"{ms} ms");
                previous = spray;
                Assert.AreEqual(-spray, Swing(pitch, -ms * 0.001, p: level).SprayAngleDegrees, 1e-9, "early/late mirror");
            }

            // 10 ms late → bat yawed 12° (1.2°/ms, rad/s units); the ball leaves a little further out than the bat face.
            double late = Swing(pitch, 0.010, p: level).SprayAngleDegrees;
            Assert.That(late, Is.InRange(12.0, 25.0));
        }

        [Test]
        public void GoldenValueForAFixedSwing()
        {
            // Pins the current model output for one swing to catch accidental formula changes (regression, not physics).
            ContactResult r = Swing(Pitch(PitchPresets.FourSeam), 0.004, 0.01, -0.006);
            Assert.AreEqual(GoldenExitSpeed, r.ExitSpeed, 1e-6);
            Assert.AreEqual(GoldenLaunch, r.LaunchAngleDegrees, 1e-6);
            Assert.AreEqual(GoldenSpray, r.SprayAngleDegrees, 1e-6);
            Assert.AreEqual(GoldenSpin, r.BattedBall.Spin.Length, 1e-4);
        }

        private const double GoldenExitSpeed = 45.396488151749104, GoldenLaunch = 33.756741897670821, GoldenSpray = 8.7799197417013719, GoldenSpin = 529.61004932374942;

        [Test]
        public void PitchThatNeverReachesTheContactPlaneCannotBeHit()
        {
            PitchInput spiked = PitchPresets.Curveball;
            spiked.VerticalAngleDegrees = -8.0;
            HittingPitch pitch = Pitch(spiked);
            Assert.IsFalse(pitch.ReachesContactPlane);
            Assert.AreEqual(ContactOutcome.NoPitch, ContactResolver.Resolve(pitch, new SwingInput(0.3, 0.0, 0.7), Params).Outcome);
        }

        [Test]
        public void SameSwingGivesBitIdenticalResult()
        {
            HittingPitch a = Pitch(PitchPresets.Changeup);
            HittingPitch b = Pitch(PitchPresets.Changeup);
            ContactResult ra = Swing(a, 0.004, 0.01, -0.006);
            ContactResult rb = Swing(b, 0.004, 0.01, -0.006);
            Assert.AreEqual(ra.BattedBall.Velocity, rb.BattedBall.Velocity);
            Assert.AreEqual(ra.BattedBall.Spin, rb.BattedBall.Spin);
            Assert.AreEqual(ra.BattedBall.Position, rb.BattedBall.Position);
        }

        [Test]
        public void OutcomeVariesContinuouslyWithSwingTimeNotWithFrames()
        {
            // The resolver takes a continuous swing time (from input-event timestamps), not a frame index: outcomes move
            // smoothly across integrator sample boundaries, with no quantization to 5 ms steps or frame intervals.
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            double previous = double.NaN;
            for (int i = -40; i <= 40; i++)
            {
                double exit = Swing(pitch, i * 0.0001).ExitSpeed;   // 0.1 ms increments
                if (!double.IsNaN(previous)) Assert.Less(Math.Abs(exit - previous), 0.05, $"jump at {i * 0.1} ms");
                previous = exit;
            }
        }

        [Test]
        public void TimingIsInSecondsOfSimulationTime()
        {
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            ContactResult r = Swing(pitch, 0.010);
            Assert.AreEqual(0.010, r.TimingError, 1e-12);
            Assert.AreEqual(pitch.IdealContactTime + 0.010, r.BattedBall.Time, 1e-12, "batted ball starts at the contact time");
        }
    }
}
