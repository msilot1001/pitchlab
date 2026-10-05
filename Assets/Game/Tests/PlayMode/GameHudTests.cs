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
using UnityEngine.TestTools.Constraints;

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

        /// <summary>The scorebug is the game; the batter and count are the shown plate appearance's (the finished one, with its
        /// last count, through the result pause; the current one otherwise).</summary>
        private void AssertMatchesGame(string when)
        {
            GameState g = _lab.Game;
            PlateAppearance pa = _hud.Shown;
            Count count = pa.IsComplete ? pa.Pitches[pa.Pitches.Count - 1].Before : g.Count;
            Assert.AreEqual((count.Balls, count.Strikes, g.Outs, g.Bases), (_hud.Balls, _hud.Strikes, _hud.Outs, _hud.Bases), when);
            StringAssert.EndsWith($" {g.AwayScore}", _hud.AwayText, when);
            StringAssert.EndsWith($" {g.HomeScore}", _hud.HomeText, when);
            Assert.AreEqual($"{(g.Half == Half.Top ? "TOP" : "BOT")} {g.Inning}", _hud.InningText, when);
            StringAssert.Contains(pa.Batter.Name, _hud.BatterText, when);
            StringAssert.StartsWith($"#{pa.Slot} ", _hud.BatterText, when);
            Assert.AreEqual(_hud.Plot.Count, pa.Pitches.Count(p => !double.IsNaN(p.Info.PlateX)), when);
            for (int i = 0; i < _hud.Plot.Count; i++)
                Assert.AreEqual((pa.Pitches[i].Info.PlateX, pa.Pitches[i].Info.PlateZ, pa.Pitches[i].Outcome), (_hud.Plot[i].X, _hud.Plot[i].Z, _hud.Plot[i].Outcome), $"{when}: the plot is the recorded crossings");
        }

        [UnityTest]
        public IEnumerator TheHudFollowsThePlateAppearanceAndItsCalls()
        {
            yield return null;
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            Frame(_now);
            AssertMatchesGame("before the first pitch");
            Assert.AreEqual(0, _hud.History.Count);

            // Nothing shown before the pitch is counted (the take waits InputGrace for a late swing).
            _lab.Target = PitchTarget.BallUp;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            At(_lab.CurrentPitch.Flight.Final.Time - 0.02);
            Assert.AreEqual((string.Empty, 0, 0), (_hud.Flash, _hud.Balls, _hud.History.Count));
            At(_lab.CurrentPitch.Flight.Final.Time + HittingLabController.InputGrace / 2.0);
            Assert.AreEqual((string.Empty, 0), (_hud.Flash, _hud.Balls), "not during the grace either");
            At(_lab.CurrentPitch.Flight.Final.Time + HittingLabController.InputGrace + 0.02);
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
            Assert.AreEqual((3, 2), (_hud.Balls, _hud.Strikes), "the walked batter's last count — never 4 balls");
            StringAssert.Contains("Away #1", _hud.BatterText, "he is still the batter until the loop is ready");
            AssertMatchesGame("walk");
            Assert.AreEqual(7, _hud.History.Count, "six pitches and the result");
            Assert.AreEqual("     WALK", _hud.History[6]);
            Assert.AreSame(_lab.Game.Completed[0], _hud.Shown);
            Assert.IsTrue(_hud.Bases.First);
            Ready();
            Assert.AreEqual(0, _hud.History.Count, "the next batter's plate appearance");
            AssertMatchesGame("next batter");
            Assert.AreEqual((0, 0), (_hud.Balls, _hud.Strikes));
            StringAssert.Contains("Away #2", _hud.BatterText);
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
                    if (i == 2)
                    {
                        Assert.AreEqual("STRIKEOUT", _hud.Flash);
                        Assert.AreSame(_lab.Game.Completed[_lab.Game.CompletedPlateAppearances - 1], _hud.Shown, $"batter {batter + 1}: his plate appearance");
                        Assert.AreEqual((4, "     STRIKEOUT"), (_hud.History.Count, _hud.History[3]));
                        StringAssert.Contains($"Away #{batter + 1}", _hud.HistoryTitle);
                        StringAssert.Contains($"Away #{batter + 1}", _hud.BatterText);
                        AssertMatchesGame($"strikeout {batter + 1}");
                    }

                    Ready();
                }

            AssertMatchesGame("bottom 1");
            Assert.AreEqual(("BOT 1", 0), (_hud.InningText, _hud.Outs));
            StringAssert.Contains("Home #1", _hud.BatterText);
            _hud.ShowLog = true;
            CollectionAssert.AreEqual(new[] { "Top 1", "  #1 STRIKEOUT", "  #2 STRIKEOUT", "  #3 STRIKEOUT" }, _hud.EventLog);
            // Bottom 1: four walks force a run in; the log gets the half's header and the run.
            for (int w = 0; w < 4; w++)
                for (int i = 0; i < 4; i++)
                {
                    Take(PitchTarget.BallUp);
                    Ready();
                }

            CollectionAssert.AreEqual(new[] { "Bottom 1", "  #1 WALK", "  #2 WALK", "  #3 WALK", "  #4 WALK (1 R)" }, _hud.EventLog.Skip(4).ToArray());
            // Only the last ten plate appearances, starting with their half's header.
            for (int i = 0; i < 9; i++)
            {
                Take(PitchTarget.Middle);
                Ready();
            }

            Assert.AreEqual(10, _hud.EventLog.Count(l => l.StartsWith("  #")));
            StringAssert.DoesNotStartWith("  ", _hud.EventLog[0], "a header first");
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
            Assert.AreEqual("     " + GameHud.EndText(pa), _hud.History[1]);
            StringAssert.EndsWith(GameHud.EndText(pa), _hud.LastResult);
            AssertMatchesGame("after the play");
        }

        [UnityTest]
        public IEnumerator APressDuringAPlaySkipsItsCallButKeepsItsResult()
        {
            yield return null;
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            _lab.Target = PitchTarget.Middle;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
            Assert.IsTrue(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration).IsContact);
            At(_lab.LastResult.Value.BattedBall.Time + HittingLabController.DoublePressGrace + 0.1);
            _lab.PressSwingButton(_now);   // the next pitch, mid-play: the play's result stands
            Frame(_now + 0.01);
            Assert.AreEqual(string.Empty, _hud.Flash, "no stale call over the new pitch");
            Assert.AreEqual(1, _lab.Game.CompletedPlateAppearances);
            StringAssert.EndsWith(GameHud.EndText(_lab.Game.Completed[0]), _hud.LastResult);
            AssertMatchesGame("new pitch");
        }

        [UnityTest]
        public IEnumerator AnUnchangedHudAllocatesNothing()
        {
            yield return null;
            // The constraint sees allocations at all (otherwise the checks below prove nothing).
            Assert.That(() => { var probe = new byte[256]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory());
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            void Unchanged(string when)
            {
                _hud.Refresh(_now);
                Assert.That(() => { for (int i = 0; i < 200; i++) _hud.Refresh(_now); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), $"{when}: refreshing an unchanged HUD allocates nothing");
            }

            Take(PitchTarget.BallUp);
            Unchanged("ball");
            Ready();
            for (int i = 0; i < 3; i++)
            {
                Take(PitchTarget.BallUp);
                if (i < 2) Ready();
            }

            Assert.AreEqual("WALK", _hud.Flash);
            Unchanged("walk");
            Ready();
            _lab.Target = PitchTarget.Middle;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
            Assert.IsTrue(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration).IsContact);
            At(_lab.LastLive.EndTime + 0.05);
            Assert.AreEqual(GameHud.EndText(_lab.Game.Completed.Last()), _hud.Flash);
            Unchanged("in play");
        }

        [UnityTest]
        public IEnumerator TheHudWorksInEveryCameraMode()
        {
            yield return null;
            var modes = Object.FindFirstObjectByType<GameplayCameraController>();
            foreach (CameraMode m in System.Enum.GetValues(typeof(CameraMode)))
            {
                modes.SetMode(m);
                _lab.Target = PitchTarget.BallUp;
                _lab.PressSwingButton(_now);
                _release = _now + _lab.DeliveryLead;
                // Through the normal player loop (the controller's Update, the HUD's LateUpdate) — no explicit refresh.
                _now = _release + _lab.CurrentPitch.Flight.Final.Time + HittingLabController.InputGrace + 0.05;
                yield return null;                       // the next frame: Update, then the LateUpdates …
                yield return new WaitForEndOfFrame();   // … done
                Assert.IsTrue(_hud.isActiveAndEnabled, m.ToString());
                Assert.AreEqual(_lab.LastEnd == PlateAppearanceEnd.Walk ? "WALK" : "BALL", _hud.Flash,
                    $"{m}: outcome {_lab.LastOutcome} state {_lab.StateAt(_now)} rendered {_lab.RenderedRealtime - _now:0.000} lab {(_hud.Lab == _lab)}");
                AssertMatchesGame(m.ToString());
                _now = _release + _lab.CurrentPitch.Flight.Final.Time + BattingStateMachine.ResultPause + 0.1;
                yield return null;
                yield return new WaitForEndOfFrame();
                Assert.AreEqual(string.Empty, _hud.Flash, m.ToString());
            }

            PitchTarget? target = _lab.Target;
            int preset = _lab.PresetIndex;
            _lab.OnPitchingKey("v");
            Assert.AreEqual((target, preset, false), (_lab.Target, _lab.PresetIndex, _lab.AutoPitch), "the HUD's log key is not a pitcher key");
        }
    }
}
