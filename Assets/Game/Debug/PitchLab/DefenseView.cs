using System;
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
        public const double ReturnTime = 2.0;
        private readonly Vector3d[] _returnFrom = new Vector3d[DefensiveAlignment.Count];
        /// <summary>The real-time clock the jog back runs on (the lab's clock: tests freeze it).</summary>
        public Func<double> Clock { get; set; } = () => Time.realtimeSinceStartupAsDouble;
        private double _returnStart = double.NegativeInfinity;
        /// <summary>The adopted pitcher moved in the last play: this view walks him back to the rubber before handing him back.</summary>
        private bool _pitcherReturning;
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
        }

        /// <summary>The figure the camera should keep with the ball (the holder, the receiver of a throw in the air, whoever
        /// plays the ball), else null.</summary>
        public Transform Focus => _defense?.FocusAt(_time) is DefensivePosition p ? _figures[(int)p].transform : null;

        private void Pose(DefensivePosition position, PlayerMannequin figure, LiveDefense d, double time, Transform ball)
        {
            Vector3d at = d?.FielderPositionAt(position, time) ?? _alignment[position];
            float speed = d == null ? 0f : (float)d.FielderSpeedAt(position, time);
            if (d == null)
            {
                // Jogging back from where the last play left him.
                double u = (Clock() - _returnStart) / ReturnTime;
                if (u < 1.0)
                {
                    Vector3d from = _returnFrom[(int)position], to = at;
                    // The adopted pitcher goes back to where the delivery has him (the rubber), which then takes over.
                    if (position == DefensivePosition.P && figure == _adoptedPitcher) to = SimulationSpace.ToSimulation(_takeoverRoot);
                    at = from + Mathf.SmoothStep(0f, 1f, (float)u) * (to - from);
                    speed = (to - from).Length > 0.5 ? 3f : 0f;
                }
            }

            Vector3 root = SimulationSpace.ToUnity(new Vector3d(at.X, at.Y, 0.0));
            root.y = FieldDressing.MoundHeight(root.x, root.z);   // stand on the mound, not in it (presentation only)
            var input = new FieldingPoseInput { Speed = speed, Distance = d == null ? 0f : (float)d.FielderDistanceAt(position, time) };

            // His takes and throws around now: the take he is reaching for (or has just made), the throw he is making.
            BallTake? take = null;
            if (d != null)
                foreach (BallTake k in d.Takes)
                    if (k.Fielder == position && k.Time - GloveLead <= time && (take == null || k.Time > take.Value.Time)) take = k;
            LiveThrow th = null;
            if (d != null)
                foreach (LiveThrow x in d.Throws)
                    if (x.Thrower == position && (take == null || x.ReleaseTime >= take.Value.Time) && time <= x.ReleaseTime + FollowThrough)
                    {
                        th = x;
                        break;
                    }

            double sinceTake = take == null ? double.NegativeInfinity : time - take.Value.Time;
            bool holding = d?.HolderAt(time) == position;
            double release = th?.ReleaseTime ?? double.PositiveInfinity;
            double armStart = release - DefensivePlay.ArmAction;

            // Heading, turned smoothly with speed: toward the incoming ball, toward the target while throwing, toward the
            // infield while holding; into the run direction as he speeds up.
            Vector3 face;
            if (th != null && (holding || time >= release && time <= release + FollowThrough)) face = SimulationSpace.ToUnity(th.AimPoint) - root;
            else if (take != null && sinceTake < 0.0 && ball != null) face = ball.position - root;
            else if (holding || ball == null) face = -root;
            else face = ball.position - root;
            face.y = 0f;
            Vector3 run = d != null ? SimulationSpace.ToUnity(d.FielderDirectionAt(position, time)) : Vector3.zero;
            float w = Mathf.SmoothStep(0f, 1f, speed / 2.5f);
            Vector3 look = face.sqrMagnitude > 1e-4f && run.sqrMagnitude > 1e-4f
                ? Vector3.Slerp(face.normalized, run.normalized, w)
                : run.sqrMagnitude > 1e-4f ? run : face;
            Quaternion rotation = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look) : figure.transform.rotation;
            bool adopted = position == DefensivePosition.P && figure == _adoptedPitcher;
            if (adopted && d != null && double.IsNaN(_pitcherTakeover))
            {
                // Takeover starts from where the sandbox pitcher stood (read before this frame moves him).
                _takeoverRoot = figure.transform.position;
                _takeoverRotation = figure.transform.rotation;
            }

            figure.transform.SetPositionAndRotation(root, rotation);

            if (take != null && sinceTake < FieldingPlay.SecureTime)
            {
                // The glove goes to the ball before the take, then carries it in to the chest over the secure time.
                double lead = -sinceTake;
                input.GloveWeight = sinceTake >= 0.0 ? 1f : Mathf.SmoothStep(0f, 1f, (float)(1.0 - lead / GloveLead));
                Vector3 point = figure.WorldToFigurePoint(SimulationSpace.ToUnity(take.Value.BallPoint));
                float secure = sinceTake >= 0.0 ? Mathf.SmoothStep(0f, 1f, (float)(sinceTake / FieldingPlay.SecureTime)) : 0f;
                input.GloveTarget = Vector3.Lerp(point, FieldingPoser.HoldGlove, secure);   // ends exactly in the hold pose
            }
            else if (th != null && sinceTake >= FieldingPlay.SecureTime)
            {
                double windStart = Math.Max(armStart - 0.15, take.Value.Time + FieldingPlay.SecureTime);   // never before the ball is secured
                if (time >= windStart)
                {
                    // Throw: the throwing hand carries the authoritative ball to the release point, then follows through.
                    float wind = Mathf.SmoothStep(0f, 1f, (float)((time - windStart) / Math.Max(1e-3, armStart - windStart)));
                    float after = Mathf.Clamp01((float)((time - release) / 0.35));
                    input.ThrowWeight = wind * (1f - Mathf.SmoothStep(0f, 1f, (float)((time - release - 0.35) / 0.4)));
                    Vector3 hand = time < release
                        ? figure.WorldToFigurePoint(SimulationSpace.ToUnity(d.BallPositionAt(time)))
                        : Vector3.Lerp(figure.WorldToFigurePoint(SimulationSpace.ToUnity(th.ReleasePoint)), new Vector3(-0.25f, 0.95f, 0.55f), Mathf.SmoothStep(0f, 1f, after));
                    input.ThrowHand = hand;
                    input.ThrowTwist = time < release ? Mathf.Lerp(-35f, 25f, Mathf.SmoothStep(0f, 1f, (float)((time - armStart) / DefensivePlay.ArmAction))) : 25f;
                    input.HoldingBall = input.ThrowWeight < 1e-3f && time < release;
                }
                else input.HoldingBall = holding;
            }
            else input.HoldingBall = holding;

            FieldingPoser.Compose(input, _pose);
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

            // The shown ball (presentation; gameplay placed it this frame): settling into the glove after a take, in the glove
            // while held, back on the gameplay path through the arm action (the hand follows it), free after release.
            if (ball == null || !holding || take == null) return;
            Vector3 glove = figure.GloveAnchor.position;
            if (sinceTake < FieldingPlay.SecureTime)
                ball.position = Vector3.Lerp(ball.position, glove, Mathf.SmoothStep(0f, 1f, (float)(sinceTake / FieldingPlay.SecureTime)));
            else if (th != null && time >= armStart)
                ball.position = Vector3.Lerp(glove, ball.position, Mathf.SmoothStep(0f, 1f, (float)((time - armStart) / 0.12)));
            else ball.position = glove;
        }

        private void OnGUI()
        {
            if (!_debug || _defense == null) return;
            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12 };
            GUI.Label(new Rect(Screen.width - 330f, 76f, 320f, 330f), LiveDefenseText.Roles(_defense, _time), _style);
        }
    }
}
