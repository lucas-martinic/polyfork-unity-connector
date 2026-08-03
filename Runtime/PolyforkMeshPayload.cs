using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// The result of running an asset module, decoded into Unity meshes.
    ///
    /// The JS side flattens its whole object graph into one envelope with base64 typed
    /// arrays, so a bake costs a single marshal across the engine boundary no matter how
    /// complex the model. Walking the object graph from C# would cost thousands of interop
    /// calls instead.
    ///
    /// Vertex colours arrive linear, the same convention glTF uses, so the meshes this
    /// produces are interchangeable with ones loaded from a baked GLB.
    /// </summary>
    public sealed class PolyforkMeshPayload
    {
        public sealed class Entry
        {
            public string Name;
            public Matrix4x4 Matrix = Matrix4x4.identity;
            public Vector3[] Positions;
            public Color[] Colors;
            public Vector3[] Normals;
            public int[] Indices;
        }

        public readonly List<Entry> Meshes = new();

        public int TotalVertices
        {
            get
            {
                var n = 0;
                foreach (var m in Meshes) n += m.Positions?.Length ?? 0;
                return n;
            }
        }

        public int TotalTriangles
        {
            get
            {
                var n = 0;
                foreach (var m in Meshes)
                    n += (m.Indices?.Length ?? m.Positions?.Length ?? 0) / 3;
                return n;
            }
        }

        public static PolyforkMeshPayload Parse(string json)
        {
            var payload = new PolyforkMeshPayload();
            if (string.IsNullOrWhiteSpace(json)) return payload;

            var root = JObject.Parse(json);
            if (root["meshes"] is not JArray meshes) return payload;

            foreach (var token in meshes)
            {
                if (token is not JObject m) continue;

                var positions = ReadVector3(m["positions"]);
                if (positions == null || positions.Length == 0) continue;

                payload.Meshes.Add(new Entry
                {
                    Name = (string)m["name"] ?? "",
                    Matrix = ReadMatrix(m["matrix"]),
                    Positions = positions,
                    Colors = ReadColor(m["colors"]),
                    Normals = ReadVector3(m["normals"]),
                    Indices = ReadInts(m["indices"])
                });
            }

            return payload;
        }

        /// <summary>
        /// Builds the hierarchy. Each entry becomes a child carrying its own world matrix,
        /// which keeps rigged assets' pivots intact rather than flattening them.
        /// </summary>
        public GameObject ToGameObject(Material material, Transform parent = null, string name = "PolyforkAsset")
        {
            var root = new GameObject(name);
            if (parent != null) root.transform.SetParent(parent, false);

            for (var i = 0; i < Meshes.Count; i++)
            {
                var entry = Meshes[i];

                var child = new GameObject(string.IsNullOrEmpty(entry.Name) ? $"mesh{i}" : entry.Name);
                child.transform.SetParent(root.transform, false);
                ApplyMatrix(child.transform, entry.Matrix);

                var mesh = BuildMesh(entry);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;

                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            return root;
        }

        static Mesh BuildMesh(Entry entry)
        {
            var mesh = new Mesh { name = string.IsNullOrEmpty(entry.Name) ? "PolyforkMesh" : entry.Name };

            // Polyfork geometry routinely exceeds 65k vertices once knobs raise part counts.
            if (entry.Positions.Length > ushort.MaxValue)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(entry.Positions);
            if (entry.Colors is { Length: > 0 }) mesh.SetColors(entry.Colors);

            if (entry.Indices is { Length: > 0 })
            {
                mesh.SetTriangles(entry.Indices, 0);
            }
            else
            {
                // Non-indexed: one vertex per corner, which is what makes the facets flat.
                var seq = new int[entry.Positions.Length];
                for (var i = 0; i < seq.Length; i++) seq[i] = i;
                mesh.SetTriangles(seq, 0);
            }

            if (entry.Normals is { Length: > 0 }) mesh.SetNormals(entry.Normals);
            else mesh.RecalculateNormals();   // per-face, since no vertex is shared

            mesh.RecalculateBounds();
            return mesh;
        }

        static void ApplyMatrix(Transform t, Matrix4x4 m)
        {
            t.localPosition = m.GetColumn(3);
            var forward = (Vector3)m.GetColumn(2);
            var up = (Vector3)m.GetColumn(1);
            if (forward.sqrMagnitude > 1e-9f && up.sqrMagnitude > 1e-9f)
                t.localRotation = Quaternion.LookRotation(forward, up);
            t.localScale = new Vector3(
                ((Vector3)m.GetColumn(0)).magnitude,
                ((Vector3)m.GetColumn(1)).magnitude,
                ((Vector3)m.GetColumn(2)).magnitude);
        }

        // ---------------------------------------------------------------- decoding

        static float[] ReadFloats(JToken token)
        {
            var b64 = (string)token;
            if (string.IsNullOrEmpty(b64)) return null;

            var bytes = Convert.FromBase64String(b64);
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * 4);
            return floats;
        }

        static int[] ReadInts(JToken token)
        {
            var b64 = (string)token;
            if (string.IsNullOrEmpty(b64)) return null;

            var bytes = Convert.FromBase64String(b64);
            var ints = new int[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, ints, 0, ints.Length * 4);
            return ints;
        }

        static Vector3[] ReadVector3(JToken token)
        {
            var f = ReadFloats(token);
            if (f == null) return null;

            var v = new Vector3[f.Length / 3];
            for (var i = 0; i < v.Length; i++) v[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
            return v;
        }

        static Color[] ReadColor(JToken token)
        {
            var f = ReadFloats(token);
            if (f == null) return null;

            var c = new Color[f.Length / 3];
            for (var i = 0; i < c.Length; i++) c[i] = new Color(f[i * 3], f[i * 3 + 1], f[i * 3 + 2], 1f);
            return c;
        }

        static Matrix4x4 ReadMatrix(JToken token)
        {
            if (token is not JArray a || a.Count < 16) return Matrix4x4.identity;

            var m = new Matrix4x4();
            for (var i = 0; i < 16; i++) m[i] = a[i].Value<float>();
            return m;
        }
    }
}
