using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Sandbox;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    public class HittingLabSceneTests
    {
        private HittingLabController _lab;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("HittingLab", LoadSceneMode.Single);
            yield return null;
            _lab = Object.FindFirstObjectByType<HittingLabController>();
            Assert.IsNotNull(_lab, "HittingLab scene must contain a HittingLabController");
        }

        [UnityTest]
        public IEnumerator WellTimedCentredSwingMakesHardContactAndFreezesBallAtContact()
        {
            _lab.ThrowPitch(0);
            HittingPitch pitch = _lab.CurrentPitch;
            Assert.IsTrue(pitch.ReachesContactPlane);
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
            ContactResult result = _lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration);
            Assert.IsTrue(result.IsContact);
            Assert.Greater(result.ExitSpeed, 40.0, "> 90 mph");

            float timeout = Time.realtimeSinceStartup + 5f;
            while (_lab.SimTime < result.BattedBall.Time + 0.05 && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.GreaterOrEqual(_lab.SimTime, result.BattedBall.Time, "playback reached the contact time");
            yield return null;
            Assert.Less(Vector3.Distance(SimulationSpace.ToUnity(result.BattedBall.Position), _lab.BallTransform.position), 1e-4f, "ball shown at the contact point");
        }

        [UnityTest]
        public IEnumerator SwingButtonThrowsWhenIdleSwingsDuringFlightAndRethrowsAfter()
        {
            int before = _lab.PitchesThrown;
            _lab.PressSwingButton(Time.realtimeSinceStartupAsDouble);
            Assert.AreEqual(before + 1, _lab.PitchesThrown, "idle → throw");
            Assert.IsFalse(_lab.LastResult.HasValue);
            yield return null;

            _lab.PressSwingButton(Time.realtimeSinceStartupAsDouble);
            Assert.AreEqual(before + 1, _lab.PitchesThrown, "in flight → swing, not a new pitch");
            Assert.IsTrue(_lab.LastResult.HasValue);
            yield return null;

            _lab.PressSwingButton(Time.realtimeSinceStartupAsDouble);
            Assert.AreEqual(before + 2, _lab.PitchesThrown, "after the swing → next pitch");
        }

        [UnityTest]
        public IEnumerator SwingUsesTheInputEventTimestampNotTheFrameTime()
        {
            _lab.ThrowPitch(0);
            double eventTime = Time.realtimeSinceStartupAsDouble;   // the button went down now…
            for (int i = 0; i < 5; i++) yield return null;           // …but is handled several frames later
            double expected = _lab.ToSimTime(eventTime);
            _lab.PressSwingButton(eventTime);

            Assert.IsTrue(_lab.LastSwing.HasValue);
            Assert.AreEqual(expected, _lab.LastSwing.Value.StartTime, 1e-12);
            Assert.Less(_lab.LastSwing.Value.StartTime, _lab.SimTime, "earlier than the frame-time reading");
        }

        [UnityTest]
        public IEnumerator PlaybackSpeedChangesApplyToTheNextPitchOnly()
        {
            _lab.ThrowPitch(0);
            yield return null;
            double probe = Time.realtimeSinceStartupAsDouble + 0.1;
            double before = _lab.ToSimTime(probe);
            typeof(HittingLabController).GetField("_playbackSpeed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_lab, 0.5f);
            Assert.AreEqual(before, _lab.ToSimTime(probe), 1e-12, "mid-pitch speed change must not rescale the live pitch clock");
        }

        [UnityTest]
        public IEnumerator BadTimingIsAMiss()
        {
            _lab.ThrowPitch(3);
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
            ContactResult result = _lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration + 0.1);
            Assert.AreEqual(ContactOutcome.MissTiming, result.Outcome);
            yield return null;
        }
    }
}
