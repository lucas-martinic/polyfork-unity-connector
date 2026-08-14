using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Says which package is missing, when one is.
    ///
    /// The connector needs glTFast and Newtonsoft JSON, both from Unity's own registry. Installed
    /// from the git URL or bought through Asset Store UPM publishing, `package.json` declares them
    /// and Package Manager fetches them before anything of ours runs, so this never fires.
    ///
    /// It fires for the `.unitypackage`. That format carries no dependency information at all - it
    /// is a bag of files, not a manifest - so nothing resolves them, and the first thing the user
    /// sees is every Polyfork assembly failing to compile against references it cannot find. That
    /// reads as "this package is broken" rather than "install two packages first".
    ///
    /// Which is why this assembly references NOTHING. It is the one part of the connector that
    /// still compiles when the dependencies are absent, so it is the only part left that can
    /// explain why the rest did not.
    ///
    /// It reports and stops there. Installing the packages itself would be the store's 2.5.1.e,
    /// which has no exception for a user who agreed - see `Tools~/make-store-package.py`.
    /// </summary>
    static class PolyforkDependencyCheck
    {
        // asmdef names, which are also the assembly names our asmdefs reference.
        static readonly (string Assembly, string Package, string Why)[] Required =
        {
            ("glTFast", "com.unity.cloud.gltfast",
             "reads and writes the .glb files models arrive as"),
            ("Unity.Nuget.Newtonsoft-Json", "com.unity.nuget.newtonsoft-json",
             "parses the catalogue and each model's parameter schema"),
        };

        [InitializeOnLoadMethod]
        static void Check()
        {
            // After a compile failure Unity still reloads the domain, so this runs on the pass
            // where our other assemblies are absent - which is exactly when it is needed.
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name)
                .ToHashSet(StringComparer.Ordinal);

            var missing = Required.Where(r => !loaded.Contains(r.Assembly)).ToArray();
            if (missing.Length == 0) return;

            var list = string.Join("\n", missing.Select(m => $"    {m.Package}   ({m.Why})"));

            Debug.LogError(
                "[Polyfork] Missing required packages, so the connector cannot compile:\n\n"
                + list
                + "\n\nInstall them with Window > Package Manager > + > Install package by name, "
                + "then paste each name above. Both come from Unity's own registry and are free.\n\n"
                + "You are seeing this because the .unitypackage format carries no dependency "
                + "information. Installing from the git URL instead resolves both automatically:\n"
                + "    https://github.com/lucas-martinic/polyfork-unity-connector.git");
        }
    }
}
