using System;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    public class ContactResolverTests
    {
        private static readonly SwingParameters Params = SwingParameters.Default;

        private static HittingPitch Pitch(PitchInput input) => HittingPitch.Create(input, EnvironmentState.Standard);

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
            HittingPitch pitch = HittingPitch.Create(release, new EnvironmentState(0.0, 0.0, Vector3d.Zero));
            ContactResult r = Swing(pitch, p: level);

            Assert.IsTrue(r.IsContact);
            double q = level.SweetSpotEfficiency;
            Assert.AreEqual(q * 40.0 + (1.0 + q) * level.BatSpeed, r.ExitSpeed, 1e-6);
            Assert.AreEqual(0.0, r.LaunchAngleDegrees, 1e-6);
            Assert.AreEqual(0.0, r.SprayAngleDegrees, 1e-6);
            Assert.AreEqual(0.0, r.BattedBall.Spin.Length, 1e-9);
            Assert.AreEqual(pitch.ContactPlaneY, r.BattedBall.Position.Y, 1e-6, "contact at the contact plane when on time");
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
            HittingPitch pitch = HittingPitch.Create(release, EnvironmentState.Standard);
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

        [TestCase(1.0, 0.0, TestName = "Oblique 30° collision, tangential impulse below friction limit")]
        [TestCase(0.05, 0.0, TestName = "Oblique 30° collision, friction-limited")]
        [TestCase(1.0, 32.0, TestName = "Oblique 30° collision, barrel tilted 32°")]
        public void ObliqueCollisionMatchesHandDerivation(double friction, double batTiltDegrees)
        {
            // Vacuum, level swing, ball at 40 m/s along −Y, bat under the ball so the line of centres is 30° above
            // horizontal. Expected values derived by hand in scalar form (Docs/HITTING.md): n = (0, cos30, sin30),
            // t̂ = (0, −sin30, cos30), slip = (40 + V)·sin30, normal impulse = (1 + q)(40 + V)·cos30.
            SwingParameters p = Params;
            p.AttackAngle = 0.0;
            p.VerticalBatAngle = Units.DegreesToRadians(batTiltDegrees);
            p.Friction = friction;
            var release = new BallState(0.0, new Vector3d(0.0, 18.0, 0.8), new Vector3d(0.0, -40.0, 0.0), Vector3d.Zero);
            HittingPitch pitch = HittingPitch.Create(release, new EnvironmentState(0.0, 0.0, Vector3d.Zero));
            double centres = BallProperties.Baseball.Radius + p.BarrelRadius;
            ContactResult r = Swing(pitch, pciDz: -0.5 * centres, p: p);

            double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6), v = 40.0, V = p.BatSpeed, q = p.SweetSpotEfficiency;
            double outNormal = q * v * c + (1 + q) * V * c;
            double slip = (v + V) * s;
            double j = Math.Min(2.0 / 7.0 * (1 + p.TangentialRestitution) / (1 + p.TangentialRecoil) * slip, friction * (1 + q) * (v + V) * c);
            // Ball velocity = normal part + bat tangential part (V·s along (0, s, −c)) + relative tangential (slip along t̂) − J·t̂.
            double vy = outNormal * c + V * s * s + (slip - j) * -s;
            double vz = outNormal * s - V * s * c + (slip - j) * c;
            double w = 2.5 * j / BallProperties.Baseball.Radius;
            // Ball and bat both move along Y, so tilting the barrel (tip, +X for a right-handed hitter, down by θ) rotates the
            // whole level-barrel result about Y: +Z (undercut direction) → (sin θ, 0, cos θ), +X (backspin axis) → (cos θ, 0, −sin θ).
            double t = Units.DegreesToRadians(batTiltDegrees);
            Assert.AreEqual(ContactOutcome.Contact, r.Outcome);
            Assert.AreEqual(vz * Math.Sin(t), r.BattedBall.Velocity.X, 1e-9);
            Assert.AreEqual(vy, r.BattedBall.Velocity.Y, 1e-9);
            Assert.AreEqual(vz * Math.Cos(t), r.BattedBall.Velocity.Z, 1e-9);
            Assert.AreEqual(w * Math.Cos(t), r.BattedBall.Spin.X, 1e-6, "backspin about the tilted barrel");
            Assert.AreEqual(0.0, r.BattedBall.Spin.Y, 1e-9);
            Assert.AreEqual(-w * Math.Sin(t), r.BattedBall.Spin.Z, 1e-6);
        }

        // ---- Batted-ball spin (TASK-004.5). Spin components in BattedBallLaunch convention: backspin lifts, + sidespin
        // curves toward +X (right field).

        /// <summary>A pitch that is its own mirror image in X: released on the centre line, pure backspin.</summary>
        private static HittingPitch SymmetricPitch() => HittingPitch.Create(
            new BallState(0.0, new Vector3d(0.0, 16.8, 1.8), new Vector3d(0.0, -38.0, -1.5), new Vector3d(-Units.RpmToRadiansPerSecond(2200.0), 0.0, 0.0)),
            EnvironmentState.Standard);

        private static BattedBallLaunch Spin(ContactResult r) => BattedBallLaunch.FromState(r.BattedBall);

        [TestCase(0.0, 0.0, 0.0), TestCase(0.0, 0.0, -0.0127), TestCase(-0.008, 0.0, -0.0127), TestCase(0.008, 0.0, -0.0127),
         TestCase(0.004, 0.03, -0.006), TestCase(-0.003, -0.02, 0.015)]
        public void LeftAndRightHandedContactAreMirrorImages(double timing, double pciDx, double pciDz)
        {
            // Mirrored swing (other hand, PCI mirrored) on a mirror-symmetric pitch: X velocity, spray and sidespin flip;
            // speed, launch and backspin are identical. No sign table — the geometry (barrel tip side) does it.
            HittingPitch pitch = SymmetricPitch();
            SwingParameters lefty = Params;
            lefty.Side = BatterSide.Left;
            ContactResult r = Swing(pitch, timing, pciDx, pciDz), l = Swing(pitch, timing, -pciDx, pciDz, lefty);
            Assert.IsTrue(r.IsContact && l.IsContact);
            Assert.AreEqual(-r.BattedBall.Velocity.X, l.BattedBall.Velocity.X, 1e-9);
            Assert.AreEqual(r.BattedBall.Velocity.Y, l.BattedBall.Velocity.Y, 1e-9);
            Assert.AreEqual(r.BattedBall.Velocity.Z, l.BattedBall.Velocity.Z, 1e-9);
            // Spin is a pseudovector: under X → −X its X component stays and Y, Z flip (backspin same, sidespin and gyro flip).
            Assert.AreEqual(r.BattedBall.Spin.X, l.BattedBall.Spin.X, 1e-9);
            Assert.AreEqual(-r.BattedBall.Spin.Y, l.BattedBall.Spin.Y, 1e-9);
            Assert.AreEqual(-r.BattedBall.Spin.Z, l.BattedBall.Spin.Z, 1e-9);
            Assert.AreEqual(-Spin(r).SidespinRpm, Spin(l).SidespinRpm, 1e-6);
        }

        [Test]
        public void SidespinFollowsBatTiltAndTiming()
        {
            // Right-handed hitter. Undercut on time: the tilted barrel (tip down toward first base) turns some backspin
            // into slice toward right field. Early (pulled) contact hooks toward left field, late (opposite field) slices
            // more (Nathan, carry-v2 2020). Topped balls get topspin and hook.
            HittingPitch pitch = SymmetricPitch();
            BattedBallLaunch onTime = Spin(Swing(pitch, pciDz: -0.0127)), pulled = Spin(Swing(pitch, -0.010, pciDz: -0.0127)),
                opposite = Spin(Swing(pitch, 0.010, pciDz: -0.0127)), topped = Spin(Swing(pitch, pciDz: 0.02));
            Assert.Greater(onTime.BackspinRpm, 1000.0);
            Assert.Greater(onTime.SidespinRpm, 0.0, "slice");
            Assert.Less(pulled.SprayAngleDegrees, -10.0);
            Assert.Less(pulled.SidespinRpm, 0.0, "pulled fly ball hooks");
            Assert.Greater(opposite.SprayAngleDegrees, 10.0);
            Assert.Greater(opposite.SidespinRpm, onTime.SidespinRpm, "opposite-field fly ball slices more");
            Assert.Less(topped.BackspinRpm, 0.0, "topspin");
            Assert.Less(topped.SidespinRpm, 0.0);

            SwingParameters level = Params;
            level.VerticalBatAngle = 0.0;
            Assert.AreEqual(0.0, Spin(Swing(pitch, pciDz: -0.0127, p: level)).SidespinRpm, 1e-6, "no tilt, no yaw → no sidespin");
        }

        [Test]
        public void SpinChangesContinuouslyWithContactOffset()
        {
            // 0.1 mm steps over ±50 mm of the ±70 mm vertical contact range, including where friction starts to cap the
            // impulse (≈ 25 rpm per step at most; the line-of-centres angle steepens only in the last few mm of grazing).
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            Vector3d previous = default;
            bool first = true;
            int contacts = 0;
            for (int i = -500; i <= 500; i++)
            {
                ContactResult r = Swing(pitch, 0.002, 0.01, i * 0.0001);
                if (!r.IsContact) { first = true; continue; }
                contacts++;
                Assert.IsFalse(double.IsNaN(r.BattedBall.Spin.Length) || double.IsInfinity(r.BattedBall.Spin.Length));
                if (!first) Assert.Less(Units.RadiansPerSecondToRpm((r.BattedBall.Spin - previous).Length), 40.0, $"jump at {i * 0.1} mm");
                previous = r.BattedBall.Spin;
                first = false;
            }

            Assert.AreEqual(1001, contacts, "±50 mm is inside the ±70 mm reach: every step is contact");
        }

        [Test]
        public void BallNeverGainsEnergyInTheBatFrame()
        {
            // On time (no yaw) the bat moves along (0, cos A, sin A). In its frame the ball's kinetic energy (translation +
            // rotation, I = 0.4·m·r²) can only fall: q ≤ 1, e_x ≤ 1, and spin about the line of centres is dropped.
            int contacts = 0;
            foreach (PitchInput input in new[] { PitchPresets.FourSeam, PitchPresets.Curveball, PitchPresets.Slider })
            {
                HittingPitch pitch = Pitch(input);
                Vector3d bat = Params.BatSpeed * new Vector3d(0.0, Math.Cos(Params.AttackAngle), Math.Sin(Params.AttackAngle));
                for (double dz = -0.07; dz <= 0.07; dz += 0.0025)
                for (double dx = -0.12; dx <= 0.12; dx += 0.03)
                {
                    ContactResult r = Swing(pitch, 0.0, dx, dz);
                    if (!r.IsContact) continue;
                    contacts++;
                    Vector3d vin = pitch.IdealContactState.Velocity - bat, vout = r.BattedBall.Velocity - bat;
                    double r2 = Math.Pow(BallProperties.Baseball.Radius, 2);
                    double before = vin.LengthSquared + 0.4 * r2 * pitch.IdealContactState.Spin.LengthSquared;
                    double after = vout.LengthSquared + 0.4 * r2 * r.BattedBall.Spin.LengthSquared;
                    Assert.LessOrEqual(after, before * (1.0 + 1e-12), $"dx {dx} dz {dz}");
                }
            }

            Assert.Greater(contacts, 300);
        }

        [Test]
        public void IncomingBackspinPartlySurvivesAsTopspinRelativeToTheBattedBall()
        {
            // Same swing on the same path, with and without the pitch's 2200 rpm backspin: the pitch's spin keeps its
            // direction, which is topspin for the ball going back out, so the batted ball has less backspin. Rigid
            // clamped bat: (0.4 − e_x)/1.4 of it survives (Nathan et al. 2012, Eq. 3); recoiling bat: 1 − (5/7)(1+e_x)/(1+r_x).
            var start = new BallState(0.0, new Vector3d(0.0, 18.0, 0.8), new Vector3d(0.0, -40.0, 0.0), Vector3d.Zero);
            var spinning = new BallState(0.0, start.Position, start.Velocity, new Vector3d(-Units.RpmToRadiansPerSecond(2200.0), 0.0, 0.0));
            var vacuum = new EnvironmentState(0.0, 0.0, Vector3d.Zero);   // no Magnus: both pitches follow the same path
            SwingParameters level = Params;
            level.AttackAngle = 0.0;
            level.VerticalBatAngle = 0.0;
            double centres = BallProperties.Baseball.Radius + level.BarrelRadius;
            double without = Swing(HittingPitch.Create(start, vacuum), pciDz: -0.3 * centres, p: level).BattedBall.Spin.X;
            double with = Swing(HittingPitch.Create(spinning, vacuum), pciDz: -0.3 * centres, p: level).BattedBall.Spin.X;
            double survives = 1.0 - 5.0 / 7.0 * (1.0 + level.TangentialRestitution) / (1.0 + level.TangentialRecoil);
            Assert.Greater(without, 0.0, "backspin");
            Assert.AreEqual(-survives * Units.RpmToRadiansPerSecond(2200.0), with - without, 1e-6);
        }

        [Test]
        public void ExtremeValidParametersGiveFiniteOutput()
        {
            HittingPitch pitch = Pitch(PitchPresets.Curveball);
            double reach = (BallProperties.Baseball.Radius + Params.BarrelRadius) * 0.999;
            int contacts = 0;
            foreach (double tilt in new[] { -89.0, 0.0, 89.0 })
            foreach (double attack in new[] { -80.0, 80.0, 10.0 })
            foreach (double ex in new[] { 0.0, 1.0 })
            foreach (double friction in new[] { 0.0, 100.0 })
            foreach (double timing in new[] { -(Params.MaxTimingError - 1e-9), 0.0, Params.MaxTimingError - 1e-9 })
            foreach (double dz in new[] { -reach, 0.0, reach })
            foreach (double dx in new[] { -Params.BarrelHalfLength * 0.999, Params.BarrelHalfLength * 0.999 })
            {
                SwingParameters p = Params;
                p.VerticalBatAngle = Units.DegreesToRadians(tilt);
                p.AttackAngle = Units.DegreesToRadians(attack);
                p.TangentialRestitution = ex;
                p.TangentialRecoil = 0.0;
                p.Friction = friction;
                ContactResult r = Swing(pitch, timing, dx, dz, p);
                if (!r.IsContact) continue;
                contacts++;
                Vector3d v = r.BattedBall.Velocity, w = r.BattedBall.Spin;
                Assert.IsTrue(Finite(v.X) && Finite(v.Y) && Finite(v.Z) && Finite(w.X) && Finite(w.Y) && Finite(w.Z), $"tilt {tilt} attack {attack} e_x {ex} μ {friction} t {timing} dz {dz} dx {dx}");
            }

            Assert.Greater(contacts, 50);
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        [Test]
        public void OffCentreContactKeepsTheSpinOfTheSameUndercut()
        {
            // Spin depends on the line of centres and the slip, not on where along the barrel the ball is hit (q only
            // changes the normal impulse, so the friction cap at most lowers it). Real bats recoil more off the sweet
            // spot (b²/I₀ in r_x); not modelled.
            HittingPitch pitch = Pitch(PitchPresets.FourSeam);
            double centred = Swing(pitch, pciDz: -0.0127).BattedBall.Spin.Length;
            foreach (double dx in new[] { -0.0762, 0.0762 })
            {
                BattedBallLaunch l = Spin(Swing(pitch, pciDx: dx, pciDz: -0.0127));
                Assert.Greater(l.BackspinRpm, 1000.0);
                Assert.LessOrEqual(Swing(pitch, pciDx: dx, pciDz: -0.0127).BattedBall.Spin.Length, centred * (1.0 + 1e-9));
            }
        }

        [Test]
        public void SprayGrowsMonotonicallyWithTimingAndIsSymmetricInVacuum()
        {
            SwingParameters level = Params;
            level.AttackAngle = 0.0;
            var release = new BallState(0.0, new Vector3d(0.0, 18.0, 0.8), new Vector3d(0.0, -40.0, 0.0), Vector3d.Zero);
            HittingPitch pitch = HittingPitch.Create(release, new EnvironmentState(0.0, 0.0, Vector3d.Zero));
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

        // TASK-004.5 (e_x 0.30, r_x 0.30, vertical bat angle 32°, incoming spin ⟂ line of centres) re-pinned these.
        private const double GoldenExitSpeed = 45.506727145173578, GoldenLaunch = 28.187673952178166, GoldenSpray = 23.816341936477784, GoldenSpin = 444.99594617101008;

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
        public void ContactPlaneComesFromThePitch()
        {
            // The pitch is the single source of the contact plane: an on-time swing meets the ball on that plane, and the
            // bat height follows the swing plane relative to it.
            var release = new BallState(0.0, new Vector3d(0.0, 18.0, 0.8), new Vector3d(0.0, -40.0, 0.0), Vector3d.Zero);
            var still = new EnvironmentState(0.0, 0.0, Vector3d.Zero);
            HittingPitch near = HittingPitch.Create(release, still);
            HittingPitch farther = HittingPitch.Create(release, still, HittingPitch.DefaultContactPlaneY + 0.3);
            Assert.AreEqual(HittingPitch.DefaultContactPlaneY, near.ContactPlaneY);
            ContactResult a = Swing(near);
            ContactResult b = Swing(farther);
            Assert.AreEqual(near.ContactPlaneY, a.BattedBall.Position.Y, 1e-9);
            Assert.AreEqual(farther.ContactPlaneY, b.BattedBall.Position.Y, 1e-9);
            Assert.AreEqual(a.LaunchAngleDegrees, b.LaunchAngleDegrees, 1e-9, "same geometry relative to each pitch's own plane");
            Assert.Throws<ArgumentOutOfRangeException>(() => HittingPitch.Create(release, still, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => HittingPitch.Create(release, still, HittingPitch.StopBehindPlateY - 0.1), "behind the stop plane");
            Assert.Throws<ArgumentOutOfRangeException>(() => HittingPitch.Create(release, still, release.Position.Y + 0.1), "beyond the release point");
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
