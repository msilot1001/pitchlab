using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// Placeholder equipment built from primitives and parented to <see cref="PlayerMannequin"/> anchors. Visual only:
    /// no colliders, never used for contact or catching decisions.
    /// </summary>
    public static class Equipment
    {
        public static readonly Color BatColor = new Color(0.78f, 0.62f, 0.4f);
        public static readonly Color GloveColor = new Color(0.45f, 0.24f, 0.1f);

        /// <summary>Wood-bat length (m) and the sweet spot's distance from the grip anchor along the bat (+Y).</summary>
        public const float BatLength = 0.86f, SweetSpotFromGrip = 0.62f, HeadFromGrip = 0.77f;

        /// <summary>A bat along the anchor's +Y from the grip; returns the sweet-spot marker transform (barrel ≈ 0.17 m from the end).</summary>
        public static Transform AttachBat(Transform anchor)
        {
            var bat = new GameObject("Bat").transform;
            bat.SetParent(anchor, false);
            Material wood = PresentationMaterials.Get(BatColor);
            // Knob 0.08 m below the grip; handle tapers into the barrel.
            PlayerMannequin.Shape(bat, PrimitiveType.Sphere, new Vector3(0f, -0.08f, 0f), Vector3.one * 0.05f, wood);
            PlayerMannequin.Shape(bat, PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f), new Vector3(0.03f, 0.2f, 0.03f), wood);
            PlayerMannequin.Shape(bat, PrimitiveType.Cylinder, new Vector3(0f, 0.38f, 0f), new Vector3(0.045f, 0.08f, 0.045f), wood);
            PlayerMannequin.Shape(bat, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.066f, 0.17f, 0.066f), wood);
            var sweetSpot = new GameObject("SweetSpot").transform;
            sweetSpot.SetParent(bat, false);
            sweetSpot.localPosition = new Vector3(0f, SweetSpotFromGrip, 0f);
            // Bat head (barrel end), ≈ 6 in beyond the sweet spot: Statcast swing length is measured on this point.
            var head = new GameObject("BatHead").transform;
            head.SetParent(bat, false);
            head.localPosition = new Vector3(0f, HeadFromGrip, 0f);
            return sweetSpot;
        }

        public static Transform AttachGlove(Transform anchor)
        {
            var glove = new GameObject("Glove").transform;
            glove.SetParent(anchor, false);
            Material leather = PresentationMaterials.Get(GloveColor);
            PlayerMannequin.Shape(glove, PrimitiveType.Sphere, new Vector3(0f, -0.02f, 0.02f), new Vector3(0.17f, 0.22f, 0.09f), leather);
            PlayerMannequin.Shape(glove, PrimitiveType.Cube, new Vector3(0.07f, 0.02f, 0.02f), new Vector3(0.05f, 0.12f, 0.05f), leather, Quaternion.Euler(0f, 0f, -25f));
            return glove;
        }
    }
}
