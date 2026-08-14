using UnityEditor;

namespace Polyfork
{
    /// <summary>
    /// Advertises the QuickJS runtime to the core assembly.
    ///
    /// The registration still points inwards even though the engine now ships inside this
    /// package: the core assembly never references Puerts, this one does, and it hands over
    /// a factory at load. That kept local baking optional when the engine was two packages
    /// the user installed, and it keeps the engine swappable now that it is vendored - the
    /// baker asks the provider for a runtime and does not know or care what answers.
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
