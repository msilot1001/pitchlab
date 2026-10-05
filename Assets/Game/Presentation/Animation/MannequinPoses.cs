using UnityEngine;
using static Pitchlab.Presentation.MannequinPose;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// Generic single poses for a right-handed player (mirror the figure for left-handers); figure frame +Z forward, +X
    /// right. Batting and pitching use the reference clips in <see cref="ReferenceMotions"/>.
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
    }
}
