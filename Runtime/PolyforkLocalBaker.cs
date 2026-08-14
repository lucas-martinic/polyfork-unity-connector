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

        /// <summary>Above this, a local bake has stopped being the fast path and says so.</summary>
        const double SlowBakeMs = 120d;

        /// <summary>
        /// How long the last bake took, split into running the module and decoding what it
        /// returned. Zero until one has run.
        ///
        /// Exposed rather than only logged because the log is threshold-based, and "slower
        /// than it should feel" is not a threshold. A number on screen answers the question
        /// without anyone having to reproduce the problem next to a console.
        /// </summary>
        public double LastEngineMs { get; private set; }

        public double LastDecodeMs { get; private set; }

        public double LastTotalMs => LastEngineMs + LastDecodeMs;

        /// <summary>Size of the last payload crossing the JS boundary, in KB.</summary>
        public int LastPayloadKb { get; private set; }

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
            => asset != null
               && asset.HasModule
               && !asset.HasRig
               && !_unbakeable.Contains(asset.Id);

        /* Rigged assets are excluded up front rather than discovered one failure at a time.
         *
         * Two independent ways of failing, both on rigged models: field-console-a92adc came
         * back with a hierarchy and no meshes, and village-engineer-a44949 threw
         * "TypeError: not a function at buildSkeleton". The trimmed three.js bundle leaves
         * out the skinning classes, so a module that builds a skeleton has nothing to build
         * it with - and no amount of retrying changes that.
         *
         * The server bakes them properly, so this costs a round trip on characters and saves
         * a guaranteed-to-fail bake plus that same round trip on every knob change. */

        /// <summary>
        /// Assets this runtime has already failed on, so it stops offering to try again.
        ///
        /// Without it a knob change on such an asset pays twice every time - a bake that
        /// cannot work, then the server fetch that does - which turns one bad asset into a
        /// permanently sluggish one. Session-scoped: a new window gets to find out for itself,
        /// since the reason may have been a module that had not downloaded yet.
        /// </summary>
        readonly HashSet<string> _unbakeable = new();

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

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var payloadJson = _runtime.Bake(request.Asset.Id, values.ToString());
            var bakeMs = watch.Elapsed.TotalMilliseconds;

            if (string.IsNullOrEmpty(payloadJson))
            {
                _unbakeable.Add(request.Asset.Id);
                throw new PolyforkBakeUnavailableException($"The module for {request.Asset.Id} produced nothing.");
            }

            watch.Restart();
            var payload = PolyforkMeshPayload.Parse(payloadJson);
            var decodeMs = watch.Elapsed.TotalMilliseconds;

            if (payload.Meshes.Count == 0)
            {
                _unbakeable.Add(request.Asset.Id);
                throw new PolyforkBakeUnavailableException(
                    $"The module for {request.Asset.Id} produced no meshes. Rigged assets are the known " +
                    "case: the bridge returns their hierarchy without geometry.");
            }

            /* A local bake is supposed to beat a ~120 ms round trip; when it does not, the
             * split says which half to look at. Silent above the threshold, so a working
             * setup never talks. */
            LastEngineMs = bakeMs;
            LastDecodeMs = decodeMs;
            LastPayloadKb = payloadJson.Length / 1024;

            var totalMs = bakeMs + decodeMs;
            if (totalMs > SlowBakeMs)
            {
                Debug.LogWarning(
                    $"[Polyfork] local bake of {request.Asset.Id} took {totalMs:0} ms " +
                    $"({bakeMs:0} ms in the engine, {decodeMs:0} ms decoding " +
                    $"{payloadJson.Length / 1024} KB). A server bake is about 120 ms.");
            }

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
                _unbakeable.Add(asset.Id);
                Debug.LogWarning($"[Polyfork] could not load the module for {asset.Id} ({e.Message}); " +
                                 "falling back to the server baker.");
                return false;
            }
        }

        Material CreateMaterial()
        {
            if (_materialFactory != null) return _materialFactory();

            /* A Polyfork asset keeps ALL of its colour in COLOR_0 - one material, no
             * textures - and Unity's stock shaders discard vertex colour. URP/Lit,
             * URP/Simple Lit and Standard all do, which is why a locally baked asset came
             * out grey while the same asset fetched as a .glb looked right: glTFast supplies
             * its own vertex-colour material and this path had nothing equivalent.
             *
             * So the package ships one. The stock shaders stay as a fallback, on the grounds
             * that a grey model is better than a magenta one. */
            var shader = Shader.Find("Polyfork/Vertex Color")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

            if (shader == null)
            {
                Debug.LogWarning("[Polyfork] no usable shader was found for locally baked meshes.");
                return null;
            }

            if (shader.name != "Polyfork/Vertex Color")
            {
                Debug.LogWarning(
                    $"[Polyfork] falling back to '{shader.name}' for locally baked meshes, which ignores " +
                    "vertex colours - the model will look grey. Polyfork/Vertex Color was not found.");
            }

            var material = new Material(shader) { name = "Polyfork Vertex Colour" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}
