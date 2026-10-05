using System.Collections;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Sandbox;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    /// <summary>
    /// Fielding Lab (TASK-005) on the production pipeline: the shown defenders follow gameplay, the glove meets the ball at
    /// the authoritative moment, the ball sits in the glove afterwards, nobody else chases, rendering does not depend on the
    /// frame cadence, and a replay starts clean.
    /// </summary>
    public class FieldingLabSceneTests
    {
        private FieldingLabController _lab;
        private double _now;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("FieldingLab", LoadSceneMode.Single);
            yield return null;
            _lab = Object.FindFirstObjectByType<FieldingLabController>();
            Assert.IsNotNull(_lab);
            _now = 1000.0;
            _lab.Clock = () => _now;
            Assert.AreEqual(0, _lab.Defense.GetComponentsInChildren<Collider>(true).Length, "defenders never collide");
        }

        private void At(double playSeconds)
        {
            _now = _launch + playSeconds;
            _lab.FrameUpdate();
        }

        private double _launch;

        /// <summary>Ground-plane position (figures stand on the mound's surface, which presentation adds).</summary>
        private static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        private FieldingPlay Launch(int preset)
        {
            _now += 100.0;
            _launch = _now;
            _lab.Launch(preset);
            return _lab.Fielding;
        }

        [UnityTest]
        public IEnumerator GloveMeetsTheBallAtTheAuthoritativeMomentForEveryPreset()
        {
            // Ground pickups ≤ 10 cm, catches ≤ 20 cm between the glove and the ball centre (the glove pocket is ~10 cm).
            yield return null;
            var log = new System.Text.StringBuilder();
            for (int k = 0; k < FieldingLabController.Presets.Length; k++)
            {
                FieldingPlay f = Launch(k);
                Assert.AreEqual(FieldingOutcome.Fielded, f.Outcome, $"{FieldingLabController.Presets[k].Name} is fielded");
                DefensivePosition p = f.Primary.Value;
                // Before the take the shown ball is still the free trajectory.
                At(f.Intercept.Time - f.Ball.First.Time - 0.05);
                Assert.Less(Vector3.Distance(_lab.Ball.position, SimulationSpace.ToUnity(f.Ball.StateAt(f.Intercept.Time - 0.05).Position)), 1e-4f, "free before possession");
                double t = f.Intercept.Time - f.Ball.First.Time;
                At(t - 1e-4);
                float d = Vector3.Distance(_lab.Defense.Figure(p).GloveAnchor.position, SimulationSpace.ToUnity(f.Intercept.Ball.Position));
                log.AppendLine($"{FieldingLabController.Presets[k].Name}: {p} {f.Intercept.Kind} glove→ball {d:0.000} m");
                Assert.Less(d, f.Intercept.Kind == InterceptKind.GroundPickup ? 0.10f : 0.20f, $"{FieldingLabController.Presets[k].Name}");
                // At the take the shown ball is exactly the gameplay ball (no jump); once secured it sits in the glove.
                At(t + 1e-5);
                Assert.Less(Vector3.Distance(_lab.Ball.position, SimulationSpace.ToUnity(f.BallPositionAt(f.Intercept.Time + 1e-5))), 2e-3f, "no jump at the take");
                At(t + 0.5);
                Assert.Less(Vector3.Distance(_lab.Ball.position, _lab.Defense.Figure(p).GloveAnchor.position), 1e-4f, "held in the glove");
            }

            TestContext.WriteLine(log.ToString());
        }

        [UnityTest]
        public IEnumerator OnlyThePrimaryLeavesHisPosition()
        {
            yield return null;
            FieldingPlay f = Launch(0);
            At(f.Intercept.Time - f.Ball.First.Time);
            foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition)))
            {
                Vector3 shown = Flat(_lab.Defense.Figure(p).transform.position), start = SimulationSpace.ToUnity(DefensiveAlignment.Standard[p]);
                if (p == f.Primary) Assert.Greater(Vector3.Distance(shown, start), 2f, "the primary ran");
                else Assert.Less(Vector3.Distance(shown, start), 1e-4f, $"{p} held");
            }
        }

        [UnityTest]
        public IEnumerator ShownPlayIsIndependentOfFrameCadence()
        {
            yield return null;
            var results = new System.Collections.Generic.List<(Vector3 Ball, Vector3 Fielder, Vector3 Glove)>();
            foreach (double step in new[] { 0.0, 1.0 / 30.0, 1.0 / 144.0 })
            {
                FieldingPlay f = Launch(5);
                double target = f.Intercept.Time - f.Ball.First.Time + 0.3;
                if (step > 0.0)
                    for (double t = 0.0; t < target; t += step) At(t);
                At(target);
                Transform fielder = _lab.Defense.Figure(f.Primary.Value).transform;
                results.Add((_lab.Ball.position, fielder.position, _lab.Defense.Figure(f.Primary.Value).GloveAnchor.position));
            }

            for (int i = 1; i < results.Count; i++)
            {
                Assert.Less(Vector3.Distance(results[0].Ball, results[i].Ball), 1e-4f, $"ball, cadence {i}");
                Assert.Less(Vector3.Distance(results[0].Fielder, results[i].Fielder), 1e-4f, $"fielder, cadence {i}");
                Assert.Less(Vector3.Distance(results[0].Glove, results[i].Glove), 1e-4f, $"glove, cadence {i}");
            }
        }

        [UnityTest]
        public IEnumerator ReplayStartsClean()
        {
            yield return null;
            FieldingPlay first = Launch(0);
            At(first.Intercept.Time - first.Ball.First.Time + 1.0);
            FieldingPlay again = Launch(0);
            Assert.AreNotSame(first, again);
            At(0.0);
            Assert.AreEqual(BallAuthority.FreeBall, again.AuthorityAt(again.Ball.First.Time));
            Vector3 contact = SimulationSpace.ToUnity(FieldingLabController.ContactPoint);
            Assert.Less(Vector3.Distance(_lab.Ball.position, contact), 1e-3f, "ball back at the plate");
            foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition)))
                Assert.Less(Vector3.Distance(Flat(_lab.Defense.Figure(p).transform.position), SimulationSpace.ToUnity(DefensiveAlignment.Standard[p])), 1e-4f, $"{p} back at his start");
        }
    }
}
