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

        /// <summary>
        /// One auto-pitched sequence from a new game: frames of <paramref name="steps"/> (cycled) for a minute; a foul swing at
        /// the 2nd pitch and a contact swing at the 5th. Returns the pitches and game, and each pitch's release time relative
        /// to the first press.
        /// </summary>
        private string AutoRun(double[] steps, System.Collections.Generic.List<double> releases)
        {
            _lab.NewGame(new GameState(RightHanded("Away"), RightHanded("Home")));
            _lab.AutoPitchSeed = 5;
            _lab.AutoPitch = true;
            double start = _now;
            int thrown = _lab.PitchesThrown;
            _lab.PressSwingButton(_now);   // the first pitch is the player's press; the rest come by themselves
            int k = 0;
            bool fouled = false;
            while (_now < start + 60.0)
            {
                if (_lab.PitchesThrown != thrown)
                {
                    Assert.AreEqual(thrown + 1, _lab.PitchesThrown, "one pitch per press");
                    thrown = _lab.PitchesThrown;
                    double release = _now - _lab.SimTime;   // the frozen clock: SimTime = now − release
                    releases.Add(release - start);
                    HittingPitch pitch = _lab.CurrentPitch;
                    // A foul on the first pitch from the 2nd on that can be fouled off; contact (in play) on the 7th.
                    if (!fouled && releases.Count >= 2 && releases.Count != 7 && FoulSwing(pitch) is double foul)
                    {
                        _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);   // as FoulSwing searched
                        Assert.IsTrue(_lab.SwingAtSimTime(foul).IsContact);
                        fouled = true;
                    }

                    if (releases.Count == 7)
                    {
                        _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
                        Assert.IsTrue(_lab.SwingAtSimTime(pitch.IdealContactTime - _lab.Swing.SwingDuration).IsContact);
                    }
                }

                Frame(_now + steps[k++ % steps.Length]);
            }

            _lab.AutoPitch = false;
            GameState g = _lab.Game;
            return string.Join("|", g.Completed.SelectMany(p => p.Pitches).Concat(g.Current.Pitches)
                .Select(p => $"{p.Info.Label}@{p.Info.PlateX:0.0000},{p.Info.PlateZ:0.0000}:{p.Outcome}")) + "#" + string.Join("|", g.Log) + "#" + g;
        }

        [UnityTest]
        public IEnumerator TheAutoPitcherThrowsEachPitchOnceOnItsOwnScheduleAtAnyFrameRate()
        {
            yield return null;
            var r30 = new System.Collections.Generic.List<double>();
            var r144 = new System.Collections.Generic.List<double>();
            var rUneven = new System.Collections.Generic.List<double>();
            string a = AutoRun(new[] { 1.0 / 30.0 }, r30);
            string b = AutoRun(new[] { 1.0 / 144.0 }, r144);
            string c = AutoRun(new[] { 0.013, 0.21, 0.004, 0.07 }, rUneven);
            Assert.AreEqual(a, b, "the same pitches, swings, results and game at 30 and 144 fps");
            Assert.AreEqual(a, c, "and with an uneven frame schedule");
            StringAssert.Contains(":Foul", a);
            StringAssert.Contains(":InPlay", a);
            Assert.Greater(r30.Count, 8, "a steady cadence: many pitches in a minute");
            int n = System.Math.Min(r30.Count, System.Math.Min(r144.Count, rUneven.Count));
            Assert.GreaterOrEqual(n, r30.Count - 1);
            for (int i = 0; i < n; i++)
            {
                Assert.AreEqual(r30[i], r144[i], 1e-9, $"pitch {i + 1} is released at its scheduled time");
                Assert.AreEqual(r30[i], rUneven[i], 1e-9, $"pitch {i + 1}");
            }
        }

        [UnityTest]
        public IEnumerator TheAutoPitcherReadsTheCountAndThePitchNumber()
        {
            yield return null;
            GameState game = _lab.Game;
            for (int i = 0; i < 3; i++) game.Pitch(PitchOutcome.Ball);
            _lab.AutoPitchSeed = 9;
            _lab.AutoPitch = true;
            _lab.PressSwingButton(_now);
            Assert.AreEqual(AutoPitcher.Choose(9, game.Current.Number, 4, new Count(3, 0)), _lab.LastCommand, "the 3–0 pitch of this plate appearance");
        }

        [UnityTest]
        public IEnumerator SwitchingAutoOnAfterAPauseNeverThrowsAPitchThatIsAlreadyOver()
        {
            yield return null;
            _lab.Target = PitchTarget.Middle;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            At(_lab.CurrentPitch.Flight.Final.Time + 15.0);   // a long idle in Ready
            int thrown = _lab.PitchesThrown;
            _lab.OnPitchingKey("p");
            Frame(_now + 1.0 / 60.0);
            Assert.AreEqual(thrown, _lab.PitchesThrown, "not at once: the delay counts from switching on");
            Frame(_now + _lab.AutoPitchDelay);
            Assert.AreEqual(thrown + 1, _lab.PitchesThrown);
            Assert.Less(_lab.SimTime, 0.0, "the new pitch is still in the wind-up");
            // A pause (the clock jumps 20 s): one pitch thrown at the frame, still in its wind-up — never a burst.
            double jump = _now + 20.0;
            Frame(jump);
            Assert.AreEqual(thrown + 2, _lab.PitchesThrown);
            Assert.Less(_lab.SimTime, 0.0);
            for (int i = 0; i < 30; i++) Frame(_now + 1.0 / 60.0);
            Assert.AreEqual(thrown + 2, _lab.PitchesThrown, "no second throw while that pitch is live");
        }

        [UnityTest]
        public IEnumerator SwitchingAutoOffStopsItAndOnResumesIt()
        {
            yield return null;
            _lab.Target = PitchTarget.DownRight;
            _lab.PresetIndex = 2;
            _lab.AutoPitch = true;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            RunAuto(1.0 / 60.0, _now + 12.0);
            int thrown = _lab.PitchesThrown;
            Assert.Greater(thrown, 2);
            Assert.AreEqual((PitchTarget.DownRight, 2), (_lab.Target.Value, _lab.PresetIndex), "auto leaves the player's selection alone");
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
            Assert.AreEqual(new PitchCommand(_lab.PresetIndex, PitchTarget.BallUp), _lab.LastCommand);
            (double x, double z) = StrikeZone.Crossing(_lab.CurrentPitch);
            Assert.That(z, Is.InRange(_lab.Zone.Top + 0.1, _lab.Zone.Top + 0.4), "thrown where the keys aimed it");
            Assert.AreEqual(0.0, x, 0.05);
            _lab.OnPitchingKey("p");   // auto again: resumes after this pitch
            RunAuto(1.0 / 60.0, _now + 10.0);
            Assert.Greater(_lab.PitchesThrown, thrown + 2);
        }

        [UnityTest]
        public IEnumerator TargetsFollowATallBattersZone()
        {
            yield return null;
            var away = new Lineup("Away", new[] { new PlayerProfile("T", "Tall", BatterSide.Right, 92.0, "") }
                .Concat(Enumerable.Range(2, 8).Select(i => new PlayerProfile($"A{i}", $"A{i}", BatterSide.Right, 73.0, ""))).ToArray());
            _lab.NewGame(new GameState(away, RightHanded("Home")));
            _lab.Target = PitchTarget.UpMiddle;
            _lab.PressSwingButton(_now);
            PlayerProfile tall = away[1];
            (double x, double z) = StrikeZone.Crossing(_lab.CurrentPitch);
            (double tx, double tz) = PitchTargets.Point(PitchTarget.UpMiddle, tall.ZoneBottom, tall.ZoneTop);
            Assert.AreEqual(tz, z, 0.03, "aimed in his zone");
            Assert.AreEqual(tx, x, 0.03);
            Assert.Greater(z, StrikeZone.Top + 0.04, "above the default zone");
        }

        private static int PitchPresetsCount => Simulation.Pitching.PitchPresets.All.Length;

        /// <summary>A swing time (sim s) at <paramref name="pitch"/> that makes contact and is called foul (null if none).</summary>
        private double? FoulSwing(HittingPitch pitch)
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

            return null;
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
                if (script[i] == 'F')
                {
                    _lab.SetPci(pitch.IdealContactState.Position.X, pitch.IdealContactState.Position.Z);
                    Assert.IsTrue(_lab.SwingAtSimTime(FoulSwing(pitch) ?? double.NaN).IsContact, "a foul swing exists for this pitch");
                    Assert.IsTrue(_lab.LastLive.IsFoul);
                }

                double ready = BattingStateMachine.OutcomeTime(pitch, _lab.LastSwing, _lab.LastResult, _lab.PlayEnd, _lab.Swing.SwingDuration) + BattingStateMachine.ResultPause;
                At(ready + 0.3);
                Assert.IsFalse(Object.FindFirstObjectByType<BaseballCamera>().IsFollowing, $"pitch {i + 1}: the camera is back in the batting view");
                if (i == 0) cameraReady = Camera.main.transform.position;
                Assert.Less(Vector3.Distance(cameraReady, Camera.main.transform.position), 0.01f, $"pitch {i + 1}: the same batting view");
                if (i < script.Length - 1) Assert.AreEqual(1, game.Current.Number, "the same plate appearance");
            }

            PlateAppearance pa = game.Completed.Single();
            Assert.AreEqual((PlateAppearanceEnd.Walk, 12), (pa.End, pa.Pitches.Count));
            Assert.AreEqual(new Count(2, 2), pa.Pitches[7].After, "fouls at two strikes leave the count");
            Assert.AreEqual(new Count(3, 2), pa.Pitches[10].Before);

            // The next batter's first pitch starts as cleanly: same set position, the ball from the release point, 0–0.
            _lab.Target = PitchTarget.Middle;
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            At(-_lab.DeliveryLead + 0.02);
            Assert.Less(Vector3.Distance(pitcherSet, _view.Pitcher.transform.position), 0.01f);
            At(0.0);
            Assert.Less(Vector3.Distance(_lab.BallTransform.position, SimulationSpace.ToUnity(_lab.CurrentPitch.Flight.First.Position)), 1e-3f);
            At(_lab.CurrentPitch.Flight.Final.Time + 0.2);
            Assert.AreEqual((2, 1, new Count(0, 1)), (game.Current.Number, game.Current.Pitches.Count, game.Count));
        }

        [UnityTest]
        public IEnumerator TheCatcherReceivesATakenPitchWhereHeCanBeSeen()
        {
            yield return null;
            Object.FindFirstObjectByType<GameplayCameraController>().SetMode(CameraMode.Tactical);
            Vector3? previous = null;
            foreach (PitchTarget target in new[] { PitchTarget.UpLeft, PitchTarget.DownRight })
            {
                _lab.Target = target;
                _lab.PressSwingButton(_now);
                _release = _now + _lab.DeliveryLead;
                HittingPitch pitch = _lab.CurrentPitch;
                double catchTime = CatchTime(pitch);
                At(catchTime - 0.004);
                Vector3 before = _lab.BallTransform.position;
                At(catchTime + 0.004);
                PlayerMannequin catcher = _view.Defense.Figure(Gameplay.Fielding.DefensivePosition.C);
                Assert.IsTrue(catcher.gameObject.activeInHierarchy, "shown when the camera is not behind the plate");
                Vector3 flight = SimulationSpace.ToUnity(pitch.Flight.StateAt(catchTime).Position);
                Assert.Less(Vector3.Distance(catcher.GloveAnchor.position, flight), HittingLabPresentation.CatcherGloveReach, $"{target}: his glove went to the pitch");
                Assert.Less(Vector3.Distance(before, _lab.BallTransform.position), 0.35f, $"{target}: no jump into the glove");
                At(pitch.Flight.Final.Time + 0.3);
                Assert.Less(Vector3.Distance(_lab.BallTransform.position, catcher.GloveAnchor.position), 1e-3f, $"{target}: the ball ends in his glove");
                if (previous.HasValue) Assert.Greater(Vector3.Distance(previous.Value, catcher.GloveAnchor.position), 0.25f, "a different pitch, a different catch");
                previous = catcher.GloveAnchor.position;
                At(pitch.Flight.Final.Time + BattingStateMachine.ResultPause + 0.5);
            }

            CollectionAssert.AreEqual(new[] { PitchOutcome.CalledStrike, PitchOutcome.CalledStrike }, _lab.Game.Current.Pitches.Select(p => p.Outcome), "the calls are the flights'");
        }

        [UnityTest]
        public IEnumerator APitchInTheDirtIsBlockedWhereItLands()
        {
            yield return null;
            var away = new Lineup("Away", new[] { new PlayerProfile("S", "Short", BatterSide.Right, 60.0, "") }
                .Concat(Enumerable.Range(2, 8).Select(i => new PlayerProfile($"A{i}", $"A{i}", BatterSide.Right, 73.0, ""))).ToArray());
            _lab.NewGame(new GameState(away, RightHanded("Home")));
            Object.FindFirstObjectByType<GameplayCameraController>().SetMode(CameraMode.Offset);
            _lab.Target = PitchTarget.BallDown;
            _lab.PresetIndex = 3;   // curveball
            _lab.PressSwingButton(_now);
            _release = _now + _lab.DeliveryLead;
            HittingPitch pitch = _lab.CurrentPitch;
            Assert.AreEqual(FlightEnd.ReachedGround, pitch.Flight.End, "this one bounces before the plate area");
            At(pitch.Flight.Final.Time + 0.3);
            PlayerMannequin catcher = _view.Defense.Figure(Gameplay.Fielding.DefensivePosition.C);
            Assert.Less(Vector3.Distance(catcher.GloveAnchor.position, SimulationSpace.ToUnity(pitch.Flight.Final.Position)), HittingLabPresentation.CatcherGloveReach, "he gets his glove down to it");
            Assert.Less(Vector3.Distance(catcher.GloveAnchor.position, _lab.BallTransform.position), 1e-3f, "and holds it");
            Assert.AreEqual(PitchOutcome.Ball, _lab.Game.Current.Pitches.Single().Outcome);
        }

        /// <summary>When the pitch reaches the catcher's glove plane (as the presentation computes it).</summary>
        private static double CatchTime(HittingPitch pitch)
        {
            double a = pitch.Flight.First.Time, b = pitch.Flight.Final.Time;
            for (int i = 0; i < 50; i++)
            {
                double m = 0.5 * (a + b);
                if (pitch.Flight.StateAt(m).Position.Y > HittingLabPresentation.CatcherGlovePlaneY) a = m;
                else b = m;
            }

            return b;
        }
    }
}
