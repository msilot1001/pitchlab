using System.Collections;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
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
    /// frame cadence, and a replay starts clean; (TASK-006B) the rules scenarios show the right call at the authoritative
    /// moment, the unassisted put-out is a carry onto the bag with no throw, and every out is counted once.
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
                LiveThrow th = _lab.Team.Throws.FirstOrDefault();
                At(th == null ? t + 0.5 : th.ReleaseTime - DefensivePlay.ArmAction - f.Ball.First.Time - 1e-3);
                Assert.Less(Vector3.Distance(_lab.Ball.position, _lab.Defense.Figure(p).GloveAnchor.position), 1e-4f, "held in the glove");
            }

            TestContext.WriteLine(log.ToString());
        }

        [UnityTest]
        public IEnumerator FiguresFollowTheTeamDefense()
        {
            // TASK-008: every fielder moves to his role (cover, backup, …) — the shown figures are exactly the gameplay
            // defenders (the pitcher and catcher are taken over from the pitch presentation and are left out here).
            yield return null;
            FieldingPlay f = Launch(FieldingLabController.IndexOf("SS routine grounder: out at 1B"));
            Assert.AreEqual(DefensivePosition.FirstBase, _lab.Team.Throws[0].Receiver);
            double t0 = f.Ball.First.Time;
            foreach (double t in new[] { 0.5, f.Intercept.Time - t0, _lab.Team.Throws[0].Catch.Time - t0 + 0.5 })
            {
                At(t);
                foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition)))
                {
                    if (p == DefensivePosition.P || p == DefensivePosition.C) continue;
                    Vector3 shown = Flat(_lab.Defense.Figure(p).transform.position), gameplay = Flat(SimulationSpace.ToUnity(_lab.Team.FielderPositionAt(p, t0 + t)));
                    Assert.Less(Vector3.Distance(shown, gameplay), 1e-3f, $"{p} at +{t:0.00}");
                }
            }

            // And they did move: the first baseman to the bag, the right fielder behind it.
            Vector3 bag = Flat(SimulationSpace.ToUnity(FieldLayout.BasePosition(Base.First)));
            foreach (DefensivePosition p in new[] { DefensivePosition.FirstBase, DefensivePosition.RightField })
                Assert.Less(Vector3.Distance(Flat(_lab.Defense.Figure(p).transform.position), bag), Vector3.Distance(Flat(SimulationSpace.ToUnity(DefensiveAlignment.Standard[p])), bag) - 2f, $"{p} went toward first");
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
            int throws = 0;
            for (int k = 0; k < FieldingLabController.Presets.Length; k++)
            {
                FieldingPlay f = Launch(k);
                LiveDefense d = _lab.Team;
                LiveThrow th = d.Throws.FirstOrDefault();
                string name = FieldingLabController.Presets[k].Name;
                if (th == null) continue;   // no throw chosen (fly out, unassisted put-out, hold)
                throws++;
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
                // Held once secured — until he winds up for a throw of his own (the catcher's 5-2-3 relay starts at once).
                LiveThrow next = d.Throws.FirstOrDefault(x => x.Thrower == th.Receiver && x.ReleaseTime > th.Catch.Time);
                double held = th.Catch.Time + FieldingPlay.SecureTime + 0.1;
                if (next != null) held = System.Math.Min(held, System.Math.Max(next.ReleaseTime - DefensivePlay.ArmAction - 0.15, th.Catch.Time + FieldingPlay.SecureTime));
                At(held - t0);
                Assert.Less(Vector3.Distance(_lab.Ball.position, receiver.GloveAnchor.position), 1e-4f, $"{name}: held by the receiver");
                log.AppendLine($"{name}: hand {hand:0.000} m, receiver glove {glove:0.000} m");
            }

            Assert.GreaterOrEqual(throws, 12, "the decision throws on most presets");
            TestContext.WriteLine(log.ToString());
        }

        [UnityTest]
        public IEnumerator ShownPlayIsIndependentOfFrameCadence()
        {
            yield return null;
            var results = new System.Collections.Generic.List<(Vector3 Ball, Vector3 Fielder, Vector3 Glove)>();
            foreach (double step in new[] { 0.0, 1.0 / 30.0, 1.0 / 144.0 })
            foreach (string preset in new[] { "Routine fly: fly out", "3B grounder", "1B grounder: unassisted" })   // a catch; a throw (3B → 1B)
            {                                                                                                 // while the receiver secures it; a carry to the bag
                FieldingPlay f = Launch(FieldingLabController.IndexOf(preset));
                LiveThrow th = _lab.Team.Throws.FirstOrDefault();
                ContinuationMotion carry = _lab.Team.Decisions.FirstOrDefault()?.Chosen.Carry;
                double target = (th != null ? th.Catch.Time : carry != null ? carry.ArrivalTime - 0.1 : f.Intercept.Time) - f.Ball.First.Time + 0.15;
                if (step > 0.0)
                    for (double t = 0.0; t < target; t += step) At(t);
                At(target);
                PlayerMannequin shown = _lab.Defense.Figure(th?.Receiver ?? f.Primary.Value);
                results.Add((_lab.Ball.position, shown.transform.position, shown.GloveAnchor.position));
            }

            for (int i = 3; i < results.Count; i++)
            {
                Assert.Less(Vector3.Distance(results[i % 3].Ball, results[i].Ball), 1e-4f, $"ball, run {i}");
                Assert.Less(Vector3.Distance(results[i % 3].Fielder, results[i].Fielder), 1e-4f, $"fielder, run {i}");
                Assert.Less(Vector3.Distance(results[i % 3].Glove, results[i].Glove), 1e-4f, $"glove, run {i}");
            }
        }

        [UnityTest]
        public IEnumerator RulesScenariosShowTheCallAtTheAuthoritativeMoment()
        {
            yield return null;
            var expected = new (string Scenario, string Call)[]
            {
                ("SS routine grounder: out at 1B", "OUT AT 1B"),
                ("1B grounder: unassisted", "OUT AT 1B"),
                ("1B ranges right: safe at 1B", "SAFE AT 1B"),
                ("Runner on 1B, grounder to SS", "OUT AT 2B"),
                ("Runner on 1B, grounder to 2B", "OUT AT 2B"),     // the lead runner (TASK-008)
                ("Bases loaded, grounder to 3B", "OUT AT HOME"),   // the lead runner: 5-2
                ("Routine fly: fly out", "FLY OUT"),
                ("Runner on 2B runs: tag at 3B", "OUT AT 3B (tag)"),
            };
            foreach (var (scenario, call) in expected)
            {
                FieldingPlay f = Launch(FieldingLabController.IndexOf(scenario));
                PlayLogEntry e = _lab.Live.Log.First(x => x.Kind == PlayLogKind.Out || x.Kind == PlayLogKind.Safe || x.Kind == PlayLogKind.Run);
                double t0 = f.Ball.First.Time;
                At(e.Time - t0 - 1e-4);
                Assert.AreEqual("", _lab.ResultText, $"{scenario}: no call before the event");
                At(e.Time - t0 + 1e-4);
                Assert.AreEqual(call, _lab.ResultText, scenario);
                At(_lab.Live.EndTime - t0 + 5.0);
                int outs = call.StartsWith("SAFE") ? 0 : scenario == "Runner on 1B, grounder to SS" ? 2 : 1;   // that one is a 6-4-3
                Assert.AreEqual(outs, _lab.Live.OutsMade, $"{scenario}: outs counted once");
                Assert.AreEqual(_lab.Live.OutsMade, _lab.Live.Log.Count(x => x.Kind == PlayLogKind.Out), "one out entry per out");
            }
        }

        [UnityTest]
        public IEnumerator UnassistedPutOutCarriesTheBallOntoTheBag()
        {
            yield return null;
            FieldingPlay f = Launch(FieldingLabController.IndexOf("1B grounder: unassisted"));
            Assert.IsEmpty(_lab.Team.Throws, "no throw");
            Assert.AreEqual(LiveActionKind.Carry, _lab.Team.Decisions[0].Chosen.Kind, "he runs it to the bag");
            PlayLogEntry e = _lab.Live.Log.Single(x => x.Kind == PlayLogKind.Out);
            double t0 = f.Ball.First.Time;
            At(e.Time - t0);
            PlayerMannequin first = _lab.Defense.Figure(DefensivePosition.FirstBase);
            Vector3 bag = Flat(SimulationSpace.ToUnity(FieldLayout.BasePosition(Base.First)));
            Assert.LessOrEqual(Vector3.Distance(Flat(first.transform.position), bag), (float)BaseTouch.Radius + 1e-3f, "shown on the bag at the out");
            Assert.Less(Vector3.Distance(_lab.Ball.position, first.GloveAnchor.position), 1e-4f, "with the ball");
            Assert.AreSame(first.transform, _lab.Defense.Focus, "camera keeps the fielder");
            // Shown position follows the authoritative carry the whole way (no snap at the take).
            for (double t = f.PossessionTime - 0.2; t < e.Time + 0.5; t += 0.05)
            {
                At(t - t0);
                Vector3 gameplay = SimulationSpace.ToUnity(_lab.Team.FielderPositionAt(DefensivePosition.FirstBase, t));
                Assert.Less(Vector3.Distance(Flat(first.transform.position), Flat(gameplay)), 1e-4f, $"+{t - t0:0.00}");
            }
        }

        [UnityTest]
        public IEnumerator SwitchingScenarioDropsADebugOverride()
        {
            yield return null;
            int sc = FieldingLabController.IndexOf("SS routine grounder: out at 1B");
            _lab.UseOverride = true;
            _lab.TargetOverride = null;   // hold the ball
            Launch(sc);
            Assert.AreEqual("override", _lab.Team.Decisions[0].Reason);
            Assert.AreEqual(LiveActionKind.Hold, _lab.Team.Decisions[0].Chosen.Kind);
            _now += 100.0;
            _launch = _now;
            _lab.SelectScenario(sc);
            Assert.AreEqual("force out on the lead runner", _lab.Team.Decisions[0].Reason, "a new scenario is the defense's own decision");
            Assert.AreEqual(Base.First, _lab.Team.Decisions[0].Chosen.Target);
        }

        [UnityTest]
        public IEnumerator RunnerFiguresFollowTheGameplayRunners()
        {
            // TASK-007: every runner figure stands exactly on its gameplay runner's position (no presentation offset),
            // faces where he runs, disappears a moment after he is out or scores, and the result does not depend on the frame
            // cadence.
            yield return null;
            foreach (string name in new[] { "Runner on 1B, ball off the wall", "Runner on 3B, 1 out, deep fly: tag-up", "Bases loaded, grounder to 3B" })
            {
                FieldingPlay f = Launch(FieldingLabController.IndexOf(name));
                LivePlay live = _lab.Live;
                double t0 = f.Ball.First.Time;
                for (double t = t0; t < live.EndTime + 2.0; t += 0.1)
                {
                    At(t - t0);
                    foreach (LiveRunner r in live.Runners)
                    {
                        PlayerMannequin m = _lab.Runners.Figure(r.Id);
                        bool gone = r.IsOut && t > r.OutTime + RunnerView.LingerAfterOut || r.HasScored && t > r.ScoreTime + RunnerView.LingerAfterScore;
                        Assert.AreEqual(!gone, m.gameObject.activeSelf, $"{name}: {r.Id} shown at +{t - t0:0.0}");
                        if (gone) continue;
                        Assert.Less(Vector3.Distance(Flat(m.transform.position), Flat(SimulationSpace.ToUnity(r.PositionAt(t)))), 1e-4f, $"{name}: {r.Id} at +{t - t0:0.0}");
                        if (r.SpeedAt(t) > 1.0)
                            Assert.Greater(Vector3.Dot(m.transform.forward, SimulationSpace.ToUnity(r.HeadingAt(t)).normalized), 0.99f, "faces his run");
                    }
                }
            }

            // Frame cadence: the same runner pose at the same play time.
            var shown = new System.Collections.Generic.List<Vector3>();
            foreach (double step in new[] { 0.0, 1.0 / 30.0, 1.0 / 144.0 })
            {
                FieldingPlay f = Launch(FieldingLabController.IndexOf("Runner on 1B, ball off the wall"));
                double target = 6.0;
                if (step > 0.0)
                    for (double t = 0.0; t < target; t += step) At(t);
                At(target);
                shown.Add(_lab.Runners.Figure(new Runner(Base.First)).transform.position);
            }

            Assert.Less(Vector3.Distance(shown[0], shown[1]), 1e-4f);
            Assert.Less(Vector3.Distance(shown[0], shown[2]), 1e-4f);
        }

        [UnityTest]
        public IEnumerator ReplayStartsClean()
        {
            yield return null;
            int sc = FieldingLabController.IndexOf("SS routine grounder: out at 1B");
            FieldingPlay first = Launch(sc);
            At(_lab.Team.Throws[0].Catch.Time - first.Ball.First.Time + 0.5);   // after the throw has been caught
            Assert.AreEqual("OUT AT 1B", _lab.ResultText);
            LivePlay rules = _lab.Live;
            FieldingPlay again = Launch(sc);
            Assert.AreNotSame(first, again);
            Assert.AreNotSame(rules, _lab.Live, "a new play has its own state");
            At(0.0);
            Assert.AreEqual("", _lab.ResultText, "no call carried over");
            Assert.AreEqual(0, _lab.Live.Log.Count(x => x.Kind == PlayLogKind.Out && x.Time <= again.Ball.First.Time));
            Assert.AreEqual(BallAuthority.FreeBall, _lab.Team.AuthorityAt(again.Ball.First.Time));
            Vector3 contact = SimulationSpace.ToUnity(FieldingLabController.ContactPoint);
            Assert.Less(Vector3.Distance(_lab.Ball.position, contact), 1e-3f, "ball back at the plate");
            foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition)))
                Assert.Less(Vector3.Distance(Flat(_lab.Defense.Figure(p).transform.position), SimulationSpace.ToUnity(DefensiveAlignment.Standard[p])), 1e-4f, $"{p} back at his start");
        }
    }
}
