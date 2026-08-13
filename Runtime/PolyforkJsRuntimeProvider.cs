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
        /// Supplies the two scripts, set by the editor assembly at load.
        ///
        /// They used to sit in a Resources folder, which is why local baking was kept out of
        /// the package: Unity copies Resources into EVERY player build whether anything
        /// references them or not, so a 336 KB three.js bundle shipped with games that never
        /// baked anything. Loading them through this hook instead means the editor can read
        /// them straight out of the package folder and a player never sees them at all.
        ///
        /// Returns (null, null) when unset, and the Resources path below is tried next so an
        /// older project that imported the sample keeps working.
        /// </summary>
        public static Func<(string three, string bridge)> ScriptSource { get; set; }

        /// <summary>
        /// Builds and initialises a runtime, or returns null if none is installed or the
        /// scripts are missing. Failure is never fatal: the caller falls back to the server.
        /// </summary>
        public static IPolyforkJsRuntime TryCreate()
        {
            if (!IsAvailable) return null;

            var (threeText, bridgeText) = ScriptSource?.Invoke() ?? (null, null);

            if (threeText == null || bridgeText == null)
            {
                // Older layout: the sample put both scripts in a Resources folder.
                threeText ??= Resources.Load<TextAsset>(ThreeBundleResource)?.text;
                bridgeText ??= Resources.Load<TextAsset>(BridgeResource)?.text;
            }

            if (string.IsNullOrEmpty(threeText) || string.IsNullOrEmpty(bridgeText))
            {
                Debug.LogWarning("[Polyfork] the JS runtime scripts could not be found; " +
                                 "falling back to server baking.");
                return null;
            }

            try
            {
                var runtime = Factory();
                runtime.Initialise(threeText, bridgeText);
                return runtime.IsReady ? runtime : null;
            }
            catch (Exception e)
            {
                /* The whole exception, not e.Message. Falling back to the server means this
                 * is only ever a warning, and a warning that has thrown its stack away turns
                 * a one-line diagnosis into an afternoon: the message alone cannot say which
                 * script, which step, or which frame. */
                Debug.LogWarning(
                    $"[Polyfork] {EngineName ?? "JS"} runtime failed to start; falling back to " +
                    $"server baking. Geometry still rebuilds, just on polyfork.dev.\n{e}");
                return null;
            }
        }
    }
}
