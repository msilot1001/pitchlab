using System;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;

namespace Pitchlab.Tests
{
    public class PciTrackTests
    {
        private static PciTrack Track() => new PciTrack(-0.6, 0.6, 0.2, 1.4, 10.0, 0.0, 0.8);

        [Test]
        public void PositionIsTheExactIntegralOfTimestampedVelocity()
        {
            PciTrack track = Track();
            track.SetVelocity(10.10, 1.2, 0.0);
            track.SetVelocity(10.25, 1.2, -0.6);
            track.SetVelocity(10.30, 0.0, 0.0);
            Assert.AreEqual(0.0, track.PositionAt(10.05).X, 1e-12, "before any motion");
            Assert.AreEqual(1.2 * 0.07, track.PositionAt(10.17).X, 1e-12);
            (double x, double z) = track.PositionAt(10.28);
            Assert.AreEqual(1.2 * 0.18, x, 1e-12);
            Assert.AreEqual(0.8 - 0.6 * 0.03, z, 1e-12);
            Assert.AreEqual(1.2 * 0.20, track.PositionAt(11.0).X, 1e-12, "stopped after 10.30");
        }

        [Test]
        public void ClampsToTheAreaAndMovesBackFromTheEdgeImmediately()
        {
            PciTrack track = Track();
            track.SetVelocity(10.0, 1.2, 0.0);          // reaches 0.6 at 10.5
            Assert.AreEqual(0.6, track.PositionAt(11.0).X, 1e-12);
            track.SetVelocity(11.0, -1.2, 0.0);         // turns around at the edge, not at the unclamped 1.2
            Assert.AreEqual(0.6 - 0.12, track.PositionAt(11.1).X, 1e-12);
        }

        [Test]
        public void PlaceKeepsMotionAndPastQueriesUseTheHistory()
        {
            PciTrack track = Track();
            track.SetVelocity(10.0, 0.0, 0.5);
            track.Place(10.2, 0.3, 0.5);
            Assert.AreEqual(0.3, track.PositionAt(10.3).X, 1e-12);
            Assert.AreEqual(0.5 + 0.05, track.PositionAt(10.3).Z, 1e-12, "velocity kept after Place");
            Assert.AreEqual(0.8 + 0.05, track.PositionAt(10.1).Z, 1e-12, "query before the Place uses the earlier segment");
        }

        [Test]
        public void HistoryPruningKeepsTheSegmentCoveringTheWindow()
        {
            // Many stick events: positions within the last HistorySeconds stay exact after old segments are pruned.
            PciTrack track = Track();
            double t = 10.0, x = 0.0;
            for (int i = 0; i < 2000; i++)
            {
                double v = (i % 2 == 0) ? 0.3 : -0.3;
                track.SetVelocity(t, v, 0.0);
                t += 0.004;
                x += v * 0.004;
            }

            double queryTime = t - PciTrack.HistorySeconds + 0.001;   // inside the kept window
            double expected = 0.0;
            double time = 10.0;
            for (int i = 0; i < 2000 && time + 0.004 <= queryTime; i++, time += 0.004) expected += ((i % 2 == 0) ? 0.3 : -0.3) * 0.004;
            int segmentIndex = (int)Math.Round((time - 10.0) / 0.004);
            expected += ((segmentIndex % 2 == 0) ? 0.3 : -0.3) * (queryTime - time);
            Assert.AreEqual(expected, track.PositionAt(queryTime).X, 1e-9);
            Assert.AreEqual(x, track.PositionAt(t).X, 1e-9, "newest position: every segment integrated exactly");
        }

        [Test]
        public void OutOfOrderEventIsAppliedAtTheLatestTime()
        {
            PciTrack track = Track();
            track.SetVelocity(10.2, 1.0, 0.0);
            track.SetVelocity(10.1, 0.0, 0.0);          // older timestamp: applied at 10.2
            Assert.AreEqual(0.0, track.PositionAt(10.5).X, 1e-12);
        }
    }
}
