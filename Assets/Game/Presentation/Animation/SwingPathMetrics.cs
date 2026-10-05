using System;
using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// Statcast-style bat-tracking quantities measured on a PRESENTATION swing (the visual bat's sweet spot over time) —
    /// a check that the animation resembles a reference swing. Never used for contact (gameplay uses ContactResolver).
    /// Field frame (Unity): +X toward first base, +Y up, +Z toward the pitcher.
    /// </summary>
    public readonly struct SwingPathMetrics
    {
        /// <summary>Sweet-spot speed at contact, mph.</summary>
        public readonly double BatSpeedMph;
        /// <summary>Path length of the bat head (Statcast definition) from swing start to contact, ft.</summary>
        public readonly double SwingLengthFeet;
        /// <summary>Vertical angle of the sweet-spot velocity at contact (+ = upward), degrees.</summary>
        public readonly double AttackAngleDegrees;
        /// <summary>Horizontal angle of the sweet-spot velocity at contact from straight away (+ = toward first base: opposite
        /// field for a right-handed hitter, pull for a left-handed one), degrees.</summary>
        public readonly double AttackDirectionDegrees;
        /// <summary>Angle between the sweet spot's path plane over the last 40 ms before contact and the ground, degrees (our
        /// reading of Statcast's "swing path tilt"; Savant does not say which bat point defines the plane).</summary>
        public readonly double SwingPathTiltDegrees;

        private SwingPathMetrics(double speed, double length, double attack, double direction, double tilt)
        {
            BatSpeedMph = speed;
            SwingLengthFeet = length;
            AttackAngleDegrees = attack;
            AttackDirectionDegrees = direction;
            SwingPathTiltDegrees = tilt;
        }

        /// <summary>
        /// Measures a sampled path: <paramref name="sweetSpotAt"/> gives the sweet spot and <paramref name="headAt"/> the bat
        /// head (world, m) at time t (s); swing from <paramref name="start"/> to <paramref name="contact"/>, sampled every
        /// <paramref name="step"/> s.
        /// </summary>
        public static SwingPathMetrics Measure(Func<double, Vector3> sweetSpotAt, Func<double, Vector3> headAt, double start, double contact, double step = 0.001)
        {
            double length = 0.0;
            Vector3 previous = headAt(start);
            for (double t = start + step; t <= contact + 1e-9; t += step)
            {
                Vector3 p = headAt(Math.Min(t, contact));
                length += Vector3.Distance(previous, p);
                previous = p;
            }

            // Velocity at contact: centred difference over ±1 ms (the bat keeps moving through contact).
            Vector3 v = (sweetSpotAt(contact + step) - sweetSpotAt(contact - step)) / (float)(2.0 * step);
            double speed = v.magnitude;
            double attack = Math.Asin(Mathf.Clamp(v.y / Mathf.Max(v.magnitude, 1e-9f), -1f, 1f)) * Mathf.Rad2Deg;
            // Direction of travel through the zone: toward the pitcher (+Z) is straight away.
            double direction = Math.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            // Swing plane over the last 40 ms: normal from two chords of the arc.
            Vector3 a = sweetSpotAt(contact - 0.040), b = sweetSpotAt(contact - 0.020), c = sweetSpotAt(contact);
            Vector3 normal = Vector3.Cross(b - a, c - b);
            double tilt = normal.sqrMagnitude > 1e-12f ? Vector3.Angle(normal, Vector3.up) : double.NaN;
            if (tilt > 90.0) tilt = 180.0 - tilt;
            return new SwingPathMetrics(speed / 0.44704, length / 0.3048, attack, direction, tilt);
        }

        public override string ToString() =>
            $"bat speed {BatSpeedMph:0.0} mph, swing length {SwingLengthFeet:0.0} ft, attack angle {AttackAngleDegrees:+0.0;-0.0}°, " +
            $"attack direction {AttackDirectionDegrees:+0.0;-0.0}° (+ = toward 1B), swing path tilt {SwingPathTiltDegrees:0.0}°";
    }
}
