using System.Collections;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Presentation;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-010 GameLab: plate appearances played in a persistent half-inning on the production systems — the result applied
    /// when the play is over, the editor locked during a play, runners leading off between plays exactly where the next play
    /// starts them, the defense in the situation's alignment, the half-inning change. Frozen, test-controlled clock.
    /// </summary>
    public class GameLabSceneTests
    {
        private HittingLabController _lab;
        private HittingLabPresentation _view;
        private double _now, _release;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("GameLab", LoadSceneMode.Single);
            yield return null;
            _lab = Object.FindFirstObjectByType<HittingLabController>();
            _view = Object.FindFirstObjectByType<HittingLabPresentation>();
            Assert.IsNotNull(_lab.Game, "the GameLab plays a game");
            Assert.IsNotNull(Object.FindFirstObjectByType<GameLabPanel>());
            _now = Time.realtimeSinceStartupAsDouble + 100.0;
            _lab.Clock = () => _now;
        }

        private void Frame(double realtime)
        {
            _now = realtime;
            _lab.FrameUpdate(_now);
            _view.FrameUpdate();
        }

        private void At(double simTime) => Frame(_release + simTime);

        /// <summary>Throws the next pitch now and swings at it with the given timing offset and PCI height offset.</summary>
        private LivePlay Swing(double late, double high)
        {
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z + high);
            Assert.IsTrue(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration + late).IsContact);
            return _lab.LastLive;
        }

        private Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        [UnityTest]
        public IEnumerator ThePlaysResultIsAppliedWhenItIsOverAndRunnersLeadOff()
        {
            yield return null;
            GameState game = _lab.Game;
            game.Set(GameState.Presets.First(p => p.Name == "R1, 0 out"));
            Frame(_now);
            Assert.IsFalse(_lab.EditorLocked, "ready: the editor works");
            LivePlay play = Swing(0.0, 0.0);
            Assert.AreEqual(new Situation(0, new BaseOccupancy(true, false, false)).Bases, play.Situation.Bases, "played from the game's situation");
            Assert.AreEqual(DefensiveAlignment.DoublePlayDepth[DefensivePosition.Shortstop], play.Fielding.Motion(DefensivePosition.Shortstop).Start, "from double-play depth");
            Assert.IsTrue(_lab.EditorLocked, "locked during the pitch and the play");

            // Applied exactly when the play is over, not before.
            At(play.EndTime - 0.01);
            Assert.AreEqual(0, game.PlateAppearance);
            Assert.IsTrue(_lab.EditorLocked);
            At(play.EndTime + 0.01);
            Assert.AreEqual(1, game.PlateAppearance);
            Assert.AreEqual(play.ResultingBases(), game.Bases);
            Assert.AreEqual(play.Outs, game.Outs);

            // The fielders jog back during the result pause: in the new situation's alignment when the batter is ready.
            double over = _release + play.EndTime + HittingLabPresentation.ResultPause + 0.01;
            Frame(over);
            Assert.IsFalse(_lab.EditorLocked, "ready again");
            foreach (DefensivePosition p in new[] { DefensivePosition.FirstBase, DefensivePosition.SecondBase, DefensivePosition.ThirdBase, DefensivePosition.Shortstop, DefensivePosition.LeftField, DefensivePosition.CenterField, DefensivePosition.RightField })
                Assert.Less(Vector3.Distance(Flat(_view.Defense.Figure(p).transform.position), Flat(SimulationSpace.ToUnity(game.Situation.Alignment[p]))), 1e-3f, $"{p} in position");

            // The next pitch thrown at once: the runners left on base walk to their leads before contact — exactly where the
            // next play starts them — and nobody jumps at contact.
            Base[] on = new[] { Base.First, Base.Second, Base.Third }.Where(game.Bases.IsOccupied).ToArray();
            Assert.IsNotEmpty(on, "somebody is on base");
            LivePlay next = Swing(0.006, 0.02);
            At(next.ContactTime - 0.001);
            foreach (Base b in on)
            {
                Vector3 lead = SimulationSpace.ToUnity(BaseLeg.Of(b, false).PositionAt(LivePlay.Lead(b)));
                Assert.Less(Vector3.Distance(Flat(_view.Runners.Figure(new Runner(b)).transform.position), Flat(lead)), 1e-3f, $"leading off {b}");
            }

            var fielders = new[] { DefensivePosition.SecondBase, DefensivePosition.Shortstop, DefensivePosition.CenterField };
            Vector3[] runnersBefore = on.Select(b => Flat(_view.Runners.Figure(new Runner(b)).transform.position)).ToArray();
            Vector3[] fieldersBefore = fielders.Select(p => Flat(_view.Defense.Figure(p).transform.position)).ToArray();
            At(next.ContactTime + 0.001);
            for (int i = 0; i < on.Length; i++)
                Assert.Less(Vector3.Distance(runnersBefore[i], Flat(_view.Runners.Figure(new Runner(on[i])).transform.position)), 0.05f, $"runner from {on[i]}");
            for (int i = 0; i < fielders.Length; i++)
                Assert.Less(Vector3.Distance(fieldersBefore[i], Flat(_view.Defense.Figure(fielders[i]).transform.position)), 0.05f, $"{fielders[i]}");
        }

        [UnityTest]
        public IEnumerator TheNextPitchIsThrownWithTheBall()
        {
            // Regression: after a play the fielder who had the ball keeps it while he jogs back — but the next pitch must
            // start from the pitcher's hand and fly on the pitch trajectory, not stay in that fielder's glove.
            yield return null;
            LivePlay play = Swing(0.0, 0.0);
            Assert.IsNotNull(play.Defense.HolderAt(double.MaxValue), "a fielder ends the play with the ball");
            for (double t = play.ContactTime; t < play.EndTime + HittingLabPresentation.ResultPause; t += 0.25) At(t);   // the play as shown
            double over = _release + play.EndTime + HittingLabPresentation.ResultPause + 0.01;
            Frame(over);
            Frame(over + 2.0);
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            At(-0.4);
            Assert.Less(Vector3.Distance(_view.Pitcher.BallAnchor.position, _lab.BallTransform.position), 1e-4f, "in the pitcher's hand");
            At(0.2);
            Vector3 pitch = SimulationSpace.ToUnity(_lab.CurrentPitch.Flight.StateAt(_lab.RenderedSimTime).Position);
            Assert.Less(Vector3.Distance(pitch, _lab.BallTransform.position), 1e-4f, "on the pitch trajectory");
        }

        [UnityTest]
        public IEnumerator TheThirdOutChangesTheHalfInning()
        {
            yield return null;
            GameState game = _lab.Game;
            game.Set(4, Half.Top, 1, BaseOccupancy.Loaded, 2, 3);
            Frame(_now);
            LivePlay play = Swing(-0.004, -0.01);   // a liner to short: caught, a runner doubled off
            Assert.AreEqual(3, play.Outs);
            At(play.EndTime + 0.01);
            Assert.AreEqual((4, Half.Bottom, 0, BaseOccupancy.Empty), (game.Inning, game.Half, game.Outs, game.Bases));
            Assert.AreEqual((2, 3), (game.AwayScore, game.HomeScore), "no run on the third out");
            // Reset PA puts the situation back to replay it.
            Frame(_release + play.EndTime + HittingLabPresentation.ResultPause + 0.1);
            Assert.IsFalse(_lab.EditorLocked);
            game.ResetPlateAppearance();
            Assert.AreEqual((4, Half.Top, 1, BaseOccupancy.Loaded), (game.Inning, game.Half, game.Outs, game.Bases));
        }
    }
}
