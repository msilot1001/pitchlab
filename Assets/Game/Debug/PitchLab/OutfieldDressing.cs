using System.Collections.Generic;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Field;
using UnityEngine;

namespace Pitchlab.Sandbox
{
    /// <summary>Outfield wall and warning track meshes drawn from the simulation's <see cref="FieldLayout"/> (shared by the labs).</summary>
    public static class OutfieldDressing
    {
        /// <summary>
        /// Warning track and outfield wall drawn from the simulation's field layout (Docs/SURFACE_PHYSICS.md), so what the
        /// ball bounces off is what the player sees. Visual only: no colliders.
        /// </summary>
        public static Mesh[] Build(Transform parent, FieldLayout field)
        {
            var track = new Mesh { name = "WarningTrack" };
            var wall = new Mesh { name = "OutfieldWall" };
            var trackVertices = new List<Vector3>();
            var wallVertices = new List<Vector3>();
            var trackTriangles = new List<int>();
            var wallTriangles = new List<int>();
            float height = (float)field.WallHeight, width = (float)FieldLayout.WarningTrackWidth;
            for (int i = 0; i + 1 < FieldLayout.FencePointCount; i++)
            {
                Vector3 a = SimulationSpace.ToUnity(field.FencePoint(i)), b = SimulationSpace.ToUnity(field.FencePoint(i + 1));
                Vector3 inward = Vector3.Cross(Vector3.up, (b - a).normalized);
                if (Vector3.Dot(inward, -a) < 0f) inward = -inward;
                AddQuad(trackVertices, trackTriangles, a + 0.006f * Vector3.up, b + 0.006f * Vector3.up, b + inward * width + 0.006f * Vector3.up, a + inward * width + 0.006f * Vector3.up);
                AddQuad(wallVertices, wallTriangles, a, b, b + height * Vector3.up, a + height * Vector3.up);
            }

            Finish(track, trackVertices, trackTriangles, "WarningTrack", new Color(0.47f, 0.32f, 0.22f));
            Finish(wall, wallVertices, wallTriangles, "OutfieldWall", new Color(0.12f, 0.25f, 0.18f));
            return new[] { track, wall };

            void AddQuad(List<Vector3> v, List<int> t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                int k = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                // Both faces (the wall is seen from the field, the track from above).
                t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2, k, k + 1, k + 2, k, k + 2, k + 3 });
            }

            void Finish(Mesh mesh, List<Vector3> v, List<int> t, string name, Color color)
            {
                mesh.SetVertices(v);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = PresentationMaterials.Get(color, unlit: true);   // flat: double-sided faces have no useful normals
            }
        }

    }
}
