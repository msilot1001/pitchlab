using System.Collections;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Presentation;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.Core;
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
            At(_lab.LastBattedBall.Flight.Final.Time + 0.1);
            Assert.IsTrue(_landing.Visible);
            Vector3 rest = _lab.BallTransform.position;
            Assert.Less(new Vector2(rest.x - _landing.transform.position.x, rest.z - _landing.transform.position.z).magnitude, 0.05f, "marker where the ball landed");

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
    }
}
