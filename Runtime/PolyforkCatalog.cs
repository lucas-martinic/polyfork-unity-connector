using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Scene-level entry point: owns the client, the loader and a warm queue of
    /// ready-to-show assets so anything user-facing can pull instantly.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Polyfork/Polyfork Catalog")]
    public sealed class PolyforkCatalog : MonoBehaviour
    {
        [Header("Connection")]
        [Tooltip("Base URL of the Polyfork instance.")]
        [SerializeField] string baseUrl = PolyforkClient.DefaultBaseUrl;

        [Tooltip("Optional API key. Prefer the POLYFORK_API_KEY environment variable or a " +
                 "polyfork.key file - a value typed here is serialised into the scene and committed. " +
                 "Leave empty to use public preview GLBs, which cover the whole catalogue.")]
        [SerializeField] string apiKey = "";

        [Header("Catalogue filter")]
        [Tooltip("Only surface assets whose knobs can be remixed. Recommended for the showcase.")]
        [SerializeField] bool remixableOnly = true;

        [Tooltip("Skip anything heavier than this. Polyfork averages ~742 triangles; 0 disables the filter.")]
        [SerializeField] int maxTriangles = 3000;

        [Header("Prefetch")]
        [Tooltip("How many upcoming assets to download and parse ahead of time.")]
        [SerializeField, Range(1, 24)] int warmQueueSize = 8;

        [Tooltip("Concurrent downloads. Quest handles a handful comfortably.")]
        [SerializeField, Range(1, 8)] int maxConcurrentPrefetch = 3;

        [Header("Remix budget")]
        [Tooltip("Cap on remix rebuilds per hour when no API key is set. Polyfork may limit " +
                 "unauthenticated remixes (~40/hour); staying under it avoids a mid-session 429. " +
                 "Ignored when an API key is present.")]
        [SerializeField] int remixRequestsPerHour = 32;

        public PolyforkClient Client { get; private set; }
        public PolyforkGlbLoader Loader { get; private set; }

        /// <summary>Guards remix rebuilds. Unlimited once an API key is attached.</summary>
        public PolyforkRemixBudget RemixBudget { get; private set; }

        /// <summary>Every asset that passed the filter, in shuffled order.</summary>
        public IReadOnlyList<PolyforkAsset> Assets => _assets;

        public bool Ready { get; private set; }

        public event Action Loaded;
        public event Action<string> LoadFailed;

        readonly List<PolyforkAsset> _assets = new();
        readonly Dictionary<string, PolyforkParams> _paramCache = new();
        readonly Queue<PolyforkAsset> _upcoming = new();

        CancellationTokenSource _cts;
        int _cursor;
        int _activePrefetch;

        void Awake()
        {
            var key = PolyforkCredentials.Resolve(apiKey, out var keySource);

            Client = new PolyforkClient(baseUrl) { ApiKey = key };
            Loader = new PolyforkGlbLoader(Client);

            // The unauthenticated remix cap does not apply to an authenticated connection.
            RemixBudget = new PolyforkRemixBudget(key != null ? 0 : remixRequestsPerHour);

            Debug.Log(key != null
                ? $"[Polyfork] API key {PolyforkCredentials.Redact(key)} from {keySource}; remix budget unlimited."
                : $"[Polyfork] no API key; public previews only, remix budget {remixRequestsPerHour}/hour.");

            if (keySource == PolyforkCredentials.Source.Inspector)
            {
                Debug.LogWarning(
                    "[Polyfork] the API key is set on the component, so it is saved into the scene asset. " +
                    $"Move it to the {PolyforkCredentials.EnvironmentVariable} environment variable or a " +
                    $"{PolyforkCredentials.KeyFileName} file before committing.");
            }

            _cts = new CancellationTokenSource();
        }

        async void Start()
        {
            try
            {
                var all = await Client.GetAllAssetsAsync(null, _cts.Token);

                var filtered = all.Where(a => !string.IsNullOrEmpty(a.PreviewGlb));
                if (remixableOnly) filtered = filtered.Where(a => a.Remixable);
                if (maxTriangles > 0) filtered = filtered.Where(a => a.Triangles <= maxTriangles);

                _assets.AddRange(filtered);
                Shuffle(_assets);

                if (_assets.Count == 0)
                {
                    LoadFailed?.Invoke("No Polyfork assets matched the filter.");
                    return;
                }

                Ready = true;
                Debug.Log($"[Polyfork] catalogue ready: {_assets.Count} assets (of {all.Count} published).");
                Loaded?.Invoke();

                PumpPrefetch();
            }
            catch (OperationCanceledException)
            {
                // Scene tore down mid-load.
            }
            catch (Exception e)
            {
                Debug.LogError($"[Polyfork] catalogue load failed: {e.Message}");
                LoadFailed?.Invoke(e.Message);
            }
        }

        void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        /// <summary>Next asset in the rotation. Cycles forever.</summary>
        public PolyforkAsset Next()
        {
            if (_assets.Count == 0) return null;
            var a = _assets[_cursor % _assets.Count];
            _cursor++;
            PumpPrefetch();
            return a;
        }

        public PolyforkAsset Peek(int offset = 0)
            => _assets.Count == 0 ? null : _assets[(_cursor + offset) % _assets.Count];

        /// <summary>
        /// Knob schema for an asset, cached. Returns null when the asset publishes none.
        /// </summary>
        public async Task<PolyforkParams> GetParamsAsync(string assetId, CancellationToken ct = default)
        {
            if (_paramCache.TryGetValue(assetId, out var cached)) return cached;

            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
                var schema = await Client.GetParamsAsync(assetId, linked.Token);
                _paramCache[assetId] = schema;
                return schema;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] no knob schema for {assetId}: {e.Message}");
                _paramCache[assetId] = null;
                return null;
            }
        }

        /// <summary>Base (unmodified) GLB URL for an asset.</summary>
        public string BaseGlbUrl(PolyforkAsset asset) => asset?.PreviewGlb;

        /// <summary>
        /// Warms the disk cache for the next few assets so pulling one is instant.
        /// </summary>
        void PumpPrefetch()
        {
            if (!Ready) return;

            for (var i = 0; i < warmQueueSize; i++)
            {
                if (_activePrefetch >= maxConcurrentPrefetch) return;

                var asset = Peek(i);
                if (asset == null) return;
                if (_upcoming.Contains(asset)) continue;

                _upcoming.Enqueue(asset);
                _ = PrefetchAsync(asset);
            }
        }

        async Task PrefetchAsync(PolyforkAsset asset)
        {
            _activePrefetch++;
            try
            {
                var url = BaseGlbUrl(asset);
                if (!string.IsNullOrEmpty(url)) await Loader.GetBytesAsync(url, _cts.Token);
                if (asset.Remixable) await GetParamsAsync(asset.Id, _cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] prefetch failed for {asset.Id}: {e.Message}");
            }
            finally
            {
                _activePrefetch--;
                if (_upcoming.Count > 0) _upcoming.Dequeue();
            }
        }

        static void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
