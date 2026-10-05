using System.Collections;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Presentation;
using Pitchlab.Sandbox;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    /// <summary>TASK-016 in the production GameLab: the end of a game (walk-off, home team ahead), the final, and a new game.</summary>
    public class GameFlowSceneTests
    {
        private HittingLabController _lab;
        private HittingLabPresentation _view;
        private GameHud _hud;
        private double _now, _release;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("GameLab", LoadSceneMode.Single);
            yield return null;
            _lab = Object.FindFirstObjectByType<HittingLabController>();
            _view = Object.FindFirstObjectByType<HittingLabPresentation>();
            _hud = Object.FindFirstObjectByType<GameHud>();
            _now = Time.realtimeSinceStartupAsDouble + 100.0;
            _lab.Clock = () => _now;
        }

        private void Frame(double realtime)
        {
            _now = realtime;
            _lab.FrameUpdate(_now);
            _view.FrameUpdate();
            _hud.Refresh(_now);
        }

        private void At(double simTime) => Frame(_release + simTime);

        private void Take(PitchTarget target)
        {
            _lab.Target = target;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            At(_lab.CurrentPitch.Flight.Final.Time + HittingLabController.InputGrace + 0.02);
        }

        private void Ready() => At(_lab.CurrentPitch.Flight.Final.Time + BattingStateMachine.ResultPause + 0.05);

        [UnityTest]
        public IEnumerator AWalkOffEndsTheGameAndAPressStartsANewOne()
        {
            yield return null;
            var modes = Object.FindFirstObjectByType<GameplayCameraController>();
            modes.SetMode(CameraMode.Offset);
            GameState game = _lab.Game;
            game.Set(9, Half.Bottom, 1, BaseOccupancy.Loaded, 3, 3);
            for (int i = 0; i < 3; i++)
            {
                Take(PitchTarget.BallUp);
                Ready();
            }

            Take(PitchTarget.BallUp);
            Assert.IsTrue(game.IsOver, "ball four forces in the winning run");
            Assert.AreEqual((3, 4, "walk-off"), (game.Result.Away, game.Result.Home, game.Result.Reason));
            Assert.AreEqual("WALK", _hud.Flash, "the call first");
            int thrown = _lab.PitchesThrown;
            _lab.PressSwingButton(_now);
            Assert.AreEqual(thrown, _lab.PitchesThrown, "no pitch after the end");
            Ready();
            StringAssert.StartsWith("FINAL", _hud.FinalText);
            StringAssert.Contains("AWAY 3", _hud.FinalText);
            StringAssert.Contains("HOME 4", _hud.FinalText);
            StringAssert.Contains("walk-off", _hud.FinalText);
            // A press right as the final appears does nothing (no mashing through it) …
            double ready = _lab.CurrentPitch.Flight.Final.Time + BattingStateMachine.ResultPause;
            At(ready + 0.1);
            _lab.PressSwingButton(_now);
            Assert.AreSame(game, _lab.Game, "not while the final has only just appeared");
            // … auto pitching does not go on either …
            _lab.AutoPitch = true;
            for (int i = 0; i < 300; i++) Frame(_now + 1.0 / 30.0);
            Assert.AreEqual(thrown, _lab.PitchesThrown);
            _lab.AutoPitch = false;
            // … and a press on the final once it has been up a moment starts a new standard game, from the top.
            Assert.Greater(_now - _release, ready + HittingLabController.FinalDwell);
            _lab.PressSwingButton(_now);
            Frame(_now + 0.01);
            GameState fresh = _lab.Game;
            Assert.AreNotSame(game, fresh);
            Assert.AreEqual((GameStatus.Pregame, 1, Half.Top, 0, BaseOccupancy.Empty, 0, 0), (fresh.Status, fresh.Inning, fresh.Half, fresh.Outs, fresh.Bases, fresh.AwayScore, fresh.HomeScore));
            Assert.AreEqual((1, 1, 0), (fresh.UpNext(TeamSide.Away), fresh.UpNext(TeamSide.Home), fresh.CompletedPlateAppearances));
            Assert.IsNull(_lab.CurrentPitch, "no pitch or play carried over");
            Assert.AreEqual(string.Empty, _hud.FinalText);
            Assert.AreEqual(0, _hud.History.Count);
            Assert.IsFalse(Object.FindFirstObjectByType<BaseballCamera>().IsFollowing, "the camera back on the batter");
            Assert.AreEqual(CameraMode.Offset, modes.Mode, "the player's camera mode is kept");
            Take(PitchTarget.Middle);
            Assert.AreEqual((1, new Count(0, 1)), (fresh.Current.Number, fresh.Count), "and it plays");
            Assert.AreEqual(string.Empty, _hud.FinalText);
        }

        [UnityTest]
        public IEnumerator NewGameMidPlayNeverAppliesTheOldPlay()
        {
            yield return null;
            _lab.Target = PitchTarget.Middle;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
            Assert.IsTrue(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration).IsContact);
            LivePlay play = _lab.LastLive;
            At(play.ContactTime + 0.5);
            _lab.NewGame();   // the editor's NEW GAME mid-play
            GameState fresh = _lab.Game;
            for (int i = 0; i < 60; i++) Frame(_now + 0.2);
            Assert.AreEqual((0, 0, 0), (fresh.CompletedPlateAppearances, fresh.AwayScore, fresh.Current.Pitches.Count), "the old play never reaches the new game");
        }

        [UnityTest]
        public IEnumerator AutoPitchingCarriesOnIntoANewGame()
        {
            yield return null;
            _lab.AutoPitch = true;
            _lab.NewGame();
            int thrown = _lab.PitchesThrown;
            Frame(_now + 0.05);
            Assert.AreEqual(thrown, _lab.PitchesThrown, "not at once");
            for (int i = 0; i < 120; i++) Frame(_now + 1.0 / 60.0);
            Assert.Greater(_lab.PitchesThrown, thrown, "it starts by itself after the delay");
            _lab.AutoPitch = false;
        }

        [UnityTest]
        public IEnumerator TheHomeTeamAheadAfterTheTopOfTheNinthWins()
        {
            yield return null;
            GameState game = _lab.Game;
            game.Set(9, Half.Top, 2, BaseOccupancy.Empty, 2, 5);
            for (int i = 0; i < 3; i++)
            {
                Take(PitchTarget.Middle);
                Ready();
            }

            Assert.IsTrue(game.IsOver);
            Assert.AreEqual((TeamSide.Home, Half.Top), (game.Result.Winner, game.Result.Half));
            StringAssert.Contains("HOME 5", _hud.FinalText);
            StringAssert.Contains("top of the 9th", _hud.FinalText);
        }
    }
}
