using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>A timestamped placement of the CPU batter's PCI: simulation time (s after release) and the contact-plane point (m).</summary>
    public readonly struct AimEvent
    {
        public AimEvent(double time, double x, double z)
        {
            Time = time;
            X = x;
            Z = z;
        }

        public double Time { get; }
        public double X { get; }
        public double Z { get; }
    }

    /// <summary>
    /// What the CPU batter did with one pitch, as input events — the same kind a human produces: PCI placements over time
    /// and (when he swings) one swing press. The swing reads the PCI where it was at the press (<see cref="PciAt"/>), as
    /// the human swing does. The rest is his perception at the decision, for debugging.
    /// </summary>
    public sealed class BatterPlan
    {
        internal readonly List<AimEvent> Events = new List<AimEvent>();

        public IReadOnlyList<AimEvent> Aim => Events;
        public bool Swing { get; internal set; }
        /// <summary>The swing press (s after release; NaN for a take).</summary>
        public double SwingStart { get; internal set; } = double.NaN;
        /// <summary>When he decided (s after release; NaN if he never got to decide — then he took).</summary>
        public double DecisionTime { get; internal set; } = double.NaN;
        /// <summary>The latest instant of the ball's flight he had seen when his last event was made (s).</summary>
        public double LastObservation { get; internal set; } = double.NaN;
        /// <summary>At the decision: where he expected the pitch to cross the front of the plate (m) and to reach the contact
        /// plane (s), how sure he was it is a strike, and his chance of swinging.</summary>
        public double PredictedX { get; internal set; } = double.NaN;
        public double PredictedZ { get; internal set; } = double.NaN;
        public double PredictedContactTime { get; internal set; } = double.NaN;
        public double StrikeBelief { get; internal set; } = double.NaN;
        public double SwingChance { get; internal set; } = double.NaN;

        /// <summary>The PCI (contact-plane m) at <paramref name="time"/>: the latest placement at or before it.</summary>
        public (double X, double Z) PciAt(double time)
        {
            AimEvent e = Aim[0];
            foreach (AimEvent a in Aim)
                if (a.Time <= time) e = a;
            return (e.X, e.Z);
        }

        /// <summary>His swing as the contact model takes it (null for a take).</summary>
        public SwingInput? Input
        {
            get
            {
                if (!Swing) return null;
                (double x, double z) = PciAt(SwingStart);
                return new SwingInput(SwingStart, x, z);
            }
        }
    }

    /// <summary>
    /// The CPU hitter (TASK-019; Docs/BATTER_AI.md). He sees the ball only up to the present, late (visuomotor delay) and
    /// noisily (angular noise, so the ball is seen worse far away); from what he has seen he fits its path (constant
    /// acceleration — the movement he observes) and predicts where and when it will arrive. Shortly before he must start
    /// the swing he decides — from that prediction, his confidence, the count and his discipline — and, if he swings,
    /// presses at his predicted time with his own timing error, while his PCI follows his prediction plus his own aim error.
    /// The swing then goes through <see cref="ContactResolver"/> like a human's. He never reads the flight beyond what he has
    /// seen, nor the call, nor the contact result.
    /// </summary>
    public static class CpuBatter
    {
        /// <summary>He samples the ball every 10 ms (a deterministic perception tick, independent of frames).</summary>
        public const double Tick = 0.010;
        /// <summary>Samples before he has a path.</summary>
        public const int MinSamples = 5;
        /// <summary>The batter's eye (simulation m): over the plate's front edge at 1.5 m (ASSUMED; he stands beside the plate).</summary>
        public static readonly Vector3d Eye = new Vector3d(0.0, PitchingGeometry.PlateFrontY, 1.5);
        /// <summary>He commits this long before he must start the swing (s).</summary>
        public const double CommitLead = 0.010;
        /// <summary>What he expects before seeing the pitch move (m/s²): gravity and a typical drag (≈ 10 m/s² against a
        /// 90-mph pitch's motion, toward +Y; DERIVED), ± 3 m/s² per axis (TUNED): he expects a straight pitch, so movement
        /// he has not yet seen enough of partly fools him.</summary>
        public static readonly Vector3d PriorAcceleration = new Vector3d(0.0, 10.0, -9.81);
        public const double PriorAccelerationSigma = 3.0;

        /// <summary>The delay (s) between seeing the ball and acting on it, beyond the swing itself: 0.06 ∓ 0.015 by Vision.
        /// With <see cref="CommitLead"/> and the 150-ms swing his last usable look is ≈ 220 ms before contact — the commit
        /// point is ≈ 175 ms before contact (≈ 24 ft; DERIVED) plus the processing delay (ASSUMED).</summary>
        public static double Latency(PlayerRatings r) => 0.06 - 0.015 * RatingScale.Unit(r.Vision);
        /// <summary>Angular noise of one look at the ball (rad, per axis; also used for depth): TUNED so an average hitter's
        /// predicted crossing misses by a few cm; ∓ 30 % by Vision.</summary>
        public static double AngleNoise(PlayerRatings r) => AngleNoiseBase * (1.0 - 0.3 * RatingScale.Unit(r.Vision));
        public const double AngleNoiseBase = 0.00035;
        /// <summary>Depth is judged from looming, far less precisely than direction: σ_depth = DepthScale · σθ · d² / (2 r_ball)
        /// (the ball's angular size is 2r/d; TUNED scale — the full looming bound is ≈ 10 % of the distance per look).</summary>
        public const double DepthScale = 0.1;
        /// <summary>How sharply he separates strikes from balls near the edge (m): 0.03 ∓ 30 % by Vision (TUNED).</summary>
        public static double JudgementSigma(PlayerRatings r) => 0.03 * (1.0 - 0.3 * RatingScale.Unit(r.Vision));
        /// <summary>His motor timing error (s, SD): 10 ms ∓ 30 % by Contact (TUNED; the sweet-spot window is ≈ 9 ms).</summary>
        public static double TimingSigma(PlayerRatings r) => 0.010 * (1.0 - 0.3 * RatingScale.Unit(r.Contact));
        /// <summary>His hand–eye aim error along the barrel (m): ∓ 30 % by Contact (TUNED, TASK-023: sets how squarely he meets
        /// the ball — exit speeds).</summary>
        public static double AimSigmaAlong(PlayerRatings r) => AimAlong * (1.0 - 0.3 * RatingScale.Unit(r.Contact));
        /// <summary>His hand–eye aim error across the barrel, up and down (m): ∓ 30 % by Contact (TUNED, TASK-023: sets
        /// whiffs and the launch-angle spread).</summary>
        public static double AimSigmaVertical(PlayerRatings r) => AimVertical * (1.0 - 0.3 * RatingScale.Unit(r.Contact));
        /// <summary>Base aim spreads (m) along and across the barrel, his lift intent (m: he aims the barrel this far under the
        /// ball's centre — hitters swing to lift), and how much harder a pitch is to square up the further outside the zone he
        /// sees it (× 1 + ReachPenalty per metre outside). TUNED to MLB batted-ball and contact rates (Docs/OFFENSE_CALIBRATION.md).</summary>
        public const double AimAlong = 0.11, AimVertical = 0.025, LiftIntent = 0.005, ReachPenalty = 10.0;
        /// <summary>Discipline scales the chase rate 1 ∓ 45 % (MLB O-Swing% spans ≈ 18–40 % around 28 %; ASSUMED).</summary>
        public const double DisciplineChaseScale = 0.45;
        /// <summary>Chasing falls off with how far outside he judges the pitch: × <see cref="ChaseScale"/> · exp(−d / falloff)
        /// (TUNED so the average O-Swing% over his pitches matches the count's rate).</summary>
        public const double ChaseScale = 3.0, ChaseFalloff = 0.12;

        /// <summary>
        /// MLB 2024 swing rates by count [balls, strikes], in the zone (Savant zones 1–9) and outside it (11–14): MEASURED
        /// from the Statcast search CSV (Docs/BATTER_AI.md). Not smooth (take signs at 3-0), so a table, not a formula.
        /// </summary>
        private static readonly double[,] ZoneSwing =
        {
            { 0.459, 0.746, 0.866 },
            { 0.603, 0.779, 0.886 },
            { 0.579, 0.782, 0.895 },
            { 0.135, 0.725, 0.889 },
        };

        private static readonly double[,] ChaseSwing =
        {
            { 0.160, 0.282, 0.331 },
            { 0.222, 0.312, 0.385 },
            { 0.210, 0.321, 0.422 },
            { 0.035, 0.271, 0.448 },
        };

        public static double ZoneSwingRate(Count c) => ZoneSwing[c.Balls, c.Strikes];
        public static double ChaseSwingRate(Count c, PlayerRatings r) => ChaseSwing[c.Balls, c.Strikes] * (1.0 - DisciplineChaseScale * RatingScale.Unit(r.Discipline));

        /// <summary>The deviates of one pitch for the batter: the game's seed, the batter, the plate appearance, the pitch.</summary>
        public static SeedStream StreamFor(int gameSeed, string batterId, int plateAppearance, int pitchNumber) =>
            new SeedStream(gameSeed, SeedStream.Key(batterId), plateAppearance, pitchNumber, 0xBA77E5);

        /// <summary>The ball as the batter can see it: its position on the authoritative flight (after the flight ends, where it stopped).</summary>
        public static Func<double, Vector3d> Observe(HittingPitch pitch) =>
            t => pitch.Flight.StateAt(Math.Min(Math.Max(t, pitch.Flight.First.Time), pitch.Flight.Final.Time)).Position;

        /// <summary>
        /// His events for one pitch. <paramref name="ballAt"/> is queried only at past instants: at no point does an event
        /// depend on the ball after the time its latest look was made. <paramref name="contactPlaneY"/> is where his swing meets
        /// the ball (geometry, <see cref="HittingPitch.ContactPlaneY"/>).
        /// </summary>
        public static BatterPlan Plan(Func<double, Vector3d> ballAt, PlayerProfile batter, Count count, SwingParameters swing, double contactPlaneY, ref SeedStream stream)
        {
            PlayerRatings r = batter.Ratings;
            double latency = Latency(r), noise = AngleNoise(r);
            // His deviates for this pitch, drawn first so the sequence never depends on what he saw.
            double decide = stream.Unit(), timing = TimingSigma(r) * stream.Normal();
            double aimX = AimSigmaAlong(r) * stream.Normal(), aimZ = AimSigmaVertical(r) * stream.Normal();

            double zoneMid = 0.5 * (batter.ZoneBottom + batter.ZoneTop);
            var plan = new BatterPlan();
            plan.Events.Add(new AimEvent(0.0, 0.0, zoneMid));   // set at the middle of his zone
            var fit = new PathFit();
            for (int i = 0; i * Tick < HittingPitch.MaxFlightTime; i++)
            {
                double seen = i * Tick, now = seen + latency;
                Vector3d p = ballAt(seen);
                double d = (p - Eye).Length, sigma = noise * d, sigmaDepth = DepthScale * noise * d * d / (2.0 * BallProperties.Baseball.Radius);
                Vector3d look = p + new Vector3d(sigma * stream.Normal(), sigmaDepth * stream.Normal(), sigma * stream.Normal());
                if (look.Y < contactPlaneY || (plan.Swing && now > plan.SwingStart)) break;   // past him, or the swing is under way
                fit.Add(seen, look, sigma, sigmaDepth);
                if (fit.Count < MinSamples || !fit.Solve()) continue;
                double contactTime = fit.TimeAtY(contactPlaneY, seen);
                if (double.IsNaN(contactTime)) continue;   // no usable path yet
                Vector3d atContact = fit.At(contactTime);
                // Out of the zone (as he sees it) the bat is harder to put on the ball: his aim error grows with the distance.
                double reach = 1.0;
                {
                    double plateTime = fit.TimeAtY(PitchingGeometry.PlateFrontY, seen);
                    if (!double.IsNaN(plateTime))
                    {
                        Vector3d atPlate0 = fit.At(plateTime);
                        reach += ReachPenalty * Math.Max(0.0, -SignedZoneDistance(atPlate0.X, atPlate0.Z, batter.ZoneBottom, batter.ZoneTop));
                    }
                }

                // The PCI lives in the aimable area, as a human's does. Under the ball's centre by his lift intent.
                (double u, double v) = PciFrame.Default.ToNormalized(atContact.X + reach * aimX, atContact.Z - LiftIntent + reach * aimZ);
                (double pciX, double pciZ) = PciFrame.Default.ToMeters(u, v);
                plan.Events.Add(new AimEvent(now, pciX, pciZ));
                plan.LastObservation = seen;
                // He commits CommitLead before his (pre-drawn) press would start, so an early press is never cut short.
                if (plan.Swing || now < contactTime - swing.SwingDuration + Math.Min(timing, 0.0) - CommitLead) continue;

                // The decision.
                Vector3d atPlate = fit.At(fit.TimeAtY(PitchingGeometry.PlateFrontY, seen));
                if (double.IsNaN(atPlate.X)) break;   // no prediction at the plate: he takes
                double edge = SignedZoneDistance(atPlate.X, atPlate.Z, batter.ZoneBottom, batter.ZoneTop);
                double strike = 1.0 / (1.0 + Math.Exp(-1.702 * edge / JudgementSigma(r)));   // logistic ≈ normal CDF
                double chase = Math.Min(1.0, ChaseSwingRate(count, r) * ChaseScale * Math.Exp(-Math.Max(0.0, -edge) / ChaseFalloff));
                plan.DecisionTime = now;
                plan.PredictedX = atPlate.X;
                plan.PredictedZ = atPlate.Z;
                plan.PredictedContactTime = contactTime;
                plan.StrikeBelief = strike;
                plan.SwingChance = strike * ZoneSwingRate(count) + (1.0 - strike) * chase;
                if (decide >= plan.SwingChance) break;   // take
                plan.Swing = true;
                plan.SwingStart = Math.Max(now, contactTime - swing.SwingDuration + timing);
            }

            return plan;
        }

        /// <summary>Distance (m) from the edge of the zone (widened by the ball's radius, as the call): + inside, − outside.</summary>
        public static double SignedZoneDistance(double x, double z, double bottom, double top)
        {
            double r = BallProperties.Baseball.Radius, halfWidth = StrikeZone.HalfWidth + r;
            double dx = Math.Abs(x) - halfWidth, dz = Math.Max(bottom - r - z, z - top - r);
            if (dx <= 0.0 && dz <= 0.0) return -Math.Max(dx, dz);
            return -Math.Sqrt(Sq(Math.Max(dx, 0.0)) + Sq(Math.Max(dz, 0.0)));
        }

        private static double Sq(double v) => v * v;

        /// <summary>
        /// His estimate of the path p(t) = a + b·t + c·t² from noisy looks (weights 1/σ²), with a prior on the acceleration
        /// 2c: gravity and a typical drag (<see cref="PriorAcceleration"/>) ± <see cref="PriorAccelerationSigma"/> per axis —
        /// what a hitter expects before he has seen the pitch move. The longer he watches, the more the observed movement
        /// overrides the prior. Running sums: O(1) per look.
        /// </summary>
        private sealed class PathFit
        {
            private readonly AxisFit _x = new AxisFit(), _y = new AxisFit(), _z = new AxisFit();

            public int Count { get; private set; }

            /// <summary>One look: across (x, z) with σ, in depth (y) with <paramref name="sigmaDepth"/>.</summary>
            public void Add(double t, Vector3d p, double sigma, double sigmaDepth)
            {
                _x.Add(t, p.X, sigma);
                _y.Add(t, p.Y, sigmaDepth);
                _z.Add(t, p.Z, sigma);
                Count++;
            }

            public bool Solve() =>
                _x.Solve(0.5 * PriorAcceleration.X) && _y.Solve(0.5 * PriorAcceleration.Y) && _z.Solve(0.5 * PriorAcceleration.Z);

            public Vector3d At(double t) => new Vector3d(_x.At(t), _y.At(t), _z.At(t));

            /// <summary>When the estimated path reaches depth <paramref name="y"/> after <paramref name="from"/> (NaN: it does not).</summary>
            public double TimeAtY(double y, double from)
            {
                double t = from;
                for (int i = 0; i < 8; i++)
                {
                    double vy = _y.B + 2.0 * _y.C * t;
                    if (!(vy < -1e-3)) return double.NaN;   // not coming toward the plate
                    t -= (_y.At(t) - y) / vy;
                }

                return t >= from && Math.Abs(_y.At(t) - y) < 1e-6 ? t : double.NaN;
            }
        }

        /// <summary>One coordinate: weighted least squares of a + b·t + c·t² with the prior c ~ N(c0, (σa/2)²) (MAP).</summary>
        private sealed class AxisFit
        {
            private double _s0, _s1, _s2, _s3, _s4, _p0, _p1, _p2;

            public double A { get; private set; }
            public double B { get; private set; }
            public double C { get; private set; }

            public void Add(double t, double p, double sigma)
            {
                double w = 1.0 / (sigma * sigma), t2 = t * t;
                _s0 += w;
                _s1 += w * t;
                _s2 += w * t2;
                _s3 += w * t2 * t;
                _s4 += w * t2 * t2;
                _p0 += w * p;
                _p1 += w * t * p;
                _p2 += w * t2 * p;
            }

            /// <summary>The normal equations with the prior added to the c row (Cramer's rule).</summary>
            public bool Solve(double c0)
            {
                double sc = 0.5 * PriorAccelerationSigma, lambda = 1.0 / (sc * sc), s4 = _s4 + lambda, p2 = _p2 + lambda * c0;
                double det = Det(_s0, _s1, _s2, _s1, _s2, _s3, _s2, _s3, s4);
                if (!(Math.Abs(det) > 1e-18)) return false;
                A = Det(_p0, _s1, _s2, _p1, _s2, _s3, p2, _s3, s4) / det;
                B = Det(_s0, _p0, _s2, _s1, _p1, _s3, _s2, p2, s4) / det;
                C = Det(_s0, _s1, _p0, _s1, _s2, _p1, _s2, _s3, p2) / det;
                return true;
            }

            public double At(double t) => A + t * B + t * t * C;

            private static double Det(double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
                a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        }
    }
}
