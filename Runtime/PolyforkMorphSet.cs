using System.Collections.Generic;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Drives a range knob by interpolating between two baked meshes instead of rebuilding.
    ///
    /// Many range knobs only move vertices: measured across 13 assets, 14 of 32 range knobs
    /// keep an identical vertex count across their whole range and merely deform. Those can
    /// be driven by a vertex lerp, which costs about 0.05 ms - against 41.5 ms to re-run the
    /// module on QuickJS, or roughly 120 ms for a server rebuild.
    ///
    /// It is also the only option that makes a slider genuinely continuous. Every rebuild
    /// path has to quantise to a handful of stops so the results can be cached; a morph has
    /// no such constraint, so the model tracks the finger exactly.
    ///
    /// Knobs that change the vertex count - part counts like `planks`, `slats`, `facets` -
    /// cannot be morphed and fall back to discrete prewarmed stops.
    /// </summary>
    public sealed class PolyforkMorphSet
    {
        sealed class Target
        {
            public Mesh Mesh;
            public Vector3[] Min;
            public Vector3[] Max;
            public Vector3[] Working;
        }

        readonly List<Target> _targets = new();

        public string KnobName { get; private set; }
        public float MinValue { get; private set; }
        public float MaxValue { get; private set; }

        /// <summary>False when the two bakes disagree on topology, so a lerp is meaningless.</summary>
        public bool IsMorphable { get; private set; }

        public int VertexCount { get; private set; }

        /// <summary>
        /// Pairs up the meshes of two bakes of the same asset.
        ///
        /// Morphability is decided by measurement rather than by reading the schema: the
        /// same knob name can deform one asset and re-topologise another, so nothing but
        /// comparing the two results can tell them apart. street-lamp's `tallness` moves
        /// vertices; plastic-drum's gains a rib.
        /// </summary>
        public static PolyforkMorphSet Build(
            GameObject atMin, GameObject atMax, string knobName, float minValue, float maxValue)
        {
            var set = new PolyforkMorphSet
            {
                KnobName = knobName,
                MinValue = minValue,
                MaxValue = maxValue
            };

            if (atMin == null || atMax == null) return set;

            var minMeshes = Collect(atMin);
            var maxMeshes = Collect(atMax);

            if (minMeshes.Count == 0 || minMeshes.Count != maxMeshes.Count) return set;

            for (var i = 0; i < minMeshes.Count; i++)
            {
                var a = minMeshes[i];
                var b = maxMeshes[i];
                if (a.vertexCount != b.vertexCount) return set;   // topology changed

                var from = a.vertices;
                var to = b.vertices;

                set._targets.Add(new Target
                {
                    // The min bake is the one kept on screen, so it owns the live mesh.
                    Mesh = a,
                    Min = from,
                    Max = to,
                    Working = (Vector3[])from.Clone()
                });
                set.VertexCount += from.Length;
            }

            // Identical bakes mean the knob does not move geometry at all, so there is
            // nothing to interpolate and a rebuild would be equally pointless.
            set.IsMorphable = set._targets.Count > 0 && Differs(set._targets);
            return set;
        }

        static bool Differs(List<Target> targets)
        {
            foreach (var t in targets)
            {
                for (var i = 0; i < t.Min.Length; i++)
                {
                    if ((t.Min[i] - t.Max[i]).sqrMagnitude > 1e-10f) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Moves the mesh to the given knob value. Safe to call every frame during a drag.
        /// </summary>
        public void Apply(float value)
        {
            if (!IsMorphable) return;

            /* The meshes belong to whatever is displaying them, and a caller that swaps the
             * displayed model destroys them. Writing into a destroyed Mesh throws from inside
             * OnGUI, which then leaves IMGUI mid-layout and reports an unrelated
             * "Invalid GUILayout state" on top of it - two errors, neither naming the cause.
             *
             * A morph set whose meshes are gone is simply finished. Saying so once is better
             * than throwing on every repaint. */
            if (!MeshesAlive())
            {
                IsMorphable = false;
                return;
            }

            var t = Mathf.Approximately(MaxValue, MinValue)
                ? 0f
                : Mathf.Clamp01((value - MinValue) / (MaxValue - MinValue));

            foreach (var target in _targets)
            {
                var min = target.Min;
                var max = target.Max;
                var work = target.Working;

                for (var i = 0; i < work.Length; i++)
                    work[i] = Vector3.LerpUnclamped(min[i], max[i], t);

                target.Mesh.SetVertices(work);
                // Bounds matter for culling and for anything that measures the object.
                target.Mesh.RecalculateBounds();
            }
        }

        /// <summary>False once anything has destroyed the meshes this writes into.</summary>
        bool MeshesAlive()
        {
            foreach (var target in _targets)
                if (target.Mesh == null) return false;   // Unity's ==, which knows about destroyed

            return true;
        }

        /// <summary>Restores the geometry the min bake arrived with.</summary>
        public void Reset() => Apply(MinValue);

        static List<Mesh> Collect(GameObject root)
        {
            var meshes = new List<Mesh>();
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh != null && f.sharedMesh.isReadable) meshes.Add(f.sharedMesh);
            }
            return meshes;
        }
    }
}
