using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Presentation
{
    public enum CameraMode
    {
        Umpire,
        Catcher,
        Offset,
        Tactical,
        Auto,
    }

    /// <summary>
    /// Gameplay camera modes (TASK-011): F1 umpire, F2 catcher, F3 offset (third-base side), F4 tactical 3D, F5 auto (the
    /// <see cref="BaseballCamera"/>: batting view, then the ball-follow view); Tab cycles. The fixed views keep their position
    /// and turn to keep the ball in frame while it is in play. Tactical: ←/→ orbit, ↑/↓ pitch, PageUp/PageDown or [ / ] zoom,
    /// R reset — only in tactical mode. Presentation only: it moves this camera and nothing else (no effect on gameplay, the
    /// PCI or timing). The mode stays across plate appearances.
    /// </summary>
    [DefaultExecutionOrder(10000)]   // after everything that places the camera this frame
    [RequireComponent(typeof(Camera), typeof(BaseballCamera))]
    public sealed class GameplayCameraController : MonoBehaviour
    {
        [SerializeField] private CameraMode _mode = CameraMode.Auto;

        /// <summary>A fixed viewpoint: where it stands, what it looks at between pitches, its field of view.</summary>
        private readonly struct View
        {
            public View(Vector3 position, Vector3 lookAt, float fov)
            {
                Position = position;
                LookAt = lookAt;
                Fov = fov;
            }

            public Vector3 Position { get; }
            public Vector3 LookAt { get; }
            public float Fov { get; }
        }

        // Unity frame: x toward first base, y up, z toward centre field; the plate's rear point at the origin.
        private static readonly View Umpire = new View(new Vector3(0f, 1.75f, -2.3f), new Vector3(0f, 1.0f, 12f), 42f);
        private static readonly View Catcher = new View(new Vector3(0f, 0.95f, -1.1f), new Vector3(0f, 1.2f, 12f), 50f);
        private static readonly View Offset = new View(new Vector3(-3.0f, 2.1f, -5.4f), new Vector3(0.8f, 1.0f, 13f), 34f);

        public static readonly Vector3 TacticalCentre = new Vector3(0f, 0f, 32f);
        public const float TacticalYaw = 0f, TacticalPitch = 38f, TacticalDistance = 95f, TacticalFov = 45f;
        public const float MinPitch = 8f, MaxPitch = 85f, MinDistance = 25f, MaxDistance = 220f;
        /// <summary>Orbit and pitch speed (°/s) and zoom rate (1/s, exponential) of the tactical controls.</summary>
        public const float OrbitSpeed = 70f, PitchSpeed = 45f, ZoomRate = 1.2f;

        private Camera _camera;
        private BaseballCamera _auto;
        private float _yaw = TacticalYaw, _pitch = TacticalPitch, _distance = TacticalDistance;
        private Quaternion _fixedRotation;
        private bool _hasFixedRotation;

        public CameraMode Mode => _mode;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public float Distance => _distance;
        /// <summary>The arrow keys belong to the camera (tactical mode); other users of them must ignore them.</summary>
        public bool CapturesArrowKeys => _mode == CameraMode.Tactical;
        /// <summary>The short label shown in a corner.</summary>
        public string Label => $"CAM {_mode}  ·  F1–F5 / Tab{(_mode == CameraMode.Tactical ? "  ·  arrows orbit, [ ] zoom, R reset" : "")}";

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _auto = GetComponent<BaseballCamera>();
            SetMode(_mode);
        }

        public void SetMode(CameraMode mode)
        {
            _mode = mode;
            _hasFixedRotation = false;
            if (_auto == null) return;
            _auto.enabled = mode == CameraMode.Auto;
            // Back to auto between pitches: its batting view (while following it recomputes the view itself).
            if (mode == CameraMode.Auto && !_auto.IsFollowing) _auto.ShowBatting();
        }

        public void Cycle() => SetMode((CameraMode)(((int)_mode + 1) % 5));

        /// <summary>Tactical orbit by <paramref name="yaw"/>° and <paramref name="pitch"/>° (ignored in other modes).</summary>
        public void Orbit(float yaw, float pitch)
        {
            if (_mode != CameraMode.Tactical) return;
            _yaw = Mathf.Repeat(_yaw + yaw + 180f, 360f) - 180f;
            _pitch = Mathf.Clamp(_pitch + pitch, MinPitch, MaxPitch);
        }

        /// <summary>Tactical zoom: the distance times <paramref name="factor"/> (ignored in other modes).</summary>
        public void Zoom(float factor)
        {
            if (_mode != CameraMode.Tactical) return;
            _distance = Mathf.Clamp(_distance * factor, MinDistance, MaxDistance);
        }

        public void ResetTactical()
        {
            if (_mode != CameraMode.Tactical) return;
            (_yaw, _pitch, _distance) = (TacticalYaw, TacticalPitch, TacticalDistance);
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k == null) return;
            if (k.f1Key.wasPressedThisFrame) SetMode(CameraMode.Umpire);
            if (k.f2Key.wasPressedThisFrame) SetMode(CameraMode.Catcher);
            if (k.f3Key.wasPressedThisFrame) SetMode(CameraMode.Offset);
            if (k.f4Key.wasPressedThisFrame) SetMode(CameraMode.Tactical);
            if (k.f5Key.wasPressedThisFrame) SetMode(CameraMode.Auto);
            if (k.tabKey.wasPressedThisFrame) Cycle();
            if (_mode != CameraMode.Tactical) return;
            float dt = Time.unscaledDeltaTime;
            Orbit(((k.rightArrowKey.isPressed ? 1f : 0f) - (k.leftArrowKey.isPressed ? 1f : 0f)) * OrbitSpeed * dt,
                ((k.upArrowKey.isPressed ? 1f : 0f) - (k.downArrowKey.isPressed ? 1f : 0f)) * PitchSpeed * dt);
            float zoomIn = (k.pageUpKey.isPressed || k.leftBracketKey.isPressed ? 1f : 0f) - (k.pageDownKey.isPressed || k.rightBracketKey.isPressed ? 1f : 0f);
            if (zoomIn != 0f) Zoom(Mathf.Exp(-zoomIn * ZoomRate * dt));
            if (k.rKey.wasPressedThisFrame) ResetTactical();
        }

        private void LateUpdate()
        {
            switch (_mode)
            {
                case CameraMode.Auto:
                    return;   // BaseballCamera drives
                case CameraMode.Tactical:
                {
                    Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
                    Vector3 position = TacticalCentre + orbit * new Vector3(0f, 0f, -_distance);
                    transform.SetPositionAndRotation(position, Quaternion.LookRotation(TacticalCentre - position));
                    _camera.fieldOfView = TacticalFov;
                    return;
                }
                default:
                {
                    View v = _mode == CameraMode.Umpire ? Umpire : _mode == CameraMode.Catcher ? Catcher : Offset;
                    // Between pitches the view's own framing; while the ball is in play it turns (smoothly) to keep it in frame.
                    Transform ball = _auto != null ? _auto.Target : null;
                    Vector3 look = ball != null ? ball.position : v.LookAt;
                    Quaternion want = Quaternion.LookRotation(look - v.Position);
                    _fixedRotation = _hasFixedRotation ? Quaternion.Slerp(_fixedRotation, want, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime)) : want;
                    _hasFixedRotation = true;
                    if (ball == null) _fixedRotation = want;
                    transform.SetPositionAndRotation(v.Position, _fixedRotation);
                    _camera.fieldOfView = v.Fov;
                    return;
                }
            }
        }

        private GUIStyle _labelStyle;

        private void OnGUI()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.LowerRight };
                _labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            }

            GUI.Label(new Rect(Screen.width - 420, Screen.height - 24, 410, 20), Label, _labelStyle);
        }
    }
}
