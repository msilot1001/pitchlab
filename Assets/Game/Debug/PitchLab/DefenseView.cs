using System;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using UnityEngine;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Shows the nine defenders of a <see cref="FieldingPlay"/> with the shared <see cref="PlayerMannequin"/> (TASK-005).
    /// Gameplay places every figure (position, speed, distance run come from <see cref="FielderMotion"/> at the play's
    /// time); this view only poses them (<see cref="FieldingPoser"/>), turns them, moves the glove to the authoritative
    /// intercept and, from the authoritative possession moment on, holds the ball in the glove. Optional debug: the primary's
    /// route, the intercept point and its numbers. Visual only — no colliders, nothing fed back.
    /// </summary>
    public sealed class DefenseView : MonoBehaviour
    {
        /// <summary>How long before the intercept the glove starts toward the ball (s).</summary>
        public const double GloveLead = 0.45;

        private readonly PlayerMannequin[] _figures = new PlayerMannequin[DefensiveAlignment.Count];
        private readonly MannequinPose _pose = new MannequinPose();
        private readonly MannequinPose _from = new MannequinPose();
        private PlayerMannequin _prefab;
        private PlayerMannequin _adoptedPitcher;
        private MannequinPose _pitcherShown;   // the sandbox's shown pitcher pose (written back while this view drives him)
        private Action<double, MannequinPose> _pitcherPoseAt;   // the sandbox pitcher's pose at a play time (deterministic blend source)
        private DefensiveAlignment _alignment = DefensiveAlignment.Standard;
        private LineRenderer _route;
        private Transform _interceptMarker;
        private FieldingPlay _play;
        private DefensivePlay _defense;
        private double _time;
        private bool _debug;
        private double _pitcherTakeover = double.NaN;
        private Vector3 _takeoverRoot;
        private Quaternion _takeoverRotation;
        private GUIStyle _style;

        /// <summary>Hide the catcher (e.g. while the batting camera looks over his shoulder).</summary>
        public bool CatcherVisible { get; set; } = true;

        public PlayerMannequin Figure(DefensivePosition position) => _figures[(int)position];

        /// <summary>The primary defender's figure while a play is shown, else null (camera framing).</summary>
        public Transform Primary => _play?.Primary is DefensivePosition p ? _figures[(int)p].transform : null;

        /// <summary>
        /// Builds the defenders. <paramref name="pitcher"/>: an existing figure (the sandbox pitcher) that this view drives only
        /// when the pitcher is the primary defender of a play (blending from his pose at the takeover, <paramref name="pitcherPoseAt"/>,
        /// and writing what is shown back to <paramref name="pitcherShown"/>); null builds its own.
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

            _route = new GameObject("FieldingRoute").AddComponent<LineRenderer>();
            _route.transform.SetParent(transform, false);
            _route.widthMultiplier = 0.12f;
            _route.sharedMaterial = PresentationMaterials.Get(new Color(0.3f, 0.85f, 1f), unlit: true);
            _route.enabled = false;
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "InterceptMarker";
            PlayerMannequin.DestroyCollider(marker);
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = Vector3.one * 0.3f;
            marker.GetComponent<MeshRenderer>().sharedMaterial = PresentationMaterials.Get(new Color(1f, 0.3f, 0.9f), unlit: true);
            _interceptMarker = marker.transform;
            marker.SetActive(false);
            Show(null, 0.0, null, false);
        }

        /// <summary>True when this view drives the adopted pitcher at <paramref name="time"/> (the sandbox must not).</summary>
        public bool DrivesPitcher(FieldingPlay play, double time) =>
            _adoptedPitcher != null && play != null && play.Primary == DefensivePosition.P && time >= TakeoverTime(play);

        /// <summary>When this view takes the sandbox pitcher over (deterministic: shortly before he starts to run).</summary>
        private static double TakeoverTime(FieldingPlay play) => play.Motion(DefensivePosition.P).StartTime - TakeoverBlend;

        private const double TakeoverBlend = 0.25;

        /// <summary>Poses every defender for <paramref name="play"/> at play time <paramref name="time"/> (null: ready at the alignment).</summary>
        public void Show(DefensivePlay play, double time, Transform ball, bool debug)
        {
            if (!ReferenceEquals(play?.Fielding, _play)) _pitcherTakeover = double.NaN;
            _defense = play;
            _play = play?.Fielding;
            _time = time;
            _debug = debug;
            for (int i = 0; i < DefensiveAlignment.Count; i++)
            {
                var p = (DefensivePosition)i;
                PlayerMannequin figure = _figures[i];
                if (figure == null) continue;
                if (p == DefensivePosition.P && figure == _adoptedPitcher && !DrivesPitcher(_play, time)) continue;
                if (p == DefensivePosition.C) figure.gameObject.SetActive(CatcherVisible || (play?.Throw is ThrowPlay th && th.Receiver == DefensivePosition.C && time >= th.ReleaseTime));
                Pose(p, figure, play, time, ball);
            }

            bool showRoute = debug && _play?.Primary != null;
            _route.enabled = showRoute;
            _interceptMarker.gameObject.SetActive(showRoute);
            if (showRoute)
            {
                FielderMotion m = _play.Motion(_play.Primary.Value);
                _route.positionCount = 2;
                _route.SetPosition(0, SimulationSpace.ToUnity(m.Start) + Vector3.up * 0.05f);
                _route.SetPosition(1, SimulationSpace.ToUnity(m.Target) + Vector3.up * 0.05f);
                bool afterRelease = play.Throw != null && time >= play.Throw.ReleaseTime && play.Throw.Caught;
                _interceptMarker.position = SimulationSpace.ToUnity(afterRelease ? play.Throw.Catch.Ball.Position : _play.Intercept.Ball.Position);
            }
        }

        /// <summary>The figure the camera should keep with the ball: the fielder, then (once thrown) the receiver.</summary>
        public Transform Focus
        {
            get
            {
                if (_defense?.Throw is ThrowPlay th && _time >= th.ReleaseTime) return _figures[(int)th.Receiver].transform;
                return Primary;
            }
        }

        private void Pose(DefensivePosition position, PlayerMannequin figure, DefensivePlay play, double time, Transform ball)
        {
            FieldingPlay fielding = play?.Fielding;
            ThrowPlay th = play?.Throw;
            Vector3d at = play?.FielderPositionAt(position, time) ?? _alignment[position];
            Vector3 root = SimulationSpace.ToUnity(new Vector3d(at.X, at.Y, 0.0));
            root.y = FieldDressing.MoundHeight(root.x, root.z);   // stand on the mound, not in it (presentation only)

            bool fielder = fielding != null && fielding.Primary == position;
            bool receiver = th != null && th.Receiver == position;
            float speed = play == null ? 0f : (float)play.FielderSpeedAt(position, time);
            var input = new FieldingPoseInput { Speed = speed, Distance = play == null ? 0f : (float)play.FielderDistanceAt(position, time) };

            // The moment this figure took the ball (the fielder's pickup, the receiver's catch) and what it does with it.
            double take = fielder ? fielding.PossessionTime : receiver && th.Caught ? th.Catch.Time : double.PositiveInfinity;
            Intercept takeAt = fielder ? fielding.Intercept : receiver ? th.Catch : default;
            double sinceTake = time - take;
            bool throwing = fielder && th != null;
            double release = throwing ? th.ReleaseTime : double.PositiveInfinity;
            double armStart = release - DefensivePlay.ArmAction;

            // Heading, turned smoothly with speed: toward the ball (the incoming throw for a receiver), toward the target
            // base while throwing, toward the infield once holding; into the run direction as he speeds up.
            Vector3 face;
            if (throwing && sinceTake >= 0.0) face = SimulationSpace.ToUnity(FieldLayout.BasePosition(th.Target)) - root;
            else if (sinceTake >= 0.0 || ball == null) face = -root;
            else face = ball.position - root;
            face.y = 0f;
            Vector3 run = play != null ? SimulationSpace.ToUnity(play.FielderDirectionAt(position, time)) : Vector3.zero;
            float w = Mathf.SmoothStep(0f, 1f, speed / 2.5f);
            Vector3 look = face.sqrMagnitude > 1e-4f && run.sqrMagnitude > 1e-4f
                ? Vector3.Slerp(face.normalized, run.normalized, w)
                : run.sqrMagnitude > 1e-4f ? run : face;
            Quaternion rotation = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look) : figure.transform.rotation;
            bool adopted = position == DefensivePosition.P && figure == _adoptedPitcher;
            if (adopted && double.IsNaN(_pitcherTakeover))
            {
                // Takeover starts from where the sandbox pitcher stood (read before this frame moves him).
                _takeoverRoot = figure.transform.position;
                _takeoverRotation = figure.transform.rotation;
            }

            figure.transform.SetPositionAndRotation(root, rotation);

            if ((fielder || receiver) && double.IsPositiveInfinity(take) == false && sinceTake < FieldingPlay.SecureTime)
            {
                // The glove goes to the ball before the take, then carries it in to the chest over the secure time.
                double lead = take - time;
                if (lead <= GloveLead)
                {
                    input.GloveWeight = sinceTake >= 0.0 ? 1f : Mathf.SmoothStep(0f, 1f, (float)(1.0 - lead / GloveLead));
                    Vector3 point = figure.WorldToFigurePoint(SimulationSpace.ToUnity(takeAt.Ball.Position));
                    float secure = sinceTake >= 0.0 ? Mathf.SmoothStep(0f, 1f, (float)(sinceTake / FieldingPlay.SecureTime)) : 0f;
                    input.GloveTarget = Vector3.Lerp(point, FieldingPoser.HoldGlove, secure);   // ends exactly in the hold pose
                }
            }
            else if (sinceTake >= FieldingPlay.SecureTime)
            {
                double windStart = Math.Max(armStart - 0.15, take + FieldingPlay.SecureTime);   // never before the ball is secured
                if (throwing && time >= windStart)
                {
                    // Throw: the throwing hand carries the authoritative ball to the release point, then follows through.
                    float wind = Mathf.SmoothStep(0f, 1f, (float)((time - windStart) / Math.Max(1e-3, armStart - windStart)));
                    float after = Mathf.Clamp01((float)((time - release) / 0.35));
                    input.ThrowWeight = wind * (1f - Mathf.SmoothStep(0f, 1f, (float)((time - release - 0.35) / 0.4)));
                    Vector3 hand = time < release
                        ? figure.WorldToFigurePoint(SimulationSpace.ToUnity(play.BallPositionAt(time)))
                        : Vector3.Lerp(figure.WorldToFigurePoint(SimulationSpace.ToUnity(th.ReleasePoint)), new Vector3(-0.25f, 0.95f, 0.55f), Mathf.SmoothStep(0f, 1f, after));
                    input.ThrowHand = hand;
                    input.ThrowTwist = time < release ? Mathf.Lerp(-35f, 25f, Mathf.SmoothStep(0f, 1f, (float)((time - armStart) / DefensivePlay.ArmAction))) : 25f;
                    input.HoldingBall = input.ThrowWeight < 1e-3f && time < release;
                }
                else input.HoldingBall = !(throwing && time >= release);
            }

            FieldingPoser.Compose(input, _pose);
            if (position == DefensivePosition.P && figure == _adoptedPitcher)
            {
                // Take the sandbox pitcher over from wherever and however he stood (root, heading and pose blend together).
                if (double.IsNaN(_pitcherTakeover))
                {
                    _pitcherTakeover = TakeoverTime(fielding);
                    // The blend source is the sandbox pitcher's pose at the takeover time itself, not whatever frame happened
                    // to be shown last, so the takeover looks the same at any frame rate (Codex review).
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
            if (position == DefensivePosition.P && figure == _adoptedPitcher && _pitcherShown != null)
                MannequinPose.Blend(_pose, _pose, 0f, _pitcherShown);   // the sandbox's next pitch blends from what was shown

            // The shown ball (presentation; gameplay placed it this frame): settling into the glove after a take, in the
            // glove while held, back on the gameplay path through the arm action (the hand follows it), free after release.
            if (ball == null || !(sinceTake >= 0.0) || (throwing && time >= release)) return;
            Vector3 glove = figure.GloveAnchor.position;
            if (sinceTake < FieldingPlay.SecureTime)
                ball.position = Vector3.Lerp(ball.position, glove, Mathf.SmoothStep(0f, 1f, (float)(sinceTake / FieldingPlay.SecureTime)));
            else if (throwing && time >= armStart)
                ball.position = Vector3.Lerp(glove, ball.position, Mathf.SmoothStep(0f, 1f, (float)((time - armStart) / 0.12)));
            else ball.position = glove;
        }

        private void OnGUI()
        {
            if (!_debug || _play == null) return;
            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12 };
            string text;
            if (_play.Primary is DefensivePosition p)
            {
                Intercept i = _play.Intercept;
                FielderMotion m = _play.Motion(p);
                double t0 = _play.Ball.First.Time;
                text = $"Fielding: {p} ({i.Kind}) · call {_play.Call}\n" +
                       $"start ({m.Start.X:0.0}, {m.Start.Y:0.0}) → route {i.RouteDistance:0.0} m · reacts +{FielderProfile.For(p).ReactionTime:0.00} s\n" +
                       $"intercept +{i.Time - t0:0.00} s at ({i.Ball.Position.X:0.0}, {i.Ball.Position.Y:0.0}, {i.Ball.Position.Z:0.00}) m · ball {_play.Ball.PhaseAt(i.Time)} {i.Ball.Velocity.Length:0.0} m/s\n" +
                       $"arrives +{m.ArrivalTime - t0:0.00} s · margin {i.Margin:0.00} s · {(m.StopsAtTarget ? "settles" : "on the run")} · now +{_time - t0:0.00} s · {_defense.AuthorityAt(_time)}";
            }
            else text = $"Fielding: {_play.Outcome} · call {_play.Call}";
            if (_defense?.Throw is ThrowPlay th)
            {
                double t0 = _play.Ball.First.Time;
                text += $"\nthrow {th.Thrower}→{th.Receiver} at {th.Target}: release +{th.ReleaseTime - t0:0.00} s · {th.Flight.First.Velocity.Length / 0.44704:0} mph · " +
                        (th.Caught ? $"caught +{th.Catch.Time - t0:0.00} s ({th.Catch.Time - th.ReleaseTime:0.00} s flight)" : "not caught") + $" · {_defense.AuthorityAt(_time)}";
            }
            GUI.Label(new Rect(Screen.width - 470f, 76f, 460f, 90f), text, _style);
        }
    }
}
