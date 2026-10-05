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
        /// <summary>Contact → the batter's first step toward first (s): 0.25 s = 4.27 s home-to-first (MEASURED average) − the
        /// 4.02 s split from the first step (DERIVED).</summary>
        public double BatterStartDelay { get; }
        /// <summary>A runner on base reading the batted ball before he commits (s; ASSUMED).</summary>
        public double ReadDelay { get; }

        public static RunnerProfile Standard => new RunnerProfile(27.0 * 0.3048, 0.69, 6.0, 0.8, 0.25, 0.25);
    }

    /// <summary>
    /// A runner's movement along one base-path leg (distance d from its start bag), from (d₀, v₀) at t₀: the sprint law
    /// v(t) = s·v_max + (v₀ − s·v_max)·e^(−t/τ) toward the target (s = ±1, so he can reverse), then constant braking so he
    /// reaches the target distance at the end speed (0 = stop on it; above 0 = through it — rounding or overrunning — then
    /// braking to rest past it). Position and velocity are continuous and exact functions of time; prediction and
    /// execution use this same law.
    /// </summary>
    public sealed class PathMotion
    {
        /// <param name="endSpeed">Speed when he reaches the target (0: stop on it; NaN: as fast as he gets there).</param>
        public PathMotion(RunnerProfile p, double startTime, double d0, double v0, double target, double endSpeed, double topSpeed = double.NaN,
            double deceleration = double.NaN)
        {
            Profile = p;
            _brake = double.IsNaN(deceleration) ? p.BrakeDeceleration : deceleration;
            StartTime = startTime;
            D0 = d0;
            V0 = v0;
            Target = target;
            _sign = target >= d0 ? 1.0 : -1.0;
            _top = double.IsNaN(topSpeed) ? p.MaxSpeed : Math.Min(topSpeed, p.MaxSpeed);
            double remaining = Math.Abs(target - d0);
            if (remaining < 1e-12 && Math.Abs(v0) < 1e-9)
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
            if (vb > vEnd)
            {
                _arrival = _brakeStart + (vb - vEnd) / _brake;
                EndSpeed = vEnd;
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

        private readonly double _sign, _top, _brakeStart, _arrival, _brake;

        public RunnerProfile Profile { get; }
        public double StartTime { get; }
        public double D0 { get; }
        public double V0 { get; }
        public double Target { get; }
        /// <summary>Speed (≥ 0) when he reaches the target.</summary>
        public double EndSpeed { get; }
        /// <summary>When he reaches the target.</summary>
        public double ArrivalTime => StartTime + _arrival;
        /// <summary>When he starts braking (= arrival if he reaches it at speed).</summary>
        public double BrakeTime => StartTime + _brakeStart;
        /// <summary>Direction of travel (+1 forward along the leg, −1 back toward its start).</summary>
        public double Sign => _sign;
        /// <summary>When he comes to rest after the target (overrun) — the arrival when he stops on it.</summary>
        public double RestTime => ArrivalTime + EndSpeed / _brake;

        /// <summary>Signed distance along the leg at <paramref name="time"/> (past the target he brakes to rest).</summary>
        public double DistanceAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0)) return D0;
            if (t <= _brakeStart) return D0 + _sign * Along(t);
            double vb = AlongSpeed(_brakeStart), tb = Math.Min(t - _brakeStart, vb / _brake);
            return D0 + _sign * (Along(_brakeStart) + vb * tb - 0.5 * _brake * tb * tb);
        }

        /// <summary>Signed velocity along the leg (m/s).</summary>
        public double VelocityAt(double time)
        {
            double t = time - StartTime;
            if (!(t > 0.0)) return V0;
            if (t <= _brakeStart) return _sign * AlongSpeed(t);
            return _sign * Math.Max(0.0, AlongSpeed(_brakeStart) - _brake * (t - _brakeStart));
        }

        /// <summary>Time ≥ start when the distance first equals <paramref name="d"/> (+∞ if never): bisection over the
        /// monotone run (the reversal of a returning runner happens before he heads for the target).</summary>
        public double TimeAt(double d)
        {
            double end = RestTime - StartTime;
            double F(double t) => _sign * (DistanceAt(StartTime + t) - d);
            // The law can first move against the direction (initial velocity the other way): find the turnaround.
            double turn = 0.0;
            double along0 = _sign * V0;
            if (along0 < 0.0) turn = Profile.AccelerationTime * Math.Log(1.0 - along0 / _top);   // velocity crosses zero
            double lo = Math.Min(turn, end), hi = end;
            if (F(hi) < 0.0) return double.PositiveInfinity;
            if (F(lo) >= 0.0)
            {
                // Already at/through it at the turnaround: the first crossing is before it (moving the other way).
                double a = 0.0, b = lo;
                if (F(0.0) >= 0.0) return StartTime;
                for (int i = 0; i < 64; i++)
                {
                    double mid = 0.5 * (a + b);
                    if (F(mid) >= 0.0) b = mid;
                    else a = mid;
                }

                return StartTime + b;
            }

            for (int i = 0; i < 64 && hi - lo > 1e-13; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (F(mid) < 0.0) lo = mid;
                else hi = mid;
            }

            return StartTime + hi;
        }

        // Distance and speed along the travel direction under the sprint law (before braking).
        private double Along(double t)
        {
            double g = Profile.AccelerationTime * (1.0 - Math.Exp(-t / Profile.AccelerationTime));
            return _top * t + (_sign * V0 - _top) * g;
        }

        private double AlongSpeed(double t) => _top + (_sign * V0 - _top) * Math.Exp(-t / Profile.AccelerationTime);
    }
}
