using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// Minimal field context from primitives (Unity frame: home plate's rear tip at the origin, +Z toward the pitcher,
    /// +X toward first base, +Y up): grass, infield dirt, mound, foul lines, batter's boxes, bases. Spatial readability
    /// only — no colliders, no gameplay meaning.
    /// </summary>
    public sealed class FieldDressing : MonoBehaviour
    {
        private const float Ft = 0.3048f;
        [SerializeField] private float _foulLineLength = 330f * Ft;
        private bool _built;

        private void Awake() => Build();

        public void Build()
        {
            if (_built) return;
            _built = true;
            Material grass = PresentationMaterials.Get(new Color(0.22f, 0.42f, 0.2f));
            Material dirt = PresentationMaterials.Get(new Color(0.55f, 0.4f, 0.27f));
            Material chalk = PresentationMaterials.Get(new Color(0.95f, 0.95f, 0.92f), unlit: true);

            float baseDistance = 90f * Ft, diagonal = baseDistance * Mathf.Sqrt(2f);
            Box("Grass", grass, new Vector3(0f, -0.02f, 70f), new Vector3(200f, 0.02f, 200f), 0f);
            // Infield dirt: a square on the diamond, enlarged past the bases, plus a dirt circle around home.
            Box("InfieldDirt", dirt, new Vector3(0f, -0.005f, diagonal / 2f), new Vector3(baseDistance + 4f, 0.01f, baseDistance + 4f), 45f);
            Box("InfieldGrass", grass, new Vector3(0f, -0.003f, diagonal / 2f), new Vector3(baseDistance - 3f, 0.01f, baseDistance - 3f), 45f);
            Disc("HomeCircle", dirt, new Vector3(0f, 0.004f, 0.2f), 13f * Ft, 0.01f);   // top just above the infield, below plate and chalk
            Mound(dirt);
            // Foul lines from the plate's rear tip, 45° to either side.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 dir = new Vector3(side, 0f, 1f).normalized;
                Box(side < 0 ? "ThirdBaseLine" : "FirstBaseLine", chalk, dir * (_foulLineLength / 2f) + Vector3.up * 0.003f,
                    new Vector3(0.08f, 0.006f, _foulLineLength), side * 45f);
            }

            // Distance arcs (200 / 300 / 400 ft) between the foul lines: a reading aid for where balls land, not a fence.
            Material faint = PresentationMaterials.Get(new Color(0.45f, 0.62f, 0.42f), unlit: true);
            foreach (float feet in new[] { 200f, 300f, 400f }) Arc(feet * Ft, faint);

            // Bases at the corners of the diamond (second base on the +Z axis).
            Box("FirstBase", chalk, new Vector3(baseDistance / Mathf.Sqrt(2f), 0.03f, baseDistance / Mathf.Sqrt(2f)), new Vector3(0.38f, 0.06f, 0.38f), 45f);
            Box("SecondBase", chalk, new Vector3(0f, 0.03f, diagonal), new Vector3(0.38f, 0.06f, 0.38f), 45f);
            Box("ThirdBase", chalk, new Vector3(-baseDistance / Mathf.Sqrt(2f), 0.03f, baseDistance / Mathf.Sqrt(2f)), new Vector3(0.38f, 0.06f, 0.38f), 45f);
            // Batter's boxes: 4 ft × 6 ft outlines, 6 in from the plate, centred on the plate's middle.
            for (int side = -1; side <= 1; side += 2)
            {
                float cx = side * (8.5f / 12f + 0.5f + 2f) * Ft, cz = 0.3f;
                float hw = 2f * Ft, hl = 3f * Ft;
                Box("BoxLine", chalk, new Vector3(cx - hw, 0.003f, cz), new Vector3(0.05f, 0.006f, 2f * hl), 0f);
                Box("BoxLine", chalk, new Vector3(cx + hw, 0.003f, cz), new Vector3(0.05f, 0.006f, 2f * hl), 0f);
                Box("BoxLine", chalk, new Vector3(cx, 0.003f, cz - hl), new Vector3(2f * hw, 0.006f, 0.05f), 0f);
                Box("BoxLine", chalk, new Vector3(cx, 0.003f, cz + hl), new Vector3(2f * hw, 0.006f, 0.05f), 0f);
            }
        }

        private void Box(string name, Material material, Vector3 position, Vector3 scale, float yaw)
        {
            PlayerMannequin.Shape(transform, PrimitiveType.Cube, position, scale, material, Quaternion.Euler(0f, yaw, 0f));
            transform.GetChild(transform.childCount - 1).name = name;
        }

        // ---- Mound (regulation-inspired, simplified): 18 ft circle centred 59 ft from the plate's rear tip, 10 in high
        // around the rubber (front edge 60.5 ft), falling 1 in per ft toward home from 6 in in front of the rubber, and
        // rounding off to the field level at the rim. Visual only: the simulation frame keeps y = 0 at plate level.
        public const float MoundTop = 10f / 12f * Ft, RubberFront = 60.5f * Ft;
        private const float MoundCentre = 59f * Ft, MoundRadius = 9f * Ft;

        /// <summary>Mound surface height (m) at a field point (Unity frame); 0 off the mound.</summary>
        public static float MoundHeight(float x, float z)
        {
            float r = new Vector2(x, z - MoundCentre).magnitude;
            if (r >= MoundRadius) return 0f;
            float front = MoundTop - Mathf.Max(0f, RubberFront - 0.5f * Ft - z) / 12f;   // 1 in per ft toward home
            float rim = MoundTop * Mathf.SmoothStep(0f, 1f, (MoundRadius - r) / 1.2f);
            return Mathf.Clamp(Mathf.Min(front, rim), 0f, MoundTop);
        }

        private void Mound(Material material)
        {
            const int rings = 14, segments = 48;
            var vertices = new Vector3[1 + rings * segments];
            var triangles = new int[(segments * 3) + (rings - 1) * segments * 6];
            vertices[0] = Point(0f, MoundCentre);
            for (int ring = 1; ring <= rings; ring++)
            for (int k = 0; k < segments; k++)
            {
                float r = MoundRadius * ring / rings, a = 2f * Mathf.PI * k / segments;
                vertices[1 + (ring - 1) * segments + k] = Point(r * Mathf.Sin(a), MoundCentre + r * Mathf.Cos(a));
            }

            int n = 0;
            for (int k = 0; k < segments; k++)
            {
                triangles[n++] = 0;
                triangles[n++] = 1 + k;
                triangles[n++] = 1 + (k + 1) % segments;
            }

            for (int ring = 1; ring < rings; ring++)
            for (int k = 0; k < segments; k++)
            {
                int a0 = 1 + (ring - 1) * segments + k, a1 = 1 + (ring - 1) * segments + (k + 1) % segments;
                int b0 = a0 + segments, b1 = a1 + segments;
                triangles[n++] = a0; triangles[n++] = b0; triangles[n++] = b1;
                triangles[n++] = a0; triangles[n++] = b1; triangles[n++] = a1;
            }

            var mesh = new Mesh { name = "Mound", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Mound");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            static Vector3 Point(float x, float z) => new Vector3(x, MoundHeight(x, z) + 0.003f, z);
        }

        private void Arc(float radius, Material material)
        {
            var arc = new GameObject($"Arc{Mathf.RoundToInt(radius / Ft)}ft").AddComponent<LineRenderer>();
            arc.transform.SetParent(transform, false);
            arc.useWorldSpace = false;
            arc.widthMultiplier = 0.35f;
            arc.sharedMaterial = material;
            const int segments = 40;
            arc.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(-45f, 45f, i / (float)segments) * Mathf.Deg2Rad;
                arc.SetPosition(i, new Vector3(radius * Mathf.Sin(a), 0.01f, radius * Mathf.Cos(a)));
            }
        }

        private void Disc(string name, Material material, Vector3 position, float radius, float height)
        {
            PlayerMannequin.Shape(transform, PrimitiveType.Cylinder, position + Vector3.up * (height / 2f - 0.01f), new Vector3(2f * radius, height / 2f, 2f * radius), material);
            transform.GetChild(transform.childCount - 1).name = name;
        }
    }
}
