using System;
using NUnit.Framework;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>
    /// Ball–surface impact (TASK-004.7, Docs/SURFACE_PHYSICS.md): measured bounce data, conservation laws of the impulse
    /// model, spin coupling through the contact point, and frame independence.
    /// </summary>
    public class SurfaceImpactTests
    {
        private static readonly BallProperties Ball = BallProperties.Baseball;
        private static readonly Vector3d Up = new Vector3d(0.0, 0.0, 1.0);

        private static BallState Incoming(double speed, double angleDegrees, Vector3d spin)
        {
            double a = Units.DegreesToRadians(angleDegrees);
            return new BallState(0.0, new Vector3d(0.0, 0.0, Ball.Radius), new Vector3d(0.0, speed * Math.Cos(a), -speed * Math.Sin(a)), spin);
        }

        // Pennbounce (Brosnan, McNitt & Schlossberg 2007, Table 5): plot means of R = |v_out|/|v_in|, no incoming spin.
        [TestCase(SurfaceKind.InfieldDirt, 25.0, 40.2, 0.610)]
        [TestCase(SurfaceKind.InfieldDirt, 35.0, 40.2, 0.520)]
        [TestCase(SurfaceKind.InfieldDirt, 25.0, 31.0, 0.550)]
        [TestCase(SurfaceKind.InfieldDirt, 35.0, 31.0, 0.470)]
        [TestCase(SurfaceKind.NaturalGrass, 25.0, 40.2, 0.427)]
        [TestCase(SurfaceKind.NaturalGrass, 35.0, 40.2, 0.280)]
        [TestCase(SurfaceKind.NaturalGrass, 25.0, 31.0, 0.447)]
        [TestCase(SurfaceKind.NaturalGrass, 35.0, 31.0, 0.360)]
        public void SpeedRatioMatchesPennbounce(SurfaceKind surface, double angle, double speed, double measured)
        {
            BallState after = SurfaceImpact.Resolve(Incoming(speed, angle, Vector3d.Zero), Up, BallSurfaceProperties.For(surface), Ball);
            double ratio = after.Velocity.Length / speed;
            TestContext.WriteLine($"{surface} {angle}° {speed} m/s: R {ratio:0.000} (measured {measured:0.000})");
            Assert.AreEqual(measured, ratio, 0.04, "within the plot-to-plot spread of the measurement");
        }

        [Test]
        public void NormalImpactWithoutSpinRestitutesOnlyTheNormalSpeed()
        {
            var s = new BallState(0.0, new Vector3d(0.0, 0.0, Ball.Radius), new Vector3d(0.0, 0.0, -10.0), Vector3d.Zero);
            BallState after = SurfaceImpact.Resolve(s, Up, BallSurfaceProperties.InfieldDirt, Ball);
            Assert.AreEqual(10.0 * BallSurfaceProperties.InfieldDirt.NormalRestitution, after.Velocity.Z, 1e-12);
            Assert.AreEqual(0.0, after.Velocity.X, 1e-12);
            Assert.AreEqual(0.0, after.Velocity.Y, 1e-12);
            Assert.AreEqual(0.0, after.Spin.Length, 1e-12);
        }

        [Test]
        public void AngularMomentumAboutTheContactPointIsConservedWithoutPlow()
        {
            // Normal and friction impulses both act through the contact point; the plow (κ) is a centre-of-mass loss, so
            // it is off here. L = I ω + m (R n) × v.
            var surface = new BallSurfaceProperties(SurfaceKind.InfieldDirt, 0.4, 0.5, 0.25, 0.0, 0.5, 0.1);
            foreach (var spin in new[] { Vector3d.Zero, new Vector3d(200.0, 0.0, 0.0), new Vector3d(-200.0, 50.0, 30.0) })
                foreach (double angle in new[] { 10.0, 30.0, 60.0 })
                {
                    BallState before = Incoming(30.0, angle, spin);
                    BallState after = SurfaceImpact.Resolve(before, Up, surface, Ball);
                    Vector3d L(BallState s) => SurfaceImpact.InertiaFactor * Ball.Mass * Ball.Radius * Ball.Radius * s.Spin + Ball.Mass * Vector3d.Cross(Ball.Radius * Up, s.Velocity);
                    Assert.AreEqual(0.0, (L(after) - L(before)).Length, 1e-9, $"spin {spin}, {angle}°");
                }
        }

        [Test]
        public void FrictionNeverReversesSlipBeyondTheGripRestitutionAndNeverAddsEnergy()
        {
            foreach (SurfaceKind kind in new[] { SurfaceKind.InfieldDirt, SurfaceKind.NaturalGrass, SurfaceKind.WarningTrack, SurfaceKind.Wall })
                foreach (double angle in new[] { 5.0, 20.0, 45.0, 80.0 })
                    foreach (double w in new[] { -300.0, 0.0, 300.0 })
                    {
                        BallState before = Incoming(35.0, angle, new Vector3d(w, 0.0, 0.0));
                        BallState after = SurfaceImpact.Resolve(before, Up, BallSurfaceProperties.For(kind), Ball);
                        double E(BallState s) => 0.5 * Ball.Mass * s.Velocity.LengthSquared + 0.5 * SurfaceImpact.InertiaFactor * Ball.Mass * Ball.Radius * Ball.Radius * s.Spin.LengthSquared;
                        Assert.LessOrEqual(E(after), E(before) * (1.0 + 1e-12), $"{kind} {angle}° ω {w}");
                        Assert.Greater(after.Velocity.Z, 0.0, "leaves the surface");
                    }
        }

        [Test]
        public void PlowNeverReversesOrOverSpinsTheBall()
        {
            // The plow acts first and friction on the remaining slip: without incoming spin the ball keeps moving forward
            // and leaves no faster at its contact point than it moves (no hidden spin for the next contact to return).
            foreach (SurfaceKind kind in new[] { SurfaceKind.NaturalGrass, SurfaceKind.InfieldDirt, SurfaceKind.WarningTrack })
                foreach (double angle in new[] { 5.0, 15.0, 25.0, 35.0, 60.0, 80.0 })
                    foreach (double speed in new[] { 5.0, 20.0, 40.0 })
                    {
                        BallState before = Incoming(speed, angle, Vector3d.Zero);
                        BallSurfaceProperties surface = BallSurfaceProperties.For(kind);
                        BallState after = SurfaceImpact.Resolve(before, Up, surface, Ball);
                        Assert.GreaterOrEqual(after.Velocity.Y, -1e-12, $"{kind} {angle}° {speed}: not reversed");
                        // Reversed slip is at most e_t times the incoming slip (grip); the plow adds none.
                        double slip = after.Velocity.Y + Vector3d.Cross(after.Spin, -Ball.Radius * Up).Y;
                        Assert.GreaterOrEqual(slip, -surface.TangentialRestitution * before.Velocity.Y - 1e-9, $"{kind} {angle}° {speed}: no over-spin");
                    }
        }

        [Test]
        public void TopspinBouncesFasterAndLowerThanBackspin()
        {
            // Ball moving +Y: topspin is ω along −X (top surface moving forward; ω × v points down), backspin along +X. At a steep incidence the ball
            // grips (not sliding throughout), so the spin changes the bounce: topspin keeps more forward speed and leaves
            // lower. (At shallow incidence it slides throughout and only the spin, not the speed, differs.)
            BallState top = SurfaceImpact.Resolve(Incoming(20.0, 55.0, new Vector3d(-150.0, 0.0, 0.0)), Up, BallSurfaceProperties.InfieldDirt, Ball);
            BallState back = SurfaceImpact.Resolve(Incoming(20.0, 55.0, new Vector3d(150.0, 0.0, 0.0)), Up, BallSurfaceProperties.InfieldDirt, Ball);
            Assert.Greater(top.Velocity.Y, back.Velocity.Y + 1.0);
            Assert.Less(Math.Atan2(top.Velocity.Z, top.Velocity.Y), Math.Atan2(back.Velocity.Z, back.Velocity.Y));
        }

        [Test]
        public void ImpactIsFrameIndependent()
        {
            // The same impact on a wall (normal along −Y) is the ground impact rotated by −90° about X.
            BallState ground = Incoming(25.0, 30.0, new Vector3d(80.0, -40.0, 60.0));
            BallState g = SurfaceImpact.Resolve(ground, Up, BallSurfaceProperties.Wall, Ball);
            Vector3d R(Vector3d v) => new Vector3d(v.X, v.Z, -v.Y);   // rotation taking +Z to −Y… (x, y, z) → (x, z, −y)
            var wall = new BallState(0.0, R(ground.Position), R(ground.Velocity), R(ground.Spin));
            BallState w = SurfaceImpact.Resolve(wall, R(Up), BallSurfaceProperties.Wall, Ball);
            Assert.AreEqual(0.0, (R(g.Velocity) - w.Velocity).Length, 1e-9);
            Assert.AreEqual(0.0, (R(g.Spin) - w.Spin).Length, 1e-9);
        }

        [Test]
        public void NoImpulseWhenMovingAwayFromTheSurface()
        {
            var s = new BallState(0.0, new Vector3d(0.0, 0.0, Ball.Radius), new Vector3d(0.0, 10.0, 2.0), new Vector3d(100.0, 0.0, 0.0));
            BallState after = SurfaceImpact.Resolve(s, Up, BallSurfaceProperties.NaturalGrass, Ball);
            Assert.AreEqual(s.Velocity, after.Velocity);
            Assert.AreEqual(s.Spin, after.Spin);
        }
    }

    /// <summary>Field geometry used by the ball-in-play simulation.</summary>
    public class FieldLayoutTests
    {
        private const double Ft = 0.3048;
        private static readonly FieldLayout Field = FieldLayout.Standard;

        private static (double X, double Y) At(double feet, double sprayDegrees)
        {
            double a = Units.DegreesToRadians(sprayDegrees);
            return (feet * Ft * Math.Sin(a), feet * Ft * Math.Cos(a));
        }

        [Test]
        public void SurfacesMatchTheDiamond()
        {
            Assert.AreEqual(SurfaceKind.InfieldDirt, Field.SurfaceAt(0.0, 1.0), "home circle");
            Assert.AreEqual(SurfaceKind.InfieldDirt, Field.SurfaceAt(0.0, 59.0 * Ft), "mound");
            Assert.AreEqual(SurfaceKind.NaturalGrass, Field.SurfaceAt(0.0, 40.0 * Ft), "infield grass");
            (double x, double y) = At(90.0, 45.0);
            Assert.AreEqual(SurfaceKind.InfieldDirt, Field.SurfaceAt(x, y), "first base");
            Assert.AreEqual(SurfaceKind.InfieldDirt, Field.SurfaceAt(0.0, 127.0 * Ft), "second base");
            (x, y) = At(132.0, 0.0);
            Assert.AreEqual(SurfaceKind.InfieldDirt, Field.SurfaceAt(x, y), "skin just behind second base");
            (x, y) = At(140.0, 0.0);
            Assert.AreEqual(SurfaceKind.NaturalGrass, Field.SurfaceAt(x, y), "outfield grass beyond the skin");
            (x, y) = At(250.0, 10.0);
            Assert.AreEqual(SurfaceKind.NaturalGrass, Field.SurfaceAt(x, y), "outfield");
            (x, y) = At(390.0, 0.0);
            Assert.AreEqual(SurfaceKind.WarningTrack, Field.SurfaceAt(x, y), "warning track in centre");
            (x, y) = At(375.0, 0.0);
            Assert.AreEqual(SurfaceKind.NaturalGrass, Field.SurfaceAt(x, y), "grass just in front of the track");
            (x, y) = At(100.0, 60.0);
            Assert.AreEqual(SurfaceKind.NaturalGrass, Field.SurfaceAt(x, y), "foul territory");
        }

        [Test]
        public void FenceDistancesAndNormal()
        {
            foreach (var (spray, feet) in new[] { (-45.0, 330.0), (-22.5, 375.0), (0.0, 400.0), (22.5, 375.0), (45.0, 330.0) })
            {
                (double x, double y) = At(feet, spray);
                Assert.AreEqual(0.0, Field.DistanceBeyondFence(x, y, out Vector3d n), 1e-9, $"{spray}°");
                Assert.Less(Vector3d.Dot(n, new Vector3d(x, y, 0.0)), 0.0, "normal points back toward home");
                Assert.AreEqual(1.0, n.Length, 1e-12);
            }

            (double cx, double cy) = At(380.0, 0.0);
            double inside = Field.DistanceBeyondFence(cx, cy, out _);
            Assert.Less(inside, -5.0, "20 ft short of the 400 ft mark is ≈ 5.7 m inside the (angled) fence face");
            Assert.Greater(inside, -20.0 * Ft - 1e-9);
            (cx, cy) = At(420.0, 0.0);
            Assert.Greater(Field.DistanceBeyondFence(cx, cy, out _), 0.0, "beyond is positive");
            Assert.AreEqual(8.0 * Ft, Field.WallHeight, 1e-12);
        }
    }

    /// <summary>
    /// Event-driven ball in play (TASK-004.7): airborne → bounces → slide → roll → rest, walls, queries, and the carry
    /// staying identical to the airborne-only simulation.
    /// </summary>
    public class BallInPlayTests
    {
        private static readonly FieldLayout Field = FieldLayout.Standard;
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);

        private static BallState Launch(double mph, double launch, double spray, double backspin, double sidespin = 0.0) =>
            new BattedBallLaunch(mph, launch, spray, backspin, sidespin).ToState(Contact);

        private static BallInPlay Run(BallState s) => BallInPlaySimulation.Run(s, EnvironmentState.Standard, Field);

        [Test]
        public void CarryIsIdenticalToTheAirborneOnlyFlight()
        {
            BallState s = Launch(95.0, 25.0, 5.0, 2200.0);
            BattedBallResult airborne = BattedBallSimulation.Run(s, EnvironmentState.Standard);
            BallInPlay play = Run(s);
            BallEvent first = play.FirstGroundContact.Value;
            Assert.AreEqual(airborne.Flight.Final.Time, first.Time, 0.0, "bit-identical landing time");
            Assert.AreEqual(airborne.Metrics.Distance, play.CarryDistance, 0.0, "carry = existing landing distance");
            Assert.Greater(play.FinalDistance, play.CarryDistance, "it bounces and rolls on");
            Assert.IsFalse(play.ReachedFenceInTheAir);
        }

        [Test]
        public void GroundBallBouncesSlidesRollsAndStops()
        {
            BallInPlay play = Run(Launch(90.0, -8.0, -10.0, -1200.0));   // topspin grounder toward short
            Assert.AreEqual(BallPhase.Rest, play.EndPhase);
            int bounces = 0;
            bool rolled = false;
            double previous = double.NegativeInfinity;
            foreach (BallEvent e in play.Events)
            {
                Assert.GreaterOrEqual(e.Time, previous, "events in time order");
                previous = e.Time;
                if (e.Kind == BallEventKind.GroundImpact) bounces++;
                if (e.Kind == BallEventKind.SlideToRoll) rolled = true;
            }

            Assert.GreaterOrEqual(bounces, 2);
            Assert.IsTrue(rolled, "slides into rolling");
            Assert.AreEqual(BallEventKind.Rest, play.Events[play.Events.Count - 1].Kind);
            Assert.AreEqual(0.0, play.Final.Velocity.Length, 0.0);
            Assert.AreEqual(BallProperties.Baseball.Radius, play.Final.Position.Z, 1e-9, "rests on the ground");
            Assert.AreEqual(BallPhase.Rest, play.PhaseAt(play.EndTime + 1.0));
            TestContext.WriteLine($"carry {play.CarryDistance:0.0} m, rest at {play.FinalDistance:0.0} m after {play.EndTime:0.00} s, {bounces} bounces");
        }

        [Test]
        public void TrajectoryIsContinuousAcrossEventsAndQueriesAgree()
        {
            BallInPlay play = Run(Launch(80.0, 5.0, 20.0, 800.0));
            for (int i = 1; i < play.Segments.Count; i++)
            {
                BallState end = play.Segments[i - 1].Path.Final, start = play.Segments[i].Path.First;
                Assert.AreEqual(end.Time, start.Time, 1e-12, $"segment {i} starts where {i - 1} ends");
                Assert.Less((end.Position - start.Position).Length, 1e-6, $"position continuous at segment {i}");
            }

            for (double t = play.First.Time; t <= play.EndTime; t += 0.01)
            {
                BallState s = play.StateAt(t);
                Assert.GreaterOrEqual(s.Position.Z, BallProperties.Baseball.Radius - 1e-6, $"never below the ground at {t:0.00}");
                Assert.IsFalse(double.IsNaN(s.Position.X + s.Velocity.X + s.Spin.X));
            }
        }

        [Test]
        public void SlidingBallStartsRollingAtFiveSeventhsAndStopsAtTheRollingDistance()
        {
            // In vacuum on dirt: no spin, sliding at v0 → rolling at v0/(1 + α) = (5/7)·v0 after 2·v0/(7·μ·g); then
            // constant rolling deceleration (5/7)·μ_r·g to a stop at v²/(2a).
            double v0 = 3.0, g = EnvironmentState.Vacuum.Gravity;
            // On the base path from first toward second base (in the dirt band of the diamond).
            var dir = new Vector3d(-1.0, 1.0, 0.0).Normalized;
            var first = new Vector3d(FieldLayout.BaseDistance / Math.Sqrt(2.0), FieldLayout.BaseDistance / Math.Sqrt(2.0), 0.0);
            var p0 = first + 2.0 * dir;
            var start = new BallState(0.0, new Vector3d(p0.X, p0.Y, BallProperties.Baseball.Radius + 1e-6), v0 * dir + new Vector3d(0.0, 0.0, -0.01), Vector3d.Zero);
            BallInPlay play = BallInPlaySimulation.Run(start, EnvironmentState.Vacuum, Field, AerodynamicModel.None);
            BallSurfaceProperties dirt = BallSurfaceProperties.InfieldDirt;
            Assert.AreEqual(SurfaceKind.InfieldDirt, Field.SurfaceAt(p0.X, p0.Y));
            BallEvent roll = Array.Find(ToArray(play), e => e.Kind == BallEventKind.SlideToRoll);
            // Coulomb sliding alone gives (5/7)·v0 after 2·v0/(7·μ·g); the turf's b·v resistance, also acting while
            // sliding, takes at most b·v0·t_slide more.
            double v = 5.0 / 7.0 * v0, tSlide = 2.0 * v0 / (7.0 * dirt.Friction * g);
            Assert.LessOrEqual(roll.Before.Velocity.Length, v + 1e-6);
            Assert.GreaterOrEqual(roll.Before.Velocity.Length, v - dirt.RollingDecelerationPerSpeed * v0 * tSlide);
            Assert.AreEqual(tSlide, roll.Time, 0.01);
            v = roll.Before.Velocity.Length;   // the rolling law from the actual roll-start speed
            (double distance, double time) = dirt.RollingStop(v);
            Assert.AreEqual(distance, Vector3d.Dot(play.Final.Position - roll.Before.Position, dir), 0.002, "rolling distance under a₀ + b·v (1 ms steps)");
            Assert.AreEqual(roll.Time + time, play.EndTime, 0.002, "stop time");
        }

        [Test]
        public void RollingStopMatchesTheClosedForm()
        {
            // dv/dt = −(a₀ + b v): s = v/b − (a₀/b²)·ln(1 + b v/a₀), t = ln(1 + b v/a₀)/b; constant law when b = 0.
            var linear = new BallSurfaceProperties(SurfaceKind.NaturalGrass, 0.3, 0.4, 0.0, 0.0, 0.75, 0.15);
            (double s, double t) = linear.RollingStop(10.0);
            Assert.AreEqual(10.0 / 0.15 - 0.75 / (0.15 * 0.15) * Math.Log(1.0 + 1.5 / 0.75), s, 1e-9);
            Assert.AreEqual(Math.Log(3.0) / 0.15, t, 1e-9);
            var constant = new BallSurfaceProperties(SurfaceKind.NaturalGrass, 0.3, 0.4, 0.0, 0.0, 1.0, 0.0);
            Assert.AreEqual(50.0, constant.RollingStop(10.0).Distance, 1e-12);
            Assert.AreEqual(10.0, constant.RollingStop(10.0).Time, 1e-12);
        }

        private static BallEvent[] ToArray(BallInPlay play)
        {
            var events = new BallEvent[play.Events.Count];
            for (int i = 0; i < events.Length; i++) events[i] = play.Events[i];
            return events;
        }

        [Test]
        public void LineDriveOffTheWallComesBackIntoPlay()
        {
            BallInPlay play = Run(Launch(110.0, 14.0, 0.0, 1500.0));
            BallEvent wall = Array.Find(ToArray(play), e => e.Kind == BallEventKind.WallImpact);
            Assert.AreEqual(BallEventKind.WallImpact, wall.Kind, "hits the wall");
            Assert.Less(wall.Before.Position.Z, Field.WallHeight + BallProperties.Baseball.Radius);
            Assert.Greater(Vector3d.Dot(wall.After.Velocity, -wall.Before.Position), 0.0, "rebounds toward home");
            Assert.AreEqual(BallPhase.Rest, play.EndPhase);
            Assert.Less(play.FinalDistance, 400.0 * 0.3048, "rests in the park");
            Assert.IsFalse(play.ClearedFence);
            BallEvent firstGround = play.FirstGroundContact.Value;
            Assert.AreEqual(wall.Time < firstGround.Time, play.ReachedFenceInTheAir, "projected distance reported only if the wall came first");
        }

        [Test]
        public void HighDriveClearsTheFenceAndLeavesPlay()
        {
            BallInPlay play = Run(Launch(112.0, 28.0, 0.0, 2300.0));
            Assert.IsTrue(play.ClearedFence);
            Assert.IsTrue(play.ReachedFenceInTheAir);
            Assert.AreEqual(BallPhase.OutOfPlay, play.EndPhase);
            Assert.Greater(play.FinalDistance, 400.0 * 0.3048);
            Assert.AreEqual(BallEventKind.LeftPlay, play.Events[play.Events.Count - 1].Kind);
        }

        [Test]
        public void BallRollingIntoTheWallBouncesBack()
        {
            var start = new BallState(0.0, new Vector3d(0.0, 380.0 * 0.3048, BallProperties.Baseball.Radius), new Vector3d(0.0, 8.0, 0.0),
                new Vector3d(-8.0 / BallProperties.Baseball.Radius, 0.0, 0.0));   // already rolling toward the 400 ft fence
            BallInPlay play = BallInPlaySimulation.Run(start, EnvironmentState.Standard, Field);
            BallEvent wall = Array.Find(ToArray(play), e => e.Kind == BallEventKind.WallImpact);
            Assert.AreEqual(BallEventKind.WallImpact, wall.Kind);
            Assert.Less(wall.After.Velocity.Y, 0.0, "comes back");
            Assert.AreEqual(BallPhase.Rest, play.EndPhase);
            Assert.Less(play.Final.Position.Y, 400.0 * 0.3048 - BallProperties.Baseball.Radius);
        }

        [Test]
        public void IdenticalInputsGiveIdenticalPlays()
        {
            BallState s = Launch(100.0, 2.0, -25.0, 600.0, 400.0);
            BallInPlay a = Run(s), b = Run(s);
            Assert.AreEqual(a.Events.Count, b.Events.Count);
            Assert.AreEqual(a.EndTime, b.EndTime, 0.0);
            Assert.AreEqual(a.Final.Position, b.Final.Position);
        }

        [Test]
        public void PhaseQueriesFollowTheEvents()
        {
            BallInPlay play = Run(Launch(85.0, -3.0, 15.0, -500.0));
            Assert.AreEqual(BallPhase.Airborne, play.PhaseAt(0.01));
            BallEvent roll = Array.Find(ToArray(play), e => e.Kind == BallEventKind.SlideToRoll);
            Assert.AreEqual(BallPhase.Rolling, play.PhaseAt(roll.Time + 0.01));
            Assert.AreEqual(BallPhase.Rest, play.PhaseAt(play.EndTime));
        }

        [Test]
        public void RollingBallDecelerationIncludesDragUnderTheRollingConstraint()
        {
            // Rolling on grass at 20 m/s with air: a(v) = a₀ + b·v plus F_drag/((1 + α)·m) with the spin-free C_D.
            double v0 = 20.0;
            var start = new BallState(0.0, new Vector3d(0.0, 60.0, BallProperties.Baseball.Radius), new Vector3d(0.0, v0, 0.0),
                new Vector3d(-v0 / BallProperties.Baseball.Radius, 0.0, 0.0));
            BallInPlay play = BallInPlaySimulation.Run(start, EnvironmentState.Standard, Field);
            Assert.AreEqual(BallPhase.Rolling, play.PhaseAt(0.05));
            BallSurfaceProperties grass = BallSurfaceProperties.NaturalGrass;
            var sim = new BallFlightSimulator(BallProperties.Baseball, EnvironmentState.Standard, AerodynamicModel.BattedBall);
            double drag = sim.DragForce(new Vector3d(0.0, v0, 0.0)).Length / BallProperties.Baseball.Mass / (1.0 + SurfaceImpact.InertiaFactor);
            double expected = grass.RollingDeceleration(v0) + drag;
            double measured = (v0 - play.StateAt(0.1).Velocity.Length) / 0.1;
            Assert.AreEqual(expected, measured, 0.05 * expected);
        }

        [Test]
        public void EnergyNeverIncreasesDuringAPlay()
        {
            // Translational + rotational + potential energy is non-increasing through bounces, sliding and rolling (air
            // drag and impacts only remove energy; spin decays). Checked on the recorded samples of every segment.
            double m = BallProperties.Baseball.Mass, I = SurfaceImpact.InertiaFactor * m * BallProperties.Baseball.Radius * BallProperties.Baseball.Radius;
            foreach (BallState s0 in new[] { Launch(90.0, -8.0, -10.0, -1200.0), Launch(100.0, 2.0, -25.0, 600.0), Launch(60.0, -20.0, 5.0, 800.0), Launch(105.0, 14.0, 0.0, 1500.0) })
            {
                BallInPlay play = Run(s0);
                double previous = double.PositiveInfinity;
                foreach (BallSegment segment in play.Segments)
                    foreach (BallState s in segment.Path.Samples)
                    {
                        double e = 0.5 * m * s.Velocity.LengthSquared + 0.5 * I * s.Spin.LengthSquared + m * 9.80665 * s.Position.Z;
                        Assert.LessOrEqual(e, previous + 1e-6, $"energy rose at {s.Time:0.000} s");
                        previous = e;
                    }
            }
        }

        [Test]
        public void GroundSpeedNeverRisesAfterTheLastBounce()
        {
            // Over-spin from a bounce would be returned as speed by sliding friction (physics review): once the ball stays
            // down, its horizontal speed only falls.
            BallInPlay play = Run(Launch(85.0, -6.0, 10.0, 0.0));
            double previous = double.PositiveInfinity;
            foreach (BallSegment segment in play.Segments)
            {
                if (segment.Phase == BallPhase.Airborne) { previous = double.PositiveInfinity; continue; }
                foreach (BallState s in segment.Path.Samples)
                {
                    Assert.LessOrEqual(s.Velocity.Length, previous + 1e-9, $"speed rose at {s.Time:0.000} s");
                    previous = s.Velocity.Length;
                }
            }
        }

        [Test]
        public void FoulBallsMeetNoWall()
        {
            // Far down the line in foul territory: no fence there, so no wall impact and no home run.
            BallInPlay play = Run(Launch(105.0, 20.0, -60.0, 1800.0));
            Assert.IsFalse(Array.Exists(ToArray(play), e => e.Kind == BallEventKind.WallImpact || e.Kind == BallEventKind.ClearedFence));
            Assert.AreEqual(BallPhase.Rest, play.EndPhase);
        }

        private static BallState RollingAt(Vector3d position, Vector3d velocity)
        {
            var p = new Vector3d(position.X, position.Y, BallProperties.Baseball.Radius);
            return new BallState(0.0, p, velocity, Vector3d.Cross(new Vector3d(0.0, 0.0, 1.0), velocity) / BallProperties.Baseball.Radius);
        }

        private static BallInPlay RunVacuum(BallState s) => BallInPlaySimulation.Run(s, EnvironmentState.Vacuum, Field, AerodynamicModel.None);

        [Test]
        public void GrassRollMatchesTheRollingLaw()
        {
            // Rolling on outfield grass in vacuum: rest exactly where a(v) = a₀ + b·v stops it.
            BallState s = RollingAt(new Vector3d(0.0, 70.0, 0.0), new Vector3d(0.0, 6.0, 0.0));
            Assert.AreEqual(SurfaceKind.NaturalGrass, Field.SurfaceAt(0.0, 70.0));
            BallInPlay play = RunVacuum(s);
            (double distance, double time) = BallSurfaceProperties.NaturalGrass.RollingStop(6.0);
            Assert.AreEqual(distance, play.Final.Position.Y - 70.0, 0.01);
            Assert.AreEqual(time, play.EndTime, 0.002);
            Assert.AreEqual(SurfaceKind.NaturalGrass, play.Events[play.Events.Count - 1].Surface);
        }

        [Test]
        public void RollingFromTheSkinOntoTheGrassChangesTheLawAtTheEdge()
        {
            // Behind second base the skin ends at 41.6 m on the centre-field axis: dirt law before, grass law after.
            double edge = FieldLayout.BaseDistance * Math.Sqrt(2.0) / 2.0 + FieldLayout.InfieldDirtSide / Math.Sqrt(2.0);
            BallInPlay play = RunVacuum(RollingAt(new Vector3d(0.0, edge - 1.0, 0.0), new Vector3d(0.0, 5.0, 0.0)));
            double Decel(double y)
            {
                double t0 = 0.0;
                while (play.StateAt(t0).Position.Y < y) t0 += 0.001;
                return (play.StateAt(t0).Velocity.Length - play.StateAt(t0 + 0.02).Velocity.Length) / 0.02;
            }

            double vBefore = 0.0, vAfter = 0.0;
            for (double t = 0.0; t < play.EndTime; t += 0.001)
            {
                BallState st = play.StateAt(t);
                if (st.Position.Y < edge - 0.2) vBefore = st.Velocity.Length;
                if (st.Position.Y > edge + 0.2) { vAfter = st.Velocity.Length; break; }
            }

            Assert.AreEqual(BallSurfaceProperties.InfieldDirt.RollingDeceleration(vBefore), Decel(edge - 0.6), 0.05, "dirt before the edge");
            Assert.AreEqual(BallSurfaceProperties.NaturalGrass.RollingDeceleration(vAfter), Decel(edge + 0.2), 0.05, "grass after it");
            Assert.AreEqual(SurfaceKind.NaturalGrass, play.Events[play.Events.Count - 1].Surface, "rests on the grass");
        }

        [TestCase(0.005, true)]
        [TestCase(-0.005, false)]
        public void WallHeightPlusARadiusDecidesClearingTheFence(double aboveEdge, bool clears)
        {
            // Horizontal 40 m/s at the centre-field fence (vacuum, no aero; the 0.5 m approach drops it < 1 mm).
            double face = 400.0 * 0.3048, r = BallProperties.Baseball.Radius;
            var s = new BallState(0.0, new Vector3d(0.0, face - r - 0.5, Field.WallHeight + r + aboveEdge), new Vector3d(0.0, 40.0, 0.0), Vector3d.Zero);
            BallInPlay play = RunVacuum(s);
            Assert.AreEqual(clears, play.ClearedFence);
            Assert.AreEqual(!clears, Array.Exists(ToArray(play), e => e.Kind == BallEventKind.WallImpact));
        }

        [Test]
        public void FoulGroundBehindTheFenceLineHasNoWall()
        {
            // Rolling from foul ground beyond the left-field pole into fair ground behind the fence (outside the park).
            double a = Units.DegreesToRadians(-48.0), d = 360.0 * 0.3048;
            var p = new Vector3d(d * Math.Sin(a), d * Math.Cos(a), 0.0);
            BallInPlay play = RunVacuum(RollingAt(p, new Vector3d(6.0, 2.0, 0.0)));
            Assert.IsFalse(Array.Exists(ToArray(play), e => e.Kind == BallEventKind.WallImpact));
            Assert.Greater(Field.DistanceBeyondFence(play.Final.Position.X, play.Final.Position.Y, out _), 0.0, "it did cross into fair ground behind the fence");
        }

        [Test]
        public void TimeGuardEndsAnEndlessPlay()
        {
            var s = new BallState(0.0, new Vector3d(0.0, 0.7, 1.0), new Vector3d(0.0, 0.0, 200.0), Vector3d.Zero);
            BallInPlay play = RunVacuum(s);
            Assert.AreEqual(BallPhase.Airborne, play.EndPhase);
            Assert.AreEqual(BallInPlaySimulation.MaxPlayTime, play.EndTime, 1e-9);
        }

        [Test]
        public void TopspinGrounderOutrunsTheSameGrounderWithBackspin()
        {
            // Same impact, opposite spin (vacuum, no aero, so only the ground contact differs): topspin runs further.
            BallInPlay Grounder(double w) => RunVacuum(new BallState(0.0, new Vector3d(0.0, 2.0, 0.3), new Vector3d(0.0, 35.0, -4.0), new Vector3d(w, 0.0, 0.0)));
            BallInPlay top = Grounder(-400.0), back = Grounder(400.0);
            TestContext.WriteLine($"topspin {top.FinalDistance:0.0} m, backspin {back.FinalDistance:0.0} m");
            Assert.Greater(top.FinalDistance, back.FinalDistance);
        }

        [Test]
        public void QueriesAgreeWithEvents()
        {
            BallInPlay play = Run(Launch(80.0, 5.0, 20.0, 800.0));
            foreach (BallEvent e in play.Events)
            {
                if (e.Kind != BallEventKind.GroundImpact && e.Kind != BallEventKind.WallImpact) continue;
                // Post-impulse state at the instant (a bounce that stays down keeps its horizontal velocity; v_z < HopSpeed is dropped).
                Vector3d d = play.StateAt(e.Time).Velocity - e.After.Velocity;
                Assert.Less(new Vector3d(d.X, d.Y, 0.0).Length, 1e-6, $"{e.Kind} at {e.Time:0.000}");
                Assert.LessOrEqual(Math.Abs(d.Z), BallInPlaySimulation.HopSpeed + 1e-9);
            }

            BallEvent roll = Array.Find(ToArray(play), e => e.Kind == BallEventKind.SlideToRoll);
            Assert.Less((play.StateAt(roll.Time - 1e-4).Velocity - play.StateAt(roll.Time + 1e-4).Velocity).Length, 0.01, "velocity continuous at slide → roll");
            Assert.AreEqual(BallPhase.Sliding, play.PhaseAt(roll.Time - 1e-4));
            Assert.AreEqual(play.First.Position, play.StateAt(play.First.Time - 1.0).Position, "clamped before the play");
        }

        [Test]
        public void RollOutDistancesStayInTheirDocumentedBands()
        {
            // Docs/SURFACE_PHYSICS.md sanity values (ASSUMED rolling); bands catch coefficient or drag regressions.
            Assert.AreEqual(254.0, Units.MetersToFeet(Run(Launch(90.0, -8.0, -10.0, -1200.0)).FinalDistance), 15.0, "90 mph grounder");
            Assert.AreEqual(168.0, Units.MetersToFeet(Run(Launch(60.0, -20.0, 5.0, -800.0)).FinalDistance), 15.0, "60 mph chopper");
        }
    }
}
