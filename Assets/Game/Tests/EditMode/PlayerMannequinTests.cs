using NUnit.Framework;
using Pitchlab.Presentation;
using UnityEngine;

namespace Pitchlab.Tests
{
    public class PlayerMannequinTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private PlayerMannequin Make(bool leftHanded)
        {
            _go = new GameObject("Mannequin");
            var m = _go.AddComponent<PlayerMannequin>();
            m.Build();
            m.LeftHanded = leftHanded;
            return m;
        }

        [Test]
        public void EveryJointAndEquipmentAnchorExistsAndNothingCollides()
        {
            PlayerMannequin m = Make(false);
            for (int i = 0; i < PlayerMannequin.JointCount; i++) Assert.IsNotNull(m.Joint((MannequinJoint)i), ((MannequinJoint)i).ToString());
            foreach (Transform anchor in new[] { m.RightHandAnchor, m.LeftHandAnchor, m.BatAnchor, m.GloveAnchor, m.BallAnchor }) Assert.IsNotNull(anchor);
            Assert.IsTrue(m.BatAnchor.IsChildOf(m.Joint(MannequinJoint.RightHand)));
            Assert.IsTrue(m.GloveAnchor.IsChildOf(m.Joint(MannequinJoint.LeftHand)));
            Equipment.AttachBat(m.BatAnchor);
            Equipment.AttachGlove(m.GloveAnchor);
            Assert.AreEqual(0, _go.GetComponentsInChildren<Collider>().Length, "presentation never collides");
        }

        [Test]
        public void LeftHandedMirrorPutsTheThrowingHandOnTheOtherSideAndKeepsAnchors()
        {
            PlayerMannequin right = Make(false);
            right.ApplyPose(MannequinPoses.PitchArmCock);
            float rightHandX = right.transform.InverseTransformPoint(right.BallAnchor.position).x;
            Object.DestroyImmediate(_go);

            PlayerMannequin left = Make(true);
            left.ApplyPose(MannequinPoses.PitchArmCock);
            float leftHandX = left.transform.InverseTransformPoint(left.BallAnchor.position).x;
            Assert.Greater(rightHandX, 0.2f, "right-hander's ball hand is on the right");
            Assert.AreEqual(-rightHandX, leftHandX, 1e-4f, "left-hander mirrors it");
            Assert.IsTrue(left.BallAnchor.IsChildOf(left.Joint(MannequinJoint.RightHand)), "anchors stay on their bones");
        }

        [Test]
        public void GripPutsBothHandsOnTheBat([Values("Stance", "Load", "Stride", "Contact", "FollowThrough")] string pose, [Values(false, true)] bool leftHanded)
        {
            PlayerMannequin m = Make(leftHanded);
            Transform sweetSpot = Equipment.AttachBat(m.BatAnchor);
            m.ApplyPose(pose switch
            {
                "Stance" => MannequinPoses.BatStance, "Load" => MannequinPoses.BatLoad, "Stride" => MannequinPoses.BatStride,
                "Contact" => MannequinPoses.BatContact, _ => MannequinPoses.BatFollowThrough,
            });
            Vector3 along = (sweetSpot.position - m.BatAnchor.position).normalized;
            // The left hand sits a hand-width below the right hand along the bat.
            Vector3 toLeft = m.Joint(MannequinJoint.LeftHand).position - m.Joint(MannequinJoint.RightHand).position;
            Assert.Less(toLeft.magnitude, 0.2f);
            Assert.Less(Vector3.Dot(toLeft, along), 0f);
        }
    }
}
