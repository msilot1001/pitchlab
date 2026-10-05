using Pitchlab.Simulation.Core;
using UnityEngine;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Simulation frame (right-handed, Z up; Docs/PHYSICS.md) to Unity world (left-handed, Y up).
    /// Swapping Y and Z flips handedness, so +X stays first base and the pitcher is at Unity +Z.
    /// </summary>
    public static class SimulationSpace
    {
        /// <summary>For positions and velocities (polar vectors). Angular velocity is an axial vector and would also
        /// need negating under this handedness flip; add a separate method if spin is ever drawn.</summary>
        public static Vector3 ToUnity(Vector3d p) => new Vector3((float)p.X, (float)p.Z, (float)p.Y);
        public static Vector3d ToSimulation(Vector3 v) => new Vector3d(v.x, v.z, v.y);
    }
}
