using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>Ring on the ground where a batted ball landed, with its distance shown next to it.</summary>
    public sealed class LandingMarker : MonoBehaviour
    {
        private const int Segments = 48;
        private LineRenderer _ring;
        private string _label = string.Empty;
        private Camera _camera;
        private GUIStyle _style;

        public bool Visible => _ring != null && _ring.enabled;

        private void Awake()
        {
            _ring = gameObject.AddComponent<LineRenderer>();
            _ring.loop = true;
            _ring.useWorldSpace = false;
            _ring.positionCount = Segments;
            _ring.widthMultiplier = 0.4f;
            _ring.sharedMaterial = PresentationMaterials.Get(new Color(1f, 0.85f, 0.2f), unlit: true);
            for (int i = 0; i < Segments; i++)
            {
                float a = 2f * Mathf.PI * i / Segments;
                _ring.SetPosition(i, new Vector3(3f * Mathf.Cos(a), 0.05f, 3f * Mathf.Sin(a)));
            }

            _ring.enabled = false;
        }

        public void Show(Vector3 groundPoint, string label, Camera viewer)
        {
            transform.position = new Vector3(groundPoint.x, 0f, groundPoint.z);
            _label = label;
            _camera = viewer;
            _ring.enabled = true;
        }

        public void Hide() => _ring.enabled = false;

        private void OnGUI()
        {
            if (!Visible || _camera == null) return;
            Vector3 screen = _camera.WorldToScreenPoint(transform.position + Vector3.up * 2f);
            if (screen.z <= 0f) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.9f, 0.3f) } };
            GUI.Label(new Rect(screen.x + 12f, Screen.height - screen.y - 14f, 240f, 30f), _label, _style);
        }
    }
}
