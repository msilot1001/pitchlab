using System.Collections;
using NUnit.Framework;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    public class BattedBallLabSceneTests
    {
        [UnityTest]
        public IEnumerator PresetsFlyAsGroundBallLineDriveFlyBallAndDeepFlyBall()
        {
            yield return SceneManager.LoadSceneAsync("BattedBallLab", LoadSceneMode.Single);
            yield return null;
            var lab = Object.FindFirstObjectByType<BattedBallLabController>();
            Assert.IsNotNull(lab);

            double[] distance = new double[4];
            double[] hang = new double[4];
            for (int i = 0; i < 4; i++)
            {
                lab.ApplyPreset(i);
                Assert.IsTrue(lab.LastFlight.Metrics.Landed);
                distance[i] = Units.MetersToFeet(lab.LastFlight.Metrics.Distance);
                hang[i] = lab.LastFlight.Metrics.HangTime;
                yield return null;
            }

            Assert.Less(distance[0], 60.0, "ground ball lands almost at once");
            Assert.Less(distance[0], distance[1]);
            Assert.Less(distance[1], distance[2]);
            Assert.Less(distance[2], distance[3], "deep fly ball goes furthest");
            Assert.Less(hang[1], hang[2], "line drive hangs less than a fly ball");
        }
    }
}
