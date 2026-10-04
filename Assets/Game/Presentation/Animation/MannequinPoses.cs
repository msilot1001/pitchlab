using UnityEngine;
using static Pitchlab.Presentation.MannequinPose;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// Key poses for a right-handed player (mirror the figure for left-handers). Figure frame: +Z forward (toward the
    /// target: home plate for the pitcher, the plate for the batter), +X the player's right. Readable, not biomechanical.
    /// </summary>
    public static class MannequinPoses
    {
        // ---- Fielder / generic (TASK-005 will add running, reaching, catching, throwing).
        public static MannequinPose Ready => new MannequinPose
        {
            PelvisOffset = new Vector3(0f, -0.12f, 0f), Spine = new Vector3(25f, 0f, 0f), Neck = new Vector3(-15f, 0f, 0f),
            RightUpperArm = Dir(20f, -60f), RightForearm = Dir(10f, -20f), LeftUpperArm = Dir(-20f, -60f), LeftForearm = Dir(-10f, -20f),
            RightThigh = Dir(120f, -70f), RightShin = Dir(150f, -80f), LeftThigh = Dir(-120f, -70f), LeftShin = Dir(-150f, -80f),
        };

        // ---- Pitcher (stretch). Pelvis yaw +90° turns the glove side toward home; the figure root faces home.
        public static MannequinPose PitchSet => new MannequinPose
        {
            Pelvis = new Vector3(0f, 90f, 0f), Neck = new Vector3(0f, -85f, 0f),
            RightUpperArm = Dir(110f, -55f), RightForearm = Dir(40f, 25f), LeftUpperArm = Dir(70f, -55f), LeftForearm = Dir(140f, 25f),
            RightThigh = Dir(180f, -82f), RightShin = Dir(180f, -88f), LeftThigh = Dir(0f, -82f), LeftShin = Dir(0f, -88f),
        };

        public static MannequinPose PitchLegLift => new MannequinPose
        {
            Pelvis = new Vector3(0f, 105f, 0f), Spine = new Vector3(-5f, 0f, 0f), Neck = new Vector3(0f, -95f, 0f),
            RightUpperArm = Dir(110f, -50f), RightForearm = Dir(40f, 30f), LeftUpperArm = Dir(70f, -50f), LeftForearm = Dir(140f, 30f),
            RightThigh = Dir(180f, -85f), RightShin = Dir(180f, -88f), LeftThigh = Dir(70f, 5f), LeftShin = Dir(40f, -80f),
        };

        public static MannequinPose PitchStride => new MannequinPose
        {
            PelvisOffset = new Vector3(0f, -0.15f, 0.35f), Pelvis = new Vector3(0f, 75f, 0f), Neck = new Vector3(0f, -75f, 0f),
            RightUpperArm = Dir(180f, 5f), RightForearm = Dir(180f, 55f), LeftUpperArm = Dir(0f, 10f), LeftForearm = Dir(0f, 15f),
            RightThigh = Dir(180f, -55f), RightShin = Dir(180f, -75f), LeftThigh = Dir(0f, -35f), LeftShin = Dir(0f, -85f),
        };

        public static MannequinPose PitchArmCock => new MannequinPose
        {
            PelvisOffset = new Vector3(0f, -0.22f, 0.6f), Pelvis = new Vector3(0f, 30f, 0f), Chest = new Vector3(0f, 10f, 0f), Neck = new Vector3(0f, -35f, 0f),
            RightUpperArm = Dir(110f, 10f), RightForearm = Dir(150f, 70f), LeftUpperArm = Dir(-30f, -20f), LeftForearm = Dir(-80f, -20f),
            RightThigh = Dir(180f, -45f), RightShin = Dir(180f, -20f), LeftThigh = Dir(0f, -45f), LeftShin = Dir(0f, -88f),
        };

        public static MannequinPose PitchRelease => new MannequinPose
        {
            PelvisOffset = new Vector3(0f, -0.26f, 0.8f), Pelvis = new Vector3(0f, -10f, 0f), Spine = new Vector3(25f, 0f, 0f), Chest = new Vector3(5f, -15f, 0f),
            Neck = new Vector3(-15f, 20f, 0f),
            RightUpperArm = Dir(35f, 35f), RightForearm = Dir(10f, 45f), LeftUpperArm = Dir(-70f, -45f), LeftForearm = Dir(-100f, -10f),
            RightThigh = Dir(180f, -40f), RightShin = Dir(180f, -10f), LeftThigh = Dir(0f, -50f), LeftShin = Dir(0f, -88f),
        };

        public static MannequinPose PitchFollowThrough => new MannequinPose
        {
            PelvisOffset = new Vector3(0f, -0.3f, 0.9f), Pelvis = new Vector3(0f, -45f, 0f), Spine = new Vector3(45f, 0f, 0f), Chest = new Vector3(10f, -10f, 0f),
            Neck = new Vector3(-40f, 40f, 0f),
            RightUpperArm = Dir(-40f, -55f), RightForearm = Dir(-70f, -60f), LeftUpperArm = Dir(-110f, -40f), LeftForearm = Dir(-150f, -20f),
            RightThigh = Dir(150f, -20f), RightShin = Dir(170f, 10f), LeftThigh = Dir(0f, -55f), LeftShin = Dir(0f, -88f),
        };

        // ---- Batter (right-handed, figure faces the plate; the pitcher is at the figure's −X, the catcher at +X).
        public static MannequinPose BatStance => new MannequinPose
        {
            PelvisOffset = new Vector3(0f, -0.08f, 0f), Spine = new Vector3(15f, 0f, 0f), Neck = new Vector3(0f, -80f, 0f),
            RightThigh = Dir(105f, -75f), RightShin = Dir(95f, -85f), LeftThigh = Dir(-105f, -75f), LeftShin = Dir(-95f, -85f),
            GripWeight = 1f, GripPoint = new Vector3(0.2f, 1.45f, 0.2f), GripDirection = Dir(115f, 55f),
        };

        public static MannequinPose BatLoad => new MannequinPose
        {
            PelvisOffset = new Vector3(0.05f, -0.1f, 0f), Pelvis = new Vector3(0f, 15f, 0f), Spine = new Vector3(15f, 0f, 0f), Neck = new Vector3(0f, -95f, 0f),
            RightThigh = Dir(105f, -72f), RightShin = Dir(100f, -85f), LeftThigh = Dir(-90f, -60f), LeftShin = Dir(-100f, -88f),
            GripWeight = 1f, GripPoint = new Vector3(0.32f, 1.45f, 0.1f), GripDirection = Dir(140f, 45f),
        };

        public static MannequinPose BatStride => new MannequinPose
        {
            PelvisOffset = new Vector3(-0.08f, -0.14f, 0f), Pelvis = new Vector3(0f, 10f, 0f), Spine = new Vector3(15f, 0f, 0f), Neck = new Vector3(0f, -90f, 0f),
            RightThigh = Dir(110f, -60f), RightShin = Dir(100f, -85f), LeftThigh = Dir(-110f, -55f), LeftShin = Dir(-95f, -88f),
            GripWeight = 1f, GripPoint = new Vector3(0.3f, 1.4f, 0.12f), GripDirection = Dir(135f, 40f),
        };

        public static MannequinPose BatContact => new MannequinPose
        {
            PelvisOffset = new Vector3(-0.12f, -0.16f, 0f), Pelvis = new Vector3(0f, -45f, 0f), Spine = new Vector3(15f, 0f, -5f), Chest = new Vector3(0f, -15f, 0f),
            Neck = new Vector3(5f, -30f, 0f),
            RightThigh = Dir(120f, -55f), RightShin = Dir(90f, -85f), LeftThigh = Dir(-110f, -55f), LeftShin = Dir(-95f, -88f),
            GripWeight = 1f, GripPoint = new Vector3(-0.05f, 1.02f, 0.38f), GripDirection = Dir(5f, -25f),
        };

        public static MannequinPose BatFollowThrough => new MannequinPose
        {
            PelvisOffset = new Vector3(-0.12f, -0.12f, 0f), Pelvis = new Vector3(0f, -85f, 0f), Spine = new Vector3(5f, 0f, -5f), Chest = new Vector3(0f, -30f, 0f),
            Neck = new Vector3(0f, 0f, 0f),
            RightThigh = Dir(140f, -65f), RightShin = Dir(150f, -60f), LeftThigh = Dir(-110f, -60f), LeftShin = Dir(-95f, -88f),
            GripWeight = 1f, GripPoint = new Vector3(-0.25f, 1.5f, 0.05f), GripDirection = Dir(160f, 15f),
        };
    }
}
