using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Remembers which Polyfork asset a GameObject is, and what its knobs were set to.
    ///
    /// Without this an imported model is an anonymous mesh the moment it lands in the
    /// project: the knob values that produced it live only in the window that made it, so
    /// coming back a week later to make the fence one section longer means finding the asset
    /// again, guessing what the sliders were, and importing a second copy beside the first.
    ///
    /// It is a plain serialisable record rather than anything live - an asset id and the
    /// values as JSON, which is exactly what the remix endpoint and the asset's own module
    /// both take. The editor reads it to put the knobs back in the Inspector; at runtime it
    /// costs a few bytes and does nothing, which is why it is safe to leave on a shipped
    /// prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Polyfork/Polyfork Asset Link")]
    public sealed class PolyforkAssetLink : MonoBehaviour
    {
        [Tooltip("Catalogue id, e.g. street-lamp-29f365.")]
        public string assetId;

        [Tooltip("Human-readable title at the time of import.")]
        public string title;

        /// <summary>
        /// The knob values this object was built with, as the `p=` payload: {"bays":4}.
        ///
        /// Stored as text rather than typed fields because the knobs belong to the asset,
        /// not to this component. A model with a `bays` knob and one with a `towerHeight`
        /// knob cannot share a struct, and inventing one here would be the client deciding
        /// what a Polyfork asset is allowed to have - which is the one thing this package
        /// exists not to do.
        /// </summary>
        [Tooltip("Knob values as JSON. Empty means the asset at its published defaults.")]
        public string knobValues = "{}";

        [Tooltip("Where this came from, for the Open button.")]
        public string page;

        /// <summary>True when the object is still exactly as published.</summary>
        public bool IsDefault => string.IsNullOrWhiteSpace(knobValues) || knobValues.Trim() == "{}";

        public PolyforkKnobValues Values => PolyforkKnobValues.FromJson(knobValues);
    }
}
