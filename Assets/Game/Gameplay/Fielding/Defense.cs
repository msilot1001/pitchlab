using System;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Fielding
{
    public enum DefensivePosition
    {
        P,
        C,
        FirstBase,
        SecondBase,
        ThirdBase,
        Shortstop,
        LeftField,
        CenterField,
        RightField,
    }

    /// <summary>
    /// Generic defender abilities (Docs/FIELDING.md records the source of each value). Ready to be replaced by player
    /// ratings: nothing in the fielding logic depends on a specific position's numbers.
    /// </summary>
    public readonly struct FielderProfile
    {
        /// <summary>Contact → first movement (s).</summary>
        public readonly double ReactionTime;
        /// <summary>Top running speed (m/s).</summary>
        public readonly double MaxSpeed;
        /// <summary>Acceleration time constant τ of v(t) = v_max·(1 − e^(−t/τ)) (s).</summary>
        public readonly double AccelerationTime;
        /// <summary>Braking deceleration (m/s²).</summary>
        public readonly double BrakeDeceleration;
        /// <summary>Horizontal distance from the body to a ball the glove can take at chest height and above (m).</summary>
        public readonly double Reach;
        /// <summary>The same for a ball on or near the ground, taken in front of the feet in a deep crouch (m).</summary>
        public readonly double GroundReach;
        /// <summary>Highest ball centre the glove can take standing (m).</summary>
        public readonly double CatchHeightMax;
        /// <summary>Ball centre at or below which a ball that has touched the ground is a ground pickup (above: a hop catch).</summary>
        public readonly double PickupHeightMax;

        public FielderProfile(double reactionTime, double maxSpeed, double accelerationTime, double brakeDeceleration, double reach,
            double groundReach, double catchHeightMax, double pickupHeightMax)
        {
            if (!(groundReach >= 0.0) || groundReach > reach) throw new ArgumentOutOfRangeException(nameof(groundReach));
            if (!(reactionTime >= 0.0)) throw new ArgumentOutOfRangeException(nameof(reactionTime));
            if (!(maxSpeed > 0.0)) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            if (!(accelerationTime > 0.0)) throw new ArgumentOutOfRangeException(nameof(accelerationTime));
            if (!(brakeDeceleration > 0.0)) throw new ArgumentOutOfRangeException(nameof(brakeDeceleration));
            if (!(reach >= 0.0)) throw new ArgumentOutOfRangeException(nameof(reach));
            if (!(catchHeightMax > 0.0) || !(pickupHeightMax > 0.0) || pickupHeightMax > catchHeightMax) throw new ArgumentOutOfRangeException(nameof(catchHeightMax));
            ReactionTime = reactionTime;
            MaxSpeed = maxSpeed;
            AccelerationTime = accelerationTime;
            BrakeDeceleration = brakeDeceleration;
            Reach = reach;
            GroundReach = groundReach;
            CatchHeightMax = catchHeightMax;
            PickupHeightMax = pickupHeightMax;
        }

        /// <summary>Glove reach for a ball centre at <paramref name="height"/>: <see cref="GroundReach"/> up to knee height (0.4 m),
        /// <see cref="Reach"/> from waist height (0.9 m) to 2.0 m, shrinking to 0.3 m at <see cref="CatchHeightMax"/>.</summary>
        public double ReachAt(double height)
        {
            double u = Math.Max(0.0, Math.Min(1.0, (height - 0.4) / 0.5));
            double reach = GroundReach + (Reach - GroundReach) * u;
            // Fully stretched upward the glove is near the body's axis: above 2.0 m the reach shrinks to 0.3 m at the
            // catch height (no jumping in gameplay yet).
            if (height > 2.0 && CatchHeightMax > 2.0)
                reach -= (Reach - 0.3) * Math.Min(1.0, (height - 2.0) / (CatchHeightMax - 2.0));
            return Math.Max(0.0, reach);
        }

        private const double FtPerS = 0.3048;

        /// <summary>
        /// Gameplay baseline. MEASURED/REPORTED references (Docs/FIELDING.md): MLB average sprint speed 27 ft/s (Statcast);
        /// τ ≈ 0.78 s fits the Statcast 90-ft splits (DERIVED); braking capped at 6 m/s² (team-sport data, REPORTED);
        /// standing glove-catch height ≈ 2.6 m and reach ≈ 1 m at chest height, 0.6 m for a ball on the ground in front of the
        /// feet (arm ≈ 0.6 m from a deep crouch) (DERIVED/ASSUMED); outfield reaction 0.40 s after contact
        /// (DERIVED from Statcast Jump, ASSUMED), infield 0.25 s, pitcher 0.45 s (follow-through), catcher 0.50 s (rising
        /// from the crouch) (ASSUMED). Top speeds: CF/SS 28, 1B/C 25, P 26, others 27 ft/s (ASSUMED around the MLB average).
        /// </summary>
        public static FielderProfile For(DefensivePosition position)
        {
            double reaction, speedFt;
            switch (position)
            {
                case DefensivePosition.P: reaction = 0.45; speedFt = 26.0; break;
                case DefensivePosition.C: reaction = 0.50; speedFt = 25.0; break;
                case DefensivePosition.FirstBase: reaction = 0.25; speedFt = 25.0; break;
                case DefensivePosition.Shortstop: reaction = 0.25; speedFt = 28.0; break;
                case DefensivePosition.SecondBase:
                case DefensivePosition.ThirdBase: reaction = 0.25; speedFt = 27.0; break;
                case DefensivePosition.CenterField: reaction = 0.40; speedFt = 28.0; break;
                default: reaction = 0.40; speedFt = 27.0; break;   // LF, RF
            }

            return new FielderProfile(reaction, speedFt * FtPerS, 0.78, 6.0, 1.0, 0.6, 2.6, 0.30);
        }
    }

    /// <summary>
    /// Generic defensive starting positions in the simulation frame (origin at the rear point of home plate, +X toward first
    /// base, +Y toward centre field, ground Z = 0). Infield/outfield: midpoints of the Statcast league-average positioning
    /// zones against right-handed batters (REPORTED, Docs/FIELDING.md); pitcher at his follow-through in front of the
    /// rubber, catcher 2.5 ft behind the plate (ASSUMED). Not a specific park.
    /// </summary>
    public sealed class DefensiveAlignment
    {
        private readonly Vector3d[] _positions;

        public DefensiveAlignment(Vector3d[] positions)
        {
            if (positions == null || positions.Length != Count) throw new ArgumentException("One position per defender.", nameof(positions));
            _positions = (Vector3d[])positions.Clone();
        }

        public const int Count = 9;

        public Vector3d this[DefensivePosition position] => _positions[(int)position];

        private static Vector3d At(double feet, double sprayDegrees)
        {
            double a = sprayDegrees * Math.PI / 180.0, d = feet * 0.3048;
            return new Vector3d(d * Math.Sin(a), d * Math.Cos(a), 0.0);
        }

        public static DefensiveAlignment Standard => new DefensiveAlignment(new[]
        {
            At(55.0, 0.0),        // P (follow-through, ~5 ft in front of the 60.5 ft rubber)
            new Vector3d(0.0, -0.76, 0.0),   // C
            At(110.0, 32.0),      // 1B
            At(148.0, 12.0),      // 2B
            At(110.0, -28.0),     // 3B
            At(148.0, -15.0),     // SS
            At(295.0, -27.0),     // LF
            At(322.0, 0.0),       // CF
            At(295.0, 27.0),      // RF
        });
    }
}
