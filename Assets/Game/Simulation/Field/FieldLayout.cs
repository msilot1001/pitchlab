using System;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Field
{
    /// <summary>
    /// Playing-surface geometry in the simulation frame (origin at the rear point of home plate, +X toward first base,
    /// +Y toward centre field, ground at Z = 0). Infield dirt matches the presentation's FieldDressing; the outfield
    /// fence is a generic polyline (330 ft lines, 375 ft alleys, 400 ft centre), 8 ft high, with a 15 ft warning
    /// track inside it between the foul lines. See Docs/SURFACE_PHYSICS.md.
    /// </summary>
    public sealed class FieldLayout
    {
        private const double Ft = 0.3048;
        public static readonly double BaseDistance = 90.0 * Ft;
        public static readonly double InfieldDirtSide = BaseDistance + 4.0, InfieldGrassSide = BaseDistance - 3.0;
        public static readonly double HomeCircleRadius = 13.0 * Ft, HomeCircleCentreY = 0.2;
        public static readonly double MoundCentreY = 59.0 * Ft, MoundRadius = 9.0 * Ft;
        public static readonly double WarningTrackWidth = 15.0 * Ft;

        private readonly Vector3d[] _fence;   // polyline points on the ground, from the left-field line to the right-field line

        public FieldLayout(double leftLineFeet, double leftAlleyFeet, double centerFeet, double rightAlleyFeet, double rightLineFeet, double wallHeight)
        {
            if (!(wallHeight > 0.0)) throw new ArgumentOutOfRangeException(nameof(wallHeight));
            double[] feet = { leftLineFeet, leftAlleyFeet, centerFeet, rightAlleyFeet, rightLineFeet };
            _fence = new Vector3d[feet.Length];
            for (int i = 0; i < feet.Length; i++)
            {
                if (!(feet[i] > 100.0)) throw new ArgumentOutOfRangeException(nameof(feet), "Fence distances must be beyond the infield.");
                double angle = (i - 2) * Math.PI / 8.0;   // −45°, −22.5°, 0, +22.5°, +45° from centre field
                _fence[i] = new Vector3d(feet[i] * Ft * Math.Sin(angle), feet[i] * Ft * Math.Cos(angle), 0.0);
            }

            WallHeight = wallHeight;
        }

        public static FieldLayout Standard => new FieldLayout(330.0, 375.0, 400.0, 375.0, 330.0, 8.0 * Ft);

        public double WallHeight { get; }

        /// <summary>Spray angle (radians from centre field, + toward first base) of a ground point.</summary>
        public static double SprayAngle(double x, double y) => Math.Atan2(x, y);

        public static bool IsFair(double x, double y) => y >= 0.0 && Math.Abs(x) <= y;

        /// <summary>
        /// Distance (m) from a ground point to fair territory (the wedge between the foul lines, lines included); ≤ 0 inside.
        /// Behind the plate's rear point the nearest fair point is the apex itself.
        /// </summary>
        public static double OutsideFoulLine(double x, double y) =>
            y + Math.Abs(x) < 0.0 ? Math.Sqrt(x * x + y * y) : (Math.Abs(x) - y) / Math.Sqrt(2.0);

        /// <summary>Fence segment between the foul lines that a ray from home at this spray angle meets (clamped to the lines).</summary>
        private int Segment(double spray)
        {
            int i = (int)Math.Floor((spray + Math.PI / 4.0) / (Math.PI / 8.0));
            return Math.Max(0, Math.Min(3, i));
        }

        /// <summary>
        /// Signed horizontal distance from the fence's inner face to (x, y) along the fence normal: negative inside the
        /// park, positive beyond the fence. Also returns the unit normal pointing back toward the field. Outside the foul
        /// lines the end segments extend.
        /// </summary>
        public double DistanceBeyondFence(double x, double y, out Vector3d inwardNormal)
        {
            int i = Segment(SprayAngle(x, y));
            Vector3d a = _fence[i], b = _fence[i + 1];
            var along = (b - a).Normalized;
            // Inward = toward home: perpendicular to the segment, on the origin's side.
            var normal = new Vector3d(-along.Y, along.X, 0.0);
            if (Vector3d.Dot(normal, -a) < 0.0) normal = -normal;
            inwardNormal = normal;
            return -Vector3d.Dot(new Vector3d(x, y, 0.0) - a, normal);
        }

        /// <summary>Ground surface under (x, y).</summary>
        public SurfaceKind SurfaceAt(double x, double y)
        {
            if (IsFair(x, y) && DistanceBeyondFence(x, y, out _) > -WarningTrackWidth) return SurfaceKind.WarningTrack;
            if (Math.Sqrt(x * x + (y - HomeCircleCentreY) * (y - HomeCircleCentreY)) <= HomeCircleRadius) return SurfaceKind.InfieldDirt;
            if (Math.Sqrt(x * x + (y - MoundCentreY) * (y - MoundCentreY)) <= MoundRadius) return SurfaceKind.InfieldDirt;
            // Squares on the diamond (rotated 45°, centred on the midpoint between home and second base).
            double c = BaseDistance * Math.Sqrt(2.0) / 2.0;
            double u = Math.Abs((x + (y - c)) / Math.Sqrt(2.0)), v = Math.Abs((-x + (y - c)) / Math.Sqrt(2.0));
            double r = Math.Max(u, v);
            if (r <= InfieldGrassSide / 2.0) return SurfaceKind.NaturalGrass;
            if (r <= InfieldDirtSide / 2.0) return SurfaceKind.InfieldDirt;
            return SurfaceKind.NaturalGrass;
        }

        /// <summary>Fence polyline points (ground level), left-field line to right-field line.</summary>
        public Vector3d FencePoint(int index) => _fence[index];
        public const int FencePointCount = 5;
    }
}
