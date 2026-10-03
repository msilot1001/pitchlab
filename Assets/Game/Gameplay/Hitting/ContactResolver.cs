using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Hitting
{
    public enum ContactOutcome
    {
        Contact,
        MissTiming,
        MissOver,
        MissUnder,
        MissOffBarrel,
        /// <summary>Bat and ball touch but are not approaching along the line of centres (grazing): no batted ball.</summary>
        MissGlancing,
        NoPitch,
        /// <summary>Non-finite swing time or PCI.</summary>
        InvalidInput,
    }

    /// <summary>Display-only timing labels; the authoritative value is <see cref="ContactResult.TimingError"/>.</summary>
    public enum TimingLabel
    {
        VeryEarly,
        Early,
        Good,
        Late,
        VeryLate,
    }

    /// <summary>The player's swing: when it started (simulation seconds since release) and where the PCI was.</summary>
    public readonly struct SwingInput
    {
        public readonly double StartTime;
        /// <summary>PCI centre in the contact plane: X (catcher's view, + = first base) and Z (height), metres.</summary>
        public readonly double PciX, PciZ;

        public SwingInput(double startTime, double pciX, double pciZ)
        {
            StartTime = startTime;
            PciX = pciX;
            PciZ = pciZ;
        }
    }

    public readonly struct ContactResult
    {
        public readonly ContactOutcome Outcome;
        /// <summary>Bat arrival minus ball arrival at the contact plane, s. Negative = early.</summary>
        public readonly double TimingError;
        /// <summary>
        /// Ball centre relative to the bat's sweet spot at contact time, in the bat's frame (m): along the barrel axis
        /// (+ = toward first base for an unyawed bat) and perpendicular to barrel and swing direction (+ = bat under ball).
        /// </summary>
        public readonly double OffsetAlongBarrel, VerticalOffset;
        public readonly double CollisionEfficiency;
        /// <summary>Ball state just after contact (time = contact time); default when there was no contact.</summary>
        public readonly BallState BattedBall;

        public ContactResult(ContactOutcome outcome, double timingError, double offsetAlongBarrel, double verticalOffset, double q, BallState battedBall)
        {
            Outcome = outcome;
            TimingError = timingError;
            OffsetAlongBarrel = offsetAlongBarrel;
            VerticalOffset = verticalOffset;
            CollisionEfficiency = q;
            BattedBall = battedBall;
        }

        public bool IsContact => Outcome == ContactOutcome.Contact;
        public double ExitSpeed => BattedBall.Velocity.Length;
        /// <summary>Degrees above horizontal.</summary>
        public double LaunchAngleDegrees => Units.RadiansToDegrees(Math.Asin(BattedBall.Velocity.Z / ExitSpeed));
        /// <summary>Degrees from straight-away centre field; + toward first base / right field.</summary>
        public double SprayAngleDegrees => Units.RadiansToDegrees(Math.Atan2(BattedBall.Velocity.X, BattedBall.Velocity.Y));

        public TimingLabel Timing =>
            TimingError < -0.020 ? TimingLabel.VeryEarly :
            TimingError < -0.007 ? TimingLabel.Early :
            TimingError <= 0.007 ? TimingLabel.Good :
            TimingError <= 0.020 ? TimingLabel.Late : TimingLabel.VeryLate;
    }

    /// <summary>
    /// Deterministic ball–bat contact (Docs/HITTING.md). Timing decides when the bat meets the ball (and so where the
    /// ball is and how the bat is yawed); the PCI decides where on the bat. Normal direction (line of centres): collision
    /// efficiency q, exit = q·incoming + (1+q)·bat (Nathan 2003). Tangential: contact-point slip reverses by e_x
    /// (Kensrud, Nathan &amp; Smith 2017), limited by Coulomb friction; this sets tangential exit velocity and spin.
    /// Incoming spin is ignored (its effect nearly cancels for e_x ≈ 0.4; Nathan et al. 2012). No Unity physics.
    /// </summary>
    public static class ContactResolver
    {
        public static ContactResult Resolve(HittingPitch pitch, SwingInput swing, SwingParameters p)
        {
            p.Validate();
            if (!pitch.ReachesContactPlane) return new ContactResult(ContactOutcome.NoPitch, double.NaN, double.NaN, double.NaN, 0.0, default);
            if (!Finite(swing.StartTime) || !Finite(swing.PciX) || !Finite(swing.PciZ))
                return new ContactResult(ContactOutcome.InvalidInput, double.NaN, double.NaN, double.NaN, 0.0, default);

            double contactTime = swing.StartTime + p.SwingDuration;
            double timingError = contactTime - pitch.IdealContactTime;
            // Too early/late, or later than the recorded flight (the ball is already on the ground or past the catcher).
            if (!(Math.Abs(timingError) <= p.MaxTimingError) || contactTime > pitch.Flight.Final.Time)
                return new ContactResult(ContactOutcome.MissTiming, timingError, double.NaN, double.NaN, 0.0, default);

            // The authoritative ball state at contact (cubic Hermite between 5 ms RK4 samples: sub-micrometre error).
            BallState ball = pitch.Flight.StateAt(contactTime);

            // Swing direction: toward the field (+Y), yawed by timing (early → pull side), tilted up by the attack angle.
            double pullSign = p.Side == BatterSide.Right ? 1.0 : -1.0;  // right-handed hitters pull toward −X (third base)
            double yaw = pullSign * p.SprayRate * timingError;
            var horizontal = new Vector3d(Math.Sin(yaw), Math.Cos(yaw), 0.0);
            Vector3d swingDirection = Math.Cos(p.AttackAngle) * horizontal + Math.Sin(p.AttackAngle) * new Vector3d(0.0, 0.0, 1.0);
            Vector3d barrelAxis = Vector3d.Cross(horizontal, new Vector3d(0.0, 0.0, 1.0));      // horizontal, ⟂ swing
            Vector3d up = Vector3d.Cross(barrelAxis, swingDirection).Normalized;               // ⟂ barrel and swing, upward

            // The bat's sweet spot travels along its swing plane, through the PCI point at the contact plane: early
            // contact happens further out front and, with an upward attack angle, higher. Offsets are measured in the
            // bat's frame (barrel axis, and perpendicular to barrel and swing).
            var sweetSpot = new Vector3d(swing.PciX, ball.Position.Y, swing.PciZ + (ball.Position.Y - p.ContactPlaneY) * Math.Tan(p.AttackAngle));
            Vector3d offset = ball.Position - sweetSpot;
            double alongBarrel = Vector3d.Dot(offset, barrelAxis);
            double vertical = Vector3d.Dot(offset, up);
            double centres = BallProperties.Baseball.Radius + p.BarrelRadius;
            if (vertical >= centres) return new ContactResult(ContactOutcome.MissUnder, timingError, alongBarrel, vertical, 0.0, default);
            if (vertical <= -centres) return new ContactResult(ContactOutcome.MissOver, timingError, alongBarrel, vertical, 0.0, default);
            if (Math.Abs(alongBarrel) > p.BarrelHalfLength) return new ContactResult(ContactOutcome.MissOffBarrel, timingError, alongBarrel, vertical, 0.0, default);

            // Line of centres from bat axis to ball centre: tilted up when the bat is under the ball (vertical > 0).
            double phi = Math.Asin(vertical / centres);
            Vector3d normal = Math.Cos(phi) * swingDirection + Math.Sin(phi) * up;

            // Barrel tip points toward first base (+X) for a right-handed hitter, third base for a left-handed one.
            bool towardTip = alongBarrel * pullSign > 0.0;
            double q = Math.Max(0.0, p.SweetSpotEfficiency - (towardTip ? p.EfficiencyFalloffTip : p.EfficiencyFalloffHandle) * Sq(alongBarrel));
            Vector3d batVelocity = p.BatSpeed * swingDirection;
            double incomingNormal = -Vector3d.Dot(ball.Velocity, normal);   // > 0: ball moving into the bat
            double outNormal = q * incomingNormal + (1.0 + q) * Vector3d.Dot(batVelocity, normal);

            Vector3d relative = ball.Velocity - batVelocity;
            Vector3d relativeTangential = relative - Vector3d.Dot(relative, normal) * normal;
            Vector3d batTangential = batVelocity - Vector3d.Dot(batVelocity, normal) * normal;
            // Tangential impulse per unit mass J: a sphere with I = 0.4·m·r² changes its contact-point slip by (7/2)·J, so
            // reversing the slip by e_x needs J = (2/7)(1 + e_x)·slip, reduced by the bat's recoil 1/(1 + r_x);
            // friction caps J at μ·(normal impulse).
            double slip = relativeTangential.Length;
            double normalImpulse = outNormal + incomingNormal;
            if (!(normalImpulse > 0.0)) return new ContactResult(ContactOutcome.MissGlancing, timingError, alongBarrel, vertical, q, default);
            double tangentialImpulse = Math.Min((2.0 / 7.0) * (1.0 + p.TangentialRestitution) / (1.0 + p.TangentialRecoil) * slip,
                p.Friction * normalImpulse);
            Vector3d slipDirection = slip > 0.0 ? relativeTangential / slip : Vector3d.Zero;
            Vector3d outVelocity = outNormal * normal + batTangential + relativeTangential - tangentialImpulse * slipDirection;
            // The impulse −J·t̂ acts at the contact point −r·n̂, spinning the ball about n̂ × t̂ (ω = (5/2)·J/r).
            Vector3d spin = 2.5 * tangentialImpulse / BallProperties.Baseball.Radius * Vector3d.Cross(normal, slipDirection);

            var batted = new BallState(contactTime, ball.Position, outVelocity, spin);
            return new ContactResult(ContactOutcome.Contact, timingError, alongBarrel, vertical, q, batted);
        }

        private static double Sq(double v) => v * v;
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
