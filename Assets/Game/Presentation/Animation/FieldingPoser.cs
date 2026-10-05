using UnityEngine;
using static Pitchlab.Presentation.MannequinPose;

namespace Pitchlab.Presentation
{
    /// <summary>What the fielder is doing this frame, all from gameplay state (presentation decides nothing).</summary>
    public struct FieldingPoseInput
    {
        /// <summary>Running speed (m/s) and distance run so far on the route (m): the run cycle is phased by distance.</summary>
        public float Speed, Distance;
        /// <summary>Glove (left hand) target in the figure frame, and how far the glove has gone to it (0–1).</summary>
        public Vector3 GloveTarget;
        public float GloveWeight;
        /// <summary>Ball in the glove: both hands together in front of the chest.</summary>
        public bool HoldingBall;
    }

    /// <summary>
    /// Procedural fielding poses for the shared <see cref="PlayerMannequin"/> (right-handed thrower, glove on the left hand):
    /// athletic ready stance, a run cycle, glove reach low/mid/high toward an authoritative target with the body sinking
    /// for low balls, and the possession hold. The run cycle's phase is the distance actually run divided by the stride, and
    /// a stance foot moves back in the figure frame exactly as fast as the root moves forward — the feet do not skate. The
    /// root itself is placed by gameplay (no root motion from animation).
    /// </summary>
    public static class FieldingPoser
    {
        /// <summary>One full cycle (two steps) at running speed (m); about 1.8 m per step (sprinting MLB players).</summary>
        public const float Stride = 3.6f;
        /// <summary>Distance covered with a foot on the ground per step (m): the rest of the cycle is the flight phase.</summary>
        public const float StanceLength = 0.85f;
        private const float StanceFraction = StanceLength / Stride;

        private static readonly MannequinPose Base = new MannequinPose();

        /// <summary>Glove (left wrist) position of the possession hold, figure frame.</summary>
        public static readonly Vector3 HoldGlove = new Vector3(-0.04f, 1.18f, 0.32f);

        public static void Compose(in FieldingPoseInput input, MannequinPose pose)
        {
            MannequinPose.Blend(Base, Base, 0f, pose);   // reset every field
            float run = Mathf.SmoothStep(0f, 1f, input.Speed / 3f);
            float phase = input.Distance / Stride;

            // Torso: athletic crouch at rest, forward lean when running.
            pose.PelvisOffset = new Vector3(0f, Mathf.Lerp(-0.12f, -0.07f, run), 0f);
            pose.Spine = new Vector3(Mathf.Lerp(22f, 14f, run), 0f, 0f);
            pose.Chest = new Vector3(Mathf.Lerp(0f, 4f, run), 0f, 0f);
            pose.Neck = new Vector3(Mathf.Lerp(-18f, -16f, run), 0f, 0f);

            // Legs (ankle IK). Ready: feet shoulder-width, knees out over the toes.
            Foot(phase, run, 0.19f, out Vector3 right, out float rightPitch);
            Foot(phase + 0.5f, run, -0.19f, out Vector3 left, out float leftPitch);
            pose.RightFootWeight = pose.LeftFootWeight = 1f;
            pose.RightFoot = right;
            pose.LeftFoot = left;
            pose.RightKneeHint = new Vector3(0.25f, 0f, 1f);
            pose.LeftKneeHint = new Vector3(-0.25f, 0f, 1f);
            pose.RightFootPitch = rightPitch;
            pose.LeftFootPitch = leftPitch;
            pose.RightFootYaw = Mathf.Lerp(12f, 0f, run);
            pose.LeftFootYaw = Mathf.Lerp(-12f, 0f, run);

            // Arms: hands in front (ready), pumping opposite to the legs (running).
            float swing = Mathf.Sin(2f * Mathf.PI * phase) * 55f * run;
            pose.RightUpperArm = Dir(Mathf.Lerp(15f, 8f, run), Mathf.Lerp(-55f, -70f, run) + swing);
            pose.RightForearm = Dir(Mathf.Lerp(-5f, 0f, run), Mathf.Lerp(-10f, 15f, run) + swing);
            pose.LeftUpperArm = Dir(Mathf.Lerp(-15f, -8f, run), Mathf.Lerp(-55f, -70f, run) - swing);
            pose.LeftForearm = Dir(Mathf.Lerp(5f, 0f, run), Mathf.Lerp(-10f, 15f, run) - swing);

            if (input.HoldingBall)
            {
                // Ball secured: glove and throwing hand together at the chest.
                pose.LeftHandWeight = pose.RightHandWeight = 1f;
                pose.LeftHand = HoldGlove;
                pose.RightHand = new Vector3(0.05f, 1.2f, 0.3f);
                pose.LeftElbowHint = new Vector3(-1f, -0.6f, 0f);
                pose.RightElbowHint = new Vector3(1f, -0.6f, 0f);
                return;
            }

            if (input.GloveWeight > 0f)
            {
                // Glove to the ball: low balls sink the body (knees and hips), high balls reach up.
                float w = Mathf.Clamp01(input.GloveWeight);
                // A ball on the ground in front of the feet needs a deep crouch with the back nearly flat (shoulder ≈ 0.65 m
                // high, ≈ 0.45 m forward), which brings it within the arm's ≈ 0.6 m (gameplay ground reach).
                float low = Mathf.Clamp01((0.95f - input.GloveTarget.y) / 0.85f) * w;
                pose.PelvisOffset += new Vector3(0f, -0.52f * low, -0.12f * low);
                pose.Spine += new Vector3(40f * low, 0f, 0f);
                pose.Chest += new Vector3(22f * low, 0f, 0f);
                pose.Neck += new Vector3(-30f * low, 0f, 0f);
                pose.LeftFoot += new Vector3(-0.12f * low, 0f, 0.25f * low);
                pose.RightFoot += new Vector3(0.12f * low, 0f, -0.1f * low);
                // Out to the side or above: shift the body toward the ball (a step and lean, up to 0.45 m) and rise into a
                // jump for balls above the standing reach (≈ 2.1 m for this figure), as a fielder does.
                var flat = new Vector2(input.GloveTarget.x + 0.2f, input.GloveTarget.z);
                float shift = Mathf.Clamp(flat.magnitude - 0.45f, 0f, 0.45f) * w;
                if (flat.sqrMagnitude > 1e-6f)
                {
                    Vector2 d = flat.normalized * shift;
                    pose.PelvisOffset += new Vector3(d.x, 0f, d.y);
                    pose.Spine += new Vector3(12f * d.y / 0.45f, 0f, -14f * d.x / 0.45f);
                }

                float jump = Mathf.Clamp(input.GloveTarget.y - 2.05f, 0f, 0.55f) * w;
                pose.PelvisOffset += new Vector3(0f, jump, 0f);
                pose.LeftFoot += new Vector3(0f, jump, 0f);
                pose.RightFoot += new Vector3(0f, jump, 0f);
                Vector3 rest = new Vector3(-0.25f, 0.9f, 0.35f);
                pose.LeftHandWeight = 1f;
                pose.LeftHand = Vector3.Lerp(rest, input.GloveTarget, w);
                pose.LeftElbowHint = new Vector3(-1f, input.GloveTarget.y > 1.4f ? 0.3f : -0.5f, 0f);
            }
        }

        /// <summary>
        /// Ankle target of one foot. Ready: beside the body. Running: during the stance part of the cycle the foot is on the
        /// ground and moves back exactly as the body moves forward (no skating); then it swings forward, lifted.
        /// </summary>
        private static void Foot(float phase, float run, float side, out Vector3 ankle, out float pitch)
        {
            var ready = new Vector3(side, PlayerMannequin.AnkleHeight, 0.02f);
            float p = phase - Mathf.Floor(phase);
            Vector3 running;
            if (p < StanceFraction)
            {
                float u = p / StanceFraction;
                running = new Vector3(side * 0.45f, PlayerMannequin.AnkleHeight, StanceLength * (0.5f - u));
                pitch = Mathf.Lerp(-5f, 25f, u);
            }
            else
            {
                float u = (p - StanceFraction) / (1f - StanceFraction);
                float z = StanceLength * (-0.5f + u) - 0.25f * Mathf.Sin(Mathf.PI * u);
                running = new Vector3(side * 0.45f, PlayerMannequin.AnkleHeight + 0.38f * Mathf.Sin(Mathf.PI * u), z);
                pitch = Mathf.Lerp(40f, -5f, u);
            }

            ankle = Vector3.Lerp(ready, running, run);
            pitch *= run;
        }
    }
}
