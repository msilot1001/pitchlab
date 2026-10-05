using System.Collections;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Presentation;
using Pitchlab.Sandbox;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-011 camera modes (HittingLab scene): F1–F5 viewpoints, Tab cycling, the tactical controls only in tactical mode,
    /// the mode kept across plate appearances, fixed views turning to the ball, and no effect on the play.
    /// </summary>
    public class CameraModeTests
    {
        private HittingLabController _lab;
        private GameplayCameraController _modes;
        private Camera _camera;
        private double _now, _release;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("HittingLab", LoadSceneMode.Single);
            yield return null;
            _lab = Object.FindFirstObjectByType<HittingLabController>();
            _modes = Object.FindFirstObjectByType<GameplayCameraController>();
            Assert.IsNotNull(_modes, "the scene's camera has the modes");
            _camera = _modes.GetComponent<Camera>();
            _now = Time.realtimeSinceStartupAsDouble + 100.0;
            _lab.Clock = () => _now;
        }

        /// <summary>Throws the selected pitch and swings at it (an ideal swing, slightly late and high: a ball in play).</summary>
        private ContactResult Swing()
        {
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z + 0.02);
            return _lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration + 0.004);
        }

        [UnityTest]
        public IEnumerator FunctionKeysSelectTheViewsAndTabCycles()
        {
            yield return null;
            Assert.AreEqual(CameraMode.Auto, _modes.Mode, "auto by default");
            var seen = new System.Collections.Generic.List<Vector3>();
            foreach (CameraMode m in new[] { CameraMode.Umpire, CameraMode.Catcher, CameraMode.Offset, CameraMode.Tactical })
            {
                _modes.SetMode(m);
                yield return null;
                StringAssert.Contains(m.ToString(), _modes.Label);
                seen.Add(_camera.transform.position);
            }

            Assert.AreEqual(4, seen.Distinct().Count(), "four different viewpoints");
            Assert.Less(seen[2].x, -1f, "the offset view is on the third-base side");
            Assert.Greater(seen[3].y, 30f, "the tactical view is high above the field");
            _modes.SetMode(CameraMode.Auto);
            yield return null;
            Assert.IsTrue(_modes.GetComponent<BaseballCamera>().enabled, "auto: the baseball camera drives");
            _modes.Cycle();
            Assert.AreEqual(CameraMode.Umpire, _modes.Mode, "Tab wraps around");
            _modes.Cycle();
            Assert.AreEqual(CameraMode.Catcher, _modes.Mode);
        }

        [UnityTest]
        public IEnumerator TacticalControlsWorkOnlyInTacticalMode()
        {
            yield return null;
            _modes.SetMode(CameraMode.Offset);
            Assert.IsFalse(_modes.CapturesArrowKeys, "the arrows choose the pitch");
            _modes.Orbit(30f, 10f);
            _modes.Zoom(0.5f);
            Assert.AreEqual((GameplayCameraController.TacticalYaw, GameplayCameraController.TacticalPitch, GameplayCameraController.TacticalDistance), (_modes.Yaw, _modes.Pitch, _modes.Distance), "ignored outside tactical");

            _modes.SetMode(CameraMode.Tactical);
            Assert.IsTrue(_modes.CapturesArrowKeys, "the arrows orbit");
            yield return null;
            Vector3 before = _camera.transform.position;
            _modes.Orbit(30f, 10f);
            _modes.Zoom(0.5f);
            yield return null;
            Assert.AreNotEqual(before, _camera.transform.position);
            Assert.AreEqual(GameplayCameraController.TacticalDistance * 0.5f, _modes.Distance, 1e-3f);
            Assert.AreEqual(GameplayCameraController.TacticalDistance * 0.5f, Vector3.Distance(_camera.transform.position, GameplayCameraController.TacticalCentre), 1e-2f);
            _modes.Orbit(0f, 500f);
            Assert.AreEqual(GameplayCameraController.MaxPitch, _modes.Pitch, "pitch limited");
            _modes.ResetTactical();
            yield return null;
            Assert.AreEqual(before, _camera.transform.position, "R resets");
        }

        [UnityTest]
        public IEnumerator TheModeStaysAcrossPlateAppearancesAndFixedViewsFollowTheBall()
        {
            yield return null;
            _modes.SetMode(CameraMode.Umpire);
            yield return null;
            Vector3 home = _camera.transform.position;
            Vector3 forward = _camera.transform.forward;
            Assert.IsTrue(Swing().IsContact);
            for (int i = 0; i < 3; i++)
            {
                _now = _release + _lab.LastLive.ContactTime + 1.0 + i;   // the ball is in play (the presentation follows it)
                yield return null;
            }

            Assert.AreEqual(CameraMode.Umpire, _modes.Mode);
            Assert.AreEqual(home, _camera.transform.position, "the umpire stays where he is");
            Vector3 toBall = (_lab.BallTransform.position - home).normalized;
            Assert.Greater(Vector3.Dot(_camera.transform.forward, toBall), Vector3.Dot(forward, toBall), "turned toward the ball");
            // The play ends, the batting view comes back (the presentation calls ShowBatting): still the umpire's view.
            _now = _release + _lab.PlayEnd + BattingStateMachine.ResultPause + 0.5;
            yield return null;
            yield return null;
            Assert.AreEqual(CameraMode.Umpire, _modes.Mode);
            Assert.AreEqual(home, _camera.transform.position);
            Assert.IsTrue(Swing().IsContact, "the next plate appearance");
            yield return null;
            Assert.AreEqual(CameraMode.Umpire, _modes.Mode, "kept across plate appearances");
        }

        [UnityTest]
        public IEnumerator TheCameraNeverChangesThePlay()
        {
            yield return null;
            ContactResult auto = Swing();
            LivePlay a = _lab.LastLive;
            _modes.SetMode(CameraMode.Tactical);
            _modes.Orbit(45f, 20f);
            yield return null;
            _now += 30.0;
            ContactResult tactical = Swing();
            LivePlay b = _lab.LastLive;
            Assert.AreEqual(auto.BattedBall.Position, tactical.BattedBall.Position);
            Assert.AreEqual(auto.BattedBall.Velocity, tactical.BattedBall.Velocity);
            Assert.AreEqual(string.Join("|", a.Log.Select(e => $"{e.Time - a.ContactTime:0.000000} {e.Text}")), string.Join("|", b.Log.Select(e => $"{e.Time - b.ContactTime:0.000000} {e.Text}")));
        }
    }
}
