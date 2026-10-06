using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// The foul tip (TASK-024; OBR Definitions, "Foul tip"): a batted ball that goes sharp and direct from the bat to the
    /// catcher's hands and is legally caught — a strike. The catcher's glove waits where the pitch would have crossed his
    /// receiving plane; he has no time to react to a tip, so it is caught only when its early, direct path (a straight line
    /// with gravity: over a metre or two, drag changes it by millimetres) crosses that plane within the glove's reach and above the
    /// ground. The reach is the mitt's (Docs/HBP_FOULTIP_CHECKSWING.md compares the rate with MLB's).
    /// </summary>
    public static class FoulTips
    {
        /// <summary>The plane (simulation Y, m) where the crouched catcher's glove takes a pitch (he sets up at −0.76 m).</summary>
        public const double CatcherPlaneY = -0.35;

        /// <summary>How far (m, centre to centre) from the waiting glove a tip is still caught: the mitt's radius (OBR 3.04:
        /// at most 38 in around, ≈ 6 in radius) plus the ball's — DERIVED; he has no time to move it.</summary>
        public const double GloveReach = 0.19;

        /// <summary>Where the pitch would have crossed the catcher's plane (its authoritative flight); null if it never does
        /// (it ends on the ground first).</summary>
        public static Vector3d? GloveAt(HittingPitch pitch)
        {
            TrajectoryResult flight = pitch.Flight;
            if (flight.First.Position.Y < CatcherPlaneY || flight.Final.Position.Y > CatcherPlaneY) return null;
            double a = flight.First.Time, b = flight.Final.Time;
            for (int i = 0; i < 60; i++)
            {
                double m = 0.5 * (a + b);
                if (flight.StateAt(m).Position.Y > CatcherPlaneY) a = m;
                else b = m;
            }

            return flight.StateAt(0.5 * (a + b)).Position;
        }

        /// <summary>Where the batted ball's direct path crosses the catcher's plane, or null (it goes forward, or reaches
        /// the ground first).</summary>
        public static Vector3d? DirectPathAt(BallState batted, double gravity = EnvironmentState.StandardGravity)
        {
            Vector3d p = batted.Position, v = batted.Velocity;
            if (!(v.Y < 0.0) || p.Y < CatcherPlaneY) return null;
            double t = (CatcherPlaneY - p.Y) / v.Y;
            var at = new Vector3d(p.X + v.X * t, CatcherPlaneY, p.Z + v.Z * t - 0.5 * gravity * t * t);
            return at.Z >= BallProperties.Baseball.Radius ? at : (Vector3d?)null;
        }

        /// <summary>Is the contact a caught foul tip?</summary>
        public static bool IsCaught(HittingPitch pitch, ContactResult contact)
        {
            if (pitch == null) throw new ArgumentNullException(nameof(pitch));
            if (!contact.IsContact) return false;
            Vector3d? glove = GloveAt(pitch), tip = DirectPathAt(contact.BattedBall);
            return glove is Vector3d g && tip is Vector3d b && (b - g).Length <= GloveReach;
        }
    }
}
