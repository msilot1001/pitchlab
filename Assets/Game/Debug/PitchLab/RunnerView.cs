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
        public void ShowSituation(BaseOccupancy bases, float leadOff)
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
                Pose(pair.Value, at, toNext, leadOff > 0f && leadOff < 1f ? 1.2f : 0f, 0f);
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

                Vector3 heading = SimulationSpace.ToUnity(r.HeadingAt(time));
                Pose(m, root, heading, (float)r.SpeedAt(time), (float)r.DistanceRunAt(time));
            }
        }

        /// <summary>Batter → batter-runner hand-over blend (s).</summary>
        public const double HandOver = 0.35;

        private void Pose(PlayerMannequin m, Vector3 root, Vector3 heading, float speed, float distance)
        {
            heading.y = 0f;
            Quaternion rotation = heading.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(heading) : m.transform.rotation;
            m.transform.SetPositionAndRotation(new Vector3(root.x, 0f, root.z), rotation);
            var input = new FieldingPoseInput { Speed = speed, Distance = distance };
            FieldingPoser.Compose(input, _pose);
            m.ApplyPose(_pose);
        }
    }
}
