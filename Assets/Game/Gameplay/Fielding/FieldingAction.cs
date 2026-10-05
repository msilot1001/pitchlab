using System;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>
    /// How a defender takes the ball (TASK-011.6), classified from authoritative geometry and timing only — the ball's state at
    /// the take, where the defender is and how he is moving — never from animation. Presentation shows the action; only
    /// <see cref="DivingCatch"/> has a gameplay consequence (its envelope, InterceptSolver.SolveDive, and its recovery).
    /// </summary>
    public enum FieldingAction
    {
        None,
        StandingCatch,
        RunningCatch,
        SlidingCatch,
        DivingCatch,
        JumpingCatch,
        OverShoulderCatch,
        CenteredPickup,
        ForehandPickup,
        BackhandPickup,
        ChargingPickup,
        ShortHopPickup,
        HopCatch,
        WallPlay,
        /// <summary>Catching a thrown ball (a base cover, cut-off, relay).</summary>
        ReceiveThrow,
    }

    public static class FieldingActions
    {
        /// <summary>Running at least this fast at the take (m/s) is a running catch.</summary>
        public const double RunningSpeed = 1.5;
        /// <summary>A catch with the ball centre above this (m) needs a jump (standing reach ≈ 2.2 m with the glove).</summary>
        public const double JumpHeight = 2.4;
        /// <summary>A low ball (centre below, m) taken running this fast (m/s) or faster is a sliding catch.</summary>
        public const double SlideHeight = 0.6, SlideSpeed = 4.5;
        /// <summary>A ball whose ground point is this far (m) to the glove side / across the body is a forehand / backhand.</summary>
        public const double SideReach = 0.35;
        /// <summary>A pickup within this long (s) after a real bounce (falling faster than <see cref="RealBounce"/> m/s) is a short
        /// hop: ≈ a foot of ball travel.</summary>
        public const double ShortHop = 0.05, RealBounce = 1.0;
        /// <summary>Charging: running at it at least this fast (m/s) for a ball slower than <see cref="SlowRoller"/> (m/s).</summary>
        public const double ChargeSpeed = 2.5, SlowRoller = 9.0;
        /// <summary>A take this close to the fence (m) is a wall play.</summary>
        public const double WallZone = 3.0;
        /// <summary>Recovery after a diving catch before the ball can be thrown (s, added to the transfer; up off the ground).</summary>
        public const double DiveRecovery = 1.0;

        /// <param name="ball">The ball in play.</param>
        /// <param name="take">The take (time, ball state, kind, dive).</param>
        /// <param name="fielder">The defender's position at the take; <paramref name="velocity"/> his ground velocity then.</param>
        public static FieldingAction Classify(BallInPlay ball, Intercept take, Vector3d fielder, Vector3d velocity, FieldLayout field)
        {
            if (!take.Feasible) return FieldingAction.None;
            Vector3d at = take.Ball.Position;
            double speed = Flat(velocity).Length;
            bool nearWall = field.DistanceBeyondFence(at.X, at.Y, out _) > -WallZone;
            switch (take.Kind)
            {
                case InterceptKind.FlyCatch:
                {
                    if (take.Dive) return FieldingAction.DivingCatch;
                    if (at.Z > JumpHeight) return FieldingAction.JumpingCatch;
                    // Running away from home with the ball coming down over his shoulder (from behind him).
                    Vector3d away = Flat(fielder).Length > 1e-6 ? Flat(fielder) / Flat(fielder).Length : new Vector3d(0.0, 1.0, 0.0);
                    Vector3d ballDir = Flat(take.Ball.Velocity);
                    if (speed >= 4.0 && Dot(velocity, away) > 0.6 * speed && Dot(ballDir, velocity) > 0.0) return FieldingAction.OverShoulderCatch;
                    // A low ball ahead of him taken at speed: feet-first slide under it.
                    Vector3d toBall = Flat(at - fielder);
                    if (at.Z < SlideHeight && speed >= SlideSpeed && Dot(toBall, velocity) >= 0.0) return FieldingAction.SlidingCatch;
                    return speed >= RunningSpeed ? FieldingAction.RunningCatch : FieldingAction.StandingCatch;
                }
                case InterceptKind.HopCatch:
                    return nearWall ? FieldingAction.WallPlay : FieldingAction.HopCatch;
                default:
                {
                    if (nearWall) return FieldingAction.WallPlay;
                    Vector3d incoming = Flat(take.Ball.Velocity);
                    double ballSpeed = incoming.Length;
                    // Charging a slow ball: running toward where it comes from.
                    // (A ball at rest: only if he runs at it toward home — an outfielder chasing a ball in the gap is not charging.)
                    if (ballSpeed < SlowRoller && speed >= ChargeSpeed
                        && (ballSpeed < 0.5 ? Dot(velocity, -fielder) > 0.5 * speed * Flat(fielder).Length : Dot(velocity, incoming) < -0.5 * speed * ballSpeed))
                        return FieldingAction.ChargingPickup;
                    if (SinceBounce(ball, take.Time) < ShortHop) return FieldingAction.ShortHopPickup;
                    // Side of the ball in the frame facing the incoming ball (a right-handed fielder: glove on the left).
                    Vector3d facing = ballSpeed > 0.5 ? -incoming / ballSpeed : (Flat(at - fielder).Length > 1e-6 ? Flat(at - fielder) / Flat(at - fielder).Length : new Vector3d(0.0, -1.0, 0.0));
                    var right = new Vector3d(facing.Y, -facing.X, 0.0);
                    double side = Dot(Flat(at - fielder), right);
                    if (side < -SideReach) return FieldingAction.ForehandPickup;
                    if (side > SideReach) return FieldingAction.BackhandPickup;
                    return FieldingAction.CenteredPickup;
                }
            }
        }

        /// <summary>Time since the ball's last ground impact before <paramref name="t"/> (∞ if none).</summary>
        private static double SinceBounce(BallInPlay ball, double t)
        {
            double last = double.NegativeInfinity;
            foreach (BallEvent e in ball.Events)
                if (e.Kind == BallEventKind.GroundImpact && e.Time <= t && e.Before.Velocity.Z < -RealBounce) last = e.Time;
            return t - last;
        }

        private static Vector3d Flat(Vector3d v) => new Vector3d(v.X, v.Y, 0.0);

        private static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y;
    }
}
