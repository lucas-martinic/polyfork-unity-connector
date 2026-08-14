using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Reports whether local baking is running, and what to do when it is not.
    ///
    /// This used to be an installer. Local baking needs a JavaScript engine, and the engine
    /// (PuerTS on QuickJS) used to be two packages the user had to add themselves, with a
    /// one-button installer here that drove the Package Manager API for them. The Asset Store
    /// forbids that outright - 2.5.1.e, and unlike 2.5.1.d it carries no exception for user
    /// consent - so the engine is vendored into this package instead, which is what Meta's XR
    /// SDK does with its own eight. See `Tools~/vendor-puerts.py`.
    ///
    /// What is left is a status page. There is nothing to install, so the only questions
    /// worth answering are whether the engine started, and if not, why.
    /// </summary>
    public sealed class PolyforkLocalBakingWindow : EditorWindow
    {
        /* The engine that would clash with ours. Anyone who used the old installer still has
         * these, and Unity refuses to import two native plugins with the same file name from
         * different folders - so this is an error the user meets at import, before they ever
         * open this window, and the only thing that fixes it is removing the packages. */
        static readonly string[] LegacyAssemblies =
        {
            "com.tencent.puerts.core",
            "com.tencent.puerts.quickjs",
        };

        [MenuItem("Polyfork/Setup", priority = 3)]
        public static void Open()
        {
            var window = GetWindow<PolyforkLocalBakingWindow>(true, "Polyfork Setup");
            window.minSize = new Vector2(430f, 300f);
            window.Show();
        }

        static bool EngineReady => PolyforkJsRuntimeProvider.IsAvailable;

        static string EngineName =>
            string.IsNullOrEmpty(PolyforkJsRuntimeProvider.EngineName)
                ? "QuickJS"
                : PolyforkJsRuntimeProvider.EngineName;

        /// <summary>
        /// The old packages, detected by assembly rather than by reading the project's package
        /// lock. Same answer, and it asks the question we actually care about - is a second
        /// copy of Puerts loaded - rather than what a file on disk says about it.
        /// </summary>
        static string[] LegacyPackagesPresent() =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name)
                .Where(n => LegacyAssemblies.Contains(n))
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

        Vector2 _scroll;

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(8f);

            var legacy = LegacyPackagesPresent();
            if (legacy.Length > 0) DrawLegacyConflict(legacy);
            else if (EngineReady) DrawReady();
            else DrawNotRunning();

            EditorGUILayout.Space(10f);
            DrawFooter();
            EditorGUILayout.EndScrollView();
        }

        void DrawReady()
        {
            EditorGUILayout.HelpBox(
                $"Local baking is on. Models rebuild in the editor on {EngineName}, "
                + "with no server round trip and nothing counted against your hourly allowance.",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("There is nothing to set up.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField(
                "The engine ships inside this package, so drag a slider in the remix screen and "
                + "the geometry rebuilds as you drag rather than when you let go.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(10f);
            if (GUILayout.Button("Run a smoke test", GUILayout.Height(26f)))
                EditorApplication.ExecuteMenuItem("Polyfork/Diagnostics/Smoke-test local baking");
        }

        void DrawNotRunning()
        {
            EditorGUILayout.HelpBox(
                "Local baking is not running. Models will still rebuild, on the server: roughly "
                + "120 ms per change, and each one spends part of your hourly allowance.",
                MessageType.Warning);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("What to check", EditorStyles.boldLabel);

            Bullet("The engine is editor-only and desktop-only: Windows, macOS and Linux on "
                   + "x64, plus Apple Silicon. It does not run in a player build, by design.");
            Bullet("Open the Console. The engine reports the step it failed on by name, so the "
                   + "message says which part broke rather than only that something did.");
            Bullet("A smoke test bakes one model and prints timings. It is the quickest way to "
                   + "turn 'nothing happens' into an error message.");

            EditorGUILayout.Space(10f);
            if (GUILayout.Button("Run a smoke test", GUILayout.Height(26f)))
                EditorApplication.ExecuteMenuItem("Polyfork/Diagnostics/Smoke-test local baking");
        }

        void DrawLegacyConflict(string[] legacy)
        {
            EditorGUILayout.HelpBox(
                "Remove the PuerTS packages from this project. The engine is built into Polyfork "
                + "now, and two copies of it cannot coexist: Unity rejects two native plugins "
                + "with the same file name, so the project will not compile until one goes.",
                MessageType.Error);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Still installed", EditorStyles.boldLabel);
            foreach (var name in legacy)
                EditorGUILayout.LabelField("    " + name, EditorStyles.miniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("How to remove them", EditorStyles.boldLabel);
            Bullet("Window ▸ Package Manager, switch to In Project, select each of the packages "
                   + "above and press Remove.");
            Bullet("An earlier version of this window unpacked them into a PuerTS folder beside "
                   + "Assets. If that folder is still there, delete it too.");

            EditorGUILayout.Space(10f);
            if (GUILayout.Button("Open Package Manager", GUILayout.Height(26f)))
                EditorApplication.ExecuteMenuItem("Window/Package Manager");
        }

        void DrawFooter()
        {
            EditorGUILayout.Space(4f);
            var style = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            EditorGUILayout.LabelField(
                "Local baking is an editor convenience. The engine and its scripts declare "
                + "Editor-only, so neither reaches a player build and a shipped game keeps using "
                + "the server. Polyfork bundles PuerTS (Tencent, BSD 3-Clause); the notice is in "
                + "Third Party Notices.md.",
                style);
        }

        /// <summary>The one action worth clicking, sized and coloured to say so.</summary>
        internal static GUIStyle PrimaryButton
        {
            get
            {
                var style = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                style.normal.textColor = PolyforkBrand.Accent;
                style.hover.textColor = PolyforkBrand.Accent;
                style.focused.textColor = PolyforkBrand.Accent;
                style.active.textColor = PolyforkBrand.Accent;
                return style;
            }
        }

        static void Bullet(string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("•", GUILayout.Width(14f));
                EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.Space(2f);
        }

        // The window flips from "not running" to "on" on a domain reload, which is not a
        // repaint, so without this it keeps showing whatever was true when it opened.
        void OnInspectorUpdate() => Repaint();
    }
}
