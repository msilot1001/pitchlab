using System;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using Pitchlab.Simulation.Pitching;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Hitting Sandbox. A pitch is simulated once and played back on a real-time clock. Everything authoritative —
    /// the throw, the swing, and the PCI position at the swing — is taken from Input System event timestamps on that
    /// same clock (PCI movement is an exact function of timestamped aim events, <see cref="PciTrack"/>), so the
    /// contact result does not depend on when frames happen. Frames only render. Contact is resolved by the
    /// deterministic <see cref="ContactResolver"/> (no colliders).
    /// Controls: Space / gamepad South = throw (when idle) or swing (during a pitch); WASD / left stick = move PCI;
    /// Left/Right or D-pad = previous/next pitch type; 1/2 = playback speed 1×/0.5×; T = trajectory lines; H = panel.
    /// A throw press starts the pitcher's delivery; the authoritative release follows <see cref="DeliveryLead"/> later
    /// (presentation fits the wind-up into that time). Swing input is accepted from the release on; a press during the
    /// wind-up is ignored (it could never make contact, and ignoring it removes an accidental-whiff trap).
    /// Mouse: left click = throw/swing; mouse movement aims (relative deltas, timestamped per input event, exactly like
    /// the stick/keys). The first click into the game captures the cursor; Escape releases it.
    /// </summary>
    public sealed class HittingLabController : MonoBehaviour
    {
        private const double PciSpeed = 1.2; // m/s at full stick / key
        private static readonly PciFrame Pci = PciFrame.Default;

        [SerializeField] private SwingParameters _swing = SwingParameters.Default;
        [SerializeField, Range(0.1f, 1f)] private float _playbackSpeed = 1f;
        /// <summary>Simulation time from the throw press to the authoritative release, s (the pitcher's wind-up; real time = lead ÷ playback speed).</summary>
        [SerializeField, Min(0.5f)] private float _deliveryLead = 1.1f;
        [SerializeField] private bool _showDebugPaths;
        [SerializeField] private bool _showPanel = true;
        /// <summary>GameLab (TASK-010): plate appearances in a persistent half-inning (<see cref="Game"/>) instead of an empty
        /// diamond every pitch.</summary>
        [SerializeField] private bool _gameMode;
        [Header("Mouse aim")]
        /// <summary>Normalized PCI units per mouse count (the PCI spans 2 units across).</summary>
        [SerializeField, Range(0.0005f, 0.02f)] private float _mouseSensitivity = 0.004f;
        /// <summary>Clicks first capture the cursor (normal play). Off for scripted input (tests).</summary>
        [SerializeField] private bool _requireMouseCapture = true;

        [Header("Scene references")]
        [SerializeField] private Transform _ball;
        [SerializeField] private Transform _rubber;
        [SerializeField] private Transform _plate;
        [SerializeField] private LineRenderer _strikeZone;
        [SerializeField] private LineRenderer _pci;
        private LineRenderer _pciRange;
        private bool _mergingWasDisabled;
        [SerializeField] private Transform _contactMarker;
        [SerializeField] private LineRenderer _exitRay;
        [SerializeField] private LineRenderer _pitchPath;
        [SerializeField] private Camera _camera;

        private static readonly PitchInput[] Presets = PitchPresets.All;
        private InputAction _swingAction, _aimAction, _nextPresetAction, _previousPresetAction, _upLocationAction, _downLocationAction, _pitchingAction, _normalSpeedAction, _slowSpeedAction, _slowestSpeedAction, _pathsAction, _panelAction,
            _clickAction, _releaseCursorAction;
        private int _presetIndex;
        private PciTrack _pciTrack;
        private double _pitchStartRealtime = double.NaN;
        private float _pitchPlaybackSpeed = 1f; // latched at throw; speed changes apply to the next pitch
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
        /// <summary>Flight of the last batted ball (first landing), or null after a miss.</summary>
        public BattedBallResult LastBattedBall { get; private set; }
        /// <summary>The batted ball played out on the field (bounces, roll, wall, rest); what the ball shows after contact.</summary>
        public BallInPlay LastPlay { get; private set; }
        /// <summary>Fair/foul call for <see cref="LastPlay"/> (Official Baseball Rules; see <see cref="FairFoul"/>).</summary>
        public FairFoulResult? LastCall { get; private set; }
        /// <summary>The defense's response to <see cref="LastPlay"/> (TASK-005): who fields it, where, when; possession.</summary>
        public FieldingPlay LastFielding { get; private set; }
        /// <summary>When the ball in play is over (possession, or rest / out of play); NaN without one.</summary>
        public double PlayEnd => LastLive?.EndTime ?? LastPlay?.EndTime ?? double.NaN;
        /// <summary>The live play with real runners (TASK-007; bases empty in the batting loop), resolved at contact. The
        /// batting loop observes its end; it holds no rules itself.</summary>
        public LivePlay LastLive { get; private set; }
        /// <summary>The game (GameLab only; null in the HittingLab): the situation each pitch is played in, updated when a play
        /// is over.</summary>
        public GameState Game { get; private set; }
        /// <summary>The situation of the current pitch (an empty diamond in the HittingLab).</summary>
        public Situation Situation => Game?.Situation ?? new Situation(0, BaseOccupancy.Empty);
        /// <summary>The Situation Editor works only between plays (Ready, the last result applied).</summary>
        public bool EditorLocked => Game == null || _resultPending || StateAt(Clock()) != BattingState.Ready;
        private bool _resultPending;
        private PitchInfo _pitchInfo;
        private PlayerProfile _zoneBatter;
        private GameplayCameraController _cameraModes;
        /// <summary>The keyboard arrows belong to the tactical camera (the gamepad's D-pad still chooses the pitch).</summary>
        /// <summary>Is the camera behind the plate (umpire, catcher, auto) — where a catcher figure would block the view?</summary>
        public bool CameraBehindPlate => _cameraModes == null || _cameraModes.Mode == CameraMode.Umpire || _cameraModes.Mode == CameraMode.Catcher || _cameraModes.Mode == CameraMode.Auto;

        private bool CameraHasArrows(InputAction.CallbackContext c) => _cameraModes != null && _cameraModes.CapturesArrowKeys && c.control?.device is Keyboard;
        /// <summary>The selected pitch preset.</summary>
        public int PresetIndex { get => _presetIndex; set => _presetIndex = ((value % Presets.Length) + Presets.Length) % Presets.Length; }
        public static string PresetLabel(int index) => Presets[index].Label;
        /// <summary>Where the next pitch is aimed (TASK-014; null: where the preset itself aims, mid-zone for the default
        /// batter). Keys U I O / J K L / N M , and 7 8 9 0; arrows / D-pad up and down step through them.</summary>
        public PitchTarget? Target { get; set; }

        /// <summary>The automatic pitcher (P / gamepad Y): each pitch is chosen by <see cref="AutoPitcher"/> from the game, and
        /// after a result the next one is thrown <see cref="AutoPitchDelay"/> after the loop is ready (GameLab only).</summary>
        public bool AutoPitch
        {
            get => _autoPitch;
            set
            {
                if (value && !_autoPitch) _autoArmed = Clock();   // the schedule starts now, never in the past
                _autoPitch = value && _gameMode;
            }
        }

        private bool _autoPitch;
        private double _autoArmed = double.NegativeInfinity;

        /// <summary>A frame later than the auto press by more than this (s; a pause, a hitch) re-anchors the schedule at the
        /// frame instead of throwing a pitch that would already be over.</summary>
        public const double AutoHitchTolerance = 0.25;

        /// <summary>
        /// Pitch execution (TASK-018, GameLab): the pitcher's command spreads his pitches around the target. Off: the intended
        /// pitch exactly (debug). The labs without a game always throw the presets exactly.
        /// </summary>
        public bool ExecutionVariance { get; set; } = true;
        /// <summary>How the current pitch's execution differed from the intent (null without variance or a rated pitcher).</summary>
        public ExecutionError? LastExecution { get; private set; }
        /// <summary>The pitcher of the current pitch (GameLab).</summary>
        public PlayerProfile PitchPitcher { get; private set; }

        /// <summary>The command the current pitch was thrown with (the auto pitcher's, or the manual selection).</summary>
        public PitchCommand? LastCommand { get; private set; }
        /// <summary>The auto pitcher's seed (the same seed and game give the same pitches).</summary>
        public int AutoPitchSeed { get; set; } = 1;
        /// <summary>Real seconds between the loop becoming ready and the auto pitcher's next press.</summary>
        public double AutoPitchDelay { get; set; } = 0.8;
        /// <summary>The last pitch's result once it is decided (GameLab: applied to the game), and how it ended the plate
        /// appearance.</summary>
        public PitchOutcome? LastOutcome { get; private set; }
        /// <summary>
        /// The side the batter at the plate bats from at real time <paramref name="realtime"/>: the current pitch's batter
        /// until the loop is ready again, then (GameLab) the game's batter — the next one once a plate appearance is over.
        /// </summary>
        public BatterSide BatterSideAt(double realtime) => BatterAt(realtime)?.Bats ?? _swing.Side;

        /// <summary>The batter at the plate at real time <paramref name="realtime"/> (GameLab; null in the HittingLab).</summary>
        public PlayerProfile BatterAt(double realtime) =>
            Game != null && !(Game.IsOver && PitchBatter != null) && (CurrentPitch == null || StateAt(realtime) == BattingState.Ready && !_resultPending) ? Game.Batter : PitchBatter;

        /// <summary>The real time the controller last rendered (<see cref="FrameUpdate"/>).</summary>
        public double RenderedRealtime { get; private set; } = double.NegativeInfinity;

        /// <summary>
        /// A take is counted this long (real seconds) after the pitch is over, so a swing pressed before the end of the pitch
        /// but delivered by the input system a frame later still counts — the result never depends on the frame schedule.
        /// </summary>
        public const double InputGrace = 0.1;

        /// <summary>The batter the current pitch is thrown to (GameLab; null in the HittingLab).</summary>
        public PlayerProfile PitchBatter { get; private set; }
        /// <summary>The current pitch's strike zone (bottom, top; m): the batter's, or the default one.</summary>
        public (double Bottom, double Top) Zone => PitchBatter != null ? (PitchBatter.ZoneBottom, PitchBatter.ZoneTop) : (StrikeZone.Bottom, StrikeZone.Top);
        public PlateAppearanceEnd LastEnd { get; private set; }
        /// <summary>The chosen defensive action's play: the ball's authority at every instant.</summary>
        public LiveDefense LastDefense => LastLive?.Defense;
        /// <summary>Distance to show for the hit: carry (first bounce), or the projected distance off or over the fence.</summary>
        public double ShownCarry => LastPlay == null ? double.NaN : LastPlay.ReachedFenceInTheAir ? LastBattedBall.Metrics.Distance : LastPlay.CarryDistance;
        public static readonly FieldLayout Field = FieldLayout.Standard;
        public SwingInput? LastSwing { get; private set; }
        public int PitchesThrown { get; private set; }
        public SwingParameters Swing => _swing;
        public EnvironmentState Environment => EnvironmentState.Standard;
        public double SimTime => ToSimTime(Clock());
        public Transform BallTransform => _ball;
        public double DeliveryLead => _deliveryLead;
        /// <summary>Simulation time last used to render the ball (<see cref="FrameUpdate"/>).</summary>
        public double RenderedSimTime { get; private set; }
        /// <summary>One-line outcome of the last swing for the HUD (empty before a swing).</summary>
        public string ResultSummary { get; private set; } = string.Empty;

        /// <summary>Simulation time (s after release) of a moment on the real-time clock shared with input events.</summary>
        public double ToSimTime(double realtime) => double.IsNaN(_pitchStartRealtime) ? 0.0 : (realtime - _pitchStartRealtime) * _pitchPlaybackSpeed;

        /// <summary>PCI position at a moment on the real-time clock (exact, from timestamped aim input).</summary>
        public (double X, double Z) PciAt(double realtime)
        {
            (double u, double v) = _pciTrack.PositionAt(realtime);
            return Pci.ToMeters(u, v);
        }

        /// <summary>Normalized PCI (<see cref="PciFrame"/>) at a moment on the shared real-time clock.</summary>
        public (double U, double V) PciNormalizedAt(double realtime) => _pciTrack.PositionAt(realtime);

        public PciFrame PciArea => Pci;

        /// <summary>Mouse sensitivity (normalized PCI units per mouse count).</summary>
        public float MouseSensitivity { get => _mouseSensitivity; set => _mouseSensitivity = value; }

        /// <summary>Whether clicks must first capture the cursor (normal play) before they throw or swing.</summary>
        public bool RequireMouseCapture { get => _requireMouseCapture; set => _requireMouseCapture = value; }

        public bool MouseCaptured => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>Debug view (T): paths, contact marker, PCI range and the contact overlay.</summary>
        public bool DebugView { get => _showDebugPaths; set { if (_pciTrack != null) SetDebugPaths(value); } }

        private void Awake()
        {
            if (_ball == null || _rubber == null || _plate == null || _strikeZone == null || _pci == null || _contactMarker == null ||
                _exitRay == null || _pitchPath == null || _camera == null)
            {
                UnityEngine.Debug.LogError("HittingLabController is missing a scene reference; disabling.", this);
                enabled = false;
                return;
            }

            if (_gameMode) Game = new GameState();
            _pciRange = Instantiate(_pci, _pci.transform.parent);
            _pciRange.name = "PciRange";
            _pciRange.widthMultiplier = 0.5f * _pci.widthMultiplier;
            _pciRange.enabled = false;

            (double u0, double v0) = Pci.ToNormalized(0.0, 0.5 * (PitchingGeometry.DefaultZoneBottom + PitchingGeometry.DefaultZoneTop));
            _pciTrack = new PciTrack(-1.0, 1.0, -1.0, 1.0, Clock(), u0, v0);

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

            // The arrow keys choose the pitch unless the tactical camera has them (TASK-011).
            _cameraModes = _camera.GetComponent<GameplayCameraController>();
            _nextPresetAction = Button("<Keyboard>/rightArrow", "<Gamepad>/dpad/right", c => { if (!CameraHasArrows(c)) _presetIndex = (_presetIndex + 1) % Presets.Length; });
            _previousPresetAction = Button("<Keyboard>/leftArrow", "<Gamepad>/dpad/left", c => { if (!CameraHasArrows(c)) _presetIndex = (_presetIndex + Presets.Length - 1) % Presets.Length; });
            _upLocationAction = Button("<Keyboard>/upArrow", "<Gamepad>/dpad/up", c => { if (!CameraHasArrows(c)) StepTarget(1); });
            _downLocationAction = Button("<Keyboard>/downArrow", "<Gamepad>/dpad/down", c => { if (!CameraHasArrows(c)) StepTarget(-1); });
            // Pitcher controls that never touch the arrows (TASK-014): the zone grid on U I O / J K L / N M , (as the catcher
            // sees it), the four balls on 7 8 9 0 (up, down, left, right), the pitch type on Z / X (shoulders), auto on P (Y).
            _pitchingAction = new InputAction("Pitching", InputActionType.Button);
            foreach (string key in PitchingKeys) _pitchingAction.AddBinding("<Keyboard>/" + key);
            _pitchingAction.AddBinding("<Gamepad>/leftShoulder");
            _pitchingAction.AddBinding("<Gamepad>/rightShoulder");
            _pitchingAction.AddBinding("<Gamepad>/buttonNorth");
            _pitchingAction.performed += c => OnPitchingKey(c.control.name);
            _normalSpeedAction = Button("<Keyboard>/1", null, _ => _playbackSpeed = 1f);
            _slowSpeedAction = Button("<Keyboard>/2", null, _ => _playbackSpeed = 0.5f);
            _slowestSpeedAction = Button("<Keyboard>/3", null, _ => _playbackSpeed = 0.25f);   // motion inspection (TASK-011.8)
            _pathsAction = Button("<Keyboard>/t", null, _ => SetDebugPaths(!_showDebugPaths));
            _panelAction = Button("<Keyboard>/h", null, _ => _showPanel = !_showPanel);
            _clickAction = Button("<Mouse>/leftButton", null, context =>
            {
                if (!MouseCaptured && Mouse.current != null && ClickBlocked != null && ClickBlocked(Mouse.current.position.ReadValue())) return;
                if (_requireMouseCapture && !MouseCaptured)
                {
                    // The capturing click never throws; but during a live pitch (the lock was lost mid at-bat) it still swings.
                    CaptureMouse();
                    if (!PitchLive(context.time)) return;
                }

                PressSwingButton(context.time);
            });
            _releaseCursorAction = Button("<Keyboard>/escape", null, _ => ReleaseMouse());
        }

        private static void CaptureMouse()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void ReleaseMouse()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// Mouse aim: every mouse state event's delta moves the PCI at that event's timestamp (exact, frame-independent,
        /// like the stick's timestamped velocity). Read per event, not per frame, so frames never decide the path.
        /// </summary>
        private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!(device is Mouse mouse) || _pciTrack == null) return;
            if (_requireMouseCapture && !MouseCaptured) return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;
            if (!mouse.delta.ReadValueFromEvent(eventPtr, out Vector2 delta) || delta == Vector2.zero) return;
            _pciTrack.Move(eventPtr.time, delta.x * _mouseSensitivity, delta.y * _mouseSensitivity);
        }

        /// <summary>Keyboard keys of the pitcher controls: the nine zone spots (PitchTarget order), then the four balls.</summary>
        private static readonly string[] PitchingKeys = { "u", "i", "o", "j", "k", "l", "n", "m", "comma", "7", "8", "9", "0", "z", "x", "p" };

        /// <summary>A pitcher control (key or gamepad control name).</summary>
        public void OnPitchingKey(string control)
        {
            int target = Array.IndexOf(PitchingKeys, control);
            if (target >= 0 && target < PitchTargets.All.Length) Target = PitchTargets.All[target];
            else if (control == "z" || control == "leftShoulder") StepPitchType(-1);
            else if (control == "x" || control == "rightShoulder") StepPitchType(1);
            else if (control == "p" || control == "buttonNorth") AutoPitch = !AutoPitch;
        }

        /// <summary>The pitch type keys: through the pitcher's repertoire in the GameLab, through every preset otherwise.</summary>
        private void StepPitchType(int step)
        {
            Repertoire repertoire = Game?.Pitcher?.Repertoire;
            if (repertoire == null) PresetIndex = _presetIndex + step;
            else _presetIndex = (int)repertoire.Step((PitchType)_presetIndex, step);
        }

        /// <summary>Arrows / D-pad up and down: through the targets (from the preset's own aim, then each target in order).</summary>
        private void StepTarget(int step)
        {
            int n = PitchTargets.All.Length + 1, i = Target.HasValue ? (int)Target.Value + 1 : 0;
            i = ((i + step) % n + n) % n;
            Target = i == 0 ? (PitchTarget?)null : (PitchTarget)(i - 1);
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
            _upLocationAction?.Enable();
            _pitchingAction?.Enable();
            _downLocationAction?.Enable();
            _normalSpeedAction?.Enable();
            _slowSpeedAction?.Enable();
            _slowestSpeedAction?.Enable();
            _pathsAction?.Enable();
            _panelAction?.Enable();
            _clickAction?.Enable();
            _releaseCursorAction?.Enable();
            if (_pciTrack == null) return;
            InputSystem.onEvent += OnInputEvent;
            // The Input System merges consecutive mouse events within an update and keeps the later timestamp; with it
            // on, a click took the next mouse report's time (3 ms late in HittingInputFrameRateTests) and aim deltas
            // shift in time. Timing and aim are read per event here, so every event must keep its own timestamp.
            // Global setting: restored in OnDisable.
            _mergingWasDisabled = InputSystem.settings.disableRedundantEventsMerging;
            InputSystem.settings.disableRedundantEventsMerging = true;
        }

        private void OnDisable()
        {
            _swingAction?.Disable();
            _aimAction?.Disable();
            _nextPresetAction?.Disable();
            _previousPresetAction?.Disable();
            _upLocationAction?.Disable();
            _pitchingAction?.Disable();
            _downLocationAction?.Disable();
            _normalSpeedAction?.Disable();
            _slowSpeedAction?.Disable();
            _slowestSpeedAction?.Disable();
            _pathsAction?.Disable();
            _panelAction?.Disable();
            _clickAction?.Disable();
            _releaseCursorAction?.Disable();
            InputSystem.onEvent -= OnInputEvent;
            if (_pciTrack != null) InputSystem.settings.disableRedundantEventsMerging = _mergingWasDisabled;
            ReleaseMouse();
        }

        private void OnDestroy()
        {
            _swingAction?.Dispose();
            _aimAction?.Dispose();
            _nextPresetAction?.Dispose();
            _previousPresetAction?.Dispose();
            _upLocationAction?.Dispose();
            _pitchingAction?.Dispose();
            _downLocationAction?.Dispose();
            _normalSpeedAction?.Dispose();
            _slowSpeedAction?.Dispose();
            _slowestSpeedAction?.Dispose();
            _pathsAction?.Dispose();
            _panelAction?.Dispose();
            _clickAction?.Dispose();
            _releaseCursorAction?.Dispose();
        }

        private void Start()
        {
            PlaceField();   // the camera is placed by the scene's presentation (BaseballCamera)
            ShowIdle();
        }

        /// <summary>Simulates the selected pitch and starts its playback now.</summary>
        public void ThrowPitch(int presetIndex) => ThrowPitch(presetIndex, Clock());

        /// <summary>The next pitch of the plate appearance (the selected preset), now.</summary>
        public void StartPlateAppearance() => ThrowPitch(_presetIndex, Clock() + _deliveryLead / _playbackSpeed);

        /// <summary>Screen points (Input System coordinates, origin bottom-left) where a click belongs to an on-screen panel and
        /// must not capture the mouse or throw/swing (the GameLab's Situation Editor).</summary>
        public Func<Vector2, bool> ClickBlocked { get; set; }

        /// <summary>
        /// GameLab: starts <paramref name="game"/> (a new standard game when null). The pitch and play on screen are dropped
        /// unapplied — they belonged to the old game — and the loop waits for the first pitch.
        /// </summary>
        public void NewGame(GameState game = null)
        {
            if (!_gameMode) throw new InvalidOperationException("Only the GameLab plays a game.");
            Game = game ?? new GameState();
            _resultPending = false;
            _autoArmed = Clock();   // auto pitching (if on) resumes AutoPitchDelay from now
            ClearPitch();
            CurrentPitch = null;
            PitchBatter = null;
            _pitchStartRealtime = double.NaN;
            _zoneBatter = null;
            DrawZone(Zone);
            ShowIdle();
        }

        /// <summary>Simulates the selected pitch; it is released at <paramref name="releaseRealtime"/> on the shared clock.</summary>
        public void ThrowPitch(int presetIndex, double releaseRealtime)
        {
            ApplyResult();   // a press during a play skips its remainder: its result stands
            if (Game != null && Game.IsOver) return;   // the game is over: no more pitches (NewGame starts the next)
            _presetIndex = ((presetIndex % Presets.Length) + Presets.Length) % Presets.Length;
            // The game's batter (GameLab): his side for the swing, his zone for the call — fixed for this pitch.
            PitchBatter = Game?.Batter;
            if (PitchBatter != null) _swing.Side = PitchBatter.Bats;
            DrawZone(Zone);
            // The auto pitcher's choice is this pitch's only — the manual selection stays as the player left it.
            int preset = _presetIndex;
            PitchTarget? target = Target;
            if (AutoPitch && Game != null)
            {
                PitchCommand auto = AutoPitcher.Choose(AutoPitchSeed, Game.Current.Number, Game.Current.Pitches.Count + 1, Game.Count, Game.Pitcher?.Repertoire);
                (preset, target) = (auto.Preset, auto.Target);
            }

            LastExecution = null;
            PitchPitcher = Game?.Pitcher;
            if (Game != null) preset = (int)GamePitches.Available(Game, (PitchType)preset);   // only from his repertoire
            LastCommand = target is PitchTarget aimed ? new PitchCommand(preset, aimed) : (PitchCommand?)null;
            if (Game != null)
            {
                // The pitcher on the mound throws it: his pitch, aimed, executed (TASK-018).
                CurrentPitch = GamePitches.Create(Game, (PitchType)preset, target, ExecutionVariance, Environment, out _pitchInfo, out ExecutionError? execution);
                LastExecution = execution;
            }
            else
            {
                (double bottom, double top) = Zone;
                CurrentPitch = target is PitchTarget at
                    ? PitchTargets.Create(new PitchCommand(preset, at), bottom, top, Environment)
                    : HittingPitch.Create(Presets[preset], Environment);
                _pitchInfo = PitchInfo.Of(Presets[preset].Label, CurrentPitch);   // as thrown (the selection may change)
            }
            _pitchStartRealtime = releaseRealtime;
            _pitchPlaybackSpeed = _playbackSpeed;
            ClearPitch();
            _resultPending = Game != null;   // every pitch has a result for the game: a take, a miss or a play
            PitchesThrown++;
            _pitchLabel = $"{Presets[preset].Label} ({(target is PitchTarget named ? PitchTargets.Name(named) : "preset aim")})";
            _readout = $"{_pitchLabel}: swing (Space / A)!";
        }

        /// <summary>Forgets the last pitch's swing, contact, play and result (not the pitch itself).</summary>
        private void ClearPitch()
        {
            LastResult = null;
            LastSwing = null;
            LastBattedBall = null;
            LastPlay = null;
            LastCall = null;
            LastFielding = null;
            LastLive = null;
            LastOutcome = null;
            LastEnd = PlateAppearanceEnd.None;
            ResultSummary = string.Empty;
            _pitchPath.enabled = false;
            _exitRay.enabled = false;
            _contactMarker.gameObject.SetActive(false);
        }

        /// <summary>Places the PCI (contact-plane metres) at the current clock time, keeping any aim motion (debug/test entry point).</summary>
        public void SetPci(double x, double z)
        {
            (double u, double v) = Pci.ToNormalized(x, z);
            _pciTrack.Place(Clock(), u, v);
        }

        /// <summary>
        /// Swing that started at simulation time <paramref name="startTime"/> (s after release), with the PCI where it was
        /// at that moment.
        /// </summary>
        public ContactResult SwingAtSimTime(double startTime)
        {
            (double x, double z) = PciAt(_pitchStartRealtime + startTime / _pitchPlaybackSpeed);
            var swing = new SwingInput(startTime, x, z);
            ContactResult result = ContactResolver.Resolve(CurrentPitch, swing, _swing);
            LastSwing = swing;
            LastResult = result;
            ShowResult(result);
            return result;
        }

        /// <summary>The final stays up at least this long (real s) before a press starts a new game (no mashing through it).</summary>
        public const double FinalDwell = 1.0;

        /// <summary>How long (real s) the loop has been ready since the game's last pitch — the final shown (∞ with no pitch).</summary>
        private double ShowingFinalSince(double realtime) => CurrentPitch == null ? double.PositiveInfinity
            : realtime - (_pitchStartRealtime + (BattingStateMachine.OutcomeTime(CurrentPitch, LastSwing, LastResult, PlayEnd, _swing.SwingDuration) + BattingStateMachine.ResultPause) / _pitchPlaybackSpeed);

        /// <summary>Presses this soon after contact are ignored (s): double clicks, switch bounce.</summary>
        public const double DoublePressGrace = 0.3;

        /// <summary>Batting loop state at real time <paramref name="realtime"/> (<see cref="BattingStateMachine"/>).</summary>
        public BattingState StateAt(double realtime) =>
            CurrentPitch == null ? BattingState.Ready : BattingStateMachine.At(ToSimTime(realtime), CurrentPitch, LastSwing, LastResult, PlayEnd, _swing.SwingDuration);

        private bool PitchLive(double realtime) => StateAt(realtime) == BattingState.PitchInFlight && !LastSwing.HasValue;

        /// <summary>
        /// The swing button at real time <paramref name="eventRealtime"/> (the input event's timestamp), by batting state:
        /// Ready, Result or BallInPlay → throw the next pitch (released <see cref="DeliveryLead"/> later; a press during a
        /// play skips its remainder); Windup or Swinging → ignored (no swing before release, no double swing);
        /// PitchInFlight → swing at exactly that moment with the PCI where it was then, however many frames have passed.
        /// </summary>
        public void PressSwingButton(double eventRealtime)
        {
            switch (StateAt(eventRealtime))
            {
                case BattingState.PitchInFlight:
                    // One swing per pitch: a second press stamped earlier than the first (another device's event handled
                    // later in the same update) reads as "before the swing" but must not swing again.
                    // A press stamped before the end of the pitch but handled after its take was counted is too late.
                    if (!LastSwing.HasValue && (Game == null || _resultPending)) SwingAtSimTime(ToSimTime(eventRealtime));
                    return;
                case BattingState.Windup:
                case BattingState.Swinging:
                    return;
                case BattingState.BallInPlay when ToSimTime(eventRealtime) < LastResult.Value.BattedBall.Time + DoublePressGrace:
                    return;   // a double click or switch bounce just after a hit must not throw the hit away
                default:
                    // After the final (and its call), a press starts a new game; until then it throws (or, when the game has
                    // just ended, does nothing).
                    if (Game != null && Game.IsOver && !_resultPending && StateAt(eventRealtime) == BattingState.Ready && ShowingFinalSince(eventRealtime) >= FinalDwell)
                    {
                        NewGame();
                        return;
                    }

                    ThrowPitch(_presetIndex, eventRealtime + _deliveryLead / _playbackSpeed);
                    return;
            }
        }

        // Known limit: the Input System merges consecutive DualSense stick reports within one update (IEventMerger),
        // so with that pad the sampled stick path can depend on how reports group into frames (≈ mm). Keyboard and
        // other gamepads deliver every report. Button presses (swing) are never merged.
        private void OnAim(InputAction.CallbackContext context)
        {
            Vector2 aim = context.ReadValue<Vector2>();
            _pciTrack.SetVelocity(context.time, aim.x * PciSpeed / Pci.HalfWidth, aim.y * PciSpeed / Pci.HalfHeight);
        }

        private void Update() => FrameUpdate(Clock());

        /// <summary>Renders the state at real time <paramref name="now"/>. Nothing authoritative happens here.</summary>
        public void FrameUpdate(double now)
        {
            if (_pciTrack == null) return; // disabled in Awake (missing scene reference)
            DrawPci(now);
            RenderedRealtime = now;
            PlayerProfile atBat = BatterAt(now);
            if (atBat != null && !ReferenceEquals(atBat, _zoneBatter))
            {
                _zoneBatter = atBat;
                DrawZone((atBat.ZoneBottom, atBat.ZoneTop));   // the outline follows the batter who steps in
            }
            // The result of the pitch on screen, once decided (a take waits InputGrace for a late swing event).
            if (CurrentPitch != null && _resultPending)
            {
                double grace = LastSwing.HasValue ? 0.0 : InputGrace * _pitchPlaybackSpeed;
                if (ToSimTime(now) >= BattingStateMachine.OutcomeTime(CurrentPitch, LastSwing, LastResult, PlayEnd, _swing.SwingDuration) + grace) ApplyResult();
            }

            // The auto pitcher's next press: AutoPitchDelay after the loop is ready (or after auto was switched on, or a new game
            // began), at that exact time — not this frame's — unless the frame is far later (a pause): then now. Before
            // rendering, so the new pitch is drawn at its own time.
            if (AutoPitch && Game != null && !Game.IsOver && !_resultPending)
            {
                double ready = CurrentPitch == null ? double.NegativeInfinity
                    : _pitchStartRealtime + (BattingStateMachine.OutcomeTime(CurrentPitch, LastSwing, LastResult, PlayEnd, _swing.SwingDuration) + BattingStateMachine.ResultPause) / _pitchPlaybackSpeed;
                double press = Math.Max(ready, _autoArmed) + AutoPitchDelay;
                if (now - press > AutoHitchTolerance) press = now;
                if (now >= press) ThrowPitch(_presetIndex, press + _deliveryLead / _playbackSpeed);
            }

            if (CurrentPitch == null) return;
            double t = ToSimTime(now);
            RenderedSimTime = t;
            // Authoritative samples only: the pitch, then (after contact) the ball in play until it rests or leaves play.
            // After contact: free on its trajectory until a defender possesses it, then carried (FieldingPlay).
            _ball.position = SimulationSpace.ToUnity(LastDefense != null && t >= LastPlay.First.Time
                ? LastDefense.BallPositionAt(t)
                : CurrentPitch.Flight.StateAt(t).Position);
        }

        /// <summary>The pitch's result into the game (once; when it is decided — the end of the pitch or the swing, the play's
        /// end — or when the next pitch is thrown first).</summary>
        private void ApplyResult()
        {
            if (!_resultPending) return;
            _resultPending = false;
            (double bottom, double top) = Zone;
            PitchOutcome outcome = PitchOutcomes.Of(CurrentPitch, LastSwing, LastResult, LastLive, bottom, top);
            LastOutcome = outcome;
            LastEnd = outcome == PitchOutcome.Foul || outcome == PitchOutcome.InPlay ? Game.Apply(LastLive, _pitchInfo) : Game.Pitch(outcome, _pitchInfo);
        }

        private void ShowResult(ContactResult r)
        {
            double inches(double m) => Units.MetersToInches(m);
            string timing = double.IsNaN(r.TimingError) ? "-" : $"{r.TimingError * 1000.0:+0;-0} ms ({r.Timing})";
            if (r.IsContact)
            {
                LastBattedBall = BattedBallSimulation.Run(r.BattedBall, Environment);
                LastPlay = BallInPlaySimulation.Run(r.BattedBall, Environment, Field);
                LastCall = FairFoul.Call(LastPlay);
                Situation situation = Situation;
                // The players on the field (GameLab: the game's rosters; HittingLab: the generic profiles).
                PlayPersonnel personnel = Game?.Personnel ?? PlayPersonnel.Standard;
                LastFielding = FieldingSolver.Solve(LastPlay, situation.Alignment, personnel.Fielder, Field);
                LastLive = new LivePlay(LastFielding, situation, personnel: personnel);
                LastLive.RunToEnd();
                BattedBallMetrics flight = LastBattedBall.Metrics;
                // Carry is the first ground contact; off or over the fence, the projected (airborne-only) distance, as Statcast.
                double carry = LastPlay.ReachedFenceInTheAir ? flight.Distance : LastPlay.CarryDistance;
                BattedBallLaunch launch = BattedBallLaunch.FromState(r.BattedBall);
                _readout =
                    $"{_pitchLabel}  timing {timing}\n" +
                    $"PCI offset: barrel {inches(r.OffsetAlongBarrel):+0.0;-0.0} in, vertical {inches(r.VerticalOffset):+0.0;-0.0} in (+ = under ball)\n" +
                    $"Exit velocity {Units.MetersPerSecondToMph(r.ExitSpeed):0.0} mph   launch {r.LaunchAngleDegrees:+0;-0}°   spray {r.SprayAngleDegrees:+0;-0}° (+ = RF)\n" +
                    $"Spin {Units.RadiansPerSecondToRpm(r.BattedBall.Spin.Length):0} rpm (back {launch.BackspinRpm:0}, side {launch.SidespinRpm:+0;-0}, + = curves to RF)   q {r.CollisionEfficiency:0.00}" +
                    $"   {LastCall.Value.Call} ({LastCall.Value.Basis})" +
                    $"\nFlight: carry {Units.MetersToFeet(carry):0} ft, hang {flight.HangTime:0.00} s, apex {Units.MetersToFeet(flight.ApexHeight):0} ft" +
                    (LastPlay.ClearedFence ? "   over the fence" : $", final {Units.MetersToFeet(LastPlay.FinalDistance):0} ft at {LastPlay.EndTime - LastPlay.First.Time:0.0} s");
                Vector3 contact = SimulationSpace.ToUnity(r.BattedBall.Position);
                _contactMarker.position = contact;
                _contactMarker.gameObject.SetActive(_showDebugPaths);
                SetPath(_exitRay, LastBattedBall.Flight);   // debug: the airborne flight (ground play is shown by the ball)
                _exitRay.enabled = _showDebugPaths;
                ResultSummary = $"{Units.MetersPerSecondToMph(r.ExitSpeed):0} mph · {r.LaunchAngleDegrees:0}° · {Units.MetersToFeet(carry):0} ft";
            }
            else
            {
                ResultSummary = r.Outcome == ContactOutcome.MissTiming ? (r.TimingError < 0 ? "Swing and miss — early" : "Swing and miss — late") : "Swing and miss";
                _readout = $"{_pitchLabel}  timing {timing}\nMISS: {r.Outcome}" +
                           (double.IsNaN(r.VerticalOffset) ? "" : $" (barrel {inches(r.OffsetAlongBarrel):+0.0;-0.0} in, vertical {inches(r.VerticalOffset):+0.0;-0.0} in)");
            }

            SetPath(_pitchPath, CurrentPitch.Flight);
            _pitchPath.enabled = _showDebugPaths;
        }

        private void SetDebugPaths(bool show)
        {
            _showDebugPaths = show;
            bool swung = LastResult.HasValue;
            _pitchPath.enabled = show && swung;
            _exitRay.enabled = show && LastBattedBall != null;
            _contactMarker.gameObject.SetActive(show && LastBattedBall != null);
        }

        private void ShowIdle() => _readout = $"Next: {Presets[_presetIndex].Label} ({TargetName}). Press Space / A to throw.";

        private string TargetName => Target is PitchTarget t ? PitchTargets.Name(t) : "preset aim";

        private void DrawPci(double now)
        {
            // PCI drawn at the contact plane: width = barrel contact range, height = bat–ball centre distance, with a
            // centre cross (one polyline; retraced edges are invisible): the aim point the swing resolves against.
            (double x, double z) = PciAt(now);
            double hx = _swing.BarrelHalfLength;
            double hz = BallProperties.Baseball.Radius + _swing.BarrelRadius;
            double y = CurrentPitch?.ContactPlaneY ?? HittingPitch.DefaultContactPlaneY;
            Vector3 P(double dx, double dz) => SimulationSpace.ToUnity(new Vector3d(x + dx * hx, y, z + dz * hz));
            _pci.loop = false;
            _pci.positionCount = PciOutline.Length;
            for (int i = 0; i < PciOutline.Length; i++) _pci.SetPosition(i, P(PciOutline[i].x, PciOutline[i].y));

            // Debug (T): the area the PCI can move in.
            _pciRange.enabled = _showDebugPaths;
            if (!_showDebugPaths) return;
            (double x0, double z0) = Pci.ToMeters(-1.0, -1.0);
            (double x1, double z1) = Pci.ToMeters(1.0, 1.0);
            _pciRange.loop = true;
            _pciRange.positionCount = 4;
            _pciRange.SetPosition(0, SimulationSpace.ToUnity(new Vector3d(x0, y, z0)));
            _pciRange.SetPosition(1, SimulationSpace.ToUnity(new Vector3d(x1, y, z0)));
            _pciRange.SetPosition(2, SimulationSpace.ToUnity(new Vector3d(x1, y, z1)));
            _pciRange.SetPosition(3, SimulationSpace.ToUnity(new Vector3d(x0, y, z1)));
        }

        // Box corners and centre cross in half-extent units: ML TL TR BR BL ML MR BR BM TM.
        private static readonly Vector2[] PciOutline =
        {
            new Vector2(-1, 0), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, -1),
            new Vector2(-1, 0), new Vector2(1, 0), new Vector2(1, -1), new Vector2(0, -1), new Vector2(0, 1),
        };

        private void SetPath(LineRenderer line, TrajectoryResult trajectory)
        {
            int count = trajectory.Samples.Count;
            if (_pathBuffer.Length < count) _pathBuffer = new Vector3[count];
            for (int i = 0; i < count; i++) _pathBuffer[i] = SimulationSpace.ToUnity(trajectory.Samples[i].Position);
            line.positionCount = count;
            line.SetPositions(_pathBuffer);
        }

        /// <summary>The strike-zone outline at the front of the plate (the batter's zone; presentation of the rule's input).</summary>
        private void DrawZone((double Bottom, double Top) zone)
        {
            double x = PitchingGeometry.PlateHalfWidth, y = PitchingGeometry.PlateFrontY;
            _strikeZone.SetPosition(0, SimulationSpace.ToUnity(new Vector3d(-x, y, zone.Bottom)));
            _strikeZone.SetPosition(1, SimulationSpace.ToUnity(new Vector3d(x, y, zone.Bottom)));
            _strikeZone.SetPosition(2, SimulationSpace.ToUnity(new Vector3d(x, y, zone.Top)));
            _strikeZone.SetPosition(3, SimulationSpace.ToUnity(new Vector3d(-x, y, zone.Top)));
        }

        private void PlaceField()
        {
            double rubberDepth = Units.InchesToMeters(6.0);
            // The rubber sits on top of the 10 in mound drawn by the presentation (FieldDressing); the simulation frame is unchanged.
            _rubber.position = SimulationSpace.ToUnity(new Vector3d(0.0, PitchingGeometry.RubberFrontY + rubberDepth / 2.0, Pitchlab.Presentation.FieldDressing.MoundTop));
            _rubber.localScale = new Vector3((float)Units.InchesToMeters(24.0), 0.02f, (float)rubberDepth);
            _plate.position = SimulationSpace.ToUnity(new Vector3d(0.0, PitchingGeometry.PlateFrontY / 2.0, 0.0));
            _plate.localScale = new Vector3((float)(2.0 * PitchingGeometry.PlateHalfWidth), 0.01f, (float)PitchingGeometry.PlateFrontY);

            double x = PitchingGeometry.PlateHalfWidth;
            double y = PitchingGeometry.PlateFrontY;
            _strikeZone.loop = true;
            _strikeZone.positionCount = 4;
            DrawZone(Zone);

            _ball.position = SimulationSpace.ToUnity(Presets[0].ToInitialState().Position);
        }

        /// <summary>Contact debug overlay: the one PCI conversion chain and the resolved contact.</summary>
        private string DebugOverlay()
        {
            double now = Clock();
            (double u, double v) = PciNormalizedAt(now);
            (double x, double z) = PciAt(now);
            string text = $"PCI now: normalized ({u:+0.000;-0.000}, {v:+0.000;-0.000}) → contact plane ({x:+0.000;-0.000}, {z:0.000}) m";
            if (LastSwing is SwingInput swing && LastResult is ContactResult r && CurrentPitch != null)
            {
                (double su, double sv) = Pci.ToNormalized(swing.PciX, swing.PciZ);
                var ball = CurrentPitch.Flight.StateAt(swing.StartTime + _swing.SwingDuration).Position;
                text += $"\nSwing @ {swing.StartTime:0.0000} s: PCI ({su:+0.000;-0.000}, {sv:+0.000;-0.000}) = ({swing.PciX:+0.000;-0.000}, {swing.PciZ:0.000}) m" +
                        $"\nBall at contact time ({ball.X:+0.000;-0.000}, {ball.Y:0.000}, {ball.Z:0.000}) m   timing {(double.IsNaN(r.TimingError) ? "-" : $"{r.TimingError * 1000.0:+0.0;-0.0} ms")}" +
                        (r.IsContact ? $"\nContact point ({r.BattedBall.Position.X:+0.000;-0.000}, {r.BattedBall.Position.Y:0.000}, {r.BattedBall.Position.Z:0.000}) m" : $"\nMiss: {r.Outcome}");
            }

            return text;
        }

        private void OnGUI()
        {
            if (!_showPanel)
            {
                GUI.Label(new Rect(10, 6, 520, 22), (_requireMouseCapture && !MouseCaptured ? "Click to capture the mouse · " : "Mouse aims · click throws/swings · Esc releases · ") +
                                                    "H details · T debug");
                if (_showDebugPaths) GUI.Label(new Rect(10, 30, 560, 90), DebugOverlay(), GUI.skin.box);
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, 440, _showDebugPaths ? 290 : 175), GUI.skin.box);
            GUILayout.Label($"Pitch: {Presets[_presetIndex].Label} · {TargetName}{(AutoPitch ? " · AUTO" : "")}   speed {_playbackSpeed:0.0}×   #{PitchesThrown}");
            GUILayout.Label(_readout);
            GUILayout.Label((_requireMouseCapture && !MouseCaptured ? "Click to capture the mouse.  " : "Mouse PCI · click throw/swing · Esc release.  ") +
                            "Space/A throw·swing  WASD/stick PCI  Z/X pitch  UIO/JKL/NM, zone  7890 ball  P auto  1/2/3 speed  T debug  H panel");
            if (_showDebugPaths) GUILayout.Label(DebugOverlay());
            GUILayout.EndArea();
        }
    }
}
