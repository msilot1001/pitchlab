using System;
using System.Collections;
using NUnit.Framework;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.Pitching;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    public class PitchLabSceneTests
    {
        private PitchLabController _controller;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("PitchLab", LoadSceneMode.Single);
            yield return null; // let Start() run
            _controller = UnityEngine.Object.FindFirstObjectByType<PitchLabController>();
            Assert.IsNotNull(_controller, "PitchLab scene must contain a PitchLabController");
        }

        [UnityTest]
        public IEnumerator ThrownPitchIsPlayedBackToThePlateCrossing()
        {
            Assert.IsNotNull(_controller.LastResult, "scene throws a pitch on start");
            Assert.IsTrue(_controller.LastResult.Metrics.ReachedPlate);

            float timeout = Time.realtimeSinceStartup + 5f;
            while (!_controller.PlaybackFinished && Time.realtimeSinceStartup < timeout) yield return null;

            Assert.IsTrue(_controller.PlaybackFinished, "playback should finish within a few seconds");
            Transform ball = _controller.Ball;
            Vector3 plate = SimulationSpace.ToUnity(_controller.LastResult.Flight.Final.Position);
            Assert.Less(Vector3.Distance(plate, ball.position), 1e-4f, "ball rests at the authoritative plate crossing");

            Assert.AreEqual(_controller.LastResult.Flight.Samples.Count, _controller.FlightPath.positionCount);
        }

        [UnityTest]
        public IEnumerator RethrowingWithNewInputsChangesTheFlight()
        {
            PitchInput input = PitchPresets.FourSeam;
            input.SpinRateRpm = 0.0;
            _controller.CurrentInput = input;
            _controller.Throw();
            yield return null;
            Assert.AreEqual(0.0, _controller.LastResult.Metrics.VerticalMovement, "no spin, no movement");

            _controller.ApplyPreset(Array.FindIndex(PitchPresets.All, p => p.Label == PitchPresets.Curveball.Label));
            Assert.AreEqual(0.0, _controller.PlaybackTime, "rethrow restarts playback");
            Assert.Less(_controller.LastResult.Metrics.VerticalMovement, 0.0, "curveball topspin breaks down");
            yield return null;
        }
    }
}
