using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>
    /// Runners' decisions (TASK-007): simple, deterministic, replaceable heuristics from standard coaching conventions
    /// (Docs/BASERUNNING.md). Every "go" compares the runner's own predicted arrival (the same running law he then runs)
    /// with an estimate of when the defense could have the ball there, and requires a margin that shrinks with outs — the
    /// run-expectancy break-even for sending a runner (0.30 s with none out, 0.15 s with one, −0.10 s with two; DERIVED).
    /// No randomness.
    /// </summary>
    internal static class RunnerBrain
    {
        public static double Margin(int outs) => outs == 0 ? 0.30 : outs == 1 ? 0.15 : -0.10;

        /// <summary>Fly caught deep enough for a runner on second to tag up and try for third (m from home; ASSUMED ≈ 250 ft).</summary>
        public const double DeepFly = 75.0;
        /// <summary>A fly deep enough that a runner on third waits on the base to tag up (m; ASSUMED ≈ 150 ft).</summary>
        public const double TagUpFly = 45.0;

        public static bool RunsThroughFirst(LivePlay play, LiveRunner r) => r.Id.IsBatter;

        /// <summary>The first intention when the ball is put in play.</summary>
        public static LivePlay.Intent Initial(LivePlay play, LiveRunner r)
        {
            if (play.Kind == LivePlay.BallKind.Dead) return new LivePlay.Intent(LivePlay.IntentKind.Return, r.Id.From, true);
            if (r.Id.IsBatter) return new LivePlay.Intent(LivePlay.IntentKind.Go, Base.First);
            int outs = play.Situation.Outs;
            bool forced = Forces.IsForced(r.Id, play.Situation.Bases);
            switch (play.Kind)
            {
                case LivePlay.BallKind.Caught:
                {
                    if (outs == 2) return new LivePlay.Intent(LivePlay.IntentKind.Go, r.Id.Next, true);   // run on contact
                    if (play.CatchHang < LivePlay.LineDriveHang) return new LivePlay.Intent(LivePlay.IntentKind.Freeze, r.Id.From);
                    double depth = play.Fielding.Intercept.Ball.Position.Length;
                    if (r.Id.From == Base.Third && depth > TagUpFly || r.Id.From == Base.Second && depth > DeepFly)
                        return new LivePlay.Intent(LivePlay.IntentKind.TagUp, r.Id.From);
                    return new LivePlay.Intent(LivePlay.IntentKind.Halfway, r.Id.Next);
                }

                case LivePlay.BallKind.Grounder:
                {
                    if (forced || outs == 2) return new LivePlay.Intent(LivePlay.IntentKind.Go, r.Id.Next, outs == 2);
                    // An unforced runner on second goes on a ball to the right side (behind him); otherwise runners hold.
                    bool rightSide = play.Fielding.Intercept.Ball.Position.X > 0.0;
                    if (r.Id.From == Base.Second && rightSide) return new LivePlay.Intent(LivePlay.IntentKind.Go, Base.Third);
                    return new LivePlay.Intent(LivePlay.IntentKind.Hold, r.Id.From);
                }

                default:   // a hit: everyone runs; extra bases are decided on the way
                    return new LivePlay.Intent(LivePlay.IntentKind.Go, r.Id.Next, outs == 2);
            }
        }

        /// <summary>At a ball event (possession, throw released, a missed throw): a runner standing or holding off his base
        /// goes on if he safely can, otherwise gets back to his base; runners on the move keep going (decisions on the way).</summary>
        public static LivePlay.Intent Reconsider(LivePlay play, LiveRunner r, double now)
        {
            if (r.MustRetouch) return new LivePlay.Intent(LivePlay.IntentKind.Return, r.LastTouched);
            if (play.Kind == LivePlay.BallKind.Dead) return LivePlay.Intent.Keep;
            if (r.Phase == RunnerPhase.Running || r.Phase == RunnerPhase.Overrunning || r.Phase == RunnerPhase.Returning) return LivePlay.Intent.Keep;
            if (play.Kind == LivePlay.BallKind.Caught && now < play.Fielding.PossessionTime) return LivePlay.Intent.Keep;
            Base next = BaseLeg.Bases(r.LastTouched);
            if (CanAdvance(play, r, next, now)) return new LivePlay.Intent(LivePlay.IntentKind.Go, next);
            return r.TouchingBaseAt(now, out _) ? LivePlay.Intent.Keep : new LivePlay.Intent(LivePlay.IntentKind.Return, r.LastTouched);
        }

        /// <summary>A fly ball has just been caught (first touch: runners may leave now, OBR 5.09(a)(1) Comment): a runner off
        /// his base must go back and retouch; one on it tags up if he can make the next base.</summary>
        public static LivePlay.Intent AfterCatch(LivePlay play, LiveRunner r, double now)
        {
            if (!r.TouchingBaseAt(now, out Base on) || on != r.LastTouched)
            {
                r.MustRetouch = true;
                return new LivePlay.Intent(LivePlay.IntentKind.Return, r.LastTouched);
            }

            Base next = BaseLeg.Bases(r.LastTouched);
            return CanAdvance(play, r, next, now) ? new LivePlay.Intent(LivePlay.IntentKind.Go, next) : new LivePlay.Intent(LivePlay.IntentKind.Hold, r.LastTouched);
        }

        /// <summary>Approaching <paramref name="destination"/> (the moment he must brake to stop there): go on to the next base?</summary>
        public static bool GoOn(LivePlay play, LiveRunner r, Base destination, double now)
        {
            if (destination == Base.Home || play.Kind == LivePlay.BallKind.Dead) return false;   // a dead ball: the award only
            // A fly that will be caught: the batter runs it out, nobody takes an extra base before the catch.
            if (play.Kind == LivePlay.BallKind.Caught && now < play.Fielding.PossessionTime) return false;
            Base next = BaseLeg.Bases(destination);
            if (Blocked(play, r, next)) return false;
            double runner = RunnerEtaGoingOn(play, r, next, now);
            return runner + Margin(play.Outs) < play.DefenseEta(next, now) + TagAllowanceFor(play, r, next, now);
        }

        private static bool CanAdvance(LivePlay play, LiveRunner r, Base next, double now)
        {
            if (Blocked(play, r, next)) return false;
            double runner = play.RunnerEta(r, next, now);
            return runner + Margin(play.Outs) < play.DefenseEta(next, now) + TagAllowanceFor(play, r, next, now);
        }

        /// <summary>A forced runner is put out by a touch of the base; otherwise the defender has to tag him too.</summary>
        private static double TagAllowanceFor(LivePlay play, LiveRunner r, Base next, double now) =>
            play.ForcedAt(r, now) && next == r.Id.Next ? 0.0 : LivePlay.TagAllowance;

        private static double RunnerEtaGoingOn(LivePlay play, LiveRunner r, Base next, double now)
        {
            PathMotion m = r.Current.Motion;
            return RunnerPlanner.ArrivalTime(play.Profile, r.Current.Leg, m.DistanceAt(now), m.VelocityAt(now), now, next, false, play.Kind == LivePlay.BallKind.Hit);
        }

        /// <summary>A runner ahead of him will be on <paramref name="b"/> (or short of it): he cannot go there.</summary>
        public static bool Blocked(LivePlay play, LiveRunner r, Base b)
        {
            int want = Index(r, b);
            foreach (LiveRunner ahead in play.Runners)
            {
                if (ahead == r || ahead.IsDone || ahead.Id.From <= r.Id.From) continue;   // ahead: started further round (home first)
                if (Index(ahead, ahead.Target) <= want) return true;
            }

            return false;
        }

        /// <summary>A base's place in a runner's trip around the bases (home is 4 once he has left it).</summary>
        private static int Index(LiveRunner r, Base b) => b == Base.Home ? 4 : (int)b;   // runners only ever head for home after third
    }
}
