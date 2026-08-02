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

        /// <summary>Bridges a Task into a UnityTest coroutine and rethrows failures.</summary>
        static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception?.GetBaseException() ?? new System.Exception("task failed");
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
