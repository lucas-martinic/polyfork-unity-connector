using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Turns an asset plus a set of knob values into a scene object.
    ///
    /// There is more than one way to do this and they differ in what they can honour:
    ///
    ///   PolyforkServerBaker  asks polyfork.dev to rebuild the mesh. Only numeric range
    ///                        knobs are baked, so colours are re-applied locally and
    ///                        structural knobs cannot be honoured at all. Costs a bake
    ///                        against the account's allowance and a network round trip.
    ///
    ///   a local baker        runs the asset's own createAsset() module. Every knob is
    ///                        honoured because it is the same program the store runs;
    ///                        no network, no allowance, and roughly two orders of
    ///                        magnitude faster.
    ///
    /// Which knobs are live is therefore a property of the active baker, not of the asset,
    /// which is why <see cref="Supports"/> lives here rather than on the knob.
    /// </summary>
    public interface IPolyforkBaker
    {
        /// <summary>Short name for logs and UI.</summary>
        string Name { get; }

        /// <summary>Higher wins when more than one baker can serve a request.</summary>
        int Priority { get; }

        /// <summary>False when prerequisites are missing (no module on disk, no engine, offline).</summary>
        bool IsAvailable { get; }

        /// <summary>True when a bake costs something metered, so callers can budget.</summary>
        bool ConsumesAllowance { get; }

        /// <summary>Whether this baker can serve this particular asset at all.</summary>
        bool CanBake(PolyforkAsset asset, PolyforkParams schema);

        /// <summary>
        /// How this baker handles one knob.
        ///
        /// ServerRebuild - honoured, but needs a rebuild (latency, possibly allowance).
        /// LocalRecolor  - honoured without a rebuild.
        /// Unsupported   - cannot be honoured; UI should not draw it.
        /// </summary>
        PolyforkKnobSupport Supports(PolyforkKnob knob);

        /// <summary>
        /// Produces the object for these knob values. The returned root is owned by the
        /// caller. Implementations should honour cancellation, since a slider drag
        /// supersedes its own in-flight requests.
        /// </summary>
        Task<GameObject> BakeAsync(PolyforkBakeRequest request, CancellationToken ct = default);
    }

    public sealed class PolyforkBakeRequest
    {
        public PolyforkAsset Asset;
        public PolyforkParams Schema;
        public PolyforkKnobValues Values;

        /// <summary>Optional parent for the created object.</summary>
        public Transform Parent;

        public PolyforkBakeRequest(
            PolyforkAsset asset, PolyforkParams schema, PolyforkKnobValues values, Transform parent = null)
        {
            Asset = asset;
            Schema = schema;
            Values = values ?? new PolyforkKnobValues();
            Parent = parent;
        }
    }

    /// <summary>
    /// Chooses the best baker for a request.
    ///
    /// A local baker outranks the server one when its prerequisites are met, so a project
    /// that has the asset module gets every knob and no round trip, while one that does not
    /// keeps working exactly as before. Nothing else in the connector needs to know which
    /// path is in use.
    /// </summary>
    public sealed class PolyforkBakerRegistry
    {
        readonly List<IPolyforkBaker> _bakers = new();

        public IReadOnlyList<IPolyforkBaker> Bakers => _bakers;

        public event Action Changed;

        public void Register(IPolyforkBaker baker)
        {
            if (baker == null || _bakers.Contains(baker)) return;
            _bakers.Add(baker);
            _bakers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            Changed?.Invoke();
        }

        public void Unregister(IPolyforkBaker baker)
        {
            if (baker != null && _bakers.Remove(baker)) Changed?.Invoke();
        }

        /// <summary>Highest-priority available baker that can serve this asset, or null.</summary>
        public IPolyforkBaker Resolve(PolyforkAsset asset, PolyforkParams schema)
        {
            foreach (var baker in _bakers)
            {
                if (baker.IsAvailable && baker.CanBake(asset, schema)) return baker;
            }
            return null;
        }

        /// <summary>
        /// Knob support under whichever baker would actually serve this asset. UI should
        /// ask this rather than reading PolyforkKnob.Support, which only describes the
        /// server path.
        /// </summary>
        public PolyforkKnobSupport Supports(PolyforkAsset asset, PolyforkParams schema, PolyforkKnob knob)
        {
            var baker = Resolve(asset, schema);
            return baker?.Supports(knob) ?? PolyforkKnobSupport.Unsupported;
        }
    }
}
