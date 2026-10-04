using UnityEngine;
using static Pitchlab.Presentation.MannequinPose;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// Reference baseball motions (Docs/MOTION_REFERENCE.md), hand-authored to match a few public measurements: a
    /// right-handed swing whose bat path near contact is constrained by a 2024 MLB hitter's Statcast bat-tracking averages
    /// (body keys from Fortenbaugh 2011 norms and still photos), and a right-handed power-pitcher delivery fitted to the
    /// authoritative release (2024 Statcast release data, Fleisig/ASMI norms, still photos). Generic motions — no likeness. Keys are authored in game-speed seconds relative to the event (contact / release) and
    /// normalized to u ∈ [0, 1]; positions are metres in the figure frame, limbs by IK targets so planted feet stay put.
    /// </summary>
    public static class ReferenceMotions
    {
        // ---- Swing: figure faces the PITCHER (+Z), plate on the right (+X), catcher behind (−Z). Root = stance hip
        // midpoint on the ground. Times relative to contact (s); launch = −0.15 s matches the gameplay swing duration.
        public const float SwingStart = -1.0f, SwingEnd = 0.6f;

        public static float SwingU(float secondsFromContact) => (secondsFromContact - SwingStart) / (SwingEnd - SwingStart);

        // Ankle targets (y = ankle height above the ground). Stance: feet 0.74 m apart (Savant stance width).
        private static readonly Vector3 RearFoot = new Vector3(-0.02f, PlayerMannequin.AnkleHeight, -0.37f);
        private static readonly Vector3 FrontStance = new Vector3(0.02f, PlayerMannequin.AnkleHeight, 0.37f);
        private static readonly Vector3 FrontPlant = new Vector3(0.07f, PlayerMannequin.AnkleHeight, 0.62f);

        // ---- Bat-path constraints (Baseball Savant bat tracking, MLB reference hitter, 2024 season averages; MEASURED).
        public const float RefBatSpeedMph = 68.93f, RefSwingLengthFeet = 6.91f, RefSwingPathTilt = 34.0f, RefAttackAngle = 8.86f;
        /// <summary>Attack direction, + = toward first base (opposite field for a right-handed hitter). Savant: −4.18° with
        /// negative = opposite field (sign convention from FanGraphs; Savant's CSV docs do not state it).</summary>
        public const float RefAttackDirection = 4.18f;
        /// <summary>Middle-middle contact point in the swing figure frame: ≈ 0.88 m in front of the stance hips (Savant intercept
        /// 33 in vs the hips' midpoint; DERIVED with the game's contact plane), over the plate's centre, mid-zone height.</summary>
        public static readonly Vector3 RefContact = new Vector3(0.92f, 0.78f, 0.88f);
        private const float LaunchTime = -0.15f, BatGrip = Equipment.SweetSpotFromGrip;
        /// <summary>
        /// Sweet-spot path length of the reference arc from launch to contact (DERIVED): Statcast swing length is the bat
        /// HEAD's path, which is longer because the bat also turns about the hands; this value is chosen so the animated
        /// head path equals <see cref="RefSwingLengthFeet"/> (checked by ReferenceMotionTests).
        /// </summary>
        public const float SweetSpotArcFeet = 5.8f;

        /// <summary>
        /// Reference sweet-spot path near contact: an arc in a plane tilted <see cref="RefSwingPathTilt"/> from the ground,
        /// passing <see cref="RefContact"/> at time 0 with the reference attack angle/direction and bat speed; speed grows
        /// from 0 at launch as (Δt)^k with k set so the launch→contact path equals the reference swing length.
        /// </summary>
        public static Vector3 ReferenceSweetSpot(float t, out Vector3 radial, out Vector3 velocityDirection)
        {
            float a = RefAttackAngle * Mathf.Deg2Rad, d = RefAttackDirection * Mathf.Deg2Rad;
            Vector3 v = new Vector3(Mathf.Sin(d) * Mathf.Cos(a), Mathf.Sin(a), Mathf.Cos(d) * Mathf.Cos(a));
            Vector3 wh = Vector3.Cross(v, Vector3.up).normalized;               // horizontal, toward the batter (−X)
            Vector3 w = InPlaneInward(v, wh);
            const float radius = 1.1f;
            float speed = RefBatSpeedMph * 0.44704f, length = SweetSpotArcFeet * 0.3048f, swing = -LaunchTime;
            float k = speed * swing / length - 1f;                              // ∫ speed·(τ/T)^k dτ over T = length
            float s = t <= 0f
                ? -(length - speed * swing / (k + 1f) * Mathf.Pow(Mathf.Clamp01((t - LaunchTime) / swing), k + 1f))
                : speed * (t - 0.5f * t * t / 0.25f);                            // decelerating after contact
            float phi = s / radius;
            Vector3 centre = RefContact + radius * w;
            radial = Mathf.Cos(phi) * -w + Mathf.Sin(phi) * v;                    // outward from the swing centre
            velocityDirection = Mathf.Cos(phi) * v + Mathf.Sin(phi) * w;
            return centre + radius * radial;
        }

        // Tilts the horizontal inward direction up about v until the swing plane's tilt equals the reference.
        private static Vector3 InPlaneInward(Vector3 v, Vector3 wh)
        {
            float lo = 0f, hi = 89f;
            Vector3 w = wh;
            for (int i = 0; i < 40; i++)
            {
                float mid = 0.5f * (lo + hi);
                w = Quaternion.AngleAxis(-mid, v) * wh;
                if (w.y < 0f) w = Quaternion.AngleAxis(mid, v) * wh;
                float tilt = Vector3.Angle(Vector3.Cross(v, w), Vector3.up);
                if (tilt > 90f) tilt = 180f - tilt;
                if (tilt < RefSwingPathTilt) lo = mid; else hi = mid;
            }

            return w;
        }

        /// <summary>Grip (hands) and bat direction on the reference path at time t, with a bat lag (−) / lead (+) angle.</summary>
        private static (Vector3 Hands, Vector3 Bat) ReferenceGrip(float t, float leadDegrees)
        {
            Vector3 spot = ReferenceSweetSpot(t, out Vector3 radial, out Vector3 vel);
            float l = leadDegrees * Mathf.Deg2Rad;
            Vector3 bat = (Mathf.Cos(l) * radial + Mathf.Sin(l) * vel).normalized;
            return (spot - BatGrip * bat, bat);
        }

        private static MannequinPose OnPath(MannequinPose pose, float t, float leadDegrees)
        {
            (pose.GripPoint, pose.GripDirection) = ReferenceGrip(t, leadDegrees);
            return pose;
        }

        public static MotionClip ReferenceRightHandedSwing()
        {
            // Body keys (pelvis/trunk timing after Fortenbaugh 2011: max counter-rotation at plant, pelvis leads, trunk lags
            // ≈ 18° at launch, pelvis ≈ 77° open at contact, trunk ≈ 135° by the finish). Hands/bat before launch are
            // authored; from launch to just after contact they ride the Statcast-constrained reference path.
            MannequinPose stance = Bat(90f, new Vector3(0f, -0.13f, 0f), 22f, 0f, -80f, FrontStance, 75f, 90f, new Vector3(0.24f, 1.38f, -0.06f), Dir(190f, 62f));
            MannequinPose quiet = Bat(90f, new Vector3(0f, -0.13f, -0.01f), 22f, 0f, -80f, FrontStance, 75f, 90f, new Vector3(0.23f, 1.39f, -0.08f), Dir(192f, 60f));
            MannequinPose loadStart = Bat(96f, new Vector3(0f, -0.135f, -0.04f), 22f, 2f, -84f, FrontStance, 75f, 90f, new Vector3(0.22f, 1.4f, -0.13f), Dir(198f, 57f));
            MannequinPose rearHipLoad = Bat(104f, new Vector3(0f, -0.14f, -0.07f), 22f, 3f, -90f, new Vector3(0.02f, 0.14f, 0.3f), 70f, 90f,
                new Vector3(0.21f, 1.41f, -0.18f), Dir(205f, 53f), frontKnee: new Vector3(0.6f, 0.2f, 0.4f));
            MannequinPose legLift = Bat(108f, new Vector3(0f, -0.14f, -0.08f), 22f, 3f, -94f, new Vector3(0.03f, 0.24f, 0.24f), 70f, 90f,
                new Vector3(0.2f, 1.41f, -0.2f), Dir(210f, 50f), frontKnee: new Vector3(0.7f, 0.4f, 0.4f));
            MannequinPose stride = Bat(109f, new Vector3(0f, -0.15f, 0f), 21f, 2f, -95f, new Vector3(0.06f, 0.14f, 0.52f), 65f, 90f,
                new Vector3(0.19f, 1.38f, -0.2f), Dir(225f, 40f), frontKnee: new Vector3(0.6f, 0.1f, 0.5f));
            MannequinPose plant = Bat(111f, new Vector3(0f, -0.17f, 0.07f), 20f, 1f, -97f, FrontPlant, 60f, 90f, new Vector3(0.18f, 1.36f, -0.19f), Dir(250f, 22f));
            MannequinPose hipsFire = Bat(103f, new Vector3(0f, -0.17f, 0.09f), 20f, 9f, -95f, FrontPlant, 60f, 88f, new Vector3(0.18f, 1.34f, -0.18f), Dir(260f, 15f));
            // From launch the grip comes from the reference path (the hands/bat values here are replaced).
            MannequinPose launch = Bat(96f, new Vector3(0f, -0.17f, 0.1f), 20f, 14f, -90f, FrontPlant, 60f, 85f, Vector3.zero, Vector3.up);
            MannequinPose zone = Bat(50f, new Vector3(0.03f, -0.15f, 0.16f), 22f, 12f, -50f, FrontPlant, 60f, 55f, Vector3.zero, Vector3.up, rearPitch: 20f, lean: -12f);
            MannequinPose contact = Bat(13f, new Vector3(0.07f, -0.16f, 0.22f), 26f, 6f, -22f, FrontPlant, 60f, 30f, Vector3.zero, Vector3.up, rearPitch: 40f, lean: -22f);
            MannequinPose postContact = Bat(4f, new Vector3(0.12f, -0.16f, 0.26f), 26f, 12f, -18f, FrontPlant, 60f, 25f, Vector3.zero, Vector3.up, rearPitch: 45f, lean: -26f);
            MannequinPose extension = Bat(0f, new Vector3(0.06f, -0.15f, 0.24f), 20f, 0f, -14f, FrontPlant, 60f, 20f, new Vector3(0.22f, 1.16f, 0.82f), Dir(0f, 2f), rearPitch: 50f, lean: -12f);
            MannequinPose followThrough = Bat(-5f, new Vector3(0f, -0.15f, 0.22f), 12f, -32f, 0f, FrontPlant, 60f, 10f, new Vector3(-0.24f, 1.36f, 0.5f), Dir(-80f, 18f), rearPitch: 60f, lean: -6f);
            MannequinPose finish = Bat(-8f, new Vector3(0f, -0.14f, 0.21f), 8f, -40f, 10f, FrontPlant, 60f, 0f, new Vector3(-0.28f, 1.58f, 0.08f), Dir(-160f, 22f), rearPitch: 62f, lean: -4f);

            MannequinPose Between(MannequinPose a, MannequinPose b, float f)
            {
                var pose = new MannequinPose();
                MannequinPose.Blend(a, b, f, pose);
                return pose;
            }

            return new MotionClip("ReferenceRightHandedSwing",
                ("stance", SwingU(-1.0f), stance),
                ("quiet", SwingU(-0.72f), quiet),
                ("loadStart", SwingU(-0.62f), loadStart),
                ("rearHipLoad", SwingU(-0.5f), rearHipLoad),
                ("legLift", SwingU(-0.42f), legLift),
                ("stride", SwingU(-0.3f), stride),
                ("plant", SwingU(-0.2f), plant),
                ("hipsFire", SwingU(-0.17f), hipsFire),
                ("launch", SwingU(-0.15f), OnPath(launch, -0.15f, -70f)),
                ("handsDrive", SwingU(-0.11f), OnPath(Between(launch, zone, 0.45f), -0.11f, -75f)),
                ("approach", SwingU(-0.08f), OnPath(Between(launch, zone, 0.8f), -0.08f, -60f)),
                ("zone", SwingU(-0.06f), OnPath(zone, -0.06f, -45f)),
                ("barrelIn", SwingU(-0.04f), OnPath(Between(zone, contact, 0.4f), -0.04f, -30f)),
                ("preContact", SwingU(-0.02f), OnPath(Between(zone, contact, 0.7f), -0.02f, -12f)),
                ("contact", SwingU(0f), OnPath(contact, 0f, 8f)),
                ("postContact", SwingU(0.02f), OnPath(postContact, 0.02f, 40f)),
                ("extension", SwingU(0.06f), extension),
                ("followThrough", SwingU(0.16f), followThrough),
                ("finish", SwingU(0.45f), finish));
        }

        /// <param name="pelvisYaw">Pelvis yaw in the figure frame (90 = facing the plate, 0 = facing the pitcher).</param>
        /// <param name="chestYaw">Chest yaw relative to the pelvis (+ = trunk lags behind / stays closed).</param>
        /// <param name="lean">Lateral trunk bend (− = toward the plate).</param>
        /// <param name="rearPitch">Rear heel lift (degrees) as the back foot pivots.</param>
        private static MannequinPose Bat(float pelvisYaw, Vector3 pelvis, float spineTilt, float chestYaw, float neckYaw,
            Vector3 front, float frontYaw, float rearYaw, Vector3 hands, Vector3 bat, Vector3? frontKnee = null, float rearPitch = 0f, float lean = 0f)
        {
            // Rear foot: planted until launch, then the heel lifts and the foot pivots on its toe (ankle rises a little).
            float lift = Mathf.Sin(rearPitch * Mathf.Deg2Rad) * 0.16f;
            return new MannequinPose
            {
                PelvisOffset = pelvis, Pelvis = new Vector3(0f, pelvisYaw, 0f),
                Spine = new Vector3(spineTilt * 0.5f, 0f, lean * 0.5f), Chest = new Vector3(spineTilt * 0.5f, chestYaw, lean * 0.5f),
                Neck = new Vector3(12f, neckYaw - chestYaw, 0f),
                RightFootWeight = 1f, RightFoot = RearFoot + new Vector3(0f, lift, lift * 0.6f), RightKneeHint = new Vector3(1f, 0f, 0.6f),
                RightFootYaw = rearYaw, RightFootPitch = rearPitch,
                LeftFootWeight = 1f, LeftFoot = front, LeftKneeHint = frontKnee ?? new Vector3(0.6f, 0f, 0.8f), LeftFootYaw = frontYaw,
                GripWeight = 1f, GripPoint = hands, GripDirection = bat,
            };
        }

        // ---- Delivery: figure faces HOME (+Z), right (throwing) side = +X. Root = the rubber's front centre on the mound
        // top. Times relative to release (s). Release hand ≈ the authoritative release point (set per pitch by the glue).
        public const float DeliveryStart = -1.1f, DeliveryEnd = 1.0f;

        public static float DeliveryU(float secondsFromRelease) => (secondsFromRelease - DeliveryStart) / (DeliveryEnd - DeliveryStart);

        /// <summary>Stride: lead ankle at foot contact, 83 % of height ahead (Fleisig 2010), slightly closed; y on the mound slope.</summary>
        public static Vector3 FootPlant(float moundDrop) => new Vector3(0.1f, PlayerMannequin.AnkleHeight - moundDrop, 1.54f);

        private static readonly Vector3 Pivot = new Vector3(0.05f, PlayerMannequin.AnkleHeight, 0.08f);

        /// <param name="release">Release point in the figure frame (from the authoritative simulation).</param>
        /// <param name="moundDrop">How much lower the landing spot is than the rubber (m).</param>
        /// <param name="handCorrection">Figure-frame correction of the release wrist target (m), found by the caller's
        /// fit so the ball itself (not the wrist) is at the release point.</param>
        public static MotionClip ReferenceRightHandedPitchDelivery(Vector3 release, float moundDrop, Vector3 handCorrection = default)
        {
            Vector3 plant = FootPlant(moundDrop);
            // The release point is the ball; the IK target is the wrist, ≈ 0.13 m back toward the shoulder (whose release
            // position is ≈ midline, 0.44 m below the ball and ≈ 0.3 m behind it — 2024 Statcast arm-angle data, scaled).
            Vector3 shoulder = new Vector3(0.05f, release.y - 0.44f, release.z - 0.3f);
            Vector3 wrist = release - 0.13f * (release - shoulder).normalized + handCorrection;
            Vector3 drop = new Vector3(0f, -moundDrop, 0f);
            return new MotionClip("ReferenceRightHandedPitchDelivery",
                ("set", DeliveryU(-1.1f), Pitch(90f, new Vector3(0f, -0.08f, 0f), 0f, 0f, 0f, 0f, -88f, Pivot, new Vector3(0.0f, 0.06f, 0.42f), 90f, 90f,
                    right: new Vector3(0.25f, 1.2f, 0.04f), rightElbow: new Vector3(0.3f, -1f, -0.3f), left: new Vector3(0.27f, 1.23f, 0.12f), leftElbow: new Vector3(0.3f, -1f, 0.4f))),
                ("initialMove", DeliveryU(-0.95f), Pitch(92f, new Vector3(0f, -0.08f, -0.02f), 0f, 0f, 0f, 0f, -90f, Pivot, new Vector3(0.05f, 0.1f, 0.36f), 85f, 90f,
                    new Vector3(0.25f, 1.24f, 0.03f), new Vector3(0.3f, -1f, -0.3f), new Vector3(0.27f, 1.27f, 0.11f), new Vector3(0.3f, -1f, 0.4f))),
                ("liftStart", DeliveryU(-0.8f), Pitch(97f, new Vector3(0f, -0.08f, -0.03f), 0f, 0f, 0f, 0f, -95f, Pivot, new Vector3(0.12f, 0.32f, 0.22f), 80f, 90f,
                    new Vector3(0.24f, 1.28f, 0.02f), new Vector3(0.3f, -1f, -0.3f), new Vector3(0.26f, 1.31f, 0.1f), new Vector3(0.3f, -1f, 0.4f), leftKnee: new Vector3(1f, 0.5f, 0.2f))),
                ("liftPeak", DeliveryU(-0.62f), Pitch(102f, new Vector3(0f, -0.08f, -0.02f), -3f, 0f, 0f, 0f, -100f, Pivot, new Vector3(0.22f, 0.5f, 0.12f), 75f, 90f,
                    new Vector3(0.23f, 1.3f, 0.02f), new Vector3(0.3f, -1f, -0.3f), new Vector3(0.25f, 1.33f, 0.09f), new Vector3(0.3f, -1f, 0.4f), leftKnee: new Vector3(1f, 0.8f, 0.1f))),
                ("drift", DeliveryU(-0.45f), Pitch(98f, new Vector3(0f, -0.1f, 0.22f), 0f, 0f, 0f, 0f, -96f, Pivot, new Vector3(0.18f, 0.36f, 0.62f), 70f, 90f,
                    new Vector3(0.22f, 1.24f, 0.18f), new Vector3(0.3f, -1f, -0.3f), new Vector3(0.24f, 1.27f, 0.26f), new Vector3(0.3f, -1f, 0.4f), leftKnee: new Vector3(1f, 0.5f, 0.5f))),
                ("handBreak", DeliveryU(-0.33f), Pitch(92f, new Vector3(0f, -0.16f, 0.42f), 3f, 0f, 0f, 0f, -92f, Pivot, new Vector3(0.14f, 0.22f, 1.05f), 40f, 90f,
                    new Vector3(0.4f, 1.0f, -0.04f), new Vector3(0.6f, -0.3f, -0.6f), new Vector3(0.1f, 1.3f, 0.75f), new Vector3(0.6f, -0.6f, 0.3f), leftKnee: new Vector3(0.6f, 0.3f, 1f))),
                ("footApproach", DeliveryU(-0.22f), Pitch(78f, new Vector3(0f, -0.21f, 0.62f), 4f, 20f, 0f, 0f, -80f, Pivot, plant + new Vector3(0f, 0.08f, -0.12f), 15f, 90f,
                    new Vector3(0.55f, 1.25f, 0.08f), new Vector3(1f, -0.3f, -0.4f), new Vector3(-0.02f, 1.32f, 1.05f), new Vector3(0.4f, -1f, 0.4f), leftKnee: new Vector3(0.2f, 0.3f, 1f))),
                ("footPlant", DeliveryU(-0.15f), Pitch(57f, new Vector3(0f, -0.33f, 0.96f), 5f, 48f, 0f, 0f, -55f, Pivot + new Vector3(0f, 0.02f, 0.02f), plant, 15f, 75f,
                    new Vector3(0.52f, 1.36f, 0.58f), new Vector3(1f, -0.2f, -0.3f), new Vector3(-0.06f, 1.32f, 1.48f), new Vector3(0.2f, -1f, 0.5f), rightPitch: 20f)),
                ("armCock", DeliveryU(-0.1f), Pitch(30f, new Vector3(0f, -0.3f, 1.05f), 12f, 28f, 0f, 5f, -30f, Pivot + new Vector3(0.0f, 0.06f, 0.12f), plant, 15f, 55f,
                    new Vector3(0.56f, 1.54f, 0.74f), new Vector3(1f, 0.2f, 0.2f), new Vector3(-0.14f, 1.2f, 1.48f), new Vector3(0.1f, -1f, 0f), rightPitch: 40f)),
                ("maxExternalRotation", DeliveryU(-0.04f), Pitch(8f, new Vector3(0f, -0.31f, 1.22f), 26f, 10f, 0f, 14f, -10f, Pivot + new Vector3(0.0f, 0.1f, 0.28f), plant, 15f, 35f,
                    new Vector3(0.48f, 1.5f, 0.95f), new Vector3(0.6f, 0.4f, 1f), new Vector3(-0.17f, 1.12f, 1.6f), new Vector3(0.1f, -1f, 0f), rightPitch: 55f)),
                ("release", DeliveryU(0f), Pitch(-5f, new Vector3(0f, -0.3f, 1.4f), 40f, -10f, 0f, 23f, 0f, Pivot + new Vector3(0.0f, 0.13f, 0.38f), plant, 15f, 25f,
                    wrist, new Vector3(1f, -0.6f, -0.2f), new Vector3(-0.21f, 1.04f, 1.64f), new Vector3(0.1f, -1f, 0f), rightPitch: 60f)),
                ("deceleration", DeliveryU(0.05f), Pitch(-15f, new Vector3(0f, -0.3f, 1.43f), 48f, -18f, 0f, 20f, 10f, Pivot + new Vector3(0.0f, 0.16f, 0.45f), plant, 15f, 15f,
                    new Vector3(0.0f, 1.05f, 2.02f), new Vector3(1f, -0.5f, 0f), new Vector3(-0.23f, 1.0f, 1.66f), new Vector3(0.1f, -1f, 0f), rightPitch: 65f)),
                ("torsoFollowThrough", DeliveryU(0.2f), Pitch(-28f, new Vector3(0f, -0.32f, 1.36f), 52f, -20f, 0f, 12f, 20f, Pivot + new Vector3(0.05f, 0.25f, 0.6f), plant, 15f, 0f,
                    new Vector3(-0.42f, 0.62f, 1.56f), new Vector3(0.6f, 0f, 0.6f), new Vector3(-0.34f, 1.0f, 1.66f), new Vector3(0f, -1f, -0.4f), rightPitch: 70f, rightKnee: new Vector3(0.3f, 0f, 1f))),
                ("backLegFollowThrough", DeliveryU(0.4f), Pitch(-20f, new Vector3(0f, -0.28f, 1.4f), 40f, -15f, 0f, 6f, 15f, new Vector3(0.38f, 0.32f, 1.25f) + drop, plant, 15f, 0f,
                    new Vector3(-0.4f, 0.7f, 1.6f), new Vector3(0.6f, 0f, 0.6f), new Vector3(-0.3f, 1.0f, 1.7f), new Vector3(0f, -1f, -0.4f), rightPitch: 30f, rightKnee: new Vector3(0.3f, 0.3f, 1f))),
                ("recovery", DeliveryU(1.0f), Pitch(0f, new Vector3(0.22f, -0.14f, 1.45f), 15f, 0f, 0f, 0f, 0f, new Vector3(0.48f, 0.06f, 1.4f) + drop, plant, 5f, -5f,
                    new Vector3(0.28f, 0.95f, 1.75f), new Vector3(0.6f, -1f, 0.2f), new Vector3(-0.05f, 0.98f, 1.75f), new Vector3(-0.6f, -1f, 0.2f), rightKnee: new Vector3(0f, 0f, 1f))));
        }

        /// <param name="pelvisYaw">Pelvis yaw (90 = sideways, glove side to home; 0 = square to home).</param>
        /// <param name="trunkTilt">Forward trunk flexion (degrees), split between spine and chest.</param>
        /// <param name="chestYaw">Chest yaw relative to the pelvis (+ = shoulders stay closed: hip–shoulder separation).</param>
        /// <param name="lateral">Lateral trunk tilt toward the glove side (degrees).</param>
        private static MannequinPose Pitch(float pelvisYaw, Vector3 pelvis, float trunkTilt, float chestYaw, float spineYaw, float lateral, float neckYaw,
            Vector3 rightAnkle, Vector3 leftAnkle, float leftFootYaw, float rightFootYaw,
            Vector3 right, Vector3 rightElbow, Vector3 left, Vector3 leftElbow, Vector3? leftKnee = null, Vector3? rightKnee = null, float rightPitch = 0f) =>
            new MannequinPose
            {
                PelvisOffset = pelvis, Pelvis = new Vector3(0f, pelvisYaw, 0f),
                Spine = new Vector3(trunkTilt * 0.5f, spineYaw, lateral * 0.5f), Chest = new Vector3(trunkTilt * 0.5f, chestYaw, lateral * 0.5f),
                Neck = new Vector3(-trunkTilt * 0.4f, neckYaw - chestYaw, 0f),
                RightFootWeight = 1f, RightFoot = rightAnkle, RightKneeHint = rightKnee ?? new Vector3(0.6f, 0f, 0.8f), RightFootYaw = rightFootYaw, RightFootPitch = rightPitch,
                LeftFootWeight = 1f, LeftFoot = leftAnkle, LeftKneeHint = leftKnee ?? new Vector3(0.3f, 0f, 1f), LeftFootYaw = leftFootYaw,
                RightHandWeight = 1f, RightHand = right, RightElbowHint = rightElbow,
                LeftHandWeight = 1f, LeftHand = left, LeftElbowHint = leftElbow,
            };
    }
}
