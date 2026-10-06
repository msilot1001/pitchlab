using System;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Hitting
{
    public enum BatterSide
    {
        Right,
        Left,
    }

    /// <summary>
    /// Everything that characterises one swing type and one hitter. Future player ratings map onto these fields;
    /// the resolver itself has no hidden constants. Values: Docs/HITTING.md (sources and tuning notes).
    /// </summary>
    [Serializable]
    public struct SwingParameters
    {
        public BatterSide Side;
        /// <summary>Bat speed at the sweet spot at contact, m/s.</summary>
        public double BatSpeed;
        /// <summary>Upward angle of the bat's path at contact, radians.</summary>
        public double AttackAngle;
        /// <summary>
        /// Vertical bat angle at contact: how far the barrel tip is below the knob, radians (the barrel is tilted about
        /// the swing direction). Tilts the undercut direction, so undercut balls get slicing sidespin.
        /// </summary>
        public double VerticalBatAngle;
        /// <summary>Time from swing start (button) to the bat reaching the contact plane, s.</summary>
        public double SwingDuration;
        /// <summary>Barrel radius, m.</summary>
        public double BarrelRadius;
        /// <summary>Collision efficiency q at the sweet spot (Nathan's e_A).</summary>
        public double SweetSpotEfficiency;
        /// <summary>Quadratic fall-off of q with distance d from the sweet spot, q = q0 − k·d², toward the barrel tip (1/m²).</summary>
        public double EfficiencyFalloffTip;
        /// <summary>Same toward the handle (1/m²); q falls off much more slowly on that side.</summary>
        public double EfficiencyFalloffHandle;
        /// <summary>Largest along-barrel distance from the sweet spot that still makes contact (PCI half-width), m.</summary>
        public double BarrelHalfLength;
        /// <summary>Horizontal bat-angle change per second of timing error, rad/s (early → pull).</summary>
        public double SprayRate;
        /// <summary>Largest |timing error| that can still make contact, s.</summary>
        public double MaxTimingError;
        /// <summary>Tangential coefficient of restitution e_x (contact-point slip reverses by this fraction; 0 = rolling).</summary>
        public double TangentialRestitution;
        /// <summary>
        /// Tangential recoil factor r_x of the bat (Kensrud, Nathan &amp; Smith 2017): the tangential impulse is divided by
        /// 1 + r_x, r_x = (m·α/(1+α))·(1/M + b²/I₀ + R²/I_z), α = 0.4; dominated by the bat rolling about its long axis.
        /// </summary>
        public double TangentialRecoil;
        /// <summary>Ball–bat friction coefficient capping the tangential impulse at μ × normal impulse.</summary>
        public double Friction;

        /// <summary>Throws <see cref="ArgumentException"/> if any field is outside its physical or playable range.</summary>
        public void Validate()
        {
            Require(BatSpeed > 0.0 && Finite(BatSpeed), nameof(BatSpeed));
            Require(Math.Abs(AttackAngle) < 0.5 * Math.PI, nameof(AttackAngle));
            Require(Math.Abs(VerticalBatAngle) < 0.5 * Math.PI, nameof(VerticalBatAngle));
            Require(SwingDuration >= 0.0 && Finite(SwingDuration), nameof(SwingDuration));
            Require(BarrelRadius > 0.0 && Finite(BarrelRadius), nameof(BarrelRadius));
            Require(SweetSpotEfficiency >= 0.0 && SweetSpotEfficiency <= 1.0, nameof(SweetSpotEfficiency));
            Require(EfficiencyFalloffTip >= 0.0 && Finite(EfficiencyFalloffTip), nameof(EfficiencyFalloffTip));
            Require(EfficiencyFalloffHandle >= 0.0 && Finite(EfficiencyFalloffHandle), nameof(EfficiencyFalloffHandle));
            Require(BarrelHalfLength > 0.0 && Finite(BarrelHalfLength), nameof(BarrelHalfLength));
            Require(MaxTimingError > 0.0 && Finite(MaxTimingError), nameof(MaxTimingError));
            // The bat may not yaw past 90°, or the swing would point back at the catcher.
            Require(SprayRate >= 0.0 && SprayRate * MaxTimingError < 0.5 * Math.PI, nameof(SprayRate));
            Require(TangentialRestitution >= 0.0 && TangentialRestitution <= 1.0, nameof(TangentialRestitution));
            Require(TangentialRecoil >= 0.0 && Finite(TangentialRecoil), nameof(TangentialRecoil));
            Require(Friction >= 0.0 && Finite(Friction), nameof(Friction));
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static void Require(bool condition, string field)
        {
            if (!condition) throw new ArgumentException($"SwingParameters.{field} is out of range.", field);
        }

        /// <summary>Bat speed (mph) from Power: 72 + 5·r̂ → 67–77 mph (TASK-019; MLB average 72 mph, 2025 qualified P10 66.5 /
        /// P90 75.8 — DERIVED, Docs/BATTER_AI.md). The physical input: exit velocity follows from the collision.</summary>
        public static double BatSpeedMph(int power) => 72.0 + 5.0 * RatingScale.Unit(power);

        /// <summary><paramref name="batter"/>'s swing: the default swing from his side, at his bat speed.</summary>
        public static SwingParameters For(PlayerProfile batter)
        {
            SwingParameters p = Default;
            p.Side = batter.Bats;
            p.BatSpeed = Units.MphToMetersPerSecond(BatSpeedMph(batter.Ratings.Power));
            return p;
        }

        /// <summary>A typical MLB right-handed hitter's swing (see Docs/HITTING.md).</summary>
        public static SwingParameters Default => new SwingParameters
        {
            Side = BatterSide.Right,
            BatSpeed = Units.MphToMetersPerSecond(72.0),
            AttackAngle = Units.DegreesToRadians(10.0),
            VerticalBatAngle = Units.DegreesToRadians(32.0),
            SwingDuration = 0.150,
            BarrelRadius = Units.InchesToMeters(2.61) / 2.0,
            SweetSpotEfficiency = 0.21,
            EfficiencyFalloffTip = 0.012 / (Units.MetersPerInch * Units.MetersPerInch),
            EfficiencyFalloffHandle = 0.003 / (Units.MetersPerInch * Units.MetersPerInch),
            BarrelHalfLength = Units.InchesToMeters(5.0),
            SprayRate = Units.DegreesToRadians(1.2) / 0.001,
            MaxTimingError = 0.035,
            TangentialRestitution = 0.30,
            TangentialRecoil = 0.30,
            Friction = 0.20,
        };
    }
}
