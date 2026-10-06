using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// One key pose of a <see cref="PlayerMannequin"/>, authored for a right-handed player, in the figure frame (+Z forward,
    /// +X right, +Y up, metres from the figure root; mirrored for left-handers). Torso: pelvis offset and local Euler
    /// angles (degrees) of pelvis, spine, chest, neck. Limbs: either bone directions (<see cref="Dir"/>) or, with weight
    /// &gt; 0, an IK target — hand or ankle position plus an elbow/knee hint direction — solved by a two-bone reach. Feet:
    /// yaw (toe direction) and pitch (+ = heel up) in the figure frame. Optional two-handed bat grip.
    /// </summary>
    public sealed class MannequinPose
    {
        public Vector3 PelvisOffset, Pelvis, Spine, Chest, Neck;
        public Vector3 RightUpperArm = Vector3.down, RightForearm = Vector3.down, LeftUpperArm = Vector3.down, LeftForearm = Vector3.down;
        public Vector3 RightThigh = Vector3.down, RightShin = Vector3.down, LeftThigh = Vector3.down, LeftShin = Vector3.down;
        /// <summary>Two-handed grip (bat): the right hand at <see cref="GripPoint"/>, the left just below it along
        /// <see cref="GripDirection"/>; overrides hand targets. Weight 0 = no grip.</summary>
        public float GripWeight;
        public Vector3 GripPoint, GripDirection = Vector3.up;
        /// <summary>Hand IK (wrist position) with an elbow hint direction; used when the weight is &gt; 0.</summary>
        public float RightHandWeight, LeftHandWeight;
        public Vector3 RightHand, LeftHand, RightElbowHint = Vector3.down, LeftElbowHint = Vector3.down;
        /// <summary>How much the left-hand target is the glove's (its anchor, 8 cm past the wrist) rather than the wrist's:
        /// 1 = the glove meets the target, 0 = the wrist; blended, so moving between them never pops the hand.</summary>
        public float LeftHandGlove;
        /// <summary>Ankle IK with a knee hint direction; used when the weight is &gt; 0. A planted foot keeps the same target.</summary>
        public float RightFootWeight, LeftFootWeight;
        public Vector3 RightFoot, LeftFoot, RightKneeHint = Vector3.forward, LeftKneeHint = Vector3.forward;
        /// <summary>Toe direction (degrees, 0 = figure forward, + toward the right) and pitch (+ = heel lifted).</summary>
        public float RightFootYaw, LeftFootYaw, RightFootPitch, LeftFootPitch;

        /// <summary>Direction from azimuth (0 = forward, +90 = right) and elevation (+90 = up), degrees.</summary>
        public static Vector3 Dir(float azimuth, float elevation)
        {
            float az = azimuth * Mathf.Deg2Rad, el = elevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        }

        public MannequinPose Clone() => (MannequinPose)MemberwiseClone();

        /// <summary>Linear blend of <paramref name="a"/> and <paramref name="b"/> at <paramref name="t"/> ∈ [0, 1] into <paramref name="into"/>.</summary>
        public static void Blend(MannequinPose a, MannequinPose b, float t, MannequinPose into) => Interpolate(a, a, b, b, t, false, into);

        /// <summary>
        /// Interpolates between <paramref name="p1"/> and <paramref name="p2"/>; with <paramref name="cubic"/> a Catmull-Rom
        /// spline through the neighbours <paramref name="p0"/>, <paramref name="p3"/> (C¹: no stop at every key). No allocation.
        /// </summary>
        public static void Interpolate(MannequinPose p0, MannequinPose p1, MannequinPose p2, MannequinPose p3, float t, bool cubic, MannequinPose into)
        {
            Vector3 V(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => cubic ? CatmullRom(a, b, c, d, t) : Vector3.Lerp(b, c, t);
            Vector3 D(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Vector3 v = V(a, b, c, d); return v.sqrMagnitude > 1e-12f ? v.normalized : c; }
            float F(float a, float b, float c, float d) => cubic ? CatmullRom(a, b, c, d, t) : Mathf.Lerp(b, c, t);
            into.PelvisOffset = V(p0.PelvisOffset, p1.PelvisOffset, p2.PelvisOffset, p3.PelvisOffset);
            into.Pelvis = V(p0.Pelvis, p1.Pelvis, p2.Pelvis, p3.Pelvis);
            into.Spine = V(p0.Spine, p1.Spine, p2.Spine, p3.Spine);
            into.Chest = V(p0.Chest, p1.Chest, p2.Chest, p3.Chest);
            into.Neck = V(p0.Neck, p1.Neck, p2.Neck, p3.Neck);
            into.RightUpperArm = D(p0.RightUpperArm, p1.RightUpperArm, p2.RightUpperArm, p3.RightUpperArm);
            into.RightForearm = D(p0.RightForearm, p1.RightForearm, p2.RightForearm, p3.RightForearm);
            into.LeftUpperArm = D(p0.LeftUpperArm, p1.LeftUpperArm, p2.LeftUpperArm, p3.LeftUpperArm);
            into.LeftForearm = D(p0.LeftForearm, p1.LeftForearm, p2.LeftForearm, p3.LeftForearm);
            into.RightThigh = D(p0.RightThigh, p1.RightThigh, p2.RightThigh, p3.RightThigh);
            into.RightShin = D(p0.RightShin, p1.RightShin, p2.RightShin, p3.RightShin);
            into.LeftThigh = D(p0.LeftThigh, p1.LeftThigh, p2.LeftThigh, p3.LeftThigh);
            into.LeftShin = D(p0.LeftShin, p1.LeftShin, p2.LeftShin, p3.LeftShin);
            // Weights switch IK on/off: interpolate linearly (no overshoot).
            into.GripWeight = Mathf.Lerp(p1.GripWeight, p2.GripWeight, t);
            into.GripPoint = V(p0.GripPoint, p1.GripPoint, p2.GripPoint, p3.GripPoint);
            into.GripDirection = D(p0.GripDirection, p1.GripDirection, p2.GripDirection, p3.GripDirection);
            into.RightHandWeight = Mathf.Lerp(p1.RightHandWeight, p2.RightHandWeight, t);
            into.LeftHandWeight = Mathf.Lerp(p1.LeftHandWeight, p2.LeftHandWeight, t);
            into.RightHand = V(p0.RightHand, p1.RightHand, p2.RightHand, p3.RightHand);
            into.LeftHand = V(p0.LeftHand, p1.LeftHand, p2.LeftHand, p3.LeftHand);
            into.LeftHandGlove = Mathf.Lerp(p1.LeftHandGlove, p2.LeftHandGlove, t);
            into.RightElbowHint = D(p0.RightElbowHint, p1.RightElbowHint, p2.RightElbowHint, p3.RightElbowHint);
            into.LeftElbowHint = D(p0.LeftElbowHint, p1.LeftElbowHint, p2.LeftElbowHint, p3.LeftElbowHint);
            into.RightFootWeight = Mathf.Lerp(p1.RightFootWeight, p2.RightFootWeight, t);
            into.LeftFootWeight = Mathf.Lerp(p1.LeftFootWeight, p2.LeftFootWeight, t);
            // A planted foot (same target on both keys) must not drift: keep it exact instead of following the spline.
            into.RightFoot = p1.RightFoot == p2.RightFoot ? p1.RightFoot : V(p0.RightFoot, p1.RightFoot, p2.RightFoot, p3.RightFoot);
            into.LeftFoot = p1.LeftFoot == p2.LeftFoot ? p1.LeftFoot : V(p0.LeftFoot, p1.LeftFoot, p2.LeftFoot, p3.LeftFoot);
            into.RightKneeHint = D(p0.RightKneeHint, p1.RightKneeHint, p2.RightKneeHint, p3.RightKneeHint);
            into.LeftKneeHint = D(p0.LeftKneeHint, p1.LeftKneeHint, p2.LeftKneeHint, p3.LeftKneeHint);
            into.RightFootYaw = F(p0.RightFootYaw, p1.RightFootYaw, p2.RightFootYaw, p3.RightFootYaw);
            into.LeftFootYaw = F(p0.LeftFootYaw, p1.LeftFootYaw, p2.LeftFootYaw, p3.LeftFootYaw);
            into.RightFootPitch = F(p0.RightFootPitch, p1.RightFootPitch, p2.RightFootPitch, p3.RightFootPitch);
            into.LeftFootPitch = F(p0.LeftFootPitch, p1.LeftFootPitch, p2.LeftFootPitch, p3.LeftFootPitch);
        }

        /// <summary>
        /// Blend that steps instead of sliding: everything blends at <paramref name="t"/>, but each foot whose ankle target
        /// moves travels in its own half of the time (left first, then right) with a small lift — a two-step return (e.g. a
        /// batter resetting his stance, a pitcher walking back to the rubber).
        /// </summary>
        public static void StepBlend(MannequinPose from, MannequinPose to, float t, MannequinPose into)
        {
            Blend(from, to, t, into);
            into.LeftFoot = Step(from.LeftFoot, to.LeftFoot, Mathf.Clamp01(2f * t));
            into.RightFoot = Step(from.RightFoot, to.RightFoot, Mathf.Clamp01(2f * t - 1f));
        }

        private static Vector3 Step(Vector3 a, Vector3 b, float f)
        {
            float e = Mathf.SmoothStep(0f, 1f, f);
            Vector3 p = Vector3.Lerp(a, b, e);
            if ((b - a).sqrMagnitude > 0.0004f) p.y += 0.1f * Mathf.Sin(Mathf.PI * f);
            return p;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) =>
            0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t));

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t));
    }
}
