using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Pitch Physics Sandbox. Runs the authoritative simulation once per throw, then plays the recorded flight back by
    /// simulation time. Frame timing only chooses which recorded instant is shown; it never changes the flight.
    /// Controls: on-screen panel; Space / gamepad South = throw; Left/Right or D-pad = previous/next preset;
    /// C / gamepad North = toggle camera.
    /// </summary>
    public sealed class PitchLabController : MonoBehaviour
    {
        [Header("Pitch (baseball units; converted to SI by PitchInput)")]
        [SerializeField] private PitchInput _input = PitchPresets.FourSeam;

        [Header("Environment")]
        [SerializeField] private double _temperatureCelsius = 21.0;
        [SerializeField, Range(50000f, 110000f)] private double _stationPressurePascals = 101325.0;
        [SerializeField, Range(0f, 1f)] private double _relativeHumidity = 0.5;

        [Header("Playback")]
        [SerializeField, Range(0.05f, 1f)] private float _playbackSpeed = 1f;
        [SerializeField] private bool _showNoLiftReference = true;

        [Header("Scene references")]
        [SerializeField] private Transform _ball;
        [SerializeField] private Transform _releaseMarker;
        [SerializeField] private Transform _rubber;
        [SerializeField] private Transform _plate;
        [SerializeField] private LineRenderer _flightPath;
        [SerializeField] private LineRenderer _referencePath;
        [SerializeField] private LineRenderer _strikeZone;
        [SerializeField] private Camera _camera;

        private static readonly PitchInput[] Presets = PitchPresets.All;
        private static readonly string[] PresetButtonLabels = Array.ConvertAll(Presets, p => p.Label.Replace("-like", ""));
        private int _presetIndex;
        private double _playbackTime;
        private bool _sideCamera;
        private Vector3[] _pathBuffer = new Vector3[256];
        private string _metricsText = string.Empty;
        private Vector2 _panelScroll;

        public PitchInput CurrentInput
        {
            get => _input;
            set => _input = value;
        }

        public PitchResult LastResult { get; private set; }
        public Transform Ball => _ball;
        public LineRenderer FlightPath => _flightPath;
        public double PlaybackTime => _playbackTime;
        public bool PlaybackFinished => LastResult != null && _playbackTime >= LastResult.Flight.Duration;

        public EnvironmentState Environment =>
            EnvironmentState.FromWeather(_temperatureCelsius, _stationPressurePascals, _relativeHumidity, Vector3d.Zero);

        private void Awake()
        {
            if (_ball == null || _releaseMarker == null || _rubber == null || _plate == null ||
                _flightPath == null || _referencePath == null || _strikeZone == null || _camera == null)
            {
                UnityEngine.Debug.LogError("PitchLabController is missing a scene reference; disabling.", this);
                enabled = false;
            }
        }

        private void Start()
        {
            PlaceFieldReferences();
            ApplyCamera();
            Throw();
        }

        /// <summary>Simulates the current input and restarts playback.</summary>
        public void Throw()
        {
            LastResult = PitchSimulation.Run(_input, Environment);
            _playbackTime = 0.0;
            SetPath(_flightPath, LastResult.Flight);
            SetPath(_referencePath, LastResult.NoLiftReference);
            _referencePath.enabled = _showNoLiftReference;
            _releaseMarker.position = SimulationSpace.ToUnity(LastResult.Flight.First.Position);
            _metricsText = FormatMetrics(LastResult);
            ShowBallAt(0.0);
        }

        public void ApplyPreset(int index)
        {
            _presetIndex = ((index % Presets.Length) + Presets.Length) % Presets.Length;
            _input = Presets[_presetIndex];
            Throw();
        }

        private void Update()
        {
            HandleDeviceInput();
            if (LastResult == null || PlaybackFinished) return;
            _playbackTime = Math.Min(_playbackTime + Time.deltaTime * _playbackSpeed, LastResult.Flight.Duration);
            ShowBallAt(_playbackTime);
        }

        private void HandleDeviceInput()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            if ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame) || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)) Throw();
            if ((keyboard != null && keyboard.rightArrowKey.wasPressedThisFrame) || (gamepad != null && gamepad.dpad.right.wasPressedThisFrame)) ApplyPreset(_presetIndex + 1);
            if ((keyboard != null && keyboard.leftArrowKey.wasPressedThisFrame) || (gamepad != null && gamepad.dpad.left.wasPressedThisFrame)) ApplyPreset(_presetIndex - 1);
            if ((keyboard != null && keyboard.cKey.wasPressedThisFrame) || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame))
            {
                _sideCamera = !_sideCamera;
                ApplyCamera();
            }
        }

        private void ShowBallAt(double time) =>
            _ball.position = SimulationSpace.ToUnity(LastResult.Flight.StateAt(LastResult.Flight.First.Time + time).Position);

        private void SetPath(LineRenderer line, TrajectoryResult trajectory)
        {
            int count = trajectory.Samples.Count;
            if (_pathBuffer.Length < count) _pathBuffer = new Vector3[count];
            for (int i = 0; i < count; i++) _pathBuffer[i] = SimulationSpace.ToUnity(trajectory.Samples[i].Position);
            line.positionCount = count;
            line.SetPositions(_pathBuffer);
        }

        private void PlaceFieldReferences()
        {
            // Rubber: 24 × 6 in slab whose front edge is the 60.5 ft reference. The mound's 10 in elevation is not
            // drawn; release height is measured from the plate's ground level, as in Statcast.
            double rubberDepth = Units.InchesToMeters(6.0);
            _rubber.position = SimulationSpace.ToUnity(new Vector3d(0.0, PitchingGeometry.RubberFrontY + rubberDepth / 2.0, 0.0));
            _rubber.localScale = new Vector3((float)Units.InchesToMeters(24.0), 0.02f, (float)rubberDepth);
            double plateDepth = PitchingGeometry.PlateFrontY;
            _plate.position = SimulationSpace.ToUnity(new Vector3d(0.0, plateDepth / 2.0, 0.0));
            _plate.localScale = new Vector3((float)(2.0 * PitchingGeometry.PlateHalfWidth), 0.01f, (float)plateDepth);

            // Zone outline on the plate-front plane, the plane where plate crossings are measured.
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
        }

        private void ApplyCamera()
        {
            // Catcher's view: behind the plate looking at the pitcher. Side view: from the first-base side.
            if (_sideCamera)
            {
                _camera.transform.SetPositionAndRotation(new Vector3(11f, 1.4f, 7.5f), Quaternion.Euler(0f, -90f, 0f));
                _camera.fieldOfView = 75f;
            }
            else
            {
                _camera.transform.SetPositionAndRotation(new Vector3(0f, 1.1f, -2.5f), Quaternion.Euler(-1f, 0f, 0f));
                _camera.fieldOfView = 40f;
            }
        }

        private static string FormatMetrics(PitchResult result)
        {
            PitchMetrics m = result.Metrics;
            Vector3d spin = m.SpinVector / Math.Max(m.SpinRate, 1e-12);
            string plate = m.ReachedPlate
                ? $"Plate crossing (front edge): x {Units.MetersToFeet(m.PlateX):+0.00;-0.00} ft, z {Units.MetersToFeet(m.PlateZ):0.00} ft\n" +
                  $"Speed at plate: {Units.MetersPerSecondToMph(m.PlateSpeed):0.0} mph\n" +
                  $"Movement vs no-lift reference (catcher's view):\n" +
                  $"  horizontal {Units.MetersToInches(m.HorizontalMovement):+0.0;-0.0} in (+ = 1B side)\n" +
                  $"  vertical   {Units.MetersToInches(m.VerticalMovement):+0.0;-0.0} in (+ = up)"
                : "Ball reached the ground before the plate.";
            return
                $"Release speed: {Units.MetersPerSecondToMph(m.ReleaseSpeed):0.0} mph\n" +
                $"Spin: {Units.RadiansPerSecondToRpm(m.SpinRate):0} rpm, efficiency {m.SpinEfficiency:0.00}\n" +
                $"Spin axis (unit, sim frame): ({spin.X:+0.00;-0.00}, {spin.Y:+0.00;-0.00}, {spin.Z:+0.00;-0.00})\n" +
                $"Flight time: {(m.ReachedPlate ? $"{m.FlightTime:0.000} s" : "-")}\n" +
                plate;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 360, Screen.height - 20), GUI.skin.box);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            GUILayout.Label($"PitchLab  —  {(string.IsNullOrEmpty(_input.Label) ? "Custom" : _input.Label)}");

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Presets.Length; i++)
            {
                if (GUILayout.Button(PresetButtonLabels[i])) ApplyPreset(i);
            }
            GUILayout.EndHorizontal();

            bool changed = false;
            changed |= Slider("Speed (mph)", ref _input.SpeedMph, 40, 105);
            changed |= Slider("Spin rate (rpm)", ref _input.SpinRateRpm, 0, 3500);
            changed |= Slider("Spin axis (deg, Statcast)", ref _input.SpinAxisDegrees, 0, 360);
            changed |= Slider("Gyro angle (deg)", ref _input.GyroAngleDegrees, -90, 90);
            changed |= Slider("Vertical angle (deg)", ref _input.VerticalAngleDegrees, -10, 10);
            changed |= Slider("Horizontal angle (deg)", ref _input.HorizontalAngleDegrees, -10, 10);
            changed |= Slider("Release side x (ft)", ref _input.ReleaseSideFeet, -4, 4);
            changed |= Slider("Release height (ft)", ref _input.ReleaseHeightFeet, 2, 8);
            changed |= Slider("Extension (ft)", ref _input.ExtensionFeet, 3, 8);
            changed |= Slider("Temperature (°C)", ref _temperatureCelsius, -10, 45);
            changed |= Slider("Pressure (Pa)", ref _stationPressurePascals, 80000, 105000);
            if (changed) _input.Label = "Custom";

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Throw / Reset (Space)") || changed) Throw();
            _showNoLiftReference = GUILayout.Toggle(_showNoLiftReference, "No-lift reference");
            GUILayout.EndHorizontal();
            _referencePath.enabled = _showNoLiftReference;
            GUILayout.Label($"Playback speed {_playbackSpeed:0.00}x");
            _playbackSpeed = GUILayout.HorizontalSlider(_playbackSpeed, 0.05f, 1f);

            GUILayout.Space(6);
            GUILayout.Label(_metricsText);
            GUILayout.Label("Orange: flight. Grey: same release without Magnus force. C: toggle camera.");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>Changes <paramref name="value"/> only when the user moves the slider, so authored values outside
        /// the slider range or with extra precision are never rewritten.</summary>
        private static bool Slider(string label, ref double value, double min, double max)
        {
            GUILayout.Label($"{label}: {value:0.##}");
            float shown = Mathf.Clamp((float)value, (float)min, (float)max);
            float next = GUILayout.HorizontalSlider(shown, (float)min, (float)max);
            if (next == shown) return false;
            value = Math.Round(next, 2);
            return true;
        }
    }
}
