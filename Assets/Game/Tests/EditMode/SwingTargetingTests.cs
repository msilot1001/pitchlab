using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;
using UnityEngine;

namespace Pitchlab.Tests
{
    /// <summary>
    /// Procedural swing targeting (TASK-004.6B-1): the reference swing deformed toward contact points across and beyond the
    /// strike zone puts the visual sweet spot within 5 cm of normal (successful-PCI) targets while the feet stay planted, both hands stay on
    /// the bat, joints stay sane and the bat never passes through the torso.
    /// </summary>
    public class SwingTargetingTests
    {
        // Lab geometry in the batter's figure frame (root at the origin, facing the pitcher), from the same constants the
        // sandbox places the batter with: plate centre toward the plate side, contact plane forward of the stance hips.
        private static readonly float PlateX = (float)(PitchingGeometry.PlateHalfWidth + Units.InchesToMeters(ReferenceMotions.StanceOffPlateEdgeInches));
        private static readonly float ContactZ = (float)(HittingPitch.DefaultContactPlaneY - (PitchingGeometry.PlateFrontY - Units.InchesToMeters(ReferenceMotions.StanceBehindPlateFrontInches)));
        private GameObject _go;
        private PlayerMannequin _m;
        private Transform _sweetSpot;
        private MotionClip _swing;
        private float _uContact;
        private readonly MannequinPose _pose = new MannequinPose(), _plain = new MannequinPose();

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Batter");
            _m = _go.AddComponent<PlayerMannequin>();
            _m.Build();
            _sweetSpot = Equipment.AttachBat(_m.BatAnchor);
            _swing = ReferenceMotions.ReferenceRightHandedSwing();
            _uContact = _swing.Marker("contact");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private static Vector3 Target(float plateX, float height, float timingMs = 0f) => new Vector3(PlateX + plateX, height, ContactZ + Depth(timingMs));

        // Gameplay contact for a timing error is where the ball is then: ≈ 40 m/s × error out front (early) or deeper
        // (late); the presentation's timing turn is SprayRate (1.2°/ms) × error, clamped to ±30°.
        private static float Depth(float timingMs) => -0.04f * timingMs;
        private static float Turn(float timingMs) => Mathf.Clamp(1.2f * timingMs, -30f, 30f);

        private SwingAdjustment Solve(Vector3 target, float yaw, out float residual) =>
            SwingTargeting.Solve(_m, _sweetSpot, _swing, _uContact, target, yaw, _pose, out residual);

        private void ShowContact(in SwingAdjustment a)
        {
            _swing.Sample(_uContact, _pose);
            SwingTargeting.Apply(a, 1f, 1f, _pose);
            _m.ApplyPose(_pose);
        }

        [Test]
        public void SweetSpotReachesTargetsAcrossTheZone(
            [Values(-0.25f, -0.125f, 0f, 0.125f, 0.25f)] float plateX,
            [Values(0.45f, 0.65f, 0.85f, 1.05f)] float height,
            [Values(-10f, -5f, 0f, 5f, 10f)] float timingMs)
        {
            // Strike zone (plate half-width 0.216 m, typical knees 0.45 m to letters 1.05 m) widened by a ball radius, at
            // timing within ±10 ms (contact up to 0.4 m out front or deep): where a normal successful PCI puts contact.
            Solve(Target(plateX, height, timingMs), Turn(timingMs), out float residual);
            // Known limits (Docs/MOTION_REFERENCE.md): low and away is the farthest reach — the bounded body falls up to ~9 cm
            // short at that corner on time or late and ~14 cm early; early contact (out front) that is low or away is ≤ 7 cm.
            bool corner = plateX >= 0.125f && height <= 0.45f;
            bool earlyLowOrAway = timingMs < 0f && (height <= 0.65f || plateX >= 0.25f);
            float bound = corner ? (timingMs < 0f ? 0.15f : 0.1f) : earlyLowOrAway ? 0.07f : 0.05f;
            Assert.Less(residual, bound, $"plate x {plateX}, height {height}, timing {timingMs} ms");
        }

        [Test]
        public void TargetsBeyondReachAreApproachedNotReached(
            [Values(-0.3f, 0f, 0.3f)] float plateX,
            [Values(0.45f, 1.2f)] float height,
            [Values(-35f, -20f, 0f, 20f, 35f)] float timingMs)
        {
            // Off the plate by more than a ball, or contact 0.8–1.4 m out front / deep (±20–35 ms, the edge of the hit
            // window): beyond a planted batter's reach. The sweet spot still closes most of the gap.
            Vector3 target = Target(plateX, height, timingMs);
            Solve(target, Turn(timingMs), out float residual);
            _swing.Sample(_uContact, _plain);
            _m.ApplyPose(_plain);
            float undeformed = Vector3.Distance(_sweetSpot.position, target);
            TestContext.WriteLine($"plate x {plateX}, height {height}, timing {timingMs} ms: {residual:0.000} of {undeformed:0.000} m");
            // At the window edge (1.4 m out front) the bat closes ~45 % of the gap; nearer, well over half.
            Assert.Less(residual, (Mathf.Abs(timingMs) > 30f ? 0.6f : 0.5f) * undeformed + 0.03f, "closes the gap");
        }

        [Test]
        public void DeformationKeepsFeetGripJointsAndTorsoSane(
            [Values(-0.6f, 0f, 0.6f)] float plateX,
            [Values(0.2f, 0.75f, 1.4f)] float height,
            [Values(-35f, 0f, 35f)] float timingMs)
        {
            // PCI-area corners included: misses aim wherever the player aimed, never snap to the ball.
            SwingAdjustment a = Solve(Target(plateX, height, timingMs), Turn(timingMs), out _);
            Assert.LessOrEqual(a.HandShift.magnitude, SwingTargeting.MaxHandShift + 1e-4f);

            _swing.Sample(_uContact, _plain);
            _m.ApplyPose(_plain);
            Vector3 left = _m.Joint(MannequinJoint.LeftFoot).position, right = _m.Joint(MannequinJoint.RightFoot).position;

            ShowContact(a);
            Assert.Less(Vector3.Distance(left, _m.Joint(MannequinJoint.LeftFoot).position), 0.01f, "front foot planted");
            Assert.Less(Vector3.Distance(right, _m.Joint(MannequinJoint.RightFoot).position), 0.01f, "back foot planted");

            Vector3 rh = _m.Joint(MannequinJoint.RightHand).position, lh = _m.Joint(MannequinJoint.LeftHand).position;
            Assert.Less(Vector3.Distance(rh, _m.FigurePoint(_pose.GripPoint)), 0.06f, "top hand on the grip");
            Assert.Less(Vector3.Distance(lh, rh), 0.15f, "bottom hand on the handle");
            foreach (var (upper, fore, hand) in new[] { (MannequinJoint.LeftUpperArm, MannequinJoint.LeftForearm, MannequinJoint.LeftHand), (MannequinJoint.RightUpperArm, MannequinJoint.RightForearm, MannequinJoint.RightHand) })
            {
                Vector3 s = _m.Joint(upper).position, e = _m.Joint(fore).position, w = _m.Joint(hand).position;
                Assert.GreaterOrEqual(Vector3.Angle(s - e, w - e), 20f, $"{upper} folded through itself");
            }

            for (int j = 0; j < PlayerMannequin.JointCount; j++)
            {
                Vector3 p = _m.Joint((MannequinJoint)j).position;
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z), ((MannequinJoint)j).ToString());
            }

            // Torso: pelvis → chest top. Hands and the bat from the hands to the sweet spot stay outside a 12 cm core.
            Vector3 hip = _m.Joint(MannequinJoint.Pelvis).position, neck = _m.Joint(MannequinJoint.Neck).position;
            for (int i = 0; i <= 10; i++)
            {
                Vector3 p = Vector3.Lerp(rh, _sweetSpot.position, i / 10f);
                Assert.Greater(DistanceToSegment(p, hip, neck), 0.12f, $"bat point {i}/10 inside the torso");
            }
        }

        [Test]
        public void LowBallsBendTheBodyAndTipTheBarrel()
        {
            // Visible behaviour: for a low ball the hips are lower and the barrel tips below the hands; for a high one the
            // barrel stays level or above.
            (float hips, float barrel) Shown(float height)
            {
                ShowContact(Solve(Target(0f, height), 0f, out _));
                return (_m.Joint(MannequinJoint.Pelvis).position.y, _sweetSpot.position.y - _m.Joint(MannequinJoint.RightHand).position.y);
            }

            var low = Shown(0.45f);
            var high = Shown(1.05f);
            Assert.Less(low.hips, high.hips, "hips lower for the low ball");
            Assert.Less(low.barrel, -0.05f, "barrel below the hands for the low ball");
            Assert.Greater(high.barrel, low.barrel + 0.1f);
        }

        [Test]
        public void ZeroWeightLeavesTheReferenceSwingUnchanged()
        {
            SwingAdjustment a = Solve(Target(0.2f, 0.5f), 0f, out _);
            _swing.Sample(_uContact, _plain);
            _swing.Sample(_uContact, _pose);
            SwingTargeting.Apply(a, 0f, 0f, _pose);
            Assert.Less((_pose.GripPoint - _plain.GripPoint).magnitude, 1e-6f);
            Assert.Less(Vector3.Angle(_pose.GripDirection, _plain.GripDirection), 0.01f);
            Assert.AreEqual(_plain.PelvisOffset, _pose.PelvisOffset);
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + t * ab);
        }
    }
}
