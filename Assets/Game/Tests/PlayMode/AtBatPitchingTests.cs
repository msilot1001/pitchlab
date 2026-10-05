using System.Collections;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Presentation;
using Pitchlab.Sandbox;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Field;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-014 in the production GameLab: pitch commands (type + target), the automatic pitcher's deterministic cadence,
    /// manual/auto switching, long plate appearances that stay visually stable, the catcher receiving taken pitches.
    /// Frozen, test-controlled clock.
    /// </summary>
    public class AtBatPitchingTests
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

        private static Lineup RightHanded(string team) =>
            new Lineup(team, Enumerable.Range(1, 9).Select(i => new PlayerProfile($"{team}{i}", $"{team} {i}", BatterSide.Right, 73.0, "")).ToArray());

        /// <summary>Runs frames of <paramref name="step"/> s until <paramref name="until"/>, taking every pitch (auto pitcher).</summary>
        private void RunAuto(double step, double until)
        {
            while (_now < until) Frame(_now + step);
        }

        [UnityTest]
        public IEnumerator TheAutoPitcherThrowsEachPitchOnceOnItsOwnScheduleAtAnyFrameRate()
        {
            yield return null;
            string Run(double step, out int thrown, out int recorded)
            {
                _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
                _lab.AutoPitchSeed = 5;
                _lab.AutoPitch = true;
                int before = _lab.PitchesThrown;
                double start = _now;
                _lab.PressSwingButton(_now);   // the first pitch is the player's press; the rest come by themselves
                RunAuto(step, start + 60.0);
                _lab.AutoPitch = false;
                GameState g = _lab.Game;
                thrown = _lab.PitchesThrown - before;
                recorded = g.Completed.Sum(p => p.Pitches.Count) + g.Current.Pitches.Count;
                return string.Join("|", g.Completed.SelectMany(p => p.Pitches).Concat(g.Current.Pitches)
                    .Select(p => $"{p.Info.Label}@{p.Info.PlateX:0.0000},{p.Info.PlateZ:0.0000}:{p.Outcome}")) + "#" + g;
            }

            string a = Run(1.0 / 30.0, out int thrownA, out int recordedA);
            string b = Run(1.0 / 144.0, out int thrownB, out int recordedB);
            Assert.AreEqual(a, b, "the same pitches, results and game at 30 and 144 fps");
            Assert.Greater(recordedA, 8, "a steady cadence: many pitches in a minute");
            // Each press throws exactly one pitch; at most the last one is still in flight (not yet recorded).
            Assert.That(thrownA - recordedA, Is.InRange(0, 1));
            Assert.AreEqual(thrownA, thrownB);
            Assert.Greater(a.Split('|').Select(p => p.Split('@')[0]).Distinct().Count(), 1, "more than one pitch type");
        }

        [UnityTest]
        public IEnumerator SwitchingAutoOffStopsItAndOnResumesIt()
        {
            yield return null;
            _lab.AutoPitch = true;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            RunAuto(1.0 / 60.0, _now + 12.0);
            int thrown = _lab.PitchesThrown;
            Assert.Greater(thrown, 2);
            _lab.OnPitchingKey("p");   // manual
            Assert.IsFalse(_lab.AutoPitch);
            RunAuto(1.0 / 60.0, _now + 10.0);
            Assert.AreEqual(thrown, _lab.PitchesThrown, "manual: nothing is thrown without a press");
            // Manual pitch selection from the pitcher keys (no arrows).
            _lab.OnPitchingKey("comma");
            Assert.AreEqual(PitchTarget.DownRight, _lab.Target);
            _lab.OnPitchingKey("7");
            Assert.AreEqual(PitchTarget.BallUp, _lab.Target);
            int preset = _lab.PresetIndex;
            _lab.OnPitchingKey("x");
            Assert.AreEqual((preset + 1) % PitchPresetsCount, _lab.PresetIndex);
            _lab.PressSwingButton(_now);
            Assert.AreEqual(thrown + 1, _lab.PitchesThrown);
            Assert.Less(StrikeZone.Crossing(_lab.CurrentPitch).Z - _lab.Zone.Top, 0.4);
            Assert.Greater(StrikeZone.Crossing(_lab.CurrentPitch).Z, _lab.Zone.Top + 0.1, "thrown where the keys aimed it");
            _lab.OnPitchingKey("p");   // auto again: resumes after this pitch
            RunAuto(1.0 / 60.0, _now + 10.0);
            Assert.Greater(_lab.PitchesThrown, thrown + 2);
        }

        private static int PitchPresetsCount => Simulation.Pitching.PitchPresets.All.Length;

        /// <summary>A swing time (sim s) at <paramref name="pitch"/> that makes contact and is called foul.</summary>
        private double FoulSwing(HittingPitch pitch)
        {
            var swing = _lab.Swing;
            for (double late = 0.006; late < 0.05; late += 0.002)
            {
                var input = new SwingInput(pitch.IdealContactTime - swing.SwingDuration + late, pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
                ContactResult r = ContactResolver.Resolve(pitch, input, swing);
                if (!r.IsContact) continue;
                BallInPlay play = BallInPlaySimulation.Run(r.BattedBall, EnvironmentState.Standard, FieldLayout.Standard);
                if (FairFoul.Call(play).Call == BallInPlayCall.Foul && r.LaunchAngleDegrees > 15.0) return input.StartTime;
            }

            Assert.Fail("no foul swing found");
            return 0.0;
        }

        [UnityTest]
        public IEnumerator ALongPlateAppearanceStaysStable()
        {
            yield return null;
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            GameState game = _lab.Game;
            // B B S S (2–2), four fouls, B (3–2), two fouls, B: a walk on the 12th pitch.
            char[] script = "BBSSFFFFBFFB".ToCharArray();
            Vector3 pitcherSet = Vector3.zero, cameraReady = Vector3.zero;
            for (int i = 0; i < script.Length; i++)
            {
                _lab.Target = script[i] == 'B' ? PitchTarget.BallUp : PitchTarget.Middle;
                _lab.PresetIndex = 0;
                _lab.PressSwingButton(_now);
                _release = _now + _lab.DeliveryLead;
                HittingPitch pitch = _lab.CurrentPitch;
                At(-_lab.DeliveryLead + 0.02);
                if (i == 0) pitcherSet = _view.Pitcher.transform.position;
                Assert.Less(Vector3.Distance(pitcherSet, _view.Pitcher.transform.position), 0.01f, $"pitch {i + 1}: the pitcher starts from the same set position");
                At(0.0);
                Assert.Less(Vector3.Distance(_lab.BallTransform.position, SimulationSpace.ToUnity(pitch.Flight.First.Position)), 1e-3f, $"pitch {i + 1}: the ball leaves from the release point");
                Assert.AreEqual(1, Object.FindObjectsByType<PlayerMannequin>(FindObjectsSortMode.None).Count(m => m == _view.Pitcher), "one pitcher");
                if (script[i] == 'F')
                {
                    Assert.IsTrue(_lab.SwingAtSimTime(FoulSwing(pitch)).IsContact);
                    Assert.IsTrue(_lab.LastLive.IsFoul);
                }

                double ready = BattingStateMachine.OutcomeTime(pitch, _lab.LastSwing, _lab.LastResult, _lab.PlayEnd, _lab.Swing.SwingDuration) + BattingStateMachine.ResultPause;
                At(ready + 0.3);
                if (i == 0) cameraReady = Camera.main.transform.position;
                Assert.Less(Vector3.Distance(cameraReady, Camera.main.transform.position), 1e-3f, $"pitch {i + 1}: the camera is back in the batting view");
                if (i < script.Length - 1) Assert.AreEqual(1, game.Current.Number, "the same plate appearance");
            }

            PlateAppearance pa = game.Completed.Single();
            Assert.AreEqual((PlateAppearanceEnd.Walk, 12), (pa.End, pa.Pitches.Count));
            Assert.AreEqual(new Count(2, 2), pa.Pitches[7].After, "fouls at two strikes leave the count");
            Assert.AreEqual(new Count(3, 2), pa.Pitches[10].Before);
        }

        [UnityTest]
        public IEnumerator TheCatcherReceivesATakenPitchWhereHeCanBeSeen()
        {
            yield return null;
            Object.FindFirstObjectByType<GameplayCameraController>().SetMode(CameraMode.Tactical);
            _lab.Target = PitchTarget.Middle;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            At(pitch.Flight.Final.Time + 0.2);
            PlayerMannequin catcher = _view.Defense.Figure(Gameplay.Fielding.DefensivePosition.C);
            Assert.IsTrue(catcher.gameObject.activeInHierarchy, "shown when the camera is not behind the plate");
            Assert.Less(Vector3.Distance(_lab.BallTransform.position, catcher.GloveAnchor.position), 1e-3f, "the ball ends in his glove");
            Assert.Less(_lab.BallTransform.position.z, (float)Simulation.Pitching.PitchingGeometry.PlateFrontY, "behind the front of the plate");
            Assert.AreEqual(PitchOutcome.CalledStrike, _lab.Game.Current.Pitches.Single().Outcome, "and the call is the flight's");
        }
    }
}
