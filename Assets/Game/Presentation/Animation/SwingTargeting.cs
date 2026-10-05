using System;
using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// Bounded procedural deformation of the reference swing toward a contact target (presentation only). Figure frame of
    /// the batter: +X plate side (outside), +Y up, +Z toward the pitcher.
    /// </summary>
    public struct SwingAdjustment
    {
        /// <summary>Grip (hands) shift, figure frame (m).</summary>
        public Vector3 HandShift;
        /// <summary>Pelvis height change (m; − = knees bend for a low ball).</summary>
        public float PelvisDrop;
        /// <summary>Hips shifted toward the plate side (+X, m) or away; the feet stay planted.</summary>
        public float PelvisLean;
        /// <summary>Extra lateral trunk bend (degrees; − = toward the plate).</summary>
        public float SideBend;
        /// <summary>Extra forward trunk flexion (degrees).</summary>
        public float TrunkTilt;
        /// <summary>Barrel raised (+) or lowered (−) about the hands (degrees).</summary>
        public float BatPitch;
        /// <summary>Barrel turned about the vertical (degrees; + = toward the plate side, − = toward the pitcher).</summary>
        public float BatYaw;
        /// <summary>Whole-body rotation from timing (degrees; − = more open, early).</summary>
        public float BodyYaw;
    }

    /// <summary>
    /// Solves <see cref="SwingAdjustment"/> so the visual bat's sweet spot meets a world target at the contact marker of
    /// a reference swing. A bounded, damped least-squares fit (finite-difference Jacobian, a few iterations) over barrel
    /// direction, hand shift, side bend, trunk flexion, hip drop and hip lean, weighing: sweet-spot error; both hands staying
    /// on the bat and both ankles on their planted targets (limb reach is limited, so an unreachable grip or a hip shift the
    /// legs cannot follow shows as a gap and is penalised); and a small preference for the undeformed reference. Bounds keep
    /// every pose plausible; the feet targets are never moved,
    /// so a target beyond reach (a miss aimed far from the ball) is approached, not snapped to.
    /// </summary>
    public static class SwingTargeting
    {
        public const float MaxHandShift = 0.4f;   // hard cap; the solver is held below it by a soft penalty from 0.35 m
        private const int Parameters = 9, Residuals = 25;
        // Per parameter: scale (one unit of the solver variable) and bounds, in the field's own units.
        //   0 BatYaw°, 1 BatPitch°, 2–4 HandShift xyz (m), 5 SideBend°, 6 TrunkTilt°, 7 PelvisDrop (m), 8 PelvisLean (m)
        private static readonly float[] Scale = { 10f, 10f, 0.1f, 0.1f, 0.1f, 10f, 10f, 0.05f, 0.05f };
        private static readonly float[] Min = { -60f, -45f, -0.4f, -0.4f, -0.4f, -25f, -12f, -0.15f, -0.1f };
        private static readonly float[] Max = { 60f, 25f, 0.4f, 0.4f, 0.4f, 20f, 22f, 0.03f, 0.1f };
        private const float GripWeight = 6f;          // hands off the bat cost 6× a sweet-spot miss of the same size
        private const float FootWeight = 6f;          // a planted ankle pulled off its target (legs out of reach) costs 6×
        private const float Preference = 0.004f;      // metres of residual per solver unit of deformation

        public static SwingAdjustment Solve(PlayerMannequin batter, Transform sweetSpot, MotionClip swing, float uContact, Vector3 worldTarget,
            float bodyYaw, MannequinPose scratch, out float residual)
        {
            var x = new float[Parameters];
            var r = new float[Residuals];
            var trial = new float[Residuals];
            var jacobian = new float[Residuals, Parameters];
            var normal = new float[Parameters, Parameters + 1];
            var candidate = new float[Parameters];
            float cost = Evaluate(batter, sweetSpot, swing, uContact, worldTarget, bodyYaw, x, scratch, r);
            float damping = 0.01f;
            for (int iteration = 0; iteration < 20; iteration++)
            {
                for (int j = 0; j < Parameters; j++)
                {
                    float keep = x[j], h = 0.01f;
                    x[j] = keep + h;
                    Evaluate(batter, sweetSpot, swing, uContact, worldTarget, bodyYaw, x, scratch, trial);
                    x[j] = keep;
                    for (int i = 0; i < Residuals; i++) jacobian[i, j] = (trial[i] - r[i]) / h;
                }

                // (JᵀJ + λ diag) δ = −Jᵀr, solved by Gaussian elimination; accept only steps that reduce the cost.
                for (int a = 0; a < Parameters; a++)
                {
                    float g = 0f;
                    for (int i = 0; i < Residuals; i++) g += jacobian[i, a] * r[i];
                    normal[a, Parameters] = -g;
                    for (int b = 0; b < Parameters; b++)
                    {
                        float sum = 0f;
                        for (int i = 0; i < Residuals; i++) sum += jacobian[i, a] * jacobian[i, b];
                        normal[a, b] = sum;
                    }

                    normal[a, a] += damping * (1f + normal[a, a]);
                }

                float[] step = SolveLinear(normal);
                for (int j = 0; j < Parameters; j++) candidate[j] = Mathf.Clamp(x[j] + step[j], Min[j] / Scale[j], Max[j] / Scale[j]);
                float candidateCost = Evaluate(batter, sweetSpot, swing, uContact, worldTarget, bodyYaw, candidate, scratch, trial);
                if (candidateCost < cost)
                {
                    Array.Copy(candidate, x, Parameters);
                    Array.Copy(trial, r, Residuals);
                    cost = candidateCost;
                    damping = Mathf.Max(1e-4f, damping * 0.3f);
                }
                else if ((damping *= 10f) > 1e4f) break;   // no step improves any more
            }

            SwingAdjustment result = ToAdjustment(x, bodyYaw);
            residual = SweetSpotError(batter, sweetSpot, swing, uContact, worldTarget, result, scratch).magnitude;
            return result;
        }

        private static SwingAdjustment ToAdjustment(float[] x, float bodyYaw) => new SwingAdjustment
        {
            BatYaw = x[0] * Scale[0], BatPitch = x[1] * Scale[1],
            HandShift = Vector3.ClampMagnitude(new Vector3(x[2] * Scale[2], x[3] * Scale[3], x[4] * Scale[4]), MaxHandShift),
            SideBend = x[5] * Scale[5], TrunkTilt = x[6] * Scale[6], PelvisDrop = x[7] * Scale[7], PelvisLean = x[8] * Scale[8],
            BodyYaw = bodyYaw,
        };

        /// <summary>Residuals (sweet-spot error; weighted grip gaps; deformation preference) for parameters x; returns ½|r|².</summary>
        private static float Evaluate(PlayerMannequin batter, Transform sweetSpot, MotionClip swing, float u, Vector3 target, float bodyYaw,
            float[] x, MannequinPose pose, float[] r)
        {
            Vector3 miss = SweetSpotError(batter, sweetSpot, swing, u, target, ToAdjustment(x, bodyYaw), pose);
            Vector3 grip = batter.FigurePoint(pose.GripPoint), dir = batter.FigureToWorld(pose.GripDirection);
            Vector3 top = batter.WorldToFigure(batter.Joint(MannequinJoint.RightHand).position - grip) * GripWeight;
            Vector3 bottom = batter.WorldToFigure(batter.Joint(MannequinJoint.LeftHand).position - (grip - dir * 0.09f)) * GripWeight;
            r[0] = miss.x; r[1] = miss.y; r[2] = miss.z;
            r[3] = top.x; r[4] = top.y; r[5] = top.z;
            r[6] = bottom.x; r[7] = bottom.y; r[8] = bottom.z;
            for (int j = 0; j < Parameters; j++) r[9 + j] = Preference * x[j];
            float hand = new Vector3(x[2] * Scale[2], x[3] * Scale[3], x[4] * Scale[4]).magnitude;
            r[18] = 3f * Mathf.Max(0f, hand - 0.35f);   // smooth reach limit (the hard cap in ToAdjustment would stall the fit)
            Vector3 right = pose.RightFootWeight > 0f ? batter.WorldToFigure(batter.Joint(MannequinJoint.RightFoot).position - batter.FigurePoint(pose.RightFoot)) * FootWeight : Vector3.zero;
            Vector3 left = pose.LeftFootWeight > 0f ? batter.WorldToFigure(batter.Joint(MannequinJoint.LeftFoot).position - batter.FigurePoint(pose.LeftFoot)) * FootWeight : Vector3.zero;
            r[19] = right.x; r[20] = right.y; r[21] = right.z;
            r[22] = left.x; r[23] = left.y; r[24] = left.z;
            float cost = 0f;
            for (int i = 0; i < Residuals; i++) cost += r[i] * r[i];
            return 0.5f * cost;
        }

        private static Vector3 SweetSpotError(PlayerMannequin batter, Transform sweetSpot, MotionClip swing, float u, Vector3 target, in SwingAdjustment a, MannequinPose pose)
        {
            swing.Sample(u, pose);
            Apply(a, 1f, 1f, pose);
            batter.ApplyPose(pose);
            return batter.WorldToFigure(target - sweetSpot.position);
        }

        private static float[] SolveLinear(float[,] m)
        {
            int n = m.GetLength(0);
            for (int c = 0; c < n; c++)
            {
                int pivot = c;
                for (int i = c + 1; i < n; i++)
                    if (Mathf.Abs(m[i, c]) > Mathf.Abs(m[pivot, c])) pivot = i;
                for (int k = 0; k <= n; k++) (m[c, k], m[pivot, k]) = (m[pivot, k], m[c, k]);
                for (int i = c + 1; i < n; i++)
                {
                    float f = m[i, c] / m[c, c];
                    for (int k = c; k <= n; k++) m[i, k] -= f * m[c, k];
                }
            }

            var x = new float[n];
            for (int i = n - 1; i >= 0; i--)
            {
                float sum = m[i, n];
                for (int k = i + 1; k < n; k++) sum -= m[i, k] * x[k];
                x[i] = sum / m[i, i];
            }

            return x;
        }

        /// <summary>Adds the adjustment to a sampled pose: <paramref name="weight"/> for the contact deformation (ramps in
        /// over the swing), <paramref name="yawWeight"/> for the timing rotation.</summary>
        public static void Apply(in SwingAdjustment a, float weight, float yawWeight, MannequinPose pose)
        {
            pose.PelvisOffset.y += a.PelvisDrop * weight;
            pose.PelvisOffset.x += a.PelvisLean * weight;
            pose.Spine.z += 0.5f * a.SideBend * weight;
            pose.Chest.z += 0.5f * a.SideBend * weight;
            pose.Spine.x += 0.5f * a.TrunkTilt * weight;
            pose.Chest.x += 0.5f * a.TrunkTilt * weight;
            pose.Pelvis.y += a.BodyYaw * yawWeight;
            pose.Chest.y += 0.5f * a.BodyYaw * yawWeight;
            // Barrel direction as azimuth (+ toward the plate side, figure +X) and elevation (+ up); the change never tips the barrel past ±80°.
            Vector3 dir = pose.GripDirection.normalized;
            float azimuth = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + a.BatYaw * weight;
            float elevation0 = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float elevation = Mathf.Clamp(elevation0 + a.BatPitch * weight, Mathf.Min(elevation0, -80f), Mathf.Max(elevation0, 80f));
            pose.GripDirection = MannequinPose.Dir(azimuth, elevation);
            pose.GripPoint += a.HandShift * weight;
        }
    }
}
