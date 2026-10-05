using System;
using System.Collections.Generic;
using System.Linq;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>The baseball situation a play starts from.</summary>
    public readonly struct Situation
    {
        public Situation(int outs, BaseOccupancy bases)
        {
            if (outs < 0 || outs > 2) throw new ArgumentOutOfRangeException(nameof(outs));
            Outs = outs;
            Bases = bases;
        }

        public int Outs { get; }
        public BaseOccupancy Bases { get; }
        public override string ToString() => $"{Bases}, {Outs} out";
    }

    public enum PlayLogKind
    {
        Fielded,
        Throw,
        Catch,
        BaseTouch,
        Out,
        Safe,
        Run,
        Decision,
        PlayOver,
    }

    /// <summary>One entry of a play's chronological log (debugging, UI, tests).</summary>
    public readonly struct PlayLogEntry
    {
        public PlayLogEntry(double time, PlayLogKind kind, Runner? runner, Base? at, DefensivePosition? fielder, string text)
        {
            Time = time;
            Kind = kind;
            Runner = runner;
            At = at;
            Fielder = fielder;
            Text = text;
        }

        public double Time { get; }
        public PlayLogKind Kind { get; }
        public Runner? Runner { get; }
        public Base? At { get; }
        public DefensivePosition? Fielder { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    /// <summary>
    /// A live play (TASK-007): the batted ball, the defense, and real runners, advanced as a deterministic discrete-event
    /// simulation on a fixed 1/120 s tick grid from contact. Continuous conditions — a runner touching a base, a force
    /// completed, a tag, a missed retouch — are detected per tick and located exactly by bisection on the motions, which are
    /// closed-form functions of time; decisions happen only at events. The result depends only on the inputs, never on
    /// how often or when <see cref="AdvanceTo"/> is called (render frame rate).
    /// TASK-007 scope: the defense is TASK-006B's single action, chosen at contact from the runners' predicted arrivals;
    /// outs are made by the gameplay state (possession + base touch, tag reach), not predicted.
    /// </summary>
    public sealed class LivePlay
    {
        public const double Tick = 1.0 / 120.0;
        /// <summary>A runner's lead at the moment of contact (m): 1B 12 ft (Statcast secondary lead 11.4–13.5 ft, REPORTED),
        /// 2B 20 ft (coaching: primary 15–18, secondary ~29, ASSUMED between), 3B 10 ft (ASSUMED).</summary>
        public static double Lead(Base b) => b switch { Base.First => 12.0 * 0.3048, Base.Second => 20.0 * 0.3048, Base.Third => 10.0 * 0.3048, _ => 0.0 };

        /// <summary>A fly caught sooner than this after contact is a line drive: runners freeze (s; ASSUMED).</summary>
        public const double LineDriveHang = 2.0;
        /// <summary>Halfway on a fly with fewer than two out (fraction of the leg; coaching "halfway", ASSUMED 40 %).</summary>
        public const double HalfwayFraction = 0.4;
        /// <summary>A defender's tag after the ball arrives (s; 0.1–0.2 s, DERIVED) — added to throw estimates for tag plays.</summary>
        public const double TagAllowance = 0.15;

        private readonly List<LiveRunner> _runners = new List<LiveRunner>();
        private readonly List<PlayLogEntry> _log = new List<PlayLogEntry>();
        private readonly List<PlayEvent> _rulesEvents = new List<PlayEvent>();
        private readonly List<(double Time, int Seq, Action Act, string Name, bool Settle)> _scheduled = new List<(double, int, Action, string, bool)>();
        private readonly List<double> _probes = new List<double>();
        private readonly Dictionary<Runner, double> _forcedTouch = new Dictionary<Runner, double>();
        private int _seq;
        private double _now;

        /// <param name="runsOnContact">Scripted runners who run on contact whatever their read (lab/test scenarios).</param>
        public LivePlay(FieldingPlay fielding, Situation situation, RunnerProfile? profile = null,
            Func<IReadOnlyList<DefensiveAction>, DefensiveAction> choose = null, Func<Runner, bool> runsOnContact = null)
        {
            Fielding = fielding;
            Situation = situation;
            Profile = profile ?? RunnerProfile.Standard;
            ContactTime = fielding.Ball.First.Time;
            _now = ContactTime;
            Kind = Classify(fielding);

            // Runners: on their bases at their leads; the batter-runner at home once the ball is fair in play.
            foreach (Runner r in situation.Bases.Runners)
                _runners.Add(new LiveRunner(r, Profile, r.From, BaseLeg.Of(r.From, false), Lead(r.From), ContactTime));
            if (Kind != BallKind.Dead)
                _runners.Insert(0, new LiveRunner(Runner.Batter, Profile, Base.Home, BaseLeg.Of(Base.Home, Kind == BallKind.Hit), 0.0, ContactTime));

            // First intentions (deterministic heuristics: RunnerBrain) and the defense's action, chosen with the runners'
            // predicted arrivals under those intentions.
            var intents = new Dictionary<Runner, Intent>();
            foreach (LiveRunner r in _runners)
                intents[r.Id] = Kind != BallKind.Dead && Kind != BallKind.Caught && !r.Id.IsBatter && runsOnContact != null && runsOnContact(r.Id)
                    ? new Intent(IntentKind.Go, r.Id.Next)
                    : RunnerBrain.Initial(this, r);
            var timing = new PredictedTiming(this, intents);
            if (Kind == BallKind.Dead) Rules = PlayResolver.Resolve(fielding, situation.Bases, situation.Outs, timing);
            else
                Rules = PlayResolver.Resolve(fielding, situation.Bases, situation.Outs, timing,
                    r => intents.TryGetValue(r, out Intent i) && i.Go, choose);
            Defense = Rules.Defense;

            foreach (LiveRunner r in _runners)
            {
                LiveRunner runner = r;
                Intent intent = intents[r.Id];
                double start = runner.Id.IsBatter ? ContactTime + Profile.BatterStartDelay : ContactTime + (intent.Immediate ? 0.0 : Profile.ReadDelay);
                Schedule(start, () => Apply(runner, intent, _now), $"{runner.Id} starts");
            }

            if (Fielding.Outcome == FieldingOutcome.Fielded)
            {
                Schedule(Fielding.PossessionTime, OnPossession, "possession");
                if (Defense.Throw is ThrowPlay th)
                {
                    Schedule(th.ReleaseTime, () => OnRelease(th), "release");
                    if (th.Caught) Schedule(th.Catch.Time, () => Note(th.Catch.Time, PlayLogKind.Catch, null, th.Target, th.Receiver, $"{Abbrev(th.Receiver)} catches at {Bases.Name(th.Target)}"), "catch");
                    else Schedule(th.FirstContactTime, OnMissedThrow, "missed throw");
                }
            }
        }

        public FieldingPlay Fielding { get; }
        public Situation Situation { get; }
        public RunnerProfile Profile { get; }
        public double ContactTime { get; }
        public BallKind Kind { get; }
        /// <summary>The defense's decision at contact (TASK-006B), its candidates and the chosen action.</summary>
        public RulesPlay Rules { get; }
        /// <summary>The defense's action: every defender's motion and the ball's authority at every instant.</summary>
        public DefensivePlay Defense { get; }
        public IReadOnlyList<LiveRunner> Runners => _runners;
        public IReadOnlyList<PlayLogEntry> Log => _log;
        /// <summary>The rules events so far (outs; force bookkeeping).</summary>
        public IReadOnlyList<PlayEvent> RulesEvents => _rulesEvents;
        /// <summary>Simulated up to here.</summary>
        public double Now => _now;
        /// <summary>When the play ended (+∞ while live).</summary>
        public double EndTime { get; private set; } = double.PositiveInfinity;
        public bool IsOver => !double.IsPositiveInfinity(EndTime);
        public int OutsMade => _rulesEvents.Count(e => e.IsOut);
        public int Outs => Situation.Outs + OutsMade;
        /// <summary>Runs that count (OBR 5.08(a)).</summary>
        public int Runs => _runners.Count(r => r.HasScored && r.RunCounts);

        public LiveRunner RunnerOf(Runner id) => _runners.FirstOrDefault(r => r.Id == id);

        /// <summary>The bases occupied when the play is over (the next plate appearance's situation).</summary>
        public BaseOccupancy ResultingBases()
        {
            bool first = false, second = false, third = false;
            if (Outs >= PlayResolution.OutsPerInning) return BaseOccupancy.Empty;
            foreach (LiveRunner r in _runners)
            {
                if (r.IsDone) continue;
                if (r.LastTouched == Base.First) first = true;
                else if (r.LastTouched == Base.Second) second = true;
                else if (r.LastTouched == Base.Third) third = true;
            }

            return new BaseOccupancy(first, second, third);
        }

        // ------------------------------------------------------------------ the clock

        /// <summary>Advances the play to <paramref name="time"/>: every event up to it, in order, on the fixed tick grid.</summary>
        public void AdvanceTo(double time)
        {
            int guard = 0;
            while (_now < time && guard++ < 1_000_000)
            {
                if (IsOver && _scheduled.Count == 0) return;   // nothing left to happen
                double tickEnd = ContactTime + (Math.Floor((_now - ContactTime) / Tick + 1e-9) + 1.0) * Tick;
                (double when, Action act) = NextOccurrence(_now, tickEnd);
                if (act != null)
                {
                    if (when > time) return;   // the next occurrence is after the requested time: motions up to it are final
                    _now = when;
                    act();
                    continue;
                }

                if (tickEnd > time) return;
                _now = tickEnd;
                CheckPlayOver(_now);
            }
        }

        /// <summary>Runs the play to its end (tests, instant resolution).</summary>
        public void RunToEnd(double limit = 120.0)
        {
            AdvanceTo(ContactTime + limit);
            if (!IsOver) EndPlay(_now, "time limit");
        }

        /// <param name="settle">Only finishes a motion after the action (the walk back after overrunning first): it neither
        /// keeps the play alive nor is cancelled by its end.</param>
        private void Schedule(double time, Action act, string name, bool settle = false) => _scheduled.Add((Math.Max(time, _now), _seq++, act, name, settle));

        /// <summary>The earliest scheduled or detected occurrence in (from, to].</summary>
        private (double, Action) NextOccurrence(double from, double to)
        {
            double best = double.PositiveInfinity;
            int bestScheduled = -1, bestSeq = int.MaxValue;
            for (int i = 0; i < _scheduled.Count; i++)
            {
                var s = _scheduled[i];
                if (s.Time <= to && (s.Time < best || (s.Time == best && s.Seq < bestSeq)))
                {
                    best = s.Time;
                    bestSeq = s.Seq;
                    bestScheduled = i;
                }
            }

            LiveRunner who = null;
            int what = 0;   // 1 foot on the bag ahead, 2 end of leg, 3 foot back on the bag behind, 4 back at the leg's start, 10+ an out
            PlayEventKind outKind = PlayEventKind.ForceOut;
            if (!IsOver)
            {
                foreach (LiveRunner r in _runners)
                {
                    if (r.IsDone) continue;
                    PathMotion m = r.Current.Motion;
                    double L = r.Current.Leg.Length, d0 = m.DistanceAt(from), d1 = m.DistanceAt(to);
                    // Rules touch a base when his foot reaches it (TouchDistance); the leg's geometry ends at the bag centre.
                    Crossing(L - LiveRunner.TouchDistance, true, 1);
                    Crossing(L, true, 2);
                    Crossing(LiveRunner.TouchDistance, false, 3);
                    Crossing(0.0, false, 4);

                    void Crossing(double at, bool forward, int kind)
                    {
                        if (forward ? !(d0 < at && d1 >= at) : !(d0 > at && d1 <= at)) return;
                        double t = m.Target == at && (forward ? m.Sign > 0.0 : m.Sign < 0.0) ? m.ArrivalTime : m.TimeAt(at);
                        if (t > from && t <= to && (t < best || t == best && kind < what && who == r))
                        {
                            best = t;
                            who = r;
                            what = kind;
                            bestScheduled = -1;
                        }
                    }
                }

                // Outs made by the gameplay state (possession, base touch, tag reach) — the first moment each holds: probed
                // across the window and at the instants a condition can begin or end (the defense taking the ball, a runner's
                // foot one simultaneous-window short of the bag), then located by bisection.
                if (Kind != BallKind.Dead)
                {
                    CollectProbes(from, to);
                    foreach (LiveRunner r in _runners)
                    {
                        if (r.IsDone) continue;
                        for (int c = 0; c < 3; c++)
                        {
                            double t = FirstHolding(r, c, from);
                            if (t < best)
                            {
                                best = t;
                                who = r;
                                what = 10 + c;
                                outKind = c == 0 ? PlayEventKind.ForceOut : c == 1 ? PlayEventKind.TagOut : PlayEventKind.RetouchOut;
                                bestScheduled = -1;
                            }
                        }
                    }
                }
            }

            if (bestScheduled >= 0)
            {
                int index = bestScheduled;
                return (best, () =>
                {
                    var item = _scheduled[index];
                    _scheduled.RemoveAt(index);
                    item.Act();
                });
            }

            if (who == null) return (best, null);
            LiveRunner runner = who;
            PlayEventKind kindOfOut = outKind;
            switch (what)
            {
                case 1: return (best, () => OnFootAhead(runner));
                case 2: return (best, () => OnReachLegEnd(runner));
                case 3: return (best, () => OnFootBack(runner));
                case 4: return (best, () => OnReturnToLegStart(runner));
                default: return (best, () => MakeOut(runner, kindOfOut, _now));
            }
        }

        private void CollectProbes(double from, double to)
        {
            _probes.Clear();
            for (int k = 0; k <= OutSamples; k++) _probes.Add(from + (to - from) * k / OutSamples);
            void Add(double t)
            {
                if (t > from && t < to) _probes.Add(t);
            }

            Add(Fielding.PossessionTime);
            if (Defense.Throw is ThrowPlay th)
            {
                Add(th.ReleaseTime);
                if (th.Caught) Add(th.Catch.Time);
            }

            if (Rules.Chosen != null)
            {
                Add(Rules.Chosen.CompletionTime);
                Add(Rules.Chosen.OutTime);
            }

            foreach (LiveRunner r in _runners)
            {
                if (r.IsDone) continue;
                PathMotion m = r.Current.Motion;
                double L = r.Current.Leg.Length;
                // The last instant before a defensive action would be "simultaneous" with his foot reaching a bag.
                double ahead = m.TimeAt(L - LiveRunner.TouchDistance), back = m.TimeAt(LiveRunner.TouchDistance);
                if (m.Sign > 0.0) Add(ahead - TimingCall.Simultaneous - 1e-7);
                else Add(back - TimingCall.Simultaneous - 1e-7);
            }

            _probes.Sort();
        }

        /// <summary>The first probe time (bisected back to the start of the interval) at which out condition
        /// <paramref name="c"/> holds for <paramref name="r"/>, or +∞.</summary>
        private double FirstHolding(LiveRunner r, int c, double from)
        {
            double previous = from;
            for (int k = 0; k < _probes.Count; k++)
            {
                double probe = _probes[k];
                if (OutCondition(r, c, probe))
                {
                    if (probe <= from) return from;
                    double lo = previous, hi = probe;
                    for (int i = 0; i < 50 && hi - lo > 1e-9; i++)
                    {
                        double mid = 0.5 * (lo + hi);
                        if (OutCondition(r, c, mid)) hi = mid;
                        else lo = mid;
                    }

                    return hi;
                }

                previous = probe;
            }

            return double.PositiveInfinity;
        }

        private const int OutSamples = 4;

        private static double First(Func<double, bool> holds, double from, double to)
        {
            double lo = from, hi = to;
            for (int i = 0; i < 50 && hi - lo > 1e-9; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (holds(mid)) hi = mid;
                else lo = mid;
            }

            return hi;
        }

        // ------------------------------------------------------------------ outs from gameplay state

        /// <summary>
        /// Out condition <paramref name="c"/> at <paramref name="t"/> — 0 force (still forced, the defender holding the ball
        /// on his base first), 1 tag (off a base and unprotected, within reach of the defender holding the ball), 2 retouch
        /// (a caught fly found him off his base; the defender holding the ball touches it before he does). A defensive action
        /// within the simultaneous window of his foot reaching the bag is safe.
        /// </summary>
        private bool OutCondition(LiveRunner r, int c, double t)
        {
            if (t < ContactTime) return false;
            DefensivePosition? holder = Defense.HolderAt(t);
            if (!(holder is DefensivePosition h)) return false;
            switch (c)
            {
                case 0:
                    return ForcedAt(r, t) && !TouchesWithin(r, t, TimingCall.Simultaneous) && BaseTouch.IsTouching(Defense.FielderPositionAt(h, t), r.Id.Next);
                case 1:
                    if (r.Phase == RunnerPhase.Scored || r.Phase == RunnerPhase.Out || r.OverrunProtected) return false;
                    if (r.TouchingBaseAt(t, out _) || TouchesWithin(r, t, TimingCall.Simultaneous)) return false;
                    return TagRules.CanTag(Defense.FielderPositionAt(h, t), r.PositionAt(t), true);
                default:
                    return r.MustRetouch && !TouchesWithin(r, t, TimingCall.Simultaneous) && BaseTouch.IsTouching(Defense.FielderPositionAt(h, t), r.LastTouched);
            }
        }

        /// <summary>He reaches the bag he is running to within <paramref name="window"/> of <paramref name="t"/> — a defensive
        /// action that close is simultaneous, and simultaneous is safe (TimingCall, Docs/RULES.md).</summary>
        private static bool TouchesWithin(LiveRunner r, double t, double window)
        {
            PathMotion m = r.Current.Motion;
            BaseLeg leg = r.Current.Leg;
            double now = m.DistanceAt(t), soon = m.DistanceAt(t + window);
            return now < leg.Length && soon >= leg.Length - LiveRunner.TouchDistance || now > 0.0 && soon <= LiveRunner.TouchDistance && m.Sign < 0.0;
        }

        /// <summary>Forced to his next base at <paramref name="time"/>: forced when the batter became a runner, not yet on it,
        /// and no following runner retired (OBR 5.09(b)(6)).</summary>
        public bool ForcedAt(LiveRunner r, double time)
        {
            if (Kind == BallKind.Dead || Kind == BallKind.Caught && !r.Id.IsBatter) return false;
            if (!Forces.IsForced(r.Id, Situation.Bases) || r.IsDone) return false;
            if (_forcedTouch.TryGetValue(r.Id, out double touched) && touched <= time) return false;
            if (r.TouchingBaseAt(time, out Base on) && on == r.Id.Next) return false;   // a foot on the bag he is forced to
            foreach (PlayEvent e in _rulesEvents)
                if (e.IsOut && e.Time <= time && Forces.Follows(e.Runner, r.Id)) return false;
            return time >= ContactTime;
        }

        private void MakeOut(LiveRunner r, PlayEventKind kind, double time)
        {
            if (r.IsDone) return;
            DefensivePosition? holder = Defense.HolderAt(time);
            Base at = kind == PlayEventKind.ForceOut ? r.Id.Next : kind == PlayEventKind.RetouchOut ? r.LastTouched : NearestBase(r, time);
            r.OutTime = time;
            r.Phase = RunnerPhase.Out;
            r.Add(r.Current.Leg, Hold(r, time));
            _rulesEvents.Add(new PlayEvent(time, kind, r.Id, kind == PlayEventKind.FlyOut ? (Base?)null : at, holder));
            string what = kind switch
            {
                PlayEventKind.FlyOut => "FLY OUT",
                PlayEventKind.ForceOut => $"OUT AT {Bases.Name(at).ToUpperInvariant()}",
                PlayEventKind.RetouchOut => $"DOUBLED OFF {Bases.Name(at).ToUpperInvariant()}",
                _ => $"OUT AT {Bases.Name(at).ToUpperInvariant()} (tag)",
            };
            Note(time, PlayLogKind.Out, r.Id, at, holder, $"{what} — {r.Id}");
            if (Outs >= PlayResolution.OutsPerInning)
            {
                // OBR 5.08(a) Exception: no run scores on a play whose third out is made by the batter-runner before he
                // touches first base (a fly out included), or by any runner being forced out. On a tag play or a retouch
                // ("not a force play", 5.08 Approved Ruling) runs that crossed before the out count.
                bool batterBeforeFirst = r.Id.IsBatter && r.LastTouched == Base.Home;
                if (kind == PlayEventKind.ForceOut || kind == PlayEventKind.FlyOut || batterBeforeFirst)
                    foreach (LiveRunner scorer in _runners.Where(x => x.HasScored && x.RunCounts))
                    {
                        scorer.RunCounts = false;
                        Note(time, PlayLogKind.Run, scorer.Id, Base.Home, null, $"run does not count (OBR 5.08(a)) — {scorer.Id}");
                    }

                EndPlay(time, "third out");
            }
        }

        private static Base NearestBase(LiveRunner r, double time)
        {
            BaseLeg leg = r.LegAt(time);
            return r.DistanceAlongAt(time) > 0.5 * leg.Length ? leg.To : leg.From;
        }

        // ------------------------------------------------------------------ runner events

        /// <summary>His foot reaches the bag ahead (the rules' touch): safe there, his force over, a run at home.</summary>
        private void OnFootAhead(LiveRunner r)
        {
            double t = _now;
            Base b = r.Current.Leg.To;
            if (r.Phase == RunnerPhase.Reading || r.IsDone) return;
            r.LastTouched = b;
            if (Forces.IsForced(r.Id, Situation.Bases) && b == r.Id.Next && !_forcedTouch.ContainsKey(r.Id)) _forcedTouch[r.Id] = t;
            Note(t, PlayLogKind.BaseTouch, r.Id, b, null, $"{r.Id} touches {Bases.Name(b)}");
            if (PlayOnBase(b, t)) Note(t, PlayLogKind.Safe, r.Id, b, null, $"SAFE AT {Bases.Name(b).ToUpperInvariant()} — {r.Id}");
            if (b != Base.Home) return;
            r.ScoreTime = t;
            r.Phase = RunnerPhase.Scored;
            Note(t, PlayLogKind.Run, r.Id, Base.Home, null, $"RUN SCORES — {r.Id}");
        }

        /// <summary>He reaches the bag's centre at the leg's end: on to the next leg, through first, or standing on it.</summary>
        private void OnReachLegEnd(LiveRunner r)
        {
            double t = _now;
            BaseLeg leg = r.Current.Leg;
            Base b = leg.To;
            PathMotion m = r.Current.Motion;
            if (r.Phase == RunnerPhase.Reading || r.IsDone) return;   // a scorer keeps braking past the plate
            if (r.Continue is Base next && next != b)
            {
                // Round the base onto the next leg at the speed he reached it with.
                bool rounding = BaseLeg.Bases(b) != r.Target;
                var nextLeg = BaseLeg.Of(b, rounding);
                Go(r, nextLeg, 0.0, m.EndSpeed, t, r.Target);
                return;
            }

            if (m.EndSpeed > 1e-6 && b == Base.First)
            {
                // Through first: overrun, then back to the bag (protected — OBR 5.09(b)(4) exception).
                r.Phase = RunnerPhase.Overrunning;
                r.OverrunProtected = true;
                LiveRunner runner = r;
                Schedule(m.RestTime, () => ReturnAfterOverrun(runner), "overrun over", true);
                return;
            }

            r.Phase = RunnerPhase.Standing;
            r.Target = b;
            r.Add(leg, new PathMotion(Profile, t, leg.Length, 0.0, leg.Length, 0.0));
        }

        private void ReturnAfterOverrun(LiveRunner r)
        {
            if (r.IsDone) return;
            BaseLeg leg = r.Current.Leg;
            r.Phase = RunnerPhase.Returning;
            // Back to the bag at a jog (ASSUMED 3 m/s), ending on it.
            r.Add(leg, new PathMotion(Profile, _now, r.Current.Motion.DistanceAt(_now), 0.0, leg.Length, 0.0, 3.0));
            r.Continue = null;
            r.Target = Base.First;
            LiveRunner runner = r;
            Schedule(r.Current.Motion.ArrivalTime, () =>
            {
                if (runner.IsDone) return;
                runner.Phase = RunnerPhase.Standing;
                runner.OverrunProtected = false;
            }, "back on first", true);
        }

        /// <summary>His foot is back on the bag behind him (a retouch, or back to his base).</summary>
        private void OnFootBack(LiveRunner r)
        {
            if (r.IsDone) return;
            BaseLeg leg = r.Current.Leg;
            r.LastTouched = leg.From;
            Note(_now, PlayLogKind.BaseTouch, r.Id, leg.From, null, $"{r.Id} back to {Bases.Name(leg.From)}");
            bool retouch = r.MustRetouch;
            r.MustRetouch = false;
            if (retouch && Kind == BallKind.Caught) Apply(r, RunnerBrain.AfterCatch(this, r, _now), _now);   // tag up from here?
        }

        /// <summary>Back on the bag's centre: standing there.</summary>
        private void OnReturnToLegStart(LiveRunner r)
        {
            if (r.IsDone || r.Current.Motion.Sign > 0.0) return;
            BaseLeg leg = r.Current.Leg;
            r.Target = leg.From;
            r.Phase = RunnerPhase.Standing;
            r.Add(leg, new PathMotion(Profile, _now, 0.0, 0.0, 0.0, 0.0));
        }

        /// <summary>Is the defense making a play at <paramref name="b"/> as the runner arrives (a throw on its way or a fielder
        /// carrying the ball there) — so his arrival is a "safe" call?</summary>
        private bool PlayOnBase(Base b, double t)
        {
            // Only an action on a runner (not a ball returned to the infield) makes a call.
            if (Rules.Chosen?.Runner == null || Rules.Chosen.Target != b) return false;
            if (Defense.Throw is ThrowPlay th) return t >= th.ReleaseTime - 0.5 && (!th.Caught || t < th.Catch.Time + 0.5);
            return Defense.Carry != null && t <= Defense.Carry.RestTime + 0.5;
        }

        // ------------------------------------------------------------------ ball events

        private void OnPossession()
        {
            DefensivePosition p = Fielding.Primary.Value;
            Note(_now, PlayLogKind.Fielded, null, null, p, $"{(Fielding.Intercept.Kind == InterceptKind.FlyCatch ? "CAUGHT" : "FIELDED")} by {Abbrev(p)}");
            if (Kind == BallKind.Caught)
            {
                LiveRunner batter = RunnerOf(Runner.Batter);
                if (batter != null) MakeOut(batter, PlayEventKind.FlyOut, _now);
                if (IsOver) return;
                foreach (LiveRunner r in _runners)
                    if (!r.IsDone) Apply(r, RunnerBrain.AfterCatch(this, r, _now), _now);
                return;
            }

            foreach (LiveRunner r in _runners)
                if (!r.IsDone) Apply(r, RunnerBrain.Reconsider(this, r, _now), _now);
        }

        private void OnRelease(ThrowPlay th)
        {
            Note(_now, PlayLogKind.Throw, null, th.Target, th.Thrower, $"THROW TO {Bases.Name(th.Target).ToUpperInvariant()} ({Abbrev(th.Thrower)} → {Abbrev(th.Receiver)})");
            foreach (LiveRunner r in _runners)
                if (!r.IsDone) Apply(r, RunnerBrain.Reconsider(this, r, _now), _now);
        }

        private void OnMissedThrow()
        {
            Note(_now, PlayLogKind.Throw, null, null, null, "throw not caught");
            foreach (LiveRunner r in _runners)
                if (!r.IsDone) Apply(r, RunnerBrain.Reconsider(this, r, _now), _now);
        }

        // ------------------------------------------------------------------ intentions → motion

        /// <summary>What a runner means to do.</summary>
        internal readonly struct Intent
        {
            public Intent(IntentKind kind, Base target, bool immediate = false)
            {
                Kind = kind;
                Target = target;
                Immediate = immediate;
            }

            public IntentKind Kind { get; }
            public Base Target { get; }
            public bool Immediate { get; }
            public bool Go => Kind == IntentKind.Go;
            public static Intent Keep => new Intent(IntentKind.Keep, Base.Home);
        }

        internal enum IntentKind { Keep, Go, Hold, Return, Halfway, TagUp, Freeze }

        private void Apply(LiveRunner r, Intent intent, double t)
        {
            if (r.IsDone || IsOver) return;
            PathMotion m = r.Current.Motion;
            BaseLeg leg = r.Current.Leg;
            double d = m.DistanceAt(t), v = m.VelocityAt(t);
            switch (intent.Kind)
            {
                case IntentKind.Keep:
                    return;
                case IntentKind.Go:
                    if (r.Phase == RunnerPhase.Overrunning) return;
                    if (r.MustRetouch) return;   // he goes back first
                    Note(t, PlayLogKind.Decision, r.Id, intent.Target, null, $"{r.Id}: go to {Bases.Name(intent.Target)}");
                    Go(r, leg, d, v, t, intent.Target);
                    return;
                case IntentKind.Hold:
                case IntentKind.Freeze:
                    r.Phase = r.TouchingBaseAt(t, out _) ? RunnerPhase.Standing : RunnerPhase.Reading;
                    r.Continue = null;
                    r.Add(leg, new PathMotion(Profile, t, d, v, d + (Math.Abs(v) > 1e-6 ? Math.Sign(v) * v * v / (2.0 * Profile.BrakeDeceleration) : 0.0), 0.0));
                    return;
                case IntentKind.Return:
                case IntentKind.TagUp:
                    if (Math.Abs(d) < 1e-9 && Math.Abs(v) < 1e-9)
                    {
                        r.Phase = intent.Kind == IntentKind.TagUp ? RunnerPhase.Reading : RunnerPhase.Standing;
                        if (r.MustRetouch) OnReturnToLegStart(r);
                        return;
                    }

                    r.Phase = intent.Kind == IntentKind.TagUp ? RunnerPhase.Reading : RunnerPhase.Returning;
                    r.Continue = null;
                    r.Target = leg.From;
                    Note(t, PlayLogKind.Decision, r.Id, leg.From, null, $"{r.Id}: back to {Bases.Name(leg.From)}");
                    r.Add(leg, new PathMotion(Profile, t, d, v, 0.0, 0.0));
                    return;
                case IntentKind.Halfway:
                    r.Phase = RunnerPhase.Reading;
                    r.Continue = null;
                    Note(t, PlayLogKind.Decision, r.Id, leg.To, null, $"{r.Id}: halfway");
                    r.Add(leg, new PathMotion(Profile, t, d, v, Math.Max(d, HalfwayFraction * leg.Length), 0.0));
                    return;
            }
        }

        /// <summary>Runs <paramref name="r"/> from (leg, d, v) to <paramref name="destination"/> (RunnerPlanner), scheduling the
        /// decision to go on before he must brake.</summary>
        private void Go(LiveRunner r, BaseLeg leg, double d, double v, double t, Base destination)
        {
            // Standing on the leg's end bag and going beyond it: start on the next leg.
            if (leg.To != destination && d >= leg.Length - 1e-9)
            {
                Base next = BaseLeg.Bases(leg.To);
                leg = BaseLeg.Of(leg.To, next != destination);
                d = 0.0;
            }

            r.Target = destination;
            r.Phase = RunnerPhase.Running;
            r.OverrunProtected = false;   // heading on: no longer returning from an overrun (OBR 5.09(b)(11))
            bool throughFirst = destination == Base.First && RunnerBrain.RunsThroughFirst(this, r);
            List<RunnerPlanner.PlannedLeg> plan = RunnerPlanner.Plan(Profile, leg, d, v, t, destination, throughFirst);
            PathMotion m = plan[0].Motion;
            r.Add(leg, m);
            r.Continue = leg.To != destination ? destination : (Base?)null;
            if (leg.To != destination || destination == Base.Home) return;

            // Decision: go on past the destination? Made when he would have to start braking for it (both plans are the
            // same motion until then).
            var rounding = new PathMotion(Profile, t, d, v, leg.Length, Profile.RoundingSpeed);
            double decide = Math.Min(m.BrakeTime, rounding.BrakeTime);
            LiveRunner runner = r;
            int legsAt = r.Segments.Count;
            Schedule(decide, () =>
            {
                if (runner.IsDone || runner.Segments.Count != legsAt || runner.Target != destination) return;   // replanned since
                if (RunnerBrain.GoOn(this, runner, destination, _now))
                {
                    Base next = BaseLeg.Bases(destination);
                    Note(_now, PlayLogKind.Decision, runner.Id, next, null, $"{runner.Id}: rounds {Bases.Name(destination)} for {Bases.Name(next)}");
                    Go(runner, runner.Current.Leg, runner.Current.Motion.DistanceAt(_now), runner.Current.Motion.VelocityAt(_now), _now, next);
                }
            }, $"{r.Id} decides at {Bases.Name(destination)}");
        }

        private PathMotion Hold(LiveRunner r, double t)
        {
            PathMotion m = r.Current.Motion;
            double d = m.DistanceAt(t), v = m.VelocityAt(t);
            return new PathMotion(Profile, t, d, v, d + (Math.Abs(v) > 1e-6 ? Math.Sign(v) * v * v / (2.0 * Profile.BrakeDeceleration) : 0.0), 0.0);
        }

        // ------------------------------------------------------------------ the end

        private void CheckPlayOver(double t)
        {
            if (IsOver) return;
            if (Kind == BallKind.Dead)
            {
                if (_runners.All(r => r.IsDone || r.Phase == RunnerPhase.Standing && r.SpeedAt(t) < 1e-6) && t >= Fielding.EndTime) EndPlay(t, "dead ball");
                return;
            }

            if (_scheduled.Any(s => !s.Settle)) return;
            if (Defense.AuthorityAt(t) != BallAuthority.Possessed || t < Defense.EndTime) return;
            foreach (LiveRunner r in _runners)
            {
                if (r.IsDone) continue;
                // The batter-runner past first (overrun) is entitled to it while he walks back (OBR 5.09(b)(4)).
                bool overrun = r.OverrunProtected;
                if (!overrun && (r.Phase != RunnerPhase.Standing || r.SpeedAt(t) > 1e-6 || r.MustRetouch)) return;
            }

            EndPlay(t, "ball held, runners on base");
        }

        private void EndPlay(double t, string why)
        {
            if (IsOver) return;
            EndTime = t;
            // Settling motions (a walk back to first, braking after the plate) continue to be shown; on a third out
            // everyone pulls up.
            bool third = Outs >= PlayResolution.OutsPerInning;
            _scheduled.RemoveAll(s => third || !s.Settle);
            foreach (LiveRunner r in _runners)
                if (third && !r.IsDone && r.SpeedAt(t) > 1e-6) r.Add(r.Current.Leg, Hold(r, t));
            Note(t, PlayLogKind.PlayOver, null, null, null, $"PLAY OVER ({why})");
        }

        private void Note(double t, PlayLogKind kind, Runner? runner, Base? at, DefensivePosition? fielder, string text) =>
            _log.Add(new PlayLogEntry(t, kind, runner, at, fielder, text));

        internal static string Abbrev(DefensivePosition p) => p switch
        {
            DefensivePosition.P => "P",
            DefensivePosition.C => "C",
            DefensivePosition.FirstBase => "1B",
            DefensivePosition.SecondBase => "2B",
            DefensivePosition.ThirdBase => "3B",
            DefensivePosition.Shortstop => "SS",
            DefensivePosition.LeftField => "LF",
            DefensivePosition.CenterField => "CF",
            _ => "RF",
        };

        // ------------------------------------------------------------------ classification

        public enum BallKind
        {
            /// <summary>Foul, or out of the park: no play (home runs: TASK-009).</summary>
            Dead,
            /// <summary>Caught on the fly.</summary>
            Caught,
            /// <summary>Fielded by an infielder (a ground ball, a chopper).</summary>
            Grounder,
            /// <summary>Fielded by an outfielder after it touched the ground: a hit.</summary>
            Hit,
        }

        private static BallKind Classify(FieldingPlay f)
        {
            if (f.Outcome != FieldingOutcome.Fielded) return BallKind.Dead;
            if (f.Intercept.Kind == InterceptKind.FlyCatch) return BallKind.Caught;
            if (f.Call != BallInPlayCall.Fair) return BallKind.Dead;
            return DefensiveDecision.IsOutfielder(f.Primary.Value) ? BallKind.Hit : BallKind.Grounder;
        }

        /// <summary>Hang time of a caught ball (s; +∞ if not caught).</summary>
        public double CatchHang => Kind == BallKind.Caught ? Fielding.PossessionTime - ContactTime : double.PositiveInfinity;

        // ------------------------------------------------------------------ estimates the runners use

        /// <summary>
        /// When the defense could have the ball at <paramref name="b"/> as seen at <paramref name="now"/>: from who has the
        /// ball (or where it will be fielded), the transfer and a throw at that fielder's routine speed covering the
        /// distance at 0.9 of it on average (TASK-006A throws: 0.72–0.96, DERIVED). An estimate — the runners' read — not the
        /// defense's actual plan.
        /// </summary>
        public double DefenseEta(Base b, double now)
        {
            Vector3d bag = FieldLayout.BasePosition(b);
            double Flight(Vector3d from, DefensivePosition thrower) =>
                new Vector3d(bag.X - from.X, bag.Y - from.Y, 0.0).Length / (0.9 * ThrowProfile.For(thrower).Speed);
            if (Fielding.Outcome != FieldingOutcome.Fielded) return double.PositiveInfinity;
            DefensivePosition primary = Fielding.Primary.Value;
            switch (Defense.AuthorityAt(now))
            {
                case BallAuthority.FreeBall when now < Fielding.PossessionTime:
                {
                    Vector3d at = Fielding.Motion(primary).PositionAt(Fielding.PossessionTime);
                    return Fielding.PossessionTime + ThrowProfile.For(primary).TransferTime + Flight(at, primary);
                }
                case BallAuthority.FreeBall:
                    return now + 3.0;   // a missed throw rolling free: nobody has it (ASSUMED)
                case BallAuthority.Thrown:
                {
                    ThrowPlay th = Defense.Throw;
                    if (th.Target == b) return th.Caught ? th.Catch.Time : th.FirstContactTime + 2.0;
                    Vector3d at = FieldLayout.BasePosition(th.Target);
                    return (th.Caught ? th.Catch.Time : th.FirstContactTime + 2.0) + ThrowProfile.For(th.Receiver).TransferTime + Flight(at, th.Receiver);
                }
                default:
                {
                    DefensivePosition holder = Defense.HolderAt(now).Value;
                    Vector3d at = Defense.FielderPositionAt(holder, now);
                    if (BaseTouch.IsTouching(at, b)) return now;
                    double ready = holder == primary && (Defense.Throw == null || now < Defense.Throw.ReleaseTime)
                        ? Fielding.PossessionTime + ThrowProfile.For(holder).TransferTime
                        : now + ThrowProfile.For(holder).TransferTime;
                    return Math.Max(now, ready) + Flight(at, holder);
                }
            }
        }

        /// <summary>When the runner would touch <paramref name="b"/> going there now (+∞ if behind him).</summary>
        public double RunnerEta(LiveRunner r, Base b, double now)
        {
            BaseLeg leg = r.Current.Leg;
            PathMotion m = r.Current.Motion;
            if (Progress(r, b) <= Progress(r, leg.From)) return double.PositiveInfinity;
            return RunnerPlanner.ArrivalTime(Profile, leg, m.DistanceAt(now), m.VelocityAt(now), now, b, b == Base.First && RunnerBrain.RunsThroughFirst(this, r));
        }

        /// <summary>Order of a base for this runner (home counts as 4 once he has left it).</summary>
        internal static int Progress(LiveRunner r, Base b) => b == Base.Home && !(r.Id.IsBatter && r.LastTouched == Base.Home && r.Current.Leg.From == Base.Home) ? 4 : (int)b;

        // ------------------------------------------------------------------ the defense's view of the runners (TASK-006B interface)

        /// <summary>The runners' predicted arrivals under their first intentions, for the defense's decision at contact
        /// (<see cref="IRunnerTiming"/>: the same running law as the motion).</summary>
        private sealed class PredictedTiming : IRunnerTiming
        {
            private readonly Dictionary<Runner, List<RunnerPlanner.PlannedLeg>> _plans = new Dictionary<Runner, List<RunnerPlanner.PlannedLeg>>();
            private readonly double _contact;

            public PredictedTiming(LivePlay play, Dictionary<Runner, Intent> intents)
            {
                _contact = play.ContactTime;
                foreach (LiveRunner r in play._runners)
                {
                    if (!intents[r.Id].Go) continue;
                    double start = r.Id.IsBatter ? _contact + play.Profile.BatterStartDelay : _contact + (intents[r.Id].Immediate ? 0.0 : play.Profile.ReadDelay);
                    Base next = r.Id.Next;
                    bool through = next == Base.First && RunnerBrain.RunsThroughFirst(play, r);
                    _plans[r.Id] = RunnerPlanner.Plan(play.Profile, r.Current.Leg, r.Current.Motion.D0, 0.0, start, next, through);
                }
            }

            public double TimeToNextBase(Runner runner) =>
                _plans.TryGetValue(runner, out var plan) ? RunnerPlanner.TouchTime(plan) - _contact : double.PositiveInfinity;

            public Vector3d PositionAt(Runner runner, double sinceContact)
            {
                if (!_plans.TryGetValue(runner, out var plan)) return FieldLayout.BasePosition(runner.From);
                double t = _contact + sinceContact;
                foreach (var leg in plan)
                    if (t <= leg.Motion.ArrivalTime || ReferenceEquals(leg.Motion, plan[plan.Count - 1].Motion))
                        return leg.Leg.PositionAt(Math.Min(leg.Motion.DistanceAt(t), leg.Leg.Length));
                return plan[plan.Count - 1].Leg.End;
            }
        }
    }
}
