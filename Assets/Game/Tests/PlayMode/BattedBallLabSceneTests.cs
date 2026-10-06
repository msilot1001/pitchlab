using System.Collections;
using NUnit.Framework;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
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

        [UnityTest]
        public IEnumerator ScenariosPlayOutOnTheField()
        {
            // TASK-004.7 scenarios: a chopper and a gap roller come to rest, a line drive comes back off the wall, a
            // home run leaves play; carry (first landing) and final distance are reported separately.
            yield return SceneManager.LoadSceneAsync("BattedBallLab", LoadSceneMode.Single);
            yield return null;
            var lab = Object.FindFirstObjectByType<BattedBallLabController>();

            lab.ApplyPreset(4);
            Assert.AreEqual(BallPhase.Rest, lab.LastPlay.EndPhase, "chopper rests");
            Assert.Greater(lab.LastPlay.FinalDistance, lab.LastPlay.CarryDistance);
            Assert.That(Units.MetersToFeet(lab.LastPlay.FinalDistance), Is.InRange(130.0, 210.0), "chopper rest distance");

            lab.ApplyPreset(5);
            Assert.IsTrue(System.Array.Exists(Events(lab.LastPlay), e => e.Kind == BallEventKind.WallImpact), "off the wall");
            Assert.AreEqual(BallPhase.Rest, lab.LastPlay.EndPhase);
            Assert.Less(FieldLayout.Standard.DistanceBeyondFence(lab.LastPlay.Final.Position.X, lab.LastPlay.Final.Position.Y, out _), 0.0, "rests in the park");

            lab.ApplyPreset(6);
            Assert.AreEqual(BallPhase.OutOfPlay, lab.LastPlay.EndPhase, "home run");
            Assert.IsTrue(lab.LastPlay.ClearedFence);
            Assert.Greater(Units.MetersToFeet(lab.LastPlay.FinalDistance), 400.0, "lands beyond the fence");

            lab.ApplyPreset(7);
            Assert.AreEqual(BallPhase.Rest, lab.LastPlay.EndPhase, "gap roller rests");
            Assert.IsTrue(System.Array.Exists(Events(lab.LastPlay), e => e.Kind == BallEventKind.SlideToRoll), "it rolls");
            Assert.AreEqual(lab.LastFlight.Metrics.Distance, lab.LastPlay.CarryDistance, 1e-9, "carry is the airborne landing");
            yield return null;
        }

        private static BallEvent[] Events(BallInPlay play)
        {
            var events = new BallEvent[play.Events.Count];
            for (int i = 0; i < events.Length; i++) events[i] = play.Events[i];
            return events;
        }
    }
}
