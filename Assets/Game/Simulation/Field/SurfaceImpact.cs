using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Field
{
    /// <summary>
    /// Instantaneous ball–surface impact (Cross, AJP 70:1093 2002 / 73:914 2005; Docs/SURFACE_PHYSICS.md). Uses the
    /// contact-point velocity v + ω × r_c, so incoming spin of any orientation changes the bounce, and the friction
    /// impulse changes both v and ω. A torque-free plow impulse at the centre of mass (turf) acts before friction.
    /// </summary>
    public static class SurfaceImpact
    {
        /// <summary>Moment-of-inertia factor I = α m R² (Cross 2002, Table I).</summary>
        public const double InertiaFactor = 0.40;

        /// <summary>
        /// State just after an impact of <paramref name="before"/> on a surface with outward unit normal
        /// <paramref name="normal"/>. No impulse if the ball is not moving into the surface.
        /// </summary>
        public static BallState Resolve(BallState before, Vector3d normal, BallSurfaceProperties surface, BallProperties ball)
        {
            Vector3d v = before.Velocity, w = before.Spin;
            double vn = Vector3d.Dot(v, normal);
            if (!(vn < 0.0)) return before;

            double m = ball.Mass, R = ball.Radius, a = InertiaFactor;
            double jn = -m * (1.0 + surface.NormalRestitution) * vn;

            // Plow first: a centre-of-mass tangential loss (turf blades), capped so it never reverses the tangential motion.
            // Friction then acts on the slip that remains, so the ball leaves gripping the surface (rolling for e_t = 0)
            // instead of over-spinning — which the next contact would turn back into speed (physics review, TASK-004.7).
            Vector3d vt = v - vn * normal;
            double vtSpeed = vt.Length;
            double plow = surface.PlowAt(v.Length);
            Vector3d jp = vtSpeed > 0.0 ? -(Math.Min(plow * jn, m * vtSpeed) / vtSpeed) * vt : Vector3d.Zero;
            Vector3d vp = v + jp / m;

            Vector3d rc = -R * normal;
            Vector3d contact = vp + Vector3d.Cross(w, rc);
            Vector3d slip = contact - Vector3d.Dot(contact, normal) * normal;
            Vector3d jt = -(m * a / (1.0 + a)) * (1.0 + surface.TangentialRestitution) * slip;
            double slipSpeed = slip.Length;
            if (jt.Length > surface.Friction * jn) jt = slipSpeed > 0.0 ? -(surface.Friction * jn / slipSpeed) * slip : Vector3d.Zero;

            Vector3d dv = (jn * normal + jt + jp) / m;
            Vector3d dw = Vector3d.Cross(rc, jt) / (a * m * R * R);
            return new BallState(before.Time, before.Position, v + dv, w + dw);
        }
    }
}
