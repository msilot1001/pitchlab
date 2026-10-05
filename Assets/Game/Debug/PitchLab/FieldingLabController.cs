using System;
using System.Collections.Generic;
using System.Linq;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Presentation;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Fielding Lab (TASK-005): launches preset batted balls with the production pipeline — <see cref="BallInPlaySimulation"/>
    /// for the complete authoritative trajectory, <see cref="FieldingSolver"/> for the defense — and plays them back on the
    /// lab's clock with the nine defenders (<see cref="DefenseView"/>), the ball and a camera framing ball and primary
    /// defender, (TASK-006A) the throw to a base and the receiver's catch, and (TASK-006B) the rules: runners on base, the
    /// force chain, the defense's candidate actions and choice, OUT/SAFE and the out count (<see cref="PlayResolver"/>). No
    /// physics or rules of its own. Keys: 1–8 rules scenarios, ←/→ all presets · Space replay · S 1×/0.5× · T debug ·
    /// Z/X/C/V throw to 1B/2B/3B/home, N hold, B the defense's own decision.
    /// </summary>
    public sealed class FieldingLabController : MonoBehaviour
    {
        /// <summary>A lab scenario: a batted ball, the bases occupied, and (scripted) a runner who is not forced but runs on
        /// contact (tag plays; TASK-007 adds runner decisions) and a forced choice of action (to show a tag play).</summary>
        public readonly struct Scenario
        {
            public Scenario(string name, BattedBallLaunch launch, BaseOccupancy bases = default, Base? runsOnContact = null, Base? tagAt = null, int outs = 0)
            {
                Outs = outs;
                Name = name;
                Launch = launch;
                Bases = bases;
                RunsOnContact = runsOnContact;
                TagAt = tagAt;
            }

            public string Name { get; }
            public BattedBallLaunch Launch { get; }
            public BaseOccupancy Bases { get; }
            public Base? RunsOnContact { get; }
            public Base? TagAt { get; }
            public int Outs { get; }
        }

        private static readonly BaseOccupancy OnFirst = new BaseOccupancy(true, false, false), OnSecond = new BaseOccupancy(false, true, false);

        /// <summary>The TASK-006B rules scenarios (keys 1–8), then the TASK-005/006A fielding and throwing plays (←/→), every
        /// one resolved by the defense's decision.</summary>
        public static readonly Scenario[] Presets =
        {
            new Scenario("SS routine grounder: out at 1B", new BattedBallLaunch(85.0, -8.0, -15.0, -1000.0)),
            // The one close play the runner wins on a grid of ~1,700 infield balls (the fielding model is idealized): the 1B
            // ranges far to his right and races the batter-runner to the bag, 0.11 s late.
            new Scenario("1B ranges right: safe at 1B", new BattedBallLaunch(95.0, -10.0, 24.0, -900.0)),
            new Scenario("1B grounder: unassisted", new BattedBallLaunch(80.0, -8.0, 38.0, -900.0)),
            new Scenario("Runner on 1B, grounder to SS", new BattedBallLaunch(85.0, -8.0, -15.0, -1000.0), OnFirst),
            new Scenario("Runner on 1B, grounder to 2B", new BattedBallLaunch(80.0, -7.0, 18.0, -900.0), OnFirst),
            new Scenario("Bases loaded, grounder to 3B", new BattedBallLaunch(80.0, -7.0, -30.0, -900.0), BaseOccupancy.Loaded),
            new Scenario("Routine fly: fly out", new BattedBallLaunch(92.0, 32.0, 0.0, 2400.0)),
            new Scenario("Runner on 2B runs: tag at 3B", new BattedBallLaunch(85.0, -8.0, -15.0, -1000.0), OnSecond, Base.Second, Base.Third),
            new Scenario("Slow roller", new BattedBallLaunch(45.0, -12.0, 8.0, -600.0)),
            new Scenario("Hard grounder", new BattedBallLaunch(105.0, -6.0, -5.0, -900.0)),
            new Scenario("Chopper", new BattedBallLaunch(60.0, -20.0, 5.0, -800.0)),
            new Scenario("Shallow fly", new BattedBallLaunch(75.0, 40.0, 10.0, 2800.0)),
            new Scenario("Gap fly", new BattedBallLaunch(90.0, 20.0, 15.0, 1800.0)),
            new Scenario("Line drive", new BattedBallLaunch(98.0, 14.0, -8.0, 1400.0)),
            new Scenario("Wall rebound", new BattedBallLaunch(105.0, 14.0, -20.0, 1800.0)),
            new Scenario("Unreachable deep ball", new BattedBallLaunch(100.0, 20.0, -15.0, 1800.0)),
            new Scenario("3B grounder", new BattedBallLaunch(80.0, -7.0, -30.0, -900.0)),
            new Scenario("2B grounder", new BattedBallLaunch(80.0, -7.0, 18.0, -900.0)),
            new Scenario("LF single", new BattedBallLaunch(98.0, 9.0, -24.0, 900.0)),
            new Scenario("CF single, runner on 3B runs home", new BattedBallLaunch(95.0, 6.0, 0.0, 700.0), new BaseOccupancy(false, false, true), Base.Third),
            // TASK-007 baserunning.
            new Scenario("Runner on 3B, 1 out, deep fly: tag-up", new BattedBallLaunch(95.0, 30.0, -10.0, 2200.0), new BaseOccupancy(false, false, true), outs: 1),
            new Scenario("Runner on 2B, single to LF", new BattedBallLaunch(98.0, 9.0, -24.0, 900.0), OnSecond),
            new Scenario("Runner on 1B, ball off the wall", new BattedBallLaunch(105.0, 14.0, -20.0, 1800.0), OnFirst),
            new Scenario("Runners 1B & 3B, 1 out, CF single", new BattedBallLaunch(95.0, 6.0, 0.0, 700.0), new BaseOccupancy(true, false, true), outs: 1),
            new Scenario("Runner on 1B, 2 out, deep fly", new BattedBallLaunch(95.0, 30.0, -10.0, 2200.0), OnFirst, outs: 2),
            // The half-inning slice's runtime scenarios (A, B, D, E, G are presets above).
            new Scenario("C: R1, 0 out, grounder to 2B (4-6-3)", new BattedBallLaunch(88.0, -8.0, 16.0, -900.0), OnFirst),
            new Scenario("F: R1, 2 out, gap ball", new BattedBallLaunch(100.0, 20.0, -15.0, 1800.0), OnFirst, outs: 2),
            new Scenario("H: loaded, 1 out, grounder to 3B", new BattedBallLaunch(80.0, -7.0, -30.0, -900.0), BaseOccupancy.Loaded, outs: 1),
            new Scenario("I: R2, 2 out, single to CF", new BattedBallLaunch(95.0, 6.0, 0.0, 700.0), OnSecond, outs: 2),
        };

        public static int IndexOf(string name) => Array.FindIndex(Presets, p => p.Name == name);

        public static readonly Vector3d ContactPoint = new Vector3d(0.0, 0.7, 0.8);

        [SerializeField] private PlayerMannequin _mannequinPrefab;
        [SerializeField] private BaseballCamera _camera;
        [SerializeField] private Transform _ball;
        [SerializeField] private bool _debug = true;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private DefenseView _defense;
        private RunnerView _runners;
        private TrailRenderer _trail;
        private double _launchRealtime;
        private float _speed = 1f;
        private GUIStyle _style, _bigStyle;

        /// <summary>Real-time clock (tests replace it).</summary>
        public Func<double> Clock { get; set; } = () => Time.realtimeSinceStartupAsDouble;

        public int PresetIndex { get; private set; }
        public BallInPlay Play { get; private set; }
        public FieldingPlay Fielding { get; private set; }
        /// <summary>The live play (runners, defense, events, outs, runs).</summary>
        public LivePlay Live { get; private set; }
        /// <summary>The live defense: roles, motions, the ball's authority at every instant, throws, decisions.</summary>
        public LiveDefense Team => Live?.Defense;
        public RunnerView Runners => _runners;
        private static readonly (Key, Base?)[] ThrowKeys = { (Key.Z, Base.First), (Key.X, Base.Second), (Key.C, Base.Third), (Key.V, Base.Home), (Key.N, null) };
        /// <summary>Action override (debug controls Z/X/C/V = throw to 1B/2B/3B/home, N = hold, B = the defense's decision).</summary>
        public Base? TargetOverride { get; set; }
        public bool UseOverride { get; set; }
        public DefenseView Defense => _defense;
        public Transform Ball => _ball;
        public bool DebugView { get => _debug; set => _debug = value; }

        /// <summary>The play's time now (s on the ball's clock; contact at <see cref="BallInPlay.First"/>).</summary>
        public double PlayTime => Play == null ? 0.0 : Play.First.Time + (Clock() - _launchRealtime) * _speed;

        private void Awake()
        {
            if (_mannequinPrefab == null || _camera == null || _ball == null)
            {
                UnityEngine.Debug.LogError("FieldingLabController is missing a scene reference; disabling.", this);
                enabled = false;
                return;
            }

            _defense = new GameObject("Defense").AddComponent<DefenseView>();
            _defense.transform.SetParent(transform, false);
            _defense.Build(_mannequinPrefab, null, null, null, DefensiveAlignment.Standard);
            _runners = new GameObject("Runners").AddComponent<RunnerView>();
            _runners.transform.SetParent(transform, false);
            _runners.Build(_mannequinPrefab);
            _meshes.AddRange(OutfieldDressing.Build(transform, FieldLayout.Standard));
            float d = (float)(2.0 * BallProperties.Baseball.Radius) * 2.5f;   // drawn larger for readability; centre on the trajectory
            _ball.localScale = new Vector3(d, d, d);
            if (_ball.TryGetComponent(out MeshRenderer r)) r.sharedMaterial = PresentationMaterials.Get(Color.white, unlit: true);
            if (!_ball.TryGetComponent(out _trail)) _trail = _ball.gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.6f;
            _trail.widthMultiplier = d * 0.6f;
            _trail.sharedMaterial = PresentationMaterials.Get(Color.white, unlit: true);
        }

        private void Start() => Launch(0);

        private void OnDestroy()
        {
            foreach (Mesh m in _meshes) if (m != null) Destroy(m);
        }

        /// <summary>Launches preset <paramref name="index"/> now (contact at the plate) and solves the defense.</summary>
        public void Launch(int index)
        {
            int i = ((index % Presets.Length) + Presets.Length) % Presets.Length;
            // Computed first, assigned together: a failure leaves the previous play whole.
            BallInPlay play = BallInPlaySimulation.Run(Presets[i].Launch.ToState(ContactPoint), EnvironmentState.Standard, FieldLayout.Standard);
            Scenario sc = Presets[i];
            var situation = new Situation(sc.Outs, sc.Bases);
            FieldingPlay fielding = FieldingSolver.Solve(play, situation.Alignment, FielderProfile.For, FieldLayout.Standard);
            _defense.Alignment = situation.Alignment;
            Func<IReadOnlyList<LiveAction>, LiveAction> choose = null;
            if (UseOverride) choose = cs => Override(cs, TargetOverride);
            else if (sc.TagAt is Base tagAt) choose = cs => cs.FirstOrDefault(a => a.Kind == LiveActionKind.Throw && a.Target == tagAt) ?? cs[0];
            // The live play (TASK-007) with real runners, resolved at once: every motion keeps its history, so it renders
            // exactly at any later time.
            var live = new LivePlay(fielding, situation, null, choose,
                sc.RunsOnContact is Base runs ? r => r.From == runs : (Func<Runner, bool>)null);
            live.RunToEnd();
            PresetIndex = i;
            Play = play;
            Fielding = fielding;
            Live = live;
            _launchRealtime = Clock();
            _ball.position = SimulationSpace.ToUnity(ContactPoint);   // move first, then clear: no streak from the last play
            _trail.Clear();
            _camera.ShowBatting();
            _camera.Follow(_ball);
        }

        /// <summary>Switches scenario: the defense's own decision again (a debug override applies to the play it was set on).</summary>
        public void SelectScenario(int index)
        {
            UseOverride = false;
            Launch(index);
        }

        /// <summary>Debug override: throw to <paramref name="target"/> (the play on a runner there if there is one), or hold.</summary>
        private static LiveAction Override(IReadOnlyList<LiveAction> candidates, Base? target)
        {
            if (target == null) return candidates[0];   // hold
            return candidates.FirstOrDefault(a => a.Kind == LiveActionKind.Throw && a.Target == target && a.Throw != null) ?? candidates[0];
        }

        /// <summary>Playback speed; the play continues from where it is (no jump in play time).</summary>
        public void SetSpeed(float speed)
        {
            double now = Clock(), t = PlayTime;
            _speed = speed;
            if (Play != null) _launchRealtime = now - (t - Play.First.Time) / _speed;
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k != null)
            {
                for (int i = 0; i < 8; i++)
                    if (k[Key.Digit1 + i].wasPressedThisFrame) SelectScenario(i);
                if (k.rightArrowKey.wasPressedThisFrame) SelectScenario(PresetIndex + 1);
                if (k.leftArrowKey.wasPressedThisFrame) SelectScenario(PresetIndex - 1);
                foreach (var (key, target) in ThrowKeys)
                    if (k[key].wasPressedThisFrame)
                    {
                        UseOverride = true;
                        TargetOverride = target;
                        Launch(PresetIndex);
                    }

                if (k.bKey.wasPressedThisFrame)
                {
                    UseOverride = false;
                    Launch(PresetIndex);
                }
                if (k.spaceKey.wasPressedThisFrame) Launch(PresetIndex);
                if (k.sKey.wasPressedThisFrame) SetSpeed(_speed > 0.75f ? 0.5f : 1f);
                if (k.tKey.wasPressedThisFrame) _debug = !_debug;
            }

            FrameUpdate();
        }

        /// <summary>Renders the play at its current time (tests call it directly). Nothing authoritative happens here.</summary>
        public void FrameUpdate()
        {
            if (Play == null) return;
            double t = PlayTime;
            _ball.position = SimulationSpace.ToUnity(Team.BallPositionAt(t));
            _trail.emitting = Team.AuthorityAt(t) != BallAuthority.Possessed;
            _defense.Show(Team, t, _ball, _debug);
            _runners.Show(Live, t);
            _camera.FollowAlso(_defense.Focus);
        }

        /// <summary>The result so far, as shown: the latest OUT/SAFE call (empty before any).</summary>
        public string ResultText => LiveText.Calls(Live, PlayTime);

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12 };
            _bigStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 30, fontStyle = FontStyle.Bold };
            var text = new System.Text.StringBuilder("Fielding Lab — 1–8 rules · ←→ all · Space replay · S speed · T debug\nZ/X/C/V throw 1B/2B/3B/home · N hold · B decision\n");
            for (int i = Math.Max(0, PresetIndex - 4); i < Math.Min(Presets.Length, Math.Max(0, PresetIndex - 4) + 10); i++)
                text.AppendLine($"{(i == PresetIndex ? "▶" : "  ")} {(i < 8 ? (i + 1).ToString() : " ")}  {Presets[i].Name}");
            if (Play != null)
            {
                BattedBallLaunch l = Presets[PresetIndex].Launch;
                text.AppendLine($"\n{l.ExitSpeedMph:0} mph · {l.LaunchAngleDegrees:+0;-0}° · spray {l.SprayAngleDegrees:+0;-0}° · spin {l.BackspinRpm:0} rpm · {_speed:0.0}×");
                text.AppendLine(Report(Fielding));
                text.Append(LiveText.Report(Live, PlayTime));
                text.Append(LiveDefenseText.Decisions(Team, PlayTime, Live.ContactTime));
            }

            GUI.Label(new Rect(10f, 10f, 420f, 640f), text.ToString(), _style);
            GUI.Label(new Rect(0f, 40f, Screen.width, 50f), ResultText, _bigStyle);
        }

        /// <summary>The objective scenario report: selected fielder, start, reaction, route, intercept and result.</summary>
        public static string Report(FieldingPlay f)
        {
            if (!(f.Primary is DefensivePosition p)) return $"{f.Outcome} (call {f.Call})";
            Intercept i = f.Intercept;
            FielderMotion m = f.Motion(p);
            double t0 = f.Ball.First.Time;
            return $"{HittingLabPresentation.Abbreviation(p)} · {i.Kind} · call {f.Call}\n" +
                   $"start ({m.Start.X:0.0}, {m.Start.Y:0.0}) m · reacts {m.Profile.ReactionTime:0.00} s\n" +
                   $"route {i.RouteDistance:0.0} m · intercept +{i.Time - t0:0.00} s at ({i.Ball.Position.X:0.0}, {i.Ball.Position.Y:0.0}, {i.Ball.Position.Z:0.00}) m\n" +
                   $"ball {f.Ball.PhaseAt(i.Time)} {i.Ball.Velocity.Length:0.0} m/s · margin {i.Margin:0.00} s · {(m.StopsAtTarget ? "settles" : "on the run")}";
        }
    }
}
