using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>Restrained contact feedback: a short procedural "crack" and a brief flash at the contact point.</summary>
    public sealed class ContactCue : MonoBehaviour
    {
        private const float FlashSeconds = 0.12f;
        private AudioSource _audio;
        private AudioClip _crack;
        private Transform _flash;
        private float _flashStart = -1f;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _crack = MakeCrack();
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "ContactFlash";
            PlayerMannequin.DestroyCollider(flash);
            flash.transform.SetParent(transform, false);
            flash.GetComponent<MeshRenderer>().sharedMaterial = PresentationMaterials.Get(new Color(1f, 0.95f, 0.75f), unlit: true);
            flash.SetActive(false);
            _flash = flash.transform;
        }

        public void Play(Vector3 position, float strength)
        {
            _audio.PlayOneShot(_crack, Mathf.Clamp01(0.4f + 0.6f * strength));
            _flash.position = position;
            _flash.gameObject.SetActive(true);
            _flashStart = Time.unscaledTime;
        }

        public void Hide()
        {
            _flash.gameObject.SetActive(false);
            _flashStart = -1f;
        }

        private void Update()
        {
            if (_flashStart < 0f) return;
            float u = (Time.unscaledTime - _flashStart) / FlashSeconds;
            if (u >= 1f) { Hide(); return; }
            _flash.localScale = Vector3.one * Mathf.Lerp(0.12f, 0.45f, u);
        }

        private static AudioClip MakeCrack()
        {
            const int rate = 44100;
            int n = rate * 6 / 100;
            var data = new float[n];
            var random = new System.Random(7);   // deterministic noise burst
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 90f);
                data[i] = envelope * (0.6f * (float)(random.NextDouble() * 2.0 - 1.0) + 0.4f * Mathf.Sin(2f * Mathf.PI * 1900f * t));
            }

            AudioClip clip = AudioClip.Create("BatCrack", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
