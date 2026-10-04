using System;

namespace Pitchlab.Simulation.Core
{
    /// <summary>Double-precision 3D vector for simulation math. Frame and units are set by the caller.</summary>
    public readonly struct Vector3d : IEquatable<Vector3d>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3d Zero => default;

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double LengthSquared => X * X + Y * Y + Z * Z;

        /// <summary>Unit vector in the same direction, or zero for a zero vector.</summary>
        public Vector3d Normalized
        {
            get
            {
                double length = Length;
                return length > 0.0 ? this / length : Zero;
            }
        }

        public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Right-handed cross product.</summary>
        public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3d operator -(Vector3d a) => new Vector3d(-a.X, -a.Y, -a.Z);
        public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.X * s, a.Y * s, a.Z * s);
        public static Vector3d operator *(double s, Vector3d a) => new Vector3d(a.X * s, a.Y * s, a.Z * s);
        public static Vector3d operator /(Vector3d a, double s) => new Vector3d(a.X / s, a.Y / s, a.Z / s);

        public bool Equals(Vector3d other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Vector3d other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
    }
}
