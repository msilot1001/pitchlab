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
            _now = Time.realtimeSinceStartupAsDouble;
            _lab.Clock = () => _now;
        }

        [TearDown]
        public void TearDown()
        {
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            _keyboard = null;
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
                Assert.AreEqual(0.0, (reference.Result.BattedBall.Velocity - o.Result.BattedBall.Velocity).Length, 1e-9, $"{name}: exit velocity");
                Assert.AreEqual(0.0, (reference.Result.BattedBall.Spin - o.Result.BattedBall.Spin).Length, 1e-6, $"{name}: spin");
            }

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

        private static Func<int, double> Jitter(int seed)
        {
            var random = new System.Random(seed);
            return _ => 0.004 + random.NextDouble() * 0.041;
        }
    }
}
