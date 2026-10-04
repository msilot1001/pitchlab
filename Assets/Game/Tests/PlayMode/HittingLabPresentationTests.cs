using System.Collections;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Presentation;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using Pitchlab.Simulation.Pitching;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-004.6 wiring: the presentation follows authoritative gameplay. Runs the HittingLab scene on a frozen,
    /// test-controlled clock so every frame shows an exact moment.
    /// </summary>
    public class HittingLabPresentationTests
    {
        private HittingLabController _lab;
        private HittingLabPresentation _view;
        private LandingMarker _landing;
        private BaseballCamera _camera;
        private double _now;
        private double _release;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("HittingLab", LoadSceneMode.Single);
            yield return null;
            _lab = Object.FindFirstObjectByType<HittingLabController>();
            _view = Object.FindFirstObjectByType<HittingLabPresentation>();
            Assert.IsNotNull(_view, "HittingLab scene must contain the presentation");
            _landing = Object.FindFirstObjectByType<LandingMarker>();
            _camera = Object.FindFirstObjectByType<BaseballCamera>();
            Assert.AreEqual(0, _view.GetComponentsInChildren<Collider>(true).Length, "presentation objects never collide");
            Assert.AreEqual(0, _lab.BallTransform.GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(0, _lab.BallTransform.GetComponentsInChildren<Rigidbody>(true).Length, "no Rigidbody: the simulation is authoritative");
            bool right = _lab.Swing.Side == BatterSide.Right;
            Assert.AreEqual(!right, _view.Batter.LeftHanded);
            Assert.AreEqual(right, _view.Batter.transform.position.x < 0f, "right-handed batter on the third-base side");
            _now = Time.realtimeSinceStartupAsDouble + 100.0;
            _lab.Clock = () => _now;
            _lab.PressSwingButton(_now);                  // throw: the delivery starts, release DeliveryLead later
            _release = _now + _lab.DeliveryLead;
        }

        /// <summary>
        /// Renders the moment <paramref name="simTime"/> after release synchronously: controller, then presentation. (A
        /// nested coroutine would resume after the next frame's Update, before LateUpdate.)
        /// </summary>
        private void At(double simTime)
        {
            _now = _release + simTime;
            _lab.FrameUpdate(_now);
            _view.FrameUpdate();
        }

        private Vector3 PitchSample => SimulationSpace.ToUnity(_lab.CurrentPitch.Flight.StateAt(_lab.RenderedSimTime).Position);

        [UnityTest]
        public IEnumerator BallStaysInThePitchersHandUntilTheAuthoritativeRelease()
        {
            yield return null;
            Assert.Greater(_lab.DeliveryLead, 0.5, "the delivery needs time for a wind-up");
            At(-0.4);
            Assert.Less(Vector3.Distance(_view.Pitcher.BallAnchor.position, _lab.BallTransform.position), 1e-4f, "in the hand");
            Assert.IsFalse(_view.BallTrail.emitting);
            At(0.2);
            Assert.Less(Vector3.Distance(PitchSample, _lab.BallTransform.position), 1e-4f, "on the authoritative pitch trajectory");
            Assert.IsTrue(_view.BallTrail.emitting);
        }

        [UnityTest]
        public IEnumerator PitcherHandMeetsTheSimulatedReleasePoint()
        {
            yield return null;
            // Presentation adapts to the simulation: at release the throwing hand is near the simulated release point.
            At(0.0);
            Vector3 release = SimulationSpace.ToUnity(_lab.CurrentPitch.Flight.First.Position);
            Assert.Less(Vector3.Distance(release, _view.Pitcher.BallAnchor.position), 0.05f, "no clamping needed for the presets");
        }

        [UnityTest]
        public IEnumerator ContactStartsTheBattedBallPresentationAndItLands()
        {
            yield return null;
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z - 0.015);
            ContactResult result = _lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration);
            Assert.IsTrue(result.IsContact);
            At(pitch.IdealContactTime - 0.01);
            Assert.IsFalse(_view.ContactShown, "nothing shown before the authoritative contact time");
            At(pitch.IdealContactTime);
            Transform sweetSpot = null;
            foreach (Transform t in _view.Batter.GetComponentsInChildren<Transform>()) if (t.name == "SweetSpot") sweetSpot = t;
            Assert.Less(Vector3.Distance(SimulationSpace.ToUnity(result.BattedBall.Position), sweetSpot.position), 0.03f, "visual bat on the contact point at contact");
            At(pitch.IdealContactTime + 0.5);
            Assert.IsTrue(_view.ContactShown);
            Vector3 batted = SimulationSpace.ToUnity(_lab.LastBattedBall.Flight.StateAt(_lab.RenderedSimTime).Position);
            Assert.Less(Vector3.Distance(batted, _lab.BallTransform.position), 1e-4f, "ball on the batted-ball trajectory");
            Assert.IsTrue(_camera.IsFollowing);
            Assert.IsFalse(_landing.Visible);
            BallEvent landing = _lab.LastPlay.FirstGroundContact.Value;
            At(landing.Time + 0.1);
            Assert.IsTrue(_landing.Visible);
            Vector3 landed = SimulationSpace.ToUnity(landing.Before.Position);
            Assert.Less(new Vector2(landed.x - _landing.transform.position.x, landed.z - _landing.transform.position.z).magnitude, 0.05f, "marker where the ball first landed");
            Assert.Less(Vector3.Distance(SimulationSpace.ToUnity(_lab.LastPlay.StateAt(_lab.RenderedSimTime).Position), _lab.BallTransform.position), 1e-4f, "bouncing on the ball-in-play trajectory");
            At(_lab.LastPlay.EndTime + 1.0);
            Assert.Less(Vector3.Distance(SimulationSpace.ToUnity(_lab.LastPlay.Final.Position), _lab.BallTransform.position), 1e-4f, "rests where the play ended");
            Assert.IsTrue(_camera.IsFollowing, "the camera stays on the ball to rest");
            Assert.IsFalse(_view.BallTrail.emitting, "no trail once at rest");

            _lab.PressSwingButton(_now);                  // next pitch: presentation resets
            _release = _now + _lab.DeliveryLead;
            At(-_lab.DeliveryLead);
            Assert.IsFalse(_landing.Visible);
            Assert.IsFalse(_view.ContactShown);
            Assert.IsFalse(_view.BallTrail.emitting);
            Assert.IsFalse(_camera.IsFollowing, "back to the batting view");
        }

        [UnityTest]
        public IEnumerator MissShowsNoBattedBall()
        {
            yield return null;
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z - 0.2);
            ContactResult result = _lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration);
            Assert.IsFalse(result.IsContact);
            At(pitch.IdealContactTime + 0.3);
            Assert.IsFalse(_view.ContactShown);
            Assert.IsFalse(_view.LandingShown);
            Assert.IsNull(_lab.LastBattedBall);
            Assert.Less(Vector3.Distance(PitchSample, _lab.BallTransform.position), 1e-4f, "the pitch carries on");
            At(_lab.CurrentPitch.Flight.Duration + 3.0);
            Assert.IsFalse(_landing.Visible);
            Assert.IsFalse(_camera.IsFollowing);
            Assert.Less(Vector3.Distance(SimulationSpace.ToUnity(_lab.CurrentPitch.Flight.Final.Position), _lab.BallTransform.position), 1e-4f, "rests where the pitch ended");
        }

        private Transform SweetSpot()
        {
            foreach (Transform t in _view.Batter.GetComponentsInChildren<Transform>()) if (t.name == "SweetSpot") return t;
            return null;
        }

        /// <summary>Where gameplay puts the bat's sweet spot at contact time (the visual target), Unity frame.</summary>
        private Vector3 GameplaySweetSpot() =>
            SimulationSpace.ToUnity(ContactResolver.SweetSpotAtContact(_lab.CurrentPitch, _lab.LastSwing.Value, _lab.Swing));

        [UnityTest]
        public IEnumerator VisualBatMeetsTheBallForEveryPresetAndTiming()
        {
            // Pitch locations differ per preset (high/low, inside/outside); timing across the whole hit window (±35 ms)
            // moves contact out front or deep (≈ 1.4 m at the edge) and turns the body. Within ±10 ms the visual sweet spot
            // is on gameplay's; beyond, contact is out of a planted batter's reach (Docs/MOTION_REFERENCE.md) and the bounds
            // are the measured values plus margin (EditMode TargetsBeyondReach checks the relative closure).
            yield return null;
            var log = new System.Text.StringBuilder();
            var failures = new System.Text.StringBuilder();
            for (int preset = 0; preset < PitchPresets.All.Length; preset++)
                foreach (double timing in new[] { -0.034, -0.02, -0.01, 0.0, 0.01, 0.02, 0.034 })
                {
                    _release = _now + _lab.DeliveryLead;
                    _lab.ThrowPitch(preset, _release);
                    At(-0.2);
                    HittingPitch pitch = _lab.CurrentPitch;
                    var ball = pitch.Flight.StateAt(pitch.IdealContactTime + timing).Position;
                    _lab.SetPci(ball.X, ball.Z);
                    double start = pitch.IdealContactTime - _lab.Swing.SwingDuration + timing;
                    ContactResult result = _lab.SwingAtSimTime(start);
                    At(start + _lab.Swing.SwingDuration);
                    float error = Vector3.Distance(GameplaySweetSpot(), SweetSpot().position);
                    log.AppendLine($"{PitchPresets.All[preset].Label} {timing * 1000:+0;-0} ms {result.Outcome}: visual error {error * 100f:0.0} cm");
                    float bound = System.Math.Abs(timing) <= 0.0101 ? 0.05f : System.Math.Abs(timing) <= 0.0201 ? 0.2f : 0.9f;
                    if (error > bound) failures.AppendLine($"preset {preset}, timing {timing}: {error:0.000} > {bound}");
                    At(start + _lab.Swing.SwingDuration + 5.0);
                    _now += 1.0;
                }

            TestContext.WriteLine(log.ToString());
            Assert.IsEmpty(failures.ToString(), "visual bat on gameplay's sweet spot");
        }

        [UnityTest]
        public IEnumerator OffBarrelHitShowsTheOffset()
        {
            // A hit 8 cm toward the barrel's end: the sweet spot is shown 8 cm from the ball, not on it.
            yield return null;
            HittingPitch pitch = _lab.CurrentPitch;
            var ball = pitch.IdealContactState.Position;
            _lab.SetPci(ball.X - 0.08, ball.Z);
            double start = pitch.IdealContactTime - _lab.Swing.SwingDuration;
            ContactResult result = _lab.SwingAtSimTime(start);
            Assert.IsTrue(result.IsContact);
            At(start + _lab.Swing.SwingDuration);
            Vector3 sweet = SweetSpot().position;
            Assert.Less(Vector3.Distance(GameplaySweetSpot(), sweet), 0.05f, "visual sweet spot where gameplay's was");
            Assert.Greater(Vector3.Distance(SimulationSpace.ToUnity(result.BattedBall.Position), sweet), 0.05f, "not snapped onto the ball");
        }

        [UnityTest]
        public IEnumerator MissAimsWhereThePlayerAimedNotAtTheBall([Values(-0.02, 0.0, 0.02)] double timing)
        {
            yield return null;
            HittingPitch pitch = _lab.CurrentPitch;
            var ball = pitch.Flight.StateAt(pitch.IdealContactTime + timing).Position;
            _lab.SetPci(ball.X + 0.12, ball.Z - 0.35);  // under and outside (out front the attack angle raises the bat ~0.14 m)
            double start = pitch.IdealContactTime - _lab.Swing.SwingDuration + timing;
            ContactResult result = _lab.SwingAtSimTime(start);
            Assert.IsFalse(result.IsContact);
            At(start + _lab.Swing.SwingDuration);
            Vector3 sweet = SweetSpot().position;
            Vector3 ballAtContact = SimulationSpace.ToUnity(ball);
            // On time the bat reaches the aim; ±20 ms out front / deep it approaches it (beyond reach, as for hits).
            Vector3 aim = GameplaySweetSpot();
            Assert.Less(Vector3.Distance(aim, sweet), timing == 0.0 ? 0.05f : 0.3f, "bat where the player aimed (at the ball's depth)");
            Assert.Less(Vector3.Distance(aim, sweet), Vector3.Distance(ballAtContact, sweet), "nearer the aim than the ball");
            Assert.Greater(Vector3.Distance(ballAtContact, sweet), 0.15f, "not snapped to the ball");
            Assert.Less(sweet.y, ballAtContact.y - 0.1f, "visibly under it");
        }

        /// <summary>Hit the current pitch flush (same PCI and swing time every call) and return the contact time.</summary>
        private double HitFlush()
        {
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z - 0.015);
            double start = pitch.IdealContactTime - _lab.Swing.SwingDuration;
            Assert.IsTrue(_lab.SwingAtSimTime(start).IsContact);
            return start + _lab.Swing.SwingDuration;
        }

        [UnityTest]
        public IEnumerator BallInPlayRenderingIsIndependentOfFrameCadence()
        {
            // The same hit shown by jumping to T, or by rendering every 1/30 s or 1/240 s up to T: identical ball, landing
            // marker and phase flags (one-way presentation flags must not depend on which frames happened).
            yield return null;
            var results = new System.Collections.Generic.List<(Vector3 Ball, bool Landed, Vector3 Marker, string Banner)>();
            foreach (double step in new[] { 0.0, 1.0 / 30.0, 1.0 / 240.0 })
            {
                _release = _now + _lab.DeliveryLead;
                _lab.ThrowPitch(0, _release);
                At(-0.2);
                double contact = HitFlush();
                BallInPlay play = _lab.LastPlay;
                // No real-time leak: the shown play is exactly the simulation of the authoritative batted ball.
                BallInPlay fresh = BallInPlaySimulation.Run(_lab.LastResult.Value.BattedBall, _lab.Environment, HittingLabController.Field);
                Assert.AreEqual(fresh.EndTime, play.EndTime, 0.0);
                Assert.AreEqual(fresh.Final.Position, play.Final.Position);
                double target = play.FirstGroundContact.Value.Time + 0.3;
                if (step > 0.0)
                    for (double t = contact; t < target; t += step) At(t);
                At(target);
                results.Add((_lab.BallTransform.position, _landing.Visible, _landing.transform.position, _view.Banner));
                Assert.AreEqual(_view.Feedback(_lab.RenderedSimTime), _view.Banner, "the shown banner is the line for this time");
                _now += 30.0;
            }

            for (int i = 1; i < results.Count; i++)
            {
                Assert.Less(Vector3.Distance(results[0].Ball, results[i].Ball), 1e-5f, $"ball, cadence {i}");
                Assert.AreEqual(results[0].Landed, results[i].Landed, $"landing shown, cadence {i}");
                Assert.Less(Vector3.Distance(results[0].Marker, results[i].Marker), 1e-5f, $"marker, cadence {i}");
                Assert.AreEqual(results[0].Banner, results[i].Banner, $"banner, cadence {i}");
            }
        }

        [UnityTest]
        public IEnumerator OutfieldWallAndTrackAreDrawnFromTheSimulationLayout()
        {
            yield return null;
            FieldLayout field = HittingLabController.Field;
            MeshFilter wall = null, track = null;
            foreach (MeshFilter f in _view.GetComponentsInChildren<MeshFilter>())
            {
                if (f.name == "OutfieldWall") wall = f;
                if (f.name == "WarningTrack") track = f;
            }

            Assert.IsNotNull(wall);
            Assert.IsNotNull(track);
            Assert.AreEqual((float)field.WallHeight, wall.sharedMesh.bounds.max.y, 1e-4f, "wall height");
            var vertices = new System.Collections.Generic.List<Vector3>(wall.sharedMesh.vertices);
            for (int i = 0; i < FieldLayout.FencePointCount; i++)
            {
                Vector3 p = SimulationSpace.ToUnity(field.FencePoint(i));
                Assert.IsTrue(vertices.Exists(v => Vector3.Distance(_view.transform.TransformPoint(v), p) < 1e-3f), $"fence point {i} on the wall's base");
            }

            // Every track vertex lies in the band the simulation calls warning track: from the fence face inward by its width
            // (1 m slack for the quads' overlap at the alley corners, where segments meet at an angle).
            foreach (Vector3 v in track.sharedMesh.vertices)
            {
                Vector3 w = _view.transform.TransformPoint(v);
                double beyond = field.DistanceBeyondFence(w.x, w.z, out _);
                Assert.That(beyond, Is.InRange(-FieldLayout.WarningTrackWidth - 1.0, 0.01), $"track vertex {w}");
            }
        }

        [UnityTest]
        public IEnumerator FeedbackBuildsWithThePlayAndReturnsToReady()
        {
            yield return null;
            double contact = HitFlush();
            BallInPlay play = _lab.LastPlay;
            FairFoulResult call = _lab.LastCall.Value;
            At(contact - 0.01);
            Assert.AreEqual(string.Empty, _view.Feedback(_lab.RenderedSimTime), "nothing before contact");
            At(contact + 0.001);
            StringAssert.Contains("mph", _view.Banner);
            StringAssert.Contains("on time", _view.Banner);
            StringAssert.Contains("sweet spot", _view.Banner);
            if (call.At.Time > contact + 0.002)
            {
                At(call.At.Time - 0.001);
                Assert.IsFalse(_view.Banner.StartsWith("FAIR") || _view.Banner.StartsWith("FOUL") || _view.Banner.StartsWith("HOME RUN"), "no call before its moment");
            }

            At(play.FirstGroundContact.Value.Time - 0.001);
            StringAssert.DoesNotEndWith("ft", _view.Banner, "no carry before the first bounce");
            At(System.Math.Max(call.At.Time, contact) + 0.001);
            string called = call.Call == BallInPlayCall.HomeRun ? "HOME RUN" : call.Call == BallInPlayCall.Fair ? "FAIR" : "FOUL";
            StringAssert.StartsWith(called, _view.Feedback(_lab.RenderedSimTime));
            At(play.FirstGroundContact.Value.Time + 0.001);
            StringAssert.EndsWith($"{Units.MetersToFeet(_lab.ShownCarry):0} ft", _view.Feedback(_lab.RenderedSimTime));
            At(play.EndTime + 0.01);
            if (play.EndPhase == BallPhase.Rest) StringAssert.EndsWith($"(rests {Units.MetersToFeet(play.FinalDistance):0} ft)", _view.Banner);
            Assert.AreEqual(BattingState.Result, _lab.StateAt(_now));
            Assert.IsTrue(_camera.IsFollowing);
            At(play.EndTime + BattingStateMachine.ResultPause + 0.01);
            Assert.AreEqual(BattingState.Ready, _lab.StateAt(_now));
            Assert.AreEqual("Click to pitch", _view.Feedback(_lab.RenderedSimTime));
            Assert.IsFalse(_camera.IsFollowing, "back to the batting view when ready");
        }

        [UnityTest]
        public IEnumerator TakeAndMissReadAfterThePitch()
        {
            yield return null;
            HittingPitch pitch = _lab.CurrentPitch;
            At(pitch.Flight.Duration - 0.01);
            Assert.AreEqual(string.Empty, _view.Feedback(_lab.RenderedSimTime));
            At(pitch.Flight.Duration + 0.01);
            Assert.AreEqual("Take", _view.Feedback(_lab.RenderedSimTime));

            _now += 5.0;
            _release = _now + _lab.DeliveryLead;
            _lab.ThrowPitch(0, _release);
            At(-0.2);
            pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z - 0.3);
            double start = pitch.IdealContactTime - _lab.Swing.SwingDuration - 0.012;
            Assert.IsFalse(_lab.SwingAtSimTime(start).IsContact);
            At(start + _lab.Swing.SwingDuration + 0.001);
            Assert.AreEqual("Swing and miss · early 12 ms", _view.Feedback(_lab.RenderedSimTime));
        }

        [UnityTest]
        public IEnumerator DebugMarksEveryEventReachedOnlyInDebugView()
        {
            yield return null;
            HitFlush();
            BallInPlay play = _lab.LastPlay;
            double t = play.EndTime + 0.1;
            At(t);
            Assert.AreEqual(0, _view.EventMarkersShown, "normal view: no markers");
            _lab.DebugView = true;
            double mid = play.Events[play.Events.Count / 2].Time + 1e-6;
            At(mid);
            int reached = 0;
            foreach (BallEvent e in play.Events) if (e.Time <= mid) reached++;
            Assert.AreEqual(reached, _view.EventMarkersShown);
            At(t);
            Assert.AreEqual(play.Events.Count, _view.EventMarkersShown);
            int k = 0;
            foreach (Transform m in _view.transform)
                if (m.name == "EventMarker" && m.gameObject.activeSelf)
                    Assert.Less(Vector3.Distance(SimulationSpace.ToUnity(play.Events[k++].Before.Position), m.position), 1e-4f, $"marker {k} at its event");
            _lab.DebugView = false;
            At(t);
            Assert.AreEqual(0, _view.EventMarkersShown);
        }

        [UnityTest]
        public IEnumerator PressDuringThePlaySkipsItButNotRightAfterContact()
        {
            yield return null;
            double contact = HitFlush();
            int thrown = _lab.PitchesThrown;
            double Real(double sim) => _release + sim;
            At(contact + 0.1);
            Assert.AreEqual(BattingState.BallInPlay, _lab.StateAt(Real(contact + 0.1)));
            _lab.PressSwingButton(Real(contact + 0.1));
            Assert.AreEqual(thrown, _lab.PitchesThrown, "a double click just after the hit keeps the hit");
            Assert.IsNotNull(_lab.LastPlay);
            At(contact + 1.0);
            Assert.AreEqual(BattingState.BallInPlay, _lab.StateAt(Real(contact + 1.0)));
            _lab.PressSwingButton(Real(contact + 1.0));
            Assert.AreEqual(thrown + 1, _lab.PitchesThrown, "a press during the play throws the next pitch");
            // Clean reset, debug view on: no call, no play, no markers, no banner during the wind-up.
            _lab.DebugView = true;
            _release = Real(contact + 1.0) + _lab.DeliveryLead;
            At(-0.5);
            Assert.IsNull(_lab.LastCall);
            Assert.IsNull(_lab.LastPlay);
            Assert.AreEqual(0, _view.EventMarkersShown);
            Assert.AreEqual(string.Empty, _view.Banner);
            Assert.IsFalse(_camera.IsFollowing);
        }

        [UnityTest]
        public IEnumerator PciPersistsAcrossPitches()
        {
            yield return null;
            _lab.SetPci(0.21, 0.63);
            (double x, double z) = _lab.PciAt(_now);
            _now += 5.0;
            _lab.PressSwingButton(_now);   // take → next pitch
            (double x2, double z2) = _lab.PciAt(_now);
            Assert.AreEqual(x, x2, 1e-12);
            Assert.AreEqual(z, z2, 1e-12);
        }
    }
}
