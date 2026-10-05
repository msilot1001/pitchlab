using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using UnityEngine;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Shows the runners of a <see cref="LivePlay"/> (TASK-007) with the shared <see cref="PlayerMannequin"/> in the offense's
    /// colour: root on the authoritative base-path position, facing where he runs, the run cycle phased by the distance he
    /// has actually run (no foot skating), standing on his base when still. Reads gameplay only; nothing here decides where
    /// a runner is or whether he is safe.
    /// </summary>
    public sealed class RunnerView : MonoBehaviour
    {
        public static readonly Color OffenseColor = new Color(0.78f, 0.78f, 0.82f);

        private readonly Dictionary<Base, PlayerMannequin> _figures = new Dictionary<Base, PlayerMannequin>();
        private readonly MannequinPose _pose = new MannequinPose();
        private PlayerMannequin _prefab;

        /// <summary>Seconds a retired runner stays visible (walking off is not modelled), and a scorer after crossing.</summary>
        public const double LingerAfterOut = 1.2, LingerAfterScore = 1.5;

        public void Build(PlayerMannequin prefab)
        {
            _prefab = prefab;
            foreach (Base b in new[] { Base.Home, Base.First, Base.Second, Base.Third })
            {
                PlayerMannequin m = Instantiate(_prefab, transform);
                m.name = b == Base.Home ? "Batter-runner" : $"Runner from {Bases.Name(b)}";
                m.BodyColor = OffenseColor;
                m.Build();
                m.gameObject.SetActive(false);
                _figures[b] = m;
            }
        }

        /// <summary>The figure for a runner (identified by the base he started on; home = the batter-runner).</summary>
        public PlayerMannequin Figure(Runner runner) => _figures[runner.From];

        /// <summary>
        /// Between plays: the runners on <paramref name="bases"/>, <paramref name="leadOff"/> of the way from their bags to
        /// their leads (1 = exactly where the next play starts them: no jump at contact). Presentation only.
        /// </summary>
        public void ShowSituation(BaseOccupancy bases, float leadOff, float secondary = 0f)
        {
            foreach (var pair in _figures)
            {
                bool on = pair.Key != Base.Home && bases.IsOccupied(pair.Key);
                if (pair.Value.gameObject.activeSelf != on) pair.Value.gameObject.SetActive(on);
                if (!on) continue;
                BaseLeg leg = BaseLeg.Of(pair.Key, false);
                float u = Mathf.SmoothStep(0f, 1f, leadOff);
                Vector3 at = SimulationSpace.ToUnity(leg.PositionAt(u * LivePlay.Lead(pair.Key)));
                Vector3 toNext = SimulationSpace.ToUnity(FieldLayout.BasePosition(BaseLeg.Bases(pair.Key))) - at;
                // Leading off: the lead stance, lower during the delivery (the secondary lead, in place).
                var input = new FieldingPoseInput { Runner = true, ActionTime = float.NaN, Ready = u > 0.5f ? ReadyStyle.RunnerLead : ReadyStyle.RunnerStand, Secondary = secondary };
                Pose(pair.Value, at, YawOf(Mound - Flat(at)), Vector3.zero, 0f, 0f, 0f, input);
            }
        }

        /// <summary>
        /// Poses every runner of <paramref name="play"/> at play time <paramref name="time"/>. <paramref name="batterFrom"/>
        /// (optional): where the batter figure stood — the batter-runner is blended from there onto the base path over his
        /// first <see cref="HandOver"/> s (presentation only; the gameplay runner starts at the plate).
        /// </summary>
        public void Show(LivePlay play, double time, Vector3? batterFrom = null)
        {
            // Each figure's visibility is decided once (no off/on toggling of its renderers every frame).
            foreach (var pair in _figures)
            {
                LiveRunner r = play?.RunnerOf(new Runner(pair.Key));
                bool shown = r != null && !(r.IsOut && time > r.OutTime + LingerAfterOut) && !(r.HasScored && time > r.ScoreTime + LingerAfterScore)
                             && !(r.Id.IsBatter && batterFrom.HasValue && time < play.ContactTime + play.Profile.BatterStartDelay);
                if (pair.Value.gameObject.activeSelf != shown) pair.Value.gameObject.SetActive(shown);
            }

            if (play == null) return;
            foreach (LiveRunner r in play.Runners)
            {
                PlayerMannequin m = _figures[r.Id.From];
                if (!m.gameObject.activeSelf) continue;
                Vector3 root = SimulationSpace.ToUnity(r.PositionAt(time));
                if (r.Id.IsBatter && batterFrom.HasValue)
                {
                    double since = time - (play.ContactTime + play.Profile.BatterStartDelay);
                    float u = Mathf.SmoothStep(0f, 1f, (float)(since / HandOver));
                    root = Vector3.Lerp(new Vector3(batterFrom.Value.x, 0f, batterFrom.Value.z), root, u);
                }

                // Standing on a bag he is shown on its outside edge (a foot on it), clear of a fielder covering it from the
                // infield side (DefenseView.BagSide) — presentation only, gone once he runs.
                root += BagSide(r, time);

                MotionTrack track = Track(r.Id.From);
                LiveRunner runner = r;
                if (!track.Follows(play)) track.Follow(play, play.ContactTime, YawOf(Facing(runner, play.ContactTime)), t => Sample(runner, t));
                (float phase, float yaw) = track.At(time);
                Vector3 velocity = Flat(SimulationSpace.ToUnity(r.VelocityAt(time)));
                float speed = velocity.magnitude;
                float accel = (float)((r.SpeedAt(time + 0.05) - r.SpeedAt(Math.Max(play.ContactTime, time - 0.05))) / 0.1);
                var input = new FieldingPoseInput { Runner = true, ActionTime = float.NaN };
                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                Action(play, r, time, root, rotation, ref input);
                _shown[r.Id.From] = input.Action;
                Pose(m, root, yaw, velocity, phase, accel, speed * track.TurnRate(time) * Mathf.Deg2Rad, input);
            }
        }

        /// <summary>A runner standing on a bag is shown this far (m) from its centre, away from the mound.</summary>
        public const float RunnerBagSide = 0.3f;

        /// <summary>The presentation offset of a runner standing on a bag (zero elsewhere, fading out as he runs).</summary>
        public static Vector3 BagSide(LiveRunner r, double time)
        {
            if (!r.TouchingBaseAt(time, out Base on) || on == Base.Home) return Vector3.zero;
            Vector3 bag = SimulationSpace.ToUnity(FieldLayout.BasePosition(on));
            float still = 1f - Mathf.SmoothStep(0f, 1f, (float)r.SpeedAt(time) / 2f);
            return (bag - Mound).normalized * (RunnerBagSide * still);
        }

        /// <summary>Time a runner stays down after a slide before getting up (s).</summary>
        public const float SlideRecovery = 0.5f;
        /// <summary>A throw caught within this long (s) of a runner's arrival makes it a close play (he slides).</summary>
        public const double ClosePlay = 1.0;

        /// <summary>
        /// The runner's body action at <paramref name="time"/>, chosen deterministically from the play: a feet-first slide into
        /// second or third whenever gameplay stops him there with the slide deceleration (covering exactly that braking, so he
        /// reaches the bag at the authoritative time); a feet-first slide across the plate on a close play at home; a head-first
        /// slide back to a base with a throw coming there; the lead-off stance off a base. Never at first (he runs through it).
        /// </summary>
        public static void Action(LivePlay play, LiveRunner r, double time, Vector3 root, Quaternion rotation, ref FieldingPoseInput input)
        {
            bool onBag = r.TouchingBaseAt(time, out _);
            input.Ready = onBag || r.SpeedAt(time) > 0.3 ? ReadyStyle.RunnerStand : ReadyStyle.RunnerLead;
            IReadOnlyList<LiveRunner.Segment> legs = r.Segments;
            for (int i = 0; i < legs.Count; i++)
            {
                PathMotion m = legs[i].Motion;
                BaseLeg leg = legs[i].Leg;
                double next = i + 1 < legs.Count ? legs[i + 1].Motion.StartTime : double.PositiveInfinity;
                if (m.Sign > 0.0 && m.EndSpeed == 0.0 && leg.To != Base.First && leg.To != Base.Home && m.Deceleration == play.Profile.SlideDeceleration
                    && m.Target >= leg.Length - 1e-6 && m.ArrivalTime <= next + 1e-6)
                {
                    // Into second or third: the authoritative slide (its braking phase).
                    double lead = m.ArrivalTime - m.BrakeTime;
                    if (time >= m.BrakeTime && time <= m.ArrivalTime + SlideRecovery + FieldingPoser.GetUp)
                        Set(ref input, BodyAction.Slide, time - m.ArrivalTime, (float)lead, root, rotation, leg.End);
                }
                else if (m.Sign > 0.0 && leg.To == Base.Home)
                {
                    // Home: run through it, unless the throw home is close — then slide across the plate.
                    double touch = m.TimeAt(leg.Length - LiveRunner.TouchDistance);
                    if (double.IsNaN(touch) || double.IsInfinity(touch) || touch > next + 1e-6) continue;
                    if (!CloseThrow(play, Base.Home, touch)) continue;
                    const double lead = 0.45;
                    if (time >= touch - lead && time <= touch + SlideRecovery + FieldingPoser.GetUp)
                        Set(ref input, BodyAction.Slide, time - touch, (float)lead, root, rotation, leg.End);
                }
                else if (m.Sign < 0.0 && m.Target <= 1e-6 && m.ArrivalTime <= next + 1e-6)
                {
                    // Back to the base he left with a throw coming there: head-first.
                    if (!CloseThrow(play, leg.From, m.ArrivalTime)) continue;
                    const double lead = 0.35;
                    if (time >= m.ArrivalTime - lead && time <= m.ArrivalTime + SlideRecovery + FieldingPoser.GetUp)
                        Set(ref input, BodyAction.HeadFirst, time - m.ArrivalTime, (float)lead, root, rotation, leg.Start);
                }
            }
        }

        private static void Set(ref FieldingPoseInput input, BodyAction action, double t, float lead, Vector3 root, Quaternion rotation, Vector3d bag)
        {
            input.Action = action;
            input.ActionTime = (float)t;
            input.ActionLead = lead;
            input.Recovery = SlideRecovery;
            Vector3 world = SimulationSpace.ToUnity(new Vector3d(bag.X, bag.Y, 0.0));
            input.ActionPoint = Quaternion.Inverse(rotation) * (world - new Vector3(root.x, 0f, root.z));
        }

        private static bool CloseThrow(LivePlay play, Base b, double arrival)
        {
            foreach (var th in play.Defense.Throws)
                if (th.Target == b && th.Caught && Math.Abs(th.Catch.Time - arrival) < ClosePlay) return true;
            return false;
        }

        private readonly Dictionary<Base, MotionTrack> _tracks = new Dictionary<Base, MotionTrack>();
        private readonly Dictionary<Base, BodyAction> _shown = new Dictionary<Base, BodyAction>();

        /// <summary>Motion debug: the body action shown for a runner last frame.</summary>
        public BodyAction ShownAction(Runner r) => _shown.TryGetValue(r.From, out BodyAction a) ? a : BodyAction.None;

        private MotionTrack Track(Base from)
        {
            if (!_tracks.TryGetValue(from, out MotionTrack t)) _tracks[from] = t = new MotionTrack();
            return t;
        }

        /// <summary>Where a runner faces: along his path while moving, toward the mound (the pitcher, the ball's way) standing.</summary>
        private static Vector3 Facing(LiveRunner r, double time)
        {
            Vector3 velocity = Flat(SimulationSpace.ToUnity(r.VelocityAt(time)));
            if (velocity.magnitude > 0.3f) return Flat(SimulationSpace.ToUnity(r.HeadingAt(time)));
            Vector3 at = Flat(SimulationSpace.ToUnity(r.PositionAt(time)));
            return Mound - at;
        }

        private static MotionSample Sample(LiveRunner r, double time) =>
            new MotionSample { Velocity = Flat(SimulationSpace.ToUnity(r.VelocityAt(time))), Facing = Facing(r, time), MaxTurnRate = 540f };

        private static readonly Vector3 Mound = new Vector3(0f, 0f, 18.44f);

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float YawOf(Vector3 v) => v.sqrMagnitude > 1e-8f ? Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg : 0f;

        /// <summary>Batter → batter-runner hand-over blend (s).</summary>
        public const double HandOver = 0.35;

        private void Pose(PlayerMannequin m, Vector3 root, float yaw, Vector3 velocity, float phase, float accel, float lateral, FieldingPoseInput input)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            m.transform.SetPositionAndRotation(new Vector3(root.x, 0f, root.z), rotation);
            Vector3 local = Quaternion.Inverse(rotation) * velocity;
            float speed = velocity.magnitude;
            input.Speed = speed;
            input.GaitPhase = phase;
            input.MoveDir = speed > 0.05f ? new Vector2(local.x, local.z) : Vector2.zero;
            input.Accel = accel;
            input.LateralAccel = lateral;
            FieldingPoser.Compose(input, _pose);
            m.ApplyPose(_pose);
        }
    }
}
