using System;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>How the fielding of a batted ball ended.</summary>
    public enum FieldingOutcome
    {
        /// <summary>A defender took the ball (catch or pickup): see <see cref="FieldingPlay.Intercept"/>.</summary>
        Fielded,
        /// <summary>Foul ball: dead, nobody fields it (TASK-005 simplification: no foul catches yet).</summary>
        DeadFoul,
        /// <summary>Out of the park (home run, or a fair bounce over the fence) before anyone could take it.</summary>
        OutOfPlay,
    }

    /// <summary>Who owns the ball at a moment of the play.</summary>
    public enum BallAuthority
    {
        /// <summary>The authoritative ball in play (<see cref="BallInPlay"/>) moves it.</summary>
        FreeBall,
        /// <summary>A defender holds it.</summary>
        Possessed,
        /// <summary>Thrown by a defender: its throw flight moves it until a catch (or its first ground contact).</summary>
        Thrown,
    }

    /// <summary>
    /// The defense's response to one batted ball, fully determined at contact from the authoritative <see cref="BallInPlay"/>:
    /// each defender's motion, the primary defender and his intercept, the possession moment and the (fielder-aware)
    /// fair/foul call. Everything is a function of time, so the play replays identically at any frame rate.
    /// </summary>
    public sealed class FieldingPlay
    {
        private readonly FielderMotion[] _motions;
        private readonly Intercept[] _candidates;

        internal FieldingPlay(BallInPlay ball, FieldingOutcome outcome, DefensivePosition? primary, Intercept intercept, FielderMotion[] motions,
            Intercept[] candidates, BallInPlayCall call, double endTime)
        {
            EndTime = endTime;
            Ball = ball;
            Outcome = outcome;
            Primary = primary;
            Intercept = intercept;
            _motions = motions;
            _candidates = candidates;
            Call = call;
            if (outcome == FieldingOutcome.Fielded)
            {
                Vector3d holder = _motions[(int)primary.Value].PositionAt(intercept.Time);
                _ballOffset = intercept.Ball.Position - holder;
            }
        }

        private readonly Vector3d _ballOffset;   // ball relative to the holder from the moment of possession

        public BallInPlay Ball { get; }
        public FieldingOutcome Outcome { get; }
        public DefensivePosition? Primary { get; }
        public Intercept Intercept { get; }
        public BallInPlayCall Call { get; }

        /// <summary>When a defender takes the ball (+∞ if nobody does).</summary>
        public double PossessionTime => Outcome == FieldingOutcome.Fielded ? Intercept.Time : double.PositiveInfinity;

        /// <summary>When the play is over for the batting loop: possession; the foul call for a dead foul; the ball leaving the
        /// park (or coming to rest) when nobody takes it.</summary>
        public double EndTime { get; }

        public FielderMotion Motion(DefensivePosition position) => _motions[(int)position];

        /// <summary>Each defender's own earliest intercept (diagnostics; infeasible ones have Feasible = false).</summary>
        public Intercept Candidate(DefensivePosition position) => _candidates[(int)position];

        public BallAuthority AuthorityAt(double time) => time >= PossessionTime ? BallAuthority.Possessed : BallAuthority.FreeBall;

        /// <summary>The ball's authoritative position: on its trajectory while free, carried by the holder once possessed.</summary>
        public Vector3d BallPositionAt(double time)
        {
            if (AuthorityAt(time) == BallAuthority.FreeBall) return Ball.StateAt(time).Position;
            // From where it was taken into the glove at the holder's chest over SecureTime (continuous at possession).
            double u = Math.Min(1.0, (time - PossessionTime) / SecureTime);
            u = u * u * (3.0 - 2.0 * u);
            return _motions[(int)Primary.Value].PositionAt(time) + (1.0 - u) * _ballOffset + u * HoldOffset;
        }

        /// <summary>Time to bring the ball from the take to the chest (s), and where it is then held relative to the feet (m).</summary>
        public const double SecureTime = 0.3;
        public static readonly Vector3d HoldOffset = new Vector3d(0.0, 0.0, 1.2);
    }

    /// <summary>
    /// Builds the <see cref="FieldingPlay"/>: every defender's earliest intercept of the authoritative trajectory, one
    /// primary (earliest intercept; within <see cref="TieWindow"/> the larger arrival margin, then positional priority),
    /// the primary's route, and everyone else holding ready (no swarm). Foul balls are dead (no foul catches yet); out of
    /// the park nobody chases the ball once it has cleared the fence.
    /// </summary>
    public static class FieldingSolver
    {
        /// <summary>Intercepts this close in time count as a tie (s).</summary>
        public const double TieWindow = 0.10;
        /// <summary>A route with at least this much spare time stops at the target and waits (s).</summary>
        public const double SettleMargin = 0.0;

        /// <summary>Priority when intercepts tie: centre fielder over corners, outfielders over infielders, SS over 2B/3B…</summary>
        private static readonly DefensivePosition[] Priority =
        {
            DefensivePosition.CenterField, DefensivePosition.LeftField, DefensivePosition.RightField, DefensivePosition.Shortstop,
            DefensivePosition.SecondBase, DefensivePosition.ThirdBase, DefensivePosition.FirstBase, DefensivePosition.P, DefensivePosition.C,
        };

        public static FieldingPlay Solve(BallInPlay ball, DefensiveAlignment alignment, Func<DefensivePosition, FielderProfile> profiles, FieldLayout field)
        {
            var motions = new FielderMotion[DefensiveAlignment.Count];
            var candidates = new Intercept[DefensiveAlignment.Count];
            for (int i = 0; i < motions.Length; i++) motions[i] = new FielderMotion(profiles((DefensivePosition)i), alignment[(DefensivePosition)i]);

            FairFoulResult unfielded = FairFoul.Call(ball);
            // A fly ball that comes down foul is dead (foul catches are not modelled yet). A ground ball that would settle
            // or pass the base foul is still in play until then: a defender who touches it over fair ground makes it fair.
            bool groundFoul = unfielded.Call == BallInPlayCall.Foul && (unfielded.Basis == CallBasis.Settled || unfielded.Basis == CallBasis.PassingBase);
            if (unfielded.Call == BallInPlayCall.Foul && !groundFoul)
                return new FieldingPlay(ball, FieldingOutcome.DeadFoul, null, Intercept.None, motions, candidates, BallInPlayCall.Foul, unfielded.At.Time);

            // In play until it leaves the park (clears the fence on the fly or on a bounce, or lands beyond it); a ball at
            // rest in the park stays there to be picked up. A foul roller only until the call.
            double playable = double.PositiveInfinity;
            foreach (BallEvent e in ball.Events)
                if (e.Kind == BallEventKind.ClearedFence || e.Kind == BallEventKind.LeftPlay)
                {
                    playable = e.Time;
                    break;
                }

            if (groundFoul) playable = Math.Min(playable, unfielded.At.Time);
            // Until the call is decided only takes over fair ground count (each defender's search applies it), so the
            // primary is chosen among legal takes and a would-be-fair ball is never touched foul (Codex review).
            for (int i = 0; i < motions.Length; i++)
                candidates[i] = InterceptSolver.Solve(ball, alignment[(DefensivePosition)i], profiles((DefensivePosition)i), field, playable, unfielded.At.Time);
            int best = SelectPrimary(candidates);

            if (best < 0)
                return groundFoul
                    ? new FieldingPlay(ball, FieldingOutcome.DeadFoul, null, Intercept.None, motions, candidates, BallInPlayCall.Foul, unfielded.At.Time)
                    : new FieldingPlay(ball, FieldingOutcome.OutOfPlay, null, Intercept.None, motions, candidates, unfielded.Call,
                        double.IsPositiveInfinity(playable) ? ball.EndTime : playable);

            var position = (DefensivePosition)best;
            Intercept intercept = candidates[best];
            FielderProfile profile = profiles(position);
            Vector3d start = alignment[position];
            double reacts = ball.First.Time + profile.ReactionTime;
            (_, double stopTime) = RunningLaw.StopTime(profile, intercept.RouteDistance);
            // Time to spare: run, brake and wait at the spot. Tight: arrive at speed exactly at the intercept (a later
            // start absorbs any spare time shorter than the braking would need) and brake past it after the take.
            motions[best] = reacts + stopTime <= intercept.Time - SettleMargin
                ? new FielderMotion(profile, start, intercept.FielderTarget, reacts, true)
                : new FielderMotion(profile, start, intercept.FielderTarget, intercept.Time - RunningLaw.RunThroughTime(profile, intercept.RouteDistance), false);

            // Fielded before the call was decided (e.g. a slow roller taken before first base): judged where it was taken —
            // fair, as only fair-ground takes are allowed then. A ball taken before it leaves the park is taken before the call.
            BallInPlayCall call = intercept.Time < unfielded.At.Time ? BallInPlayCall.Fair : unfielded.Call;   // early takes are over fair ground
            return new FieldingPlay(ball, FieldingOutcome.Fielded, position, intercept, motions, candidates, call, intercept.Time);
        }

        public static FieldingPlay Solve(BallInPlay ball) => Solve(ball, DefensiveAlignment.Standard, FielderProfile.For, FieldLayout.Standard);

        /// <summary>
        /// The primary among the defenders' intercepts: the earliest; among those within <see cref="TieWindow"/> of it, the
        /// larger arrival margin, then positional priority (−1 if nobody can take the ball). Two passes, so the choice never
        /// drifts beyond the window through a chain of pairwise ties.
        /// </summary>
        public static int SelectPrimary(Intercept[] candidates)
        {
            double earliest = double.PositiveInfinity;
            foreach (Intercept c in candidates) if (c.Feasible) earliest = Math.Min(earliest, c.Time);
            int best = -1;
            for (int i = 0; i < candidates.Length; i++)
            {
                Intercept c = candidates[i];
                if (!c.Feasible || c.Time > earliest + TieWindow) continue;
                if (best < 0) { best = i; continue; }
                Intercept b = candidates[best];
                if (Math.Abs(c.Margin - b.Margin) > 1e-9 ? c.Margin > b.Margin
                    : Array.IndexOf(Priority, (DefensivePosition)i) < Array.IndexOf(Priority, (DefensivePosition)best)) best = i;
            }

            return best;
        }
    }
}
