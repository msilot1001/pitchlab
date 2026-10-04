using System;
using UnityEngine;

namespace Pitchlab.Presentation
{
    public enum MannequinJoint
    {
        Pelvis, Spine, Chest, Neck,
        LeftUpperArm, LeftForearm, LeftHand,
        RightUpperArm, RightForearm, RightHand,
        LeftThigh, LeftShin, LeftFoot,
        RightThigh, RightShin, RightFoot,
    }

    /// <summary>
    /// Abstract dark player figure built from primitives (Statcast/Gameday style), reused for every role: pitcher,
    /// batter, and later fielders and runners. Transform hierarchy only — no Rigidbody, no colliders; it shows what
    /// gameplay decided and never decides anything. Local frame: +Z forward (facing), +Y up, +X the player's right;
    /// in the rest pose arms and legs hang straight down. <see cref="LeftHanded"/> mirrors the figure, so poses are
    /// authored once for a right-handed player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMannequin : MonoBehaviour
    {
        public const int JointCount = 16;

        [SerializeField] private bool _leftHanded;
        [SerializeField] private Color _bodyColor = new Color(0.07f, 0.07f, 0.08f);

        private readonly Transform[] _joints = new Transform[JointCount];
        private Transform _visual;
        private Vector3 _pelvisRest;

        /// <summary>Equipment anchors (children of the hands): bat grip, glove, and a held ball.</summary>
        public Transform RightHandAnchor { get; private set; }
        public Transform LeftHandAnchor { get; private set; }
        public Transform BatAnchor { get; private set; }
        public Transform GloveAnchor { get; private set; }
        public Transform BallAnchor { get; private set; }

        public bool IsBuilt => _visual != null;

        public bool LeftHanded
        {
            get => _leftHanded;
            set
            {
                _leftHanded = value;
                if (_visual != null) _visual.localScale = new Vector3(value ? -1f : 1f, 1f, 1f);
            }
        }

        public Transform Joint(MannequinJoint joint) => _joints[(int)joint];

        private void Awake() => Build();

        /// <summary>Creates the body once (idempotent; also callable before Awake, e.g. from tests).</summary>
        public void Build()
        {
            if (_visual != null) return;
            Material body = PresentationMaterials.Get(_bodyColor);
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);

            // Proportions of a ≈ 1.85 m player (m).
            Transform pelvis = Bone(MannequinJoint.Pelvis, _visual, new Vector3(0f, 0.98f, 0f));
            Shape(pelvis, PrimitiveType.Cube, Vector3.zero, new Vector3(0.30f, 0.16f, 0.18f), body);
            Transform spine = Bone(MannequinJoint.Spine, pelvis, new Vector3(0f, 0.08f, 0f));
            Shape(spine, PrimitiveType.Capsule, new Vector3(0f, 0.13f, 0f), new Vector3(0.24f, 0.13f, 0.16f), body);
            Transform chest = Bone(MannequinJoint.Chest, spine, new Vector3(0f, 0.24f, 0f));
            Shape(chest, PrimitiveType.Capsule, new Vector3(0f, 0.12f, 0f), new Vector3(0.38f, 0.16f, 0.22f), body, Quaternion.Euler(0f, 0f, 90f));
            Transform neck = Bone(MannequinJoint.Neck, chest, new Vector3(0f, 0.26f, 0f));
            Shape(neck, PrimitiveType.Sphere, new Vector3(0f, 0.15f, 0.01f), new Vector3(0.21f, 0.24f, 0.22f), body);   // head

            Limb(MannequinJoint.RightUpperArm, chest, new Vector3(0.21f, 0.2f, 0f), 0.29f, 0.085f, body);
            Limb(MannequinJoint.RightForearm, Joint(MannequinJoint.RightUpperArm), new Vector3(0f, -0.29f, 0f), 0.26f, 0.07f, body);
            Transform rightHand = Bone(MannequinJoint.RightHand, Joint(MannequinJoint.RightForearm), new Vector3(0f, -0.26f, 0f));
            Shape(rightHand, PrimitiveType.Sphere, new Vector3(0f, -0.04f, 0f), new Vector3(0.08f, 0.1f, 0.06f), body);
            Limb(MannequinJoint.LeftUpperArm, chest, new Vector3(-0.21f, 0.2f, 0f), 0.29f, 0.085f, body);
            Limb(MannequinJoint.LeftForearm, Joint(MannequinJoint.LeftUpperArm), new Vector3(0f, -0.29f, 0f), 0.26f, 0.07f, body);
            Transform leftHand = Bone(MannequinJoint.LeftHand, Joint(MannequinJoint.LeftForearm), new Vector3(0f, -0.26f, 0f));
            Shape(leftHand, PrimitiveType.Sphere, new Vector3(0f, -0.04f, 0f), new Vector3(0.08f, 0.1f, 0.06f), body);

            Limb(MannequinJoint.RightThigh, pelvis, new Vector3(0.1f, -0.05f, 0f), 0.44f, 0.13f, body);
            Limb(MannequinJoint.RightShin, Joint(MannequinJoint.RightThigh), new Vector3(0f, -0.44f, 0f), 0.43f, 0.1f, body);
            Transform rightFoot = Bone(MannequinJoint.RightFoot, Joint(MannequinJoint.RightShin), new Vector3(0f, -0.43f, 0f));
            Shape(rightFoot, PrimitiveType.Cube, new Vector3(0f, -0.04f, 0.06f), new Vector3(0.1f, 0.07f, 0.26f), body);
            Limb(MannequinJoint.LeftThigh, pelvis, new Vector3(-0.1f, -0.05f, 0f), 0.44f, 0.13f, body);
            Limb(MannequinJoint.LeftShin, Joint(MannequinJoint.LeftThigh), new Vector3(0f, -0.44f, 0f), 0.43f, 0.1f, body);
            Transform leftFoot = Bone(MannequinJoint.LeftFoot, Joint(MannequinJoint.LeftShin), new Vector3(0f, -0.43f, 0f));
            Shape(leftFoot, PrimitiveType.Cube, new Vector3(0f, -0.04f, 0.06f), new Vector3(0.1f, 0.07f, 0.26f), body);

            RightHandAnchor = Anchor("RightHandAnchor", rightHand, new Vector3(0f, -0.06f, 0.02f));
            LeftHandAnchor = Anchor("LeftHandAnchor", leftHand, new Vector3(0f, -0.06f, 0.02f));
            // The bat grip sits in the right (top) hand; glove on the left hand; a held ball in the right fingers.
            BatAnchor = Anchor("BatAnchor", rightHand, new Vector3(0f, -0.06f, 0f));
            GloveAnchor = Anchor("GloveAnchor", leftHand, new Vector3(0f, -0.08f, 0.02f));
            BallAnchor = Anchor("BallAnchor", rightHand, new Vector3(0f, -0.1f, 0.04f));

            _pelvisRest = pelvis.localPosition;
            LeftHanded = _leftHanded;
        }

        /// <summary>
        /// Applies a pose: torso joint rotations, pelvis offset, and limb directions. Directions are in the figure's own
        /// frame (+Z forward, +X right) and are applied in each parent's local space, so a mirrored (left-handed) figure
        /// mirrors them. Feet stay flat. No allocation.
        /// </summary>
        public void ApplyPose(MannequinPose pose)
        {
            Transform pelvis = _joints[(int)MannequinJoint.Pelvis];
            pelvis.localPosition = _pelvisRest + pose.PelvisOffset;
            pelvis.localRotation = Quaternion.Euler(pose.Pelvis);
            _joints[(int)MannequinJoint.Spine].localRotation = Quaternion.Euler(pose.Spine);
            _joints[(int)MannequinJoint.Chest].localRotation = Quaternion.Euler(pose.Chest);
            _joints[(int)MannequinJoint.Neck].localRotation = Quaternion.Euler(pose.Neck);
            Point(MannequinJoint.RightUpperArm, pose.RightUpperArm);
            Point(MannequinJoint.RightForearm, pose.RightForearm);
            Point(MannequinJoint.LeftUpperArm, pose.LeftUpperArm);
            Point(MannequinJoint.LeftForearm, pose.LeftForearm);
            Point(MannequinJoint.RightThigh, pose.RightThigh);
            Point(MannequinJoint.RightShin, pose.RightShin);
            Point(MannequinJoint.LeftThigh, pose.LeftThigh);
            Point(MannequinJoint.LeftShin, pose.LeftShin);
            _joints[(int)MannequinJoint.RightHand].localRotation = Quaternion.identity;
            _joints[(int)MannequinJoint.LeftHand].localRotation = Quaternion.identity;
            Flatten(MannequinJoint.RightFoot);
            Flatten(MannequinJoint.LeftFoot);
            if (pose.GripWeight > 0f) Grip(pose.GripPoint, pose.GripDirection);
        }

        /// <summary>
        /// Two-handed grip: the right (top) hand at <paramref name="figurePoint"/>, the left hand just below it along
        /// <paramref name="figureDirection"/>, and the bat anchor pointing along it (figure frame; mirrored for lefties).
        /// </summary>
        public void Grip(Vector3 figurePoint, Vector3 figureDirection)
        {
            Vector3 point = _visual.TransformPoint(figurePoint), dir = FigureToWorld(figureDirection);
            Vector3 down = -Vector3.up * 0.3f, body = _joints[(int)MannequinJoint.Chest].position;
            ReachArm(true, point, point + down + (point - body) * 0.2f);
            ReachArm(false, point - dir * 0.09f, point - dir * 0.09f + down);
            AimLocalAxis(BatAnchor, Vector3.up, dir);
        }

        /// <summary>Points a bone (its local −Y) along <paramref name="figureDirection"/> (figure frame).</summary>
        public void Point(MannequinJoint joint, Vector3 figureDirection) =>
            PointWorld(_joints[(int)joint], _visual.TransformVector(figureDirection));

        /// <summary>World direction of a figure-frame direction (includes the left-handed mirror).</summary>
        public Vector3 FigureToWorld(Vector3 figureDirection) => _visual.TransformVector(figureDirection).normalized;

        /// <summary>Orients <paramref name="item"/> so its local axis <paramref name="itemAxis"/> points along a world direction.</summary>
        public static void AimLocalAxis(Transform item, Vector3 itemAxis, Vector3 worldDirection)
        {
            Vector3 local = item.parent.InverseTransformVector(worldDirection);
            if (local.sqrMagnitude > 1e-12f) item.localRotation = Quaternion.FromToRotation(itemAxis, local.normalized);
        }

        /// <summary>
        /// Bends an arm so its hand reaches toward <paramref name="target"/> (world): two-bone analytic solution with the
        /// elbow toward <paramref name="elbowHint"/> (world point). Presentation-only grip helper (second hand on the bat).
        /// </summary>
        public void ReachArm(bool rightArm, Vector3 target, Vector3 elbowHint)
        {
            Transform upper = Joint(rightArm ? MannequinJoint.RightUpperArm : MannequinJoint.LeftUpperArm);
            Transform lower = Joint(rightArm ? MannequinJoint.RightForearm : MannequinJoint.LeftForearm);
            Transform hand = Joint(rightArm ? MannequinJoint.RightHand : MannequinJoint.LeftHand);
            float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
            Vector3 toTarget = target - upper.position;
            float d = Mathf.Clamp(toTarget.magnitude, 0.05f, (a + b) * 0.999f);
            Vector3 dir = toTarget.normalized;
            Vector3 hint = Vector3.ProjectOnPlane(elbowHint - upper.position, dir);
            hint = hint.sqrMagnitude > 1e-8f ? hint.normalized : Vector3.Cross(dir, Vector3.up).normalized;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector3 elbow = upper.position + a * (cosA * dir + Mathf.Sqrt(1f - cosA * cosA) * hint);
            PointWorld(upper, elbow - upper.position);
            PointWorld(lower, upper.position + dir * d - lower.position);
        }

        private static void PointWorld(Transform bone, Vector3 world) => AimLocalAxis(bone, Vector3.down, world);

        private void Flatten(MannequinJoint foot)
        {
            Transform f = _joints[(int)foot];
            Vector3 forward = Vector3.ProjectOnPlane(_joints[(int)MannequinJoint.Pelvis].TransformVector(Vector3.forward), Vector3.up);
            Vector3 up = f.parent.InverseTransformVector(Vector3.up), fwd = f.parent.InverseTransformVector(forward);
            if (fwd.sqrMagnitude > 1e-8f) f.localRotation = Quaternion.LookRotation(fwd.normalized, up.normalized);
        }

        private Transform Bone(MannequinJoint joint, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(joint.ToString()).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            _joints[(int)joint] = t;
            return t;
        }

        private void Limb(MannequinJoint joint, Transform parent, Vector3 localPosition, float length, float thickness, Material material)
        {
            Transform bone = Bone(joint, parent, localPosition);
            Shape(bone, PrimitiveType.Capsule, new Vector3(0f, -length / 2f, 0f), new Vector3(thickness, length / 2f, thickness), material);
            Shape(bone, PrimitiveType.Sphere, Vector3.zero, Vector3.one * thickness * 1.05f, material);   // joint ball
        }

        private static Transform Anchor(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        public static void Shape(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material, Quaternion? localRotation = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            DestroyCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation ?? Quaternion.identity;
            go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>Presentation primitives never collide: gameplay contact is resolved elsewhere.</summary>
        public static void DestroyCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }
    }

    /// <summary>Shared URP materials by colour (one instance per colour, no per-object leaks).</summary>
    public static class PresentationMaterials
    {
        private static readonly System.Collections.Generic.Dictionary<Color, Material> Cache = new System.Collections.Generic.Dictionary<Color, Material>();

        /// <param name="unlit">Flat, always-bright colour (the ball), unaffected by lighting.</param>
        public static Material Get(Color color, bool unlit = false)
        {
            Color key = unlit ? new Color(color.r, color.g, color.b, 0.5f) : color;
            if (Cache.TryGetValue(key, out Material m) && m != null) return m;
            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader) { color = color, name = $"Presentation {ColorUtility.ToHtmlStringRGB(color)}{(unlit ? " unlit" : "")}" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            Cache[key] = m;
            return m;
        }
    }
}
