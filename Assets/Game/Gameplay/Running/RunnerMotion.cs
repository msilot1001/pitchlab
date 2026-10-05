using System;

namespace Pitchlab.Gameplay.Running
{
    /// <summary>
    /// Generic runner abilities (Docs/BASERUNNING.md has the sources). One profile for every runner (no ratings yet).
    /// </summary>
    public readonly struct RunnerProfile
    {
        public RunnerProfile(double maxSpeed, double accelerationTime, double brakeDeceleration, double roundingSpeedFactor, double batterStartDelay, double readDelay,
            double slideDeceleration = 10.0)
        {
            SlideDeceleration = slideDeceleration;
            if (!(maxSpeed > 0.0) || !(accelerationTime > 0.0) || !(brakeDeceleration > 0.0)) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            MaxSpeed = maxSpeed;
            AccelerationTime = accelerationTime;
            BrakeDeceleration = brakeDeceleration;
            RoundingSpeed = roundingSpeedFactor * maxSpeed;
            BatterStartDelay = batterStartDelay;
            ReadDelay = readDelay;
        }

        /// <summary>Top speed (m/s): 27 ft/s, the MLB average Statcast sprint speed (MEASURED).</summary>
        public double MaxSpeed { get; }
        /// <summary>τ of v(t) = v_max·(1 − e^(−t/τ)): 0.69 s fits the average 90-ft split of 4.02 s from the first step at
        /// 27 ft/s (Statcast running splits, DERIVED).</summary>
        public double AccelerationTime { get; }
        /// <summary>Braking to a stop on a base or after an overrun (m/s²; ASSUMED — gives the 15–25 ft overrun of first).</summary>
        public double BrakeDeceleration { get; }
        /// <summary>Stopping on second, third or home: he slides (m/s²; ASSUMED — a slide stops a runner in ~3 m from top speed).</summary>
        public double SlideDeceleration { get; }
        /// <summary>Highest speed through a base he rounds: 0.8·v_max (15–25 % lost per turn, optimal-path model — REPORTED).</summary>
        public double RoundingSpeed { get; }
        /// <summary>Contact → the batter's first step toward first (s): 0.34 s, chosen in the 0.20–0.35 s range DERIVED from
        /// the 4.27 s home-to-first average (MEASURED, contact → foot on the bag) minus the 4.02 s 90-ft split from the first
        /// step, so that his foot reaches first 4.28 s after contact over this path (plate centre → bag, 27.05 m).</summary>
        public double BatterStartDelay { get; }
        /// <summary>A runner on base reading the batted ball before he commits (s; ASSUMED).</summary>
        public double ReadDelay { get; }

        public static RunnerProfile Standard => new RunnerProfile(27.0 * 0.3048, 0.69, 6.0, 0.8, 0.34, 0.25);

        /// <summary>The trot speed as a fraction of the top speed: ≈ 5.2 m/s, giving the measured ≈ 22 s home-run trot
        /// (SABR: 22.0–22.7 s average, Docs/BASERUNNING_MOTION_REFERENCE.md).</summary>
        public const double TrotFactor = 0.64;

        /// <summary>The same runner trotting (an awarded dead-ball advance: home run, ground-rule double): slower, keeping his
        /// speed through the bases, the batter watching the ball a moment longer before he goes.</summary>
        public RunnerProfile Trot() =>
            new RunnerProfile(TrotFactor * MaxSpeed, AccelerationTime, BrakeDeceleration, 0.95, BatterStartDelay + 0.4, ReadDelay, SlideDeceleration);
    }

    /// <summary>
    /// A runner's movement along one base-path leg (distance d from its start bag), from (d₀, v₀) at t₀. If he is moving away
    /// from the target he first brakes to a stop at the braking deceleration (no sharper than any other stop), then runs the
    /// sprint law v(t) = s·v_max·(1 − e^(−t/τ)) toward the target (s = ±1), then brakes at constant deceleration so he
    /// reaches the target distance at the end speed (0 = stop exactly on it; above 0 = through it — rounding or overrunning
    /// — then braking to rest past it). Position and velocity are continuous and exact functions of time; prediction and
    /// execution use this same law.
    /// </summary>
    public sealed class PathMotion
    {
        /// <param name="endSpeed">Speed when he reaches the target (0: stop on it; NaN: as fast as he gets there).</param>
        public PathMotion(RunnerProfile p, double startTime, double d0, double v0, double target, double endSpeed, double topSpeed = double.NaN,
            double deceleration = double.NaN)
        {
            if (double.IsNaN(d0) || double.IsNaN(v0) || double.IsNaN(target)) throw new ArgumentException("Motion state must be finite.");
            Profile = p;
            _brake = double.IsNaN(deceleration) ? p.BrakeDeceleration : deceleration;
            StartTime = startTime;
            D0 = d0;
            V0 = v0;
            Target = target;
            _top = double.IsNaN(topSpeed) ? p.MaxSpeed : Math.Min(topSpeed, p.MaxSpeed);
            if (!(_top > 0.0)) throw new ArgumentOutOfRangeException(nameof(topSpeed));

            // Moving away from where he now has to go: brake to a stop first.
            double towards = target >= d0 ? 1.0 : -1.0;
            if (v0 * towards < 0.0)
            {
                _reverse = Math.Abs(v0) / _brake;
                _d1 = d0 + 0.5 * v0 * _reverse;
                _sign = target >= _d1 ? 1.0 : -1.0;
            }
            else
            {
                _d1 = d0;
                _sign = towards;
                _u0 = Math.Abs(v0);
            }

            double remaining = Math.Abs(target - _d1);
            if (remaining < 1e-12 && _u0 < 1e-9)
            {
                _brakeStart = 0.0;
                _arrival = 0.0;
                EndSpeed = 0.0;
                return;
            }

            // Brake start: the last moment the sprint law can run before braking to the end speed lands on the target.
            double vEnd = double.IsNaN(endSpeed) ? double.PositiveInfinity : Math.Max(0.0, endSpeed);
            double Overshoot(double t)
            {
                double v = AlongSpeed(t);
                double brake = v > vEnd ? (v * v - vEnd * vEnd) / (2.0 * _brake) : 0.0;
                return Along(t) + brake - remaining;
            }

            double hi = 0.25;
            while (Overshoot(hi) < 0.0) hi *= 2.0;
            double lo = 0.0;
            if (Overshoot(0.0) >= 0.0) hi = 0.0;
            for (int i = 0; i < 64 && hi - lo > 1e-12; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Overshoot(mid) < 0.0) lo = mid;
                else hi = mid;
            }

            _brakeStart = hi;
            double vb = AlongSpeed(_brakeStart);
            if (hi == 0.0 && Overshoot(0.0) > 1e-9 && vb > vEnd)
            {
                // Too close to stop (or slow to the end speed) by the target: he brakes from now and passes it at the speed
                // he still has, then comes to rest beyond it — never snapped back onto it.
                double cross = Math.Sqrt(Math.Max(0.0, vb * vb - 2.0 * _brake * remaining));
                _arrival = (vb - cross) / _brake;
                EndSpeed = cross;
                return;
            }

            if (vb > vEnd)
            {
                _arrival = _brakeStart + (vb - vEnd) / _brake;
                EndSpeed = vEnd;
                _exactStop = vEnd == 0.0;
            }
            else
            {
                // No braking needed: he reaches the target at speed (run through / free end); find when.
                double a = 0.0, b = Math.Max(_brakeStart, 1e-6);
                while (Along(b) < remaining) b *= 2.0;
                for (int i = 0; i < 64 && b - a > 1e-12; i++)
                {
                    double mid = 0.5 * (a + b);
                    if (Along(mid) < remaining) a = mid;
                    else b = mid;
                }

                _brakeStart = b;
                _arrival = b;
                EndSpeed = AlongSpeed(b);
            }
        }

        private readonly double _sign, _top, _brakeStart, _arrival, _brake, _reverse, _d1, _u0;
        private readonly bool _exactStop;

        public RunnerProfile Profile { get; }
        public double StartTime { get; }
        public double D0 { get; }
        public double V0 { get; }
        public double Target { get; }
        /// <summary>Speed (≥ 0) when he reaches the target.</summary>
        public double EndSpeed { get; }
        /// <summary>When he reaches the target.</summary>
        public double ArrivalTime => StartTime + _reverse + _arrival;
        /// <summary>When he starts braking (= arrival if he reaches it at speed).</summary>
        public double BrakeTime => StartTime + _reverse + _brakeStart;
        /// <summary>Direction of travel toward the target (+1 forward along the leg, −1 back toward its start).</summary>
        public double Sign => _sign;
        /// <summary>Braking rate for the stop (m/s²): the profile's slide deceleration when he slides into the base.</summary>
        public double Deceleration => _brake;
        /// <summary>When he comes to rest after the target (overrun) — the arrival when he stops on it.</summary>
        public double RestTime => ArrivalTime + EndSpeed / _brake;

        /// <summary>Signed distance along the leg at <paramref name="time"/> (past the target he brakes to rest).</summary>
        public double DistanceAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0)) return D0;
            if (_exactStop && time >= ArrivalTime) return Target;   // exactly on the bag, not a rounding error short of it
            if (t < _reverse) return D0 + V0 * t - Math.Sign(V0) * 0.5 * _brake * t * t;
            t -= _reverse;
            if (t <= _brakeStart) return _d1 + _sign * Along(t);
            double vb = AlongSpeed(_brakeStart), tb = Math.Min(t - _brakeStart, vb / _brake);
            return _d1 + _sign * (Along(_brakeStart) + vb * tb - 0.5 * _brake * tb * tb);
        }

        /// <summary>Signed velocity along the leg (m/s).</summary>
        public double VelocityAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0)) return V0;
            if (t < _reverse) return V0 - Math.Sign(V0) * _brake * t;
            t -= _reverse;
            if (t <= _brakeStart) return _sign * AlongSpeed(t);
            return _sign * Math.Max(0.0, AlongSpeed(_brakeStart) - _brake * (t - _brakeStart));
        }

        /// <summary>
        /// The first time ≥ the end of any reversal at which he is at distance <paramref name="d"/> on his way to the target
        /// (+∞ if he never gets there): bisection over the monotone run toward and past the target.
        /// </summary>
        public double TimeAt(double d)
        {
            double a = StartTime + _reverse, b = RestTime;
            double F(double t) => _sign * (DistanceAt(t) - d);
            if (F(a) >= 0.0) return a;
            if (F(b) < 0.0) return double.PositiveInfinity;
            for (int i = 0; i < 64 && b - a > 1e-13; i++)
            {
                double mid = 0.5 * (a + b);
                if (F(mid) < 0.0) a = mid;
                else b = mid;
            }

            return b;
        }

        // Distance and speed along the travel direction under the sprint law (after any reversal, before braking).
        private double Along(double t)
        {
            double g = Profile.AccelerationTime * (1.0 - Math.Exp(-t / Profile.AccelerationTime));
            return _top * t + (_u0 - _top) * g;
        }

        private double AlongSpeed(double t) => _top + (_u0 - _top) * Math.Exp(-t / Profile.AccelerationTime);
    }
}
