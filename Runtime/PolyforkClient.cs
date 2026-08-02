using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Polyfork
{
    /// <summary>
    /// Typed access to the public Polyfork HTTP surface.
    ///
    /// Endpoints (all verified against polyfork.dev):
    ///   GET /api/assets?page=N       paged catalogue, 50 per page
    ///   GET /api/assets/{id}         one asset record
    ///   GET /api/kits                kit list
    ///   GET /cdn/{id}-params.json    machine-readable knob schema
    ///   GET /cdn/{id}-remix.glb?p={} GLB rebuilt with the given range knobs
    /// </summary>
    public class PolyforkClient
    {
        public const string DefaultBaseUrl = "https://polyfork.dev";

        readonly string _baseUrl;

        /// <summary>Optional API key, sent as a bearer token. Unauthenticated access still
        /// returns every asset's public preview GLB, which is what this connector streams.</summary>
        public string ApiKey { get; set; }

        public PolyforkClient(string baseUrl = DefaultBaseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public string BaseUrl => _baseUrl;

        // ---------------------------------------------------------------- catalogue

        public async Task<PolyforkPage> GetPageAsync(int page, CancellationToken ct = default)
        {
            var json = await GetStringAsync($"{_baseUrl}/api/assets?page={page}", ct);
            var root = JObject.Parse(json);
            var result = new PolyforkPage
            {
                Total = root["total"]?.Value<int>() ?? 0,
                Page = root["page"]?.Value<int>() ?? page,
                PerPage = root["per_page"]?.Value<int>() ?? 50
            };
            if (root["assets"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    var a = PolyforkAsset.Parse(t as JObject);
                    if (a != null) result.Assets.Add(a);
                }
            }
            return result;
        }

        /// <summary>Walks every page. The catalogue is ~285 assets across 6 pages.</summary>
        public async Task<List<PolyforkAsset>> GetAllAssetsAsync(
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            var all = new List<PolyforkAsset>();
            var page = 1;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var p = await GetPageAsync(page, ct);
                all.AddRange(p.Assets);

                if (p.Total > 0) progress?.Report(Mathf.Clamp01((float)all.Count / p.Total));
                if (p.Assets.Count == 0 || !p.HasMore) break;
                page++;
            }
            return all;
        }

        public async Task<List<PolyforkKit>> GetKitsAsync(CancellationToken ct = default)
        {
            var json = await GetStringAsync($"{_baseUrl}/api/kits", ct);
            var kits = new List<PolyforkKit>();
            var token = JToken.Parse(json);
            var arr = token as JArray ?? token["kits"] as JArray;
            if (arr != null)
            {
                foreach (var t in arr)
                {
                    var k = PolyforkKit.Parse(t as JObject);
                    if (k != null) kits.Add(k);
                }
            }
            return kits;
        }

        public async Task<PolyforkAsset> GetAssetAsync(string id, CancellationToken ct = default)
            => PolyforkAsset.Parse(JObject.Parse(await GetStringAsync($"{_baseUrl}/api/assets/{id}", ct)));

        // ---------------------------------------------------------------- access

        /// <summary>
        /// Current tier and remaining bake allowance. Answers without a key, so this can be
        /// called at startup to show the allowance rather than discovering it as a 429.
        /// </summary>
        public async Task<PolyforkAccess> GetAccessAsync(CancellationToken ct = default)
            => PolyforkAccess.Parse(await GetStringAsync($"{_baseUrl}/api/me", ct));

        // ---------------------------------------------------------------- knobs

        public async Task<PolyforkParams> GetParamsAsync(string id, CancellationToken ct = default)
            => PolyforkParams.Parse(id, await GetStringAsync(ParamsUrl(id), ct));

        public string ParamsUrl(string id) => $"{_baseUrl}/cdn/{id}-params.json";

        // ---------------------------------------------------------------- geometry

        /// <summary>
        /// URL for a rebuilt GLB. Only range knobs are sent: the endpoint silently ignores
        /// colour, choice and toggle values, so including them would just bust the cache
        /// and imply a change that never happens.
        /// </summary>
        public string RemixGlbUrl(string id, IReadOnlyDictionary<string, float> rangeValues)
        {
            var baseUrl = $"{_baseUrl}/cdn/{id}-remix.glb";
            if (rangeValues == null || rangeValues.Count == 0) return baseUrl;

            var obj = new JObject();
            foreach (var kv in rangeValues) obj[kv.Key] = kv.Value;
            return $"{baseUrl}?p={UnityWebRequest.EscapeURL(obj.ToString(Newtonsoft.Json.Formatting.None))}";
        }

        public Task<byte[]> GetGlbAsync(string url, CancellationToken ct = default) => GetBytesAsync(url, ct);

        // ---------------------------------------------------------------- transport

        public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
            => Encoding.UTF8.GetString(await GetBytesAsync(url, ct));

        public async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
        {
            using var req = UnityWebRequest.Get(url);
            if (!string.IsNullOrEmpty(ApiKey)) req.SetRequestHeader("Authorization", $"Bearer {ApiKey}");
            req.timeout = 30;

            await req.SendWebRequestAsync(ct);

            if (req.responseCode == 429)
                throw new PolyforkRateLimitException(url, ParseRetryAfter(req.GetResponseHeader("Retry-After")));

            if (req.result != UnityWebRequest.Result.Success)
                throw new PolyforkRequestException(url, req.responseCode, req.error);

            // The remix endpoint reports whether it actually applied the parameters.
            // "fallback" means it served the baseline, which is what happens for knob
            // types it does not bake - worth surfacing rather than looking like a no-op.
            var remix = req.GetResponseHeader("x-remix");
            if (!string.IsNullOrEmpty(remix) && remix != "exact")
                LastRemixStatus = remix;

            return req.downloadHandler.data;
        }

        /// <summary>Most recent non-"exact" value of the x-remix header, or null.</summary>
        public string LastRemixStatus { get; private set; }

        static TimeSpan ParseRetryAfter(string header)
        {
            if (!string.IsNullOrEmpty(header))
            {
                if (int.TryParse(header, out var seconds)) return TimeSpan.FromSeconds(seconds);
                if (DateTime.TryParse(header, out var when))
                {
                    var delta = when.ToUniversalTime() - DateTime.UtcNow;
                    if (delta > TimeSpan.Zero) return delta;
                }
            }
            return TimeSpan.FromMinutes(5);
        }

        public async Task<Texture2D> GetTextureAsync(string url, CancellationToken ct = default)
        {
            using var req = UnityWebRequestTexture.GetTexture(url, nonReadable: true);
            if (!string.IsNullOrEmpty(ApiKey)) req.SetRequestHeader("Authorization", $"Bearer {ApiKey}");
            req.timeout = 30;

            await req.SendWebRequestAsync(ct);

            if (req.result != UnityWebRequest.Result.Success)
                throw new PolyforkRequestException(url, req.responseCode, req.error);

            return DownloadHandlerTexture.GetContent(req);
        }
    }

    public class PolyforkRequestException : Exception
    {
        public string Url { get; }
        public long StatusCode { get; }

        public PolyforkRequestException(string url, long statusCode, string error)
            : base($"Polyfork request failed ({statusCode}) for {url}: {error}")
        {
            Url = url;
            StatusCode = statusCode;
        }
    }

    /// <summary>Thrown when Polyfork answers 429. Callers should degrade, not retry hard.</summary>
    public sealed class PolyforkRateLimitException : PolyforkRequestException
    {
        public TimeSpan RetryAfter { get; }

        public PolyforkRateLimitException(string url, TimeSpan retryAfter)
            : base(url, 429, $"rate limited; retry after {retryAfter.TotalSeconds:0}s")
        {
            RetryAfter = retryAfter;
        }
    }

    internal static class UnityWebRequestAwaiterExtensions
    {
        public static Task SendWebRequestAsync(this UnityWebRequest request, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var op = request.SendWebRequest();

            CancellationTokenRegistration reg = default;
            if (ct.CanBeCanceled)
            {
                reg = ct.Register(() =>
                {
                    // Abort() drives the operation to completed with Result.ConnectionError.
                    if (!request.isDone) request.Abort();
                });
            }

            op.completed += _ =>
            {
                reg.Dispose();
                if (ct.IsCancellationRequested) tcs.TrySetCanceled(ct);
                else tcs.TrySetResult(true);
            };

            return tcs.Task;
        }
    }
}
