using System;
using System.Collections.Generic;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.Field
{
    public enum BallPhase
    {
        Airborne,
        Sliding,
        Rolling,
        /// <summary>Stopped on the field.</summary>
        Rest,
        /// <summary>Over the fence and down beyond it (no further play).</summary>
        OutOfPlay,
    }

    public enum BallEventKind
    {
        /// <summary>Ground impact; the ball bounces (or stays down, see the next phase).</summary>
        GroundImpact,
        WallImpact,
        /// <summary>Contact slip reached zero: from here the ball rolls.</summary>
        SlideToRoll,
        Rest,
        /// <summary>Passed over the fence (above wall height + ball radius).</summary>
        ClearedFence,
        /// <summary>Came down beyond the fence.</summary>
        LeftPlay,
    }

    public readonly struct BallEvent
    {
        public readonly BallEventKind Kind;
        /// <summary>State at the event; for impacts, just before the impulse.</summary>
        public readonly BallState Before;
        /// <summary>For impacts, just after the impulse; otherwise equal to <see cref="Before"/>.</summary>
        public readonly BallState After;
        public readonly SurfaceKind Surface;

        public BallEvent(BallEventKind kind, BallState before, BallState after, SurfaceKind surface)
        {
            Kind = kind;
            Before = before;
            After = after;
            Surface = surface;
        }

        public double Time => Before.Time;
    }

    /// <summary>One event-free stretch of the play: a single phase, sampled (positions and velocities continuous inside).</summary>
    public readonly struct BallSegment
    {
        public readonly BallPhase Phase;
        public readonly TrajectoryResult Path;

        public BallSegment(BallPhase phase, TrajectoryResult path)
        {
            Phase = phase;
            Path = path;
        }
    }

    /// <summary>
    /// A batted ball from contact to rest (or out of play): airborne flight, ground and wall impacts, sliding, rolling
    /// and the stop. Query the state and phase at any time; events and distances are exact outputs of the simulation.
    /// </summary>
    public sealed class BallInPlay
    {
        private readonly BallSegment[] _segments;
        private readonly BallEvent[] _events;

        public BallInPlay(BallSegment[] segments, BallEvent[] events, BallPhase endPhase)
        {
            if (segments == null || segments.Length == 0) throw new ArgumentException("A play needs at least one segment.", nameof(segments));
            _segments = segments;
            _events = events ?? Array.Empty<BallEvent>();
            EndPhase = endPhase;
        }

        public IReadOnlyList<BallSegment> Segments => _segments;
        public IReadOnlyList<BallEvent> Events => _events;
        /// <summary><see cref="BallPhase.Rest"/>, <see cref="BallPhase.OutOfPlay"/>, or the last phase if the time limit cut the play.</summary>
        public BallPhase EndPhase { get; }
        public BallState First => _segments[0].Path.First;
        public BallState Final => _segments[_segments.Length - 1].Path.Final;
        /// <summary>Time the ball stopped (or left play).</summary>
        public double EndTime => Final.Time;

        /// <summary>First ground contact (any surface), or null if the ball never reached the ground.</summary>
        public BallEvent? FirstGroundContact
        {
            get
            {
                foreach (BallEvent e in _events)
                    if (e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.LeftPlay) return e;
                return null;
            }
        }

        /// <summary>Horizontal distance from the plate origin to the first ground contact (carry), m; NaN if none.</summary>
        public double CarryDistance => FirstGroundContact is BallEvent e ? Horizontal(e.Before.Position) : double.NaN;
        /// <summary>Horizontal distance from the plate origin to where the ball ended (rest or out of play), m.</summary>
        public double FinalDistance => Horizontal(Final.Position);
        /// <summary>The ball met the wall (or cleared it) before touching the ground: its carry is then cut short, and the
        /// projected airborne-only distance is the meaningful "distance" (as Statcast reports for such balls).</summary>
        public bool ReachedFenceInTheAir
        {
            get
            {
                foreach (BallEvent e in _events)
                {
                    if (e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.LeftPlay) return false;
                    if (e.Kind == BallEventKind.WallImpact || e.Kind == BallEventKind.ClearedFence) return true;
                }

                return false;
            }
        }

        public bool ClearedFence
        {
            get
            {
                foreach (BallEvent e in _events) if (e.Kind == BallEventKind.ClearedFence) return true;
                return false;
            }
        }

        private static double Horizontal(Vector3d p) => Math.Sqrt(p.X * p.X + p.Y * p.Y);

        private int SegmentAt(double time)
        {
            for (int i = 0; i < _segments.Length; i++)
                if (time < _segments[i].Path.Final.Time) return i;
            return _segments.Length - 1;
        }

        /// <summary>State at <paramref name="time"/> (clamped to the play): position, velocity and spin.</summary>
        public BallState StateAt(double time) => _segments[SegmentAt(time)].Path.StateAt(time);

        /// <summary>Phase at <paramref name="time"/>; after the end, the end phase.</summary>
        public BallPhase PhaseAt(double time) => time >= EndTime ? EndPhase : _segments[SegmentAt(time)].Phase;
    }

    /// <summary>
    /// Event-driven ball-in-play simulation (Docs/SURFACE_PHYSICS.md). Airborne stretches use the unchanged batted-ball
    /// flight model (<see cref="BallFlightSimulator"/>, RK4 5 ms, exact event location); impacts use
    /// <see cref="SurfaceImpact"/>; ground contact is integrated at 1 ms with Coulomb sliding friction, then rolling
    /// resistance, until a deterministic stop. No Unity physics.
    /// </summary>
    public static class BallInPlaySimulation
    {
        /// <summary>Rebound normal speed below which the ball stays on the ground (a 5 mm hop).</summary>
        public const double HopSpeed = 0.31;
        public const double GroundStep = 0.001;
        public const double MaxPlayTime = 30.0;
        public const int MaxImpacts = 40;
        private const int RecordEvery = 4;   // ground samples every 4 ms (Hermite playback between them)
        private const double Lift = 1e-7;    // re-launch just above the surface so the flight does not end at its start

        public static BallInPlay Run(BallState contact, EnvironmentState environment, FieldLayout field) =>
            Run(contact, environment, field, AerodynamicModel.BattedBall);

        public static BallInPlay Run(BallState contact, EnvironmentState environment, FieldLayout field, AerodynamicModel aerodynamics)
        {
            BallProperties ball = BallProperties.Baseball;
            var sim = new BallFlightSimulator(ball, environment, aerodynamics);
            var segments = new List<BallSegment>();
            var events = new List<BallEvent>();
            double endTime = contact.Time + MaxPlayTime;
            BallState state = contact;
            bool airborne = true, beyondFence = false;
            int impacts = 0;

            while (true)
            {
                if (airborne)
                {
                    TrajectoryResult flight = sim.Simulate(state, new FlightLimits(double.NegativeInfinity, 0.0, Math.Max(1e-6, endTime - state.Time)));
                    if (!beyondFence && FirstWallCrossing(sim, field, flight, out int before, out BallState atWall, out Vector3d normal))
                    {
                        var samples = new BallState[before + 2];
                        for (int i = 0; i <= before; i++) samples[i] = flight.Samples[i];
                        samples[before + 1] = atWall;
                        segments.Add(new BallSegment(BallPhase.Airborne, new TrajectoryResult(samples, FlightEnd.ReachedGround)));
                        if (atWall.Position.Z > field.WallHeight + ball.Radius)
                        {
                            // Over the fence: the rest of this flight (to the ground beyond it) ends the play.
                            events.Add(new BallEvent(BallEventKind.ClearedFence, atWall, atWall, SurfaceKind.Wall));
                            beyondFence = true;
                            state = atWall;
                            continue;
                        }

                        BallState after = SurfaceImpact.Resolve(atWall, normal, BallSurfaceProperties.Wall, ball);
                        airborne = after.Position.Z - ball.Radius > 1e-6 || after.Velocity.Z > HopSpeed;
                        state = airborne ? after : OnGround(after, ball);
                        events.Add(new BallEvent(BallEventKind.WallImpact, atWall, state, SurfaceKind.Wall));
                        if (++impacts >= MaxImpacts) return Finish(segments, events, BallPhase.Airborne);
                        continue;
                    }

                    segments.Add(new BallSegment(BallPhase.Airborne, flight));
                    if (flight.End != FlightEnd.ReachedGround) return Finish(segments, events, BallPhase.Airborne);
                    BallState landing = flight.Final;
                    if (beyondFence)
                    {
                        events.Add(new BallEvent(BallEventKind.LeftPlay, landing, landing, field.SurfaceAt(landing.Position.X, landing.Position.Y)));
                        return Finish(segments, events, BallPhase.OutOfPlay);
                    }

                    SurfaceKind surface = field.SurfaceAt(landing.Position.X, landing.Position.Y);
                    BallState bounced = SurfaceImpact.Resolve(landing, new Vector3d(0.0, 0.0, 1.0), BallSurfaceProperties.For(surface), ball);
                    // The event records the state the play continues from (a rebound below HopSpeed stays down: v_z = 0),
                    // so StateAt(event time) and the event agree.
                    bool hops = bounced.Velocity.Z > HopSpeed;
                    BallState continues = hops ? bounced : OnGround(bounced, ball);
                    events.Add(new BallEvent(BallEventKind.GroundImpact, landing, continues, surface));
                    if (++impacts >= MaxImpacts) return Finish(segments, events, BallPhase.Airborne);
                    if (hops)
                    {
                        state = new BallState(bounced.Time, new Vector3d(bounced.Position.X, bounced.Position.Y, ball.Radius + Lift), bounced.Velocity, bounced.Spin);
                        continue;
                    }

                    state = continues;
                    airborne = false;
                    continue;
                }

                // Ground contact: slide, then roll, until rest, a wall, or the time limit.
                GroundResult ground = Roll(sim, field, environment, ball, state, endTime, segments, events);
                if (ground.End == GroundEnd.Rest) return Finish(segments, events, BallPhase.Rest);
                if (ground.End == GroundEnd.TimeLimit) return Finish(segments, events, BallPhase.Rolling);
                // Wall from the ground: bounce off it and continue (along the ground, or briefly in the air).
                if (++impacts >= MaxImpacts) return Finish(segments, events, BallPhase.Rolling);
                state = ground.State;
                airborne = state.Velocity.Z > HopSpeed;
                state = airborne
                    ? new BallState(state.Time, new Vector3d(state.Position.X, state.Position.Y, ball.Radius + Lift), state.Velocity, state.Spin)
                    : OnGround(state, ball);
            }
        }

        private static BallInPlay Finish(List<BallSegment> segments, List<BallEvent> events, BallPhase end) =>
            new BallInPlay(segments.ToArray(), events.ToArray(), end);

        private static BallState OnGround(BallState s, BallProperties ball) =>
            new BallState(s.Time, new Vector3d(s.Position.X, s.Position.Y, ball.Radius), new Vector3d(s.Velocity.X, s.Velocity.Y, 0.0), s.Spin);

        /// <summary>Signed gap between the ball's surface and the fence's inner face (≥ 0: touching or beyond). The fence stands
        /// between the foul poles (poles and lines included); foul territory has no wall (Docs/SURFACE_PHYSICS.md).</summary>
        private static double WallGap(FieldLayout field, BallState s, BallProperties ball, out Vector3d normal)
        {
            double gap = field.DistanceBeyondFence(s.Position.X, s.Position.Y, out normal) + ball.Radius;
            // The fence and foul pole cover the line too: a ball with any part over the line meets them.
            return FieldLayout.OutsideFoulLine(s.Position.X, s.Position.Y) <= ball.Radius ? gap : double.NegativeInfinity;
        }

        private static bool FirstWallCrossing(BallFlightSimulator sim, FieldLayout field, TrajectoryResult flight, out int before, out BallState atWall, out Vector3d normal)
        {
            BallProperties ball = sim.Ball;
            for (int i = 1; i < flight.Samples.Count; i++)
            {
                BallState a = flight.Samples[i - 1], b = flight.Samples[i];
                if (WallGap(field, a, ball, out _) >= 0.0 || WallGap(field, b, ball, out _) < 0.0) continue;
                // Locate the crossing inside the step by bisection on a partial RK4 step from a (as the simulator does).
                double lo = 0.0, hi = b.Time - a.Time;
                for (int k = 0; k < 60 && hi - lo > 1e-12; k++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (WallGap(field, sim.Step(a, mid), ball, out _) >= 0.0) hi = mid;
                    else lo = mid;
                }

                atWall = sim.Step(a, hi);
                double gap = WallGap(field, atWall, ball, out normal);
                if (Vector3d.Dot(atWall.Velocity, normal) >= 0.0) continue;   // grazing outward-moving: not an impact
                // A real crossing meets the face (gap ≈ 0). A jump from foul ground (no wall) into fair ground behind the
                // fence is outside the park, not a wall contact.
                if (gap > 0.01 || double.IsNegativeInfinity(WallGap(field, a, ball, out _))) continue;
                before = i - 1;
                return true;
            }

            before = -1;
            atWall = default;
            normal = default;
            return false;
        }

        private enum GroundEnd
        {
            Rest,
            Wall,
            TimeLimit,
        }

        private readonly struct GroundResult
        {
            public readonly GroundEnd End;
            public readonly BallState State;

            public GroundResult(GroundEnd end, BallState state)
            {
                End = end;
                State = state;
            }
        }

        /// <summary>
        /// Ball on the ground (Z = R): Coulomb sliding friction while the contact point slips, then rolling resistance;
        /// air drag throughout (no lift). Records Sliding and Rolling segments and the transition, rest and wall events.
        /// </summary>
        private static GroundResult Roll(BallFlightSimulator sim, FieldLayout field, EnvironmentState environment, BallProperties ball,
            BallState start, double endTime, List<BallSegment> segments, List<BallEvent> events)
        {
            double g = environment.Gravity, R = ball.Radius, m = ball.Mass, inertia = SurfaceImpact.InertiaFactor * m * R * R;
            var up = new Vector3d(0.0, 0.0, 1.0);
            var rc = new Vector3d(0.0, 0.0, -R);
            BallState s = start;
            bool rolling = Slip(s, rc).Length < 1e-6;
            if (rolling) s = Rolled(s, R);
            var samples = new List<BallState> { s };
            long step = 0;
            double t0 = s.Time;

            while (true)
            {
                SurfaceKind surface = field.SurfaceAt(s.Position.X, s.Position.Y);
                BallSurfaceProperties props = BallSurfaceProperties.For(surface);
                Vector3d v = s.Velocity, w = s.Spin;
                // Air drag on the ground: the spin-free C_D (the spin-dependent fit covers fly-ball spins ≤ ~3000 rpm, not a
                // rolling ball's ω = v/R); under the rolling constraint it decelerates the ball by F/((1 + α)·m).
                Vector3d drag = sim.DragForce(v) / m;
                drag = new Vector3d(drag.X, drag.Y, 0.0);
                if (rolling) drag = drag / (1.0 + SurfaceImpact.InertiaFactor);

                if (rolling)
                {
                    double speed = v.Length, decel = props.RollingDeceleration(speed);
                    if (speed <= decel * GroundStep || speed < 1e-9)
                    {
                        // Deterministic stop: the exact remaining distance and time under a(v) = a₀ + b·v (drag negligible
                        // below a few cm/s).
                        (double sStop, double tStop) = speed > 0.0 ? props.RollingStop(speed) : (0.0, 0.0);
                        Vector3d dir = speed > 0.0 ? v / speed : Vector3d.Zero;
                        var rest = new BallState(s.Time + tStop, s.Position + sStop * dir, Vector3d.Zero, Vector3d.Zero);
                        samples.Add(rest);
                        segments.Add(new BallSegment(BallPhase.Rolling, new TrajectoryResult(samples.ToArray(), FlightEnd.ReachedGround)));
                        events.Add(new BallEvent(BallEventKind.Rest, rest, rest, field.SurfaceAt(rest.Position.X, rest.Position.Y)));
                        return new GroundResult(GroundEnd.Rest, rest);
                    }
                }

                // One step. Sliding: friction opposite the contact slip changes v and (through its torque) ω.
                Vector3d accel = drag, dw = Vector3d.Zero;
                Vector3d slip = Slip(s, rc);
                if (!rolling)
                {
                    Vector3d friction = -(props.Friction * m * g / slip.Length) * slip;
                    // The speed-proportional turf resistance (b·v, canopy drag) acts whether or not the ball slips; its
                    // constant part is the Coulomb friction here. Without it a skidding ball would outrun a rolling one.
                    accel = accel + friction / m - props.RollingDecelerationPerSpeed * v;
                    dw = Vector3d.Cross(rc, friction) / inertia * GroundStep;
                }
                else
                {
                    accel = accel - props.RollingDeceleration(v.Length) / v.Length * v;
                }

                ++step;
                double time = t0 + step * GroundStep;
                Vector3d vNext = v + accel * GroundStep;
                var next = new BallState(time, s.Position + 0.5 * (v + vNext) * GroundStep, vNext, w + dw);
                if (!rolling && Vector3d.Dot(Slip(next, rc), slip) <= 0.0)
                {
                    // Slip reaches zero within the step. It falls linearly under constant friction, so the instant is the
                    // fraction s0 / (s0 − s1) of the step; from there the ball rolls (pure rolling, vertical spin kept),
                    // and the fixed-step grid restarts at that exact time.
                    double s0 = slip.Length, s1 = Vector3d.Dot(Slip(next, rc), slip) / s0;
                    double f = s0 / (s0 - s1), h = f * GroundStep;
                    Vector3d vAt = v + accel * h;
                    next = Rolled(new BallState(s.Time + h, s.Position + 0.5 * (v + vAt) * h, vAt, w + dw * f), R);
                    t0 = next.Time;
                    step = 0;
                    samples.Add(next);
                    segments.Add(new BallSegment(BallPhase.Sliding, new TrajectoryResult(samples.ToArray(), FlightEnd.ReachedGround)));
                    events.Add(new BallEvent(BallEventKind.SlideToRoll, next, next, field.SurfaceAt(next.Position.X, next.Position.Y)));
                    samples.Clear();
                    samples.Add(next);
                    rolling = true;
                    s = next;
                    continue;
                }

                if (rolling) next = Rolled(next, R);

                // Wall contact from the field side only (from foul ground behind the fence line there is no wall).
                if (WallGap(field, next, ball, out Vector3d normal) >= 0.0 && Vector3d.Dot(next.Velocity, normal) < 0.0
                    && !double.IsNegativeInfinity(WallGap(field, s, ball, out _)) && WallGap(field, s, ball, out _) < 0.0)
                {
                    samples.Add(next);
                    segments.Add(new BallSegment(rolling ? BallPhase.Rolling : BallPhase.Sliding, new TrajectoryResult(samples.ToArray(), FlightEnd.ReachedGround)));
                    BallState after = SurfaceImpact.Resolve(next, normal, BallSurfaceProperties.Wall, ball);
                    // Record the state the play continues from (on the ground unless the wall pops it up past HopSpeed).
                    BallState continues = after.Velocity.Z > HopSpeed ? after : OnGround(after, ball);
                    events.Add(new BallEvent(BallEventKind.WallImpact, next, continues, SurfaceKind.Wall));
                    return new GroundResult(GroundEnd.Wall, continues);
                }

                if (step % RecordEvery == 0) samples.Add(next);
                s = next;
                if (time >= endTime)
                {
                    if (samples[samples.Count - 1].Time < s.Time) samples.Add(s);
                    segments.Add(new BallSegment(rolling ? BallPhase.Rolling : BallPhase.Sliding, new TrajectoryResult(samples.ToArray(), FlightEnd.ReachedMaxDuration)));
                    return new GroundResult(GroundEnd.TimeLimit, s);
                }
            }
        }

        /// <summary>Contact-point velocity on the ground (horizontal for a ball on the ground).</summary>
        private static Vector3d Slip(BallState s, Vector3d rc) => s.Velocity + Vector3d.Cross(s.Spin, rc);

        /// <summary>Pure rolling: ω = (ẑ × v)/R plus the unchanged vertical spin component.</summary>
        private static BallState Rolled(BallState s, double radius)
        {
            var up = new Vector3d(0.0, 0.0, 1.0);
            Vector3d spin = Vector3d.Cross(up, s.Velocity) / radius + s.Spin.Z * up;
            return new BallState(s.Time, s.Position, s.Velocity, spin);
        }
    }
}
