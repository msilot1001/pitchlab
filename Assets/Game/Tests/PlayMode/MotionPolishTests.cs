using System.Collections;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Presentation;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-011.5–011.8 objective motion invariants (FieldingLab, frozen clock): finite poses, no visual ball jumps, planted
    /// feet, no skating while sprinting, slides arriving at the bag at the authoritative time, the run through first, the tag
    /// and the safe call explained by the figures, and poses independent of the frame schedule. No pixel tests.
    /// </summary>
    public class MotionPolishTests
    {
        private FieldingLabController _lab;
        private double _now, _launch;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("FieldingLab", LoadSceneMode.Single);
            yield return null;
            _lab = Object.FindFirstObjectByType<FieldingLabController>();
            _now = 1000.0;
            _lab.Clock = () => _now;
        }

        private FieldingPlay Launch(string preset)
        {
            _now += 100.0;
            _launch = _now;
            _lab.Launch(FieldingLabController.IndexOf(preset));
            return _lab.Fielding;
        }

        /// <summary>Renders play time <paramref name="t"/> (seconds after contact).</summary>
        private void At(double t)
        {
            _now = _launch + t;
            _lab.FrameUpdate();
        }

        private double Start => _lab.Play.First.Time;
        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private System.Collections.Generic.IEnumerable<PlayerMannequin> Figures()
        {
            foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition))) yield return _lab.Defense.Figure(p);
            foreach (LiveRunner r in _lab.Live.Runners) yield return _lab.Runners.Figure(r.Id);
        }

        [UnityTest]
        public IEnumerator EveryPoseIsFinite()
        {
            yield return null;
            foreach (FieldingLabController.Scenario sc in FieldingLabController.Presets)
            {
                Launch(sc.Name);
                for (double t = 0.0; t < _lab.Live.EndTime - Start + 1.0; t += 0.1)
                {
                    At(t);
                    foreach (PlayerMannequin m in Figures())
                        foreach (Transform j in m.GetComponentsInChildren<Transform>())
                        {
                            Vector3 p = j.position;
                            Assert.IsTrue(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z), $"{sc.Name}: {m.name}/{j.name} at +{t:0.0}");
                            Assert.Greater(p.y, -0.05f, $"{sc.Name}: {m.name}/{j.name} under the ground at +{t:0.0}");
                        }
                }
            }
        }

        [UnityTest]
        public IEnumerator TheShownBallNeverJumps()
        {
            // At 120 Hz the visual ball moves at most as far as a hard-hit ball flies in a tick, plus a little: through every
            // catch, pickup, transfer, release and receiver catch (TASK-011.8 ball ↔ glove continuity).
            yield return null;
            var worst = new System.Text.StringBuilder();
            foreach (FieldingLabController.Scenario sc in FieldingLabController.Presets)
            {
                Launch(sc.Name);
                const double dt = 1.0 / 120.0;
                At(0.0);
                Vector3 last = _lab.Ball.position;
                float max = 0f;
                double at = 0.0;
                for (double t = dt; t < _lab.Live.EndTime - Start; t += dt)
                {
                    At(t);
                    float step = Vector3.Distance(last, _lab.Ball.position);
                    if (step > max) (max, at) = (step, t);
                    last = _lab.Ball.position;
                }

                worst.AppendLine($"{sc.Name}: largest step {max:0.000} m at +{at:0.00}");
                Assert.Less(max, 50f * (float)dt + 0.05f, $"{sc.Name}: the ball jumped at +{at:0.00}");
            }

            TestContext.WriteLine(worst.ToString());
        }

        [UnityTest]
        public IEnumerator FeetOfAStandingFielderStayPlanted()
        {
            // A defender who is not moving and not turning keeps both feet where they are (no drift).
            yield return null;
            Launch("SS routine grounder: out at 1B");
            int checkedFeet = 0;
            foreach (DefensivePosition p in new[] { DefensivePosition.LeftField, DefensivePosition.ThirdBase, DefensivePosition.C })
                for (double t = 0.0; t < _lab.Live.EndTime - Start - 0.25; t += 0.25)
                {
                    LiveDefense d = _lab.Team;
                    if (d.FielderSpeedAt(p, Start + t) > 1e-3 || d.FielderSpeedAt(p, Start + t + 0.2) > 1e-3) continue;
                    if (d.Takes.Any(k => k.Fielder == p && System.Math.Abs(k.Time - (Start + t)) < 1.5)) continue;
                    PlayerMannequin m = _lab.Defense.Figure(p);
                    At(t);
                    Vector3 l0 = m.Joint(MannequinJoint.LeftFoot).position, r0 = m.Joint(MannequinJoint.RightFoot).position;
                    float yaw0 = m.transform.eulerAngles.y;
                    At(t + 0.2);
                    if (Mathf.Abs(Mathf.DeltaAngle(yaw0, m.transform.eulerAngles.y)) > 1f) continue;   // turning on the spot
                    Assert.Less(Vector3.Distance(l0, m.Joint(MannequinJoint.LeftFoot).position), 0.02f, $"{p} left foot at +{t:0.00}");
                    Assert.Less(Vector3.Distance(r0, m.Joint(MannequinJoint.RightFoot).position), 0.02f, $"{p} right foot at +{t:0.00}");
                    checkedFeet++;
                }

            Assert.Greater(checkedFeet, 3);
        }

        [UnityTest]
        public IEnumerator ASprintersStanceFootDoesNotSkate()
        {
            // While sprinting the foot on the ground stays put on the ground (it moves back in the figure exactly as the body moves
            // forward): its ground speed is a small fraction of the body's.
            yield return null;
            FieldingPlay f = Launch("Runner on 1B, ball off the wall");
            DefensivePosition lf = f.Primary.Value;
            PlayerMannequin m = _lab.Defense.Figure(lf);
            const double dt = 1.0 / 240.0;
            int stance = 0;
            for (double t = 1.0; t < f.PossessionTime - Start - FieldingPoser.ActionLead - 0.1; t += 0.05)
            {
                double speed = _lab.Team.FielderSpeedAt(lf, Start + t);
                if (speed < 5.0) continue;
                foreach (MannequinJoint foot in new[] { MannequinJoint.LeftFoot, MannequinJoint.RightFoot })
                {
                    At(t);
                    Vector3 a = m.Joint(foot).position;
                    At(t + dt);
                    Vector3 b = m.Joint(foot).position;
                    if (a.y > PlayerMannequin.AnkleHeight + 0.01f || b.y > PlayerMannequin.AnkleHeight + 0.01f) continue;   // in the air
                    float footSpeed = Vector3.Distance(Flat(a), Flat(b)) / (float)dt;
                    Assert.Less(footSpeed, 0.15f * (float)speed, $"{foot} at +{t:0.00} (body {speed:0.0} m/s)");
                    stance++;
                }
            }

            Assert.Greater(stance, 5, "stance phases were sampled");
        }

        [UnityTest]
        public IEnumerator ASlideReachesTheBagAtTheAuthoritativeArrival()
        {
            // The runner from first slides into third (gameplay stops him there with the slide deceleration): he goes down when
            // the authoritative braking starts and his lead foot is at the bag when he arrives — the arrival time is gameplay's.
            yield return null;
            Launch("Runner on 1B, ball off the wall");
            LiveRunner r = _lab.Live.RunnerOf(new Runner(Base.First));
            var slide = r.Segments.Last(s => s.Leg.To == Base.Third && s.Motion.Sign > 0.0 && s.Motion.ArrivalTime > s.Motion.StartTime);   // the run, not the standing state after it
            Assert.AreEqual(_lab.Live.Profile.SlideDeceleration, slide.Motion.Deceleration, "a slide stop");
            PlayerMannequin m = _lab.Runners.Figure(r.Id);
            Vector3 bag = SimulationSpace.ToUnity(FieldLayout.BasePosition(Base.Third));
            At(slide.Motion.BrakeTime - Start - 0.15);
            float upright = m.Joint(MannequinJoint.Pelvis).position.y;
            Assert.Greater(upright, 0.75f, "running before the slide");
            At(slide.Motion.ArrivalTime - Start);
            Assert.Less(m.Joint(MannequinJoint.Pelvis).position.y, 0.55f, "down in the slide");
            float lead = Vector3.Distance(Flat(m.Joint(MannequinJoint.LeftFoot).position), Flat(bag));
            Assert.Less(lead, 0.45f, "the lead foot at the bag on arrival");
            TestContext.WriteLine($"slide: lead foot – bag {lead:0.00} m");
        }

        [UnityTest]
        public IEnumerator TheBatterRunnerRunsThroughFirst()
        {
            // No slide at first: he runs through the bag, upright, slows past it, then comes back.
            yield return null;
            Launch("1B ranges right: safe at 1B");
            LiveRunner b = _lab.Live.RunnerOf(Runner.Batter);
            PlayerMannequin m = _lab.Runners.Figure(Runner.Batter);
            double touch = _lab.Live.Log.First(e => e.Kind == PlayLogKind.BaseTouch && e.Runner == Runner.Batter && e.At == Base.First).Time;
            Vector3 bag = SimulationSpace.ToUnity(FieldLayout.BasePosition(Base.First));
            float furthest = 0f;
            for (double t = touch - Start; t < touch - Start + 2.5; t += 0.05)
            {
                At(t);
                Assert.Greater(m.Joint(MannequinJoint.Pelvis).position.y, 0.7f, $"upright at +{t:0.00} (no slide at first)");
                furthest = Mathf.Max(furthest, Vector3.Distance(Flat(m.transform.position), Flat(bag)));
            }

            Assert.Greater(furthest, 2.0f, "through the bag and beyond it");
            Assert.IsTrue(b.Segments.Any(s => s.Motion.Sign < 0.0), "and back");
        }

        [UnityTest]
        public IEnumerator TheTagAndTheSafeCallAreExplainedByTheFigures()
        {
            yield return null;
            // The tag: at the authoritative tag the glove (with the ball) is at the runner.
            Launch("Runner on 2B runs: tag at 3B");
            var tag = _lab.Team.Tags.Single();
            At(tag.Time - Start);
            PlayerMannequin fielder = _lab.Defense.Figure(tag.Fielder);
            PlayerMannequin runner = _lab.Runners.Figure(new Runner(Base.Second));
            float tagGap = new[] { MannequinJoint.LeftFoot, MannequinJoint.RightFoot, MannequinJoint.LeftShin, MannequinJoint.RightShin, MannequinJoint.Pelvis }
                .Min(j => Vector3.Distance(fielder.GloveAnchor.position, runner.Joint(j).position));
            Assert.Less(tagGap, 0.5f, "glove at the runner at the tag");
            // The safe call: when SAFE is called at first the runner's foot is at the bag.
            Launch("1B ranges right: safe at 1B");
            PlayLogEntry safe = _lab.Live.Log.First(e => e.Kind == PlayLogKind.Safe);
            At(safe.Time - Start);
            PlayerMannequin b = _lab.Runners.Figure(Runner.Batter);
            Vector3 bag = SimulationSpace.ToUnity(FieldLayout.BasePosition(Base.First));
            float footGap = Mathf.Min(Vector3.Distance(Flat(b.Joint(MannequinJoint.LeftFoot).position), Flat(bag)), Vector3.Distance(Flat(b.Joint(MannequinJoint.RightFoot).position), Flat(bag)));
            Assert.Less(footGap, 0.6f, "a foot at the bag at the call");
            TestContext.WriteLine($"tag glove–runner {tagGap:0.00} m, safe foot–bag {footGap:0.00} m");
        }

        [UnityTest]
        public IEnumerator PosesDoNotDependOnTheFrameSchedule()
        {
            // The gait phase and heading are sampled on a fixed grid (MotionTrack): the same moment looks the same whether it
            // was reached at 30 fps, 144 fps or in one jump.
            yield return null;
            const string preset = "Runner on 1B, ball off the wall";
            double end = 6.0;
            System.Collections.Generic.List<Vector3> Pose(double step)
            {
                Launch(preset);
                for (double t = 0.0; t < end; t += step) At(t);
                At(end);
                return Figures().SelectMany(m => m.GetComponentsInChildren<Transform>()).Select(j => j.position).ToList();
            }

            var a = Pose(1.0 / 30.0);
            var b = Pose(1.0 / 144.0);
            var c = Pose(end);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Less(Vector3.Distance(a[i], b[i]), 1e-3f, $"joint {i}: 30 vs 144 fps");
                Assert.Less(Vector3.Distance(a[i], c[i]), 1e-3f, $"joint {i}: 30 fps vs one jump");
            }
        }
    }
}
