using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Loads Polyfork GLBs into scene objects, with a two-tier cache:
    /// bytes on disk (survives app restarts) and parsed results in memory.
    /// </summary>
    public sealed class PolyforkGlbLoader
    {
        readonly PolyforkClient _client;
        readonly string _cacheDir;
        readonly Dictionary<string, Task<byte[]>> _inFlight = new();

        public PolyforkGlbLoader(PolyforkClient client, string cacheDir = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _cacheDir = cacheDir ?? Path.Combine(Application.persistentDataPath, "polyfork-glb");
            Directory.CreateDirectory(_cacheDir);
        }

        public string CacheDirectory => _cacheDir;

        /// <summary>
        /// True when this URL can be served without touching the network. Lets callers
        /// decide whether a fetch needs to be counted against a request budget.
        /// </summary>
        public bool IsCached(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            lock (_inFlight)
            {
                if (_inFlight.TryGetValue(url, out var task) &&
                    task.Status == TaskStatus.RanToCompletion) return true;
            }

            var path = Path.Combine(_cacheDir, CacheKey(url) + ".glb");
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }

        /// <summary>Downloads (or reads from disk) the GLB bytes for a URL.</summary>
        public Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
        {
            lock (_inFlight)
            {
                if (_inFlight.TryGetValue(url, out var existing) && !existing.IsFaulted && !existing.IsCanceled)
                    return existing;

                var task = FetchAsync(url, ct);
                _inFlight[url] = task;
                return task;
            }
        }

        async Task<byte[]> FetchAsync(string url, CancellationToken ct)
        {
            var path = Path.Combine(_cacheDir, CacheKey(url) + ".glb");

            try
            {
                if (File.Exists(path))
                {
                    var cached = await File.ReadAllBytesAsync(path, ct);
                    if (cached.Length > 0) return cached;
                }
            }
            catch (IOException)
            {
                // Unreadable cache entry is not fatal: fall through and refetch.
            }

            var bytes = await _client.GetGlbAsync(url, ct);

            try
            {
                await File.WriteAllBytesAsync(path, bytes, CancellationToken.None);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[Polyfork] could not cache {url}: {e.Message}");
            }

            return bytes;
        }

        /// <summary>
        /// Instantiates a GLB under <paramref name="parent"/> and returns the new root.
        /// </summary>
        public async Task<GameObject> LoadAsync(
            string url, Transform parent = null, CancellationToken ct = default)
        {
            var bytes = await GetBytesAsync(url, ct);
            ct.ThrowIfCancellationRequested();
            return await InstantiateAsync(bytes, url, parent, ct);
        }

        public async Task<GameObject> InstantiateAsync(
            byte[] bytes, string sourceUri, Transform parent = null, CancellationToken ct = default)
        {
            // Outside play mode glTFast's default defer agent spawns a "glTF-StableFramerate"
            // object and calls DontDestroyOnLoad, which is illegal in the editor and aborts
            // the import. UninterruptedDeferAgent does the work in one go instead, which is
            // what an editor-side load wants anyway.
            var deferAgent = Application.isPlaying ? null : new UninterruptedDeferAgent();
            var gltf = new GltfImport(deferAgent: deferAgent);

            var settings = new ImportSettings
            {
                GenerateMipMaps = false,
                AnisotropicFilterLevel = 0,
                // Polyfork assets are flat-shaded with baked vertex colours and no textures,
                // so there is nothing to gain from texture-side work.
                DefaultMinFilterMode = GLTFast.Schema.Sampler.MinFilterMode.Nearest,
                DefaultMagFilterMode = GLTFast.Schema.Sampler.MagFilterMode.Nearest
            };

            var ok = await gltf.Load(bytes, new Uri(sourceUri), settings, ct);
            if (!ok) throw new PolyforkLoadException($"glTF import failed for {sourceUri}");

            ct.ThrowIfCancellationRequested();

            var root = new GameObject("PolyforkAsset");
            if (parent != null) root.transform.SetParent(parent, false);

            var instantiated = await gltf.InstantiateMainSceneAsync(root.transform, ct);
            if (!instantiated)
            {
                DestroyObject(root);
                throw new PolyforkLoadException($"glTF instantiation failed for {sourceUri}");
            }

            // In the editor these are transient (preview / import staging), so keep the whole
            // subtree out of the open scene and out of the user's save. Applied after
            // instantiation so glTFast's children are covered too.
            if (!Application.isPlaying)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            return root;
        }

        /// <summary>
        /// Destroy() is deferred to the end of the frame and does nothing outside play mode,
        /// so editor-side callers need DestroyImmediate.
        /// </summary>
        static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        static string CacheKey(string url)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
            var sb = new StringBuilder(32);
            for (var i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        public void ClearMemory()
        {
            lock (_inFlight) _inFlight.Clear();
        }

        public void ClearDisk()
        {
            try
            {
                if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, true);
                Directory.CreateDirectory(_cacheDir);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[Polyfork] could not clear GLB cache: {e.Message}");
            }
        }
    }

    public sealed class PolyforkLoadException : Exception
    {
        public PolyforkLoadException(string message) : base(message) { }
    }
}
