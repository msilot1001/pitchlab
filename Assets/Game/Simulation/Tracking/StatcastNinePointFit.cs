using System;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Tracking
{
    /// <summary>
    /// Statcast's 9-parameter constant-acceleration trajectory, in feet and seconds, with t = 0 at y = 50 ft
    /// (Nathan, "Determining the Magnus force on a baseball from PITCHf/x data"). The CSV has no x0/z0 columns, so the
    /// y = 50 ft position is recovered from the release point: t_release solves y(t) = release_pos_y (t &lt; 0).
    /// </summary>
    public readonly struct StatcastNinePointFit
    {
        public const double ReferenceY = 50.0;

        public readonly double X0, Z0;
        public readonly double Vx0, Vy0, Vz0;
        public readonly double Ax, Ay, Az;
        public readonly double ReleaseTime;

        public StatcastNinePointFit(StatcastPitch pitch)
        {
            Vx0 = pitch.Vx0;
            Vy0 = pitch.Vy0;
            Vz0 = pitch.Vz0;
            Ax = pitch.Ax;
            Ay = pitch.Ay;
            Az = pitch.Az;
            if (!(Vy0 < 0.0)) throw new ArgumentException("Pitch must move toward the plate (vy0 < 0).", nameof(pitch));
            ReleaseTime = SolveTime(Vy0, Ay, pitch.ReleaseY);
            X0 = pitch.ReleaseX - Vx0 * ReleaseTime - 0.5 * Ax * ReleaseTime * ReleaseTime;
            Z0 = pitch.ReleaseZ - Vz0 * ReleaseTime - 0.5 * Az * ReleaseTime * ReleaseTime;
        }

        /// <summary>Time (s, relative to y = 50 ft) at which the fit reaches <paramref name="y"/> feet.</summary>
        public double TimeAtY(double y) => SolveTime(Vy0, Ay, y);

        /// <summary>Position (ft) at time t.</summary>
        public Vector3d PositionFeet(double t) => new Vector3d(
            X0 + Vx0 * t + 0.5 * Ax * t * t,
            ReferenceY + Vy0 * t + 0.5 * Ay * t * t,
            Z0 + Vz0 * t + 0.5 * Az * t * t);

        /// <summary>Velocity (ft/s) at time t.</summary>
        public Vector3d VelocityFeet(double t) => new Vector3d(Vx0 + Ax * t, Vy0 + Ay * t, Vz0 + Az * t);

        public Vector3d AccelerationFeet => new Vector3d(Ax, Ay, Az);

        // y(t) = 50 + vy0·t + ½·ay·t²; the physical root is the one continuous with t = 0 at y = 50 (pitch moving −y).
        private static double SolveTime(double vy0, double ay, double y)
        {
            double c = ReferenceY - y;
            if (Math.Abs(ay) < 1e-12) return -c / vy0;
            double discriminant = vy0 * vy0 - 2.0 * ay * c;
            if (discriminant < 0.0) throw new ArgumentException("The fitted trajectory never reaches that y.");
            return (-vy0 - Math.Sqrt(discriminant)) / ay;
        }
    }
}
