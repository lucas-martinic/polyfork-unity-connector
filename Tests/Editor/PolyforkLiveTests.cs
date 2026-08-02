using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Polyfork.Tests
{
    /// <summary>
    /// End-to-end tests against the live polyfork.dev API.
    ///
    /// Marked [Category("Network")] so they can be excluded in CI or offline - filter the
    /// Test Runner by category to skip them. They are the ones that would catch the API
    /// changing under the integration.
    /// </summary>
    [Category("Network")]
    public class PolyforkLiveTests
    {
        const string DrumId = "plastic-drum-da992f";

        static PolyforkClient NewClient() =>
            new() { ApiKey = PolyforkCredentials.Resolve(null) };

        /// <summary>
        /// Bridges a Task into a UnityTest coroutine and rethrows failures.
        /// Logs the full AggregateException first: GetBaseException keeps the message but
        /// discards the inner stack, which is the part that says where it actually broke.
        /// </summary>
        static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted) yield return null;
            if (!task.IsFaulted) yield break;

            // Warning, not error: an error log is itself treated as a test failure by Unity,
            // which would mask the real assertion. The throw below is what fails the test.
            Debug.LogWarning($"[Polyfork] awaited task faulted:\n{task.Exception}");
            throw task.Exception?.GetBaseException() ?? new System.Exception("task failed");
        }

        [UnityTest]
        public IEnumerator CatalogueLoadsEveryPage()
        {
            var task = NewClient().GetAllAssetsAsync();
            yield return Await(task);

            var assets = task.Result;
            Assert.Greater(assets.Count, 250, "the catalogue should be a few hundred assets");
            Assert.IsTrue(assets.All(a => !string.IsNullOrEmpty(a.Id)));
            Assert.Greater(assets.Count(a => a.Remixable), 100, "most of the catalogue is remixable");

            Debug.Log($"[Polyfork] catalogue: {assets.Count} assets, " +
                      $"{assets.Count(a => a.Free)} free, {assets.Count(a => a.Remixable)} remixable.");
        }

        [UnityTest]
        public IEnumerator KnobSchemaDownloadsAndClassifies()
        {
            var task = NewClient().GetParamsAsync(DrumId);
            yield return Await(task);

            var schema = task.Result;
            Assert.IsNotNull(schema.Knobs["tallness"], "tallness is a published knob on the drum");
            Assert.AreEqual(PolyforkKnobSupport.ServerRebuild, schema.Knobs["tallness"].Support);
            Assert.Greater(schema.Remixable.Count(), 0);
        }

        [UnityTest]
        public IEnumerator RangeKnobActuallyChangesTheMesh()
        {
            var client = NewClient();

            var baseline = client.GetGlbAsync(client.RemixGlbUrl(DrumId, null));
            yield return Await(baseline);

            var taller = client.GetGlbAsync(client.RemixGlbUrl(
                DrumId, new Dictionary<string, float> { ["tallness"] = 1.12f }));
            yield return Await(taller);

            Assert.AreNotEqual(baseline.Result.Length, taller.Result.Length,
                "a range knob is baked server-side, so the GLB must differ");
        }

        [UnityTest]
        public IEnumerator ColorKnobIsIgnoredByTheEndpoint()
        {
            // Pins the platform behaviour the local recolour path exists to work around.
            // If this ever fails, the endpoint started baking colours - reclassify and
            // the client can hand colour knobs to the server instead.
            var client = NewClient();

            var baseline = client.GetGlbAsync($"{client.BaseUrl}/cdn/{DrumId}-remix.glb?p=%7B%7D");
            yield return Await(baseline);

            var coloured = client.GetGlbAsync(
                $"{client.BaseUrl}/cdn/{DrumId}-remix.glb?p=" +
                UnityEngine.Networking.UnityWebRequest.EscapeURL("{\"body\":\"#FF6600\"}"));
            yield return Await(coloured);

            Assert.AreEqual(baseline.Result.Length, coloured.Result.Length,
                "colour knobs are not baked by the remix endpoint");
        }

        [UnityTest]
        public IEnumerator VertexColorsMatchTheDeclaredSlots()
        {
            var client = NewClient();
            var loader = new PolyforkGlbLoader(client);

            var schemaTask = client.GetParamsAsync(DrumId);
            yield return Await(schemaTask);
            var schema = schemaTask.Result;

            var url = client.RemixGlbUrl(DrumId, null);
            var loadTask = loader.LoadAsync(url);
            yield return Await(loadTask);

            var go = loadTask.Result;
            try
            {
                var slots = PolyforkColorSlots.Build(go, schema);
                Assert.IsTrue(slots.HasSlots,
                    "the GLB's distinct vertex colours should map onto the declared colour knobs");
                CollectionAssert.Contains(slots.SlotNames, "body");

                // Recolouring must not throw and must leave the mesh intact.
                slots.Apply(new Dictionary<string, Color> { ["body"] = Color.magenta });

                var filter = go.GetComponentInChildren<MeshFilter>();
                Assert.IsNotNull(filter);
                Assert.Greater(filter.sharedMesh.vertexCount, 0);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator FlatShadingSurvivesImport()
        {
            // Polyfork geometry is deliberately non-indexed: one vertex per triangle corner,
            // and no NORMAL attribute at all (plastic-drum is 616 tris / 1848 positions).
            // Flat facets are a property of that topology, so anything that welds or merges
            // vertices silently turns the whole catalogue into smooth gradients.
            //
            // glTFast does not weld today. This test exists so that if an importer setting
            // or a package upgrade ever starts to, the suite says so instead of the bug
            // reaching someone's project.
            var client = NewClient();
            var loader = new PolyforkGlbLoader(client);

            var task = loader.LoadAsync(client.RemixGlbUrl(DrumId, null));
            yield return Await(task);

            var go = task.Result;
            try
            {
                var filter = go.GetComponentInChildren<MeshFilter>();
                Assert.IsNotNull(filter, "the drum should import as a mesh");

                var mesh = filter.sharedMesh;
                var triangles = mesh.triangles.Length / 3;

                Assert.AreEqual(triangles * 3, mesh.vertexCount,
                    "vertices were merged: flat shading depends on one vertex per triangle corner");

                // Per-face normals: the three corners of a triangle must agree, and
                // neighbouring faces must not have been averaged together.
                var normals = mesh.normals;
                Assert.AreEqual(mesh.vertexCount, normals.Length, "normals should be generated per vertex");

                var tris = mesh.triangles;
                var distinct = new List<Vector3>();
                for (var t = 0; t < tris.Length; t += 3)
                {
                    var n0 = normals[tris[t]];
                    Assert.AreEqual(1f, Vector3.Dot(n0, normals[tris[t + 1]]), 0.01f,
                        "a triangle's own corners must share one normal (flat face)");
                    Assert.AreEqual(1f, Vector3.Dot(n0, normals[tris[t + 2]]), 0.01f,
                        "a triangle's own corners must share one normal (flat face)");

                    if (!distinct.Any(d => Vector3.Dot(d, n0) > 0.999f)) distinct.Add(n0);
                }

                Assert.Greater(distinct.Count, 4,
                    "a faceted drum should present many distinct face normals, not a smoothed shell");

                Debug.Log($"[Polyfork] flat shading intact: {triangles} tris, " +
                          $"{mesh.vertexCount} verts, {distinct.Count} distinct face normals.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator AccessReportsAnAllowanceWithoutAKey()
        {
            // /api/me answers unauthenticated, which is what lets the package show the
            // remaining allowance up front instead of discovering it as a 429.
            var task = new PolyforkClient().GetAccessAsync();
            yield return Await(task);

            var access = task.Result;
            Assert.IsNotNull(access.Plan);
            Assert.IsNotNull(access.Remaining, "an anonymous caller should still get a figure");
            Assert.Greater(access.Remaining.Value, -1);

            Debug.Log($"[Polyfork] {access.Describe()}");
        }

        [UnityTest]
        public IEnumerator ImportWritesAGlbIntoTheProject()
        {
            var client = NewClient();
            var loader = new PolyforkGlbLoader(client);

            var assetTask = client.GetAssetAsync(DrumId);
            yield return Await(assetTask);

            var schemaTask = client.GetParamsAsync(DrumId);
            yield return Await(schemaTask);

            var schema = schemaTask.Result;
            var colors = schema.DefaultSlotColors();
            colors["body"] = Color.magenta;              // force the recolour + re-export path

            var importTask = EditorTools.PolyforkAssetImporter.ImportAsync(
                client, loader, assetTask.Result, schema,
                new Dictionary<string, float> { ["tallness"] = 1.12f },
                colors,
                "Assets/Polyfork.Tests");

            yield return Await(importTask);

            var result = importTask.Result;
            Assert.IsTrue(result.Success, $"import failed: {result.Error}");
            Assert.IsTrue(System.IO.File.Exists(result.AssetPath), "the .glb should be on disk");
            Assert.IsTrue(result.ColorsBaked, "a changed colour should be baked into the saved file");

            Debug.Log($"[Polyfork] imported {result.AssetPath}");

            UnityEditor.AssetDatabase.DeleteAsset(result.AssetPath);
            UnityEditor.AssetDatabase.DeleteAsset("Assets/Polyfork.Tests");
        }
    }
}
