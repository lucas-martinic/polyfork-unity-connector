using System.Collections.Generic;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Maps every vertex of a loaded Polyfork GLB onto one of the asset's named colour
    /// slots, so colour and colourway knobs can be applied instantly with no network call.
    ///
    /// This is exact rather than approximate. A Polyfork asset is a single mesh with a
    /// single material and baked COLOR_0 vertex colours, and the set of distinct vertex
    /// colours is precisely the set of default hexes declared by the asset's colour knobs.
    /// (Verified: plastic-drum-da992f has exactly three distinct vertex colours -
    /// #8FB4C9 x1386, #1B1D20 x336, #4E5459 x126 - matching body / bung / lid.)
    /// So a vertex whose colour equals knob X's default hex is, by construction, part of
    /// slot X, and recolouring it is what Polyfork's own viewer does.
    /// </summary>
    public sealed class PolyforkColorSlots
    {
        const float MatchEpsilon = 0.02f;

        sealed class MeshBinding
        {
            public Mesh Mesh;
            public int[] VertexSlot;     // -1 when the vertex matched no slot
            public Color[] Working;      // scratch buffer reused on every apply
        }

        readonly List<MeshBinding> _bindings = new();
        readonly List<string> _slotNames = new();
        readonly Dictionary<string, int> _slotIndex = new();

        /// <summary>True when vertex colours are stored linear (the glTF convention).</summary>
        public bool LinearVertexColors { get; private set; } = true;

        public IReadOnlyList<string> SlotNames => _slotNames;

        public bool HasSlots => _slotNames.Count > 0 && _bindings.Count > 0;

        /// <summary>
        /// Binds a freshly loaded GLB hierarchy to the asset's declared colour slots.
        /// </summary>
        public static PolyforkColorSlots Build(GameObject root, PolyforkParams schema)
        {
            var slots = new PolyforkColorSlots();
            if (root == null || schema == null) return slots;

            var defaults = schema.DefaultSlotColors();
            if (defaults.Count == 0) return slots;

            foreach (var name in defaults.Keys)
            {
                slots._slotIndex[name] = slots._slotNames.Count;
                slots._slotNames.Add(name);
            }

            var meshes = CollectMeshes(root);
            if (meshes.Count == 0)
            {
                slots._slotNames.Clear();
                slots._slotIndex.Clear();
                return slots;
            }

            // The GLB stores linear colours, but be tolerant of an importer that has
            // already converted to sRGB: score both and keep whichever classifies better.
            var linearTargets = new Color[slots._slotNames.Count];
            var srgbTargets = new Color[slots._slotNames.Count];
            for (var i = 0; i < slots._slotNames.Count; i++)
            {
                var authored = defaults[slots._slotNames[i]];   // sRGB, as published
                srgbTargets[i] = authored;
                linearTargets[i] = authored.linear;
            }

            var linearScore = Score(meshes, linearTargets);
            var srgbScore = Score(meshes, srgbTargets);
            slots.LinearVertexColors = linearScore >= srgbScore;
            var targets = slots.LinearVertexColors ? linearTargets : srgbTargets;

            foreach (var mesh in meshes)
            {
                var colors = mesh.colors;
                if (colors == null || colors.Length == 0) continue;

                var map = new int[colors.Length];
                for (var v = 0; v < colors.Length; v++) map[v] = NearestSlot(colors[v], targets);

                slots._bindings.Add(new MeshBinding
                {
                    Mesh = mesh,
                    VertexSlot = map,
                    Working = colors
                });
            }

            return slots;
        }

        /// <summary>Fraction of vertices that land cleanly on a slot, used to pick colour space.</summary>
        static float Score(List<Mesh> meshes, Color[] targets)
        {
            var matched = 0;
            var total = 0;
            foreach (var mesh in meshes)
            {
                var colors = mesh.colors;
                if (colors == null) continue;
                for (var i = 0; i < colors.Length; i++)
                {
                    total++;
                    if (NearestSlot(colors[i], targets) >= 0) matched++;
                }
            }
            return total == 0 ? 0f : (float)matched / total;
        }

        static int NearestSlot(Color c, Color[] targets)
        {
            var best = -1;
            var bestDist = MatchEpsilon;
            for (var i = 0; i < targets.Length; i++)
            {
                var t = targets[i];
                var d = Mathf.Abs(c.r - t.r) + Mathf.Abs(c.g - t.g) + Mathf.Abs(c.b - t.b);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        static List<Mesh> CollectMeshes(GameObject root)
        {
            var meshes = new List<Mesh>();

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                // sharedMesh is the imported instance and is safe to mutate: glTFast
                // creates fresh meshes per load rather than handing back shared assets.
                if (mf.sharedMesh != null && mf.sharedMesh.isReadable) meshes.Add(mf.sharedMesh);
            }

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null && smr.sharedMesh.isReadable) meshes.Add(smr.sharedMesh);
            }

            return meshes;
        }

        /// <summary>
        /// Writes the given slot colours onto the mesh. Keys are colour-knob names
        /// (e.g. "body", "lid", "bung"); values are the authored sRGB colours.
        /// Slots not present are left at whatever they currently are.
        /// </summary>
        public void Apply(IReadOnlyDictionary<string, Color> slotColors)
        {
            if (!HasSlots || slotColors == null || slotColors.Count == 0) return;

            var resolved = new Color[_slotNames.Count];
            var assigned = new bool[_slotNames.Count];
            foreach (var kv in slotColors)
            {
                if (!_slotIndex.TryGetValue(kv.Key, out var idx)) continue;
                resolved[idx] = LinearVertexColors ? kv.Value.linear : kv.Value;
                assigned[idx] = true;
            }

            foreach (var binding in _bindings)
            {
                var changed = false;
                var buf = binding.Working;
                var map = binding.VertexSlot;

                for (var v = 0; v < map.Length; v++)
                {
                    var slot = map[v];
                    if (slot < 0 || !assigned[slot]) continue;

                    var c = resolved[slot];
                    c.a = buf[v].a;
                    if (buf[v] == c) continue;

                    buf[v] = c;
                    changed = true;
                }

                if (changed) binding.Mesh.colors = buf;
            }
        }
    }
}
