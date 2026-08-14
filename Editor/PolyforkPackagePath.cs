using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Where this package's own files live, in whichever layout it was installed as.
    ///
    /// There are two, and code that assumes one is broken in the other. Installed from the git
    /// URL or the registry, the package is at <c>Packages/com.polyfork.connector</c>. Imported
    /// from a <c>.unitypackage</c> - which is how the Asset Store delivers it - there is no
    /// package at all: the files land under <c>Assets/Polyfork</c>, and the folder the user
    /// chose is not fixed, because they can move it.
    ///
    /// So the fallback anchors on a file that is certainly ours and walks up from it. Its own
    /// script asset is the natural anchor: whatever else a user rearranges, this file is still
    /// at <c>&lt;root&gt;/Editor/PolyforkPackagePath.cs</c>.
    /// </summary>
    public static class PolyforkPackagePath
    {
        const string UpmRoot = "Packages/com.polyfork.connector";
        const string Anchor = "/Editor/PolyforkPackagePath.cs";

        static string _root;

        /// <summary>The package root as an asset path, or null if it cannot be found.</summary>
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_root)) return _root;

                if (AssetDatabase.IsValidFolder(UpmRoot)) return _root = UpmRoot;

                foreach (var guid in AssetDatabase.FindAssets("PolyforkPackagePath t:MonoScript"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(Anchor)) continue;
                    return _root = path.Substring(0, path.Length - Anchor.Length);
                }

                return null;
            }
        }

        /// <summary>Reads a TextAsset relative to the package root, or null with a reason logged.</summary>
        public static string ReadText(string relativePath)
        {
            var root = Root;
            if (root == null)
            {
                Debug.LogWarning("[Polyfork] could not locate the package folder, so " +
                                 $"{relativePath} was not read. Local baking will fall back to the server.");
                return null;
            }

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{root}/{relativePath}");
            if (asset != null) return asset.text;

            Debug.LogWarning($"[Polyfork] could not read {root}/{relativePath}. " +
                             "Local baking will fall back to the server.");
            return null;
        }
    }
}
