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

        /// <summary>The TASK-012 location names on the TASK-014 targets.</summary>
        private static PitchTarget T(string location) => location switch
        {
            "Middle" => PitchTarget.Middle, "Up" => PitchTarget.UpMiddle, "Down" => PitchTarget.DownMiddle,
            "In" => PitchTarget.MiddleLeft, "Away" => PitchTarget.MiddleRight, "Ball high" => PitchTarget.BallUp,
            "Ball low" => PitchTarget.BallDown, "Ball in" => PitchTarget.BallLeft, "Ball away" => PitchTarget.BallRight,
            _ => throw new System.ArgumentException(location),
        };

        private static Lineup RightHanded(string team) =>
            new Lineup(team, Enumerable.Range(1, 9).Select(i => new PlayerProfile($"{team}{i}", $"{team} {i}", BatterSide.Right, 73.0, "")).ToArray());

        /// <summary>Throws the next pitch at <paramref name="location"/> and takes it; returns once its result is applied.</summary>
        private void Take(string location)
        {
            _lab.Target = T(location);
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            double end = _lab.CurrentPitch.Flight.Final.Time;
            At(end - 0.01);
            Assert.IsNull(_lab.LastOutcome, "not decided before the pitch is over");
            At(end + HittingLabController.InputGrace - 0.01);
            Assert.IsNull(_lab.LastOutcome, "nor while a late swing event may still arrive");
            At(end + HittingLabController.InputGrace + 0.01);
        }

        [UnityTest]
        public IEnumerator AMissIsCountedWhenThePitchAndTheSwingAreOverAndALateSwingIsIgnored()
        {
            yield return null;
            GameState game = _lab.Game;
            _lab.Target = T("Ball high");
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            Assert.IsFalse(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration - 0.2).IsContact, "far too early");
            double decided = BattingStateMachine.OutcomeTime(pitch, _lab.LastSwing, _lab.LastResult, _lab.PlayEnd, _lab.Swing.SwingDuration);
            At(decided - 0.01);
            Assert.IsNull(_lab.LastOutcome);
            At(decided + 0.01);
            Assert.AreEqual((PitchOutcome.SwingingStrike, new Count(0, 1)), (_lab.LastOutcome.Value, game.Count), "a swing at a ball is a strike");

            // A press stamped just before the end of the pitch but delivered a frame after it still swings (the take waits
            // InputGrace); one delivered after the take was counted cannot swing.
            At(decided + BattingStateMachine.ResultPause + 0.5);
            _lab.Target = T("Ball high");
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            double end = _lab.CurrentPitch.Flight.Final.Time;
            At(end + 0.02);
            _lab.PressSwingButton(_release + end - 0.005);
            Assert.IsTrue(_lab.LastSwing.HasValue, "a late event for an in-time press still swings");
            At(end + _lab.Swing.SwingDuration + HittingLabController.InputGrace + 0.05);
            Assert.AreEqual((PitchOutcome.SwingingStrike, new Count(0, 2)), (_lab.LastOutcome.Value, game.Count));

            At(end + BattingStateMachine.ResultPause + 1.0);
            Take("Ball high");
            Assert.AreEqual(new Count(1, 2), game.Count);
            double end2 = _lab.CurrentPitch.Flight.Final.Time;
            _lab.PressSwingButton(_release + end2 - 0.005);
            Assert.IsNull(_lab.LastSwing, "too late: the take was counted");
            Assert.AreEqual(new Count(1, 2), game.Count);
        }

        [UnityTest]
        public IEnumerator ThrowingDuringAPlayAppliesItsResultOnce()
        {
            yield return null;
            GameState game = _lab.Game;
            game.Set(GameState.Presets.First(p => p.Name == "R1, 0 out"));
            Frame(_now);
            LivePlay play = Swing(0.0, 0.0);
            Assert.AreEqual(LivePlay.BallKind.Hit, play.Kind);
            At(_lab.LastResult.Value.BattedBall.Time + HittingLabController.DoublePressGrace + 0.05);
            Assert.Less(_now - _release, play.EndTime, "still in play");
            _lab.PressSwingButton(_now);   // the next pitch: the play's result stands
            Assert.AreEqual((1, play.ResultingBases()), (game.CompletedPlateAppearances, game.Bases));
            _release = _now + _lab.DeliveryLead;
            At(_lab.CurrentPitch.Flight.Final.Time + HittingLabController.InputGrace + 0.01);
            Assert.AreEqual((1, PitchOutcome.CalledStrike, new Count(0, 1)), (game.CompletedPlateAppearances, _lab.LastOutcome.Value, game.Count), "applied once; the next pitch counts for the next batter");
        }

        [UnityTest]
        public IEnumerator TheNextBatterComesUpFromHisSideWithHisZone()
        {
            yield return null;
            GameState game = _lab.Game;
            PlayerProfile first = game.Batter, second = game.LineupOf(TeamSide.Away)[2];
            Assert.AreEqual((BatterSide.Left, BatterSide.Right), (first.Bats, second.Bats), "the generic order alternates here");
            for (int i = 0; i < 4; i++)
            {
                Take("Ball high");
                Assert.AreSame(first, _lab.PitchBatter, "the same batter throughout his plate appearance");
                Assert.IsTrue(_view.Batter.LeftHanded, "a left-handed batter stands on the first-base side");
                Assert.Greater(_view.Batter.transform.position.x, 0f);
                Assert.AreEqual(BatterSide.Left, _lab.Swing.Side);
            }

            Assert.AreEqual((PlateAppearanceEnd.Walk, second), (_lab.LastEnd, game.Batter));
            Assert.IsTrue(_view.Batter.LeftHanded, "the walk shows with the batter who drew it");
            At(_lab.CurrentPitch.Flight.Final.Time + BattingStateMachine.ResultPause + 0.05);
            Assert.IsFalse(_view.Batter.LeftHanded, "then the next batter steps in, before the next pitch");
            Assert.Less(_view.Batter.transform.position.x, 0f);
            Take("Middle");
            Assert.AreSame(second, _lab.PitchBatter);
            Assert.AreEqual(BatterSide.Right, _lab.Swing.Side, "the swing is his");
            Assert.IsFalse(_view.Batter.LeftHanded);
            Assert.Less(_view.Batter.transform.position.x, 0f, "on the third-base side");
            Assert.AreEqual((second.ZoneBottom, second.ZoneTop), _lab.Zone, "called against his zone");
            Assert.AreNotEqual(first.ZoneTop, second.ZoneTop);
            Assert.AreEqual(new Count(0, 1), game.Count);
        }

        [UnityTest]
        public IEnumerator ANewBatterFromTheSameSideStepsInInHisStance()
        {
            yield return null;
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            Frame(_now);
            Vector3 stance = _view.Batter.GloveAnchor.position;
            // The first batter swings through three pitches: his finish is still up when the loop is ready again.
            for (int i = 0; i < 3; i++)
            {
                _lab.Target = T("Ball high");
                _lab.PressSwingButton(_now);
                _release = _now + _lab.DeliveryLead;
                HittingPitch pitch = _lab.CurrentPitch;
                Assert.IsFalse(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration - 0.25).IsContact);
                double ready = BattingStateMachine.OutcomeTime(pitch, _lab.LastSwing, _lab.LastResult, _lab.PlayEnd, _lab.Swing.SwingDuration) + BattingStateMachine.ResultPause;
                At(ready - 0.05);
                if (i < 2) Assert.Greater(Vector3.Distance(stance, _view.Batter.GloveAnchor.position), 0.1f, "still in his finish");
                At(ready + 0.05);
            }

            Assert.AreEqual((PlateAppearanceEnd.Strikeout, "Away2"), (_lab.LastEnd, _lab.Game.Batter.Id));
            Assert.Less(Vector3.Distance(stance, _view.Batter.GloveAnchor.position), 1e-3f, "the next batter (same side) in his stance, not the last one's finish");
        }

        [UnityTest]
        public IEnumerator TheNextBatterComesUpOnlyWhenThePlayIsOver()
        {
            yield return null;
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            GameState game = _lab.Game;
            PlayerProfile batter = game.Batter;
            LivePlay play = Swing(0.0, 0.0);
            Assert.AreEqual(LivePlay.BallKind.Hit, play.Kind);
            At(play.EndTime - 0.01);
            Assert.AreEqual((batter, 0, 0), (game.Batter, game.CompletedPlateAppearances, game.Current.Pitches.Count), "still his plate appearance while the ball is live (the pitch is recorded with its play)");
            At(play.EndTime + 0.01);
            Assert.AreEqual((1, 2), (game.CompletedPlateAppearances, game.Current.Slot));
            Assert.AreEqual(PlateAppearanceEnd.InPlay, game.Completed[0].End);
            Assert.AreEqual(1, game.Completed[0].Pitches.Count);
            Assert.AreEqual(Units.MetersPerSecondToMph(_lab.CurrentPitch.Flight.First.Velocity.Length), game.Completed[0].Pitches[0].Info.SpeedMph, 1e-9, "the pitch is recorded as thrown");
        }

        [UnityTest]
        public IEnumerator ThePlateAppearanceDoesNotDependOnTheFrameSchedule()
        {
            yield return null;
            string Run(double frame)
            {
                _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
                GameState game = _lab.Game;
                foreach (string location in new[] { "Ball high", "Middle", "Ball low", "Down", "Ball in", "Ball away", "Up", "Middle", "Middle", "Middle", "Middle", "Middle" })
                {
                    _lab.Target = T(location);
                    _lab.PressSwingButton(_now);
                    _release = _now + _lab.DeliveryLead;
                    HittingPitch pitch = _lab.CurrentPitch;
                    double end = _release + pitch.Flight.Final.Time + BattingStateMachine.ResultPause + 0.2;
                    if (game.Current.Pitches.Count == 0 && (game.CompletedPlateAppearances == 2 || game.CompletedPlateAppearances == 3))
                    {
                        // Swing (contact) at the third and fourth batters' first pitches; the fourth's play is cut short by
                        // the next press, mid-play.
                        _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
                        Assert.IsTrue(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration).IsContact);
                        end = game.CompletedPlateAppearances == 2 ? _release + _lab.PlayEnd + BattingStateMachine.ResultPause + 0.2
                            : _release + _lab.LastResult.Value.BattedBall.Time + 1.0;
                    }

                    while (_now < end) Frame(_now + frame);
                }

                return string.Join("|", game.Log) + "#" + game + "#" + string.Join(",", game.Completed.SelectMany(p => p.Pitches).Concat(game.Current.Pitches).Select(p => p.ToString()));
            }

            string at30 = Run(1.0 / 30.0), at144 = Run(1.0 / 144.0);
            StringAssert.Contains("walk", at30);
            StringAssert.Contains("IN PLAY", at30);
            Assert.IsTrue(_lab.Game.Completed.Any(p => p.End == PlateAppearanceEnd.InPlay && p.PlayResult.HasValue), "the play's result is recorded");
            StringAssert.Contains(PlayResults.Describe(_lab.Game.Completed.First(p => p.PlayResult.HasValue).PlayResult.Value), at30, "and logged");
            Assert.AreEqual(at30, at144);
        }

        [UnityTest]
        public IEnumerator ThePitchIsRecordedAsThrownEvenIfTheSelectionChanges()
        {
            yield return null;
            _lab.PresetIndex = 0;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            string thrown = HittingLabController.PresetLabel(0);
            At(0.2);
            _lab.PresetIndex = 3;   // the next pitch chosen while this one is in flight
            At(_lab.CurrentPitch.Flight.Final.Time + HittingLabController.InputGrace + 0.01);
            Assert.AreEqual(thrown, _lab.Game.Current.Pitches[0].Info.Label);
            Assert.AreNotEqual(thrown, HittingLabController.PresetLabel(3));
        }

        [UnityTest]
        public IEnumerator ANewGameDropsThePitchInFlight()
        {
            yield return null;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            At(0.2);
            var next = new GameState();
            _lab.NewGame(next);
            At(3.0);
            _lab.PressSwingButton(_now);   // the first pitch of the new game
            Assert.AreEqual((0, 0, new Count()), (next.Current.Pitches.Count, next.CompletedPlateAppearances, next.Count), "the old pitch never reaches it");
            _release = _now + _lab.DeliveryLead;
            At(_lab.CurrentPitch.Flight.Final.Time + HittingLabController.InputGrace + 0.01);
            Assert.AreEqual(1, next.Current.Pitches.Count);
            Assert.AreEqual(1, next.Current.Pitches[0].Number);
        }

        [UnityTest]
        public IEnumerator TheCallUsesTheBattersOwnZone()
        {
            yield return null;
            // A very tall leadoff man: the "up" target of his zone is 8 cm above the default zone's top — a strike for him only.
            PlayerProfile Make(string id, double height) => new PlayerProfile(id, id, BatterSide.Right, height, "");
            var away = new Lineup("Away", new[] { Make("T", 92.0) }.Concat(Enumerable.Range(2, 8).Select(i => Make($"A{i}", 73.0))).ToArray());
            _lab.NewGame(new GameState(away, RightHanded("Home")));
            Take("Up");
            (double x, double z) = StrikeZone.Crossing(_lab.CurrentPitch);
            Assert.IsFalse(StrikeZone.Contains(x, z), "outside the default zone");
            Assert.AreEqual(PitchOutcome.CalledStrike, _lab.LastOutcome, "inside his");
        }

        [UnityTest]
        public IEnumerator TakenPitchesAreCalledAndCountedUntilAWalkOrAStrikeout()
        {
            yield return null;
            GameState game = _lab.Game;
            game.Set(GameState.Presets.First(p => p.Name == "R1, 1 out"));
            Frame(_now);
            Take("Ball high");
            Assert.AreEqual(PitchOutcome.Ball, _lab.LastOutcome);
            Assert.AreEqual(new Count(1, 0), game.Count);
            Assert.AreEqual("Take · ball", _view.Feedback(_lab.RenderedSimTime));
            Take("Middle");
            Assert.AreEqual((PitchOutcome.CalledStrike, new Count(1, 1)), (_lab.LastOutcome.Value, game.Count));
            Take("Ball away");
            Take("Ball low");
            Assert.AreEqual(new Count(3, 1), game.Count);
            Assert.IsTrue(_lab.EditorLocked, "locked while the result shows");
            At(_lab.CurrentPitch.Flight.Final.Time + BattingStateMachine.ResultPause + 0.01);
            Assert.IsFalse(_lab.EditorLocked, "between pitches the editor works");
            Take("Ball in");
            Assert.AreEqual(PlateAppearanceEnd.Walk, _lab.LastEnd);
            Assert.AreEqual((1, new BaseOccupancy(true, true, false), 1, new Count()), (game.Outs, game.Bases, game.CompletedPlateAppearances, game.Count), "walked: the runner forced to second");

            // The next batter strikes out looking; the runners lead off again from where the walk put them.
            foreach (string strike in new[] { "Up", "Down", "Away" })
            {
                Take(strike);
                Assert.AreEqual(PitchOutcome.CalledStrike, _lab.LastOutcome, strike);
            }

            Assert.AreEqual(PlateAppearanceEnd.Strikeout, _lab.LastEnd);
            Assert.AreEqual((2, new BaseOccupancy(true, true, false), 2), (game.Outs, game.Bases, game.CompletedPlateAppearances));
        }

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
            Assert.AreEqual(0, game.CompletedPlateAppearances);
            Assert.IsTrue(_lab.EditorLocked);
            At(play.EndTime + 0.01);
            Assert.AreEqual(1, game.CompletedPlateAppearances);
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
            // A right-handed batter (the scripted swing's liner is his).
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
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
