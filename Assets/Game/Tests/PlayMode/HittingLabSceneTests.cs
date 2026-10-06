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

        [Test]
        public void SceneSwingIsTheValidatedDefault()
        {
            // The scene serializes its own copy of the swing; it must not silently keep stale physics (TASK-004.5 found
            // the old e_x/r_x and no bat tilt here). Change SwingParameters.Default and the scene together.
            Assert.AreEqual(SwingParameters.Default, _lab.Swing);
        }

        [UnityTest]
        public IEnumerator WellTimedCentredSwingMakesHardContactAndBallFollowsTheBattedTrajectory()
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
            // After contact the rendered ball is the authoritative batted-ball sample for the rendered time (TASK-004.6; it
            // used to freeze at the contact point).
            Vector3 expected = SimulationSpace.ToUnity(_lab.LastBattedBall.Flight.StateAt(_lab.RenderedSimTime).Position);
            Assert.Less(Vector3.Distance(expected, _lab.BallTransform.position), 1e-4f, "ball on the batted-ball trajectory");
        }

        [UnityTest]
        public IEnumerator SwingButtonThrowsWhenIdleSwingsDuringFlightAndRethrowsAfter()
        {
            double now = Time.realtimeSinceStartupAsDouble + 100.0;
            _lab.Clock = () => now;
            int before = _lab.PitchesThrown;
            _lab.PressSwingButton(now);
            Assert.AreEqual(before + 1, _lab.PitchesThrown, "idle → throw (the delivery starts)");
            Assert.IsFalse(_lab.LastResult.HasValue);
            Assert.AreEqual(-_lab.DeliveryLead, _lab.ToSimTime(now), 1e-9, "released DeliveryLead after the press");
            yield return null;

            now += _lab.DeliveryLead + 0.2;
            _lab.PressSwingButton(now);
            Assert.AreEqual(before + 1, _lab.PitchesThrown, "in flight → swing, not a new pitch");
            Assert.IsTrue(_lab.LastResult.HasValue);
            yield return null;

            _lab.PressSwingButton(now);
            Assert.AreEqual(before + 1, _lab.PitchesThrown, "during the swing (a double click) → ignored");
            Assert.AreEqual(BattingState.Swinging, _lab.StateAt(now));
            yield return null;

            now += 1.0;                                    // the outcome is on screen (result, or the ball still in play)
            BattingState state = _lab.StateAt(now);
            Assert.IsTrue(state == BattingState.Result || state == BattingState.BallInPlay, state.ToString());
            _lab.PressSwingButton(now);
            Assert.AreEqual(before + 2, _lab.PitchesThrown, "after the swing's outcome → next pitch (skipping the rest of a play)");
            Assert.AreEqual(BattingState.Windup, _lab.StateAt(now));
        }

        [UnityTest]
        public IEnumerator TheCheckButtonStopsTheSwingOnlyBeforeTheOffer()
        {
            // TASK-024: C / East during the swing. Before the offer point: no swing (the take is called); after it: too late.
            double now = Time.realtimeSinceStartupAsDouble + 100.0;
            _lab.Clock = () => now;
            double RealtimeAt(double simTime) => now + (simTime - _lab.ToSimTime(now));   // playback speed 1
            _lab.PressSwingButton(now);                                     // throw
            now += _lab.DeliveryLead + 0.2;
            _lab.PressSwingButton(now);                                     // swing
            double offer = _lab.LastSwing.Value.OfferTime(_lab.Swing.SwingDuration);
            _lab.PressCheckButton(RealtimeAt(offer - 0.005));
            Assert.AreEqual(ContactOutcome.CheckedSwing, _lab.LastResult.Value.Outcome, "stopped in time");
            Assert.IsNull(_lab.LastLive, "no play");
            Assert.AreEqual(offer - 0.005, _lab.LastSwing.Value.CheckTime, 1e-9);
            yield return null;

            now += 2.0;
            _lab.PressSwingButton(now);                                     // the next pitch
            now += _lab.DeliveryLead + 0.2;
            _lab.PressSwingButton(now);
            ContactResult swung = _lab.LastResult.Value;
            offer = _lab.LastSwing.Value.OfferTime(_lab.Swing.SwingDuration);
            _lab.PressCheckButton(RealtimeAt(offer + 0.005));
            Assert.AreEqual(swung.Outcome, _lab.LastResult.Value.Outcome, "too late: the swing stands");
            Assert.IsTrue(double.IsNaN(_lab.LastSwing.Value.CheckTime));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PressDuringTheWindupIsIgnored()
        {
            // Wind-up policy B: a press before release can never connect (contact is ~0.4 s after release), so it is
            // ignored instead of spending the pitch on a whiff; a later press still swings at this pitch.
            double now = Time.realtimeSinceStartupAsDouble + 100.0;
            _lab.Clock = () => now;
            _lab.PressSwingButton(now);                 // throw
            now += 0.5 * _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            int thrown = _lab.PitchesThrown;
            _lab.PressSwingButton(now);                 // before release
            Assert.IsFalse(_lab.LastSwing.HasValue);
            Assert.IsFalse(_lab.LastResult.HasValue);
            Assert.AreSame(pitch, _lab.CurrentPitch, "no re-throw");
            Assert.AreEqual(thrown, _lab.PitchesThrown);
            now += 0.5 * _lab.DeliveryLead + 0.2;
            _lab.PressSwingButton(now);                 // after release
            Assert.IsTrue(_lab.LastSwing.HasValue);
            Assert.AreEqual(_lab.ToSimTime(now), _lab.LastSwing.Value.StartTime, 1e-12);
            yield return null;
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

        [UnityTest]
        public IEnumerator OneSwingPerPitchEvenForAnEarlierStampedSecondPress()
        {
            // Space and a click a few ms apart from two devices can be handled out of timestamp order: the second press,
            // stamped earlier than the first swing, must not swing again (unity-reviewer, TASK-004.6B-2).
            double now = Time.realtimeSinceStartupAsDouble + 100.0;
            _lab.Clock = () => now;
            _lab.PressSwingButton(now);                    // throw
            double first = now + _lab.DeliveryLead + 0.25;
            now = first + 0.01;
            _lab.PressSwingButton(first);
            SwingInput swing = _lab.LastSwing.Value;
            ContactResult result = _lab.LastResult.Value;
            _lab.PressSwingButton(first - 0.004);
            Assert.AreEqual(swing.StartTime, _lab.LastSwing.Value.StartTime, 0.0, "still the first swing");
            Assert.AreEqual(result.Outcome, _lab.LastResult.Value.Outcome);
            yield return null;
        }
    }
}
