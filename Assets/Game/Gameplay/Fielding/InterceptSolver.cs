using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Fielding
{
    public enum InterceptKind
    {
        /// <summary>Caught in the air before the ball touched the ground or the wall.</summary>
        FlyCatch,
        /// <summary>Gloved in the air after a bounce or a wall carom (a hop; not a catch for an out).</summary>
        HopCatch,
        /// <summary>Picked up low: rolling, sliding or a low hop, or the ball at rest.</summary>
        GroundPickup,
    }

    /// <summary>Where and when a defender can first take the ball.</summary>
    public readonly struct Intercept
    {
        public readonly bool Feasible;
        /// <summary>Ball time of the intercept (s, the play's clock).</summary>
        public readonly double Time;
        /// <summary>The authoritative ball state at <see cref="Time"/>.</summary>
        public readonly BallState Ball;
        /// <summary>Where the defender stands to take it (the ball's ground point less his reach).</summary>
        public readonly Vector3d FielderTarget;
        public readonly double RouteDistance;
        /// <summary>Earliest time he can be at <see cref="FielderTarget"/> (reaction + fastest run).</summary>
        public readonly double EarliestArrival;
        public readonly InterceptKind Kind;
        /// <summary>A diving catch (TASK-011.6): taken in the air beyond the normal reach, within the dive envelope.</summary>
        public readonly bool Dive;

        public Intercept(double time, BallState ball, Vector3d fielderTarget, double routeDistance, double earliestArrival, InterceptKind kind, bool dive = false)
        {
            Dive = dive;
            Feasible = true;
            Time = time;
            Ball = ball;
            FielderTarget = fielderTarget;
            RouteDistance = routeDistance;
            EarliestArrival = earliestArrival;
            Kind = kind;
        }

        /// <summary>Spare time: intercept time − earliest arrival (s, ≥ 0).</summary>
        public double Margin => Time - EarliestArrival;

        public static Intercept None => default;
    }

    /// <summary>
    /// Earliest plausible intercept of the authoritative ball in play (<see cref="BallInPlay"/>, the same trajectory the
    /// player sees: flight, hops, slides, rolls, wall caroms, rest) by one defender. For a candidate ball time t the ball's
    /// state is queried; the defender must be able to stand within his reach of its ground point by t (reaction + the
    /// running law, <see cref="RunningLaw"/>) and the ball must be inside his glove envelope (centre at or below the catch
    /// height). The time line is searched on a fine grid and the first feasible transition refined by bisection, so the
    /// defender and the ball meet to well under a millimetre of ball travel. A fly ball with time to spare is taken at a
    /// comfortable height instead of at the top of the reach.
    /// </summary>
    public static class InterceptSolver
    {
        /// <summary>Search grid (s). A feasible window shorter than this (a ball passing within ~0.5 cm of the edge of a
        /// defender's reach) can be missed; at 35 m/s that band is under a centimetre wide (physics review).</summary>
        public const double SearchStep = 0.005;
        public const double ComfortCatchHeight = 1.8;
        /// <summary>How far inside the fence face a defender can stand (m).</summary>
        public const double WallClearance = 0.3;

        /// <param name="playableUntil">Last ball time at which the ball is in play (when it clears the fence or lands beyond it;
        /// +∞ otherwise — a ball at rest stays there to be picked up).</param>
        /// <param name="fairBefore">Before this time the fair/foul call is not decided: a take there counts only over fair
        /// ground (touching it over foul ground would make it a dead foul; the defense lets it go).</param>
        /// <param name="initialVelocity">The defender's velocity when he starts reacting (TASK-006B: a receiver already running
        /// adjusts without stopping; zero for TASK-005's fielders standing at their positions).</param>
        public static Intercept Solve(BallInPlay play, Vector3d start, FielderProfile profile, FieldLayout field, double playableUntil,
            double fairBefore = double.NegativeInfinity, Vector3d initialVelocity = default)
        {
            double t0 = play.First.Time;
            double rest = play.EndTime;
            // Past the end of the play the ball lies at rest; search until the defender could walk there and a bit.
            Vector3d final = play.Final.Position;
            double restReach = t0 + profile.ReactionTime + RunningLaw.RunThroughTime(profile, Ground(final - start).Length) + 0.1;
            double end = Math.Min(playableUntil, Math.Max(rest, restReach));
            double previous = t0;
            for (double t = t0 + SearchStep; t <= end + 1e-12; t += SearchStep)
            {
                if (!Feasible(play, start, profile, field, t, fairBefore, initialVelocity, out _))
                {
                    previous = t;
                    continue;
                }

                // Refine the first feasible time between the last infeasible sample and this one.
                double lo = previous, hi = t;
                for (int i = 0; i < 40; i++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (Feasible(play, start, profile, field, mid, fairBefore, initialVelocity, out _)) hi = mid;
                    else lo = mid;
                }

                double when = hi;
                // Plenty of time on a fly ball: let it come down to a comfortable height (still in the air, still feasible).
                BallState atWhen = play.StateAt(when);
                if (BeforeFirstContact(play, when) && atWhen.Position.Z > ComfortCatchHeight && atWhen.Velocity.Z < 0.0)
                    for (double c = when + SearchStep; c <= end && BeforeFirstContact(play, c); c += SearchStep)
                        if (play.StateAt(c).Position.Z <= ComfortCatchHeight && Feasible(play, start, profile, field, c, fairBefore, initialVelocity, out _))
                        {
                            when = c;
                            break;
                        }

                Feasible(play, start, profile, field, when, fairBefore, initialVelocity, out Intercept found);
                return found;
            }

            return Intercept.None;
        }

        /// <summary>Can the defender take the ball at ball time <paramref name="t"/>?</summary>
        public static bool Feasible(BallInPlay play, Vector3d start, FielderProfile profile, FieldLayout field, double t, out Intercept intercept) =>
            Feasible(play, start, profile, field, t, double.NegativeInfinity, default, out intercept);

        public static bool Feasible(BallInPlay play, Vector3d start, FielderProfile profile, FieldLayout field, double t, double fairBefore, out Intercept intercept) =>
            Feasible(play, start, profile, field, t, fairBefore, default, out intercept);

        /// <summary>As above for a defender moving at <paramref name="initialVelocity"/> when he starts reacting: his momentum
        /// carries him to <see cref="ContinuationMotion.Carried"/> and the route is measured from there (the same law
        /// <see cref="ContinuationMotion"/> then runs).</summary>
        public static bool Feasible(BallInPlay play, Vector3d start, FielderProfile profile, FieldLayout field, double t, double fairBefore,
            Vector3d initialVelocity, out Intercept intercept)
        {
            intercept = Intercept.None;
            BallState ball = play.StateAt(t);
            if (ball.Position.Z > profile.CatchHeightMax) return false;
            if (t < fairBefore && !FairFoul.OverFairTerritory(ball.Position)) return false;
            Vector3d origin = initialVelocity.X == 0.0 && initialVelocity.Y == 0.0
                ? start
                : ContinuationMotion.Carried(profile, start, initialVelocity, t - play.First.Time - profile.ReactionTime);
            Vector3d toBall = Ground(ball.Position - origin);
            double gap = toBall.Length;
            double route = Math.Max(0.0, gap - profile.ReachAt(ball.Position.Z));
            Vector3d target = gap > 0.0 ? origin + toBall * (route / gap) : origin;
            if (!InsidePark(field, target)) return false;
            // Reachable by t ⇔ the running law covers the route in the time available (Distance is monotone, so this equals
            // RunThroughTime(route) ≤ t − start without its bisection on every probe).
            double available = t - play.First.Time - profile.ReactionTime;
            if (available < 0.0 || route > RunningLaw.Distance(profile, available)) return false;
            double arrival = play.First.Time + profile.ReactionTime + RunningLaw.RunThroughTime(profile, route);
            InterceptKind kind = BeforeFirstContact(play, t) ? InterceptKind.FlyCatch
                : ball.Position.Z <= profile.PickupHeightMax ? InterceptKind.GroundPickup : InterceptKind.HopCatch;
            intercept = new Intercept(t, ball, target, route, arrival, kind);
            return true;
        }

        /// <summary>Extra horizontal reach of a dive (m): the glove laid out ≈ one body length beyond the running reach
        /// (Docs/FIELDING_MOTION_REFERENCE.md; conservative).</summary>
        public const double DiveReach = 1.0;
        /// <summary>A dive takes a ball this low and no higher (centre, m): from just off the grass to about the waist.</summary>
        public const double DiveLow = 0.15, DiveHigh = 1.2;
        /// <summary>He dives only out of a run: at least this far run to the take-off (m; at ≈ 70 % of top speed by then).</summary>
        public const double DiveRunUp = 6.0;

        /// <summary>
        /// The earliest diving catch of a fly ball that would otherwise drop: before its first contact, low enough to dive for,
        /// the ball's ground point within the running reach plus <see cref="DiveReach"/> after a run of at least
        /// <see cref="DiveRunUp"/>. The fielder runs through his take-off point at full speed (no stop).
        /// </summary>
        public static Intercept SolveDive(BallInPlay play, Vector3d start, FielderProfile profile, FieldLayout field, double playableUntil, double fairBefore = double.NegativeInfinity)
        {
            double t0 = play.First.Time;
            for (double t = t0 + SearchStep; t <= playableUntil && BeforeFirstContact(play, t); t += SearchStep)
            {
                BallState ball = play.StateAt(t);
                if (ball.Position.Z < DiveLow || ball.Position.Z > DiveHigh) continue;
                if (t < fairBefore && !FairFoul.OverFairTerritory(ball.Position)) continue;
                Vector3d toBall = Ground(ball.Position - start);
                double gap = toBall.Length;
                double route = Math.Max(0.0, gap - profile.ReachAt(ball.Position.Z) - DiveReach);
                if (route < DiveRunUp) continue;
                double available = t - t0 - profile.ReactionTime;
                if (available < 0.0 || route > RunningLaw.Distance(profile, available)) continue;
                Vector3d target = start + toBall * (route / gap);
                if (!InsidePark(field, target)) continue;
                double arrival = t0 + profile.ReactionTime + RunningLaw.RunThroughTime(profile, route);
                return new Intercept(t, ball, target, route, arrival, InterceptKind.FlyCatch, dive: true);
            }

            return Intercept.None;
        }

        /// <summary>The ball has not yet touched the ground or the wall at <paramref name="t"/>.</summary>
        public static bool BeforeFirstContact(BallInPlay play, double t)
        {
            foreach (BallEvent e in play.Events)
                if (e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.WallImpact || e.Kind == BallEventKind.LeftPlay)
                    return t < e.Time;
            return t < play.EndTime;
        }

        private static bool InsidePark(FieldLayout field, Vector3d p) =>
            FieldLayout.OutsideFoulLine(p.X, p.Y) > 0.0 || field.DistanceBeyondFence(p.X, p.Y, out _) <= -WallClearance;

        private static Vector3d Ground(Vector3d v) => new Vector3d(v.X, v.Y, 0.0);
    }
}
