using System;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// How an engine integration makes itself available without the core assembly knowing
    /// it exists.
    ///
    /// The engine lives in an optional assembly that only compiles when its package is
    /// installed, so the dependency has to point inwards: the integration registers a
    /// factory here at load, and the catalog picks it up if one arrived. Referencing the
    /// engine directly would make the whole connector require it.
    /// </summary>
    public static class PolyforkJsRuntimeProvider
    {
        /// <summary>Set by an engine integration at load. Null when none is installed.</summary>
        public static Func<IPolyforkJsRuntime> Factory { get; set; }

        public static string EngineName { get; set; }

        public static bool IsAvailable => Factory != null;

        /// <summary>The trimmed three.js bundle and the bake bridge, shipped with the package.</summary>
        public const string ThreeBundleResource = "Polyfork/three-trimmed";
        public const string BridgeResource = "Polyfork/polyfork-bridge";

        /// <summary>
        /// Builds and initialises a runtime, or returns null if none is installed or the
        /// scripts are missing. Failure is never fatal: the caller falls back to the server.
        /// </summary>
        public static IPolyforkJsRuntime TryCreate()
        {
            if (!IsAvailable) return null;

            var three = Resources.Load<TextAsset>(ThreeBundleResource);
            var bridge = Resources.Load<TextAsset>(BridgeResource);

            if (three == null || bridge == null)
            {
                Debug.LogWarning("[Polyfork] the JS runtime scripts are missing from Resources; " +
                                 "falling back to server baking.");
                return null;
            }

            try
            {
                var runtime = Factory();
                runtime.Initialise(three.text, bridge.text);
                return runtime.IsReady ? runtime : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] {EngineName ?? "JS"} runtime failed to start ({e.Message}); " +
                                 "falling back to server baking.");
                return null;
            }
        }
    }
}
