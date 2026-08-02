using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// A spawned Polyfork asset that can be remixed at runtime through its real knobs.
    ///
    /// Two paths, matching what the platform actually supports:
    ///   * range knobs  -> refetch /cdn/{id}-remix.glb?p={...} and swap the mesh (~120 ms).
    ///   * colour knobs -> recolour vertex slots in place (same frame).
    /// Knobs the platform cannot honour are never exposed; see PolyforkKnobSupport.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Polyfork/Polyfork Remixable")]
    public sealed class PolyforkRemixable : MonoBehaviour
    {
        public PolyforkAsset Asset { get; private set; }
        public PolyforkParams Schema { get; private set; }
        public PolyforkCatalog Catalog { get; private set; }

        /// <summary>Root of the currently displayed GLB hierarchy.</summary>
        public GameObject Model { get; private set; }

        /// <summary>Raised after a geometry rebuild swaps the mesh.</summary>
        public event Action<PolyforkRemixable> ModelChanged;

        /// <summary>Raised when a geometry rebuild starts or finishes.</summary>
        public event Action<bool> BusyChanged;

        public bool IsBusy { get; private set; }

        /// <summary>Knobs safe to show in UI, already ordered for display.</summary>
        public IReadOnlyList<PolyforkKnob> RemixableKnobs { get; private set; } = Array.Empty<PolyforkKnob>();

        readonly Dictionary<string, float> _ranges = new();
        readonly Dictionary<string, Color> _slotColors = new();
        string _activeColorway;

        PolyforkColorSlots _slots;
        CancellationTokenSource _cts;
        int _rebuildGeneration;

        /// <summary>Coalesces slider drags into one request per settle.</summary>
        [SerializeField, Range(0f, 0.5f)] float rebuildDebounceSeconds = 0.12f;

        [Tooltip("Discrete positions each range knob snaps to. Fewer stops means fewer " +
                 "distinct rebuilds, all of which can be prewarmed into the cache so " +
                 "dragging costs nothing and never stutters. Raise it for a more continuous " +
                 "feel once an API key removes the request cap.")]
        [SerializeField, Range(3, 12)] int stopsPerRangeKnob = 5;

        [Tooltip("Re-warm the neighbouring variants after each rebuild, so moving a second " +
                 "knob stays instant. Costs knobs x stops per move; safe with an API key.")]
        [SerializeField] bool prewarmAfterRebuild = true;

        float _pendingRebuildAt = -1f;

        readonly Dictionary<string, float[]> _stops = new();

        public IReadOnlyDictionary<string, float> RangeValues => _ranges;
        public IReadOnlyDictionary<string, Color> SlotColors => _slotColors;
        public string ActiveColorway => _activeColorway;

        void Awake() => _cts = new CancellationTokenSource();

        void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        void Update()
        {
            if (_pendingRebuildAt >= 0f && Time.unscaledTime >= _pendingRebuildAt)
            {
                _pendingRebuildAt = -1f;
                _ = RebuildGeometryAsync();
            }
        }

        /// <summary>
        /// Adopts an already-loaded model. Called by whatever spawned the asset.
        /// </summary>
        public void Initialise(PolyforkCatalog catalog, PolyforkAsset asset, PolyforkParams schema, GameObject model)
        {
            Catalog = catalog;
            Asset = asset;
            Schema = schema;
            Model = model;

            RemixableKnobs = schema?.Remixable.ToArray() ?? Array.Empty<PolyforkKnob>();

            _ranges.Clear();
            _slotColors.Clear();
            _activeColorway = null;

            if (schema != null)
            {
                foreach (var knob in schema.All)
                {
                    switch (knob.Support)
                    {
                        case PolyforkKnobSupport.ServerRebuild:
                            _ranges[knob.Name] = knob.DefaultFloat;
                            break;
                        case PolyforkKnobSupport.LocalRecolor when knob.Type == PolyforkKnobType.Color:
                            if (PolyforkParams.TryParseHex(knob.DefaultString, out var c))
                                _slotColors[knob.Name] = c;
                            break;
                        case PolyforkKnobSupport.LocalRecolor when knob.Type == PolyforkKnobType.Choice:
                            _activeColorway ??= knob.DefaultString;
                            break;
                    }
                }
            }

            BindSlots();
        }

        void BindSlots()
        {
            _slots = Model != null && Schema != null
                ? PolyforkColorSlots.Build(Model, Schema)
                : null;
        }

        // ------------------------------------------------------------ colour knobs

        /// <summary>Sets one colour slot. Applies this frame, no network.</summary>
        public void SetColor(string knobName, Color color)
        {
            if (Schema == null || !Schema.Knobs.TryGetValue(knobName, out var knob)) return;
            if (knob.Support != PolyforkKnobSupport.LocalRecolor || knob.Type != PolyforkKnobType.Color) return;

            _slotColors[knobName] = color;
            // An explicit colour edit means the model no longer matches a curated colourway.
            _activeColorway = null;
            _slots?.Apply(_slotColors);
        }

        /// <summary>
        /// Selects a curated colourway, writing every slot it defines. Applies this frame.
        /// </summary>
        public void SetColorway(string knobName, string presetName)
        {
            if (Schema == null || !Schema.Knobs.TryGetValue(knobName, out var knob)) return;
            if (knob.Support != PolyforkKnobSupport.LocalRecolor || knob.Type != PolyforkKnobType.Choice) return;
            if (!Schema.TryGetPreset(presetName, out var slots)) return;

            foreach (var kv in slots)
            {
                if (PolyforkParams.TryParseHex(kv.Value, out var c)) _slotColors[kv.Key] = c;
            }

            _activeColorway = presetName;
            _slots?.Apply(_slotColors);
        }

        // ------------------------------------------------------------ geometry knobs

        /// <summary>
        /// Sets a range knob. The value snaps to one of the knob's discrete stops and the
        /// rebuild is debounced, so a slider drag resolves to one request at most - and to
        /// none at all once <see cref="PrewarmAsync"/> has run.
        /// </summary>
        public void SetRange(string knobName, float value)
        {
            if (Schema == null || !Schema.Knobs.TryGetValue(knobName, out var knob)) return;
            if (knob.Support != PolyforkKnobSupport.ServerRebuild) return;

            var snapped = Snap(knob, value);
            if (_ranges.TryGetValue(knobName, out var current) && Mathf.Approximately(current, snapped)) return;

            _ranges[knobName] = snapped;
            _pendingRebuildAt = Time.unscaledTime + rebuildDebounceSeconds;
        }

        public float GetRange(string knobName)
            => _ranges.TryGetValue(knobName, out var v) ? v : 0f;

        /// <summary>
        /// The discrete values a range knob can take. Integral knobs with a small span use
        /// every value; everything else is sampled evenly across min..max.
        /// </summary>
        public float[] GetStops(PolyforkKnob knob)
        {
            if (knob == null || !knob.HasRange) return Array.Empty<float>();
            if (_stops.TryGetValue(knob.Name, out var cached)) return cached;

            float[] stops;
            var span = knob.Max - knob.Min;

            if (knob.IsIntegral && span + 1 <= stopsPerRangeKnob * 2)
            {
                var count = Mathf.RoundToInt(span) + 1;
                stops = new float[count];
                for (var i = 0; i < count; i++) stops[i] = knob.Min + i;
            }
            else
            {
                var count = Mathf.Max(2, stopsPerRangeKnob);
                stops = new float[count];
                for (var i = 0; i < count; i++)
                {
                    var v = Mathf.Lerp(knob.Min, knob.Max, i / (float)(count - 1));
                    if (knob.IsIntegral) v = Mathf.Round(v);
                    else if (knob.Step > 0f) v = Mathf.Round(v / knob.Step) * knob.Step;
                    stops[i] = v;
                }
            }

            _stops[knob.Name] = stops;
            return stops;
        }

        float Snap(PolyforkKnob knob, float value)
        {
            var stops = GetStops(knob);
            if (stops.Length == 0) return Mathf.Clamp(value, knob.Min, knob.Max);

            var best = stops[0];
            var bestDelta = Mathf.Abs(value - best);
            for (var i = 1; i < stops.Length; i++)
            {
                var delta = Mathf.Abs(value - stops[i]);
                if (delta >= bestDelta) continue;
                bestDelta = delta;
                best = stops[i];
            }
            return best;
        }

        /// <summary>
        /// Warms the cache one axis at a time around the knob values that are set *right now*.
        ///
        /// Knobs combine, so the full variant space is a product - plastic-drum alone is
        /// tallness(7) x facets(8) x taper(7) = 392 GLBs - and prefetching that would be
        /// wasteful and would trip any rate limit immediately. Instead this walks each range
        /// knob's stops while holding the others at their current values: linear in
        /// knobs x stops (typically 5-15 requests), covering every single-slider move the
        /// user can make from where they are.
        ///
        /// Because the base shifts when a knob changes, this is re-run after each rebuild so
        /// the next axis is warm again. Combinations two moves ahead are still a live fetch,
        /// which the debounce and the ~120 ms round trip absorb.
        /// </summary>
        public async Task PrewarmAsync(CancellationToken ct = default)
        {
            if (Catalog == null || Asset == null || Schema == null) return;

            var budget = Catalog.RemixBudget;
            var loader = Catalog.Loader;

            // Everything currently off-default forms the base each axis is explored from.
            var basis = new Dictionary<string, float>();
            foreach (var kv in _ranges)
            {
                if (Schema.Knobs.TryGetValue(kv.Key, out var k) &&
                    !Mathf.Approximately(kv.Value, k.DefaultFloat)) basis[kv.Key] = kv.Value;
            }

            foreach (var knob in Schema.All)
            {
                if (knob.Support != PolyforkKnobSupport.ServerRebuild) continue;

                foreach (var stop in GetStops(knob))
                {
                    if (ct.IsCancellationRequested) return;

                    var payload = new Dictionary<string, float>(basis);
                    if (Mathf.Approximately(stop, knob.DefaultFloat)) payload.Remove(knob.Name);
                    else payload[knob.Name] = stop;

                    var url = payload.Count == 0
                        ? Catalog.BaseGlbUrl(Asset)
                        : Catalog.Client.RemixGlbUrl(Asset.Id, payload);

                    if (string.IsNullOrEmpty(url) || loader.IsCached(url)) continue;
                    if (budget != null && !budget.TryConsume()) return;   // stop quietly, keep what we have

                    try
                    {
                        await loader.GetBytesAsync(url, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (PolyforkRateLimitException e)
                    {
                        budget?.MarkExhausted(e.RetryAfter);
                        Debug.LogWarning($"[Polyfork] remix rate limited; prewarm stopped ({e.Message}).");
                        return;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Polyfork] prewarm failed for {knob.Name}={stop}: {e.Message}");
                    }
                }
            }
        }

        async Task RebuildGeometryAsync()
        {
            if (Catalog == null || Asset == null) return;

            var generation = ++_rebuildGeneration;
            SetBusy(true);

            try
            {
                // Send only knobs that differ from their default: a smaller query is a
                // better CDN cache key, and the endpoint treats absent as default anyway.
                var payload = new Dictionary<string, float>();
                foreach (var kv in _ranges)
                {
                    if (Schema.Knobs.TryGetValue(kv.Key, out var knob) &&
                        Mathf.Approximately(kv.Value, knob.DefaultFloat)) continue;
                    payload[kv.Key] = kv.Value;
                }

                var url = payload.Count == 0
                    ? Catalog.BaseGlbUrl(Asset)
                    : Catalog.Client.RemixGlbUrl(Asset.Id, payload);

                // Cached variants are free; only a real network fetch costs budget. If the
                // budget is gone, keep the current mesh rather than freezing on a 429.
                if (!Catalog.Loader.IsCached(url))
                {
                    var budget = Catalog.RemixBudget;
                    if (budget != null && !budget.TryConsume())
                    {
                        Debug.LogWarning(
                            $"[Polyfork] remix budget exhausted; keeping current mesh for {Asset.Id}. " +
                            $"Next slot at {budget.NextFreeAt:HH:mm:ss}. Set an API key to lift this.");
                        return;
                    }
                }

                var bytes = await Catalog.Loader.GetBytesAsync(url, _cts.Token);
                if (generation != _rebuildGeneration) return;   // superseded by a newer drag

                var next = await Catalog.Loader.InstantiateAsync(bytes, url, transform, _cts.Token);
                if (generation != _rebuildGeneration)
                {
                    Destroy(next);
                    return;
                }

                var previous = Model;
                next.transform.SetLocalPositionAndRotation(
                    previous != null ? previous.transform.localPosition : Vector3.zero,
                    previous != null ? previous.transform.localRotation : Quaternion.identity);
                next.transform.localScale = previous != null ? previous.transform.localScale : Vector3.one;

                Model = next;
                if (previous != null) Destroy(previous);

                // The remix endpoint ignores colour knobs, so the rebuilt mesh comes back
                // in default colours. Re-apply whatever the user has already dialled in.
                BindSlots();
                if (_slotColors.Count > 0) _slots?.Apply(_slotColors);

                ModelChanged?.Invoke(this);

                // The base moved, so the previously warmed axes no longer match. Warm the
                // new neighbourhood in the background; cached entries cost nothing.
                if (prewarmAfterRebuild) _ = PrewarmAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (PolyforkRateLimitException e)
            {
                Catalog.RemixBudget?.MarkExhausted(e.RetryAfter);
                Debug.LogWarning($"[Polyfork] remix rate limited for {Asset?.Id}: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] rebuild failed for {Asset?.Id}: {e.Message}");
            }
            finally
            {
                if (generation == _rebuildGeneration) SetBusy(false);
            }
        }

        void SetBusy(bool busy)
        {
            if (IsBusy == busy) return;
            IsBusy = busy;
            BusyChanged?.Invoke(busy);
        }

        /// <summary>Returns every knob to its published default.</summary>
        public void ResetToDefaults()
        {
            if (Schema == null) return;
            Initialise(Catalog, Asset, Schema, Model);
            _pendingRebuildAt = Time.unscaledTime;
        }
    }
}
