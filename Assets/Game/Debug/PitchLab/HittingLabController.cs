using System;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Hitting Sandbox. A pitch is simulated once and played back on a real-time clock; the swing time comes from the
    /// input event's own timestamp on the same clock, so frame rate never changes timing. Contact is resolved by the
    /// deterministic <see cref="ContactResolver"/> (no colliders).
    /// Controls: Space / gamepad South = throw (when idle) or swing (during a pitch); WASD / left stick = move PCI;
    /// Left/Right or D-pad = previous/next pitch type; 1/2 = playback speed 1×/0.5×.
    /// </summary>
    public sealed class HittingLabController : MonoBehaviour
    {
        private const float PciSpeed = 1.2f; // m/s

        [SerializeField] private SwingParameters _swing = SwingParameters.Default;
        [SerializeField, Range(0.1f, 1f)] private float _playbackSpeed = 1f;

        [Header("Scene references")]
        [SerializeField] private Transform _ball;
        [SerializeField] private Transform _rubber;
        [SerializeField] private Transform _plate;
        [SerializeField] private LineRenderer _strikeZone;
        [SerializeField] private LineRenderer _pci;
        [SerializeField] private Transform _contactMarker;
        [SerializeField] private LineRenderer _exitRay;
        [SerializeField] private LineRenderer _pitchPath;
        [SerializeField] private Camera _camera;

        private static readonly PitchInput[] Presets = PitchPresets.All;
        private InputAction _swingAction;
        private int _presetIndex;
        private double _pciX, _pciZ;
        private double _pitchStartRealtime = double.NaN;
        private float _pitchPlaybackSpeed = 1f; // latched at throw; speed changes apply to the next pitch
        private bool _swung;
        private string _readout = "Press Space / A to throw.";
        private Vector3[] _pathBuffer = new Vector3[256];

        public HittingPitch CurrentPitch { get; private set; }
        public ContactResult? LastResult { get; private set; }
        /// <summary>Flight of the last batted ball (first landing), or null after a miss.</summary>
        public BattedBallResult LastBattedBall { get; private set; }
        public SwingInput? LastSwing { get; private set; }
        public int PitchesThrown { get; private set; }
        public double PciX => _pciX;
        public double PciZ => _pciZ;
        public SwingParameters Swing => _swing;
        public EnvironmentState Environment => EnvironmentState.Standard;
        public bool PitchInFlight => CurrentPitch != null && !_swung && SimTime < CurrentPitch.Flight.Duration;
        public double SimTime => ToSimTime(Time.realtimeSinceStartupAsDouble);
        public Transform BallTransform => _ball;

        /// <summary>Simulation time (s after release) of a moment on the real-time clock shared with input events.</summary>
        public double ToSimTime(double realtime) => double.IsNaN(_pitchStartRealtime) ? 0.0 : (realtime - _pitchStartRealtime) * _pitchPlaybackSpeed;

        private void Awake()
        {
            if (_ball == null || _rubber == null || _plate == null || _strikeZone == null || _pci == null || _contactMarker == null ||
                _exitRay == null || _pitchPath == null || _camera == null)
            {
                UnityEngine.Debug.LogError("HittingLabController is missing a scene reference; disabling.", this);
                enabled = false;
                return;
            }

            _swingAction = new InputAction("Swing", InputActionType.Button);
            _swingAction.AddBinding("<Keyboard>/space");
            _swingAction.AddBinding("<Gamepad>/buttonSouth");
            _swingAction.performed += OnSwingPressed;
        }

        private void OnEnable() => _swingAction?.Enable();
        private void OnDisable() => _swingAction?.Disable();
        private void OnDestroy() => _swingAction?.Dispose();

        private void Start()
        {
            PlaceField();
            ResetPci();
            _camera.transform.SetPositionAndRotation(new Vector3(0f, 1.1f, -2.5f), Quaternion.Euler(-1f, 0f, 0f));
            _camera.fieldOfView = 40f;
            ShowIdle();
        }

        /// <summary>Simulates the selected pitch and starts its playback now.</summary>
        public void ThrowPitch(int presetIndex)
        {
            _presetIndex = ((presetIndex % Presets.Length) + Presets.Length) % Presets.Length;
            CurrentPitch = HittingPitch.Create(Presets[_presetIndex], Environment, _swing.ContactPlaneY);
            _pitchStartRealtime = Time.realtimeSinceStartupAsDouble;
            _pitchPlaybackSpeed = _playbackSpeed;
            _swung = false;
            LastResult = null;
            LastSwing = null;
            LastBattedBall = null;
            PitchesThrown++;
            _pitchPath.enabled = false;
            _exitRay.enabled = false;
            _contactMarker.gameObject.SetActive(false);
            _readout = $"{Presets[_presetIndex].Label}: swing (Space / A)!";
        }

        public void SetPci(double x, double z)
        {
            _pciX = Math.Max(-0.6, Math.Min(0.6, x));
            _pciZ = Math.Max(0.2, Math.Min(1.4, z));
            DrawPci();
        }

        /// <summary>Swing that started at simulation time <paramref name="startTime"/> (s after release), PCI as now.</summary>
        public ContactResult SwingAtSimTime(double startTime)
        {
            var swing = new SwingInput(startTime, _pciX, _pciZ);
            ContactResult result = ContactResolver.Resolve(CurrentPitch, swing, _swing);
            _swung = true;
            LastSwing = swing;
            LastResult = result;
            ShowResult(result);
            return result;
        }

        // Event timestamp (same timeline as Time.realtimeSinceStartupAsDouble), not the frame time.
        private void OnSwingPressed(InputAction.CallbackContext context) => PressSwingButton(context.time);

        /// <summary>
        /// The swing button at real time <paramref name="eventRealtime"/>: throws when no pitch is live, otherwise swings
        /// at exactly that moment, however many frames have passed since.
        /// </summary>
        public void PressSwingButton(double eventRealtime)
        {
            if (CurrentPitch == null || _swung || ToSimTime(eventRealtime) >= CurrentPitch.Flight.Duration)
            {
                ThrowPitch(_presetIndex);
                return;
            }

            SwingAtSimTime(ToSimTime(eventRealtime));
        }

        private void Update()
        {
            HandleFrameInput();
            if (CurrentPitch == null) return;

            double t = SimTime;
            if (_swung && LastResult.HasValue && LastResult.Value.IsContact && t >= LastResult.Value.BattedBall.Time)
            {
                // Freeze the pitch at the contact point; the batted-ball flight is drawn as a path (no fielding yet).
                _ball.position = SimulationSpace.ToUnity(LastResult.Value.BattedBall.Position);
                return;
            }

            _ball.position = SimulationSpace.ToUnity(CurrentPitch.Flight.StateAt(t).Position);
        }

        private void HandleFrameInput()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            Vector2 move = Vector2.zero;
            if (keyboard != null)
            {
                move.x += (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                move.y += (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                if (keyboard.rightArrowKey.wasPressedThisFrame) _presetIndex = (_presetIndex + 1) % Presets.Length;
                if (keyboard.leftArrowKey.wasPressedThisFrame) _presetIndex = (_presetIndex + Presets.Length - 1) % Presets.Length;
                if (keyboard.digit1Key.wasPressedThisFrame) _playbackSpeed = 1f;
                if (keyboard.digit2Key.wasPressedThisFrame) _playbackSpeed = 0.5f;
            }

            if (gamepad != null)
            {
                move += gamepad.leftStick.ReadValue();
                if (gamepad.dpad.right.wasPressedThisFrame) _presetIndex = (_presetIndex + 1) % Presets.Length;
                if (gamepad.dpad.left.wasPressedThisFrame) _presetIndex = (_presetIndex + Presets.Length - 1) % Presets.Length;
            }

            if (move != Vector2.zero) SetPci(_pciX + move.x * PciSpeed * Time.deltaTime, _pciZ + move.y * PciSpeed * Time.deltaTime);
        }

        private void ShowResult(ContactResult r)
        {
            double inches(double m) => Units.MetersToInches(m);
            string timing = double.IsNaN(r.TimingError) ? "-" : $"{r.TimingError * 1000.0:+0;-0} ms ({r.Timing})";
            if (r.IsContact)
            {
                LastBattedBall = BattedBallSimulation.Run(r.BattedBall, Environment);
                BattedBallMetrics flight = LastBattedBall.Metrics;
                _readout =
                    $"{Presets[_presetIndex].Label}  timing {timing}\n" +
                    $"PCI offset: barrel {inches(r.OffsetAlongBarrel):+0.0;-0.0} in, vertical {inches(r.VerticalOffset):+0.0;-0.0} in (+ = under ball)\n" +
                    $"Exit velocity {Units.MetersPerSecondToMph(r.ExitSpeed):0.0} mph   launch {r.LaunchAngleDegrees:+0;-0}°   spray {r.SprayAngleDegrees:+0;-0}° (+ = RF)\n" +
                    $"Spin {Units.RadiansPerSecondToRpm(r.BattedBall.Spin.Length):0} rpm   q {r.CollisionEfficiency:0.00}" +
                    (Math.Abs(r.SprayAngleDegrees) > 45.0 ? "   FOUL" : "") +
                    $"\nFlight: {Units.MetersToFeet(flight.Distance):0} ft, hang {flight.HangTime:0.00} s, apex {Units.MetersToFeet(flight.ApexHeight):0} ft" +
                    " (model carries ≈ 30 ft long vs 2024 Statcast)";
                Vector3 contact = SimulationSpace.ToUnity(r.BattedBall.Position);
                _contactMarker.position = contact;
                _contactMarker.gameObject.SetActive(true);
                SetPath(_exitRay, LastBattedBall.Flight);
                _exitRay.enabled = true;
            }
            else
            {
                _readout = $"{Presets[_presetIndex].Label}  timing {timing}\nMISS: {r.Outcome}" +
                           (double.IsNaN(r.VerticalOffset) ? "" : $" (barrel {inches(r.OffsetAlongBarrel):+0.0;-0.0} in, vertical {inches(r.VerticalOffset):+0.0;-0.0} in)");
            }

            SetPath(_pitchPath, CurrentPitch.Flight);
            _pitchPath.enabled = true;
        }

        private void ShowIdle() => _readout = $"Next: {Presets[_presetIndex].Label}. Press Space / A to throw.";

        private void ResetPci() => SetPci(0.0, 0.5 * (PitchingGeometry.DefaultZoneBottom + PitchingGeometry.DefaultZoneTop));

        private void DrawPci()
        {
            // PCI drawn at the contact plane: width = barrel contact range, height = bat–ball centre distance.
            double hx = _swing.BarrelHalfLength;
            double hz = BallProperties.Baseball.Radius + _swing.BarrelRadius;
            double y = _swing.ContactPlaneY;
            _pci.loop = true;
            _pci.positionCount = 4;
            _pci.SetPosition(0, SimulationSpace.ToUnity(new Vector3d(_pciX - hx, y, _pciZ - hz)));
            _pci.SetPosition(1, SimulationSpace.ToUnity(new Vector3d(_pciX + hx, y, _pciZ - hz)));
            _pci.SetPosition(2, SimulationSpace.ToUnity(new Vector3d(_pciX + hx, y, _pciZ + hz)));
            _pci.SetPosition(3, SimulationSpace.ToUnity(new Vector3d(_pciX - hx, y, _pciZ + hz)));
        }

        private void SetPath(LineRenderer line, TrajectoryResult trajectory)
        {
            int count = trajectory.Samples.Count;
            if (_pathBuffer.Length < count) _pathBuffer = new Vector3[count];
            for (int i = 0; i < count; i++) _pathBuffer[i] = SimulationSpace.ToUnity(trajectory.Samples[i].Position);
            line.positionCount = count;
            line.SetPositions(_pathBuffer);
        }

        private void PlaceField()
        {
            double rubberDepth = Units.InchesToMeters(6.0);
            _rubber.position = SimulationSpace.ToUnity(new Vector3d(0.0, PitchingGeometry.RubberFrontY + rubberDepth / 2.0, 0.0));
            _rubber.localScale = new Vector3((float)Units.InchesToMeters(24.0), 0.02f, (float)rubberDepth);
            _plate.position = SimulationSpace.ToUnity(new Vector3d(0.0, PitchingGeometry.PlateFrontY / 2.0, 0.0));
            _plate.localScale = new Vector3((float)(2.0 * PitchingGeometry.PlateHalfWidth), 0.01f, (float)PitchingGeometry.PlateFrontY);

            double x = PitchingGeometry.PlateHalfWidth;
            double y = PitchingGeometry.PlateFrontY;
            _strikeZone.loop = true;
            _strikeZone.positionCount = 4;
            _strikeZone.SetPosition(0, SimulationSpace.ToUnity(new Vector3d(-x, y, PitchingGeometry.DefaultZoneBottom)));
            _strikeZone.SetPosition(1, SimulationSpace.ToUnity(new Vector3d(x, y, PitchingGeometry.DefaultZoneBottom)));
            _strikeZone.SetPosition(2, SimulationSpace.ToUnity(new Vector3d(x, y, PitchingGeometry.DefaultZoneTop)));
            _strikeZone.SetPosition(3, SimulationSpace.ToUnity(new Vector3d(-x, y, PitchingGeometry.DefaultZoneTop)));

            float diameter = (float)(2.0 * BallProperties.Baseball.Radius);
            _ball.localScale = new Vector3(diameter, diameter, diameter);
            _ball.position = SimulationSpace.ToUnity(Presets[0].ToInitialState().Position);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 470, 220), GUI.skin.box);
            GUILayout.Label($"HittingLab — pitch: {Presets[_presetIndex].Label}   playback {_playbackSpeed:0.0}×   pitches {PitchesThrown}");
            GUILayout.Label(_readout);
            GUILayout.Label("Space/A: throw or swing · WASD/stick: PCI · ←/→: pitch type · 1/2: speed 1×/0.5×");
            GUILayout.EndArea();
        }
    }
}
