using System.Collections;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Presentation;
using Pitchlab.Sandbox;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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

    /// <summary>TASK-011 camera keys through the Input System (virtual devices, manual input updates).</summary>
    public class CameraKeyTests
    {
        private HittingLabController _lab;
        private GameplayCameraController _modes;
        private Keyboard _keyboard;
        private Gamepad _pad;
        private InputSettings.UpdateMode _updateMode;
        private InputSettings.EditorInputBehaviorInPlayMode _editorBehavior;
        private InputSettings.BackgroundBehavior _background;
        private readonly System.Collections.Generic.List<InputDevice> _disabled = new System.Collections.Generic.List<InputDevice>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("HittingLab", LoadSceneMode.Single);
            yield return null;
            _lab = Object.FindFirstObjectByType<HittingLabController>();
            _modes = Object.FindFirstObjectByType<GameplayCameraController>();
            InputSettings settings = InputSystem.settings;
            (_updateMode, _editorBehavior, _background) = (settings.updateMode, settings.editorInputBehaviorInPlayMode, settings.backgroundBehavior);
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            _keyboard = InputSystem.AddDevice<Keyboard>("CameraTestKeyboard");
            _pad = InputSystem.AddDevice<Gamepad>("CameraTestPad");
            foreach (InputDevice device in InputSystem.devices)
                if (device != _keyboard && device != _pad && device.enabled)
                {
                    InputSystem.DisableDevice(device);
                    _disabled.Add(device);
                }
        }

        [TearDown]
        public void TearDown()
        {
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_pad != null) InputSystem.RemoveDevice(_pad);
            foreach (InputDevice device in _disabled) if (device.added) InputSystem.EnableDevice(device);
            _disabled.Clear();
            InputSettings settings = InputSystem.settings;
            (settings.updateMode, settings.editorInputBehaviorInPlayMode, settings.backgroundBehavior) = (_updateMode, _editorBehavior, _background);
        }

        /// <summary>One input update with <paramref name="keys"/> held, then the camera reads it (a 0.5 s frame).</summary>
        private void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
            InputSystem.Update();
            _modes.ReadKeys(_keyboard, 0.5f);
        }

        private void Release() => Keys();

        [UnityTest]
        public IEnumerator FunctionKeysTabAndTheTacticalKeys()
        {
            yield return null;
            foreach (var (key, mode) in new[] { (Key.F1, CameraMode.Umpire), (Key.F2, CameraMode.Catcher), (Key.F3, CameraMode.Offset), (Key.F5, CameraMode.Auto), (Key.F4, CameraMode.Tactical) })
            {
                Keys(key);
                Release();
                Assert.AreEqual(mode, _modes.Mode, key.ToString());
            }

            Keys(Key.RightArrow);
            Assert.AreEqual(GameplayCameraController.OrbitSpeed * 0.5f, _modes.Yaw, 1e-3f, "→ orbits");
            Keys(Key.UpArrow);
            Assert.AreEqual(GameplayCameraController.TacticalPitch + GameplayCameraController.PitchSpeed * 0.5f, _modes.Pitch, 1e-3f, "↑ pitches");
            Keys(Key.LeftBracket);
            Assert.Less(_modes.Distance, GameplayCameraController.TacticalDistance, "[ zooms in");
            Keys(Key.PageDown);
            Keys(Key.PageDown);
            Assert.Greater(_modes.Distance, GameplayCameraController.TacticalDistance, "PageDown zooms out");
            Release();
            Keys(Key.R);
            Release();
            Assert.AreEqual((GameplayCameraController.TacticalYaw, GameplayCameraController.TacticalPitch, GameplayCameraController.TacticalDistance), (_modes.Yaw, _modes.Pitch, _modes.Distance), "R resets");

            Keys(Key.Tab);
            Release();
            Assert.AreEqual(CameraMode.Auto, _modes.Mode, "Tab: the next mode");
            Keys(Key.RightArrow);
            Release();
            Assert.AreEqual(GameplayCameraController.TacticalYaw, _modes.Yaw, "no orbit outside tactical");
        }

        [UnityTest]
        public IEnumerator TheTacticalCameraTakesTheKeyboardArrowsNotTheDPad()
        {
            yield return null;
            int preset = _lab.PresetIndex;
            Keys(Key.RightArrow);
            Release();
            Assert.AreEqual(preset + 1, _lab.PresetIndex, "auto: → chooses the next pitch");
            _modes.SetMode(CameraMode.Tactical);
            Keys(Key.RightArrow);
            Release();
            Assert.AreEqual(preset + 1, _lab.PresetIndex, "tactical: → orbits, the pitch stays");
            InputSystem.QueueStateEvent(_pad, new GamepadState(GamepadButton.DpadRight));
            InputSystem.Update();
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            InputSystem.Update();
            Assert.AreEqual(preset + 2, _lab.PresetIndex, "the D-pad still chooses the pitch");
        }
    }
}
