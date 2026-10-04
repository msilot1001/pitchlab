using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Batting
{
    /// <summary>
    /// Batted-ball launch described in baseball terms (Statcast-style), converted to an SI simulation state.
    /// Spray: degrees from straight-away centre field, + toward first base / right field (+X).
    /// Backspin: rpm about the horizontal axis ⟂ to the horizontal launch direction (positive lifts the ball).
    /// Sidespin: rpm about the axis ⟂ to both the launch velocity and the backspin axis (Nathan 2017, Eq. 4; vertical
    /// for a level launch); positive curves the ball toward +X (right field) — the opposite sign of Nathan's ω_s.
    /// </summary>
    public readonly struct BattedBallLaunch
    {
        public readonly double ExitSpeedMph, LaunchAngleDegrees, SprayAngleDegrees, BackspinRpm, SidespinRpm;

        public BattedBallLaunch(double exitSpeedMph, double launchAngleDegrees, double sprayAngleDegrees, double backspinRpm, double sidespinRpm = 0.0)
        {
            ExitSpeedMph = exitSpeedMph;
            LaunchAngleDegrees = launchAngleDegrees;
            SprayAngleDegrees = sprayAngleDegrees;
            BackspinRpm = backspinRpm;
            SidespinRpm = sidespinRpm;
        }

        public BallState ToState(Vector3d contactPosition)
        {
            double speed = Units.MphToMetersPerSecond(ExitSpeedMph);
            double launch = Units.DegreesToRadians(LaunchAngleDegrees);
            double spray = Units.DegreesToRadians(SprayAngleDegrees);
            var horizontal = new Vector3d(Math.Sin(spray), Math.Cos(spray), 0.0);
            Vector3d velocity = speed * (Math.Cos(launch) * horizontal + Math.Sin(launch) * new Vector3d(0.0, 0.0, 1.0));
            // Backspin axis = horizontal direction × up (for a ball moving +Y: +X, so ω × v points up).
            Vector3d backspinAxis = Vector3d.Cross(horizontal, new Vector3d(0.0, 0.0, 1.0));
            // Sidespin axis = backspin axis × velocity direction (Nathan's ω̂_s = ω̂_b × ω̂_g), so all launch spin is transverse.
            Vector3d sidespinAxis = Math.Cos(launch) * new Vector3d(0.0, 0.0, 1.0) - Math.Sin(launch) * horizontal;
            Vector3d spin = Units.RpmToRadiansPerSecond(BackspinRpm) * backspinAxis - Units.RpmToRadiansPerSecond(SidespinRpm) * sidespinAxis;
            return new BallState(0.0, contactPosition, velocity, spin);
        }

        /// <summary>
        /// Inverse of <see cref="ToState"/>: exit speed, angles, and the spin projected on the backspin and sidespin
        /// axes. Spin along the velocity (gyrospin) has no component here; it is <c>Spin · v̂</c>.
        /// </summary>
        public static BattedBallLaunch FromState(BallState state)
        {
            Vector3d v = state.Velocity;
            double speed = v.Length, launch = Math.Asin(v.Z / speed), spray = Math.Atan2(v.X, v.Y);
            var horizontal = new Vector3d(Math.Sin(spray), Math.Cos(spray), 0.0);
            Vector3d backspinAxis = Vector3d.Cross(horizontal, new Vector3d(0.0, 0.0, 1.0));
            Vector3d sidespinAxis = Math.Cos(launch) * new Vector3d(0.0, 0.0, 1.0) - Math.Sin(launch) * horizontal;
            return new BattedBallLaunch(Units.MetersPerSecondToMph(speed), Units.RadiansToDegrees(launch), Units.RadiansToDegrees(spray),
                Units.RadiansPerSecondToRpm(Vector3d.Dot(state.Spin, backspinAxis)), -Units.RadiansPerSecondToRpm(Vector3d.Dot(state.Spin, sidespinAxis)));
        }
    }

    /// <summary>Landing and shape of a batted-ball flight (SI; distances on the ground from the plate origin).</summary>
    public readonly struct BattedBallMetrics
    {
        public readonly double ExitSpeed, LaunchAngleDegrees, SprayAngleDegrees, SpinRate;
        /// <summary>Highest point of the ball centre above the ground, m.</summary>
        public readonly double ApexHeight;
        /// <summary>Contact to landing (ball touching the ground), s.</summary>
        public readonly double HangTime;
        /// <summary>Landing point (ball centre over the ground), m.</summary>
        public readonly double LandingX, LandingY;
        /// <summary>Horizontal distance from the rear point of home plate to the landing point, m.</summary>
        public readonly double Distance;
        public readonly bool Landed;

        public BattedBallMetrics(TrajectoryResult flight)
        {
            BallState start = flight.First;
            Vector3d v = start.Velocity;
            ExitSpeed = v.Length;
            LaunchAngleDegrees = Units.RadiansToDegrees(Math.Asin(v.Z / ExitSpeed));
            SprayAngleDegrees = Units.RadiansToDegrees(Math.Atan2(v.X, v.Y));
            SpinRate = start.Spin.Length;
            double apex = double.NegativeInfinity;
            for (int i = 0; i < flight.Samples.Count; i++) apex = Math.Max(apex, flight.Samples[i].Position.Z);
            ApexHeight = apex;
            Landed = flight.End == FlightEnd.ReachedGround;
            BallState end = flight.Final;
            HangTime = Landed ? end.Time - start.Time : double.NaN;
            LandingX = Landed ? end.Position.X : double.NaN;
            LandingY = Landed ? end.Position.Y : double.NaN;
            Distance = Landed ? Math.Sqrt(end.Position.X * end.Position.X + end.Position.Y * end.Position.Y) : double.NaN;
        }
    }

    public sealed class BattedBallResult
    {
        public BattedBallResult(TrajectoryResult flight)
        {
            Flight = flight;
            Metrics = new BattedBallMetrics(flight);
        }

        public TrajectoryResult Flight { get; }
        public BattedBallMetrics Metrics { get; }
    }

    /// <summary>
    /// Flies a batted ball from contact until it lands, with the shared <see cref="BallFlightSimulator"/>. No fielders,
    /// walls or bounces; only the first landing.
    /// </summary>
    public static class BattedBallSimulation
    {
        public const double MaxFlightTime = 12.0;

        public static BattedBallResult Run(BallState contact, EnvironmentState environment) =>
            Run(contact, environment, AerodynamicModel.BattedBall);

        public static BattedBallResult Run(BallState contact, EnvironmentState environment, AerodynamicModel aerodynamics) =>
            new BattedBallResult(new BallFlightSimulator(BallProperties.Baseball, environment, aerodynamics)
                .Simulate(contact, new FlightLimits(double.NegativeInfinity, 0.0, MaxFlightTime)));
    }
}
