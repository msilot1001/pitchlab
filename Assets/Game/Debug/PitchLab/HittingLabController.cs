using System;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Hitting Sandbox. A pitch is simulated once and played back on a real-time clock. Everything authoritative —
    /// the throw, the swing, and the PCI position at the swing — is taken from Input System event timestamps on that
    /// same clock (PCI movement is an exact function of timestamped aim events, <see cref="PciTrack"/>), so the
    /// contact result does not depend on when frames happen. Frames only render. Contact is resolved by the
    /// deterministic <see cref="ContactResolver"/> (no colliders).
    /// Controls: Space / gamepad South = throw (when idle) or swing (during a pitch); WASD / left stick = move PCI;
    /// Left/Right or D-pad = previous/next pitch type; 1/2 = playback speed 1×/0.5×.
    /// </summary>
    public sealed class HittingLabController : MonoBehaviour
    {
        private const double PciSpeed = 1.2; // m/s at full stick / key
        private const double PciMinX = -0.6, PciMaxX = 0.6, PciMinZ = 0.2, PciMaxZ = 1.4;

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
        private InputAction _swingAction, _aimAction, _nextPresetAction, _previousPresetAction, _normalSpeedAction, _slowSpeedAction;
        private int _presetIndex;
        private PciTrack _pciTrack;
        private double _pitchStartRealtime = double.NaN;
        private float _pitchPlaybackSpeed = 1f; // latched at throw; speed changes apply to the next pitch
        private bool _swung;
        private string _pitchLabel = string.Empty; // latched at throw; the preset selection may change mid-pitch
        private string _readout = "Press Space / A to throw.";
        private Vector3[] _pathBuffer = new Vector3[256];

        /// <summary>
        /// The real-time clock shared with Input System event timestamps (Time.realtimeSinceStartupAsDouble). Tests replace
        /// it to drive a scripted input trace under chosen frame schedules.
        /// </summary>
        public Func<double> Clock { get; set; } = () => Time.realtimeSinceStartupAsDouble;

        public HittingPitch CurrentPitch { get; private set; }
        public ContactResult? LastResult { get; private set; }
        public SwingInput? LastSwing { get; private set; }
        public int PitchesThrown { get; private set; }
        public SwingParameters Swing => _swing;
        public EnvironmentState Environment => EnvironmentState.Standard;
        public bool PitchInFlight => CurrentPitch != null && !_swung && SimTime < CurrentPitch.Flight.Duration;
        public double SimTime => ToSimTime(Clock());
        public Transform BallTransform => _ball;

        /// <summary>Simulation time (s after release) of a moment on the real-time clock shared with input events.</summary>
        public double ToSimTime(double realtime) => double.IsNaN(_pitchStartRealtime) ? 0.0 : (realtime - _pitchStartRealtime) * _pitchPlaybackSpeed;

        /// <summary>PCI position at a moment on the real-time clock (exact, from timestamped aim input).</summary>
        public (double X, double Z) PciAt(double realtime) => _pciTrack.PositionAt(realtime);

        private void Awake()
        {
            if (_ball == null || _rubber == null || _plate == null || _strikeZone == null || _pci == null || _contactMarker == null ||
                _exitRay == null || _pitchPath == null || _camera == null)
            {
                UnityEngine.Debug.LogError("HittingLabController is missing a scene reference; disabling.", this);
                enabled = false;
                return;
            }

            _pciTrack = new PciTrack(PciMinX, PciMaxX, PciMinZ, PciMaxZ, Clock(), 0.0,
                0.5 * (PitchingGeometry.DefaultZoneBottom + PitchingGeometry.DefaultZoneTop));

            _swingAction = new InputAction("Swing", InputActionType.Button);
            _swingAction.AddBinding("<Keyboard>/space");
            _swingAction.AddBinding("<Gamepad>/buttonSouth");
            _swingAction.performed += context => PressSwingButton(context.time);

            // Digital (un-normalised) WASD keeps the previous per-axis key speed; the stick is analogue.
            _aimAction = new InputAction("Aim", InputActionType.Value, expectedControlType: "Vector2");
            _aimAction.AddCompositeBinding("2DVector(mode=1)")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _aimAction.AddBinding("<Gamepad>/leftStick");
            _aimAction.performed += OnAim;
            _aimAction.canceled += OnAim;

            _nextPresetAction = Button("<Keyboard>/rightArrow", "<Gamepad>/dpad/right", _ => _presetIndex = (_presetIndex + 1) % Presets.Length);
            _previousPresetAction = Button("<Keyboard>/leftArrow", "<Gamepad>/dpad/left", _ => _presetIndex = (_presetIndex + Presets.Length - 1) % Presets.Length);
            _normalSpeedAction = Button("<Keyboard>/digit1", null, _ => _playbackSpeed = 1f);
            _slowSpeedAction = Button("<Keyboard>/digit2", null, _ => _playbackSpeed = 0.5f);
        }

        private static InputAction Button(string binding, string secondBinding, Action<InputAction.CallbackContext> onPress)
        {
            var action = new InputAction(type: InputActionType.Button, binding: binding);
            if (secondBinding != null) action.AddBinding(secondBinding);
            action.performed += onPress;
            return action;
        }

        private void OnEnable()
        {
            _swingAction?.Enable();
            _aimAction?.Enable();
            _nextPresetAction?.Enable();
            _previousPresetAction?.Enable();
            _normalSpeedAction?.Enable();
            _slowSpeedAction?.Enable();
        }

        private void OnDisable()
        {
            _swingAction?.Disable();
            _aimAction?.Disable();
            _nextPresetAction?.Disable();
            _previousPresetAction?.Disable();
            _normalSpeedAction?.Disable();
            _slowSpeedAction?.Disable();
        }

        private void OnDestroy()
        {
            _swingAction?.Dispose();
            _aimAction?.Dispose();
            _nextPresetAction?.Dispose();
            _previousPresetAction?.Dispose();
            _normalSpeedAction?.Dispose();
            _slowSpeedAction?.Dispose();
        }

        private void Start()
        {
            PlaceField();
            _camera.transform.SetPositionAndRotation(new Vector3(0f, 1.1f, -2.5f), Quaternion.Euler(-1f, 0f, 0f));
            _camera.fieldOfView = 40f;
            ShowIdle();
        }

        /// <summary>Simulates the selected pitch and starts its playback now.</summary>
        public void ThrowPitch(int presetIndex) => ThrowPitch(presetIndex, Clock());

        /// <summary>Simulates the selected pitch; it is released at <paramref name="releaseRealtime"/> on the shared clock.</summary>
        public void ThrowPitch(int presetIndex, double releaseRealtime)
        {
            _presetIndex = ((presetIndex % Presets.Length) + Presets.Length) % Presets.Length;
            CurrentPitch = HittingPitch.Create(Presets[_presetIndex], Environment);
            _pitchStartRealtime = releaseRealtime;
            _pitchPlaybackSpeed = _playbackSpeed;
            _swung = false;
            LastResult = null;
            LastSwing = null;
            PitchesThrown++;
            _pitchPath.enabled = false;
            _exitRay.enabled = false;
            _contactMarker.gameObject.SetActive(false);
            _pitchLabel = Presets[_presetIndex].Label;
            _readout = $"{_pitchLabel}: swing (Space / A)!";
        }

        /// <summary>Places the PCI at the current clock time, keeping any aim motion (debug/test entry point, not input).</summary>
        public void SetPci(double x, double z) => _pciTrack.Place(Clock(), x, z);

        /// <summary>
        /// Swing that started at simulation time <paramref name="startTime"/> (s after release), with the PCI where it was
        /// at that moment.
        /// </summary>
        public ContactResult SwingAtSimTime(double startTime)
        {
            (double x, double z) = PciAt(_pitchStartRealtime + startTime / _pitchPlaybackSpeed);
            var swing = new SwingInput(startTime, x, z);
            ContactResult result = ContactResolver.Resolve(CurrentPitch, swing, _swing);
            _swung = true;
            LastSwing = swing;
            LastResult = result;
            ShowResult(result);
            return result;
        }

        /// <summary>
        /// The swing button at real time <paramref name="eventRealtime"/> (the input event's timestamp): throws when no
        /// pitch is live (released at that moment), otherwise swings at exactly that moment with the PCI where it was
        /// then, however many frames have passed since.
        /// </summary>
        public void PressSwingButton(double eventRealtime)
        {
            if (CurrentPitch == null || _swung || ToSimTime(eventRealtime) >= CurrentPitch.Flight.Duration)
            {
                ThrowPitch(_presetIndex, eventRealtime);
                return;
            }

            SwingAtSimTime(ToSimTime(eventRealtime));
        }

        // Known limit: the Input System merges consecutive DualSense stick reports within one update (IEventMerger),
        // so with that pad the sampled stick path can depend on how reports group into frames (≈ mm). Keyboard and
        // other gamepads deliver every report. Button presses (swing) are never merged.
        private void OnAim(InputAction.CallbackContext context)
        {
            Vector2 aim = context.ReadValue<Vector2>();
            _pciTrack.SetVelocity(context.time, aim.x * PciSpeed, aim.y * PciSpeed);
        }

        private void Update() => FrameUpdate(Clock());

        /// <summary>Renders the state at real time <paramref name="now"/>. Nothing authoritative happens here.</summary>
        public void FrameUpdate(double now)
        {
            if (_pciTrack == null) return; // disabled in Awake (missing scene reference)
            DrawPci(now);
            if (CurrentPitch == null) return;

            double t = ToSimTime(now);
            if (_swung && LastResult.HasValue && LastResult.Value.IsContact && t >= LastResult.Value.BattedBall.Time)
            {
                // Freeze at the contact point; the exit ray shows the batted-ball direction.
                _ball.position = SimulationSpace.ToUnity(LastResult.Value.BattedBall.Position);
                return;
            }

            _ball.position = SimulationSpace.ToUnity(CurrentPitch.Flight.StateAt(t).Position);
        }

        private void ShowResult(ContactResult r)
        {
            double inches(double m) => Units.MetersToInches(m);
            string timing = double.IsNaN(r.TimingError) ? "-" : $"{r.TimingError * 1000.0:+0;-0} ms ({r.Timing})";
            if (r.IsContact)
            {
                _readout =
                    $"{_pitchLabel}  timing {timing}\n" +
                    $"PCI offset: barrel {inches(r.OffsetAlongBarrel):+0.0;-0.0} in, vertical {inches(r.VerticalOffset):+0.0;-0.0} in (+ = under ball)\n" +
                    $"Exit velocity {Units.MetersPerSecondToMph(r.ExitSpeed):0.0} mph   launch {r.LaunchAngleDegrees:+0;-0}°   spray {r.SprayAngleDegrees:+0;-0}° (+ = RF)\n" +
                    $"Spin {Units.RadiansPerSecondToRpm(r.BattedBall.Spin.Length):0} rpm   q {r.CollisionEfficiency:0.00}" +
                    (Math.Abs(r.SprayAngleDegrees) > 45.0 ? "   FOUL" : "");
                Vector3 contact = SimulationSpace.ToUnity(r.BattedBall.Position);
                _contactMarker.position = contact;
                _contactMarker.gameObject.SetActive(true);
                _exitRay.positionCount = 2;
                _exitRay.SetPosition(0, contact);
                _exitRay.SetPosition(1, contact + SimulationSpace.ToUnity(r.BattedBall.Velocity.Normalized * 6.0));
                _exitRay.enabled = true;
            }
            else
            {
                _readout = $"{_pitchLabel}  timing {timing}\nMISS: {r.Outcome}" +
                           (double.IsNaN(r.VerticalOffset) ? "" : $" (barrel {inches(r.OffsetAlongBarrel):+0.0;-0.0} in, vertical {inches(r.VerticalOffset):+0.0;-0.0} in)");
            }

            SetPath(CurrentPitch.Flight);
            _pitchPath.enabled = true;
        }

        private void ShowIdle() => _readout = $"Next: {Presets[_presetIndex].Label}. Press Space / A to throw.";

        private void DrawPci(double now)
        {
            // PCI drawn at the contact plane: width = barrel contact range, height = bat–ball centre distance.
            (double x, double z) = PciAt(now);
            double hx = _swing.BarrelHalfLength;
            double hz = BallProperties.Baseball.Radius + _swing.BarrelRadius;
            double y = CurrentPitch?.ContactPlaneY ?? HittingPitch.DefaultContactPlaneY;
            _pci.loop = true;
            _pci.positionCount = 4;
            _pci.SetPosition(0, SimulationSpace.ToUnity(new Vector3d(x - hx, y, z - hz)));
            _pci.SetPosition(1, SimulationSpace.ToUnity(new Vector3d(x + hx, y, z - hz)));
            _pci.SetPosition(2, SimulationSpace.ToUnity(new Vector3d(x + hx, y, z + hz)));
            _pci.SetPosition(3, SimulationSpace.ToUnity(new Vector3d(x - hx, y, z + hz)));
        }

        private void SetPath(TrajectoryResult trajectory)
        {
            int count = trajectory.Samples.Count;
            if (_pathBuffer.Length < count) _pathBuffer = new Vector3[count];
            for (int i = 0; i < count; i++) _pathBuffer[i] = SimulationSpace.ToUnity(trajectory.Samples[i].Position);
            _pitchPath.positionCount = count;
            _pitchPath.SetPositions(_pathBuffer);
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
            GUILayout.BeginArea(new Rect(10, 10, 430, 200), GUI.skin.box);
            GUILayout.Label($"HittingLab — pitch: {Presets[_presetIndex].Label}   playback {_playbackSpeed:0.0}×   pitches {PitchesThrown}");
            GUILayout.Label(_readout);
            GUILayout.Label("Space/A: throw or swing · WASD/stick: PCI · ←/→: pitch type · 1/2: speed 1×/0.5×");
            GUILayout.EndArea();
        }
    }
}
