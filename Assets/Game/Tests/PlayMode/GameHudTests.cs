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
    /// <summary>
    /// TASK-015: the GameLab HUD shows the authoritative game — count, outs, bases, score, inning, batter, the pitches of
    /// the plate appearance and the calls — and never anything else (it only reads). No pixel tests: its view model.
    /// </summary>
    public class GameHudTests
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
            Assert.IsNotNull(_hud, "the GameLab has a HUD");
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

        private static Lineup RightHanded(string team) =>
            new Lineup(team, Enumerable.Range(1, 9).Select(i => new PlayerProfile($"{team}{i}", $"{team} #{i}", BatterSide.Right, 73.0, "")).ToArray());

        /// <summary>Throws a pitch at <paramref name="target"/>, takes it, and stops just after its result is counted.</summary>
        private void Take(PitchTarget target)
        {
            _lab.Target = target;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            At(_lab.CurrentPitch.Flight.Final.Time + HittingLabController.InputGrace + 0.02);
        }

        private void Ready() => At(_lab.CurrentPitch.Flight.Final.Time + BattingStateMachine.ResultPause + 0.05);

        private void AssertMatchesGame(string when)
        {
            GameState g = _lab.Game;
            Assert.AreEqual((g.Count.Balls, g.Count.Strikes, g.Outs, g.Bases), (_hud.Balls, _hud.Strikes, _hud.Outs, _hud.Bases), when);
            StringAssert.EndsWith($" {g.AwayScore}", _hud.AwayText, when);
            StringAssert.EndsWith($" {g.HomeScore}", _hud.HomeText, when);
            Assert.AreEqual($"{(g.Half == Half.Top ? "TOP" : "BOT")} {g.Inning}", _hud.InningText, when);
            StringAssert.Contains(g.Batter.Name, _hud.BatterText, when);
            StringAssert.StartsWith($"#{g.Current.Slot} ", _hud.BatterText, when);
        }

        [UnityTest]
        public IEnumerator TheHudFollowsThePlateAppearanceAndItsCalls()
        {
            yield return null;
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            Frame(_now);
            AssertMatchesGame("before the first pitch");
            Assert.AreEqual(0, _hud.History.Count);

            Take(PitchTarget.BallUp);
            AssertMatchesGame("1–0");
            Assert.AreEqual("BALL", _hud.Flash, "the call shows through the result pause");
            Assert.AreEqual(1, _hud.History.Count);
            StringAssert.EndsWith("BALL", _hud.History[0]);
            StringAssert.Contains("Four-Seam", _hud.History[0]);
            Ready();
            Assert.AreEqual(string.Empty, _hud.Flash, "and is gone when the next pitch can be thrown");

            Take(PitchTarget.Middle);
            Assert.AreEqual("CALLED STRIKE", _hud.Flash);
            Ready();
            foreach (PitchTarget t in new[] { PitchTarget.BallDown, PitchTarget.BallLeft, PitchTarget.UpLeft })
            {
                Take(t);
                Ready();
            }

            AssertMatchesGame("3–2");
            Assert.AreEqual((3, 2, 5), (_hud.Balls, _hud.Strikes, _hud.History.Count));

            // Ball four: WALK, the finished plate appearance's pitches still shown, the count already the next batter's 0–0.
            Take(PitchTarget.BallRight);
            Assert.AreEqual("WALK", _hud.Flash);
            Assert.AreEqual((0, 0), (_hud.Balls, _hud.Strikes), "no count past ball four");
            Assert.AreEqual(7, _hud.History.Count, "six pitches and the result");
            Assert.AreEqual("     WALK", _hud.History[6]);
            Assert.AreSame(_lab.Game.Completed[0], _hud.Shown);
            Assert.IsTrue(_hud.Bases.First);
            Ready();
            Assert.AreEqual(0, _hud.History.Count, "the next batter's plate appearance");
            AssertMatchesGame("next batter");
            StringAssert.Contains("WALK", _hud.LastResult);
            StringAssert.Contains("Away #1", _hud.LastResult);
        }

        [UnityTest]
        public IEnumerator StrikeoutsAndTheHalfInningShow()
        {
            yield return null;
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            for (int batter = 0; batter < 3; batter++)
                for (int i = 0; i < 3; i++)
                {
                    Take(PitchTarget.Middle);
                    if (i == 2) Assert.AreEqual("STRIKEOUT", _hud.Flash);
                    Ready();
                }

            AssertMatchesGame("bottom 1");
            Assert.AreEqual(("BOT 1", 0), (_hud.InningText, _hud.Outs));
            StringAssert.Contains("Home #1", _hud.BatterText);
            _hud.ShowLog = true;
            CollectionAssert.AreEqual(new[] { "Top 1", "  #1 STRIKEOUT", "  #2 STRIKEOUT", "  #3 STRIKEOUT" }, _hud.EventLog);
        }

        [UnityTest]
        public IEnumerator ABallInPlayShowsItsResult()
        {
            yield return null;
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            _lab.Target = PitchTarget.Middle;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
            Assert.IsTrue(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration).IsContact);
            LivePlay play = _lab.LastLive;
            At(play.EndTime - 0.01);
            Assert.AreEqual(string.Empty, _hud.Flash, "no result call before the play is over");
            At(play.EndTime + 0.02);
            PlateAppearance pa = _lab.Game.Completed.Single();
            Assert.AreEqual(GameHud.EndText(pa), _hud.Flash);
            Assert.AreEqual(PlayResults.Describe(PlayResults.Classify(play)).ToUpperInvariant(), _hud.Flash);
            StringAssert.EndsWith("IN PLAY", _hud.History[0]);
            AssertMatchesGame("after the play");
        }

        [UnityTest]
        public IEnumerator AnUnchangedHudAllocatesNothing()
        {
            yield return null;
            Take(PitchTarget.BallUp);
            _hud.Refresh(_now);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) _hud.Refresh(_now);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before, "refreshing an unchanged HUD allocates nothing");
        }

        [UnityTest]
        public IEnumerator TheHudWorksInEveryCameraMode()
        {
            yield return null;
            var modes = Object.FindFirstObjectByType<GameplayCameraController>();
            foreach (CameraMode m in System.Enum.GetValues(typeof(CameraMode)))
            {
                modes.SetMode(m);
                Take(PitchTarget.BallUp);
                AssertMatchesGame(m.ToString());
                Assert.AreEqual(_lab.LastEnd == PlateAppearanceEnd.Walk ? "WALK" : "BALL", _hud.Flash, m.ToString());
                Ready();
            }
        }
    }
}
