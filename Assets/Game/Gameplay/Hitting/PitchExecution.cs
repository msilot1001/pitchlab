using System;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>How the executed pitch differed from the intended one (for records and debugging).</summary>
    public readonly struct ExecutionError
    {
        public ExecutionError(double horizontalDeg, double verticalDeg, double speedMph, double spinRpm, double axisDeg, bool wild)
        {
            HorizontalDeg = horizontalDeg;
            VerticalDeg = verticalDeg;
            SpeedMph = speedMph;
            SpinRpm = spinRpm;
            AxisDeg = axisDeg;
            Wild = wild;
        }

        public double HorizontalDeg { get; }
        public double VerticalDeg { get; }
        public double SpeedMph { get; }
        public double SpinRpm { get; }
        public double AxisDeg { get; }
        /// <summary>From the heavy tail (a non-competitive miss).</summary>
        public bool Wild { get; }
    }

    /// <summary>
    /// A pitcher's pitches and their execution (TASK-018; Docs/PITCH_EXECUTION.md). Intent (pitch type, target) and execution
    /// stay separate: the intended pitch is the pitcher's own pitch aimed at the target (release angles only); the executed
    /// pitch perturbs the release angles, the speed, the spin rate and the spin axis by his command and a seeded draw; the
    /// pitch physics then decides where it really goes. Nothing here sets a plate location or a call.
    /// </summary>
    public static class PitchExecution
    {
        /// <summary>League-average speed by pitch type (mph): 2025 per-pitcher means of the Savant pitch-arsenal leaderboard
        /// (DERIVED): four-seam 93.8, sinker 93.2, slider 85.8, curveball 80.4, changeup 86.8.</summary>
        public static double LeagueSpeedMph(PitchType t) => t switch
        {
            PitchType.FourSeam => 93.8,
            PitchType.Sinker => 93.2,
            PitchType.Slider => 85.8,
            PitchType.Curveball => 80.4,
            _ => 86.8,
        };

        /// <summary>His speed on <paramref name="type"/>: league average + 3.2 mph·r̂(Velocity) (the 2025 P10–P90 spread of
        /// four-seam averages is ≈ ±3.2 mph, DERIVED) + his offset on the pitch.</summary>
        public static double SpeedMph(PlayerProfile pitcher, RepertoirePitch pitch) =>
            LeagueSpeedMph(pitch.Type) + 3.2 * RatingScale.Unit(pitcher.Ratings.Velocity) + pitch.VelocityOffsetMph;

        /// <summary>Movement scales the spin rate 1 ± 20 % (TUNED: lift grows sublinearly with spin — dlnC_L/dlnS ≈ 0.56 —
        /// so ± 20 % spin ≈ ± 11 % movement, ≈ ± 2 in of four-seam rise; a modest share of the real pitcher-to-pitcher spread).</summary>
        public const double MovementSpinScale = 0.20;

        /// <summary>
        /// The pitcher's own pitch now: the type's preset (release point, angles, spin axis — Docs/PHYSICS.md) at his speed —
        /// less any fatigue loss after <paramref name="pitchCount"/> pitches — and with his spin rate scaled by Movement; a
        /// left-hander's is the mirror image (<see cref="Mirror"/>). The aim is solved with this speed, so fatigue costs
        /// velocity, not a systematic miss.
        /// </summary>
        public static PitchInput PitcherPitch(PlayerProfile pitcher, PitchType type, int pitchCount = 0)
        {
            if (pitcher?.Repertoire == null) throw new ArgumentException("A pitcher with a repertoire.", nameof(pitcher));
            RepertoirePitch pitch = pitcher.Repertoire.Get(type);
            PitchInput p = PitchPresets.All[(int)type];
            p.SpeedMph = SpeedMph(pitcher, pitch) - FatigueSpeedLossMph(pitcher, pitchCount);
            p.SpinRateRpm *= 1.0 + MovementSpinScale * RatingScale.Unit(pitcher.Ratings.Movement);
            return pitcher.Throws == Hand.Left ? Mirror(p) : p;
        }

        /// <summary>
        /// The mirror image across the plane x = 0 (a left-hander's pitch from a right-hander's): release side and azimuth
        /// change sign; spin is a pseudovector, so under the reflection ω → (ωx, −ωy, −ωz): spin axis θ → −θ and gyro γ → −γ
        /// (with ω̂ = (cos γ cos θ, −sin γ, cos γ sin θ), PitchInput.SpinDirection). In still air (or a head/tail wind) the
        /// flight then mirrors exactly; a crosswind is not mirrored (physically right).
        /// </summary>
        public static PitchInput Mirror(PitchInput p)
        {
            p.ReleaseSideFeet = -p.ReleaseSideFeet;
            p.HorizontalAngleDegrees = -p.HorizontalAngleDegrees;
            p.SpinAxisDegrees = (360.0 - p.SpinAxisDegrees) % 360.0;
            p.GyroAngleDegrees = -p.GyroAngleDegrees;
            return p;
        }

        // ------------------------------------------------------------------ execution

        /// <summary>Per-axis release-angle spread (°) of an average command (50): 0.7° ≈ 19 cm (7.4 in) per axis at the plate
        /// (≈ 27 cm per degree, Nasu &amp; Kashino 2021, DERIVED) — a mean radial miss ≈ 9 in. TUNED between the sources, which
        /// disagree: a mean miss of 11–13 in from the catcher's target (COMMANDf/x, Driveline; glove drift inflates it) implies
        /// ≈ 9–10 in per axis, Inside Edge's 58 % within ≈ 6 in implies ≈ 4.6 in (Docs/PITCH_EXECUTION.md).</summary>
        public const double AverageAngleSigmaDeg = 0.7;
        /// <summary>Command scales the spread by 1 ∓ 35 % (0.39°–0.81° per axis; ASSUMED range — elite ≈ 4–5 in, poor ≈ 9 in).</summary>
        public const double CommandScale = 0.35;
        /// <summary>Familiarity (−50…+50) scales it a further ∓ 15 %.</summary>
        public const double FamiliarityScale = 0.15;
        /// <summary>Arm-slot ellipse: horizontal and vertical errors correlate — a pitcher misses up-and-to-his-arm-side or
        /// down-and-to-his-glove-side (Shinya et al. 2017). A right-hander's arm side is −X (third base): ρ &lt; 0 for him,
        /// &gt; 0 for a left-hander. ASSUMED magnitude.</summary>
        public const double ArmSlotCorrelation = 0.3;
        /// <summary>A few pitches get away: with this probability the spread is <see cref="WildFactor"/> times larger (the
        /// heavy tail of misses beyond 18 in; ASSUMED share).</summary>
        public const double WildChance = 0.03, WildFactor = 2.2;
        /// <summary>Within-pitcher spread of speed (mph), spin rate (rpm) and spin axis (°): ≈ 1.0 mph, 70–90 rpm, 4° within a
        /// start (Statcast pitch-level data for one pitcher, DERIVED; Nasu 2021 spin SD 68 rpm).</summary>
        public const double SpeedSigmaMph = 0.9, SpinSigmaRpm = 70.0;

        /// <summary>Spin-axis spread (°) by type: ≈ 4° for fastballs (DERIVED, one pitcher), wider for the changeup (6°) and
        /// breaking balls (8°; Statcast movement-based axis scatter is larger for them — ASSUMED values).</summary>
        public static double AxisSigmaDeg(PitchType t) => t switch
        {
            PitchType.FourSeam or PitchType.Sinker => 4.0,
            PitchType.Changeup => 6.0,
            _ => 8.0,
        };
        /// <summary>Fatigue (deliberately mild, ASSUMED): past 85 ± 25·r̂(Stamina) pitches the angle spread grows 0.5 % per
        /// pitch (at most +25 %) and his speed drops 0.02 mph per pitch (at most 1.5 mph; in <see cref="PitcherPitch"/>).</summary>
        public static int FatigueThreshold(PlayerProfile pitcher) => (int)Math.Round(85.0 + 25.0 * RatingScale.Unit(pitcher.Ratings.Stamina));

        /// <summary>The per-axis release-angle spread (°) of <paramref name="pitcher"/> throwing <paramref name="type"/>
        /// after <paramref name="pitchCount"/> pitches.</summary>
        public static double AngleSigmaDeg(PlayerProfile pitcher, PitchType type, int pitchCount)
        {
            int familiarity = pitcher.Repertoire.Get(type).Familiarity;
            double sigma = AverageAngleSigmaDeg * (1.0 - CommandScale * RatingScale.Unit(pitcher.Ratings.Command)) * (1.0 - FamiliarityScale * familiarity / 50.0);
            int over = pitchCount - FatigueThreshold(pitcher);
            return over > 0 ? sigma * Math.Min(1.25, 1.0 + 0.005 * over) : sigma;
        }

        public static double FatigueSpeedLossMph(PlayerProfile pitcher, int pitchCount) => Math.Min(1.5, 0.02 * Math.Max(0, pitchCount - FatigueThreshold(pitcher)));

        /// <summary>
        /// The pitch as executed from <paramref name="intended"/> (his pitch aimed at the target), with the deviates of
        /// <paramref name="stream"/>: correlated release-angle errors (wider when tired), zero-mean speed, spin-rate and
        /// spin-axis errors. The physics of the returned input decides the location.
        /// </summary>
        public static PitchInput Execute(PitchInput intended, PlayerProfile pitcher, PitchType type, int pitchCount, ref SeedStream stream, out ExecutionError error)
        {
            double sigma = AngleSigmaDeg(pitcher, type, pitchCount);
            bool wild = stream.Unit() < WildChance;
            if (wild) sigma *= WildFactor;
            double rho = pitcher.Throws == Hand.Left ? ArmSlotCorrelation : -ArmSlotCorrelation;
            double z1 = stream.Normal(), z2 = stream.Normal();
            double dh = sigma * z1, dv = sigma * (rho * z1 + Math.Sqrt(1.0 - rho * rho) * z2);
            double dSpeed = SpeedSigmaMph * stream.Normal();
            double dSpin = SpinSigmaRpm * stream.Normal(), dAxis = AxisSigmaDeg(type) * stream.Normal();
            PitchInput p = intended;
            p.HorizontalAngleDegrees += dh;
            p.VerticalAngleDegrees += dv;
            p.SpeedMph += dSpeed;
            p.SpinRateRpm = Math.Max(0.0, p.SpinRateRpm + dSpin);
            p.SpinAxisDegrees = ((p.SpinAxisDegrees + dAxis) % 360.0 + 360.0) % 360.0;
            error = new ExecutionError(dh, dv, dSpeed, dSpin, dAxis, wild);
            return p;
        }

        /// <summary>The deviates of one pitch: the game's seed, the plate appearance, the pitch number (and the pitcher).</summary>
        public static SeedStream StreamFor(int gameSeed, string pitcherId, int plateAppearance, int pitchNumber) =>
            new SeedStream(gameSeed, SeedStream.Key(pitcherId), plateAppearance, pitchNumber, 0x0E5EC);
    }
}
