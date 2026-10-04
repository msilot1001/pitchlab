using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pitchlab.Presentation;
using UnityEngine;

namespace Pitchlab.Tests
{
    /// <summary>
    /// Objective invariants of the reference motions (TASK-004.6B-0): marker order, planted feet, two-handed grip,
    /// anatomical bend directions, continuity, and the presentation bat path against the Statcast reference (tolerances
    /// fixed before tuning; Docs/MOTION_REFERENCE.md). Aesthetics are judged in Game View, not here.
    /// </summary>
    public class ReferenceMotionTests
    {
        private static readonly Vector3 Release = new Vector3(0.55f, 1.55f, 1.95f);   // four-seam preset, pitcher frame
        private GameObject _go;
        private readonly MannequinPose _pose = new MannequinPose();

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private PlayerMannequin Figure(bool leftHanded = false)
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            _go = new GameObject("Figure");
            var m = _go.AddComponent<PlayerMannequin>();
            m.Build();
            m.LeftHanded = leftHanded;
            return m;
        }

        private static MotionClip Swing => ReferenceMotions.ReferenceRightHandedSwing();
        private static MotionClip Delivery => ReferenceMotions.ReferenceRightHandedPitchDelivery(Release, 0.12f);

        [Test]
        public void MarkersAreInMotionOrder()
        {
            string[] swing = { "stance", "loadStart", "legLift", "plant", "launch", "contact", "extension", "finish" };
            string[] delivery = { "set", "liftPeak", "handBreak", "footPlant", "armCock", "maxExternalRotation", "release", "torsoFollowThrough", "recovery" };
            foreach (var (clip, names) in new[] { (Swing, swing), (Delivery, delivery) })
                for (int i = 1; i < names.Length; i++) Assert.Greater(clip.Marker(names[i]), clip.Marker(names[i - 1]), $"{clip.Name}: {names[i]}");
        }

        /// <summary>World positions of a joint over [u0, u1] (300 samples).</summary>
        private List<Vector3> Track(PlayerMannequin m, MotionClip clip, MannequinJoint joint, float u0, float u1)
        {
            var points = new List<Vector3>();
            for (int i = 0; i <= 300; i++)
            {
                clip.Sample(Mathf.Lerp(u0, u1, i / 300f), _pose);
                m.ApplyPose(_pose);
                points.Add(m.Joint(joint).position);
            }

            return points;
        }

        private static float Spread(List<Vector3> points)
        {
            float max = 0f;
            foreach (Vector3 p in points) max = Mathf.Max(max, Vector3.Distance(p, points[0]));
            return max;
        }

        [Test]
        public void PlantedFeetDoNotSkate()
        {
            PlayerMannequin m = Figure();
            MotionClip swing = Swing, delivery = Delivery;
            Assert.Less(Spread(Track(m, swing, MannequinJoint.LeftFoot, swing.Marker("plant"), swing.Marker("finish"))), 0.01f, "front foot from plant to finish");
            Assert.Less(Spread(Track(m, swing, MannequinJoint.RightFoot, swing.Marker("stance"), swing.Marker("launch"))), 0.01f, "rear foot until launch");
            Assert.Less(Spread(Track(m, delivery, MannequinJoint.RightFoot, delivery.Marker("set"), delivery.Marker("footApproach"))), 0.01f, "pivot foot until the stride lands");
            Assert.Less(Spread(Track(m, delivery, MannequinJoint.LeftFoot, delivery.Marker("footPlant"), delivery.Marker("recovery"))), 0.01f, "lead foot from foot plant on");
        }

        [Test]
        public void BothHandsStayOnTheBatThroughTheSwing([Values(false, true)] bool leftHanded)
        {
            PlayerMannequin m = Figure(leftHanded);
            MotionClip swing = Swing;
            for (int i = 0; i <= 400; i++)
            {
                swing.Sample(i / 400f, _pose);
                m.ApplyPose(_pose);
                Vector3 right = m.Joint(MannequinJoint.RightHand).position, left = m.Joint(MannequinJoint.LeftHand).position;
                Assert.Less(Vector3.Distance(right, m.FigurePoint(_pose.GripPoint)), 0.06f, $"top hand on the grip at u {i / 400f:0.000}");
                Assert.Less(Vector3.Distance(left, right), 0.15f, $"bottom hand on the handle at u {i / 400f:0.000}");
            }
        }

        [Test]
        public void ElbowsAndKneesBendTheAnatomicalWayAndNothingIsNaN()
        {
            PlayerMannequin m = Figure();
            foreach (MotionClip clip in new[] { Swing, Delivery })
                for (int i = 0; i <= 200; i++)
                {
                    clip.Sample(i / 200f, _pose);
                    m.ApplyPose(_pose);
                    for (int j = 0; j < PlayerMannequin.JointCount; j++)
                    {
                        Vector3 p = m.Joint((MannequinJoint)j).position;
                        Assert.IsFalse(float.IsNaN(p.x + p.y + p.z), $"{clip.Name} joint {(MannequinJoint)j}");
                    }

                    // Knees: the knee lies in front of the hip–ankle line (toward the toes), never behind it.
                    foreach (var (thigh, shin, foot) in new[] { (MannequinJoint.LeftThigh, MannequinJoint.LeftShin, MannequinJoint.LeftFoot), (MannequinJoint.RightThigh, MannequinJoint.RightShin, MannequinJoint.RightFoot) })
                    {
                        Vector3 hip = m.Joint(thigh).position, knee = m.Joint(shin).position, ankle = m.Joint(foot).position;
                        Vector3 toes = m.Joint(foot).forward;
                        Vector3 bend = knee - Vector3.Lerp(hip, ankle, Vector3.Dot(knee - hip, ankle - hip) / (ankle - hip).sqrMagnitude);
                        if (bend.magnitude > 0.03f) Assert.Greater(Vector3.Dot(bend.normalized, toes), -0.3f, $"{clip.Name} u {i / 200f:0.00} {thigh} bends backward");
                    }

                    // Elbows: never fully straight-locked past 180°, i.e. the interior angle stays ≥ 20°.
                    foreach (var (upper, fore, hand) in new[] { (MannequinJoint.LeftUpperArm, MannequinJoint.LeftForearm, MannequinJoint.LeftHand), (MannequinJoint.RightUpperArm, MannequinJoint.RightForearm, MannequinJoint.RightHand) })
                    {
                        Vector3 s = m.Joint(upper).position, e = m.Joint(fore).position, w = m.Joint(hand).position;
                        Assert.GreaterOrEqual(Vector3.Angle(s - e, w - e), 20f, $"{clip.Name} u {i / 200f:0.00} {upper} folded through itself");
                    }
                }
        }

        [Test]
        public void MotionIsContinuous()
        {
            // 1 ms steps of game time: no joint jumps more than 5 cm (≈ 50 m/s, beyond any body part's speed).
            PlayerMannequin m = Figure();
            foreach (var (clip, start, end) in new[] { (Swing, ReferenceMotions.SwingStart, ReferenceMotions.SwingEnd), (Delivery, ReferenceMotions.DeliveryStart, ReferenceMotions.DeliveryEnd) })
            {
                var previous = new Vector3[PlayerMannequin.JointCount];
                int steps = Mathf.RoundToInt((end - start) * 1000f);
                for (int i = 0; i <= steps; i++)
                {
                    clip.Sample(i / (float)steps, _pose);
                    m.ApplyPose(_pose);
                    for (int j = 0; j < PlayerMannequin.JointCount; j++)
                    {
                        Vector3 p = m.Joint((MannequinJoint)j).position;
                        if (i > 0) Assert.Less(Vector3.Distance(p, previous[j]), 0.05f, $"{clip.Name} {(MannequinJoint)j} at {start + i / 1000f:0.000} s");
                        previous[j] = p;
                    }
                }
            }
        }

        [Test]
        public void PresentationBatPathMatchesTheStatcastReference()
        {
            // Tolerances set before final tuning: bat speed ±10 %, swing length ±15 %, attack angle ±4°, attack direction
            // ±6°, swing-path tilt ±6° (presentation resemblance, not gameplay).
            PlayerMannequin m = Figure();
            Transform sweetSpot = Equipment.AttachBat(m.BatAnchor), head = sweetSpot.parent.Find("BatHead");
            MotionClip swing = Swing;
            Vector3 At(double t, Transform point)
            {
                swing.Sample(ReferenceMotions.SwingU((float)t), _pose);
                m.ApplyPose(_pose);
                return point.position;
            }

            // Gameplay maps launch → contact onto SwingDuration (0.15 s by default), so clip seconds are gameplay seconds.
            SwingPathMetrics r = SwingPathMetrics.Measure(t => At(t, sweetSpot), t => At(t, head), -0.15, 0.0);
            TestContext.WriteLine(r.ToString());
            Assert.AreEqual(ReferenceMotions.RefBatSpeedMph, r.BatSpeedMph, 0.10 * ReferenceMotions.RefBatSpeedMph, "bat speed");
            Assert.AreEqual(ReferenceMotions.RefSwingLengthFeet, r.SwingLengthFeet, 0.15 * ReferenceMotions.RefSwingLengthFeet, "swing length");
            Assert.AreEqual(ReferenceMotions.RefAttackAngle, r.AttackAngleDegrees, 4.0, "attack angle");
            Assert.AreEqual(ReferenceMotions.RefAttackDirection, r.AttackDirectionDegrees, 6.0, "attack direction");
            Assert.AreEqual(ReferenceMotions.RefSwingPathTilt, r.SwingPathTiltDegrees, 6.0, "swing path tilt");
        }

        [Test]
        public void LeftHandedSwingMirrorsTheBatPath()
        {
            Vector3 SweetAt(bool left)
            {
                PlayerMannequin m = Figure(left);
                Transform ss = Equipment.AttachBat(m.BatAnchor);
                Swing.Sample(ReferenceMotions.SwingU(-0.02f), _pose);
                m.ApplyPose(_pose);
                return ss.position;
            }

            Vector3 r = SweetAt(false), l = SweetAt(true);
            Assert.AreEqual(-r.x, l.x, 1e-4f);
            Assert.AreEqual(r.y, l.y, 1e-4f);
            Assert.AreEqual(r.z, l.z, 1e-4f);
        }
    }
}
