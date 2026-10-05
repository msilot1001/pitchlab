using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Pitching;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    /// <summary>
    /// Regression test for frame-rate-dependent hitting: the same timestamped keyboard trace (throw, move the PCI with
    /// D/W, swing while D is still held) is fed through the real Input System into HittingLabController under several
    /// frame schedules. The Input System runs in manual-update mode and frames are stepped explicitly, so only the frame
    /// schedule differs between runs. The authoritative swing (PCI at the swing, timing, outcome, batted ball) must match.
    /// </summary>
    public class HittingInputFrameRateTests
    {
        private struct Outcome
        {
            public SwingInput Swing;
            public ContactResult Result;
        }

        private HittingLabController _lab;
        private HittingLabPresentation _view;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private readonly List<InputDevice> _disabled = new List<InputDevice>();
        private InputSettings.UpdateMode _originalUpdateMode;
        private InputSettings.EditorInputBehaviorInPlayMode _originalEditorBehavior;
        private InputSettings.BackgroundBehavior _originalBackgroundBehavior;
        private double _now;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("HittingLab", LoadSceneMode.Single);
            yield return null;
            _lab = UnityEngine.Object.FindFirstObjectByType<HittingLabController>();
            Assert.IsNotNull(_lab);
            _view = UnityEngine.Object.FindFirstObjectByType<HittingLabPresentation>();
            Assert.IsNotNull(_view);

            // Change the active settings in place and restore them in TearDown (assigning a new settings object would
            // destroy the in-memory default). Input must reach the game whether or not the Game view has focus.
            InputSettings settings = InputSystem.settings;
            _originalUpdateMode = settings.updateMode;
            _originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            _originalBackgroundBehavior = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            _keyboard = InputSystem.AddDevice<Keyboard>("FrameRateTestKeyboard");
            _mouse = InputSystem.AddDevice<Mouse>("FrameRateTestMouse");
            // Only the virtual devices drive the lab: a real mouse or keyboard touched during the run would change the trace.
            foreach (InputDevice device in InputSystem.devices)
                if (device != _keyboard && device != _mouse && device.enabled)
                {
                    InputSystem.DisableDevice(device);
                    _disabled.Add(device);
                }

            _lab.RequireMouseCapture = false;   // a test cannot lock the OS cursor; capture is a UI gate, not part of aiming
            _now = Time.realtimeSinceStartupAsDouble;
            _lab.Clock = () => _now;
        }

        [TearDown]
        public void TearDown()
        {
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            foreach (InputDevice device in _disabled) if (device.added) InputSystem.EnableDevice(device);
            _disabled.Clear();
            _keyboard = null;
            _mouse = null;
            InputSettings settings = InputSystem.settings;
            settings.updateMode = _originalUpdateMode;
            settings.editorInputBehaviorInPlayMode = _originalEditorBehavior;
            settings.backgroundBehavior = _originalBackgroundBehavior;
        }

        [UnityTest]
        public IEnumerator SameInputTraceGivesTheSameSwingAt30_60_144FpsAndJitteredFrames()
        {
            var schedules = new (string Name, Func<int, double> Interval)[]
            {
                ("30 fps", _ => 1.0 / 30.0),
                ("60 fps", _ => 1.0 / 60.0),
                ("144 fps", _ => 1.0 / 144.0),
                ("jittered 4–45 ms", Jitter(12345)),
            };

            // Event times on the real clock's timeline (ahead of now, increasing between runs).
            double baseTime = Time.realtimeSinceStartupAsDouble + 10.0;
            var outcomes = new List<Outcome>();
            for (int i = 0; i < schedules.Length; i++) outcomes.Add(Run(baseTime + 100.0 * i, schedules[i].Interval));

            // Per-schedule values, for the record (TestResults/ is git-ignored).
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < outcomes.Count; i++)
                log.AppendLine($"{schedules[i].Name}: swing start {outcomes[i].Swing.StartTime:F9} s, PCI ({outcomes[i].Swing.PciX:F9}, {outcomes[i].Swing.PciZ:F9}) m, " +
                               $"{outcomes[i].Result.Outcome}, timing {outcomes[i].Result.TimingError * 1000.0:F6} ms, exit {outcomes[i].Result.ExitSpeed:F9} m/s");
            System.IO.Directory.CreateDirectory("TestResults");
            System.IO.File.WriteAllText("TestResults/hitting_framerate.txt", log.ToString());

            AssertSame(schedules, outcomes);
            yield break;
        }

        [UnityTest]
        public IEnumerator SameMouseTraceGivesTheSameSwingAt30_60_144FpsAndJitteredFrames()
        {
            var schedules = new (string Name, Func<int, double> Interval)[]
            {
                ("30 fps", _ => 1.0 / 30.0),
                ("60 fps", _ => 1.0 / 60.0),
                ("144 fps", _ => 1.0 / 144.0),
                ("jittered 4–45 ms", Jitter(777)),
            };

            double baseTime = Time.realtimeSinceStartupAsDouble + 10.0;
            var outcomes = new List<Outcome>();
            for (int i = 0; i < schedules.Length; i++) outcomes.Add(RunMouse(baseTime + 100.0 * i, schedules[i].Interval));
            AssertSame(schedules, outcomes);
            yield break;
        }

        // Mouse trace (times after the throw click at `start`; after the throw shifted by DeliveryLead): left click throws;
        // a 125 Hz mouse reports (+3, −1) counts every 8 ms from 0.05 s, and keeps moving after the left-click swing (4 ms
        // late) — so a PCI read at any frame time instead of the click's timestamp would land somewhere else (≈ 0.9 m/s:
        // one 30 fps frame ≈ 3 cm). Exactly where the counts put it at the click is checked independently of frames.
        private Outcome RunMouse(double start, Func<int, double> interval)
        {
            HittingPitch preview = HittingPitch.Create(PitchPresets.FourSeam, EnvironmentState.Standard);
            double swingAt = preview.IdealContactTime - _lab.Swing.SwingDuration + 0.004;
            var ball = preview.IdealContactState.Position;
            double lead = _lab.DeliveryLead;

            var moves = new List<double>();
            for (double t = 0.05; t < 0.45; t += 0.008) moves.Add(lead + t);
            int before = moves.FindAll(t => t < lead + swingAt).Count;
            // Normalized PCI units per count, then metres through the one PciFrame mapping.
            PciFrame area = _lab.PciArea;
            double stepX = (double)(3f * _lab.MouseSensitivity) * area.HalfWidth, stepZ = (double)(-1f * _lab.MouseSensitivity) * area.HalfHeight;
            _now = start - 0.5;
            _lab.SetPci(ball.X - before * stepX, ball.Z - before * stepZ);

            var events = new List<(double Time, Vector2 Delta, bool Left)>
            {
                (0.000, Vector2.zero, true),
                (0.040, Vector2.zero, false),
                (lead + swingAt, Vector2.zero, true),
                (lead + swingAt + 0.030, Vector2.zero, false),
            };
            foreach (double t in moves) events.Add((t, new Vector2(3f, -1f), t >= lead + swingAt && t < lead + swingAt + 0.030));
            events.Sort((a, b) => a.Time.CompareTo(b.Time));

            int next = 0;
            double frame = start - 0.1;
            for (int k = 0; frame < start + lead + 0.6; k++)
            {
                frame += interval(k);
                while (next < events.Count && start + events[next].Time <= frame)
                {
                    var state = new MouseState { delta = events[next].Delta }.WithButton(MouseButton.Left, events[next].Left);
                    InputSystem.QueueStateEvent(_mouse, state, start + events[next].Time);
                    next++;
                }

                _now = frame;
                InputSystem.Update();
                _lab.SendMessage("Update");
                _view.FrameUpdate();
            }

            Assert.AreEqual(events.Count, next, "all events queued");
            Assert.IsTrue(_lab.LastSwing.HasValue && _lab.LastResult.HasValue, "the click swung");
            Assert.AreEqual(swingAt, _lab.LastSwing.Value.StartTime, 1e-9, "swing time from the click's timestamp");
            Assert.AreEqual(ball.X, _lab.LastSwing.Value.PciX, 1e-9, "PCI X from the counts before the click");
            Assert.AreEqual(ball.Z, _lab.LastSwing.Value.PciZ, 1e-9, "PCI Z from the counts before the click");
            return new Outcome { Swing = _lab.LastSwing.Value, Result = _lab.LastResult.Value };
        }

        /// <summary>Throws with a click, then steps (one Input System update per frame) to <paramref name="simTime"/> after release.</summary>
        private double ThrowAndWait(double simTime)
        {
            double start = Time.realtimeSinceStartupAsDouble + 10.0;
            _now = start;
            QueueMouse(start, Vector2.zero, true);
            QueueMouse(start + 0.02, Vector2.zero, false);
            _now = start + 0.03;
            InputSystem.Update();
            Assert.IsNotNull(_lab.CurrentPitch, "the click threw");
            double release = start + _lab.DeliveryLead;
            _now = release + simTime;
            InputSystem.Update();
            return release;
        }

        private void QueueMouse(double time, Vector2 delta, bool left) =>
            InputSystem.QueueStateEvent(_mouse, new MouseState { delta = delta }.WithButton(MouseButton.Left, left), time);

        [UnityTest]
        public IEnumerator ClickFollowedByAMouseReportInTheSameUpdateKeepsItsOwnTimestamp()
        {
            // Pins event merging: the click is the first mouse event of its update and a delta-only report with the button
            // still held follows within it — merged, the swing would start at the report's time and include its delta.
            double release = ThrowAndWait(0.15);
            (double x, double z) = _lab.PciAt(release + 0.15);
            double click = release + 0.2;
            QueueMouse(click, Vector2.zero, true);
            QueueMouse(click + 0.003, new Vector2(40f, 0f), true);
            _now = click + 0.01;
            InputSystem.Update();
            Assert.IsTrue(_lab.LastSwing.HasValue, "the click swung");
            Assert.AreEqual(_lab.ToSimTime(click), _lab.LastSwing.Value.StartTime, 1e-9, "swing at the click's own timestamp");
            Assert.AreEqual(x, _lab.LastSwing.Value.PciX, 1e-12, "the later report is not part of the swing's PCI");
            Assert.AreEqual(z, _lab.LastSwing.Value.PciZ, 1e-12);
            Assert.Greater(_lab.PciAt(click + 0.004).X, x, "the later report still moves the PCI afterwards");
            yield break;
        }

        [UnityTest]
        public IEnumerator MouseReportBeforeAClickInTheSameUpdateIsPartOfTheSwing()
        {
            // Pins per-event aim: a report just before the click, processed in the same frame, counts — a per-frame or
            // frame-time delta would apply it after the click.
            double release = ThrowAndWait(0.15);
            (double x, double z) = _lab.PciAt(release + 0.15);
            double click = release + 0.2;
            QueueMouse(click - 0.003, new Vector2(40f, 10f), false);
            QueueMouse(click, Vector2.zero, true);
            _now = click + 0.01;
            InputSystem.Update();
            Assert.IsTrue(_lab.LastSwing.HasValue, "the click swung");
            Assert.AreEqual(x + (double)(40f * _lab.MouseSensitivity) * _lab.PciArea.HalfWidth, _lab.LastSwing.Value.PciX, 1e-9);
            Assert.AreEqual(z + (double)(10f * _lab.MouseSensitivity) * _lab.PciArea.HalfHeight, _lab.LastSwing.Value.PciZ, 1e-9);
            yield break;
        }

        [UnityTest]
        public IEnumerator CursorCaptureGatesTheMouse()
        {
            _lab.RequireMouseCapture = true;
            Cursor.lockState = CursorLockMode.None;
            double t = Time.realtimeSinceStartupAsDouble + 10.0;
            int thrown = _lab.PitchesThrown;
            (double x0, _) = _lab.PciAt(t);

            QueueMouse(t, new Vector2(50f, 0f), false);   // not captured: aim ignored
            _now = t + 0.01;
            InputSystem.Update();
            Assert.AreEqual(x0, _lab.PciAt(t + 0.01).X, 1e-12, "uncaptured mouse does not aim");

            QueueMouse(t + 0.02, Vector2.zero, true);     // the capturing click does not throw
            QueueMouse(t + 0.03, Vector2.zero, false);
            _now = t + 0.04;
            InputSystem.Update();
            Assert.AreEqual(thrown, _lab.PitchesThrown, "the capturing click did not throw");
            if (!_lab.MouseCaptured) Assert.Ignore("This Editor/session cannot lock the cursor; capture gating above still verified.");

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape), t + 0.05);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(), t + 0.06);
            _now = t + 0.07;
            InputSystem.Update();
            Assert.IsFalse(_lab.MouseCaptured, "Escape released the cursor");
            yield break;
        }

        [UnityTest]
        public IEnumerator LostCaptureClickDuringALivePitchStillSwings()
        {
            _lab.RequireMouseCapture = false;
            double release = ThrowAndWait(0.2);
            _lab.RequireMouseCapture = true;
            Cursor.lockState = CursorLockMode.None;      // e.g. focus lost mid at-bat
            double click = release + 0.25;
            QueueMouse(click, Vector2.zero, true);
            _now = click + 0.01;
            InputSystem.Update();
            Assert.IsTrue(_lab.LastSwing.HasValue, "the click recaptured and swung");
            Assert.AreEqual(_lab.ToSimTime(click), _lab.LastSwing.Value.StartTime, 1e-9);
            yield break;
        }

        // Trace (seconds after the throw press at `start`; everything after the throw also shifted by the pitcher's
        // DeliveryLead, so times are relative to the release): Space throw, D held 0.10 → 0.35, W held 0.170 → 0.205,
        // swing 4 ms late while D is still held. PCI speed 1.2 m/s, so one 30 fps frame of error would be 4 cm.
        private Outcome Run(double start, Func<int, double> interval)
        {
            HittingPitch preview = HittingPitch.Create(PitchPresets.FourSeam, EnvironmentState.Standard);
            double swingAt = preview.IdealContactTime - _lab.Swing.SwingDuration + 0.004;
            var ball = preview.IdealContactState.Position;

            // Start with the PCI placed so that the scripted motion ends on the ball at the swing.
            _now = start - 0.5;
            _lab.SetPci(ball.X - 1.2 * (swingAt - 0.100), ball.Z - 1.2 * (0.205 - 0.170));

            double lead = _lab.DeliveryLead;
            var events = new List<(double Time, Key[] Keys)>
            {
                (0.000, new[] { Key.Space }),
                (0.040, new Key[0]),
                (lead + 0.100, new[] { Key.D }),
                (lead + 0.170, new[] { Key.D, Key.W }),
                (lead + 0.205, new[] { Key.D }),
                (lead + swingAt, new[] { Key.D, Key.Space }),
                (lead + swingAt + 0.030, new[] { Key.D }),
                (lead + 0.350, new Key[0]),
            };

            int next = 0;
            double frame = start - 0.1;
            for (int k = 0; frame < start + lead + 0.6; k++)
            {
                double previous = frame;
                frame += interval(k);
                while (next < events.Count && start + events[next].Time <= frame)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(events[next].Keys), start + events[next].Time);
                    next++;
                }

                _now = frame;
                InputSystem.Update();           // processes this frame's events (callbacks carry the event timestamps)
                _lab.SendMessage("Update");     // the controller's real per-frame path (clock = this frame's time)
                _view.FrameUpdate();            // presentation renders every frame too; it must not change any outcome
                Assert.Greater(frame, previous);
            }

            Assert.AreEqual(events.Count, next, "all events queued");
            Assert.IsTrue(_lab.LastSwing.HasValue && _lab.LastResult.HasValue, "the trace produced a swing");
            Assert.AreEqual(preview.IdealContactTime, _lab.CurrentPitch.IdealContactTime, 1e-12, "four-seam preset thrown");
            // Independent of any frame: the pitch was released DeliveryLead after the Space press and the swing started at its press.
            Assert.AreEqual(swingAt, _lab.LastSwing.Value.StartTime, 1e-9, "swing time from event timestamps (playback 1×)");
            // Independent of any frame: the PCI at the swing is exactly where the trace put it (on the ball).
            Assert.AreEqual(ball.X, _lab.LastSwing.Value.PciX, 1e-9);
            Assert.AreEqual(ball.Z, _lab.LastSwing.Value.PciZ, 1e-9);
            return new Outcome { Swing = _lab.LastSwing.Value, Result = _lab.LastResult.Value };
        }

        private static void AssertSame((string Name, Func<int, double> Interval)[] schedules, List<Outcome> outcomes)
        {
            Outcome reference = outcomes[0];
            Assert.AreEqual(ContactOutcome.Contact, reference.Result.Outcome, "the trace is built to make contact");
            for (int i = 1; i < outcomes.Count; i++)
            {
                string name = schedules[i].Name;
                Outcome o = outcomes[i];
                Assert.AreEqual(reference.Swing.StartTime, o.Swing.StartTime, 1e-9, $"{name}: swing time");
                Assert.AreEqual(reference.Swing.PciX, o.Swing.PciX, 1e-9, $"{name}: PCI X at the swing");
                Assert.AreEqual(reference.Swing.PciZ, o.Swing.PciZ, 1e-9, $"{name}: PCI Z at the swing");
                Assert.AreEqual(reference.Result.Outcome, o.Result.Outcome, name);
                Assert.AreEqual(reference.Result.TimingError, o.Result.TimingError, 1e-9, $"{name}: timing error");
                Assert.AreEqual(reference.Result.OffsetAlongBarrel, o.Result.OffsetAlongBarrel, 1e-9, $"{name}: barrel offset");
                Assert.AreEqual(reference.Result.VerticalOffset, o.Result.VerticalOffset, 1e-9, $"{name}: vertical offset");
                // Times are differences of absolute wall-clock timestamps (rounding ≈ 1e-13 s after hours of uptime), which the
                // contact solve amplifies ~10³×; any frame dependence would be ≥ mm/s.
                Assert.AreEqual(0.0, (reference.Result.BattedBall.Velocity - o.Result.BattedBall.Velocity).Length, 1e-7, $"{name}: exit velocity");
                Assert.AreEqual(0.0, (reference.Result.BattedBall.Spin - o.Result.BattedBall.Spin).Length, 1e-5, $"{name}: spin");
            }
        }

        private static Func<int, double> Jitter(int seed)
        {
            var random = new System.Random(seed);
            return _ => 0.004 + random.NextDouble() * 0.041;
        }
    }
}
