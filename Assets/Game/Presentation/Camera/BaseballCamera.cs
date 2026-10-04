using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// First-pass baseball camera: a fixed batting view, and after contact a short hold followed by a ball-follow view
    /// from high behind home plate that zooms to keep a field-sized window around the ball. Reads presentation
    /// transforms only; timing comes from whoever calls <see cref="Follow"/> / <see cref="ShowBatting"/>.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BaseballCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 _battingPosition = new Vector3(0f, 2.3f, -5.6f);
        [SerializeField] private Vector3 _battingLookAt = new Vector3(0f, 0.9f, 12f);
        [SerializeField] private float _battingFov = 30f;
        [SerializeField] private Vector3 _followPosition = new Vector3(0f, 12f, -16f);
        [SerializeField] private float _holdSeconds = 0.3f, _blendSeconds = 0.8f;
        /// <summary>Width of field (m) kept in view around the ball while following.</summary>
        [SerializeField] private float _followWindow = 70f;

        private Camera _camera;
        private Transform _target;
        private float _followSince = -1f, _shakeUntil, _shakeAmplitude, _shakeSeconds = 1f;
        private Vector3 _lookPoint, _basePosition;

        public bool IsFollowing => _target != null;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            ShowBatting();
        }

        public void ShowBatting()
        {
            _target = null;
            _basePosition = _battingPosition;
            transform.SetPositionAndRotation(_battingPosition, Quaternion.LookRotation(_battingLookAt - _battingPosition));
            if (_camera != null) _camera.fieldOfView = _battingFov;
        }

        /// <summary>Starts following <paramref name="ball"/> after the hold (call at contact).</summary>
        public void Follow(Transform ball)
        {
            _target = ball;
            _followSince = Time.unscaledTime;
            _lookPoint = new Vector3(ball.position.x, 0.5f * ball.position.y, ball.position.z);
        }

        /// <summary>Small positional shake (m) for <paramref name="seconds"/>: contact feedback.</summary>
        public void Impulse(float amplitude, float seconds)
        {
            _shakeAmplitude = amplitude;
            _shakeSeconds = Mathf.Max(seconds, 1e-3f);
            _shakeUntil = Time.unscaledTime + _shakeSeconds;
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (_target != null)
            {
                float u = Mathf.SmoothStep(0f, 1f, (now - _followSince - _holdSeconds) / _blendSeconds);
                // Aim between the ball and the ground below it so the field stays in frame while the ball climbs.
                Vector3 ball = _target.position, aim = new Vector3(ball.x, 0.5f * Mathf.Max(ball.y, 0f), ball.z);
                _lookPoint = Vector3.Lerp(_lookPoint, aim, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
                float window = Mathf.Max(_followWindow, 1.6f * ball.y + 15f);
                Vector3 position = Vector3.Lerp(_battingPosition, _followPosition, u);
                Vector3 look = Vector3.Lerp(_battingLookAt, _lookPoint, Mathf.Max(u, Mathf.Clamp01((now - _followSince) / _holdSeconds) * 0.35f));
                float distance = Vector3.Distance(position, _lookPoint);
                float fov = Mathf.Clamp(2f * Mathf.Atan(window / 2f / Mathf.Max(distance, 1f)) * Mathf.Rad2Deg, 22f, 60f);
                _basePosition = position;
                transform.rotation = Quaternion.LookRotation(look - position);
                _camera.fieldOfView = Mathf.Lerp(_battingFov, fov, u);
            }

            // Shake is an offset on the recomputed base position, so it never accumulates.
            Vector3 shake = now < _shakeUntil ? Random.insideUnitSphere * _shakeAmplitude * ((_shakeUntil - now) / _shakeSeconds) : Vector3.zero;
            transform.position = _basePosition + shake;
        }
    }
}
