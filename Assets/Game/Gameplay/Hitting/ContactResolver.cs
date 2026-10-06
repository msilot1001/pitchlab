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
        /// <summary>Stopped before the offer point (TASK-024): no swing — the pitch is called as a take.</summary>
        CheckedSwing,
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
        /// <summary>
        /// The offer point (TASK-024): a swing stopped this long (s) or more before its contact time is no swing. No rule
        /// defines the offer; umpires judge whether the bat head passed the front of the plate — with the 150 ms swing, about
        /// its last 60 ms (ASSUMED). A check after it is too late: the swing goes on.
        /// </summary>
        public const double OfferLead = 0.060;

        public readonly double StartTime;
        /// <summary>PCI centre in the contact plane: X (catcher's view, + = first base) and Z (height), metres.</summary>
        public readonly double PciX, PciZ;
        /// <summary>When the batter tried to stop the swing — or pulled a bunt back (s after release; NaN: he did not).</summary>
        public readonly double CheckTime;
        /// <summary>A bunt (TASK-025): <see cref="StartTime"/> is when he squared around; the bat waits for the ball, which it
        /// meets on its arrival at the contact plane with the PCI where it was then.</summary>
        public readonly bool IsBunt;
        /// <summary>A bunt's direction: the bat, squared to the ball's incoming path, angled this many radians toward first base
        /// (+, right side) or third base (−).</summary>
        public readonly double BuntAim;

        public SwingInput(double startTime, double pciX, double pciZ, double checkTime = double.NaN, bool isBunt = false, double buntAim = 0.0)
        {
            StartTime = startTime;
            PciX = pciX;
            PciZ = pciZ;
            CheckTime = checkTime;
            IsBunt = isBunt;
            BuntAim = buntAim;
        }

        /// <summary>A bunt squared at <paramref name="squareTime"/> with the bat at the PCI (<paramref name="pciX"/>,
        /// <paramref name="pciZ"/>) when the ball arrives, aimed <paramref name="aim"/> radians toward first base.</summary>
        public static SwingInput Bunt(double squareTime, double pciX, double pciZ, double aim, double pullBack = double.NaN) =>
            new SwingInput(squareTime, pciX, pciZ, pullBack, true, aim);

        /// <summary>The same swing, checked (a bunt: pulled back) at <paramref name="time"/>.</summary>
        public SwingInput CheckedAt(double time) => new SwingInput(StartTime, PciX, PciZ, time, IsBunt, BuntAim);

        /// <summary>The same bunt with the bat at (<paramref name="pciX"/>, <paramref name="pciZ"/>).</summary>
        public SwingInput WithPci(double pciX, double pciZ) => new SwingInput(StartTime, pciX, pciZ, CheckTime, IsBunt, BuntAim);

        /// <summary>
        /// When it became an attempt: a swing's offer point (the latest check that still stops it, <paramref name="swingDuration"/>
        /// long); a bunt's, once he is set (squared <paramref name="swingDuration"/> earlier: the bunt's set-up time).
        /// </summary>
        public double OfferTime(double swingDuration) => IsBunt ? StartTime + swingDuration : StartTime + swingDuration - OfferLead;

        /// <summary>The latest a bunt can be pulled back on a pitch arriving at <paramref name="arrival"/> (s).</summary>
        public static double PullBackDeadline(double arrival) => arrival - OfferLead;

        /// <summary>Whether the swing was stopped in time: no swing (a take).</summary>
        public bool IsChecked(double swingDuration) => CheckTime <= OfferTime(swingDuration);
    }

    public readonly struct ContactResult
    {
        public readonly ContactOutcome Outcome;
        /// <summary>Bat arrival minus ball arrival at the contact plane, s. Negative = early.</summary>
        public readonly double TimingError;
        /// <summary>
        /// Ball centre relative to the bat's sweet spot at contact time, in the level bat frame (m): along the barrel axis
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
    /// (Kensrud, Nathan &amp; Smith 2017), limited by Coulomb friction; this sets tangential exit velocity and spin. The
    /// barrel is tilted by the vertical bat angle, so spin is a full 3D vector: undercut → backspin plus slice, bat yaw
    /// (timing) → hook or slice. Incoming spin ⟂ the line of centres takes part in the slip (Nathan et al. 2012, Eq. 3,
    /// generalised to a recoiling bat). No Unity physics.
    /// </summary>
    public static class ContactResolver
    {
        public static ContactResult Resolve(HittingPitch pitch, SwingInput swing, SwingParameters p)
        {
            p.Validate();
            if (!pitch.ReachesContactPlane) return new ContactResult(ContactOutcome.NoPitch, double.NaN, double.NaN, double.NaN, 0.0, default);
            if (!Finite(swing.StartTime) || !Finite(swing.PciX) || !Finite(swing.PciZ))
                return new ContactResult(ContactOutcome.InvalidInput, double.NaN, double.NaN, double.NaN, 0.0, default);

            double contactTime = ContactTime(pitch, swing, p);
            double timingError = contactTime - pitch.IdealContactTime;
            if (swing.IsBunt)
            {
                // A bunt (TASK-025): pulled back in time is no attempt; squared too late, the bat is not there.
                if (swing.CheckTime <= SwingInput.PullBackDeadline(contactTime)) return new ContactResult(ContactOutcome.CheckedSwing, 0.0, double.NaN, double.NaN, 0.0, default);
                if (swing.StartTime > contactTime - p.SwingDuration) return new ContactResult(ContactOutcome.MissTiming, swing.StartTime + p.SwingDuration - contactTime, double.NaN, double.NaN, 0.0, default);
            }
            else if (swing.IsChecked(p.SwingDuration)) return new ContactResult(ContactOutcome.CheckedSwing, timingError, double.NaN, double.NaN, 0.0, default);
            // Too early/late, or later than the recorded flight (the ball is already on the ground or past the catcher).
            if (!(Math.Abs(timingError) <= p.MaxTimingError) || contactTime > pitch.Flight.Final.Time)
                return new ContactResult(ContactOutcome.MissTiming, timingError, double.NaN, double.NaN, 0.0, default);

            // The authoritative ball state at contact (cubic Hermite between 5 ms RK4 samples: sub-micrometre error).
            BallState ball = pitch.Flight.StateAt(contactTime);

            // Swing direction: toward the field (+Y), yawed by timing (early → pull side), tilted up by the attack angle.
            double pullSign = p.Side == BatterSide.Right ? 1.0 : -1.0;  // right-handed hitters pull toward −X (third base)
            // A bunt: the bat squared to the ball's incoming path (horizontal), then angled by his aim.
            double yaw = swing.IsBunt ? Math.Atan2(-ball.Velocity.X, -ball.Velocity.Y) + swing.BuntAim : pullSign * p.SprayRate * timingError;
            var horizontal = new Vector3d(Math.Sin(yaw), Math.Cos(yaw), 0.0);
            Vector3d swingDirection = Math.Cos(p.AttackAngle) * horizontal + Math.Sin(p.AttackAngle) * new Vector3d(0.0, 0.0, 1.0);
            Vector3d barrelAxis = Vector3d.Cross(horizontal, new Vector3d(0.0, 0.0, 1.0));      // horizontal, ⟂ swing
            Vector3d up = Vector3d.Cross(barrelAxis, swingDirection).Normalized;               // ⟂ barrel and swing, upward
            // The real barrel is tilted: its tip (+X for a right-handed hitter, −X for a left-handed one) is lowered by the
            // vertical bat angle, a rotation about the swing direction. PCI offsets stay in the level frame above (the PCI
            // is the barrel as the player sees it), so the tilt only turns the direction of undercut and overcut.
            double tilt = pullSign * p.VerticalBatAngle;
            Vector3d tiltedBarrel = Math.Cos(tilt) * barrelAxis + Math.Sin(tilt) * Vector3d.Cross(swingDirection, barrelAxis);
            Vector3d tiltedUp = Vector3d.Cross(tiltedBarrel, swingDirection).Normalized;

            // The bat's sweet spot travels along its swing plane, through the PCI point at the contact plane: early
            // contact happens further out front and, with an upward attack angle, higher. Offsets are measured in the
            // bat's frame (barrel axis, and perpendicular to barrel and swing).
            Vector3d sweetSpot = SweetSpot(pitch, swing, p, ball.Position);
            Vector3d offset = ball.Position - sweetSpot;
            double alongBarrel = Vector3d.Dot(offset, barrelAxis);
            double vertical = Vector3d.Dot(offset, up);
            // Where on the bat (+ toward the tip): from the end of the bat to near the hands (TASK-023).
            double towardTipDistance = alongBarrel * pullSign;
            if (towardTipDistance > p.TipReach || towardTipDistance < -p.HandleReach)
                return new ContactResult(ContactOutcome.MissOffBarrel, timingError, alongBarrel, vertical, 0.0, default);
            double centres = BallProperties.Baseball.Radius + BatRadiusAt(p, towardTipDistance);
            if (vertical >= centres) return new ContactResult(ContactOutcome.MissUnder, timingError, alongBarrel, vertical, 0.0, default);
            if (vertical <= -centres) return new ContactResult(ContactOutcome.MissOver, timingError, alongBarrel, vertical, 0.0, default);

            // Line of centres from bat axis to ball centre: tilted up when the bat is under the ball (vertical > 0).
            double phi = Math.Asin(vertical / centres);
            Vector3d normal = Math.Cos(phi) * swingDirection + Math.Sin(phi) * tiltedUp;

            // Barrel tip points toward first base (+X) for a right-handed hitter, third base for a left-handed one.
            bool towardTip = alongBarrel * pullSign > 0.0;
            double q = Math.Max(towardTip ? p.MinTipEfficiency : 0.0, p.SweetSpotEfficiency - (towardTip ? p.EfficiencyFalloffTip : p.EfficiencyFalloffHandle) * Sq(alongBarrel));
            // The bat turns about its pivot: slower toward the hands, faster toward the tip (TASK-023).
            Vector3d batVelocity = BatSpeedAt(p, towardTipDistance) * swingDirection;
            double incomingNormal = -Vector3d.Dot(ball.Velocity, normal);   // > 0: ball moving into the bat
            double outNormal = q * incomingNormal + (1.0 + q) * Vector3d.Dot(batVelocity, normal);

            Vector3d relative = ball.Velocity - batVelocity;
            Vector3d relativeTangential = relative - Vector3d.Dot(relative, normal) * normal;
            Vector3d batTangential = batVelocity - Vector3d.Dot(batVelocity, normal) * normal;
            // Incoming spin ⟂ the line of centres moves the ball's contact point (−r·n̂) and so enters the slip; with bat
            // recoil 1 − (5/7)(1 + e_x)/(1 + r_x) of it survives (2/7 at e_x = r_x = 0.3). Spin about n̂ itself is not
            // touched by a tangential impulse; torsional friction over the contact patch can remove it, so it is dropped [Approx].
            Vector3d incomingSpin = ball.Spin - Vector3d.Dot(ball.Spin, normal) * normal;
            Vector3d contactSlip = relativeTangential + Vector3d.Cross(incomingSpin, -BallProperties.Baseball.Radius * normal);
            // Tangential impulse per unit mass J: a sphere with I = 0.4·m·r² changes its contact-point slip by (7/2)·J, so
            // reversing the slip by e_x needs J = (2/7)(1 + e_x)·slip, reduced by the bat's recoil 1/(1 + r_x);
            // friction caps J at μ·(normal impulse).
            double slip = contactSlip.Length;
            double normalImpulse = outNormal + incomingNormal;
            if (!(normalImpulse > 0.0)) return new ContactResult(ContactOutcome.MissGlancing, timingError, alongBarrel, vertical, q, default);
            double tangentialImpulse = Math.Min((2.0 / 7.0) * (1.0 + p.TangentialRestitution) / (1.0 + p.TangentialRecoil) * slip,
                p.Friction * normalImpulse);
            Vector3d slipDirection = slip > 0.0 ? contactSlip / slip : Vector3d.Zero;
            Vector3d outVelocity = outNormal * normal + batTangential + relativeTangential - tangentialImpulse * slipDirection;
            // The impulse −J·t̂ acts at the contact point −r·n̂, spinning the ball about n̂ × t̂ (ω = (5/2)·J/r).
            Vector3d spin = incomingSpin + 2.5 * tangentialImpulse / BallProperties.Baseball.Radius * Vector3d.Cross(normal, slipDirection);

            var batted = new BallState(contactTime, ball.Position, outVelocity, spin);
            return new ContactResult(ContactOutcome.Contact, timingError, alongBarrel, vertical, q, batted);
        }

        /// <summary>
        /// Where the bat's sweet spot is at the swing's contact time (start + SwingDuration): on its swing plane through
        /// the PCI at the contact plane, at the ball's depth then. Exactly the point the contact offsets are measured
        /// from; presentation aims the visual bat here (hit or miss). Defined for any swing time within the pitch flight.
        /// </summary>
        public static Vector3d SweetSpotAtContact(HittingPitch pitch, SwingInput swing, SwingParameters p) =>
            SweetSpot(pitch, swing, p, pitch.Flight.StateAt(ContactTime(pitch, swing, p)).Position);

        /// <summary>When the bat meets the ball's path: a swing at its start + duration; a bunt when the ball arrives.</summary>
        public static double ContactTime(HittingPitch pitch, SwingInput swing, SwingParameters p) =>
            swing.IsBunt ? pitch.IdealContactTime : swing.StartTime + p.SwingDuration;

        private static Vector3d SweetSpot(HittingPitch pitch, SwingInput swing, SwingParameters p, Vector3d ball) =>
            new Vector3d(swing.PciX, ball.Y, swing.PciZ + (ball.Y - pitch.ContactPlaneY) * Math.Tan(p.AttackAngle));

        /// <summary>The bat's speed at <paramref name="towardTip"/> m from the sweet spot (+ toward the tip): a rotation about a
        /// pivot <see cref="SwingParameters.PivotRadius"/> from the sweet spot.</summary>
        public static double BatSpeedAt(SwingParameters p, double towardTip) => p.BatSpeed * (1.0 + towardTip / p.PivotRadius);

        /// <summary>The bat's radius at <paramref name="towardTip"/> m from the sweet spot: the barrel's, tapering toward the handle.</summary>
        public static double BatRadiusAt(SwingParameters p, double towardTip)
        {
            double d = -towardTip;   // toward the handle
            if (d <= p.TaperStart) return p.BarrelRadius;
            double u = Math.Min(1.0, (d - p.TaperStart) / (p.HandleReach - p.TaperStart));
            return p.BarrelRadius + u * (p.HandleRadius - p.BarrelRadius);
        }

        private static double Sq(double v) => v * v;
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
