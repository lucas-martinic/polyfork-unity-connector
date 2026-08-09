#if POLYFORK_PUERTS
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterAtRuntime() => Register();

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif

        static void Register()
        {
            PolyforkJsRuntimeProvider.EngineName = "QuickJS";
            PolyforkJsRuntimeProvider.Factory = () => new PolyforkPuertsRuntime();
        }
    }
}
#endif
