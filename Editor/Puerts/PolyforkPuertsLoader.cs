using System.Collections.Generic;
using Polyfork.EditorTools;
using Puerts;
using UnityEditor;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// Supplies Puerts with its own JavaScript bootstrap, out of the package rather than out
    /// of a <c>Resources</c> folder.
    ///
    /// Puerts ships <c>DefaultLoader</c>, which calls <c>Resources.Load</c>. Using it would
    /// mean keeping a Resources folder, and Unity copies those into every player build whether
    /// anything references them or not - the same trap that once kept local baking out of the
    /// package. Putting the folder under <c>Editor/</c> dodges the build but not the loader:
    /// assets there are reachable through <c>EditorGUIUtility.Load</c>, not
    /// <c>Resources.Load</c>, so <c>DefaultLoader</c> would come back empty.
    ///
    /// The vendored scripts also carry a <c>.txt</c> suffix, because <c>.mjs</c> is not a type
    /// Unity imports: upstream registers a ScriptedImporter for it, and that lives in the
    /// editor tooling this package deliberately does not vendor. A <c>.mjs</c> here would load
    /// as null. Requests still arrive under the original name, so <see cref="IsESM"/> sees the
    /// extension Puerts expects.
    /// </summary>
    sealed class PolyforkPuertsLoader : ILoader, IModuleChecker
    {
        const string Root = "Editor/Puerts/Vendor/JS";

        // Puerts probes for a path before reading it, and misses are routine rather than
        // errors, so this answers both from one lookup and stays quiet about the misses.
        readonly Dictionary<string, string> _cache = new();

        public bool FileExists(string filepath) => Read(filepath) != null;

        public string ReadFile(string filepath, out string debugpath)
        {
            debugpath = $"{PolyforkPackagePath.Root}/{Root}/{filepath}";
            return Read(filepath);
        }

        /// <summary>Matches Puerts's own rule: everything is a module except <c>.cjs</c>.</summary>
        public bool IsESM(string filepath)
            => filepath.Length >= 4 && !filepath.EndsWith(".cjs");

        string Read(string filepath)
        {
            if (_cache.TryGetValue(filepath, out var cached)) return cached;

            var root = PolyforkPackagePath.Root;
            var text = root == null
                ? null
                : AssetDatabase.LoadAssetAtPath<TextAsset>($"{root}/{Root}/{filepath}.txt")?.text;

            _cache[filepath] = text;
            return text;
        }

        /// <summary>
        /// Whether the bootstrap is actually there, checked once before an engine is built.
        ///
        /// Without this a missing bootstrap surfaces from inside native as a null-string
        /// exception with no filename in it, which is a bad way to learn that a package was
        /// installed in a layout the path resolver did not expect.
        /// </summary>
        public bool Verify(out string problem)
        {
            if (PolyforkPackagePath.Root == null)
            {
                problem = "the Polyfork package folder could not be located";
                return false;
            }

            if (Read("puerts/init.mjs") == null)
            {
                problem = $"the JavaScript bootstrap is missing from {PolyforkPackagePath.Root}/{Root}";
                return false;
            }

            problem = null;
            return true;
        }
    }
}
