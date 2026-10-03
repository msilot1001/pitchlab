using System;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Batted-Ball Physics Sandbox: launch a batted ball from exit speed, launch angle, spray and spin, fly it with the
    /// shared simulator (<see cref="AerodynamicModel.BattedBall"/>) to its first landing, and show the flight and its
    /// metrics. No fielders, walls or bounces. Controls: panel; Space / gamepad South = relaunch; C / North = camera.
    /// </summary>
    public sealed class BattedBallLabController : MonoBehaviour
    {
        private static readonly (string Name, BattedBallLaunch Launch)[] Presets =
        {
            ("Ground ball", new BattedBallLaunch(95.0, -5.0, -15.0, -800.0)),
            ("Line drive", new BattedBallLaunch(100.0, 14.0, 10.0, 700.0)),
            ("Fly ball", new BattedBallLaunch(95.0, 30.0, -5.0, 2300.0)),
            ("Deep fly ball", new BattedBallLaunch(108.0, 28.0, -20.0, 2100.0)),
        };

        [SerializeField] private double _exitSpeedMph = 100.0;
        [SerializeField] private double _launchAngle = 28.0;
        [SerializeField] private double _sprayAngle;
        [SerializeField] private double _backspinRpm = 2100.0;
        [SerializeField] private double _sidespinRpm;
        [SerializeField, Range(-20f, 50f)] private double _temperatureCelsius = 21.0;
        [SerializeField, Range(0f, 3000f)] private double _elevationMeters;

        [Header("Scene references")]
        [SerializeField] private Transform _ball;
        [SerializeField] private LineRenderer _flightPath;
        [SerializeField] private LineRenderer _foulLines;
        [SerializeField] private LineRenderer _distanceArcs;
        [SerializeField] private Transform _landingMarker;
        [SerializeField] private Camera _camera;

        private Vector3[] _pathBuffer = new Vector3[2048];
        private double _launchRealtime = double.NaN;
        private bool _overhead = true;
        private string _readout = string.Empty;

        public BattedBallResult LastFlight { get; private set; }
        public static readonly Vector3d ContactPoint = new Vector3d(0.0, 0.7, 0.9);

        public BattedBallLaunch CurrentLaunch => new BattedBallLaunch(_exitSpeedMph, _launchAngle, _sprayAngle, _backspinRpm, _sidespinRpm);

        public EnvironmentState Environment => EnvironmentState.FromWeather(_temperatureCelsius,
            EnvironmentState.StandardAtmospherePressure(_elevationMeters), 0.5, Vector3d.Zero);

        private void Awake()
        {
            if (_ball == null || _flightPath == null || _foulLines == null || _distanceArcs == null || _landingMarker == null || _camera == null)
            {
                UnityEngine.Debug.LogError("BattedBallLabController is missing a scene reference; disabling.", this);
                enabled = false;
            }
        }

        private void Start()
        {
            DrawField();
            ApplyCamera();
            Launch();
        }

        public void ApplyPreset(int index)
        {
            BattedBallLaunch l = Presets[((index % Presets.Length) + Presets.Length) % Presets.Length].Launch;
            _exitSpeedMph = l.ExitSpeedMph;
            _launchAngle = l.LaunchAngleDegrees;
            _sprayAngle = l.SprayAngleDegrees;
            _backspinRpm = l.BackspinRpm;
            _sidespinRpm = l.SidespinRpm;
            Launch();
        }

        public void Launch()
        {
            LastFlight = BattedBallSimulation.Run(CurrentLaunch.ToState(ContactPoint), Environment);
            int count = LastFlight.Flight.Samples.Count;
            if (_pathBuffer.Length < count) _pathBuffer = new Vector3[count];
            for (int i = 0; i < count; i++) _pathBuffer[i] = SimulationSpace.ToUnity(LastFlight.Flight.Samples[i].Position);
            _flightPath.positionCount = count;
            _flightPath.SetPositions(_pathBuffer);
            _landingMarker.position = SimulationSpace.ToUnity(LastFlight.Flight.Final.Position);
            _launchRealtime = Time.realtimeSinceStartupAsDouble;

            BattedBallMetrics m = LastFlight.Metrics;
            string kind = m.LaunchAngleDegrees < 10.0 ? "ground ball" : m.LaunchAngleDegrees < 25.0 ? "line drive" : m.LaunchAngleDegrees < 50.0 ? "fly ball" : "pop-up";
            _readout =
                $"EV {Units.MetersPerSecondToMph(m.ExitSpeed):0.0} mph   launch {m.LaunchAngleDegrees:+0;-0}°   spray {m.SprayAngleDegrees:+0;-0}° (+ = RF)   ({kind})\n" +
                $"Spin {Units.RadiansPerSecondToRpm(m.SpinRate):0} rpm   air density {Environment.AirDensity:0.000} kg/m³\n" +
                (m.Landed
                    ? $"Distance {Units.MetersToFeet(m.Distance):0} ft   hang time {m.HangTime:0.00} s   apex {Units.MetersToFeet(m.ApexHeight):0} ft\n" +
                      $"Landing x {Units.MetersToFeet(m.LandingX):+0;-0} ft, y {Units.MetersToFeet(m.LandingY):0} ft" +
                      (Math.Abs(Math.Atan2(m.LandingX, m.LandingY)) > Math.PI / 4.0 ? "   FOUL" : "")
                    : "Still in the air at the time limit") +
                "\nNote: this model carries fly balls ≈ 30 ft further than 2024 Statcast (Docs/VALIDATION_TASK004.md).";
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            if ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame) || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)) Launch();
            if ((keyboard != null && keyboard.cKey.wasPressedThisFrame) || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame))
            {
                _overhead = !_overhead;
                ApplyCamera();
            }

            if (LastFlight == null) return;
            double t = Time.realtimeSinceStartupAsDouble - _launchRealtime;
            _ball.position = SimulationSpace.ToUnity(LastFlight.Flight.StateAt(LastFlight.Flight.First.Time + t).Position);
        }

        private void ApplyCamera()
        {
            if (_overhead)
            {
                _camera.transform.SetPositionAndRotation(new Vector3(0f, 190f, 55f), Quaternion.Euler(90f, 0f, 0f));
                _camera.fieldOfView = 60f;
            }
            else
            {
                _camera.transform.SetPositionAndRotation(new Vector3(-95f, 25f, 60f), Quaternion.Euler(8f, 90f, 0f));
                _camera.fieldOfView = 60f;
            }
        }

        private void DrawField()
        {
            // Foul lines 400 ft down each line; distance arcs at 300 and 400 ft between the foul lines.
            double line = Units.FeetToMeters(400.0);
            _foulLines.positionCount = 3;
            _foulLines.SetPosition(0, SimulationSpace.ToUnity(new Vector3d(-line * Math.Sqrt(0.5), line * Math.Sqrt(0.5), 0.02)));
            _foulLines.SetPosition(1, SimulationSpace.ToUnity(new Vector3d(0.0, 0.0, 0.02)));
            _foulLines.SetPosition(2, SimulationSpace.ToUnity(new Vector3d(line * Math.Sqrt(0.5), line * Math.Sqrt(0.5), 0.02)));

            const int segments = 24;
            _distanceArcs.positionCount = 2 * (segments + 1);
            int k = 0;
            foreach (double feet in new[] { 300.0, 400.0 })
            {
                double r = Units.FeetToMeters(feet);
                for (int i = 0; i <= segments; i++)
                {
                    int index = feet < 350.0 ? i : segments - i;   // walk the second arc backwards so the line zig-zags cleanly
                    double angle = -Math.PI / 4.0 + (Math.PI / 2.0) * index / segments;
                    _distanceArcs.SetPosition(k++, SimulationSpace.ToUnity(new Vector3d(r * Math.Sin(angle), r * Math.Cos(angle), 0.02)));
                }
            }

            float d = (float)(2.0 * BallProperties.Baseball.Radius) * 8f;   // drawn 8× larger to be visible at field scale
            _ball.localScale = new Vector3(d, d, d);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 380, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("BattedBallLab");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Presets.Length; i++)
                if (GUILayout.Button(Presets[i].Name)) ApplyPreset(i);
            GUILayout.EndHorizontal();
            bool changed = false;
            changed |= Slider("Exit speed (mph)", ref _exitSpeedMph, 40, 120);
            changed |= Slider("Launch angle (°)", ref _launchAngle, -20, 70);
            changed |= Slider("Spray angle (°, + = RF)", ref _sprayAngle, -50, 50);
            changed |= Slider("Backspin (rpm, − = topspin)", ref _backspinRpm, -2000, 5000);
            changed |= Slider("Sidespin (rpm, + curves to RF)", ref _sidespinRpm, -3000, 3000);
            changed |= Slider("Temperature (°C)", ref _temperatureCelsius, -20, 50);
            changed |= Slider("Elevation (m)", ref _elevationMeters, 0, 3000);
            if (GUILayout.Button("Launch (Space)") || changed) Launch();
            GUILayout.Label(_readout);
            GUILayout.Label("C: side / overhead camera");
            GUILayout.EndArea();
        }

        private static bool Slider(string label, ref double value, double min, double max)
        {
            GUILayout.Label($"{label}: {value:0.#}");
            float shown = Mathf.Clamp((float)value, (float)min, (float)max);
            float next = GUILayout.HorizontalSlider(shown, (float)min, (float)max);
            if (next == shown) return false;
            value = Math.Round(next, 1);
            return true;
        }
    }
}
