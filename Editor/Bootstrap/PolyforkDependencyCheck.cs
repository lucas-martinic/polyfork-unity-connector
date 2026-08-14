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
        static readonly (string Package, string Why)[] Required =
        {
            ("com.unity.cloud.gltfast",
             "reads and writes the .glb files models arrive as"),
            ("com.unity.nuget.newtonsoft-json",
             "parses the catalogue and each model's parameter schema"),
        };

        [InitializeOnLoadMethod]
        static void Check()
        {
            /* Ask the package manager, not the loaded assemblies.
             *
             * This checked AppDomain assembly names against the names our asmdefs reference,
             * which is a different question and gets a different answer: the Newtonsoft package
             * ships its code as a precompiled `Newtonsoft.Json` assembly, so the asmdef
             * reference `Unity.Nuget.Newtonsoft-Json` resolves at compile time while no
             * assembly by that name is ever loaded. The result was this error firing on a
             * perfectly good install, which is worse than not checking at all - it tells a user
             * their working project is broken. Package names are what the message asks them to
             * install, so they are also what it should look for. */
            // Fully qualified: `PackageInfo` is not a unique name across Unity's namespaces,
            // and an ambiguous reference here costs a round trip to find out.
            var installed = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Select(p => p.name)
                .ToHashSet(StringComparer.Ordinal);

            // Nothing registered yet: too early to conclude anything, and a false alarm here is
            // the exact failure being fixed.
            if (installed.Count == 0) return;

            var missing = Required.Where(r => !installed.Contains(r.Package)).ToArray();
            if (missing.Length == 0) return;

            var list = string.Join("\n", missing.Select(m => $"    {m.Package}   ({m.Why})"));

            Debug.LogError(
                "[Polyfork] Missing required packages, so the connector cannot compile:\n\n"
                + list
                + "\n\nInstall each with Window > Package Manager > + > Install package by name. "
                + "They come from Unity's own registry and are free.\n\n"
                + "A .unitypackage carries no dependency information, which is the usual reason "
                + "for this. Package Manager installs - the Asset Store or the git URL - read "
                + "them from the manifest and resolve both on their own:\n"
                + "    https://github.com/lucas-martinic/polyfork-unity-connector.git");
        }
    }
}
