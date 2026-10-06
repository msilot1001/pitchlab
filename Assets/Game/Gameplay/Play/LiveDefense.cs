using System;
using System.Collections.Generic;
using System.Linq;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Play
{
    public enum LiveActionKind
    {
        Hold,
        /// <summary>Throw to the defender covering a base, or to the cut-off / relay man.</summary>
        Throw,
        /// <summary>Carry the ball to a base himself.</summary>
        Carry,
    }

    /// <summary>One thing the defender with the ball can do, evaluated on the live state (TASK-008).</summary>
    public sealed class LiveAction
    {
        internal LiveAction(LiveActionKind kind, Base? target, DefensivePosition actor, double completion, Runner? runner, double runnerArrival,
            bool force, LiveThrow throwPlan, ContinuationMotion carry, string note)
        {
            Kind = kind;
            Target = target;
            Actor = actor;
            Completion = completion;
            Runner = runner;
            RunnerArrival = runnerArrival;
            IsForce = force;
            Throw = throwPlan;
            Carry = carry;
            Note = note;
        }

        public LiveActionKind Kind { get; }
        /// <summary>The base (null: a throw to the cut-off or relay man).</summary>
        public Base? Target { get; }
        /// <summary>Who has the ball at the end of it.</summary>
        public DefensivePosition Actor { get; }
        /// <summary>When the actor has the ball on the base (+∞: cannot).</summary>
        public double Completion { get; }
        public Runner? Runner { get; }
        /// <summary>When that runner's foot reaches the base (+∞ without a runner).</summary>
        public double RunnerArrival { get; }
        /// <summary>A force (or a retouch): touching the base is enough; otherwise the runner must be tagged.</summary>
        public bool IsForce { get; }
        public LiveThrow Throw { get; }
        public ContinuationMotion Carry { get; }
        public string Note { get; }
        public bool Feasible => !double.IsPositiveInfinity(Completion);
        /// <summary>Arrival − the out moment (a tag adds the tag allowance) — > 0: the defense first.</summary>
        public double Margin => RunnerArrival - (Completion + (IsForce ? 0.0 : LivePlay.TagAllowance));
        public bool Retires => Runner != null && TimingCall.DefenseFirst(Completion + (IsForce ? 0.0 : LivePlay.TagAllowance), RunnerArrival);

        public override string ToString()
        {
            string where = Target is Base b ? Bases.Name(b) : "cut-off";
            string what = Kind switch { LiveActionKind.Hold => "Hold", LiveActionKind.Carry => $"Carry to {where}", _ => $"Throw {where}" };
            return Note == null ? what : $"{what} ({Note})";
        }
    }

    /// <summary>A defender's decision with the ball: what he could do and what he did, and why.</summary>
    public sealed class HolderDecision
    {
        internal HolderDecision(double time, DefensivePosition holder, IReadOnlyList<LiveAction> candidates, LiveAction chosen, string reason)
        {
            Time = time;
            Holder = holder;
            Candidates = candidates;
            Chosen = chosen;
            Reason = reason;
        }

        public double Time { get; }
        public DefensivePosition Holder { get; }
        public IReadOnlyList<LiveAction> Candidates { get; }
        public LiveAction Chosen { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// The live defense (TASK-008): all nine defenders' tracks, the ball's segments (batted ball free → held → thrown → held
    /// … or loose after a missed throw until a retriever fields it), the takes and throws, the roles from
    /// <see cref="DefensiveCoordinator"/> (reassigned at every event) and the decisions of each defender with the ball. Owned
    /// and advanced by <see cref="LivePlay"/>; exact functions of time for presentation and the rules.
    /// </summary>
    public sealed class LiveDefense : IDefenseTimeline
    {
        /// <summary>A loose ball is reacted to this long after it first touches the ground (s; ASSUMED).</summary>
        public const double LooseReaction = 0.2;

        private enum SegmentKind { Free, Held, Thrown }

        private sealed class Segment
        {
            public SegmentKind Kind;
            public double Start;
            public BallInPlay Free;
            public DefensivePosition Holder;
            public Vector3d TakeOffset;
            public LiveThrow Throw;
            /// <summary>The throw that ends this hold (null: he keeps it).</summary>
            public LiveThrow Next;
            /// <summary>He has acted on this hold (a decision was carried out).</summary>
            public bool Acted;
            /// <summary>An out was just made with this ball: the next throw is the turn of a double play, at full effort.</summary>
            public bool Turn;
        }

        private readonly LivePlay _play;
        /// <summary>Where the defenders stood at contact (the alignment the fielding play was solved with).</summary>
        private readonly DefensiveAlignment _alignment;
        private readonly FielderTrack[] _tracks = new FielderTrack[DefensiveAlignment.Count];
        private readonly double[] _onPoint = new double[DefensiveAlignment.Count];
        private readonly Dictionary<(DefensivePosition, DefensivePosition, double, double, double, double, double, int, int), LiveThrow> _plans =
            new Dictionary<(DefensivePosition, DefensivePosition, double, double, double, double, double, int, int), LiveThrow>();
        private readonly List<Segment> _ball = new List<Segment>();
        private readonly List<BallTake> _takes = new List<BallTake>();
        private readonly List<LiveThrow> _throws = new List<LiveThrow>();
        private readonly List<(double Time, RoleAssignment[] Roles)> _roles = new List<(double, RoleAssignment[])>();
        private readonly List<HolderDecision> _decisions = new List<HolderDecision>();
        private readonly List<(double Start, double End, Base Base, DefensivePosition Carrier)> _carries = new List<(double, double, Base, DefensivePosition)>();
        private Func<IReadOnlyList<LiveAction>, LiveAction> _firstChoice;
        private DefensivePosition? _retriever;

        internal LiveDefense(LivePlay play, Func<IReadOnlyList<LiveAction>, LiveAction> firstChoice)
        {
            _play = play;
            _firstChoice = firstChoice;
            FieldingPlay f = play.Fielding;
            double t0 = play.ContactTime;
            for (int i = 0; i < _tracks.Length; i++)
            {
                var p = (DefensivePosition)i;
                FielderProfile profile = _play.Personnel.Fielder(p);
                IFieldMotion first = f.Primary == p ? f.Motion(p) : (IFieldMotion)new ContinuationMotion(profile, f.Motion(p).Start, Vector3d.Zero, t0);
                _tracks[i] = new FielderTrack(profile, first, t0);
                _onPoint[i] = double.PositiveInfinity;
            }

            _ball.Add(new Segment { Kind = SegmentKind.Free, Start = t0, Free = f.Ball });
            var start = new Vector3d[DefensiveAlignment.Count];
            for (int i = 0; i < start.Length; i++) start[i] = f.Motion((DefensivePosition)i).Start;
            _alignment = new DefensiveAlignment(start);
        }

        public IReadOnlyList<BallTake> Takes => _takes;
        public IReadOnlyList<LiveThrow> Throws => _throws;
        public IReadOnlyList<HolderDecision> Decisions => _decisions;

        /// <summary>The tag outs of the play: when, by whom (the defender with the ball), and where the runner was (for the
        /// tag motion; the out itself is the rules' decision).</summary>
        public IReadOnlyList<(double Time, DefensivePosition Fielder, Vector3d Runner)> Tags
        {
            get
            {
                if (_tagsFrom == _play.RulesEvents.Count) return _tags;
                _tags.Clear();
                foreach (PlayEvent e in _play.RulesEvents)
                    if (e.Kind == PlayEventKind.TagOut && e.Fielder is DefensivePosition f)
                        _tags.Add((e.Time, f, _play.RunnerOf(e.Runner).PositionAt(e.Time)));
                _tagsFrom = _play.RulesEvents.Count;
                return _tags;
            }
        }

        private readonly List<(double, DefensivePosition, Vector3d)> _tags = new List<(double, DefensivePosition, Vector3d)>();
        private int _tagsFrom = -1;
        public FielderTrack Track(DefensivePosition p) => _tracks[(int)p];
        public double FirstMoveTime(DefensivePosition p) => _tracks[(int)p].FirstMoveTime;

        /// <summary>The roles in force at <paramref name="time"/> (null before the first assignment).</summary>
        public RoleAssignment[] RolesAt(double time)
        {
            RoleAssignment[] roles = null;
            foreach (var r in _roles)
                if (r.Time <= time) roles = r.Roles;
            return roles;
        }

        // ------------------------------------------------------------------ timeline

        public double StartTime => _play.ContactTime;
        public Vector3d FielderPositionAt(DefensivePosition p, double t) => _tracks[(int)p].PositionAt(t);
        public Vector3d FielderVelocityAt(DefensivePosition p, double t) => _tracks[(int)p].VelocityAt(t);
        public double FielderSpeedAt(DefensivePosition p, double t) => _tracks[(int)p].SpeedAt(t);
        public double FielderDistanceAt(DefensivePosition p, double t) => _tracks[(int)p].DistanceAt(t);
        public Vector3d FielderDirectionAt(DefensivePosition p, double t) => _tracks[(int)p].DirectionAt(t);

        private Segment SegmentAt(double t)
        {
            for (int i = _ball.Count - 1; i > 0; i--)
                if (t >= _ball[i].Start) return _ball[i];
            return _ball[0];
        }

        public BallAuthority AuthorityAt(double t) => SegmentAt(t).Kind switch
        {
            SegmentKind.Held => BallAuthority.Possessed,
            SegmentKind.Thrown => BallAuthority.Thrown,
            _ => BallAuthority.FreeBall,
        };

        public DefensivePosition? HolderAt(double t)
        {
            Segment s = SegmentAt(t);
            return s.Kind == SegmentKind.Held ? s.Holder : (DefensivePosition?)null;
        }

        public Vector3d BallPositionAt(double t)
        {
            Segment s = SegmentAt(t);
            switch (s.Kind)
            {
                case SegmentKind.Free: return s.Free.StateAt(t).Position;
                case SegmentKind.Thrown: return s.Throw.Flight.StateAt(t).Position;
            }

            // Held: from the take into the chest over the secure time; then, before a throw, to the release point over the arm
            // action (the hand follows it).
            double u = Math.Min(1.0, (t - s.Start) / FieldingPlay.SecureTime);
            u = u * u * (3.0 - 2.0 * u);
            Vector3d holder = _tracks[(int)s.Holder].PositionAt(t);
            Vector3d held = holder + (1.0 - u) * s.TakeOffset + u * FieldingPlay.HoldOffset;
            if (s.Next == null) return held;
            double action = Math.Min(DefensivePlay.ArmAction, s.Next.ReleaseTime - s.Start);
            double a = Math.Max(0.0, Math.Min(1.0, 1.0 - (s.Next.ReleaseTime - t) / action));
            a = a * a * (3.0 - 2.0 * a);
            Vector3d release = holder + (s.Next.ReleasePoint - _tracks[(int)s.Holder].PositionAt(s.Next.ReleaseTime));
            return (1.0 - a) * held + a * release;
        }

        public DefensivePosition? FocusAt(double t)
        {
            Segment s = SegmentAt(t);
            switch (s.Kind)
            {
                case SegmentKind.Held: return s.Holder;
                case SegmentKind.Thrown: return s.Throw.Receiver;
            }

            return _ball.Count == 1 ? _play.Fielding.Primary : _retriever;
        }

        /// <summary>The ball is held and nobody has anything left to do with it (no throw coming, no carry running).</summary>
        internal bool IdleAt(double t)
        {
            Segment s = SegmentAt(t);
            if (s.Kind != SegmentKind.Held || s.Next != null) return false;
            foreach (var c in _carries)
                if (c.Carrier == s.Holder && t < c.End) return false;
            return true;
        }

        /// <summary>A defensive play is being made at <paramref name="b"/> around <paramref name="t"/> (a throw there in the air or
        /// just caught, or a carry to it) — a runner reaching it then is a "safe" call.</summary>
        internal bool PlayOn(Base b, double t)
        {
            foreach (LiveThrow th in _throws)
                if (th.Target == b && t >= th.ReleaseTime - 0.5 && t <= th.EndTime + 0.5) return true;
            foreach (var c in _carries)
                if (c.Base == b && t >= c.Start && t <= c.End + 0.5) return true;
            return false;
        }

        /// <summary>Instants at which an out condition may start to hold (takes, releases, catches, carries).</summary>
        internal IEnumerable<double> KeyTimes()
        {
            foreach (BallTake k in _takes) yield return k.Time;
            if (_play.Fielding.Outcome == FieldingOutcome.Fielded) yield return _play.Fielding.PossessionTime;
            foreach (LiveThrow th in _throws)
            {
                yield return th.ReleaseTime;
                yield return th.EndTime;
            }

            foreach (var c in _carries)
            {
                yield return c.Start;
                yield return c.End;
            }
        }

        // ------------------------------------------------------------------ events

        /// <summary>At contact: roles for the batted ball, and the possession scheduled.</summary>
        internal void Start()
        {
            FieldingPlay f = _play.Fielding;
            if (f.Outcome != FieldingOutcome.Fielded) return;   // a dead ball: everyone holds
            DefensivePosition primary = f.Primary.Value;
            Reassign(_play.ContactTime, primary, f.Intercept.Ball.Position, null);
            _play.ScheduleDefense(f.PossessionTime, () => Possess(primary, f.PossessionTime, f.Intercept.Ball.Position, true, f.Action), "possession");
        }

        /// <summary><paramref name="p"/> takes the ball at <paramref name="t"/>.</summary>
        private void Possess(DefensivePosition p, double t, Vector3d ballPoint, bool batted, FieldingAction action)
        {
            FielderTrack track = _tracks[(int)p];
            Vector3d at = track.PositionAt(t);
            _ball.Add(new Segment { Kind = SegmentKind.Held, Start = t, Holder = p, TakeOffset = ballPoint - at });
            _takes.Add(new BallTake(t, p, ballPoint, action));
            // He keeps moving as his momentum carries him (no dead stop when he takes it).
            track.Add(t, new ContinuationMotion(track.Profile, at, track.VelocityAt(t), t));
            _play.OnDefenseTook(p, t, batted);
            if (_play.IsOver) return;
            Decide(p, t, t);
        }

        /// <summary>
        /// The holder's decision. At the take he steps on a bag he is next to, or carries the ball to one, at once; a throw is
        /// committed only when his arm action must start (the transfer's end), with the runners as they are then — an
        /// outfielder sees the runner rounding third before he picks his target.
        /// </summary>
        private void Decide(DefensivePosition h, double took, double t)
        {
            if (_play.IsOver || HolderAt(t) != h) return;
            // Roles around the holder, then his options with the current roles, then roles for the play he makes.
            Reassign(t, h, _tracks[(int)h].PositionAt(t), null);
            List<LiveAction> candidates = Candidates(h, took, t);
            LiveAction chosen;
            string reason;
            if (_firstChoice != null)
            {
                chosen = _firstChoice(candidates) ?? candidates[0];
                reason = "override";
                _firstChoice = null;
            }
            else (chosen, reason) = Choose(h, candidates);

            double commit = took + _play.Personnel.Throw(h).TransferTime + Recovery(h, took) - DefensivePlay.ArmAction;
            if (chosen.Kind == LiveActionKind.Throw && t < commit - 1e-9 && _firstChoice == null && reason != "override")
            {
                // Not yet: he gathers himself and decides when he must start the throw.
                _play.ScheduleDefense(commit, () => Decide(h, took, commit), "commit");
                return;
            }

            _decisions.Add(new HolderDecision(t, h, candidates, chosen, reason));
            _play.NoteDecision(t, h, $"{LivePlay.Abbrev(h)}: {chosen} — {reason}");
            SegmentAt(t).Acted = true;
            Execute(h, t, chosen);
        }

        /// <summary>An out was made: the defender with the ball (if he is not already throwing or carrying it somewhere) looks
        /// for the next play — the pivot of a double play.</summary>
        internal void OnOut(double t)
        {
            DefensivePosition? h = HolderAt(t);
            if (!(h is DefensivePosition holder) || !IdleAt(t)) return;
            Segment s = SegmentAt(t);
            if (!s.Acted) return;   // his decision on this take is still to come (and will see the out)
            s.Turn = true;
            _play.ScheduleDefense(t, () => Decide(holder, s.Start, t), "after out");
        }

        private void Execute(DefensivePosition h, double t, LiveAction a)
        {
            switch (a.Kind)
            {
                case LiveActionKind.Throw:
                {
                    LiveThrow th = a.Throw;
                    _throws.Add(th);
                    _ball[_ball.Count - 1].Next = th;
                    _ball.Add(new Segment { Kind = SegmentKind.Thrown, Start = th.ReleaseTime, Throw = th });
                    if (th.ReceiverMotion != null) _tracks[(int)th.Receiver].Add(th.SwitchTime, th.ReceiverMotion);
                    Reassign(t, h, _tracks[(int)h].PositionAt(t), th);
                    _play.ScheduleDefense(th.ReleaseTime, () => _play.OnThrowReleased(th), "release");
                    if (th.Caught) _play.ScheduleDefense(th.Catch.Time, () => Possess(th.Receiver, th.Catch.Time, th.Catch.Ball.Position, false, FieldingAction.ReceiveThrow), "catch");
                    else _play.ScheduleDefense(th.FirstContactTime, () => Loose(th), "loose");
                    return;
                }

                case LiveActionKind.Carry:
                    _tracks[(int)h].Add(t, a.Carry);
                    _carries.Add((t, a.Completion, a.Target.Value, h));
                    return;
            }
        }

        /// <summary>A missed throw is loose: whoever can field it first (from where and as he is moving) goes for it.</summary>
        private void Loose(LiveThrow th)
        {
            double t = th.FirstContactTime;
            _ball.Add(new Segment { Kind = SegmentKind.Free, Start = t, Free = th.Flight });
            _play.OnLooseBall(t);
            var takes = new Intercept[DefensiveAlignment.Count];
            for (int i = 0; i < takes.Length; i++)
            {
                FielderTrack track = _tracks[i];
                FielderProfile p = track.Profile;
                var react = new FielderProfile(t - th.Flight.First.Time + LooseReaction, p.MaxSpeed, p.AccelerationTime, p.BrakeDeceleration, p.Reach, p.GroundReach, p.CatchHeightMax, p.PickupHeightMax);
                double start = t + LooseReaction;
                takes[i] = InterceptSolver.Solve(th.Flight, track.PositionAt(start), react, _play.Field, double.PositiveInfinity, initialVelocity: track.VelocityAt(start));
            }

            int best = FieldingSolver.SelectPrimary(takes);
            if (best < 0) return;   // nobody can get to it (it left the park)
            var who = (DefensivePosition)best;
            Intercept take = takes[best];
            FielderTrack tr = _tracks[best];
            double go = t + LooseReaction;
            if (take.RouteDistance > 0.0)
                tr.Add(Math.Max(go, tr.CurrentStart), new ContinuationMotion(tr.Profile, tr.PositionAt(go), tr.VelocityAt(go), go, take.FielderTarget, take.Time));
            _retriever = who;
            Reassign(t, who, take.Ball.Position, null);
            FieldingAction action = FieldingActions.Classify(th.Flight, take, tr.PositionAt(take.Time), tr.VelocityAt(take.Time), _play.Field);
            _play.ScheduleDefense(take.Time, () => Possess(who, take.Time, take.Ball.Position, false, action), "retrieve");
        }

        // ------------------------------------------------------------------ roles

        private void Reassign(double t, DefensivePosition ballPlayer, Vector3d ballPoint, LiveThrow throwing)
        {
            // The batted ball decides infield or outfield coverage for the whole play (the relay man with the ball is still an
            // outfield play).
            FieldingPlay f = _play.Fielding;
            bool infield = !DefensiveDecision.IsOutfielder(f.Primary.Value) && !DefensiveDecision.IsOutfielder(ballPlayer);
            var situation = new DefensiveCoordinator.Situation(ballPlayer, ballPoint, f.Intercept.Ball.Position, infield, _play.Kind == LivePlay.BallKind.Caught,
                throwing?.Target ?? LikelyThrow(t, !infield), throwing?.Receiver, throwing?.Target);
            RoleAssignment[] roles = DefensiveCoordinator.Assign(situation, _alignment);
            RoleAssignment[] previous = _roles.Count > 0 ? _roles[_roles.Count - 1].Roles : null;
            _roles.Add((t, roles));
            for (int i = 0; i < roles.Length; i++)
            {
                var p = (DefensivePosition)i;
                RoleAssignment r = roles[i];
                if (p == ballPlayer || HolderAt(t) == p) continue;
                if (throwing != null && p == throwing.Receiver) continue;   // he is taking the throw
                if (_ball.Count > 0 && _ball[_ball.Count - 1].Kind == SegmentKind.Free && p == _retriever) continue;
                RoleAssignment? was = previous?[i];
                bool same = was is RoleAssignment w && w.Role == r.Role && w.At == r.At && (w.Point - r.Point).Length < 0.75;
                if (same) continue;
                FielderTrack track = _tracks[i];
                double start = Math.Max(Math.Max(t, _play.ContactTime + track.Profile.ReactionTime), track.CurrentStart);
                Vector3d at = track.PositionAt(start), v = track.VelocityAt(start);
                if (r.Role == DefensiveRole.Hold)
                {
                    if (v.Length > 1e-6) track.Add(start, new ContinuationMotion(track.Profile, at, v, start));
                    _onPoint[i] = double.PositiveInfinity;
                    continue;
                }

                ContinuationMotion go = ContinuationMotion.ToRest(track.Profile, at, v, start, r.Point);
                if (go == null) continue;
                track.Add(start, go);
                _onPoint[i] = go.RestTime;
            }
        }

        /// <summary>The base the throw most likely goes to: the base the lead runner is going for ("throw one base ahead of
        /// the lead runner"); second when the batter-runner is the lead runner on a ball to the outfield.</summary>
        private Base LikelyThrow(double t, bool outfieldBall)
        {
            LiveRunner lead = null;
            foreach (LiveRunner r in _play.Runners)
            {
                if (r.IsDone || r.Target == r.LastTouched) continue;
                if (lead == null || Index(r.Target) > Index(lead.Target)) lead = r;
            }

            if (lead != null)
                return outfieldBall && lead.Id.IsBatter && lead.Target == Base.First ? Base.Second : lead.Target;
            // At contact nobody has started yet: the lead forced runner's base (the batter-runner's first on a fair ball);
            // second on a caught fly.
            if (_play.Kind == LivePlay.BallKind.Caught || _play.Kind == LivePlay.BallKind.Dead) return Base.Second;
            IReadOnlyList<Runner> forced = Forces.Forced(_play.Situation.Bases);
            Runner leadOnBase = Runner.Batter;
            foreach (Runner r in _play.Situation.Bases.Runners) leadOnBase = r;
            if (outfieldBall) return leadOnBase.IsBatter ? Base.Second : leadOnBase.Next;
            return forced[forced.Count - 1].Next;
        }

        private static int Index(Base b) => b == Base.Home ? 4 : (int)b;

        private DefensivePosition? CovererOf(Base b, double t)
        {
            RoleAssignment[] roles = RolesAt(t);
            if (roles == null) return null;
            foreach (RoleAssignment r in roles)
                if (r.Role == DefensiveRole.CoverBase && r.At == b) return r.Position;
            return null;
        }

        private DefensivePosition? CutoffMan(double t, out Vector3d point)
        {
            point = default;
            RoleAssignment[] roles = RolesAt(t);
            if (roles == null) return null;
            // The relay man first (on a relayed ball the first baseman is also a cut-off, for home, in line from the relay).
            foreach (DefensiveRole role in new[] { DefensiveRole.Relay, DefensiveRole.Cutoff })
                foreach (RoleAssignment r in roles)
                    if (r.Role == role)
                    {
                        point = r.Point;
                        return r.Position;
                    }

            return null;
        }

        // ------------------------------------------------------------------ the holder's options

        private List<LiveAction> Candidates(DefensivePosition h, double took, double t)
        {
            var list = new List<LiveAction> { new LiveAction(LiveActionKind.Hold, null, h, t, null, double.PositiveInfinity, false, null, null, null) };
            FielderTrack holder = _tracks[(int)h];
            // Full effort on the turn of a double play and from the outfield (Statcast's arm strength is measured on those
            // competitive throws); an infielder's routine throw otherwise.
            ThrowProfile arm = SegmentAt(t).Turn || DefensiveDecision.IsOutfielder(h) ? _play.Personnel.FullThrow(h) : _play.Personnel.Throw(h);
            double ready = Math.Max(t, took + arm.TransferTime + Recovery(h, took));
            var planned = new Dictionary<Base, LiveThrow>();

            foreach (LiveRunner r in _play.Runners)
            {
                if (r.IsDone) continue;
                Base b;
                double arrival;
                bool force;
                if (r.MustRetouch)
                {
                    b = r.LastTouched;
                    PathMotion m = r.Current.Motion;
                    arrival = m.Sign < 0.0 ? m.TimeAt(LiveRunner.TouchDistance) : double.PositiveInfinity;
                    force = true;   // touching the base is enough (an appeal)
                }
                else if (r.Target != r.LastTouched && (r.Phase == RunnerPhase.Running))
                {
                    b = r.Target;
                    arrival = _play.RunnerEta(r, b, t);
                    force = _play.ForcedAt(r, t) && b == r.Id.Next;
                }
                else continue;

                // Throw to whoever covers that base (never to an empty base).
                DefensivePosition? cover = CovererOf(b, t);
                if (cover is DefensivePosition c && c != h)
                {
                    // Hopeless without planning it: even a ball keeping its release speed on a straight line to a glove 2 m
                    // short of the bag is later than the runner by more than the close-play window (no out, no close play).
                    Vector3d at = holder.PositionAt(ready), bag = FieldLayout.BasePosition(b);
                    double soonest = ready + Math.Max(0.0, new Vector3d(bag.X - at.X, bag.Y - at.Y, 0.0).Length - 2.0) / arm.Speed;
                    if (arrival - soonest < -DefensiveDecision.CloseWindow)
                        list.Add(new LiveAction(LiveActionKind.Throw, b, c, double.PositiveInfinity, r.Id, arrival, force, null, null, "hopeless"));
                    else
                    {
                        if (!planned.TryGetValue(b, out LiveThrow th))
                            planned[b] = th = Plan(h, arm, ready, c, _onPoint[(int)c], bag, b);
                        // A force (or retouch) is complete only with the receiver on the bag; a tag play when he has the ball (the
                        // tag allowance is added). A catch off the bag for a force does not complete it (he decides again).
                        double done = !th.Caught ? double.PositiveInfinity
                            : !force || BaseTouch.IsTouching(_tracks[(int)c].PositionAt(th.Catch.Time), b) ? th.Catch.Time
                            : double.PositiveInfinity;
                        list.Add(new LiveAction(LiveActionKind.Throw, b, c, done, r.Id, arrival, force, th, null, th.Caught ? done < double.PositiveInfinity ? null : "caught off the bag" : "not caught"));
                    }
                }

                // Carry it there himself.
                (ContinuationMotion carry, double touch) = CarryTo(h, t, b, force);
                if (carry != null || touch == t)
                    list.Add(new LiveAction(LiveActionKind.Carry, b, h, touch, r.Id, arrival, force, null, carry ?? new ContinuationMotion(holder.Profile, holder.PositionAt(t), holder.VelocityAt(t), t), null));
            }

            // Outfielders: the throw to the cut-off or relay man (the ball back to the infield; he decides again).
            if (DefensiveDecision.IsOutfielder(h) && CutoffMan(t, out Vector3d spot) is DefensivePosition cut && cut != h)
            {
                // To where he will be when the ball gets there (he keeps going to his spot; no waiting for him: he adjusts).
                FielderTrack cutTrack = _tracks[(int)cut];
                Vector3d from = holder.PositionAt(ready), there = cutTrack.PositionAt(ready);
                for (int i = 0; i < 2; i++)
                    there = cutTrack.PositionAt(ready + new Vector3d(there.X - from.X, there.Y - from.Y, 0.0).Length / arm.Speed);
                LiveThrow th = Plan(h, arm, ready, cut, double.NegativeInfinity, there, null);
                list.Add(new LiveAction(LiveActionKind.Throw, null, cut, th.Caught ? th.Catch.Time : double.PositiveInfinity, null, double.PositiveInfinity, false, th, null, "cut-off"));
            }

            return list;
        }

        /// <summary>Extra time before <paramref name="h"/> can throw after his take at <paramref name="took"/>: getting up from a
        /// diving catch (TASK-011.6); none otherwise.</summary>
        private double Recovery(DefensivePosition h, double took)
        {
            foreach (BallTake k in _takes)
                if (k.Fielder == h && k.Time == took) return k.Action == FieldingAction.DivingCatch ? FieldingActions.DiveRecovery : 0.0;
            return 0.0;
        }

        /// <summary>A planned throw, reused while nothing it depends on has changed (the holder's options are evaluated at the
        /// take and again at the commit; the throws are mostly the same).</summary>
        private LiveThrow Plan(DefensivePosition h, ThrowProfile arm, double ready, DefensivePosition receiver, double onPoint, Vector3d point, Base? target)
        {
            FielderTrack from = _tracks[(int)h], to = _tracks[(int)receiver];
            var key = (h, receiver, point.X, point.Y, ready, arm.Speed, onPoint, from.Version, to.Version);
            if (_plans.TryGetValue(key, out LiveThrow th)) return th;
            th = ThrowPlanner.PlanLive(h, from, ready, arm, receiver, to, onPoint, point, target, _play.Environment, _play.Field);
            _plans[key] = th;
            return th;
        }

        /// <summary>Carrying the ball to <paramref name="b"/>: for a force the earliest touch (running across the bag), for a tag
        /// a stop on it.</summary>
        private (ContinuationMotion, double) CarryTo(DefensivePosition h, double t, Base b, bool force)
        {
            FielderTrack track = _tracks[(int)h];
            Vector3d at = track.PositionAt(t), v = track.VelocityAt(t), bag = FieldLayout.BasePosition(b);
            if (BaseTouch.IsTouching(at, b)) return (null, t);
            if (!force)
            {
                ContinuationMotion rest = ContinuationMotion.ToRest(track.Profile, at, v, t, bag);
                if (rest == null) return (null, double.PositiveInfinity);
                double first = rest.RestTime;
                for (double s = t; s <= rest.RestTime; s += 0.005)
                    if (BaseTouch.IsTouching(rest.PositionAt(s), b))
                    {
                        first = s;
                        break;
                    }

                return (rest, first);
            }

            double e = ContinuationMotion.EarliestWithin(track.Profile, at, v, bag, BaseTouch.Radius);
            if (double.IsPositiveInfinity(e)) return (null, double.PositiveInfinity);
            Vector3d carried = ContinuationMotion.Carried(track.Profile, at, v, e), off = carried - bag;
            double d = new Vector3d(off.X, off.Y, 0.0).Length, rr = BaseTouch.Radius * (1.0 - 1e-6);
            Vector3d aim = d > rr ? bag + new Vector3d(off.X, off.Y, 0.0) * (rr / d) : carried;
            return (new ContinuationMotion(track.Profile, at, v, t, aim, t + e), t + e);
        }

        /// <summary>
        /// The choice (deterministic priorities, as TASK-006B): an immediate out; the earliest force or retouch out; the earliest
        /// tag out; a close play the runner wins by less than the close window; an outfielder returns the ball through the
        /// cut-off (or to second); anyone else holds. An outfielder's long direct throw goes through only when it gets the out.
        /// </summary>
        private (LiveAction, string) Choose(DefensivePosition h, List<LiveAction> cands)
        {
            LiveAction hold = cands[0];
            double t = hold.Completion;
            LiveAction immediate = cands.Where(a => a.Kind == LiveActionKind.Carry && a.IsForce && a.Retires && a.Completion <= t + 1e-9).OrderBy(a => a.Completion).FirstOrDefault();
            if (immediate != null) return (immediate, "immediate out");
            // Of the force outs he can make, the lead runner (then the earliest).
            LiveAction force = cands.Where(a => a.IsForce && a.Retires).OrderByDescending(a => Index(a.Target.Value)).ThenBy(a => a.Completion)
                .ThenBy(a => a.Kind == LiveActionKind.Carry ? 0 : 1).FirstOrDefault();
            if (force != null) return (force, "force out on the lead runner");
            LiveAction tag = cands.Where(a => !a.IsForce && a.Retires).OrderBy(a => a.Completion).FirstOrDefault();
            if (tag != null) return (tag, "tag out");
            LiveAction close = cands.Where(a => a.Runner != null && a.Feasible && a.Margin > -DefensiveDecision.CloseWindow).OrderByDescending(a => a.Margin).FirstOrDefault();
            if (close != null) return (close, "close play");
            if (DefensiveDecision.IsOutfielder(h))
            {
                LiveAction cut = cands.FirstOrDefault(a => a.Kind == LiveActionKind.Throw && a.Target == null && a.Feasible);
                if (cut != null) return (cut, "ball to the cut-off");
            }

            return (hold, "no play");
        }

        /// <summary>When the defense could have the ball at <paramref name="b"/> as seen at <paramref name="now"/> — the runners'
        /// estimate: who has it (or will field it), the transfer, a throw at that fielder's routine speed at 0.9 of it.</summary>
        internal double Eta(Base b, double now)
        {
            Vector3d bag = FieldLayout.BasePosition(b);
            double Flight(Vector3d from, DefensivePosition thrower) =>
                new Vector3d(bag.X - from.X, bag.Y - from.Y, 0.0).Length / (0.9 * _play.Personnel.Throw(thrower).Speed);
            FieldingPlay f = _play.Fielding;
            if (f.Outcome != FieldingOutcome.Fielded) return double.PositiveInfinity;
            Segment s = SegmentAt(now);
            switch (s.Kind)
            {
                case SegmentKind.Free when _ball.Count == 1:
                {
                    DefensivePosition primary = f.Primary.Value;
                    Vector3d at = f.Motion(primary).PositionAt(f.PossessionTime);
                    return f.PossessionTime + _play.Personnel.Throw(primary).TransferTime + (f.Action == FieldingAction.DivingCatch ? FieldingActions.DiveRecovery : 0.0) + Flight(at, primary);
                }
                case SegmentKind.Free:
                    return now + 3.0;   // a loose ball (ASSUMED)
                case SegmentKind.Thrown:
                {
                    LiveThrow th = s.Throw;
                    double end = th.Caught ? th.Catch.Time : th.FirstContactTime + 2.0;
                    if (th.Target == b) return end;
                    return end + _play.Personnel.Throw(th.Receiver).TransferTime + Flight(th.AimPoint, th.Receiver);
                }
                default:
                {
                    Vector3d at = _tracks[(int)s.Holder].PositionAt(now);
                    if (BaseTouch.IsTouching(at, b)) return now;
                    double ready = s.Next != null ? s.Next.ReleaseTime : Math.Max(now, s.Start + _play.Personnel.Throw(s.Holder).TransferTime + Recovery(s.Holder, s.Start));
                    return Math.Max(now, ready) + Flight(at, s.Holder);
                }
            }
        }
    }
}
