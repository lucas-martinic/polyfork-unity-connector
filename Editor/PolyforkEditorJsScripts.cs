using UnityEditor;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Feeds the local baker its two scripts, read straight out of the package.
    ///
    /// These used to live in a <c>Resources</c> folder, which is the reason local baking was
    /// exiled to a sample: Unity copies <c>Resources</c> into every player build whether
    /// anything references it or not, so a 336 KB three.js bundle rode along with games that
    /// never baked a thing. Under <c>Editor/</c> they are editor-only by construction - the
    /// player build cannot see them, and there is nothing to strip or remember to exclude.
    ///
    /// AssetDatabase rather than Resources because a package path is a real asset path, and
    /// this is the one place that knows where the package keeps them.
    ///
    /// The root used to be hardcoded to <c>Packages/dev.polyfork.unity-connector</c>, which exists
    /// only for a UPM install. Imported from a <c>.unitypackage</c> - the Asset Store's own
    /// delivery - there is no package by that name and the read returned null, so local baking
    /// silently fell back to the server for exactly the users the store sends.
    /// <see cref="PolyforkPackagePath"/> answers for both layouts.
    /// </summary>
    static class PolyforkEditorJsScripts
    {
        [InitializeOnLoadMethod]
        static void Register()
        {
            PolyforkJsRuntimeProvider.ScriptSource = () => (Read("three-trimmed"), Read("polyfork-bridge"));
        }

        static string Read(string name) => PolyforkPackagePath.ReadText($"Editor/JS/{name}.txt");
    }
}
