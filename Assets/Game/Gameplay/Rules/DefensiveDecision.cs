using System;
using System.Collections.Generic;
using System.Linq;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Rules
{
    public enum DefensiveActionKind
    {
        /// <summary>Keep the ball (no play is on).</summary>
        HoldBall,
        /// <summary>Carry the ball to a base himself (an unassisted force out).</summary>
        TouchBase,
        /// <summary>Throw to the defender covering a base (a force out there, or returning the ball to the infield).</summary>
        ThrowToBase,
        /// <summary>Get the ball to a base ahead of a runner who is not forced and tag him (by a throw or by carrying it).</summary>
        TagRunner,
    }

    /// <summary>
    /// One thing the defender holding the ball can do, evaluated on the authoritative state: who ends up with the ball on
    /// which base and when (<see cref="CompletionTime"/>, +∞ if it cannot be done), the runner it can retire and when he
    /// arrives, whether it retires him, and the resulting defensive play (the motion and ball timeline that would follow).
    /// </summary>
    public sealed class DefensiveAction
    {
        internal DefensiveAction(DefensiveActionKind kind, Base? target, DefensivePosition actor, double completionTime, Runner? runner,
            double runnerArrival, double outTime, DefensivePlay play, string note)
        {
            Kind = kind;
            Target = target;
            Actor = actor;
            CompletionTime = completionTime;
            Runner = runner;
            RunnerArrival = runnerArrival;
            Play = play;
            Note = note;
            OutTime = outTime;
        }

        public DefensiveActionKind Kind { get; }
        public Base? Target { get; }
        /// <summary>The defender who has the ball at the end of it (the fielder himself, or the receiver of the throw).</summary>
        public DefensivePosition Actor { get; }
        /// <summary>When the actor holds the ball on the target base (+∞: cannot; the possession time for a hold).</summary>
        public double CompletionTime { get; }
        /// <summary>The runner it can put out (null: no runner involved).</summary>
        public Runner? Runner { get; }
        /// <summary>When that runner touches the target base (+∞ without a runner).</summary>
        public double RunnerArrival { get; }
        /// <summary>When the out is made if it is (the base touch for a force; the tag for a tag play; +∞ if not).</summary>
        public double OutTime { get; }
        public DefensivePlay Play { get; }
        public string Note { get; }
        /// <summary>Set for a force at second with the batter-runner forced too: a relay to first could beat him (an
        /// estimate for the debug view; double plays are not executed yet).</summary>
        public bool DoublePlayPossible { get; internal set; }

        public bool Feasible => !double.IsPositiveInfinity(CompletionTime);
        /// <summary>A runner forced to the target base (else a tag is needed).</summary>
        public bool IsForce => Runner != null && (Kind == DefensiveActionKind.TouchBase || Kind == DefensiveActionKind.ThrowToBase);
        public bool Retires => Runner != null && TimingCall.DefenseFirst(OutTime, RunnerArrival);
        /// <summary>Runner's arrival − the defense's out moment (completion for a force, the tag for a tag play; s; > 0: the
        /// defense is first; −∞ for a tag that never comes).</summary>
        public double Margin => RunnerArrival - (IsForce ? CompletionTime : OutTime);

        public override string ToString()
        {
            string what = Kind switch
            {
                DefensiveActionKind.HoldBall => "Hold",
                DefensiveActionKind.TouchBase => $"Touch {Bases.Name(Target.Value)}",
                DefensiveActionKind.ThrowToBase => $"Throw {Bases.Name(Target.Value)}",
                _ => $"Tag at {Bases.Name(Target.Value)}" + (Play.Throw != null ? " (throw)" : " (carry)"),
            };
            return Note == null ? what : $"{what} — {Note}";
        }
    }

    /// <summary>
    /// The defense's first-pass decision after a possession (simple, deterministic, replaceable): candidate actions are built
    /// for every runner going somewhere — touch the base, throw to it, or (a runner who is not forced) tag him there — plus
    /// holding the ball; <see cref="Choose"/> picks by priority:
    /// 1. an immediate out (the fielder already holds the ball on a forced runner's base);
    /// 2. the earliest force out (a touch before a throw when equally early);
    /// 3. the earliest tag out;
    /// 4. a close play that misses (the runner by less than <see cref="CloseWindow"/>): still made — the closest one;
    /// 5. an outfielder returns the ball to second; anyone else holds it.
    /// No double plays, cut-offs or game situation yet.
    /// </summary>
    public static class DefensiveDecision
    {
        /// <summary>A play the runner wins by less than this is still made (s, ASSUMED: fielders throw on close plays).</summary>
        public const double CloseWindow = 0.5;

        public static IReadOnlyList<DefensiveAction> Candidates(FieldingPlay fielding, BaseOccupancy before, IReadOnlyList<Runner> advancing,
            IRunnerTiming timing, Func<DefensivePosition, FielderProfile> profiles, Func<Base, DefensivePlay> throwTo)
        {
            var list = new List<DefensiveAction>();
            if (fielding.Outcome != FieldingOutcome.Fielded) return list;
            var planned = new Dictionary<Base, DefensivePlay>();
            DefensivePlay ThrowTo(Base b) => planned.TryGetValue(b, out DefensivePlay p) ? p : planned[b] = throwTo(b);
            DefensivePosition holder = fielding.Primary.Value;
            double take = fielding.PossessionTime, contact = fielding.Ball.First.Time;
            list.Add(new DefensiveAction(DefensiveActionKind.HoldBall, null, holder, take, null, double.PositiveInfinity, double.PositiveInfinity,
                new DefensivePlay(fielding, null), null));

            foreach (Runner runner in advancing)
            {
                Base target = runner.Next;
                double arrival = contact + timing.TimeToNextBase(runner);
                bool forced = Forces.IsForced(runner, before);
                DefensiveAction touch = Carry(fielding, holder, target, runner, arrival, forced, timing, profiles);
                DefensiveAction thrown = Throw(fielding, ThrowTo(target), target, runner, arrival, forced, timing, profiles);
                list.Add(touch);
                if (thrown != null) list.Add(thrown);
                if (forced && thrown != null && target == Base.Second && advancing.Contains(Rules.Runner.Batter))
                    thrown.DoublePlayPossible = thrown.Retires && RelayBeatsBatter(thrown, contact + timing.TimeToNextBase(Rules.Runner.Batter));
            }

            if (IsOutfielder(holder) && throwTo != null)
                list.Add(Throw(fielding, ThrowTo(Base.Second), Base.Second, null, double.PositiveInfinity, false, timing, profiles)
                         ?? new DefensiveAction(DefensiveActionKind.ThrowToBase, Base.Second, holder, double.PositiveInfinity, null,
                             double.PositiveInfinity, double.PositiveInfinity, new DefensivePlay(fielding, null), "not caught"));
            return list;
        }

        /// <summary>Picks among <paramref name="candidates"/> (see the class summary) and says why.</summary>
        public static (DefensiveAction Action, string Reason) Choose(IReadOnlyList<DefensiveAction> candidates)
        {
            DefensiveAction hold = candidates.First(a => a.Kind == DefensiveActionKind.HoldBall);
            double take = hold.CompletionTime;
            DefensiveAction immediate = candidates.Where(a => a.IsForce && a.Retires && a.CompletionTime <= take + 1e-9).OrderBy(a => a.CompletionTime).FirstOrDefault();
            if (immediate != null) return (immediate, "immediate out");
            DefensiveAction force = candidates.Where(a => a.IsForce && a.Retires)
                .OrderBy(a => a.CompletionTime).ThenBy(a => a.Kind == DefensiveActionKind.TouchBase ? 0 : 1).FirstOrDefault();
            if (force != null) return (force, "earliest force out");
            DefensiveAction tag = candidates.Where(a => a.Kind == DefensiveActionKind.TagRunner && a.Retires).OrderBy(a => a.OutTime).FirstOrDefault();
            if (tag != null) return (tag, "tag out");
            DefensiveAction close = candidates.Where(a => a.Runner != null && a.Feasible && a.Margin > -CloseWindow)
                .OrderByDescending(a => a.Margin).FirstOrDefault();
            if (close != null) return (close, "close play");
            DefensiveAction back = candidates.FirstOrDefault(a => a.Kind == DefensiveActionKind.ThrowToBase && a.Runner == null && a.Feasible);
            if (back != null) return (back, "no play: ball back to the infield");
            return (hold, "no play");
        }

        /// <summary>A throw to <paramref name="target"/> with no runner (debug override, or returning the ball).</summary>
        public static DefensiveAction ThrowTo(FieldingPlay fielding, DefensivePlay play, Base target) =>
            Throw(fielding, play, target, null, double.PositiveInfinity, false, ReferenceRunnerTiming.Instance, FielderProfile.For)
            ?? new DefensiveAction(DefensiveActionKind.ThrowToBase, target, fielding.Primary.Value, double.PositiveInfinity, null,
                double.PositiveInfinity, double.PositiveInfinity, play, "not caught");

        /// <summary>
        /// Does the actor hold the ball while touching <paramref name="b"/> at <paramref name="time"/>? The authoritative force
        /// test: possession (the play's ball authority) and the base-touch envelope — no colliders.
        /// </summary>
        public static bool HoldsBallOnBase(DefensivePlay play, DefensivePosition actor, Base b, double time) =>
            play.HolderAt(time) == actor && BaseTouch.IsTouching(play.FielderPositionAt(actor, time), b);

        private static DefensiveAction Carry(FieldingPlay fielding, DefensivePosition holder, Base target, Runner runner, double arrival, bool forced,
            IRunnerTiming timing, Func<DefensivePosition, FielderProfile> profiles)
        {
            double take = fielding.PossessionTime;
            FielderMotion route = fielding.Motion(holder);
            Vector3d at = route.PositionAt(take), velocity = route.VelocityAt(take), bag = FieldLayout.BasePosition(target);
            FielderProfile profile = profiles(holder);
            double t = ContinuationMotion.EarliestWithin(profile, at, velocity, bag, BaseTouch.Radius);
            DefensivePlay play;
            if (t == 0.0) play = new DefensivePlay(fielding, null);   // already on it
            else if (!forced)
            {
                // A tag: he goes to the bag and stops on it to wait for the runner (running through it he could not tag).
                ContinuationMotion toRest = ContinuationMotion.ToRest(profile, at, velocity, take, bag);
                if (toRest == null) return Tag(DefensiveActionKind.TagRunner, target, holder, double.PositiveInfinity, runner, arrival, new DefensivePlay(fielding, null), timing);
                play = new DefensivePlay(fielding, null, toRest);
                t = FirstWithin(toRest, bag, take) - take;
            }
            else
            {
                // A force: the earliest touch, at speed — he runs across the bag.
                // To the nearest point of the touch envelope toward where his momentum takes him, exactly then.
                Vector3d carried = ContinuationMotion.Carried(profile, at, velocity, t), off = carried - bag;
                double d = new Vector3d(off.X, off.Y, 0.0).Length, r = BaseTouch.Radius * (1.0 - 1e-6);
                Vector3d aim = d > r ? bag + new Vector3d(off.X, off.Y, 0.0) * (r / d) : carried;
                play = new DefensivePlay(fielding, null, new ContinuationMotion(profile, at, velocity, take, aim, take + t));
            }

            double done = take + t;
            if (!HoldsBallOnBase(play, holder, target, done)) done = double.PositiveInfinity;   // never on it with the ball
            return forced
                ? new DefensiveAction(DefensiveActionKind.TouchBase, target, holder, done, runner, arrival, done, play, null)
                : Tag(DefensiveActionKind.TagRunner, target, holder, done, runner, arrival, play, timing);
        }

        private static DefensiveAction Throw(FieldingPlay fielding, DefensivePlay play, Base target, Runner? runner, double arrival, bool forced,
            IRunnerTiming timing, Func<DefensivePosition, FielderProfile> profiles)
        {
            ThrowPlay th = play.Throw;
            if (th == null) return null;
            // The force is made when the receiver holds the ball on the bag: at the catch if he takes it there (covering
            // receivers do); a catch off the bag does not complete it (ponytail: returning to the bag is not modelled).
            double done = th.Caught && HoldsBallOnBase(play, th.Receiver, target, th.Catch.Time) ? th.Catch.Time : double.PositiveInfinity;
            string note = !th.Caught ? "not caught" : double.IsPositiveInfinity(done) ? "caught off the bag" : null;
            if (runner == null || forced)
                return new DefensiveAction(DefensiveActionKind.ThrowToBase, target, th.Receiver, done, runner, arrival, done, play, note);
            return Tag(DefensiveActionKind.TagRunner, target, th.Receiver, done, runner.Value, arrival, play, timing);
        }

        /// <summary>The first moment (≥ <paramref name="from"/>) the motion is within the touch envelope of <paramref name="bag"/>
        /// (5 ms scan then bisection; it ends on the bag, so there is one).</summary>
        private static double FirstWithin(ContinuationMotion m, Vector3d bag, double from)
        {
            bool In(double t)
            {
                Vector3d d = m.PositionAt(t) - bag;
                return Math.Sqrt(d.X * d.X + d.Y * d.Y) <= BaseTouch.Radius;
            }

            double previous = from;
            for (double t = from; t <= m.RestTime + 0.005; t += 0.005)
            {
                if (!In(t))
                {
                    previous = t;
                    continue;
                }

                if (t == from) return t;
                double lo = previous, hi = t;
                for (int i = 0; i < 40; i++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (In(mid)) hi = mid;
                    else lo = mid;
                }

                return hi;
            }

            return m.RestTime;
        }

        /// <summary>A tag play: from <paramref name="ready"/> (the defender holds the ball at the base) the first moment the
        /// runner comes within tag reach of him (<see cref="TagRules.CanTag"/>, 1 ms steps) before he touches the base.</summary>
        private static DefensiveAction Tag(DefensiveActionKind kind, Base target, DefensivePosition actor, double ready, Runner runner, double arrival,
            DefensivePlay play, IRunnerTiming timing)
        {
            double contact = play.Fielding.Ball.First.Time, tag = double.PositiveInfinity;
            if (!double.IsPositiveInfinity(ready))
                for (double t = ready; t < arrival; t += 1e-3)
                    if (TagRules.CanTag(play.FielderPositionAt(actor, t), timing.PositionAt(runner, t - contact), play.HolderAt(t) == actor))
                    {
                        tag = t;
                        break;
                    }

            return new DefensiveAction(kind, target, actor, ready, runner, arrival, tag, play, null);
        }

        /// <summary>Estimate (debug only): catch at second + the receiver's transfer + the throw to first at his speed.</summary>
        private static bool RelayBeatsBatter(DefensiveAction atSecond, double batterArrival)
        {
            if (!atSecond.Feasible) return false;
            ThrowProfile arm = ThrowProfile.For(atSecond.Actor);
            double distance = (FieldLayout.BasePosition(Base.First) - FieldLayout.BasePosition(Base.Second)).Length;
            return TimingCall.DefenseFirst(atSecond.CompletionTime + arm.TransferTime + distance / arm.Speed, batterArrival);
        }

        public static bool IsOutfielder(DefensivePosition p) =>
            p == DefensivePosition.LeftField || p == DefensivePosition.CenterField || p == DefensivePosition.RightField;
    }

    public enum RunnerStatus
    {
        /// <summary>On his base, not running.</summary>
        OnBase,
        /// <summary>Running to his next base.</summary>
        Advancing,
        /// <summary>Reached the base he was going to.</summary>
        Safe,
        Out,
    }

    /// <summary>A runner's logical state at a moment (no motion: TASK-007).</summary>
    public readonly struct RunnerState
    {
        public RunnerState(Runner runner, RunnerStatus status, Base at, bool forced)
        {
            Runner = runner;
            Status = status;
            At = at;
            Forced = forced;
        }

        public Runner Runner { get; }
        public RunnerStatus Status { get; }
        /// <summary>The base he is on, going to (advancing) or reached (safe); where he was retired.</summary>
        public Base At { get; }
        /// <summary>Forced to advance right now (OBR force, removal applied).</summary>
        public bool Forced { get; }
    }

    /// <summary>
    /// One play under the rules: the fielding play, the bases occupied before it, the runners going somewhere, the defense's
    /// candidate actions and the chosen one (whose <see cref="DefensivePlay"/> is what happens), and the resolution — the
    /// chronological out/safe events and the out count. A pure function of its inputs.
    /// </summary>
    public sealed class RulesPlay
    {
        internal RulesPlay(FieldingPlay fielding, BaseOccupancy before, IRunnerTiming timing, IReadOnlyList<Runner> advancing,
            IReadOnlyList<DefensiveAction> candidates, DefensiveAction chosen, string reason, PlayResolution resolution)
        {
            Fielding = fielding;
            Before = before;
            Timing = timing;
            Advancing = advancing;
            Candidates = candidates;
            Chosen = chosen;
            Reason = reason;
            Resolution = resolution;
            Defense = chosen?.Play ?? new DefensivePlay(fielding, null);
        }

        public FieldingPlay Fielding { get; }
        public BaseOccupancy Before { get; }
        public IRunnerTiming Timing { get; }
        /// <summary>The runners going to their next base: the forced ones and any scripted to run on contact.</summary>
        public IReadOnlyList<Runner> Advancing { get; }
        public IReadOnlyList<DefensiveAction> Candidates { get; }
        /// <summary>The action taken (null: no play — foul ball, ball out of the park).</summary>
        public DefensiveAction Chosen { get; }
        public string Reason { get; }
        public PlayResolution Resolution { get; }
        public DefensivePlay Defense { get; }
        public double ContactTime => Fielding.Ball.First.Time;
        /// <summary>A batter-runner exists: the ball was fielded in play (a caught fly retires him at the catch).</summary>
        public bool HasBatterRunner => Fielding.Outcome == FieldingOutcome.Fielded;

        /// <summary>The runners of this play: the batter-runner (if any), then the runners on base.</summary>
        public IEnumerable<Runner> Runners
        {
            get
            {
                if (HasBatterRunner) yield return Runner.Batter;
                foreach (Runner r in Before.Runners) yield return r;
            }
        }

        public double ArrivalTime(Runner runner) => ContactTime + Timing.TimeToNextBase(runner);

        public RunnerState RunnerStateAt(Runner runner, double time)
        {
            foreach (PlayEvent e in Resolution.EventsUntil(time))
                if (e.Runner == runner)
                    return new RunnerState(runner, e.IsOut ? RunnerStatus.Out : RunnerStatus.Safe, e.At ?? runner.From, false);
            bool going = Advancing.Contains(runner) && time >= ContactTime;
            return new RunnerState(runner, going ? RunnerStatus.Advancing : RunnerStatus.OnBase, going ? runner.Next : runner.From,
                going && Forces.IsForcedAt(runner, Before, Resolution.Events, time));
        }
    }

    public static class PlayResolver
    {
        /// <summary>
        /// Resolves a fielded (or unfielded) ball under the rules. <paramref name="runsOnContact"/> scripts runners who are
        /// not forced to run anyway (TASK-006B tag scenarios; TASK-007 replaces this with runner decisions);
        /// <paramref name="choose"/> overrides the decision (debug controls).
        /// </summary>
        public static RulesPlay Resolve(FieldingPlay fielding, BaseOccupancy before, int outsBefore, IRunnerTiming timing = null,
            Func<Runner, bool> runsOnContact = null, Func<IReadOnlyList<DefensiveAction>, DefensiveAction> choose = null)
        {
            timing ??= ReferenceRunnerTiming.Instance;
            if (fielding.Outcome != FieldingOutcome.Fielded)
            {
                // Foul, or out of the park (a home run's trot is not modelled): a dead ball, no plays.
                var dead = new PlayResolution(Array.Empty<PlayEvent>(), outsBefore, fielding.EndTime, true);
                return new RulesPlay(fielding, before, timing, Array.Empty<Runner>(), Array.Empty<DefensiveAction>(), null, "dead ball", dead);
            }

            DefensivePosition holder = fielding.Primary.Value;
            if (fielding.Intercept.Kind == InterceptKind.FlyCatch)
            {
                // A catch on the fly retires the batter-runner (running until then); runners are not forced and hold
                // (tagging up: TASK-007/008).
                DefensiveAction hold = DefensiveDecision.Candidates(fielding, before, Array.Empty<Runner>(), timing, FielderProfile.For, null)
                    .First(a => a.Kind == DefensiveActionKind.HoldBall);
                var flyOut = new PlayEvent(fielding.PossessionTime, PlayEventKind.FlyOut, Runner.Batter, null, holder);
                var resolution = new PlayResolution(new[] { flyOut }, outsBefore, hold.Play.EndTime, false);
                return new RulesPlay(fielding, before, timing, new[] { Runner.Batter }, new[] { hold }, hold, "fly out", resolution);
            }

            if (fielding.Call != BallInPlayCall.Fair)
            {
                var dead = new PlayResolution(Array.Empty<PlayEvent>(), outsBefore, fielding.EndTime, true);
                return new RulesPlay(fielding, before, timing, Array.Empty<Runner>(), Array.Empty<DefensiveAction>(), null, "foul", dead);
            }

            var advancing = new List<Runner>(Forces.Forced(before));
            foreach (Runner r in before.Runners)
                if (!advancing.Contains(r) && runsOnContact != null && runsOnContact(r)) advancing.Add(r);

            IReadOnlyList<DefensiveAction> candidates = DefensiveDecision.Candidates(fielding, before, advancing, timing, FielderProfile.For,
                b => ThrowPlanner.Plan(fielding, b));
            (DefensiveAction chosen, string reason) = choose != null ? (choose(candidates), "override") : DefensiveDecision.Choose(candidates);

            var events = new List<PlayEvent>();
            if (chosen.Retires)
                events.Add(new PlayEvent(chosen.OutTime, chosen.IsForce ? PlayEventKind.ForceOut : PlayEventKind.TagOut, chosen.Runner.Value, chosen.Target, chosen.Actor));
            foreach (Runner r in advancing)
                if (!(chosen.Retires && chosen.Runner == r))
                    events.Add(new PlayEvent(fielding.Ball.First.Time + timing.TimeToNextBase(r), PlayEventKind.Safe, r, r.Next, null));
            return new RulesPlay(fielding, before, timing, advancing, candidates, chosen, reason,
                new PlayResolution(events, outsBefore, chosen.Play.EndTime, false));
        }
    }
}
