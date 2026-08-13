#if POLYFORK_PUERTS
using UnityEditor;

namespace Polyfork
{
    /// <summary>
    /// Advertises the QuickJS runtime to the core assembly.
    ///
    /// This assembly only compiles when Puerts is installed, so registering from here is
    /// what makes local baking appear automatically without the connector ever referencing
    /// the engine. Removing the Puerts packages removes this file from the build, the
    /// factory is never set, and everything falls back to the server baker.
    /// </summary>
    public static class PolyforkPuertsBootstrap
    {
        /* Editor-only: this assembly declares includePlatforms: ["Editor"], so there is no
         * player build for a RuntimeInitializeOnLoadMethod to run in. Local baking is an
         * editor convenience - instant sliders while you are dressing a scene - and a shipped
         * game keeps using the server baker, which is what stops the JS engine and its
         * bundle from ever reaching a player. */
        [InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();

        static void Register()
        {
            PolyforkJsRuntimeProvider.EngineName = "QuickJS";
            PolyforkJsRuntimeProvider.Factory = () => new PolyforkPuertsRuntime();
        }
    }
}
#endif
