using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>Where a pitch touched the batter (TASK-024).</summary>
    public readonly struct BodyHit
    {
        public BodyHit(double time, Vector3d ballCentre, string part)
        {
            Time = time;
            BallCentre = ballCentre;
            Part = part;
        }

        /// <summary>Simulation time of the touch (s after release).</summary>
        public double Time { get; }
        /// <summary>The ball's centre at the touch (simulation frame, m).</summary>
        public Vector3d BallCentre { get; }
        public string Part { get; }
    }

    /// <summary>
    /// The batter's body for the hit-by-pitch rule (TASK-024): a few simple volumes in the simulation frame (+X toward first
    /// base, +Y toward the pitcher, +Z up). He stands where Baseball Savant's batter positioning puts the reference hitter
    /// (hips 24.7 in behind the front of the plate, 27.7 in off its inside edge; MEASURED, as the presentation places him),
    /// side-on: his chest faces the plate, his shoulders line up with the pitch. Sizes scale with his height (ASSUMED,
    /// anthropometric round numbers). The body does not move during the pitch (ASSUMED: no stride or flinch); he always
    /// tries to avoid the pitch (OBR 5.05(b)(2)(B) is not modelled).
    /// </summary>
    public static class BatterBody
    {
        public static readonly double StanceBehindPlateFront = Units.InchesToMeters(24.7), StanceOffPlateEdge = Units.InchesToMeters(27.7);

        /// <summary>One volume: a vertical cylinder with an elliptical section (semi-axes <see cref="Rx"/> across,
        /// <see cref="Ry"/> along the pitch) from <see cref="Bottom"/> to <see cref="Top"/>, or a sphere when Rx = Ry and
        /// Bottom = Top.</summary>
        public readonly struct Part
        {
            public Part(string name, double x, double y, double bottom, double top, double rx, double ry)
            {
                Name = name;
                X = x;
                Y = y;
                Bottom = bottom;
                Top = top;
                Rx = rx;
                Ry = ry;
            }

            public string Name { get; }
            public double X { get; }
            public double Y { get; }
            public double Bottom { get; }
            public double Top { get; }
            public double Rx { get; }
            public double Ry { get; }

            /// <summary>Does a ball of radius <paramref name="r"/> centred at <paramref name="p"/> touch it?</summary>
            public bool Touches(Vector3d p, double r)
            {
                double dz = p.Z < Bottom ? Bottom - p.Z : p.Z > Top ? p.Z - Top : 0.0;
                if (dz > r) return false;
                // Grow the section by the part of the ball's radius left at that height (a capsule-like end).
                double grow = Math.Sqrt(r * r - dz * dz);
                double ex = (p.X - X) / (Rx + grow), ey = (p.Y - Y) / (Ry + grow);
                return ex * ex + ey * ey <= 1.0;
            }
        }

        /// <summary>The volumes of a batter of <paramref name="heightInches"/> on <paramref name="side"/>.</summary>
        public static Part[] Parts(BatterSide side, double heightInches)
        {
            double h = Units.InchesToMeters(heightInches);
            double s = side == BatterSide.Right ? -1.0 : 1.0;   // a right-handed hitter stands on the −X side
            double x = s * (PitchingGeometry.PlateHalfWidth + StanceOffPlateEdge), y = PitchingGeometry.PlateFrontY - StanceBehindPlateFront;
            double toPlate = -s;   // toward the plate
            return new[]
            {
                new Part("front leg", x, y + 0.30, 0.0, 0.48 * h, 0.08, 0.08),
                new Part("back leg", x, y - 0.20, 0.0, 0.48 * h, 0.08, 0.08),
                new Part("torso", x, y, 0.48 * h, 0.82 * h, 0.12, 0.19),
                new Part("head", x, y - 0.03, 0.93 * h, 0.93 * h, 0.11, 0.11),
                // Hands at the back shoulder, forearms and the front elbow reaching toward the plate.
                new Part("hands", x + toPlate * 0.18, y - 0.12, 0.75 * h, 0.75 * h, 0.09, 0.09),
                new Part("front arm", x + toPlate * 0.18, y + 0.08, 0.70 * h, 0.70 * h, 0.09, 0.09),
            };
        }

        /// <summary>Step (s) along the authoritative flight: ≤ 2 cm of ball travel, well under the smallest part.</summary>
        public const double Step = 0.0005;

        /// <summary>
        /// The first touch of the pitch on the batter (<paramref name="from"/> onward, s after release), or null. The flight is
        /// the authoritative pitch (to the catcher or the ground); nothing is re-simulated.
        /// </summary>
        public static BodyHit? FirstTouch(HittingPitch pitch, BatterSide side, double heightInches, double from = 0.0)
        {
            if (pitch == null) throw new ArgumentNullException(nameof(pitch));
            Part[] parts = Parts(side, heightInches);
            double r = BallProperties.Baseball.Radius, front = double.NegativeInfinity;
            foreach (Part p in parts) front = Math.Max(front, p.Y + p.Ry);
            TrajectoryResult flight = pitch.Flight;
            for (double t = Math.Max(from, flight.First.Time); t <= flight.Final.Time; t += Step)
            {
                Vector3d at = flight.StateAt(t).Position;
                if (at.Y > front + r) continue;   // not yet at him
                foreach (Part p in parts)
                    if (p.Touches(at, r)) return new BodyHit(t, at, p.Name);
            }

            return null;
        }
    }
}
