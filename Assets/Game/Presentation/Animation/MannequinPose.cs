using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// One key pose of a <see cref="PlayerMannequin"/>, authored for a right-handed player. Torso: local Euler angles
    /// (degrees) and a pelvis offset (m, e.g. crouch or stride). Limbs: bone directions in the figure frame (+Z forward,
    /// +X right, +Y up); see <see cref="Dir"/>.
    /// </summary>
    public sealed class MannequinPose
    {
        public Vector3 PelvisOffset, Pelvis, Spine, Chest, Neck;
        public Vector3 RightUpperArm = Vector3.down, RightForearm = Vector3.down, LeftUpperArm = Vector3.down, LeftForearm = Vector3.down;
        public Vector3 RightThigh = Vector3.down, RightShin = Vector3.down, LeftThigh = Vector3.down, LeftShin = Vector3.down;
        /// <summary>Two-handed grip (bat): both hands reach <see cref="GripPoint"/> (figure frame, m from the figure root)
        /// and the held item points along <see cref="GripDirection"/>. Weight 0 = arms follow their directions.</summary>
        public float GripWeight;
        public Vector3 GripPoint, GripDirection = Vector3.up;

        /// <summary>Direction from azimuth (0 = forward, +90 = right) and elevation (+90 = up), degrees.</summary>
        public static Vector3 Dir(float azimuth, float elevation)
        {
            float az = azimuth * Mathf.Deg2Rad, el = elevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        }

        /// <summary>Writes the blend of <paramref name="a"/> and <paramref name="b"/> at <paramref name="t"/> ∈ [0, 1] into <paramref name="into"/>.</summary>
        public static void Blend(MannequinPose a, MannequinPose b, float t, MannequinPose into)
        {
            into.PelvisOffset = Vector3.Lerp(a.PelvisOffset, b.PelvisOffset, t);
            into.Pelvis = Euler(a.Pelvis, b.Pelvis, t);
            into.Spine = Euler(a.Spine, b.Spine, t);
            into.Chest = Euler(a.Chest, b.Chest, t);
            into.Neck = Euler(a.Neck, b.Neck, t);
            into.RightUpperArm = Vector3.Slerp(a.RightUpperArm, b.RightUpperArm, t);
            into.RightForearm = Vector3.Slerp(a.RightForearm, b.RightForearm, t);
            into.LeftUpperArm = Vector3.Slerp(a.LeftUpperArm, b.LeftUpperArm, t);
            into.LeftForearm = Vector3.Slerp(a.LeftForearm, b.LeftForearm, t);
            into.RightThigh = Vector3.Slerp(a.RightThigh, b.RightThigh, t);
            into.RightShin = Vector3.Slerp(a.RightShin, b.RightShin, t);
            into.LeftThigh = Vector3.Slerp(a.LeftThigh, b.LeftThigh, t);
            into.LeftShin = Vector3.Slerp(a.LeftShin, b.LeftShin, t);
            into.GripWeight = Mathf.Lerp(a.GripWeight, b.GripWeight, t);
            into.GripPoint = Vector3.Lerp(a.GripPoint, b.GripPoint, t);
            into.GripDirection = Vector3.Slerp(a.GripDirection, b.GripDirection, t);
        }

        // Euler angles are blended per component (poses stay well inside ±180°), which keeps authoring intuitive.
        private static Vector3 Euler(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, t);
    }

    /// <summary>Key poses on a timeline (seconds), smoothly interpolated; clamps outside the first/last key.</summary>
    public sealed class PoseSequence
    {
        private readonly float[] _times;
        private readonly MannequinPose[] _poses;

        public PoseSequence(params (float Time, MannequinPose Pose)[] keys)
        {
            _times = new float[keys.Length];
            _poses = new MannequinPose[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                _times[i] = keys[i].Time;
                _poses[i] = keys[i].Pose;
            }
        }

        public float Start => _times[0];
        public float End => _times[_times.Length - 1];

        /// <summary>Writes the pose at time <paramref name="t"/> into <paramref name="into"/> (no allocation).</summary>
        public void Sample(float t, MannequinPose into)
        {
            int last = _times.Length - 1;
            if (t <= _times[0]) { MannequinPose.Blend(_poses[0], _poses[0], 0f, into); return; }
            if (t >= _times[last]) { MannequinPose.Blend(_poses[last], _poses[last], 0f, into); return; }
            int i = 0;
            while (t > _times[i + 1]) i++;
            float u = Mathf.SmoothStep(0f, 1f, (t - _times[i]) / (_times[i + 1] - _times[i]));
            MannequinPose.Blend(_poses[i], _poses[i + 1], u, into);
        }
    }
}
