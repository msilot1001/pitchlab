using System.Collections;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Field;
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
                // Held until the throwing arm action starts (TASK-006A), or for good without a throw.
                ThrowPlay th = _lab.DefensivePlay.Throw;
                At(th == null ? t + 0.5 : th.ReleaseTime - DefensivePlay.ArmAction - f.Ball.First.Time - 1e-3);
                Assert.Less(Vector3.Distance(_lab.Ball.position, _lab.Defense.Figure(p).GloveAnchor.position), 1e-4f, "held in the glove");
            }

            TestContext.WriteLine(log.ToString());
        }

        [UnityTest]
        public IEnumerator OnlyThePrimaryAndTheReceiverLeaveTheirPositions()
        {
            yield return null;
            FieldingPlay f = Launch(0);   // SS grounder, thrown to first: the first baseman covers the bag
            DefensivePosition receiver = _lab.DefensivePlay.Throw.Receiver;
            Assert.AreEqual(DefensivePosition.FirstBase, receiver);
            At(f.Intercept.Time - f.Ball.First.Time);
            Vector3 bag = Flat(SimulationSpace.ToUnity(FieldLayout.BasePosition(Base.First)));
            foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition)))
            {
                Vector3 shown = Flat(_lab.Defense.Figure(p).transform.position), start = SimulationSpace.ToUnity(DefensiveAlignment.Standard[p]);
                if (p == f.Primary) Assert.Greater(Vector3.Distance(shown, start), 2f, "the primary ran");
                else if (p == receiver) Assert.Less(Vector3.Distance(shown, bag), Vector3.Distance(Flat(start), bag) - 2f, "the receiver covers the bag");
                else Assert.Less(Vector3.Distance(shown, start), 1e-4f, $"{p} held");
            }

            // After the catch, still only those two have moved.
            At(_lab.DefensivePlay.Throw.Catch.Time - f.Ball.First.Time + 0.5);
            foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition)))
                if (p != f.Primary && p != receiver)
                    Assert.Less(Vector3.Distance(Flat(_lab.Defense.Figure(p).transform.position), SimulationSpace.ToUnity(DefensiveAlignment.Standard[p])), 1e-4f, $"{p} held after the catch");
        }

        [UnityTest]
        public IEnumerator BagsAreDrawnOnTheAuthoritativeBases()
        {
            yield return null;
            var dressing = Object.FindFirstObjectByType<Pitchlab.Presentation.FieldDressing>();
            foreach (var (name, b) in new[] { ("FirstBase", Base.First), ("SecondBase", Base.Second), ("ThirdBase", Base.Third) })
                Assert.Less(Vector3.Distance(Flat(dressing.transform.Find(name).position), Flat(SimulationSpace.ToUnity(FieldLayout.BasePosition(b)))), 1e-3f, name);
        }

        [UnityTest]
        public IEnumerator ThrowLeavesTheHandAtReleaseAndEndsInTheReceiversGlove()
        {
            // TASK-006A presentation follows gameplay: the ball is in the throwing hand up to the authoritative release, on
            // the authoritative flight after it, meets the receiver's glove at the authoritative catch and stays there.
            yield return null;
            var log = new System.Text.StringBuilder();
            for (int k = 0; k < FieldingLabController.Presets.Length; k++)
            {
                if (FieldingLabController.Presets[k].Throw == null) continue;
                FieldingPlay f = Launch(k);
                DefensivePlay d = _lab.DefensivePlay;
                ThrowPlay th = d.Throw;
                string name = FieldingLabController.Presets[k].Name;
                Assert.IsNotNull(th, name);
                double t0 = f.Ball.First.Time;
                PlayerMannequin thrower = _lab.Defense.Figure(th.Thrower), receiver = _lab.Defense.Figure(th.Receiver);

                At(th.ReleaseTime - t0 - 1e-4);
                Assert.Less(Vector3.Distance(_lab.Ball.position, SimulationSpace.ToUnity(d.BallPositionAt(th.ReleaseTime - 1e-4))), 1e-3f, $"{name}: gameplay ball at release");
                float hand = Vector3.Distance(_lab.Ball.position, thrower.RightHandAnchor.position);
                Assert.Less(hand, 0.15f, $"{name}: in the throwing hand at release");
                Assert.AreEqual(BallAuthority.Possessed, d.AuthorityAt(th.ReleaseTime - 1e-4));

                At(th.ReleaseTime - t0 + 0.05);
                Assert.Less(Vector3.Distance(_lab.Ball.position, SimulationSpace.ToUnity(th.Flight.StateAt(th.ReleaseTime + 0.05).Position)), 1e-4f, $"{name}: on the flight");
                Assert.AreSame(receiver.transform, _lab.Defense.Focus, $"{name}: camera focus on the receiver");

                Assert.IsTrue(th.Caught, $"{name}: every throwing preset is caught");
                At(th.Catch.Time - t0 - 1e-4);
                float glove = Vector3.Distance(receiver.GloveAnchor.position, SimulationSpace.ToUnity(th.Catch.Ball.Position));
                Assert.Less(glove, 0.20f, $"{name}: receiver's glove at the catch");
                At(th.Catch.Time - t0 + FieldingPlay.SecureTime + 0.1);
                Assert.Less(Vector3.Distance(_lab.Ball.position, receiver.GloveAnchor.position), 1e-4f, $"{name}: held by the receiver");
                log.AppendLine($"{name}: hand {hand:0.000} m, receiver glove {glove:0.000} m");
            }

            TestContext.WriteLine(log.ToString());
        }

        [UnityTest]
        public IEnumerator ShownPlayIsIndependentOfFrameCadence()
        {
            yield return null;
            var results = new System.Collections.Generic.List<(Vector3 Ball, Vector3 Fielder, Vector3 Glove)>();
            foreach (double step in new[] { 0.0, 1.0 / 30.0, 1.0 / 144.0 })
            foreach (int preset in new[] { 5, 10 })   // a fly ball; a throw (3B → 1B) sampled while the receiver secures it
            {
                FieldingPlay f = Launch(preset);
                ThrowPlay th = _lab.DefensivePlay.Throw;
                double target = (th != null ? th.Catch.Time : f.Intercept.Time) - f.Ball.First.Time + 0.15;
                if (step > 0.0)
                    for (double t = 0.0; t < target; t += step) At(t);
                At(target);
                PlayerMannequin shown = _lab.Defense.Figure(th?.Receiver ?? f.Primary.Value);
                results.Add((_lab.Ball.position, shown.transform.position, shown.GloveAnchor.position));
            }

            for (int i = 2; i < results.Count; i++)
            {
                Assert.Less(Vector3.Distance(results[i % 2].Ball, results[i].Ball), 1e-4f, $"ball, run {i}");
                Assert.Less(Vector3.Distance(results[i % 2].Fielder, results[i].Fielder), 1e-4f, $"fielder, run {i}");
                Assert.Less(Vector3.Distance(results[i % 2].Glove, results[i].Glove), 1e-4f, $"glove, run {i}");
            }
        }

        [UnityTest]
        public IEnumerator ReplayStartsClean()
        {
            yield return null;
            FieldingPlay first = Launch(0);
            At(_lab.DefensivePlay.Throw.Catch.Time - first.Ball.First.Time + 0.5);   // after the throw has been caught
            FieldingPlay again = Launch(0);
            Assert.AreNotSame(first, again);
            At(0.0);
            Assert.AreEqual(BallAuthority.FreeBall, _lab.DefensivePlay.AuthorityAt(again.Ball.First.Time));
            Vector3 contact = SimulationSpace.ToUnity(FieldingLabController.ContactPoint);
            Assert.Less(Vector3.Distance(_lab.Ball.position, contact), 1e-3f, "ball back at the plate");
            foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition)))
                Assert.Less(Vector3.Distance(Flat(_lab.Defense.Figure(p).transform.position), SimulationSpace.ToUnity(DefensiveAlignment.Standard[p])), 1e-4f, $"{p} back at his start");
        }
    }
}
