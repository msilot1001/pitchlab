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
        /// <summary>Upper-arm length (m).</summary>
        public const float UpperArm = 0.33f;
        /// <summary>Ankle height (m) above the ground when the foot is flat (the foot box reaches the ground).</summary>
        public const float AnkleHeight = 0.075f;

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

        /// <summary>Uniform colour (recolours a built body; equipment keeps its own materials).</summary>
        public Color BodyColor
        {
            get => _bodyColor;
            set
            {
                Material old = PresentationMaterials.Get(_bodyColor), body = PresentationMaterials.Get(value);
                _bodyColor = value;
                if (_visual == null) return;
                foreach (MeshRenderer r in _visual.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.sharedMaterial == old) r.sharedMaterial = body;
            }
        }

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

            // Segment lengths ≈ standard adult ratios of height H = 1.85 m (Drillis & Contini / Winter): upper arm 0.186 H,
            // forearm 0.146 H, thigh/shank ≈ 0.245 H, shoulder height 0.818 H, hip height 0.53 H.
            Limb(MannequinJoint.RightUpperArm, chest, new Vector3(0.2f, 0.2f, 0f), UpperArm, 0.085f, body);
            Limb(MannequinJoint.RightForearm, Joint(MannequinJoint.RightUpperArm), new Vector3(0f, -UpperArm, 0f), 0.26f, 0.07f, body);
            Transform rightHand = Bone(MannequinJoint.RightHand, Joint(MannequinJoint.RightForearm), new Vector3(0f, -0.26f, 0f));
            Shape(rightHand, PrimitiveType.Sphere, new Vector3(0f, -0.04f, 0f), new Vector3(0.08f, 0.1f, 0.06f), body);
            Limb(MannequinJoint.LeftUpperArm, chest, new Vector3(-0.2f, 0.2f, 0f), UpperArm, 0.085f, body);
            Limb(MannequinJoint.LeftForearm, Joint(MannequinJoint.LeftUpperArm), new Vector3(0f, -UpperArm, 0f), 0.26f, 0.07f, body);
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
            BatAnchor = Anchor("BatAnchor", rightHand, Vector3.zero);   // at the wrist joint the grip IK targets
            GloveAnchor = Anchor("GloveAnchor", leftHand, new Vector3(0f, -0.08f, 0.02f));
            BallAnchor = Anchor("BallAnchor", rightHand, new Vector3(0f, -0.13f, 0.03f));   // ball centre in the fingers (hand ≈ 0.108 H)

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
            if (pose.RightFootWeight > 0f) ReachLimb(MannequinJoint.RightThigh, pose.RightFoot, pose.RightKneeHint);
            if (pose.LeftFootWeight > 0f) ReachLimb(MannequinJoint.LeftThigh, pose.LeftFoot, pose.LeftKneeHint);
            OrientFoot(MannequinJoint.RightFoot, pose.RightFootYaw, pose.RightFootPitch);
            OrientFoot(MannequinJoint.LeftFoot, pose.LeftFootYaw, pose.LeftFootPitch);
            if (pose.RightHandWeight > 0f) ReachLimb(MannequinJoint.RightUpperArm, pose.RightHand, pose.RightElbowHint);
            if (pose.LeftHandWeight > 0f) ReachLimb(MannequinJoint.LeftUpperArm, pose.LeftHand, pose.LeftElbowHint);
            if (pose.GripWeight > 0f) Grip(pose.GripPoint, pose.GripDirection);
        }

        /// <summary>Figure-frame vector of a world vector (includes the left-handed mirror).</summary>
        public Vector3 WorldToFigure(Vector3 worldVector) => _visual.InverseTransformVector(worldVector);

        /// <summary>Figure-frame point of a world position (includes the left-handed mirror).</summary>
        public Vector3 WorldToFigurePoint(Vector3 worldPoint) => _visual.InverseTransformPoint(worldPoint);

        /// <summary>World position of a figure-frame point (includes the left-handed mirror).</summary>
        public Vector3 FigurePoint(Vector3 figurePoint) => _visual.TransformPoint(figurePoint);

        /// <summary>
        /// Two-bone reach of a limb (upper arm or thigh as <paramref name="root"/>) so its end joint (wrist or ankle) is at a
        /// figure-frame point, bending toward a figure-frame hint direction (elbow/knee side).
        /// </summary>
        public void ReachLimb(MannequinJoint root, Vector3 figureTarget, Vector3 figureHint)
        {
            Transform upper = Joint(root);
            Transform lower = Joint(root + 1);
            Vector3 target = _visual.TransformPoint(figureTarget);
            Reach(upper, lower, Joint(root + 2), target, (upper.position + target) * 0.5f + FigureToWorld(figureHint) * 0.5f);
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
        public void ReachArm(bool rightArm, Vector3 target, Vector3 elbowHint) => Reach(
            Joint(rightArm ? MannequinJoint.RightUpperArm : MannequinJoint.LeftUpperArm),
            Joint(rightArm ? MannequinJoint.RightForearm : MannequinJoint.LeftForearm),
            Joint(rightArm ? MannequinJoint.RightHand : MannequinJoint.LeftHand), target, elbowHint);

        private static void Reach(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 elbowHint)
        {
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

        /// <summary>Foot toe direction (yaw, figure frame) and pitch (+ = heel up), independent of the shin.</summary>
        private void OrientFoot(MannequinJoint foot, float yaw, float pitch)
        {
            Transform f = _joints[(int)foot];
            Quaternion q = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 forward = _visual.TransformVector(q * Vector3.forward), up = _visual.TransformVector(q * Vector3.up);
            Vector3 fwd = f.parent.InverseTransformVector(forward), u = f.parent.InverseTransformVector(up);
            if (fwd.sqrMagnitude > 1e-8f) f.localRotation = Quaternion.LookRotation(fwd.normalized, u.normalized);
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
