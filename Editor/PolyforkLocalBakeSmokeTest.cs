using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Exercises the whole local-baking path against a real JS engine.
    ///
    /// Everything up to this point was verified in Node, which is not the engine that
    /// ships. This is the first thing that actually starts QuickJS, evaluates the bundle,
    /// runs a catalogue module and decodes the result into a Unity mesh - so it is where
    /// engine-specific problems surface, well before spending a device build on them.
    ///
    /// Uses a free asset, so it needs no key.
    /// </summary>
    public static class PolyforkLocalBakeSmokeTest
    {
        const string FreeAssetId = "street-lamp-29f365";

        [MenuItem("Polyfork/Diagnostics/Smoke-test local baking", priority = 100)]
        public static void Run() => _ = RunAsync();

        /// <summary>Greyed out unless a JS engine is actually installed, so the menu does not
        /// offer to test something this project cannot do.</summary>
        [MenuItem("Polyfork/Diagnostics/Smoke-test local baking", validate = true)]
        static bool CanRun() => PolyforkJsRuntimeProvider.IsAvailable;

        public static async Task RunAsync()
        {
            if (!PolyforkJsRuntimeProvider.IsAvailable)
            {
                Debug.LogWarning("[Polyfork] no JS engine is installed, so local baking is unavailable. " +
                                 "Install the Puerts core and QuickJS packages to enable it.");
                return;
            }

            IPolyforkJsRuntime runtime = null;
            try
            {
                var sw = Stopwatch.StartNew();
                runtime = PolyforkJsRuntimeProvider.TryCreate();
                sw.Stop();

                if (runtime == null)
                {
                    Debug.LogError("[Polyfork] the JS runtime did not start.");
                    return;
                }

                Debug.Log($"[Polyfork] {PolyforkJsRuntimeProvider.EngineName} started and parsed " +
                          $"the three.js bundle in {sw.ElapsedMilliseconds} ms.");

                var client = new PolyforkClient { ApiKey = PolyforkCredentials.Resolve(null) };
                var asset = await client.GetAssetAsync(FreeAssetId);

                if (!asset.HasModule)
                {
                    Debug.LogWarning($"[Polyfork] {FreeAssetId} publishes no module for this connection.");
                    return;
                }

                var source = await client.GetStringAsync(asset.Download.Mjs);
                sw.Restart();
                runtime.LoadModule(asset.Id, source);
                sw.Stop();
                Debug.Log($"[Polyfork] module registered in {sw.ElapsedMilliseconds} ms " +
                          $"({source.Length / 1024f:0.0} KB of source).");

                // Defaults, then a structural knob the remix endpoint refuses to bake.
                Report(runtime, asset.Id, "{}", "defaults");
                Report(runtime, asset.Id, "{\"postSides\":\"square\"}", "postSides=square");

                sw.Restart();
                const int iterations = 20;
                for (var i = 0; i < iterations; i++) runtime.Bake(asset.Id, "{\"tallness\":1.0}");
                sw.Stop();
                Debug.Log($"[Polyfork] {sw.Elapsed.TotalMilliseconds / iterations:0.00} ms per bake " +
                          $"(a server rebuild is roughly 120 ms).");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Polyfork] local bake smoke test failed: {e}");
            }
            finally
            {
                runtime?.Dispose();
            }
        }

        static void Report(IPolyforkJsRuntime runtime, string assetId, string paramsJson, string label)
        {
            var sw = Stopwatch.StartNew();
            var json = runtime.Bake(assetId, paramsJson);
            sw.Stop();

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[Polyfork] {label}: the module produced nothing.");
                return;
            }

            var payload = PolyforkMeshPayload.Parse(json);
            Debug.Log($"[Polyfork] {label}: meshes={payload.Meshes.Count} " +
                      $"verts={payload.TotalVertices} tris={payload.TotalTriangles} " +
                      $"payload={json.Length / 1024f:0.0} KB in {sw.ElapsedMilliseconds} ms");
        }
    }
}
