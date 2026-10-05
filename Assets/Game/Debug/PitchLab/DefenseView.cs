using System;
using System.Collections.Generic;
using System.Linq;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Core;
using UnityEngine;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Shows the nine defenders of a live defense (<see cref="LiveDefense"/>, TASK-008) with the shared
    /// <see cref="PlayerMannequin"/>. Gameplay places every figure (position, speed, distance run come from the defenders'
    /// tracks at the play's time); this view only poses them (<see cref="FieldingPoser"/>), turns them, moves the glove to
    /// every authoritative take (a fielded ball, a caught throw, a retrieved loose ball), holds the ball, and throws it
    /// from the hand at every authoritative release. Optional debug: every defender's role and the decisions with the ball.
    /// Visual only — no colliders, nothing fed back.
    /// </summary>
    public sealed class DefenseView : MonoBehaviour
    {
        /// <summary>How long before a take the glove starts toward the ball (s).</summary>
        public const double GloveLead = 0.45;
        /// <summary>The catcher appears once he is this far from his spot behind the plate (m).</summary>
        private const double CatcherShowDistance = 1.5;
        /// <summary>The follow-through after a release (s).</summary>
        public const double FollowThrough = 0.75;

        private readonly PlayerMannequin[] _figures = new PlayerMannequin[DefensiveAlignment.Count];
        private readonly MannequinPose _pose = new MannequinPose();
        private readonly MannequinPose _from = new MannequinPose();
        private PlayerMannequin _prefab;
        private PlayerMannequin _adoptedPitcher;
        private MannequinPose _pitcherShown;
        private Action<double, MannequinPose> _pitcherPoseAt;
        private DefensiveAlignment _alignment = DefensiveAlignment.Standard;

        /// <summary>Where the defenders stand before a play (the situation's alignment: double-play depth…).</summary>
        public DefensiveAlignment Alignment
        {
            get => _alignment;
            set => _alignment = value ?? DefensiveAlignment.Standard;
        }
        private LiveDefense _defense;
        private double _time;
        private bool _debug;
        private double _pitcherTakeover = double.NaN;
        private Vector3 _takeoverRoot;
        private Quaternion _takeoverRotation;
        private GUIStyle _style;

        /// <summary>Hide the catcher until he leaves his crouch (e.g. while the batting camera looks over his shoulder).</summary>
        public bool CatcherVisible { get; set; } = true;

        public PlayerMannequin Figure(DefensivePosition position) => _figures[(int)position];

        /// <summary>
        /// Builds the defenders. <paramref name="pitcher"/>: an existing figure (the sandbox pitcher) that this view drives only
        /// once the pitcher moves in a play (blending from his pose at the takeover, <paramref name="pitcherPoseAt"/>, and
        /// writing what is shown back to <paramref name="pitcherShown"/>); null builds its own.
        /// </summary>
        public void Build(PlayerMannequin prefab, PlayerMannequin pitcher, MannequinPose pitcherShown, Action<double, MannequinPose> pitcherPoseAt,
            DefensiveAlignment alignment)
        {
            _prefab = prefab;
            _adoptedPitcher = pitcher;
            _pitcherShown = pitcherShown;
            _pitcherPoseAt = pitcherPoseAt;
            _alignment = alignment ?? DefensiveAlignment.Standard;
            foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
            {
                if (p == DefensivePosition.P && pitcher != null)
                {
                    _figures[(int)p] = pitcher;
                    continue;
                }

                PlayerMannequin m = Instantiate(_prefab, transform);
                m.name = $"Fielder {p}";
                m.Build();
                Equipment.AttachGlove(m.GloveAnchor);
                _figures[(int)p] = m;
            }

            Show(null, 0.0, null, false);
        }

        /// <summary>True when this view drives the adopted pitcher at <paramref name="time"/> (the sandbox must not): from
        /// shortly before he first moves in the play.</summary>
        public bool DrivesPitcher(LiveDefense defense, double time) =>
            _adoptedPitcher != null && defense != null && time >= TakeoverTime(defense);

        private static double TakeoverTime(LiveDefense defense) => defense.FirstMoveTime(DefensivePosition.P) - TakeoverBlend;

        private const double TakeoverBlend = 0.25;

        /// <summary>After a play the fielders jog back to the alignment in this long (real s; presentation only — the next play
        /// starts them from the alignment).</summary>
        public const double ReturnTime = 1.4;
        private readonly Vector3d[] _returnFrom = new Vector3d[DefensiveAlignment.Count];
        /// <summary>The real-time clock the jog back runs on (the lab's clock: tests freeze it).</summary>
        public Func<double> Clock { get; set; } = () => Time.realtimeSinceStartupAsDouble;
        private double _returnStart = double.NegativeInfinity;
        /// <summary>The adopted pitcher moved in the last play: this view walks him back to the rubber before handing him back.</summary>
        private bool _pitcherReturning;
        /// <summary>Who had the ball when the play ended: he keeps it in his glove as he jogs back.</summary>
        private DefensivePosition? _returnHolder;

        /// <summary>A new pitch: the delivery has the pitcher (whatever is left of his jog back) and the ball — the fielder who
        /// ended the last play with it no longer holds it.</summary>
        public void EndPitcherReturn()
        {
            _pitcherReturning = false;
            _returnHolder = null;
        }
        private bool Returning => _defense == null && Clock() - _returnStart < ReturnTime;

        /// <summary>Poses every defender for <paramref name="defense"/> at play time <paramref name="time"/> (null: ready at the
        /// alignment).</summary>
        public void Show(LiveDefense defense, double time, Transform ball, bool debug)
        {
            if (defense == null && _defense != null)
            {
                for (int i = 0; i < _returnFrom.Length; i++) _returnFrom[i] = _defense.FielderPositionAt((DefensivePosition)i, _time);
                _returnStart = Clock();
                _pitcherReturning = DrivesPitcher(_defense, _time);
                _returnHolder = _defense.HolderAt(double.MaxValue);   // whoever ends up with it
                _returnKeys[0] = new object();   // the jog back: a new motion track for every figure
            }

            if (!ReferenceEquals(defense, _defense)) _pitcherTakeover = double.NaN;

            _defense = defense;
            _time = time;
            _debug = debug;
            for (int i = 0; i < DefensiveAlignment.Count; i++)
            {
                var p = (DefensivePosition)i;
                PlayerMannequin figure = _figures[i];
                if (figure == null) continue;
                if (p == DefensivePosition.P && figure == _adoptedPitcher && !DrivesPitcher(defense, time) && !(_pitcherReturning && Returning)) continue;
                if (p == DefensivePosition.C)
                {
                    // Shown when he plays the ball or leaves the plate area (covering home is standing there: not shown).
                    bool involved = false;
                    if (defense != null)
                    {
                        Vector3d from = _alignment[p], at = defense.FielderPositionAt(p, time);
                        involved = new Vector3d(at.X - from.X, at.Y - from.Y, 0.0).Length > CatcherShowDistance;
                        foreach (BallTake k in defense.Takes)
                            if (k.Fielder == p && k.Time <= time + GloveLead) involved = true;
                    }

                    bool shown = CatcherVisible || involved;
                    if (figure.gameObject.activeSelf != shown) figure.gameObject.SetActive(shown);
                }

                Pose(p, figure, defense, time, ball);
            }

            // Between plays the ball stays in the glove of whoever had it.
            if (defense == null && ball != null && _returnHolder is DefensivePosition h && _figures[(int)h] != null && _figures[(int)h].gameObject.activeSelf)
                ball.position = _figures[(int)h].GloveAnchor.position;
        }

        /// <summary>The figure the camera should keep with the ball (the holder, the receiver of a throw in the air, whoever
        /// plays the ball), else null.</summary>
        public Transform Focus => _defense?.FocusAt(_time) is DefensivePosition p ? _figures[(int)p].transform : null;

        /// <summary>A defender's part in the play at a moment: the take he is reaching for or has just made, the throw he is
        /// making, whether he holds the ball — all from the authoritative defense.</summary>
        private readonly struct Context
        {
            public Context(LiveDefense d, DefensivePosition position, double time)
            {
                Take = null;
                Throw = null;
                // Index loops (no enumerator boxing: this runs for every fielder every frame and every motion-grid step).
                IReadOnlyList<BallTake> takes = d.Takes;
                for (int i = 0; i < takes.Count; i++)
                    if (takes[i].Fielder == position && takes[i].Time - GloveLead <= time && (Take == null || takes[i].Time > Take.Value.Time)) Take = takes[i];
                IReadOnlyList<LiveThrow> throws = d.Throws;
                for (int i = 0; i < throws.Count; i++)
                {
                    LiveThrow x = throws[i];
                    if (x.Thrower == position && (Take == null || x.ReleaseTime >= Take.Value.Time) && time <= x.ReleaseTime + FollowThrough + StrideRecovery)
                    {
                        Throw = x;
                        break;
                    }
                }

                SinceTake = Take == null ? double.NegativeInfinity : time - Take.Value.Time;
                Holding = d.HolderAt(time) == position;
                Release = Throw?.ReleaseTime ?? double.PositiveInfinity;
            }

            public BallTake? Take { get; }
            public LiveThrow Throw { get; }
            public double SinceTake { get; }
            public bool Holding { get; }
            public double Release { get; }
            public double ArmStart => Release - DefensivePlay.ArmAction;
            /// <summary>When the throwing motion starts (never before the ball is secured).</summary>
            public double WindStart => Take == null ? ArmStart - 0.15 : Math.Max(ArmStart - 0.15, Take.Value.Time + FieldingPlay.SecureTime);
        }

        /// <summary>A sliding catch keeps him down this long after the catch (s) before he gets up.</summary>
        private const double SlideRecovery = 0.35;
        /// <summary>A throw released while running faster than this (m/s) is a throw on the run (no planted stride).</summary>
        private const double MovingThrowSpeed = 2.5;
        /// <summary>The outfielder's crow hop before the arm action (s).</summary>
        private const double CrowHopTime = 0.35;
        /// <summary>The tag: the glove goes down this long before the authoritative tag and comes back over this long (s).</summary>
        private const double TagLead = 0.25, TagHold = 0.35;

        private static ReadyStyle ReadyOf(DefensivePosition p) => p switch
        {
            DefensivePosition.FirstBase or DefensivePosition.ThirdBase => ReadyStyle.CornerInfield,
            DefensivePosition.LeftField or DefensivePosition.CenterField or DefensivePosition.RightField => ReadyStyle.Outfield,
            DefensivePosition.P => ReadyStyle.Pitcher,
            DefensivePosition.C => ReadyStyle.Catcher,
            _ => ReadyStyle.Infield,
        };

        /// <summary>The body action for a take (the gameplay classification decides; this only maps it to a body shape). A
        /// throw caught by a defender standing on a base is a stretch.</summary>
        private static BodyAction BodyOf(FieldingAction action, LiveDefense d, DefensivePosition position, BallTake take) => action switch
        {
            FieldingAction.CenteredPickup => BodyAction.Pickup,
            FieldingAction.ForehandPickup => BodyAction.Forehand,
            FieldingAction.BackhandPickup => BodyAction.Backhand,
            FieldingAction.ShortHopPickup => BodyAction.ShortHop,
            FieldingAction.ChargingPickup => BodyAction.Charge,
            FieldingAction.SlidingCatch => BodyAction.Slide,
            FieldingAction.DivingCatch => BodyAction.Dive,
            FieldingAction.JumpingCatch => BodyAction.Jump,
            FieldingAction.ReceiveThrow when OnABase(d.FielderPositionAt(position, take.Time)) => BodyAction.Stretch,
            FieldingAction.None => BodyAction.None,
            _ => BodyAction.Reach,
        };

        private static bool OnABase(Vector3d at)
        {
            foreach (Simulation.Field.Base b in Bags)
                if (Gameplay.Rules.BaseTouch.IsTouching(at, b)) return true;
            return false;
        }

        /// <summary>The planted throwing stride comes back (the fielder recovers) over this long after the follow-through (s).</summary>
        private const double StrideRecovery = 0.3;
        /// <summary>Turn-rate limits (°/s): running and reading the ball, and the quick turn to throw.</summary>
        private const float TurnRate = 540f, ThrowTurnRate = 900f;

        private readonly MotionTrack[] _motion = Enumerable.Range(0, DefensiveAlignment.Count).Select(_ => new MotionTrack()).ToArray();
        private readonly float[] _shownYaw = new float[DefensiveAlignment.Count];
        private readonly BodyAction[] _shownAction = new BodyAction[DefensiveAlignment.Count];
        private readonly FieldingAction[] _gameplayAction = new FieldingAction[DefensiveAlignment.Count];

        /// <summary>Motion debug: the body action shown for <paramref name="p"/> last frame, and the gameplay classification it
        /// came from.</summary>
        public BodyAction ShownAction(DefensivePosition p) => _shownAction[(int)p];
        public FieldingAction GameplayAction(DefensivePosition p) => _gameplayAction[(int)p];
        private readonly object[] _returnKeys = new object[1];

        /// <summary>Where a defender wants to face at <paramref name="time"/>, from authoritative state only: the target while
        /// throwing, the ball before a take, the infield while holding it, the ball otherwise — turned into the run direction
        /// as he speeds up (a fielder sprinting for a ball over his head runs facing his route, not backward).</summary>
        private static MotionSample Desired(LiveDefense d, DefensivePosition position, double time)
        {
            var c = new Context(d, position, time);
            Vector3 root = Flat(SimulationSpace.ToUnity(d.FielderPositionAt(position, time)));
            Vector3 ballAt = SimulationSpace.ToUnity(d.BallPositionAt(time));
            Vector3 face;
            bool throwing = c.Throw != null && time >= c.WindStart && time <= c.Release + FollowThrough;
            if (throwing) face = SimulationSpace.ToUnity(c.Throw.AimPoint) - root;
            else if (c.Take != null && c.SinceTake < 0.0) face = ballAt - root;
            else if (c.Holding) face = -root;
            else face = ballAt - root;
            face.y = 0f;
            Vector3 velocity = Flat(SimulationSpace.ToUnity(d.FielderVelocityAt(position, time)));
            float speed = velocity.magnitude;
            float w = throwing ? 0f : Mathf.SmoothStep(0f, 1f, (speed - 1.5f) / 3f);
            Vector3 look = face.sqrMagnitude > 1e-4f && speed > 1e-3f ? Vector3.Slerp(face.normalized, velocity / speed, w) : speed > 1e-3f ? velocity / speed : face;
            // Down on the ground after a dive or a sliding catch he cannot turn until he is up again.
            if (c.Take is BallTake k && c.SinceTake >= 0.0 && (k.Action == FieldingAction.DivingCatch || k.Action == FieldingAction.SlidingCatch)
                && c.SinceTake < (k.Action == FieldingAction.DivingCatch ? FieldingActions.DiveRecovery : SlideRecovery) + FieldingPoser.GetUp)
                return new MotionSample { Velocity = velocity, Facing = look, MaxTurnRate = 0f };
            return new MotionSample { Velocity = velocity, Facing = look, MaxTurnRate = throwing ? ThrowTurnRate : TurnRate };
        }

        private static Func<double, MotionSample> Sampler(LiveDefense d, DefensivePosition position) => t => Desired(d, position, t);

        /// <summary>The jog back: along the way, turning toward home over its last 40 % so he is facing it when he arrives.</summary>
        private static Func<double, MotionSample> ReturnSampler(double start, Vector3d from, Vector3d to) => t =>
        {
            double v = (t - start) / ReturnTime;
            Vector3 travel = Flat(SimulationSpace.ToUnity(to - from)) * (float)(6.0 * v * (1.0 - v) / ReturnTime);   // d/dt smoothstep
            Vector3 facing = v < 0.6 && travel.sqrMagnitude > 0.01f ? travel : -SimulationSpace.ToUnity(to);
            return new MotionSample { Velocity = v < 1.0 ? travel : Vector3.zero, Facing = facing, MaxTurnRate = TurnRate };
        };

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        /// <summary>The figure's ready heading at its spot: toward home plate (the pitcher's too).</summary>
        private float ReadyYaw(DefensivePosition position) => YawOf(-SimulationSpace.ToUnity(_alignment[position]));

        private void Pose(DefensivePosition position, PlayerMannequin figure, LiveDefense d, double time, Transform ball)
        {
            int i = (int)position;
            Vector3d at = d?.FielderPositionAt(position, time) ?? _alignment[position];
            float speed = d == null ? 0f : (float)d.FielderSpeedAt(position, time);
            float yaw, phase = 0f, accel = 0f, lateral = 0f;
            Vector3 velocity = Vector3.zero;
            if (d != null)
            {
                MotionTrack track = _motion[i];
                // A new play starts every figure's track from the heading it was shown with (the delivery's pitcher from his own).
                // A new play starts every figure facing home from its spot (deterministic: not whatever the last play left), the
                // delivery's pitcher from his own heading.
                if (!track.Follows(d))
                {
                    float initial = position == DefensivePosition.P && figure == _adoptedPitcher ? figure.transform.eulerAngles.y : ReadyYaw(position);
                    track.Follow(d, d.StartTime, initial, Sampler(d, position));
                }
                (phase, yaw) = track.At(time);
                velocity = Flat(SimulationSpace.ToUnity(d.FielderVelocityAt(position, time)));
                accel = (float)((d.FielderSpeedAt(position, time + 0.05) - d.FielderSpeedAt(position, Math.Max(d.StartTime, time - 0.05))) / 0.1);
                lateral = speed * track.TurnRate(time) * Mathf.Deg2Rad;
            }
            else
            {
                // Between plays: jogging back from where the last play left him, then ready at his spot facing home.
                yaw = ReadyYaw(position);
                double u = (Clock() - _returnStart) / ReturnTime;
                if (u < 1.0)
                {
                    Vector3d from = _returnFrom[i], to = at;
                    // The adopted pitcher goes back to where the delivery has him (the rubber), which then takes over.
                    if (position == DefensivePosition.P && figure == _adoptedPitcher) to = SimulationSpace.ToSimulation(_takeoverRoot);
                    at = from + Mathf.SmoothStep(0f, 1f, (float)u) * (to - from);
                    MotionTrack track = _motion[i];
                    object key = _returnKeys[0];
                    if (!track.Follows(key)) track.Follow(key, _returnStart, _shownYaw[i], ReturnSampler(_returnStart, from, to));
                    double now = Clock();
                    (phase, yaw) = track.At(now);
                    velocity = Flat(SimulationSpace.ToUnity(to - from)) * (float)(6.0 * u * (1.0 - u) / ReturnTime);
                    speed = velocity.magnitude;
                }
            }

            _shownYaw[i] = yaw;
            Vector3 root = SimulationSpace.ToUnity(new Vector3d(at.X, at.Y, 0.0)) + BagSide(at, speed);
            root.y = FieldDressing.MoundHeight(root.x, root.z);   // stand on the mound, not in it (presentation only)
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 local = Quaternion.Inverse(rotation) * velocity;
            var input = new FieldingPoseInput
            {
                Speed = speed,
                GaitPhase = phase,
                MoveDir = speed > 0.05f ? new Vector2(local.x, local.z) : Vector2.zero,
                Accel = accel,
                LateralAccel = lateral,
            };

            var c = d != null ? new Context(d, position, time) : default;
            BallTake? take = c.Take;
            LiveThrow th = c.Throw;
            double sinceTake = c.SinceTake;
            bool holding = d != null && c.Holding;
            double release = c.Release, armStart = c.ArmStart;
            bool adopted = position == DefensivePosition.P && figure == _adoptedPitcher;
            if (adopted && d != null && double.IsNaN(_pitcherTakeover))
            {
                // Takeover starts from where the sandbox pitcher stood (read before this frame moves him).
                _takeoverRoot = figure.transform.position;
                _takeoverRotation = figure.transform.rotation;
            }

            figure.transform.SetPositionAndRotation(root, rotation);
            // The catcher's crouch is his stance behind the plate before the pitch; in a play he is up like an infielder.
            input.Ready = position == DefensivePosition.C && d != null ? ReadyStyle.Infield : ReadyOf(position);
            input.ActionTime = float.NaN;
            if (take != null)
            {
                // The body around the take, from the gameplay classification (a slide or dive stays down for its recovery).
                input.Action = BodyOf(take.Value.Action, d, position, take.Value);
                input.ActionTime = (float)sinceTake;
                // The take point in the figure's frame at the take (frozen: after it he keeps moving, braking — the action's
                // direction and reach must not swing as he passes the spot).
                Vector3d takeAt = d.FielderPositionAt(position, take.Value.Time);
                Vector3 takeRoot = SimulationSpace.ToUnity(new Vector3d(takeAt.X, takeAt.Y, 0.0)) + BagSide(takeAt, (float)d.FielderSpeedAt(position, take.Value.Time));   // the shown root
                takeRoot.y = FieldDressing.MoundHeight(takeRoot.x, takeRoot.z);
                float takeYaw = _motion[i].Follows(d) ? _motion[i].At(take.Value.Time).Yaw : yaw;
                input.ActionPoint = Quaternion.Inverse(Quaternion.Euler(0f, takeYaw, 0f)) * (SimulationSpace.ToUnity(take.Value.BallPoint) - takeRoot);
                input.Recovery = take.Value.Action == FieldingAction.DivingCatch ? (float)FieldingActions.DiveRecovery : take.Value.Action == FieldingAction.SlidingCatch ? (float)SlideRecovery : 0f;
            }

            if (d != null)
            {
                var tags = d.Tags;
                for (int k = 0; k < tags.Count; k++)
                {
                    var tag = tags[k];
                    if (tag.Fielder != position || !holding) continue;
                    // The tag: the glove with the ball sweeps down to where the runner is at the authoritative tag — only once
                    // he has the ball (a tag right after a catch starts at the catch, the sweep taking at least 0.1 s).
                    double from = Math.Max(tag.Time - TagLead, take?.Time ?? double.NegativeInfinity);
                    double peak = Math.Max(tag.Time, from + 0.1);
                    if (time > from && time < peak + TagHold)
                    {
                        double u = time < peak ? (time - from) / (peak - from) : 1.0 - (time - peak) / TagHold;
                        input.TagWeight = Mathf.SmoothStep(0f, 1f, (float)u);
                        input.TagTarget = figure.WorldToFigurePoint(SimulationSpace.ToUnity(tag.Runner) + new Vector3(0f, 0.35f, 0f));
                    }
                }
            }

            if (take != null && sinceTake < FieldingPlay.SecureTime)
            {
                // The glove goes to the ball before the take, then carries it in to the chest over the secure time.
                double lead = -sinceTake;
                input.GloveWeight = sinceTake >= 0.0 ? 1f : Mathf.SmoothStep(0f, 1f, (float)(1.0 - lead / GloveLead));
                // Caught, the ball moves with him (he keeps moving after the take): the take point frozen in his frame.
                Vector3 point = sinceTake >= 0.0 ? input.ActionPoint : figure.WorldToFigurePoint(SimulationSpace.ToUnity(take.Value.BallPoint));
                float secure = sinceTake >= 0.0 ? Mathf.SmoothStep(0f, 1f, (float)(sinceTake / FieldingPlay.SecureTime)) : 0f;
                input.GloveTarget = Vector3.Lerp(point, FieldingPoser.HoldPoint(input), secure);   // ends exactly in the hold pose
            }
            else if (th != null && sinceTake >= FieldingPlay.SecureTime)
            {
                double windStart = c.WindStart;
                if (time >= windStart)
                {
                    // Throw: the throwing hand carries the authoritative ball to the release point, then follows through. The
                    // stride steps as the arm loads and stays planted through the release and the follow-through.
                    // The wind-up ramps over at least 0.15 s (when the secure ends just before the arm action, it overlaps it).
                    float wind = Mathf.SmoothStep(0f, 1f, (float)((time - windStart) / Math.Max(0.15, armStart - windStart)));
                    float after = Mathf.Clamp01((float)((time - release) / 0.35));
                    input.ThrowWeight = time > release + FollowThrough ? 0f : wind * (1f - Mathf.SmoothStep(0f, 1f, (float)((time - release - 0.35) / 0.4)));
                    // On the move (gameplay releases while he still runs) there is no planted stride: the gait carries him.
                    bool moving = d.FielderSpeedAt(position, release) > MovingThrowSpeed;
                    input.ThrowStride = moving ? 0f : Mathf.SmoothStep(0f, 1f, (float)((time - windStart) / Math.Max(1e-3, release - windStart)))
                                        * (1f - Mathf.SmoothStep(0f, 1f, (float)((time - release - FollowThrough) / StrideRecovery)));
                    // Outfielders crow-hop into a planted throw.
                    if (!moving && Gameplay.Rules.DefensiveDecision.IsOutfielder(position) && time < armStart)
                    {
                        // Never into the catch: the hop starts once the ball is secured (shorter when the arm action is soon).
                        double hopStart = Math.Max(armStart - CrowHopTime, take.Value.Time + FieldingPlay.SecureTime);
                        input.CrowHop = hopStart < armStart ? Mathf.Clamp01((float)((time - hopStart) / (armStart - hopStart))) : 0f;
                    }
                    // Through the arm action the ball (and the hand holding it) goes from the hold at the chest to the gameplay ball
                    // path, arriving exactly on it at the release (the transfer, readable; no jump).
                    Vector3 hand = time < release
                        ? figure.WorldToFigurePoint(ArmBall(figure, input, d, time, armStart, release))
                        : Vector3.Lerp(figure.WorldToFigurePoint(SimulationSpace.ToUnity(th.ReleasePoint)), new Vector3(-0.25f, 0.95f, 0.55f), Mathf.SmoothStep(0f, 1f, after));
                    input.ThrowHand = hand;
                    input.ThrowTwist = time < release ? Mathf.Lerp(-35f, 25f, Mathf.SmoothStep(0f, 1f, (float)((time - armStart) / DefensivePlay.ArmAction))) : 25f;
                    input.HoldingBall = input.ThrowWeight < 1e-3f && time < release;
                }
                else input.HoldingBall = holding;
            }
            else input.HoldingBall = holding;

            _shownAction[i] = input.TagWeight > 0f ? BodyAction.None : input.Action;
            _gameplayAction[i] = take?.Action ?? FieldingAction.None;
            FieldingPoser.Compose(input, _pose);
            GroundFeet(root, rotation);
            if (adopted && d != null)
            {
                // Take the sandbox pitcher over from wherever and however he stood (root, heading and pose blend together).
                if (double.IsNaN(_pitcherTakeover))
                {
                    _pitcherTakeover = TakeoverTime(d);
                    // The blend source is the sandbox pitcher's pose at the takeover time itself, not whatever frame happened
                    // to be shown last, so the takeover looks the same at any frame rate.
                    if (_pitcherPoseAt != null) _pitcherPoseAt(_pitcherTakeover, _from);
                    else MannequinPose.Blend(_pitcherShown, _pitcherShown, 0f, _from);
                }

                float u = Mathf.SmoothStep(0f, 1f, (float)((time - _pitcherTakeover) / TakeoverBlend));
                if (u < 1f)
                {
                    MannequinPose.StepBlend(_from, _pose, u, _pose);
                    root = Vector3.Lerp(_takeoverRoot, root, u);
                    rotation = Quaternion.Slerp(_takeoverRotation, rotation, u);
                }
            }

            figure.transform.SetPositionAndRotation(root, rotation);
            figure.ApplyPose(_pose);
            if (adopted && _pitcherShown != null) MannequinPose.Blend(_pose, _pose, 0f, _pitcherShown);   // the next pitch blends from what was shown

            // The shown ball (presentation; gameplay placed it this frame): from the take point (carried with him) into the glove, in the glove
            // while held, back on the gameplay path through the arm action (the hand follows it), free after release.
            if (ball == null || !holding || take == null) return;
            Vector3 glove = figure.GloveAnchor.position;
            if (sinceTake < FieldingPlay.SecureTime)
                ball.position = Vector3.Lerp(figure.FigurePoint(input.ActionPoint), glove, Mathf.SmoothStep(0f, 1f, (float)(sinceTake / FieldingPlay.SecureTime)));
            else if (th != null && time >= c.WindStart)
                ball.position = ArmBall(figure, input, d, time, armStart, release);   // from the wind-up: at the hold, between the hands
            else ball.position = glove;
        }

        /// <summary>
        /// A fielder standing on a base is shown on its infield (mound) side edge — a foot on the bag, the base path clear for the
        /// runner — rather than on the bag's centre where gameplay puts him (and the runner): presentation only, faded in as
        /// he settles on the bag so nothing jumps.
        /// </summary>
        public static Vector3 BagSide(Vector3d at, float speed)
        {
            foreach (Simulation.Field.Base b in Bags)
            {
                Vector3d bag = Simulation.Field.FieldLayout.BasePosition(b);
                double d = new Vector3d(at.X - bag.X, at.Y - bag.Y, 0.0).Length;
                if (d > BagSideZone) continue;
                float near = Mathf.SmoothStep(0f, 1f, (float)((BagSideZone - d) / BagSideZone)) * (1f - Mathf.SmoothStep(0f, 1f, speed / 3f));
                Vector3 towardMound = (Mound - SimulationSpace.ToUnity(new Vector3d(bag.X, bag.Y, 0.0))).normalized;
                return towardMound * ((b == Simulation.Field.Base.Home ? HomeSideOffset : BagSideOffset) * near);
            }

            return Vector3.zero;
        }

        private static readonly Simulation.Field.Base[] Bags = { Simulation.Field.Base.First, Simulation.Field.Base.Second, Simulation.Field.Base.Third, Simulation.Field.Base.Home };
        private static readonly Vector3 Mound = new Vector3(0f, 0f, 18.44f);
        /// <summary>Within this distance of a bag's centre (m) the fielder is shown this far toward its infield edge (m).</summary>
        private const double BagSideZone = 0.8;
        private const float BagSideOffset = 0.3f, HomeSideOffset = 0.25f;

        /// <summary>The ball through the arm action: from the hold point (world) to the authoritative ball path, reaching it at the
        /// release.</summary>
        private static Vector3 ArmBall(PlayerMannequin figure, in FieldingPoseInput input, LiveDefense d, double time, double armStart, double release)
        {
            float s = Mathf.SmoothStep(0f, 1f, (float)((time - armStart) / Math.Max(1e-3, release - armStart)));
            Vector3 hold = figure.FigurePoint(FieldingPoser.HoldPoint(input));
            return Vector3.Lerp(hold, SimulationSpace.ToUnity(d.BallPositionAt(time)), s);
        }

        /// <summary>Feet on the ground where it is not flat (the mound): each ankle target is raised or lowered by the ground
        /// height under it relative to the root's.</summary>
        private void GroundFeet(Vector3 root, Quaternion rotation)
        {
            float under = FieldDressing.MoundHeight(root.x, root.z);
            Vector3 l = root + rotation * _pose.LeftFoot, r = root + rotation * _pose.RightFoot;
            _pose.LeftFoot.y += FieldDressing.MoundHeight(l.x, l.z) - under;
            _pose.RightFoot.y += FieldDressing.MoundHeight(r.x, r.z) - under;
        }

        private void OnGUI()
        {
            if (!_debug || _defense == null) return;
            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12 };
            GUI.Label(new Rect(Screen.width - 330f, 76f, 320f, 330f), LiveDefenseText.Roles(_defense, _time), _style);
        }
    }
}
