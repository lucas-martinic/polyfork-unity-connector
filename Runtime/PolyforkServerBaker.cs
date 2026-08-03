using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Bakes through polyfork.dev's remix endpoint.
    ///
    /// What this baker can and cannot do is not a design choice, it is measured behaviour
    /// of /cdn/{id}-remix.glb (see the package README):
    ///
    ///   range knobs            rebuilt server-side, and clamped to min/max.
    ///   colour / choice knobs  accepted and silently ignored - the response is byte
    ///                          identical to the baseline. Colours are therefore applied
    ///                          here by remapping vertex-colour slots.
    ///   structural knobs       (choice/toggle marked affects: geometry) ignored by the
    ///                          endpoint and impossible to emulate locally, so reported
    ///                          Unsupported rather than drawn as a control that does
    ///                          nothing.
    ///
    /// A baker that runs the asset's own module has none of these limits, which is the
    /// reason this behaviour lives behind an interface instead of being assumed.
    /// </summary>
    public sealed class PolyforkServerBaker : IPolyforkBaker
    {
        readonly PolyforkClient _client;
        readonly PolyforkGlbLoader _loader;
        readonly PolyforkRemixBudget _budget;

        public PolyforkServerBaker(PolyforkClient client, PolyforkGlbLoader loader, PolyforkRemixBudget budget = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _budget = budget;
        }

        public string Name => "polyfork.dev";

        /// <summary>Baseline: any local baker should outrank it.</summary>
        public int Priority => 0;

        public bool IsAvailable => true;

        public bool ConsumesAllowance => true;

        public bool CanBake(PolyforkAsset asset, PolyforkParams schema)
            => asset != null && !string.IsNullOrEmpty(asset.PreviewGlb);

        public PolyforkKnobSupport Supports(PolyforkKnob knob)
        {
            if (knob == null) return PolyforkKnobSupport.Unsupported;

            return knob.Type switch
            {
                // The only type the endpoint actually bakes.
                PolyforkKnobType.Range => knob.HasRange
                    ? PolyforkKnobSupport.ServerRebuild
                    : PolyforkKnobSupport.Unsupported,

                // Ignored by the endpoint, but reproducible here: the asset's distinct
                // vertex colours are exactly its colour knobs' default hexes.
                PolyforkKnobType.Color => PolyforkParams.IsHex(knob.DefaultString)
                    ? PolyforkKnobSupport.LocalRecolor
                    : PolyforkKnobSupport.Unsupported,

                // A colourway resolves to slot colours, so it is reproducible. A structural
                // choice is not; the schema tells them apart by whether every option names
                // a preset, which PolyforkParams has already decided.
                PolyforkKnobType.Choice => knob.Support == PolyforkKnobSupport.LocalRecolor
                    ? PolyforkKnobSupport.LocalRecolor
                    : PolyforkKnobSupport.Unsupported,

                _ => PolyforkKnobSupport.Unsupported
            };
        }

        public async Task<GameObject> BakeAsync(PolyforkBakeRequest request, CancellationToken ct = default)
        {
            if (request?.Asset == null) return null;

            var schema = request.Schema;
            var effective = request.Values.WithoutDefaults(schema).Filter(schema, this);

            var url = effective.Count == 0
                ? request.Asset.PreviewGlb
                : _client.RemixGlbUrl(request.Asset.Id, ToFloats(effective));

            if (string.IsNullOrEmpty(url)) return null;

            // Only a request that leaves the machine can cost a bake, and only geometry
            // nobody has produced before is metered at all.
            if (_budget != null && effective.Count > 0 && !_loader.IsCached(url) && !_budget.TryConsume())
                throw new PolyforkBakeUnavailableException("Out of remix bakes for now.");

            var bytes = await _loader.GetBytesAsync(url, ct);
            ct.ThrowIfCancellationRequested();

            var root = await _loader.InstantiateAsync(bytes, url, request.Parent, ct);

            ApplyColours(root, schema, request.Values);
            return root;
        }

        /// <summary>
        /// Re-applies colour knobs, which the endpoint dropped. Runs after every rebuild
        /// because the returned mesh always comes back in the asset's default colours.
        /// </summary>
        void ApplyColours(GameObject root, PolyforkParams schema, PolyforkKnobValues values)
        {
            if (root == null || schema == null) return;

            var slots = new Dictionary<string, Color>();

            // A colourway expands to several slots first, so an explicit colour set
            // afterwards still wins.
            foreach (var knob in schema.All)
            {
                if (knob.Type != PolyforkKnobType.Choice) continue;
                if (Supports(knob) != PolyforkKnobSupport.LocalRecolor) continue;

                var chosen = values.GetString(knob.Name, knob.DefaultString);
                if (chosen == null || !schema.TryGetPreset(chosen, out var preset)) continue;

                foreach (var kv in preset)
                {
                    if (PolyforkParams.TryParseHex(kv.Value, out var c)) slots[kv.Key] = c;
                }
            }

            foreach (var knob in schema.All)
            {
                if (knob.Type != PolyforkKnobType.Color) continue;
                if (values.TryGetColor(knob.Name, out var c)) slots[knob.Name] = c;
            }

            if (slots.Count == 0) return;

            var binding = PolyforkColorSlots.Build(root, schema);
            if (binding.HasSlots) binding.Apply(slots);
        }

        static Dictionary<string, float> ToFloats(PolyforkKnobValues values)
        {
            var floats = new Dictionary<string, float>();
            foreach (var kv in values)
            {
                if (kv.Value is float f) floats[kv.Key] = f;
            }
            return floats;
        }
    }

    /// <summary>Thrown when a bake cannot proceed for a reason the caller should surface.</summary>
    public sealed class PolyforkBakeUnavailableException : Exception
    {
        public PolyforkBakeUnavailableException(string message) : base(message) { }
    }
}
