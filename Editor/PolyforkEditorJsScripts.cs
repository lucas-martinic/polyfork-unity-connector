using UnityEditor;
using UnityEngine;

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
    /// </summary>
    static class PolyforkEditorJsScripts
    {
        const string Root = "Packages/com.polyfork.connector/Editor/JS";

        [InitializeOnLoadMethod]
        static void Register()
        {
            PolyforkJsRuntimeProvider.ScriptSource = () => (Read("three-trimmed"), Read("polyfork-bridge"));
        }

        static string Read(string name)
        {
            // The package path when installed normally. An embedded or local checkout resolves
            // through the same virtual path, so this covers both.
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Root}/{name}.txt");
            if (asset != null) return asset.text;

            Debug.LogWarning($"[Polyfork] could not read {Root}/{name}.txt; local baking is unavailable " +
                             "and geometry will be rebuilt on the server instead.");
            return null;
        }
    }
}
