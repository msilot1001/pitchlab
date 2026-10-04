using System;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>
    /// The PCI coordinate system. PCI state is normalized and device-independent: (u, v) ∈ [−1, 1]², +u toward first
    /// base (catcher's view right, simulation +X), +v up. This frame is the ONE conversion to gameplay metres: the
    /// contact-plane point (X, Z) that <see cref="SwingInput"/> and <see cref="ContactResolver"/> use. Presentation maps
    /// those metres to world space with the simulation→Unity mapping; no other PCI coordinate system exists.
    /// </summary>
    public readonly struct PciFrame
    {
        /// <summary>Centre (m, contact plane) and half-extents (m) of the aimable area.</summary>
        public readonly double CenterX, CenterZ, HalfWidth, HalfHeight;

        public PciFrame(double centerX, double centerZ, double halfWidth, double halfHeight)
        {
            if (!(halfWidth > 0.0) || !(halfHeight > 0.0)) throw new ArgumentOutOfRangeException(nameof(halfWidth), "Empty PCI area.");
            CenterX = centerX;
            CenterZ = centerZ;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        /// <summary>Aimable area of the hitting sandbox: 1.2 m wide (plate ± 0.6 m) from 0.2 m to 1.4 m high.</summary>
        public static PciFrame Default => new PciFrame(0.0, 0.8, 0.6, 0.6);

        public static double Clamp(double n) => Math.Max(-1.0, Math.Min(1.0, n));

        /// <summary>Normalized PCI → contact-plane metres (gameplay contact aim).</summary>
        public (double X, double Z) ToMeters(double u, double v) => (CenterX + Clamp(u) * HalfWidth, CenterZ + Clamp(v) * HalfHeight);

        /// <summary>Contact-plane metres → normalized PCI (clamped).</summary>
        public (double U, double V) ToNormalized(double x, double z) => (Clamp((x - CenterX) / HalfWidth), Clamp((z - CenterZ) / HalfHeight));
    }
}
