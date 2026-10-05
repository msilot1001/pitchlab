using System;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Field;
using UnityEngine;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Development only: keeps the camera on one figure from a fixed offset (its own frame or world), after everything
    /// else has placed it this frame — for inspecting motion close up (slow motion, frozen clock). Disabled by default; it
    /// takes the camera over while it has a target and gives it back when the target is cleared.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public sealed class InspectionCamera : MonoBehaviour
    {
        public Transform Target;
        /// <summary>Camera position relative to the target (world axes), and the height looked at on the target.</summary>
        public Vector3 Offset = new Vector3(-3.5f, 1.6f, -3.5f);
        public float LookHeight = 0.9f;
        public float FieldOfView = 40f;

        private const double Frozen = 1000.0;
        private static bool _frozen;

        /// <summary>
        /// FieldingLab inspection (development): freezes the lab's clock at play time <paramref name="t"/> of preset
        /// <paramref name="scenario"/> and puts the camera on <paramref name="figure"/> ("SS", "CF", … or "R1", "R2", "R3",
        /// "BR"); returns what gameplay says the figure is doing. Null figure: the lab's own camera.
        /// </summary>
        public static string Inspect(string scenario, double t, string figure, Vector3? offset = null)
        {
            var lab = FindFirstObjectByType<FieldingLabController>();
            if (lab == null) return "no FieldingLab";
            int index = FieldingLabController.IndexOf(scenario);
            if (index < 0) return $"no preset '{scenario}'";
            // Relaunch on the frozen clock when the scenario changes or the lab is running on the real one.
            if (lab.PresetIndex != index || lab.Live == null || !_frozen)
            {
                lab.Clock = () => Frozen;
                lab.SelectScenario(index);
                _frozen = true;
            }

            double start = lab.Play.First.Time;
            lab.Clock = () => Frozen + t;
            Camera cam = Camera.main;
            InspectionCamera insp = cam.GetComponent<InspectionCamera>();
            if (insp == null) insp = cam.gameObject.AddComponent<InspectionCamera>();   // (Unity's fake null: no ??)
            if (offset.HasValue) insp.Offset = offset.Value;
            insp.Target = null;
            if (figure == null) return "lab camera";
            string info;
            if (figure.StartsWith("R") || figure == "BR")
            {
                var id = figure == "BR" ? Runner.Batter : new Runner(figure == "R1" ? Base.First : figure == "R2" ? Base.Second : Base.Third);
                insp.Target = lab.Runners.Figure(id).transform;
                var r = lab.Live.RunnerOf(id);
                info = r == null ? "no such runner" : $"{id}: phase {r.Phase} speed {r.SpeedAt(start + t):0.0} leg {r.LegAt(start + t).From}→{r.LegAt(start + t).To} d {r.DistanceAlongAt(start + t):0.00}";
            }
            else
            {
                var p = (DefensivePosition)Enum.Parse(typeof(DefensivePosition), figure switch
                {
                    "1B" => "FirstBase", "2B" => "SecondBase", "3B" => "ThirdBase", "SS" => "Shortstop", "LF" => "LeftField", "CF" => "CenterField", "RF" => "RightField", _ => figure,
                });
                insp.Target = lab.Defense.Figure(p).transform;
                var d = lab.Team;
                string takes = string.Join(", ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(d.Takes, k => k.Fielder == p), k => $"{k.Action}@{k.Time - start:0.00}"));
                info = $"{p}: speed {d.FielderSpeedAt(p, start + t):0.0} takes [{takes}] holder {d.HolderAt(start + t)}";
            }

            return $"{scenario} +{t:0.00}: {info}";
        }

        private void OnDestroy() => _frozen = false;

        private void LateUpdate()
        {
            if (Target == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 look = Target.position + Vector3.up * LookHeight;
            cam.transform.SetPositionAndRotation(Target.position + Offset, Quaternion.LookRotation(look - (Target.position + Offset)));
            cam.fieldOfView = FieldOfView;
        }
    }
}
