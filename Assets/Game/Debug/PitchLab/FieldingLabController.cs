using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Fielding;
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
    /// defender, and (TASK-006A) the throw to a base and the receiver's catch. No physics of its own. Keys: 1–0 and ←/→
    /// presets · Space replay · S 1×/0.5× · T debug · Z/X/C/V throw to 1B/2B/3B/home, N no throw, B preset's target.
    /// </summary>
    public sealed class FieldingLabController : MonoBehaviour
    {
        /// <summary>Presets: the TASK-005 fielding plays (1–0) and the TASK-006A throws (←/→ to reach them), each with its default
        /// throw target (null: no throw).</summary>
        public static readonly (string Name, BattedBallLaunch Launch, Base? Throw)[] Presets =
        {
            ("Routine SS grounder → 1B", new BattedBallLaunch(85.0, -8.0, -15.0, -1000.0), Base.First),
            ("Slow roller → 1B", new BattedBallLaunch(45.0, -12.0, 8.0, -600.0), Base.First),
            ("Hard grounder → 2B", new BattedBallLaunch(105.0, -6.0, -5.0, -900.0), Base.Second),
            ("Chopper → 1B", new BattedBallLaunch(60.0, -20.0, 5.0, -800.0), Base.First),
            ("Shallow fly", new BattedBallLaunch(75.0, 40.0, 10.0, 2800.0), null),
            ("Routine CF fly", new BattedBallLaunch(92.0, 32.0, 0.0, 2400.0), null),
            ("Gap fly", new BattedBallLaunch(90.0, 20.0, 15.0, 1800.0), null),
            ("Line drive", new BattedBallLaunch(98.0, 14.0, -8.0, 1400.0), null),
            ("Wall rebound → 2B", new BattedBallLaunch(105.0, 14.0, -20.0, 1800.0), Base.Second),
            ("Unreachable deep ball → 2B", new BattedBallLaunch(100.0, 20.0, -15.0, 1800.0), Base.Second),
            ("3B grounder → 1B", new BattedBallLaunch(80.0, -7.0, -30.0, -900.0), Base.First),
            ("2B grounder → 1B", new BattedBallLaunch(80.0, -7.0, 18.0, -900.0), Base.First),
            ("LF single → 2B", new BattedBallLaunch(98.0, 9.0, -24.0, 900.0), Base.Second),
            ("CF single → home", new BattedBallLaunch(95.0, 6.0, 0.0, 700.0), Base.Home),
        };

        public static readonly Vector3d ContactPoint = new Vector3d(0.0, 0.7, 0.8);

        [SerializeField] private PlayerMannequin _mannequinPrefab;
        [SerializeField] private BaseballCamera _camera;
        [SerializeField] private Transform _ball;
        [SerializeField] private bool _debug = true;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private DefenseView _defense;
        private TrailRenderer _trail;
        private double _launchRealtime;
        private float _speed = 1f;
        private GUIStyle _style;

        /// <summary>Real-time clock (tests replace it).</summary>
        public Func<double> Clock { get; set; } = () => Time.realtimeSinceStartupAsDouble;

        public int PresetIndex { get; private set; }
        public BallInPlay Play { get; private set; }
        public FieldingPlay Fielding { get; private set; }
        /// <summary>The fielding play plus the throw (if any): the ball's authority at every instant.</summary>
        public DefensivePlay DefensivePlay { get; private set; }
        private static readonly (Key, Base?)[] ThrowKeys = { (Key.Z, Base.First), (Key.X, Base.Second), (Key.C, Base.Third), (Key.V, Base.Home), (Key.N, null) };
        /// <summary>Throw target override (debug controls Z/X/C/V = 1B/2B/3B/home, N = no throw, B = the preset's own).</summary>
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
            PresetIndex = ((index % Presets.Length) + Presets.Length) % Presets.Length;
            Play = BallInPlaySimulation.Run(Presets[PresetIndex].Launch.ToState(ContactPoint), EnvironmentState.Standard, FieldLayout.Standard);
            Fielding = FieldingSolver.Solve(Play);
            DefensivePlay = ThrowPlanner.Plan(Fielding, UseOverride ? TargetOverride : Presets[PresetIndex].Throw);
            _launchRealtime = Clock();
            _ball.position = SimulationSpace.ToUnity(ContactPoint);   // move first, then clear: no streak from the last play
            _trail.Clear();
            _camera.ShowBatting();
            _camera.Follow(_ball);
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
                for (int i = 0; i < 10; i++)
                    if (k[i == 9 ? Key.Digit0 : Key.Digit1 + i].wasPressedThisFrame) Launch(i);
                if (k.rightArrowKey.wasPressedThisFrame) Launch(PresetIndex + 1);
                if (k.leftArrowKey.wasPressedThisFrame) Launch(PresetIndex - 1);
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
            _ball.position = SimulationSpace.ToUnity(DefensivePlay.BallPositionAt(t));
            _trail.emitting = DefensivePlay.AuthorityAt(t) != BallAuthority.Possessed;
            _defense.Show(DefensivePlay, t, _ball, _debug);
            _camera.FollowAlso(_defense.Focus);
        }

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12 };
            var text = new System.Text.StringBuilder("Fielding Lab — 1–0 / ←→ presets · Space replay · S speed · T debug\nThrow: Z 1B · X 2B · C 3B · V home · N none · B preset's\n");
            for (int i = 0; i < Presets.Length; i++) text.AppendLine($"{(i == PresetIndex ? "▶" : "  ")} {(i < 10 ? ((i + 1) % 10).ToString() : " ")}  {Presets[i].Name}");
            if (Play != null)
            {
                BattedBallLaunch l = Presets[PresetIndex].Launch;
                text.AppendLine($"\n{l.ExitSpeedMph:0} mph · {l.LaunchAngleDegrees:+0;-0}° · spray {l.SprayAngleDegrees:+0;-0}° · spin {l.BackspinRpm:0} rpm · {_speed:0.0}×");
                text.Append(Report(Fielding));
                if (DefensivePlay.Throw is ThrowPlay th)
                    text.Append($"\nthrow → {th.Target} ({HittingLabPresentation.Abbreviation(th.Receiver)}) · {th.Flight.First.Velocity.Length / 0.44704:0} mph · {(th.Caught ? $"caught after {th.Catch.Time - th.ReleaseTime:0.00} s" : "NOT caught")}");
            }

            GUI.Label(new Rect(10f, 10f, 380f, 400f), text.ToString(), _style);
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
