using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Minimal contract a JavaScript engine must meet to run asset modules.
    ///
    /// Deliberately tiny, because the engine is the only part that differs per platform.
    /// QuickJS is the one that works on Quest: it is a small C engine with existing Unity
    /// Android ARM64 bindings and needs no JIT, so it survives IL2CPP's ahead-of-time
    /// compilation. Jint is pure managed and fine in the editor but leans on reflection
    /// that IL2CPP stripping tends to remove; Jurassic emits IL at runtime and cannot work
    /// on device at all.
    /// </summary>
    public interface IPolyforkJsRuntime : IDisposable
    {
        bool IsReady { get; }

        /// <summary>
        /// Loads the shared runtime once: the trimmed three.js bundle plus the bake bridge.
        /// Roughly 334 KB minified for the 24 classes the catalogue actually uses.
        /// </summary>
        void Initialise(string threeBundle, string bridgeScript);

        /// <summary>Registers an asset module's source under an id.</summary>
        void LoadModule(string moduleId, string source);

        bool HasModule(string moduleId);

        /// <summary>
        /// Runs createAsset with these knob values and returns the flattened payload.
        /// One call per bake regardless of mesh complexity.
        /// </summary>
        string Bake(string moduleId, string paramsJson);

        /// <summary>The module's own params/presets, so no second schema fetch is needed.</summary>
        string Describe(string moduleId);
    }

    /// <summary>
    /// Bakes by running the asset's own createAsset() program, the same one the store runs.
    ///
    /// This outranks the server baker wherever it can be used, because it has none of that
    /// path's limits: every knob is honoured (including the structural choice and toggle
    /// knobs the remix endpoint silently ignores), colours are baked by the program rather
    /// than re-applied afterwards, nothing is metered, and a rebuild costs well under a
    /// millisecond instead of a round trip.
    ///
    /// It applies per asset, not globally: the module is the product, so a caller without a
    /// key has it for free assets and falls back to the server for the rest.
    /// </summary>
    public sealed class PolyforkLocalBaker : IPolyforkBaker
    {
        readonly IPolyforkJsRuntime _runtime;
        readonly PolyforkClient _client;
        readonly Func<Material> _materialFactory;
        readonly HashSet<string> _loaded = new();
        readonly Dictionary<string, Task<bool>> _loading = new();

        public PolyforkLocalBaker(
            IPolyforkJsRuntime runtime, PolyforkClient client, Func<Material> materialFactory = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _materialFactory = materialFactory;
        }

        public string Name => "local module";

        /// <summary>Above the server baker, which is priority 0.</summary>
        public int Priority => 100;

        public bool IsAvailable => _runtime is { IsReady: true };

        public bool ConsumesAllowance => false;

        /// <summary>
        /// Only assets whose module this connection may fetch. That is what the catalogue's
        /// download field reports: free assets publish it to everyone, paid assets need a key.
        /// </summary>
        public bool CanBake(PolyforkAsset asset, PolyforkParams schema)
            => asset != null && asset.HasModule;

        /// <summary>
        /// Everything the schema declares. Running the real program means there is no knob
        /// type this cannot honour - which is the entire point of the local path.
        /// </summary>
        public PolyforkKnobSupport Supports(PolyforkKnob knob)
            => knob == null || knob.Type == PolyforkKnobType.Unknown
                ? PolyforkKnobSupport.Unsupported
                : PolyforkKnobSupport.ServerRebuild;   // "needs a re-bake" - here, a local one

        public async Task<GameObject> BakeAsync(PolyforkBakeRequest request, CancellationToken ct = default)
        {
            if (request?.Asset == null) return null;
            if (!await EnsureModuleAsync(request.Asset, ct)) return null;

            ct.ThrowIfCancellationRequested();

            // Send only what differs from the defaults; the module fills the rest in itself.
            var values = request.Values.WithoutDefaults(request.Schema);
            var payloadJson = _runtime.Bake(request.Asset.Id, values.ToString());

            if (string.IsNullOrEmpty(payloadJson))
                throw new PolyforkBakeUnavailableException($"The module for {request.Asset.Id} produced nothing.");

            var payload = PolyforkMeshPayload.Parse(payloadJson);
            if (payload.Meshes.Count == 0)
                throw new PolyforkBakeUnavailableException($"The module for {request.Asset.Id} produced no meshes.");

            return payload.ToGameObject(CreateMaterial(), request.Parent, $"Polyfork_{request.Asset.Id}");
        }

        /// <summary>Fetches and registers the module once, coalescing concurrent requests.</summary>
        async Task<bool> EnsureModuleAsync(PolyforkAsset asset, CancellationToken ct)
        {
            if (_loaded.Contains(asset.Id)) return true;

            Task<bool> pending;
            lock (_loading)
            {
                if (!_loading.TryGetValue(asset.Id, out pending))
                {
                    pending = FetchModuleAsync(asset, ct);
                    _loading[asset.Id] = pending;
                }
            }

            var ok = await pending;
            lock (_loading) _loading.Remove(asset.Id);
            return ok;
        }

        async Task<bool> FetchModuleAsync(PolyforkAsset asset, CancellationToken ct)
        {
            try
            {
                var source = await _client.GetStringAsync(asset.Download.Mjs, ct);
                _runtime.LoadModule(asset.Id, source);
                _loaded.Add(asset.Id);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] could not load the module for {asset.Id} ({e.Message}); " +
                                 "falling back to the server baker.");
                return false;
            }
        }

        Material CreateMaterial()
        {
            if (_materialFactory != null) return _materialFactory();

            // Polyfork assets are flat-shaded with baked vertex colours and no textures, so
            // the material only has to multiply base colour by the vertex colour.
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

            var material = new Material(shader) { name = "Polyfork Vertex Colour" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}
