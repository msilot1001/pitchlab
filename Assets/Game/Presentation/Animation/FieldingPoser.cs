using UnityEngine;
using static Pitchlab.Presentation.MannequinPose;

namespace Pitchlab.Presentation
{
    /// <summary>The body action around a take (mapped by the view from the gameplay classification; presentation decides nothing).</summary>
    public enum BodyAction
    {
        None,
        /// <summary>Glove to the ball, body as it is (standing or running catch, hop catch, wall play).</summary>
        Reach,
        /// <summary>Ground ball in the fielding triangle: centred, forehand (glove side), backhand (across), short hop.</summary>
        Pickup,
        Forehand,
        Backhand,
        ShortHop,
        /// <summary>Slow roller: low, one-hand pickup beside the glove-side foot, on the run.</summary>
        Charge,
        /// <summary>Feet-first sliding catch, then up.</summary>
        Slide,
        /// <summary>Layout dive, prone, then up.</summary>
        Dive,
        /// <summary>Jump for a high ball, land.</summary>
        Jump,
        /// <summary>Receiving a throw on a base: the glove-side foot stretches toward the throw.</summary>
        Stretch,
        /// <summary>Runner: head-first slide (a dive) to the base at <see cref="FieldingPoseInput.ActionPoint"/>, arms reaching it.</summary>
        HeadFirst,
    }

    /// <summary>Ready stance by position (parameterised: crouch depth and trunk angle).</summary>
    public enum ReadyStyle
    {
        Infield,
        CornerInfield,
        Outfield,
        Pitcher,
        Catcher,
        /// <summary>A runner leading off: wide, low, square to the plate, hands off the knees.</summary>
        RunnerLead,
        /// <summary>A runner standing on a base: upright, ready.</summary>
        RunnerStand,
    }

    /// <summary>What the fielder is doing this frame, all from gameplay state (presentation decides nothing).</summary>
    public struct FieldingPoseInput
    {
        public ReadyStyle Ready;
        /// <summary>The action around his take and the time from the take (s; − before it, NaN: none).</summary>
        public BodyAction Action;
        public float ActionTime;
        /// <summary>The ball at the take in the figure frame (where the body goes on a dive or slide).</summary>
        public Vector3 ActionPoint;
        /// <summary>How long the action keeps him down after the take (s; slides and dives get up after it).</summary>
        public float Recovery;
        /// <summary>How long before <see cref="ActionTime"/> = 0 the action starts (s; 0: the action's default) — a runner's slide
        /// starts when his authoritative braking starts.</summary>
        public float ActionLead;
        /// <summary>A runner (no glove): in a slide his hands go up, not to a glove.</summary>
        public bool Runner;
        /// <summary>Runner's secondary lead during the delivery (0–1): lower, weight toward the next base.</summary>
        public float Secondary;
        /// <summary>Tag (0–1): the glove (with the ball) sweeps to <see cref="TagTarget"/> (figure frame, the runner).</summary>
        public float TagWeight;
        public Vector3 TagTarget;
        /// <summary>Outfield crow hop before the throw (0–1 progress; 0 = none).</summary>
        public float CrowHop;
        /// <summary>Ground speed (m/s) and the gait phase (cycles of two steps; <see cref="MotionTrack"/>: ∫ cadence dt).</summary>
        public float Speed, GaitPhase;
        /// <summary>Direction of travel in the figure frame (x right, y = figure forward z); zero = forward. Running sideways
        /// or backward moves the stance feet along it (no skating).</summary>
        public Vector2 MoveDir;
        /// <summary>Acceleration along the travel direction (m/s²; − braking) and sideways (m/s², + toward the figure's right:
        /// turning or curving), for the body lean.</summary>
        public float Accel, LateralAccel;
        /// <summary>Glove (left hand) target in the figure frame, and how far the glove has gone to it (0–1).</summary>
        public Vector3 GloveTarget;
        public float GloveWeight;
        /// <summary>Ball in the glove: both hands together in front of the chest.</summary>
        public bool HoldingBall;
        /// <summary>Throwing motion (0–1 weight): throwing hand at <see cref="ThrowHand"/> (figure frame), torso turned by
        /// <see cref="ThrowTwist"/> degrees (− closed, + open), glove arm leading toward the target.</summary>
        public float ThrowWeight, ThrowTwist;
        public Vector3 ThrowHand;
        /// <summary>The throwing stride (0–1, monotone through the throw: the lead foot steps toward the target and stays
        /// planted through the release and the follow-through; it only comes back as he recovers).</summary>
        public float ThrowStride;
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
        /// <summary>Step cadence (steps/s) at ground speed <paramref name="v"/> (m/s): ≈ 2.2 walking, 2.6 jogging, 4.1 sprinting
        /// at 8 m/s (Docs/FIELDING_MOTION_REFERENCE.md). Step length = v / cadence (≈ 0.65 m, 1.15 m, 2.0 m).</summary>
        public static float Cadence(float v) => 1.8f + 0.28f * Mathf.Max(0f, v);

        public static float StepLength(float v) => v / Cadence(v);

        /// <summary>Fraction of a cycle each foot is on the ground: ≈ 0.6 walking, 0.28 sprinting.</summary>
        public static float DutyFactor(float v) => Mathf.Lerp(0.6f, 0.28f, Mathf.InverseLerp(1.5f, 8f, v));

        private static readonly MannequinPose Base = new MannequinPose();

        /// <summary>Glove (left wrist) position of the possession hold, figure frame.</summary>
        public static readonly Vector3 HoldGlove = new Vector3(-0.04f, 1.18f, 0.32f);

        /// <summary>Where the ball is held (glove at the chest), following the body when an action has taken it down (a slide,
        /// a dive): the hold moves with the pelvis and, prone, forward with the chest.</summary>
        public static Vector3 HoldPoint(in FieldingPoseInput input) => HoldGlove + BodyShift(input);

        /// <summary>How far a slide or dive has moved the upper body from the standing hold (figure frame).</summary>
        public static Vector3 BodyShift(in FieldingPoseInput input)
        {
            float t = input.ActionTime;
            if (float.IsNaN(t)) return Vector3.zero;
            switch (input.Action)
            {
                case BodyAction.Slide:
                {
                    float down = SlideDown(t, input.Recovery, Lead(input, SlideLead));
                    return new Vector3(0.05f * down, -0.55f * down, 0.1f * down);
                }
                case BodyAction.Dive:
                case BodyAction.HeadFirst:
                {
                    (float a, float height, Vector3 dir, float reach) = DiveShape(input);
                    return dir * ((reach + 0.35f) * a) + new Vector3(0f, height * a + 0.2f * a, 0f);
                }
                default:
                    return Vector3.zero;
            }
        }

        private static float SlideDown(float t, float recovery, float lead = SlideLead) =>
            Mathf.SmoothStep(0f, 1f, (t + lead) / lead) * (1f - Mathf.SmoothStep(0f, 1f, (t - recovery) / GetUp));

        private static float Lead(in FieldingPoseInput input, float fallback) => input.ActionLead > 0f ? input.ActionLead : fallback;

        /// <summary>The dive's weight, pelvis height offset, direction and forward reach at the input's time.</summary>
        private static (float A, float Height, Vector3 Dir, float Reach) DiveShape(in FieldingPoseInput input)
        {
            float t = input.ActionTime;
            Vector3 p = input.ActionPoint;
            float lead = Lead(input, DiveLead);
            float fly = Mathf.SmoothStep(0f, 1f, (t + lead) / lead);
            float land = Mathf.SmoothStep(0f, 1f, (t + (input.Action == BodyAction.HeadFirst ? 0.15f : 0f)) / 0.25f);   // head-first lands on the bag
            float up = Mathf.SmoothStep(0f, 1f, (t - input.Recovery) / GetUp);
            var toward = new Vector3(p.x, 0f, p.z);
            Vector3 dir = toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.forward;
            // Shoulder ≈ 0.5 m beyond the pelvis when horizontal, the arm ≈ 0.6 m more: the pelvis goes the rest.
            float reach = Mathf.Clamp(toward.magnitude - 1.05f, 0f, 1.4f);
            const float standing = 0.91f;   // pelvis height of the ready figure (m)
            float air = Mathf.Max(0.3f, p.y - 0.15f) - standing, prone = 0.3f - standing;
            return (fly * (1f - up), Mathf.Lerp(air, prone, land), dir, reach);
        }

        /// <summary>How long before the take the body starts into its action (s).</summary>
        public const float ActionLead = 0.45f;
        /// <summary>Slide: down this long before the catch (s); dive: take-off this long before it.</summary>
        public const float SlideLead = 0.35f, DiveLead = 0.3f;
        /// <summary>Getting up after a slide / dive (s).</summary>
        public const float GetUp = 0.5f;

        public static void Compose(in FieldingPoseInput input, MannequinPose pose)
        {
            ComposeCore(input, pose);
            // On his feet the pelvis never sinks below a full squat (a catcher's crouch plus a tag must not put a shin in the
            // ground); slides and dives are on the ground by design.
            bool grounded = input.Action == BodyAction.Slide || input.Action == BodyAction.Dive || input.Action == BodyAction.HeadFirst;
            if (!grounded || float.IsNaN(input.ActionTime)) pose.PelvisOffset.y = Mathf.Max(pose.PelvisOffset.y, SquatFloor);
        }

        private static void ComposeCore(in FieldingPoseInput input, MannequinPose pose)
        {
            MannequinPose.Blend(Base, Base, 0f, pose);   // reset every field
            float v = input.Speed;
            float run = Mathf.SmoothStep(0f, 1f, v / 1.5f);
            float phase = input.GaitPhase;
            Vector2 dir = input.MoveDir.sqrMagnitude > 1e-6f ? input.MoveDir.normalized : Vector2.up;

            // Torso: athletic crouch at rest, forward lean when running — more while accelerating, back and lower while
            // braking, into the turn when curving.
            float accel = Mathf.Clamp(input.Accel, -8f, 6f) * run;
            float brake = Mathf.Clamp01(-accel / 6f);
            float lateral = Mathf.Clamp(input.LateralAccel, -12f, 12f) * run;
            (float crouch, float trunk) = input.Ready switch
            {
                ReadyStyle.CornerInfield => (-0.17f, 26f),
                ReadyStyle.Outfield => (-0.07f, 14f),
                ReadyStyle.Pitcher => (-0.05f, 10f),
                ReadyStyle.Catcher => (-0.45f, 18f),
                ReadyStyle.RunnerLead => (-0.16f - 0.08f * input.Secondary, 24f + 6f * input.Secondary),
                ReadyStyle.RunnerStand => (-0.05f, 8f),
                _ => (-0.13f, 22f),
            };
            pose.PelvisOffset = new Vector3(0f, Mathf.Lerp(crouch, -0.07f, run) - 0.1f * brake, 0f);
            // Lean goes along the travel direction (forward running leans forward; a backpedal or shuffle leans that way less).
            float along = 12f * run + 1.6f * accel;
            pose.Spine = new Vector3(Mathf.Lerp(trunk, 8f, run) + along * dir.y, 0f, -along * dir.x * 0.6f - 1.2f * lateral);
            pose.Chest = new Vector3(Mathf.Lerp(0f, 4f, run), 0f, 0f);
            pose.Neck = new Vector3(Mathf.Lerp(-18f, -14f, run) - 0.6f * along * dir.y, 0f, 0f);

            // Legs (ankle IK). Ready: feet shoulder-width, knees out over the toes.
            Foot(phase, v, run, dir, 0.17f, out Vector3 right, out float rightPitch);
            Foot(phase + 0.5f, v, run, dir, -0.17f, out Vector3 left, out float leftPitch);
            pose.RightFootWeight = pose.LeftFootWeight = 1f;
            pose.RightFoot = right;
            pose.LeftFoot = left;
            pose.RightKneeHint = new Vector3(0.25f, 0f, 1f);
            pose.LeftKneeHint = new Vector3(-0.25f, 0f, 1f);
            pose.RightFootPitch = rightPitch;
            pose.LeftFootPitch = leftPitch;
            if (input.Ready == ReadyStyle.RunnerLead)
            {
                // Lead-off: feet well outside the shoulders (square to the plate), only while standing.
                float wide = 0.16f * (1f - run);
                pose.LeftFoot.x -= wide;
                pose.RightFoot.x += wide;
            }

            // Toes point along the travel direction when running (sideways shuffle: turned out), out a little at rest.
            float travelYaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            float toe = Mathf.Abs(travelYaw) > 100f ? 0f : Mathf.Clamp(travelYaw, -60f, 60f);   // backpedal: toes stay forward
            pose.RightFootYaw = Mathf.Lerp(12f, toe, run);
            pose.LeftFootYaw = Mathf.Lerp(-12f, toe, run);

            // Arms: hands in front (ready), pumping opposite to the legs (running), harder the faster he runs.
            float swing = Mathf.Sin(2f * Mathf.PI * phase) * Mathf.Lerp(30f, 60f, Mathf.InverseLerp(2f, 8f, v)) * run;
            pose.RightUpperArm = Dir(Mathf.Lerp(15f, 8f, run), Mathf.Lerp(-55f, -70f, run) + swing);
            pose.RightForearm = Dir(Mathf.Lerp(-5f, 0f, run), Mathf.Lerp(-10f, 15f, run) + swing);
            pose.LeftUpperArm = Dir(Mathf.Lerp(-15f, -8f, run), Mathf.Lerp(-55f, -70f, run) - swing);
            pose.LeftForearm = Dir(Mathf.Lerp(5f, 0f, run), Mathf.Lerp(-10f, 15f, run) - swing);

            bool bodyCrouch = ApplyAction(input, pose, run);
            if (input.CrowHop > 0f)
            {
                // Crow hop: a quick low hop, the back (throwing-side) foot passing behind the front one, landing into the stride.
                float h = Mathf.Sin(Mathf.PI * Mathf.Clamp01(input.CrowHop));
                pose.PelvisOffset += new Vector3(0f, 0.12f * h, 0.15f * input.CrowHop);
                pose.LeftFoot += new Vector3(0f, 0.12f * h, 0.2f * input.CrowHop);
                pose.RightFoot += new Vector3(-0.15f * h, 0.12f * h, 0.15f * input.CrowHop);
            }

            if (input.ThrowStride > 0f)
            {
                // The throwing stride: the lead (glove-side) foot steps toward the target, the back foot pushes off.
                float stride = Mathf.Clamp01(input.ThrowStride);
                pose.LeftFoot += new Vector3(0.05f * stride, 0.06f * Mathf.Sin(Mathf.PI * stride) * (1f - stride), 0.45f * stride);
                pose.RightFoot += new Vector3(0f, 0f, -0.15f * stride);
            }

            if (input.ThrowWeight > 0f)
            {
                // Throw: stride toward the target, hips and chest closed then whipping open, the throwing hand on the
                // authoritative ball path (before the release) or following through, the glove arm leading then tucking.
                float w = Mathf.Clamp01(input.ThrowWeight);
                pose.Pelvis += new Vector3(0f, 0.6f * input.ThrowTwist * w, 0f);
                pose.Chest += new Vector3(0f, 0.6f * input.ThrowTwist * w, 0f);
                pose.Spine += new Vector3(8f * w, 0f, 0f);
                // Blended from where the hands were (the hold at the chest while he has the ball; hands down in front after the
                // follow-through), so starting and ending the throw never snaps the arms.
                Vector3 hold = HoldPoint(input);
                Vector3 baseLeft = input.HoldingBall || input.ThrowTwist < 25f ? hold : new Vector3(-0.22f, 0.95f, 0.3f);
                Vector3 baseRight = input.HoldingBall || input.ThrowTwist < 25f ? hold + new Vector3(0.09f, 0.02f, -0.02f) : new Vector3(0.22f, 0.95f, 0.3f);
                pose.RightHandWeight = 1f;
                pose.RightHand = Vector3.Lerp(baseRight, input.ThrowHand, w);
                pose.RightElbowHint = Vector3.Slerp(new Vector3(1f, -0.6f, 0f), new Vector3(1f, 0.4f, -0.6f), w);
                pose.LeftHandWeight = 1f;
                Vector3 lead = Vector3.Lerp(new Vector3(-0.35f, 1.35f, 0.45f), new Vector3(-0.15f, 1.05f, 0.25f), Mathf.Clamp01(input.ThrowTwist / 30f + 0.5f));
                pose.LeftHand = Vector3.Lerp(baseLeft, lead, w);
                pose.LeftElbowHint = new Vector3(-1f, -0.3f, 0f);
                return;
            }

            if (input.TagWeight > 0f)
            {
                // Tag: the glove with the ball goes down to the runner (a sweep), the throwing hand covering it; body low.
                float w = Mathf.Clamp01(input.TagWeight);
                pose.PelvisOffset += new Vector3(0f, -0.25f * w, 0f);
                pose.Spine += new Vector3(30f * w, 0f, 0f);
                pose.LeftHandWeight = pose.RightHandWeight = 1f;
                pose.LeftHand = Vector3.Lerp(HoldGlove, input.TagTarget, w);
                pose.RightHand = Vector3.Lerp(new Vector3(0.05f, 1.2f, 0.3f), input.TagTarget + new Vector3(0.12f, 0.08f, -0.05f), w);
                pose.LeftElbowHint = new Vector3(-1f, -0.3f, 0f);
                pose.RightElbowHint = new Vector3(1f, -0.3f, 0f);
                return;
            }

            if (input.HoldingBall)
            {
                // Ball secured: glove and throwing hand together at the chest.
                pose.LeftHandWeight = pose.RightHandWeight = 1f;
                Vector3 hold = HoldPoint(input);
                pose.LeftHand = hold;
                pose.RightHand = hold + new Vector3(0.09f, 0.02f, -0.02f);
                pose.LeftElbowHint = new Vector3(-1f, -0.6f, 0f);
                pose.RightElbowHint = new Vector3(1f, -0.6f, 0f);
                return;
            }

            if (input.GloveWeight > 0f)
            {
                // Glove to the ball: low balls sink the body (knees and hips), high balls reach up.
                float w = Mathf.Clamp01(input.GloveWeight);
                // A ball on the ground in front of the feet needs a deep crouch with the back nearly flat (shoulder ≈ 0.65 m
                // high, ≈ 0.45 m forward), which brings it within the arm's ≈ 0.6 m (gameplay ground reach) — unless the action
                // has already shaped the body (pickups, slides, dives), and less when running (no splits).
                float low = bodyCrouch ? 0f : Mathf.Clamp01((0.95f - input.GloveTarget.y) / 0.85f) * w * Mathf.Lerp(1f, 0.45f, run);
                pose.PelvisOffset += new Vector3(0f, -0.52f * low, -0.12f * low);
                pose.Spine += new Vector3(40f * low, 0f, 0f);
                pose.Chest += new Vector3(22f * low, 0f, 0f);
                pose.Neck += new Vector3(-30f * low, 0f, 0f);
                pose.LeftFoot += new Vector3(-0.12f * low, 0f, 0.25f * low);
                pose.RightFoot += new Vector3(0.12f * low, 0f, -0.1f * low);
                // Out to the side or above: shift the body toward the ball (a step and lean, up to 0.45 m) and rise into a
                // jump for balls above the standing reach (≈ 2.1 m for this figure), as a fielder does.
                var flat = new Vector2(input.GloveTarget.x + 0.2f, input.GloveTarget.z);
                bool laidOut = input.Action == BodyAction.Dive;   // the dive placed the whole body toward the ball
                float shift = laidOut ? 0f : Mathf.Clamp(flat.magnitude - 0.45f, 0f, 0.45f) * w;
                if (flat.sqrMagnitude > 1e-6f)
                {
                    Vector2 d = flat.normalized * shift;
                    pose.PelvisOffset += new Vector3(d.x, 0f, d.y);
                    pose.Spine += new Vector3(12f * d.y / 0.45f, 0f, -14f * d.x / 0.45f);
                }

                float jump = input.Action == BodyAction.Jump || bodyCrouch ? 0f : Mathf.Clamp(input.GloveTarget.y - 2.05f, 0f, 0.55f) * w;
                pose.PelvisOffset += new Vector3(0f, jump, 0f);
                pose.LeftFoot += new Vector3(0f, jump, 0f);
                pose.RightFoot += new Vector3(0f, jump, 0f);
                Vector3 rest = new Vector3(-0.25f, 0.9f, 0.35f);
                pose.LeftHandWeight = 1f;
                pose.LeftHand = Vector3.Lerp(rest, input.GloveTarget, w);
                pose.LeftElbowHint = new Vector3(-1f, input.GloveTarget.y > 1.4f ? 0.3f : -0.5f, 0f);
                if (input.Action == BodyAction.Pickup || input.Action == BodyAction.ShortHop)
                {
                    // Ground ball: the bare hand comes down over the glove (two hands).
                    pose.RightHandWeight = 1f;
                    pose.RightHand = Vector3.Lerp(new Vector3(0.2f, 0.95f, 0.3f), input.GloveTarget + new Vector3(0.12f, 0.14f, -0.02f), w);
                    pose.RightElbowHint = new Vector3(1f, -0.5f, 0f);
                }
            }
        }

        /// <summary>The lowest the pelvis goes on its feet (offset, m): a full squat. Only slides and dives go below it.</summary>
        public const float SquatFloor = -0.72f;

        /// <summary>
        /// The body of the action around the take (before the glove reach, which still meets the authoritative ball): returns
        /// whether it shaped the crouch itself.
        /// </summary>
        private static bool ApplyAction(in FieldingPoseInput input, MannequinPose pose, float run)
        {
            float t = input.ActionTime;
            if (input.Action == BodyAction.None || input.Action == BodyAction.Reach || float.IsNaN(t)) return false;
            Vector3 p = input.ActionPoint;
            switch (input.Action)
            {
                case BodyAction.Pickup:
                case BodyAction.Forehand:
                case BodyAction.Backhand:
                case BodyAction.ShortHop:
                case BodyAction.Charge:
                {
                    // Into the fielding position over the last steps, out of it as the ball is secured and gathered.
                    float a = Mathf.SmoothStep(0f, 1f, (t + ActionLead) / ActionLead) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.3f));
                    if (a <= 0f) return false;
                    float moving = run;   // on the move the stride shortens and the crouch is shallower (no splits)
                    // Settled: down into the triangle. On the run: the stride goes on, so the body bends at the waist instead
                    // of sinking (a sinking pelvis on a 2 m stride splits the legs).
                    float depth = Mathf.Lerp(input.Action == BodyAction.Charge ? 0.32f : 0.45f, 0.22f, moving);
                    pose.PelvisOffset += new Vector3(0f, -depth * a, -0.08f * a);
                    pose.Spine += new Vector3(Mathf.Lerp(38f, 66f, moving) * a, 0f, 0f);
                    // On the run, around the pickup the legs pass through a lunge — glove-side foot out front, the other leg
                    // trailing — instead of a stride under lowered hips (which kneels the figure). Brief: it peaks at the take.
                    float lunge = moving * Mathf.Clamp01(1f - Mathf.Abs(t) / 0.22f);
                    // Backhand: the throwing-side leg leads across; forehand: a wide glove-side step.
                    bool backhand = input.Action == BodyAction.Backhand;
                    Vector3 front = backhand ? new Vector3(0.28f, PlayerMannequin.AnkleHeight, 0.6f)
                        : input.Action == BodyAction.Forehand ? new Vector3(-0.45f, PlayerMannequin.AnkleHeight, 0.5f)
                        : new Vector3(-0.2f, PlayerMannequin.AnkleHeight, 0.62f);
                    Vector3 back = backhand ? new Vector3(-0.22f, PlayerMannequin.AnkleHeight + 0.14f, -0.48f) : new Vector3(0.22f, PlayerMannequin.AnkleHeight + 0.14f, -0.48f);
                    if (backhand)
                    {
                        pose.RightFoot = Vector3.Lerp(pose.RightFoot, front, lunge);
                        pose.LeftFoot = Vector3.Lerp(pose.LeftFoot, back, lunge);
                        pose.LeftFootPitch = Mathf.Lerp(pose.LeftFootPitch, 45f, lunge);
                        pose.RightFootPitch = Mathf.Lerp(pose.RightFootPitch, 0f, lunge);
                    }
                    else
                    {
                        pose.LeftFoot = Vector3.Lerp(pose.LeftFoot, front, lunge);
                        pose.RightFoot = Vector3.Lerp(pose.RightFoot, back, lunge);
                        pose.RightFootPitch = Mathf.Lerp(pose.RightFootPitch, 45f, lunge);
                        pose.LeftFootPitch = Mathf.Lerp(pose.LeftFootPitch, 0f, lunge);
                    }
                    pose.Chest += new Vector3(15f * a, 0f, 0f);
                    pose.Neck += new Vector3(-28f * a, 0f, 0f);
                    // Feet: the fielding triangle (wide, glove-side foot a little forward) when he has settled; on the run the
                    // gait keeps going (its feet), only lower.
                    float settle = a * (1f - moving);
                    Vector3 leftTri = new Vector3(-0.36f, PlayerMannequin.AnkleHeight, 0.18f), rightTri = new Vector3(0.36f, PlayerMannequin.AnkleHeight, -0.08f);
                    if (input.Action == BodyAction.Backhand)
                    {
                        // Backhand: right leg forward across, glove side reaching over to the right.
                        leftTri = new Vector3(-0.2f, PlayerMannequin.AnkleHeight, -0.15f);
                        rightTri = new Vector3(0.45f, PlayerMannequin.AnkleHeight, 0.3f);
                        pose.Pelvis += new Vector3(0f, 35f * a, 0f);
                        pose.Chest += new Vector3(0f, 20f * a, 0f);
                    }
                    else if (input.Action == BodyAction.Forehand)
                    {
                        // Forehand: a long step to the glove side, body leaning over it.
                        leftTri = new Vector3(-0.6f, PlayerMannequin.AnkleHeight, 0.15f);
                        pose.Spine += new Vector3(0f, 0f, 12f * a);
                    }
                    else if (input.Action == BodyAction.ShortHop)
                        leftTri += new Vector3(0f, 0f, 0.12f);   // reaching forward to pick it at the bounce

                    pose.LeftFoot = Vector3.Lerp(pose.LeftFoot, leftTri, settle);
                    pose.RightFoot = Vector3.Lerp(pose.RightFoot, rightTri, settle);
                    pose.LeftFootPitch *= 1f - settle;
                    pose.RightFootPitch *= 1f - settle;
                    return true;
                }
                case BodyAction.Slide:
                {
                    // Feet-first: down onto the right hip, left leg out in front, back leaning; up again after the recovery.
                    float down = SlideDown(t, input.Recovery, Lead(input, SlideLead));
                    if (down <= 0f) return false;
                    if (input.Runner)
                    {
                        // A runner's feet-first slide: hands up off the ground, the bent trail leg under him (figure 4).
                        pose.LeftHandWeight = pose.RightHandWeight = down;
                        pose.LeftHand = new Vector3(-0.3f, 1.0f - 0.55f * down, 0.0f);
                        pose.RightHand = new Vector3(0.3f, 1.0f - 0.55f * down, 0.0f);
                        pose.LeftElbowHint = new Vector3(-1f, 0f, 0f);
                        pose.RightElbowHint = new Vector3(1f, 0f, 0f);
                    }

                    // A runner's lead foot reaches the bag (his gameplay position) as he arrives: the body slides in behind it.
                    float behind = input.Runner ? -0.75f * down : 0f;
                    pose.PelvisOffset += new Vector3(0.05f * down, -0.62f * down, behind);
                    pose.Spine += new Vector3(-28f * down, 0f, -10f * down);
                    pose.Neck += new Vector3(20f * down, 0f, 0f);
                    pose.LeftFoot = Vector3.Lerp(pose.LeftFoot, new Vector3(-0.1f, PlayerMannequin.AnkleHeight, 0.85f + behind), down);
                    pose.RightFoot = Vector3.Lerp(pose.RightFoot, new Vector3(0.2f, PlayerMannequin.AnkleHeight, 0.15f + behind), down);
                    pose.LeftFootPitch = Mathf.Lerp(pose.LeftFootPitch, -60f, down);
                    pose.RightKneeHint = Vector3.Lerp(pose.RightKneeHint, new Vector3(1f, 0.2f, 0f), down);
                    return true;
                }
                case BodyAction.Dive:
                case BodyAction.HeadFirst:
                {
                    // Layout toward the ball (a head-first slide: toward the base): launched horizontally, the body at the ball's height and stretched toward it at the
                    // catch (airborne), landing prone just after, staying down for the recovery, then pushing up.
                    (float a, float height, Vector3 dirXZ, float reach) = DiveShape(input);
                    if (a <= 0f) return false;
                    const float standing = 0.91f;
                    pose.PelvisOffset += dirXZ * (reach * a) + new Vector3(0f, height * a, 0f);
                    pose.Spine += new Vector3(82f * a, 0f, 0f);
                    pose.Chest += new Vector3(5f * a, 0f, 0f);
                    pose.Neck += new Vector3(-70f * a, 0f, 0f);
                    Vector3 legs = dirXZ * (reach * a) + new Vector3(0f, (standing + height) * a, 0f);
                    pose.LeftFoot = Vector3.Lerp(pose.LeftFoot, legs + new Vector3(-0.12f, -0.15f, -0.95f), a);
                    pose.RightFoot = Vector3.Lerp(pose.RightFoot, legs + new Vector3(0.12f, -0.1f, -1.0f), a);
                    pose.LeftFoot.y = Mathf.Max(pose.LeftFoot.y, PlayerMannequin.AnkleHeight);
                    pose.RightFoot.y = Mathf.Max(pose.RightFoot.y, PlayerMannequin.AnkleHeight);
                    pose.LeftFootPitch = Mathf.Lerp(pose.LeftFootPitch, 70f, a);
                    pose.RightFootPitch = Mathf.Lerp(pose.RightFootPitch, 70f, a);
                    pose.LeftKneeHint = pose.RightKneeHint = Vector3.Lerp(Vector3.forward, Vector3.down, a);
                    if (input.Action == BodyAction.Dive)
                    {
                        // The free hand stays with the glove (no run swing while laid out or prone).
                        pose.RightHandWeight = a;
                        pose.RightHand = HoldPoint(input) + new Vector3(0.12f, 0.05f, -0.05f);
                        pose.RightElbowHint = new Vector3(1f, 0.2f, 0f);
                    }

                    if (input.Action == BodyAction.HeadFirst)
                    {
                        // Arms out to the base.
                        Vector3 bag = new Vector3(p.x, 0.12f, p.z);
                        pose.LeftHandWeight = pose.RightHandWeight = a;
                        pose.LeftHand = bag + new Vector3(-0.12f, 0f, 0f);
                        pose.RightHand = bag + new Vector3(0.12f, 0f, 0f);
                        pose.LeftElbowHint = new Vector3(-1f, 0.5f, 0f);
                        pose.RightElbowHint = new Vector3(1f, 0.5f, 0f);
                    }

                    return true;
                }
                case BodyAction.Jump:
                {
                    // A two-foot jump timed to the catch, landing after it.
                    float h = Mathf.Clamp(p.y - 2.15f, 0.08f, 0.5f);
                    float u = (t + 0.3f) / 0.6f;
                    if (u <= 0f || u >= 1f) return false;
                    float lift = h * Mathf.Sin(Mathf.PI * u);
                    pose.PelvisOffset += new Vector3(0f, lift, 0f);
                    pose.LeftFoot += new Vector3(0f, lift, 0f);
                    pose.RightFoot += new Vector3(0f, lift, 0f);
                    pose.LeftFootPitch = pose.RightFootPitch = 30f * Mathf.Sin(Mathf.PI * u);
                    return true;
                }
                case BodyAction.Stretch:
                {
                    // On the bag: the glove-side foot strides toward the throw, chest over the front knee; back out of it after.
                    float a = Mathf.SmoothStep(0f, 1f, (t + 0.3f) / 0.3f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.35f));
                    if (a <= 0f) return false;
                    Vector3 toward = new Vector3(p.x, 0f, Mathf.Max(0.2f, p.z));
                    toward = toward.normalized * Mathf.Clamp(toward.magnitude, 0.3f, 0.75f);
                    pose.LeftFoot = Vector3.Lerp(pose.LeftFoot, new Vector3(toward.x - 0.1f, PlayerMannequin.AnkleHeight, toward.z), a);
                    pose.RightFoot = Vector3.Lerp(pose.RightFoot, new Vector3(0.15f, PlayerMannequin.AnkleHeight, -0.1f), a);
                    pose.PelvisOffset += new Vector3(0f, -0.15f * a, 0.15f * a);
                    pose.Spine += new Vector3(15f * a, 0f, 0f);
                    return false;   // the glove reach still adds its low/high shaping
                }
            }

            return false;
        }

        /// <summary>
        /// Ankle target of one foot. Ready: beside the body. Moving: during the stance part of the cycle (the duty factor) the
        /// foot is on the ground and moves back along the travel direction exactly as fast as the body moves (no skating:
        /// stance length 2 × duty × step); then it swings forward, lifted higher the faster he runs.
        /// </summary>
        private static void Foot(float phase, float v, float run, Vector2 dir, float side, out Vector3 ankle, out float pitch)
        {
            var ready = new Vector3(side * 1.12f, PlayerMannequin.AnkleHeight, 0.02f);
            float p = phase - Mathf.Floor(phase);
            float duty = DutyFactor(v), stance = 2f * duty * StepLength(v);
            float lift = 0.08f + 0.04f * v;
            // Feet beside the line of travel (perpendicular to it), so a sideways shuffle steps across rather than through.
            var across = new Vector2(dir.y, -dir.x);
            float s, h;
            if (p < duty)
            {
                float u = p / duty;
                s = stance * (0.5f - u);
                h = 0f;
                pitch = Mathf.Lerp(-5f, 25f, u);
            }
            else
            {
                float u = (p - duty) / (1f - duty);
                s = stance * (-0.5f + u) - 0.25f * Mathf.Sin(Mathf.PI * u) * Mathf.InverseLerp(0f, 8f, v);
                h = lift * Mathf.Sin(Mathf.PI * u);
                pitch = Mathf.Lerp(40f, -5f, u) * Mathf.InverseLerp(1f, 6f, v);
            }

            Vector2 flat = across * side * 0.75f + dir * s;
            var running = new Vector3(flat.x, PlayerMannequin.AnkleHeight + h, flat.y);
            ankle = Vector3.Lerp(ready, running, run);
            pitch *= run;
        }
    }
}
