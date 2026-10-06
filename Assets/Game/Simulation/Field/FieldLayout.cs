using System;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Field
{
    /// <summary>
    /// Playing-surface geometry in the simulation frame (origin at the rear point of home plate, +X toward first base,
    /// +Y toward centre field, ground at Z = 0). Infield dirt matches the presentation's FieldDressing; the outfield
    /// fence is a generic polyline (332 ft lines, 385 ft alleys, 405 ft centre: an MLB-average park, TASK-023), 8 ft high, with a 15 ft warning
    /// track inside it between the foul lines. See Docs/SURFACE_PHYSICS.md.
    /// </summary>
    public enum Base
    {
        Home,
        First,
        Second,
        Third,
    }

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
            foreach (double f in feet)
                if (!(f > 100.0)) throw new ArgumentOutOfRangeException(nameof(feet), "Fence distances must be beyond the infield.");
            // The quoted distances are at −45°, −22.5°, 0, +22.5°, +45° from centre field; between them the wall's distance
            // changes smoothly with the angle (as park dimensions describe it), sampled every 2.5° (TASK-023: straight chords
            // between the five points cut ≈ 10 ft off the wall between the alleys and centre field).
            _fence = new Vector3d[FencePointCount];
            for (int i = 0; i < FencePointCount; i++)
            {
                double u = 4.0 * i / (FencePointCount - 1);   // 0…4 across the five control points
                int k = Math.Min(3, (int)Math.Floor(u));
                double dist = feet[k] + (feet[k + 1] - feet[k]) * (u - k);
                double angle = -Math.PI / 4.0 + i * (Math.PI / 2.0) / (FencePointCount - 1);
                _fence[i] = new Vector3d(dist * Ft * Math.Sin(angle), dist * Ft * Math.Cos(angle), 0.0);
            }

            WallHeight = wallHeight;
        }

        /// <summary>Side of a base bag (18 in, Official Baseball Rules 2.03 since 2023).</summary>
        public static readonly double BaseBagSide = 18.0 * 0.0254;

        /// <summary>
        /// Centre of a base (ground level, Z = 0). Official Baseball Rules diamond: first and third base bags lie wholly in
        /// fair territory with their outer corners 90 ft from the plate's rear point along the foul lines; second base is
        /// centred 127 ft 3⅜ in from it; home is the centre of the plate (17 in wide, rear point at the origin).
        /// </summary>
        public static Vector3d BasePosition(Base b)
        {
            double s = Math.Sqrt(0.5), half = BaseBagSide / 2.0;
            switch (b)
            {
                case Base.First: return new Vector3d(s * (BaseDistance - half) - s * half, s * (BaseDistance - half) + s * half, 0.0);
                case Base.Third: return new Vector3d(-(s * (BaseDistance - half) - s * half), s * (BaseDistance - half) + s * half, 0.0);
                case Base.Second: return new Vector3d(0.0, (127.0 + 3.375 / 12.0) * Ft, 0.0);
                default: return new Vector3d(0.0, 0.5 * 17.0 * 0.0254, 0.0);   // home: plate centre (8.5 in in front of the rear point)
            }
        }

        /// <summary>
        /// The generic MLB-average park (TASK-023): 332 ft lines, 385 ft alleys, 405 ft centre, an 8-ft wall. The lines and
        /// centre are the 2024–25 parks' averages (≈ 333 / 403 ft, Wikipedia infoboxes); the alleys and the wall reproduce MLB's
        /// measured home-run probability by projected distance (2024 Statcast, LA 20–40°: 18 / 27 / 38 / 55 / 74 / 90 % for
        /// 360…420 ft in 10-ft bins) — real walls run deep and tall between the quoted points (TUNED geometry; physics
        /// untouched). Docs/OFFENSE_CALIBRATION.md.
        /// </summary>
        public static FieldLayout Standard { get; } = new FieldLayout(332.0, 385.0, 405.0, 385.0, 332.0, 8.0 * Ft);   // immutable: built once

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
            int i = (int)Math.Floor((spray + Math.PI / 4.0) / (Math.PI / 2.0 / (FencePointCount - 1)));
            return Math.Max(0, Math.Min(FencePointCount - 2, i));
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
        public const int FencePointCount = 37;
    }
}
