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
            var pose = new MannequinPose();
            ReferenceMotions.ReferenceRightHandedPitchDelivery(new Vector3(0.55f, 1.55f, 1.95f), 0.12f).Sample(ReferenceMotions.DeliveryU(-0.1f), pose);
            PlayerMannequin right = Make(false);
            right.ApplyPose(pose);
            float rightHandX = right.transform.InverseTransformPoint(right.BallAnchor.position).x;
            Object.DestroyImmediate(_go);

            PlayerMannequin left = Make(true);
            left.ApplyPose(pose);
            float leftHandX = left.transform.InverseTransformPoint(left.BallAnchor.position).x;
            Assert.Greater(rightHandX, 0.2f, "right-hander's ball hand is on the right");
            Assert.AreEqual(-rightHandX, leftHandX, 1e-4f, "left-hander mirrors it");
            Assert.IsTrue(left.BallAnchor.IsChildOf(left.Joint(MannequinJoint.RightHand)), "anchors stay on their bones");
        }
    }
}
